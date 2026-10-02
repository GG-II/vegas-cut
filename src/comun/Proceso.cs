using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

// =====================================================================
// Pasos sin ventana para PrepararEpisodio: quitar silencios y transcribir.
// Hacen lo mismo que sus herramientas, con lo que ya elegiste guardado.
// =====================================================================

public static class PasoSilencios
{
    // Mide las voces de todo el proyecto, detecta las pausas con el umbral
    // de cada pista y las quita de todas las pistas (Ctrl+Z lo deshace).
    // Devuelve cuantos silencios quito y cuanto tiempo.
    public static int Ejecutar(Vegas vegas, List<InfoPista> voces, Valores v, Action<string, double> estado, out double quitado)
    {
        Project p = vegas.Project;
        double duracion = p.Length.ToMilliseconds() / 1000.0;
        List<Analisis> datos = new List<Analisis>();
        List<double> umbrales = new List<double>();
        for (int i = 0; i < voces.Count; i++)
        {
            estado("Midiendo " + voces[i].Nombre + " (" + (i + 1) + " de " + voces.Count + ")…", (double)i / voces.Count);
            Analisis a = PistasVegas.Niveles(vegas, voces[i].Pista, 0, duracion);
            datos.Add(a);
            umbrales.Add(Detector.UmbralAutomatico(a.Db));
        }
        List<Rango> rangos = Editor.AjustarAFotogramas(Detector.Detectar(datos, umbrales, v), p.Video.FrameRate);
        quitado = 0;
        foreach (Rango r in rangos) quitado += r.Fin - r.Inicio;
        if (rangos.Count == 0) return 0;

        estado("Quitando " + rangos.Count + " silencios…", 1);
        List<Track> todas = new List<Track>();
        foreach (Track t in p.Tracks) todas.Add(t);
        using (UndoBlock deshacer = new UndoBlock("Quitar silencios"))
            Editor.Eliminar(p, todas, rangos, true, true, v.SuavizadoMs / 1000.0);
        Transcripcion.RegistrarCortes(p.FilePath, rangos, duracion, p.Length.ToMilliseconds() / 1000.0);
        return rangos.Count;
    }
}

// Transcribe las voces con Whisper en segundo plano. Se llama a Revisar()
// cada poco (con un temporizador) hasta que Terminado sea true.
public class ProcesoTranscripcion
{
    readonly Vegas vegas;
    readonly Configuracion config;
    readonly string ruta;
    double inicio, duracion, segundosHechos, segundosTotales;
    List<int> cola = new List<int>();
    Dictionary<int, string> wavs = new Dictionary<int, string>();
    TareaWhisper tarea;
    int actual = -1;
    DateTime comienzo;

    public Transcripcion Resultado;
    public string Texto = "", Detalle = "", Error;
    public double Fraccion;
    public bool Terminado;

    public ProcesoTranscripcion(Vegas vegas, Configuracion config, string ruta)
    {
        this.vegas = vegas; this.config = config; this.ruta = ruta;
    }

    // Render y niveles de cada pista (en el hilo de Vegas) y arranca Whisper.
    public void Empezar(List<InfoPista> pistas, List<int> voces, List<int> ambiente, Action<string, double> estado)
    {
        comienzo = DateTime.Now;
        inicio = 0;
        duracion = vegas.Project.Length.ToMilliseconds() / 1000.0;
        Resultado = new Transcripcion();
        Resultado.Proyecto = vegas.Project.FilePath;
        Resultado.Creada = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        Resultado.Idioma = config.Idioma;
        Resultado.Modelo = config.WhisperModelo;
        Resultado.Inicio = inicio;
        Resultado.Duracion = duracion;
        Resultado.DuracionProyecto = duracion;

        Dictionary<string, string> nombresPrevios = new Dictionary<string, string>();
        try
        {
            if (File.Exists(ruta))
                foreach (Hablante h in Transcripcion.Cargar(ruta).Hablantes) nombresPrevios[h.Etiqueta] = h.Nombre;
        }
        catch { }

        List<int> leer = new List<int>(voces);
        foreach (int i in ambiente) if (!leer.Contains(i)) leer.Add(i);
        leer.Sort();
        try
        {
            for (int k = 0; k < leer.Count; k++)
            {
                InfoPista p = pistas[leer[k]];
                estado("Leyendo el audio de " + p.Nombre + " (" + (k + 1) + " de " + leer.Count + ")…", 0.02 * (k + 1) / leer.Count);
                string wav = PistasVegas.RenderizarWav(vegas, p.Pista, inicio, duracion);
                Analisis a = WavNiveles.Leer(wav, Analisis.Paso);
                Hablante h = new Hablante();
                h.Etiqueta = p.Etiqueta;
                string previo;
                h.Nombre = nombresPrevios.TryGetValue(h.Etiqueta, out previo) && previo.Length > 0 ? previo : h.Etiqueta;
                h.Archivo = p.Archivo ?? "";
                h.Voz = voces.Contains(leer[k]);
                Transcripcion.NivelesPorSegundo(a, out h.Nivel, out h.Pico);
                h.Fuentes = PistasVegas.Fuentes(p.Pista);
                Resultado.Hablantes.Add(h);
                if (h.Voz)
                {
                    wavs[Resultado.Hablantes.Count - 1] = wav;
                    cola.Add(Resultado.Hablantes.Count - 1);
                }
                else try { File.Delete(wav); } catch { }
            }
        }
        catch (Exception ex)
        {
            Limpiar();
            throw new Exception("No se pudo leer el audio: " + ex.Message);
        }
        segundosTotales = duracion * cola.Count;
        segundosHechos = 0;
        Siguiente();
    }

