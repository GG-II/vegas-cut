// PrepararEpisodio.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Todo de una pasada sobre la grabacion: quita los silencios (con el perfil
// y las pistas de voz que elijas), transcribe con Whisper y al final abre
// MomentosIA listo para pedir. Recuerda lo que elegiste para la proxima vez.
// Cada paso se deshace por separado con Ctrl+Z.
//
// Requiere Faster-Whisper-XXL configurado en ConfigurarVegasCut.cs.
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/preparar/Preparar.cs ----

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Project p = vegas.Project;
        if (String.IsNullOrEmpty(p.FilePath))
        {
            MessageBox.Show("Guarda el proyecto primero: la transcripci\u00f3n se guarda junto al .veg.", "Preparar episodio");
            return;
        }
        List<InfoPista> pistas = PistasVegas.Listar(p);
        if (pistas.Count == 0)
        {
            MessageBox.Show("El proyecto no tiene pistas de audio.", "Preparar episodio");
            return;
        }
        bool abrir, pedir;
        using (VentanaPreparar v = new VentanaPreparar(vegas, pistas))
        {
            v.ShowDialog();
            abrir = v.AbrirMomentos;
            pedir = v.Pedir;
        }
        if (abrir) AbrirMomentos.Abrir(vegas, pedir);
    }
}

// Lo que se recuerda entre episodios (%APPDATA%\vegas-cut\preparar.ini).
public class AjustesPreparar
{
    public List<string> Voces = new List<string>(), Ambiente = new List<string>();
    public string Perfil = "Gameplay";
    public bool Silencios = true, Transcribir = true, Pedir = false;

    static string Ruta
    {
        get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"), "preparar.ini"); }
    }

    static List<string> Lista(string v)
    {
        List<string> r = new List<string>();
        foreach (string x in v.Split(',')) if (x.Trim().Length > 0) r.Add(x.Trim());
        return r;
    }

    public static AjustesPreparar Cargar()
    {
        AjustesPreparar a = new AjustesPreparar();
        try
        {
            if (!File.Exists(Ruta)) return a;
            foreach (string l in File.ReadAllLines(Ruta, Encoding.UTF8))
            {
                int i = l.IndexOf('=');
                if (i < 0) continue;
                string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                switch (k)
                {
                    case "voces": a.Voces = Lista(v); break;
                    case "ambiente": a.Ambiente = Lista(v); break;
                    case "perfil": a.Perfil = v; break;
                    case "silencios": a.Silencios = v != "0"; break;
                    case "transcribir": a.Transcribir = v != "0"; break;
                    case "pedir": a.Pedir = v == "1"; break;
                }
            }
        }
        catch { }
        return a;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
            File.WriteAllText(Ruta, "voces=" + String.Join(",", Voces.ToArray()) + "\nambiente=" + String.Join(",", Ambiente.ToArray()) +
                "\nperfil=" + Perfil + "\nsilencios=" + (Silencios ? "1" : "0") + "\ntranscribir=" + (Transcribir ? "1" : "0") +
                "\npedir=" + (Pedir ? "1" : "0") + "\n", new UTF8Encoding(false));
        }
        catch { }
    }
}

class VentanaPreparar : VentanaBase
{
    readonly Vegas vegas;
    readonly List<InfoPista> pistas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly AjustesPreparar ajustes = AjustesPreparar.Cargar();
    readonly string ruta;
    List<Perfil_> perfiles = new List<Perfil_>();
    public bool AbrirMomentos, Pedir;

    List<Boton> chipsVoz = new List<Boton>(), chipsAmbiente = new List<Boton>();
    Combo comboPerfil = new Combo();
    Etiqueta lblPerfil, lblPaso1, lblPaso2, lblPaso3, lblEstado, lblDetalle, lblWhisper;
    Boton chipSilencios = new Boton("Quitar silencios", EstiloBoton.Chip);
    Boton chipTranscribir = new Boton("Transcribir", EstiloBoton.Chip);
    Boton chipPedir = new Boton("Pedir a Gemini al abrir", EstiloBoton.Chip);
    BarraProgreso barra = new BarraProgreso();
    Boton btnEmpezar = new Boton("Empezar", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);
    System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
    ProcesoTranscripcion proceso;
    bool trabajando;
    DateTime comienzo;

    public VentanaPreparar(Vegas vegas, List<InfoPista> pistas) : base("Preparar episodio", 820)
    {
        this.vegas = vegas;
        this.pistas = pistas;
        ruta = Transcripcion.RutaPara(vegas.Project.FilePath);
        int m = Margen, w = Ancho;
        Encabezado("Preparar episodio", "Quita los silencios y transcribe de una pasada, guarda una copia BASE y abre MomentosIA.");

        int y = 92;
        if (File.Exists(ruta))
        {
            Texto("Este proyecto ya tiene transcripci\u00f3n: si transcribes otra vez, se reemplaza.", Tema.Pequena, Tema.AcentoHover, m, y, w, 18);
            y += 24;
        }
        Texto("VOCES", Tema.Pequena, Tema.TextoSuave, m, y, 60, 18);
        Texto("Se usan para detectar los silencios y se transcriben.", Tema.Pequena, Tema.TextoSuave, m + 64, y, w - 64, 18);
        y = Chips(chipsVoz, y + 22) + 12;
        Texto("AMBIENTE", Tema.Pequena, Tema.TextoSuave, m, y, 80, 18);
        Texto("Solo se mide su sonido (juego, m\u00fasica) para encontrar momentos intensos.", Tema.Pequena, Tema.TextoSuave, m + 84, y, w - 84, 18);
        y = Chips(chipsAmbiente, y + 22) + 16;
        Elegir();
        for (int i = 0; i < pistas.Count; i++)
        {
            int k = i;
            chipsVoz[k].Click += delegate { if (trabajando) return; chipsVoz[k].Activo = !chipsVoz[k].Activo; if (chipsVoz[k].Activo) chipsAmbiente[k].Activo = false; Actualizar(); };
            chipsAmbiente[k].Click += delegate { if (trabajando) return; chipsAmbiente[k].Activo = !chipsAmbiente[k].Activo; if (chipsAmbiente[k].Activo) chipsVoz[k].Activo = false; Actualizar(); };
        }

        Texto("PERFIL DE SILENCIOS", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        perfiles.AddRange(Perfil_.Incluidos);
        perfiles.AddRange(Perfil_.CargarPropios());
        foreach (Perfil_ p in perfiles) comboPerfil.Items.Add(p.Nombre);
        int ip = perfiles.FindIndex(delegate (Perfil_ p) { return p.Nombre == ajustes.Perfil; });
        comboPerfil.SelectedIndex = ip >= 0 ? ip : 3;
        Pos(comboPerfil, m, y + 22, 260, 30);
        lblPerfil = Texto("", Tema.Pequena, Tema.TextoSuave, m + 276, y + 20, w - 276, 36);
        y += 66;

        // Pasos
        Texto("PASOS", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        y += 22;
        chipSilencios.Activo = ajustes.Silencios;
        chipTranscribir.Activo = ajustes.Transcribir;
        chipPedir.Activo = ajustes.Pedir;
        lblPaso1 = Paso(chipSilencios, "1", y); y += 34;
        lblPaso2 = Paso(chipTranscribir, "2", y); y += 34;
        lblPaso3 = Paso(chipPedir, "3", y); y += 40;
        lblPaso3.Text = "Al final se abre MomentosIA. Activa esto para que adem\u00e1s le pida a Gemini solo.";
        lblWhisper = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 26;

        lblEstado = Texto("", Tema.Negrita, Tema.Texto, m, y, w, 22);
        Pos(barra, m, y + 28, w, 10);
        lblDetalle = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 44, w, 18);
        y += 76;
        Pos(btnCerrar, m + w - 330, y, 110, 40);
        Pos(btnEmpezar, m + w - 210, y, 210, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        foreach (Boton c in new Boton[] { chipSilencios, chipTranscribir, chipPedir })
        {
            Boton b = c;
            b.Click += delegate { if (!trabajando) { b.Activo = !b.Activo; Actualizar(); } };
        }
        comboPerfil.SelectedIndexChanged += delegate { Actualizar(); };
        btnEmpezar.Click += delegate { if (trabajando) Cancelar(); else Empezar(); };
        btnCerrar.Click += delegate { Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e)
        {
            if (trabajando && MessageBox.Show(this, "\u00bfCancelar la preparaci\u00f3n?", "Preparar episodio", MessageBoxButtons.YesNo) == DialogResult.No)
            { e.Cancel = true; return; }
            if (trabajando) Cancelar();
        };
        reloj.Interval = 300;
        reloj.Tick += delegate { Revisar(); };
        Actualizar();
    }

    Etiqueta Paso(Boton chip, string n, int y)
    {
        Pos(chip, Margen, y, 210, 28);
        return Texto("", Tema.Pequena, Tema.TextoSuave, Margen + 222, y + 4, Ancho - 222, 20);
    }

    int Chips(List<Boton> lista, int y)
    {
        int cx = Margen;
        foreach (InfoPista p in pistas)
        {
            Boton c = new Boton(p.Nombre, EstiloBoton.Chip);
            int w = Math.Min(Ancho, TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 26);
            if (cx + w > Margen + Ancho) { cx = Margen; y += 34; }
            Pos(c, cx, y, w, 28);
            lista.Add(c);
            cx += w + 6;
        }
        return y + 28;
    }

    // Las pistas de la vez pasada (por etiqueta); si no hay, la voz sugerida.
    void Elegir()
    {
        bool alguna = false;
        for (int i = 0; i < pistas.Count; i++)
        {
            chipsVoz[i].Activo = ajustes.Voces.Contains(pistas[i].Etiqueta);
            chipsAmbiente[i].Activo = !chipsVoz[i].Activo && ajustes.Ambiente.Contains(pistas[i].Etiqueta);
            alguna |= chipsVoz[i].Activo;
        }
        if (!alguna) chipsVoz[PistasVegas.SugerirVoz(pistas)].Activo = true;
    }

    List<int> Elegidas(List<Boton> chips)
    {
        List<int> r = new List<int>();
        for (int i = 0; i < chips.Count; i++) if (chips[i].Activo) r.Add(i);
        return r;
    }

    Perfil_ PerfilElegido { get { return perfiles[Math.Max(0, comboPerfil.SelectedIndex)]; } }

    void Actualizar()
    {
        lblPerfil.Text = PerfilElegido.Descripcion;
        lblPaso1.Text = chipSilencios.Activo ? "Mide las voces y quita las pausas de todas las pistas, con el perfil elegido." : "Se salta (ya est\u00e1n quitados).";
        lblPaso2.Text = chipTranscribir.Activo ? "Whisper transcribe las voces; tarda seg\u00fan la duraci\u00f3n y tu tarjeta." : "Se salta (usa la transcripci\u00f3n que ya hay).";
        bool voces = Elegidas(chipsVoz).Count > 0;
        if (chipTranscribir.Activo && !config.TieneWhisper)
        {
            lblWhisper.ForeColor = Tema.Silencio;
            lblWhisper.Text = "Falta configurar Faster-Whisper-XXL: ejecuta \u201cConfigurarVegasCut\u201d.";
        }
        else if (!chipTranscribir.Activo && !File.Exists(ruta))
        {
            lblWhisper.ForeColor = Tema.Silencio;
            lblWhisper.Text = "Sin transcribir no hay transcripci\u00f3n para MomentosIA: activa \u201cTranscribir\u201d.";
        }
        else
        {
            lblWhisper.ForeColor = Tema.TextoSuave;
            lblWhisper.Text = "Whisper: " + config.WhisperModelo + " \u00b7 " + (config.WhisperDispositivo == "cpu" ? "procesador" : "tarjeta") +
                              " \u00b7 idioma " + config.Idioma;
        }
        btnEmpezar.Enabled = trabajando || (voces && (!chipTranscribir.Activo || config.TieneWhisper) &&
                                            (chipTranscribir.Activo || File.Exists(ruta)));
    }

    void Estado(string texto, double fraccion)
    {
        lblEstado.ForeColor = Tema.Texto;
        lblEstado.Text = texto;
        barra.Valor = fraccion;
        Application.DoEvents();
    }

    void Bloquear(bool si)
    {
        trabajando = si;
        btnEmpezar.Text = si ? "Cancelar" : "Empezar";
        btnCerrar.Enabled = !si;
        comboPerfil.Enabled = !si;
        foreach (Boton b in chipsVoz) b.Enabled = !si;
        foreach (Boton b in chipsAmbiente) b.Enabled = !si;
        foreach (Boton b in new Boton[] { chipSilencios, chipTranscribir, chipPedir }) b.Enabled = !si;
    }

    void Empezar()
    {
        // Se recuerda lo elegido.
        ajustes.Voces.Clear(); ajustes.Ambiente.Clear();
        foreach (int i in Elegidas(chipsVoz)) ajustes.Voces.Add(pistas[i].Etiqueta);
        foreach (int i in Elegidas(chipsAmbiente)) ajustes.Ambiente.Add(pistas[i].Etiqueta);
        ajustes.Perfil = PerfilElegido.Nombre;
        ajustes.Silencios = chipSilencios.Activo; ajustes.Transcribir = chipTranscribir.Activo; ajustes.Pedir = chipPedir.Activo;
        ajustes.Guardar();

        Bloquear(true);
        comienzo = DateTime.Now;
        if (chipSilencios.Activo)
        {
            lblPaso1.ForeColor = Tema.Texto;
            try
            {
                List<InfoPista> voces = new List<InfoPista>();
                foreach (int i in Elegidas(chipsVoz)) voces.Add(pistas[i]);
                double quitado;
                int n = PasoSilencios.Ejecutar(vegas, voces, PerfilElegido, delegate (string t, double f) { Estado("1 \u00b7 " + t, f * 0.1); }, out quitado);
                Hecho(lblPaso1, n == 0 ? "No hab\u00eda silencios que quitar." : "\u2714 " + n + " silencios quitados (" + Formato.Tiempo(quitado) + ").");
            }
            catch (Exception ex) { Fallo(lblPaso1, "No se pudieron quitar los silencios: " + ex.Message); return; }
        }
        if (!chipTranscribir.Activo) { Terminar(); return; }

        lblPaso2.ForeColor = Tema.Texto;
        proceso = new ProcesoTranscripcion(vegas, config, ruta);
        try
        {
            proceso.Empezar(pistas, Elegidas(chipsVoz), Elegidas(chipsAmbiente), delegate (string t, double f) { Estado("2 \u00b7 " + t, 0.1 + f * 0.9); });
        }
        catch (Exception ex) { Fallo(lblPaso2, ex.Message); return; }
        reloj.Start();
    }

    void Revisar()
    {
        if (proceso == null) return;
        proceso.Revisar();
        if (!proceso.Terminado)
        {
            Estado("2 \u00b7 " + proceso.Texto, 0.1 + 0.9 * proceso.Fraccion);
            string d = proceso.Detalle ?? "";
            lblDetalle.Text = d.Length > 140 ? d.Substring(0, 140) + "\u2026" : d;
            return;
        }
        reloj.Stop();
        ProcesoTranscripcion p = proceso;
        proceso = null;
        if (p.Error != null) { Fallo(lblPaso2, p.Error); return; }
        Hecho(lblPaso2, "\u2714 " + p.Texto);
        Terminar();
    }

    void Hecho(Etiqueta l, string texto)
    {
        l.ForeColor = Color.FromArgb(120, 220, 150);
        l.Text = texto;
        Application.DoEvents();
    }

    void Fallo(Etiqueta l, string mensaje)
    {
        reloj.Stop();
        Bloquear(false);
        Actualizar();
        l.ForeColor = Tema.Silencio;
        l.Text = "\u2716 " + mensaje;
        lblEstado.ForeColor = Tema.Silencio;
        lblEstado.Text = "Se detuvo. Lo que ya se hizo se puede deshacer con Ctrl+Z.";
    }

    void Terminar()
    {
        Bloquear(false);
        barra.Valor = 1;
        string copia = "";
        if (chipTranscribir.Activo || chipSilencios.Activo)
            try
            {
                string b = CopiaBase.Guardar(vegas);
                if (b != null) copia = " Copia base: " + Path.GetFileName(b) + ".";
            }
            catch (Exception ex) { copia = " (No se pudo guardar la copia base: " + ex.Message + ")"; }
        lblEstado.Text = "\u2714 Listo en " + Formato.Tiempo((DateTime.Now - comienzo).TotalSeconds) + "." + copia + " Abriendo MomentosIA\u2026";
        Application.DoEvents();
        AbrirMomentos = true;
        Pedir = chipPedir.Activo;
        DialogResult = DialogResult.OK;
        Close();
    }

    void Cancelar()
    {
        if (proceso != null) { reloj.Stop(); proceso.Cancelar(); proceso = null; }
        Fallo(lblPaso2, "Transcripci\u00f3n cancelada; no se guard\u00f3.");
    }
}

// ---- src/comun/Proceso.cs ----

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
            estado("Midiendo " + voces[i].Nombre + " (" + (i + 1) + " de " + voces.Count + ")\u2026", (double)i / voces.Count);
            Analisis a = PistasVegas.Niveles(vegas, voces[i].Pista, 0, duracion);
            datos.Add(a);
            umbrales.Add(Detector.UmbralAutomatico(a.Db));
        }
        List<Rango> rangos = Editor.AjustarAFotogramas(Detector.Detectar(datos, umbrales, v), p.Video.FrameRate);
        quitado = 0;
        foreach (Rango r in rangos) quitado += r.Fin - r.Inicio;
        if (rangos.Count == 0) return 0;

        estado("Quitando " + rangos.Count + " silencios\u2026", 1);
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
                estado("Leyendo el audio de " + p.Nombre + " (" + (k + 1) + " de " + leer.Count + ")\u2026", 0.02 * (k + 1) / leer.Count);
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
                Formato.Tiempo(duracion) + " \u00b7 " + Formato.Tiempo(pasado) + " transcurrido" +
                (hecho > 0.03 ? " \u00b7 faltan ~" + Formato.Tiempo(pasado * (1 - hecho) / hecho) : "");
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
        catch (Exception ex) { Fallar("No se pudo guardar la transcripci\u00f3n: " + ex.Message); return; }
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

// ---- src/momentos/Entrada.cs ----

// Abre la ventana de MomentosIA para el proyecto (la usan MomentosIA y
// PrepararEpisodio). Con "pedir", le pide a Gemini apenas se abre.
public static class AbrirMomentos
{
    public static void Abrir(Vegas vegas, bool pedir)
    {
        string veg = vegas.Project.FilePath;
        string ruta = Transcripcion.RutaPara(veg);
        if (ruta == null || !File.Exists(ruta))
        {
            MessageBox.Show("Este proyecto a\u00fan no tiene transcripci\u00f3n.\n\nEjecuta primero \u201cTranscribir\u201d.", "Momentos con IA");
            return;
        }
        Transcripcion t;
        try { t = Transcripcion.Cargar(ruta); }
        catch (Exception ex) { MessageBox.Show("No se pudo leer la transcripci\u00f3n: " + ex.Message, "Momentos con IA"); return; }

        // Con las fuentes, la transcripcion sigue tambien las ediciones a mano.
        if (t.TieneFuentes) t.Ubicador = PistasVegas.Ubicador(vegas.Project, t);
        using (VentanaMomentos v = new VentanaMomentos(vegas, t, ruta))
        {
            v.PedirAlAbrir = pedir;
            v.ShowDialog();
        }
    }
}

// ---- src/momentos/Momentos.cs ----

// Editor de las reglas del canal (se guardan para todos los proyectos).
class DialogoReglas : VentanaBase
{
    CampoTexto txt = new CampoTexto();
    public string Reglas { get { return txt.Text.Trim(); } }

    public DialogoReglas(string reglas) : base("Reglas del canal", 640)
    {
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Reglas del canal", "Se aplican siempre, en todos los videos. Una regla por l\u00ednea.");
        txt.Multilinea = true;
        txt.Text = reglas;
        Pos(txt, m, 92, w, 300);
        Boton restaurar = new Boton("Restaurar las de siempre", EstiloBoton.Secundario);
        Boton cancelar = new Boton("Cancelar", EstiloBoton.Secundario);
        Boton guardar = new Boton("Guardar", EstiloBoton.Primario);
        Pos(restaurar, m, 408, 200, 38);
        Pos(cancelar, m + w - 250, 408, 110, 38);
        Pos(guardar, m + w - 130, 408, 130, 38);
        ClientSize = new Size(ClientSize.Width, 470);
        restaurar.Click += delegate { txt.Text = PeticionIA.ReglasPorDefecto; };
        cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        guardar.Click += delegate { DialogResult = DialogResult.OK; Close(); };
    }
}

// Pide el tramo fijo: desde y hasta, en 57:00, 1:09:30 o segundos.
class DialogoTramo : VentanaBase
{
    CampoTexto txtDesde = new CampoTexto(), txtHasta = new CampoTexto();
    Etiqueta lblError;
    readonly double total;
    public double Inicio, Fin;

    public DialogoTramo(double a, double b, double total) : base("Conservar tramo", 520)
    {
        this.total = total;
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Conservar tramo completo", "Entra al corte tal cual, aunque la IA no lo haya elegido.");
        Texto(a >= 0 ? "Se llen\u00f3 con lo que ten\u00edas seleccionado en Vegas. Puedes corregirlo."
                     : "Escribe el tramo como 57:00 y 1:09:30 (o selecci\u00f3nalo en Vegas antes de abrir MomentosIA).",
              Tema.Pequena, Tema.TextoSuave, m, 90, w, 34);
        Texto("DESDE", Tema.Pequena, Tema.TextoSuave, m, 130, 200, 18);
        Texto("HASTA", Tema.Pequena, Tema.TextoSuave, m + 236, 130, 200, 18);
        txtDesde.Text = a >= 0 ? Formato.Tiempo(a) : "";
        txtHasta.Text = b >= 0 ? Formato.Tiempo(b) : "";
        Pos(txtDesde, m, 150, 220, 36);
        Pos(txtHasta, m + 236, 150, 220, 36);
        lblError = Texto("", Tema.Pequena, Tema.Silencio, m, 194, w, 20);
        Boton cancelar = new Boton("Cancelar", EstiloBoton.Secundario);
        Boton aceptar = new Boton("Conservar", EstiloBoton.Primario);
        Pos(cancelar, m + w - 260, 226, 110, 38);
        Pos(aceptar, m + w - 140, 226, 140, 38);
        ClientSize = new Size(ClientSize.Width, 288);
        cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        aceptar.Click += delegate { Aceptar(); };
        AcceptButton = null;
        txtHasta.Caja.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) Aceptar(); };
        Shown += delegate { (a >= 0 ? txtHasta : txtDesde).Caja.Focus(); };
    }

    void Aceptar()
    {
        double a = Leer(txtDesde.Text), b = Leer(txtHasta.Text);
        if (double.IsNaN(a) || double.IsNaN(b)) { lblError.Text = "Escribe los tiempos como 57:00, 1:09:30 o en segundos."; return; }
        if (b < a) { double x = a; a = b; b = x; }
        a = Math.Max(0, a); b = Math.Min(total, b);
        if (b - a < 1) { lblError.Text = "El tramo tiene que durar al menos 1 segundo y estar dentro del proyecto (" + Formato.Tiempo(total) + ")."; return; }
        Inicio = a; Fin = b;
        DialogResult = DialogResult.OK;
        Close();
    }

    // "1:09:30", "57:00", "57:00.5" o "3420".
    public static double Leer(string texto)
    {
        string t = (texto ?? "").Trim().Replace(',', '.');
        if (t.Length == 0) return double.NaN;
        string[] partes = t.Split(':');
        if (partes.Length > 3) return double.NaN;
        double r = 0;
        foreach (string p in partes)
        {
            double v;
            if (!double.TryParse(p.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) || v < 0) return double.NaN;
            r = r * 60 + v;
        }
        return r;
    }
}

class VentanaMomentos : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion transcripcion;
    readonly string rutaTranscripcion, rutaIA, rutaInforme, rutaHistorial;
    readonly Configuracion config = Configuracion.Cargar();
    readonly double total;
    ResultadoIA resultado;
    OpcionesIA opciones = new OpcionesIA();
    public bool PedirAlAbrir;                          // PrepararEpisodio: pedir a Gemini al abrir
    List<CapSerie> capitulos = new List<CapSerie>();   // capitulos de la misma serie
    SerieProyecto serie;
    List<string> historial = new List<string>();  // respuestas guardadas de este proyecto
    bool cargando, aplicado, vigente;

    Segmentado segTipo = new Segmentado(new string[] { "Gameplay", "Narraci\u00f3n", "Podcast", "Otro" });
    CampoNumero numMin = new CampoNumero(), numMax = new CampoNumero();
    Segmentado segAcelerar = new Segmentado(new string[] { "Cortar", "Acelerar" });
    Segmentado segAudio = new Segmentado(new string[] { "Mudo", "Acelerado" });
    List<CampoTexto> nombres = new List<CampoTexto>();
    CampoTexto txtInstrucciones = new CampoTexto();
    Boton btnReglas = new Boton("Reglas del canal\u2026", EstiloBoton.Secundario);
    Boton btnContexto = new Boton("Serie\u2026", EstiloBoton.Secundario);
    Etiqueta lblContexto;
    Combo comboModelo = new Combo(true);
    Etiqueta lblModelo;
    Boton btnPedir = new Boton("Pedir a Gemini", EstiloBoton.Primario);
    Etiqueta lblEstado;

    Combo comboHistorial = new Combo();
    Segmentado pestanas = new Segmentado(new string[] { "Corte", "Momentos", "Textos", "Resumen", "Shorts y t\u00edtulos" });
    Lista lstCorte = new Lista(), lstMomentos = new Lista(), lstTextos = new Lista(), lstShorts = new Lista();
    CampoTexto txtResumen = new CampoTexto();
    Etiqueta lblCorte;

    Boton btnInforme = new Boton("Guardar informe", EstiloBoton.Secundario);
    Boton btnMarcar = new Boton("Crear regiones y marcadores", EstiloBoton.Secundario);
    Boton btnCortar = new Boton("Aplicar corte", EstiloBoton.Primario);
    Boton btnFijar = new Boton("Conservar tramo\u2026", EstiloBoton.Secundario);
    Boton chipCuentan = new Boton("Los fijos cuentan en la duraci\u00f3n", EstiloBoton.Chip);
    bool fijosCuentan = true;
    List<Tramo> fijos = new List<Tramo>();   // tramos elegidos a mano (selecci\u00f3n de tiempo)
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    static readonly string[] Tipos = { "Gameplay", "Narraci\u00f3n", "Podcast", "Otro" };

    public VentanaMomentos(Vegas vegas, Transcripcion t, string ruta) : base("Momentos con IA", 1040)
    {
        this.vegas = vegas;
        transcripcion = t;
        rutaTranscripcion = ruta;
        string veg = vegas.Project.FilePath;
        string baseNombre = Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg));
        rutaIA = baseNombre + ".vegascut-ia.json";
        rutaInforme = baseNombre + ".vegascut-informe.md";
        rutaHistorial = baseNombre + ".vegascut-ia-historial";
        total = vegas.Project.Length.ToMilliseconds() / 1000.0;

        int m = Margen, w = Ancho;
        Encabezado("Momentos con IA", "Gemini lee la transcripci\u00f3n y la intensidad del sonido, y sugiere qu\u00e9 conservar.");

        // Estado de la transcripcion
        string aviso = t.Sincronizar(total);
        if (aviso.StartsWith("Se detect")) { try { t.Guardar(ruta); } catch { } }
        if (t.Ubicador != null && !aviso.StartsWith("Se detect")) aviso = ""; // sigue las ediciones a mano
        int frases = t.SegmentosActuales().Count;
        Texto("Transcripci\u00f3n del " + t.Creada + ": " + frases + " frases \u00b7 proyecto de " +
            Formato.Tiempo(total) + (aviso.Length > 0 ? "\n" + aviso : ""), Tema.Pequena,
            aviso.Length > 0 && !aviso.StartsWith("Se detect") ? Tema.AcentoHover : Tema.TextoSuave, m, 88, w, 34);

        // ---------------- Columna izquierda: lo que se pide
        int y = 130, ci = 320;
        Texto("Tipo de video", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        Pos(segTipo, m, y + 22, ci, 34);
        y += 66;
        Texto("Duraci\u00f3n del corte", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        foreach (CampoNumero n in new CampoNumero[] { numMin, numMax })
        {
            n.Sufijo = "min"; n.Minimo = 1; n.Maximo = 600; n.Paso = 1;
        }
        Pos(numMin, m, y + 22, 92, 36);
        Texto("a", Tema.Normal, Tema.TextoSuave, m + 98, y + 30, 16, 20);
        Pos(numMax, m + 118, y + 22, 92, 36);
        Texto("m\u00ednimo y m\u00e1ximo", Tema.Pequena, Tema.TextoSuave, m + 218, y + 30, ci - 218, 20);
        y += 66;
        Texto("Transiciones", Tema.Negrita, Tema.Texto, m, y + 8, 110, 20);
        Pos(segAcelerar, m + 120, y, ci - 120, 34);
        y += 42;
        Texto("Audio acelerado", Tema.Negrita, Tema.Texto, m, y + 8, 120, 20);
        Pos(segAudio, m + 120, y, ci - 120, 34);
        y += 46;
        Texto("Nombres de las personas", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        y += 24;
        foreach (Hablante h in t.Hablantes)
        {
            if (!h.Voz) continue;
            Texto(h.Etiqueta, Tema.Negrita, Tema.Voz, m, y + 6, 48, 22);
            CampoTexto c = new CampoTexto();
            c.Text = h.Nombre;
            c.Tag = h;
            Pos(c, m + 52, y, ci - 52, 32);
            nombres.Add(c);
            y += 38;
        }
        y += 4;
        Texto("Indicaciones de este episodio", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        txtInstrucciones.Multilinea = true;
        Pos(txtInstrucciones, m, y + 22, ci, 84);
        y += 114;
        Pos(btnReglas, m, y, 150, 32);
        Pos(btnContexto, m + 158, y, ci - 158, 32);
        lblContexto = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 36, ci, 18);
        y += 60;
        Texto("Modelo", Tema.Negrita, Tema.Texto, m, y + 6, 70, 20);
        comboModelo.Items.Add(config.GeminiModelo);
        foreach (string x in new string[] { "gemini-flash-latest", "gemini-pro-latest", "gemini-flash-lite-latest" })
            if (!comboModelo.Items.Contains(x)) comboModelo.Items.Add(x);
        comboModelo.Text = config.GeminiModelo;
        Pos(comboModelo, m + 70, y + 2, ci - 70, 30);
        lblModelo = Texto("", Tema.Pequena, Tema.AcentoHover, m, y + 36, ci, 32);
        y += 72;
        Pos(btnPedir, m, y, ci, 42);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 48, ci, 48);
        int fondoIzq = y + 100;

        // ---------------- Columna derecha: resultado
        int dx = m + ci + 24, dw = w - ci - 24, dy = 130;
        Texto("Respuesta", Tema.Negrita, Tema.Texto, dx, dy + 6, 84, 20);
        Pos(comboHistorial, dx + 88, dy + 2, dw - 88, 30);
        dy += 42;
        Pos(pestanas, dx, dy, dw, 34);
        dy += 44;
        int alto = Math.Max(420, fondoIzq - dy - 40);
        ConfigurarListas();
        foreach (Control c in new Control[] { lstCorte, lstMomentos, lstTextos, lstShorts, txtResumen }) Pos(c, dx, dy, dw, alto);
        // La ultima columna ocupa el espacio que sobra, sin barra horizontal.
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts })
        {
            int usado = 0;
            for (int i = 0; i < l.Columns.Count - 1; i++) usado += l.Columns[i].Width;
            l.Columns[l.Columns.Count - 1].Width = Math.Max(150, dw - usado - SystemInformation.VerticalScrollBarWidth - 4);
        }
        txtResumen.Multilinea = true;
        txtResumen.Caja.ReadOnly = true;
        lblCorte = Texto("", Tema.Negrita, Tema.Texto, dx, dy + alto + 8, dw - 262, 22);
        Pos(chipCuentan, dx + dw - 254, dy + alto + 6, 254, 26);
        int fondo = Math.Max(fondoIzq, dy + alto + 40);

        Pos(btnInforme, m, fondo, 150, 40);
        Pos(btnMarcar, m + 160, fondo, 230, 40);
        Pos(btnFijar, m + 400, fondo, 190, 40);
        Pos(btnCerrar, m + w - 300, fondo, 110, 40);
        Pos(btnCortar, m + w - 180, fondo, 180, 40);
        ClientSize = new Size(ClientSize.Width, fondo + 40 + 24);

        // Eventos
        pestanas.Cambio += delegate { MostrarPestana(); };
        btnPedir.Click += delegate { Pedir(); };
        btnInforme.Click += delegate { GuardarInforme(); };
        btnMarcar.Click += delegate { CrearMarcas(); };
        btnFijar.Click += delegate { Fijar(); };
        chipCuentan.Click += delegate { CambiarCuentan(); };
        btnCortar.Click += delegate { AplicarCorte(); };
        btnCerrar.Click += delegate { Close(); };
        btnReglas.Click += delegate { EditarReglas(); };
        btnContexto.Click += delegate { ElegirContexto(); };
        comboModelo.TextChanged += delegate { AvisoModelo(); };
        comboHistorial.SelectedIndexChanged += delegate { if (!cargando && comboHistorial.SelectedIndex >= 0) CargarEntrada(historial[comboHistorial.SelectedIndex], false); };
        lstCorte.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
        {
            if (cargando || resultado == null) return;
            Tramo tc = (Tramo)e.Item.Tag;
            tc.Elegido = e.Item.Checked;
            if (!tc.Elegido && tc.Fijo) QuitarFijo(tc);
            ActualizarResumenCorte();
        };
        foreach (Lista l in new Lista[] { lstMomentos, lstShorts })
            l.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((Tramo)e.Item.Tag).Elegido = e.Item.Checked; };
        lstTextos.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((TextoResumen)e.Item.Tag).Elegido = e.Item.Checked; };
        lstCorte.MouseClick += delegate (object s, MouseEventArgs e) { CambiarVelocidad(e); };
        segAcelerar.Cambio += delegate { segAudio.Enabled = segAcelerar.Seleccion == 1; };
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts })
            l.DoubleClick += delegate (object s, EventArgs e) { IrA((ListView)s); };

        capitulos = Serie.DelProyecto(vegas.Project.FilePath, out serie);
        MostrarContexto();
        fijos = TramosFijos.Cargar(vegas.Project.FilePath, total);
        fijosCuentan = TramosFijos.Cuentan(vegas.Project.FilePath);
        chipCuentan.Activo = fijosCuentan;

        // Valores iniciales: los de la ultima respuesta guardada.
        cargando = true;
        LlenarHistorial();
        PonerOpciones(historial.Count > 0 ? historial[0] : null);
        cargando = false;
        if (historial.Count > 0) CargarEntrada(historial[0], true);
        AvisoModelo();
        if (!config.TieneGemini)
        {
            btnPedir.Enabled = false;
            Estado("Falta la clave de Gemini: ejecuta \u201cConfigurarVegasCut\u201d.", true);
        }
        MostrarResultado();
        Shown += delegate { if (PedirAlAbrir && btnPedir.Enabled) Pedir(); };
    }

    void ConfigurarListas()
    {
        lstCorte.Columns.Add("Inicio", 70); lstCorte.Columns.Add("Fin", 70); lstCorte.Columns.Add("Dura", 52);
        lstCorte.Columns.Add("Velocidad", 84); lstCorte.Columns.Add("Imp.", 40); lstCorte.Columns.Add("Tramo", 180);
        lstCorte.Columns.Add("Por qu\u00e9", 900);
        lstMomentos.Columns.Add("Nota", 60); lstMomentos.Columns.Add("Inicio", 70); lstMomentos.Columns.Add("Fin", 70);
        lstMomentos.Columns.Add("Momento", 200); lstMomentos.Columns.Add("Por qu\u00e9", 900);
        lstTextos.Columns.Add("D\u00f3nde", 70); lstTextos.Columns.Add("Texto", 280); lstTextos.Columns.Add("Qu\u00e9 se salta", 900);
        lstShorts.Columns.Add("Inicio", 70); lstShorts.Columns.Add("Fin", 70); lstShorts.Columns.Add("Short", 220);
        lstShorts.Columns.Add("Por qu\u00e9 funciona", 900);
    }

    // ------------------------------------------------------- opciones

    void PonerOpciones(string archivo)
    {
        double mins = Math.Max(1, Math.Round(total / 60 / 4));
        opciones.MinutosMin = mins; opciones.MinutosMax = mins + 4;
        opciones.ReglasCanal = config.ReglasCanal.Length > 0 ? config.ReglasCanal : PeticionIA.ReglasPorDefecto;
        try
        {
            object op = archivo != null ? Json.Obj(Json.Leer(File.ReadAllText(archivo, Encoding.UTF8)), "opciones") : null;
            if (op != null)
            {
                opciones.Tipo = Json.Texto(op, "tipo");
                double viejo = Json.Numero(op, "minutos", -1); // respuestas de antes de min/max
                opciones.MinutosMin = Json.Numero(op, "minutosMin", viejo > 0 ? viejo : opciones.MinutosMin);
                opciones.MinutosMax = Json.Numero(op, "minutosMax", viejo > 0 ? viejo + 2 : opciones.MinutosMax);
                opciones.Instrucciones = Json.Texto(op, "instrucciones");
                opciones.PermitirAcelerar = Json.Texto(op, "acelerar") != "False";
                opciones.SilenciarAcelerado = Json.Texto(op, "silenciarAcelerado") != "False";
            }
        }
        catch { }
        segTipo.Seleccion = Math.Max(0, Array.IndexOf(Tipos, opciones.Tipo));
        numMin.Valor = (int)opciones.MinutosMin;
        numMax.Valor = (int)opciones.MinutosMax;
        txtInstrucciones.Text = opciones.Instrucciones;
        segAcelerar.Seleccion = opciones.PermitirAcelerar ? 1 : 0;
        segAudio.Seleccion = opciones.SilenciarAcelerado ? 0 : 1;
        segAudio.Enabled = opciones.PermitirAcelerar;
        MostrarContexto();
    }

    void LeerOpciones()
    {
        opciones.Tipo = Tipos[segTipo.Seleccion];
        opciones.MinutosMin = Math.Min(numMin.Valor, numMax.Valor);
        opciones.MinutosMax = Math.Max(numMin.Valor, numMax.Valor);
        opciones.Instrucciones = txtInstrucciones.Text;
        opciones.PermitirAcelerar = segAcelerar.Seleccion == 1;
        opciones.SilenciarAcelerado = segAudio.Seleccion == 0;
        opciones.ReglasCanal = config.ReglasCanal.Length > 0 ? config.ReglasCanal : PeticionIA.ReglasPorDefecto;
        opciones.Contexto = Serie.Contexto(serie, capitulos);
        opciones.Fijos = new List<Tramo>(fijos);
        opciones.FijosCuentan = fijosCuentan;
        foreach (CampoTexto c in nombres)
        {
            Hablante h = (Hablante)c.Tag;
            h.Nombre = c.Text.Trim().Length > 0 ? c.Text.Trim() : h.Etiqueta;
        }
    }

    void EditarReglas()
    {
        string actuales = config.ReglasCanal.Length > 0 ? config.ReglasCanal : PeticionIA.ReglasPorDefecto;
        using (DialogoReglas d = new DialogoReglas(actuales))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            config.ReglasCanal = d.Reglas == PeticionIA.ReglasPorDefecto.Trim() ? "" : d.Reglas;
            try { config.Guardar(); Estado("\u2714 Reglas del canal guardadas.", false); }
            catch (Exception ex) { Estado("No se pudieron guardar las reglas: " + ex.Message, true); }
        }
    }

    void ElegirContexto()
    {
        string modelo = comboModelo.Text.Trim().Length > 0 ? comboModelo.Text.Trim() : config.GeminiModelo;
        using (VentanaSeries d = new VentanaSeries(vegas.Project.FilePath, config.GeminiClave, modelo, true)) d.ShowDialog(this);
        capitulos = Serie.DelProyecto(vegas.Project.FilePath, out serie);
        MostrarContexto();
    }

    void MostrarContexto()
    {
        lblContexto.Text = VentanaSeries.Resumen(serie, capitulos);
    }

    void AvisoModelo()
    {
        string mo = comboModelo.Text.ToLowerInvariant();
        lblModelo.Text = mo.Contains("lite")
            ? "Lite es m\u00e1s barato pero sigue peor las reglas y la duraci\u00f3n. Para cortes largos usa flash o pro."
            : "";
    }

    // ------------------------------------------------------- historial

    void LlenarHistorial()
    {
        historial.Clear();
        if (Directory.Exists(rutaHistorial))
        {
            string[] f = Directory.GetFiles(rutaHistorial, "*.json");
            Array.Sort(f);
            Array.Reverse(f);
            historial.AddRange(f);
        }
        if (historial.Count == 0 && File.Exists(rutaIA)) historial.Add(rutaIA);
        comboHistorial.Items.Clear();
        foreach (string f in historial)
        {
            string texto = Path.GetFileName(f);
            try
            {
                object o = Json.Leer(File.ReadAllText(f, Encoding.UTF8));
                object op = Json.Obj(o, "opciones");
                double mn = Json.Numero(op, "minutosMin", Json.Numero(op, "minutos", 0)), mx = Json.Numero(op, "minutosMax", mn);
                texto = Json.Texto(o, "fecha") + "  \u00b7  " + Json.Texto(o, "modelo") + "  \u00b7  " + mn + "\u2013" + mx + " min" +
                        (Math.Abs(Json.Numero(o, "duracionProyecto", -1) - total) > 0.5 ? "  \u00b7  (proyecto distinto)" : "");
            }
            catch { }
            comboHistorial.Items.Add(texto);
        }
        if (comboHistorial.Items.Count == 0) comboHistorial.Items.Add("Todav\u00eda no hay respuestas");
        comboHistorial.SelectedIndex = 0;
        comboHistorial.Enabled = historial.Count > 1;
    }

    // Arma el resultado desde la respuesta y la revision guardadas, con los
    // mismos pasos siempre (asi los indices de la revision cuadran).
    ResultadoIA Construir(string respuesta, string revision, double duracion, double min, double max, out string nota)
    {
        ResultadoIA r = ResultadoIA.Leer(respuesta, duracion);
        r.AjustarAPalabras(transcripcion.SegmentosActuales());
        nota = "";
        if (!String.IsNullOrEmpty(revision))
        {
            try
            {
                int n = r.AplicarRevision(revision);
                if (n > 0) nota = "revisi\u00f3n: " + n + (n == 1 ? " tramo quitado o recortado" : " tramos quitados o recortados");
            }
            catch { }
        }
        // Los tramos fijos se agregan despues de la revision (no cambian sus indices).
        if (Math.Abs(duracion - total) <= 0.5)
            foreach (Tramo f in fijos) r.AgregarFijo(f.Inicio, f.Fin, f.Titulo);
        r.FijosCuentan = fijosCuentan;
        string ajuste = r.AjustarDuracion(min * 60, max * 60);
        if (ajuste.Length > 0) nota += (nota.Length > 0 ? "; " : "") + ajuste;
        return r;
    }

    void CargarEntrada(string archivo, bool inicial)
    {
        try
        {
            object o = Json.Leer(File.ReadAllText(archivo, Encoding.UTF8));
            double duracion = Json.Numero(o, "duracionProyecto", total);
            object op = Json.Obj(o, "opciones");
            double mn = Json.Numero(op, "minutosMin", Json.Numero(op, "minutos", 1));
            double mx = Json.Numero(op, "minutosMax", Json.Numero(op, "minutos", 600) + 2);
            string nota;
            resultado = Construir(Json.Texto(o, "respuesta"), Json.Texto(o, "revision"), duracion, mn, mx, out nota);
            vigente = Math.Abs(duracion - total) <= 0.5;
            aplicado = false;
            if (vigente)
                Estado("Mostrando la respuesta del " + Json.Texto(o, "fecha") + " (" + Json.Texto(o, "modelo") + ")." +
                       (nota.Length > 0 ? " Ajustes: " + nota + "." : ""), false);
            else
                Estado("Respuesta del " + Json.Texto(o, "fecha") + ": el proyecto cambi\u00f3 desde entonces (duraba " +
                       Formato.Tiempo(duracion) + ", ahora " + Formato.Tiempo(total) + "). Puedes revisarla, pero para " +
                       "aplicarla deshaz los cambios (Ctrl+Z) o pide una nueva.", !inicial);
        }
        catch (Exception ex)
        {
            resultado = null;
            Estado("No se pudo abrir esa respuesta: " + ex.Message, true);
        }
        MostrarResultado();
    }

    // ------------------------------------------------------- Gemini

    void Pedir()
    {
        LeerOpciones();
        try { transcripcion.Guardar(rutaTranscripcion); } catch { } // guarda los nombres
        string clave = config.GeminiClave, modelo = comboModelo.Text.Trim();
        if (modelo.Length == 0) modelo = config.GeminiModelo;
        if (modelo != config.GeminiModelo) { config.GeminiModelo = modelo; try { config.Guardar(); } catch { } }
        OpcionesIA op = opciones;
        Transcripcion t = transcripcion;
        double duracion = total;

        btnPedir.Enabled = false;
        DateTime inicio = DateTime.Now;
        string paso = "Preparando\u2026";
        System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        reloj.Interval = 500;
        reloj.Tick += delegate
        {
            Estado(paso + " " + Formato.Tiempo((DateTime.Now - inicio).TotalSeconds) +
                   (duracion > 35 * 60 ? "\nVideo largo: se analiza por partes (varios minutos)." : "\nPuede tardar 1 o 2 minutos."), false);
        };
        reloj.Start();

        AsistenteIA asistente = new AsistenteIA(delegate (string instrucciones, string mensaje)
        {
            return Gemini.Generar(clave, modelo, instrucciones, mensaje, true);
        });
        asistente.Progreso = delegate (string texto) { paso = texto; };

        Thread hilo = new Thread(delegate ()
        {
            string respuesta = null, revision = "", error = null;
            try
            {
                respuesta = asistente.Ejecutar(t, duracion, op);
                // Segunda pasada: revisa el corte contra las reglas.
                ResultadoIA previo = ResultadoIA.Leer(respuesta, duracion);
                previo.AjustarAPalabras(t.SegmentosActuales());
                revision = asistente.Revisar(t, previo, op);
            }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    reloj.Stop();
                    btnPedir.Enabled = true;
                    if (error != null) { Estado(error, true); return; }
                    Recibir(respuesta, revision, modelo);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Recibir(string respuesta, string revision, string modelo)
    {
        string nota;
        try
        {
            resultado = Construir(respuesta, revision, total, opciones.MinutosMin, opciones.MinutosMax, out nota);
        }
        catch (Exception ex)
        {
            Estado("La respuesta de Gemini no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true);
            return;
        }

        // Se guarda cada respuesta (historial) y la ultima aparte.
        Dictionary<string, object> guardar = new Dictionary<string, object>();
        guardar["fecha"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        guardar["modelo"] = modelo;
        guardar["duracionProyecto"] = total;
        Dictionary<string, object> op = new Dictionary<string, object>();
        op["tipo"] = opciones.Tipo;
        op["minutosMin"] = opciones.MinutosMin;
        op["minutosMax"] = opciones.MinutosMax;
        op["instrucciones"] = opciones.Instrucciones;
        op["acelerar"] = opciones.PermitirAcelerar;
        op["silenciarAcelerado"] = opciones.SilenciarAcelerado;
        op["reglasCanal"] = opciones.ReglasCanal;
        List<object> ctx = new List<object>();
        foreach (CapSerie c in capitulos) if (c.Elegido && c.Relacion != 0 && c.TieneFicha) ctx.Add(c.Nombre);
        op["contexto"] = ctx;
        guardar["opciones"] = op;
        guardar["respuesta"] = respuesta;
        guardar["revision"] = revision;
        string texto = Json.Escribir(guardar);
        try
        {
            Directory.CreateDirectory(rutaHistorial);
            File.WriteAllText(Path.Combine(rutaHistorial, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), texto, new UTF8Encoding(false));
            File.WriteAllText(rutaIA, texto, new UTF8Encoding(false));
        }
        catch { }
        cargando = true;
        LlenarHistorial();
        cargando = false;

        aplicado = false;
        vigente = true;
        Estado("\u2714 Listo. Revisa las pesta\u00f1as; desmarca lo que no quieras." +
               (nota.Length > 0 ? " Ajustes autom\u00e1ticos: " + nota + "." : ""), false);
        MostrarResultado();
    }

    // ------------------------------------------------------- mostrar

    static string T(double s) { return Formato.TiempoPreciso(s); }

    static string PorQue(Tramo t)
    {
        return t.Nota.Length > 0 ? "\u26a0 " + t.Nota + " \u00b7 " + t.Motivo : t.Motivo;
    }

    void MostrarResultado()
    {
        cargando = true;
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts }) l.Items.Clear();
        bool hay = resultado != null;
        if (hay)
        {
            foreach (Tramo t in resultado.Corte)
                Fila(lstCorte, t, t.Elegido, T(t.Inicio), T(t.Fin), Formato.Tiempo(t.Duracion), Velocidad(t),
                     t.Fijo ? "fijo" : t.Puntuacion > 0 ? t.Puntuacion.ToString("0") : "", t.Titulo, PorQue(t));
            foreach (Tramo t in resultado.Momentos)
                Fila(lstMomentos, t, t.Elegido, t.Puntuacion.ToString("0") + "/10", T(t.Inicio), T(t.Fin), t.Titulo, t.Motivo);
            foreach (TextoResumen t in resultado.Textos)
                Fila(lstTextos, t, t.Elegido, T(t.Posicion), t.Texto, t.Motivo);
            foreach (Tramo t in resultado.Shorts)
                Fila(lstShorts, t, t.Elegido, T(t.Inicio), T(t.Fin), t.Titulo, t.Motivo);

            StringBuilder sb = new StringBuilder();
            sb.Append(resultado.Resumen.Replace("\n", "\r\n") + "\r\n\r\n");
            if (resultado.Secciones.Count > 0) sb.Append("SECCIONES\r\n");
            foreach (Tramo t in resultado.Secciones)
                sb.Append(T(t.Inicio) + "\u2013" + T(t.Fin) + "  " + t.Titulo + ": " + t.Motivo + "\r\n");
            txtResumen.Text = sb.ToString();
        }
        else txtResumen.Text = "";
        cargando = false;

        btnInforme.Enabled = hay;
        btnMarcar.Enabled = hay && vigente;
        btnCortar.Enabled = hay && vigente && !aplicado;
        ActualizarResumenCorte();
        MostrarPestana();
    }

    static string Velocidad(Tramo t)
    {
        return t.Acelerar ? "\u23e9 \u00d7" + t.Velocidad.ToString("0") + " (" + Formato.Tiempo(t.DuracionFinal) + ")" : "normal";
    }

    // Clic en la columna Velocidad: normal -> x2 -> x3 -> x4 -> normal.
    void CambiarVelocidad(MouseEventArgs e)
    {
        ListViewHitTestInfo hit = lstCorte.HitTest(e.Location);
        if (hit.Item == null || hit.SubItem == null || hit.Item.SubItems.IndexOf(hit.SubItem) != 3) return;
        Tramo t = (Tramo)hit.Item.Tag;
        if (!t.Acelerar) { t.Acelerar = true; t.Velocidad = 2; }
        else if (t.Velocidad < Editor.VelocidadMaxima) t.Velocidad++;
        else { t.Acelerar = false; t.Velocidad = 1; }
        hit.SubItem.Text = Velocidad(t);
        ActualizarResumenCorte();
    }

    void Fila(Lista l, object dato, bool marcado, params string[] columnas)
    {
        ListViewItem it = new ListViewItem(columnas[0]);
        for (int i = 1; i < columnas.Length; i++) it.SubItems.Add(columnas[i]);
        it.Tag = dato;
        it.Checked = marcado;
        l.Items.Add(it);
    }

    void MostrarPestana()
    {
        Control[] paginas = { lstCorte, lstMomentos, lstTextos, txtResumen, lstShorts };
        for (int i = 0; i < paginas.Length; i++) paginas[i].Visible = i == pestanas.Seleccion;
        if (pestanas.Seleccion == 4 && resultado != null && resultado.Titulos.Count > 0)
            lblCorte.Text = "T\u00edtulos: " + String.Join("  \u00b7  ", resultado.Titulos.ToArray());
        else ActualizarResumenCorte();
    }

    void ActualizarResumenCorte()
    {
        if (pestanas.Seleccion == 4) return;
        if (resultado == null) { lblCorte.Text = "Pide una sugerencia a Gemini para ver los resultados aqu\u00ed."; return; }
        double d = resultado.DuracionCorte, ajustable = resultado.DuracionAjustable;
        bool fuera = ajustable < numMin.Valor * 60 - 0.5 || ajustable > numMax.Valor * 60 + 0.5;
        lblCorte.ForeColor = fuera ? Tema.AcentoHover : Tema.Texto;
        string aparte = Math.Abs(d - ajustable) > 0.5 ? ", " + Formato.Tiempo(d - ajustable) + " fijos aparte" : "";
        lblCorte.Text = "Conserva " + Formato.Tiempo(d) + " de " + Formato.Tiempo(total) + " (" + numMin.Valor + "\u2013" +
                        numMax.Valor + " min" + aparte + (fuera ? ", fuera del rango" : "") + ") \u00b7 doble clic: ir";
    }

    // ------------------------------------------------------- tramos fijos

    // La seleccion de tiempo de Vegas se conserva completa en el corte, diga
    // lo que diga la IA. Sirve para lo que la IA no puede ver (una carrera
    // con poca conversacion) sin volver a pedir.
    // Lo que habia seleccionado en Vegas al abrir: la seleccion de tiempo o,
    // si no hay, los clips seleccionados. Devuelve false si no hay nada.
    bool SeleccionVegas(out double a, out double b)
    {
        a = 0; b = 0;
        try
        {
            a = vegas.Transport.SelectionStart.ToMilliseconds() / 1000.0;
            double largo = vegas.Transport.SelectionLength.ToMilliseconds() / 1000.0;
            if (largo < 0) { a += largo; largo = -largo; }
            if (largo >= 1) { b = a + largo; return true; }
        }
        catch { }
        a = double.MaxValue; b = double.MinValue;
        foreach (Track pista in vegas.Project.Tracks)
            foreach (TrackEvent e in pista.Events)
                if (e.Selected)
                {
                    a = Math.Min(a, e.Start.ToMilliseconds() / 1000.0);
                    b = Math.Max(b, e.End.ToMilliseconds() / 1000.0);
                }
        if (b - a >= 1) return true;
        a = 0; b = 0;
        return false;
    }

    void Fijar()
    {
        if (resultado != null && !vigente)
        {
            Estado("Esta respuesta es de antes de cambiar el proyecto. Deshaz el corte (Ctrl+Z), vuelve a abrir MomentosIA y elige el tramo.", true);
            return;
        }
        double a, b;
        bool hay = SeleccionVegas(out a, out b);
        using (DialogoTramo d = new DialogoTramo(hay ? a : -1, hay ? b : -1, total))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            a = d.Inicio; b = d.Fin;
        }

        string titulo = "Elegido a mano (" + Formato.Tiempo(a) + "\u2013" + Formato.Tiempo(b) + ")";
        fijos = TramosFijos.Agregar(fijos, a, b, titulo);
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos, fijosCuentan);
        string nota = "";
        if (resultado != null)
        {
            resultado.AgregarFijo(a, b, titulo);
            nota = resultado.AjustarDuracion(Math.Min(numMin.Valor, numMax.Valor) * 60, Math.Max(numMin.Valor, numMax.Valor) * 60);
            MostrarResultado();
        }
        Estado("\u2714 " + Formato.Tiempo(a) + "\u2013" + Formato.Tiempo(b) + " se conserva completo" +
               (resultado != null ? " en el corte" : "") + " y se le avisa a Gemini en las pr\u00f3ximas peticiones." +
               (nota.Length > 0 ? " Ajustes: " + nota + "." : "") + " Desm\u00e1rcalo en la lista para quitarlo.", false);
    }

    // Los tramos fijos cuentan o no para el minimo y el maximo del corte.
    void CambiarCuentan()
    {
        fijosCuentan = !fijosCuentan;
        chipCuentan.Activo = fijosCuentan;
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos, fijosCuentan);
        string nota = "";
        if (resultado != null)
        {
            resultado.FijosCuentan = fijosCuentan;
            nota = resultado.AjustarDuracion(Math.Min(numMin.Valor, numMax.Valor) * 60, Math.Max(numMin.Valor, numMax.Valor) * 60);
            MostrarResultado();
        }
        Estado(fijosCuentan
            ? "Los tramos fijos cuentan en la duraci\u00f3n: todo el corte queda entre el m\u00ednimo y el m\u00e1ximo."
            : "Los tramos fijos van aparte: el m\u00ednimo y el m\u00e1ximo son solo para el resto del corte." +
              (nota.Length > 0 ? " Ajustes: " + nota + "." : ""), false);
    }

    void QuitarFijo(Tramo t)
    {
        t.Fijo = false;
        t.Nota = "Ya no es fijo";
        fijos.RemoveAll(delegate (Tramo f) { return f.Inicio < t.Fin - 0.05 && f.Fin > t.Inicio + 0.05; });
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos, fijosCuentan);
        Estado("Ese tramo ya no es fijo.", false);
    }

    void IrA(ListView l)
    {
        if (l.SelectedItems.Count == 0) return;
        object d = l.SelectedItems[0].Tag;
        double t = d is Tramo ? ((Tramo)d).Inicio : ((TextoResumen)d).Posicion;
        try { vegas.Transport.CursorPosition = Timecode.FromMilliseconds(t * 1000); } catch { }
    }

    void Estado(string texto, bool error)
    {
        lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave;
        lblEstado.Text = texto;
    }

    // ------------------------------------------------------- acciones

    void GuardarInforme()
    {
        LeerOpciones();
        try
        {
            File.WriteAllText(rutaInforme, resultado.Informe(vegas.Project.FilePath, opciones, total), new UTF8Encoding(false));
            Estado("\u2714 Informe guardado en " + rutaInforme, false);
        }
        catch (Exception ex) { Estado("No se pudo guardar el informe: " + ex.Message, true); }
    }

    void CrearMarcas()
    {
        Project p = vegas.Project;
        List<Ancla> anclas = new List<Ancla>();
        int n = 0;
        using (UndoBlock deshacer = new UndoBlock("Momentos con IA: marcas"))
        {
            foreach (Tramo t in resultado.Corte)
                if (t.Elegido) { anclas.Add(Regiones(p, t.Inicio, t.Fin, (t.Acelerar ? "Acelerar \u00d7" + t.Velocidad.ToString("0") : "Conservar") + ": " + t.Titulo)); n++; }
            foreach (Tramo t in resultado.Momentos)
                if (t.Elegido) { anclas.Add(Marcador(p, t.Inicio, "\u2605" + t.Puntuacion.ToString("0") + " " + t.Titulo)); n++; }
            foreach (TextoResumen t in resultado.Textos)
                if (t.Elegido) { anclas.Add(Marcador(p, t.Posicion, "TEXTO: " + t.Texto)); n++; }
            foreach (Tramo t in resultado.Shorts)
                if (t.Elegido) { anclas.Add(Regiones(p, t.Inicio, t.Fin, "SHORT: " + t.Titulo)); n++; }
        }
        Anclas.Guardar(p.FilePath, anclas);
        Estado("\u2714 " + n + " regiones y marcadores creados y anclados a sus clips (Ctrl+Z los quita). " +
               "Si mueves clips, ejecuta ReubicarMarcadores.", false);
    }

    static Ancla Marcador(Project p, double t, string texto)
    {
        p.Markers.Add(new Marker(Timecode.FromMilliseconds(t * 1000), texto));
        return Anclas.Crear(p, t, -1, texto);
    }

    static Ancla Regiones(Project p, double a, double b, string texto)
    {
        p.Regions.Add(new ScriptPortal.Vegas.Region(Timecode.FromMilliseconds(a * 1000), Timecode.FromMilliseconds((b - a) * 1000), texto));
        return Anclas.Crear(p, a, b, texto);
    }

    void AplicarCorte()
    {
        double fps = vegas.Project.Video.FrameRate;
        List<Rango> quitar = Editor.AjustarAFotogramas(resultado.Quitar(total), fps);
        List<Acelerado> acelerar = new List<Acelerado>();
        foreach (Acelerado a in resultado.Acelerados(quitar))
        {
            Acelerado f = new Acelerado(Math.Round(a.Inicio * fps) / fps, Math.Round(a.Fin * fps) / fps, a.Factor);
            if (f.Fin - f.Inicio >= 2 / fps) acelerar.Add(f);
        }
        double quitado = 0, ahorro = 0;
        foreach (Rango r in quitar) quitado += r.Fin - r.Inicio;
        foreach (Acelerado a in acelerar) ahorro += a.Ahorro;
        if (quitar.Count == 0 && acelerar.Count == 0) { Estado("El corte no cambia nada.", true); return; }
        bool silenciar = segAudio.Seleccion == 0;
        if (MessageBox.Show(this,
                "Se quitar\u00e1n " + Formato.Tiempo(quitado) + " en " + quitar.Count + " tramos" +
                (acelerar.Count > 0 ? " y se acelerar\u00e1n " + acelerar.Count + " tramos (" +
                    (silenciar ? "sin audio" : "con audio acelerado") + ")" : "") +
                ". El video quedar\u00e1 de " + Formato.Tiempo(total - quitado - ahorro) + ".\n\n" +
                "Se aplica en todas las pistas para mantener la sincron\u00eda (haz esto antes de poner m\u00fasica). " +
                "Los textos y momentos marcados quedan como marcadores anclados a sus clips.\n\n\u00bfAplicar? (Ctrl+Z lo deshace)",
                "Aplicar corte", MessageBoxButtons.OKCancel) != DialogResult.OK) return;

        // Que el material sin cortar quede guardado: la BASE nunca se corta.
        string copia = "";
        string veg = vegas.Project.FilePath ?? "";
        try
        {
            if (CopiaBase.EsBase(veg))
            {
                string destino = CopiaBase.RutaCorte(veg);
                DialogResult d = MessageBox.Show(this, "Est\u00e1s en la copia BASE (el material sin cortar).\n\n\u00bfGuardar el corte en una copia nueva \u00ab" +
                    Path.GetFileName(destino) + "\u00bb y dejar la BASE intacta?\n\n(\u00abNo\u00bb corta la BASE igual.)", "Aplicar corte", MessageBoxButtons.YesNoCancel);
                if (d == DialogResult.Cancel) return;
                if (d == DialogResult.Yes) { CopiaBase.GuardarComo(vegas, destino); copia = " Trabajas ahora en \u00ab" + Path.GetFileName(destino) + "\u00bb; la BASE qued\u00f3 sin cortar."; }
            }
            else if (veg.Length > 0 && !Path.GetFileNameWithoutExtension(veg).EndsWith(" CAP") && !CopiaBase.EsCorte(veg) && !File.Exists(CopiaBase.RutaPara(veg)))
            {
                string b = CopiaBase.Guardar(vegas);
                if (b != null) copia = " Antes de cortar guard\u00e9 \u00ab" + Path.GetFileName(b) + "\u00bb (el material sin cortar).";
            }
        }
        catch (Exception ex) { copia = " (No pude guardar la copia sin cortar: " + ex.Message + ")"; }

        Project p = vegas.Project;
        List<Track> todas = new List<Track>();
        foreach (Track t in p.Tracks) todas.Add(t);
        List<Ancla> anclas = new List<Ancla>();
        double trasCortar;
        using (UndoBlock deshacer = new UndoBlock("Momentos con IA: corte"))
        {
            if (quitar.Count > 0) Editor.Eliminar(p, todas, quitar, true, true, 0.02);
            trasCortar = p.Length.ToMilliseconds() / 1000.0;
            if (acelerar.Count > 0) Editor.Acelerar(p, todas, acelerar, silenciar, true, 0.02);
            foreach (TextoResumen t in resultado.Textos)
                if (t.Elegido) anclas.Add(Marcador(p, Acelerado.Posicion(Editor.PosicionTrasQuitar(t.Posicion, quitar), acelerar), "TEXTO: " + t.Texto));
            foreach (Tramo t in resultado.Momentos)
            {
                if (!t.Elegido || Dentro(t.Inicio, quitar)) continue;
                double nuevo = Acelerado.Posicion(Editor.PosicionTrasQuitar(t.Inicio, quitar), acelerar);
                anclas.Add(Marcador(p, nuevo, "\u2605" + t.Puntuacion.ToString("0") + " " + t.Titulo));
            }
        }
        Anclas.Guardar(p.FilePath, anclas);
        double despues = p.Length.ToMilliseconds() / 1000.0;
        string aviso = "";
        if (quitar.Count > 0) aviso = Transcripcion.RegistrarCortes(p.FilePath, quitar, total, trasCortar);
        if (acelerar.Count > 0) aviso = Transcripcion.RegistrarAceleracion(p.FilePath, acelerar, trasCortar, despues);
        aplicado = true;
        btnCortar.Enabled = false;
        btnMarcar.Enabled = false;
        Estado("\u2714 Corte aplicado: el video dura ahora " + Formato.Tiempo(despues) + "." + aviso.Replace("\n", " ") +
               " Si lo deshaces (Ctrl+Z), al volver a abrir esta ventana la respuesta aparece lista otra vez." + copia, false);
    }

    static bool Dentro(double t, List<Rango> rangos)
    {
        foreach (Rango r in rangos) if (t > r.Inicio && t < r.Fin) return true;
        return false;
    }
}

// ---- src/momentos/LogicaMomentos.cs ----

// =====================================================================
// Momentos con IA: que se le pide a Gemini y como se lee la respuesta.
// Todos los tiempos estan en la linea de tiempo ACTUAL (despues de los
// cortes que ya se hayan hecho).
//
// Videos cortos: una sola peticion. Videos largos (mas de ~35 min): por
// partes de ~20 min (cada parte elige candidatos y resume lo que pasa) y una
// pasada final que arma el corte completo cuidando la historia.
// =====================================================================

public class Tramo
{
    public double Inicio, Fin, Puntuacion;
    public string Titulo = "", Motivo = "";
    public bool Elegido = true;
    public string Nota = "";       // por que se marco o desmarco solo (revision, duracion)
    public bool PorRevision;       // desmarcado por incumplir reglas: no se vuelve a marcar solo
    public bool Acelerar;          // en el corte: se conserva pero mas rapido
    public double Velocidad = 1;   // 2 = el doble de rapido
    public bool Fijo;              // lo elegiste tu: ni la IA ni los ajustes lo quitan

    public double Duracion { get { return Fin - Inicio; } }
    public object MemberwiseCopia() { return MemberwiseClone(); }
    // Lo que dura en el video final.
    public double DuracionFinal { get { return Acelerar ? Duracion / Velocidad : Duracion; } }
}

public class TextoResumen
{
    public double Posicion;
    public string Texto = "", Motivo = "";
    public bool Elegido = true;
}

public class OpcionesIA
{
    public string Tipo = "Gameplay";
    public double MinutosMin = 11, MinutosMax = 15;
    public double MinutosObjetivo { get { return (MinutosMin + MinutosMax) / 2; } }
    public string ReglasCanal = PeticionIA.ReglasPorDefecto;
    public string Contexto = "";   // resumenes de episodios anteriores (opcional)
    public string Instrucciones = "";
    public bool PermitirAcelerar = true;   // transiciones aceleradas en vez de cortadas
    public bool SilenciarAcelerado = true; // audio mudo en lo acelerado
    public List<Tramo> Fijos = new List<Tramo>(); // tramos que el editor ya eligio
    public bool FijosCuentan = true;              // si cuentan para la duracion minima y maxima
}

public class ResultadoIA
{
    public string Resumen = "";
    public List<Tramo> Secciones = new List<Tramo>();
    public List<Tramo> Momentos = new List<Tramo>();
    public List<Tramo> Corte = new List<Tramo>();
    public List<TextoResumen> Textos = new List<TextoResumen>();
    public List<Tramo> Shorts = new List<Tramo>();
    public List<string> Titulos = new List<string>();
    // Tramos que propusieron las partes (videos largos): sirven para completar
    // el corte si queda corto.
    public List<Tramo> Candidatos = new List<Tramo>();

    public bool FijosCuentan = true;

    public double DuracionFijos
    {
        get
        {
            double d = 0;
            foreach (Tramo t in Corte) if (t.Elegido && t.Fijo) d += t.DuracionFinal;
            return d;
        }
    }

    // Lo que se compara con el minimo y el maximo.
    public double DuracionAjustable { get { return FijosCuentan ? DuracionCorte : DuracionCorte - DuracionFijos; } }

    public double DuracionCorte
    {
        get
        {
            double d = 0;
            foreach (Tramo t in Corte) if (t.Elegido) d += t.DuracionFinal;
            return d;
        }
    }

    public static double LimitarVelocidad(double v)
    {
        if (double.IsNaN(v) || v < 1.5) return 2;
        return Math.Min(4, Math.Round(v));
    }

    static List<Tramo> Tramos(object o, string clave, double total)
    {
        List<Tramo> r = new List<Tramo>();
        foreach (object x in Json.Lista(o, clave))
        {
            Tramo t = new Tramo();
            t.Inicio = Math.Max(0, Json.Numero(x, "inicio", 0));
            t.Fin = Math.Min(total, Json.Numero(x, "fin", 0));
            t.Puntuacion = Json.Numero(x, "puntuacion", Json.Numero(x, "importancia", 0));
            t.Titulo = Json.Texto(x, "titulo");
            t.Motivo = Json.Texto(x, "motivo");
            if (t.Motivo.Length == 0) t.Motivo = Json.Texto(x, "descripcion");
            if (t.Motivo.Length == 0) t.Motivo = Json.Texto(x, "gancho");
            t.Acelerar = Json.Texto(x, "accion").ToLowerInvariant().StartsWith("aceler");
            t.Velocidad = t.Acelerar ? LimitarVelocidad(Json.Numero(x, "velocidad", 3)) : 1;
            if (t.Fin - t.Inicio >= 0.2) r.Add(t);
        }
        r.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
        return r;
    }

    // Lee la respuesta (JSON). "total" es la duracion actual del proyecto.
    public static ResultadoIA Leer(string json, double total)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        ResultadoIA r = new ResultadoIA();
        r.Resumen = Json.Texto(o, "resumen");
        r.Secciones = Tramos(o, "secciones", total);
        r.Momentos = Tramos(o, "momentos", total);
        r.Momentos.Sort(delegate (Tramo a, Tramo b) { return b.Puntuacion.CompareTo(a.Puntuacion); });
        r.Shorts = Tramos(o, "shorts", total);
        r.Corte = UnirSolapados(Tramos(o, "corte", total));
        r.Candidatos = Tramos(o, "candidatos", total);
        foreach (object x in Json.Lista(o, "textos"))
        {
            TextoResumen t = new TextoResumen();
            t.Posicion = Math.Max(0, Math.Min(total, Json.Numero(x, "posicion", 0)));
            t.Texto = Json.Texto(x, "texto");
            t.Motivo = Json.Texto(x, "motivo");
            if (t.Texto.Length > 0) r.Textos.Add(t);
        }
        r.Textos.Sort(delegate (TextoResumen a, TextoResumen b) { return a.Posicion.CompareTo(b.Posicion); });
        foreach (object x in Json.Lista(o, "titulos"))
            if (x is string && ((string)x).Length > 0) r.Titulos.Add((string)x);
        return r;
    }

    // Une tramos que se tocan con la misma accion; si se enciman con distinta
    // accion, el segundo empieza donde termina el primero.
    static List<Tramo> UnirSolapados(List<Tramo> l)
    {
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in l)
        {
            Tramo u = r.Count > 0 ? r[r.Count - 1] : null;
            if (u != null && t.Inicio <= u.Fin + 0.05)
            {
                if (u.Acelerar == t.Acelerar && u.Velocidad == t.Velocidad)
                {
                    u.Fin = Math.Max(u.Fin, t.Fin);
                    if (t.Titulo.Length > 0 && u.Titulo.IndexOf(t.Titulo) < 0) u.Titulo += " / " + t.Titulo;
                    continue;
                }
                t.Inicio = u.Fin;
                if (t.Fin - t.Inicio < 0.2) continue;
            }
            r.Add(t);
        }
        return r;
    }

    // Aplica la revision: {"tramos":[{"indice":n,"quitar":bool,"inicio":s,"fin":s,"motivo":"..."}]}.
    // Devuelve cuantos tramos cambio.
    public int AplicarRevision(string json)
    {
        int cambios = 0;
        List<Tramo> nuevos = new List<Tramo>();
        object o = Json.Leer(Gemini.QuitarCercas(json));
        foreach (object x in Json.Lista(o, "tramos"))
        {
            int i = (int)Json.Numero(x, "indice", -1);
            if (i < 0 || i >= Corte.Count) continue;
            Tramo t = Corte[i];
            if (t.Fijo) continue;
            string motivo = Json.Texto(x, "motivo");
            object quitar;
            Dictionary<string, object> d = x as Dictionary<string, object>;
            if (d != null && d.TryGetValue("quitar", out quitar) && quitar is bool && (bool)quitar)
            {
                // Si dice que parte quitar y es solo un pedazo del tramo, se
                // quita ese pedazo y el resto se queda.
                double qa = Json.Numero(x, "inicio", -1), qb = Json.Numero(x, "fin", -1);
                if (qb - qa >= 0.5)
                {
                    qa = Math.Max(qa, t.Inicio); qb = Math.Min(qb, t.Fin);
                    if (qb - qa < 0.5) continue; // no toca lo que queda del tramo
                    bool alInicio = qa <= t.Inicio + 0.5, alFinal = qb >= t.Fin - 0.5;
                    if (!(alInicio && alFinal))
                    {
                        string nota = "Recortado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                        if (alInicio) t.Inicio = qb;
                        else if (alFinal) t.Fin = qa;
                        else
                        {
                            Tramo resto = Pedazo(t, qb, t.Fin);
                            resto.Nota = nota;
                            nuevos.Add(resto);
                            t.Fin = qa;
                        }
                        t.Nota = nota;
                        cambios++;
                        continue;
                    }
                }
                t.Elegido = false;
                t.PorRevision = true;
                t.Nota = "Quitado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                cambios++;
                continue;
            }
            // Recorte: conservar solo una parte del tramo.
            double a = Json.Numero(x, "inicio", t.Inicio), b = Json.Numero(x, "fin", t.Fin);
            if ((a > t.Inicio + 0.5 || b < t.Fin - 0.5) && a >= t.Inicio - 0.01 && b <= t.Fin + 0.01 && b - a >= 2)
            {
                t.Inicio = a; t.Fin = b;
                t.Nota = "Recortado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                cambios++;
            }
        }
        if (nuevos.Count > 0)
        {
            Corte.AddRange(nuevos);
            Corte.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
        }
        return cambios;
    }

    // Agrega un tramo elegido a mano. Lo que la IA tenia adentro se absorbe;
    // lo que sobresale se conserva recortado.
    public void AgregarFijo(double a, double b, string titulo)
    {
        if (b - a < 0.5) return;
        Tramo n = new Tramo();
        n.Inicio = a; n.Fin = b; n.Puntuacion = 10; n.Fijo = true;
        n.Titulo = titulo;
        n.Motivo = "Lo elegiste t\u00fa: se conserva completo.";
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in Corte)
        {
            if (t.Fin <= a + 0.05 || t.Inicio >= b - 0.05) { r.Add(t); continue; }
            if (t.Fijo) { n.Inicio = Math.Min(n.Inicio, t.Inicio); n.Fin = Math.Max(n.Fin, t.Fin); continue; }
            if (t.Inicio < a - 0.5) r.Add(Pedazo(t, t.Inicio, a));
            if (t.Fin > b + 0.5) r.Add(Pedazo(t, b, t.Fin));
        }
        r.Add(n);
        r.Sort(delegate (Tramo x, Tramo y) { return x.Inicio.CompareTo(y.Inicio); });
        Corte = r;
    }

    static Tramo Pedazo(Tramo t, double a, double b)
    {
        Tramo p = (Tramo)t.MemberwiseCopia();
        p.Inicio = a; p.Fin = b;
        return p;
    }

    bool SeEncima(Tramo c)
    {
        foreach (Tramo t in Corte)
            if (t != c && t.Elegido && c.Inicio < t.Fin - 0.05 && c.Fin > t.Inicio + 0.05) return true;
        return false;
    }

    // Deja el corte entre el minimo y el maximo (segundos). Si sobra, desmarca
    // los tramos de menor importancia (nunca el primero ni el ultimo); si falta,
    // vuelve a marcar tramos desmarcados por duracion o agrega candidatos.
    // Devuelve un resumen de lo que hizo ("" si no hizo nada).
    public string AjustarDuracion(double minimo, double maximo)
    {
        int quitados = 0, agregados = 0;
        while (DuracionAjustable > maximo + 0.5)
        {
            List<Tramo> elegidos = Corte.FindAll(delegate (Tramo t) { return t.Elegido; });
            Tramo peor = null;
            for (int i = 1; i < elegidos.Count - 1; i++)
            {
                Tramo t = elegidos[i];
                if (t.Fijo) continue;
                if (peor == null || t.Puntuacion < peor.Puntuacion ||
                    (t.Puntuacion == peor.Puntuacion && t.DuracionFinal > peor.DuracionFinal)) peor = t;
            }
            if (peor == null) break;
            peor.Elegido = false;
            peor.Nota = "Desmarcado para no pasar del m\u00e1ximo (importancia " + peor.Puntuacion.ToString("0") + ")";
            quitados++;
        }
        while (DuracionAjustable < minimo - 0.5)
        {
            Tramo mejor = null;
            bool nuevo = false;
            foreach (Tramo t in Corte)
                if (!t.Elegido && !t.PorRevision && !SeEncima(t) && DuracionAjustable + t.DuracionFinal <= maximo + 0.5 &&
                    (mejor == null || t.Puntuacion > mejor.Puntuacion)) mejor = t;
            if (mejor == null)
                foreach (Tramo c in Candidatos)
                    if (!Corte.Contains(c) && !SeEncima(c) && DuracionAjustable + c.DuracionFinal <= maximo + 0.5 &&
                        (mejor == null || c.Puntuacion > mejor.Puntuacion)) { mejor = c; nuevo = true; }
            if (mejor == null) break;
            mejor.Elegido = true;
            mejor.Nota = "Agregado para llegar al m\u00ednimo";
            if (nuevo)
            {
                Corte.Add(mejor);
                Corte.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
            }
            agregados++;
        }
        List<string> partes = new List<string>();
        if (quitados > 0) partes.Add(quitados + (quitados == 1 ? " tramo desmarcado" : " tramos desmarcados") + " por pasar del m\u00e1ximo");
        if (agregados > 0) partes.Add(agregados + (agregados == 1 ? " tramo agregado" : " tramos agregados") + " para llegar al m\u00ednimo");
        return String.Join("; ", partes.ToArray());
    }

    // Lleva los bordes del corte al inicio/fin de la palabra que cortarian,
    // para no partir palabras a la mitad.
    public void AjustarAPalabras(List<Segmento> segmentos)
    {
        List<Palabra> palabras = new List<Palabra>();
        foreach (Segmento s in segmentos) palabras.AddRange(s.Palabras);
        foreach (Tramo t in Corte)
        {
            foreach (Palabra p in palabras)
            {
                if (t.Inicio > p.Inicio && t.Inicio < p.Fin) t.Inicio = p.Inicio;
                if (t.Fin > p.Inicio && t.Fin < p.Fin) t.Fin = p.Fin;
            }
        }
        Corte = UnirSolapados(Corte);
    }

    // Lo que se quita para quedarse solo con los tramos elegidos del corte.
    public List<Rango> Quitar(double total)
    {
        List<Rango> r = new List<Rango>();
        double cursor = 0;
        foreach (Tramo t in Corte)
        {
            if (!t.Elegido) continue;
            if (t.Inicio - cursor > 0.01) r.Add(new Rango(cursor, t.Inicio));
            cursor = Math.Max(cursor, t.Fin);
        }
        if (total - cursor > 0.01) r.Add(new Rango(cursor, total));
        return r;
    }

    // Instante despues de quitar los rangos (si cae adentro, queda en el corte).
    public static double TrasQuitar(double t, List<Rango> quitados)
    {
        double q = 0;
        foreach (Rango r in quitados)
        {
            if (t >= r.Fin) q += r.Fin - r.Inicio;
            else if (t > r.Inicio) q += t - r.Inicio;
        }
        return t - q;
    }

    // Tramos a acelerar, ya en la linea de tiempo que queda despues de quitar.
    public List<Acelerado> Acelerados(List<Rango> quitados)
    {
        List<Acelerado> r = new List<Acelerado>();
        foreach (Tramo t in Corte)
            if (t.Elegido && t.Acelerar && t.Velocidad > 1)
                r.Add(new Acelerado(TrasQuitar(t.Inicio, quitados), TrasQuitar(t.Fin, quitados), t.Velocidad));
        return r;
    }

    // ------------------------------------------------------------ informe

    public string Informe(string proyecto, OpcionesIA op, double total)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("# " + Path.GetFileNameWithoutExtension(proyecto) + "\n\n");
        sb.Append("Generado con vegas-cut y Gemini el " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                  ". Tipo: " + op.Tipo + ". Objetivo: " + op.MinutosMin + "\u2013" + op.MinutosMax + " min. Duraci\u00f3n original: " +
                  Formato.Tiempo(total) + ".\n\n");
        sb.Append("## Resumen\n\n" + Resumen + "\n\n");
        if (Secciones.Count > 0)
        {
            sb.Append("## Secciones\n\n");
            foreach (Tramo t in Secciones)
                sb.Append("- **" + Formato.Tiempo(t.Inicio) + "\u2013" + Formato.Tiempo(t.Fin) + " " + t.Titulo + "**: " + t.Motivo + "\n");
            sb.Append("\n");
        }
        sb.Append("## Corte sugerido (" + Formato.Tiempo(DuracionCorte) + ")\n\n");
        foreach (Tramo t in Corte)
            sb.Append("- [" + (t.Elegido ? "x" : " ") + "] " + Formato.Tiempo(t.Inicio) + "\u2013" + Formato.Tiempo(t.Fin) +
                      " (" + Formato.Tiempo(t.Duracion) + (t.Acelerar ? ", acelerado \u00d7" + t.Velocidad + " \u2192 " + Formato.Tiempo(t.DuracionFinal) : "") +
                      ") **" + t.Titulo + "**: " + t.Motivo + (t.Nota.Length > 0 ? " _(" + t.Nota + ")_" : "") + "\n");
        sb.Append("\n## Momentos destacados\n\n");
        foreach (Tramo t in Momentos)
            sb.Append("- " + t.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + "/10 \u00b7 " + Formato.Tiempo(t.Inicio) + "\u2013" +
                      Formato.Tiempo(t.Fin) + " **" + t.Titulo + "**: " + t.Motivo + "\n");
        if (Textos.Count > 0)
        {
            sb.Append("\n## Textos de resumen\n\n");
            foreach (TextoResumen t in Textos)
                sb.Append("- " + Formato.Tiempo(t.Posicion) + ": \u201c" + t.Texto + "\u201d" + (t.Motivo.Length > 0 ? " (" + t.Motivo + ")" : "") + "\n");
        }
        if (Shorts.Count > 0)
        {
            sb.Append("\n## Ideas para Shorts\n\n");
            foreach (Tramo t in Shorts)
                sb.Append("- " + Formato.Tiempo(t.Inicio) + "\u2013" + Formato.Tiempo(t.Fin) + " **" + t.Titulo + "**: " + t.Motivo + "\n");
        }
        if (Titulos.Count > 0)
        {
            sb.Append("\n## T\u00edtulos sugeridos\n\n");
            foreach (string t in Titulos) sb.Append("- " + t + "\n");
        }
        return sb.ToString();
    }
}

// =====================================================================
// Textos que se envian a Gemini
// =====================================================================

// Tramos fijos del proyecto (<proyecto>.vegascut-fijos.json), en tiempos de
// la linea de tiempo de cuando se eligieron. Solo valen mientras el proyecto
// dure lo mismo (antes de aplicar el corte).
public static class TramosFijos
{
    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-fijos.json");
    }

    public static bool Cuentan(string veg)
    {
        string ruta = RutaPara(veg);
        try
        {
            if (ruta != null && File.Exists(ruta))
                return Json.Texto(Json.Leer(File.ReadAllText(ruta, Encoding.UTF8)), "cuentan") != "False";
        }
        catch { }
        return true;
    }

    public static List<Tramo> Cargar(string veg, double duracion)
    {
        List<Tramo> r = new List<Tramo>();
        string ruta = RutaPara(veg);
        try
        {
            if (ruta == null || !File.Exists(ruta)) return r;
            object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
            if (Math.Abs(Json.Numero(o, "duracionProyecto", -1) - duracion) > 0.5) return r;
            foreach (object x in Json.Lista(o, "fijos"))
            {
                Tramo t = new Tramo();
                t.Inicio = Json.Numero(x, "inicio", 0); t.Fin = Json.Numero(x, "fin", 0);
                t.Titulo = Json.Texto(x, "titulo"); t.Fijo = true; t.Puntuacion = 10;
                if (t.Fin > t.Inicio) r.Add(t);
            }
        }
        catch { }
        return r;
    }

    public static void Guardar(string veg, double duracion, List<Tramo> fijos)
    {
        Guardar(veg, duracion, fijos, true);
    }

    public static void Guardar(string veg, double duracion, List<Tramo> fijos, bool cuentan)
    {
        string ruta = RutaPara(veg);
        if (ruta == null) return;
        List<object> l = new List<object>();
        foreach (Tramo t in fijos)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["inicio"] = Math.Round(t.Inicio, 3); d["fin"] = Math.Round(t.Fin, 3); d["titulo"] = t.Titulo;
            l.Add(d);
        }
        Dictionary<string, object> raiz = new Dictionary<string, object>();
        raiz["duracionProyecto"] = duracion;
        raiz["fijos"] = l;
        raiz["cuentan"] = cuentan;
        try { File.WriteAllText(ruta, Json.Escribir(raiz), new UTF8Encoding(false)); } catch { }
    }

    // Agrega un tramo a la lista, uniendo los que se enciman.
    public static List<Tramo> Agregar(List<Tramo> fijos, double a, double b, string titulo)
    {
        Tramo n = new Tramo();
        n.Inicio = a; n.Fin = b; n.Titulo = titulo; n.Fijo = true; n.Puntuacion = 10;
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in fijos)
        {
            if (t.Fin < a - 0.05 || t.Inicio > b + 0.05) { r.Add(t); continue; }
            n.Inicio = Math.Min(n.Inicio, t.Inicio); n.Fin = Math.Max(n.Fin, t.Fin);
        }
        r.Add(n);
        r.Sort(delegate (Tramo x, Tramo y) { return x.Inicio.CompareTo(y.Inicio); });
        return r;
    }
}

public static class PeticionIA
{
    public static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    // Reglas editoriales que aplican siempre (se pueden editar en la ventana;
    // se guardan para todos los proyectos).
    public const string ReglasPorDefecto =
        "- Empieza directo en la acci\u00f3n o en un gancho: nada de saludos largos, \u201c\u00bfme escuchan?\u201d, cargas de mundo, problemas t\u00e9cnicos ni preparaci\u00f3n.\r\n" +
        "- Excluye conversaciones personales o privadas aunque sean graciosas: vida amorosa, parejas, ex, familia, salud, dinero, escuela o trabajo, y cualquier dato personal.\r\n" +
        "- Excluye charla que no tenga que ver con el juego ni con la historia, problemas t\u00e9cnicos (lag, micr\u00f3fono, Discord, OBS), silencios, AFK y grindeo repetitivo.\r\n" +
        "- Mant\u00e9n el hilo: antes de cambiar de lugar o de actividad, conserva de 1 a 3 frases que digan a d\u00f3nde van o qu\u00e9 van a hacer. Si no existen, prop\u00f3n un texto de resumen.\r\n" +
        "- No cortes a mitad de una idea, chiste o reacci\u00f3n: incluye el remate.\r\n" +
        "- Termina con el cl\u00edmax o con un cierre o suspenso claro.";

    const string Rol =
        "Eres un editor de video experto en contenido de YouTube en espa\u00f1ol (gameplays con amigos, " +
        "narraciones, video ensayos). Recibes la transcripci\u00f3n de un video con tiempos en segundos de la " +
        "l\u00ednea de tiempo y una tabla de intensidad de sonido. Tu trabajo es ayudar a editarlo.\n\n";

    const string ReglasCorte =
        "- \"corte\": tramos a CONSERVAR, en orden, sin solaparse. Deben contar la historia completa sin omitir " +
        "partes importantes (objetivos, decisiones, resultados, momentos graciosos o intensos). Empieza y termina " +
        "cada tramo en l\u00edmites de frase, nunca a mitad de una palabra. Prefiere tramos de 10 s a 3 min.\n" +
        "- Cada tramo del corte lleva \"importancia\" de 1 a 10 (10 = imprescindible para la historia; 1 = relleno). " +
        "Se usa para ajustar la duraci\u00f3n quitando primero lo menos importante.\n" +
        "- Las REGLAS DEL CANAL y las INDICACIONES DEL EPISODIO son obligatorias: un tramo que las incumple no va " +
        "en el corte aunque sea gracioso o intenso.\n" +
        "- Poca conversaci\u00f3n no significa que no pase nada: en carreras, peleas, persecuciones, exploraci\u00f3n o " +
        "construcci\u00f3n puede haber acci\u00f3n con poca voz. F\u00edjate en la intensidad de ambiente y en las indicaciones; " +
        "si piden mostrar una actividad completa, cons\u00e9rvala completa aunque hablen poco.\n" +
        "- Los TRAMOS FIJOS ya los eligi\u00f3 el editor: van completos en el corte (incl\u00fayelos tal cual). Si dicen " +
        "que cuentan para la duraci\u00f3n, el resto tiene que caber en lo que queda.\n";

    const string ReglasAcelerar =
        "- Cada tramo del corte lleva \"accion\": \"conservar\" (velocidad normal) o \"acelerar\" (se ve m\u00e1s r\u00e1pido, " +
        "sin audio): \u00fasalo para transiciones con poca conversaci\u00f3n que ayudan a entender el progreso (viajar, " +
        "minar, construir, preparar). \"velocidad\" de 2 a 4. Un tramo acelerado cuenta para la duraci\u00f3n como " +
        "duraci\u00f3n/velocidad. No aceleres tramos con di\u00e1logo importante.\n";

    const string SinAcelerar = "- Todos los tramos del corte llevan \"accion\": \"conservar\" (no se acelera nada).\n";

    const string EsquemaFinal =
        "Responde SOLO con un objeto JSON con exactamente estas claves:\n" +
        "{\n" +
        "  \"resumen\": \"qu\u00e9 pasa en el video, en orden, en 1 a 3 p\u00e1rrafos\",\n" +
        "  \"secciones\": [{\"inicio\": s, \"fin\": s, \"titulo\": \"...\", \"descripcion\": \"qu\u00e9 pasa\"}],\n" +
        "  \"momentos\": [{\"inicio\": s, \"fin\": s, \"puntuacion\": 1-10, \"titulo\": \"...\", \"motivo\": \"por qu\u00e9 es bueno\"}],\n" +
        "  \"corte\": [{\"inicio\": s, \"fin\": s, \"importancia\": 1-10, \"accion\": \"conservar\" o \"acelerar\", \"velocidad\": 1-4, \"titulo\": \"...\", \"motivo\": \"por qu\u00e9 se conserva\"}],\n" +
        "  \"textos\": [{\"posicion\": s, \"texto\": \"texto corto en pantalla\", \"motivo\": \"qu\u00e9 se salta\"}],\n" +
        "  \"shorts\": [{\"inicio\": s, \"fin\": s, \"titulo\": \"...\", \"gancho\": \"por qu\u00e9 funciona solo\"}],\n" +
        "  \"titulos\": [\"t\u00edtulo para el video\", \"...\"]\n" +
        "}\n\n";

    const string ReglasResto =
        "- \"textos\": frases muy cortas tipo \"Construimos la base\" o \"3 horas despu\u00e9s\u2026\" para explicar lo que " +
        "el corte se salta; \"posicion\" es el inicio del tramo conservado donde conviene mostrarlo. Solo donde " +
        "realmente ayude a no perderse.\n" +
        "- \"momentos\": los mejores 5 a 15 (risas, gritos, sorpresas, frases memorables, acci\u00f3n intensa). Usa la " +
        "intensidad: valores altos de voz suelen ser gritos o risas; de ambiente, explosiones o peleas.\n" +
        "- \"shorts\": 2 a 5 tramos de 15 a 60 s que se entiendan sin contexto.\n" +
        "- \"titulos\": 3 a 5 opciones atractivas.\n" +
        "- Todos los tiempos son segundos (n\u00famero) de la l\u00ednea de tiempo dada; no inventes tiempos fuera del video.\n" +
        "- Escribe todo en espa\u00f1ol natural. Usa los nombres de las personas.";

    // ---------------------------------------------------- una peticion

    public static string Instrucciones(OpcionesIA op)
    {
        return Rol + EsquemaFinal + "Reglas:\n" + ReglasCorte +
               "- El corte completo debe durar entre la duraci\u00f3n m\u00ednima y la m\u00e1xima (apunta a la ideal). Antes de " +
               "responder, suma las duraciones de los tramos (los acelerados cuentan duraci\u00f3n/velocidad) y corrige si te pasas.\n" +
               (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) + ReglasResto;
    }

    static void Cabecera(StringBuilder sb, Transcripcion t, double duracionActual, OpcionesIA op)
    {
        sb.Append("Tipo de video: " + op.Tipo + "\n");
        sb.Append("Duraci\u00f3n actual: " + S(duracionActual) + " s (" + Formato.Tiempo(duracionActual) + ")\n");
        sb.Append("Duraci\u00f3n del corte: m\u00ednimo " + S(op.MinutosMin * 60) + " s, m\u00e1ximo " + S(op.MinutosMax * 60) +
                  " s, ideal " + S(op.MinutosObjetivo * 60) + " s (" + op.MinutosMin + " a " + op.MinutosMax + " min)\n");
        if (!String.IsNullOrEmpty(op.ReglasCanal)) sb.Append("\nREGLAS DEL CANAL (siempre):\n" + ConSegundos(op.ReglasCanal.Trim()) + "\n");
        if (!String.IsNullOrEmpty(op.Instrucciones)) sb.Append("\nINDICACIONES DEL EPISODIO:\n" + ConSegundos(op.Instrucciones.Trim()) + "\n");
        Fijos_(sb, op);
        if (!String.IsNullOrEmpty(op.Contexto))
            sb.Append("\nCONTEXTO DE LA SERIE (otros cap\u00edtulos; solo para entender la historia):\n" + op.Contexto.Trim() + "\n");
        sb.Append("\nPersonas (cada una es una pista de audio):\n");
        foreach (Hablante h in t.Hablantes)
            if (h.Voz) sb.Append("- " + h.Nombre + (h.Nombre != h.Etiqueta ? " (" + h.Etiqueta + ")" : "") + "\n");
    }

    static void Fijos_(StringBuilder sb, OpcionesIA op)
    {
        if (op.Fijos.Count == 0) return;
        double total = 0;
        sb.Append("\nTRAMOS FIJOS (elegidos por el editor; van completos en el corte):\n");
        foreach (Tramo f in op.Fijos)
        {
            sb.Append("- [" + S(f.Inicio) + "-" + S(f.Fin) + "] " + f.Titulo + " (" + Formato.Tiempo(f.Duracion) + ")\n");
            total += f.Duracion;
        }
        if (op.FijosCuentan)
            sb.Append("Suman " + S(total) + " s y CUENTAN para la duraci\u00f3n: el resto del corte debe caber en lo que queda.\n");
        else
            sb.Append("Suman " + S(total) + " s y NO cuentan para la duraci\u00f3n: el m\u00ednimo y el m\u00e1ximo son solo para el resto " +
                      "del corte, aparte de estos tramos.\n");
    }

    // Los tiempos escritos como 57:00 o 1:09:30 se acompa\u00f1an con su valor en
    // segundos, que es como estan los tiempos de la transcripcion.
    public static string ConSegundos(string texto)
    {
        return Regex.Replace(texto ?? "", @"(?<![\d:])(\d{1,2}):(\d{2})(?::(\d{2}))?(?![\d:])", delegate (Match m)
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
            double seg = m.Groups[3].Success ? a * 3600 + b * 60 + int.Parse(m.Groups[3].Value) : a * 60 + b;
            return m.Value + " (= " + S(seg) + " s)";
        });
    }

    static void Transcripcion_(StringBuilder sb, Transcripcion t, List<Segmento> segmentos, double desde, double hasta)
    {
        sb.Append("\nTranscripci\u00f3n [inicio-fin] persona: texto\n");
        foreach (Segmento s in segmentos)
            if (s.Fin > desde && s.Inicio < hasta)
                sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + t.Hablantes[s.Hablante].Nombre + ": " + s.Texto + "\n");
    }

    static void Intensidad_(StringBuilder sb, Transcripcion t, double duracionActual, double desde, double hasta)
    {
        sb.Append("\nIntensidad cada 5 s (0 = silencio, 10 = lo m\u00e1s fuerte de esa pista). Columnas: inicio;voz;ambiente\n");
        foreach (string linea in Intensidad(t, duracionActual, 5))
        {
            double inicio = double.Parse(linea.Substring(0, linea.IndexOf(';')), CultureInfo.InvariantCulture);
            if (inicio + 5 > desde && inicio < hasta) sb.Append(linea + "\n");
        }
    }

    // Mensaje con la transcripcion e intensidad en la linea de tiempo actual.
    public static string Mensaje(Transcripcion t, double duracionActual, OpcionesIA op)
    {
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        Transcripcion_(sb, t, t.SegmentosActuales(), 0, duracionActual);
        Intensidad_(sb, t, duracionActual, 0, duracionActual);
        return sb.ToString();
    }

    // ------------------------------------------------------- por partes

    // Divide el video en partes de ~"tamano" segundos, cortando en la pausa
    // mas larga entre frases cerca de cada limite.
    public static List<Rango> Partes(List<Segmento> segmentos, double total, double tamano)
    {
        List<Rango> r = new List<Rango>();
        int n = Math.Max(1, (int)Math.Round(total / tamano));
        double inicio = 0;
        for (int i = 1; i < n; i++)
        {
            double ideal = total * i / n, mejor = ideal, hueco = -1;
            for (int k = 0; k + 1 < segmentos.Count; k++)
            {
                double a = segmentos[k].Fin, b = segmentos[k + 1].Inicio;
                double medio = (a + b) / 2;
                if (Math.Abs(medio - ideal) > tamano * 0.15 || b - a <= hueco) continue;
                hueco = b - a;
                mejor = medio;
            }
            if (mejor <= inicio + 60) mejor = ideal;
            r.Add(new Rango(inicio, mejor));
            inicio = mejor;
        }
        r.Add(new Rango(inicio, total));
        return r;
    }

    public static string InstruccionesParte(OpcionesIA op)
    {
        return Rol +
            "Este video es largo, as\u00ed que lo recibes POR PARTES. Ahora te toca UNA parte. Elige candidatos para el " +
            "corte final (despu\u00e9s se elegir\u00e1 entre los candidatos de todas las partes) y resume lo que pasa.\n\n" +
            "Responde SOLO con un objeto JSON con exactamente estas claves:\n" +
            "{\n" +
            "  \"resumen\": \"qu\u00e9 pasa en esta parte, en orden, en 2 a 5 frases (con nombres y hechos concretos)\",\n" +
            "  \"candidatos\": [{\"inicio\": s, \"fin\": s, \"importancia\": 1-10, \"accion\": \"conservar\" o \"acelerar\", \"velocidad\": 1-4, \"titulo\": \"...\", \"motivo\": \"...\"}],\n" +
            "  \"momentos\": [{\"inicio\": s, \"fin\": s, \"puntuacion\": 1-10, \"titulo\": \"...\", \"motivo\": \"...\"}]\n" +
            "}\n\n" +
            "Reglas:\n" + ReglasCorte +
            "- Los candidatos de esta parte deben sumar cerca de la duraci\u00f3n sugerida para la parte (es generosa: " +
            "luego se recorta). \"importancia\": 10 = imprescindible para entender la historia o lo m\u00e1s gracioso; " +
            "1 = relleno prescindible.\n" +
            (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) +
            "- \"momentos\": los mejores de esta parte (0 a 6).\n" +
            "- Usa solo tiempos dentro de esta parte. Escribe en espa\u00f1ol natural y usa los nombres de las personas.";
    }

    public static string MensajeParte(Transcripcion t, List<Segmento> segmentos, double duracionActual, OpcionesIA op,
                                      int numero, List<Rango> partes, List<string> resumenesPrevios)
    {
        Rango p = partes[numero];
        double sugerido = op.MinutosObjetivo * 60 * (p.Fin - p.Inicio) / duracionActual * 1.5;
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        sb.Append("\nParte " + (numero + 1) + " de " + partes.Count + ": de " + S(p.Inicio) + " s a " + S(p.Fin) + " s (" +
                  Formato.Tiempo(p.Inicio) + "\u2013" + Formato.Tiempo(p.Fin) + ").\n");
        sb.Append("Duraci\u00f3n sugerida para los candidatos de esta parte: " + S(sugerido) + " s.\n");
        if (resumenesPrevios.Count > 0)
        {
            sb.Append("\nLo que pas\u00f3 en las partes anteriores:\n");
            for (int i = 0; i < resumenesPrevios.Count; i++) sb.Append("Parte " + (i + 1) + ": " + resumenesPrevios[i] + "\n");
        }
        Transcripcion_(sb, t, segmentos, p.Inicio, p.Fin);
        Intensidad_(sb, t, duracionActual, p.Inicio, p.Fin);
        return sb.ToString();
    }

    public static string InstruccionesFinal(OpcionesIA op)
    {
        return Rol +
            "Este video es largo y ya se analiz\u00f3 por partes. Recibes el resumen de cada parte y una lista de " +
            "CANDIDATOS (tramos posibles con su importancia). Arma el video final.\n\n" + EsquemaFinal + "Reglas:\n" +
            "- \"corte\": elige y ordena candidatos para que el video final dure entre la duraci\u00f3n m\u00ednima y la m\u00e1xima " +
            "(apunta a la ideal; suma antes de responder). Usa sus tiempos tal cual o rec\u00f3rtalos por dentro; no inventes tramos fuera de los candidatos. " +
            "Cada tramo lleva \"importancia\" de 1 a 10. Las REGLAS DEL CANAL y las INDICACIONES DEL EPISODIO son obligatorias. " +
            "Que la historia completa se entienda de principio a fin: no te saltes objetivos, decisiones ni " +
            "resultados importantes. Prefiere los de mayor importancia, pero mant\u00e9n el ritmo y la variedad.\n" +
            (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) +
            "- \"secciones\": cubren todo el video original (una por etapa de la historia).\n" + ReglasResto;
    }

    public static string MensajeFinal(Transcripcion t, double duracionActual, OpcionesIA op, List<Rango> partes,
                                      List<string> resumenes, List<Tramo> candidatos, List<Tramo> momentos)
    {
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        sb.Append("\nResumen por partes:\n");
        for (int i = 0; i < partes.Count; i++)
            sb.Append("Parte " + (i + 1) + " (" + S(partes[i].Inicio) + "-" + S(partes[i].Fin) + " s): " +
                      (i < resumenes.Count ? resumenes[i] : "") + "\n");
        double suma = 0;
        foreach (Tramo c in candidatos) suma += c.DuracionFinal;
        sb.Append("\nCandidatos [inicio-fin] importancia acci\u00f3n: t\u00edtulo \u2014 motivo (suman " + S(suma) + " s):\n");
        foreach (Tramo c in candidatos)
            sb.Append("[" + S(c.Inicio) + "-" + S(c.Fin) + "] " + c.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + " " +
                      (c.Acelerar ? "acelerar\u00d7" + c.Velocidad.ToString("0", CultureInfo.InvariantCulture) : "conservar") + ": " +
                      c.Titulo + " \u2014 " + c.Motivo + "\n");
        sb.Append("\nMomentos destacados encontrados [inicio-fin] puntuaci\u00f3n: t\u00edtulo \u2014 motivo:\n");
        foreach (Tramo m in momentos)
            sb.Append("[" + S(m.Inicio) + "-" + S(m.Fin) + "] " + m.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + ": " +
                      m.Titulo + " \u2014 " + m.Motivo + "\n");
        return sb.ToString();
    }

    // Contexto de episodios anteriores: el resumen y las secciones de sus
    // respuestas de MomentosIA (opcional; solo para entender la historia).
    public static string ContextoDe(List<string> rutas)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string ruta in rutas)
        {
            try
            {
                object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
                object r = Json.Leer(Gemini.QuitarCercas(Json.Texto(o, "respuesta")));
                string nombre = Path.GetFileName(ruta).Replace(".vegascut-ia.json", "");
                StringBuilder ep = new StringBuilder();
                ep.Append("- " + nombre + ": " + Json.Texto(r, "resumen").Replace("\n", " ") + "\n");
                foreach (object s in Json.Lista(r, "secciones"))
                    ep.Append("  \u00b7 " + Json.Texto(s, "titulo") + ": " + Json.Texto(s, "descripcion") + "\n");
                string texto = ep.ToString();
                if (texto.Length > 3000) texto = texto.Substring(0, 3000) + "\u2026\n";
                sb.Append(texto);
            }
            catch { }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------- revision

    public static string InstruccionesRevision()
    {
        return "Eres un revisor estricto de cortes de video. Recibes las REGLAS DEL CANAL, las INDICACIONES DEL " +
            "EPISODIO y la lista numerada de tramos que se van a conservar, con lo que se dice en cada uno.\n\n" +
            "Revisa cada tramo contra las reglas y las indicaciones:\n" +
            "- Si la mayor parte del tramo las incumple (por ejemplo, una conversaci\u00f3n personal o de vida amorosa), " +
            "m\u00e1rcalo con \"quitar\": true.\n" +
            "- Si solo una parte las incumple, deja \"quitar\": false y da \"inicio\" y \"fin\" (segundos) de la parte que " +
            "S\u00cd se puede conservar, dentro del tramo y en l\u00edmites de frase.\n" +
            "- Si cumple, no lo incluyas en la respuesta.\n\n" +
            "Responde SOLO con JSON: {\"tramos\": [{\"indice\": n, \"quitar\": true o false, \"inicio\": s, \"fin\": s, " +
            "\"motivo\": \"qu\u00e9 regla incumple\"}]}. Si todo cumple: {\"tramos\": []}.";
    }

    public static string MensajeRevision(Transcripcion t, ResultadoIA r, OpcionesIA op)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("REGLAS DEL CANAL:\n" + (op.ReglasCanal ?? "").Trim() + "\n");
        if (!String.IsNullOrEmpty(op.Instrucciones)) sb.Append("\nINDICACIONES DEL EPISODIO:\n" + op.Instrucciones.Trim() + "\n");
        sb.Append("\nTramos del corte:\n");
        List<Segmento> segmentos = t.SegmentosActuales();
        for (int i = 0; i < r.Corte.Count; i++)
        {
            Tramo c = r.Corte[i];
            if (!c.Elegido) continue;
            sb.Append("\n#" + i + " [" + S(c.Inicio) + "-" + S(c.Fin) + "] " + c.Titulo + "\n");
            StringBuilder texto = new StringBuilder();
            foreach (Segmento s in segmentos)
                if (s.Fin > c.Inicio && s.Inicio < c.Fin)
                    texto.Append("[" + S(s.Inicio) + "] " + t.Hablantes[s.Hablante].Nombre + ": " + s.Texto + "\n");
            string tx = texto.ToString();
            if (tx.Length > 2500) tx = tx.Substring(0, 2500) + "\u2026\n";
            sb.Append(tx);
        }
        return sb.ToString();
    }

    // Pico de cada bloque de "bloque" segundos, normalizado por pista, en la
    // linea de tiempo actual. Solo se listan los bloques con algo de sonido.
    public static List<string> Intensidad(Transcripcion t, double duracionActual, int bloque)
    {
        int n = (int)Math.Ceiling(duracionActual / bloque) + 1;
        double[] voz = new double[n], amb = new double[n];
        for (int ih = 0; ih < t.Hablantes.Count; ih++)
        {
            Hablante h = t.Hablantes[ih];
            if (h.Pico == null || h.Pico.Length == 0) continue;
            float[] orden = (float[])h.Pico.Clone();
            Array.Sort(orden);
            double bajo = orden[(int)(orden.Length * 0.10)], alto = orden[Math.Min(orden.Length - 1, (int)(orden.Length * 0.995))];
            if (alto - bajo < 3) continue;
            for (int s = 0; s < h.Pico.Length; s++)
            {
                double ahora = t.Mapear(ih, t.Inicio + s);
                if (double.IsNaN(ahora)) continue;
                int b = (int)(ahora / bloque);
                if (b < 0 || b >= n) continue;
                double v = Math.Max(0, Math.Min(10, (h.Pico[s] - bajo) / (alto - bajo) * 10));
                if (h.Voz) voz[b] = Math.Max(voz[b], v); else amb[b] = Math.Max(amb[b], v);
            }
        }
        List<string> r = new List<string>();
        for (int b = 0; b < n; b++)
            if (voz[b] >= 1 || amb[b] >= 1)
                r.Add((b * bloque) + ";" + Math.Round(voz[b]) + ";" + Math.Round(amb[b]));
        return r;
    }
}

// =====================================================================
// Orquesta las peticiones (una sola o por partes) y reintenta si hace falta.
// =====================================================================

public delegate string LlamadaIA(string instrucciones, string mensaje);

public class AsistenteIA
{
    // Videos mas largos que esto se analizan por partes.
    public double UmbralPartes = 35 * 60;
    public double TamanoParte = 20 * 60;
    public Action<string> Progreso = delegate { };
    readonly LlamadaIA llamar;

    public AsistenteIA(LlamadaIA llamar) { this.llamar = llamar; }

    // Pide y comprueba que sea JSON valido; un reintento si sale roto.
    string PedirJson(string instrucciones, string mensaje)
    {
        for (int intento = 1; ; intento++)
        {
            string r = llamar(instrucciones, mensaje);
            try { Json.Leer(Gemini.QuitarCercas(r)); return r; }
            catch (FormatException)
            {
                if (intento >= 2) throw new Exception("Gemini devolvi\u00f3 una respuesta que no se pudo leer dos veces seguidas.");
                Progreso("La respuesta lleg\u00f3 incompleta; reintentando\u2026");
            }
        }
    }

    // Devuelve el JSON final con el mismo formato en ambos modos.
    public string Ejecutar(Transcripcion t, double total, OpcionesIA op)
    {
        if (total <= UmbralPartes)
        {
            try
            {
                Progreso("Gemini est\u00e1 analizando el video completo\u2026");
                return PedirJson(PeticionIA.Instrucciones(op), PeticionIA.Mensaje(t, total, op));
            }
            catch (RespuestaCortada)
            {
                Progreso("La respuesta no cupo completa: se analizar\u00e1 por partes.");
            }
        }
        return PorPartes(t, total, op);
    }

    string PorPartes(Transcripcion t, double total, OpcionesIA op)
    {
        List<Segmento> segmentos = t.SegmentosActuales();
        List<Rango> partes = PeticionIA.Partes(segmentos, total, TamanoParte);
        List<string> resumenes = new List<string>();
        List<Tramo> candidatos = new List<Tramo>(), momentos = new List<Tramo>();
        string instrucciones = PeticionIA.InstruccionesParte(op);

        for (int i = 0; i < partes.Count; i++)
        {
            Progreso("Analizando la parte " + (i + 1) + " de " + partes.Count + " (" +
                     Formato.Tiempo(partes[i].Inicio) + "\u2013" + Formato.Tiempo(partes[i].Fin) + ")\u2026");
            string r = PedirJson(instrucciones, PeticionIA.MensajeParte(t, segmentos, total, op, i, partes, resumenes));
            object o = Json.Leer(Gemini.QuitarCercas(r));
            resumenes.Add(Json.Texto(o, "resumen"));
            // Los candidatos y momentos se leen con el mismo lector del resultado
            // final, limitados a esta parte.
            ResultadoIA parte = ResultadoIA.Leer("{\"corte\": " + Json.Escribir(Json.Lista(o, "candidatos"), false) +
                                                  ", \"momentos\": " + Json.Escribir(Json.Lista(o, "momentos"), false) + "}", total);
            foreach (Tramo c in parte.Corte)
            {
                c.Inicio = Math.Max(c.Inicio, partes[i].Inicio);
                c.Fin = Math.Min(c.Fin, partes[i].Fin);
                if (c.Fin - c.Inicio >= 0.5) candidatos.Add(c);
            }
            momentos.AddRange(parte.Momentos);
        }

        Progreso("Armando el video final con " + candidatos.Count + " candidatos\u2026");
        string final = PedirJson(PeticionIA.InstruccionesFinal(op),
                                 PeticionIA.MensajeFinal(t, total, op, partes, resumenes, candidatos, momentos));
        // Se guardan los candidatos junto al resultado para poder completar el
        // corte si queda corto.
        Dictionary<string, object> d = Json.Leer(Gemini.QuitarCercas(final)) as Dictionary<string, object>;
        if (d == null) return final;
        List<object> lista = new List<object>();
        foreach (Tramo c in candidatos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["inicio"] = c.Inicio; x["fin"] = c.Fin; x["importancia"] = c.Puntuacion;
            x["accion"] = c.Acelerar ? "acelerar" : "conservar"; x["velocidad"] = c.Velocidad;
            x["titulo"] = c.Titulo; x["motivo"] = c.Motivo;
            lista.Add(x);
        }
        d["candidatos"] = lista;
        return Json.Escribir(d);
    }

    // Segunda opinion: revisa el corte contra las reglas. Devuelve el JSON de
    // la revision ("" si fallo; la revision es opcional).
    public string Revisar(Transcripcion t, ResultadoIA r, OpcionesIA op)
    {
        if (r.Corte.Count == 0) return "";
        Progreso("Revisando que el corte cumpla las reglas\u2026");
        try { return PedirJson(PeticionIA.InstruccionesRevision(), PeticionIA.MensajeRevision(t, r, op)); }
        catch (Exception ex) { Progreso("No se pudo revisar (" + ex.Message + ")."); return ""; }
    }
}

// ---- src/comun/Editor.cs ----

// =====================================================================
// Edicion en la linea de tiempo
// =====================================================================

static class Editor
{
    const double Tolerancia = 0.0005; // segundos

    public static List<Rango> AjustarAFotogramas(List<Rango> rangos, double fps)
    {
        List<Rango> r = new List<Rango>();
        foreach (Rango x in rangos)
        {
            double a = Math.Round(x.Inicio * fps) / fps;
            double b = Math.Round(x.Fin * fps) / fps;
            if (b - a >= 1.0 / fps) r.Add(new Rango(a, b));
        }
        return r;
    }

    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000.0); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Corta cada evento en los bordes de los rangos. Devuelve los eventos
    // resultantes de la pista.
    static List<TrackEvent> CortarEnBordes(Track pista, List<Rango> rangos)
    {
        List<double> bordes = new List<double>();
        foreach (Rango r in rangos) { bordes.Add(r.Inicio); bordes.Add(r.Fin); }
        bordes.Sort();

        List<TrackEvent> originales = new List<TrackEvent>();
        foreach (TrackEvent e in pista.Events) originales.Add(e);

        foreach (TrackEvent e in originales)
        {
            double ini = S(e.Start), fin = S(e.End);
            // De atras hacia adelante: el evento original conserva la parte izquierda.
            for (int i = bordes.Count - 1; i >= 0; i--)
            {
                double b = bordes[i];
                if (b > ini + Tolerancia && b < fin - Tolerancia)
                    e.Split(TC(b - ini));
            }
        }

        List<TrackEvent> todos = new List<TrackEvent>();
        foreach (TrackEvent e in pista.Events) todos.Add(e);
        return todos;
    }

    // Fundido corto en el audio que empieza o termina en un corte, para que
    // no se oiga un chasquido.
    static void Suavizar(List<TrackEvent> eventos, List<Rango> rangos, double segundos)
    {
        if (segundos <= 0) return;
        foreach (TrackEvent e in eventos)
        {
            if (!(e is AudioEvent)) continue;
            double ini = S(e.Start), fin = S(e.End);
            double largo = Math.Min(segundos, (fin - ini) / 2);
            foreach (Rango r in rangos)
            {
                if (Math.Abs(ini - r.Fin) < Tolerancia) e.FadeIn.Length = TC(largo);
                if (Math.Abs(fin - r.Inicio) < Tolerancia) e.FadeOut.Length = TC(largo);
            }
        }
    }

    static bool DentroDeRango(TrackEvent e, List<Rango> rangos)
    {
        double medio = (S(e.Start) + S(e.End)) / 2;
        foreach (Rango r in rangos)
            if (medio > r.Inicio && medio < r.Fin) return true;
        return false;
    }

    static double QuitadoAntesDe(double t, List<Rango> rangos)
    {
        double q = 0;
        foreach (Rango r in rangos)
            if (r.Fin <= t + Tolerancia) q += r.Fin - r.Inicio;
        return q;
    }

    // Nueva posicion de un instante despues de quitar los rangos. Si cae
    // dentro de un rango, queda en el punto del corte.
    public static double PosicionTrasQuitar(double t, List<Rango> rangos)
    {
        double q = QuitadoAntesDe(t, rangos);
        foreach (Rango r in rangos)
            if (t > r.Inicio && t < r.Fin - Tolerancia) q += t - r.Inicio;
        return t - q;
    }

    // Quita los tramos de los rangos. Con "juntar" mueve lo que sigue para
    // cerrar el hueco; sin el, deja el espacio vacio.
    public static void Eliminar(Project proyecto, List<Track> pistas, List<Rango> rangos, bool juntar,
                                bool moverMarcadores, double suavizado)
    {
        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = CortarEnBordes(pista, rangos);
            List<TrackEvent> quedan = new List<TrackEvent>();
            foreach (TrackEvent e in eventos)
                if (DentroDeRango(e, rangos)) pista.Events.Remove(e); else quedan.Add(e);
            Suavizar(quedan, rangos, suavizado);
            if (!juntar) continue;

            List<TrackEvent> restantes = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) restantes.Add(e);
            restantes.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });

            // De izquierda a derecha: cada evento se mueve a un espacio ya libre.
            foreach (TrackEvent e in restantes)
            {
                double q = QuitadoAntesDe(S(e.Start), rangos);
                if (q > 0) e.Start = TC(S(e.Start) - q);
            }
        }
        Reagrupar(proyecto, pistas);

        if (!moverMarcadores) return;
        List<Marker> marcadores = new List<Marker>();
        foreach (Marker m in proyecto.Markers) marcadores.Add(m);
        foreach (Region m in proyecto.Regions) marcadores.Add(m);
        foreach (Marker m in marcadores)
        {
            double t = S(m.Position), nuevo = PosicionTrasQuitar(t, rangos);
            if (nuevo < t - Tolerancia)
            {
                try { m.Position = TC(nuevo); } catch { }
            }
        }
    }

    // Vegas no acepta velocidades de evento mayores a 4x.
    public const double VelocidadMaxima = 4;

    // Reproduce mas rapido los tramos indicados: corta en sus bordes, sube la
    // velocidad de cada evento de adentro (y acorta su duracion en la misma
    // proporcion) y corre hacia la izquierda lo que sigue. Con
    // "silenciarAudio" el audio de esos tramos queda mudo (acelerado suena raro).
    public static void Acelerar(Project proyecto, List<Track> pistas, List<Acelerado> tramos, bool silenciarAudio,
                                bool moverMarcadores, double suavizado)
    {
        List<Rango> bordes = new List<Rango>();
        foreach (Acelerado a in tramos) bordes.Add(new Rango(a.Inicio, a.Fin));

        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = CortarEnBordes(pista, bordes);
            eventos.Sort(delegate (TrackEvent x, TrackEvent y) { return S(x.Start).CompareTo(S(y.Start)); });

            // De izquierda a derecha: primero se acorta el evento y luego se
            // mueve, asi nunca se encima con el siguiente.
            foreach (TrackEvent e in eventos)
            {
                double ini = S(e.Start), fin = S(e.End), medio = (ini + fin) / 2;
                foreach (Acelerado a in tramos)
                {
                    if (medio <= a.Inicio || medio >= a.Fin) continue;
                    double antes = e.PlaybackRate;
                    double despues = Math.Min(VelocidadMaxima, antes * a.Factor);
                    e.PlaybackRate = despues;
                    e.Length = TC((fin - ini) * antes / despues);
                    if (silenciarAudio && e is AudioEvent) e.Mute = true;
                    break;
                }
                double nuevo = Acelerado.Posicion(ini, tramos);
                if (nuevo < ini - Tolerancia) e.Start = TC(nuevo);
            }

            // Fundido corto donde el audio normal se junta con el acelerado.
            if (suavizado > 0 && pista.IsAudio())
            {
                List<Rango> nuevosBordes = new List<Rango>();
                foreach (Acelerado a in tramos)
                    nuevosBordes.Add(new Rango(Acelerado.Posicion(a.Inicio, tramos), Acelerado.Posicion(a.Fin, tramos)));
                List<TrackEvent> todos = new List<TrackEvent>();
                foreach (TrackEvent e in pista.Events) todos.Add(e);
                Suavizar(todos, nuevosBordes, suavizado);
                // Los bordes de adentro del tramo tambien llevan fundido.
                List<Rango> invertidos = new List<Rango>();
                foreach (Rango r in nuevosBordes) invertidos.Add(new Rango(r.Fin, r.Inicio));
                Suavizar(todos, invertidos, suavizado);
            }
        }
        Reagrupar(proyecto, pistas);

        if (!moverMarcadores) return;
        List<Marker> marcadores = new List<Marker>();
        foreach (Marker m in proyecto.Markers) marcadores.Add(m);
        foreach (Region m in proyecto.Regions) marcadores.Add(m);
        foreach (Marker m in marcadores)
        {
            double t = S(m.Position), nuevo = Acelerado.Posicion(t, tramos);
            if (nuevo < t - Tolerancia)
            {
                try { m.Position = TC(nuevo); } catch { }
            }
        }
    }

    public static void Silenciar(Project proyecto, List<Track> pistas, List<Rango> rangos, double suavizado)
    {
        Silenciar(pistas, rangos, suavizado);
        Reagrupar(proyecto, pistas);
    }

    public static void Silenciar(List<Track> pistas, List<Rango> rangos, double suavizado)
    {
        foreach (Track pista in pistas)
        {
            if (!pista.IsAudio()) continue;
            List<TrackEvent> eventos = CortarEnBordes(pista, rangos);
            List<TrackEvent> suenan = new List<TrackEvent>();
            foreach (TrackEvent e in eventos)
                if (DentroDeRango(e, rangos)) e.Mute = true; else suenan.Add(e);
            Suavizar(suenan, rangos, suavizado);
        }
    }

    public static void Marcar(Project proyecto, List<Rango> rangos)
    {
        foreach (Rango r in rangos)
            proyecto.Regions.Add(new Region(TC(r.Inicio), TC(r.Fin - r.Inicio), "Silencio"));
    }

    // ------------------------------------------------------------ grupos

    // Al cortar con Split, Vegas deja cada pedazo nuevo en el mismo grupo que
    // el clip original: al final todos los pedazos quedan unidos y mover o
    // borrar uno mueve o borra todos. Aqui cada grupo asi se separa en
    // grupos chicos: los eventos que se enciman en el tiempo (el video y sus
    // audios del mismo pedazo) siguen juntos. Solo se tocan grupos con dos o
    // mas eventos en la misma pista, que es la marca de este problema.
    // Devuelve cuantos pedazos quedaron en su propio grupo.
    public static int Reagrupar(Project proyecto, IEnumerable<Track> pistas)
    {
        // Dictionary y no HashSet: Vegas compila sin System.Core.
        Dictionary<string, bool> vistos = new Dictionary<string, bool>();
        int separados = 0;
        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) eventos.Add(e);
            foreach (TrackEvent e in eventos)
            {
                if (vistos.ContainsKey(Clave(e))) continue;
                TrackEventGroup grupo = null;
                try { if (e.IsGrouped) grupo = e.Group; } catch { }
                if (grupo == null) continue;

                List<TrackEvent> miembros = new List<TrackEvent>();
                foreach (TrackEvent m in grupo) miembros.Add(m);
                Dictionary<int, bool> pistasDelGrupo = new Dictionary<int, bool>();
                bool roto = false;
                foreach (TrackEvent m in miembros)
                {
                    vistos[Clave(m)] = true;
                    if (pistasDelGrupo.ContainsKey(m.Track.Index)) roto = true;
                    pistasDelGrupo[m.Track.Index] = true;
                }
                if (!roto) continue;

                List<List<TrackEvent>> partes = Partes(miembros);
                // La primera parte se queda en el grupo original.
                for (int i = 1; i < partes.Count; i++)
                {
                    foreach (TrackEvent m in partes[i])
                        try { grupo.Remove(m); } catch { }
                    if (partes[i].Count > 1)
                    {
                        TrackEventGroup nuevo = NuevoGrupo(proyecto);
                        foreach (TrackEvent m in partes[i]) nuevo.Add(m);
                    }
                    separados++;
                }
            }
        }
        return separados;
    }

    public static int Reagrupar(Project proyecto)
    {
        return Reagrupar(proyecto, proyecto.Tracks);
    }

    // Segun la version de Vegas el grupo se crea sin argumentos o con el
    // proyecto; por reflexion sirve para ambas.
    public static TrackEventGroup NuevoGrupo(Project proyecto)
    {
        TrackEventGroup g;
        try { g = (TrackEventGroup)Activator.CreateInstance(typeof(TrackEventGroup)); }
        catch { g = (TrackEventGroup)Activator.CreateInstance(typeof(TrackEventGroup), proyecto); }
        proyecto.Groups.Add(g);
        return g;
    }

    static string Clave(TrackEvent e)
    {
        return e.Track.Index + ":" + Math.Round(e.Start.ToMilliseconds());
    }

    // Eventos que se enciman en el tiempo van juntos.
    static List<List<TrackEvent>> Partes(List<TrackEvent> eventos)
    {
        eventos.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });
        List<List<TrackEvent>> partes = new List<List<TrackEvent>>();
        double fin = double.MinValue;
        foreach (TrackEvent e in eventos)
        {
            if (partes.Count == 0 || S(e.Start) >= fin - 0.002)
            {
                partes.Add(new List<TrackEvent>());
                fin = S(e.End);
            }
            else fin = Math.Max(fin, S(e.End));
            partes[partes.Count - 1].Add(e);
        }
        return partes;
    }

    // Corre todo (eventos, marcadores y regiones) "segundos" a la derecha.
    public static void Desplazar(Project p, double segundos)
    {
        if (segundos <= 0) return;
        List<TrackEvent> eventos = new List<TrackEvent>();
        foreach (Track t in p.Tracks) foreach (TrackEvent e in t.Events) eventos.Add(e);
        // De derecha a izquierda para que nada se encime al moverse.
        eventos.Sort(delegate (TrackEvent a, TrackEvent b) { return b.Start.ToMilliseconds().CompareTo(a.Start.ToMilliseconds()); });
        foreach (TrackEvent e in eventos) e.Start = Timecode.FromMilliseconds(e.Start.ToMilliseconds() + segundos * 1000);
        List<Marker> marcas = new List<Marker>();
        foreach (Marker m in p.Markers) marcas.Add(m);
        foreach (Region r in p.Regions) marcas.Add(r);
        marcas.Sort(delegate (Marker a, Marker b) { return b.Position.ToMilliseconds().CompareTo(a.Position.ToMilliseconds()); });
        foreach (Marker m in marcas) try { m.Position = Timecode.FromMilliseconds(m.Position.ToMilliseconds() + segundos * 1000); } catch { }
    }
}

// ---- src/comun/PistasVegas.cs ----

// =====================================================================
// Pistas de audio del proyecto y render a WAV
// =====================================================================

public class InfoPista
{
    public AudioTrack Pista;
    public string Nombre;   // corto, para botones: "A3 \u00b7 voz.wav"
    public string Detalle;  // largo, para ayudas
    public string Etiqueta; // "A3"
    public string Archivo;  // archivo mas usado en la pista
    public int Eventos;
}

public static class PistasVegas
{
    public static List<InfoPista> Listar(Project proyecto)
    {
        List<InfoPista> lista = new List<InfoPista>();
        foreach (Track t in proyecto.Tracks)
        {
            AudioTrack a = t as AudioTrack;
            if (a == null) continue;
            InfoPista p = new InfoPista();
            int flujo;
            ArchivoPrincipal(a, out p.Archivo, out p.Eventos, out flujo);
            string detalle = !String.IsNullOrEmpty(a.Name) ? a.Name : p.Archivo ?? "vac\u00eda";
            string corto = detalle.Length > 22 ? detalle.Substring(0, 21) + "\u2026" : detalle;
            if (String.IsNullOrEmpty(a.Name) && flujo > 0) corto += " (audio " + (flujo + 1) + ")";
            p.Pista = a;
            p.Etiqueta = "A" + (a.Index + 1);
            p.Nombre = p.Etiqueta + " \u00b7 " + corto;
            p.Detalle = "Pista " + (a.Index + 1) + ": " + detalle +
                (flujo > 0 ? " (audio " + (flujo + 1) + ")" : "") + " \u00b7 " + p.Eventos + " eventos";
            lista.Add(p);
        }
        return lista;
    }

    // La pista de voz probable: la que tenga un archivo "mejorada" o, si no
    // hay, la que tenga mas eventos.
    public static int SugerirVoz(List<InfoPista> pistas)
    {
        int sugerida = 0, mejor = -1;
        for (int i = 0; i < pistas.Count; i++)
        {
            InfoPista p = pistas[i];
            int puntos = p.Eventos + (p.Archivo != null && p.Archivo.ToLowerInvariant().Contains("mejorada") ? 100000 : 0);
            if (puntos > mejor) { mejor = puntos; sugerida = i; }
        }
        return sugerida;
    }

    static void ArchivoPrincipal(Track t, out string archivo, out int eventos, out int flujo)
    {
        Dictionary<string, int> cuenta = new Dictionary<string, int>();
        archivo = null;
        flujo = 0;
        eventos = t.Events.Count;
        int max = 0;
        foreach (TrackEvent e in t.Events)
        {
            if (e.ActiveTake == null || e.ActiveTake.Media == null) continue;
            string f = Path.GetFileName(e.ActiveTake.Media.FilePath ?? "");
            int c;
            cuenta.TryGetValue(f, out c);
            cuenta[f] = ++c;
            if (c > max)
            {
                max = c;
                archivo = f;
                flujo = IndiceFlujo(e.ActiveTake);
            }
        }
    }

    // Indice del flujo de audio que usa la toma (OBS graba varias pistas de
    // audio en el mismo .mp4). Por reflexion para no depender de la API exacta.
    public static int IndiceFlujo(Take toma)
    {
        try
        {
            object flujo = toma.GetType().GetProperty("MediaStream").GetValue(toma, null);
            object indice = flujo.GetType().GetProperty("Index").GetValue(flujo, null);
            return Convert.ToInt32(indice);
        }
        catch { return 0; }
    }

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Eventos de la pista con su archivo, para la transcripcion.
    public static List<Fuente> Fuentes(Track pista)
    {
        List<Fuente> r = new List<Fuente>();
        foreach (TrackEvent e in pista.Events)
        {
            Take toma = e.ActiveTake;
            if (toma == null || toma.Media == null || toma.Media.IsGenerated() || String.IsNullOrEmpty(toma.Media.FilePath)) continue;
            Fuente f = new Fuente();
            f.Inicio = S(e.Start); f.Fin = S(e.End);
            f.Desde = S(toma.Offset); f.Velocidad = e.PlaybackRate;
            f.Media = toma.Media.FilePath;
            f.Flujo = IndiceFlujo(toma);
            r.Add(f);
        }
        r.Sort(delegate (Fuente a, Fuente b) { return a.Inicio.CompareTo(b.Inicio); });
        return r;
    }

    // Donde suena ahora ese segundo de ese archivo (y flujo): pista e instante
    // de cada evento de audio que lo contiene.
    public class Lugar { public Track Pista; public double Tiempo, Velocidad; }

    public static List<Lugar> Donde(Project p, string media, int flujo, double segundo)
    {
        List<Lugar> r = new List<Lugar>();
        foreach (Track pista in p.Tracks)
        {
            if (!pista.IsAudio()) continue;
            foreach (TrackEvent e in pista.Events)
            {
                Take toma = e.ActiveTake;
                if (toma == null || toma.Media == null || e.Mute ||
                    !String.Equals(toma.Media.FilePath, media, StringComparison.OrdinalIgnoreCase) || IndiceFlujo(toma) != flujo) continue;
                double desde = S(toma.Offset), largo = (S(e.End) - S(e.Start)) * e.PlaybackRate;
                if (segundo < desde - 0.0005 || segundo >= desde + largo - 0.0005) continue;
                Lugar l = new Lugar();
                l.Pista = pista; l.Velocidad = e.PlaybackRate;
                l.Tiempo = S(e.Start) + (segundo - desde) / e.PlaybackRate;
                r.Add(l);
            }
        }
        return r;
    }

    // Ubicador para la transcripcion: cada palabra se busca por su archivo y
    // segundo en los eventos de audio actuales, asi sigue cualquier edicion
    // (tambien a mano). Si un archivo se repite, gana la primera aparicion.
    public static Func<int, double, double> Ubicador(Project p, Transcripcion t)
    {
        Dictionary<string, List<double[]>> eventos = new Dictionary<string, List<double[]>>();
        foreach (Track pista in p.Tracks)
        {
            if (!pista.IsAudio()) continue;
            foreach (TrackEvent e in pista.Events)
            {
                Take toma = e.ActiveTake;
                if (toma == null || toma.Media == null || String.IsNullOrEmpty(toma.Media.FilePath)) continue;
                string clave = toma.Media.FilePath.ToLowerInvariant() + "|" + IndiceFlujo(toma);
                List<double[]> l;
                if (!eventos.TryGetValue(clave, out l)) { l = new List<double[]>(); eventos[clave] = l; }
                double desde = S(toma.Offset), largo = (S(e.End) - S(e.Start)) * e.PlaybackRate;
                l.Add(new double[] { desde, desde + largo, S(e.Start), e.PlaybackRate });
            }
        }
        // Lo copiado al inicio como gancho repite el mismo audio: se prefiere
        // donde esta de verdad, fuera de la region "GANCHO".
        List<Rango> ganchos = new List<Rango>();
        try
        {
            foreach (Region r in p.Regions)
                if ((r.Label ?? "").StartsWith("GANCHO"))
                    ganchos.Add(new Rango(S(r.Position), S(r.Position) + S(r.Length) + 0.5));
        }
        catch { }
        return delegate (int hablante, double tiempo)
        {
            Fuente f;
            double segundo;
            if (!t.AFuente(hablante, tiempo, out f, out segundo)) return double.NaN;
            List<double[]> l;
            if (!eventos.TryGetValue(f.Media.ToLowerInvariant() + "|" + f.Flujo, out l)) return double.NaN;
            double mejor = double.NaN, enGancho = double.NaN;
            foreach (double[] x in l)
                if (segundo >= x[0] - 0.0005 && segundo < x[1] - 0.0005)
                {
                    double ahora = x[2] + (segundo - x[0]) / x[3];
                    bool gancho = false;
                    foreach (Rango g in ganchos) if (ahora >= g.Inicio && ahora < g.Fin) { gancho = true; break; }
                    if (gancho) { if (double.IsNaN(enGancho) || ahora < enGancho) enGancho = ahora; }
                    else if (double.IsNaN(mejor) || ahora < mejor) mejor = ahora;
                }
            return double.IsNaN(mejor) ? enGancho : mejor;
        };
    }

    public static bool HaySeleccion(Vegas vegas)
    {
        return Math.Abs(vegas.Transport.SelectionLength.ToMilliseconds()) > 1;
    }

    // Rango a procesar, en segundos: todo el proyecto o la seleccion de tiempo.
    public static void ObtenerRango(Vegas vegas, bool usarSeleccion, out double inicio, out double duracion)
    {
        if (usarSeleccion)
        {
            inicio = vegas.Transport.SelectionStart.ToMilliseconds() / 1000.0;
            duracion = vegas.Transport.SelectionLength.ToMilliseconds() / 1000.0;
            if (duracion < 0) { inicio += duracion; duracion = -duracion; }
        }
        else
        {
            inicio = 0;
            duracion = vegas.Project.Length.ToMilliseconds() / 1000.0;
        }
        if (duracion < 0.1) throw new Exception("El rango a analizar est\u00e1 vac\u00edo.");
    }

    // Renderiza solo esa pista a un WAV temporal (las demas se silencian
    // durante el render y se restauran despues). Quien llama borra el archivo.
    public static string RenderizarWav(Vegas vegas, AudioTrack pista, double inicio, double duracion)
    {
        Project proyecto = vegas.Project;
        RenderTemplate plantilla = PlantillaWav(vegas);
        string wav = Path.Combine(Path.GetTempPath(), "vegas-cut-" + Guid.NewGuid().ToString("N") + ".wav");

        // Las pistas se identifican por indice: Vegas puede devolver objetos
        // distintos para la misma pista.
        Dictionary<int, bool> muteAntes = new Dictionary<int, bool>();
        try
        {
            foreach (Track t in proyecto.Tracks)
            {
                if (!t.IsAudio()) continue;
                muteAntes[t.Index] = t.Mute;
                t.Mute = t.Index != pista.Index;
            }

            RenderArgs args = new RenderArgs();
            args.OutputFile = wav;
            args.RenderTemplate = plantilla;
            args.Start = Timecode.FromMilliseconds(inicio * 1000);
            args.Length = Timecode.FromMilliseconds(duracion * 1000);
            RenderStatus estado = vegas.Render(args);
            if (estado != RenderStatus.Complete)
                throw new Exception("El render del audio no termin\u00f3 (" + estado + ").");
        }
        finally
        {
            foreach (Track t in proyecto.Tracks)
                if (muteAntes.ContainsKey(t.Index)) t.Mute = muteAntes[t.Index];
        }
        return wav;
    }

    // Render + niveles cada 10 ms, sin dejar archivos.
    public static Analisis Niveles(Vegas vegas, AudioTrack pista, double inicio, double duracion)
    {
        string wav = RenderizarWav(vegas, pista, inicio, duracion);
        try
        {
            Analisis a = WavNiveles.Leer(wav, Analisis.Paso);
            a.Inicio = inicio;
            return a;
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
    }

    static RenderTemplate PlantillaWav(Vegas vegas)
    {
        RenderTemplate primera = null;
        foreach (Renderer r in vegas.Renderers)
        {
            string ext = (r.FileExtension ?? "").ToLowerInvariant();
            if (!ext.EndsWith(".wav")) continue;
            foreach (RenderTemplate t in r.Templates)
            {
                if (!t.IsValid()) continue;
                if (primera == null) primera = t;
                string n = t.Name ?? "";
                if (n.Contains("PCM") && n.Contains("16")) return t;
            }
        }
        if (primera == null)
            throw new Exception("No se encontr\u00f3 la plantilla de render WAV en Vegas.");
        return primera;
    }
}

// ---- src/comun/Serie.cs ----

// =====================================================================
// Series: proyectos de varias partes (gameplays, video ensayos, podcast...)
//
// Una serie es un archivo "<nombre>.vegascut-serie.json" (normalmente en la
// carpeta de sus capitulos) con su nombre, tipo, notas y la lista de
// capitulos EN ORDEN. Se administra con el script Series; MomentosIA y
// Anteriormente reconocen a que serie pertenece el proyecto abierto. vegas-cut
// recuerda las series usadas en %APPDATA%\vegas-cut\series.json.
//
// Cada capitulo guarda junto a su .veg su ficha (<proyecto>.vegascut-ficha.json):
// resumen, hilos abiertos y frases clave, hecha una vez por Gemini.
// =====================================================================

// Un capitulo ya transcrito, con su transcripcion cargada.
public class Episodio
{
    public string Veg = "", Nombre = "";
    public Transcripcion T;
    public string Resumen = "";   // de su ficha o de su respuesta de MomentosIA
    public Ficha Ficha;
    public bool TieneFuentes { get { return T != null && T.TieneFuentes; } }

    public static Episodio Abrir(string ruta)
    {
        Episodio e = new Episodio();
        string veg = ruta;
        if (ruta.EndsWith(".vegascut.json", StringComparison.OrdinalIgnoreCase))
            veg = ruta.Substring(0, ruta.Length - ".vegascut.json".Length) + ".veg";
        e.Veg = veg;
        e.Nombre = Path.GetFileNameWithoutExtension(veg);
        string rt = Transcripcion.RutaPara(veg);
        if (rt == null || !File.Exists(rt)) throw new Exception(e.Nombre + " no tiene transcripci\u00f3n (ejecuta Transcribir en ese proyecto).");
        e.T = Transcripcion.Cargar(rt);
        e.Ficha = Ficha.Cargar(veg);
        if (e.Ficha != null) e.Resumen = e.Ficha.Resumen;
        else
            try
            {
                string ia = Path.Combine(Path.GetDirectoryName(veg), e.Nombre + ".vegascut-ia.json");
                if (File.Exists(ia))
                {
                    object o = Json.Leer(File.ReadAllText(ia, Encoding.UTF8));
                    e.Resumen = Json.Texto(Json.Leer(Gemini.QuitarCercas(Json.Texto(o, "respuesta"))), "resumen");
                }
            }
            catch { }
        return e;
    }

    // Frases que quedaron en el video (sin lo cortado con las herramientas),
    // con los tiempos originales de la transcripcion.
    public List<Segmento> Publicado()
    {
        List<Segmento> r = new List<Segmento>();
        foreach (Segmento s in T.Segmentos)
        {
            if (Transcripcion.Alucinacion(s.Texto) || String.IsNullOrEmpty(s.Texto)) continue;
            if (double.IsNaN(T.Mapear(s.Inicio)) || double.IsNaN(T.Mapear(s.Fin))) continue;
            r.Add(s);
        }
        return r;
    }

    public static string Nombre_(Transcripcion t, int h) { return h >= 0 && h < t.Hablantes.Count ? t.Hablantes[h].Nombre : "?"; }

    public string Transcrito()
    {
        StringBuilder sb = new StringBuilder();
        foreach (Segmento s in Publicado())
            sb.Append("[" + Serie.S(s.Inicio) + "-" + Serie.S(s.Fin) + "] " + Nombre_(T, s.Hablante) + ": " + s.Texto + "\n");
        return sb.ToString();
    }
}

public class FraseClave
{
    public double Inicio, Fin;
    public string Quien = "", Texto = "", Por = "";
}

// Resumen de un capitulo para usarlo de contexto en los demas.
public class Ficha
{
    public string Resumen = "", Generada = "";
    public string Estructura = "";   // como abre, como avanza y como cierra (para no repetir la formula)
    public List<string> Hilos = new List<string>(), Recurrentes = new List<string>();
    public List<FraseClave> Frases = new List<FraseClave>();

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-ficha.json");
    }

    public static Ficha Leer(string json)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        Ficha f = new Ficha();
        f.Resumen = Json.Texto(o, "resumen");
        f.Generada = Json.Texto(o, "generada");
        f.Estructura = Json.Texto(o, "estructura");
        foreach (object x in Json.Lista(o, "hilos")) if (x is string) f.Hilos.Add((string)x);
        foreach (object x in Json.Lista(o, "recurrentes")) if (x is string) f.Recurrentes.Add((string)x);
        foreach (object x in Json.Lista(o, "frases"))
        {
            FraseClave c = new FraseClave();
            c.Inicio = Json.Numero(x, "inicio", -1); c.Fin = Json.Numero(x, "fin", -1);
            c.Quien = Json.Texto(x, "quien"); c.Texto = Json.Texto(x, "texto"); c.Por = Json.Texto(x, "por");
            if (c.Inicio >= 0 && c.Fin > c.Inicio) f.Frases.Add(c);
        }
        if (f.Resumen.Length == 0) throw new Exception("La ficha no trae resumen.");
        return f;
    }

    public static Ficha Cargar(string veg)
    {
        try
        {
            string r = RutaPara(veg);
            return File.Exists(r) ? Leer(File.ReadAllText(r, Encoding.UTF8)) : null;
        }
        catch { return null; }
    }

    public void Guardar(string veg)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["resumen"] = Resumen;
        d["generada"] = Generada;
        d["estructura"] = Estructura;
        d["hilos"] = new List<object>(Hilos.ToArray());
        d["recurrentes"] = new List<object>(Recurrentes.ToArray());
        List<object> fs = new List<object>();
        foreach (FraseClave c in Frases)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["inicio"] = Math.Round(c.Inicio, 2); x["fin"] = Math.Round(c.Fin, 2);
            x["quien"] = c.Quien; x["texto"] = c.Texto; x["por"] = c.Por;
            fs.Add(x);
        }
        d["frases"] = fs;
        File.WriteAllText(RutaPara(veg), Json.Escribir(d), new UTF8Encoding(false));
    }

    // Para el contexto de MomentosIA (sin tiempos) o de Anteriormente (con
    // las frases clave y sus tiempos).
    public string Texto(bool frases)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(Resumen.Trim().Replace("\n", " ") + "\n");
        if (Hilos.Count > 0) sb.Append("  Hilos abiertos: " + String.Join("; ", Hilos.ToArray()) + "\n");
        if (Recurrentes.Count > 0) sb.Append("  Recurrente: " + String.Join("; ", Recurrentes.ToArray()) + "\n");
        if (frases)
            foreach (FraseClave c in Frases)
                sb.Append("  [" + Serie.S(c.Inicio) + "-" + Serie.S(c.Fin) + "] " + c.Quien + ": " + c.Texto +
                          (c.Por.Length > 0 ? " (" + c.Por + ")" : "") + "\n");
        return sb.ToString();
    }
}

// Un capitulo de la serie (sin cargar su transcripcion).
public class CapSerie
{
    public string Veg = "", Nombre = "";
    public int Posicion;          // 1, 2, 3... en la serie
    public int Relacion = -1;     // -1 anterior, 0 el proyecto abierto, 1 posterior
    public bool Elegido = true;   // usarlo de contexto en este proyecto
    public string Papel = "Normal";
    public bool Existe { get { return File.Exists(Veg); } }
    public bool Transcrito { get { return File.Exists(Transcripcion.RutaPara(Veg)); } }
    public bool TieneFicha { get { return File.Exists(Ficha.RutaPara(Veg)); } }
}

public class SerieProyecto
{
    public static readonly string[] Tipos = { "Gameplay", "Video ensayo", "Podcast", "Otro" };

    public string Ruta = "", Nombre = "", Tipo = "Gameplay", Notas = "", Carpeta = "";
    public FormatoSerie Formato = FormatoSerie.Preset("100 d\u00edas");
    public MusicaSerie Musica = new MusicaSerie();
    public List<string> Episodios = new List<string>();   // rutas de los .veg, en orden
    // Papel de cada capitulo (primero, especial, final...) y su nota, por
    // ruta del .veg. Lo que no esta aqui es "Normal" (o "Primer cap\u00edtulo").
    public Dictionary<string, string> Papeles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> NotasEpisodio = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    // Lo que se produjo de cada capitulo (tipo, titulo, como cerro), para los siguientes.
    public Dictionary<string, RegistroCapitulo> Producidos = new Dictionary<string, RegistroCapitulo>(StringComparer.OrdinalIgnoreCase);

    public static string Extension = ".vegascut-serie.json";

    public static string RutaPara(string carpeta, string nombre)
    {
        string limpio = nombre;
        foreach (char c in Path.GetInvalidFileNameChars()) limpio = limpio.Replace(c, '_');
        return Path.Combine(carpeta, limpio + Extension);
    }

    public static SerieProyecto Cargar(string ruta)
    {
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        if (Json.Texto(o, "formato") != "vegas-cut-serie") throw new Exception("No es un archivo de serie de vegas-cut.");
        SerieProyecto s = new SerieProyecto();
        s.Ruta = ruta;
        s.Nombre = Json.Texto(o, "nombre");
        s.Tipo = Json.Texto(o, "tipo");
        if (Array.IndexOf(Tipos, s.Tipo) < 0) s.Tipo = "Otro";
        s.Notas = Json.Texto(o, "notas");
        s.Carpeta = Json.Texto(o, "carpeta");
        object f = Json.Valor(o, "estructura");
        s.Formato = f != null ? FormatoSerie.Leer(f) : FormatoSerie.Preset(FormatoSerie.SegunTipo(s.Tipo));
        s.Musica = MusicaSerie.Leer(Json.Valor(o, "musica"));
        string dir = Path.GetDirectoryName(ruta);
        foreach (object x in Json.Lista(o, "episodios"))
        {
            string veg = Json.Texto(x, "veg"), rel = Json.Texto(x, "relativo");
            // Si se movio la carpeta (u otra letra de disco), se busca junto al archivo de la serie.
            if (!File.Exists(veg) && rel.Length > 0 && File.Exists(Path.Combine(dir, rel))) veg = Path.GetFullPath(Path.Combine(dir, rel));
            if (veg.Length == 0) continue;
            s.Episodios.Add(veg);
            string papel = Json.Texto(x, "papel");
            if (Array.IndexOf(PapelEpisodio.Papeles, papel) >= 0) s.Papeles[veg] = papel;
            string nota = Json.Texto(x, "nota");
            if (nota.Length > 0) s.NotasEpisodio[veg] = nota;
            RegistroCapitulo rc = RegistroCapitulo.Leer(Json.Valor(x, "producido"));
            if (rc != null) s.Producidos[veg] = rc;
        }
        return s;
    }

    public void Guardar()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-serie";
        d["nombre"] = Nombre; d["tipo"] = Tipo; d["notas"] = Notas; d["carpeta"] = Carpeta;
        d["estructura"] = Formato.Escribir();
        d["musica"] = Musica.Escribir();
        List<object> l = new List<object>();
        foreach (string veg in Episodios)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["veg"] = veg;
            x["relativo"] = Relativa(Path.GetDirectoryName(Ruta), veg);
            string papel, nota;
            if (Papeles.TryGetValue(veg, out papel)) x["papel"] = papel;
            if (NotasEpisodio.TryGetValue(veg, out nota) && nota.Trim().Length > 0) x["nota"] = nota.Trim();
            RegistroCapitulo rc;
            if (Producidos.TryGetValue(veg, out rc) && !rc.Vacio) x["producido"] = rc.Escribir();
            l.Add(x);
        }
        d["episodios"] = l;
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
        File.WriteAllText(Ruta, Json.Escribir(d), new UTF8Encoding(false));
        Registrar(Ruta);
    }

    // Ruta relativa si el capitulo esta dentro de la carpeta de la serie.
    static string Relativa(string dir, string veg)
    {
        string d = dir.TrimEnd(Path.DirectorySeparatorChar, '/') + Path.DirectorySeparatorChar;
        return veg.StartsWith(d, StringComparison.OrdinalIgnoreCase) ? veg.Substring(d.Length) : "";
    }

    // Papel del capitulo: el elegido o, si no, "Primer cap\u00edtulo" para el
    // primero de la lista y "Normal" para los demas.
    public string Papel(string veg)
    {
        string p;
        if (!String.IsNullOrEmpty(veg) && Papeles.TryGetValue(veg, out p)) return p;
        return IndiceDe(veg ?? "") == 0 ? "Primer cap\u00edtulo" : "Normal";
    }

    public void CambiarPapel(string veg, string papel)
    {
        if (Array.IndexOf(PapelEpisodio.Papeles, papel) >= 0) Papeles[veg] = papel;
    }

    public string NotaEpisodio(string veg)
    {
        string n;
        return !String.IsNullOrEmpty(veg) && NotasEpisodio.TryGetValue(veg, out n) ? n : "";
    }

    public RegistroCapitulo Producido(string veg)
    {
        RegistroCapitulo r;
        return !String.IsNullOrEmpty(veg) && Producidos.TryGetValue(veg, out r) ? r : null;
    }

    public int IndiceDe(string veg)
    {
        for (int i = 0; i < Episodios.Count; i++)
            if (String.Equals(Episodios[i], veg, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // Agrega un capitulo en su lugar: por temporada y numero si el nombre los
    // trae (S01E03), si no al final.
    public bool Agregar(string veg)
    {
        if (IndiceDe(veg) >= 0) return false;
        int orden = Serie.Orden(Path.GetFileNameWithoutExtension(veg));
        int i = Episodios.Count;
        if (orden < int.MaxValue)
            for (int k = 0; k < Episodios.Count; k++)
                if (Serie.Orden(Path.GetFileNameWithoutExtension(Episodios[k])) > orden) { i = k; break; }
        Episodios.Insert(i, veg);
        return true;
    }

    // Agrega los .veg de la carpeta (y subcarpetas) que parecen de esta serie:
    // mismo nombre con otro S01E02 que los capitulos que ya tiene, o, si no
    // tiene ninguno, todos los que traen S01E02. Devuelve cuantos agrego.
    public int BuscarEnCarpeta()
    {
        if (String.IsNullOrEmpty(Carpeta) || !Directory.Exists(Carpeta)) return 0;
        List<string> claves = new List<string>();
        foreach (string e in Episodios)
        {
            int t, n; string k;
            if (Serie.Clave(Path.GetFileNameWithoutExtension(e), out t, out n, out k) && !claves.Contains(k)) claves.Add(k);
        }
        int agregados = 0;
        foreach (string f in Serie.ArchivosVeg(Carpeta, 6))
        {
            if (Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"\s(BASE|CAP|MOM( \d+)?)$", RegexOptions.IgnoreCase)) continue;   // copias del mismo capitulo
            int t, n; string k;
            if (!Serie.Clave(Path.GetFileNameWithoutExtension(f), out t, out n, out k)) continue;
            if (claves.Count > 0 && !claves.Contains(k)) continue;
            if (Agregar(f)) agregados++;
        }
        return agregados;
    }

    // Capitulos con su relacion al proyecto abierto (si no esta en la serie,
    // todos cuentan como anteriores).
    public List<CapSerie> Capitulos(string vegActual)
    {
        List<CapSerie> r = new List<CapSerie>();
        int actual = IndiceDe(vegActual ?? "");
        for (int i = 0; i < Episodios.Count; i++)
        {
            CapSerie c = new CapSerie();
            c.Veg = Episodios[i]; c.Nombre = Path.GetFileNameWithoutExtension(Episodios[i]); c.Posicion = i + 1;
            c.Relacion = actual < 0 ? -1 : i.CompareTo(actual);
            c.Papel = Papel(Episodios[i]);
            r.Add(c);
        }
        return r;
    }

    // ------------------------------------------------ series conocidas

    static string RutaRegistro
    {
        get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"), "series.json"); }
    }

    public static List<string> Registradas()
    {
        List<string> r = new List<string>();
        try
        {
            if (File.Exists(RutaRegistro))
                foreach (object x in Json.Lista(Json.Leer(File.ReadAllText(RutaRegistro, Encoding.UTF8)), "series"))
                    if (x is string && File.Exists((string)x) && !r.Contains((string)x)) r.Add((string)x);
        }
        catch { }
        return r;
    }

    static void GuardarRegistro(List<string> l)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaRegistro));
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["series"] = new List<object>(l.ToArray());
            File.WriteAllText(RutaRegistro, Json.Escribir(d), new UTF8Encoding(false));
        }
        catch { }
    }

    public static void Registrar(string ruta)
    {
        List<string> l = Registradas();
        l.RemoveAll(delegate (string x) { return String.Equals(x, ruta, StringComparison.OrdinalIgnoreCase); });
        l.Insert(0, ruta);
        GuardarRegistro(l);
    }

    public static void Olvidar(string ruta)
    {
        List<string> l = Registradas();
        l.RemoveAll(delegate (string x) { return String.Equals(x, ruta, StringComparison.OrdinalIgnoreCase); });
        GuardarRegistro(l);
    }

    // La serie del proyecto: la que eligio para el (guardado junto al .veg)
    // o la primera conocida que lo tenga como capitulo.
    public static SerieProyecto DelProyecto(string veg)
    {
        string elegida = AjustesProyecto.Serie(veg);
        if (elegida.Length > 0 && File.Exists(elegida))
            try { return Cargar(elegida); } catch { }
        foreach (string r in Registradas())
            try
            {
                SerieProyecto s = Cargar(r);
                if (s.IndiceDe(veg) >= 0) return s;
            }
            catch { }
        return null;
    }
}

// De que va la serie y como se edita: formato, premisa, como se marca el
// avance (Dia N, Parte N...), el narrador y las reglas de ritmo. Lo usan
// MomentosIA (contexto) y PulirEpisodio (medidor, estructura y narracion).
public class FormatoSerie
{
    public static readonly string[] Formatos = { "100 d\u00edas", "Aventura por episodios", "Serie de TV / anime", "Retos / minijuegos",
                                                 "Video ensayo", "Top / lista", "Podcast", "Otro" };
    public const string TV = "Serie de TV / anime";
    public static readonly string[] Avances = { "D\u00eda N", "Parte N", "Etapa N", "Ronda N", "Acto N", "N\u00famero N", "Ninguno" };

    public string Nombre = "100 d\u00edas";
    public string Premisa = "";            // de que va y que se busca (el objetivo de la serie)
    public string Avance = "D\u00eda N";
    public bool Narrador = true;
    public string NarradorNombre = "Narrador";   // hablante de la transcripcion
    public string EstiloNarrador = "";
    public string Aprendido = "";          // de que proyecto salieron las reglas
    public ReglasRitmo Reglas = new ReglasRitmo();
    public PlantillaTV Tv = PlantillaTV.PorDefecto();   // bloques y kit (formato Serie de TV)
    public bool EsTV { get { return Nombre == TV; } }

    public static string SegunTipo(string tipo)
    {
        switch (tipo)
        {
            case "Video ensayo": return "Video ensayo";
            case "Podcast": return "Podcast";
            case "Gameplay": return "100 d\u00edas";
            default: return "Otro";
        }
    }

    // Valores de partida de cada formato. "100 d\u00edas" sale de lo que funcion\u00f3
    // en JoJoMania (docs/estilo-jojomania.md).
    public static FormatoSerie Preset(string formato)
    {
        FormatoSerie f = new FormatoSerie();
        f.Nombre = Array.IndexOf(Formatos, formato) >= 0 ? formato : "Otro";
        ReglasRitmo r = f.Reglas;
        switch (f.Nombre)
        {
            case "100 d\u00edas":
                f.Avance = "D\u00eda N";
                f.EstiloNarrador = "En pasado, como un cuento, con humor; deja ganchos de anticipaci\u00f3n (\"lo cual seguramente no fue " +
                                   "la mejor idea\") y resume cada d\u00eda en una o dos frases.";
                break;
            case "Aventura por episodios":
                f.Avance = "Parte N";
                f.EstiloNarrador = "En pasado, como un cuento: presenta el objetivo de la parte, los obst\u00e1culos y deja el gancho " +
                                   "para la siguiente.";
                r.NarradorCadaSeg = 90; r.DuracionMin = 12; r.DuracionMax = 16;
                break;
            case TV:
                f.Avance = "Etapa N";
                f.EstiloNarrador = "Como Johnny en Steel Ball Run: primera persona, en pasado; enmarca el inicio y el final del " +
                                   "cap\u00edtulo y une escenas con frases cortas. Deja que los personajes cuenten la historia.";
                r.NarradorCadaSeg = 120; r.RecursosPorMin = 3; r.CortesMin = 14; r.CortesMax = 20; r.MusicaCadaSeg = 45;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 15; r.DuracionMax = 18;
                break;
            case "Retos / minijuegos":
                f.Avance = "Ronda N";
                f.EstiloNarrador = "R\u00e1pido y con energ\u00eda: reglas del reto en una frase, marcador y qui\u00e9n va ganando.";
                r.NarradorCadaSeg = 60; r.RecursosPorMin = 5; r.CortesMin = 18; r.CortesMax = 25; r.MusicaCadaSeg = 30;
                r.DuracionMin = 10; r.DuracionMax = 14;
                break;
            case "Video ensayo":
                f.Avance = "Acto N";
                f.EstiloNarrador = "Primera persona, cercano; plantea una pregunta al inicio y la responde paso a paso, con ejemplos.";
                r.NarradorCadaSeg = 20; r.RecursosPorMin = 6; r.CortesMin = 8; r.CortesMax = 14; r.MusicaCadaSeg = 60;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 12; r.DuracionMax = 20;
                break;
            case "Top / lista":
                f.Avance = "N\u00famero N";
                f.EstiloNarrador = "Directo: presenta cada puesto con un dato que sorprenda; guarda el mejor para el final.";
                r.NarradorCadaSeg = 30; r.RecursosPorMin = 6; r.CortesMin = 12; r.CortesMax = 18; r.MusicaCadaSeg = 45;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 8; r.DuracionMax = 12;
                break;
            case "Podcast":
                f.Avance = "Ninguno";
                f.Narrador = false;
                r.NarradorCadaSeg = 300; r.RecursosPorMin = 1; r.CortesMin = 4; r.CortesMax = 10; r.MusicaCadaSeg = 300;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 30; r.DuracionMax = 60;
                break;
            default:
                f.Avance = "Ninguno";
                break;
        }
        return f;
    }

    // Que busca cada formato (para Gemini).
    public static string Objetivo(string formato)
    {
        switch (formato)
        {
            case "100 d\u00edas": return "sobrevivir y progresar d\u00eda a d\u00eda; cada d\u00eda debe aportar un avance, un problema o una risa, y el video " +
                                    "termina con algo pendiente para el siguiente";
            case "Aventura por episodios": return "avanzar en una historia por partes; cada parte tiene un objetivo, obst\u00e1culos y un final con gancho";
            case TV: return "un cap\u00edtulo de serie de TV al estilo del anime de JoJo: cold open con gancho, opening, actos con " +
                            "carteles de lugar y tiempo, crisis a la mitad, resoluci\u00f3n con ranking y cliffhanger con \u00abcontinuar\u00e1\u00bb";
            case "Retos / minijuegos": return "competir en rondas; se entiende qui\u00e9n va ganando y la tensi\u00f3n sube hasta la \u00faltima ronda";
            case "Video ensayo": return "responder una pregunta o defender una idea con argumentos y ejemplos, en actos claros";
            case "Top / lista": return "recorrer una lista de menor a mayor; cada puesto se justifica y el mejor queda para el final";
            case "Podcast": return "una charla con temas claros; se marcan los cambios de tema y los mejores momentos";
            default: return "";
        }
    }

    public FormatoSerie Copia()
    {
        FormatoSerie f = (FormatoSerie)MemberwiseClone();
        f.Reglas = Reglas.Copia();
        f.Tv = Tv.Copia();
        return f;
    }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = Nombre; d["premisa"] = Premisa; d["avance"] = Avance; d["narrador"] = Narrador;
        d["narradorNombre"] = NarradorNombre; d["estiloNarrador"] = EstiloNarrador; d["aprendido"] = Aprendido;
        Dictionary<string, object> r = new Dictionary<string, object>();
        Reglas.Escribir(r);
        d["ritmo"] = r;
        if (EsTV) d["tv"] = Tv.Escribir();
        return d;
    }

    public static FormatoSerie Leer(object o)
    {
        string nombre = Json.Texto(o, "formato");
        FormatoSerie f = Preset(nombre);
        f.Premisa = Json.Texto(o, "premisa");
        string av = Json.Texto(o, "avance");
        if (Array.IndexOf(Avances, av) >= 0) f.Avance = av;
        object n = Json.Valor(o, "narrador");
        if (n is bool) f.Narrador = (bool)n;
        string nn = Json.Texto(o, "narradorNombre");
        if (nn.Length > 0) f.NarradorNombre = nn;
        if (Json.Valor(o, "estiloNarrador") != null) f.EstiloNarrador = Json.Texto(o, "estiloNarrador");
        f.Aprendido = Json.Texto(o, "aprendido");
        f.Reglas = ReglasRitmo.Leer(Json.Valor(o, "ritmo"), f.Reglas);
        object tv = Json.Valor(o, "tv");
        if (tv != null) f.Tv = PlantillaTV.Leer(tv);
        return f;
    }

    // "D\u00eda 3", "Parte 3"... o "" si no se marca.
    public string Marca(int n) { return Avance == "Ninguno" ? "" : Avance.Replace("N", n.ToString()); }

    public string Texto()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Formato: " + Nombre);
        string obj = Objetivo(Nombre);
        if (obj.Length > 0) sb.Append(" (" + obj + ")");
        sb.Append("\n");
        if (Premisa.Trim().Length > 0) sb.Append("Premisa / objetivo de la serie: " + Premisa.Trim() + "\n");
        if (Avance != "Ninguno") sb.Append("El avance se marca con \"" + Avance + "\" en pantalla.\n");
        sb.Append(Narrador ? "Hay narrador (" + NarradorNombre + ")" + (EstiloNarrador.Trim().Length > 0 ? ": " + EstiloNarrador.Trim() : "") + "\n"
                           : "Sin narrador.\n");
        sb.Append("Duraci\u00f3n objetivo: " + Reglas.DuracionMin + "\u2013" + Reglas.DuracionMax + " min\n");
        return sb.ToString();
    }
}

// No todos los capitulos son iguales: el primero presenta, uno intermedio
// avanza, un especial rompe el formato y el final cierra hilos. Cada papel
// cambia las reglas de ritmo y lo que se le pide a Gemini, para que la serie
// no parezca hecha en fabrica.
public static class PapelEpisodio
{
    // Salen de analizar los primeros capitulos, los finales y los capitulos
    // clave de todas las partes del anime de JoJo (docs/estructura-episodio-sc.md).
    public static readonly string[] Papeles = { "Primer cap\u00edtulo", "Normal", "Inicio de arco", "Cap\u00edtulo clave", "Cap\u00edtulo de respiro",
                                                "Especial", "Pen\u00faltimo (cl\u00edmax)", "Final de temporada", "Final de la serie" };

    public static string Instrucciones(string papel)
    {
        switch (papel)
        {
            case "Primer cap\u00edtulo":
                return "Es el PRIMER cap\u00edtulo. En todas las partes de JoJo el primer episodio NO abre con el opening: empieza mostrando " +
                       "el mundo con calma (la ciudad, la radio del pueblo, el barco con el ata\u00fad), presenta al protagonista con una " +
                       "escena que muestra c\u00f3mo es, tiene una escena de \u00abmentor\u00bb que explica las reglas, revela el poder hacia la mitad y " +
                       "cierra presentando la amenaza o el rival. Aqu\u00ed: presenta la premisa y a cada jugador (qui\u00e9n es, un rasgo) sin prisa " +
                       "(la intro puede durar 2\u20134 min), explica de qu\u00e9 va la serie y termina prometiendo lo que viene. Ritmo pausado al " +
                       "inicio; el opening puede ir despu\u00e9s de la intro o no ir. Es el cap\u00edtulo m\u00e1s importante: el INICIO es CINEMATOGR\u00c1FICO " +
                       "(planos del mundo sin di\u00e1logo, m\u00fasica, el t\u00edtulo de la serie, narraci\u00f3n que sit\u00faa) y nunca se lanza al " +
                       "espectador a una escena sin explicar antes de d\u00f3nde viene.";
            case "Inicio de arco":
                return "Empieza un ARCO nuevo (otra etapa, otro rival, otro objetivo). Como en Golden Wind 20\u201321: arranque tranquilo " +
                       "con el grupo, un giro o traici\u00f3n hacia el 40 %, el re-gancho o eyecatch, un flashback o explicaci\u00f3n de por qu\u00e9 " +
                       "importa, y cierre con una decisi\u00f3n del grupo o el nuevo rival en escena.";
            case "Cap\u00edtulo clave":
                return "Es un cap\u00edtulo CLAVE (una muerte, una revelaci\u00f3n grande, la llegada al destino). En JoJo estos cap\u00edtulos van " +
                       "m\u00e1s LENTOS que la media: menos di\u00e1logo, silencios largos, temas de m\u00fasica largos y m\u00e1s voz interna. Dale aire " +
                       "al momento (respiros largos, ritmo lento alrededor) y ponlo hacia el final como cierre (la muerte de Avdol e " +
                       "Iggy est\u00e1 al 90 %) o a la mitad con sus consecuencias despu\u00e9s.";
            case "Cap\u00edtulo de respiro":
                return "Es un cap\u00edtulo de RESPIRO (humor, vida diaria, exploraci\u00f3n tranquila), como los de Diamond is Unbreakable: " +
                       "sin pelea al inicio, el foco en los personajes y sus man\u00edas, un peque\u00f1o problema que crece y un final feliz o " +
                       "c\u00f3mico. Ritmo ligero, m\u00fasica de comedia y calma.";
            case "Especial":
                return "Es un cap\u00edtulo ESPECIAL: puede romper el formato (otra estructura, otro ritmo, otro tipo de inicio). " +
                       "Que se note desde el inicio qu\u00e9 lo hace distinto; no repitas la f\u00f3rmula de los cap\u00edtulos normales.";
            case "Pen\u00faltimo (cl\u00edmax)":
                return "Es el PEN\u00daLTIMO cap\u00edtulo: abre con el recap del cliffhanger anterior, la tensi\u00f3n est\u00e1 al m\u00e1ximo de principio a " +
                       "fin, puede caer alguien importante y termina en el PEOR momento (\u00abDIO est\u00e1 totalmente sincronizado\u00bb), sin remate " +
                       "c\u00f3mico.";
            case "Final de temporada":
                return "Es el FINAL DE TEMPORADA. En los finales de JoJo el cl\u00edmax se resuelve entre el 40 y el 75 % y despu\u00e9s viene " +
                       "un EP\u00cdLOGO largo (25\u201350 %): despedidas, la broma de siempre del grupo, \u00abla vida sigue\u00bb, con m\u00fasica tranquila y " +
                       "el ending sonando sobre el ep\u00edlogo; a veces sin opening. Paga los hilos abiertos de la temporada y termina con " +
                       "un nuevo estado de cosas y un gancho peque\u00f1o para la pr\u00f3xima.";
            case "Final de la serie":
                return "Es el FINAL DE LA SERIE. Como los finales de JoJo: el cl\u00edmax se resuelve entre el 40 y el 75 % y queda un " +
                       "EP\u00cdLOGO largo con despedidas, recuerdos de cap\u00edtulos anteriores, la broma de siempre del grupo y el ending " +
                       "sobre el ep\u00edlogo; puede no tener opening. Cierra todos los hilos; no anuncies un pr\u00f3ximo episodio.";
            default:
                return "Es un cap\u00edtulo intermedio: recuerda en pocos segundos d\u00f3nde qued\u00f3 la historia (si el anterior termin\u00f3 en " +
                       "cliffhanger, el cold open es su recap), avanza con algo nuevo (un logro, un problema, alguien nuevo) y termina " +
                       "con un pendiente para el siguiente. Var\u00eda el inicio y los recursos respecto a los cap\u00edtulos anteriores.";
        }
    }

    // Reglas ajustadas al papel.
    public static ReglasRitmo Reglas(ReglasRitmo r, string papel)
    {
        ReglasRitmo x = r.Copia();
        switch (papel)
        {
            case "Primer cap\u00edtulo":
                x.ZonaCriticaSeg = Math.Max(x.ZonaCriticaSeg, 180);
                x.NarradorCadaSeg = Math.Max(30, (int)(x.NarradorCadaSeg * 0.8));
                x.DuracionMax = Math.Round(x.DuracionMax * 1.3);      // SBR estreno con un episodio doble
                break;
            case "Cap\u00edtulo clave":
                x.CortesMin = Math.Max(4, (int)(x.CortesMin * 0.75));
                x.CortesMax = Math.Max(x.CortesMin + 2, (int)(x.CortesMax * 0.8));
                x.MusicaCadaSeg = (int)(x.MusicaCadaSeg * 2);       // temas largos (E46 de SC: mediana de 2 min)
                break;
            case "Cap\u00edtulo de respiro":
                x.MusicaCadaSeg = (int)(x.MusicaCadaSeg * 1.2);
                break;
            case "Especial":
                x.DuracionMin = Math.Max(1, Math.Round(x.DuracionMin * 0.7));
                x.DuracionMax = Math.Round(x.DuracionMax * 1.4);
                break;
            case "Pen\u00faltimo (cl\u00edmax)":
                x.CortesMax = (int)(x.CortesMax * 1.15);
                break;
            case "Final de temporada":
            case "Final de la serie":
                x.DuracionMax = Math.Round(x.DuracionMax * 1.4);
                x.MusicaCadaSeg = (int)(x.MusicaCadaSeg * 1.3);   // temas mas largos en el climax y el epilogo
                break;
        }
        return x;
    }
}

// Lo que cada proyecto recuerda de su serie (<proyecto>.vegascut-serie.json):
// que serie usa y que capitulos no quiere de contexto.
public static class AjustesProyecto
{
    static string Ruta(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-proyecto-serie.json");
    }

    static object Leer(string veg)
    {
        try { return File.Exists(Ruta(veg)) ? Json.Leer(File.ReadAllText(Ruta(veg), Encoding.UTF8)) : null; } catch { return null; }
    }

    public static string Serie(string veg) { return String.IsNullOrEmpty(veg) ? "" : Json.Texto(Leer(veg), "serie"); }

    public static List<string> Excluidos(string veg)
    {
        List<string> r = new List<string>();
        if (String.IsNullOrEmpty(veg)) return r;
        foreach (object x in Json.Lista(Leer(veg), "excluidos")) if (x is string) r.Add((string)x);
        return r;
    }

    public static void Guardar(string veg, string serie, List<CapSerie> caps)
    {
        if (String.IsNullOrEmpty(veg)) return;
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["serie"] = serie ?? "";
        List<object> ex = new List<object>();
        if (caps != null) foreach (CapSerie c in caps) if (!c.Elegido && c.Relacion != 0) ex.Add(c.Nombre);
        d["excluidos"] = ex;
        try { File.WriteAllText(Ruta(veg), Json.Escribir(d), new UTF8Encoding(false)); } catch { }
    }
}

public static class Serie
{
    public static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    static readonly Regex Patron = new Regex(@"S(\d{1,2})\s*[-_ ]?\s*E(\d{1,3})", RegexOptions.IgnoreCase);
    static readonly Regex Parte = new Regex(@"\b(?:parte|part|cap(?:itulo|\u00edtulo)?|ep(?:isodio)?|episode)\s*[-_ ]?\s*(\d{1,3})", RegexOptions.IgnoreCase);

    // "S01E02 SCR" -> temporada 1, capitulo 2, serie "# scr". Tambien
    // "Parte 3", "Cap 3" o "Ep 3" (temporada 1).
    public static bool Clave(string nombre, out int temporada, out int numero, out string serie)
    {
        temporada = 0; numero = 0; serie = "";
        nombre = Regex.Replace(nombre ?? "", @"\s+(BASE|CAP|MOM( \d+)?)$", "", RegexOptions.IgnoreCase);
        Match m = Patron.Match(nombre);
        if (m.Success) { temporada = int.Parse(m.Groups[1].Value); numero = int.Parse(m.Groups[2].Value); }
        else
        {
            m = Parte.Match(nombre);
            if (!m.Success) return false;
            temporada = 1; numero = int.Parse(m.Groups[1].Value);
        }
        string resto = (nombre.Substring(0, m.Index) + "#" + nombre.Substring(m.Index + m.Length)).ToLowerInvariant();
        serie = Regex.Replace(resto, @"[\s_\-\.]+", " ").Trim();
        return true;
    }

    // Para ordenar capitulos: temporada y numero (los que no tienen, al final).
    public static int Orden(string nombre)
    {
        int t, n;
        string s;
        return Clave(nombre, out t, out n, out s) ? t * 1000 + n : int.MaxValue;
    }

    // .veg de la carpeta y de sus subcarpetas hasta "niveles" de hondo.
    public static List<string> ArchivosVeg(string carpeta, int niveles)
    {
        List<string> l = new List<string>();
        Agregar(l, carpeta, niveles);
        return l;
    }

    static void Agregar(List<string> l, string carpeta, int niveles)
    {
        try
        {
            if (String.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta) || l.Count > 5000) return;
            foreach (string f in Directory.GetFiles(carpeta, "*.veg"))
                if (Path.GetExtension(f).Equals(".veg", StringComparison.OrdinalIgnoreCase) && !l.Contains(f)) l.Add(f);
            if (niveles > 0)
                foreach (string d in Directory.GetDirectories(carpeta)) Agregar(l, d, niveles - 1);
        }
        catch { }
    }

    // ------------------------------------------------------ fichas

    static string QueGuardar(string tipo)
    {
        switch (tipo)
        {
            case "Video ensayo":
                return "\"hilos\": [\"temas, preguntas o argumentos que quedan abiertos o que se retoman en otras partes\"],\n" +
                       " \"recurrentes\": [\"conceptos, ejemplos, personajes o frases que se repiten\"],\n";
            case "Podcast":
                return "\"hilos\": [\"temas pendientes, promesas, debates que siguen en otros episodios\"],\n" +
                       " \"recurrentes\": [\"secciones, chistes internos o frases que se repiten\"],\n";
            default:
                return "\"hilos\": [\"objetivos, promesas, conflictos, rivalidades, objetos o lugares que pueden volver a aparecer\"],\n" +
                       " \"recurrentes\": [\"chistes, apodos o frases que se repiten\"],\n";
        }
    }

    public static string QueEs(string tipo)
    {
        switch (tipo)
        {
            case "Video ensayo": return "una serie de video ensayos en espa\u00f1ol (varias partes)";
            case "Podcast": return "un podcast o serie de charlas en espa\u00f1ol";
            case "Gameplay": return "una serie de gameplays en espa\u00f1ol (con amigos)";
            default: return "una serie de videos de YouTube en espa\u00f1ol";
        }
    }

    public static string InstruccionesFicha() { return InstruccionesFicha("Gameplay"); }

    public static string InstruccionesFicha(string tipo)
    {
        return "Eres editor de " + QueEs(tipo) + ". Recibes la transcripci\u00f3n de lo que qued\u00f3 en un cap\u00edtulo. " +
               "Haz su ficha para usarla de contexto al editar los otros cap\u00edtulos.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"qu\u00e9 pasa en el cap\u00edtulo, en orden, en 3 a 6 frases\",\n " + QueGuardar(tipo) +
 " \"estructura\": \"c\u00f3mo abre (tipo de gancho), c\u00f3mo avanza y c\u00f3mo cierra el cap\u00edtulo, en una frase\",\n" +
               " \"frases\": [{\"inicio\": s, \"fin\": s, \"quien\": \"nombre\", \"texto\": \"lo que se dice\", \"por\": \"por qu\u00e9 es clave\"}]}\n\n" +
               "Reglas:\n- \"frases\": de 5 a 15 frases cortas (2 a 7 s) que mejor cuentan lo importante del cap\u00edtulo; " +
               "sirven para un \"anteriormente\". Usa solo tiempos de la transcripci\u00f3n.\n" +
               "- Nada de datos personales ni charla t\u00e9cnica. Escribe en espa\u00f1ol natural, con los nombres de las personas.";
    }

    public static string MensajeFicha(Episodio e, string notas)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Cap\u00edtulo: " + e.Nombre + "\n");
        if (!String.IsNullOrEmpty(notas)) sb.Append("\nNOTAS DE LA SERIE:\n" + notas.Trim() + "\n");
        sb.Append("\nTranscripci\u00f3n [inicio-fin] persona: texto\n" + e.Transcrito());
        return sb.ToString();
    }

    // Contexto para MomentosIA: la serie, sus notas, anteriores y posteriores.
    public static string Contexto(SerieProyecto serie, List<CapSerie> caps)
    {
        StringBuilder sb = new StringBuilder();
        if (serie != null)
        {
            sb.Append("Serie: " + serie.Nombre + " (" + serie.Tipo + ")\n");
            sb.Append(serie.Formato.Texto());
            if (!String.IsNullOrEmpty(serie.Notas)) sb.Append("Notas de la serie:\n" + serie.Notas.Trim() + "\n");
            foreach (CapSerie c in caps)
            {
                if (c.Relacion != 0) continue;
                sb.Append("Este cap\u00edtulo (" + c.Posicion + " de " + caps.Count + "): " + c.Papel + ". " + PapelEpisodio.Instrucciones(c.Papel) + "\n");
                if (c.Posicion >= 2 && c.Posicion <= 3)
                    sb.Append("Es de los primeros cap\u00edtulos: el inicio todav\u00eda es especial y cuidado (m\u00e1s cinematogr\u00e1fico), recuerda " +
                              "qui\u00e9n es qui\u00e9n y sigue presentando el mundo.\n");
                string nota = serie.NotaEpisodio(c.Veg);
                if (nota.Length > 0) sb.Append("Nota del editor para este cap\u00edtulo: " + nota + "\n");
                sb.Append(Anterior(serie, caps, c));
            }
        }
        foreach (int rel in new int[] { -1, 1 })
        {
            bool titulo = false;
            foreach (CapSerie c in caps)
            {
                if (c.Relacion != rel || !c.Elegido) continue;
                Ficha f = Ficha.Cargar(c.Veg);
                if (f == null) continue;
                if (!titulo)
                {
                    sb.Append(rel < 0 ? "\nCap\u00edtulos anteriores:\n"
                                      : "\nCap\u00edtulos POSTERIORES (ya grabados): si algo de este cap\u00edtulo prepara lo que se retoma " +
                                        "despu\u00e9s, cons\u00e9rvalo aunque aqu\u00ed parezca menor; no adelantes lo que pasa despu\u00e9s ni uses sus lugares o hechos en " +
                                        "este cap\u00edtulo.\n");
                    titulo = true;
                }
                RegistroCapitulo rc = serie != null ? serie.Producido(c.Veg) : null;
                sb.Append("- " + c.Nombre + (c.Papel != "Normal" ? " (" + c.Papel + ")" : "") + ": " +
                          (rc != null ? "[" + rc.Texto() + "] " : "") + f.Texto(false));
            }
        }
        return sb.ToString();
    }

    // El capitulo inmediatamente anterior (como cerro, que tipo fue) y los
    // tipos de los ultimos, para que este siga bien y no repita la formula.
    static string Anterior(SerieProyecto serie, List<CapSerie> caps, CapSerie actual)
    {
        StringBuilder sb = new StringBuilder();
        CapSerie ant = caps.Find(delegate (CapSerie x) { return x.Posicion == actual.Posicion - 1; });
        if (ant == null)
        {
            if (actual.Posicion == 1) sb.Append("No hay cap\u00edtulo anterior: es el primero de la serie.\n");
            return sb.ToString();
        }
        RegistroCapitulo rc = serie.Producido(ant.Veg);
        Ficha f = Ficha.Cargar(ant.Veg);
        sb.Append("CAP\u00cdTULO ANTERIOR (" + ant.Posicion + ", " + ant.Nombre + (ant.Papel != "Normal" ? ", " + ant.Papel : "") + "): ");
        if (rc != null) sb.Append(rc.Texto() + ". ");
        if (f != null) sb.Append(f.Resumen.Trim().Replace("\n", " ") + (f.Estructura.Length > 0 ? " Estructura: " + f.Estructura.Trim().Replace("\n", " ") : "") + " ");
        if (rc == null && f == null) sb.Append("(sin ficha ni producci\u00f3n guardada) ");
        sb.Append("\nSi el anterior qued\u00f3 a medias o en cliffhanger, este abre con su recap y lo retoma (cierra o sigue el enfrentamiento); " +
                  "si cerr\u00f3 anunciando algo, este lo cumple.\n");
        List<string> tipos = new List<string>();
        foreach (CapSerie c in caps)
        {
            if (c.Posicion >= actual.Posicion || c.Posicion < actual.Posicion - 4) continue;
            RegistroCapitulo r = serie.Producido(c.Veg);
            if (r != null && r.Tipo.Length > 0) tipos.Add(c.Posicion + ": " + TiposCapitulo.Nombre(r.Tipo));
        }
        if (tipos.Count > 0)
            sb.Append("Tipos de los \u00faltimos cap\u00edtulos: " + String.Join(", ", tipos.ToArray()) + ". Var\u00eda: no repitas el tipo de los dos " +
                      "anteriores salvo que el material o el editor lo pidan (o sea la continuaci\u00f3n de un arco).\n");
        return sb.ToString();
    }

    // La serie del proyecto con sus capitulos y lo que el proyecto excluyo.
    public static List<CapSerie> DelProyecto(string veg, out SerieProyecto serie)
    {
        serie = SerieProyecto.DelProyecto(veg);
        if (serie == null) return new List<CapSerie>();
        List<CapSerie> caps = serie.Capitulos(veg);
        List<string> ex = AjustesProyecto.Excluidos(veg);
        foreach (CapSerie c in caps) if (ex.Contains(c.Nombre)) c.Elegido = false;
        return caps;
    }
}

// ---- src/comun/TiposCapitulo.cs ----

// =====================================================================
// Tipos de capitulo: la formula con la que esta armado un capitulo (un
// juego, un misterio, una persecucion, el capitulo de un personaje...),
// aparte de su papel en la temporada (primero, clave, final...).
//
// Salen de revisar las escenas de los ~190 episodios del anime de JoJo
// (PB/BT, SC, DU, GW, SO y SBR) en jojowiki; docs/tipos-de-capitulo.md
// tiene la tabla con los ejemplos.
// =====================================================================

public class TipoCapitulo
{
    public string Clave, Nombre, Estructura, Senales, EnSerie, Ejemplos;

    public TipoCapitulo(string clave, string nombre, string estructura, string senales, string enSerie, string ejemplos)
    {
        Clave = clave; Nombre = nombre; Estructura = estructura; Senales = senales; EnSerie = enSerie; Ejemplos = ejemplos;
    }
}

// Lo que se recuerda de un capitulo ya producido, para los siguientes.
public class RegistroCapitulo
{
    public string Tipo = "", Forma = "", Titulo = "", Cierre = "";

    public bool Vacio { get { return Tipo.Length == 0 && Titulo.Length == 0 && Cierre.Length == 0; } }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["tipo"] = Tipo; d["forma"] = Forma; d["titulo"] = Titulo; d["cierre"] = Cierre;
        return d;
    }

    public static RegistroCapitulo Leer(object o)
    {
        if (o == null) return null;
        RegistroCapitulo r = new RegistroCapitulo();
        r.Tipo = TiposCapitulo.Normalizar(Json.Texto(o, "tipo"));
        r.Forma = Json.Texto(o, "forma"); r.Titulo = Json.Texto(o, "titulo"); r.Cierre = Json.Texto(o, "cierre");
        return r.Vacio ? null : r;
    }

    public string Texto()
    {
        TipoCapitulo t = TiposCapitulo.Buscar(Tipo);
        List<string> l = new List<string>();
        if (Titulo.Length > 0) l.Add("\u00ab" + Titulo + "\u00bb");
        if (t != null) l.Add("tipo: " + t.Nombre);
        if (Forma.Length > 0 && Forma != "normal") l.Add(Forma.Replace("_", " "));
        if (Cierre.Length > 0) l.Add("cerr\u00f3 con: " + Cierre);
        return String.Join("; ", l.ToArray());
    }
}

public static class TiposCapitulo
{
    public const string Detectar = "Que lo detecte la IA";

    public static readonly TipoCapitulo[] Todos = {
        new TipoCapitulo("estreno", "Estreno (primer cap\u00edtulo)",
            "inicio CINEMATOGR\u00c1FICO y sin opening al principio: el mundo con calma (planos del lugar sin di\u00e1logo, m\u00fasica, el t\u00edtulo " +
            "de la serie, la voz del narrador o la radio del pueblo) \u2192 el protagonista en una escena que muestra c\u00f3mo es, sin " +
            "explicarlo con palabras \u2192 cada personaje con su momento y su tarjeta de presentaci\u00f3n \u2192 el \u00abmentor\u00bb explica las reglas " +
            "del mundo \u2192 la primera prueba o el poder (~50 %) \u2192 la amenaza o el reto grande se asoma al final. El opening va " +
            "despu\u00e9s de la intro o al final. Nunca lanza al espectador a una escena sin contexto.",
            "es el cap\u00edtulo 1 o el material presenta el mundo, a los personajes y las reglas.",
            "la llegada al mundo, cada jugador presentado, las reglas del modpack o de la carrera, la primera prueba y el peligro que viene.",
            "PB 1 Dio the Invader, BT 10 JoJo of New York, SC 1, DU 1 (\u00abMorioh-cho RADIO\u00bb), GW 1 (la ciudad), SO 1, SBR 1 (doble duraci\u00f3n, 47 min)."),
        new TipoCapitulo("rival", "Rival de la semana",
            "llegada o viaje con humor (0\u201315 %) \u2192 algo raro, sin explicarlo (~13 %) \u2192 se revela qu\u00e9 es (~35 %) \u2192 crisis (~45 %) \u2192 " +
            "giro: el truco o la ayuda (~58 %) \u2192 derrota (~77 %) \u2192 remate c\u00f3mico \u2192 gancho al siguiente.",
            "un solo problema (mob, jugador, trampa, jefe) que aparece y se resuelve dentro del material.",
            "un mob fuerte, un jefe, un jugador rival o una trampa de la etapa.",
            "SC 4 Tower of Gray, SC 7 Strength, SC 8 Devil, SC 13 Wheel of Fortune, GW 24\u201325 Notorious B.I.G."),
        new TipoCapitulo("arco_abre", "Abre un enfrentamiento largo (parte 1)",
            "llegada y humor largos (0\u201325 %) \u2192 el problema aparece tarde (~30 %) \u2192 todo empeora sin pausa \u2192 termina en el PEOR " +
            "momento (98 %), sin remate, con \u00abcontinuar\u00e1\u00bb.",
            "el material termina a mitad de algo (la pelea o el reto no se resuelve) o el problema da para m\u00e1s de un cap\u00edtulo.",
            "una grabaci\u00f3n que corta a mitad del reto o de la pelea.",
            "SC 10 Emperor and Hanged Man 1 (muere Avdol), SC 38 Pet Shop 1, DU 28 Highway Star 1, GW 15 Grateful Dead 1."),
        new TipoCapitulo("arco_medio", "Parte del medio de un arco",
            "recap corto del cliffhanger \u2192 la pelea sigue \u2192 cambio de foco a otro grupo u otro hilo \u2192 un logro a medias y un nuevo " +
            "golpe \u2192 termina en otro cliffhanger.",
            "el cap\u00edtulo anterior qued\u00f3 a medias y este material tampoco lo cierra.",
            "la segunda sesi\u00f3n de un reto largo (un jefe, una construcci\u00f3n, una carrera de varias etapas).",
            "SC 43 Vanilla Ice 2, DU 4 Nijimura 2, DU 32\u201333 15 de julio 2\u20133, GW 31 Green Day 2."),
        new TipoCapitulo("arco_cierra", "Cierra un enfrentamiento (parte 2)",
            "cold open con el recap del cliffhanger \u2192 la crisis sigue \u2192 giro (50\u201360 %) \u2192 derrota (65\u201390 %) \u2192 remate \u2192 anuncio de " +
            "lo siguiente (98 %).",
            "el material retoma algo que qued\u00f3 pendiente en el cap\u00edtulo anterior y lo resuelve.",
            "la sesi\u00f3n que termina lo que qued\u00f3 a medias.",
            "SC 11, SC 39 Pet Shop 2, DU 9 Yukako 2, DU 29 Highway Star 2, GW 16 Grateful Dead 2."),
        new TipoCapitulo("juego", "Juego o apuesta",
            "el reto y lo que est\u00e1 en juego (0\u201320 %) \u2192 las reglas explicadas (con texto en pantalla) \u2192 la primera ronda la pierde " +
            "el protagonista \u2192 trampas del rival \u2192 el protagonista apuesta todo o hace un farol \u2192 se revela su trampa (80\u201390 %) \u2192 " +
            "el perdedor humillado. Ritmo lento en las apuestas: silencios, caras, tensi\u00f3n.",
            "reglas, apuestas, marcador, rondas, \u00abel que pierda\u2026\u00bb, minijuegos, PvP con reglas, tratos.",
            "minijuegos, apuestas entre amigos, PvP con reglas, parkour, carrera de recolecci\u00f3n, tradeos.",
            "SC 34\u201335 D'Arby (p\u00f3ker), SC 40\u201342 D'Arby el jugador, SC 27 Oingo Boingo, DU 26 piedra, papel o tijera, DU 27 dados, SO 9 Marilyn Manson."),
        new TipoCapitulo("comedia", "Comedia o vida diaria",
            "situaci\u00f3n cotidiana (0\u201315 %) \u2192 algo raro pero peque\u00f1o \u2192 malentendido que crece \u2192 las man\u00edas de un personaje en el " +
            "centro \u2192 cl\u00edmax absurdo \u2192 se aclara todo y final feliz o un chiste. Sin peligro real; m\u00fasica de comedia y calma.",
            "mucha risa, bromas, nada en juego, un problema peque\u00f1o (una mascota, una casa, un bug, alguien perdido).",
            "construir una casa, una mascota, un aldeano, un bug, el que se pierde o se cae.",
            "DU 10 el restaurante de Tonio, DU 13 el beb\u00e9 invisible, DU 20 Cinderella, DU 27 Mikitaka, SC 31 Mariah 2."),
        new TipoCapitulo("foco", "El cap\u00edtulo de un personaje",
            "cold open con su pasado o un rasgo suyo \u2192 se queda solo con el problema \u2192 recuerda por qu\u00e9 es as\u00ed (flashback, 30\u201345 %) " +
            "\u2192 lo resuelve a su manera \u2192 los dem\u00e1s lo reconocen. Su tema musical en el momento clave.",
            "un jugador lleva casi todo el material o tiene su gran momento.",
            "el cap\u00edtulo de un jugador: su reto, su base, su venganza.",
            "GW 6 Abbacchio, GW 8 Mista, GW 11 Narancia, GW 25 Trish, SO 6 Ermes, DU 6 Koichi, DU 17 Rohan, SC 38\u201339 Iggy."),
        new TipoCapitulo("villano", "Del lado del rival",
            "abre en la vida del rival (su rutina, sus man\u00edas, 0\u201320 %) \u2192 c\u00f3mo ve a los h\u00e9roes \u2192 los h\u00e9roes casi lo descubren \u2192 " +
            "cierre inquietante: se escapa o gana esta vez.",
            "material de otro jugador o del equipo contrario, una traici\u00f3n, alguien que trama algo.",
            "el equipo rival, el amigo que traiciona, \u00abmientras tanto\u00bb del otro lado.",
            "DU 21 Kira solo quiere vivir tranquilo, DU 30 Cats Love Kira, GW 10 el equipo de sicarios, GW 26\u201327 Doppio, SC 36 Hol Horse."),
        new TipoCapitulo("misterio", "Misterio o investigaci\u00f3n",
            "algo no cuadra (0\u201310 %) \u2192 investigan con pistas (10\u201345 %) \u2192 sospecha falsa \u2192 la revelaci\u00f3n (50\u201360 %: \u00abson todos el " +
            "enemigo\u00bb) \u2192 pelean con lo que ya entienden \u2192 la explicaci\u00f3n final. Ritmo lento, silencios, m\u00fasica de misterio.",
            "buscar algo, preguntas sin respuesta, \u00ab\u00bfqui\u00e9n fue?\u00bb, ruidos, una estructura o base desconocida.",
            "buscar una estructura, una base abandonada, \u00ab\u00bfqui\u00e9n rob\u00f3 el cofre?\u00bb.",
            "SC 7 el barco vac\u00edo, SC 14 Justice (niebla y cad\u00e1ver), DU 16 la caza de ratas, DU 17 el callej\u00f3n, SO 7 \u00abhay una de m\u00e1s\u00bb."),
        new TipoCapitulo("persecucion", "Persecuci\u00f3n o carrera",
            "la salida o alguien huye \u2192 choque u obst\u00e1culo \u2192 el perseguidor gana terreno \u2192 escondite breve (respiro) \u2192 truco con " +
            "el terreno \u2192 final al l\u00edmite. R\u00e1pido, con pausas cortas.",
            "correr, huir, carreras, cron\u00f3metro, \u00ab\u00a1corre!\u00bb, viajes con prisa.",
            "carrera de etapa, escapar de un mob o de la noche, ir a por alguien.",
            "SC 13 Wheel of Fortune, BT 19 carrera al precipicio, DU 28\u201329 Highway Star, GW 19 White Album, SBR 1\u20133."),
        new TipoCapitulo("entrenamiento", "Entrenamiento o prueba",
            "el mentor o el reto plantea algo imposible (0\u201315 %) \u2192 intentos fallidos con humor \u2192 entienden el truco (~55 %) \u2192 lo " +
            "superan al l\u00edmite \u2192 reconocimiento y algo nuevo (poder, equipo, permiso).",
            "aprender una mec\u00e1nica, practicar, farmear, preparar equipo, \u00aba ver si puedes\u00bb.",
            "aprender una mec\u00e1nica, farmear, prepararse para un jefe.",
            "BT 4 Zeppeli, BT 16 Lisa Lisa (el pilar), GW 3 el examen de Polpo, SC 41 Jotaro aprende a jugar."),
        new TipoCapitulo("mision", "Misi\u00f3n u operaci\u00f3n",
            "la orden o el objetivo con mapa o itinerario en pantalla (0\u201315 %) \u2192 el plan \u2192 el plan se tuerce (~40 %) \u2192 improvisan \u2192 " +
            "lo logran a medias o con un costo \u2192 la siguiente orden.",
            "un objetivo claro (ir a, conseguir, robar, escoltar), un plan hablado.",
            "ir al Nether, conseguir un objeto, matar al drag\u00f3n, saquear una estructura.",
            "GW 5 la fortuna de Polpo, GW 9 la primera orden, GW 14 el tren a Florencia, SO 10\u201311 Operaci\u00f3n Savage Garden, SO 24 la fuga."),
        new TipoCapitulo("duelo", "Duelo uno a uno",
            "el reto y las reglas de honor (0\u201315 %) \u2192 respeto mutuo \u2192 intercambio de golpes, cada uno con su truco \u2192 el rival casi " +
            "gana \u2192 el \u00faltimo truco \u2192 respeto al vencido.",
            "dos jugadores frente a frente, PvP, una competencia directa.",
            "PvP 1 contra 1 entre amigos, la final de un torneo.",
            "BT 21\u201323 la carrera de cuadrigas con Wamuu, GW 2 Giorno contra Bucciarati, DU 15 Josuke contra Rohan, SC 46\u201348 DIO."),
        new TipoCapitulo("pasado", "Flashback u origen",
            "abre en el pasado (otra m\u00fasica, otro color) \u2192 alterna pasado y presente \u2192 el pasado explica una decisi\u00f3n de ahora \u2192 " +
            "vuelve al presente con esa decisi\u00f3n.",
            "se habla mucho de algo que pas\u00f3 antes; hay clips viejos o recuerdos.",
            "recuerdos de cap\u00edtulos o temporadas anteriores, la historia de una base o de una pelea vieja.",
            "GW 26 Doppio, GW 20 el pasado de Bucciarati, BT 20 Caesar, BT 24 Elizabeth, SO 31 Heavy Weather 2."),
        new TipoCapitulo("despedida", "Muerte o despedida",
            "inicio c\u00e1lido con quien va a caer (presagio) \u2192 el peligro \u2192 el sacrificio o la ca\u00edda (hasta el 90 %) \u2192 silencio \u2192 " +
            "reacci\u00f3n del grupo \u2192 su \u00faltima frase o la pista que deja. Tema triste y largo.",
            "una muerte en hardcore, alguien que se va de la serie, perder la base o una mascota.",
            "muerte en hardcore, un amigo que deja la serie, perder algo querido.",
            "BT 20 Caesar, SC 10 Avdol, SC 43 Iggy, SC 46 Kakyoin, DU 22 Shigechi, GW 28 Abbacchio, SO 22 F.F."),
        new TipoCapitulo("revelacion", "Revelaci\u00f3n o traici\u00f3n",
            "arranque normal con pistas sembradas \u2192 el giro (40\u201360 % o al final) \u2192 repaso r\u00e1pido de las pistas \u2192 el grupo decide " +
            "(\u00ab\u00bfqui\u00e9n viene conmigo?\u00bb) \u2192 cierre con el nuevo estado de cosas.",
            "un secreto, una traici\u00f3n, algo que cambia lo que se sab\u00eda.",
            "el aliado que traiciona, el secreto de un jugador, la regla oculta del reto.",
            "GW 20\u201321 el jefe traiciona, BT 23 Lisa Lisa es su madre, SC 22 Avdol vive, DU 35 Bites the Dust."),
        new TipoCapitulo("separados", "El grupo separado",
            "el grupo se separa (0\u201315 %) \u2192 se intercalan 2\u20133 historias cortando en los momentos de tensi\u00f3n (cada 1\u20133 min) con " +
            "carteles de lugar u hora \u2192 las historias se juntan al final (o no: cliffhanger).",
            "varios jugadores haciendo cosas distintas a la vez, en lugares distintos.",
            "cada amigo con su propia aventura (sus pistas de grabaci\u00f3n) el mismo d\u00eda.",
            "DU 31\u201334 15 de julio (jueves), SC 32\u201333 Alessi, GW 12\u201313 Pompeya, SO 25\u201326 Bohemian Rhapsody."),
        new TipoCapitulo("encierro", "Encierro o regla rara",
            "quedan atrapados con una regla rara (0\u201320 %) \u2192 la regla en pantalla \u2192 fallan por la regla \u2192 la entienden (50\u201360 %) \u2192 " +
            "la usan contra el problema \u2192 salen.",
            "atrapados, sin salida, sin comida, un bug, un reto con restricci\u00f3n.",
            "atrapados en una cueva o en el End, un reto con una restricci\u00f3n.",
            "SC 8 Devil (la habitaci\u00f3n), SC 19\u201320 Death 13 (el sue\u00f1o), SC 23\u201324 el submarino, GW 12\u201313 el espejo, DU 35\u201336 el bucle."),
        new TipoCapitulo("reclutamiento", "Alguien se une",
            "el nuevo aparece como rival o problema \u2192 pelea o prueba \u2192 se descubre por qu\u00e9 era as\u00ed \u2192 se une \u2192 presentaci\u00f3n (tarjeta " +
            "con su nombre) y chiste de bienvenida.",
            "un jugador nuevo, un aliado, una mascota que se queda.",
            "un amigo nuevo que entra a la serie.",
            "SC 5 Polnareff, SC 25 Iggy, DU 3\u20135 Okuyasu, GW 4\u20135 Giorno entra a la banda, SO 8 F.F., SO 15 Anasui."),
        new TipoCapitulo("poder", "Despertar o mejora",
            "el problema supera al grupo (0\u201340 %) \u2192 desesperaci\u00f3n \u2192 tocan fondo \u2192 despierta el poder o llega la mejora (60\u201375 %) con " +
            "su tarjeta de stats y su tema \u2192 lo usa para ganar.",
            "conseguir algo clave (netherite, un encantamiento, el elytra) despu\u00e9s de pasarla mal.",
            "la primera armadura buena, un encantamiento, el elytra, un beacon.",
            "DU 9 Echoes ACT2, DU 23 ACT3, GW 25 Spice Girl, GW 37 Requiem, SC 48 Jotaro detiene el tiempo."),
        new TipoCapitulo("transicion", "Puente o viaje",
            "consecuencias de lo anterior \u2192 recap del viaje con mapa \u2192 objetivo nuevo \u2192 viaje y llegada \u2192 primer vistazo del " +
            "arco nuevo al final.",
            "cambiar de lugar o de etapa, preparar el viaje, mudarse, una dimensi\u00f3n nueva.",
            "cambio de etapa, mudanza, el primer viaje al Nether o al End.",
            "SC 3 la partida, SC 24 por fin Egipto, SC 39 la mansi\u00f3n de DIO, GW 29 destino Roma, BT 10 la nueva generaci\u00f3n."),
        new TipoCapitulo("epilogo", "Ep\u00edlogo",
            "cap\u00edtulo tranquilo despu\u00e9s de la gran batalla: consecuencias \u2192 despedidas \u2192 la broma de siempre \u2192 una historia corta " +
            "aparte \u2192 \u00abla vida sigue\u00bb.",
            "material tranquilo despu\u00e9s de algo grande, recuento, charla.",
            "despu\u00e9s del jefe, el recuento de la temporada.",
            "SC 48 (segunda mitad), DU 39, GW 39 Sleeping Slaves, BT 26, SO 38."),
    };

    public static TipoCapitulo Buscar(string x)
    {
        if (String.IsNullOrEmpty(x)) return null;
        foreach (TipoCapitulo t in Todos)
            if (String.Equals(t.Clave, x, StringComparison.OrdinalIgnoreCase) || String.Equals(t.Nombre, x, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    // Clave del tipo ("" si no es ninguno).
    public static string Normalizar(string x)
    {
        TipoCapitulo t = Buscar((x ?? "").Trim());
        return t != null ? t.Clave : "";
    }

    public static string Nombre(string clave)
    {
        TipoCapitulo t = Buscar(clave);
        return t != null ? t.Nombre : "";
    }

    // Para el combo: el primero es "que lo detecte".
    public static string[] Opciones()
    {
        List<string> l = new List<string>();
        l.Add(Detectar);
        foreach (TipoCapitulo t in Todos) l.Add(t.Nombre);
        return l.ToArray();
    }

    // Como se mezclan a lo largo de una temporada (SC, DU, GW).
    public const string Mezcla =
        "C\u00f3mo se mezclan en una temporada: los primeros cap\u00edtulos presentan y reclutan (GW 1\u201311: casi todos son el cap\u00edtulo de " +
        "un personaje que se une); luego la columna es el rival de la semana o la misi\u00f3n, con enfrentamientos de dos partes cuando " +
        "el material no se resuelve; un respiro de comedia cada 4\u20136 cap\u00edtulos (DU los intercala entre los arcos serios); el villano " +
        "tiene su propio cap\u00edtulo hacia los 2/3 (DU 21, GW 26); los juegos se agrupan cerca del final (SC 27\u201341); un puente a mitad " +
        "de temporada (SC 24, por fin Egipto); las muertes y revelaciones se concentran en el \u00faltimo tercio. No m\u00e1s de dos " +
        "seguidos del mismo tipo, salvo las partes de un mismo enfrentamiento.\n";

    // Todos, para que Gemini elija.
    public static string Catalogo()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("TIPOS DE CAP\u00cdTULO (c\u00f3mo var\u00eda el anime de JoJo; usa la clave):\n");
        foreach (TipoCapitulo t in Todos)
            sb.Append("- " + t.Clave + " (" + t.Nombre + "): " + t.Estructura + " Se nota en: " + t.Senales + " En la serie: " + t.EnSerie + "\n");
        sb.Append(Mezcla);
        return sb.ToString();
    }

    // El elegido, completo, para la escaleta final.
    public static string Instrucciones(string clave)
    {
        TipoCapitulo t = Buscar(clave);
        if (t == null) return "";
        return "TIPO DE CAP\u00cdTULO: " + t.Nombre + ". C\u00f3mo lo arma JoJo: " + t.Estructura + " En la serie: " + t.EnSerie +
               " (ejemplos: " + t.Ejemplos + "). \u00dasalo como gu\u00eda; las notas del editor mandan.\n";
    }
}

// ---- src/comun/VentanaSerie.cs ----

// Administrador de series: crear, abrir, ordenar capitulos, notas y fichas.
// Lo abre el script Series y, para elegir la serie del proyecto, MomentosIA
// y Anteriormente (con "Usar esta serie").
class VentanaSeries : VentanaBase
{
    readonly string veg, clave, modelo;
    readonly bool elegir;
    List<string> rutas = new List<string>();
    public SerieProyecto Serie_;
    public List<CapSerie> Caps = new List<CapSerie>();
    bool cargando, trabajando;

    Lista lstSeries = new Lista();
    Boton btnNueva = new Boton("Nueva\u2026", EstiloBoton.Secundario);
    Boton btnAbrir = new Boton("Abrir\u2026", EstiloBoton.Secundario);
    Boton btnOlvidar = new Boton("Quitar de la lista", EstiloBoton.Secundario);
    CampoTexto txtNombre = new CampoTexto();
    Segmentado segTipo = new Segmentado(SerieProyecto.Tipos);
    Etiqueta lblCarpeta, lblEstado;
    Boton btnCarpeta = new Boton("Elegir carpeta\u2026", EstiloBoton.Secundario);
    Boton btnBuscar = new Boton("Buscar cap\u00edtulos ah\u00ed", EstiloBoton.Secundario);
    Lista lstCaps = new Lista();
    Boton btnAgregar = new Boton("Agregar\u2026", EstiloBoton.Secundario);
    Boton btnEste = new Boton("Este proyecto", EstiloBoton.Secundario);
    Boton btnSubir = new Boton("Subir", EstiloBoton.Secundario);
    Boton btnBajar = new Boton("Bajar", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Quitar", EstiloBoton.Secundario);
    Boton btnFichas = new Boton("Hacer las fichas que faltan", EstiloBoton.Secundario);
    Boton btnRehacer = new Boton("Rehacer la elegida", EstiloBoton.Secundario);
    CampoTexto txtNotas = new CampoTexto();
    Combo cmbPapel = new Combo();
    Boton btnNota = new Boton("Nota\u2026", EstiloBoton.Secundario);
    Boton btnFormato = new Boton("Formato y ritmo\u2026", EstiloBoton.Secundario);
    Boton btnMusica = new Boton("M\u00fasica\u2026", EstiloBoton.Secundario);
    Etiqueta lblFormato;
    // Mide el proyecto abierto (para "Aprender de este proyecto"); null si no se puede.
    public Func<string, Medicion> Medidor;
    Boton btnSinSerie = new Boton("Sin serie", EstiloBoton.Secundario);
    Boton btnListo;

    public VentanaSeries(string vegActual, string clave, string modelo, bool elegir) : base("Series", 1040)
    {
        this.veg = vegActual ?? ""; this.clave = clave; this.modelo = modelo; this.elegir = elegir;
        if (elegir) StartPosition = FormStartPosition.CenterParent;
        btnListo = new Boton(elegir ? "Usar esta serie" : "Listo", EstiloBoton.Primario);
        int m = Margen, w = Ancho;
        Encabezado("Series", "Proyectos de varias partes: cap\u00edtulos en orden, notas y fichas. MomentosIA y Anteriormente los usan de contexto.");

        // Columna izquierda: series conocidas
        int y = 92, ci = 250;
        Texto("Tus series", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        lstSeries.CheckBoxes = false;
        lstSeries.Columns.Add("Serie", ci - SystemInformation.VerticalScrollBarWidth - 4);
        Pos(lstSeries, m, y + 24, ci, 330);
        Pos(btnNueva, m, y + 362, (ci - 8) / 2, 32);
        Pos(btnAbrir, m + (ci + 8) / 2, y + 362, (ci - 8) / 2, 32);
        Pos(btnOlvidar, m, y + 400, ci, 32);

        // Columna derecha: la serie elegida
        int dx = m + ci + 24, dw = w - ci - 24;
        Texto("NOMBRE", Tema.Pequena, Tema.TextoSuave, dx, y, 200, 18);
        Texto("TIPO", Tema.Pequena, Tema.TextoSuave, dx + dw - 420, y, 200, 18);
        Pos(txtNombre, dx, y + 20, dw - 436, 34);
        Pos(segTipo, dx + dw - 420, y + 20, 420, 34);
        y += 64;
        lblCarpeta = Texto("", Tema.Pequena, Tema.TextoSuave, dx, y + 6, dw - 330, 20);
        Pos(btnCarpeta, dx + dw - 320, y, 150, 30);
        Pos(btnBuscar, dx + dw - 162, y, 162, 30);
        y += 38;
        lstCaps.Columns.Add("#", 36);
        lstCaps.Columns.Add("Cap\u00edtulo", dw - 36 - 90 - 130 - 84 - 84 - SystemInformation.VerticalScrollBarWidth - 4);
        lstCaps.Columns.Add("Es", 90);
        lstCaps.Columns.Add("Papel", 130);
        lstCaps.Columns.Add("Transcrito", 84);
        lstCaps.Columns.Add("Ficha", 84);
        Pos(lstCaps, dx, y, dw, 200);
        y += 208;
        int bx = dx;
        foreach (KeyValuePair<Boton, int> b in new KeyValuePair<Boton, int>[] {
            new KeyValuePair<Boton, int>(btnAgregar, 100), new KeyValuePair<Boton, int>(btnEste, 130),
            new KeyValuePair<Boton, int>(btnSubir, 70), new KeyValuePair<Boton, int>(btnBajar, 70), new KeyValuePair<Boton, int>(btnQuitar, 80) })
        {
            Pos(b.Key, bx, y, b.Value, 32);
            bx += b.Value + 8;
        }
        foreach (string p in PapelEpisodio.Papeles) cmbPapel.Items.Add(p);
        Pos(cmbPapel, dx + dw - 230, y + 1, 152, 30);
        Pos(btnNota, dx + dw - 70, y, 70, 32);
        y += 40;
        Pos(btnFichas, dx, y, 230, 32);
        Pos(btnRehacer, dx + 238, y, 170, 32);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, dx + 418, y - 2, dw - 418, 38);
        y += 44;
        Texto("Notas de la serie", Tema.Negrita, Tema.Texto, dx, y, 150, 20);
        lblFormato = Texto("", Tema.Pequena, Tema.TextoSuave, dx + 150, y + 4, dw - 430, 20);
        Pos(btnMusica, dx + dw - 270, y - 6, 102, 30);
        lblFormato.TextAlign = ContentAlignment.MiddleRight;
        Pos(btnFormato, dx + dw - 160, y - 6, 160, 30);
        txtNotas.Multilinea = true;
        Pos(txtNotas, dx, y + 28, dw, 84);
        y += 124;
        if (elegir) Pos(btnSinSerie, m + w - 300, y, 130, 40);
        Pos(btnListo, m + w - 160, y, 160, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        // Eventos
        lstSeries.SelectedIndexChanged += delegate
        {
            if (cargando || lstSeries.SelectedIndices.Count == 0) return;
            GuardarActual();
            Abrir(rutas[lstSeries.SelectedIndices[0]]);
        };
        btnNueva.Click += delegate { Nueva(); };
        btnAbrir.Click += delegate { AbrirArchivo(); };
        btnOlvidar.Click += delegate
        {
            if (Serie_ == null) return;
            SerieProyecto.Olvidar(Serie_.Ruta);
            Serie_ = null;
            LlenarSeries(null);
            Mostrar();
            Estado("La serie se quit\u00f3 de la lista (su archivo sigue en su carpeta; \u201cAbrir\u2026\u201d la trae de vuelta).", false);
        };
        btnCarpeta.Click += delegate { ElegirCarpeta(); };
        btnBuscar.Click += delegate
        {
            if (Serie_ == null) return;
            int n = Serie_.BuscarEnCarpeta();
            Cambio();
            Estado(n == 0 ? "No encontr\u00e9 cap\u00edtulos nuevos (busca .veg con S01E02, \u201cParte 2\u201d, \u201cCap 2\u201d\u2026 en el nombre)." : n + " cap\u00edtulos agregados.", false);
        };
        btnAgregar.Click += delegate { AgregarArchivos(); };
        btnEste.Click += delegate { if (Serie_ != null && veg.Length > 0) { Serie_.Agregar(veg); Cambio(); } };
        btnSubir.Click += delegate { Mover(-1); };
        btnBajar.Click += delegate { Mover(1); };
        btnQuitar.Click += delegate
        {
            int i = Elegido();
            if (i < 0) return;
            Serie_.Episodios.RemoveAt(i);
            Cambio();
        };
        lstCaps.SelectedIndexChanged += delegate
        {
            int i = Elegido();
            btnNota.Enabled = i >= 0 && Serie_ != null;
            if (i < 0) return;
            cargando = true;
            cmbPapel.SelectedIndex = Math.Max(0, Array.IndexOf(PapelEpisodio.Papeles, Caps[i].Papel));
            cargando = false;
        };
        cmbPapel.SelectedIndexChanged += delegate
        {
            int i = Elegido();
            if (cargando || i < 0 || Serie_ == null) return;
            Serie_.CambiarPapel(Caps[i].Veg, (string)cmbPapel.SelectedItem);
            Cambio();
            lstCaps.Items[i].Selected = true;
        };
        btnNota.Click += delegate
        {
            int i = Elegido();
            if (i < 0 || Serie_ == null) return;
            using (DialogoNombre d = new DialogoNombre(Serie_.NotaEpisodio(Caps[i].Veg), "Nota de " + Caps[i].Nombre,
                                                       "Qu\u00e9 tiene de distinto este cap\u00edtulo (se la paso a Gemini)"))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Serie_.NotasEpisodio[Caps[i].Veg] = d.Nombre;
            }
            Cambio();
            lstCaps.Items[i].Selected = true;
        };
        btnFormato.Click += delegate
        {
            if (Serie_ == null) return;
            GuardarActual();
            using (VentanaFormato f = new VentanaFormato(Serie_.Formato, veg, Medidor))
            {
                if (f.ShowDialog(this) != DialogResult.OK) return;
                Serie_.Formato = f.Resultado;
            }
            Cambio();
            Estado("\u2714 Formato guardado.", false);
        };
        btnMusica.Click += delegate
        {
            if (Serie_ == null) return;
            GuardarActual();
            using (VentanaMusicaSerie d = new VentanaMusicaSerie(Serie_.Musica, Serie_.Formato.Premisa, clave, modelo))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Serie_.Musica = d.Resultado;
            }
            Cambio();
            Estado("\u2714 M\u00fasica de la serie guardada.", false);
        };
        btnFichas.Click += delegate { Fichas(false); };
        btnRehacer.Click += delegate { Fichas(true); };
        lstCaps.ItemCheck += delegate (object s, ItemCheckEventArgs e)
        {
            if (cargando) return;
            CapSerie c = (CapSerie)lstCaps.Items[e.Index].Tag;
            if (c.Relacion == 0 || !c.Transcrito) e.NewValue = CheckState.Unchecked;
        };
        lstCaps.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((CapSerie)e.Item.Tag).Elegido = e.Item.Checked; };
        btnSinSerie.Click += delegate { Serie_ = null; Caps = new List<CapSerie>(); AjustesProyecto.Guardar(veg, "", null); DialogResult = DialogResult.OK; Close(); };
        btnListo.Click += delegate
        {
            if (trabajando) return;
            GuardarActual();
            if (veg.Length > 0 && Serie_ != null) AjustesProyecto.Guardar(veg, Serie_.Ruta, Caps);
            DialogResult = DialogResult.OK;
            Close();
        };
        FormClosing += delegate (object s, FormClosingEventArgs e)
        {
            if (trabajando) { e.Cancel = true; return; }
            GuardarActual();
        };

        // Inicio: la serie del proyecto abierto, o la ultima usada.
        SerieProyecto delProyecto = veg.Length > 0 ? SerieProyecto.DelProyecto(veg) : null;
        LlenarSeries(delProyecto != null ? delProyecto.Ruta : null);
        if (delProyecto != null) Abrir(delProyecto.Ruta);
        else if (rutas.Count > 0 && !elegir) Abrir(rutas[0]);
        else Mostrar();
        if (Serie_ == null)
            Estado(rutas.Count == 0 ? "Crea tu primera serie con \u201cNueva\u2026\u201d." : "Elige una serie de la lista o crea una nueva.", false);
        else if (veg.Length > 0 && Serie_.IndiceDe(veg) < 0)
            Estado("Este proyecto no est\u00e1 en la serie: \u201cEste proyecto\u201d lo agrega en su lugar.", false);
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void LlenarSeries(string elegida)
    {
        cargando = true;
        rutas = SerieProyecto.Registradas();
        lstSeries.Items.Clear();
        foreach (string r in rutas)
        {
            string nombre = Path.GetFileName(r).Replace(SerieProyecto.Extension, "");
            try { nombre = SerieProyecto.Cargar(r).Nombre; } catch { }
            ListViewItem it = new ListViewItem(nombre);
            if (elegida != null && String.Equals(r, elegida, StringComparison.OrdinalIgnoreCase)) it.Selected = true;
            lstSeries.Items.Add(it);
        }
        cargando = false;
    }

    void Abrir(string ruta)
    {
        try
        {
            Serie_ = SerieProyecto.Cargar(ruta);
            SerieProyecto.Registrar(ruta);
        }
        catch (Exception ex) { Serie_ = null; Estado("No se pudo abrir la serie: " + ex.Message, true); }
        Mostrar();
    }

    // Pone en pantalla la serie elegida.
    void Mostrar()
    {
        bool hay = Serie_ != null;
        foreach (Control c in new Control[] { txtNombre, segTipo, btnCarpeta, btnBuscar, lstCaps, btnAgregar, btnEste, btnSubir,
                                              btnBajar, btnQuitar, btnFichas, btnRehacer, txtNotas, btnOlvidar, btnFormato, btnMusica })
            c.Enabled = hay;
        btnNota.Enabled = false;
        cmbPapel.Enabled = hay;   // deshabilitado se ve blanco en Windows
        btnListo.Enabled = hay || !elegir;
        cargando = true;
        txtNombre.Text = hay ? Serie_.Nombre : "";
        segTipo.Seleccion = hay ? Math.Max(0, Array.IndexOf(SerieProyecto.Tipos, Serie_.Tipo)) : 0;
        txtNotas.Text = hay ? (Serie_.Notas ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
        cargando = false;
        btnEste.Enabled = hay && veg.Length > 0 && Serie_.IndiceDe(veg) < 0;
        LlenarCapitulos();
    }

    void LlenarCapitulos()
    {
        List<string> fuera = AjustesProyecto.Excluidos(veg);
        foreach (CapSerie c in Caps) if (!c.Elegido && !fuera.Contains(c.Nombre)) fuera.Add(c.Nombre);
        Caps = Serie_ != null ? Serie_.Capitulos(veg) : new List<CapSerie>();
        foreach (CapSerie c in Caps) if (fuera.Contains(c.Nombre)) c.Elegido = false;
        cargando = true;
        lstCaps.Items.Clear();
        foreach (CapSerie c in Caps)
        {
            ListViewItem it = new ListViewItem(c.Posicion.ToString());
            it.SubItems.Add(c.Nombre);
            it.SubItems.Add(!c.Existe ? "no se encuentra" : c.Relacion < 0 ? (veg.Length > 0 && Serie_.IndiceDe(veg) >= 0 ? "anterior" : "\u2014") :
                            c.Relacion > 0 ? "posterior" : "este");
            string nota = Serie_.NotaEpisodio(c.Veg);
            it.SubItems.Add(c.Papel + (nota.Length > 0 ? " \u270e" : ""));
            it.SubItems.Add(c.Transcrito ? "s\u00ed" : "no");
            it.SubItems.Add(c.TieneFicha ? "s\u00ed" : c.Transcrito ? "falta" : "\u2014");
            it.Checked = c.Relacion != 0 && c.Transcrito && c.Elegido;
            if (c.Relacion == 0 || !c.Transcrito) it.ForeColor = Tema.TextoSuave;
            it.Tag = c;
            lstCaps.Items.Add(it);
        }
        cargando = false;
        lblFormato.Text = Serie_ == null ? "" : Serie_.Formato.Nombre + (String.IsNullOrEmpty(Serie_.Formato.Aprendido) ? "" : " \u00b7 aprendido");
        lblCarpeta.Text = Serie_ == null ? "" : "Carpeta: " + (String.IsNullOrEmpty(Serie_.Carpeta) ? "(sin elegir)" : Serie_.Carpeta) +
                                                " \u00b7 " + Caps.Count + " cap\u00edtulos";
    }

    void Cambio()
    {
        GuardarActual();
        btnEste.Enabled = Serie_ != null && veg.Length > 0 && Serie_.IndiceDe(veg) < 0;
        LlenarCapitulos();
    }

    void GuardarActual()
    {
        if (Serie_ == null || cargando) return;
        Serie_.Nombre = txtNombre.Text.Trim().Length > 0 ? txtNombre.Text.Trim() : Serie_.Nombre;
        Serie_.Tipo = SerieProyecto.Tipos[Math.Max(0, segTipo.Seleccion)];
        Serie_.Notas = txtNotas.Text.Trim();
        try { Serie_.Guardar(); } catch (Exception ex) { Estado("No se pudo guardar la serie: " + ex.Message, true); }
    }

    int Elegido() { return lstCaps.SelectedIndices.Count == 0 ? -1 : lstCaps.SelectedIndices[0]; }

    void Mover(int d)
    {
        int i = Elegido(), j = i + d;
        if (i < 0 || j < 0 || j >= Serie_.Episodios.Count) return;
        string x = Serie_.Episodios[i];
        Serie_.Episodios[i] = Serie_.Episodios[j];
        Serie_.Episodios[j] = x;
        Cambio();
        lstCaps.Items[j].Selected = true;
    }

    // ------------------------------------------------- crear y abrir

    string CarpetaSugerida()
    {
        if (veg.Length == 0) return "";
        string dir = Path.GetDirectoryName(veg);
        int t, n; string k;
        // Si cada capitulo tiene su carpeta (S01E02/...), la serie va en la de arriba.
        return Serie.Clave(Path.GetFileName(dir), out t, out n, out k) ? Path.GetDirectoryName(dir) : dir;
    }

    void Nueva()
    {
        GuardarActual();
        string sugerido = "";
        int t, n; string k;
        if (veg.Length > 0 && Serie.Clave(Path.GetFileNameWithoutExtension(veg), out t, out n, out k))
            sugerido = k.Replace("#", "").Trim().ToUpperInvariant();
        string nombre;
        using (DialogoNombre d = new DialogoNombre(sugerido, "Nueva serie", "Nombre de la serie"))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            nombre = d.Nombre;
        }
        string carpeta;
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta de la serie (donde est\u00e1n o estar\u00e1n sus cap\u00edtulos). Ah\u00ed se guarda el archivo de la serie.";
            string s = CarpetaSugerida();
            if (s.Length > 0 && Directory.Exists(s)) d.SelectedPath = s;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            carpeta = d.SelectedPath;
        }
        SerieProyecto nueva = new SerieProyecto();
        nueva.Nombre = nombre;
        nueva.Carpeta = carpeta;
        nueva.Ruta = SerieProyecto.RutaPara(carpeta, nombre);
        if (File.Exists(nueva.Ruta)) { Estado("Ya hay una serie con ese nombre en esa carpeta: \u00e1brela con \u201cAbrir\u2026\u201d.", true); return; }
        if (veg.Length > 0) nueva.Agregar(veg);
        int encontrados = nueva.BuscarEnCarpeta();
        try { nueva.Guardar(); }
        catch (Exception ex) { Estado("No se pudo crear la serie: " + ex.Message, true); return; }
        LlenarSeries(nueva.Ruta);
        Abrir(nueva.Ruta);
        Estado("\u2714 Serie creada" + (encontrados > 0 ? " con " + Serie_.Episodios.Count + " cap\u00edtulos encontrados en la carpeta" : "") +
               ". Revisa el orden y escribe las notas.", false);
    }

    void AbrirArchivo()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Archivo de una serie";
            d.Filter = "Series de vegas-cut|*" + SerieProyecto.Extension;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            GuardarActual();
            SerieProyecto.Registrar(d.FileName);
            LlenarSeries(d.FileName);
            Abrir(d.FileName);
        }
    }

    void ElegirCarpeta()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta donde est\u00e1n los cap\u00edtulos (se busca tambi\u00e9n en sus subcarpetas)";
            if (!String.IsNullOrEmpty(Serie_.Carpeta) && Directory.Exists(Serie_.Carpeta)) d.SelectedPath = Serie_.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Serie_.Carpeta = d.SelectedPath;
        }
        Cambio();
    }

    void AgregarArchivos()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Cap\u00edtulos de la serie";
            d.Filter = "Proyectos de Vegas|*.veg";
            d.Multiselect = true;
            if (!String.IsNullOrEmpty(Serie_.Carpeta) && Directory.Exists(Serie_.Carpeta)) d.InitialDirectory = Serie_.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            foreach (string f in d.FileNames) Serie_.Agregar(f);
        }
        Cambio();
    }

    // ------------------------------------------------------------ fichas

    void Fichas(bool rehacer)
    {
        if (String.IsNullOrEmpty(clave)) { Estado("Falta la clave de Gemini: ejecuta \u201cConfigurarVegasCut\u201d.", true); return; }
        GuardarActual();
        List<CapSerie> cola = new List<CapSerie>();
        if (rehacer)
        {
            int i = Elegido();
            if (i < 0) { Estado("Elige un cap\u00edtulo de la lista.", true); return; }
            if (!Caps[i].Transcrito) { Estado(Caps[i].Nombre + " no est\u00e1 transcrito.", true); return; }
            cola.Add(Caps[i]);
        }
        else
            foreach (CapSerie c in Caps) if (c.Transcrito && !c.TieneFicha) cola.Add(c);
        if (cola.Count == 0) { Estado("Todos los cap\u00edtulos transcritos ya tienen ficha.", false); return; }

        string notas = Serie_.Notas, tipo = Serie_.Tipo;
        trabajando = true;
        Habilitar(false);
        Thread hilo = new Thread(delegate ()
        {
            List<string> errores = new List<string>();
            for (int i = 0; i < cola.Count; i++)
            {
                CapSerie c = cola[i];
                Avisar("Haciendo la ficha de " + c.Nombre + " (" + (i + 1) + " de " + cola.Count + ")\u2026");
                try
                {
                    Episodio e = Episodio.Abrir(c.Veg);
                    Ficha f = Ficha.Leer(Gemini.Generar(clave, modelo, Serie.InstruccionesFicha(tipo), Serie.MensajeFicha(e, notas), true));
                    f.Generada = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " \u00b7 " + modelo;
                    f.Guardar(c.Veg);
                }
                catch (Exception ex) { errores.Add(c.Nombre + ": " + ex.Message); }
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    Habilitar(true);
                    LlenarCapitulos();
                    if (errores.Count > 0) Estado(String.Join("\n", errores.ToArray()), true);
                    else Estado("\u2714 Fichas listas.", false);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Habilitar(bool si)
    {
        foreach (Control c in new Control[] { btnFichas, btnRehacer, btnListo, btnNueva, btnAbrir, lstSeries, btnAgregar, btnQuitar, btnSubir, btnBajar })
            c.Enabled = si;
    }

    void Avisar(string t)
    {
        try { BeginInvoke((MethodInvoker)delegate { Estado(t, false); }); } catch { }
    }

    // "Serie Steel Ball Run: 2 anteriores \u00b7 1 posterior \u00b7 1 sin ficha"
    public static string Resumen(SerieProyecto serie, List<CapSerie> caps)
    {
        if (serie == null) return "Sin serie (pulsa \u201cSerie\u2026\u201d para elegirla o crearla).";
        int ant = 0, pos = 0, sin = 0;
        bool esta = caps.Exists(delegate (CapSerie c) { return c.Relacion == 0; });
        foreach (CapSerie c in caps)
        {
            if (c.Relacion == 0 || !c.Elegido || !c.Transcrito) continue;
            if (c.Relacion < 0) ant++; else pos++;
            if (!c.TieneFicha) sin++;
        }
        return "Serie " + serie.Nombre + ": " + ant + (ant == 1 ? " anterior" : " anteriores") +
               (pos > 0 ? " \u00b7 " + pos + (pos == 1 ? " posterior" : " posteriores") : "") +
               (sin > 0 ? " \u00b7 " + sin + " sin ficha" : "") + (esta ? "" : " \u00b7 este proyecto no est\u00e1 en la serie");
    }
}

// ---- src/comun/VentanaFormato.cs ----

// Formato y ritmo de una serie: que tipo de video es, que busca, como se
// marca el avance, el narrador y las reglas de ritmo. "Aprender de este
// proyecto" mide el proyecto abierto (un episodio que funciono) y usa su
// ritmo como objetivo.
class VentanaFormato : VentanaBase
{
    public FormatoSerie Resultado;
    readonly Func<string, Medicion> medidor;
    readonly string proyecto;
    bool cargando;

    Combo cmbFormato = new Combo();
    Boton btnPreset = new Boton("Usar valores del formato", EstiloBoton.Secundario);
    Boton btnPlantilla = new Boton("Plantilla y kit\u2026", EstiloBoton.Secundario);
    CampoTexto txtPremisa = new CampoTexto();
    Combo cmbAvance = new Combo();
    Segmentado segNarrador = new Segmentado(new string[] { "Con narrador", "Sin narrador" });
    CampoTexto txtNarrador = new CampoTexto();
    CampoTexto txtEstilo = new CampoTexto();
    CampoNumero numPPM = Num("ppm", 80, 320, 5), numNarr = Num("s", 10, 600, 5), numRec = Num("/min", 0, 60, 1),
                numCMin = Num("/min", 1, 80, 1), numCMax = Num("/min", 1, 80, 1), numMus = Num("s", 10, 900, 5),
                numZona = Num("s", 30, 600, 10), numDMin = Num("min", 1, 240, 1), numDMax = Num("min", 1, 240, 1);
    Etiqueta lblAprendido, lblEstado;
    Boton btnAprender = new Boton("Aprender de este proyecto", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);

    static CampoNumero Num(string sufijo, int min, int max, int paso)
    {
        CampoNumero n = new CampoNumero();
        n.Sufijo = sufijo; n.Minimo = min; n.Maximo = max; n.Paso = paso;
        return n;
    }

    // medidor: mide el proyecto abierto con el nombre del narrador (null si no se puede).
    public VentanaFormato(FormatoSerie f, string proyecto, Func<string, Medicion> medidor) : base("Formato y ritmo", 760)
    {
        this.medidor = medidor; this.proyecto = proyecto ?? "";
        StartPosition = FormStartPosition.CenterParent;
        Resultado = f.Copia();
        int m = Margen, w = Ancho;
        Encabezado("Formato y ritmo", "Qu\u00e9 tipo de video es, qu\u00e9 busca y a qu\u00e9 ritmo. PulirEpisodio lo usa para medir y proponer.");
        int y = 92;
        Texto("FORMATO", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        foreach (string x in FormatoSerie.Formatos) cmbFormato.Items.Add(x);
        Pos(cmbFormato, m, y + 20, 260, 30);
        Pos(btnPreset, m + 272, y + 18, 210, 32);
        Pos(btnPlantilla, m + 490, y + 18, 170, 32);
        y += 60;
        Texto("PREMISA Y OBJETIVO (de qu\u00e9 va la serie, a d\u00f3nde quieres llevarla)", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        txtPremisa.Multilinea = true;
        Pos(txtPremisa, m, y + 20, w, 60);
        y += 90;
        Texto("AVANCE EN PANTALLA", Tema.Pequena, Tema.TextoSuave, m, y, 160, 18);
        foreach (string x in FormatoSerie.Avances) cmbAvance.Items.Add(x);
        Pos(cmbAvance, m, y + 20, 160, 30);
        Texto("NARRADOR", Tema.Pequena, Tema.TextoSuave, m + 176, y, 200, 18);
        Pos(segNarrador, m + 176, y + 18, 280, 34);
        Texto("SE LLAMA (en la transcripci\u00f3n)", Tema.Pequena, Tema.TextoSuave, m + 472, y, w - 472, 18);
        Pos(txtNarrador, m + 472, y + 18, w - 472, 34);
        y += 62;
        Texto("ESTILO DEL NARRADOR (c\u00f3mo cuenta: tiempo verbal, humor, ganchos)", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        txtEstilo.Multilinea = true;
        Pos(txtEstilo, m, y + 20, w, 52);
        y += 84;

        Texto("Ritmo", Tema.Seccion, Tema.Texto, m, y, 200, 22);
        y += 28;
        object[,] campos = {
            { "Velocidad del narrador", numPPM }, { "M\u00e1ximo sin narrador", numNarr }, { "Recursos por minuto", numRec },
            { "Cortes por minuto, desde", numCMin }, { "hasta", numCMax }, { "Cambiar la m\u00fasica cada", numMus },
            { "Zona cr\u00edtica del inicio", numZona }, { "Duraci\u00f3n, desde", numDMin }, { "hasta", numDMax } };
        int cw = (w - 32) / 3;
        for (int i = 0; i < campos.GetLength(0); i++)
        {
            int cx = m + (i % 3) * (cw + 16), cy = y + (i / 3) * 58;
            Texto(((string)campos[i, 0]).ToUpperInvariant(), Tema.Pequena, Tema.TextoSuave, cx, cy, cw, 18);
            Pos((Control)campos[i, 1], cx, cy + 18, cw, 32);
        }
        y += 3 * 58 + 6;
        Pos(btnAprender, m, y, 230, 34);
        lblAprendido = Texto("", Tema.Pequena, Tema.TextoSuave, m + 242, y - 2, w - 242, 38);
        y += 44;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 290, 40);
        Pos(btnCancelar, m + w - 280, y, 120, 40);
        Pos(btnGuardar, m + w - 150, y, 150, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        btnAprender.Enabled = medidor != null;
        if (medidor == null) Estado("Para aprender de un episodio, abre su proyecto en Vegas y ejecuta Series desde ah\u00ed.", false);

        Mostrar(Resultado);
        cmbFormato.SelectedIndexChanged += delegate { btnPlantilla.Enabled = (string)cmbFormato.SelectedItem == FormatoSerie.TV; };
        btnPlantilla.Click += delegate
        {
            FormatoSerie actual = Leer();
            using (VentanaPlantillaTV d = new VentanaPlantillaTV(actual.Tv, actual.Reglas))
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Resultado.Tv = d.Resultado;
            }
            Estado("Plantilla y kit listos (se guardan con \u00abGuardar\u00bb).", false);
        };
        cmbFormato.SelectedIndexChanged += delegate { if (!cargando) Estado("Pulsa \u201cUsar valores del formato\u201d para cargar sus reglas de partida.", false); };
        btnPreset.Click += delegate
        {
            FormatoSerie p = FormatoSerie.Preset((string)cmbFormato.SelectedItem);
            p.Premisa = txtPremisa.Text.Trim();
            p.NarradorNombre = txtNarrador.Text.Trim().Length > 0 ? txtNarrador.Text.Trim() : p.NarradorNombre;
            foreach (KeyValuePair<string, string> kv in Resultado.Tv.Kit) p.Tv.Kit[kv.Key] = kv.Value;   // el kit no se pierde
            Resultado.Tv = p.Tv;
            Mostrar(p);
            Estado("Valores de partida de \u201c" + p.Nombre + "\u201d.", false);
        };
        btnAprender.Click += delegate { Aprender(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        btnGuardar.Click += delegate { Resultado = Leer(); DialogResult = DialogResult.OK; Close(); };
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Mostrar(FormatoSerie f)
    {
        cargando = true;
        cmbFormato.SelectedIndex = Math.Max(0, Array.IndexOf(FormatoSerie.Formatos, f.Nombre));
        btnPlantilla.Enabled = f.EsTV;
        txtPremisa.Text = (f.Premisa ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        cmbAvance.SelectedIndex = Math.Max(0, Array.IndexOf(FormatoSerie.Avances, f.Avance));
        segNarrador.Seleccion = f.Narrador ? 0 : 1;
        txtNarrador.Text = f.NarradorNombre;
        txtEstilo.Text = (f.EstiloNarrador ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        MostrarReglas(f.Reglas);
        lblAprendido.Text = String.IsNullOrEmpty(f.Aprendido) ? "Las reglas salen del formato. Si tienes un episodio que funcion\u00f3 bien, \u00e1brelo y aprende de \u00e9l."
                                                               : "Aprendido de " + f.Aprendido;
        Resultado.Aprendido = f.Aprendido;
        cargando = false;
    }

    void MostrarReglas(ReglasRitmo r)
    {
        numPPM.Valor = r.PPM; numNarr.Valor = r.NarradorCadaSeg; numRec.Valor = r.RecursosPorMin;
        numCMin.Valor = r.CortesMin; numCMax.Valor = r.CortesMax; numMus.Valor = r.MusicaCadaSeg;
        numZona.Valor = r.ZonaCriticaSeg; numDMin.Valor = (int)Math.Round(r.DuracionMin); numDMax.Valor = (int)Math.Round(r.DuracionMax);
    }

    FormatoSerie Leer()
    {
        FormatoSerie f = Resultado.Copia();
        f.Nombre = (string)cmbFormato.SelectedItem ?? "Otro";
        f.Premisa = txtPremisa.Text.Trim();
        f.Avance = (string)cmbAvance.SelectedItem ?? "Ninguno";
        f.Narrador = segNarrador.Seleccion == 0;
        f.NarradorNombre = txtNarrador.Text.Trim().Length > 0 ? txtNarrador.Text.Trim() : "Narrador";
        f.EstiloNarrador = txtEstilo.Text.Trim();
        ReglasRitmo r = f.Reglas;
        r.PPM = numPPM.Valor; r.NarradorCadaSeg = numNarr.Valor; r.RecursosPorMin = numRec.Valor;
        r.CortesMin = Math.Min(numCMin.Valor, numCMax.Valor); r.CortesMax = Math.Max(numCMin.Valor, numCMax.Valor);
        r.MusicaCadaSeg = numMus.Valor; r.ZonaCriticaSeg = numZona.Valor;
        r.DuracionMin = Math.Min(numDMin.Valor, numDMax.Valor); r.DuracionMax = Math.Max(numDMin.Valor, numDMax.Valor);
        return f;
    }

    void Aprender()
    {
        Medicion med;
        string narrador = txtNarrador.Text.Trim().Length > 0 ? txtNarrador.Text.Trim() : "Narrador";
        try { med = medidor(narrador); }
        catch (Exception ex) { Estado("No se pudo medir el proyecto: " + ex.Message, true); return; }
        if (med.Duracion < 60) { Estado("El proyecto dura menos de un minuto: abre un episodio terminado.", true); return; }
        FormatoSerie f = Leer();
        f.Reglas = Ritmo.Aprender(med, f.Reglas);
        if (med.HayNarrador) f.Narrador = true;
        f.Aprendido = (proyecto.Length > 0 ? Path.GetFileNameWithoutExtension(proyecto) : "el proyecto abierto") + " \u00b7 " +
                      DateTime.Now.ToString("yyyy-MM-dd");
        Resultado.Aprendido = f.Aprendido;
        Mostrar(f);
        Estado("\u2714 " + Ritmo.Resumen(med) + (med.HayNarrador ? "" : " \u00b7 no encontr\u00e9 la voz \u201c" + narrador + "\u201d en la transcripci\u00f3n, as\u00ed " +
               "que no se aprendi\u00f3 nada del narrador.") + " Revisa los valores y guarda.", false);
    }
}

// ---- src/comun/SerieTV.cs ----

// =====================================================================
// Serie de TV / anime: la plantilla de bloques de cada capitulo (cold open,
// opening, titulo, actos, re-gancho, continuara, ending, avance) y el kit de
// archivos fijos de la serie. Si falta un archivo del kit se pone un
// placeholder con la duracion del bloque.
// Salio de analizar Stardust Crusaders y Steel Ball Run
// (docs/estructura-episodio-sc.md).
// =====================================================================

public class BloqueTV
{
    public string Clave = "", Nombre = "";
    public string Tipo = "contenido";   // contenido (del capitulo), kit (archivo fijo), texto
    public double Segundos;             // duracion fija (cold open, kit, texto, avance)
    public double Porcentaje;           // de lo que queda (actos)
    public string Descripcion = "";
    public string Kit = "";             // archivo del kit que usa (vacio = el de su clave)
    public string Ritmo = "";           // lento, medio o rapido (lo propone la escaleta)

    public string ClaveKit { get { return Kit.Length > 0 ? Kit : Clave; } }

    public BloqueTV() { }
    public BloqueTV(string clave, string nombre, string tipo, double segundos, double porcentaje, string descripcion)
    {
        Clave = clave; Nombre = nombre; Tipo = tipo; Segundos = segundos; Porcentaje = porcentaje; Descripcion = descripcion;
    }

    public BloqueTV Copia() { return (BloqueTV)MemberwiseClone(); }
}

public class PlantillaTV
{
    public List<BloqueTV> Bloques = new List<BloqueTV>();
    public Dictionary<string, string> Kit = new Dictionary<string, string>();   // clave del bloque -> archivo

    public static PlantillaTV PorDefecto()
    {
        PlantillaTV p = new PlantillaTV();
        p.Bloques.Add(new BloqueTV("cold_open", "Cold open", "contenido", 45, 0,
            "Recap del cliffhanger anterior, llegada con humor o el rival tramando algo; termina en un gancho."));
        p.Bloques.Add(new BloqueTV("op", "Opening", "kit", 20, 0, "Opening propio (montaje de la serie)."));
        p.Bloques.Add(new BloqueTV("titulo", "T\u00edtulo y lugar", "texto", 3, 0, "\u00abEtapa N \u00b7 nombre del cap\u00edtulo\u00bb y cartel del lugar o del tiempo."));
        p.Bloques.Add(new BloqueTV("acto_a", "Acto A", "contenido", 0, 55,
            "Viaje y llegada (0 %), aparece el problema (~13 %), se revela (~35 %) y la crisis (~45 %)."));
        p.Bloques.Add(new BloqueTV("regancho", "Re-gancho", "kit", 6, 0, "Tarjeta de stats del rival o ranking de la etapa (el eyecatch)."));
        p.Bloques.Add(new BloqueTV("acto_b", "Acto B", "contenido", 0, 45,
            "Giro (~58 %), resoluci\u00f3n con ranking (~77 %), remate y gancho final (95\u201399 %)."));
        p.Bloques.Add(new BloqueTV("continuara", "Continuar\u00e1", "kit", 3, 0, "Flecha \u00abTo Be Continued\u00bb sobre el cliffhanger."));
        p.Bloques.Add(new BloqueTV("ed", "Ending", "kit", 15, 0, "Ending corto."));
        p.Bloques.Add(new BloqueTV("avance", "Avance / post-cr\u00e9ditos", "contenido", 12, 0,
            "Escena del pr\u00f3ximo cap\u00edtulo (ya grabado) o un hilo nuevo."));
        return p;
    }

    public PlantillaTV Copia()
    {
        PlantillaTV p = new PlantillaTV();
        foreach (BloqueTV b in Bloques) p.Bloques.Add(b.Copia());
        foreach (KeyValuePair<string, string> kv in Kit) p.Kit[kv.Key] = kv.Value;
        return p;
    }

    public BloqueTV Bloque(string clave)
    {
        foreach (BloqueTV b in Bloques) if (b.Clave == clave) return b;
        return null;
    }

    public string Archivo(string clave)
    {
        string a;
        return Kit.TryGetValue(clave, out a) ? a : "";
    }

    // Segundos de lo fijo (todo menos los actos).
    public double Fijo()
    {
        double s = 0;
        foreach (BloqueTV b in Bloques) s += b.Segundos;
        return s;
    }

    // Cuanto dura cada acto para que el capitulo dure "total" segundos.
    public double Acto(string clave, double total)
    {
        BloqueTV b = Bloque(clave);
        double pct = 0;
        foreach (BloqueTV x in Bloques) pct += x.Porcentaje;
        return b == null || pct <= 0 ? 0 : Math.Max(0, total - Fijo()) * b.Porcentaje / pct;
    }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        List<object> l = new List<object>();
        foreach (BloqueTV b in Bloques)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["clave"] = b.Clave; x["nombre"] = b.Nombre; x["tipo"] = b.Tipo; x["segundos"] = b.Segundos;
            x["porcentaje"] = b.Porcentaje; x["descripcion"] = b.Descripcion;
            string a = Archivo(b.Clave);
            if (a.Length > 0) x["archivo"] = a;
            l.Add(x);
        }
        d["bloques"] = l;
        return d;
    }

    public static PlantillaTV Leer(object o)
    {
        List<object> l = Json.Lista(o, "bloques");
        if (l.Count == 0) return PorDefecto();
        PlantillaTV p = new PlantillaTV();
        foreach (object x in l)
        {
            BloqueTV b = new BloqueTV(Json.Texto(x, "clave"), Json.Texto(x, "nombre"), Json.Texto(x, "tipo"),
                                      Json.Numero(x, "segundos", 0), Json.Numero(x, "porcentaje", 0), Json.Texto(x, "descripcion"));
            if (b.Clave.Length == 0) continue;
            p.Bloques.Add(b);
            string a = Json.Texto(x, "archivo");
            if (a.Length > 0) p.Kit[b.Clave] = a;
        }
        return p;
    }
}

// Tema asignado (principal o de un personaje), con sus variantes.
public class TemaAsignado
{
    public string Archivo = "", Motivo = "";
    public List<string> Variantes = new List<string>();

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["archivo"] = Archivo; d["motivo"] = Motivo; d["variantes"] = new List<object>(Variantes.ToArray());
        return d;
    }

    public static TemaAsignado Leer(object o)
    {
        if (o == null) return null;
        TemaAsignado t = new TemaAsignado();
        t.Archivo = Json.Texto(o, "archivo"); t.Motivo = Json.Texto(o, "motivo");
        foreach (object x in Json.Lista(o, "variantes")) if (x is string) t.Variantes.Add((string)x);
        return t.Archivo.Length > 0 ? t : null;
    }
}

// Musica de la serie: la carpeta (con su indice), el reparto y sus temas.
// Una vez elegidos se mantienen en todos los capitulos.
public class MusicaSerie
{
    public string Carpeta = "", Reparto = "";
    public double VolumenDb = -21;   // nivel de la pista de musica al producir (el balance fino va en el paso final)
    public TemaAsignado Principal;
    public Dictionary<string, TemaAsignado> Personajes = new Dictionary<string, TemaAsignado>();

    // Ganancia lineal de la pista (1 = 0 dB).
    public static float Lineal(double db) { return (float)Math.Pow(10, db / 20.0); }

    // Nombres del reparto: "Nombre: como es" o "Nombre - como es", uno por linea.
    public List<string> Nombres()
    {
        List<string> r = new List<string>();
        foreach (string l in (Reparto ?? "").Replace("\r", "").Split('\n'))
        {
            string n = l.Split(new char[] { ':', '\u2014', '\u2013' }, 2)[0];
            int g = n.IndexOf(" - ");
            if (g > 0) n = n.Substring(0, g);
            n = n.Trim().TrimStart('-', '*', '\u2022').Trim();
            if (n.Length > 0 && !r.Contains(n)) r.Add(n);
        }
        return r;
    }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["carpeta"] = Carpeta; d["reparto"] = Reparto; d["volumen_db"] = VolumenDb;
        if (Principal != null) d["principal"] = Principal.Escribir();
        Dictionary<string, object> p = new Dictionary<string, object>();
        foreach (KeyValuePair<string, TemaAsignado> kv in Personajes) p[kv.Key] = kv.Value.Escribir();
        d["personajes"] = p;
        return d;
    }

    public static MusicaSerie Leer(object o)
    {
        MusicaSerie m = new MusicaSerie();
        if (o == null) return m;
        m.Carpeta = Json.Texto(o, "carpeta"); m.Reparto = Json.Texto(o, "reparto");
        m.VolumenDb = Math.Max(-60, Math.Min(0, Json.Numero(o, "volumen_db", -21)));
        m.Principal = TemaAsignado.Leer(Json.Valor(o, "principal"));
        Dictionary<string, object> p = Json.Obj(o, "personajes");
        if (p != null)
            foreach (KeyValuePair<string, object> kv in p)
            {
                TemaAsignado t = TemaAsignado.Leer(kv.Value);
                if (t != null) m.Personajes[kv.Key] = t;
            }
        return m;
    }

    // ------------------------------------------- elegir los temas con Gemini

    // Un tema por grupo de variantes (para no ofrecer el mismo tema dos veces).
    public static List<ArchivoMusica> Candidatos(BibliotecaMusica b)
    {
        List<ArchivoMusica> r = new List<ArchivoMusica>();
        Dictionary<string, bool> vistos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (ArchivoMusica a in b.Archivos)
        {
            bool sirve = a.Sirve;
            if (!sirve || vistos.ContainsKey(a.Ruta)) continue;
            vistos[a.Ruta] = true;
            foreach (string v in a.Variantes) vistos[v] = true;
            r.Add(a);
        }
        return r;
    }

    public static string Instrucciones()
    {
        return "Eres supervisor musical de una serie de YouTube de Minecraft con amigos, editada como un anime de JoJo's " +
               "Bizarre Adventure. Elige de la biblioteca el TEMA PRINCIPAL de la serie (suena en los momentos clave y en las " +
               "victorias) y UN TEMA PARA CADA PERSONAJE del reparto (suena cuando ese personaje se luce o entra en escena).\n\n" +
               "Reglas:\n- Usa la personalidad de cada personaje y la premisa de la serie. Prefiere temas que en el anime ya son " +
               "de un personaje parecido (\"tema de\") o que suenan en escenas que le quedan.\n" +
               "- Un tema distinto para cada personaje y distinto del principal.\n" +
               "- Respeta las preferencias del editor si las hay.\n" +
               "- Solo ids de la lista.\n\n" +
               "Responde SOLO con JSON:\n{\"principal\": {\"id\": n, \"motivo\": \"...\"}, " +
               "\"personajes\": [{\"nombre\": \"...\", \"id\": n, \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(List<ArchivoMusica> candidatos, string premisa, string reparto, string preferencias)
    {
        StringBuilder sb = new StringBuilder();
        if (!String.IsNullOrEmpty(premisa)) sb.Append("PREMISA DE LA SERIE:\n" + premisa.Trim() + "\n\n");
        sb.Append("REPARTO (nombre: c\u00f3mo es):\n" + (reparto ?? "").Trim() + "\n\n");
        if (!String.IsNullOrEmpty(preferencias)) sb.Append("PREFERENCIAS DEL EDITOR:\n" + preferencias.Trim() + "\n\n");
        sb.Append("BIBLIOTECA [id] t\u00edtulo (de d\u00f3nde) | \u00e1nimo | tema de | d\u00f3nde suena en el anime\n");
        for (int i = 0; i < candidatos.Count; i++)
        {
            ArchivoMusica a = candidatos[i];
            sb.Append("[" + i + "] " + a.Titulo + " (" + (a.Parte.Length > 0 ? a.Parte : a.Fuente) + ")");
            if (a.Animos.Count > 0) sb.Append(" | " + String.Join(", ", a.Animos.ToArray()));
            if (a.TemaDe.Count > 0) sb.Append(" | tema de " + String.Join(", ", a.TemaDe.ToArray()));
            if (a.Escenas.Count > 0) sb.Append(" | " + String.Join("; ", a.Escenas.GetRange(0, Math.Min(2, a.Escenas.Count)).ToArray()));
            else if (a.Descripcion.Length > 0) sb.Append(" | " + a.Descripcion);
            sb.Append("\n");
        }
        return sb.ToString();
    }

    static TemaAsignado Asignar(List<ArchivoMusica> c, object x)
    {
        int id = (int)Json.Numero(x, "id", -1);
        if (id < 0 || id >= c.Count) return null;
        TemaAsignado t = new TemaAsignado();
        t.Archivo = c[id].Ruta; t.Motivo = Json.Texto(x, "motivo");
        t.Variantes.AddRange(c[id].Variantes);
        return t;
    }

    // Aplica la respuesta: solo los personajes del reparto y sin repetir temas.
    public int Aplicar(string json, List<ArchivoMusica> candidatos)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        int n = 0;
        TemaAsignado p = Asignar(candidatos, Json.Valor(o, "principal"));
        if (p != null) { Principal = p; n++; }
        List<string> nombres = Nombres();
        Dictionary<string, bool> usados = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (Principal != null) usados[Principal.Archivo] = true;
        foreach (object x in Json.Lista(o, "personajes"))
        {
            string nombre = Json.Texto(x, "nombre").Trim();
            string real = nombres.Find(delegate (string q) { return String.Equals(q, nombre, StringComparison.OrdinalIgnoreCase); });
            if (real == null) continue;
            TemaAsignado t = Asignar(candidatos, x);
            if (t == null || usados.ContainsKey(t.Archivo)) continue;
            usados[t.Archivo] = true;
            Personajes[real] = t;
            n++;
        }
        return n;
    }
}

// ---- src/comun/BibliotecaMusica.cs ----

// =====================================================================
// Biblioteca de musica: recorre la carpeta de musica, empareja cada archivo
// con su tema del anime (CatalogoAnime: escenas donde suena) y le pone
// estados de animo, la parte del episodio donde suele ir y de que personaje
// es tema. Se guarda en "<carpeta>\musica-indice.json".
// =====================================================================

public class ArchivoMusica
{
    public string Ruta = "", Titulo = "", Album = "", Fuente = "", TemaAnime = "", Parte = "";
    public double Duracion;
    public int Usos, LargoTipico;
    public List<string> Animos = new List<string>(), TemaDe = new List<string>(), Escenas = new List<string>(), Variantes = new List<string>();
    public string Momento = "";     // donde suena mas: inicio, medio, final, avance, eyecatch...
    public string Descripcion = ""; // como suena (de la IA, para lo que no es del anime)
    public bool Etiquetado;         // animos puestos por la IA (juegos, fanmade...)
    public bool ConUso { get { return Usos > 0; } }
    // Sirve para elegir musica: tiene datos del anime, es fanmade de SBR o ya esta etiquetado.
    public bool Sirve { get { return ConUso || Fuente == "SBR fan" || (Etiquetado && Animos.Count > 0); } }
}

// Lo que se sabe de un archivo antes de emparejarlo (de sus etiquetas o del CSV).
public class FilaMusica
{
    public string Ruta = "", Titulo = "", Album = "";
    public double Duracion;
}

public class BibliotecaMusica
{
    public string Carpeta = "";
    public List<ArchivoMusica> Archivos = new List<ArchivoMusica>();
    public const string NombreIndice = "musica-indice.json";
    public static readonly string[] Extensiones = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".opus", ".aac", ".wma" };

    // ----------------------------------------------------------- normalizar

    public static string Norm(string t)
    {
        string s = (t ?? "").Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c < 128) sb.Append(c);
        s = sb.ToString().ToLowerInvariant();
        s = Regex.Replace(s, @"^\s*\d{1,3}[\s.\-_]+", "");
        s = Regex.Replace(s, @"\(.*?\)|\[.*?\]|~.*?~", " ");
        s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // Titulo sin la version: "Rest ~Piano Ver.~", "Fighting Gold (Instrumental)" -> mismo nucleo.
    public static string Nucleo(string t)
    {
        string s = Regex.Replace(t ?? "", @"(?i)\s*[-\u2013(\[~]?\s*(piano|instrumental|english|tv size|tv|full|short|extended|remix|giorno|diavolo|units|acoustic|orchestra)\s*(ver(sion)?\.?)?\s*[)\]~]?", " ");
        return Norm(s);
    }

    static readonly string[][] Fuentes = {
        new[] { "vento aureo soundtrack", "videojuego" },
        new[] { "stardust crusaders", "SC" }, new[] { "golden wind|vento aureo|giogio", "GW" }, new[] { "diamond is unbreakable|morioh", "DU" },
        new[] { "stone ocean", "SO" }, new[] { @"phantom blood.*o\.?s\.?t|battle tendency", "PB/BT" }, new[] { "steel ball run|gwinn", "SBR fan" },
        new[] { @"all star battle|eyes of heaven|ora ora overdrive|heritage for the future|\brpg\b|stardust shooters|video game|diamond records|ps3", "videojuego" },
        new[] { @"\bova\b|2000", "OVA" }, new[] { @"anthology|op\d? single|theme song|opening|ending", "canci\u00f3n" },
    };

    public static string Fuente(string album, string ruta)
    {
        string t = (album + " " + ruta).ToLowerInvariant();
        foreach (string[] f in Fuentes) if (Regex.IsMatch(t, f[0])) return f[1];
        return "otro";
    }

    static readonly string[] ClavesOst = { "departure", "journey", "world", "destination", "overture", "intermezzo", "finale",
                                           "good morning", "good night", "future", "destiny", "musik", "leicht", "stone ocean" };

    static readonly Dictionary<string, string> Animo = new Dictionary<string, string> {
        { "pelea", "battle|fight|clash|duel|assault|attack|vs|fist|rush|showdown|combat|pelea|batalla|combate|lucha|jefe" },
        { "tension", "tension|imminen|crisis|danger|threat|pursuit|approach|creeping|urgency|omen|foreboding|unease|anxiety|chase|tension|suspenso|peligro|persecucion" },
        { "villano", "dio|evil|dark|devil|villain|boss|kira|diavolo|pucci|killer|enemy|rebirth|malice|sinister|villano|malvado|oscuro" },
        { "misterio", "myster|strange|bizarre|enigma|secret|plot|mist|unknown|question|riddle|misterioso|misterio|cueva|cave" },
        { "comedia", "comic|funny|jolly|silly|comical|humor|playful|cheer|comedia|gracios|divertid" },
        { "viaje", "journey|travel|departure|sightseeing|wilderness|road|desert|wind|voyage|setting off|ride|horse|run|viaje|aventura|explora|overworld" },
        { "calma", "calm|rest|peace|gentle|repose|daily|morning|sunlight|quiet|serene|night|calma|tranquil|relaj|menu|lobby|ambient" },
        { "tristeza", "sad|sorrow|tears|requiem|farewell|grief|lament|memory|memories|hesitation|loneliness|triste|tristeza|melancol" },
        { "victoria", "victory|triumph|glory|hero|pride|proud|win|victoria|triunfo" },
        { "epico", "theme|crusaders|stardust|golden|giorno|decisive|final|vento|oro|awakening|platinum|fate|destiny|epico|epic|heroic" },
    };

    // ------------------------------------------------------------- catalogo

    class Tema
    {
        public string T, N, P, O;
        public int U, L;
        public List<string> A = new List<string>(), D = new List<string>(), E = new List<string>();
        public string M = "";
        public Dictionary<string, bool> Pares;
    }

    static List<Tema> catalogo;
    static Dictionary<string, string> alias;

    static void CargarCatalogo()
    {
        if (catalogo != null) return;
        catalogo = new List<Tema>();
        alias = new Dictionary<string, string>();
        object o = Json.Leer(CatalogoAnime.Json);
        foreach (object x in Json.Lista(o, "temas"))
        {
            Tema t = new Tema();
            t.T = Json.Texto(x, "t"); t.N = Json.Texto(x, "n"); t.P = Json.Texto(x, "p"); t.O = Json.Texto(x, "o");
            t.U = (int)Json.Numero(x, "u", 0); t.L = (int)Json.Numero(x, "l", 0);
            foreach (object a in Json.Lista(x, "a")) t.A.Add((string)a);
            foreach (object a in Json.Lista(x, "d")) t.D.Add((string)a);
            foreach (object a in Json.Lista(x, "e")) t.E.Add((string)a);
            int mejor = -1;
            Dictionary<string, object> m = Json.Obj(x, "m");
            if (m != null) foreach (KeyValuePair<string, object> kv in m) if (Convert.ToInt32(kv.Value) > mejor) { mejor = Convert.ToInt32(kv.Value); t.M = kv.Key; }
            t.Pares = Pares(t.N);
            catalogo.Add(t);
        }
        Dictionary<string, object> al = Json.Obj(o, "alias");
        if (al != null) foreach (KeyValuePair<string, object> kv in al) alias[kv.Key] = Norm((string)kv.Value);
    }

    // Pares de letras (para descartar rapido los que no se parecen nada).
    static Dictionary<string, bool> Pares(string s)
    {
        Dictionary<string, bool> d = new Dictionary<string, bool>();
        for (int i = 0; i + 1 < s.Length; i++) d[s.Substring(i, 2)] = true;
        return d;
    }

    static double Dice(Dictionary<string, bool> a, Dictionary<string, bool> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        int c = 0;
        foreach (string k in a.Keys) if (b.ContainsKey(k)) c++;
        return 2.0 * c / (a.Count + b.Count);
    }

    // Parecido entre dos textos (0..1), como difflib: 2*coincidencias/(largo total).
    public static double Parecido(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        return 2.0 * Coincidencias(a, 0, a.Length, b, 0, b.Length) / (a.Length + b.Length);
    }

    static int Coincidencias(string a, int a0, int a1, string b, int b0, int b1)
    {
        int mejor = 0, ia = 0, ib = 0;
        for (int i = a0; i < a1; i++)
            for (int j = b0; j < b1; j++)
            {
                int k = 0;
                while (i + k < a1 && j + k < b1 && a[i + k] == b[j + k]) k++;
                if (k > mejor) { mejor = k; ia = i; ib = j; }
            }
        if (mejor == 0) return 0;
        return mejor + Coincidencias(a, a0, ia, b, b0, ib) + Coincidencias(a, ia + mejor, a1, b, ib + mejor, b1);
    }

    static Tema Emparejar(string titulo, string album, string fuente)
    {
        string n = Norm(titulo);
        if (n.Length == 0) return null;
        string parte = fuente == "SC" || fuente == "GW" || fuente == "DU" || fuente == "SO" || fuente == "PB/BT" ? fuente : null;
        string objetivo = null;
        if ((fuente == "SC" || fuente == "GW") && alias.ContainsKey(n)) objetivo = alias[n];
        Tema mejor = null;
        double puntaje = 0;
        if (objetivo == null && fuente != "SC" && fuente != "GW" && fuente != "DU" && fuente != "SO" && fuente != "PB/BT" &&
            fuente != "canci\u00f3n" && fuente != "otro") return null;
        string alb = (album ?? "").ToLowerInvariant();
        Dictionary<string, bool> pn = Pares(n);
        foreach (Tema t in catalogo)
        {
            double s;
            if (objetivo != null) s = t.N == objetivo ? 1.5 : 0;
            else
            {
                if (Math.Abs(t.N.Length - n.Length) > Math.Max(t.N.Length, n.Length) / 2 + 3) continue;
                if (n != t.N && Dice(pn, t.Pares) < 0.5) continue;
                s = Parecido(n, t.N);
                if (t.O.Length > 0 && alb.Contains(t.O)) s += 0.15;
            }
            if (parte != null && t.P == parte) s += 0.001;     // desempata: el mismo nombre en su propia parte
            if (s > puntaje) { mejor = t; puntaje = s; }
        }
        return puntaje >= 0.86 ? mejor : null;
    }

    // ------------------------------------------------------------- indexar

    public static BibliotecaMusica Indexar(string carpeta, List<FilaMusica> filas, Action<string, double> avance)
    {
        CargarCatalogo();
        BibliotecaMusica b = new BibliotecaMusica();
        b.Carpeta = carpeta;
        for (int i = 0; i < filas.Count; i++)
        {
            FilaMusica f = filas[i];
            if (avance != null && i % 25 == 0) avance("Emparejando " + (i + 1) + " de " + filas.Count + "\u2026", (double)i / filas.Count);
            ArchivoMusica a = new ArchivoMusica();
            a.Ruta = f.Ruta; a.Titulo = f.Titulo.Length > 0 ? f.Titulo : Path.GetFileNameWithoutExtension(f.Ruta);
            a.Album = f.Album; a.Duracion = f.Duracion;
            a.Fuente = Fuente(a.Album, a.Ruta);
            Tema t = Emparejar(a.Titulo, a.Album, a.Fuente);
            if (t != null)
            {
                a.TemaAnime = t.T; a.Parte = t.P; a.Usos = t.U; a.LargoTipico = t.L; a.Momento = t.M;
                a.Animos.AddRange(t.A); a.TemaDe.AddRange(t.D); a.Escenas.AddRange(t.E);
            }
            if (a.Animos.Count == 0)
                foreach (KeyValuePair<string, string> kv in Animo)
                    if (Regex.IsMatch(a.Titulo, kv.Value, RegexOptions.IgnoreCase) && a.Animos.Count < 2) a.Animos.Add(kv.Key);
            // Lo que no es del anime: la subcarpeta tambien dice el animo (\u00abJuegos/Pelea/...\u00bb).
            if (!a.ConUso && a.Fuente != "SBR fan")
            {
                List<string> porCarpeta = AnimosDeCarpeta(carpeta, a.Ruta);
                if (porCarpeta.Count > 0)
                {
                    a.Animos.Clear(); a.Animos.AddRange(porCarpeta);
                    a.Etiquetado = true; a.Descripcion = "por su carpeta";
                }
            }
            b.Archivos.Add(a);
        }
        b.MarcarVariantes();
        return b;
    }

    // Animos que dicen las subcarpetas (dentro de la carpeta de la musica), por palabra completa.
    public static List<string> AnimosDeCarpeta(string carpeta, string ruta)
    {
        List<string> r = new List<string>();
        string dir = Path.GetDirectoryName(ruta) ?? "";
        string raiz = (carpeta ?? "").TrimEnd('\\', '/');
        if (raiz.Length > 0 && dir.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) dir = dir.Substring(raiz.Length);
        else if (Path.IsPathRooted(dir)) return r;
        dir = Norm(dir.Replace('\\', ' ').Replace('/', ' '));
        foreach (KeyValuePair<string, string> kv in Animo)
            if (Regex.IsMatch(dir, @"(?<![a-z])(" + kv.Value + ")", RegexOptions.IgnoreCase) && r.Count < 2) r.Add(kv.Key);
        return r;
    }

    // Variantes: el mismo tema en otra version o en otro album.
    public void MarcarVariantes()
    {
        Dictionary<string, List<ArchivoMusica>> grupos = new Dictionary<string, List<ArchivoMusica>>();
        foreach (ArchivoMusica a in Archivos)
        {
            string k = a.TemaAnime.Length > 0 ? "t:" + Norm(a.TemaAnime) + "|" + a.Parte : "n:" + Nucleo(a.Titulo);
            if (k.Length <= 2) continue;
            List<ArchivoMusica> l;
            if (!grupos.TryGetValue(k, out l)) { l = new List<ArchivoMusica>(); grupos[k] = l; }
            l.Add(a);
        }
        foreach (List<ArchivoMusica> l in grupos.Values)
            foreach (ArchivoMusica a in l)
            {
                a.Variantes.Clear();
                foreach (ArchivoMusica b in l) if (b != a) a.Variantes.Add(b.Ruta);
            }
    }

    // Archivos de la carpeta con sus etiquetas (titulo, album, duracion) leidas
    // por el Explorador de Windows; si no se puede, solo el nombre del archivo.
    public static List<FilaMusica> Escanear(string carpeta, Action<string, double> avance)
    {
        List<string> rutas = new List<string>();
        foreach (string f in Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories))
            if (Array.IndexOf(Extensiones, Path.GetExtension(f).ToLowerInvariant()) >= 0) rutas.Add(f);
        rutas.Sort(StringComparer.OrdinalIgnoreCase);
        object shell = null;
        Type tipo = null;
        try { tipo = Type.GetTypeFromProgID("Shell.Application"); if (tipo != null) shell = Activator.CreateInstance(tipo); } catch { shell = null; }
        Dictionary<string, object> carpetas = new Dictionary<string, object>();
        List<FilaMusica> filas = new List<FilaMusica>();
        string raiz = carpeta.TrimEnd('\\', '/');
        for (int i = 0; i < rutas.Count; i++)
        {
            if (avance != null && i % 20 == 0) avance("Leyendo " + (i + 1) + " de " + rutas.Count + "\u2026", (double)i / Math.Max(1, rutas.Count));
            string r = rutas[i];
            FilaMusica f = new FilaMusica();
            f.Ruta = r.Substring(raiz.Length + 1);
            if (shell != null)
                try
                {
                    string dir = Path.GetDirectoryName(r);
                    object ns;
                    if (!carpetas.TryGetValue(dir, out ns))
                    {
                        ns = tipo.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { dir });
                        carpetas[dir] = ns;
                    }
                    object item = ns.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, ns, new object[] { Path.GetFileName(r) });
                    f.Titulo = Detalle(ns, item, 21);
                    f.Album = Detalle(ns, item, 14);
                    f.Duracion = Segundos(Detalle(ns, item, 27));
                }
                catch { }
            filas.Add(f);
        }
        return filas;
    }

    static string Detalle(object ns, object item, int i)
    {
        object v = ns.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, ns, new object[] { item, i });
        return (v as string ?? "").Replace("\u200e", "").Replace("\u200f", "").Trim();
    }

    public static double Segundos(string d)
    {
        double s = 0;
        foreach (string p in (d ?? "").Split(':'))
        {
            int x;
            if (!int.TryParse(p.Trim(), out x)) return 0;
            s = s * 60 + x;
        }
        return s;
    }

    // El listado de PowerShell (musica.csv), por si se prefiere.
    public static List<FilaMusica> DesdeCsv(string ruta)
    {
        List<FilaMusica> filas = new List<FilaMusica>();
        string[] lineas = File.ReadAllLines(ruta, Encoding.UTF8);
        if (lineas.Length == 0) return filas;
        List<string> cab = Csv(lineas[0]);
        int iR = cab.IndexOf("Ruta"), iT = cab.IndexOf("Titulo"), iA = cab.IndexOf("Album"), iD = cab.IndexOf("Duracion"), iN = cab.IndexOf("Archivo");
        for (int k = 1; k < lineas.Length; k++)
        {
            List<string> c = Csv(lineas[k]);
            if (iR < 0 || c.Count <= iR) continue;
            FilaMusica f = new FilaMusica();
            f.Ruta = c[iR];
            f.Titulo = iT >= 0 && iT < c.Count && c[iT].Length > 0 ? c[iT] : (iN >= 0 && iN < c.Count ? c[iN] : "");
            f.Album = iA >= 0 && iA < c.Count ? c[iA] : "";
            f.Duracion = iD >= 0 && iD < c.Count ? Segundos(c[iD]) : 0;
            filas.Add(f);
        }
        return filas;
    }

    static List<string> Csv(string l)
    {
        List<string> r = new List<string>();
        StringBuilder sb = new StringBuilder();
        bool comillas = false;
        for (int i = 0; i < l.Length; i++)
        {
            char c = l[i];
            if (comillas)
            {
                if (c == '"' && i + 1 < l.Length && l[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') comillas = false;
                else sb.Append(c);
            }
            else if (c == '"') comillas = true;
            else if (c == ',') { r.Add(sb.ToString()); sb.Length = 0; }
            else sb.Append(c);
        }
        r.Add(sb.ToString());
        if (r.Count > 0) r[0] = r[0].TrimStart('\ufeff');
        return r;
    }

    // ------------------------------------------------------ guardar y cargar

    public void Guardar(string ruta)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-musica";
        d["carpeta"] = Carpeta;
        d["fecha"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        List<object> l = new List<object>();
        foreach (ArchivoMusica a in Archivos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["ruta"] = a.Ruta; x["titulo"] = a.Titulo; x["album"] = a.Album; x["duracion"] = Math.Round(a.Duracion);
            x["fuente"] = a.Fuente; x["animos"] = new List<object>(a.Animos.ToArray());
            if (a.ConUso)
            {
                x["tema_anime"] = a.TemaAnime; x["parte"] = a.Parte; x["usos"] = a.Usos; x["largo_tipico"] = a.LargoTipico;
                x["momento"] = a.Momento; x["escenas"] = new List<object>(a.Escenas.ToArray());
                if (a.TemaDe.Count > 0) x["tema_de"] = new List<object>(a.TemaDe.ToArray());
            }
            else if (a.Etiquetado)
            {
                x["ia"] = true; x["momento"] = a.Momento; x["descripcion"] = a.Descripcion;
            }
            if (a.Variantes.Count > 0) x["variantes"] = new List<object>(a.Variantes.ToArray());
            l.Add(x);
        }
        d["archivos"] = l;
        File.WriteAllText(ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    public static BibliotecaMusica Cargar(string carpeta)
    {
        string ruta = Path.Combine(carpeta, NombreIndice);
        if (!File.Exists(ruta)) return null;
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        BibliotecaMusica b = new BibliotecaMusica();
        b.Carpeta = carpeta;
        foreach (object x in Json.Lista(o, "archivos"))
        {
            ArchivoMusica a = new ArchivoMusica();
            a.Ruta = Json.Texto(x, "ruta"); a.Titulo = Json.Texto(x, "titulo"); a.Album = Json.Texto(x, "album");
            a.Duracion = Json.Numero(x, "duracion", 0); a.Fuente = Json.Texto(x, "fuente");
            a.TemaAnime = Json.Texto(x, "tema_anime"); a.Parte = Json.Texto(x, "parte");
            a.Usos = (int)Json.Numero(x, "usos", 0); a.LargoTipico = (int)Json.Numero(x, "largo_tipico", 0);
            a.Momento = Json.Texto(x, "momento"); a.Descripcion = Json.Texto(x, "descripcion");
            object ia = Json.Valor(x, "ia");
            a.Etiquetado = ia is bool && (bool)ia;
            foreach (object y in Json.Lista(x, "animos")) a.Animos.Add((string)y);
            foreach (object y in Json.Lista(x, "tema_de")) a.TemaDe.Add((string)y);
            foreach (object y in Json.Lista(x, "escenas")) a.Escenas.Add((string)y);
            foreach (object y in Json.Lista(x, "variantes")) a.Variantes.Add((string)y);
            b.Archivos.Add(a);
        }
        return b;
    }

    // ------------------------------------- etiquetar con IA (juegos, fanmade)

    public static readonly string[] Animos = { "calma", "viaje", "comedia", "misterio", "tension", "pelea", "villano", "epico",
                                               "victoria", "tristeza" };

    // Al volver a indexar, lo que la IA ya etiqueto se conserva.
    public void ConservarEtiquetas(BibliotecaMusica vieja)
    {
        if (vieja == null) return;
        foreach (ArchivoMusica a in Archivos)
        {
            if (a.ConUso) continue;
            ArchivoMusica v = vieja.Buscar(a.Ruta);
            if (v == null || !v.Etiquetado) continue;
            a.Etiquetado = true; a.Momento = v.Momento; a.Descripcion = v.Descripcion;
            a.Animos.Clear(); a.Animos.AddRange(v.Animos);
        }
    }

    // Lo que no tiene datos del anime ni etiquetas de la IA.
    public List<ArchivoMusica> PorEtiquetar()
    {
        List<ArchivoMusica> r = new List<ArchivoMusica>();
        foreach (ArchivoMusica a in Archivos) if (!a.ConUso && !a.Etiquetado && a.Fuente != "SBR fan") r.Add(a);
        return r;
    }

    public static string InstruccionesEtiquetar()
    {
        return "Eres supervisor musical de una serie de YouTube de Minecraft editada como un anime. Te paso archivos de m\u00fasica que " +
               "no son del anime (bandas sonoras de videojuegos, fanmade, remixes...) con su t\u00edtulo, \u00e1lbum y carpeta. Por lo que " +
               "sabes de cada tema (si lo conoces) o por su t\u00edtulo, \u00e1lbum y carpeta, di c\u00f3mo suena y para qu\u00e9 escenas sirve.\n" +
               "- \"animos\": 1 a 3 de: " + String.Join(", ", Animos) + ".\n" +
               "- \"momento\": d\u00f3nde queda mejor (inicio, exploraci\u00f3n, construcci\u00f3n, pelea, jefe, cliffhanger, ep\u00edlogo, men\u00fa...).\n" +
               "- \"descripcion\": c\u00f3mo suena, en pocas palabras (instrumentos, tempo, energ\u00eda).\n" +
               "- Si no tienes idea de c\u00f3mo suena uno, no lo pongas (mejor nada que inventar).\n" +
               "Responde SOLO con JSON: {\"temas\": [{\"id\": n, \"animos\": [\"...\"], \"momento\": \"...\", \"descripcion\": \"...\"}]}";
    }

    public string MensajeEtiquetar(List<ArchivoMusica> lote)
    {
        StringBuilder sb = new StringBuilder("ARCHIVOS [id] t\u00edtulo | \u00e1lbum | carpeta | duraci\u00f3n\n");
        for (int i = 0; i < lote.Count; i++)
        {
            ArchivoMusica a = lote[i];
            string dir = Path.GetDirectoryName(a.Ruta) ?? "";
            sb.Append("[" + i + "] " + a.Titulo + " | " + a.Album + " | " + dir + " | " + Math.Round(a.Duracion) + " s\n");
        }
        return sb.ToString();
    }

    // Pone las etiquetas de la respuesta; devuelve cuantos quedaron etiquetados.
    public static int AplicarEtiquetas(List<ArchivoMusica> lote, string json)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        int n = 0;
        foreach (object x in Json.Lista(o, "temas"))
        {
            int id = (int)Json.Numero(x, "id", -1);
            if (id < 0 || id >= lote.Count) continue;
            List<string> an = new List<string>();
            foreach (object y in Json.Lista(x, "animos"))
            {
                string k = Norm(y as string).Replace(" ", "");
                if (Array.IndexOf(Animos, k) >= 0 && !an.Contains(k)) an.Add(k);
            }
            if (an.Count == 0) continue;
            ArchivoMusica a = lote[id];
            a.Animos.Clear(); a.Animos.AddRange(an);
            a.Momento = Json.Texto(x, "momento"); a.Descripcion = Json.Texto(x, "descripcion");
            a.Etiquetado = true;
            n++;
        }
        return n;
    }

    public ArchivoMusica Buscar(string ruta)
    {
        foreach (ArchivoMusica a in Archivos) if (String.Equals(a.Ruta, ruta, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }

    public string Completa(string ruta) { return Path.Combine(Carpeta, ruta); }
}

// ---- src/comun/CatalogoAnime.cs ----

// GENERADO con herramientas/catalogo_musica.py desde las listas de musica de jojowiki
// (ejemplos/subs): 489 temas del anime con como se usan. No editar a mano.
public static class CatalogoAnime
{
    public static readonly string Json = String.Concat(new string[] {
        "{\"temas\":[{\"t\":\"A Bizarre Hunch\",\"n\":\"a bizarre hunch\",\"p\":\"DU\",\"o\":\"\",\"u\":8,\"l\":48,\"a\":[\"misterio\"],\"m\":{\"medio\":7,\"final\":1},\"d\":[],\"e\":[\"Learning English with Yukako\",\"The intricacies of baby gear\",\"Koichi doesn't remember what's wrong\",\"Treating Shigechi carefully\"]},{\"t\":\"A Bizarre Hunch\",\"n\":\"a bizarre hunch\",\"p\":\"SO\",\"o\":\"\",\"u\":1,\"l\":23,\"a\":[\"tension\",\"misterio\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Kenzou is trapped in a trash can beyond recovery\"]},{\"t\":\"A Duet of Courage\",\"n\":\"a duet of courage\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":5,\"l\":87,\"a\":[\"pelea\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[],\"e\":[\"Jonathan defeats Speedwagon with a single kick.\",\"The Joestar Mansion set on fire.\",\"Dio is stronger than both JoJo and Zeppeli.\",\"JoJo and Bruford continue to fight.\"]},{\"t\":\"A Fine Fellow Appears\",\"n\":\"a fine fellow appears\",\"p\":\"SC\",\"o\":\"journey\",\"u\":9,\"l\":36,\"a\":[\"viaje\"],\"m\":{\"final\":3,\"medio\":3,\"inicio\":2,\"recap\":1},\"d\":[],\"e\":[\"Arrival in Singapore\",\"Nena's demise\",\"The pathetic ZZ\",\"Arrival in Karachi\"]},{\"t\":\"A Fine Fellow Arrives\",\"n\":\"a fine fellow arrives\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":1,\"l\":59,\"a\":[\"villano\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Speedwagon sees through Dio's facade.\"]},{\"t\":\"a Little Bird\",\"n\":\"a little bird\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":3,\"l\":121,\"a\":[\"pelea\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Gwess and Pi-chan/A corpse in the bird\",\"Jolyne becomes Gwess's pet\",\"Goo Goo Dolls attacks\"]},{\"t\":\"A Lurking Evil\",\"n\":\"a lurking evil\",\"p\":\"DU\",\"o\":\"good night\",\"u\":20,\"l\":68,\"a\":[\"villano\"],\"m\":{\"medio\":12,\"inicio\":7,\"final\":1},\"d\":[],\"e\":[\"A curiously empty street\",\"Koichi warns Josuke & Okuyasu\",\"Receiving the paycheck/The banker is suspicious\",\"Okuyasu's stratagem\"]},{\"t\":\"A Lurking Evil\",\"n\":\"a lurking evil\",\"p\":\"GW\",\"o\":\"good night\",\"u\":2,\"l\":108,\"a\":[\"explicacion\",\"villano\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"The team thinks of a plan to stop Sale from getting the money\",\"Unknown ally explains the origins of the Arrow\"]},{\"t\":\"A Lurking Evil\",\"n\":\"a lurking evil\",\"p\":",
        "\"SO\",\"o\":\"good night\",\"u\":2,\"l\":94,\"a\":[\"villano\",\"explicacion\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Anasui explains assassination feng shui\",\"Kenzou vows to reign as founder once again\"]},{\"t\":\"A Message to My Friends\",\"n\":\"a message to my friends\",\"p\":\"SC\",\"o\":\"destination\",\"u\":2,\"l\":76,\"a\":[\"tristeza\",\"epico\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Kakyoin remembers how DIO swayed him\",\"Kakyoin's last message\"]},{\"t\":\"A Moment's Happiness\",\"n\":\"a moment s happiness\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":3,\"l\":45,\"a\":[\"tension\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Erina returns Jonathan's handkerchief.\",\"Jonathan and Erina are married.\",\"An old photo.\"]},{\"t\":\"A Party of Stardust\",\"n\":\"a party of stardust\",\"p\":\"DU\",\"o\":\"destination\",\"u\":1,\"l\":8,\"a\":[\"pelea\",\"epico\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Star Platinum attacks SHA again\"]},{\"t\":\"A Party of Stardust\",\"n\":\"a party of stardust\",\"p\":\"SC\",\"o\":\"destination\",\"u\":5,\"l\":54,\"a\":[\"epico\"],\"m\":{\"recap\":2,\"final\":2,\"medio\":1},\"d\":[],\"e\":[\"Recap\",\"Jotaro swiftly defeats the enemy\",\"Recap: Joseph & Avdol have been magnetized\",\"Star Platinum pummels D'Arby\"]},{\"t\":\"A Piece of Stardust\",\"n\":\"a piece of stardust\",\"p\":\"SO\",\"o\":\"world\",\"u\":1,\"l\":11,\"a\":[\"epico\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Jolyne thinking about her father\"]},{\"t\":\"A Superhuman Reborn\",\"n\":\"a superhuman reborn\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":5,\"l\":80,\"a\":[\"villano\",\"pelea\",\"viaje\"],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"Dio is bulletproof now.\",\"Jack the Ripper attacks.\",\"Bruford & Tarkus are Dio's servants now.\",\"Doobie appears.\"]},{\"t\":\"A Well-Laid Trap\",\"n\":\"a well laid trap\",\"p\":\"DU\",\"o\":\"\",\"u\":8,\"l\":26,\"a\":[\"pelea\"],\"m\":{\"medio\":4,\"inicio\":4},\"d\":[],\"e\":[\"Rohan's trap complete\",\"Yoshihiro plans to create as many Stand users as he can\",\"Yoshihiro is observing the battle\",\"Yoshihiro strikes another person with the Arrow\"]},{\"t\":\"A Well-Laid Trap\",\"n\":\"a well laid trap\",\"p\":\"SO\",\"o\":\"\",\"u\":1,\"l\":92,\"a\":[\"pelea\"],\"m\":{\"recap\":1},\"d\":[],\"e\":[\"Recap: Anasui obliges/F.F. vs Kenzou\"]},{\"t\":\"abyss",
        "\",\"n\":\"abyss\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":10,\"l\":69,\"a\":[],\"m\":{\"medio\":8,\"final\":1,\"inicio\":1},\"d\":[\"joseph\"],\"e\":[\"Santana invades a Nazi soldier's body.\",\"Santana's Rib Blades./Santana discovers the Ripple.\",\"Santana invades Stroheim's body.\",\"Joseph's slipping.\"]},{\"t\":\"aereo da caccia\",\"n\":\"aereo da caccia\",\"p\":\"GW\",\"o\":\"overture\",\"u\":7,\"l\":47,\"a\":[\"pelea\"],\"m\":{\"medio\":4,\"final\":2,\"recap\":1},\"d\":[\"narancia\"],\"e\":[\"Narancia summons Aerosmith\",\"Recap of Narancia confronting Formaggio\",\"Narancia tries to call Bruno\",\"Aerosmith explodes some cars\"]},{\"t\":\"affection\",\"n\":\"affection\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":3,\"l\":100,\"a\":[\"calma\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"A bit of Joseph and Erina's story.\",\"Joseph knows that Speedwagon is alive.\",\"Lisa Lisa takes a bath.\"]},{\"t\":\"alba\",\"n\":\"alba\",\"p\":\"GW\",\"o\":\"overture\",\"u\":5,\"l\":28,\"a\":[\"tristeza\"],\"m\":{\"medio\":3,\"opening\":1,\"inicio\":1},\"d\":[],\"e\":[\"Opening shots of the city\",\"Girls ask Giorno for directions\",\"The team is sailing on a yacht\",\"Bucciarati's past\"]},{\"t\":\"Ally\",\"n\":\"ally\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":2,\"l\":40,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"F.F. wants to protect Jolyne\",\"Jolyne on Anasui's shoulder/Anasui gives Jolyne a ring\"]},{\"t\":\"An Alien??\",\"n\":\"an alien\",\"p\":\"DU\",\"o\":\"\",\"u\":7,\"l\":31,\"a\":[],\"m\":{\"medio\":6,\"inicio\":1},\"d\":[],\"e\":[\"They deflect the Arrow?\",\"Are tissues an earthen treat?\",\"Nu Mikitakazo Nshi, the self-proclaimed alien\",\"Mikitaka turns into super sneakers\"]},{\"t\":\"An Enveloping Peace\",\"n\":\"an enveloping peace\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":2,\"l\":87,\"a\":[\"calma\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"The history of Bruford and Tarkus.\",\"Tarkus is still alive.\"]},{\"t\":\"ancientry\",\"n\":\"ancientry\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":12,\"l\":90,\"a\":[\"pelea\"],\"m\":{\"medio\":6,\"inicio\":4,\"final\":2},\"d\":[],\"e\":[\"The Pillar Men seek the Super Aja.\",\"The Pillar Men are familiar with the Ripple.\",\"Wamuu and Esidisi's Wedding Rings of Death.\",\"Kars and the Stone of Aja.\"]},{\"t\":\"Anger\",\"n\":\"anger\",\"p\":\"S",
        "O\",\"o\":\"stone ocean\",\"u\":1,\"l\":97,\"a\":[\"viaje\",\"explicacion\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jolyne understands Planet Waves's ability/Jolyne defends herself against Westwood\"]},{\"t\":\"Another Face, Same Mind\",\"n\":\"another face same mind\",\"p\":\"DU\",\"o\":\"good night\",\"u\":9,\"l\":47,\"a\":[\"villano\"],\"m\":{\"medio\":7,\"inicio\":2},\"d\":[\"kira\"],\"e\":[\"Kosaku Kawajiri is not himself today\",\"Kira working to perfect his disguise\",\"Kira relieved for Shinobu\",\"Yet another flaw in Kira's disguise\"]},{\"t\":\"Another Face, Same Mind\",\"n\":\"another face same mind\",\"p\":\"SO\",\"o\":\"good night\",\"u\":1,\"l\":18,\"a\":[],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Jolyne forgetting while watching a movie\"]},{\"t\":\"appearance\",\"n\":\"appearance\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":4,\"l\":39,\"a\":[\"pelea\"],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"Battle Tendency title card\",\"Joseph steps in./Joseph's Ripple Clackers.\",\"Trial completed!\",\"Joseph says goodbye to Suzi Q.\"]},{\"t\":\"Approach\",\"n\":\"approach\",\"p\":\"DU\",\"o\":\"departure\",\"u\":1,\"l\":18,\"a\":[\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jotaro guesses SHA detects heat\"]},{\"t\":\"Approach\",\"n\":\"approach\",\"p\":\"SC\",\"o\":\"departure\",\"u\":21,\"l\":71,\"a\":[\"tension\"],\"m\":{\"medio\":13,\"final\":6,\"recap\":1,\"inicio\":1},\"d\":[],\"e\":[\"Jotaro shows his evil spirit\",\"Star Platinum reveals Hierophant Green\",\"The heroes learn DIO is in Egypt\",\"Joseph plans the sea route\"]},{\"t\":\"Approach\",\"n\":\"approach\",\"p\":\"SO\",\"o\":\"departure\",\"u\":1,\"l\":28,\"a\":[\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jotaro asks Jolyne to summon Stone Free\"]},{\"t\":\"ascensione\",\"n\":\"ascensione\",\"p\":\"GW\",\"o\":\"finale\",\"u\":2,\"l\":46,\"a\":[\"tristeza\",\"epico\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Giorno says goodbye to Narancia\",\"Giorno gets the Arrow\"]},{\"t\":\"assassinio\",\"n\":\"assassinio\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":10,\"l\":78,\"a\":[\"villano\",\"tension\"],\"m\":{\"medio\":7,\"inicio\":2,\"final\":1},\"d\":[],\"e\":[\"Zucchero demands Bruno to tell where the money is\",\"Formaggio shrinks a car\",\"Formaggio meets his team\",\"Sorbet and Gelato are missing\"]},{\"t\":\"assassinio\",\"n\":\"assassinio",
        "\",\"p\":\"SO\",\"o\":\"intermezzo\",\"u\":1,\"l\":85,\"a\":[\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Cape Canaveral's gravity/Pucci's face morphing\"]},{\"t\":\"attacco\",\"n\":\"attacco\",\"p\":\"GW\",\"o\":\"overture\",\"u\":3,\"l\":46,\"a\":[\"pelea\"],\"m\":{\"final\":2,\"inicio\":1},\"d\":[],\"e\":[\"Black Sabbath switches to Giorno and attacks him\",\"Formaggio attacks Narancia\",\"Formaggio hides inside Narancia's pocket\"]},{\"t\":\"attacco\",\"n\":\"attacco\",\"p\":\"SO\",\"o\":\"overture\",\"u\":2,\"l\":78,\"a\":[\"pelea\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Kenzou makes F.F.'s arm enter his Stand\",\"Ermes touches a rainbow while attacking Versus, snails emerge from her arm\"]},{\"t\":\"Avalon\",\"n\":\"avalon\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":3,\"l\":156,\"a\":[],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Kars has the Stone Mask and Super Aja!!!!/Apotheosis.\",\"Kars has tamed the sun!!!/Run away!!\",\"Kars can use the Ripple!/Joseph uses the Super Aja on Kars's Ripple.\"]},{\"t\":\"awake\",\"n\":\"awake\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":7,\"l\":94,\"a\":[],\"m\":{\"medio\":5,\"inicio\":2},\"d\":[],\"e\":[\"The Pillar Men awaken!\",\"Esidisi corners Joseph.\",\"Light blade Mode!/Kars takes the Super Aja.\",\"The real Kars backstabs Lisa Lisa!\"]},{\"t\":\"Awakening of the Evil Spirit\",\"n\":\"awakening of the evil spirit\",\"p\":\"SC\",\"o\":\"journey\",\"u\":6,\"l\":109,\"a\":[\"villano\",\"epico\"],\"m\":{\"medio\":4,\"inicio\":2},\"d\":[],\"e\":[\"Star Platinum appears\",\"Silver Chariot sheds its armor\",\"Kakyoin victimizes a pickpocket\",\"Jotaro unmasks Enya\"]},{\"t\":\"backfoot\",\"n\":\"backfoot\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":6,\"l\":110,\"a\":[\"tristeza\",\"pelea\"],\"m\":{\"medio\":4,\"inicio\":2},\"d\":[\"joseph\"],\"e\":[\"Straizo survives Joseph's tommy gun.\",\"Stroheim's sacrifice.\",\"Joseph gets serious.\",\"Joseph is a Ripple novice.\"]},{\"t\":\"Barbarism\",\"n\":\"barbarism\",\"p\":\"SC\",\"o\":\"journey\",\"u\":7,\"l\":76,\"a\":[\"tension\",\"pelea\"],\"m\":{\"medio\":6,\"inicio\":1},\"d\":[],\"e\":[\"The tower card: Tower of Gray\",\"Jotaro confronts the ape\",\"Cobra attack\",\"J. Geil appears/Beggar mob\"]},{\"t\":\"bargain\",\"n\":\"bargain\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":4,\"l\":118,\"a\":[],\"m\":{\"medio",
        "\":4},\"d\":[\"joseph\"],\"e\":[\"Joseph's mentalism skill.\",\"Joseph's bargain with Wamuu.\",\"Joseph cuts off Esidisi's arm.\",\"Lisa Lisa's bluff.\"]},{\"t\":\"Batting, Pitching, Turning the Tables\",\"n\":\"batting pitching turning the tables\",\"p\":\"DU\",\"o\":\"world\",\"u\":1,\"l\":60,\"a\":[\"tristeza\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Josuke plays video games/Angelo remembers Ryohei\"]},{\"t\":\"Batting, Pitching, Turning the Tables\",\"n\":\"batting pitching turning the tables\",\"p\":\"SC\",\"o\":\"world\",\"u\":1,\"l\":152,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"The match begins/Jotaro is a complete noob?!\"]},{\"t\":\"Battle Between Equals\",\"n\":\"battle between equals\",\"p\":\"SC\",\"o\":\"world\",\"u\":11,\"l\":56,\"a\":[\"pelea\"],\"m\":{\"medio\":11},\"d\":[],\"e\":[\"The heroes must hide under a rock\",\"The plane is crashing\",\"Kakyoin appears\",\"Avdol vs. Judgement\"]},{\"t\":\"Battle Between Equals\",\"n\":\"battle between equals\",\"p\":\"SO\",\"o\":\"world\",\"u\":3,\"l\":41,\"a\":[\"pelea\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Anasui starts turning inside out to save Jolyne\",\"Signal of an attack of Made in Heaven/Jotaro's final time stop\",\"Pucci inserts Weather Report's DISC into Emporio\"]},{\"t\":\"Bet on a Bluff\",\"n\":\"bet on a bluff\",\"p\":\"SC\",\"o\":\"world\",\"u\":5,\"l\":96,\"a\":[\"victoria\",\"villano\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[\"jotaro\"],\"e\":[\"Star Platinum's formidable eyes\",\"Jotaro breaks D'Arby's fingers\",\"Jotaro bets Holy's soul/Too much pressure\",\"Jotaro: Global Elite at video games\"]},{\"t\":\"Between the Silence...\",\"n\":\"between the silence\",\"p\":\"DU\",\"o\":\"good night\",\"u\":19,\"l\":104,\"a\":[\"villano\"],\"m\":{\"medio\":13,\"inicio\":4,\"final\":2},\"d\":[],\"e\":[\"Do not turn around when exiting the alley\",\"The \\\"invincible trio\\\" interrogated about the ticket\",\"Aya Tsuji's last chance\",\"Kira hidden but surrounded/Trying to recover the bag\"]},{\"t\":\"Between the Silence...\",\"n\":\"between the silence\",\"p\":\"SO\",\"o\":\"good night\",\"u\":2,\"l\":44,\"a\":[\"pelea\",\"viaje\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Weather meets Van Gogh/The Mother Goat attacks\",\"Anasui escaped via police car/The goats are inescapabl",
        "e\"]},{\"t\":\"BLOODY STREAM\",\"n\":\"bloody stream\",\"p\":\"PB/BT\",\"o\":\"\",\"u\":15,\"l\":89,\"a\":[],\"m\":{\"opening\":14,\"ending\":1},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"bolt\",\"n\":\"bolt\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":4,\"l\":26,\"a\":[\"tristeza\"],\"m\":{\"inicio\":2,\"medio\":1,\"final\":1},\"d\":[\"joseph\"],\"e\":[\"Joseph befriends Smokey.\",\"Joseph greets Santana to no avail.\",\"Spaghetti Nero.\",\"Joseph seeks revenge on Lisa Lisa for the deadly trial.\"]},{\"t\":\"British Blue\",\"n\":\"british blue\",\"p\":\"DU\",\"o\":\"good night\",\"u\":2,\"l\":50,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"The cat reborn as a plant\",\"Stray Cat is distracted\"]},{\"t\":\"British Blue\",\"n\":\"british blue\",\"p\":\"SO\",\"o\":\"good night\",\"u\":3,\"l\":33,\"a\":[\"comedia\"],\"m\":{\"medio\":2,\"ending\":1},\"d\":[],\"e\":[\"Dwarf antics\",\"The shop owner's happy ending\",\"...Only for her to slip in anyways, cutting him open and freeing her children\"]},{\"t\":\"Brothers' Rhapsody\",\"n\":\"brothers rhapsody\",\"p\":\"SBR\",\"o\":\"destination\",\"u\":1,\"l\":27,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Andre showing off\"]},{\"t\":\"Brothers' Rhapsody\",\"n\":\"brothers rhapsody\",\"p\":\"SC\",\"o\":\"destination\",\"u\":11,\"l\":55,\"a\":[\"pelea\"],\"m\":{\"medio\":8,\"final\":2,\"recap\":1},\"d\":[\"hol horse\"],\"e\":[\"Stuck with the bomb\",\"Oingo must swallow five cigarettes, then drink juice\",\"Has Oingo been outed?\",\"Oingo tries to escape\"]},{\"t\":\"bugia\",\"n\":\"bugia\",\"p\":\"GW\",\"o\":\"overture\",\"u\":5,\"l\":131,\"a\":[\"misterio\"],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"Formaggio puts Narancia inside a bottle with a spider\",\"Fugo tries to escape from the mirror world\",\"Prosciutto and Pesci start searching for Bucciarati's team\",\"Prosciutto and Pesci are searching for Bruno's team\"]},{\"t\":\"Burning Colosseum\",\"n\":\"burning colosseum\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":7,\"l\":44,\"a\":[],\"m\":{\"medio\":6,\"inicio\":1},\"d\":[],\"e\":[\"Divine Sandstorm!\",\"Esidisi has the advantage.\",\"Esidisi gets out of Suzi Q.\",\"Divine Sandstorm!\"]},{\"t\":\"C-Moon\",\"n\":\"c moon\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":4,\"l\":62,\"a\":[\"villano\"],\"m\":{\"medio\":3,\"final\":1},\"d\":[\"pucci\"],\"",
        "e\":[\"C-MOON appears\",\"Pucci spotted/Jotaro stops time\",\"Pucci must defeat Emporio or he will lose to him at Cape Canaveral\",\"Pucci tries to kill Emporio as he enters the ghost room\"]},{\"t\":\"Calm Sightseeing\",\"n\":\"calm sightseeing\",\"p\":\"SBR\",\"o\":\"departure\",\"u\":1,\"l\":8,\"a\":[\"viaje\",\"calma\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"The cork was inside what?!\"]},{\"t\":\"Calm Sightseeing\",\"n\":\"calm sightseeing\",\"p\":\"SC\",\"o\":\"departure\",\"u\":15,\"l\":45,\"a\":[\"viaje\",\"calma\"],\"m\":{\"inicio\":9,\"medio\":3,\"final\":3},\"d\":[],\"e\":[\"Jotaro goes to school\",\"The heroes in Hong Kong\",\"Polnareff is freed from DIO's flesh bud\",\"Girls asking for a photo\"]},{\"t\":\"canzoni preferite\",\"n\":\"canzoni preferite\",\"p\":\"GW\",\"o\":\"overture\",\"u\":4,\"l\":28,\"a\":[\"comedia\"],\"m\":{\"medio\":4},\"d\":[\"narancia\"],\"e\":[\"Narancia listens to his boombox\",\"Moody Blues (as Narancia) is listening to a boombox\",\"Narancia, Mista and Fugo dance to torture Zucchero\",\"Narancia drives Bruno's car\"]},{\"t\":\"capo\",\"n\":\"capo\",\"p\":\"SO\",\"o\":\"intermezzo\",\"u\":1,\"l\":75,\"a\":[\"villano\",\"viaje\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Pucci arrives at the Space Center/A nice tourist bothers Pucci\"]},{\"t\":\"Capture the Target\",\"n\":\"capture the target\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":2,\"l\":106,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Zeppeli vs. Jack the Ripper.\",\"Zeppeli attacks Dio.\"]},{\"t\":\"carne\",\"n\":\"carne\",\"p\":\"GW\",\"o\":\"finale\",\"u\":5,\"l\":119,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[],\"e\":[\"Carne appears\",\"Notorious B.I.G attacks Giorno\",\"Notorious B.I.G eats Giorno's arm and Sex Pistols\",\"Trish gets closer to the brooch\"]},{\"t\":\"carne\",\"n\":\"carne\",\"p\":\"SO\",\"o\":\"finale\",\"u\":1,\"l\":126,\"a\":[\"tristeza\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Versus sends Weather's memory DISC\"]},{\"t\":\"carro\",\"n\":\"carro\",\"p\":\"GW\",\"o\":\"finale\",\"u\":7,\"l\":60,\"a\":[\"explicacion\"],\"m\":{\"medio\":5,\"inicio\":2},\"d\":[\"polnareff\"],\"e\":[\"Someone hacks Bruno's computer\",\"Unknown ally tells them about Diavolo's ability\",\"Unknown ally is watching Bucciarati and Secco\",\"Polnareff t",
        "alks about the Arrow\"]},{\"t\":\"Carve Out That Ripple\",\"n\":\"carve out that ripple\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":91,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Dire vs. Dio.\"]},{\"t\":\"cavaliere\",\"n\":\"cavaliere\",\"p\":\"GW\",\"o\":\"finale\",\"u\":5,\"l\":78,\"a\":[\"villano\",\"epico\"],\"m\":{\"medio\":4,\"final\":1},\"d\":[],\"e\":[\"Bucciarati and his team sail to Roma\",\"Giorno reaches the helicopter\",\"Silver Chariot is pierced by the Arrow, but Diavolo kills Polnareff\",\"Polnareff picks up the Arrow\"]},{\"t\":\"Chained Power\",\"n\":\"chained power\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":81,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jonathan vs. Dio.\"]},{\"t\":\"chaos\",\"n\":\"chaos\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":7,\"l\":53,\"a\":[],\"m\":{\"medio\":6,\"inicio\":1},\"d\":[\"joseph\"],\"e\":[\"Joseph arrives at the Nazi base.\",\"Santana prevents Joseph from reaching the exit.\",\"Joseph is \\\"defeated\\\".\",\"Lisa Lisa looks at Suzi Q's memories.\"]},{\"t\":\"chase\",\"n\":\"chase\",\"p\":\"DU\",\"o\":\"\",\"u\":9,\"l\":72,\"a\":[\"tension\"],\"m\":{\"opening\":9},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"Cheerful Journey\",\"n\":\"cheerful journey\",\"p\":\"SC\",\"o\":\"world\",\"u\":15,\"l\":29,\"a\":[\"viaje\",\"comedia\"],\"m\":{\"final\":6,\"medio\":6,\"inicio\":3},\"d\":[],\"e\":[\"The Sun card, just another dumbass\",\"Joseph makes the baby eat\",\"Everybody knew about Avdol\",\"\\\"I can see your panties\\\"\"]},{\"t\":\"Clash\",\"n\":\"clash\",\"p\":\"SC\",\"o\":\"departure\",\"u\":8,\"l\":93,\"a\":[\"pelea\"],\"m\":{\"medio\":8},\"d\":[],\"e\":[\"Dark Blue Moon's true power\",\"Jotaro takes an ice cream/Pocky's death\",\"Polnareff vs Hol Horse/Avdol to the rescue\",\"Wheel of Fortune, the car Stand\"]},{\"t\":\"Clock Works\",\"n\":\"clock works\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":7,\"l\":112,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":4,\"final\":2,\"eyecatch\":1},\"d\":[\"pucci\"],\"e\":[\"The nice tourist gets impaled\",\"Pucci's new Stand emerging\",\"Pucci monologuing/Pucci shoots at Jolyne\",\"Pucci finds the correct position\"]},{\"t\":\"Close Match\",\"n\":\"close match\",\"p\":\"DU\",\"o\":\"world\",\"u\":2,\"l\":26,\"a\":[],\"m\":{\"final\":1,\"medio\":1}",
        ",\"d\":[],\"e\":[\"Josuke & Okuyasu just won the jackpot!!!\",\"Dreaming of the prize money\"]},{\"t\":\"Close Match\",\"n\":\"close match\",\"p\":\"SC\",\"o\":\"world\",\"u\":5,\"l\":62,\"a\":[\"pelea\"],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"Tower of Gray attacks Jotaro\",\"Empress beats up Joseph/Joseph running in the streets\",\"...and beats him up. Alessi and Sethan\",\"Sethan touches Polnareff/Pursuing Alessi\"]},{\"t\":\"coercizione\",\"n\":\"coercizione\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":20,\"l\":70,\"a\":[\"tension\",\"villano\"],\"m\":{\"medio\":16,\"inicio\":2,\"final\":2},\"d\":[],\"e\":[\"Luca gets violent\",\"Polpo begins the interview\",\"Bruno informs his team about Polpo's fortune\",\"Mista starts torturing Zucchero\"]},{\"t\":\"Collector\",\"n\":\"collector\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":1,\"l\":66,\"a\":[\"victoria\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Miraschon wins the bet\"]},{\"t\":\"Confrontation\",\"n\":\"confrontation\",\"p\":\"DU\",\"o\":\"future\",\"u\":11,\"l\":77,\"a\":[\"tension\"],\"m\":{\"medio\":11},\"d\":[],\"e\":[\"Tamami makes it look like Koichi stabbed Tamami\",\"Hazamada approaches the train tracks\",\"Yukako's stubborness\",\"RHCP out of battery/Psyching Okuyasu up\"]},{\"t\":\"Confrontation\",\"n\":\"confrontation\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":2,\"l\":68,\"a\":[\"villano\",\"epico\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Zeppeli advances.\",\"Jonathan finally meets Dio again.\"]},{\"t\":\"constrain\",\"n\":\"constrain\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":2,\"l\":145,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"The Nazis experimenting on the Pillar Man.\",\"The Pillar Man Santana wakes up.\"]},{\"t\":\"contrattacco\",\"n\":\"contrattacco\",\"p\":\"GW\",\"o\":\"finale\",\"u\":2,\"l\":35,\"a\":[\"pelea\",\"epico\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Doppio throws a pair of scissors at Risotto\",\"Doppio decides to attack Risotto one last time\"]},{\"t\":\"Conviction\",\"n\":\"conviction\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":73,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Ripple masters vs. Dio's Zombies.\"]},{\"t\":\"Cornered\",\"n\":\"cornered\",\"p\":\"DU\",\"o\":\"good night\",\"u\":20,\"l\":56,\"a\":[\"villano\",\"tension\"],\"m\":{\"medio\":15,\"final\":3,\"inicio\":2},\"d\":[\"josuke\"],\"e\":[\"",
        "Josuke cornered\",\"Tamami is unhurt, but there are sounds on him\",\"A copy of Josuke appears\",\"How to contact Josuke\"]},{\"t\":\"Cornered\",\"n\":\"cornered\",\"p\":\"SO\",\"o\":\"good night\",\"u\":2,\"l\":26,\"a\":[\"tension\"],\"m\":{\"inicio\":2},\"d\":[],\"e\":[\"Anasui pursues his body\",\"The pursuit continues\"]},{\"t\":\"Courage\",\"n\":\"courage\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":8,\"l\":67,\"a\":[],\"m\":{\"medio\":5,\"final\":3},\"d\":[],\"e\":[\"Echoes ACT1 appears\",\"Koichi convinces his mother & Tamami yields\",\"Echoes has tricked Hazamada\",\"A new Stand: Echoes ACT2\"]},{\"t\":\"Crazy in Love\",\"n\":\"crazy in love\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":11,\"l\":17,\"a\":[\"tension\"],\"m\":{\"medio\":10,\"final\":1},\"d\":[\"koichi\"],\"e\":[\"A girl looking by the window\",\"Yukako gets mad\",\"Hair in Koichi's drink\",\"Yukako stalking Koichi\"]},{\"t\":\"Crazy in Love\",\"n\":\"crazy in love\",\"p\":\"SO\",\"o\":\"good morning\",\"u\":1,\"l\":35,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Ungalo loses the will to live\"]},{\"t\":\"Crazy Noisy Bizarre Town\",\"n\":\"crazy noisy bizarre town\",\"p\":\"DU\",\"o\":\"\",\"u\":11,\"l\":74,\"a\":[\"misterio\"],\"m\":{\"opening\":11},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"Creep on\",\"n\":\"creep on\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":17,\"l\":86,\"a\":[],\"m\":{\"medio\":13,\"final\":2,\"inicio\":1,\"recap\":1},\"d\":[],\"e\":[\"Jolyne wakes up/What was real and what was fake\",\"Ermes runs back to find McQueen about to electrocute himself again\",\"Something suspect in the bucket\",\"Marilyn Manson's power explained/Jolyne makes a third bet\"]},{\"t\":\"Creeping Enemy\",\"n\":\"creeping enemy\",\"p\":\"SC\",\"o\":\"departure\",\"u\":13,\"l\":77,\"a\":[\"tension\",\"villano\"],\"m\":{\"medio\":13},\"d\":[],\"e\":[\"Kakyoin explains he's controlling the nurse\",\"The plane is crashing\",\"Avdol declares his Crossfire Hurricane Special\",\"An aquatic Stand appears\"]},{\"t\":\"crepuscolo\",\"n\":\"crepuscolo\",\"p\":\"GW\",\"o\":\"overture\",\"u\":14,\"l\":140,\"a\":[\"misterio\",\"tristeza\"],\"m\":{\"inicio\":8,\"medio\":5,\"final\":1},\"d\":[],\"e\":[\"Showcasing of crime\",\"Beginning of Giorno's backstory\",\"Giorno visits the prison where the capo is to",
        " enter Passione\",\"Passione members discuss Polpo's death\"]},{\"t\":\"crepuscolo\",\"n\":\"crepuscolo\",\"p\":\"SO\",\"o\":\"overture\",\"u\":1,\"l\":159,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Donatello Versus backstory\"]},{\"t\":\"Crime Scene Express\",\"n\":\"crime scene express\",\"p\":\"DU\",\"o\":\"\",\"u\":10,\"l\":54,\"a\":[],\"m\":{\"medio\":8,\"final\":1,\"inicio\":1},\"d\":[\"josuke\"],\"e\":[\"Crazy Diamond vs. Bad Company\",\"Josuke & Koichi must find Jotaro\",\"Okuyasu eating the spaghetti like crazy/Okuyasu's teeth are replaced\",\"Akira Otoishi has infiltrated the boat/Okuyasu's dilemma\"]},{\"t\":\"Crime Scene Express\",\"n\":\"crime scene express\",\"p\":\"SO\",\"o\":\"\",\"u\":1,\"l\":57,\"a\":[\"pelea\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Kenzou runs towards Jolyne/Anasui's intervention in the fight\"]},{\"t\":\"crisi\",\"n\":\"crisi\",\"p\":\"GW\",\"o\":\"overture\",\"u\":12,\"l\":73,\"a\":[\"pelea\"],\"m\":{\"medio\":8,\"inicio\":2,\"final\":2},\"d\":[],\"e\":[\"Bucciarati escapes\",\"Abbacchio solves Zucchero's second mystery\",\"Giorno and Mista seek for Sale\",\"Sale uses rocks as a ladder to climb back to Mista\"]},{\"t\":\"Cunning Rats\",\"n\":\"cunning rats\",\"p\":\"DU\",\"o\":\"\",\"u\":1,\"l\":131,\"a\":[\"pelea\",\"calma\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Bug-Eaten's sniper duel with Josuke and Jotaro\"]},{\"t\":\"Curse of Nightmares\",\"n\":\"curse of nightmares\",\"p\":\"SC\",\"o\":\"world\",\"u\":2,\"l\":47,\"a\":[\"tristeza\",\"calma\"],\"m\":{\"inicio\":1,\"medio\":1},\"d\":[],\"e\":[\"The Death card\",\"Death Thirteen revealed\"]},{\"t\":\"Daily Conversation\",\"n\":\"daily conversation\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":9,\"l\":28,\"a\":[\"calma\"],\"m\":{\"medio\":5,\"inicio\":4},\"d\":[\"jolyne\"],\"e\":[\"Jolyne's confession\",\"Loccobarocco presents the rules of G.D. Street\",\"Ermes offers McQueen her panties\",\"F.F. is obsessed with water\"]},{\"t\":\"Dance with STEEL BALL RUN\",\"n\":\"dance with steel ball run\",\"p\":\"SBR\",\"o\":\"\",\"u\":15,\"l\":41,\"a\":[\"viaje\"],\"m\":{\"medio\":7,\"eyecatch\":5,\"final\":2,\"ending\":1},\"d\":[\"gyro\"],\"e\":[\"Gyro wins the duel\",\"Eyecatch 1\",\"Eyecatch 2\",\"Gyro runs first\"]},{\"t\":\"Dark Rebirth\",\"n\":\"dark rebirth\",\"p\":\"DU\",\"o\":\"departure\",\"u\":1,\"l\":13,\"a\":[\"v",
        "illano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"DIO's legacy\"]},{\"t\":\"Dark Rebirth\",\"n\":\"dark rebirth\",\"p\":\"GW\",\"o\":\"departure\",\"u\":1,\"l\":44,\"a\":[\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jotaro mentions DIO\"]},{\"t\":\"Dark Rebirth\",\"n\":\"dark rebirth\",\"p\":\"SC\",\"o\":\"departure\",\"u\":23,\"l\":67,\"a\":[\"villano\"],\"m\":{\"medio\":12,\"final\":5,\"inicio\":5,\"recap\":1},\"d\":[\"dio\"],\"e\":[\"DIO realizes the Joestar are after him\",\"DIO in his lair\",\"DIO confronts Avdol\",\"DIO watches the Joestars\"]},{\"t\":\"Dark Rebirth\",\"n\":\"dark rebirth\",\"p\":\"SO\",\"o\":\"departure\",\"u\":8,\"l\":84,\"a\":[\"villano\"],\"m\":{\"medio\":6,\"inicio\":2},\"d\":[\"dio\",\"pucci\"],\"e\":[\"Memory of Pucci with DIO\",\"DIO explains his heaven plan to Pucci\",\"The bone is in the Ultra Security House Unit\",\"DIO explains Survivor\"]},{\"t\":\"Darkness of The World's Awakening\",\"n\":\"darkness of the world s awakening\",\"p\":\"SC\",\"o\":\"destination\",\"u\":9,\"l\":50,\"a\":[\"villano\"],\"m\":{\"final\":3,\"medio\":3,\"inicio\":2,\"avance\":1},\"d\":[\"dio\"],\"e\":[\"DIO deflects the Emerald Splash\",\"DIO can stop time!\",\"Jotaro vs. DIO\",\"Next Episode Preview\"]},{\"t\":\"Darkness of The World's Awakening\",\"n\":\"darkness of the world s awakening\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":42,\"a\":[\"villano\",\"epico\"],\"m\":{\"recap\":1},\"d\":[],\"e\":[\"Recap: Pucci and Sports Maxx/Jolyne going after DIO's bone\"]},{\"t\":\"Dawn\",\"n\":\"dawn\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":63,\"a\":[],\"m\":{\"final\":1},\"d\":[],\"e\":[\"The story of Jonathan Joestar.\"]},{\"t\":\"Day job\",\"n\":\"day job\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":5,\"l\":42,\"a\":[\"pelea\",\"comedia\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[],\"e\":[\"Joseph's \\\"clever\\\" disguise.\",\"Spaghetti battle./Caesar Anthonio Zeppeli appears.\",\"Joseph flirts with Suzi Q.\",\"Messing with a cat.\"]},{\"t\":\"Dead or Alive\",\"n\":\"dead or alive\",\"p\":\"SBR\",\"o\":\"\",\"u\":2,\"l\":88,\"a\":[],\"m\":{\"ending\":2},\"d\":[],\"e\":[\"Ending\",\"Ending\"]},{\"t\":\"Decisive Battle ~Overlapping Destinies~\",\"n\":\"decisive battle\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":8,\"l\":90,\"a\":[\"pelea\"],\"m\":{\"medio\":7,\"final\":1},\"d\":[],\"e\":[\"Jonathan's surrounded by Og",
        "re Street thugs.\",\"Dio's attacked by a vampire, which resulted from the mask.\",\"Jonathan must pursue Jack the Ripper.\",\"Dio's ice power counters the Ripple.\"]},{\"t\":\"Deep curse song\",\"n\":\"deep curse song\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":2,\"l\":58,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"A man slumbering in a pillar.\",\"There are three other Pillar Men?!\"]},{\"t\":\"demon\",\"n\":\"demon\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":10,\"l\":67,\"a\":[],\"m\":{\"medio\":9,\"inicio\":1},\"d\":[],\"e\":[\"Straizo on Brooklyn Bridge.\",\"Santana escapes his cage.\",\"Joseph meets Santana.\",\"Something's suspicious with the Pillar Men...\"]},{\"t\":\"Depths of the Pale Darkness\",\"n\":\"depths of the pale darkness\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":66,\"a\":[\"villano\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Bruford's powerful Danse Macab-hair technique.\"]},{\"t\":\"Desperate Situation\",\"n\":\"desperate situation\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":23,\"l\":67,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":12,\"final\":7,\"recap\":2,\"inicio\":2},\"d\":[],\"e\":[\"...but he tries to electrocute himself anyways\",\"A creature in the water\",\"Foo Fighters withdrawing/Who is the enemy?\",\"Jolyne has lost her gravity!\"]},{\"t\":\"Desperate Struggle\",\"n\":\"desperate struggle\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":3,\"l\":85,\"a\":[\"tristeza\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Jotaro throws a harpoon in stopped time\",\"Jolyne's sacrifice/Time accelerates to the end of the universe\",\"Emporio's memories of Jolyne strenghten his resolve\"]},{\"t\":\"Destinies Pulled Together\",\"n\":\"destinies pulled together\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":1,\"l\":261,\"a\":[\"villano\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Showcase of the lives of Jonathan Joestar and Dio Brando\"]},{\"t\":\"Determination\",\"n\":\"determination\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":3,\"l\":127,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":3},\"d\":[\"dio\"],\"e\":[\"Jonathan beats up Dio.\",\"Jonathan is determined to stop Dio.\",\"Dio shoots JoJo in the throat.\"]},{\"t\":\"Determination\",\"n\":\"determination\",\"p\":\"SC\",\"o\":\"destiny\",\"u\":5,\"l\":96,\"a\":[\"pelea\",\"epico\"],\"m\":{\"medio\":3,\"final\":1,\"inici",
        "o\":1},\"d\":[],\"e\":[\"Jotaro doesn't look at his cards\",\"Despair! Option #3, reality is cruel!\",\"Vanilla Ice is a vampire\",\"The sun is setting/The group splits up\"]},{\"t\":\"Determination\",\"n\":\"determination\",\"p\":\"SO\",\"o\":\"destiny\",\"u\":12,\"l\":56,\"a\":[\"pelea\"],\"m\":{\"medio\":10,\"inicio\":1,\"recap\":1},\"d\":[\"jolyne\"],\"e\":[\"Jolyne tricks Foo Fighters and starts up the tractor/Foo Fighters running on dry soil/Foo Fighters defeated\",\"Jolyne & F.F. pursuing Miraschon/Miraschon turns off the lights\",\"F.F defeats the alligator\",\"Jolyne restrains Westwood\"]},{\"t\":\"determinazione\",\"n\":\"determinazione\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":6,\"l\":76,\"a\":[\"epico\",\"pelea\"],\"m\":{\"medio\":4,\"final\":1,\"eyecatch\":1},\"d\":[],\"e\":[\"Bruno develops hatred towards the boss\",\"Bruno grabs Prosciutto and jumps from the train\",\"Pesci stops the train\",\"Bucciarati explains the situation\"]},{\"t\":\"develop\",\"n\":\"develop\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":3,\"l\":88,\"a\":[\"pelea\"],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Training with Ripple instructors Loggins and Messina.\",\"Psychological warfare with Esidisi.\",\"Two one-on-one battles.\"]},{\"t\":\"di molto\",\"n\":\"di molto\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":4,\"l\":39,\"a\":[\"villano\",\"tension\"],\"m\":{\"final\":2,\"inicio\":1,\"medio\":1},\"d\":[],\"e\":[\"Melone appears\",\"Melone uses Baby Face on a woman\",\"Baby Face steals Melone's bike\",\"Baby Face chops off Gold Experience's hand\"]},{\"t\":\"Diamond is Unbreakable ~Stand Activated~\",\"n\":\"diamond is unbreakable\",\"p\":\"DU\",\"o\":\"good night\",\"u\":74,\"l\":6,\"a\":[],\"m\":{\"avance\":38,\"medio\":29,\"final\":6,\"inicio\":1},\"d\":[],\"e\":[\"Jotaro arrives in Morioh\",\"\\\"Hey senior, what did you say about my hair?!\\\"\",\"Jotaro angers Josuke\",\"Josuke's Stand appears\"]},{\"t\":\"diavolo\",\"n\":\"diavolo\",\"p\":\"GW\",\"o\":\"finale\",\"u\":8,\"l\":66,\"a\":[\"villano\"],\"m\":{\"medio\":5,\"final\":2,\"inicio\":1},\"d\":[],\"e\":[\"Squalo gets an order from The Boss\",\"The teenager arrives to Costa Smeralda\",\"Giorno is searching the criminal database\",\"Doppio and Bucciarati get to the Colosseum\"]},{\"t\":\"Disc\",\"n\":",
        "\"disc\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":6,\"l\":90,\"a\":[\"villano\"],\"m\":{\"medio\":5,\"inicio\":1},\"d\":[],\"e\":[\"McQueen's DISCs are revealed/Ermes investigates McQueen and his memory DISC\",\"Guard Westwood is a Stand user!\",\"DIO gives Pucci his bone\",\"Emporio checks on Jolyne in solitary confinement\"]},{\"t\":\"Disciplinary Wing\",\"n\":\"disciplinary wing\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":5,\"l\":70,\"a\":[\"victoria\",\"pelea\"],\"m\":{\"medio\":5},\"d\":[\"jolyne\"],\"e\":[\"Miu Miu appears with Jail House Lock\",\"Miu Miu taunts Jolyne without her knowing\",\"Miu Miu wipes some of the writing off Jolyne's arm\",\"Miu Miu followed Jolyne to the ghost room\"]},{\"t\":\"Discomfort\",\"n\":\"discomfort\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":9,\"l\":73,\"a\":[\"tension\",\"villano\"],\"m\":{\"medio\":8,\"inicio\":1},\"d\":[],\"e\":[\"Jolyne seeking Emporio\",\"Miraschon makes a second bet\",\"The dangers of vacuum\",\"Sports Maxx realizes he's dead\"]},{\"t\":\"Distant Dreamer\",\"n\":\"distant dreamer\",\"p\":\"SO\",\"o\":\"\",\"u\":34,\"l\":90,\"a\":[],\"m\":{\"ending\":33,\"final\":1},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"Dive\",\"n\":\"dive\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":4,\"l\":72,\"a\":[\"explicacion\",\"tristeza\",\"pelea\"],\"m\":{\"medio\":3,\"final\":1},\"d\":[],\"e\":[\"Emporio explains Anasui's past\",\"Anasui suddenly obliges to help\",\"Diver Down's attack is revealed\",\"Anasui grafts Yo-Yo Ma's brain to a frog's\"]},{\"t\":\"Dizziness\",\"n\":\"dizziness\",\"p\":\"SC\",\"o\":\"journey\",\"u\":19,\"l\":41,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":15,\"inicio\":3,\"final\":1},\"d\":[],\"e\":[\"Joseph confronts Jotaro\",\"Nurse gets possessed\",\"Captain Tennille\",\"The heroes board the empty freighter\"]},{\"t\":\"dominazione\",\"n\":\"dominazione\",\"p\":\"GW\",\"o\":\"finale\",\"u\":6,\"l\":24,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":4,\"final\":1,\"recap\":1},\"d\":[\"narancia\"],\"e\":[\"Narancia starts lying\",\"Narancia lies again\",\"Tizzano reveals his Stand\",\"Giorno wants to heal Narancia\"]},{\"t\":\"dominazione\",\"n\":\"dominazione\",\"p\":\"SO\",\"o\":\"finale\",\"u\":1,\"l\":45,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Anasui catches Pinocchio\"]},{\"t\":\"doppio\",",
        "\"n\":\"doppio\",\"p\":\"GW\",\"o\":\"finale\",\"u\":15,\"l\":74,\"a\":[\"villano\",\"misterio\"],\"m\":{\"medio\":11,\"final\":2,\"recap\":1,\"inicio\":1},\"d\":[\"doppio\"],\"e\":[\"Bucciarati witnesses King Crimson's ultimate ability\",\"Bucciarati and his team escape from the church\",\"Doppio gets a call from The Boss\",\"Doppio and The Boss talk\"]},{\"t\":\"Echoes ACT1\",\"n\":\"echoes act1\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":3,\"l\":73,\"a\":[],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Echoes ACT1's sound power\",\"Koichi sends out Echoes\",\"Koichi has neutralized SHA\"]},{\"t\":\"Egypt Landing\",\"n\":\"egypt landing\",\"p\":\"SC\",\"o\":\"destination\",\"u\":4,\"l\":47,\"a\":[\"comedia\",\"misterio\"],\"m\":{\"medio\":3,\"inicio\":1},\"d\":[],\"e\":[\"Cameo the genie appears\",\"Cameo asks for the 2nd wish\",\"Cameo grants Polnareff's wish\",\"Judgement the fake genie\"]},{\"t\":\"Electric Guitarist\",\"n\":\"electric guitarist\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":9,\"l\":20,\"a\":[\"villano\"],\"m\":{\"medio\":7,\"final\":1,\"inicio\":1},\"d\":[],\"e\":[\"Red Hot Chili Pepper appears\",\"Red Hot Chili Pepper lurks\",\"Red Hot Chili Pepper reappears\",\"RHCP has head everything\"]},{\"t\":\"Electric Potential\",\"n\":\"electric potential\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":1,\"l\":148,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Survivor's effects on the prison guards\"]},{\"t\":\"Elephant Talk 1\",\"n\":\"elephant talk 1\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":7,\"l\":98,\"a\":[],\"m\":{\"medio\":5,\"inicio\":2},\"d\":[\"joseph\"],\"e\":[\"Straizo piecing himself together.\",\"Santana absorbs a Vampire.\",\"Santana's superior intelligence and resilience.\",\"Esidisi sees through Joseph.\"]},{\"t\":\"Elephant Talk 2\",\"n\":\"elephant talk 2\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":1,\"l\":58,\"a\":[\"epico\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Final Mode: Atmospheric Rift!\"]},{\"t\":\"epitaffio\",\"n\":\"epitaffio\",\"p\":\"GW\",\"o\":\"finale\",\"u\":2,\"l\":44,\"a\":[\"villano\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"The Boss takes control of the teen's body\",\"Mista destroys Rolling Stones\"]},{\"t\":\"eremita\",\"n\":\"eremita\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":7,\"l\":34,\"a\":[\"villano\"],\"m\":{\"medio\":4,\"final\":2,\"inicio\":1},\"d\":[],\"e\":[\"Giorno ret",
        "urns Polpo's lighter\",\"Prosciutto shoots Mista\",\"The Boss loses trace of Risotto\",\"The Boss gets away\"]},{\"t\":\"esperienza d'oro\",\"n\":\"esperienza d oro\",\"p\":\"GW\",\"o\":\"overture\",\"u\":3,\"l\":50,\"a\":[\"epico\",\"tristeza\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Giorno extends his range and lands a blow\",\"Giorno sacrifices himself\",\"King Crimson destroys Gold Experience\"]},{\"t\":\"Evolution\",\"n\":\"evolution\",\"p\":\"DU\",\"o\":\"good night\",\"u\":5,\"l\":63,\"a\":[\"explicacion\"],\"m\":{\"medio\":4,\"final\":1},\"d\":[],\"e\":[\"Echoes blows Yukako away\",\"Echoes ACT3 appears\",\"but is immobilized by Echoes's \\\"freeze\\\" effect\",\"Echoes ACT3's powers explained\"]},{\"t\":\"Evolution\",\"n\":\"evolution\",\"p\":\"GW\",\"o\":\"good night\",\"u\":2,\"l\":32,\"a\":[\"viaje\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Koichi uses ACT3 on Giorno's car\",\"Koichi confronts Giorno\"]},{\"t\":\"Execution\",\"n\":\"execution\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":5,\"l\":49,\"a\":[\"tristeza\"],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"Jolyne decides to protect Emporio\",\"Jolyne sees that Jotaro is practically dead/Jolyne surrenders\",\"Anasui dying/F.F.'s death\",\"Ermes pays respect to F.F.\"]},{\"t\":\"Eye Catching (Voice Ver.)\",\"n\":\"eye catching\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":19,\"l\":3,\"a\":[],\"m\":{\"eyecatch\":19},\"d\":[],\"e\":[\"Eyecatch\",\"Eyecatch\",\"Eyecatch\",\"Eyecatch\"]},{\"t\":\"Eyelids\",\"n\":\"eyelids\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":1,\"l\":97,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"\\\"I am *Apollo 11*!\\\"\"]},{\"t\":\"Fairy Godmother\",\"n\":\"fairy godmother\",\"p\":\"DU\",\"o\":\"good night\",\"u\":4,\"l\":86,\"a\":[],\"m\":{\"inicio\":2,\"medio\":2},\"d\":[],\"e\":[\"Yukako decides to try the salon\",\"Aya Tsuji the beautician\",\"Yukako wants the \\\"capture love\\\" treatment/Aya's way of life\",\"Yuya regains his beautiful face\"]},{\"t\":\"Fairy Tale\",\"n\":\"fairy tale\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":2,\"l\":78,\"a\":[\"viaje\",\"explicacion\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Ungalo escapes via plane\",\"Ungalo celebrates his ability/Weather is seemingly finished\"]},{\"t\":\"FAITH\",\"n\":\"faith\",\"p\":\"DU\",\"o\":\"good night\",\"u\":2,\"l\":37,\"a\":[\"villano\",\"calma\"],\"m\":{\"inicio\":2},",
        "\"d\":[],\"e\":[\"Kira's breakfast with his girlfriend\",\"Jotaro receives the file on Kira\"]},{\"t\":\"fango\",\"n\":\"fango\",\"p\":\"GW\",\"o\":\"finale\",\"u\":5,\"l\":43,\"a\":[\"pelea\"],\"m\":{\"medio\":3,\"inicio\":1,\"recap\":1},\"d\":[\"bruno\"],\"e\":[\"The duo's position in Passione\",\"Secco attacks Bruno\",\"Recap of Secco punching Bruno\",\"Secco attacks Bruno\"]},{\"t\":\"Fascination\",\"n\":\"fascination\",\"p\":\"SC\",\"o\":\"journey\",\"u\":3,\"l\":51,\"a\":[\"misterio\",\"tension\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Shark-infested waters\",\"Hol Horse's philosophy\",\"Empress has grown big enough to kill Joseph\"]},{\"t\":\"Fascination\",\"n\":\"fascination\",\"p\":\"SO\",\"o\":\"journey\",\"u\":1,\"l\":82,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"The entire world begins to experience accelerated time\"]},{\"t\":\"Fate\",\"n\":\"fate\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":11,\"l\":70,\"a\":[\"epico\"],\"m\":{\"medio\":5,\"final\":4,\"inicio\":2},\"d\":[],\"e\":[\"Jolyne wants to save her father/Emporio's backstory\",\"Jolyne is informed about Ermes\",\"Aftermath of Operation Savage Garden\",\"Ermes's backstory on her desire for revenge\"]},{\"t\":\"Father-Son\",\"n\":\"father son\",\"p\":\"DU\",\"o\":\"good night\",\"u\":2,\"l\":21,\"a\":[],\"m\":{\"inicio\":2},\"d\":[],\"e\":[\"Yoshihiro looking for Yoshikage\",\"Yoshihiro finds Yoshikage\"]},{\"t\":\"Fear\",\"n\":\"fear\",\"p\":\"SC\",\"o\":\"journey\",\"u\":1,\"l\":47,\"a\":[\"villano\",\"misterio\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Ch\u00e9rie coming back to life\"]},{\"t\":\"fend off\",\"n\":\"fend off\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":4,\"l\":76,\"a\":[\"pelea\",\"tension\"],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"The Super Aja is going to Switzerland!\",\"The Aja is slipping towards a cliff./The race to the cliff.\",\"Joseph's great trick: shooting Wamuu from behind!\",\"Kars pursues Joseph.\"]},{\"t\":\"FENG SHUI\",\"n\":\"feng shui\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":7,\"l\":72,\"a\":[],\"m\":{\"medio\":6,\"inicio\":1},\"d\":[],\"e\":[\"Kenzou introduces himself\",\"Kenzou reveals he drowned the prisoners\",\"Kenzou brings out Dragon's Dream/F.F. is drowned by Kenzou but survives\",\"Kenzou begins his approach of F.F.\"]},{\"t\":\"Fields of fright\",\"n\":\"fields of fright\",\"p\":\"PB/B",
        "T\",\"o\":\"musik\",\"u\":8,\"l\":95,\"a\":[],\"m\":{\"medio\":4,\"inicio\":3,\"final\":1},\"d\":[],\"e\":[\"49 years later, Speedwagon & Straizo visit a Mexican temple.\",\"The Speedwagon Foundation has sealed the still alive Santana.\",\"Lisa Lisa has the Super Aja!\",\"The chariot race between Joseph and Wamuu!\"]},{\"t\":\"Fight the Fight!\",\"n\":\"fight the fight\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":7,\"l\":49,\"a\":[\"pelea\"],\"m\":{\"medio\":7},\"d\":[],\"e\":[\"The class rep trapped by Yukako's hair\",\"RHCP's tremendous speed\",\"Rohan & Koichi cannot escape from the ghost alley\",\"The evil spirits grab Koichi\"]},{\"t\":\"Fighting Gold\",\"n\":\"fighting gold\",\"p\":\"GW\",\"o\":\"\",\"u\":20,\"l\":89,\"a\":[\"pelea\"],\"m\":{\"opening\":20},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"figlia\",\"n\":\"figlia\",\"p\":\"GW\",\"o\":\"finale\",\"u\":2,\"l\":82,\"a\":[\"pelea\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Spice Girl kills Notorious B.I.G\",\"Spice Girls destroys the airplane\"]},{\"t\":\"Final Battle\",\"n\":\"final battle\",\"p\":\"DU\",\"o\":\"destination\",\"u\":3,\"l\":44,\"a\":[\"villano\"],\"m\":{\"medio\":2,\"final\":1},\"d\":[\"kira\"],\"e\":[\"Yoshikage Kira is cornered\",\"How Koichi stopped Kira from using Bites the Dust\",\"Yoshikage Kira taken away by the hands\"]},{\"t\":\"Final Battle\",\"n\":\"final battle\",\"p\":\"SC\",\"o\":\"destination\",\"u\":11,\"l\":72,\"a\":[\"pelea\",\"epico\"],\"m\":{\"medio\":5,\"final\":4,\"inicio\":2},\"d\":[\"dio\",\"jotaro\"],\"e\":[\"Jotaro challenges D'Arby to poker\",\"Jotaro declares his pitch\",\"Polnareff & Iggy try to trick Ice\",\"Polnareff meets DIO\"]},{\"t\":\"Final Battle\",\"n\":\"final battle\",\"p\":\"SO\",\"o\":\"destination\",\"u\":3,\"l\":74,\"a\":[\"pelea\",\"epico\"],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Ermes avenges her sister\",\"Weather's about to deliver the fatal blow\",\"\\\"You were two steps behind\\\"\"]},{\"t\":\"fine della vento aureo\",\"n\":\"fine della vento aureo\",\"p\":\"GW\",\"o\":\"finale\",\"u\":2,\"l\":82,\"a\":[\"epico\"],\"m\":{\"medio\":1,\"final\":1},\"d\":[],\"e\":[\"Polnareff's soul is inside the turtle\",\"Giorno refuses to destroy the Arrow\"]},{\"t\":\"Fists of Platinum\",\"n\":\"fists of platinum\",\"p\":\"DU\",\"o\":\"destinatio",
        "n\",\"u\":2,\"l\":32,\"a\":[\"explicacion\",\"pelea\",\"epico\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Jotaro explains Stands to Josuke\",\"Jotaro must stop time\"]},{\"t\":\"Fists of Platinum\",\"n\":\"fists of platinum\",\"p\":\"GW\",\"o\":\"destination\",\"u\":1,\"l\":12,\"a\":[\"epico\",\"pelea\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"The Arrow's powers\"]},{\"t\":\"Fists of Platinum\",\"n\":\"fists of platinum\",\"p\":\"SC\",\"o\":\"destination\",\"u\":63,\"l\":14,\"a\":[\"epico\",\"pelea\"],\"m\":{\"avance\":42,\"medio\":9,\"final\":7,\"inicio\":3,\"recap\":2},\"d\":[],\"e\":[\"Next episode preview\",\"Recap 1: Stands\",\"Next episode preview\",\"Jotaro extracts the flesh bud\"]},{\"t\":\"Foreboding\",\"n\":\"foreboding\",\"p\":\"SC\",\"o\":\"world\",\"u\":24,\"l\":76,\"a\":[\"tension\"],\"m\":{\"medio\":17,\"final\":4,\"inicio\":3},\"d\":[],\"e\":[\"Avdol steps out of the fight\",\"DIO has stolen Jonathan's body\",\"Kakyoin explains Hierophant Green\",\"Joseph shows Kakyoin's flesh bud\"]},{\"t\":\"Foreboding\",\"n\":\"foreboding\",\"p\":\"SO\",\"o\":\"world\",\"u\":3,\"l\":74,\"a\":[\"tension\",\"pelea\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Jotaro's plan/Something following Jolyne?/Jolyne trips\",\"F.F. survived Kenzou's attack!\",\"The gang study the situation\"]},{\"t\":\"Freek'n You\",\"n\":\"freek n you\",\"p\":\"GW\",\"o\":\"\",\"u\":16,\"l\":87,\"a\":[],\"m\":{\"ending\":16},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"Friends! Friends?\",\"n\":\"friends friends\",\"p\":\"DU\",\"o\":\"good night\",\"u\":5,\"l\":94,\"a\":[\"villano\",\"calma\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[],\"e\":[\"A Stand that collects coins\",\"...is just a kid, Shigekiyo \\\"Shigechi\\\" Yangu!\",\"Shigechi's greed surfaces\",\"Josuke & Okuyasu ask Shigechi for money/Shigechi picks Kira's bag\"]},{\"t\":\"Friendship\",\"n\":\"friendship\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":2,\"l\":70,\"a\":[],\"m\":{\"final\":2},\"d\":[],\"e\":[\"Straizo and Tonpetty appear.\",\"The heroes have won!\"]},{\"t\":\"From Darkness\",\"n\":\"from darkness\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":13,\"l\":71,\"a\":[\"villano\"],\"m\":{\"medio\":10,\"inicio\":2,\"final\":1},\"d\":[\"jolyne\"],\"e\":[\"Jolyne growing back\",\"Jolyne realizes something is really wrong/The dream is collapsing\",\"Jolyne a",
        "nd Ermes find the tractor\",\"Ermes tricks the lesser Foo Fighters\"]},{\"t\":\"From the Dark Abyss\",\"n\":\"from the dark abyss\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":2,\"l\":108,\"a\":[\"villano\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Dio steals Jonathan's first kiss with Erina.\",\"How Elizabeth took revenge and had to flee the country.\"]},{\"t\":\"Full-Body Courage\",\"n\":\"full body courage\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":6,\"l\":86,\"a\":[\"pelea\"],\"m\":{\"final\":4,\"medio\":2},\"d\":[],\"e\":[\"George stops Jonathan and Dio.\",\"Jonathan defeats Jack the Ripper.\",\"Jonathan fighting with Bruford underwater.\",\"Poco opens the door to the chamber.\"]},{\"t\":\"Fun Friends\",\"n\":\"fun friends\",\"p\":\"SC\",\"o\":\"journey\",\"u\":8,\"l\":42,\"a\":[\"comedia\",\"viaje\"],\"m\":{\"medio\":6,\"inicio\":2},\"d\":[],\"e\":[\"Polnareff argues with a policeman\",\"Culture shock in Calcutta\",\"Joseph being ripped off\",\"How to ride camel with Joseph Joestar\"]},{\"t\":\"gambit\",\"n\":\"gambit\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":6,\"l\":72,\"a\":[\"pelea\"],\"m\":{\"medio\":6},\"d\":[\"joseph\"],\"e\":[\"Speedwagon has been murdered!?\",\"Santana is truly intelligent!\",\"Wamuu accepts to fight Joseph.\",\"The merciless Pillar./Joseph understands the trick.\"]},{\"t\":\"Gambler\",\"n\":\"gambler\",\"p\":\"SC\",\"o\":\"world\",\"u\":6,\"l\":114,\"a\":[\"villano\"],\"m\":{\"medio\":4,\"inicio\":2},\"d\":[],\"e\":[\"Someone recognizes DIO's lair\",\"Joseph plays against D'Arby\",\"Daniel J. D'Arby formidable sense of touch/Open the game\",\"D'Arby's stratagems over stratagems\"]},{\"t\":\"Gentle Sunlight\",\"n\":\"gentle sunlight\",\"p\":\"DU\",\"o\":\"journey\",\"u\":6,\"l\":24,\"a\":[\"calma\"],\"m\":{\"medio\":4,\"final\":1,\"inicio\":1},\"d\":[],\"e\":[\"Tamami falls on the pavement\",\"Delicious water\",\"Dreaming of how to spend the money\",\"Clients are satisfied with Cinderella\"]},{\"t\":\"Gentle Sunlight\",\"n\":\"gentle sunlight\",\"p\":\"SC\",\"o\":\"journey\",\"u\":9,\"l\":39,\"a\":[\"calma\"],\"m\":{\"medio\":6,\"final\":2,\"inicio\":1},\"d\":[\"joseph\"],\"e\":[\"Joseph meets Holy\",\"Joseph and Holy argue about Japan\",\"Joseph taking care of Holy\",\"Joseph calls Suzi Q\"]},{\"t\":\"Gentle Sunlight\",\"n\":\"gentle sunlight\",",
        "\"p\":\"SO\",\"o\":\"journey\",\"u\":1,\"l\":22,\"a\":[\"calma\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Ermes momentarily cheers McQueen up...\"]},{\"t\":\"Get Excited\",\"n\":\"get excited\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":1,\"l\":11,\"a\":[],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Emporio wants to take the bus\"]},{\"t\":\"ghiaccio\",\"n\":\"ghiaccio\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":4,\"l\":48,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"Black Sabbath traps Giorno\",\"Ghiaccio continues to freeze Giorno and Mista\",\"Ghiaccio chases Mista\",\"Ghiaccio deflects the bullets\"]},{\"t\":\"Ghost Room\",\"n\":\"ghost room\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":3,\"l\":78,\"a\":[\"misterio\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Emporio narrates Jolyne's punishment/Ermes wakes up in the infirmary ward\",\"Jolyne discovers the ghost room\",\"Overview of Kennedy Space Center\"]},{\"t\":\"9 Glory Gods\",\"n\":\"glory gods\",\"p\":\"SC\",\"o\":\"destination\",\"u\":5,\"l\":89,\"a\":[\"victoria\"],\"m\":{\"final\":2,\"inicio\":2,\"medio\":1},\"d\":[],\"e\":[\"The great N'Doul attacks the car\",\"Geb harrasses Jotaro and Iggy\",\"D'Arby own stratagem\",\"D'Arby takes Kakyoin's soul\"]},{\"t\":\"9 Glory Gods\",\"n\":\"glory gods\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":25,\"a\":[\"pelea\",\"victoria\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Prisoner fight club!\"]},{\"t\":\"Great Days\",\"n\":\"great days\",\"p\":\"DU\",\"o\":\"\",\"u\":12,\"l\":81,\"a\":[],\"m\":{\"opening\":11,\"final\":1},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"Green Dolphin Street Prison\",\"n\":\"green dolphin street prison\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":7,\"l\":47,\"a\":[],\"m\":{\"medio\":4,\"inicio\":2,\"final\":1},\"d\":[\"jolyne\"],\"e\":[\"Green Dolphin Street presentation\",\"Strip search\",\"Jolyne meets Gwess/Gwess getting angry\",\"The rules of the phone booths\"]},{\"t\":\"guardia\",\"n\":\"guardia\",\"p\":\"GW\",\"o\":\"finale\",\"u\":10,\"l\":44,\"a\":[\"villano\",\"misterio\"],\"m\":{\"medio\":8,\"inicio\":1,\"final\":1},\"d\":[],\"e\":[\"Squalo and Tizzano's reveal\",\"Squalo and Tizzano talk\",\"Bruno's team gets wounded by the explosion\",\"Risotto is searching for The Boss\"]},{\"t\":\"Hatred\",\"n\":\"hatred\",\"p\":\"SO\",\"o\":\"st",
        "one ocean\",\"u\":3,\"l\":47,\"a\":[\"explicacion\",\"revelacion\"],\"m\":{\"inicio\":1,\"recap\":1,\"medio\":1},\"d\":[],\"e\":[\"Explanation of Stone Free\",\"Recap: Jolyne surrenders/Ermes's awakening\",\"Jolyne and Romeo reunite\"]},{\"t\":\"Heart of Darkness\",\"n\":\"heart of darkness\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":4,\"l\":50,\"a\":[\"villano\"],\"m\":{\"medio\":4},\"d\":[\"dio\"],\"e\":[\"Dio plots to steal Jonathan's happiness.\",\"Drunken Dio decides to test the mask.\",\"Dio's superhuman strength.\",\"Dio regaining his strength.\"]},{\"t\":\"Heartbeat\",\"n\":\"heartbeat\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":35,\"a\":[\"pelea\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jonathan vs. Adams.\"]},{\"t\":\"Heaven's falling down\",\"n\":\"heaven s falling down\",\"p\":\"SO\",\"o\":\"\",\"u\":13,\"l\":89,\"a\":[],\"m\":{\"opening\":13},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"hellcrimb\",\"n\":\"hellcrimb\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":2,\"l\":64,\"a\":[\"viaje\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"The Hell Climb Pillar!\",\"Arrival in Switzerland.\"]},{\"t\":\"Hesitation\",\"n\":\"hesitation\",\"p\":\"SC\",\"o\":\"stone ocean\",\"u\":12,\"l\":56,\"a\":[\"tristeza\",\"calma\"],\"m\":{\"medio\":10,\"final\":1,\"recap\":1},\"d\":[],\"e\":[\"Avdol explains Stand sickness\",\"Anne approaches the ape, the heroes find nothing\",\"Joseph and Jotaro find Avdol\",\"Leaving Benares\"]},{\"t\":\"Hesitation\",\"n\":\"hesitation\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":11,\"l\":80,\"a\":[\"tristeza\"],\"m\":{\"medio\":9,\"final\":2},\"d\":[\"jolyne\"],\"e\":[\"Jolyne with her lawyer/The Stone Pendant\",\"Jolyne's mind racing in bed/Gwess jump scare\",\"Emporio warns Jolyne\",\"Jolyne believes Jotaro wants to help her/Jolyne's resentment\"]},{\"t\":\"Hidden Thoughts\",\"n\":\"hidden thoughts\",\"p\":\"SC\",\"o\":\"departure\",\"u\":8,\"l\":36,\"a\":[\"tristeza\",\"calma\"],\"m\":{\"medio\":7,\"final\":1},\"d\":[],\"e\":[\"The heroes lament their plane crashed\",\"Polnareff refuses to use a dagger\",\"Polnareff gets his revenge\",\"Polnareff accepts his fate\"]},{\"t\":\"Hidden Thoughts\",\"n\":\"hidden thoughts\",\"p\":\"SO\",\"o\":\"departure\",\"u\":1,\"l\":91,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jotaro falls into a coma\"]},{\"t\":\"High ",
        "Tension\",\"n\":\"high tension\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":16,\"l\":86,\"a\":[\"tension\",\"pelea\"],\"m\":{\"medio\":14,\"recap\":1,\"inicio\":1},\"d\":[\"jolyne\"],\"e\":[\"Propellers appear on Ermes's neck\",\"McQueen tries to drown himself in a sink\",\"Lang Rangler cracks Jolyne's suit\",\"Westwood gets the upper hand\"]},{\"t\":\"hike\",\"n\":\"hike\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":3,\"l\":36,\"a\":[\"victoria\"],\"m\":{\"medio\":1,\"inicio\":1,\"final\":1},\"d\":[],\"e\":[\"The Nazis in Mexico.\",\"Stroheim is alive!\",\"Stroheim prepares to finish off Kars.\"]},{\"t\":\"Hopelessness\",\"n\":\"hopelessness\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":7,\"l\":62,\"a\":[\"tristeza\"],\"m\":{\"medio\":4,\"inicio\":2,\"final\":1},\"d\":[\"jolyne\"],\"e\":[\"Jolyne narrating\",\"Jolyne flashback/Jolyne hitting the guard\",\"Jotaro's last words to Jolyne\",\"It's raining poisonous frogs!\"]},{\"t\":\"Hurry Up!\",\"n\":\"hurry up\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":17,\"l\":50,\"a\":[\"pelea\"],\"m\":{\"medio\":13,\"inicio\":4},\"d\":[\"jolyne\"],\"e\":[\"The girls have to run after the guard\",\"They are all the enemy!\",\"Almost failing to catch the ball/Marilyn Manson appears\",\"Objects sticking to Jolyne?\"]},{\"t\":\"I'm in control\",\"n\":\"i m in control\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":7,\"l\":36,\"a\":[\"victoria\"],\"m\":{\"final\":3,\"inicio\":2,\"medio\":2},\"d\":[\"joseph\"],\"e\":[\"Joseph beats the policemen with Ripple-infused cola.\",\"Joseph defeats Santana.\",\"Caesar attacks Wamuu.\",\"Joseph & Wamuu in the minecart.\"]},{\"t\":\"I Want You\",\"n\":\"i want you\",\"p\":\"DU\",\"o\":\"\",\"u\":30,\"l\":68,\"a\":[],\"m\":{\"ending\":30},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"Il mare eterno nella mia anima\",\"n\":\"il mare eterno nella mia anima\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":2,\"l\":147,\"a\":[\"victoria\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Joseph's and Lisa Lisa's grief over Caesar.\",\"Joseph defeats Wamuu.\"]},{\"t\":\"Il mare eterno nella mia anima\u301cLunetta\u301c\",\"n\":\"il mare eterno nella mia animalunetta\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":4,\"l\":22,\"a\":[\"tristeza\"],\"m\":{\"inicio\":3,\"medio\":1},\"d\":[],\"e\":[\"What a womanizer!\",\"Joseph in Venice.\",\"Air Supplena Island.\",\"C",
        "aesar's flashback: happy childhood.\"]},{\"t\":\"il primo assassino\",\"n\":\"il primo assassino\",\"p\":\"GW\",\"o\":\"overture\",\"u\":6,\"l\":38,\"a\":[\"villano\"],\"m\":{\"inicio\":4,\"medio\":1,\"final\":1},\"d\":[],\"e\":[\"Luca appears\",\"Sale and Zucchero talk about Polpo's fortune\",\"Notorious B.I.G is defeated\",\"Cioccolata pets Secco\"]},{\"t\":\"il primo assassino\",\"n\":\"il primo assassino\",\"p\":\"SBR\",\"o\":\"overture\",\"u\":2,\"l\":73,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Benjamin orders LA to flank the heroes\",\"The Boombooms planning their next move\"]},{\"t\":\"il sole\",\"n\":\"il sole\",\"p\":\"GW\",\"o\":\"overture\",\"u\":1,\"l\":36,\"a\":[\"comedia\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Mista and Trish laugh together\"]},{\"t\":\"il vento d'oro\",\"n\":\"il vento d oro\",\"p\":\"GW\",\"o\":\"overture\",\"u\":95,\"l\":5,\"a\":[\"epico\"],\"m\":{\"eyecatch\":38,\"avance\":38,\"medio\":10,\"final\":5,\"inicio\":3,\"recap\":1},\"d\":[],\"e\":[\"Eyecatch\",\"Next Episode Title\",\"Eyecatch\",\"Next Episode Title\"]},{\"t\":\"Impending Crisis\",\"n\":\"impending crisis\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":16,\"l\":100,\"a\":[\"tension\"],\"m\":{\"medio\":13,\"final\":1,\"recap\":1,\"inicio\":1},\"d\":[],\"e\":[\"Ermes runs away from McQueen\",\"The handcuffs are beeping\",\"Jolyne and Ermes split up\",\"Pursuing Lang Rangler\"]},{\"t\":\"incontrollabile\",\"n\":\"incontrollabile\",\"p\":\"GW\",\"o\":\"finale\",\"u\":4,\"l\":28,\"a\":[\"epico\"],\"m\":{\"final\":2,\"inicio\":2},\"d\":[],\"e\":[\"Chariot Requiem awakens\",\"Chariot Requiem walks away from Diavolo\",\"Bucciarati stumbles Chariot Requiem\",\"Chariot Requiem dashes towards Polnareff\"]},{\"t\":\"Increasing Power\",\"n\":\"increasing power\",\"p\":\"SC\",\"o\":\"journey\",\"u\":6,\"l\":53,\"a\":[\"villano\"],\"m\":{\"medio\":4,\"recap\":1,\"final\":1},\"d\":[\"dio\"],\"e\":[\"Recap 2: DIO has returned\",\"Enya decides to personally go and kill the heroes\",\"Waking up in Death Thirteen's dream world\",\"Polnareff realizes the baby is the enemy\"]},{\"t\":\"Increasing Power\",\"n\":\"increasing power\",\"p\":\"SO\",\"o\":\"journey\",\"u\":1,\"l\":38,\"a\":[\"villano\",\"tristeza\"],\"m\":{\"recap\":1},\"d\":[],\"e\":[\"Recap: Weather and Pucci's past\"]},{\"t\":\"incursione\",\"n\":\"incursione\"",
        ",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":16,\"l\":56,\"a\":[\"pelea\"],\"m\":{\"medio\":11,\"final\":3,\"inicio\":2},\"d\":[\"mista\"],\"e\":[\"Sale escapes from Mista\",\"Sale gets away from Mista\",\"Mista shoots inside Sale's mout\",\"Formaggio continues to escape from Aerosmith\"]},{\"t\":\"Interception\",\"n\":\"interception\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":23,\"l\":64,\"a\":[\"pelea\"],\"m\":{\"medio\":16,\"final\":6,\"inicio\":1},\"d\":[],\"e\":[\"Aqua Necklace enters Josuke's mouth\",\"Koichi is dragged inside/Keicho appears\",\"Koichi tries to intervene\",\"Josuke hits the chest\"]},{\"t\":\"Interception\",\"n\":\"interception\",\"p\":\"GW\",\"o\":\"good morning\",\"u\":2,\"l\":92,\"a\":[],\"m\":{\"medio\":1,\"inicio\":1},\"d\":[],\"e\":[\"Sale aims at Mista\",\"Giorno, Mista and Ghiaccio in the canal\"]},{\"t\":\"Intertwined Destinies\",\"n\":\"intertwined destinies\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":11,\"l\":16,\"a\":[\"victoria\"],\"m\":{\"avance\":8,\"medio\":3},\"d\":[],\"e\":[\"Jonathan loses to Dio in a boxing match.\",\"Next Episode Title\",\"Next episode preview\",\"Next episode preview\"]},{\"t\":\"invecchiare\",\"n\":\"invecchiare\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":3,\"l\":49,\"a\":[\"tristeza\",\"pelea\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"The Grateful Dead activated\",\"Bruno's team under the effect of The Grateful Dead\",\"Prosciutto attacks Mista\"]},{\"t\":\"invecchiare\",\"n\":\"invecchiare\",\"p\":\"SO\",\"o\":\"intermezzo\",\"u\":1,\"l\":58,\"a\":[\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Versus can't find the DISC on Emporio/Jolyne and Ermes approach\"]},{\"t\":\"Invisible Corpse\",\"n\":\"invisible corpse\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":4,\"l\":88,\"a\":[],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"Sports Maxx meets with Pucci\",\"Ermes attacked by an invisible alligator/Jolyne and F.F. arrive/F.F.'s leg gets bitten off\",\"Sports Maxx surrounds and taunts Ermes\",\"A memory of Sports Maxx appears/Ermes falls into the hole\"]},{\"t\":\"Irreversible Sorrow\",\"n\":\"irreversible sorrow\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":12,\"l\":78,\"a\":[\"tristeza\"],\"m\":{\"medio\":11,\"final\":1},\"d\":[],\"e\":[\"Ryohei's death/Josuke swears to protect Morioh\",\"The family photo is repaired\",\"There ",
        "is still hope for the Nijimuras' father\",\"The death of Keicho Nijimura\"]},{\"t\":\"Italian Restaurant\",\"n\":\"italian restaurant\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":3,\"l\":149,\"a\":[\"calma\",\"tristeza\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Tonio Trussardi the Italian chef\",\"The antipasto: mozarella and tomato slices\",\"Primo piatto: Spaghetti alla puttanesca\"]},{\"t\":\"Joestar Family\",\"n\":\"joestar family\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":2,\"l\":44,\"a\":[\"villano\",\"calma\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Dio moves into the Joestar Mansion.\",\"Restaurant music.\"]},{\"t\":\"JoJo ~The Fate of That Blood~\",\"n\":\"jojo\",\"p\":\"PB/BT\",\"o\":\"\",\"u\":9,\"l\":89,\"a\":[\"epico\"],\"m\":{\"opening\":8,\"medio\":1},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"JoJo: The Memories of That Blood ~end of THE WORLD~\",\"n\":\"jojo the memories of that blood\",\"p\":\"SC\",\"o\":\"world\",\"u\":22,\"l\":89,\"a\":[\"tristeza\"],\"m\":{\"opening\":22},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"Killer\",\"n\":\"killer\",\"p\":\"DU\",\"o\":\"good night\",\"u\":16,\"l\":56,\"a\":[\"villano\"],\"m\":{\"medio\":9,\"inicio\":5,\"final\":2},\"d\":[\"kira\"],\"e\":[\"Introduction of Yoshikage Kira, serial-killer\",\"Yoshikage Kira sees the salon\",\"Yoshikage Kira's picnic\",\"Kira is protected by lady luck\"]},{\"t\":\"Knights of Terror\",\"n\":\"knights of terror\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":2,\"l\":80,\"a\":[\"pelea\",\"calma\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Tarkus has the advantage.\",\"Zeppeli vs. Tarkus.\"]},{\"t\":\"l'oscurita\",\"n\":\"l oscurita\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":10,\"l\":46,\"a\":[\"villano\"],\"m\":{\"medio\":8,\"inicio\":1,\"final\":1},\"d\":[],\"e\":[\"Giorno saves Koichi\",\"Formaggio gets into the car\",\"Narancia gets a new tongue\",\"Squalo and Tizzano escape from Narancia\"]},{\"t\":\"la battaglia finale\",\"n\":\"la battaglia finale\",\"p\":\"GW\",\"o\":\"finale\",\"u\":2,\"l\":76,\"a\":[\"pelea\",\"epico\",\"villano\"],\"m\":{\"final\":1,\"medio\":1},\"d\":[],\"e\":[\"King Crimson punches Trish in the stomach\",\"Diavolo erases time and attacks Giorno\"]},{\"t\":\"la strada giusta\",\"n\":\"la strada giusta\",\"p\":\"GW\",\"o\":\"finale\",\"u\":4,",
        "\"l\":162,\"a\":[\"tristeza\"],\"m\":{\"medio\":4},\"d\":[\"bucciarati\"],\"e\":[\"Bucciarati's team reacts to Abbacchio's death\",\"Bucciarati reveals to Giorno he is dead\",\"Narancia is killed by King Crimson\",\"Giorno sees Bucciarati, Narancia and Abbacchio in the clouds\"]},{\"t\":\"Lady With Beautiful Legs\",\"n\":\"lady with beautiful legs\",\"p\":\"SC\",\"o\":\"destination\",\"u\":5,\"l\":53,\"a\":[\"tension\"],\"m\":{\"medio\":3,\"final\":2},\"d\":[],\"e\":[\"A beautiful lady appears\",\"Joseph and Avdol pursue Mariah\",\"Pursuing Mariah in the streets/Avdol & Joseph stuck together\",\"Mariah gloats a little too soon\"]},{\"t\":\"Last Train Home\",\"n\":\"last train home\",\"p\":\"SC\",\"o\":\"\",\"u\":18,\"l\":89,\"a\":[],\"m\":{\"ending\":18},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"legame\",\"n\":\"legame\",\"p\":\"GW\",\"o\":\"overture\",\"u\":13,\"l\":70,\"a\":[\"epico\",\"victoria\"],\"m\":{\"medio\":7,\"final\":6},\"d\":[],\"e\":[\"Giorno reflects Luca's attack\",\"Giorno plans to become a gang-star\",\"Giorno indirectly kills Polpo\",\"Abbacchio awakens\"]},{\"t\":\"Life-and-Death Matter\",\"n\":\"life and death matter\",\"p\":\"SC\",\"o\":\"destination\",\"u\":5,\"l\":148,\"a\":[\"tension\",\"epico\"],\"m\":{\"medio\":3,\"final\":1,\"inicio\":1},\"d\":[\"jotaro\"],\"e\":[\"Jotaro's determination & Star Platinum's speed\",\"Racing in the tunnel\",\"Jotaro's determination/\\\"I've pretty much mastered how to swing\\\"\",\"Vanilla Ice violently kicks Iggy\"]},{\"t\":\"Life-and-Death Matter\",\"n\":\"life and death matter\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":18,\"a\":[\"explicacion\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jolyne explains the situation to Emporio\"]},{\"t\":\"Lightning Speed\",\"n\":\"lightning speed\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":2,\"l\":113,\"a\":[\"villano\"],\"m\":{\"inicio\":1,\"medio\":1},\"d\":[],\"e\":[\"Jonathan and Dio play rugby.\",\"A glider made of leaves.\"]},{\"t\":\"Looking Toward Tomorrow\",\"n\":\"looking toward tomorrow\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":140,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Poco's Courage.\"]},{\"t\":\"Looming Crisis\",\"n\":\"looming crisis\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":19,\"l\":65,\"a\":[\"tension\"],\"m\":{\"medio\":15,\"inicio\"",
        ":2,\"final\":2},\"d\":[],\"e\":[\"Aqua Necklace kills Ryohei\",\"Koichi summons his Stand\",\"Something in the attic\",\"Surface has already lured Jotaro out\"]},{\"t\":\"Looming Crisis\",\"n\":\"looming crisis\",\"p\":\"GW\",\"o\":\"good morning\",\"u\":1,\"l\":58,\"a\":[\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Mista tells Sale how many bullets he has\"]},{\"t\":\"Looming Crisis\",\"n\":\"looming crisis\",\"p\":\"SO\",\"o\":\"good morning\",\"u\":1,\"l\":38,\"a\":[\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Wes interrogates the thug/kills him with Heavy Weather\"]},{\"t\":\"lotta feroce\",\"n\":\"lotta feroce\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":12,\"l\":153,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":8,\"final\":2,\"recap\":1,\"inicio\":1},\"d\":[],\"e\":[\"Bruno tastes a liar\",\"Giorno begins his battle with Bucciarati\",\"Bruno sinks the yacht and defeats Zucchero\",\"Sale reveals his Stand's ability\"]},{\"t\":\"Love\",\"n\":\"love\",\"p\":\"DU\",\"o\":\"good night\",\"u\":6,\"l\":20,\"a\":[\"villano\"],\"m\":{\"medio\":4,\"inicio\":2},\"d\":[\"kira\"],\"e\":[\"Shinobu is feeling in love\",\"Shinobu thinks Kira was being romantic\",\"Shinobu happy to cling to Kira's chest\",\"A British blue cat\"]},{\"t\":\"Love for the Father\",\"n\":\"love for the father\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":2,\"l\":98,\"a\":[\"tristeza\"],\"m\":{\"medio\":1,\"inicio\":1},\"d\":[],\"e\":[\"George Joestar's death.\",\"Erina Pendleton tending to JoJo's wounds.\"]},{\"t\":\"Love, Lively\",\"n\":\"love lively\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":2,\"l\":35,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Jonathan's dates with Erina.\",\"Leaving on a honeymoon.\"]},{\"t\":\"Loyal Follower\",\"n\":\"loyal follower\",\"p\":\"SC\",\"o\":\"departure\",\"u\":2,\"l\":136,\"a\":[\"villano\",\"viaje\"],\"m\":{\"medio\":1,\"inicio\":1},\"d\":[],\"e\":[\"Enya welcomes the heroes in her hotel\",\"Polnareff enraging Enya\"]},{\"t\":\"Mad Dash\",\"n\":\"mad dash\",\"p\":\"SC\",\"o\":\"destination\",\"u\":1,\"l\":90,\"a\":[\"pelea\",\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Running in the streets and gathering metal\"]},{\"t\":\"magnetica\",\"n\":\"magnetica\",\"p\":\"GW\",\"o\":\"finale\",\"u\":3,\"l\":92,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Risotto attacks Dopp",
        "io\",\"Risotto almost kills Doppio\",\"Risotto grabs The Boss\"]},{\"t\":\"male\",\"n\":\"male\",\"p\":\"GW\",\"o\":\"overture\",\"u\":14,\"l\":92,\"a\":[\"tension\",\"villano\"],\"m\":{\"medio\":9,\"inicio\":5},\"d\":[],\"e\":[\"Bruno appears\",\"Giorno discovers injured gangster\",\"Giorno witnesses gangster hit\",\"Abbacchio's backstory\"]},{\"t\":\"male\",\"n\":\"male\",\"p\":\"SO\",\"o\":\"overture\",\"u\":1,\"l\":74,\"a\":[\"villano\",\"explicacion\",\"tristeza\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Rikiel explains his past to Pucci\"]},{\"t\":\"Malice\",\"n\":\"malice\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":28,\"l\":76,\"a\":[\"villano\"],\"m\":{\"medio\":20,\"final\":5,\"inicio\":3},\"d\":[],\"e\":[\"A peculiar first-year appears\",\"A looming crisis\",\"New enemies!\",\"Josuke interrogates Okuyasu\"]},{\"t\":\"master\u301cacostic\u301c\",\"n\":\"masteracostic\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":2,\"l\":78,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Lisa Lisa appears.\",\"Saint-Moritz.\"]},{\"t\":\"maze\",\"n\":\"maze\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":5,\"l\":46,\"a\":[\"explicacion\",\"misterio\",\"viaje\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[\"joseph\"],\"e\":[\"Caesar explains why he looks down on Joseph.\",\"The remains of the still alive Esidisi, his brain, stuck to Joseph's back.\",\"Joseph is worried about his ring./A suspicious hotel.\",\"Kars proposes that Lisa Lisa drinks a suicide poison.\"]},{\"t\":\"Memories\",\"n\":\"memories\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":10,\"l\":110,\"a\":[\"villano\",\"tristeza\"],\"m\":{\"medio\":6,\"inicio\":2,\"final\":2},\"d\":[\"pucci\"],\"e\":[\"Miraschon's meeting with Pucci\",\"Pucci realizes the Green Baby has been born\",\"Another son of DIO is revealed\",\"Rikiel believes Jolyne and Weather Report's powerful fates will help Pucci\"]},{\"t\":\"meraviglia\",\"n\":\"meraviglia\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":9,\"l\":58,\"a\":[\"misterio\",\"calma\"],\"m\":{\"medio\":7,\"inicio\":2},\"d\":[],\"e\":[\"Giorno sits in a tree\",\"Giorno and Koichi think of how to defeat Black Sabbath\",\"Giorno amazes the team\",\"Fugo and Narancia talk\"]},{\"t\":\"Messiah\",\"n\":\"messiah\",\"p\":\"SO\",\"o\":\"\",\"u\":1,\"l\":30,\"a\":[\"villano\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Pucci plays \\\"Messiah\\\" on Guccio\"]},{\"t\":",
        "\"Microorganism\",\"n\":\"microorganism\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":9,\"l\":35,\"a\":[\"pelea\",\"tension\",\"villano\"],\"m\":{\"medio\":6,\"final\":2,\"recap\":1},\"d\":[],\"e\":[\"Foo Fighters pursuing Jolyne and Ermes\",\"F.F. takes over a new body\",\"F.F. wants her water back\",\"Throwing in the dark/Cornering Miraschon\"]},{\"t\":\"misterioso\",\"n\":\"misterioso\",\"p\":\"GW\",\"o\":\"overture\",\"u\":31,\"l\":56,\"a\":[\"misterio\"],\"m\":{\"medio\":20,\"inicio\":6,\"final\":4,\"recap\":1},\"d\":[],\"e\":[\"Luca learns Giorno's name\",\"Giorno meets Luca\",\"Giorno's attack fails\",\"Black Sabbath tries to pierce Giorno with the arrow\"]},{\"t\":\"misterioso\",\"n\":\"misterioso\",\"p\":\"SO\",\"o\":\"overture\",\"u\":1,\"l\":85,\"a\":[\"pelea\",\"misterio\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"The Big Bad Wolf attacks\"]},{\"t\":\"Modern Crusaders\",\"n\":\"modern crusaders\",\"p\":\"GW\",\"o\":\"\",\"u\":17,\"l\":99,\"a\":[\"tristeza\",\"epico\"],\"m\":{\"ending\":17},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"Morioh in the Early Afternoon\",\"n\":\"morioh in the early afternoon\",\"p\":\"DU\",\"o\":\"\",\"u\":12,\"l\":23,\"a\":[],\"m\":{\"medio\":7,\"final\":4,\"inicio\":1},\"d\":[],\"e\":[\"Anjuro becomes a landmark\",\"Aftermath of the battle\",\"How to definitely get unpopular with girls\",\"Okuyasu's stiff shoulder is cured\"]},{\"t\":\"Morioh Town Radio\",\"n\":\"morioh town radio\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":10,\"l\":22,\"a\":[\"villano\"],\"m\":{\"inicio\":7,\"medio\":3},\"d\":[],\"e\":[\"Morioh-cho RADIO\",\"Morioh-cho RADIO\",\"Morioh-cho RADIO\",\"Morioh-cho RADIO\"]},{\"t\":\"Morning etude for Charlie\",\"n\":\"morning etude for charlie\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":4,\"l\":86,\"a\":[\"calma\",\"misterio\"],\"m\":{\"medio\":3,\"inicio\":1},\"d\":[],\"e\":[\"Stroheim's first appearance.\",\"The Nazi testing facility.\",\"A mysterious Nazi officer...\",\"Stroheim is now a cyborg/Kars swears revenge.\"]},{\"t\":\"morte\",\"n\":\"morte\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":7,\"l\":86,\"a\":[\"tristeza\"],\"m\":{\"medio\":7},\"d\":[\"trish\"],\"e\":[\"Narancia gets an eye infection and gives up on life\",\"Bruno tells Trish to keep the ice\",\"Sex Pistols are alive\",\"Trish worries about meeting her father\"",
        "]},{\"t\":\"muffa\",\"n\":\"muffa\",\"p\":\"GW\",\"o\":\"finale\",\"u\":5,\"l\":56,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[],\"e\":[\"Cioccolata and Secco's introduction\",\"Bruno's team is attacked by mold\",\"Secco records Narancia\",\"Cioccolata catches up to Bruno and his team\"]},{\"t\":\"Mysterious Visitor\",\"n\":\"mysterious visitor\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":88,\"a\":[\"misterio\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Will Anthonio Zeppeli appears.\"]},{\"t\":\"mystic\",\"n\":\"mystic\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":6,\"l\":48,\"a\":[],\"m\":{\"medio\":4,\"final\":2},\"d\":[\"joseph\"],\"e\":[\"Caesar's respect for Joseph.\",\"Caesar's respect for Joseph.\",\"Joseph and Lisa Lisa decide to go after Caesar.\",\"Joseph's compassion.\"]},{\"t\":\"nella cerniera\",\"n\":\"nella cerniera\",\"p\":\"GW\",\"o\":\"overture\",\"u\":5,\"l\":14,\"a\":[\"villano\",\"pelea\",\"epico\"],\"m\":{\"medio\":3,\"final\":1,\"inicio\":1},\"d\":[\"bucciarati\"],\"e\":[\"Bucciarati breaks Pesci's neck\",\"Bucciarati pummels Secco\",\"Bruno's soul is inside Diavolo's body\",\"Bruno attacks Chariot Requiem\"]},{\"t\":\"nervoso\",\"n\":\"nervoso\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":22,\"l\":58,\"a\":[\"tension\"],\"m\":{\"medio\":12,\"final\":8,\"inicio\":2},\"d\":[],\"e\":[\"Bruno questions Giorno\",\"Giorno's intent to kill Bucciarati\",\"Giorno takes the lighter and begins his entrance exam\",\"The bullet goes deeper inside Sale's brain\"]},{\"t\":\"Never Be Mine\",\"n\":\"never be mine\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":8,\"l\":64,\"a\":[\"tristeza\"],\"m\":{\"medio\":7,\"final\":1},\"d\":[\"joseph\"],\"e\":[\"Erina's despair.\",\"Joseph pays respect to his instructor.\",\"Joseph pays his respect to the deceased Esidisi.\",\"The Zeppeli Family spirit!/Caesar Anthonio Zeppeli's death./The Crimson Bubble.\"]},{\"t\":\"New Moon Gravity\",\"n\":\"new moon gravity\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":3,\"l\":121,\"a\":[\"villano\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Westwood's ability/Meteors crash through the glass ceiling\",\"Jolyne touches Pucci/Pucci starts a fire\",\"Emporio discovers Jolyne and Jotaro's doppelg\u00e4ngers/in this universe, all of Pucci's enemies are dead...\"]},{\"t\":",
        "\"Newfound Courage\",\"n\":\"newfound courage\",\"p\":\"DU\",\"o\":\"good night\",\"u\":18,\"l\":42,\"a\":[\"misterio\",\"villano\"],\"m\":{\"medio\":9,\"final\":5,\"inicio\":4},\"d\":[],\"e\":[\"Kosaku has changed/The owner demands the rent\",\"The Kawajiri son, Hayato, is suspicious\",\"Shinobu sees a cat\",\"Hayato leaves the house/Something has happened in the basement\"]},{\"t\":\"Nightmare World\",\"n\":\"nightmare world\",\"p\":\"DU\",\"o\":\"destination\",\"u\":1,\"l\":86,\"a\":[\"calma\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Shinobu and the cat\"]},{\"t\":\"Nightmare World\",\"n\":\"nightmare world\",\"p\":\"SC\",\"o\":\"destination\",\"u\":6,\"l\":60,\"a\":[\"misterio\"],\"m\":{\"medio\":5,\"inicio\":1},\"d\":[],\"e\":[\"Kakyoin in a strange theme park\",\"Kakyoin and Polnareff in the dream\",\"Joseph thinking it's a mere dream\",\"Fake Star Platinum\"]},{\"t\":\"Nightmare World\",\"n\":\"nightmare world\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":18,\"a\":[\"tension\",\"calma\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"McQueen catches Ermes stalking him/Ermes confronts McQueen about the DISCs\"]},{\"t\":\"Noble Hierophant\",\"n\":\"noble hierophant\",\"p\":\"SC\",\"o\":\"departure\",\"u\":10,\"l\":75,\"a\":[\"victoria\",\"pelea\"],\"m\":{\"medio\":8,\"inicio\":1,\"final\":1},\"d\":[\"kakyoin\"],\"e\":[\"Kakyoin fights Tower of Gray\",\"Polnareff defeats the doll\",\"Kakyoin to the rescue\",\"Polnareff wounds and traps Hanged Man\"]},{\"t\":\"Not Alone\",\"n\":\"not alone\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":3,\"l\":67,\"a\":[],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Joseph takes the antidote.\",\"Joseph pays his respect to the fallen Wamuu.\",\"The story of Elizabeth and George Joestar.\"]},{\"t\":\"Oddity\",\"n\":\"oddity\",\"p\":\"DU\",\"o\":\"\",\"u\":21,\"l\":70,\"a\":[\"misterio\"],\"m\":{\"medio\":13,\"inicio\":5,\"final\":3},\"d\":[],\"e\":[\"Hand foreshadowing\",\"Something is lurking in this town\",\"Anjuro Katagiri threatens the Higashikata\",\"Katagiri Anjuro's crimes\"]},{\"t\":\"Oddity\",\"n\":\"oddity\",\"p\":\"GW\",\"o\":\"\",\"u\":1,\"l\":109,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Mista thinks on how to kill Sale\"]},{\"t\":\"Oh My God!\",\"n\":\"oh my god\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":6,\"l\":42,\"a\":[\"tristeza\"],\"m\":{\"medio\":5,\"in",
        "icio\":1},\"d\":[],\"e\":[\"How to smuggle cash inside of prison\",\"Playing catch\",\"Jolyne asks for the phone\",\"Kenzou's legs have turned into springs?\"]},{\"t\":\"Oh please..\",\"n\":\"oh please\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":20,\"l\":15,\"a\":[],\"m\":{\"avance\":17,\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Next episode preview\",\"Next episode preview\",\"Joseph defeats Straizo.\",\"Next episode preview\"]},{\"t\":\"old town\",\"n\":\"old town\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":5,\"l\":45,\"a\":[\"pelea\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[\"joseph\"],\"e\":[\"Smokey Brown steals Joseph Joestar's wallet.\",\"The hostage punches Joseph.\",\"Joseph and Caesar playing poker.\",\"Joseph peeps on Lisa Lisa.\"]},{\"t\":\"Omen\",\"n\":\"omen\",\"p\":\"GW\",\"o\":\"departure\",\"u\":1,\"l\":45,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Polnareff's reveal\"]},{\"t\":\"Omen\",\"n\":\"omen\",\"p\":\"SC\",\"o\":\"departure\",\"u\":22,\"l\":44,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":11,\"final\":5,\"inicio\":4,\"recap\":2},\"d\":[],\"e\":[\"Abandoned boat found\",\"Jotaro's reaction to the nurse\",\"Jotaro decides to bring Kakyoin to his house\",\"Avdol alone by night\"]},{\"t\":\"Omen\",\"n\":\"omen\",\"p\":\"SO\",\"o\":\"departure\",\"u\":1,\"l\":51,\"a\":[\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Operation Savage Garden begins\"]},{\"t\":\"Orchestrated Battle\",\"n\":\"orchestrated battle\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":5,\"l\":82,\"a\":[\"pelea\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[],\"e\":[\"A mysterious man shoots Angelo with an Arrow\",\"Koichi is shot/Okuyasu engages Josuke\",\"Keichi Nijimura appears\",\"Keicho's Bad Company\"]},{\"t\":\"Oui Monsieur\",\"n\":\"oui monsieur\",\"p\":\"PB/BT\",\"o\":\"\",\"u\":1,\"l\":10,\"a\":[],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"A bird's-eye view of Rome and the [Colosseum](https://jojowiki.com/Colosseum).\"]},{\"t\":\"Over-Drive\",\"n\":\"over drive\",\"p\":\"SC\",\"o\":\"world\",\"u\":1,\"l\":200,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Playing at F-MEGA\"]},{\"t\":\"Overdrive\",\"n\":\"overdrive\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":12,\"l\":40,\"a\":[\"pelea\"],\"m\":{\"medio\":11,\"inicio\":1},\"d\":[\"joseph\"],\"e\":[\"Tarkus attacks.\",\"Hostage situation.\",\"Joseph places grena",
        "des on Straizo.\",\"Joseph blasts Santana apart.\"]},{\"t\":\"pace\",\"n\":\"pace\",\"p\":\"GW\",\"o\":\"overture\",\"u\":6,\"l\":78,\"a\":[\"calma\",\"epico\"],\"m\":{\"medio\":5,\"final\":1},\"d\":[\"giorno\"],\"e\":[\"Giorno tells Koichi his dream\",\"Bruno introduces Giorno to his team\",\"Giorno retrieves the disc\",\"Giorno gives Bucciarati his brooch\"]},{\"t\":\"Pain Just Like Strange Rain\",\"n\":\"pain just like strange rain\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":4,\"l\":82,\"a\":[\"misterio\",\"tristeza\",\"viaje\"],\"m\":{\"final\":2,\"medio\":1,\"inicio\":1},\"d\":[],\"e\":[\"The Pillar Man was absorbing blood of Straizo's victims on the temple./Straizo's suicide.\",\"Mark's death.\",\"Caesar is headed to the hotel.\",\"Caesar and Mario Zeppeli's backhistory\"]},{\"t\":\"Pale Snake\",\"n\":\"pale snake\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":15,\"l\":68,\"a\":[\"villano\"],\"m\":{\"medio\":5,\"final\":5,\"recap\":3,\"inicio\":2},\"d\":[\"pucci\"],\"e\":[\"Whitesnake steals Jotaro's Stand and memory\",\"Whitesnake eliminates Johngalli A.\",\"McQueen is the worst kind of evil/Ermes breaks free of McQueen's propellers\",\"Whitesnake investigates the barn\"]},{\"t\":\"Parting Regrets\",\"n\":\"parting regrets\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":4,\"l\":94,\"a\":[\"tristeza\"],\"m\":{\"final\":2,\"medio\":2},\"d\":[],\"e\":[\"Jonathan at Danny's grave.\",\"Bruford regains his humanity.\",\"Will Anthonio Zeppeli's death.\",\"Jonathan Joestar's death.\"]},{\"t\":\"Pass Away\",\"n\":\"pass away\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":2,\"l\":98,\"a\":[\"tristeza\"],\"m\":{\"final\":1,\"medio\":1},\"d\":[],\"e\":[\"F.F.'s goodbye\",\"Jolyne is dead?\"]},{\"t\":\"passato\",\"n\":\"passato\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":9,\"l\":82,\"a\":[\"tristeza\"],\"m\":{\"medio\":8,\"inicio\":1},\"d\":[\"trish\"],\"e\":[\"Trish finishes changing her clothes\",\"Narancia's past\",\"Fugo's past\",\"Continuation of Mista's backstory\"]},{\"t\":\"Passing Anxiety\",\"n\":\"passing anxiety\",\"p\":\"DU\",\"o\":\"\",\"u\":8,\"l\":62,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":6,\"final\":2},\"d\":[],\"e\":[\"Too many tears\",\"An invisible baby Stand user\",\"Rohan Kishibe is suspicious\",\"Rohan has done something to Koichi\"]},{\"t\":\"passione\",\"n\":\"passione\",",
        "\"p\":\"GW\",\"o\":\"overture\",\"u\":10,\"l\":32,\"a\":[\"villano\",\"tension\"],\"m\":{\"medio\":8,\"final\":1,\"inicio\":1},\"d\":[],\"e\":[\"Giorno meets Polpo\",\"Black Sabbath (Polpo's Stand) appears\",\"Black Sabbath attacks the old janitor\",\"Polpo accepts Giorno into Passione\"]},{\"t\":\"passione\",\"n\":\"passione\",\"p\":\"SO\",\"o\":\"overture\",\"u\":1,\"l\":36,\"a\":[],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Versus grows impatient\"]},{\"t\":\"pazzo\",\"n\":\"pazzo\",\"p\":\"GW\",\"o\":\"finale\",\"u\":3,\"l\":57,\"a\":[\"villano\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Cioccolata's backstory\",\"People start to turn into monsters\",\"Bruno's team is shocked\"]},{\"t\":\"Peace\",\"n\":\"peace\",\"p\":\"DU\",\"o\":\"\",\"u\":19,\"l\":35,\"a\":[\"calma\"],\"m\":{\"medio\":9,\"inicio\":5,\"final\":5},\"d\":[],\"e\":[\"Ryohei pranks Josuke\",\"\\\"Yo, Angelo\\\"\",\"Josuke and Koichi's teamwork tricked Hazamada\",\"Koichi asks what Yukako wants\"]},{\"t\":\"Peaceful Street Corner\",\"n\":\"peaceful street corner\",\"p\":\"DU\",\"o\":\"\",\"u\":20,\"l\":27,\"a\":[\"calma\"],\"m\":{\"final\":10,\"medio\":8,\"inicio\":2},\"d\":[\"josuke\"],\"e\":[\"Josuke's groupies\",\"Josuke & Koichi are late\",\"Josuke & Okuyasu are pals now/\\\"Your mom's a total babe\\\"\",\"Tamami becomes Koichi's lackey\"]},{\"t\":\"pensare\",\"n\":\"pensare\",\"p\":\"GW\",\"o\":\"finale\",\"u\":15,\"l\":64,\"a\":[\"tristeza\",\"calma\"],\"m\":{\"medio\":12,\"final\":2,\"inicio\":1},\"d\":[],\"e\":[\"The team waits for Narancia\",\"Bruno gets another message from The Boss\",\"Abbacchio reads the message on the key\",\"Giorno figures out The Grateful Dead's weakness\"]},{\"t\":\"permanenza\",\"n\":\"permanenza\",\"p\":\"GW\",\"o\":\"finale\",\"u\":2,\"l\":166,\"a\":[\"misterio\",\"tristeza\"],\"m\":{\"medio\":1,\"inicio\":1},\"d\":[],\"e\":[\"Polnareff explains Diavolo's split personality\",\"Diavolo dies from a drug addict\"]},{\"t\":\"Persistence ~Innocent Scream~\",\"n\":\"persistence\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":5,\"l\":197,\"a\":[\"villano\"],\"m\":{\"medio\":5},\"d\":[\"dio\"],\"e\":[\"JoJo and Dio plunge into the fire/Jonathan emerges victorious.\",\"Zeppeli reveals that Dio is still alive.\",\"Jonathan will fight Bruford.\",\"Jonathan defeats Dio.\"]},{\"t\":\"pesce\",\"n\":\"pesce\",\"p\":\"GW\",\"o\":\"in",
        "termezzo\",\"u\":4,\"l\":64,\"a\":[\"pelea\"],\"m\":{\"medio\":3,\"inicio\":1},\"d\":[],\"e\":[\"Pesci uses Beach Boy\",\"Mista is on Beach Boy's hook\",\"Beach Boy chases Bucciarati\",\"Beach Boy reaches Bruno's heart\"]},{\"t\":\"piccolo\",\"n\":\"piccolo\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":3,\"l\":66,\"a\":[\"comedia\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Narancia notices he became smaller\",\"Formaggio picks up Narancia's map and laughs\",\"Formaggio extinguishes himself and hides\"]},{\"t\":\"piccolo\",\"n\":\"piccolo\",\"p\":\"SBR\",\"o\":\"intermezzo\",\"u\":1,\"l\":11,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Gyro sees Tomb of the Boom\"]},{\"t\":\"Pierrot Headroom\",\"n\":\"pierrot headroom\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":4,\"l\":28,\"a\":[\"pelea\"],\"m\":{\"medio\":3,\"final\":1},\"d\":[\"joseph\"],\"e\":[\"Joseph in Mexico.\",\"Joseph beats up Wamuu with the clackers.\",\"Double Ripple!\",\"Joseph's trick with the rope.\"]},{\"t\":\"Possession\",\"n\":\"possession\",\"p\":\"SC\",\"o\":\"journey\",\"u\":2,\"l\":72,\"a\":[\"pelea\"],\"m\":{\"inicio\":1,\"medio\":1},\"d\":[],\"e\":[\"Kakyoin attacks Jotaro though a painting\",\"Jotaro meets Kakyoin\"]},{\"t\":\"Powerful Enemy\",\"n\":\"powerful enemy\",\"p\":\"SC\",\"o\":\"destination\",\"u\":20,\"l\":81,\"a\":[\"villano\",\"tension\"],\"m\":{\"medio\":17,\"inicio\":2,\"final\":1},\"d\":[],\"e\":[\"Kakyoin left alone with Death 13\",\"Is Kakyoin mad?/Polnareff knocks out Kakyoin\",\"Death 13 scything itself\",\"Waiting in the desert\"]},{\"t\":\"Powerful Enemy\",\"n\":\"powerful enemy\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":64,\"a\":[\"explicacion\",\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"How Survivor's ability works\"]},{\"t\":\"Pressure\",\"n\":\"pressure\",\"p\":\"SC\",\"o\":\"destination\",\"u\":17,\"l\":42,\"a\":[\"pelea\"],\"m\":{\"medio\":11,\"final\":4,\"inicio\":2},\"d\":[],\"e\":[\"A scorching heat and no enemy in sight\",\"Death Thirteen attacks Jotaro/Star Platinum\",\"Death 13 is almighty in the nightmare\",\"The heroes must evacuate the sub\"]},{\"t\":\"Pressure\",\"n\":\"pressure\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":64,\"a\":[\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Anasui's body double trick/Pucci's face turns inside out\"]},{\"t\":\"Priest\",\"n\":\"pri",
        "est\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":29,\"l\":45,\"a\":[\"villano\"],\"m\":{\"medio\":13,\"final\":7,\"inicio\":5,\"recap\":4},\"d\":[\"pucci\"],\"e\":[\"Enrico Pucci appears\",\"Pucci monologuing\",\"Pucci inquires about the phone call log\",\"Jolyne encounters Pucci\"]},{\"t\":\"proiettile\",\"n\":\"proiettile\",\"p\":\"GW\",\"o\":\"overture\",\"u\":4,\"l\":12,\"a\":[\"pelea\"],\"m\":{\"medio\":4},\"d\":[\"mista\"],\"e\":[\"Sex Pistols destroy Pesci's ice\",\"Mista shoots Ghiaccio\",\"Mista shoots in the airhole\",\"Mista shoots Carne\"]},{\"t\":\"Propaganda\",\"n\":\"propaganda\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":6,\"l\":45,\"a\":[],\"m\":{\"medio\":5,\"final\":1},\"d\":[],\"e\":[\"Stroheim steps in.\",\"The strength of a German cyborg!\",\"Eye UV laser!\",\"The Nazis and the Speedwagon Foundation to the rescue!\"]},{\"t\":\"pulse\",\"n\":\"pulse\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":12,\"l\":44,\"a\":[\"pelea\"],\"m\":{\"medio\":9,\"final\":2,\"inicio\":1},\"d\":[\"joseph\"],\"e\":[\"Corrupt policemen catch Smokey.\",\"Joseph survives Straizo's Space Ripper Stingy Eyes.\",\"Santana survives the explosion.\",\"Joseph vs. Caesar.\"]},{\"t\":\"Puppet\",\"n\":\"puppet\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":19,\"l\":75,\"a\":[\"villano\"],\"m\":{\"medio\":11,\"final\":4,\"inicio\":4},\"d\":[],\"e\":[\"Surface, the puppet master\",\"Hazamada the incel\",\"Josuke is controlled by Surface\",\"Hazamada claiming that Stand users are drawn to each other\"]},{\"t\":\"Puppet\",\"n\":\"puppet\",\"p\":\"SO\",\"o\":\"good morning\",\"u\":3,\"l\":82,\"a\":[\"tristeza\",\"pelea\",\"villano\"],\"m\":{\"medio\":1,\"final\":1,\"inicio\":1},\"d\":[],\"e\":[\"The enemy's nature is revealed/Weather shoots himself\",\"Weather regains his memories\",\"Weather's memory has returned\"]},{\"t\":\"puro\",\"n\":\"puro\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":4,\"l\":52,\"a\":[\"calma\"],\"m\":{\"inicio\":3,\"medio\":1},\"d\":[],\"e\":[\"Giorno steals money\",\"Mista is having a lunch break\",\"Continuation of Mista's backstory\",\"Bruno and his team eat\"]},{\"t\":\"Purple Thorns\",\"n\":\"purple thorns\",\"p\":\"DU\",\"o\":\"departure\",\"u\":1,\"l\":24,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Tomoko mistakes Jotaro for Joseph\"]},{\"t\":\"Purple Thorns\",\"n\":\"purple thorns\",\"p\":\"SC\",\"o\":\"depa",
        "rture\",\"u\":8,\"l\":49,\"a\":[\"viaje\",\"explicacion\"],\"m\":{\"medio\":4,\"final\":2,\"inicio\":2},\"d\":[],\"e\":[\"Joseph shows Hermit Purple\",\"Arrival in India\",\"In the busy streets of Benares\",\"Description of Pakistan\"]},{\"t\":\"Pursuit\",\"n\":\"pursuit\",\"p\":\"DU\",\"o\":\"\",\"u\":3,\"l\":24,\"a\":[\"tension\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Highway Star appears\",\"Josuke cannot make a proper phone call\",\"Highway Star manages to reach Josuke\"]},{\"t\":\"Quietness\",\"n\":\"quietness\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":12,\"l\":66,\"a\":[\"calma\"],\"m\":{\"medio\":9,\"final\":2,\"inicio\":1},\"d\":[\"jolyne\"],\"e\":[\"Jolyne seeking revenge on her lawyer\",\"Breakfast is sold out\",\"Ermes remembers how she cut her hand\",\"Searching the farmlands/Dispute with the guard\"]},{\"t\":\"quirk\",\"n\":\"quirk\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":2,\"l\":46,\"a\":[],\"m\":{\"medio\":1,\"inicio\":1},\"d\":[],\"e\":[\"Straizo's betrayal.\",\"Straizo enjoying his Vampiric powers.\"]},{\"t\":\"Rebellion Against Despair\",\"n\":\"rebellion against despair\",\"p\":\"SC\",\"o\":\"destination\",\"u\":7,\"l\":72,\"a\":[\"villano\"],\"m\":{\"medio\":4,\"final\":1,\"recap\":1,\"inicio\":1},\"d\":[],\"e\":[\"D'Arby wins the first round, next game\",\"Pet Shop draws its full power\",\"Recap: Horus at full power\",\"Polnareff & Iggy vs. Vanilla Ice\"]},{\"t\":\"Rebellion Against Despair\",\"n\":\"rebellion against despair\",\"p\":\"SO\",\"o\":\"destination\",\"u\":2,\"l\":74,\"a\":[\"pelea\",\"victoria\",\"tension\"],\"m\":{\"medio\":1,\"final\":1},\"d\":[],\"e\":[\"C-MOON tries to stand/Jolyne's string inverting\",\"Jotaro manages to stop time before Jolyne is hit/Anasui saves Jotaro/Jotaro struggles to approach Pucci\"]},{\"t\":\"Repose of a Soul\",\"n\":\"repose of a soul\",\"p\":\"SC\",\"o\":\"departure\",\"u\":4,\"l\":136,\"a\":[\"calma\"],\"m\":{\"medio\":3,\"inicio\":1},\"d\":[],\"e\":[\"Polnareff runs after Ch\u00e9rie\",\"N'Doul's devotion to DIO\",\"Polnareff mourns Avdol\",\"Kakyoin, mortally wounded, realizes the truth\"]},{\"t\":\"Requiem\",\"n\":\"requiem\",\"p\":\"SC\",\"o\":\"destination\",\"u\":4,\"l\":106,\"a\":[\"tristeza\"],\"m\":{\"medio\":3,\"final\":1},\"d\":[\"iggy\"],\"e\":[\"Iggy is drowning\",\"\\\"Adieu, Iggy.\\\"\",\"Iggy's re",
        "solve saved Polnareff\",\"Avdol & Iggy's souls salute Polnareff\"]},{\"t\":\"Requiem\",\"n\":\"requiem\",\"p\":\"SO\",\"o\":\"destination\",\"u\":2,\"l\":169,\"a\":[\"tristeza\"],\"m\":{\"final\":1,\"recap\":1},\"d\":[],\"e\":[\"Weather is found dead\",\"Recap of Weather's death/Two more days until the new moon\"]},{\"t\":\"Requiem for a Traitor\",\"n\":\"requiem for a traitor\",\"p\":\"GW\",\"o\":\"\",\"u\":18,\"l\":91,\"a\":[\"tristeza\"],\"m\":{\"opening\":18},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"resa dei conti\",\"n\":\"resa dei conti\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":5,\"l\":57,\"a\":[\"pelea\",\"viaje\"],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"Zucchero jumps out of the car\",\"Moody Blues replays Abbacchio's actions and gets the key to Giorno\",\"Pesci takes out Coco Jumbo\",\"Mista shoots the screw into White Album's helmet\"]},{\"t\":\"Rest ~Piano Ver.~\",\"n\":\"rest\",\"p\":\"DU\",\"o\":\"journey\",\"u\":3,\"l\":117,\"a\":[\"calma\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[\"koichi\"],\"e\":[\"Koichi is with a girl\",\"Yukako Yamagishi has confessed her love to Koichi\",\"Yukako is literally crazy for Koichi\"]},{\"t\":\"Results of the Plot\",\"n\":\"results of the plot\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":4,\"l\":115,\"a\":[\"villano\",\"tension\",\"misterio\"],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"Jonathan confronts Dio about poisoning George.\",\"The Dark Knights, Bruford and Tarkus appear.\",\"Jonathan trapped with Tarkus in the Chamber of the Two-Headed Dragon.\",\"Dio traps Jonathan.\"]},{\"t\":\"Return from the Verge of Death\",\"n\":\"return from the verge of death\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":1,\"l\":29,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"The knights' training grounds.\"]},{\"t\":\"riproduzione\",\"n\":\"riproduzione\",\"p\":\"GW\",\"o\":\"overture\",\"u\":5,\"l\":48,\"a\":[\"misterio\"],\"m\":{\"medio\":5},\"d\":[\"abbacchio\"],\"e\":[\"Abbacchio uses Moody Blues to replay Narancia's actions\",\"Moody Blues continues to replay Narancia's actions\",\"Abbacchio is searching for Zucchero's location\",\"Moody Blues replays Zucchero talking to Sale\"]},{\"t\":\"ristorante bar\",\"n\":\"ristorante bar\",\"p\":\"GW\",\"o\":\"overture\",\"u\":7,\"l\":33,\"a\":[\"comedia\",\"calma\"],\"m\"",
        ":{\"medio\":4,\"inicio\":2,\"final\":1},\"d\":[\"mista\"],\"e\":[\"Giorno performs his ear trick\",\"Mista doesn't want to eat a cake with 4 slices\",\"Mista continues to torture Zucchero\",\"Bruno congratulates his team\"]},{\"t\":\"Roundabout\",\"n\":\"roundabout\",\"p\":\"PB/BT\",\"o\":\"\",\"u\":25,\"l\":92,\"a\":[],\"m\":{\"ending\":25},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"Roundabout\",\"n\":\"roundabout\",\"p\":\"SO\",\"o\":\"\",\"u\":1,\"l\":124,\"a\":[],\"m\":{\"ending\":1},\"d\":[],\"e\":[\"Ending\"]},{\"t\":\"Rubicon\",\"n\":\"rubicon\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":4,\"l\":64,\"a\":[\"misterio\"],\"m\":{\"medio\":2,\"final\":1,\"recap\":1},\"d\":[],\"e\":[\"Santana kills the remaining soldiers and researchers.\",\"Two crossbows given out for the racers.\",\"Something's wrong with Kars...\",\"Recap./JoJo can't use his hands.\"]},{\"t\":\"Sadness\",\"n\":\"sadness\",\"p\":\"DU\",\"o\":\"departure\",\"u\":1,\"l\":140,\"a\":[\"villano\",\"tristeza\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Kira has switched face with someone else!\"]},{\"t\":\"Sadness\",\"n\":\"sadness\",\"p\":\"SC\",\"o\":\"departure\",\"u\":5,\"l\":126,\"a\":[\"tristeza\"],\"m\":{\"medio\":4,\"final\":1},\"d\":[],\"e\":[\"Joseph narrates Jonathan's fate\",\"Polnareff explains how Ch\u00e9rie was killed\",\"Avdol's death\",\"Polnareff & Ch\u00e9rie reunited\"]},{\"t\":\"Sadness\",\"n\":\"sadness\",\"p\":\"SO\",\"o\":\"departure\",\"u\":1,\"l\":78,\"a\":[\"tristeza\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Jotaro's body in the care of the Speedwagon Foundation\"]},{\"t\":\"Scorching Flames\",\"n\":\"scorching flames\",\"p\":\"SBR\",\"o\":\"destination\",\"u\":1,\"l\":20,\"a\":[\"tension\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Urmd Avdul approaches\"]},{\"t\":\"Scorching Flames\",\"n\":\"scorching flames\",\"p\":\"SC\",\"o\":\"destination\",\"u\":3,\"l\":43,\"a\":[\"victoria\"],\"m\":{\"medio\":3},\"d\":[\"avdol\"],\"e\":[\"Magician's Red destroys the fake Avdol\",\"Avdol defeats Judgement\",\"Joseph and Avdol confront Mariah\"]},{\"t\":\"Season\",\"n\":\"season\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":1,\"l\":90,\"a\":[],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Epilogue for the cast.\"]},{\"t\":\"Second Bomb\",\"n\":\"second bomb\",\"p\":\"DU\",\"o\":\"good night\",\"u\":15,\"l\":56,\"a\":[\"villano\"],\"m\":{\"medio\":8,\"final\":5,\"inicio",
        "\":2},\"d\":[\"kira\"],\"e\":[\"Kira must retrieve the damning evidence\",\"Kira follows Shigechi to school\",\"Kira retrieves the bag\",\"Killer Queen reveal\"]},{\"t\":\"Secret Plans\",\"n\":\"secret plans\",\"p\":\"DU\",\"o\":\"good night\",\"u\":19,\"l\":48,\"a\":[\"misterio\",\"villano\"],\"m\":{\"medio\":15,\"inicio\":2,\"final\":2},\"d\":[],\"e\":[\"Angelo waits for the right moment\",\"Okuyasu helps Josuke save Koichi\",\"Koichi determined to neutralize Hazamada\",\"Koichi is missing\"]},{\"t\":\"Secret Plans\",\"n\":\"secret plans\",\"p\":\"SO\",\"o\":\"good night\",\"u\":1,\"l\":115,\"a\":[\"misterio\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Anasui's interrogation\"]},{\"t\":\"Secret Thoughts\",\"n\":\"secret thoughts\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":7,\"l\":89,\"a\":[\"villano\",\"misterio\"],\"m\":{\"medio\":4,\"inicio\":2,\"final\":1},\"d\":[\"dio\"],\"e\":[\"Dio resolved to control his emotions.\",\"Dio plots internally.\",\"Description of Windknight's Lot.\",\"Dio's cruelty.\"]},{\"t\":\"Separation and Departure\",\"n\":\"separation and departure\",\"p\":\"DU\",\"o\":\"good night\",\"u\":5,\"l\":120,\"a\":[\"viaje\"],\"m\":{\"medio\":4,\"final\":1},\"d\":[],\"e\":[\"Reimi is waiting by the Owson/Rohan's link to Reimi\",\"Reimi sees Shigechi's soul\",\"Rohan acknowledges Ken's guts\",\"Okuyasu has returned/Keicho's words to his little brother\"]},{\"t\":\"serenamente\",\"n\":\"serenamente\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":5,\"l\":36,\"a\":[\"tristeza\",\"pelea\"],\"m\":{\"medio\":3,\"inicio\":2},\"d\":[\"mista\",\"narancia\"],\"e\":[\"Bruno rents a yacht\",\"Mista's backstory\",\"Continuation of Narancia's past\",\"Mista and Narancia discuss food\"]},{\"t\":\"Setting Off\",\"n\":\"setting off\",\"p\":\"DU\",\"o\":\"departure\",\"u\":4,\"l\":30,\"a\":[\"viaje\"],\"m\":{\"medio\":2,\"final\":1,\"ending\":1},\"d\":[],\"e\":[\"Josuke is a reliable ally after all\",\"Yukako and Koichi kiss\",\"Koichi sees Yukako again and calls her\",\"A happy ending\"]},{\"t\":\"Setting Off\",\"n\":\"setting off\",\"p\":\"SC\",\"o\":\"departure\",\"u\":13,\"l\":39,\"a\":[\"viaje\",\"calma\"],\"m\":{\"medio\":8,\"final\":3,\"inicio\":2},\"d\":[],\"e\":[\"Holy knows that Jotaro is a good boy\",\"Kakyoin asks why he was saved\",\"Under the Red Sea\",\"Egypt is in sight\"]},{\"",
        "t\":\"Setting Off\",\"n\":\"setting off\",\"p\":\"SO\",\"o\":\"departure\",\"u\":2,\"l\":89,\"a\":[\"tristeza\",\"viaje\"],\"m\":{\"inicio\":1,\"medio\":1},\"d\":[],\"e\":[\"Both Jotaro and Jolyne share the same scars/Jolyne understands her father\",\"Emporio and Jolyne walk past the guards using Jail House Lock\"]},{\"t\":\"Shoot for a Decisive Battle\",\"n\":\"shoot for a decisive battle\",\"p\":\"SC\",\"o\":\"destination\",\"u\":4,\"l\":108,\"a\":[\"pelea\",\"epico\"],\"m\":{\"final\":2,\"medio\":2},\"d\":[],\"e\":[\"Vanilla Ice kills Avdol/Vanilla Ice vs. Polnareff & Iggy\",\"Polnareff ambushes Ice/Ice is immortal\",\"Kakyoin learns a bit about The World\",\"Jotaro awaits DIO's final attack\"]},{\"t\":\"Shoot for a Decisive Battle\",\"n\":\"shoot for a decisive battle\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":32,\"a\":[\"villano\",\"pelea\",\"epico\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Pucci cuts off Weather's other leg/\\\"Domine quo vadis?\\\"\"]},{\"t\":\"Silent Horizon\",\"n\":\"silent horizon\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":2,\"l\":76,\"a\":[\"villano\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Dio grabs Jonathan's hand.\",\"Erina happy to be with Jonathan.\"]},{\"t\":\"Silver Twist\",\"n\":\"silver twist\",\"p\":\"SC\",\"o\":\"departure\",\"u\":6,\"l\":37,\"a\":[\"comedia\"],\"m\":{\"medio\":4,\"final\":2},\"d\":[\"polnareff\"],\"e\":[\"Polnareff takes the photo\",\"A pig in the toilets\",\"Joseph crashes his camel\",\"\\\"I wanna be more famous than Disney\\\"\"]},{\"t\":\"situazione difficile\",\"n\":\"situazione difficile\",\"p\":\"GW\",\"o\":\"overture\",\"u\":21,\"l\":64,\"a\":[\"pelea\",\"tension\"],\"m\":{\"medio\":16,\"final\":3,\"inicio\":2},\"d\":[],\"e\":[\"Giorno summons Gold Experience\",\"Giorno's and Bruno's final clash\",\"Giorno must go for a second patting down while trying to hide the lighter\",\"The prison guard pats Giorno down for any items\"]},{\"t\":\"Skeepy Meeting\",\"n\":\"skeepy meeting\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":13,\"l\":30,\"a\":[],\"m\":{\"medio\":10,\"inicio\":2,\"final\":1},\"d\":[],\"e\":[\"Tomoko and Ryohei Higashikata\",\"Tamami Kobayashi appears and demands to be paid back for his cat\",\"Tamami gets away\",\"Tamami at Koichi's house\"]},{\"t\":\"skew\",\"n\":\"skew\",\"p\":\"PB/BT\",\"o\":",
        "\"leicht\",\"u\":5,\"l\":69,\"a\":[],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"Straizo's dream of immortality and eternal youth.\",\"Straizo's Ripple-conducting scarf.\",\"Straizo planning to take a woman hostage.\",\"Esidisi tries to make Suzi Q explode.\"]},{\"t\":\"Small Soldier\",\"n\":\"small soldier\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":2,\"l\":98,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Caesar's Bubble Cutters make Wamuu retreat.\",\"Lisa Lisa hits \\\"Kars\\\" in the head.\"]},{\"t\":\"Sniper\",\"n\":\"sniper\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":6,\"l\":62,\"a\":[\"misterio\",\"explicacion\",\"pelea\"],\"m\":{\"medio\":5,\"final\":1},\"d\":[\"jotaro\"],\"e\":[\"Johngalli A. learns about Jotaro's visit\",\"Jotaro explaining Johngalli A.'s plan\",\"Discussing how Johngalli A. could have a sniper rifle/Manhattan Transfer appears\",\"Johngalli A. decides to kill Emporio against\"]},{\"t\":\"Something is Wrong\",\"n\":\"something is wrong\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":16,\"l\":96,\"a\":[\"tension\"],\"m\":{\"medio\":15,\"inicio\":1},\"d\":[],\"e\":[\"Jolyne's been shrunk\",\"Johngalli A.'s speech about sniping\",\"Ermes takes McQueen's DISC and leaves\",\"Foo Fighters kills two prisoners\"]},{\"t\":\"Sorrow\",\"n\":\"sorrow\",\"p\":\"DU\",\"o\":\"journey\",\"u\":5,\"l\":28,\"a\":[\"tristeza\"],\"m\":{\"medio\":4,\"inicio\":1},\"d\":[],\"e\":[\"Yukako feeling depressed/The Cinderella beauty salon\",\"Yukako has become unrecognizable\",\"Ken is sad to have lost the first match\",\"Ken makes a scene\"]},{\"t\":\"Sorrow\",\"n\":\"sorrow\",\"p\":\"SC\",\"o\":\"journey\",\"u\":4,\"l\":48,\"a\":[\"tristeza\"],\"m\":{\"medio\":3,\"final\":1},\"d\":[],\"e\":[\"Joseph sorrowful about Holy\",\"Polnareff remembering Avdol's death\",\"Polnareff wishes for Ch\u00e9rie to disappear\",\"Suzi Q always knew that Holy is in peril/Mother & daughter\"]},{\"t\":\"Sorrow\",\"n\":\"sorrow\",\"p\":\"SO\",\"o\":\"journey\",\"u\":3,\"l\":16,\"a\":[\"tristeza\",\"tension\"],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"McQueen cries\",\"... but he proceeds to hang himself\",\"McQueen realizes Ermes is a nice person...\"]},{\"t\":\"Space of a Lone God\",\"n\":\"space of a lone god\",\"p\":\"SC\",\"o\":\"destination\",\"u\":5,\"l\":63,\"a\":[\"villano\"],\"m\":{\"medio\"",
        ":2,\"inicio\":2,\"final\":1},\"d\":[],\"e\":[\"A new enemy appears\",\"D'Arby's collection/The heroes must play with D'Arby\",\"D'Arby is still ahead\",\"D'Arby's abilities are still unknown\"]},{\"t\":\"specchio\",\"n\":\"specchio\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":4,\"l\":42,\"a\":[\"pelea\",\"misterio\"],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"Fugo's Stand can't protect him inside the mirror world\",\"Man in the Mirror deflects the flying rocks back at Fugo\",\"Illuso takes Abbacchio into the mirror world\",\"Illuso tricks Abbacchio and beats him\"]},{\"t\":\"spensierato-rabbia\",\"n\":\"spensierato rabbia\",\"p\":\"GW\",\"o\":\"overture\",\"u\":10,\"l\":33,\"a\":[\"pelea\"],\"m\":{\"medio\":6,\"inicio\":4},\"d\":[\"narancia\",\"fugo\"],\"e\":[\"Fugo teaches Narancia maths\",\"Fugo bashes Narancia's head against the table\",\"Narancia's boombox breaks\",\"Narancia, Fugo and Abbacchio beat up Zucchero's body\"]},{\"t\":\"SPIN (Opening)\",\"n\":\"spin\",\"p\":\"SBR\",\"o\":\"\",\"u\":2,\"l\":89,\"a\":[],\"m\":{\"opening\":2},\"d\":[],\"e\":[\"Opening\",\"Opening\"]},{\"t\":\"spiritoso\",\"n\":\"spiritoso\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":5,\"l\":46,\"a\":[\"comedia\"],\"m\":{\"medio\":4,\"final\":1},\"d\":[\"mista\"],\"e\":[\"Mista feeds Sex Pistols\",\"Narancia gets the keys from Fugo\",\"Narancia sees Giorno healing Mista\",\"Abbacchio realizes the guy wasn't an enemy\"]},{\"t\":\"squadra\",\"n\":\"squadra\",\"p\":\"GW\",\"o\":\"overture\",\"u\":12,\"l\":46,\"a\":[\"pelea\",\"villano\"],\"m\":{\"medio\":6,\"final\":4,\"recap\":1,\"inicio\":1},\"d\":[],\"e\":[\"First introduction of Bruno's team\",\"Mista chases Sale\",\"Mista shoots a bullet in Sale's neck\",\"Sale defeated\"]},{\"t\":\"Squalo\",\"n\":\"squalo\",\"p\":\"GW\",\"o\":\"finale\",\"u\":9,\"l\":24,\"a\":[\"pelea\"],\"m\":{\"medio\":6,\"inicio\":2,\"final\":1},\"d\":[\"narancia\"],\"e\":[\"Clash attacks Narancia\",\"Clash comes out\",\"Clash teleports into Narancia's tears\",\"Clash attacks Giorno\"]},{\"t\":\"STAND PROUD\",\"n\":\"stand proud\",\"p\":\"SC\",\"o\":\"\",\"u\":23,\"l\":90,\"a\":[\"victoria\"],\"m\":{\"opening\":21,\"recap\":2},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"Stardust Crusaders\",\"n\":\"stardust crusaders\",\"p\":\"DU\",\"o\":\"departure\",\"u\":4,\"l\":51,\"a\":[\"pe",
        "lea\",\"tristeza\",\"epico\"],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"Star Platinum pummels Sheer Heart Attack\",\"Star Platinum pummels Kira\",\"Jotaro neutralizes Yoshihiro\",\"ORAORAORAORA!!\"]},{\"t\":\"Stardust Crusaders\",\"n\":\"stardust crusaders\",\"p\":\"SC\",\"o\":\"departure\",\"u\":17,\"l\":67,\"a\":[\"victoria\",\"epico\"],\"m\":{\"medio\":12,\"final\":4,\"ending\":1},\"d\":[\"jotaro\"],\"e\":[\"Jotaro defeats Kakyoin\",\"Placeholder ending\",\"The heroes depart\",\"Jotaro punches a shark\"]},{\"t\":\"Stardust Crusaders\",\"n\":\"stardust crusaders\",\"p\":\"SO\",\"o\":\"departure\",\"u\":4,\"l\":56,\"a\":[\"victoria\",\"tristeza\",\"epico\"],\"m\":{\"medio\":3,\"recap\":1},\"d\":[\"jotaro\"],\"e\":[\"Jotaro defeats Johngalli A.\",\"Jotaro appears and saves Jolyne\",\"Recap of Jotaro's return\",\"Emporio defeats Pucci\"]},{\"t\":\"Steel Tower\",\"n\":\"steel tower\",\"p\":\"DU\",\"o\":\"\",\"u\":3,\"l\":124,\"a\":[\"tension\",\"pelea\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Someone is living on the tower\",\"Mikitaka trapped in the tower\",\"Toyohiro's bouncing attacks\"]},{\"t\":\"Sticker\",\"n\":\"sticker\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":5,\"l\":28,\"a\":[\"misterio\",\"comedia\",\"explicacion\"],\"m\":{\"medio\":4,\"final\":1},\"d\":[],\"e\":[\"Ermes discovers her stickers and uses them to incapacitate McQueen with his mop\",\"Ermes's Stand Kiss is fully revealed\",\"The guard's prank\",\"Ermes explains her Stand briefly\"]},{\"t\":\"Stillness\",\"n\":\"stillness\",\"p\":\"DU\",\"o\":\"\",\"u\":9,\"l\":33,\"a\":[\"tristeza\",\"pelea\"],\"m\":{\"medio\":6,\"inicio\":2,\"final\":1},\"d\":[],\"e\":[\"Aftermath of Ryohei's death\",\"Josuke cannot heal himself\",\"Okuyasu steps out of the fight\",\"Keicho is defeated, Josuke & Koichi look for the Arrow\"]},{\"t\":\"Stillness\",\"n\":\"stillness\",\"p\":\"GW\",\"o\":\"\",\"u\":1,\"l\":74,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Koichi updates Jotaro on phone\"]},{\"t\":\"Stone Mask ~Prologue~\",\"n\":\"stone mask\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":6,\"l\":87,\"a\":[\"tristeza\",\"pelea\"],\"m\":{\"medio\":4,\"inicio\":2},\"d\":[],\"e\":[\"Dario Brando meets George Joestar.\",\"Jonathan experiments with the stone mask.\",\"Flashback of Dario and George.\",\"Jonathan's training/Zeppeli's story",
        ".\"]},{\"t\":\"STONE OCEAN (Opening)\",\"n\":\"stone ocean\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":22,\"l\":89,\"a\":[],\"m\":{\"opening\":22},\"d\":[],\"e\":[\"Opening\",\"Opening\",\"Opening\",\"Opening\"]},{\"t\":\"Strange and Mysterious\",\"n\":\"strange and mysterious\",\"p\":\"SC\",\"o\":\"journey\",\"u\":5,\"l\":44,\"a\":[\"misterio\"],\"m\":{\"medio\":3,\"inicio\":2},\"d\":[],\"e\":[\"The enemy reveals himself\",\"Inside the brain\",\"Joseph and Avdol are stuck to each other\",\"Polnareff gambles his soul... and loses\"]},{\"t\":\"Strange Attack\",\"n\":\"strange attack\",\"p\":\"DU\",\"o\":\"\",\"u\":2,\"l\":130,\"a\":[\"pelea\",\"misterio\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"The main dish is a success\",\"Rohan reads Koichi's bio\"]},{\"t\":\"Stress\",\"n\":\"stress\",\"p\":\"DU\",\"o\":\"\",\"u\":10,\"l\":95,\"a\":[],\"m\":{\"medio\":7,\"inicio\":2,\"final\":1},\"d\":[],\"e\":[\"Hostage situation\",\"Aqua Necklace invades the house\",\"Okuyasu chastised by his big brother\",\"Keicho's mysterious ability\"]},{\"t\":\"Strings\",\"n\":\"strings\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":12,\"l\":101,\"a\":[\"misterio\",\"revelacion\"],\"m\":{\"medio\":10,\"final\":1,\"recap\":1},\"d\":[\"jolyne\"],\"e\":[\"Jolyne discovering her string/Saving Ermes\",\"The pendant in possession of another person\",\"Stone Free appears/Gwess tricks Jolyne\",\"Recap: Jolyne's sentencing and Stand awakening\"]},{\"t\":\"Strutting the Ogre Street\",\"n\":\"strutting the ogre street\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":1,\"l\":103,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jonathan defends against Speedwagon, Tattoo and Kempo Master.\"]},{\"t\":\"Submission\",\"n\":\"submission\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":3,\"l\":122,\"a\":[\"pelea\",\"comedia\"],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Yo-Yo Ma acts submissive/Jolyne and Anasui take Yo-Yo Ma to the wetlands\",\"Yo-Yo Ma's mosquito attack/\\\"Be All Eyes\\\"\",\"Yo-Yo Ma's frog antics\"]},{\"t\":\"Sudden Battle\",\"n\":\"sudden battle\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":8,\"l\":61,\"a\":[\"pelea\"],\"m\":{\"medio\":7,\"final\":1},\"d\":[],\"e\":[\"Koichi fights Yukako\",\"RHCP at full power\",\"Josuke & Okuyasu pursuing the tiny bee-like Stands\",\"Josuke is saved by Rohan\"]},{\"t\":\"Sudden Battle\",\"n\":\"su",
        "dden battle\",\"p\":\"SO\",\"o\":\"good morning\",\"u\":1,\"l\":15,\"a\":[\"pelea\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Perla's bag is stolen\"]},{\"t\":\"Sudden Turn\",\"n\":\"sudden turn\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":3,\"l\":29,\"a\":[\"villano\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Jack the Ripper murders his victim.\",\"Dio is reborn as a Vampire!\",\"Jack the Ripper is recruited by Dio.\"]},{\"t\":\"Surrounded\",\"n\":\"surrounded\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":18,\"l\":98,\"a\":[],\"m\":{\"medio\":12,\"inicio\":4,\"final\":2},\"d\":[],\"e\":[\"Jolyne sentenced to 15 years in prison/It was Romeo\",\"Gwess has the Stone Pendant/Something wrong with the parakeet\",\"Gwess tells Jolyne to assert herself\",\"Manhattan Transfer appears/Jotaro is shot\"]},{\"t\":\"Surviver\",\"n\":\"surviver\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":2,\"l\":68,\"a\":[\"tension\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Guccio appears\",\"D an G falls victim to Anasui's trap/Guccio is Survivor's user\"]},{\"t\":\"suspense\",\"n\":\"suspense\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":22,\"l\":60,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":16,\"inicio\":5,\"final\":1},\"d\":[],\"e\":[\"Giorno exits the prison and walks on his way to his dorm\",\"The lighter goes out\",\"Black Sabbath sees Koichi and attacks him\",\"Koichi tells Giorno that Polpo is still alive\"]},{\"t\":\"Suspenseful\",\"n\":\"suspenseful\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":17,\"l\":72,\"a\":[],\"m\":{\"medio\":12,\"inicio\":2,\"final\":2,\"recap\":1},\"d\":[\"jolyne\"],\"e\":[\"Romeo and Jolyne hiding the body\",\"Something is wrong?/Jolyne is shot\",\"Manhattan Transfer is reading the air currents\",\"Jolyne triggers the fire alarm/A secret passage under the pillar\"]},{\"t\":\"Suspicion\",\"n\":\"suspicion\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":3,\"l\":31,\"a\":[\"villano\"],\"m\":{\"inicio\":2,\"medio\":1},\"d\":[],\"e\":[\"Jonathan doubts Dio's friendliness.\",\"Entrance to Windknight's Lot.\",\"Where is Poco's sister?\"]},{\"t\":\"sventura\",\"n\":\"sventura\",\"p\":\"GW\",\"o\":\"finale\",\"u\":12,\"l\":74,\"a\":[\"villano\",\"tristeza\"],\"m\":{\"medio\":6,\"inicio\":4,\"final\":1,\"recap\":1},\"d\":[\"doppio\"],\"e\":[\"A fortune teller harasses the teenager\",\"Doppio u",
        "ses Epitaph to find Risotto\",\"Risotto guesses Doppio's secret\",\"Doppio gets near Bucciarati\"]},{\"t\":\"sventura\",\"n\":\"sventura\",\"p\":\"SO\",\"o\":\"finale\",\"u\":2,\"l\":76,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Pinocchio lies\",\"Bohemian Rhapsody, a capriccio of the free\"]},{\"t\":\"Sword Attack\",\"n\":\"sword attack\",\"p\":\"SC\",\"o\":\"world\",\"u\":2,\"l\":105,\"a\":[\"pelea\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Polnareff's trick/Alessi flees\",\"Polnareff finds Hol Horse\"]},{\"t\":\"Take Cover\",\"n\":\"take cover\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":8,\"l\":54,\"a\":[\"pelea\"],\"m\":{\"medio\":5,\"inicio\":2,\"final\":1},\"d\":[\"joseph\"],\"e\":[\"The Special Force soldier Donovan attacks Joseph.\",\"Joseph vs. Donovan.\",\"Santana tied up./Joseph drags Santana outside.\",\"Caesar vs. Messina.\"]},{\"t\":\"tense\",\"n\":\"tense\",\"p\":\"PB/BT\",\"o\":\"leicht\",\"u\":11,\"l\":65,\"a\":[],\"m\":{\"medio\":8,\"inicio\":2,\"final\":1},\"d\":[\"joseph\"],\"e\":[\"Straizo appears to Joseph.\",\"Straizo tears off the hostage's tooth.\",\"Santana partially absorbing Joseph\",\"The three slumbering Pillar Men.\"]},{\"t\":\"Tense Air\",\"n\":\"tense air\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":25,\"l\":72,\"a\":[],\"m\":{\"medio\":15,\"inicio\":6,\"final\":3,\"recap\":1},\"d\":[\"jolyne\"],\"e\":[\"Jolyne bonding with Ermes\",\"Jolyne going to the visiting room/Emporio's second warning\",\"Recap: Emporio's warning and Jotaro's visit\",\"Emporio explains the DISCs to Ermes/Ermes gets electrocuted\"]},{\"t\":\"Tension\",\"n\":\"tension\",\"p\":\"SC\",\"o\":\"departure\",\"u\":21,\"l\":74,\"a\":[\"tension\"],\"m\":{\"medio\":20,\"final\":1},\"d\":[],\"e\":[\"Star Platinum breaks bars\",\"Star Platinum analyzes a photo\",\"Silver Chariot attacks at super speed\",\"Jotaro dragged underwater\"]},{\"t\":\"Tension\",\"n\":\"tension\",\"p\":\"SO\",\"o\":\"departure\",\"u\":5,\"l\":65,\"a\":[\"tension\",\"villano\"],\"m\":{\"medio\":4,\"eyecatch\":1},\"d\":[],\"e\":[\"Jolyne is seriously injured\",\"The birth of a new hero, Put Back\",\"Diver Down submerged into Weather's body!\",\"Weather has Pucci trapped\"]},{\"t\":\"tensione\",\"n\":\"tensione\",\"p\":\"GW\",\"o\":\"overture\",\"u\":20,\"l\":51,\"a\":[\"tension\"],\"m\":{\"medio\":15,\"inicio\":5},\"d\":[],\"e\"",
        ":[\"Koichi finds a frog\",\"Gold Experience and Sticky Fingers clash fists\",\"Giorno finds a way to keep the lighter safe\",\"Giorno examines Black Sabbath's behavior\"]},{\"t\":\"tensione\",\"n\":\"tensione\",\"p\":\"SO\",\"o\":\"overture\",\"u\":1,\"l\":6,\"a\":[\"villano\",\"tension\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"The prisoner with DIO's bone is nearby\"]},{\"t\":\"teso\",\"n\":\"teso\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":13,\"l\":72,\"a\":[\"tension\"],\"m\":{\"medio\":9,\"final\":2,\"inicio\":2},\"d\":[],\"e\":[\"Echoes activates \\\"Three Freeze\\\" on Black Sabbath\",\"Abbacchio doesn't trust Giorno\",\"Giorno notices Sale\",\"Mista shoots Sale\"]},{\"t\":\"The Advance of Darkness\",\"n\":\"the advance of darkness\",\"p\":\"SC\",\"o\":\"departure\",\"u\":13,\"l\":83,\"a\":[\"villano\"],\"m\":{\"medio\":9,\"inicio\":4},\"d\":[],\"e\":[\"Yellow Temperance's invincibility\",\"Rubber Soul ambushes Jotaro\",\"Hanged Man appears\",\"Enya learns her son is dead\"]},{\"t\":\"The Advance of Darkness\",\"n\":\"the advance of darkness\",\"p\":\"SO\",\"o\":\"departure\",\"u\":2,\"l\":110,\"a\":[\"villano\",\"explicacion\",\"tension\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Pucci explains Heavy Weather's subliminal messaging\",\"The gang realizes everything around them's going faster\"]},{\"t\":\"The Alley\",\"n\":\"the alley\",\"p\":\"DU\",\"o\":\"good night\",\"u\":4,\"l\":114,\"a\":[\"villano\"],\"m\":{\"medio\":3,\"inicio\":1},\"d\":[],\"e\":[\"Reimi is a ghost\",\"Reimi's killer is still lurking\",\"Shinobu Kawajiri's miserable married life\",\"Reimi finds the link between Hayato & Kosaku\"]},{\"t\":\"The Artist's Bizarre Passion\",\"n\":\"the artist s bizarre passion\",\"p\":\"DU\",\"o\":\"good night\",\"u\":7,\"l\":82,\"a\":[\"misterio\"],\"m\":{\"medio\":4,\"inicio\":3},\"d\":[\"rohan\"],\"e\":[\"Koichi meets Rohan in the streets/An unmapped street\",\"Rohan won't hold back\",\"Rohan wins the match, and the rematch\",\"Rohan determined to find how is Josuke cheating\"]},{\"t\":\"The Battle Begins\",\"n\":\"the battle begins\",\"p\":\"SC\",\"o\":\"destination\",\"u\":7,\"l\":41,\"a\":[\"pelea\",\"tension\"],\"m\":{\"medio\":6,\"inicio\":1},\"d\":[],\"e\":[\"Geb claws Kakyoin eyes\",\"Polnareff is pursued by Geb\",\"Joseph and Avdol are cornered by",
        " the magnetism\",\"Alessi is drowning Polnareff\"]},{\"t\":\"The Bow and Arrow\",\"n\":\"the bow and arrow\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":7,\"l\":27,\"a\":[],\"m\":{\"medio\":7},\"d\":[],\"e\":[\"Angelo survives and acquires a Stand\",\"Yukako was struck with the Arrow\",\"Ken Oyanagi is becoming a Stand user\",\"Was the cat a Stand user\"]},{\"t\":\"The Bow and Arrow\",\"n\":\"the bow and arrow\",\"p\":\"GW\",\"o\":\"good morning\",\"u\":1,\"l\":12,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Koichi wants to call Jotaro, but Giorno stops him\"]},{\"t\":\"The Curtain Rises\",\"n\":\"the curtain rises\",\"p\":\"SC\",\"o\":\"destination\",\"u\":9,\"l\":70,\"a\":[\"tension\",\"villano\"],\"m\":{\"medio\":8,\"final\":1},\"d\":[],\"e\":[\"Kakyoin gets excited\",\"The heroes trapped, Death 13 comes\",\"Ch\u00e9rie is a zombie!\",\"Polnareff being devoured\"]},{\"t\":\"The Curtain Rises\",\"n\":\"the curtain rises\",\"p\":\"SO\",\"o\":\"destination\",\"u\":2,\"l\":48,\"a\":[\"tension\",\"villano\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Jolyne and Ermes escape the hospital\",\"Pucci is cornered\"]},{\"t\":\"The End Of The Universe\",\"n\":\"the end of the universe\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":8,\"l\":46,\"a\":[\"villano\",\"explicacion\",\"epico\"],\"m\":{\"medio\":5,\"inicio\":2,\"eyecatch\":1},\"d\":[],\"e\":[\"Pucci and DIO in bed\",\"Weather's kindness is repaid\",\"Jolyne forgot to check Versus's room\",\"Explanation of the ozone layer\"]},{\"t\":\"The Fate That Still Remains\",\"n\":\"the fate that still remains\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":19,\"l\":56,\"a\":[\"epico\"],\"m\":{\"medio\":10,\"inicio\":8,\"final\":1},\"d\":[\"jotaro\"],\"e\":[\"The strange power of Josuke's Stand\",\"Josuke and Jotaro discuss\",\"Aqua Necklace captured\",\"Flashback: Josuke puts Angelo in a rock\"]},{\"t\":\"The Fate That Still Remains\",\"n\":\"the fate that still remains\",\"p\":\"GW\",\"o\":\"good morning\",\"u\":2,\"l\":42,\"a\":[\"tristeza\",\"misterio\",\"epico\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Flashback of Koichi and Jotaro\",\"Polnareff and Jotaro are searching for the Arrows\"]},{\"t\":\"The Fate That Still Remains\",\"n\":\"the fate that still remains\",\"p\":\"SO\",\"o\":\"good morning\",\"u\":8,\"l\":72,\"a\":[\"epico\"],\"m\":{\"medio\":6,",
        "\"final\":2},\"d\":[\"jotaro\",\"jolyne\"],\"e\":[\"Jotaro Kujo appears\",\"Johngalli A.'s grudge/Jotaro plan on breaking Jolyne out\",\"Jolyne finds the Star Platinum DISC\",\"Jolyne discusses with the SPW Foundation\"]},{\"t\":\"The Fool of Sand\",\"n\":\"the fool of sand\",\"p\":\"SC\",\"o\":\"destination\",\"u\":5,\"l\":20,\"a\":[\"comedia\"],\"m\":{\"medio\":4,\"final\":1},\"d\":[\"iggy\"],\"e\":[\"*Battle in Egypt* teaser\",\"Iggy the dog Stand user\",\"Iggy steps in to protect the boy\",\"Iggy lands a hit\"]},{\"t\":\"The Fool's Rampage\",\"n\":\"the fool s rampage\",\"p\":\"SC\",\"o\":\"world\",\"u\":4,\"l\":29,\"a\":[\"comedia\"],\"m\":{\"medio\":4},\"d\":[],\"e\":[\"Scorpion in the crib\",\"The Stand of sand, The Fool\",\"Iggy plays the fool\",\"Senator Wilson Philips\"]},{\"t\":\"The Green Baby\",\"n\":\"the green baby\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":3,\"l\":36,\"a\":[],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"The Green Baby is alive!\",\"The bottle almost crushes Jolyne and Anasui/Back to normal size?\",\"Jolyne and Anasui observe the Green Baby\"]},{\"t\":\"The Hand\",\"n\":\"the hand\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":12,\"l\":26,\"a\":[\"pelea\"],\"m\":{\"medio\":11,\"final\":1},\"d\":[\"okuyasu\"],\"e\":[\"The Hand appears\",\"Crazy Diamond vs. The Hand\",\"The Hand save the class rep\",\"Okuyasu vs. RHCP\"]},{\"t\":\"The Lady's Invasion\",\"n\":\"the lady s invasion\",\"p\":\"SC\",\"o\":\"world\",\"u\":5,\"l\":57,\"a\":[\"viaje\",\"pelea\"],\"m\":{\"medio\":5},\"d\":[\"joseph\"],\"e\":[\"Joseph's hands are almost crushed\",\"Stuck on train tracks\",\"Joseph devises his strategy\",\"Joseph begs Mariah\"]},{\"t\":\"The Love Protecting This Town\",\"n\":\"the love protecting this town\",\"p\":\"DU\",\"o\":\"good night\",\"u\":8,\"l\":42,\"a\":[],\"m\":{\"final\":4,\"medio\":3,\"inicio\":1},\"d\":[\"josuke\"],\"e\":[\"Koichi wins and saves Yukako\",\"Okuyasu is brought to tears\",\"Josuke meets his father\",\"Josuke & Joseph go together\"]},{\"t\":\"The Magician of Fire\",\"n\":\"the magician of fire\",\"p\":\"SC\",\"o\":\"departure\",\"u\":9,\"l\":31,\"a\":[\"viaje\",\"epico\"],\"m\":{\"inicio\":5,\"medio\":3,\"final\":1},\"d\":[],\"e\":[\"Avdol appears\",\"The heroes have arrived in Araby\",\"The real Muhammad Avdol is alive\",\"Geb ",
        "avoids the trap\"]},{\"t\":\"The Moment of Decisive Battle\",\"n\":\"the moment of decisive battle\",\"p\":\"SC\",\"o\":\"departure\",\"u\":13,\"l\":84,\"a\":[\"pelea\",\"epico\"],\"m\":{\"medio\":10,\"final\":2,\"inicio\":1},\"d\":[],\"e\":[\"Silver Chariot attacks\",\"Silver Chariot vs Magician's Red, first round\",\"Dark Blue Moon reveal\",\"Trying to electrocute Polnareff\"]},{\"t\":\"The Moment of Decisive Battle\",\"n\":\"the moment of decisive battle\",\"p\":\"SO\",\"o\":\"departure\",\"u\":1,\"l\":131,\"a\":[\"pelea\",\"tension\",\"epico\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jolyne turning her body into M\u00f6bius strips\"]},{\"t\":\"The Plot Lurking in the Mist\",\"n\":\"the plot lurking in the mist\",\"p\":\"SC\",\"o\":\"world\",\"u\":37,\"l\":56,\"a\":[\"misterio\"],\"m\":{\"medio\":29,\"inicio\":7,\"final\":1},\"d\":[],\"e\":[\"The heroes discover Holy is sick\",\"Is the stowaway girl an enemy?\",\"Investigating the ship\",\"Polnareff discovers his room\"]},{\"t\":\"The Plot Lurking in the Mist\",\"n\":\"the plot lurking in the mist\",\"p\":\"SO\",\"o\":\"world\",\"u\":2,\"l\":66,\"a\":[\"misterio\",\"villano\"],\"m\":{\"final\":1,\"medio\":1},\"d\":[],\"e\":[\"The useless DISCs\",\"The gang wonder of Pucci's whereabouts\"]},{\"t\":\"The Possessor\",\"n\":\"the possessor\",\"p\":\"DU\",\"o\":\"\",\"u\":9,\"l\":34,\"a\":[\"misterio\"],\"m\":{\"medio\":4,\"inicio\":4,\"final\":1},\"d\":[\"rohan\"],\"e\":[\"A mysterious posted near Rohan's house\",\"The strange man at Rohan's doorstep\",\"Masozo Kinoto the architect\",\"Kinoto really wants to hide his back\"]},{\"t\":\"The Possessor\",\"n\":\"the possessor\",\"p\":\"SO\",\"o\":\"\",\"u\":2,\"l\":100,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Pinocchio tells the truth\",\"Anasui is dragged into the wolf's story\"]},{\"t\":\"The Prophecy That's Never Wrong\",\"n\":\"the prophecy that s never wrong\",\"p\":\"SC\",\"o\":\"world\",\"u\":19,\"l\":79,\"a\":[\"comedia\",\"misterio\"],\"m\":{\"medio\":13,\"final\":4,\"inicio\":2},\"d\":[],\"e\":[\"A kid and his strange comic-book\",\"Boingo predicts that the heroes will drink poison\",\"Disguising themselves as cafe owners\",\"Oingo must believe in Tohth\"]},{\"t\":\"The Scheme\",\"n\":\"the scheme\",\"p\":\"GW\",\"o\":\"world\",\"u\":1,\"l\":116,\"a\":[\"tension\",\"misterio\"",
        "],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Giorno and Mista tracking down Sale\"]},{\"t\":\"The Scheme\",\"n\":\"the scheme\",\"p\":\"SC\",\"o\":\"world\",\"u\":22,\"l\":86,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":17,\"inicio\":5},\"d\":[],\"e\":[\"Holy runs toward Jotaro's cell\",\"The heroes see Tower of Gray\",\"Polnareff tells Avdol to go outside\",\"The imposter can hold his breath underwater for 6 min.\"]},{\"t\":\"The Scheme\",\"n\":\"the scheme\",\"p\":\"SO\",\"o\":\"world\",\"u\":1,\"l\":92,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jolyne asks about the bone/Time to break out\"]},{\"t\":\"The Seeker\",\"n\":\"the seeker\",\"p\":\"DU\",\"o\":\"good night\",\"u\":16,\"l\":36,\"a\":[],\"m\":{\"medio\":12,\"inicio\":2,\"final\":2},\"d\":[\"rohan\"],\"e\":[\"Rohan foreshadowing\",\"Rohan Kishibe opens the door to Koichi & Hazamada\",\"Rohan licks a spider\",\"Heaven's Door turns Koichi & Hazamada into books\"]},{\"t\":\"The Shadow Lurking in Town\",\"n\":\"the shadow lurking in town\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":21,\"l\":68,\"a\":[\"villano\"],\"m\":{\"medio\":16,\"inicio\":4,\"final\":1},\"d\":[],\"e\":[\"A dangerous Stand user is in town\",\"Josuke & Jotaro wait for Angelo\",\"Okuyasu banters with Josuke\",\"A scary creature in the attic\"]},{\"t\":\"The Shadow Lurking in Town\",\"n\":\"the shadow lurking in town\",\"p\":\"SO\",\"o\":\"good morning\",\"u\":1,\"l\":95,\"a\":[\"villano\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Pucci blinding a guard\"]},{\"t\":\"The Sign of Fear\",\"n\":\"the sign of fear\",\"p\":\"DU\",\"o\":\"\",\"u\":6,\"l\":20,\"a\":[\"pelea\",\"calma\",\"tristeza\"],\"m\":{\"medio\":5,\"final\":1},\"d\":[],\"e\":[\"Koichi attacked by Miyamoto\",\"Tomoko startled by Miyamoto\",\"Koichi has disappeared\",\"Flashback to Tomoko's morning\"]},{\"t\":\"The Stardust Man Appeared\",\"n\":\"the stardust man appeared\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":10,\"l\":86,\"a\":[\"epico\"],\"m\":{\"medio\":6,\"inicio\":4},\"d\":[\"jotaro\"],\"e\":[\"Koichi Hirose meets Jotaro Kujo\",\"Jotaro Kujo meets Josuke Higashikata\",\"Jotaro describes said ally\",\"Practicing shooting with ball bearings\"]},{\"t\":\"The Travelers Rest\",\"n\":\"the travelers rest\",\"p\":\"DU\",\"o\":\"world\",\"u\":1,\"l\":32,\"a\":[\"viaje\",\"calma\"],\"m\":{\"medio\":1},",
        "\"d\":[],\"e\":[\"Clearing the misunderstanding\"]},{\"t\":\"The Travelers Rest\",\"n\":\"the travelers rest\",\"p\":\"SC\",\"o\":\"world\",\"u\":6,\"l\":41,\"a\":[\"calma\",\"viaje\"],\"m\":{\"final\":3,\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Kakyoin intimidates the baby\",\"The heroes choose a cafe\",\"Arabic numbers explained\",\"Joseph and Avdol go for breakfest\"]},{\"t\":\"The Travelers Return\",\"n\":\"the travelers return\",\"p\":\"DU\",\"o\":\"destination\",\"u\":2,\"l\":106,\"a\":[\"tristeza\",\"viaje\"],\"m\":{\"final\":1,\"medio\":1},\"d\":[],\"e\":[\"Joseph cuts his wrists to find the baby\",\"The dead cannot return, but Morioh is safe now/Jotaro & Joseph leave Morioh\"]},{\"t\":\"The Travelers Return\",\"n\":\"the travelers return\",\"p\":\"SC\",\"o\":\"destination\",\"u\":1,\"l\":139,\"a\":[\"viaje\",\"tristeza\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"Holy is healed/The crusaders return home\"]},{\"t\":\"The Travelers Return\",\"n\":\"the travelers return\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":71,\"a\":[\"viaje\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Jolyne and Emporio reunite with Ermes and prepare to leave the prison\"]},{\"t\":\"Theme of Stone Ocean\",\"n\":\"theme of stone ocean\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":21,\"l\":30,\"a\":[\"epico\",\"victoria\"],\"m\":{\"medio\":10,\"final\":8,\"inicio\":2,\"recap\":1},\"d\":[\"jolyne\"],\"e\":[\"The lawyer strangled by a string\",\"Jolyne beats Gwess\",\"Jolyne defeats Johngalli A.\",\"Jolyne defeats Johngalli A. for real\"]},{\"t\":\"Third Bomb\",\"n\":\"third bomb\",\"p\":\"DU\",\"o\":\"good night\",\"u\":8,\"l\":50,\"a\":[\"villano\"],\"m\":{\"medio\":6,\"final\":2},\"d\":[],\"e\":[\"Pursuing Shigechi\",\"Killer Queen Bites the Dust can loop time!\",\"Killer Queen kills the heroes\",\"Kira protected by luck once more\"]},{\"t\":\"Three-Way Deadlock\",\"n\":\"three way deadlock\",\"p\":\"DU\",\"o\":\"\",\"u\":8,\"l\":46,\"a\":[\"victoria\"],\"m\":{\"medio\":8},\"d\":[],\"e\":[\"\\\"Hey, mister. Wanna play jan-ken-pon?\",\"The first jan-ken-pon round\",\"Ken is back\",\"The second round\"]},{\"t\":\"Throw\",\"n\":\"throw\",\"p\":\"SC\",\"o\":\"world\",\"u\":7,\"l\":15,\"a\":[\"pelea\",\"victoria\"],\"m\":{\"medio\":4,\"inicio\":2,\"final\":1},\"d\":[],\"e\":[\"Avdol the zombie appears\",\"Polnareff is attack",
        "ed\",\"Jotaro defeats N'Doul\",\"Anubis flying into the Nile\"]},{\"t\":\"Tiny Stone\",\"n\":\"tiny stone\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":10,\"l\":74,\"a\":[\"pelea\"],\"m\":{\"medio\":9,\"inicio\":1},\"d\":[\"jolyne\"],\"e\":[\"Gwess ordering Jolyne to scout the guards's station\",\"D an G watches from the shadows/Jolyne has been affected by the bone\",\"D an G approaches Guccio\",\"Anasui thinks they will never reach the baby\"]},{\"t\":\"Tragedy\",\"n\":\"tragedy\",\"p\":\"DU\",\"o\":\"good night\",\"u\":5,\"l\":63,\"a\":[\"villano\",\"tristeza\"],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"The Nijimura Family's story\",\"Okuyasu wants to avenge his big brother\",\"Josuke thinks about his father\",\"Reimi begs Rohan & Koichi to find the killer\"]},{\"t\":\"Tragedy\",\"n\":\"tragedy\",\"p\":\"SO\",\"o\":\"good night\",\"u\":2,\"l\":94,\"a\":[],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Wes and Perla are harassed by KKK members/Perla takes her life after Wes is hung\",\"Weather is an angry, bitter husk of his former self\"]},{\"t\":\"Transcendence\",\"n\":\"transcendence\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":4,\"l\":58,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":3,\"inicio\":1},\"d\":[\"dio\"],\"e\":[\"Jonathan throws Dio through the banister.\",\"Dio stabs George and becomes a Vampire.\",\"Dio attacks.\",\"Dio decapitates himself to survive.\"]},{\"t\":\"trasfigurazione\",\"n\":\"trasfigurazione\",\"p\":\"GW\",\"o\":\"finale\",\"u\":7,\"l\":66,\"a\":[\"epico\"],\"m\":{\"medio\":6,\"final\":1},\"d\":[],\"e\":[\"Beach Boy baits Mista\",\"Doppio pretends to be Trish\",\"Narancia wonders who is inside Bruno's body\",\"Polnareff tells Chariot Requiem's ability\"]},{\"t\":\"Two Boys\",\"n\":\"two boys\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":1,\"l\":37,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"George lectures Jonathan.\"]},{\"t\":\"Tyrannical Servant\",\"n\":\"tyrannical servant\",\"p\":\"SC\",\"o\":\"destination\",\"u\":1,\"l\":35,\"a\":[\"villano\"],\"m\":{\"final\":1},\"d\":[],\"e\":[\"DIO is closing in on Joseph\"]},{\"t\":\"un'altra persona\",\"n\":\"un altra persona\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":11,\"l\":98,\"a\":[\"villano\"],\"m\":{\"medio\":8,\"final\":2,\"inicio\":1},\"d\":[],\"e\":[\"Sorbet and Gelato's punishment\",\"Pesci's personality changes\"",
        ",\"The Boss appears\",\"King Crimson severely injures Bucciarati\"]},{\"t\":\"un sogno\",\"n\":\"un sogno\",\"p\":\"GW\",\"o\":\"overture\",\"u\":13,\"l\":90,\"a\":[\"epico\",\"calma\"],\"m\":{\"medio\":7,\"final\":4,\"inicio\":2},\"d\":[\"giorno\"],\"e\":[\"Gangster watches over Giorno\",\"Giorno is inspired by Gangster\",\"Giorno reveals his golden dream to be a gang-star\",\"Koichi sees Giorno's golden spirit\"]},{\"t\":\"un sogno\",\"n\":\"un sogno\",\"p\":\"SO\",\"o\":\"overture\",\"u\":1,\"l\":132,\"a\":[],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Anasui asks Jotaro for his blessing/The gang are stuck on a roof\"]},{\"t\":\"Under the Ground\",\"n\":\"under the ground\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":4,\"l\":72,\"a\":[\"tristeza\"],\"m\":{\"medio\":3,\"recap\":1},\"d\":[],\"e\":[\"Recap: Versus sends Weather's memory DISC\",\"Versus finds out about Emporio\",\"Versus catches Emporio\",\"Versus digs up memories of the Super Bowl\"]},{\"t\":\"Undiscovered Power, the Ancient Product\",\"n\":\"undiscovered power the ancient product\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":2,\"l\":97,\"a\":[\"revelacion\",\"tristeza\"],\"m\":{\"medio\":1,\"inicio\":1},\"d\":[],\"e\":[\"Zeppeli demonstrates the power of the Ripple.\",\"Tonpetty's prediction of Zeppeli's gruesome death.\"]},{\"t\":\"Uneasiness\",\"n\":\"uneasiness\",\"p\":\"SC\",\"o\":\"world\",\"u\":17,\"l\":62,\"a\":[\"tension\",\"misterio\"],\"m\":{\"medio\":13,\"inicio\":2,\"final\":2},\"d\":[],\"e\":[\"The Sun is a Stand\",\"Mannish Boy plotting\",\"Kakyoin is knocked out\",\"Polnareff finds a treasure. Is Cameo for real?\"]},{\"t\":\"Uneasiness\",\"n\":\"uneasiness\",\"p\":\"SO\",\"o\":\"world\",\"u\":2,\"l\":92,\"a\":[\"villano\",\"tristeza\"],\"m\":{\"medio\":1,\"final\":1},\"d\":[],\"e\":[\"Whitesnake possesses Anasui\",\"Past visages of Pucci taunt the gang\"]},{\"t\":\"Unfolding Crisis\",\"n\":\"unfolding crisis\",\"p\":\"SC\",\"o\":\"destination\",\"u\":15,\"l\":53,\"a\":[\"tension\",\"pelea\"],\"m\":{\"medio\":10,\"final\":3,\"inicio\":2},\"d\":[],\"e\":[\"Death Thirteen attacks Polnareff & Kakyoin\",\"Kakyoin knifes himself\",\"Cameo is Judgement, an enemy Stand!\",\"Avdol and Ch\u00e9rie attack\"]},{\"t\":\"Unfolding Crisis\",\"n\":\"unfolding crisis\",\"p\":\"SO\",\"o\":\"destination\",\"u\":1,\"l\":107,\"a\":[\"ten",
        "sion\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Versus escapes/everyone in the hospital is affected by snails?\"]},{\"t\":\"Urgency\",\"n\":\"urgency\",\"p\":\"SC\",\"o\":\"departure\",\"u\":12,\"l\":80,\"a\":[\"tension\"],\"m\":{\"medio\":11,\"inicio\":1},\"d\":[],\"e\":[\"Kakyoin reveals the Emerald Splash\",\"Avdol tricks Polnareff with a statue\",\"Polnareff is trapped under the bed by the doll\",\"Jotaro drags Rubber Soul into the water\"]},{\"t\":\"Urgency\",\"n\":\"urgency\",\"p\":\"SO\",\"o\":\"departure\",\"u\":1,\"l\":11,\"a\":[\"tension\"],\"m\":{\"inicio\":1},\"d\":[],\"e\":[\"Star Platinum instinctively defends Jotaro/Jotaro's arm is scarred with Jolyne's name\"]},{\"t\":\"Villain\u25c7Concerto\",\"n\":\"villainconcerto\",\"p\":\"SC\",\"o\":\"destination\",\"u\":3,\"l\":88,\"a\":[\"villano\"],\"m\":{\"ending\":3},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"virus\",\"n\":\"virus\",\"p\":\"GW\",\"o\":\"overture\",\"u\":1,\"l\":107,\"a\":[\"explicacion\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"Purple Haze's ability revealed\"]},{\"t\":\"vita\",\"n\":\"vita\",\"p\":\"GW\",\"o\":\"intermezzo\",\"u\":10,\"l\":66,\"a\":[\"epico\",\"victoria\"],\"m\":{\"medio\":7,\"final\":3},\"d\":[\"giorno\"],\"e\":[\"Giorno indicates, that Narancia is still alive\",\"Aerosmith ignites the car's spilled gasoline\",\"Giorno recreates his missing parts\",\"Giorno indirectly kills Melone\"]},{\"t\":\"Walk Like an Egyptian\",\"n\":\"walk like an egyptian\",\"p\":\"SC\",\"o\":\"\",\"u\":22,\"l\":87,\"a\":[],\"m\":{\"ending\":22},\"d\":[],\"e\":[\"Ending\",\"Ending\",\"Ending\",\"Ending\"]},{\"t\":\"Waves of the Sun, the Undiscovered Power\",\"n\":\"waves of the sun the undiscovered power\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":3,\"l\":53,\"a\":[\"explicacion\"],\"m\":{\"medio\":2,\"inicio\":1},\"d\":[],\"e\":[\"Explanation of the Ripple.\",\"Zeppeli and JoJo walk on water.\",\"Dire appears.\"]},{\"t\":\"Weaknesses of the Heart\",\"n\":\"weaknesses of the heart\",\"p\":\"PB/BT\",\"o\":\"destiny\",\"u\":2,\"l\":39,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":2},\"d\":[],\"e\":[\"Jonathan is angered by Dio kicking Danny.\",\"Dio plots internally.\"]},{\"t\":\"Weather\",\"n\":\"weather\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":3,\"l\":111,\"a\":[\"villano\",\"pelea\"],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Weathe",
        "r Report presentation/An enemy is watching Jolyne\",\"Weather Report vs Lang Rangler/Alarm activated\",\"Weather gives Jolyne his suit/Lang cancels his power\"]},{\"t\":\"Weightlessness\",\"n\":\"weightlessness\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":5,\"l\":26,\"a\":[\"tension\",\"pelea\",\"tristeza\"],\"m\":{\"medio\":5},\"d\":[],\"e\":[\"Lang in pursuit\",\"Lang Rangler seeks the heroes\",\"Lang bombarding Jolyne and Weather, part 1\",\"Lang Rangler gloating\"]},{\"t\":\"WELCOME TO THE WORLD\",\"n\":\"welcome to the world\",\"p\":\"PB/BT\",\"o\":\"musik\",\"u\":3,\"l\":42,\"a\":[],\"m\":{\"medio\":2,\"final\":1},\"d\":[],\"e\":[\"Joseph appears.\",\"Focus the Ripple on the fingertips!\",\"Joseph jumps on the horse./Wamuu inside the horses!\"]},{\"t\":\"What a Wonderful World\",\"n\":\"what a wonderful world\",\"p\":\"SO\",\"o\":\"stone ocean\",\"u\":1,\"l\":140,\"a\":[\"pelea\"],\"m\":{\"medio\":1},\"d\":[],\"e\":[\"The universe resets again/Emporio encounters Eldis and Anakiss\"]},{\"t\":\"Wicked Thoughts\",\"n\":\"wicked thoughts\",\"p\":\"DU\",\"o\":\"\",\"u\":5,\"l\":35,\"a\":[],\"m\":{\"medio\":2,\"final\":2,\"inicio\":1},\"d\":[],\"e\":[\"Katagiri Anjuro presentation\",\"Aqua Necklace appears\",\"Angelo sends Aqua Necklace into the Higashikata's faucet\",\"Angelo takes a boy hostage\"]},{\"t\":\"Wind in the Wilderness\",\"n\":\"wind in the wilderness\",\"p\":\"SC\",\"o\":\"departure\",\"u\":8,\"l\":80,\"a\":[\"viaje\",\"epico\"],\"m\":{\"medio\":6,\"final\":2},\"d\":[\"hol horse\"],\"e\":[\"Hol Horse appears\",\"Polnareff banters with Hol Horse\",\"Hol Horse finds Polnareff and Kakyoin\",\"Hol Horse runs away\"]},{\"t\":\"With Humanity, Affection, and Nobility\",\"n\":\"with humanity affection and nobility\",\"p\":\"PB/BT\",\"o\":\"future\",\"u\":3,\"l\":60,\"a\":[\"victoria\",\"viaje\"],\"m\":{\"medio\":3},\"d\":[],\"e\":[\"Jonathan's friends ignore him.\",\"Erina wants to die with Jonathan/Erina saves a baby.\",\"Lisa Lisa was the baby on the ship!\"]},{\"t\":\"YOU ARE MY WOMAN/Alex Reid\",\"n\":\"you are my woman alex reid\",\"p\":\"DU\",\"o\":\"good morning\",\"u\":2,\"l\":33,\"a\":[\"calma\"],\"m\":{\"inicio\":2},\"d\":[],\"e\":[\"Breakfast is served\",\"Jotaro Kujo's taxi ride\"]}],\"alias\":{\"fire shaman\":\"The Magician of Fire\",",
        "\"noble pope\":\"Noble Hierophant\",\"imminence\":\"Urgency\",\"increasing strength\":\"Increasing Power\",\"brutality\":\"Barbarism\",\"bizarre\":\"Strange and Mysterious\",\"conspiracy\":\"The Scheme\",\"nightmare spell\":\"Curse of Nightmares\",\"head to head\":\"Battle Between Equals\",\"fight to antagonize\":\"Close Match\",\"ken\":\"Sword Attack\",\"the off unexpected prophecy\":\"The Prophecy That's Never Wrong\",\"the kakero the bluff\":\"Bet on a Bluff\",\"blow throwing reverse play\":\"Batting, Pitching, Turning the Tables\",\"the battle starts\":\"The Battle Begins\",\"rampage\":\"Mad Dash\",\"apparent crisis\":\"Unfolding Crisis\",\"rhapsody of brothers\":\"Brothers' Rhapsody\",\"awakening darkness of the world\":\"Darkness of The World's Awakening\",\"the return of travelers\":\"The Travelers Return\",\"wonder\":\"meraviglia\",\"serenely\":\"serenamente\",\"witty\":\"spiritoso\",\"small\":\"piccolo\",\"mirror\":\"specchio\",\"showdown\":\"resa dei conti\",\"growing old\":\"invecchiare\",\"fish\":\"pesce\",\"extremely\":\"di molto\",\"ice\":\"ghiaccio\",\"the darkness\":\"l'oscurita\",\"life\":\"vita\",\"another person\":\"un'altra persona\",\"fierce fight\":\"lotta feroce\",\"death\":\"morte\",\"tense\":\"teso\"}}",
    });
}

// ---- src/comun/VentanaMusica.cs ----

// Plantilla de bloques y kit de una serie de TV: duracion de cada bloque y el
// archivo fijo de los que lo tienen (opening, re-gancho, continuara, ending).
// Sin archivo se usa un placeholder con esa duracion.
class VentanaPlantillaTV : VentanaBase
{
    public PlantillaTV Resultado;
    bool cargando;
    Lista lst = new Lista();
    CampoNumero numDur = new CampoNumero();
    Etiqueta lblDur, lblDesc, lblTotal;
    Boton btnArchivo = new Boton("Elegir archivo\u2026", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Usar placeholder", EstiloBoton.Secundario);
    Boton btnDefecto = new Boton("Valores de partida", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);
    readonly double minimo, maximo;

    public VentanaPlantillaTV(PlantillaTV p, ReglasRitmo reglas) : base("Plantilla y kit", 860)
    {
        StartPosition = FormStartPosition.CenterParent;
        Resultado = p.Copia();
        minimo = reglas.DuracionMin * 60; maximo = reglas.DuracionMax * 60;
        int m = Margen, w = Ancho;
        Encabezado("Plantilla y kit", "Bloques de cada cap\u00edtulo. Los del kit usan tu archivo; sin archivo, un placeholder de esa duraci\u00f3n.");
        int y = 92;
        lst.CheckBoxes = false;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Bloque", 170);
        lst.Columns.Add("Qu\u00e9 es", 90);
        lst.Columns.Add("Duraci\u00f3n", 120);
        lst.Columns.Add("Archivo", w - 170 - 90 - 120 - sb);
        Pos(lst, m, y, w, 250);
        y += 258;
        lblDesc = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 34);
        y += 40;
        lblDur = Texto("DURACI\u00d3N", Tema.Pequena, Tema.TextoSuave, m, y, 160, 18);
        Pos(numDur, m, y + 18, 150, 32);
        numDur.Minimo = 0; numDur.Maximo = 600; numDur.Paso = 1;
        Pos(btnArchivo, m + 166, y + 18, 150, 32);
        Pos(btnQuitar, m + 324, y + 18, 150, 32);
        Pos(btnDefecto, m + w - 160, y + 18, 160, 32);
        y += 60;
        lblTotal = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 290, 40);
        Pos(btnCancelar, m + w - 280, y, 120, 40);
        Pos(btnGuardar, m + w - 150, y, 150, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        lst.SelectedIndexChanged += delegate { Elegido(); };
        numDur.Cambio += delegate
        {
            BloqueTV b = Actual();
            if (b == null || cargando) return;
            if (b.Tipo == "contenido" && b.Segundos == 0) b.Porcentaje = numDur.Valor; else b.Segundos = numDur.Valor;
            Llenar();
        };
        btnArchivo.Click += delegate
        {
            BloqueTV b = Actual();
            if (b == null) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Archivo para \u00ab" + b.Nombre + "\u00bb";
                d.Filter = "Video, imagen o audio|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.png;*.jpg;*.jpeg;*.gif;*.mp3;*.wav|Todos|*.*";
                string a = Resultado.Archivo(b.Clave);
                if (a.Length > 0 && Directory.Exists(Path.GetDirectoryName(a))) d.InitialDirectory = Path.GetDirectoryName(a);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Resultado.Kit[b.Clave] = d.FileName;
                if (b.Tipo != "kit") b.Tipo = "kit";
            }
            Llenar();
        };
        btnQuitar.Click += delegate
        {
            BloqueTV b = Actual();
            if (b == null) return;
            Resultado.Kit.Remove(b.Clave);
            Llenar();
        };
        btnDefecto.Click += delegate
        {
            Dictionary<string, string> kit = Resultado.Kit;
            Resultado = PlantillaTV.PorDefecto();
            foreach (KeyValuePair<string, string> kv in kit) Resultado.Kit[kv.Key] = kv.Value;
            Llenar();
        };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        btnGuardar.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        Llenar();
        if (lst.Items.Count > 0) lst.Items[0].Selected = true;
    }

    BloqueTV Actual() { return lst.SelectedIndices.Count == 0 ? null : Resultado.Bloques[lst.SelectedIndices[0]]; }

    static string Duracion(BloqueTV b)
    {
        return b.Segundos > 0 ? b.Segundos + " s" : b.Porcentaje + " % del resto";
    }

    void Llenar()
    {
        int sel = lst.SelectedIndices.Count > 0 ? lst.SelectedIndices[0] : -1;
        cargando = true;
        lst.Items.Clear();
        foreach (BloqueTV b in Resultado.Bloques)
        {
            ListViewItem it = new ListViewItem(b.Nombre);
            it.SubItems.Add(b.Tipo == "kit" ? "kit" : b.Tipo == "texto" ? "texto" : "del cap\u00edtulo");
            it.SubItems.Add(Duracion(b));
            string a = Resultado.Archivo(b.Clave);
            it.SubItems.Add(b.Tipo != "kit" ? "\u2014" : a.Length > 0 ? Path.GetFileName(a) + (File.Exists(a) ? "" : " (no se encuentra)") : "placeholder");
            if (b.Tipo == "kit" && a.Length == 0) it.ForeColor = Tema.AcentoHover;
            lst.Items.Add(it);
        }
        if (sel >= 0 && sel < lst.Items.Count) lst.Items[sel].Selected = true;
        cargando = false;
        double fijo = Resultado.Fijo();
        lblTotal.Text = "Lo fijo suma " + Formato.Tiempo(fijo) + ": para " + Formato.Tiempo(minimo) + "\u2013" + Formato.Tiempo(maximo) +
                        " quedan " + Formato.Tiempo(Math.Max(0, minimo - fijo)) + "\u2013" + Formato.Tiempo(Math.Max(0, maximo - fijo)) + " para los actos.";
        Elegido();
    }

    void Elegido()
    {
        BloqueTV b = Actual();
        btnArchivo.Enabled = btnQuitar.Enabled = numDur.Enabled = b != null;
        if (b == null) { lblDesc.Text = ""; return; }
        cargando = true;
        bool pct = b.Tipo == "contenido" && b.Segundos == 0;
        lblDur.Text = pct ? "PORCENTAJE DEL RESTO" : "DURACI\u00d3N (S)";
        numDur.Sufijo = pct ? "%" : "s";
        numDur.Valor = (int)Math.Round(pct ? b.Porcentaje : b.Segundos);
        numDur.Invalidate();
        cargando = false;
        btnArchivo.Enabled = b.Tipo == "kit" || b.Tipo == "texto";
        btnQuitar.Enabled = Resultado.Archivo(b.Clave).Length > 0;
        lblDesc.Text = b.Descripcion;
    }
}

// Musica de la serie: carpeta con su indice, reparto y el tema de cada uno.
class VentanaMusicaSerie : VentanaBase
{
    public MusicaSerie Resultado;
    readonly string clave, modelo, premisa;
    BibliotecaMusica biblioteca;
    bool trabajando;

    Etiqueta lblCarpeta, lblIndice, lblEstado;
    Boton btnCarpeta = new Boton("Elegir carpeta\u2026", EstiloBoton.Secundario);
    Boton btnIndexar = new Boton("Indexar", EstiloBoton.Secundario);
    Boton btnEtiquetar = new Boton("Etiquetar con IA", EstiloBoton.Secundario);
    CampoTexto txtReparto = new CampoTexto();
    CampoTexto txtPreferencias = new CampoTexto();
    Lista lst = new Lista();
    Boton btnIA = new Boton("Elegir con IA", EstiloBoton.Primario);
    Boton btnCambiar = new Boton("Cambiar\u2026", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Quitar", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);
    BarraProgreso barra = new BarraProgreso();

    public VentanaMusicaSerie(MusicaSerie m, string premisa, string clave, string modelo) : base("M\u00fasica de la serie", 1000)
    {
        StartPosition = FormStartPosition.CenterParent;
        this.clave = clave; this.modelo = modelo; this.premisa = premisa ?? "";
        Resultado = MusicaSerie.Leer(m.Escribir());
        int x = Margen, w = Ancho;
        Encabezado("M\u00fasica de la serie", "Tu biblioteca indexada por c\u00f3mo se usa cada tema en el anime, y el tema de cada personaje.");
        int y = 92;
        lblCarpeta = Texto("", Tema.Normal, Tema.Texto, x, y + 6, w - 490, 20);
        Pos(btnCarpeta, x + w - 480, y, 150, 32);
        Pos(btnIndexar, x + w - 322, y, 152, 32);
        Pos(btnEtiquetar, x + w - 162, y, 162, 32);
        y += 38;
        lblIndice = Texto("", Tema.Pequena, Tema.TextoSuave, x, y, w, 18);
        Pos(barra, x, y + 22, w, 6);
        barra.Visible = false;
        y += 34;
        int mitad = (w - 16) / 2;
        Texto("REPARTO (uno por l\u00ednea: \u00abNombre: c\u00f3mo es\u00bb)", Tema.Pequena, Tema.TextoSuave, x, y, mitad, 18);
        Texto("PREFERENCIAS (opcional: \u00abpara Gerber algo de Golden Wind\u00bb\u2026)", Tema.Pequena, Tema.TextoSuave, x + mitad + 16, y, mitad, 18);
        txtReparto.Multilinea = true; txtPreferencias.Multilinea = true;
        Pos(txtReparto, x, y + 20, mitad, 96);
        Pos(txtPreferencias, x + mitad + 16, y + 20, mitad, 96);
        y += 126;
        lst.CheckBoxes = false;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Para", 150);
        lst.Columns.Add("Tema", 220);
        lst.Columns.Add("Variantes", 74);
        lst.Columns.Add("Por qu\u00e9", w - 150 - 220 - 74 - sb);
        Pos(lst, x, y, w, 200);
        y += 208;
        Pos(btnIA, x, y, 160, 34);
        Pos(btnCambiar, x + 168, y, 120, 34);
        Pos(btnQuitar, x + 296, y, 100, 34);
        y += 44;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, x, y, w - 290, 40);
        Pos(btnCancelar, x + w - 280, y, 120, 40);
        Pos(btnGuardar, x + w - 150, y, 150, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        txtReparto.Text = (Resultado.Reparto ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        btnCarpeta.Click += delegate { ElegirCarpeta(); };
        btnIndexar.Click += delegate { Indexar(); };
        btnEtiquetar.Click += delegate { Etiquetar(); };
        btnIA.Click += delegate { ConIA(); };
        btnCambiar.Click += delegate { Cambiar(); };
        btnQuitar.Click += delegate
        {
            string k = Fila();
            if (k == null) return;
            if (k == "") Resultado.Principal = null; else Resultado.Personajes.Remove(k);
            Llenar();
        };
        txtReparto.Caja.Leave += delegate { Resultado.Reparto = txtReparto.Text.Trim(); Llenar(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        btnGuardar.Click += delegate { Resultado.Reparto = txtReparto.Text.Trim(); DialogResult = DialogResult.OK; Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };

        if (Resultado.Carpeta.Length > 0 && Directory.Exists(Resultado.Carpeta))
            try { biblioteca = BibliotecaMusica.Cargar(Resultado.Carpeta); } catch { biblioteca = null; }
        Mostrar();
        Llenar();
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Mostrar()
    {
        lblCarpeta.Text = Resultado.Carpeta.Length > 0 ? "Carpeta: " + Resultado.Carpeta : "Elige la carpeta de tu m\u00fasica.";
        btnIndexar.Enabled = Resultado.Carpeta.Length > 0;
        btnIndexar.Text = biblioteca == null ? "Indexar" : "Volver a indexar";
        if (biblioteca == null)
            lblIndice.Text = Resultado.Carpeta.Length > 0 ? "Sin \u00edndice todav\u00eda: pulsa \u00abIndexar\u00bb (lee las etiquetas de cada archivo; tarda un poco la primera vez)." : "";
        else
        {
            int con = 0, etiq = 0;
            foreach (ArchivoMusica a in biblioteca.Archivos) { if (a.ConUso) con++; else if (a.Etiquetado) etiq++; }
            int falta = biblioteca.PorEtiquetar().Count;
            lblIndice.Text = biblioteca.Archivos.Count + " archivos \u00b7 " + con + " con datos de c\u00f3mo se usan en el anime \u00b7 " + etiq +
                             " etiquetados (juegos, fanmade\u2026)" + (falta > 0 ? " \u00b7 " + falta + " sin etiquetar: \u00abEtiquetar con IA\u00bb" : "");
        }
        btnIA.Enabled = biblioteca != null && !String.IsNullOrEmpty(clave);
        btnEtiquetar.Enabled = biblioteca != null && !String.IsNullOrEmpty(clave) && !trabajando && biblioteca.PorEtiquetar().Count > 0;
    }

    string Nombre(string ruta)
    {
        ArchivoMusica a = biblioteca != null ? biblioteca.Buscar(ruta) : null;
        return a != null ? a.Titulo + (a.Parte.Length > 0 ? " (" + a.Parte + ")" : "") : Path.GetFileNameWithoutExtension(ruta);
    }

    void Llenar()
    {
        lst.Items.Clear();
        List<KeyValuePair<string, TemaAsignado>> filas = new List<KeyValuePair<string, TemaAsignado>>();
        filas.Add(new KeyValuePair<string, TemaAsignado>("", Resultado.Principal));
        List<string> nombres = Resultado.Nombres();
        foreach (string n in nombres)
        {
            TemaAsignado t;
            Resultado.Personajes.TryGetValue(n, out t);
            filas.Add(new KeyValuePair<string, TemaAsignado>(n, t));
        }
        foreach (KeyValuePair<string, TemaAsignado> kv in Resultado.Personajes)
            if (!nombres.Contains(kv.Key)) filas.Add(kv);
        foreach (KeyValuePair<string, TemaAsignado> f in filas)
        {
            ListViewItem it = new ListViewItem(f.Key.Length == 0 ? "Tema principal" : f.Key);
            it.SubItems.Add(f.Value != null ? Nombre(f.Value.Archivo) : "\u2014");
            it.SubItems.Add(f.Value != null && f.Value.Variantes.Count > 0 ? f.Value.Variantes.Count.ToString() : "");
            it.SubItems.Add(f.Value != null ? f.Value.Motivo : "");
            it.Tag = f.Key;
            if (f.Value == null) it.ForeColor = Tema.TextoSuave;
            lst.Items.Add(it);
        }
    }

    string Fila() { return lst.SelectedIndices.Count == 0 ? null : (string)lst.Items[lst.SelectedIndices[0]].Tag; }

    void ElegirCarpeta()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta de tu m\u00fasica (se busca tambi\u00e9n en subcarpetas)";
            if (Resultado.Carpeta.Length > 0 && Directory.Exists(Resultado.Carpeta)) d.SelectedPath = Resultado.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Resultado.Carpeta = d.SelectedPath;
        }
        try { biblioteca = BibliotecaMusica.Cargar(Resultado.Carpeta); } catch { biblioteca = null; }
        Mostrar();
        Llenar();
    }

    void Indexar()
    {
        trabajando = true;
        barra.Visible = true;
        foreach (Control c in new Control[] { btnIndexar, btnCarpeta, btnIA, btnGuardar }) c.Enabled = false;
        try
        {
            Action<string, double> av = delegate (string t, double f) { Estado(t, false); barra.Valor = f; Application.DoEvents(); };
            List<FilaMusica> filas = BibliotecaMusica.Escanear(Resultado.Carpeta, delegate (string t, double f) { av(t, f * 0.8); });
            BibliotecaMusica vieja = biblioteca;
            biblioteca = BibliotecaMusica.Indexar(Resultado.Carpeta, filas, delegate (string t, double f) { av(t, 0.8 + f * 0.2); });
            biblioteca.ConservarEtiquetas(vieja);   // lo que la IA ya etiqueto no se pierde
            biblioteca.Guardar(Path.Combine(Resultado.Carpeta, BibliotecaMusica.NombreIndice));
            Estado("\u2714 \u00cdndice guardado en " + BibliotecaMusica.NombreIndice + " (en la carpeta de la m\u00fasica).", false);
        }
        catch (Exception ex) { Estado("No se pudo indexar: " + ex.Message, true); }
        trabajando = false;
        barra.Visible = false;
        foreach (Control c in new Control[] { btnIndexar, btnCarpeta, btnGuardar }) c.Enabled = true;
        Mostrar();
        Llenar();
    }

    // Juegos, fanmade y demas: Gemini dice como suena cada uno (en lotes) y se guarda en el indice.
    void Etiquetar()
    {
        List<ArchivoMusica> falta = biblioteca.PorEtiquetar();
        if (falta.Count == 0) return;
        string c = clave, mo = modelo;
        BibliotecaMusica b = biblioteca;
        string indice = Path.Combine(Resultado.Carpeta, BibliotecaMusica.NombreIndice);
        trabajando = true;
        barra.Visible = true;
        foreach (Control x in new Control[] { btnIA, btnGuardar, btnIndexar, btnEtiquetar, btnCarpeta }) x.Enabled = false;
        Thread hilo = new Thread(delegate ()
        {
            int hechos = 0;
            string error = null;
            const int Lote = 100;
            for (int i = 0; i < falta.Count && error == null; i += Lote)
            {
                List<ArchivoMusica> lote = falta.GetRange(i, Math.Min(Lote, falta.Count - i));
                int ii = i;
                try { BeginInvoke((MethodInvoker)delegate { Estado("Gemini est\u00e1 escuchando " + (ii + 1) + "\u2013" + (ii + lote.Count) + " de " + falta.Count + "\u2026", false); barra.Valor = (double)ii / falta.Count; }); } catch { }
                try { hechos += BibliotecaMusica.AplicarEtiquetas(lote, Gemini.Generar(c, mo, BibliotecaMusica.InstruccionesEtiquetar(), b.MensajeEtiquetar(lote), true)); }
                catch (Exception ex) { error = ex.Message; }
            }
            try { b.Guardar(indice); } catch (Exception ex) { if (error == null) error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    barra.Visible = false;
                    foreach (Control x in new Control[] { btnIA, btnGuardar, btnIndexar, btnCarpeta }) x.Enabled = true;
                    Estado((error != null ? "Se cort\u00f3 (" + error + "). " : "\u2714 ") + hechos + " de " + falta.Count + " etiquetados y guardados en el \u00edndice" +
                           (hechos < falta.Count ? "; los que Gemini no conoce quedan sin etiquetar (ponlos en subcarpetas como \u00abPelea\u00bb o \u00abCalma\u00bb)." : "."), error != null);
                    Mostrar();
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void ConIA()
    {
        Resultado.Reparto = txtReparto.Text.Trim();
        if (Resultado.Nombres().Count == 0) { Estado("Escribe el reparto: un personaje por l\u00ednea.", true); return; }
        List<ArchivoMusica> cand = MusicaSerie.Candidatos(biblioteca);
        string instr = MusicaSerie.Instrucciones(), msg = MusicaSerie.Mensaje(cand, premisa, Resultado.Reparto, txtPreferencias.Text);
        string c = clave, mo = modelo;
        trabajando = true;
        foreach (Control x in new Control[] { btnIA, btnGuardar, btnIndexar }) x.Enabled = false;
        Estado("Gemini est\u00e1 escuchando tu biblioteca\u2026", false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(c, mo, instr, msg, true); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    foreach (Control x in new Control[] { btnIA, btnGuardar, btnIndexar }) x.Enabled = true;
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        int n = Resultado.Aplicar(resp, cand);
                        Llenar();
                        Estado("\u2714 " + n + " temas elegidos. Cambia los que quieras y guarda: quedan fijos para toda la serie.", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer: " + ex.Message, true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Cambiar()
    {
        string k = Fila();
        if (k == null) { Estado("Elige una fila.", true); return; }
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Tema para " + (k.Length == 0 ? "la serie" : k);
            d.Filter = "M\u00fasica|*.mp3;*.flac;*.wav;*.m4a;*.ogg;*.opus;*.aac;*.wma";
            if (Resultado.Carpeta.Length > 0 && Directory.Exists(Resultado.Carpeta)) d.InitialDirectory = Resultado.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            TemaAsignado t = new TemaAsignado();
            string raiz = Resultado.Carpeta.TrimEnd('\\', '/');
            t.Archivo = raiz.Length > 0 && d.FileName.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? d.FileName.Substring(raiz.Length + 1) : d.FileName;
            t.Motivo = "elegido a mano";
            ArchivoMusica a = biblioteca != null ? biblioteca.Buscar(t.Archivo) : null;
            if (a != null) t.Variantes.AddRange(a.Variantes);
            if (k.Length == 0) Resultado.Principal = t; else Resultado.Personajes[k] = t;
        }
        Llenar();
    }
}

// ---- src/comun/Ritmo.cs ----

// =====================================================================
// Medidor de ritmo: minuto a minuto, cuanto narra el narrador, cuantos
// cortes hay, cuantos recursos (textos, imagenes, memes, efectos de sonido)
// y cuantas veces cambia la musica. Se compara con las reglas de la serie
// para encontrar los "valles" donde la gente se suele ir.
// =====================================================================

public class ReglasRitmo
{
    public int PPM = 195;              // palabras por minuto del narrador
    public int NarradorCadaSeg = 90;   // como maximo, tanto sin narrador
    public int RecursosPorMin = 4;
    public int CortesMin = 15, CortesMax = 20;
    public int MusicaCadaSeg = 40;     // cambiar de musica al menos cada tanto
    public int ZonaCriticaSeg = 180;   // de 0:30 a aqui se decide si se quedan
    public double DuracionMin = 10, DuracionMax = 12;   // minutos

    public ReglasRitmo Copia() { return (ReglasRitmo)MemberwiseClone(); }

    public void Escribir(Dictionary<string, object> d)
    {
        d["ppm"] = PPM; d["narradorCada"] = NarradorCadaSeg; d["recursosMin"] = RecursosPorMin;
        d["cortesMin"] = CortesMin; d["cortesMax"] = CortesMax; d["musicaCada"] = MusicaCadaSeg;
        d["zonaCritica"] = ZonaCriticaSeg; d["duracionMin"] = DuracionMin; d["duracionMax"] = DuracionMax;
    }

    public static ReglasRitmo Leer(object o, ReglasRitmo base_)
    {
        ReglasRitmo r = base_.Copia();
        if (o == null) return r;
        r.PPM = (int)Json.Numero(o, "ppm", r.PPM);
        r.NarradorCadaSeg = (int)Json.Numero(o, "narradorCada", r.NarradorCadaSeg);
        r.RecursosPorMin = (int)Json.Numero(o, "recursosMin", r.RecursosPorMin);
        r.CortesMin = (int)Json.Numero(o, "cortesMin", r.CortesMin);
        r.CortesMax = (int)Json.Numero(o, "cortesMax", r.CortesMax);
        r.MusicaCadaSeg = (int)Json.Numero(o, "musicaCada", r.MusicaCadaSeg);
        r.ZonaCriticaSeg = (int)Json.Numero(o, "zonaCritica", r.ZonaCriticaSeg);
        r.DuracionMin = Json.Numero(o, "duracionMin", r.DuracionMin);
        r.DuracionMax = Json.Numero(o, "duracionMax", r.DuracionMax);
        return r;
    }
}

public class MinutoRitmo
{
    public int Minuto;
    public double Narrador;     // 0..1 del minuto con narracion
    public int Cortes, Recursos, Musica;
}

public class Medicion
{
    public double Duracion;
    public List<MinutoRitmo> Minutos = new List<MinutoRitmo>();
    public List<Rango> Narracion = new List<Rango>();
    public List<double> CambiosMusica = new List<double>();
    public double PPM;          // 0 si no hay narrador
    public int Palabras;
    public bool HayNarrador { get { return Narracion.Count > 0; } }
}

// Un aviso del medidor, con el tramo al que se refiere.
public class Valle
{
    public double Inicio, Fin;
    public string Tipo = "", Texto = "";
    public bool Critico;        // en la zona critica del inicio
}

public static class Ritmo
{
    // Arma la medicion con lo que ya se sabe del proyecto.
    public static Medicion Medir(double duracion, List<double> cortes, List<double> recursos, List<double> cambiosMusica,
                                 List<Rango> narracion, int palabrasNarrador, double segundosHablados)
    {
        Medicion m = new Medicion();
        m.Duracion = duracion;
        m.Narracion = Rangos.Unir(narracion, 0.6);
        m.CambiosMusica = new List<double>(cambiosMusica);
        m.Palabras = palabrasNarrador;
        m.PPM = segundosHablados > 5 ? Math.Round(palabrasNarrador / segundosHablados * 60) : 0;
        int n = Math.Max(1, (int)Math.Ceiling(duracion / 60));
        for (int i = 0; i < n; i++) m.Minutos.Add(new MinutoRitmo { Minuto = i });
        foreach (double t in cortes) Sumar(m, t, 0);
        foreach (double t in recursos) Sumar(m, t, 1);
        foreach (double t in cambiosMusica) Sumar(m, t, 2);
        foreach (Rango r in m.Narracion)
        {
            double t = r.Inicio;
            while (t < r.Fin - 1e-6)
            {
                int i = (int)(t / 60);
                if (i >= n) break;
                double hasta = Math.Min(r.Fin, (i + 1) * 60.0);
                m.Minutos[i].Narrador += (hasta - t) / 60.0;
                t = hasta;
            }
        }
        return m;
    }

    static void Sumar(Medicion m, double t, int que)
    {
        int i = (int)(t / 60);
        if (i < 0 || i >= m.Minutos.Count) return;
        if (que == 0) m.Minutos[i].Cortes++;
        else if (que == 1) m.Minutos[i].Recursos++;
        else m.Minutos[i].Musica++;
    }

    // Tramos sin narrador mas largos que "maximo" segundos.
    public static List<Rango> SinNarrador(Medicion m, double maximo)
    {
        List<Rango> r = new List<Rango>();
        double cursor = 0;
        foreach (Rango n in m.Narracion)
        {
            if (n.Inicio - cursor > maximo) r.Add(new Rango(cursor, n.Inicio));
            cursor = Math.Max(cursor, n.Fin);
        }
        if (m.Duracion - cursor > maximo) r.Add(new Rango(cursor, m.Duracion));
        return r;
    }

    // Lo que no cumple las reglas, del mas importante al menos.
    public static List<Valle> Valles(Medicion m, ReglasRitmo reglas, bool conNarrador)
    {
        List<Valle> v = new List<Valle>();
        double zona = reglas.ZonaCriticaSeg;
        if (conNarrador)
            foreach (Rango r in SinNarrador(m, reglas.NarradorCadaSeg))
                v.Add(new Valle
                {
                    Inicio = r.Inicio, Fin = r.Fin, Tipo = "narrador", Critico = r.Inicio < zona,
                    Texto = Formato.Tiempo(r.Fin - r.Inicio) + " sin narrador (m\u00e1ximo " + reglas.NarradorCadaSeg + " s)"
                });
        // Minutos seguidos con pocos recursos o pocos cortes.
        Agrupar(m, v, delegate (MinutoRitmo x) { return x.Recursos < reglas.RecursosPorMin; }, "recursos",
                delegate (int a, int b) { return "pocos recursos (menos de " + reglas.RecursosPorMin + "/min)"; }, zona);
        Agrupar(m, v, delegate (MinutoRitmo x) { return x.Cortes < reglas.CortesMin && (x.Minuto + 1) * 60 <= m.Duracion + 30; }, "cortes",
                delegate (int a, int b) { return "ritmo lento (menos de " + reglas.CortesMin + " cortes/min)"; }, zona);
        // Musica que no cambia.
        List<double> cambios = new List<double>(m.CambiosMusica);
        cambios.Sort();
        if (cambios.Count > 0)
        {
            cambios.Add(m.Duracion);
            for (int i = 0; i + 1 < cambios.Count; i++)
                if (cambios[i + 1] - cambios[i] > reglas.MusicaCadaSeg * 1.5)
                    v.Add(new Valle
                    {
                        Inicio = cambios[i], Fin = cambios[i + 1], Tipo = "musica",
                        Texto = "la misma m\u00fasica " + Formato.Tiempo(cambios[i + 1] - cambios[i]) + " (cambiarla cada ~" + reglas.MusicaCadaSeg + " s)"
                    });
        }
        if (m.Duracion > reglas.DuracionMax * 60 + 30)
            v.Add(new Valle
            {
                Inicio = reglas.DuracionMax * 60, Fin = m.Duracion, Tipo = "duracion",
                Texto = "dura " + Formato.Tiempo(m.Duracion) + ": m\u00e1s que el objetivo (" + reglas.DuracionMin + "\u2013" + reglas.DuracionMax + " min)"
            });
        v.Sort(delegate (Valle a, Valle b)
        {
            if (a.Critico != b.Critico) return a.Critico ? -1 : 1;
            return a.Inicio.CompareTo(b.Inicio);
        });
        return v;
    }

    delegate bool Condicion(MinutoRitmo m);
    delegate string Descripcion(int desde, int hasta);

    static void Agrupar(Medicion m, List<Valle> v, Condicion mal, string tipo, Descripcion texto, double zona)
    {
        int i = 0;
        while (i < m.Minutos.Count)
        {
            if (!mal(m.Minutos[i])) { i++; continue; }
            int j = i;
            while (j + 1 < m.Minutos.Count && mal(m.Minutos[j + 1])) j++;
            if (j - i + 1 >= 2 || i * 60 < zona)   // un solo minuto flojo solo importa al inicio
                v.Add(new Valle
                {
                    Inicio = i * 60, Fin = Math.Min(m.Duracion, (j + 1) * 60), Tipo = tipo, Critico = i * 60 < zona,
                    Texto = "min " + i + (j > i ? "\u2013" + j : "") + ": " + texto(i, j)
                });
            i = j + 1;
        }
    }

    static double Percentil(List<double> l, double p)
    {
        if (l.Count == 0) return 0;
        List<double> o = new List<double>(l);
        o.Sort();
        double x = p * (o.Count - 1);
        int a = (int)Math.Floor(x), b = Math.Min(o.Count - 1, a + 1);
        return o[a] + (o[b] - o[a]) * (x - a);
    }

    // Reglas sacadas de un episodio que funciono: su ritmo es el objetivo.
    public static ReglasRitmo Aprender(Medicion m, ReglasRitmo base_)
    {
        ReglasRitmo r = base_.Copia();
        List<double> cortes = new List<double>(), recursos = new List<double>();
        foreach (MinutoRitmo x in m.Minutos)
            if ((x.Minuto + 1) * 60 <= m.Duracion + 30) { cortes.Add(x.Cortes); recursos.Add(x.Recursos); }
        if (cortes.Count > 0)
        {
            r.CortesMin = (int)Math.Max(5, Math.Round(Percentil(cortes, 0.40)));
            r.CortesMax = (int)Math.Max(r.CortesMin + 2, Math.Round(Percentil(cortes, 0.80)));
            r.RecursosPorMin = (int)Math.Max(1, Math.Round(Percentil(recursos, 0.50)));
        }
        if (m.HayNarrador)
        {
            if (m.PPM > 60) r.PPM = (int)m.PPM;
            List<double> huecos = new List<double>();
            double cursor = 0;
            foreach (Rango n in m.Narracion) { if (n.Inicio > cursor) huecos.Add(n.Inicio - cursor); cursor = Math.Max(cursor, n.Fin); }
            if (huecos.Count > 2) r.NarradorCadaSeg = (int)Math.Max(45, Math.Min(150, Math.Round(Percentil(huecos, 0.75) / 5) * 5));
        }
        if (m.CambiosMusica.Count > 2)
        {
            List<double> c = new List<double>(m.CambiosMusica);
            c.Sort();
            List<double> d = new List<double>();
            for (int i = 1; i < c.Count; i++) d.Add(c[i] - c[i - 1]);
            r.MusicaCadaSeg = (int)Math.Max(15, Math.Round(Percentil(d, 0.50) / 5) * 5 + 10);
        }
        double min = m.Duracion / 60;
        r.DuracionMin = Math.Max(1, Math.Floor(min - 0.5));
        r.DuracionMax = Math.Ceiling(min + 1);
        return r;
    }

    // "10:35 \u00b7 17 cortes/min \u00b7 6 recursos/min \u00b7 narrador 33 % a 196 ppm \u00b7 m\u00fasica cada 30 s"
    public static string Resumen(Medicion m)
    {
        double min = Math.Max(1, m.Duracion / 60);
        int cortes = 0, recursos = 0;
        double narr = 0;
        foreach (MinutoRitmo x in m.Minutos) { cortes += x.Cortes; recursos += x.Recursos; narr += x.Narrador * 60; }
        string r = Formato.Tiempo(m.Duracion) + " \u00b7 " + Math.Round(cortes / min) + " cortes/min \u00b7 " + Math.Round(recursos / min) + " recursos/min";
        if (m.HayNarrador) r += " \u00b7 narrador " + Math.Round(narr / Math.Max(1, m.Duracion) * 100) + " %" + (m.PPM > 0 ? " a " + m.PPM + " ppm" : "");
        if (m.CambiosMusica.Count > 1) r += " \u00b7 m\u00fasica cada " + Math.Round(m.Duracion / m.CambiosMusica.Count) + " s";
        return r;
    }

    // Tabla en texto (para el informe y para Gemini).
    public static string Tabla(Medicion m)
    {
        StringBuilder sb = new StringBuilder("min;narrador%;cortes;recursos;cambiosMusica\n");
        foreach (MinutoRitmo x in m.Minutos)
            sb.Append(x.Minuto + ";" + Math.Round(x.Narrador * 100) + ";" + x.Cortes + ";" + x.Recursos + ";" + x.Musica + "\n");
        return sb.ToString();
    }
}

public static class Rangos
{
    public static List<Rango> Unir(List<Rango> l, double hueco)
    {
        List<Rango> o = new List<Rango>(l), r = new List<Rango>();
        o.Sort(delegate (Rango a, Rango b) { return a.Inicio.CompareTo(b.Inicio); });
        foreach (Rango x in o)
        {
            if (r.Count > 0 && x.Inicio <= r[r.Count - 1].Fin + hueco)
                r[r.Count - 1] = new Rango(r[r.Count - 1].Inicio, Math.Max(r[r.Count - 1].Fin, x.Fin));
            else r.Add(x);
        }
        return r;
    }
}

// Lee el proyecto de Vegas y reconoce para que es cada pista.
public static class RitmoVegas
{
    // Pista donde PulirEpisodio pone la narracion provisional.
    public const string PistaNarracion = "vegas-cut \u00b7 Narraci\u00f3n provisional";

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Nombre del archivo con barras de Windows o de las otras.
    static string NombreArchivo(string ruta)
    {
        ruta = ruta ?? "";
        int i = Math.Max(ruta.LastIndexOf('\\'), ruta.LastIndexOf('/'));
        return i >= 0 ? ruta.Substring(i + 1) : ruta;
    }

    static string Archivo(TrackEvent e)
    {
        try { return e.ActiveTake != null && e.ActiveTake.Media != null ? (e.ActiveTake.Media.FilePath ?? "") : ""; } catch { return ""; }
    }

    static bool Generado(TrackEvent e)
    {
        try { return e.ActiveTake != null && e.ActiveTake.Media != null && e.ActiveTake.Media.IsGenerated(); } catch { return false; }
    }

    static double Cubierto(List<Rango> l, double a, double b)
    {
        double c = 0;
        foreach (Rango r in l) c += Math.Max(0, Math.Min(b, r.Fin) - Math.Max(a, r.Inicio));
        return c;
    }

    static double Mediana(List<double> l)
    {
        if (l.Count == 0) return 0;
        l.Sort();
        return l[l.Count / 2];
    }

    // Pistas de audio con las grabaciones (voces y sonido del juego): las que
    // tienen sobre todo archivos de la pista principal o de la transcripcion.
    // La del narrador (por su nombre o "Narr...") no cuenta.
    public static List<Track> PistasGrabacion(Project p, Transcripcion t, string narrador)
    {
        Dictionary<string, bool> grab = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Track principal = null;
        double mejor = 0;
        foreach (Track pista in p.Tracks)
        {
            if (pista.IsAudio()) continue;
            double cubre = 0;
            foreach (TrackEvent e in pista.Events) if (!Generado(e)) cubre += S(e.Length);
            if (cubre > mejor) { mejor = cubre; principal = pista; }
        }
        if (principal != null) foreach (TrackEvent e in principal.Events) grab[NombreArchivo(Archivo(e))] = true;
        if (t != null)
            foreach (Hablante h in t.Hablantes)
            {
                string n = (h.Nombre ?? "").Trim();
                if (String.Equals(n, (narrador ?? "").Trim(), StringComparison.OrdinalIgnoreCase) || n.ToLowerInvariant().StartsWith("narr")) continue;
                if (!String.IsNullOrEmpty(h.Archivo)) grab[NombreArchivo(h.Archivo)] = true;
                foreach (Fuente f in h.Fuentes) grab[NombreArchivo(f.Media)] = true;
            }
        List<Track> r = new List<Track>();
        foreach (Track pista in p.Tracks)
        {
            if (!pista.IsAudio()) continue;
            int si = 0, total = 0;
            foreach (TrackEvent e in pista.Events) { total++; if (grab.ContainsKey(NombreArchivo(Archivo(e)))) si++; }
            if (total > 0 && si * 2 >= total) r.Add(pista);
        }
        return r;
    }

    // Pistas de audio con las voces transcritas (no el sonido del juego).
    public static List<Track> PistasDeVoz(Project p, Transcripcion t)
    {
        Dictionary<string, bool> voz = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (t != null)
            foreach (Hablante h in t.Hablantes)
            {
                if (!h.Voz) continue;
                if (!String.IsNullOrEmpty(h.Archivo)) voz[NombreArchivo(h.Archivo)] = true;
                foreach (Fuente f in h.Fuentes) voz[NombreArchivo(f.Media)] = true;
            }
        List<Track> r = new List<Track>();
        foreach (Track pista in p.Tracks)
        {
            if (!pista.IsAudio()) continue;
            int si = 0, total = 0;
            foreach (TrackEvent e in pista.Events) { total++; if (voz.ContainsKey(NombreArchivo(Archivo(e)))) si++; }
            if (total > 0 && si * 2 >= total) r.Add(pista);
        }
        return r;
    }

    // Mide el proyecto abierto en Vegas con su transcripcion (si la tiene),
    // siguiendo las ediciones hechas despues de transcribir.
    public static Medicion MedirAbierto(Vegas vegas, string narrador)
    {
        Transcripcion t = null;
        string ruta = String.IsNullOrEmpty(vegas.Project.FilePath) ? null : Transcripcion.RutaPara(vegas.Project.FilePath);
        if (ruta != null && File.Exists(ruta))
        {
            t = Transcripcion.Cargar(ruta);
            if (t.TieneFuentes) t.Ubicador = PistasVegas.Ubicador(vegas.Project, t);
        }
        return Medir(vegas.Project, t, narrador);
    }

    // El narrador por su nombre en la transcripcion.
    public static Medicion Medir(Project p, Transcripcion t, string narrador)
    {
        int i = -1;
        if (t != null)
            for (int k = 0; k < t.Hablantes.Count; k++)
                if (String.Equals((t.Hablantes[k].Nombre ?? "").Trim(), (narrador ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) { i = k; break; }
        return Medir(p, t, i);
    }

    // narrador: indice del hablante narrador en la transcripcion (-1 = el que
    // se llame "Narrador", o ninguno).
    public static Medicion Medir(Project p, Transcripcion t, int narrador)
    {
        double duracion = S(p.Length);

        // 1. Pista principal: la de video con mas tiempo cubierto por grabaciones.
        Track principal = null;
        double mejor = 0;
        foreach (Track pista in p.Tracks)
        {
            if (pista.IsAudio()) continue;
            double cubre = 0;
            foreach (TrackEvent e in pista.Events) if (!Generado(e)) cubre += S(e.Length);
            if (cubre > mejor) { mejor = cubre; principal = pista; }
        }
        List<double> cortes = new List<double>();
        Dictionary<string, bool> deJuego = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (principal != null)
            foreach (TrackEvent e in principal.Events) { cortes.Add(S(e.Start)); deJuego[Archivo(e)] = true; }

        // 2. Narracion (de la transcripcion) y su archivo, para no contarlo como efecto.
        if (narrador < 0 && t != null)
            for (int i = 0; i < t.Hablantes.Count; i++)
                if ((t.Hablantes[i].Nombre ?? "").ToLowerInvariant().StartsWith("narr")) { narrador = i; break; }
        List<Rango> narracion = new List<Rango>();
        int palabras = 0;
        double hablado = 0;
        string archivoNarrador = "";
        // Grabaciones: lo de la pista principal, lo transcrito y todo archivo
        // que pase de 45 s en el proyecto (POV, voces). Cortadas a lo largo
        // del video no son "recursos": solo cuenta cuando aparecen.
        Dictionary<string, double> uso = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (Track pista in p.Tracks)
            foreach (TrackEvent e in pista.Events)
            {
                if (Generado(e)) continue;
                string f = NombreArchivo(Archivo(e));
                double u;
                uso.TryGetValue(f, out u);
                uso[f] = u + S(e.Length);
            }
        Dictionary<string, bool> grabacion = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in deJuego.Keys) grabacion[NombreArchivo(f)] = true;
        foreach (KeyValuePair<string, double> u in uso) if (u.Value > 45) grabacion[u.Key] = true;
        if (t != null)
            foreach (Hablante h in t.Hablantes)
            {
                if (!String.IsNullOrEmpty(h.Archivo)) grabacion[NombreArchivo(h.Archivo)] = true;
                foreach (Fuente f in h.Fuentes) grabacion[NombreArchivo(f.Media ?? "")] = true;
            }
        if (t != null && narrador >= 0 && narrador < t.Hablantes.Count)
        {
            archivoNarrador = NombreArchivo(t.Hablantes[narrador].Archivo ?? "");
            // Donde esta de verdad el archivo de narracion (en esa pista tambien
            // puede haber memes, que Whisper transcribe como si fueran del narrador).
            List<Rango> donde = new List<Rango>();
            if (archivoNarrador.Length > 0)
                foreach (Track pista in p.Tracks)
                    if (pista.IsAudio())
                        foreach (TrackEvent e in pista.Events)
                            if (String.Equals(NombreArchivo(Archivo(e)), archivoNarrador, StringComparison.OrdinalIgnoreCase))
                                donde.Add(new Rango(S(e.Start), S(e.Start) + S(e.Length)));
            foreach (Segmento s in t.SegmentosActuales())
            {
                if (s.Hablante != narrador) continue;
                if (donde.Count > 0 && Cubierto(donde, s.Inicio, s.Fin) < 0.5 * (s.Fin - s.Inicio)) continue;
                narracion.Add(new Rango(s.Inicio, s.Fin));
                palabras += s.Palabras.Count;
                if (s.Palabras.Count > 0) hablado += s.Palabras[s.Palabras.Count - 1].Fin - s.Palabras[0].Inicio;
            }
        }

        // La narracion provisional (voz de Windows) cuenta como narrador.
        foreach (Track pista in p.Tracks)
            if (pista.IsAudio() && pista.Name == PistaNarracion)
                foreach (TrackEvent e in pista.Events) if (!e.Mute) narracion.Add(new Rango(S(e.Start), S(e.Start) + S(e.Length)));

        // 3. Lo demas: recursos (video encima, efectos cortos) y musica (audio largo).
        List<double> recursos = new List<double>(), musica = new List<double>();
        foreach (Track pista in p.Tracks)
        {
            if (pista == principal || (principal != null && pista.Index == principal.Index)) continue;
            if (pista.Name == PistaNarracion) continue;
            List<TrackEvent> eventos = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) eventos.Add(e);
            if (eventos.Count == 0) continue;
            eventos.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });
            if (pista.IsAudio())
            {
                // Las voces y el sonido del juego no cuentan; lo que queda es musica o efectos.
                List<TrackEvent> otros = new List<TrackEvent>();
                foreach (TrackEvent e in eventos) if (!grabacion.ContainsKey(NombreArchivo(Archivo(e)))) otros.Add(e);
                if (otros.Count == 0) continue;
                List<double> largos = new List<double>();
                foreach (TrackEvent e in otros) largos.Add(S(e.Length));
                if (Mediana(largos) >= 15)
                {
                    string previo = null;
                    foreach (TrackEvent e in otros)
                    {
                        string f = Archivo(e);
                        if (f != previo) musica.Add(S(e.Start));
                        previo = f;
                    }
                }
                else foreach (TrackEvent e in otros) recursos.Add(S(e.Start));
                continue;
            }
            double finPrevio = -10;
            foreach (TrackEvent e in eventos)
            {
                double ini = S(e.Start);
                bool seguido = ini <= finPrevio + 0.5;
                finPrevio = Math.Max(finPrevio, ini + S(e.Length));
                if (Generado(e)) { if (S(e.Length) <= 20) recursos.Add(ini); continue; }   // un "D\u00eda N" fijo no cuenta
                if (grabacion.ContainsKey(NombreArchivo(Archivo(e))) && seguido) continue;   // la misma ventana de POV, cortada
                recursos.Add(ini);
            }
        }
        return Ritmo.Medir(duracion, cortes, recursos, musica, narracion, palabras, hablado);
    }
}

// ---- src/comun/Anclas.cs ----

// =====================================================================
// Marcadores anclados a los clips
//
// Vegas pone los marcadores en la linea de tiempo, no en los clips. Para que
// sigan a su clip, al crearlos se guarda en <proyecto>.vegascut-marcas.json
// a que archivo y a que segundo de ese archivo corresponden. Despues, el
// script ReubicarMarcadores busca el clip que tiene ese segundo y vuelve a
// poner el marcador encima, aunque hayas movido, cortado o reordenado clips.
// =====================================================================

public class Ancla
{
    public string Etiqueta = "", Media = "", MediaFin = "";
    public double Fuente, FuenteFin = -1;   // segundos dentro del archivo
    public bool Region;
}

public static class Anclas
{
    const double Tol = 0.0005;

    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-marcas.json");
    }

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Clip de video (si no hay, de audio) que esta en el instante t, con un
    // archivo real (no textos ni colores generados).
    static TrackEvent ClipEn(Project p, double t)
    {
        foreach (bool video in new bool[] { true, false })
            foreach (Track pista in p.Tracks)
            {
                if (pista.IsAudio() == video) continue;
                foreach (TrackEvent e in pista.Events)
                {
                    if (S(e.Start) > t + Tol || S(e.End) <= t + Tol) continue;
                    Take toma = e.ActiveTake;
                    if (toma == null || toma.Media == null || toma.Media.IsGenerated()) continue;
                    return e;
                }
            }
        return null;
    }

    static bool Fuente(Project p, double t, out string media, out double fuente)
    {
        media = ""; fuente = 0;
        TrackEvent e = ClipEn(p, t);
        if (e == null) return false;
        media = e.ActiveTake.Media.FilePath;
        fuente = S(e.ActiveTake.Offset) + (t - S(e.Start)) * e.PlaybackRate;
        return true;
    }

    // Crea el ancla de un marcador (fin < 0) o de una region.
    public static Ancla Crear(Project p, double t, double fin, string etiqueta)
    {
        Ancla a = new Ancla();
        a.Etiqueta = etiqueta;
        a.Region = fin >= 0;
        if (!Fuente(p, t, out a.Media, out a.Fuente)) return null;
        if (a.Region && !Fuente(p, Math.Max(t, fin - 0.001), out a.MediaFin, out a.FuenteFin)) a.FuenteFin = -1;
        return a;
    }

    public static List<Ancla> Cargar(string veg)
    {
        List<Ancla> r = new List<Ancla>();
        string ruta = RutaPara(veg);
        if (ruta == null || !File.Exists(ruta)) return r;
        try
        {
            foreach (object x in Json.Lista(Json.Leer(File.ReadAllText(ruta, Encoding.UTF8)), "anclas"))
            {
                Ancla a = new Ancla();
                a.Etiqueta = Json.Texto(x, "etiqueta");
                a.Media = Json.Texto(x, "media");
                a.MediaFin = Json.Texto(x, "mediaFin");
                a.Fuente = Json.Numero(x, "fuente", 0);
                a.FuenteFin = Json.Numero(x, "fuenteFin", -1);
                a.Region = Json.Texto(x, "region") == "True";
                r.Add(a);
            }
        }
        catch { }
        return r;
    }

    // Agrega anclas nuevas (reemplaza las que tengan la misma etiqueta).
    public static void Guardar(string veg, List<Ancla> nuevas)
    {
        string ruta = RutaPara(veg);
        if (ruta == null) return;
        List<Ancla> todas = Cargar(veg);
        foreach (Ancla n in nuevas)
        {
            if (n == null) continue;
            todas.RemoveAll(delegate (Ancla a) { return a.Etiqueta == n.Etiqueta && a.Region == n.Region; });
            todas.Add(n);
        }
        List<object> lista = new List<object>();
        foreach (Ancla a in todas)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["etiqueta"] = a.Etiqueta; d["region"] = a.Region;
            d["media"] = a.Media; d["fuente"] = Math.Round(a.Fuente, 3);
            if (a.Region) { d["mediaFin"] = a.MediaFin; d["fuenteFin"] = Math.Round(a.FuenteFin, 3); }
            lista.Add(d);
        }
        Dictionary<string, object> raiz = new Dictionary<string, object>();
        raiz["formato"] = "vegas-cut-marcas";
        raiz["anclas"] = lista;
        try { File.WriteAllText(ruta, Json.Escribir(raiz), new UTF8Encoding(false)); } catch { }
    }

    // Instantes de la linea de tiempo donde se ve ese segundo del archivo
    // (puede haber varios si el clip esta repetido).
    static List<double> Donde(Project p, string media, double fuente)
    {
        List<double> r = new List<double>();
        foreach (Track pista in p.Tracks)
            foreach (TrackEvent e in pista.Events)
            {
                Take toma = e.ActiveTake;
                if (toma == null || toma.Media == null || !String.Equals(toma.Media.FilePath, media, StringComparison.OrdinalIgnoreCase)) continue;
                double desde = S(toma.Offset), largo = (S(e.End) - S(e.Start)) * e.PlaybackRate;
                if (fuente >= desde - Tol && fuente < desde + largo - Tol)
                    r.Add(S(e.Start) + (fuente - desde) / e.PlaybackRate);
            }
        return r;
    }

    static double MasCerca(List<double> l, double t)
    {
        double mejor = l[0];
        foreach (double x in l) if (Math.Abs(x - t) < Math.Abs(mejor - t)) mejor = x;
        return mejor;
    }

    // Vuelve a poner cada marcador/region anclado sobre su clip. Devuelve
    // cuantos se movieron; "perdidos" son los que ya no tienen clip (esa
    // parte se borro).
    public static int Reubicar(Project p, string veg, out int perdidos, out int revisados)
    {
        perdidos = 0; revisados = 0;
        List<Ancla> anclas = Cargar(veg);
        Dictionary<string, Ancla> marcas = new Dictionary<string, Ancla>(), regiones = new Dictionary<string, Ancla>();
        foreach (Ancla a in anclas) (a.Region ? regiones : marcas)[a.Etiqueta] = a;
        int movidos = 0;

        List<Marker> lista = new List<Marker>();
        foreach (Marker m in p.Markers) lista.Add(m);
        foreach (Region r in p.Regions) lista.Add(r);
        foreach (Marker m in lista)
        {
            Region region = m as Region;
            Ancla a;
            if (!(region != null ? regiones : marcas).TryGetValue(m.Label ?? "", out a)) continue;
            revisados++;
            List<double> donde = Donde(p, a.Media, a.Fuente);
            if (donde.Count == 0) { perdidos++; continue; }
            double actual = S(m.Position), nuevo = MasCerca(donde, actual);
            bool cambio = Math.Abs(nuevo - actual) > 0.001;
            if (region != null && a.FuenteFin >= 0)
            {
                List<double> fines = Donde(p, a.MediaFin, a.FuenteFin);
                if (fines.Count > 0)
                {
                    double fin = MasCerca(fines, nuevo + S(region.Length));
                    if (fin > nuevo + 0.01 && Math.Abs((fin - nuevo) - S(region.Length)) > 0.001)
                    {
                        try { region.Length = Timecode.FromMilliseconds((fin - nuevo) * 1000); cambio = true; } catch { }
                    }
                }
            }
            if (Math.Abs(nuevo - actual) > 0.001)
            {
                try { m.Position = Timecode.FromMilliseconds(nuevo * 1000); } catch { }
            }
            if (cambio) movidos++;
        }
        return movidos;
    }
}

// ---- src/comun/Audio.cs ----

// =====================================================================
// Analisis de audio y deteccion (sin dependencias de Vegas)
// =====================================================================

public struct Rango
{
    public double Inicio, Fin; // segundos en la linea de tiempo
    public Rango(double inicio, double fin) { Inicio = inicio; Fin = fin; }
}

// Tramo que se reproduce mas rapido (Factor 2 = el doble de rapido).
public class Acelerado
{
    public double Inicio, Fin, Factor;
    public Acelerado(double inicio, double fin, double factor) { Inicio = inicio; Fin = fin; Factor = factor; }

    public double Ahorro { get { return (Fin - Inicio) * (1 - 1 / Factor); } }

    // Nueva posicion de un instante despues de acelerar los tramos.
    public static double Posicion(double t, List<Acelerado> tramos)
    {
        double ahorro = 0;
        foreach (Acelerado a in tramos)
        {
            if (t >= a.Fin - 1e-6) ahorro += a.Ahorro;
            else if (t > a.Inicio) ahorro += (t - a.Inicio) * (1 - 1 / a.Factor);
        }
        return t - ahorro;
    }
}

public class Analisis
{
    public const double Paso = 0.01;  // 10 ms por medicion
    public float[] Db;                // nivel RMS de cada paso, en dBFS
    public double Inicio;             // segundo de la linea de tiempo del primer paso
    public double Duracion { get { return Db.Length * Paso; } }

    // Une varias pistas: en cada instante cuenta la que suene mas fuerte, asi
    // hay voz si habla cualquiera de ellas.
    public static Analisis Combinar(List<Analisis> pistas)
    {
        if (pistas.Count == 1) return pistas[0];
        int n = int.MaxValue;
        foreach (Analisis a in pistas) n = Math.Min(n, a.Db.Length);
        Analisis r = new Analisis();
        r.Inicio = pistas[0].Inicio;
        r.Db = new float[n];
        for (int i = 0; i < n; i++)
        {
            float m = -100;
            foreach (Analisis a in pistas) if (a.Db[i] > m) m = a.Db[i];
            r.Db[i] = m;
        }
        return r;
    }
}

public static class WavNiveles
{
    public static Analisis Leer(string ruta, double paso)
    {
        using (FileStream fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        using (BinaryReader br = new BinaryReader(fs))
        {
            if (new string(br.ReadChars(4)) != "RIFF") throw new Exception("El archivo no es WAV.");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new Exception("El archivo no es WAV.");

            int formato = 0, canales = 0, frecuencia = 0, bits = 0;
            long datos = -1, largo = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(br.ReadChars(4));
                long tam = br.ReadUInt32();
                long siguiente = fs.Position + tam + (tam & 1);
                if (id == "fmt ")
                {
                    formato = br.ReadUInt16();
                    canales = br.ReadUInt16();
                    frecuencia = br.ReadInt32();
                    br.ReadInt32(); br.ReadUInt16();
                    bits = br.ReadUInt16();
                    if (formato == 0xFFFE && tam >= 40)
                    {
                        br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32();
                        formato = br.ReadUInt16(); // subformato: 1 PCM, 3 float
                    }
                }
                else if (id == "data")
                {
                    datos = fs.Position;
                    largo = Math.Min(tam, fs.Length - datos);
                    break;
                }
                fs.Position = siguiente;
            }
            if (datos < 0 || canales == 0) throw new Exception("WAV sin datos de audio.");
            if (!(formato == 1 && (bits == 16 || bits == 24 || bits == 32)) && !(formato == 3 && bits == 32))
                throw new Exception("Formato WAV no soportado (" + formato + ", " + bits + " bits).");

            int bytesMuestra = bits / 8;
            int bytesCuadro = bytesMuestra * canales;
            int cuadrosPorPaso = Math.Max(1, (int)Math.Round(frecuencia * paso));
            long cuadros = largo / bytesCuadro;
            int pasos = (int)(cuadros / cuadrosPorPaso);
            float[] db = new float[pasos];

            byte[] buf = new byte[cuadrosPorPaso * bytesCuadro];
            fs.Position = datos;
            for (int p = 0; p < pasos; p++)
            {
                int leidos = 0;
                while (leidos < buf.Length)
                {
                    int n = fs.Read(buf, leidos, buf.Length - leidos);
                    if (n <= 0) break;
                    leidos += n;
                }
                double suma = 0;
                int muestras = leidos / bytesMuestra;
                for (int i = 0; i < muestras; i++)
                {
                    int o = i * bytesMuestra;
                    double x;
                    if (formato == 3) x = BitConverter.ToSingle(buf, o);
                    else if (bits == 16) x = BitConverter.ToInt16(buf, o) / 32768.0;
                    else if (bits == 24) x = ((buf[o] | (buf[o + 1] << 8) | ((sbyte)buf[o + 2] << 16))) / 8388608.0;
                    else x = BitConverter.ToInt32(buf, o) / 2147483648.0;
                    suma += x * x;
                }
                double rms = muestras > 0 ? Math.Sqrt(suma / muestras) : 0;
                db[p] = rms > 1e-5 ? (float)(20 * Math.Log10(rms)) : -100f;
            }

            Analisis a = new Analisis();
            a.Db = db;
            return a;
        }
    }
}


public static class Formato
{
    public static string Tiempo(double s)
    {
        if (s < 0) s = 0;
        int t = (int)Math.Round(s);
        if (t >= 3600) return (t / 3600) + ":" + ((t / 60) % 60).ToString("00") + ":" + (t % 60).ToString("00");
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    // Con decimas: 1:02.5
    public static string TiempoPreciso(double s)
    {
        if (s < 0) s = 0;
        int d = (int)Math.Round(s * 10);
        int t = d / 10;
        string r = ((t / 60) % 60).ToString(t >= 3600 ? "00" : "0") + ":" + (t % 60).ToString("00") + "." + (d % 10);
        return t >= 3600 ? (t / 3600) + ":" + r : r;
    }
}

// ---- src/comun/Json.cs ----

// =====================================================================
// JSON minimo (Vegas no trae una libreria de JSON)
//   Objeto -> Dictionary<string, object>, lista -> List<object>,
//   numero -> double, texto -> string, true/false -> bool, null -> null.
// =====================================================================

public static class Json
{
    // ------------------------------------------------------------ escribir

    public static string Escribir(object valor) { return Escribir(valor, true); }

    public static string Escribir(object valor, bool sangria)
    {
        StringBuilder sb = new StringBuilder();
        Valor(sb, valor, 0, sangria);
        return sb.ToString();
    }

    static void Valor(StringBuilder sb, object v, int nivel, bool sangria)
    {
        if (v == null) { sb.Append("null"); return; }
        if (v is string) { Cadena(sb, (string)v); return; }
        if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
        if (v is double || v is float || v is decimal)
        {
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (Double.IsNaN(d) || Double.IsInfinity(d)) sb.Append("null");
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            return;
        }
        if (v.GetType().IsPrimitive) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
        if (v is Enum) { Cadena(sb, v.ToString()); return; }

        IDictionary dic = v as IDictionary;
        if (dic != null)
        {
            if (dic.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool primero = true;
            foreach (DictionaryEntry e in dic)
            {
                if (!primero) sb.Append(',');
                primero = false;
                Salto(sb, nivel + 1, sangria);
                Cadena(sb, e.Key.ToString());
                sb.Append(sangria ? ": " : ":");
                Valor(sb, e.Value, nivel + 1, sangria);
            }
            Salto(sb, nivel, sangria);
            sb.Append('}');
            return;
        }

        IEnumerable lista = v as IEnumerable;
        if (lista != null)
        {
            // Listas de numeros en una sola linea (niveles de sonido): mucho mas compacto.
            bool simple = true, vacia = true;
            foreach (object o in lista) { vacia = false; if (o is IDictionary || (o is IEnumerable && !(o is string))) { simple = false; break; } }
            if (vacia) { sb.Append("[]"); return; }
            sb.Append('[');
            bool primero = true;
            foreach (object o in lista)
            {
                if (!primero) sb.Append(',');
                primero = false;
                if (!simple) Salto(sb, nivel + 1, sangria);
                Valor(sb, o, nivel + 1, sangria);
            }
            if (!simple) Salto(sb, nivel, sangria);
            sb.Append(']');
            return;
        }

        Cadena(sb, v.ToString());
    }

    static void Salto(StringBuilder sb, int nivel, bool sangria)
    {
        if (!sangria) return;
        sb.Append('\n');
        sb.Append(' ', nivel * 2);
    }

    static void Cadena(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // --------------------------------------------------------------- leer

    public static object Leer(string texto)
    {
        int i = 0;
        object v = LeerValor(texto, ref i);
        Espacios(texto, ref i);
        if (i < texto.Length) throw new FormatException("JSON con texto de sobra en la posici\u00f3n " + i);
        return v;
    }

    static void Espacios(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static Exception Error(string s, int i, string que)
    {
        return new FormatException("JSON inv\u00e1lido (" + que + ") en la posici\u00f3n " + i);
    }

    static object LeerValor(string s, ref int i)
    {
        Espacios(s, ref i);
        if (i >= s.Length) throw Error(s, i, "fin inesperado");
        char c = s[i];
        if (c == '{') return LeerObjeto(s, ref i);
        if (c == '[') return LeerLista(s, ref i);
        if (c == '"') return LeerCadena(s, ref i);
        if (c == 't' && Sigue(s, i, "true")) { i += 4; return true; }
        if (c == 'f' && Sigue(s, i, "false")) { i += 5; return false; }
        if (c == 'n' && Sigue(s, i, "null")) { i += 4; return null; }
        if (c == '-' || char.IsDigit(c)) return LeerNumero(s, ref i);
        throw Error(s, i, "car\u00e1cter '" + c + "'");
    }

    static bool Sigue(string s, int i, string palabra)
    {
        return String.CompareOrdinal(s, i, palabra, 0, palabra.Length) == 0;
    }

    static Dictionary<string, object> LeerObjeto(string s, ref int i)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        i++; // {
        Espacios(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (true)
        {
            Espacios(s, ref i);
            if (i >= s.Length || s[i] != '"') throw Error(s, i, "se esperaba una clave");
            string clave = LeerCadena(s, ref i);
            Espacios(s, ref i);
            if (i >= s.Length || s[i] != ':') throw Error(s, i, "se esperaba ':'");
            i++;
            d[clave] = LeerValor(s, ref i);
            Espacios(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; return d; }
            throw Error(s, i, "se esperaba ',' o '}'");
        }
    }

    static List<object> LeerLista(string s, ref int i)
    {
        List<object> l = new List<object>();
        i++; // [
        Espacios(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return l; }
        while (true)
        {
            l.Add(LeerValor(s, ref i));
            Espacios(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; return l; }
            throw Error(s, i, "se esperaba ',' o ']'");
        }
    }

    static string LeerCadena(string s, ref int i)
    {
        StringBuilder sb = new StringBuilder();
        i++; // "
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 > s.Length) throw Error(s, i, "escape \\u incompleto");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break; // \" \\ \/
            }
        }
        throw Error(s, i, "texto sin cerrar");
    }

    static double LeerNumero(string s, ref int i)
    {
        int ini = i;
        if (s[i] == '-') i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
        return double.Parse(s.Substring(ini, i - ini), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // ----------------------------------------------------- acceso comodo

    public static Dictionary<string, object> Obj(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        return d != null && d.TryGetValue(clave, out v) ? v as Dictionary<string, object> : null;
    }

    // El valor tal cual (o null): para booleanos o lo que pueda faltar.
    public static object Valor(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        return d != null && d.TryGetValue(clave, out v) ? v : null;
    }

    public static List<object> Lista(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        List<object> l = d != null && d.TryGetValue(clave, out v) ? v as List<object> : null;
        return l ?? new List<object>();
    }

    public static string Texto(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        if (d == null || !d.TryGetValue(clave, out v) || v == null) return "";
        return v is string ? (string)v : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public static double Numero(object o, string clave, double siNo)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        if (d == null || !d.TryGetValue(clave, out v) || v == null) return siNo;
        if (v is double) return (double)v;
        double r;
        return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out r) ? r : siNo;
    }
}

// ---- src/comun/Transcripcion.cs ----

// =====================================================================
// Transcripcion del proyecto (<proyecto>.vegascut.json junto al .veg)
//
// Guarda los tiempos tal como estaban al transcribir y una lista de los
// cortes que hicieron las herramientas despues. Asi los tiempos se pueden
// llevar a la linea de tiempo actual, y si deshaces un corte (Ctrl+Z) se
// nota porque la duracion del proyecto vuelve a la de antes.
// =====================================================================

public class Palabra
{
    public double Inicio, Fin, Prob;
    public string Texto;
}

public class Segmento
{
    public int Hablante;
    public double Inicio, Fin;
    public string Texto;
    public List<Palabra> Palabras = new List<Palabra>();
}

public class Hablante
{
    public string Etiqueta;  // "A11"
    public string Nombre;    // como se llama la persona (editable)
    public string Archivo;
    public bool Voz;         // true: se transcribio; false: solo niveles (juego, musica)
    public float[] Nivel;    // dB RMS por segundo
    public float[] Pico;     // dB maximo por segundo
    public List<Fuente> Fuentes = new List<Fuente>(); // de donde salia cada parte al transcribir
}

// Un evento de la pista al transcribir: que archivo (y flujo de audio) sonaba
// de Inicio a Fin y desde que segundo del archivo. Con esto una palabra se
// puede encontrar en la linea de tiempo aunque despues edites a mano.
public class Fuente
{
    public double Inicio, Fin, Desde, Velocidad = 1;
    public string Media = "";
    public int Flujo;
}

public class Edicion
{
    public List<Rango> Quitados = new List<Rango>();
    public List<Acelerado> Acelerados = new List<Acelerado>();
    public double Antes, Despues; // duracion del proyecto
}

public class Transcripcion
{
    public string Proyecto = "", Creada = "", Idioma = "es", Modelo = "";
    public double Inicio, Duracion;      // rango transcrito (tiempos originales)
    public double DuracionProyecto;      // al transcribir
    public List<Hablante> Hablantes = new List<Hablante>();
    public List<Segmento> Segmentos = new List<Segmento>();
    public List<Edicion> Ediciones = new List<Edicion>();

    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut.json");
    }

    // --------------------------------------------------- tiempos actuales

    // Si esta puesto, lleva (hablante, tiempo original) a la linea de tiempo
    // actual buscando el archivo y segundo de cada palabra (fuentes): asi la
    // transcripcion sigue cualquier edicion, tambien las hechas a mano. Lo
    // ponen las herramientas que tienen el proyecto de Vegas a mano.
    public Func<int, double, double> Ubicador;

    public double Mapear(int hablante, double t)
    {
        if (Ubicador != null && hablante >= 0 && hablante < Hablantes.Count && Hablantes[hablante].Fuentes.Count > 0)
            return Ubicador(hablante, t);
        return Mapear(t);
    }

    // Lleva un tiempo original a la linea de tiempo actual. Devuelve NaN si
    // ese instante fue cortado.
    public double Mapear(double t)
    {
        foreach (Edicion e in Ediciones)
        {
            if (e.Acelerados.Count > 0) t = Acelerado.Posicion(t, e.Acelerados);
            double q = 0;
            foreach (Rango r in e.Quitados)
            {
                if (t >= r.Fin - 1e-6) q += r.Fin - r.Inicio;
                else if (t > r.Inicio + 1e-6) return double.NaN;
            }
            t -= q;
        }
        return t;
    }

    // Archivo, flujo y segundo del archivo que sonaba en el instante
    // original t en la pista de ese hablante.
    public bool AFuente(int hablante, double t, out Fuente f, out double segundo)
    {
        f = null; segundo = 0;
        if (hablante < 0 || hablante >= Hablantes.Count) return false;
        foreach (Fuente x in Hablantes[hablante].Fuentes)
            if (t >= x.Inicio - 1e-6 && t < x.Fin - 1e-6)
            {
                f = x;
                segundo = x.Desde + (t - x.Inicio) * x.Velocidad;
                return true;
            }
        return false;
    }

    // Frases que Whisper inventa en los silencios (vienen de los subtitulos de
    // YouTube con los que se entreno). No se le mandan a la IA.
    static readonly string[] Inventadas = { "suscribeteacanal", "suscribeteanuestrocanal", "suscribete", "graciasporver",
        "subtitulosrealizadosporlacomunidaddeamaraorg", "subtitulosporlacomunidaddeamaraorg", "amaraorg",
        "noolvidesdesuscribirte", "dalelike" };

    public static bool Alucinacion(string texto)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (texto ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(c);
        string n = sb.ToString().Replace("suscribetealcanal", "suscribeteacanal");
        if (n.Length == 0) return false;
        foreach (string x in Inventadas)
            if (n == x || (x.Length >= 10 && n.Contains(x) && n.Length <= x.Length + 12)) return true;
        return false;
    }

    public bool TieneFuentes
    {
        get { foreach (Hablante h in Hablantes) if (h.Fuentes.Count > 0) return true; return false; }
    }

    // Segmentos con tiempos de la linea de tiempo actual, sin lo cortado.
    public List<Segmento> SegmentosActuales()
    {
        List<Segmento> r = new List<Segmento>();
        foreach (Segmento s in Segmentos)
        {
            if (Alucinacion(s.Texto)) continue;
            Segmento n = new Segmento();
            n.Hablante = s.Hablante;
            StringBuilder texto = new StringBuilder();
            if (s.Palabras.Count > 0)
            {
                foreach (Palabra p in s.Palabras)
                {
                    double a = Mapear(s.Hablante, p.Inicio), b = Mapear(s.Hablante, p.Fin);
                    if (double.IsNaN(a) && double.IsNaN(b)) continue;
                    if (double.IsNaN(a)) a = b - Math.Min(0.2, p.Fin - p.Inicio);
                    if (double.IsNaN(b)) b = a + Math.Min(0.2, p.Fin - p.Inicio);
                    Palabra q = new Palabra();
                    q.Inicio = a; q.Fin = b; q.Prob = p.Prob; q.Texto = p.Texto;
                    n.Palabras.Add(q);
                    texto.Append(p.Texto);
                }
                if (n.Palabras.Count == 0) continue;
                n.Inicio = n.Palabras[0].Inicio;
                n.Fin = n.Palabras[n.Palabras.Count - 1].Fin;
                n.Texto = texto.ToString().Trim();
            }
            else
            {
                n.Inicio = Mapear(s.Hablante, s.Inicio); n.Fin = Mapear(s.Hablante, s.Fin); n.Texto = s.Texto;
                if (double.IsNaN(n.Inicio) || double.IsNaN(n.Fin)) continue;
            }
            r.Add(n);
        }
        r.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
        return r;
    }

    public double DuracionEsperada
    {
        get { return Ediciones.Count > 0 ? Ediciones[Ediciones.Count - 1].Despues : DuracionProyecto; }
    }

    // Compara con la duracion actual del proyecto. Si deshiciste cortes
    // (Ctrl+Z), los quita de la lista. Devuelve "" si todo cuadra o un aviso.
    public string Sincronizar(double duracionActual)
    {
        const double tol = 0.05;
        bool cambio = false;
        while (Ediciones.Count > 0 && Math.Abs(DuracionEsperada - duracionActual) > tol &&
               Math.Abs(Ediciones[Ediciones.Count - 1].Antes - duracionActual) <= tol)
        {
            Ediciones.RemoveAt(Ediciones.Count - 1);
            cambio = true;
        }
        if (Math.Abs(DuracionEsperada - duracionActual) <= tol)
            return cambio ? "Se detect\u00f3 un Ctrl+Z: la transcripci\u00f3n se ajust\u00f3." : "";
        return "El proyecto cambi\u00f3 desde la transcripci\u00f3n (dura " + Formato.Tiempo(duracionActual) +
               ", se esperaba " + Formato.Tiempo(DuracionEsperada) + "). Si editaste a mano, los tiempos " +
               "pueden no cuadrar: vuelve a transcribir para m\u00e1s precisi\u00f3n.";
    }

    // La llaman las herramientas que cortan (Quitar silencios, Momentos).
    public static string RegistrarCortes(string veg, List<Rango> quitados, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Quitados.AddRange(quitados);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    public static string RegistrarAceleracion(string veg, List<Acelerado> tramos, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Acelerados.AddRange(tramos);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    static string RegistrarEdicion(string veg, Edicion e)
    {
        string ruta = RutaPara(veg);
        if (ruta == null || !File.Exists(ruta)) return "";
        try
        {
            Transcripcion t = Cargar(ruta);
            t.Sincronizar(e.Antes);
            t.Ediciones.Add(e);
            t.Guardar(ruta);
            return "\nLa transcripci\u00f3n tambi\u00e9n se ajust\u00f3 a los cortes.";
        }
        catch (Exception ex)
        {
            return "\nNo se pudo ajustar la transcripci\u00f3n: " + ex.Message;
        }
    }

    // ------------------------------------------------- niveles por segundo

    public static void NivelesPorSegundo(Analisis a, out float[] nivel, out float[] pico)
    {
        int porSegundo = (int)Math.Round(1 / Analisis.Paso);
        int n = (a.Db.Length + porSegundo - 1) / porSegundo;
        nivel = new float[n];
        pico = new float[n];
        for (int s = 0; s < n; s++)
        {
            double energia = 0;
            float max = -100;
            int cuantos = 0;
            for (int i = s * porSegundo; i < Math.Min(a.Db.Length, (s + 1) * porSegundo); i++)
            {
                energia += Math.Pow(10, a.Db[i] / 10.0);
                if (a.Db[i] > max) max = a.Db[i];
                cuantos++;
            }
            nivel[s] = cuantos > 0 ? (float)Math.Round(10 * Math.Log10(Math.Max(1e-10, energia / cuantos))) : -100;
            pico[s] = (float)Math.Round(max);
        }
    }

    // ------------------------------------------------------ Whisper (JSON)

    // Agrega los segmentos de la salida JSON de Whisper (tiempos relativos al
    // WAV) desplazados al inicio del rango.
    public void AgregarWhisper(string json, int hablante, double desplazamiento)
    {
        object o = Json.Leer(json);
        foreach (object s in Json.Lista(o, "segments"))
        {
            Segmento seg = new Segmento();
            seg.Hablante = hablante;
            seg.Inicio = Json.Numero(s, "start", 0) + desplazamiento;
            seg.Fin = Json.Numero(s, "end", 0) + desplazamiento;
            seg.Texto = Json.Texto(s, "text").Trim();
            foreach (object w in Json.Lista(s, "words"))
            {
                Palabra p = new Palabra();
                p.Inicio = Json.Numero(w, "start", seg.Inicio - desplazamiento) + desplazamiento;
                p.Fin = Json.Numero(w, "end", seg.Fin - desplazamiento) + desplazamiento;
                p.Prob = Json.Numero(w, "probability", Json.Numero(w, "prob", 1));
                p.Texto = Json.Texto(w, "word");
                if (p.Texto.Length == 0) p.Texto = Json.Texto(w, "text");
                seg.Palabras.Add(p);
            }
            if (seg.Texto.Length > 0 || seg.Palabras.Count > 0) Segmentos.Add(seg);
        }
        Segmentos.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
    }

    // ---------------------------------------------------- guardar / cargar

    static double R(double v) { return Math.Round(v, 3); }

    public void Guardar(string ruta)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-transcripcion";
        d["version"] = 1;
        d["proyecto"] = Proyecto;
        d["creada"] = Creada;
        d["idioma"] = Idioma;
        d["modelo"] = Modelo;
        d["inicio"] = R(Inicio);
        d["duracion"] = R(Duracion);
        d["duracionProyecto"] = R(DuracionProyecto);

        List<object> hs = new List<object>();
        foreach (Hablante h in Hablantes)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["etiqueta"] = h.Etiqueta;
            x["nombre"] = h.Nombre;
            x["archivo"] = h.Archivo;
            x["voz"] = h.Voz;
            x["nivel"] = h.Nivel ?? new float[0];
            x["pico"] = h.Pico ?? new float[0];
            List<object> fs = new List<object>();
            // Fuentes compactas: [inicio, fin, desde, velocidad, flujo, archivo]
            foreach (Fuente f in h.Fuentes)
                fs.Add(new List<object> { R(f.Inicio), R(f.Fin), R(f.Desde), Math.Round(f.Velocidad, 4), f.Flujo, f.Media });
            x["fuentes"] = fs;
            hs.Add(x);
        }
        d["hablantes"] = hs;

        List<object> ss = new List<object>();
        foreach (Segmento s in Segmentos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["h"] = s.Hablante;
            x["inicio"] = R(s.Inicio);
            x["fin"] = R(s.Fin);
            x["texto"] = s.Texto;
            List<object> ps = new List<object>();
            // Palabras compactas: [inicio, fin, probabilidad, texto]
            foreach (Palabra p in s.Palabras)
                ps.Add(new List<object> { R(p.Inicio), R(p.Fin), Math.Round(p.Prob, 2), p.Texto });
            x["palabras"] = ps;
            ss.Add(x);
        }
        d["segmentos"] = ss;

        List<object> es = new List<object>();
        foreach (Edicion e in Ediciones)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["antes"] = R(e.Antes);
            x["despues"] = R(e.Despues);
            List<object> qs = new List<object>();
            foreach (Rango r in e.Quitados) qs.Add(new List<object> { R(r.Inicio), R(r.Fin) });
            x["quitados"] = qs;
            if (e.Acelerados.Count > 0)
            {
                List<object> acs = new List<object>();
                foreach (Acelerado a in e.Acelerados) acs.Add(new List<object> { R(a.Inicio), R(a.Fin), a.Factor });
                x["acelerados"] = acs;
            }
            es.Add(x);
        }
        d["ediciones"] = es;

        File.WriteAllText(ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    public static Transcripcion Cargar(string ruta)
    {
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        if (Json.Texto(o, "formato") != "vegas-cut-transcripcion")
            throw new Exception("El archivo no es una transcripci\u00f3n de vegas-cut.");
        Transcripcion t = new Transcripcion();
        t.Proyecto = Json.Texto(o, "proyecto");
        t.Creada = Json.Texto(o, "creada");
        t.Idioma = Json.Texto(o, "idioma");
        t.Modelo = Json.Texto(o, "modelo");
        t.Inicio = Json.Numero(o, "inicio", 0);
        t.Duracion = Json.Numero(o, "duracion", 0);
        t.DuracionProyecto = Json.Numero(o, "duracionProyecto", 0);

        foreach (object x in Json.Lista(o, "hablantes"))
        {
            Hablante h = new Hablante();
            h.Etiqueta = Json.Texto(x, "etiqueta");
            h.Nombre = Json.Texto(x, "nombre");
            h.Archivo = Json.Texto(x, "archivo");
            object voz;
            Dictionary<string, object> dx = (Dictionary<string, object>)x;
            h.Voz = dx.TryGetValue("voz", out voz) && voz is bool && (bool)voz;
            h.Nivel = Numeros(Json.Lista(x, "nivel"));
            h.Pico = Numeros(Json.Lista(x, "pico"));
            foreach (object q in Json.Lista(x, "fuentes"))
            {
                List<object> l = q as List<object>;
                if (l == null || l.Count < 6) continue;
                Fuente f = new Fuente();
                f.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                f.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                f.Desde = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                f.Velocidad = Convert.ToDouble(l[3], CultureInfo.InvariantCulture);
                f.Flujo = Convert.ToInt32(l[4], CultureInfo.InvariantCulture);
                f.Media = l[5] as string ?? "";
                h.Fuentes.Add(f);
            }
            t.Hablantes.Add(h);
        }

        foreach (object x in Json.Lista(o, "segmentos"))
        {
            Segmento s = new Segmento();
            s.Hablante = (int)Json.Numero(x, "h", 0);
            s.Inicio = Json.Numero(x, "inicio", 0);
            s.Fin = Json.Numero(x, "fin", 0);
            s.Texto = Json.Texto(x, "texto");
            foreach (object p in Json.Lista(x, "palabras"))
            {
                List<object> l = p as List<object>;
                if (l == null || l.Count < 4) continue;
                Palabra w = new Palabra();
                w.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                w.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                w.Prob = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                w.Texto = l[3] as string ?? "";
                s.Palabras.Add(w);
            }
            t.Segmentos.Add(s);
        }

        foreach (object x in Json.Lista(o, "ediciones"))
        {
            Edicion e = new Edicion();
            e.Antes = Json.Numero(x, "antes", 0);
            e.Despues = Json.Numero(x, "despues", 0);
            foreach (object q in Json.Lista(x, "quitados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 2)
                    e.Quitados.Add(new Rango(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                             Convert.ToDouble(l[1], CultureInfo.InvariantCulture)));
            }
            foreach (object q in Json.Lista(x, "acelerados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 3)
                    e.Acelerados.Add(new Acelerado(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[1], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[2], CultureInfo.InvariantCulture)));
            }
            t.Ediciones.Add(e);
        }
        return t;
    }

    static float[] Numeros(List<object> l)
    {
        float[] r = new float[l.Count];
        for (int i = 0; i < l.Count; i++) r[i] = (float)Convert.ToDouble(l[i], CultureInfo.InvariantCulture);
        return r;
    }
}

// ---- src/silencios/Deteccion.cs ----

public enum Modo { Eliminar, DejarHuecos, Silenciar, Marcar }

// Valores de deteccion. Un perfil es un conjunto de estos valores con nombre.
public class Valores
{
    public int SilencioMinMs = 500;   // solo se quitan pausas mas largas
    public int HablaMinMs = 150;      // sonidos mas cortos no cuentan como voz
    public int MargenAntesMs = 150;   // pausa que queda antes de hablar
    public int MargenDespuesMs = 250; // pausa que queda al terminar de hablar
    public int PedazoMinMs = 800;     // no deja clips mas cortos que esto
    public int SuavizadoMs = 20;      // fundido del audio en cada corte
    public int Sensibilidad = 0;      // dB que se suman al umbral de cada pista

    public bool Igual(Valores o)
    {
        return SilencioMinMs == o.SilencioMinMs && HablaMinMs == o.HablaMinMs &&
               MargenAntesMs == o.MargenAntesMs && MargenDespuesMs == o.MargenDespuesMs &&
               PedazoMinMs == o.PedazoMinMs && SuavizadoMs == o.SuavizadoMs && Sensibilidad == o.Sensibilidad;
    }

    public void CopiarDe(Valores o)
    {
        SilencioMinMs = o.SilencioMinMs; HablaMinMs = o.HablaMinMs;
        MargenAntesMs = o.MargenAntesMs; MargenDespuesMs = o.MargenDespuesMs;
        PedazoMinMs = o.PedazoMinMs; SuavizadoMs = o.SuavizadoMs; Sensibilidad = o.Sensibilidad;
    }

    public string Texto()
    {
        return "silencioMin=" + SilencioMinMs + "\n" + "hablaMin=" + HablaMinMs + "\n" +
               "margenAntes=" + MargenAntesMs + "\n" + "margenDespues=" + MargenDespuesMs + "\n" +
               "pedazoMin=" + PedazoMinMs + "\n" + "suavizado=" + SuavizadoMs + "\n" +
               "sensibilidad=" + Sensibilidad + "\n";
    }

    // Devuelve true si la clave era de estos valores.
    public bool Leer(string k, string v)
    {
        int n;
        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
        switch (k)
        {
            case "silencioMin": SilencioMinMs = n; return true;
            case "hablaMin": HablaMinMs = n; return true;
            case "margenAntes": MargenAntesMs = n; return true;
            case "margenDespues": MargenDespuesMs = n; return true;
            case "pedazoMin": PedazoMinMs = n; return true;
            case "suavizado": SuavizadoMs = n; return true;
            case "sensibilidad": Sensibilidad = n; return true;
        }
        return false;
    }

    public static string Carpeta
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut");
        }
    }
}

public class Ajustes : Valores
{
    public Modo Modo = Modo.Eliminar;
    public bool TodasLasPistas = true;
    public string Perfil = "Narraci\u00f3n";

    static string Ruta { get { return Path.Combine(Carpeta, "silencios.ini"); } }

    public static Ajustes Cargar()
    {
        Ajustes a = new Ajustes();
        a.CopiarDe(Perfil_.Incluidos[0]);
        try
        {
            if (!File.Exists(Ruta)) return a;
            foreach (string linea in File.ReadAllLines(Ruta))
            {
                int i = linea.IndexOf('=');
                if (i < 0) continue;
                string k = linea.Substring(0, i).Trim(), v = linea.Substring(i + 1).Trim();
                if (a.Leer(k, v)) continue;
                switch (k)
                {
                    case "modo": a.Modo = (Modo)Enum.Parse(typeof(Modo), v); break;
                    case "todas": a.TodasLasPistas = v == "1"; break;
                    case "perfil": a.Perfil = v; break;
                }
            }
        }
        catch { }
        return a;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            File.WriteAllText(Ruta, Texto() +
                "modo=" + Modo + "\n" +
                "todas=" + (TodasLasPistas ? "1" : "0") + "\n" +
                "perfil=" + Perfil + "\n", new UTF8Encoding(false));
        }
        catch { }
    }
}

// Perfil_ (con guion bajo) para no chocar con nombres de la API de Vegas.
public class Perfil_ : Valores
{
    public string Nombre, Descripcion;
    public bool Incluido;

    static Perfil_ Nuevo(string nombre, string descripcion, int silencio, int voz, int antes, int despues,
                         int pedazo, int suavizado, int sensibilidad)
    {
        Perfil_ p = new Perfil_();
        p.Nombre = nombre; p.Descripcion = descripcion; p.Incluido = true;
        p.SilencioMinMs = silencio; p.HablaMinMs = voz; p.MargenAntesMs = antes; p.MargenDespuesMs = despues;
        p.PedazoMinMs = pedazo; p.SuavizadoMs = suavizado; p.Sensibilidad = sensibilidad;
        return p;
    }

    // Valores pensados para cada tipo de video. En tus video ensayos las pausas
    // que quitas a mano duran 1 a 1.5 s y los pedazos 4 a 7 s.
    public static readonly Perfil_[] Incluidos = new Perfil_[]
    {
        Nuevo("Narraci\u00f3n", "Voz en off y video ensayos: quita casi todas las pausas y deja la voz fluida.",
              350, 150, 100, 180, 700, 20, 0),
        Nuevo("Tutorial", "Explicaciones con pantalla: deja respirar para que se entienda cada paso.",
              600, 150, 150, 300, 1000, 25, 0),
        Nuevo("Podcast / charla", "Conversaci\u00f3n entre varios: solo quita pausas largas y conserva las reacciones.",
              900, 200, 200, 350, 1500, 30, 0),
        Nuevo("Gameplay", "Partidas con voz: quita los silencios largos, deja que el juego respire e ignora clics de teclado.",
              1200, 250, 250, 450, 2000, 30, -3),
        Nuevo("Shorts / r\u00e1pido", "Clips cortos y din\u00e1micos: corta hasta las pausas peque\u00f1as.",
              200, 100, 50, 80, 400, 15, 2),
    };

    static string Ruta { get { return Path.Combine(Carpeta, "perfiles.ini"); } }

    // Perfiles guardados por el usuario, en formato:
    //   [Nombre]
    //   silencioMin=...
    public static List<Perfil_> CargarPropios()
    {
        List<Perfil_> lista = new List<Perfil_>();
        try
        {
            if (!File.Exists(Ruta)) return lista;
            Perfil_ actual = null;
            foreach (string l in File.ReadAllLines(Ruta, Encoding.UTF8))
            {
                string linea = l.Trim();
                if (linea.StartsWith("[") && linea.EndsWith("]"))
                {
                    actual = new Perfil_();
                    actual.Nombre = linea.Substring(1, linea.Length - 2);
                    actual.Descripcion = "Perfil guardado por ti.";
                    lista.Add(actual);
                    continue;
                }
                int i = linea.IndexOf('=');
                if (actual != null && i > 0) actual.Leer(linea.Substring(0, i).Trim(), linea.Substring(i + 1).Trim());
            }
        }
        catch { }
        return lista;
    }

    public static void GuardarPropios(List<Perfil_> propios)
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            StringBuilder sb = new StringBuilder();
            foreach (Perfil_ p in propios) sb.Append("[" + p.Nombre + "]\n" + p.Texto() + "\n");
            File.WriteAllText(Ruta, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}

public static class Detector
{
    // Umbral de una pista por el metodo de Otsu: se hace un histograma de los
    // niveles (1 dB por barra) y se busca el corte que mejor separa los dos
    // grupos, ruido de fondo y voz. Asi cada pista tiene su propio umbral
    // aunque tengan volumenes distintos.
    public static double UmbralAutomatico(float[] db)
    {
        int[] h = new int[101];
        int total = 0;
        foreach (float x in db)
        {
            if (x <= -99) continue; // silencio digital
            int i = Math.Max(0, Math.Min(100, 100 + (int)Math.Round(x)));
            h[i]++;
            total++;
        }
        if (total < 50) return -40;

        double suma = 0;
        for (int i = 0; i <= 100; i++) suma += (double)i * h[i];
        double sumaFondo = 0, mejor = -1;
        long pesoFondo = 0;
        int corte = 60;
        for (int i = 0; i <= 100; i++)
        {
            pesoFondo += h[i];
            if (pesoFondo == 0) continue;
            long pesoVoz = total - pesoFondo;
            if (pesoVoz == 0) break;
            sumaFondo += (double)i * h[i];
            double mFondo = sumaFondo / pesoFondo, mVoz = (suma - sumaFondo) / pesoVoz;
            double entre = (double)pesoFondo * pesoVoz * (mFondo - mVoz) * (mFondo - mVoz);
            if (entre > mejor) { mejor = entre; corte = i; }
        }
        // Otsu solo separa los grupos; el umbral va a la mitad entre el borde
        // alto del ruido (percentil 90 del fondo) y el borde bajo de la voz
        // (percentil 20), para no quedar pegado al ruido.
        double bordeRuido = Percentil(h, 0, corte, 0.90) - 100;
        double bordeVoz = Percentil(h, corte + 1, 100, 0.20) - 100;
        double u = Math.Max(bordeRuido + 3, (bordeRuido + bordeVoz) / 2);
        return Math.Max(-70, Math.Min(-15, Math.Round(u)));
    }

    // Percentil de las barras desde..hasta del histograma (devuelve la barra).
    static int Percentil(int[] h, int desde, int hasta, double fraccion)
    {
        long total = 0;
        for (int i = desde; i <= hasta; i++) total += h[i];
        if (total == 0) return hasta;
        long objetivo = (long)Math.Ceiling(total * fraccion), acumulado = 0;
        for (int i = desde; i <= hasta; i++)
        {
            acumulado += h[i];
            if (acumulado >= objetivo) return i;
        }
        return hasta;
    }

    public static List<Rango> Detectar(Analisis a, double umbral, Valores v)
    {
        return Detectar(new List<Analisis> { a }, new List<double> { umbral }, v);
    }

    // Hay voz en un instante si cualquier pista supera su propio umbral
    // (mas la sensibilidad general).
    public static List<Rango> Detectar(List<Analisis> pistas, List<double> umbrales, Valores v)
    {
        List<Rango> resultado = new List<Rango>();
        if (pistas.Count == 0) return resultado;
        int n = int.MaxValue;
        foreach (Analisis p in pistas) n = Math.Min(n, p.Db.Length);
        if (n == 0) return resultado;
        double paso = Analisis.Paso, inicio = pistas[0].Inicio;

        bool[] hay = new bool[n];
        for (int k = 0; k < pistas.Count; k++)
        {
            float[] db = pistas[k].Db;
            double u = umbrales[k] + v.Sensibilidad;
            for (int i = 0; i < n; i++) if (db[i] >= u) hay[i] = true;
        }

        // 1. Tramos de voz.
        List<int[]> voz = new List<int[]>();
        int j = 0;
        while (j < n)
        {
            if (hay[j])
            {
                int f = j;
                while (f < n && hay[f]) f++;
                voz.Add(new int[] { j, f });
                j = f;
            }
            else j++;
        }

        // 2. Descartar voz demasiado corta (clics, respiraciones).
        int hablaMin = (int)Math.Round(v.HablaMinMs / 1000.0 / paso);
        List<int[]> vozBuena = new List<int[]>();
        foreach (int[] t in voz) if (t[1] - t[0] >= hablaMin) vozBuena.Add(t);

        // 3. Los huecos entre voz son silencios candidatos (incluye inicio y final).
        int silMin = (int)Math.Round(v.SilencioMinMs / 1000.0 / paso);
        int antes = (int)Math.Round(v.MargenAntesMs / 1000.0 / paso);
        int despues = (int)Math.Round(v.MargenDespuesMs / 1000.0 / paso);
        int cursor = 0;
        for (int k = 0; k <= vozBuena.Count; k++)
        {
            int ini = cursor;
            int fin = k < vozBuena.Count ? vozBuena[k][0] : n;
            bool alInicio = k == 0, alFinal = k == vozBuena.Count;
            if (fin - ini >= silMin)
            {
                // Margen: se deja algo de silencio despues de la voz anterior y
                // antes de la siguiente para que los cortes no suenen bruscos.
                int a0 = ini + (alInicio ? 0 : despues);
                int b0 = fin - (alFinal ? 0 : antes);
                if (b0 - a0 >= 2)
                    resultado.Add(new Rango(inicio + a0 * paso, inicio + b0 * paso));
            }
            if (k < vozBuena.Count) cursor = vozBuena[k][1];
        }

        return PedazoMinimo(resultado, inicio, inicio + n * paso, v.PedazoMinMs / 1000.0);
    }

    // Si entre dos silencios queda un clip mas corto que el minimo, no se
    // corta el segundo silencio: el clip se une con lo que sigue.
    static List<Rango> PedazoMinimo(List<Rango> rangos, double inicio, double fin, double minimo)
    {
        if (minimo <= 0) return rangos;
        List<Rango> r = new List<Rango>();
        double ultimoFin = inicio;
        foreach (Rango x in rangos)
        {
            double pedazo = x.Inicio - ultimoFin;
            if (pedazo > 0.001 && pedazo < minimo) continue;
            r.Add(x);
            ultimoFin = x.Fin;
        }
        // El ultimo clip, entre el ultimo silencio y el final.
        while (r.Count > 0)
        {
            double pedazo = fin - r[r.Count - 1].Fin;
            if (pedazo > 0.001 && pedazo < minimo) r.RemoveAt(r.Count - 1);
            else break;
        }
        return r;
    }
}

// ---- src/comun/Configuracion.cs ----

// =====================================================================
// Configuracion compartida por todas las herramientas
// (%APPDATA%\vegas-cut\config.json). La clave de Gemini se guarda cifrada
// con DPAPI: solo tu usuario de Windows en esta PC puede leerla.
// =====================================================================

public class Configuracion
{
    public string GeminiClave = "";
    public string GeminiModelo = "gemini-flash-latest";
    public string WhisperExe = "";
    public string WhisperModelo = "large-v3-turbo";
    public string WhisperDispositivo = "cuda";   // cuda (tarjeta NVIDIA) o cpu
    public string WhisperPrecision = "int8";     // int8 usa menos memoria de video
    public string Idioma = "es";
    public string WhisperExtra = "";             // opciones extra para el .exe
    public string ReglasCanal = "";              // reglas fijas de MomentosIA ("" = las de siempre)
    public string CarpetaMemes = "";             // carpeta de memes (imagenes, videos, sonidos) con su indice

    public static string Carpeta
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"); }
    }

    static string Ruta { get { return Path.Combine(Carpeta, "config.json"); } }

    public bool TieneGemini { get { return GeminiClave.Length > 0; } }

    public bool TieneWhisper { get { return WhisperExe.Length > 0 && File.Exists(WhisperExe); } }

    public static Configuracion Cargar()
    {
        Configuracion c = new Configuracion();
        try
        {
            if (!File.Exists(Ruta)) return c;
            object o = Json.Leer(File.ReadAllText(Ruta, Encoding.UTF8));
            c.GeminiClave = Descifrar(Json.Texto(o, "geminiClave"));
            c.GeminiModelo = Valor(Json.Texto(o, "geminiModelo"), c.GeminiModelo);
            c.WhisperExe = Json.Texto(o, "whisperExe");
            c.WhisperModelo = Valor(Json.Texto(o, "whisperModelo"), c.WhisperModelo);
            c.WhisperDispositivo = Valor(Json.Texto(o, "whisperDispositivo"), c.WhisperDispositivo);
            c.WhisperPrecision = Valor(Json.Texto(o, "whisperPrecision"), c.WhisperPrecision);
            c.Idioma = Valor(Json.Texto(o, "idioma"), c.Idioma);
            c.WhisperExtra = Json.Texto(o, "whisperExtra");
            c.ReglasCanal = Json.Texto(o, "reglasCanal");
            c.CarpetaMemes = Json.Texto(o, "carpetaMemes");
        }
        catch { }
        return c;
    }

    static string Valor(string v, string siVacio) { return String.IsNullOrEmpty(v) ? siVacio : v; }

    public void Guardar()
    {
        Directory.CreateDirectory(Carpeta);
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["geminiClave"] = Cifrar(GeminiClave);
        d["geminiModelo"] = GeminiModelo;
        d["whisperExe"] = WhisperExe;
        d["whisperModelo"] = WhisperModelo;
        d["whisperDispositivo"] = WhisperDispositivo;
        d["whisperPrecision"] = WhisperPrecision;
        d["idioma"] = Idioma;
        d["whisperExtra"] = WhisperExtra;
        d["reglasCanal"] = ReglasCanal;
        d["carpetaMemes"] = CarpetaMemes;
        File.WriteAllText(Ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    // ------------------------------------------------------------- DPAPI

    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Largo; public IntPtr Datos; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob entrada, string descripcion, IntPtr entropia,
        IntPtr reservado, IntPtr aviso, int banderas, ref Blob salida);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob entrada, IntPtr descripcion, IntPtr entropia,
        IntPtr reservado, IntPtr aviso, int banderas, ref Blob salida);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr p);

    const int SinInterfaz = 0x1;

    static byte[] Dpapi(byte[] datos, bool cifrar)
    {
        Blob entrada = new Blob(), salida = new Blob();
        GCHandle h = GCHandle.Alloc(datos, GCHandleType.Pinned);
        try
        {
            entrada.Largo = datos.Length;
            entrada.Datos = h.AddrOfPinnedObject();
            bool ok = cifrar
                ? CryptProtectData(ref entrada, "vegas-cut", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, SinInterfaz, ref salida)
                : CryptUnprotectData(ref entrada, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, SinInterfaz, ref salida);
            if (!ok) throw new Exception("DPAPI fall\u00f3 (" + Marshal.GetLastWin32Error() + ")");
            byte[] r = new byte[salida.Largo];
            Marshal.Copy(salida.Datos, r, 0, salida.Largo);
            return r;
        }
        finally
        {
            h.Free();
            if (salida.Datos != IntPtr.Zero) LocalFree(salida.Datos);
        }
    }

    // "dpapi:..." si se pudo cifrar; "b64:..." solo como respaldo fuera de Windows.
    static string Cifrar(string texto)
    {
        if (String.IsNullOrEmpty(texto)) return "";
        byte[] b = Encoding.UTF8.GetBytes(texto);
        try { return "dpapi:" + Convert.ToBase64String(Dpapi(b, true)); }
        catch { return "b64:" + Convert.ToBase64String(b); }
    }

    static string Descifrar(string guardado)
    {
        try
        {
            if (guardado.StartsWith("dpapi:"))
                return Encoding.UTF8.GetString(Dpapi(Convert.FromBase64String(guardado.Substring(6)), false));
            if (guardado.StartsWith("b64:"))
                return Encoding.UTF8.GetString(Convert.FromBase64String(guardado.Substring(4)));
        }
        catch { }
        return "";
    }
}

// ---- src/comun/Gemini.cs ----

// =====================================================================
// Cliente minimo de la API de Gemini (REST generateContent).
// Solo se envia texto; el audio nunca sale de la PC.
// =====================================================================

public static class Gemini
{
    // Se puede cambiar solo para pruebas (servidor local que imita la API).
    public static string Base = "https://generativelanguage.googleapis.com/v1beta/";

    static HttpWebRequest Peticion(string url, string clave, string metodo)
    {
        // Vegas corre en .NET Framework: hay que activar TLS 1.2 a mano.
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
        r.Method = metodo;
        r.Headers.Add("x-goog-api-key", clave);
        r.Timeout = 10 * 60 * 1000;
        r.ReadWriteTimeout = 10 * 60 * 1000;
        return r;
    }

    static string Responder(HttpWebRequest r)
    {
        try
        {
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
        catch (WebException ex)
        {
            string detalle = ex.Message;
            if (ex.Response != null)
            {
                try
                {
                    using (StreamReader sr = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        string cuerpo = sr.ReadToEnd();
                        string msg = Json.Texto(Json.Obj(Json.Leer(cuerpo), "error"), "message");
                        if (msg.Length > 0) detalle = msg;
                    }
                }
                catch { }
            }
            throw new Exception("Gemini: " + detalle);
        }
    }

    // Modelos disponibles para esta clave que sirven para generar texto.
    public static List<string> ListarModelos(string clave)
    {
        List<string> r = new List<string>();
        string pagina = "";
        do
        {
            string url = Base + "models?pageSize=200" + (pagina.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(pagina) : "");
            object o = Json.Leer(Responder(Peticion(url, clave, "GET")));
            foreach (object m in Json.Lista(o, "models"))
            {
                bool genera = false;
                foreach (object metodo in Json.Lista(m, "supportedGenerationMethods"))
                    if ((metodo as string) == "generateContent") genera = true;
                string nombre = Json.Texto(m, "name");
                if (nombre.StartsWith("models/")) nombre = nombre.Substring(7);
                if (genera && nombre.StartsWith("gemini")) r.Add(nombre);
            }
            pagina = Json.Texto(o, "nextPageToken");
        } while (pagina.Length > 0);
        r.Sort(StringComparer.OrdinalIgnoreCase);
        return r;
    }

    // Pide una respuesta. Con "json" se exige que conteste solo JSON.
    public static string Generar(string clave, string modelo, string instrucciones, string mensaje, bool json)
    {
        return Generar(clave, modelo, instrucciones, mensaje, json, null);
    }

    // Con imagenes (tipo MIME y bytes), por ejemplo para describir memes.
    public static string Generar(string clave, string modelo, string instrucciones, string mensaje, bool json,
                                 List<KeyValuePair<string, byte[]>> imagenes)
    {
        Dictionary<string, object> cuerpo = new Dictionary<string, object>();
        if (!String.IsNullOrEmpty(instrucciones))
            cuerpo["systemInstruction"] = Partes(instrucciones, null);
        Dictionary<string, object> usuario = Partes(mensaje, "user");
        if (imagenes != null)
            foreach (KeyValuePair<string, byte[]> im in imagenes)
            {
                Dictionary<string, object> dato = new Dictionary<string, object>();
                dato["mime_type"] = im.Key; dato["data"] = Convert.ToBase64String(im.Value);
                Dictionary<string, object> parte = new Dictionary<string, object>();
                parte["inline_data"] = dato;
                ((List<object>)usuario["parts"]).Add(parte);
            }
        cuerpo["contents"] = new List<object> { usuario };
        Dictionary<string, object> config = new Dictionary<string, object>();
        config["temperature"] = 0.4;
        if (json) config["responseMimeType"] = "application/json";
        cuerpo["generationConfig"] = config;

        HttpWebRequest r = Peticion(Base + "models/" + Uri.EscapeDataString(modelo) + ":generateContent", clave, "POST");
        r.ContentType = "application/json; charset=utf-8";
        byte[] datos = Encoding.UTF8.GetBytes(Json.Escribir(cuerpo, false));
        r.ContentLength = datos.Length;
        using (Stream s = r.GetRequestStream()) s.Write(datos, 0, datos.Length);

        object resp = Json.Leer(Responder(r));
        List<object> candidatos = Json.Lista(resp, "candidates");
        if (candidatos.Count == 0)
        {
            string motivo = Json.Texto(Json.Obj(resp, "promptFeedback"), "blockReason");
            throw new Exception("Gemini no devolvi\u00f3 respuesta" + (motivo.Length > 0 ? " (" + motivo + ")" : "") + ".");
        }
        StringBuilder texto = new StringBuilder();
        foreach (object parte in Json.Lista(Json.Obj(candidatos[0], "content"), "parts"))
        {
            object pensamiento;
            Dictionary<string, object> d = parte as Dictionary<string, object>;
            if (d != null && d.TryGetValue("thought", out pensamiento) && pensamiento is bool && (bool)pensamiento) continue;
            texto.Append(Json.Texto(parte, "text"));
        }
        // Si se acabo el espacio de respuesta, el JSON queda a medias.
        if (Json.Texto(candidatos[0], "finishReason") == "MAX_TOKENS")
            throw new RespuestaCortada();
        if (texto.Length == 0)
            throw new Exception("Gemini devolvi\u00f3 una respuesta vac\u00eda (" + Json.Texto(candidatos[0], "finishReason") + ").");
        return QuitarCercas(texto.ToString());
    }

    static Dictionary<string, object> Partes(string texto, string rol)
    {
        Dictionary<string, object> parte = new Dictionary<string, object>();
        parte["text"] = texto;
        Dictionary<string, object> c = new Dictionary<string, object>();
        if (rol != null) c["role"] = rol;
        c["parts"] = new List<object> { parte };
        return c;
    }

    // Algunos modelos envuelven el JSON en ```json ... ```.
    public static string QuitarCercas(string t)
    {
        string s = t.Trim();
        if (s.StartsWith("```"))
        {
            int salto = s.IndexOf('\n');
            int fin = s.LastIndexOf("```");
            if (salto > 0 && fin > salto) s = s.Substring(salto + 1, fin - salto - 1).Trim();
        }
        return s;
    }
}

// La respuesta no cupo completa (finishReason MAX_TOKENS).
public class RespuestaCortada : Exception
{
    public RespuestaCortada() : base("La respuesta de Gemini sali\u00f3 cortada por ser demasiado larga.") { }
}

// ---- src/comun/Whisper.cs ----

// =====================================================================
// Faster-Whisper-XXL en segundo plano: lanza el .exe, sigue su avance
// leyendo las lineas "[00:01.000 --> 00:04.000] texto" y al final deja el
// JSON con los tiempos de cada palabra.
// =====================================================================

public class TareaWhisper
{
    Process proceso;
    readonly object candado = new object();
    readonly StringBuilder registro = new StringBuilder();
    string carpeta, wav;
    double duracion, avance;
    string ultimaLinea = "";

    public bool Terminada { get; private set; }
    public bool Cancelada { get; private set; }
    public int Codigo { get; private set; }

    // Segundos de audio ya procesados (segun lo que imprime Whisper).
    public double Avance { get { lock (candado) return avance; } }
    public double Fraccion { get { return duracion > 0 ? Math.Min(1, Avance / duracion) : 0; } }
    public string UltimaLinea { get { lock (candado) return ultimaLinea; } }
    public string Registro { get { lock (candado) return registro.ToString(); } }

    public static string Argumentos(Configuracion c, string wav, string carpeta)
    {
        string a = "\"" + wav + "\"" +
            " --model " + c.WhisperModelo +
            " --language " + c.Idioma +
            " --output_format json" +
            " --output_dir \"" + carpeta + "\"" +
            " --word_timestamps True" +
            " --vad_filter True" +
            " --device " + c.WhisperDispositivo +
            " --compute_type " + c.WhisperPrecision;
        if (!String.IsNullOrEmpty(c.WhisperExtra)) a += " " + c.WhisperExtra;
        return a;
    }

    public void Iniciar(Configuracion c, string wav, double duracionAudio)
    {
        if (!c.TieneWhisper) throw new Exception("Configura la ruta de Faster-Whisper-XXL en \u201cConfigurar vegas-cut\u201d.");
        this.wav = wav;
        duracion = duracionAudio;
        carpeta = Path.Combine(Path.GetTempPath(), "vegas-cut-whisper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);

        ProcessStartInfo info = new ProcessStartInfo(c.WhisperExe, Argumentos(c, wav, carpeta));
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.WorkingDirectory = Path.GetDirectoryName(c.WhisperExe);

        proceso = new Process();
        proceso.StartInfo = info;
        proceso.EnableRaisingEvents = true;
        proceso.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { Linea(e.Data); };
        proceso.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { Linea(e.Data); };
        proceso.Exited += delegate
        {
            try { proceso.WaitForExit(); Codigo = proceso.ExitCode; } catch { }
            Terminada = true;
        };
        proceso.Start();
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
    }

    static readonly Regex Marca = new Regex(@"-->\s*(?:(\d+):)?(\d+):(\d+(?:[.,]\d+)?)\]");

    void Linea(string l)
    {
        if (l == null) return;
        lock (candado)
        {
            registro.AppendLine(l);
            if (registro.Length > 200000) registro.Remove(0, 100000);
            if (l.Trim().Length > 0) ultimaLinea = l.Trim();
            Match m = Marca.Match(l);
            if (m.Success)
            {
                double h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                double min = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double s = double.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                avance = Math.Max(avance, h * 3600 + min * 60 + s);
            }
        }
    }

    public void Cancelar()
    {
        Cancelada = true;
        try { if (proceso != null && !proceso.HasExited) proceso.Kill(); } catch { }
    }

    // JSON resultante; lanza un error con el final del registro si fallo.
    public string Resultado()
    {
        string esperado = Path.Combine(carpeta, Path.GetFileNameWithoutExtension(wav) + ".json");
        string ruta = File.Exists(esperado) ? esperado : null;
        if (ruta == null)
            foreach (string f in Directory.GetFiles(carpeta, "*.json")) { ruta = f; break; }
        if (ruta == null)
        {
            string r = Registro.Trim();
            if (r.Length > 1200) r = "\u2026" + r.Substring(r.Length - 1200);
            throw new Exception("Whisper no gener\u00f3 la transcripci\u00f3n (c\u00f3digo " + Codigo + ").\n\n" + r);
        }
        return File.ReadAllText(ruta, Encoding.UTF8);
    }

    public void Limpiar()
    {
        try { if (carpeta != null) Directory.Delete(carpeta, true); } catch { }
    }
}

// ---- src/comun/Ui.cs ----

// =====================================================================
// Interfaz
// =====================================================================

static class Tema
{
    public static readonly Color Fondo = Color.FromArgb(18, 19, 23);
    public static readonly Color Panel = Color.FromArgb(27, 28, 34);
    public static readonly Color Campo = Color.FromArgb(35, 37, 44);
    public static readonly Color CampoHover = Color.FromArgb(44, 46, 55);
    public static readonly Color Borde = Color.FromArgb(52, 54, 64);
    public static readonly Color Texto = Color.FromArgb(236, 237, 241);
    public static readonly Color TextoSuave = Color.FromArgb(150, 153, 164);
    public static readonly Color Acento = Color.FromArgb(255, 106, 43);
    public static readonly Color AcentoHover = Color.FromArgb(255, 132, 80);
    public static readonly Color Voz = Color.FromArgb(120, 200, 255);
    public static readonly Color Silencio = Color.FromArgb(255, 84, 84);

    public static Font Fuente(float tam, FontStyle estilo)
    {
        try { return new Font("Segoe UI", tam, estilo); }
        catch { return new Font(FontFamily.GenericSansSerif, tam, estilo); }
    }
    public static readonly Font Normal = Fuente(9f, FontStyle.Regular);
    public static readonly Font Negrita = Fuente(9f, FontStyle.Bold);
    public static readonly Font Pequena = Fuente(8f, FontStyle.Regular);
    public static readonly Font Titulo = Fuente(15f, FontStyle.Bold);
    public static readonly Font Seccion = Fuente(10f, FontStyle.Bold);

    public static GraphicsPath Redondeado(RectangleF r, float radio)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

class ControlBase : Control
{
    protected bool encima;
    public ControlBase()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
    }
    protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }
}

enum EstiloBoton { Primario, Secundario, Chip }

class Boton : ControlBase
{
    public EstiloBoton Estilo = EstiloBoton.Secundario;
    bool activo;
    public bool Activo { get { return activo; } set { activo = value; Invalidate(); } }

    public Boton(string texto, EstiloBoton estilo)
    {
        Text = texto;
        Estilo = estilo;
        Cursor = Cursors.Hand;
        if (estilo == EstiloBoton.Primario) Font = Tema.Fuente(10f, FontStyle.Bold);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fondo, borde, texto = Tema.Texto;
        if (Estilo == EstiloBoton.Primario)
        {
            fondo = !Enabled ? Color.FromArgb(90, 60, 48) : encima ? Tema.AcentoHover : Tema.Acento;
            borde = fondo;
            texto = Enabled ? Color.White : Color.FromArgb(170, 150, 140);
        }
        else if (Estilo == EstiloBoton.Chip && activo)
        {
            fondo = Color.FromArgb(60, Tema.Acento);
            borde = Tema.Acento;
        }
        else
        {
            fondo = encima && Enabled ? Tema.CampoHover : Tema.Campo;
            borde = Tema.Borde;
            if (!Enabled) texto = Tema.TextoSuave;
        }
        using (GraphicsPath p = Tema.Redondeado(r, Estilo == EstiloBoton.Chip ? Height / 2f : 8))
        {
            using (SolidBrush b = new SolidBrush(fondo)) g.FillPath(b, p);
            using (Pen pen = new Pen(borde)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, texto,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class Segmentado : ControlBase
{
    string[] opciones;
    int seleccion;
    int hover = -1;
    public event EventHandler Cambio;

    public Segmentado(string[] opciones) { this.opciones = opciones; Cursor = Cursors.Hand; }

    public int Seleccion
    {
        get { return seleccion; }
        set { if (value != seleccion) { seleccion = value; Invalidate(); if (Cambio != null) Cambio(this, EventArgs.Empty); } }
    }

    bool[] habilitadas;
    public void Habilitar(int i, bool si)
    {
        if (habilitadas == null) { habilitadas = new bool[opciones.Length]; for (int k = 0; k < opciones.Length; k++) habilitadas[k] = true; }
        habilitadas[i] = si;
        Invalidate();
    }
    bool Habilitada(int i) { return habilitadas == null || habilitadas[i]; }

    int Indice(int x) { return Math.Max(0, Math.Min(opciones.Length - 1, x * opciones.Length / Math.Max(1, Width))); }

    protected override void OnMouseMove(MouseEventArgs e) { int h = Indice(e.X); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int i = Indice(e.X); if (Habilitada(i)) Seleccion = i; base.OnMouseDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath p = Tema.Redondeado(r, 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(Tema.Borde)) g.DrawPath(pen, p);
        }
        float ancho = (Width - 6f) / opciones.Length;
        for (int i = 0; i < opciones.Length; i++)
        {
            RectangleF c = new RectangleF(3 + i * ancho, 3, ancho, Height - 7);
            if (i == seleccion)
                using (GraphicsPath p = Tema.Redondeado(c, 6))
                using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
            else if (i == hover && Habilitada(i))
                using (GraphicsPath p = Tema.Redondeado(c, 6))
                using (SolidBrush b = new SolidBrush(Tema.CampoHover)) g.FillPath(b, p);
            Color col = i == seleccion ? Color.White : Habilitada(i) ? Tema.Texto : Color.FromArgb(90, 92, 100);
            TextRenderer.DrawText(g, opciones[i], i == seleccion ? Tema.Negrita : Font, Rectangle.Round(c), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

class Deslizador : ControlBase
{
    public double Minimo = -70, Maximo = -10;
    public bool DesdeCentro;
    double valor = 0;
    bool arrastrando;
    public event EventHandler Cambio;

    public Deslizador() { Cursor = Cursors.Hand; }

    public double Valor
    {
        get { return valor; }
        set
        {
            double v = Math.Max(Minimo, Math.Min(Maximo, Math.Round(value)));
            if (v != valor) { valor = v; Invalidate(); if (Cambio != null) Cambio(this, EventArgs.Empty); }
        }
    }

    float X(double v) { return 8 + (float)((v - Minimo) / (Maximo - Minimo)) * (Width - 16); }

    void Mover(int x) { Valor = Minimo + (x - 8) / (double)Math.Max(1, Width - 16) * (Maximo - Minimo); }

    protected override void OnMouseDown(MouseEventArgs e) { arrastrando = true; Mover(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (arrastrando) Mover(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { arrastrando = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Valor = valor + (e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f, x = X(valor);
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(8, cy - 3, Width - 16, 6), 3))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        float desde = DesdeCentro ? X((Minimo + Maximo) / 2) : 8;
        if (DesdeCentro)
            using (SolidBrush b = new SolidBrush(Tema.Borde)) g.FillRectangle(b, desde - 1, cy - 7, 2, 14);
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(Math.Min(desde, x), cy - 3, Math.Max(6, Math.Abs(x - desde)), 6), 3))
        using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
        float rad = encima || arrastrando ? 9 : 8;
        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, x - rad, cy - rad, rad * 2, rad * 2);
        using (Pen pen = new Pen(Tema.Acento, 3)) g.DrawEllipse(pen, x - rad + 1.5f, cy - rad + 1.5f, rad * 2 - 3, rad * 2 - 3);
    }
}

// Campo numerico con sufijo "ms": escribir, rueda del raton o flechas.
class CampoNumero : ControlBase
{
    TextBox caja = new TextBox();
    int valor;
    public int Minimo = 0, Maximo = 5000, Paso = 10;
    public string Sufijo = "ms";
    public event EventHandler Cambio;

    public CampoNumero()
    {
        caja.BorderStyle = BorderStyle.None;
        caja.BackColor = Tema.Campo;
        caja.ForeColor = Tema.Texto;
        caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        caja.TextAlign = HorizontalAlignment.Left;
        caja.KeyPress += delegate (object s, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
        caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Valor = valor + Paso; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Valor = valor - Paso; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { Confirmar(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        caja.Leave += delegate { Confirmar(); };
        caja.MouseWheel += delegate (object s, MouseEventArgs e) { Valor = valor + (e.Delta > 0 ? Paso : -Paso); };
        Controls.Add(caja);
        Cursor = Cursors.IBeam;
    }

    void Confirmar()
    {
        int v;
        if (int.TryParse(caja.Text, out v)) Valor = v; else caja.Text = valor.ToString();
    }

    public int Valor
    {
        get { return valor; }
        set
        {
            int v = Math.Max(Minimo, Math.Min(Maximo, value));
            caja.Text = v.ToString();
            if (v != valor) { valor = v; if (Cambio != null) Cambio(this, EventArgs.Empty); }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e) { caja.Focus(); base.OnMouseDown(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        caja.SetBounds(12, (Height - caja.PreferredHeight) / 2 + 1, Width - 50, caja.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(caja.Focused ? Tema.Acento : Tema.Borde)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Sufijo, Tema.Pequena, new Rectangle(Width - 36, 0, 28, Height), Tema.TextoSuave,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

class Combo : ComboBox
{
    public Combo() : this(false) { }

    // Editable: se puede escribir un valor que no este en la lista.
    public Combo(bool editable)
    {
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Tema.Campo;
        ForeColor = Tema.Texto;
        Font = Tema.Fuente(10f, FontStyle.Regular);
        ItemHeight = 24;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (SolidBrush b = new SolidBrush(sel ? Tema.Acento : Tema.Campo)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            sel ? Color.White : Tema.Texto, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class Etiqueta : Label
{
    public Etiqueta(string texto, Font fuente, Color color)
    {
        Text = texto; Font = fuente; ForeColor = color;
        BackColor = Color.Transparent;
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleLeft;
    }
}

// Pide el nombre para guardar un perfil.
class DialogoNombre : Form
{
    TextBox caja = new TextBox();
    public string Nombre { get { return caja.Text.Trim(); } }

    public DialogoNombre(string sugerido) : this(sugerido, "Guardar perfil", "Nombre del perfil") { }

    public DialogoNombre(string sugerido, string titulo, string etiqueta)
    {
        Text = titulo;
        ClientSize = new Size(380, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;

        Controls.Add(Pos(new Etiqueta(etiqueta, Tema.Seccion, Tema.Texto), 20, 16, 340, 22));
        Panel marco = new Panel();
        marco.BackColor = Tema.Campo;
        marco.Padding = new Padding(10, 8, 10, 6);
        caja.BorderStyle = BorderStyle.None;
        caja.BackColor = Tema.Campo;
        caja.ForeColor = Tema.Texto;
        caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        caja.Dock = DockStyle.Fill;
        caja.Text = sugerido;
        marco.Controls.Add(caja);
        Controls.Add(Pos(marco, 20, 46, 340, 34));

        Boton guardar = new Boton("Guardar", EstiloBoton.Primario);
        Boton cancelar = new Boton("Cancelar", EstiloBoton.Secundario);
        Controls.Add(Pos(cancelar, 150, 100, 100, 34));
        Controls.Add(Pos(guardar, 260, 100, 100, 34));
        guardar.Click += delegate { if (Nombre.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
        cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && Nombre.Length > 0) { DialogResult = DialogResult.OK; Close(); }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
        Shown += delegate { caja.Focus(); caja.SelectAll(); };
    }

    static Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); return c; }
}

// Campo de texto oscuro con borde redondeado.
class CampoTexto : ControlBase
{
    public TextBox Caja = new TextBox();

    public CampoTexto()
    {
        Caja.BorderStyle = BorderStyle.None;
        Caja.BackColor = Tema.Campo;
        Caja.ForeColor = Tema.Texto;
        Caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        Caja.GotFocus += delegate { Invalidate(); };
        Caja.LostFocus += delegate { Invalidate(); };
        Controls.Add(Caja);
        Cursor = Cursors.IBeam;
    }

    public override string Text { get { return Caja.Text; } set { Caja.Text = value; } }

    public bool Oculto { get { return Caja.UseSystemPasswordChar; } set { Caja.UseSystemPasswordChar = value; } }

    public bool Multilinea
    {
        get { return Caja.Multiline; }
        set { Caja.Multiline = value; Caja.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None; PerformLayout(); }
    }

    protected override void OnMouseDown(MouseEventArgs e) { Caja.Focus(); base.OnMouseDown(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Caja.Multiline) Caja.SetBounds(10, 8, Width - 20, Height - 16);
        else Caja.SetBounds(12, (Height - Caja.PreferredHeight) / 2 + 1, Width - 24, Caja.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(Caja.Focused ? Tema.Acento : Tema.Borde)) g.DrawPath(pen, p);
        }
    }
}

// Lista oscura con casillas (ListView con encabezado dibujado a mano).
class Lista : ListView
{
    public Lista()
    {
        View = View.Details;
        FullRowSelect = true;
        CheckBoxes = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        BackColor = Tema.Campo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        OwnerDraw = true;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        DoubleBuffered = true;
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using (SolidBrush b = new SolidBrush(Tema.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, Tema.Pequena,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Tema.TextoSuave,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) { e.DrawDefault = true; }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) { e.DrawDefault = true; }
}

// Barra de progreso redondeada.
class BarraProgreso : ControlBase
{
    double valor;
    public double Valor { get { return valor; } set { valor = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        if (valor > 0)
            using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Math.Max(Height, (float)(Width - 1) * (float)valor), Height - 1), Height / 2f))
            using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
    }
}

// Ventana base con el tema oscuro y la linea de acento bajo el titulo.
class VentanaBase : Form
{
    protected const int Margen = 24;

    public VentanaBase(string titulo, int ancho)
    {
        Text = titulo + " \u00b7 vegas-cut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(ancho, 400);
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    protected int Ancho { get { return ClientSize.Width - Margen * 2; } }

    protected Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); Controls.Add(c); return c; }

    protected Etiqueta Texto(string t, Font f, Color c, int x, int y, int w, int h)
    {
        Etiqueta e = new Etiqueta(t, f, c);
        if (h > 22) e.TextAlign = ContentAlignment.TopLeft;
        Pos(e, x, y, w, h);
        return e;
    }

    protected void Encabezado(string titulo, string subtitulo)
    {
        Texto(titulo, Tema.Titulo, Tema.Texto, Margen, 18, Ancho, 32);
        Texto(subtitulo, Tema.Normal, Tema.TextoSuave, Margen, 50, Ancho, 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (SolidBrush b = new SolidBrush(Tema.Acento)) e.Graphics.FillRectangle(b, Margen, 76, 36, 3);
    }
}

// ---- src/comun/CopiaBase.cs ----

// Copia "<proyecto> BASE.veg": el episodio sin silencios y transcrito, para
// volver a partir de ahi (MomentosIA o ProducirCapitulo) sin repetir esos pasos.
public static class CopiaBase
{
    public const string Sufijo = " BASE";

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + Sufijo + ".veg");
    }

    // El proyecto original de una copia base o de un corte de MomentosIA (o el mismo si no lo es).
    public static string Original(string veg)
    {
        string n = Path.GetFileNameWithoutExtension(veg);
        if (n.EndsWith(Sufijo)) return Path.Combine(Path.GetDirectoryName(veg), n.Substring(0, n.Length - Sufijo.Length) + ".veg");
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(n, @"^(.*) MOM( \d+)?$");
        return m.Success ? Path.Combine(Path.GetDirectoryName(veg), m.Groups[1].Value + ".veg") : veg;
    }

    public static bool EsCorte(string veg) { return !String.IsNullOrEmpty(veg) && Original(veg) != veg && !EsBase(veg); }

    public static bool EsBase(string veg) { return !String.IsNullOrEmpty(veg) && Path.GetFileNameWithoutExtension(veg).EndsWith(Sufijo); }

    // Copia del corte de MomentosIA cuando se trabaja sobre la BASE: "<original> MOM.veg" (o MOM 2, 3...).
    public static string RutaCorte(string veg)
    {
        string o = Original(veg), dir = Path.GetDirectoryName(o), n = Path.GetFileNameWithoutExtension(o);
        string r = Path.Combine(dir, n + " MOM.veg");
        for (int i = 2; File.Exists(r); i++) r = Path.Combine(dir, n + " MOM " + i + ".veg");
        return r;
    }

    // La transcripcion y el enlace a la serie del proyecto "desde" pasan a "hacia".
    static void CopiarAnexos(string desde, string hacia)
    {
        string t = Transcripcion.RutaPara(desde);
        if (File.Exists(t))
        {
            Transcripcion tr = Transcripcion.Cargar(t);
            tr.Proyecto = hacia;
            tr.Guardar(Transcripcion.RutaPara(hacia));
        }
        string dir = Path.GetDirectoryName(desde);
        string serie = Path.Combine(dir, Path.GetFileNameWithoutExtension(desde) + ".vegascut-proyecto-serie.json");
        if (File.Exists(serie))
            File.Copy(serie, Path.Combine(Path.GetDirectoryName(hacia), Path.GetFileNameWithoutExtension(hacia) + ".vegascut-proyecto-serie.json"), true);
    }

    // Guarda el proyecto abierto como "destino" (con su transcripcion y su serie) y se queda en el.
    public static void GuardarComo(Vegas vegas, string destino)
    {
        string desde = vegas.Project.FilePath;
        vegas.SaveProject(destino);
        CopiarAnexos(desde, destino);
    }

    // Guarda la copia (con su transcripcion y su serie) y vuelve al proyecto original.
    public static string Guardar(Vegas vegas)
    {
        string original = vegas.Project.FilePath;
        if (String.IsNullOrEmpty(original) || Path.GetFileNameWithoutExtension(original).EndsWith(Sufijo)) return null;
        string base_ = RutaPara(original);
        vegas.SaveProject(base_);
        try { CopiarAnexos(original, base_); }
        finally { vegas.SaveProject(original); }
        return base_;
    }
}
