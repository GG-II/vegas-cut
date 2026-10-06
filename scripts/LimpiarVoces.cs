// LimpiarVoces.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Quita el ruido de las pistas de voz (DeepFilterNet) y empareja su volumen
// (ffmpeg), solo de lo que quedo en el video. Lo limpio entra como toma nueva
// de cada evento, en el mismo lugar; el original queda como toma alternativa.
// Va al final: despues de cortar y reordenar, antes de censurar.
//
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
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/voces/EntryPoint.cs ----

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        using (VentanaVoces v = new VentanaVoces(vegas)) v.ShowDialog();
    }
}

// ---- src/voces/Voces.cs ----

// Ventana de Limpiar voces (tambien es el primer paso de PasoFinal).
// Dos formas de dejar lo limpio:
//  - Una pista limpia (por defecto): un archivo por pista de voz, en una pista
//    nueva \u00ab\u2026 \u00b7 limpia\u00bb justo debajo; la pista original queda silenciada.
//  - Por clip: cada clip recibe lo limpio como toma nueva (el original queda
//    como toma alternativa; tecla T).
class VentanaVoces : VentanaBase
{
    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly List<InfoPista> pistas;
    readonly List<Boton> chips = new List<Boton>();
    readonly string carpeta;

    Etiqueta lblDf, lblFf, lblEstado;
    Boton btnDf = new Boton("Elegir\u2026", EstiloBoton.Secundario), btnFf = new Boton("Elegir\u2026", EstiloBoton.Secundario);
    Segmentado segModo = new Segmentado(new string[] { "Una pista limpia", "Por clip" });
    Segmentado segRuido = new Segmentado(new string[] { "No quitar", "Suave", "Medio", "Fuerte" });
    Boton chipNivelar = new Boton("Emparejar frase por frase", EstiloBoton.Chip);
    CampoNumero numObjetivo = new CampoNumero(), numPico = new CampoNumero();
    BarraProgreso barra = new BarraProgreso();
    Boton btnLimpiar = new Boton("Limpiar voces", EstiloBoton.Primario);
    Boton btnAlternar = new Boton("Escuchar originales", EstiloBoton.Secundario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);
    static readonly double[] Limites = { 0, 12, 24, 100 };

    volatile bool cancelar;
    bool trabajando;

    // Lo que usa un clip y como esta en la linea de tiempo.
    class Clip { public TrackEvent Evento; public UsoVoz Uso; public double En, Largo, FadeIn, FadeOut, Velocidad; public AudioTrack Pista; }

    public VentanaVoces(Vegas vegas) : base("Limpiar voces", 760)
    {
        this.vegas = vegas;
        StartPosition = FormStartPosition.CenterParent;
        string veg = vegas.Project.FilePath ?? "";
        carpeta = veg.Length > 0 ? Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-voces") : "";
        pistas = PistasVegas.Listar(vegas.Project);
        int m = Margen, w = Ancho;
        Encabezado("Limpiar voces", "Quita el ruido y empareja el volumen de lo que qued\u00f3 en el video. Despu\u00e9s vuelve a transcribir y censura.");
        int y = 92;
        Texto("QUITAR RUIDO", Tema.Pequena, Tema.TextoSuave, m, y + 8, 110, 18);
        lblDf = Texto("", Tema.Normal, Tema.Texto, m + 112, y + 6, w - 112 - 90, 20);
        Pos(btnDf, m + w - 80, y, 80, 30);
        y += 36;
        Texto("FFMPEG", Tema.Pequena, Tema.TextoSuave, m, y + 8, 110, 18);
        lblFf = Texto("", Tema.Normal, Tema.Texto, m + 112, y + 6, w - 112 - 90, 20);
        Pos(btnFf, m + w - 80, y, 80, 30);
        y += 46;

        Texto("Pistas de voz", Tema.Negrita, Tema.Texto, m, y, w, 20);
        y += 24;
        List<string> elegidas = new List<string>(config.VocesPistas.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
        int cx = m;
        foreach (InfoPista p in pistas)
        {
            Boton c = new Boton(p.Nombre, EstiloBoton.Chip);
            int cw = Math.Min(w, TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 26);
            if (cx + cw > m + w) { cx = m; y += 34; }
            Pos(c, cx, y, cw, 28);
            c.Tag = p;
            c.Activo = elegidas.Count > 0 ? elegidas.Contains(p.Etiqueta) : PareceVoz(p);
            c.Click += delegate { c.Activo = !c.Activo; };
            chips.Add(c);
            cx += cw + 6;
        }
        y += 44;

        Texto("Resultado", Tema.Negrita, Tema.Texto, m, y + 8, 90, 20);
        Pos(segModo, m + 90, y, 300, 34);
        segModo.Seleccion = config.VocesModo == "clip" ? 1 : 0;
        Texto("Una pista: pista nueva \u00ab\u00b7 limpia\u00bb debajo de cada voz y la original silenciada. Por clip: toma nueva en cada uno.",
              Tema.Pequena, Tema.TextoSuave, m + 400, y, w - 400, 34);
        y += 42;
        Texto("Ruido", Tema.Negrita, Tema.Texto, m, y + 8, 90, 20);
        Pos(segRuido, m + 90, y, 420, 34);
        double lim;
        double.TryParse(config.VocesRuido, NumberStyles.Any, CultureInfo.InvariantCulture, out lim);
        segRuido.Seleccion = Math.Max(0, Array.IndexOf(Limites, lim));
        y += 42;
        Texto("Volumen", Tema.Negrita, Tema.Texto, m, y + 8, 90, 20);
        numObjetivo.Sufijo = ""; numObjetivo.Minimo = -30; numObjetivo.Maximo = -10; numObjetivo.Paso = 1;
        numPico.Sufijo = ""; numPico.Minimo = -12; numPico.Maximo = -1; numPico.Paso = 1;
        int obj, pico;
        // Hasta elegir el pico, el volumen de antes (-16) era demasiado alto con juego y musica: -20.
        numObjetivo.Valor = config.VocesPico.Length == 0 ? -20 : int.TryParse(config.VocesObjetivo, out obj) ? obj : -20;
        numPico.Valor = int.TryParse(config.VocesPico, out pico) ? pico : -6;
        Pos(numObjetivo, m + 90, y, 90, 34);
        Texto("LUFS", Tema.Pequena, Tema.TextoSuave, m + 186, y + 9, 40, 18);
        Texto("PICO", Tema.Pequena, Tema.TextoSuave, m + 232, y + 9, 36, 18);
        Pos(numPico, m + 270, y, 90, 34);
        Texto("dB", Tema.Pequena, Tema.TextoSuave, m + 366, y + 9, 24, 18);
        Pos(chipNivelar, m + 400, y + 3, 220, 28);
        chipNivelar.Activo = config.VocesNivelar != "no";
        chipNivelar.Click += delegate { chipNivelar.Activo = !chipNivelar.Activo; };
        Texto("Cada pista elegida queda a ese volumen y ning\u00fan pico pasa del tope. \u00abFrase por frase\u00bb sube lo que se grab\u00f3 bajo y baja lo que se grab\u00f3 alto.",
              Tema.Pequena, Tema.TextoSuave, m + 90, y + 40, w - 90, 32);
        y += 82;

        Pos(barra, m, y, w, 8);
        y += 16;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 54);
        y += 62;
        Pos(btnAlternar, m, y, 190, 40);
        Pos(btnCerrar, m + w - 330, y, 130, 40);
        Pos(btnLimpiar, m + w - 190, y, 190, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        btnDf.Click += delegate { ElegirExe(true); };
        btnFf.Click += delegate { ElegirExe(false); };
        btnLimpiar.Click += delegate { if (trabajando) cancelar = true; else Limpiar(); };
        btnAlternar.Click += delegate { Alternar(); };
        btnCerrar.Click += delegate { Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) { cancelar = true; e.Cancel = true; } };
        Mostrar();
        if (carpeta.Length == 0) { btnLimpiar.Enabled = false; Estado("Guarda el proyecto primero (los archivos limpios van junto a \u00e9l).", true); }
    }