    void Siguiente()
    {
        if (cola.Count == 0) { Guardar(); return; }
        actual = cola[0];
        cola.RemoveAt(0);
        tarea = new TareaWhisper();
        tarea.Iniciar(config, wavs[actual], duracion);
    }

    public void Revisar()
    {
        if (Terminado || tarea == null) return;
        double hecho = (segundosHechos + tarea.Avance) / Math.Max(1, segundosTotales);
        double pasado = (DateTime.Now - comienzo).TotalSeconds;
        Fraccion = 0.02 + 0.98 * hecho;
        Texto = "Transcribiendo " + Resultado.Hablantes[actual].Etiqueta + ": " + Formato.Tiempo(tarea.Avance) + " de " +
                Formato.Tiempo(duracion) + " · " + Formato.Tiempo(pasado) + " transcurrido" +
                (hecho > 0.03 ? " · faltan ~" + Formato.Tiempo(pasado * (1 - hecho) / hecho) : "");
        Detalle = tarea.UltimaLinea;
        if (!tarea.Terminada) return;

        TareaWhisper t = tarea;
        tarea = null;
        if (t.Cancelada) { t.Limpiar(); return; }
        try { Resultado.AgregarWhisper(t.Resultado(), actual, inicio); }
        catch (Exception ex) { t.Limpiar(); Limpiar(); Fallar(ex.Message); return; }
        t.Limpiar();
        try { File.Delete(wavs[actual]); } catch { }
        wavs.Remove(actual);
        segundosHechos += duracion;
        try { Siguiente(); }
        catch (Exception ex) { Limpiar(); Fallar("No se pudo iniciar Whisper: " + ex.Message); }
    }

    void Guardar()
    {
        try
        {
            Resultado.Guardar(ruta);
            int palabras = 0;
            foreach (Segmento s in Resultado.Segmentos) palabras += s.Palabras.Count;
            Texto = Resultado.Segmentos.Count + " frases y " + palabras + " palabras en " +
                    Formato.Tiempo((DateTime.Now - comienzo).TotalSeconds) + ".";
            Fraccion = 1;
        }
        catch (Exception ex) { Fallar("No se pudo guardar la transcripción: " + ex.Message); return; }
        Terminado = true;
    }

    void Fallar(string mensaje) { Error = mensaje; Terminado = true; }

    public void Cancelar()
    {
        if (tarea != null) { tarea.Cancelar(); tarea.Limpiar(); tarea = null; }
        cola.Clear();
        Limpiar();
        Fallar("Cancelado.");
    }

    void Limpiar()
    {
        foreach (string w in wavs.Values) { try { File.Delete(w); } catch { } }
        wavs.Clear();
    }
}

// Copia "<proyecto> BASE.veg": el episodio sin silencios y transcrito, para
// volver a partir de ahi (MomentosIA o ProducirCapitulo) sin repetir esos pasos.
public static class CopiaBase
{
    public const string Sufijo = " BASE";

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + Sufijo + ".veg");
    }

    // El proyecto original de una copia base (o el mismo si no lo es).
    public static string Original(string veg)
    {
        string n = Path.GetFileNameWithoutExtension(veg);
        return n.EndsWith(Sufijo) ? Path.Combine(Path.GetDirectoryName(veg), n.Substring(0, n.Length - Sufijo.Length) + ".veg") : veg;
    }

    // Guarda la copia (con su transcripcion y su serie) y vuelve al proyecto original.
    public static string Guardar(Vegas vegas)
    {
        string original = vegas.Project.FilePath;
        if (String.IsNullOrEmpty(original) || Path.GetFileNameWithoutExtension(original).EndsWith(Sufijo)) return null;
        string base_ = RutaPara(original);
        vegas.SaveProject(base_);
        try
        {
            string t = Transcripcion.RutaPara(original);
            if (File.Exists(t))
            {
                Transcripcion tr = Transcripcion.Cargar(t);
                tr.Proyecto = base_;
                tr.Guardar(Transcripcion.RutaPara(base_));
            }
            string dir = Path.GetDirectoryName(original), n = Path.GetFileNameWithoutExtension(original);
            string serie = Path.Combine(dir, n + ".vegascut-proyecto-serie.json");
            if (File.Exists(serie)) File.Copy(serie, Path.Combine(dir, n + Sufijo + ".vegascut-proyecto-serie.json"), true);
        }
        finally { vegas.SaveProject(original); }
        return base_;
    }
}
