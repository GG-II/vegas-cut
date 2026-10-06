using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Ventana de Limpiar voces (tambien es el primer paso de PasoFinal).
class VentanaVoces : VentanaBase
{
    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly List<InfoPista> pistas;
    readonly List<Boton> chips = new List<Boton>();
    readonly string carpeta;

    Etiqueta lblDf, lblFf, lblEstado;
    Boton btnDf = new Boton("Elegir…", EstiloBoton.Secundario), btnFf = new Boton("Elegir…", EstiloBoton.Secundario);
    Segmentado segRuido = new Segmentado(new string[] { "No quitar", "Suave", "Medio", "Fuerte" });
    Boton chipNivelar = new Boton("Emparejar frase por frase", EstiloBoton.Chip);
    CampoNumero numObjetivo = new CampoNumero();
    BarraProgreso barra = new BarraProgreso();
    Boton btnLimpiar = new Boton("Limpiar voces", EstiloBoton.Primario);
    Boton btnAlternar = new Boton("Escuchar originales", EstiloBoton.Secundario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);
    static readonly double[] Limites = { 0, 12, 24, 100 };

    volatile bool cancelar;
    bool trabajando;

    public VentanaVoces(Vegas vegas) : base("Limpiar voces", 760)
    {
        this.vegas = vegas;
        StartPosition = FormStartPosition.CenterParent;
        string veg = vegas.Project.FilePath ?? "";
        carpeta = veg.Length > 0 ? Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-voces") : "";
        pistas = PistasVegas.Listar(vegas.Project);
        int m = Margen, w = Ancho;
        Encabezado("Limpiar voces", "Quita el ruido y empareja el volumen de lo que quedó en el video. Después de cortar, antes de censurar.");
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

        Texto("Ruido", Tema.Negrita, Tema.Texto, m, y + 8, 90, 20);
        Pos(segRuido, m + 90, y, 420, 34);
        double lim;
        double.TryParse(config.VocesRuido, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lim);
        segRuido.Seleccion = Math.Max(0, Array.IndexOf(Limites, lim));
        y += 42;
        Texto("Volumen", Tema.Negrita, Tema.Texto, m, y + 8, 90, 20);
        numObjetivo.Sufijo = ""; numObjetivo.Minimo = -24; numObjetivo.Maximo = -10; numObjetivo.Paso = 1;
        int obj;
        numObjetivo.Valor = int.TryParse(config.VocesObjetivo, out obj) ? obj : -16;
        Pos(numObjetivo, m + 90, y, 100, 34);
        Texto("LUFS", Tema.Pequena, Tema.TextoSuave, m + 196, y + 9, 40, 18);
        Pos(chipNivelar, m + 244, y + 3, 220, 28);
        chipNivelar.Activo = config.VocesNivelar != "no";
        chipNivelar.Click += delegate { chipNivelar.Activo = !chipNivelar.Activo; };
        Texto("-16 LUFS es lo de YouTube; todas las pistas elegidas quedan a ese volumen. «Frase por frase» sube lo que se grabó bajo y baja lo que se grabó alto.",
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

    // ------------------------------------------------- tomas

    bool EsLimpia(Take t)
    {
        try { return t != null && t.Media != null && t.Media.FilePath != null && carpeta.Length > 0 &&
                     t.Media.FilePath.StartsWith(carpeta, StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    Take Original(TrackEvent e)
    {
        if (!EsLimpia(e.ActiveTake)) return e.ActiveTake;
        foreach (Take t in e.Takes) if (!EsLimpia(t)) return t;
        return null;
    }

    Take Limpia(TrackEvent e)
    {
        foreach (Take t in e.Takes) if (EsLimpia(t)) return t;
        return null;
    }

    bool HayLimpias()
    {
        foreach (Track t in vegas.Project.Tracks)
            if (t.IsAudio()) foreach (TrackEvent e in t.Events) if (Limpia(e) != null) return true;
        return false;
    }

    // Pista de audio que usa la toma dentro de su archivo (0 = la primera).
    static int IndiceAudio(Take t)
    {
        try
        {
            object flujo = t.GetType().GetProperty("MediaStream").GetValue(t, null);
            int n = 0;
            foreach (MediaStream s in t.Media.Streams)
            {
                if (s.MediaType != MediaType.Audio) continue;
                if (Object.ReferenceEquals(s, flujo)) return n;
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
        string df = config.DeepFilterExe.Length > 0 && File.Exists(config.DeepFilterExe) ? config.DeepFilterExe : "";
        List<string> etiquetas = new List<string>();
        foreach (AudioTrack t in sel) etiquetas.Add("A" + (t.Index + 1));
        config.VocesPistas = String.Join(",", etiquetas.ToArray());
        config.VocesRuido = op.LimiteRuido.ToString(System.Globalization.CultureInfo.InvariantCulture);
        config.VocesNivelar = op.Nivelar ? "si" : "no";
        config.VocesObjetivo = numObjetivo.Valor.ToString();
        try { config.Guardar(); } catch { }

        // Lo que usa cada evento (en este hilo: la API de Vegas no es para otros hilos).
        List<KeyValuePair<TrackEvent, UsoVoz>> usos = new List<KeyValuePair<TrackEvent, UsoVoz>>();
        foreach (AudioTrack pista in sel)
            foreach (TrackEvent e in pista.Events)
            {
                if (e.Mute) continue;
                Take t = Original(e);
                if (t == null || t.Media == null || String.IsNullOrEmpty(t.Media.FilePath) || !File.Exists(t.Media.FilePath)) continue;
                double off = t.Offset.ToMilliseconds() / 1000.0, largo = e.Length.ToMilliseconds() / 1000.0 * e.PlaybackRate;
                usos.Add(new KeyValuePair<TrackEvent, UsoVoz>(e, new UsoVoz
                {
                    Archivo = t.Media.FilePath, Flujo = IndiceAudio(t), A = off, B = off + largo, Pista = "A" + (pista.Index + 1)
                }));
            }
        if (usos.Count == 0) { Estado("Esas pistas no tienen eventos con audio.", true); return; }
        List<UsoVoz> lista = new List<UsoVoz>();
        foreach (KeyValuePair<TrackEvent, UsoVoz> kv in usos) lista.Add(kv.Value);
        List<TrozoVoz> trozos = LogicaVoces.Trozos(lista, 1.0, 3.0);
        LogicaVoces.Nombrar(trozos, carpeta);
        double total = 0;
        foreach (TrozoVoz t in trozos) total += t.Duracion;

        trabajando = true; cancelar = false;
        btnLimpiar.Text = "Cancelar";
        foreach (Control c in new Control[] { btnAlternar, btnCerrar, btnDf, btnFf, segRuido, chipNivelar, numObjetivo }) c.Enabled = false;
        barra.Valor = 0;
        DateTime inicio = DateTime.Now;
        Thread hilo = new Thread(delegate ()
        {
            string error = null;
            Dictionary<string, double> ganancias = new Dictionary<string, double>();
            try { Procesar(ffmpeg, df, op, trozos, total, ganancias, inicio); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    btnLimpiar.Text = "Limpiar voces";
                    foreach (Control c in new Control[] { btnAlternar, btnCerrar, btnDf, btnFf, segRuido, chipNivelar, numObjetivo }) c.Enabled = true;
                    if (cancelar) { Estado("Cancelado: no se cambió nada.", true); Mostrar(); return; }
                    if (error != null) { Estado("No se pudo: " + error, true); Mostrar(); return; }
                    Poner(usos, trozos, ganancias, df.Length > 0 && op.LimiteRuido > 0, DateTime.Now - inicio);
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
            int hechos = 0;
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
                            hechos++;
                            foreach (TrozoVoz t in mio) segHechos += t.Duracion;
                            double f = segHechos / Math.Max(1, total);
                            double pasado = (DateTime.Now - inicio).TotalSeconds;
                            string falta = f > 0.03 ? " · faltan ~" + Formato.Tiempo(pasado / f * (1 - f)) : "";
                            Avance("Quitando el ruido: " + Formato.Tiempo(segHechos) + " de " + Formato.Tiempo(total) + falta, 0.08 + 0.77 * f);
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
            Avance("Emparejando el volumen… (" + (++i) + " de " + trozos.Count + ")", 0.85 + 0.08 * i / trozos.Count);
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
            Avance("Dejando todas al mismo volumen… (" + (++i) + " de " + trozos.Count + ")", 0.93 + 0.07 * i / trozos.Count);
            Correr(ffmpeg, LogicaVoces.ArgsFinal(t, ganancias[t.Pista]), "Volumen final");
        }
        try { Directory.Delete(Path.Combine(carpeta, "tmp"), true); } catch { }
    }

    // De vuelta en el hilo de Vegas: cada evento usa lo limpio como toma nueva.
    void Poner(List<KeyValuePair<TrackEvent, UsoVoz>> usos, List<TrozoVoz> trozos, Dictionary<string, double> ganancias, bool sinRuido, TimeSpan tardo)
    {
        int n = 0, faltan = 0;
        Dictionary<string, Media> medios = new Dictionary<string, Media>();
        using (UndoBlock u = new UndoBlock("Limpiar voces"))
            foreach (KeyValuePair<TrackEvent, UsoVoz> kv in usos)
            {
                TrozoVoz t = LogicaVoces.Buscar(trozos, kv.Value);
                if (t == null || !File.Exists(t.Final)) { faltan++; continue; }
                try
                {
                    TrackEvent e = kv.Key;
                    Take orig = Original(e);
                    List<Take> viejas = new List<Take>();
                    foreach (Take x in e.Takes) if (EsLimpia(x)) viejas.Add(x);
                    if (orig != null) e.ActiveTake = orig;
                    foreach (Take x in viejas) try { e.Takes.Remove(x); } catch { }
                    Media md;
                    if (!medios.TryGetValue(t.Final, out md)) { md = new Media(t.Final); medios[t.Final] = md; }
                    Take nueva = e.AddTake(md.Streams.GetItemByMediaType(MediaType.Audio, 0), true);
                    nueva.Offset = Timecode.FromMilliseconds((kv.Value.A - t.A) * 1000);
                    n++;
                }
                catch { faltan++; }
            }
        List<string> g = new List<string>();
        foreach (KeyValuePair<string, double> kv in ganancias)
            g.Add(kv.Key + " " + (kv.Value >= 0 ? "+" : "") + kv.Value.ToString("0.#") + " dB");
        Estado("✔ " + n + " eventos con la voz limpia" + (sinRuido ? " (sin ruido)" : "") + " en " + Formato.Tiempo(tardo.TotalSeconds) +
               ". Volumen corregido: " + String.Join(", ", g.ToArray()) + "." + (faltan > 0 ? " " + faltan + " eventos se quedaron como estaban." : "") +
               " El original queda como toma alternativa (tecla T o «Escuchar originales»). Ctrl+Z lo deshace.", faltan > 0);
        barra.Valor = 1;
        Mostrar();
        btnAlternar.Text = "Escuchar originales";
    }

    // Cambia todas las pistas elegidas entre lo limpio y lo original.
    void Alternar()
    {
        bool aOriginal = btnAlternar.Text.StartsWith("Escuchar originales");
        int n = 0;
        using (UndoBlock u = new UndoBlock(aOriginal ? "Voces originales" : "Voces limpias"))
            foreach (Track tr in vegas.Project.Tracks)
            {
                if (!tr.IsAudio()) continue;
                foreach (TrackEvent e in tr.Events)
                {
                    Take l = Limpia(e), o = Original(e);
                    if (l == null) continue;
                    if (aOriginal && o != null && e.ActiveTake != o) { e.ActiveTake = o; n++; }
                    if (!aOriginal && e.ActiveTake != l) { e.ActiveTake = l; n++; }
                }
            }
        btnAlternar.Text = aOriginal ? "Escuchar limpias" : "Escuchar originales";
        Estado(n + " eventos con la voz " + (aOriginal ? "original" : "limpia") + ".", false);
    }
}