    static bool PareceVoz(InfoPista p)
    {
        string n = (p.Pista.Name + " " + p.Archivo).ToLowerInvariant();
        foreach (string k in new string[] { "voz", "voice", "mic", "discord", "llamada", "narr" }) if (n.Contains(k)) return true;
        return false;
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    string Ffmpeg() { return LogicaVoces.BuscarFfmpeg(config.FfmpegExe, config.WhisperExe); }

    void Mostrar()
    {
        bool df = config.DeepFilterExe.Length > 0 && File.Exists(config.DeepFilterExe);
        lblDf.Text = df ? Path.GetFileName(config.DeepFilterExe) : "Falta DeepFilterNet: elige deep-filter-\u2026-windows-msvc.exe (sin \u00e9l solo se empareja el volumen).";
        lblDf.ForeColor = df ? Tema.Texto : Tema.AcentoHover;
        string ff = Ffmpeg();
        lblFf.Text = ff.Length > 0 ? ff : "No encontr\u00e9 ffmpeg: elige ffmpeg.exe.";
        lblFf.ForeColor = ff.Length > 0 ? Tema.Texto : Tema.Silencio;
        btnAlternar.Enabled = HayLimpias();
    }

    void ElegirExe(bool deepFilter)
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Filter = "Programa (*.exe)|*.exe";
            d.Title = deepFilter ? "deep-filter-\u2026-x86_64-pc-windows-msvc.exe" : "ffmpeg.exe";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (deepFilter) config.DeepFilterExe = d.FileName; else config.FfmpegExe = d.FileName;
        }
        try { config.Guardar(); } catch { }
        Mostrar();
    }

    // Las pistas de voz elegidas (si eliges una limpia, cuenta su original).
    List<AudioTrack> Elegidas()
    {
        List<AudioTrack> r = new List<AudioTrack>();
        foreach (Boton c in chips)
        {
            if (!c.Activo) continue;
            AudioTrack t = ((InfoPista)c.Tag).Pista;
            if (EsPistaLimpia(t)) t = OriginalDe(t);
            if (t != null && !r.Contains(t)) r.Add(t);
        }
        return r;
    }

    static string Etiqueta(Track t) { return "A" + (t.Index + 1); }

    // ------------------------------------------------- lo que ya se limpio

    bool EsLimpio(Take t)
    {
        try { return t != null && t.Media != null && t.Media.FilePath != null && carpeta.Length > 0 &&
                     t.Media.FilePath.StartsWith(carpeta, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public const string SufijoLimpia = " \u00b7 limpia";

    // Modo pista: la pista nueva con el archivo limpio (va justo debajo de su original).
    bool EsPistaLimpia(Track t)
    {
        if (!t.IsAudio() || !(t.Name ?? "").EndsWith(SufijoLimpia)) return false;
        foreach (TrackEvent e in t.Events) if (!EsLimpio(e.ActiveTake)) return false;
        return true;
    }

    AudioTrack OriginalDe(Track limpia)
    {
        foreach (Track t in vegas.Project.Tracks) if (t.IsAudio() && t.Index == limpia.Index - 1) return (AudioTrack)t;
        return null;
    }

    AudioTrack LimpiaDe(Track original)
    {
        foreach (Track t in vegas.Project.Tracks) if (t.Index == original.Index + 1 && EsPistaLimpia(t)) return (AudioTrack)t;
        return null;
    }

    Take Original(TrackEvent e)
    {
        if (!EsLimpio(e.ActiveTake)) return e.ActiveTake;
        foreach (Take t in e.Takes) if (!EsLimpio(t)) return t;
        return null;
    }

    Take TomaLimpia(TrackEvent e)
    {
        foreach (Take t in e.Takes) if (EsLimpio(t)) return t;
        return null;
    }

    bool HayLimpias()
    {
        foreach (Track t in vegas.Project.Tracks)
        {
            if (!t.IsAudio()) continue;
            if (EsPistaLimpia(t)) return true;
            foreach (TrackEvent e in t.Events) if (TomaLimpia(e) != null) return true;
        }
        return false;
    }

    // Deja la pista como antes de limpiarla: sin su pista limpia, sin tomas
    // limpias y sonando otra vez.
    void Restaurar(AudioTrack pista)
    {
        AudioTrack limpia = LimpiaDe(pista);
        if (limpia != null) { vegas.Project.Tracks.Remove(limpia); pista.Mute = false; }
        foreach (TrackEvent e in pista.Events)
        {
            Take o = Original(e);
            List<Take> limpias = new List<Take>();
            foreach (Take t in e.Takes) if (EsLimpio(t)) limpias.Add(t);
            if (limpias.Count > 0 && o != null) e.ActiveTake = o;
            foreach (Take t in limpias) try { e.Takes.Remove(t); } catch { }
        }
    }

    // Pista de audio que usa la toma dentro de su archivo (0 = la primera). Se
    // compara por el numero del flujo: Vegas da objetos nuevos cada vez.
    static int IndiceAudio(Take t)
    {
        try
        {
            int buscado = PistasVegas.IndiceFlujo(t);
            int n = 0;
            foreach (MediaStream s in t.Media.Streams)
            {
                if (s.MediaType != MediaType.Audio) continue;
                object i = s.GetType().GetProperty("Index").GetValue(s, null);
                if (Convert.ToInt32(i) == buscado) return n;
                n++;
            }
        }
        catch { }
        return 0;
    }

    // ------------------------------------------------- limpiar

    void Limpiar()
    {
        List<AudioTrack> sel = Elegidas();
        if (sel.Count == 0) { Estado("Elige al menos una pista de voz.", true); return; }
        string ffmpeg = Ffmpeg();
        if (ffmpeg.Length == 0) { Estado("Falta ffmpeg: pulsa \u00abElegir\u2026\u00bb y busca ffmpeg.exe.", true); return; }
        OpcionesVoces op = new OpcionesVoces();
        op.LimiteRuido = Limites[segRuido.Seleccion];
        op.Nivelar = chipNivelar.Activo;
        op.Objetivo = numObjetivo.Valor;
        op.Pico = numPico.Valor;
        op.PorClip = segModo.Seleccion == 1;
        string df = config.DeepFilterExe.Length > 0 && File.Exists(config.DeepFilterExe) ? config.DeepFilterExe : "";
        List<string> etiquetas = new List<string>();
        foreach (AudioTrack t in sel) etiquetas.Add(Etiqueta(t));
        config.VocesPistas = String.Join(",", etiquetas.ToArray());
        config.VocesRuido = op.LimiteRuido.ToString(CultureInfo.InvariantCulture);
        config.VocesNivelar = op.Nivelar ? "si" : "no";
        config.VocesObjetivo = numObjetivo.Valor.ToString();
        config.VocesPico = numPico.Valor.ToString();
        config.VocesModo = op.PorClip ? "clip" : "pista";
        try { config.Guardar(); } catch { }

        // Lo que usa cada clip (en este hilo: la API de Vegas no es para otros
        // hilos). Si ya se habia limpiado, se parte de los originales.
        List<Clip> clips = new List<Clip>();
        foreach (AudioTrack pista in sel)
        {
            foreach (TrackEvent e in pista.Events)
            {
                if (e.Mute) continue; // silenciado por ti (p. ej. lo acelerado)
                Take t = Original(e);
                if (t == null || t.Media == null || String.IsNullOrEmpty(t.Media.FilePath) || !File.Exists(t.Media.FilePath)) continue;
                double off = t.Offset.ToMilliseconds() / 1000.0, largo = e.Length.ToMilliseconds() / 1000.0;
                Clip c = new Clip
                {
                    Evento = e, Pista = pista, En = e.Start.ToMilliseconds() / 1000.0, Largo = largo, Velocidad = e.PlaybackRate,
                    Uso = new UsoVoz { Archivo = t.Media.FilePath, Flujo = IndiceAudio(t), A = off, B = off + largo * e.PlaybackRate, Pista = Etiqueta(pista) }
                };
                try { c.FadeIn = e.FadeIn.Length.ToMilliseconds() / 1000.0; c.FadeOut = e.FadeOut.Length.ToMilliseconds() / 1000.0; } catch { }
                clips.Add(c);
            }
        }
        if (clips.Count == 0) { Estado("Esas pistas no tienen clips con audio.", true); return; }
        List<UsoVoz> lista = new List<UsoVoz>();
        foreach (Clip c in clips) lista.Add(c.Uso);
        List<TrozoVoz> trozos = LogicaVoces.Trozos(lista, 1.0, 3.0);
        LogicaVoces.Nombrar(trozos, carpeta);
        double total = 0;
        foreach (TrozoVoz t in trozos) total += t.Duracion;

        trabajando = true; cancelar = false;
        btnLimpiar.Text = "Cancelar";
        Control[] bloquear = { btnAlternar, btnCerrar, btnDf, btnFf, segModo, segRuido, chipNivelar, numObjetivo, numPico };
        foreach (Control c in bloquear) c.Enabled = false;
        barra.Valor = 0;
        DateTime inicio = DateTime.Now;
        string sello = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Thread hilo = new Thread(delegate ()
        {
            string error = null;
            Dictionary<string, double> ganancias = new Dictionary<string, double>();
            Dictionary<string, KeyValuePair<string, double>> archivos = new Dictionary<string, KeyValuePair<string, double>>();
            try
            {
                Procesar(ffmpeg, df, op, trozos, total, ganancias, inicio);
                if (!op.PorClip) ArmarPistas(clips, trozos, sello, archivos);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    btnLimpiar.Text = "Limpiar voces";
                    foreach (Control c in bloquear) c.Enabled = true;
                    if (cancelar) { Estado("Cancelado: no se cambi\u00f3 nada.", true); Mostrar(); return; }
                    if (error != null) { Estado("No se pudo: " + error, true); Mostrar(); return; }
                    Poner(sel, clips, trozos, archivos, ganancias, op, df.Length > 0 && op.LimiteRuido > 0, DateTime.Now - inicio);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Avance(string texto, double f)
    {
        try { BeginInvoke((MethodInvoker)delegate { Estado(texto, false); barra.Valor = f; }); } catch { }
    }

    string Correr(string exe, string args, string que)
    {
        int codigo;
        string salida = LogicaVoces.Ejecutar(exe, args, out codigo, delegate { return cancelar; });
        if (cancelar) throw new OperationCanceledException();
        if (codigo != 0)
        {
            string[] l = salida.Trim().Split('\n');
            throw new Exception(que + " fall\u00f3: " + (l.Length > 0 ? l[l.Length - 1].Trim() : "c\u00f3digo " + codigo));
        }
        return salida;
    }

    // En otro hilo: sacar, quitar ruido, nivelar y dejar todo al mismo volumen.
    void Procesar(string ffmpeg, string df, OpcionesVoces op, List<TrozoVoz> trozos, double total, Dictionary<string, double> ganancias, DateTime inicio)
    {
        Directory.CreateDirectory(Path.Combine(carpeta, "tmp"));
        int i = 0;
        foreach (TrozoVoz t in trozos)
        {
            Avance("Sacando lo que qued\u00f3 en el video\u2026 (" + (++i) + " de " + trozos.Count + ")", 0.08 * i / trozos.Count);
            Correr(ffmpeg, LogicaVoces.ArgsExtraer(t), "Sacar el audio de " + Path.GetFileName(t.Archivo));
        }

        bool quitar = df.Length > 0 && op.LimiteRuido > 0;
        if (quitar)
        {
            // Varios deep-filter a la vez (cada uno usa un nucleo), de a pocos archivos.
            string salida = Path.Combine(Path.Combine(carpeta, "tmp"), "df");
            Directory.CreateDirectory(salida);
            List<List<TrozoVoz>> grupos = new List<List<TrozoVoz>>();
            List<TrozoVoz> g = null;
            double enGrupo = 0;
            foreach (TrozoVoz t in trozos)
            {
                if (g == null || g.Count >= 8 || enGrupo > 300) { g = new List<TrozoVoz>(); grupos.Add(g); enGrupo = 0; }
                g.Add(t); enGrupo += t.Duracion;
            }
            double segHechos = 0;
            int siguiente = 0;
            string fallo = null;
            object candado = new object();
            int hilos = Math.Max(1, Math.Min(4, Environment.ProcessorCount / 2));
            List<Thread> trabajadores = new List<Thread>();
            for (int k = 0; k < hilos; k++)
            {
                Thread th = new Thread(delegate ()
                {
                    while (true)
                    {
                        List<TrozoVoz> mio;
                        lock (candado)
                        {
                            if (siguiente >= grupos.Count || fallo != null || cancelar) return;
                            mio = grupos[siguiente++];
                        }
                        try { Correr(df, LogicaVoces.ArgsDeepFilter(mio, op.LimiteRuido, salida), "DeepFilterNet"); }
                        catch (Exception ex) { lock (candado) { if (fallo == null) fallo = ex.Message; } return; }
                        lock (candado)
                        {
                            foreach (TrozoVoz t in mio) segHechos += t.Duracion;
                            double f = segHechos / Math.Max(1, total);
                            double pasado = (DateTime.Now - inicio).TotalSeconds;
                            string falta = f > 0.03 ? " \u00b7 faltan ~" + Formato.Tiempo(pasado / f * (1 - f)) : "";
                            Avance("Quitando el ruido: " + Formato.Tiempo(segHechos) + " de " + Formato.Tiempo(total) + falta, 0.08 + 0.72 * f);
                        }
                    }
                });
                th.IsBackground = true;
                trabajadores.Add(th);
                th.Start();
            }
            foreach (Thread th in trabajadores) th.Join();
            if (cancelar) throw new OperationCanceledException();
            if (fallo != null) throw new Exception(fallo);
        }

        i = 0;
        foreach (TrozoVoz t in trozos)
        {
            Avance("Emparejando el volumen\u2026 (" + (++i) + " de " + trozos.Count + ")", 0.80 + 0.08 * i / trozos.Count);
            string entrada = quitar && File.Exists(t.Sinruido) ? t.Sinruido : t.Entrada;
            t.Lufs = LogicaVoces.LeerLufs(Correr(ffmpeg, LogicaVoces.ArgsNivelar(entrada, t, op), "Nivelar"));
        }
        Dictionary<string, List<TrozoVoz>> porPista = new Dictionary<string, List<TrozoVoz>>();
        foreach (TrozoVoz t in trozos)
        {
            if (!porPista.ContainsKey(t.Pista)) porPista[t.Pista] = new List<TrozoVoz>();
            porPista[t.Pista].Add(t);
        }
        foreach (KeyValuePair<string, List<TrozoVoz>> kv in porPista)
            ganancias[kv.Key] = LogicaVoces.Ganancia(LogicaVoces.LufsPista(kv.Value), op.Objetivo);
        i = 0;
        foreach (TrozoVoz t in trozos)
        {
            Avance("Dejando todas al mismo volumen\u2026 (" + (++i) + " de " + trozos.Count + ")", 0.88 + 0.07 * i / trozos.Count);
            Correr(ffmpeg, LogicaVoces.ArgsFinal(t, ganancias[t.Pista], op.Pico), "Volumen final");
        }
        try { Directory.Delete(Path.Combine(carpeta, "tmp"), true); } catch { }
    }

    // En otro hilo: un archivo por pista con cada clip en su lugar (desde el primero).
    void ArmarPistas(List<Clip> clips, List<TrozoVoz> trozos, string sello, Dictionary<string, KeyValuePair<string, double>> archivos)
    {
        Dictionary<string, List<Clip>> porPista = new Dictionary<string, List<Clip>>();
        foreach (Clip c in clips)
        {
            if (Math.Abs(c.Velocidad - 1) > 0.001) continue;   // acelerado: se queda como estaba
            if (!porPista.ContainsKey(c.Uso.Pista)) porPista[c.Uso.Pista] = new List<Clip>();
            porPista[c.Uso.Pista].Add(c);
        }
        int k = 0;
        foreach (KeyValuePair<string, List<Clip>> kv in porPista)
        {
            if (cancelar) throw new OperationCanceledException();
            Avance("Armando la pista limpia de " + kv.Key + "\u2026", 0.95 + 0.05 * (++k) / porPista.Count);
            double ini = double.MaxValue, fin = 0;
            foreach (Clip c in kv.Value) { ini = Math.Min(ini, c.En); fin = Math.Max(fin, c.En + c.Largo); }
            List<PiezaPista> piezas = new List<PiezaPista>();
            foreach (Clip c in kv.Value)
            {
                TrozoVoz t = LogicaVoces.Buscar(trozos, c.Uso);
                if (t == null || !File.Exists(t.Final)) continue;
                piezas.Add(new PiezaPista { Archivo = t.Final, Desde = c.Uso.A - t.A, En = c.En - ini, Largo = c.Largo, FundidoEntrada = c.FadeIn, FundidoSalida = c.FadeOut });
            }
            string salida = Path.Combine(carpeta, kv.Key + " limpia " + sello + ".wav");
            LogicaVoces.ArmarPista(salida, piezas, fin - ini);
            archivos[kv.Key] = new KeyValuePair<string, double>(salida, ini);
        }
        // Los pedazos ya estan dentro de las pistas.
        foreach (TrozoVoz t in trozos) try { File.Delete(t.Final); } catch { }
    }

    // De vuelta en el hilo de Vegas.
    void Poner(List<AudioTrack> sel, List<Clip> clips, List<TrozoVoz> trozos, Dictionary<string, KeyValuePair<string, double>> archivos,
               Dictionary<string, double> ganancias, OpcionesVoces op, bool sinRuido, TimeSpan tardo)
    {
        int n = 0, faltan = 0;
        // La etiqueta de cada pista al empezar (al quitar o agregar pistas cambia el numero).
        Dictionary<AudioTrack, string> etiqueta = new Dictionary<AudioTrack, string>();
        foreach (Clip c in clips) etiqueta[c.Pista] = c.Uso.Pista;
        using (UndoBlock u = new UndoBlock("Limpiar voces"))
        {
            foreach (AudioTrack pista in sel) Restaurar(pista);
            if (op.PorClip)
            {
                Dictionary<string, Media> medios = new Dictionary<string, Media>();
                foreach (Clip c in clips)
                {
                    TrozoVoz t = LogicaVoces.Buscar(trozos, c.Uso);
                    if (t == null || !File.Exists(t.Final)) { faltan++; continue; }
                    try
                    {
                        Media md;
                        if (!medios.TryGetValue(t.Final, out md)) { md = new Media(t.Final); medios[t.Final] = md; }
                        Take nueva = c.Evento.AddTake(md.Streams.GetItemByMediaType(MediaType.Audio, 0), true);
                        nueva.Offset = Timecode.FromMilliseconds((c.Uso.A - t.A) * 1000);
                        n++;
                    }
                    catch { faltan++; }
                }
            }
            else
            {
                foreach (AudioTrack pista in sel)
                {
                    KeyValuePair<string, double> a;
                    string et;
                    if (!etiqueta.TryGetValue(pista, out et) || !archivos.TryGetValue(et, out a) || !File.Exists(a.Key)) continue;
                    // Pista nueva justo debajo, con el mismo volumen; la original queda silenciada.
                    string nombre = (String.IsNullOrEmpty(pista.Name) ? et : pista.Name) + SufijoLimpia;
                    AudioTrack nueva = new AudioTrack(pista.Index + 1, nombre);
                    vegas.Project.Tracks.Add(nueva);
                    try { nueva.Volume = pista.Volume; } catch { }
                    Media md = new Media(a.Key);
                    double largo = md.Length.ToMilliseconds() / 1000.0;
                    AudioEvent ev = nueva.AddAudioEvent(Timecode.FromMilliseconds(a.Value * 1000), Timecode.FromMilliseconds(largo * 1000));
                    ev.AddTake(md.Streams.GetItemByMediaType(MediaType.Audio, 0));
                    pista.Mute = true;
                    foreach (Clip c in clips) if (c.Pista == pista && Math.Abs(c.Velocidad - 1) <= 0.001) n++;
                }
            }
        }
        List<string> g = new List<string>();
        foreach (KeyValuePair<string, double> kv in ganancias)
            g.Add(kv.Key + " " + (kv.Value >= 0 ? "+" : "") + kv.Value.ToString("0.#") + " dB");
        Estado("\u2714 " + (op.PorClip ? n + " clips con la voz limpia" : archivos.Count + " pistas \u00ab\u00b7 limpia\u00bb nuevas (" + n + " clips; las originales quedaron silenciadas)") +
               (sinRuido ? ", sin ruido" : "") + ", en " + Formato.Tiempo(tardo.TotalSeconds) + ". Volumen: " + String.Join(", ", g.ToArray()) +
               (faltan > 0 ? ". " + faltan + " clips se quedaron como estaban" : "") +
               ". Ahora vuelve a TRANSCRIBIR (sobre lo limpio) y despu\u00e9s censura. \u00abEscuchar originales\u00bb compara; Ctrl+Z lo deshace.", faltan > 0);
        barra.Valor = 1;
        Mostrar();
        btnAlternar.Text = "Escuchar originales";
    }

    // Cambia entre lo limpio y lo original (pistas limpias y tomas limpias).
    void Alternar()
    {
        bool aOriginal = btnAlternar.Text.StartsWith("Escuchar originales");
        int n = 0;
        using (UndoBlock u = new UndoBlock(aOriginal ? "Voces originales" : "Voces limpias"))
            foreach (Track tr in vegas.Project.Tracks)
            {
                if (!tr.IsAudio()) continue;
                if (EsPistaLimpia(tr))
                {
                    tr.Mute = aOriginal;
                    AudioTrack o = OriginalDe(tr);
                    if (o != null) o.Mute = !aOriginal;
                    n++;
                    continue;
                }
                foreach (TrackEvent e in tr.Events)
                {
                    Take l = TomaLimpia(e), o = Original(e);
                    if (l == null) continue;
                    if (aOriginal && o != null && e.ActiveTake != o) { e.ActiveTake = o; n++; }
                    if (!aOriginal && e.ActiveTake != l) { e.ActiveTake = l; n++; }
                }
            }
        btnAlternar.Text = aOriginal ? "Escuchar limpias" : "Escuchar originales";
        Estado((aOriginal ? "Suenan los originales" : "Suena lo limpio") + " (" + n + ").", false);
    }
}

// ---- src/voces/LogicaVoces.cs ----

// =====================================================================
// Limpiar voces: quita el ruido (DeepFilterNet) y empareja el volumen
// (ffmpeg) SOLO de lo que quedo en el video. Por cada archivo de voz se
// sacan los pedazos que usan los eventos (con un margen), se limpian y se
// ponen como toma nueva de cada evento, en el mismo lugar: nada se mueve y
// el original queda como toma alternativa (tecla T en Vegas).
// =====================================================================

// Lo que un evento usa de su archivo (segundos del archivo).
public class UsoVoz
{
    public string Archivo = "";
    public int Flujo;          // pista de audio dentro del archivo (OBS graba varias)
    public double A, B;
    public string Pista = "";  // etiqueta de la pista de Vegas ("A2")
}

// Un pedazo continuo de un archivo que se limpia de una vez.
public class TrozoVoz
{
    public string Archivo = "", Pista = "";
    public int Flujo;
    public double A, B;
    public string Entrada = "", Sinruido = "", Nivelado = "", Final = "";
    public double Lufs = double.NaN;   // volumen medido despues de nivelar
    public double Duracion { get { return B - A; } }
}

public class OpcionesVoces
{
    public double LimiteRuido = 24;    // dB que puede bajar el ruido (0 = no quitar; 100 = todo)
    public bool Nivelar = true;        // emparejar frase por frase
    public double Objetivo = -20;      // LUFS de cada voz (con juego y musica encima, mas bajo que -16)
    public double Pico = -6;           // dBFS: ningun pico pasa de aqui
    public double Graves = 80;         // Hz: corta golpes y retumbes
    public bool PorClip;               // false: un solo archivo limpio por pista; true: toma nueva en cada clip
}

// Un clip dentro de la pista limpia: de que archivo limpio sale y donde va.
public class PiezaPista
{
    public string Archivo = "";
    public double Desde;       // segundo dentro del archivo limpio
    public double En, Largo;   // donde empieza en la pista (desde su inicio) y cuanto dura
    public double FundidoEntrada, FundidoSalida;
}

public static class LogicaVoces
{
    static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    // Junta lo que usan los eventos en pedazos por archivo: con un margen a cada
    // lado (para que el filtro tenga contexto) y unidos si quedan cerca.
    public static List<TrozoVoz> Trozos(List<UsoVoz> usos, double margen, double unir)
    {
        Dictionary<string, List<UsoVoz>> porArchivo = new Dictionary<string, List<UsoVoz>>();
        foreach (UsoVoz u in usos)
        {
            if (u.B - u.A < 0.01) continue;
            string k = u.Pista + "|" + u.Archivo.ToLowerInvariant() + "|" + u.Flujo;
            if (!porArchivo.ContainsKey(k)) porArchivo[k] = new List<UsoVoz>();
            porArchivo[k].Add(u);
        }
        List<TrozoVoz> r = new List<TrozoVoz>();
        foreach (List<UsoVoz> l in porArchivo.Values)
        {
            l.Sort(delegate (UsoVoz x, UsoVoz y) { return x.A.CompareTo(y.A); });
            TrozoVoz t = null;
            foreach (UsoVoz u in l)
            {
                double a = Math.Max(0, u.A - margen), b = u.B + margen;
                if (t != null && a <= t.B + unir) { t.B = Math.Max(t.B, b); continue; }
                t = new TrozoVoz { Archivo = u.Archivo, Flujo = u.Flujo, Pista = u.Pista, A = a, B = b };
                r.Add(t);
            }
        }
        return r;
    }

    // El pedazo que contiene lo que usa un evento.
    public static TrozoVoz Buscar(List<TrozoVoz> trozos, UsoVoz u)
    {
        foreach (TrozoVoz t in trozos)
            if (t.Pista == u.Pista && t.Flujo == u.Flujo && String.Equals(t.Archivo, u.Archivo, StringComparison.OrdinalIgnoreCase) &&
                u.A >= t.A - 0.001 && u.B <= t.B + 0.001) return t;
        return null;
    }

    // Nombres de los archivos de cada pedazo, dentro de la carpeta de trabajo.
    public static void Nombrar(List<TrozoVoz> trozos, string carpeta)
    {
        for (int i = 0; i < trozos.Count; i++)
        {
            TrozoVoz t = trozos[i];
            string baseNombre = Limpio(t.Pista + " " + Path.GetFileNameWithoutExtension(t.Archivo)) + " " + (i + 1).ToString("000") +
                                " " + ((int)t.A) + "s";
            t.Entrada = Path.Combine(carpeta, "tmp", baseNombre + ".wav");
            t.Sinruido = Path.Combine(carpeta, "tmp", "df", baseNombre + ".wav");
            t.Nivelado = Path.Combine(carpeta, "tmp", baseNombre + " nivelado.wav");
            t.Final = Path.Combine(carpeta, baseNombre + ".wav");
        }
    }

    static string Limpio(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        foreach (char c in "<>:\"|?*") s = s.Replace(c.ToString(), "");
        return s.Trim();
    }

    static string Q(string ruta) { return "\"" + ruta + "\""; }

    // 1) Sacar el pedazo: mono, 48 kHz (lo que usa DeepFilterNet).
    public static string ArgsExtraer(TrozoVoz t)
    {
        return "-hide_banner -v error -y -ss " + F(t.A) + " -t " + F(t.Duracion) + " -i " + Q(t.Archivo) +
               " -map 0:a:" + t.Flujo + " -ac 1 -ar 48000 -c:a pcm_s16le " + Q(t.Entrada);
    }

    // 2) Quitar el ruido (varios archivos de una vez; -D: sin retraso).
    public static string ArgsDeepFilter(List<TrozoVoz> trozos, double limite, string salida)
    {
        StringBuilder sb = new StringBuilder("-D -a " + F(limite) + " -o " + Q(salida));
        foreach (TrozoVoz t in trozos) sb.Append(" " + Q(t.Entrada));
        return sb.ToString();
    }

    // 3) Graves fuera y volumen parejo frase por frase; al final mide el volumen.
    public static string ArgsNivelar(string entrada, TrozoVoz t, OpcionesVoces op)
    {
        List<string> f = new List<string>();
        if (op.Graves > 0) f.Add("highpass=f=" + F(op.Graves));
        // Ventanas de ~0.2 s suavizadas en ~6 s: sube lo bajo y baja lo alto sin
        // aplastar gritos ni susurros; como mucho x5 (no sube el ruido de fondo).
        if (op.Nivelar) f.Add("dynaudnorm=f=200:g=31:p=0.5:m=5");
        f.Add("ebur128=framelog=quiet");
        return "-hide_banner -nostats -y -i " + Q(entrada) + " -af " + String.Join(",", f.ToArray()) +
               " -ar 48000 -c:a pcm_s16le " + Q(t.Nivelado);
    }

    // 4) La misma ganancia a todos los pedazos de la pista (para llegar al
    // objetivo), un tope para los picos y exactamente la duracion original.
    public static string ArgsFinal(TrozoVoz t, double ganancia, double pico)
    {
        double limite = Math.Max(0.0625, Math.Min(1, Math.Pow(10, pico / 20)));
        return "-hide_banner -v error -y -i " + Q(t.Nivelado) + " -af volume=" + F(ganancia) + "dB,alimiter=limit=" + F(limite) + ":level=false,apad" +
               " -t " + F(t.Duracion) + " -ar 48000 -c:a pcm_s16le " + Q(t.Final);
    }

    // El "I: -23.4 LUFS" del resumen de ebur128.
    public static double LeerLufs(string salida)
    {
        MatchCollection ms = Regex.Matches(salida ?? "", @"I:\s*(-?\d+(?:\.\d+)?)\s*LUFS");
        if (ms.Count == 0) return double.NaN;
        double v = double.Parse(ms[ms.Count - 1].Groups[1].Value, CultureInfo.InvariantCulture);
        return v <= -69 ? double.NaN : v;
    }

    // Volumen de toda la pista: promedio de energia pesado por duracion.
    public static double LufsPista(List<TrozoVoz> trozos)
    {
        double e = 0, d = 0;
        foreach (TrozoVoz t in trozos)
        {
            if (double.IsNaN(t.Lufs)) continue;
            e += t.Duracion * Math.Pow(10, t.Lufs / 10);
            d += t.Duracion;
        }
        return d <= 0 ? double.NaN : 10 * Math.Log10(e / d);
    }

    // Cuanto subir o bajar para llegar al objetivo (con limites razonables).
    public static double Ganancia(double lufs, double objetivo)
    {
        if (double.IsNaN(lufs)) return 0;
        return Math.Max(-20, Math.Min(20, objetivo - lufs));
    }

    // ------------------------------------------------- una sola pista

    const int Muestras = 48000;

    // Donde empiezan los datos de un WAV PCM de 16 bits (salta los otros bloques).
    static long DatosWav(FileStream f, out long bytes)
    {
        BinaryReader r = new BinaryReader(f);
        f.Position = 12;
        while (f.Position + 8 <= f.Length)
        {
            string id = new string(r.ReadChars(4));
            uint largo = r.ReadUInt32();
            if (id == "data") { bytes = Math.Min(largo, f.Length - f.Position); return f.Position; }
            f.Position += largo + (largo % 2);
        }
        throw new Exception("WAV sin datos: " + f.Name);
    }

    // Arma el archivo de la pista: cada clip en su lugar (mono, 48 kHz), con sus
    // fundidos; lo que se encima se suma. "largo" en segundos.
    public static void ArmarPista(string salida, List<PiezaPista> piezas, double largo)
    {
        long total = (long)Math.Ceiling(largo * Muestras);
        Dictionary<string, FileStream> abiertos = new Dictionary<string, FileStream>();
        Dictionary<string, long> inicioDatos = new Dictionary<string, long>(), finDatos = new Dictionary<string, long>();
        try
        {
            using (FileStream o = new FileStream(salida, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(o))
            {
                w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write((uint)(36 + total * 2));
                w.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16u);
                w.Write((ushort)1); w.Write((ushort)1); w.Write((uint)Muestras); w.Write((uint)(Muestras * 2));
                w.Write((ushort)2); w.Write((ushort)16);
                w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write((uint)(total * 2));
                const int Bloque = Muestras * 10;
                float[] mezcla = new float[Bloque];
                byte[] lectura = new byte[Bloque * 2];
                for (long b0 = 0; b0 < total; b0 += Bloque)
                {
                    int n = (int)Math.Min(Bloque, total - b0);
                    Array.Clear(mezcla, 0, n);
                    foreach (PiezaPista p in piezas)
                    {
                        long ini = (long)Math.Round(p.En * Muestras), len = (long)Math.Round(p.Largo * Muestras);
                        long a = Math.Max(ini, b0), z = Math.Min(ini + len, b0 + n);
                        if (z <= a) continue;
                        FileStream f;
                        if (!abiertos.TryGetValue(p.Archivo, out f))
                        {
                            f = new FileStream(p.Archivo, FileMode.Open, FileAccess.Read, FileShare.Read);
                            abiertos[p.Archivo] = f;
                            long bytes;
                            inicioDatos[p.Archivo] = DatosWav(f, out bytes);
                            finDatos[p.Archivo] = inicioDatos[p.Archivo] + bytes;
                        }
                        long desde = (long)Math.Round(p.Desde * Muestras) + (a - ini);
                        long pos = inicioDatos[p.Archivo] + desde * 2;
                        int cuantas = (int)Math.Max(0, Math.Min(z - a, (finDatos[p.Archivo] - pos) / 2));
                        if (cuantas <= 0 || pos < inicioDatos[p.Archivo]) continue;
                        f.Position = pos;
                        int leidos = 0;
                        while (leidos < cuantas * 2) { int k = f.Read(lectura, leidos, cuantas * 2 - leidos); if (k <= 0) break; leidos += k; }
                        double fe = p.FundidoEntrada * Muestras, fs = p.FundidoSalida * Muestras;
                        for (int i = 0; i < leidos / 2; i++)
                        {
                            long enPieza = a - ini + i;
                            double g = 1;
                            if (fe > 1 && enPieza < fe) g = enPieza / fe;
                            if (fs > 1 && len - enPieza < fs) g = Math.Min(g, (len - enPieza) / fs);
                            mezcla[a - b0 + i] += (float)(g * (short)(lectura[2 * i] | (lectura[2 * i + 1] << 8)));
                        }
                    }
                    byte[] sal = new byte[n * 2];
                    for (int i = 0; i < n; i++)
                    {
                        int v = (int)Math.Round(Math.Max(-32768, Math.Min(32767, mezcla[i])));
                        sal[2 * i] = (byte)(v & 0xFF); sal[2 * i + 1] = (byte)((v >> 8) & 0xFF);
                    }
                    w.Write(sal);
                }
            }
        }
        finally { foreach (FileStream f in abiertos.Values) f.Dispose(); }
    }

    // ------------------------------------------------- programas externos

    // ffmpeg: el configurado, el que viene con Whisper o el del PATH.
    public static string BuscarFfmpeg(string configurado, string whisperExe)
    {
        List<string> c = new List<string>();
        if (!String.IsNullOrEmpty(configurado)) c.Add(configurado);
        if (!String.IsNullOrEmpty(whisperExe))
        {
            string d = Path.GetDirectoryName(whisperExe) ?? "";
            c.Add(Path.Combine(d, "ffmpeg.exe"));
            c.Add(Path.Combine(Path.Combine(d, "_xxl_data"), "ffmpeg.exe"));
        }
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Trim().Length == 0) continue;
            try { c.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe")); c.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg")); } catch { }
        }
        foreach (string f in c) if (File.Exists(f)) return f;
        return "";
    }

    // Ejecuta un programa sin ventana; devuelve lo que escribio (salida y errores).
    public static string Ejecutar(string exe, string args, out int codigo, Func<bool> cancelar)
    {
        ProcessStartInfo info = new ProcessStartInfo(exe, args);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;
        StringBuilder sb = new StringBuilder();
        using (Process p = new Process())
        {
            p.StartInfo = info;
            p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            while (!p.WaitForExit(200))
                if (cancelar != null && cancelar()) { try { p.Kill(); } catch { } p.WaitForExit(); codigo = -1; return "cancelado"; }
            p.WaitForExit();
            codigo = p.ExitCode;
        }
        lock (sb) return sb.ToString();
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

    // Copia lo que hay entre a y b (todas las pistas) a "destino". Los
    // pedazos que estaban agrupados quedan agrupados entre si.
    public static int CopiarTramo(Project p, double a, double b, double destino)
    {
        List<TrackEvent> origen = new List<TrackEvent>();
        foreach (Track t in p.Tracks)
            foreach (TrackEvent e in t.Events)
                if (S(e.Start) < b - 0.001 && S(e.End) > a + 0.001) origen.Add(e);
        Dictionary<TrackEventGroup, TrackEventGroup> grupos = new Dictionary<TrackEventGroup, TrackEventGroup>();
        int n = 0;
        foreach (TrackEvent e in origen)
        {
            double ini = Math.Max(a, S(e.Start)), fin = Math.Min(b, S(e.End));
            double recorte = ini - S(e.Start);
            double offset = e.ActiveTake != null ? S(e.ActiveTake.Offset) : 0;
            TrackEvent c = e.Copy(e.Track, TC(destino + ini - a));
            c.Length = TC(fin - ini);
            if (c.ActiveTake != null && recorte > 0) c.ActiveTake.Offset = TC(offset + recorte * e.PlaybackRate);
            try { c.FadeIn.Length = TC(0); c.FadeOut.Length = TC(0); } catch { }
            n++;
            if (e.IsGrouped)
            {
                TrackEventGroup g;
                if (!grupos.TryGetValue(e.Group, out g)) { g = Editor.NuevoGrupo(p); grupos[e.Group] = g; }
                if (!c.IsGrouped) g.Add(c);
            }
        }
        return n;
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
    public string CarpetaMemesEntrada = "";      // carpeta con los archivos por revisar (programa Memes)
    public string MemesOrden = "recientes";      // orden de lo que falta revisar: "recientes" o "nombre"
    public string MemesOrdenLista = "recientes"; // orden de la biblioteca: "recientes", "nombre" o "usados"
    public string MemesSubcarpeta = "";          // subcarpeta donde se guardo el ultimo meme
    public string DeepFilterExe = "";            // deep-filter-...-windows-msvc.exe (quitar ruido de las voces)
    public string FfmpegExe = "";                // "" = el de Whisper o el del PATH
    public string VocesPistas = "";              // etiquetas de las pistas de voz a limpiar ("A2,A3")
    public string VocesRuido = "24";             // dB que puede bajar el ruido (0, 12, 24, 100)
    public string VocesNivelar = "si";
    public string VocesObjetivo = "-20";         // LUFS
    public string VocesPico = "";                // dBFS ("" = aun sin elegir: -6)
    public string VocesModo = "pista";           // "pista": un archivo limpio por pista; "clip": toma nueva en cada clip

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
            c.CarpetaMemesEntrada = Json.Texto(o, "carpetaMemesEntrada");
            c.MemesOrden = Valor(Json.Texto(o, "memesOrden"), c.MemesOrden);
            c.MemesOrdenLista = Valor(Json.Texto(o, "memesOrdenLista"), c.MemesOrdenLista);
            c.MemesSubcarpeta = Json.Texto(o, "memesSubcarpeta");
            c.DeepFilterExe = Json.Texto(o, "deepFilterExe");
            c.FfmpegExe = Json.Texto(o, "ffmpegExe");
            c.VocesPistas = Json.Texto(o, "vocesPistas");
            c.VocesRuido = Valor(Json.Texto(o, "vocesRuido"), c.VocesRuido);
            c.VocesNivelar = Valor(Json.Texto(o, "vocesNivelar"), c.VocesNivelar);
            c.VocesObjetivo = Valor(Json.Texto(o, "vocesObjetivo"), c.VocesObjetivo);
            c.VocesPico = Json.Texto(o, "vocesPico");
            c.VocesModo = Valor(Json.Texto(o, "vocesModo"), c.VocesModo);
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
        d["carpetaMemesEntrada"] = CarpetaMemesEntrada;
        d["memesOrden"] = MemesOrden;
        d["memesOrdenLista"] = MemesOrdenLista;
        d["memesSubcarpeta"] = MemesSubcarpeta;
        d["deepFilterExe"] = DeepFilterExe;
        d["ffmpegExe"] = FfmpegExe;
        d["vocesPistas"] = VocesPistas;
        d["vocesRuido"] = VocesRuido;
        d["vocesNivelar"] = VocesNivelar;
        d["vocesObjetivo"] = VocesObjetivo;
        d["vocesPico"] = VocesPico;
        d["vocesModo"] = VocesModo;
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
// Es "partial" para que los scripts que hablan con Gemini le agreguen el aviso
// de espera (comun/AvisoGemini.cs); los demas no lo llevan.
partial class VentanaBase : Form
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
        Extras();
    }

    partial void Extras();

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
