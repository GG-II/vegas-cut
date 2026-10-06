using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Ventana de Limpiar voces (tambien es el primer paso de PasoFinal).
// Dos formas de dejar lo limpio:
//  - Una pista limpia (por defecto): un archivo por pista de voz, en un solo
//    evento en esa misma pista; los clips originales quedan silenciados debajo.
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
    Boton btnDf = new Boton("Elegir…", EstiloBoton.Secundario), btnFf = new Boton("Elegir…", EstiloBoton.Secundario);
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
        Encabezado("Limpiar voces", "Quita el ruido y empareja el volumen de lo que quedó en el video. Después vuelve a transcribir y censura.");
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
        Texto("Una pista: un archivo limpio por voz; los clips quedan silenciados debajo. Por clip: toma nueva en cada uno.",
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
        Texto("Cada pista elegida queda a ese volumen y ningún pico pasa del tope. «Frase por frase» sube lo que se grabó bajo y baja lo que se grabó alto.",
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
        if (carpeta.Length == 0) { btnLimpiar.Enabled = false; Estado("Guarda el proyecto primero (los archivos limpios van junto a él).", true); }
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
        lblDf.Text = df ? Path.GetFileName(config.DeepFilterExe) : "Falta DeepFilterNet: elige deep-filter-…-windows-msvc.exe (sin él solo se empareja el volumen).";
        lblDf.ForeColor = df ? Tema.Texto : Tema.AcentoHover;
        string ff = Ffmpeg();
        lblFf.Text = ff.Length > 0 ? ff : "No encontré ffmpeg: elige ffmpeg.exe.";
        lblFf.ForeColor = ff.Length > 0 ? Tema.Texto : Tema.Silencio;
        btnAlternar.Enabled = HayLimpias();
    }

    void ElegirExe(bool deepFilter)
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Filter = "Programa (*.exe)|*.exe";
            d.Title = deepFilter ? "deep-filter-…-x86_64-pc-windows-msvc.exe" : "ffmpeg.exe";
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (deepFilter) config.DeepFilterExe = d.FileName; else config.FfmpegExe = d.FileName;
        }
        try { config.Guardar(); } catch { }
        Mostrar();
    }

    List<AudioTrack> Elegidas()
    {
        List<AudioTrack> r = new List<AudioTrack>();
        foreach (Boton c in chips) if (c.Activo) r.Add(((InfoPista)c.Tag).Pista);
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

    // Modo pista: el evento con el archivo limpio de toda la pista.
    bool EsPistaLimpia(TrackEvent e) { return EsLimpio(e.ActiveTake) && e.Takes.Count == 1; }

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
            if (t.IsAudio()) foreach (TrackEvent e in t.Events) if (TomaLimpia(e) != null) return true;
        return false;
    }

    // Clips que este script silencio en modo pista (por pista: inicio|largo en ms).
    string RutaEstado { get { return Path.Combine(carpeta, "silenciados.json"); } }

    Dictionary<string, List<string>> LeerEstado()
    {
        Dictionary<string, List<string>> r = new Dictionary<string, List<string>>();
        try
        {
            if (!File.Exists(RutaEstado)) return r;
            Dictionary<string, object> o = Json.Leer(File.ReadAllText(RutaEstado)) as Dictionary<string, object>;
            if (o != null)
                foreach (KeyValuePair<string, object> kv in o)
                {
                    List<string> l = new List<string>();
                    foreach (object x in Json.Lista(o, kv.Key)) if (x is string) l.Add((string)x);
                    r[kv.Key] = l;
                }
        }
        catch { }
        return r;
    }

    void GuardarEstado(Dictionary<string, List<string>> estado)
    {
        Dictionary<string, object> o = new Dictionary<string, object>();
        foreach (KeyValuePair<string, List<string>> kv in estado) o[kv.Key] = new List<object>(kv.Value.ConvertAll(delegate (string x) { return (object)x; }));
        try { Directory.CreateDirectory(carpeta); File.WriteAllText(RutaEstado, Json.Escribir(o)); } catch { }
    }

    static string Clave(TrackEvent e)
    {
        return ((long)Math.Round(e.Start.ToMilliseconds())).ToString(CultureInfo.InvariantCulture) + "|" +
               ((long)Math.Round(e.Length.ToMilliseconds())).ToString(CultureInfo.InvariantCulture);
    }

    // Deja la pista como antes de limpiarla: sin el evento limpio, sin tomas
    // limpias y con los clips que se silenciaron sonando otra vez.
    void Restaurar(AudioTrack pista, Dictionary<string, List<string>> estado)
    {
        List<TrackEvent> quitar = new List<TrackEvent>();
        foreach (TrackEvent e in pista.Events)
        {
            if (EsPistaLimpia(e)) { quitar.Add(e); continue; }
            Take o = Original(e);
            List<Take> limpias = new List<Take>();
            foreach (Take t in e.Takes) if (EsLimpio(t)) limpias.Add(t);
            if (limpias.Count > 0 && o != null) e.ActiveTake = o;
            foreach (Take t in limpias) try { e.Takes.Remove(t); } catch { }
        }
        foreach (TrackEvent e in quitar) pista.Events.Remove(e);
        List<string> silenciados;
        if (estado.TryGetValue(Etiqueta(pista), out silenciados))
            foreach (TrackEvent e in pista.Events)
                if (silenciados.Contains(Clave(e))) e.Mute = false;
        estado.Remove(Etiqueta(pista));
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
        if (ffmpeg.Length == 0) { Estado("Falta ffmpeg: pulsa «Elegir…» y busca ffmpeg.exe.", true); return; }
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
        Dictionary<string, List<string>> estado = LeerEstado();
        List<string> yaSilenciados = new List<string>();
        foreach (List<string> l in estado.Values) yaSilenciados.AddRange(l);
        List<Clip> clips = new List<Clip>();
        foreach (AudioTrack pista in sel)
        {
            List<string> nuestros;
            estado.TryGetValue(Etiqueta(pista), out nuestros);
            foreach (TrackEvent e in pista.Events)
            {
                if (EsPistaLimpia(e)) continue;
                if (e.Mute && (nuestros == null || !nuestros.Contains(Clave(e)))) continue; // silenciado por ti (p. ej. lo acelerado)
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
                    if (cancelar) { Estado("Cancelado: no se cambió nada.", true); Mostrar(); return; }
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
            throw new Exception(que + " falló: " + (l.Length > 0 ? l[l.Length - 1].Trim() : "código " + codigo));
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
            Avance("Sacando lo que quedó en el video… (" + (++i) + " de " + trozos.Count + ")", 0.08 * i / trozos.Count);
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
                            string falta = f > 0.03 ? " · faltan ~" + Formato.Tiempo(pasado / f * (1 - f)) : "";
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
            Avance("Emparejando el volumen… (" + (++i) + " de " + trozos.Count + ")", 0.80 + 0.08 * i / trozos.Count);
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
            Avance("Dejando todas al mismo volumen… (" + (++i) + " de " + trozos.Count + ")", 0.88 + 0.07 * i / trozos.Count);
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
            Avance("Armando la pista limpia de " + kv.Key + "…", 0.95 + 0.05 * (++k) / porPista.Count);
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
        Dictionary<string, List<string>> estado = LeerEstado();
        using (UndoBlock u = new UndoBlock("Limpiar voces"))
        {
            foreach (AudioTrack pista in sel) Restaurar(pista, estado);
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
                    if (!archivos.TryGetValue(Etiqueta(pista), out a) || !File.Exists(a.Key)) continue;
                    Media md = new Media(a.Key);
                    double largo = md.Length.ToMilliseconds() / 1000.0;
                    AudioEvent ev = pista.AddAudioEvent(Timecode.FromMilliseconds(a.Value * 1000), Timecode.FromMilliseconds(largo * 1000));
                    ev.AddTake(md.Streams.GetItemByMediaType(MediaType.Audio, 0));
                    List<string> silenciados = new List<string>();
                    foreach (Clip c in clips)
                    {
                        if (c.Pista != pista || Math.Abs(c.Velocidad - 1) > 0.001) continue;
                        if (!c.Evento.Mute) { c.Evento.Mute = true; silenciados.Add(Clave(c.Evento)); }
                        n++;
                    }
                    estado[Etiqueta(pista)] = silenciados;
                }
            }
        }
        GuardarEstado(estado);
        List<string> g = new List<string>();
        foreach (KeyValuePair<string, double> kv in ganancias)
            g.Add(kv.Key + " " + (kv.Value >= 0 ? "+" : "") + kv.Value.ToString("0.#") + " dB");
        Estado("✔ " + (op.PorClip ? n + " clips con la voz limpia" : archivos.Count + " pistas limpias (" + n + " clips debajo, silenciados)") +
               (sinRuido ? ", sin ruido" : "") + ", en " + Formato.Tiempo(tardo.TotalSeconds) + ". Volumen: " + String.Join(", ", g.ToArray()) +
               (faltan > 0 ? ". " + faltan + " clips se quedaron como estaban" : "") +
               ". Ahora vuelve a TRANSCRIBIR (sobre lo limpio) y después censura. «Escuchar originales» compara; Ctrl+Z lo deshace.", faltan > 0);
        barra.Valor = 1;
        Mostrar();
        btnAlternar.Text = "Escuchar originales";
    }

    // Cambia las pistas elegidas entre lo limpio y lo original.
    void Alternar()
    {
        bool aOriginal = btnAlternar.Text.StartsWith("Escuchar originales");
        Dictionary<string, List<string>> estado = LeerEstado();
        int n = 0;
        using (UndoBlock u = new UndoBlock(aOriginal ? "Voces originales" : "Voces limpias"))
            foreach (Track tr in vegas.Project.Tracks)
            {
                if (!tr.IsAudio()) continue;
                List<string> silenciados;
                estado.TryGetValue(Etiqueta(tr), out silenciados);
                foreach (TrackEvent e in tr.Events)
                {
                    if (EsPistaLimpia(e)) { e.Mute = aOriginal; n++; continue; }
                    if (silenciados != null && silenciados.Contains(Clave(e))) { e.Mute = !aOriginal; continue; }
                    Take l = TomaLimpia(e), o = Original(e);
                    if (l == null) continue;
                    if (aOriginal && o != null && e.ActiveTake != o) { e.ActiveTake = o; n++; }
                    if (!aOriginal && e.ActiveTake != l) { e.ActiveTake = l; n++; }
                }
            }
        btnAlternar.Text = aOriginal ? "Escuchar limpias" : "Escuchar originales";
        Estado((aOriginal ? "Suenan los originales" : "Suena lo limpio") + " (" + n + " eventos).", false);
    }
}
