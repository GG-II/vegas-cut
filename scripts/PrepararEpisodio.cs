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
               " Si lo deshaces (Ctrl+Z), al volver a abrir esta ventana la respuesta aparece lista otra vez.", false);
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
    public List<string> Episodios = new List<string>();   // rutas de los .veg, en orden
    // Papel de cada capitulo (primero, especial, final...) y su nota, por
    // ruta del .veg. Lo que no esta aqui es "Normal" (o "Primer cap\u00edtulo").
    public Dictionary<string, string> Papeles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> NotasEpisodio = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        }
        return s;
    }

    public void Guardar()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-serie";
        d["nombre"] = Nombre; d["tipo"] = Tipo; d["notas"] = Notas; d["carpeta"] = Carpeta;
        d["estructura"] = Formato.Escribir();
        List<object> l = new List<object>();
        foreach (string veg in Episodios)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["veg"] = veg;
            x["relativo"] = Relativa(Path.GetDirectoryName(Ruta), veg);
            string papel, nota;
            if (Papeles.TryGetValue(veg, out papel)) x["papel"] = papel;
            if (NotasEpisodio.TryGetValue(veg, out nota) && nota.Trim().Length > 0) x["nota"] = nota.Trim();
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
            if (Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"\s(BASE|CAP)$", RegexOptions.IgnoreCase)) continue;   // copias del mismo capitulo
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
    public static readonly string[] Formatos = { "100 d\u00edas", "Aventura por episodios", "Retos / minijuegos", "Video ensayo",
                                                 "Top / lista", "Podcast", "Otro" };
    public static readonly string[] Avances = { "D\u00eda N", "Parte N", "Etapa N", "Ronda N", "Acto N", "N\u00famero N", "Ninguno" };

    public string Nombre = "100 d\u00edas";
    public string Premisa = "";            // de que va y que se busca (el objetivo de la serie)
    public string Avance = "D\u00eda N";
    public bool Narrador = true;
    public string NarradorNombre = "Narrador";   // hablante de la transcripcion
    public string EstiloNarrador = "";
    public string Aprendido = "";          // de que proyecto salieron las reglas
    public ReglasRitmo Reglas = new ReglasRitmo();

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
    public static readonly string[] Papeles = { "Primer cap\u00edtulo", "Normal", "Especial", "Final de temporada", "Final de la serie" };

    public static string Instrucciones(string papel)
    {
        switch (papel)
        {
            case "Primer cap\u00edtulo":
                return "Es el PRIMER cap\u00edtulo: presenta la premisa, a cada persona (qui\u00e9n es, un rasgo) y las reglas o el objetivo " +
                       "antes del minuto 1; el espectador no sabe nada. Promete lo que va a venir en la serie.";
            case "Especial":
                return "Es un cap\u00edtulo ESPECIAL: puede romper el formato (otra estructura, otro ritmo, otro tipo de inicio). " +
                       "Que se note desde el inicio qu\u00e9 lo hace distinto; no repitas la f\u00f3rmula de los cap\u00edtulos normales.";
            case "Final de temporada":
                return "Es el FINAL DE TEMPORADA: retoma y paga los hilos abiertos de la temporada, sube la tensi\u00f3n hacia el " +
                       "cl\u00edmax, deja tiempo a un cierre emotivo o \u00e9pico y termina con un gancho para la pr\u00f3xima temporada.";
            case "Final de la serie":
                return "Es el FINAL DE LA SERIE: cierra todos los hilos importantes, recuerda momentos de cap\u00edtulos anteriores, " +
                       "dale peso al cl\u00edmax y un cierre con despedida; no anuncies un pr\u00f3ximo episodio.";
            default:
                return "Es un cap\u00edtulo intermedio: recuerda en pocos segundos d\u00f3nde qued\u00f3 la historia, avanza con algo nuevo " +
                       "(un logro, un problema, alguien nuevo) y termina con un pendiente para el siguiente. Var\u00eda el inicio y " +
                       "los recursos respecto a los cap\u00edtulos anteriores.";
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
                break;
            case "Especial":
                x.DuracionMin = Math.Max(1, Math.Round(x.DuracionMin * 0.7));
                x.DuracionMax = Math.Round(x.DuracionMax * 1.4);
                break;
            case "Final de temporada":
            case "Final de la serie":
                x.DuracionMax = Math.Round(x.DuracionMax * 1.4);
                x.MusicaCadaSeg = (int)(x.MusicaCadaSeg * 1.3);   // temas mas largos en el climax
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
        nombre = Regex.Replace(nombre ?? "", @"\s+(BASE|CAP)$", "", RegexOptions.IgnoreCase);
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
                string nota = serie.NotaEpisodio(c.Veg);
                if (nota.Length > 0) sb.Append("Nota del editor para este cap\u00edtulo: " + nota + "\n");
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
                                        "despu\u00e9s, cons\u00e9rvalo aunque aqu\u00ed parezca menor; no adelantes lo que pasa despu\u00e9s.\n");
                    titulo = true;
                }
                sb.Append("- " + c.Nombre + ": " + f.Texto(false));
            }
        }
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
        Texto("Notas de la serie (personajes, apodos, lugares)", Tema.Negrita, Tema.Texto, dx, y, 340, 20);
        lblFormato = Texto("", Tema.Pequena, Tema.TextoSuave, dx + 350, y + 4, dw - 520, 20);
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
                                              btnBajar, btnQuitar, btnFichas, btnRehacer, txtNotas, btnOlvidar, btnFormato })
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
        cmbFormato.SelectedIndexChanged += delegate { if (!cargando) Estado("Pulsa \u201cUsar valores del formato\u201d para cargar sus reglas de partida.", false); };
        btnPreset.Click += delegate
        {
            FormatoSerie p = FormatoSerie.Preset((string)cmbFormato.SelectedItem);
            p.Premisa = txtPremisa.Text.Trim();
            p.NarradorNombre = txtNarrador.Text.Trim().Length > 0 ? txtNarrador.Text.Trim() : p.NarradorNombre;
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
        Dictionary<string, object> cuerpo = new Dictionary<string, object>();
        if (!String.IsNullOrEmpty(instrucciones))
            cuerpo["systemInstruction"] = Partes(instrucciones, null);
        cuerpo["contents"] = new List<object> { Partes(mensaje, "user") };
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
