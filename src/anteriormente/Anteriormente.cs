using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        if (String.IsNullOrEmpty(vegas.Project.FilePath))
        {
            MessageBox.Show("Guarda el proyecto primero.", "Anteriormente");
            return;
        }
        using (VentanaAnteriormente v = new VentanaAnteriormente(vegas)) v.ShowDialog();
    }
}

// Lo que toca la linea de tiempo.
public static class InsertarAnteriormente
{
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Primer instante con algo en la linea de tiempo (MaxValue si esta vacia).
    public static double PrimerEvento(Project p)
    {
        double r = double.MaxValue;
        foreach (Track t in p.Tracks) foreach (TrackEvent e in t.Events) r = Math.Min(r, S(e.Start));
        return r;
    }

    // Corre todo (eventos, marcadores y regiones) "segundos" a la derecha.
    public static void Desplazar(Project p, double segundos)
    {
        if (segundos <= 0) return;
        List<TrackEvent> eventos = new List<TrackEvent>();
        foreach (Track t in p.Tracks) foreach (TrackEvent e in t.Events) eventos.Add(e);
        // De derecha a izquierda para que nada se encime al moverse.
        eventos.Sort(delegate (TrackEvent a, TrackEvent b) { return S(b.Start).CompareTo(S(a.Start)); });
        foreach (TrackEvent e in eventos) e.Start = TC(S(e.Start) + segundos);
        List<Marker> marcas = new List<Marker>();
        foreach (Marker m in p.Markers) marcas.Add(m);
        foreach (Region r in p.Regions) marcas.Add(r);
        marcas.Sort(delegate (Marker a, Marker b) { return S(b.Position).CompareTo(S(a.Position)); });
        foreach (Marker m in marcas) try { m.Position = TC(S(m.Position) + segundos); } catch { }
    }

    static Track PistaAudio(Project p, Hablante h, bool mismas, Dictionary<string, Track> nuevas)
    {
        int n;
        if (mismas && h.Etiqueta.Length > 1 && int.TryParse(h.Etiqueta.Substring(1), out n))
            foreach (Track t in p.Tracks)
                if (t.IsAudio() && t.Index == n - 1) return t;
        string clave = "a:" + h.Etiqueta + ":" + h.Nombre;
        Track r;
        if (!nuevas.TryGetValue(clave, out r))
        {
            r = new AudioTrack(p.Tracks.Count, "Anteriormente · " + h.Nombre);
            p.Tracks.Add(r);
            nuevas[clave] = r;
        }
        return r;
    }

    static Track PistaVideo(Project p, bool mismas, Dictionary<string, Track> nuevas)
    {
        if (mismas)
        {
            Track primera = null;
            foreach (Track t in p.Tracks)
                if (!t.IsAudio() && (primera == null || t.Index < primera.Index)) primera = t;
            if (primera != null) return primera;
        }
        Track r;
        if (!nuevas.TryGetValue("v", out r))
        {
            r = new VideoTrack(0, "Anteriormente");
            p.Tracks.Add(r);
            nuevas["v"] = r;
        }
        return r;
    }

    // Inserta los clips elegidos al inicio. Devuelve un resumen para mostrar.
    public static string Aplicar(Project p, List<Episodio> episodios, List<ClipAnterior> clips, double espacio,
                                 bool hacerEspacio, bool mismasPistas)
    {
        double total = LogicaAnteriormente.Total(clips);
        if (total <= 0) throw new Exception("No hay clips marcados.");

        // Archivos que ya no estan (grabaciones movidas o borradas).
        List<string> faltan = new List<string>();
        foreach (ClipAnterior c in clips)
        {
            if (!c.Elegido) continue;
            foreach (PiezaAnterior x in LogicaAnteriormente.Piezas(episodios[c.Episodio].T, c.Inicio, c.Fin, 0))
                if (!File.Exists(x.Media) && !faltan.Contains(x.Media)) faltan.Add(x.Media);
        }

        double necesario = Math.Max(espacio, total + 1);
        double desplazado = 0;
        if (hacerEspacio)
        {
            double primero = PrimerEvento(p);
            if (primero < double.MaxValue) desplazado = Math.Max(0, necesario - primero);
            Desplazar(p, desplazado);
        }

        Dictionary<string, Media> medios = new Dictionary<string, Media>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, Track> nuevas = new Dictionary<string, Track>();
        double en = 0;
        int eventos = 0;
        foreach (ClipAnterior c in clips)
        {
            if (!c.Elegido) continue;
            Episodio ep = episodios[c.Episodio];
            List<PiezaAnterior> piezas = LogicaAnteriormente.Piezas(ep.T, c.Inicio, c.Fin, en);
            List<TrackEvent> delClip = new List<TrackEvent>();
            int principal = -1; // pista grabada cuyo archivo trae el video
            foreach (PiezaAnterior x in piezas)
            {
                Media m = Medio(p, x.Media, medios);
                if (m == null) continue;
                MediaStream audio = m.Streams.GetItemByMediaType(MediaType.Audio, x.Flujo);
                if (audio != null)
                {
                    Track pista = PistaAudio(p, ep.T.Hablantes[x.Hablante], mismasPistas, nuevas);
                    AudioEvent ev = ((AudioTrack)pista).AddAudioEvent(TC(x.En), TC(x.Hasta - x.Desde));
                    Take toma = ev.AddTake(audio);
                    toma.Offset = TC(x.Desde);
                    try { ev.FadeIn.Length = TC(0.04); ev.FadeOut.Length = TC(0.06); } catch { }
                    delClip.Add(ev);
                    eventos++;
                }
                if (principal < 0 && m.Streams.GetItemByMediaType(MediaType.Video, 0) != null) principal = x.Hablante;
            }
            foreach (PiezaAnterior x in piezas)
            {
                if (x.Hablante != principal) continue;
                Media m = Medio(p, x.Media, medios);
                MediaStream video = m == null ? null : m.Streams.GetItemByMediaType(MediaType.Video, 0);
                if (video == null) continue;
                VideoEvent ev = ((VideoTrack)PistaVideo(p, mismasPistas, nuevas)).AddVideoEvent(TC(x.En), TC(x.Hasta - x.Desde));
                Take toma = ev.AddTake(video);
                toma.Offset = TC(x.Desde);
                delClip.Add(ev);
                eventos++;
            }
            if (delClip.Count > 1)
                try { TrackEventGroup g = Editor.NuevoGrupo(p); foreach (TrackEvent e in delClip) g.Add(e); } catch { }
            en += c.Duracion;
        }
        if (eventos == 0) throw new Exception("No se pudo traer ningún clip." +
            (faltan.Count > 0 ? " No se encuentran las grabaciones: " + String.Join(", ", faltan.ToArray()) : ""));

        p.Regions.Add(new Region(TC(0), TC(total), "ANTERIORMENTE"));
        p.Markers.Add(new Marker(TC(0), "TEXTO: Anteriormente…"));

        return "Anteriormente de " + Formato.Tiempo(total) + " al inicio (" + eventos + " eventos)." +
               (desplazado > 0 ? "\nTodo el video se corrió " + Formato.Tiempo(desplazado) + " para dejar " +
                                 Formato.Tiempo(necesario) + " libres al inicio." : "") +
               (faltan.Count > 0 ? "\nNo se encontraron: " + String.Join(", ", faltan.ToArray()) : "");
    }

    static Media Medio(Project p, string ruta, Dictionary<string, Media> cache)
    {
        Media m;
        if (cache.TryGetValue(ruta, out m)) return m;
        m = null;
        if (File.Exists(ruta))
        {
            try { m = p.MediaPool.Find(ruta); } catch { }
            if (m == null) try { m = new Media(ruta); } catch { }
        }
        cache[ruta] = m;
        return m;
    }
}

class VentanaAnteriormente : VentanaBase
{
    static readonly int[] Duraciones = { 15, 20, 30, 45, 60 };

    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly string rutaGuardado;
    List<Episodio> episodios = new List<Episodio>();
    List<ClipAnterior> clips = new List<ClipAnterior>();
    string respuesta = "";
    bool cargando;

    Lista lstEpisodios = new Lista();
    Boton btnAgregar = new Boton("Agregar episodios…", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Quitar", EstiloBoton.Secundario);
    Segmentado segDuracion = new Segmentado(new string[] { "15 s", "20 s", "30 s", "45 s", "60 s" });
    CampoTexto txtIndicaciones = new CampoTexto();
    Combo comboModelo = new Combo(true);
    Boton btnPedir = new Boton("Pedir a Gemini", EstiloBoton.Primario);
    Etiqueta lblEstado, lblTotal;
    Lista lstClips = new Lista();
    CampoNumero numEspacio = new CampoNumero();
    Boton chipEspacio = new Boton("Correr el video para hacer espacio", EstiloBoton.Chip);
    Segmentado segPistas = new Segmentado(new string[] { "Pistas del proyecto", "Pistas nuevas" });
    Boton btnInsertar = new Boton("Insertar al inicio", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaAnteriormente(Vegas vegas) : base("Anteriormente", 1040)
    {
        this.vegas = vegas;
        string veg = vegas.Project.FilePath;
        rutaGuardado = Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-anteriormente.json");
        int m = Margen, w = Ancho;
        Encabezado("Anteriormente", "Un mini resumen con frases de los episodios pasados que importan para este capítulo.");

        // Columna izquierda
        int y = 92, ci = 340;
        Texto("Episodios anteriores", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        y += 24;
        lstEpisodios.CheckBoxes = false;
        lstEpisodios.Columns.Add("Proyecto", ci - 150);
        lstEpisodios.Columns.Add("Trae clips", 150 - SystemInformation.VerticalScrollBarWidth - 4);
        Pos(lstEpisodios, m, y, ci, 120);
        y += 128;
        Pos(btnAgregar, m, y, ci - 100, 32);
        Pos(btnQuitar, m + ci - 92, y, 92, 32);
        y += 44;
        Texto("Duración", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        Pos(segDuracion, m, y + 22, ci, 34);
        y += 66;
        Texto("Qué quieres que recuerde (opcional)", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        txtIndicaciones.Multilinea = true;
        Pos(txtIndicaciones, m, y + 22, ci, 84);
        y += 116;
        Texto("Modelo", Tema.Negrita, Tema.Texto, m, y + 6, 70, 20);
        comboModelo.Items.Add(config.GeminiModelo);
        comboModelo.Text = config.GeminiModelo;
        Pos(comboModelo, m + 74, y + 2, ci - 74, 30);
        y += 44;
        Pos(btnPedir, m, y, ci, 42);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 48, ci, 52);
        int fondoIzq = y + 104;

        // Columna derecha
        int dx = m + ci + 24, dw = w - ci - 24, dy = 92;
        Texto("Clips propuestos (desmarca lo que no quieras)", Tema.Negrita, Tema.Texto, dx, dy, dw, 20);
        dy += 24;
        lstClips.Columns.Add("Ep.", 44);
        lstClips.Columns.Add("Dura", 50);
        lstClips.Columns.Add("Quién", 110);
        lstClips.Columns.Add("Dice", 230);
        lstClips.Columns.Add("Por qué", Math.Max(150, dw - 44 - 50 - 110 - 230 - SystemInformation.VerticalScrollBarWidth - 4));
        int alto = Math.Max(300, fondoIzq - dy - 40);
        Pos(lstClips, dx, dy, dw, alto);
        lblTotal = Texto("", Tema.Negrita, Tema.Texto, dx, dy + alto + 8, dw, 22);
        int fondo = Math.Max(fondoIzq, dy + alto + 40);

        Texto("ESPACIO AL INICIO", Tema.Pequena, Tema.TextoSuave, m, fondo, 160, 18);
        numEspacio.Sufijo = "s"; numEspacio.Minimo = 5; numEspacio.Maximo = 600; numEspacio.Paso = 5; numEspacio.Valor = 60;
        Pos(numEspacio, m, fondo + 20, 110, 36);
        chipEspacio.Activo = true;
        Pos(chipEspacio, m + 122, fondo + 24, 260, 28);
        Texto("DÓNDE", Tema.Pequena, Tema.TextoSuave, m + 400, fondo, 120, 18);
        segPistas.Seleccion = 0;
        Pos(segPistas, m + 400, fondo + 20, 270, 36);
        Pos(btnCerrar, m + w - 300, fondo + 18, 110, 40);
        Pos(btnInsertar, m + w - 180, fondo + 18, 180, 40);
        ClientSize = new Size(ClientSize.Width, fondo + 58 + 24);

        btnAgregar.Click += delegate { Agregar(); };
        btnQuitar.Click += delegate
        {
            if (lstEpisodios.SelectedIndices.Count == 0) return;
            episodios.RemoveAt(lstEpisodios.SelectedIndices[0]);
            clips.Clear(); respuesta = "";
            MostrarEpisodios(); MostrarClips();
        };
        btnPedir.Click += delegate { Pedir(); };
        btnInsertar.Click += delegate { Insertar(); };
        btnCerrar.Click += delegate { Guardar(); Close(); };
        lstClips.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
        {
            if (cargando) return;
            ((ClipAnterior)e.Item.Tag).Elegido = e.Item.Checked;
            MostrarTotal();
        };

        segDuracion.Seleccion = 2;
        Cargar();
        MostrarEpisodios();
        MostrarClips();
        if (!config.TieneGemini) { btnPedir.Enabled = false; Estado("Falta la clave de Gemini: ejecuta “ConfigurarVegasCut”.", true); }
        else if (episodios.Count == 0) Estado("Agrega uno o más episodios anteriores (su .veg, ya transcrito).", false);
    }

    int Segundos { get { return Duraciones[Math.Max(0, segDuracion.Seleccion)]; } }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    // ------------------------------------------------------- episodios

    void Agregar()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Proyectos de episodios anteriores (ya transcritos)";
            d.Filter = "Proyectos de Vegas o transcripciones|*.veg;*.vegascut.json";
            d.Multiselect = true;
            try { d.InitialDirectory = Path.GetDirectoryName(Path.GetDirectoryName(vegas.Project.FilePath)); } catch { }
            if (d.ShowDialog(this) != DialogResult.OK) return;
            List<string> errores = new List<string>();
            foreach (string f in d.FileNames)
            {
                try
                {
                    Episodio e = Episodio.Abrir(f);
                    if (String.Equals(e.Veg, vegas.Project.FilePath, StringComparison.OrdinalIgnoreCase)) continue;
                    episodios.RemoveAll(delegate (Episodio x) { return String.Equals(x.Veg, e.Veg, StringComparison.OrdinalIgnoreCase); });
                    episodios.Add(e);
                }
                catch (Exception ex) { errores.Add(ex.Message); }
            }
            episodios.Sort(delegate (Episodio a, Episodio b) { return String.Compare(a.Nombre, b.Nombre, StringComparison.OrdinalIgnoreCase); });
            clips.Clear(); respuesta = "";
            MostrarEpisodios(); MostrarClips();
            if (errores.Count > 0) Estado(String.Join("\n", errores.ToArray()), true);
        }
    }

    void MostrarEpisodios()
    {
        lstEpisodios.Items.Clear();
        foreach (Episodio e in episodios)
        {
            ListViewItem it = new ListViewItem(e.Nombre);
            it.SubItems.Add(e.TieneFuentes ? "sí" : "no: transcribir");
            if (!e.TieneFuentes) it.ForeColor = Tema.AcentoHover;
            lstEpisodios.Items.Add(it);
        }
        btnPedir.Enabled = config.TieneGemini && episodios.Count > 0;
    }

    // ------------------------------------------------------------ Gemini

    void Pedir()
    {
        string clave = config.GeminiClave, modelo = comboModelo.Text.Trim();
        if (modelo.Length == 0) modelo = config.GeminiModelo;
        int segundos = Segundos;
        string indicaciones = txtIndicaciones.Text;
        List<Episodio> eps = new List<Episodio>(episodios);
        string actual = LogicaAnteriormente.Actual(TranscripcionActual(), ResumenActual());

        btnPedir.Enabled = false;
        DateTime inicio = DateTime.Now;
        System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        reloj.Interval = 500;
        reloj.Tick += delegate { Estado("Gemini está eligiendo los clips… " + Formato.Tiempo((DateTime.Now - inicio).TotalSeconds), false); };
        reloj.Start();
        Thread hilo = new Thread(delegate ()
        {
            string r = null, error = null;
            try { r = Gemini.Generar(clave, modelo, LogicaAnteriormente.Instrucciones(segundos), LogicaAnteriormente.Mensaje(eps, actual, indicaciones, segundos), true); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    reloj.Stop();
                    btnPedir.Enabled = true;
                    if (error != null) { Estado(error, true); return; }
                    try
                    {
                        clips = LogicaAnteriormente.Leer(r, episodios);
                        respuesta = r;
                        Guardar();
                        MostrarClips();
                        Estado("✔ Listo. Desmarca lo que no quieras y pulsa “Insertar al inicio”.", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    Transcripcion TranscripcionActual()
    {
        try
        {
            string r = Transcripcion.RutaPara(vegas.Project.FilePath);
            if (r == null || !File.Exists(r)) return null;
            Transcripcion t = Transcripcion.Cargar(r);
            if (t.TieneFuentes) t.Ubicador = PistasVegas.Ubicador(vegas.Project, t);
            return t;
        }
        catch { return null; }
    }

    string ResumenActual()
    {
        try
        {
            string veg = vegas.Project.FilePath;
            string ia = Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-ia.json");
            if (!File.Exists(ia)) return "";
            return Json.Texto(Json.Leer(Gemini.QuitarCercas(Json.Texto(Json.Leer(File.ReadAllText(ia, Encoding.UTF8)), "respuesta"))), "resumen");
        }
        catch { return ""; }
    }

    // ------------------------------------------------------------ clips

    void MostrarClips()
    {
        cargando = true;
        lstClips.Items.Clear();
        foreach (ClipAnterior c in clips)
        {
            Episodio e = episodios[c.Episodio];
            ListViewItem it = new ListViewItem((c.Episodio + 1).ToString());
            it.SubItems.Add(c.Duracion.ToString("0.0") + " s");
            it.SubItems.Add(c.Quien);
            it.SubItems.Add(c.Texto);
            it.SubItems.Add(e.TieneFuentes ? c.Motivo : "⚠ " + e.Nombre + " no tiene fuentes: vuelve a transcribirlo · " + c.Motivo);
            if (!e.TieneFuentes) { c.Elegido = false; it.ForeColor = Tema.TextoSuave; }
            it.Checked = c.Elegido;
            it.Tag = c;
            lstClips.Items.Add(it);
        }
        cargando = false;
        MostrarTotal();
    }

    void MostrarTotal()
    {
        double t = LogicaAnteriormente.Total(clips);
        int n = 0;
        foreach (ClipAnterior c in clips) if (c.Elegido) n++;
        lblTotal.Text = clips.Count == 0 ? "Pide a Gemini los clips para ver la propuesta aquí."
            : n + " clips · " + t.ToString("0.0") + " s (pediste " + Segundos + " s)";
        btnInsertar.Enabled = n > 0;
    }

    void Insertar()
    {
        Guardar();
        string r;
        try
        {
            using (UndoBlock deshacer = new UndoBlock("Anteriormente"))
                r = InsertarAnteriormente.Aplicar(vegas.Project, episodios, clips, numEspacio.Valor, chipEspacio.Activo, segPistas.Seleccion == 0);
        }
        catch (Exception ex) { MessageBox.Show(this, "No se pudo insertar: " + ex.Message, "Anteriormente"); return; }
        MessageBox.Show(this, r + "\n\nHay una región “ANTERIORMENTE” y un marcador “TEXTO: Anteriormente…” (con " +
            "TextosDesdeMarcadores se vuelve título). Ctrl+Z lo deshace todo.", "Anteriormente");
        DialogResult = DialogResult.OK;
        Close();
    }

    // ------------------------------------------------- guardar / cargar

    void Guardar()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        List<object> eps = new List<object>();
        foreach (Episodio e in episodios) eps.Add(e.Veg);
        d["episodios"] = eps;
        d["segundos"] = Segundos;
        d["indicaciones"] = txtIndicaciones.Text;
        d["respuesta"] = respuesta;
        try { File.WriteAllText(rutaGuardado, Json.Escribir(d), new UTF8Encoding(false)); } catch { }
    }

    void Cargar()
    {
        try
        {
            if (!File.Exists(rutaGuardado)) return;
            object o = Json.Leer(File.ReadAllText(rutaGuardado, Encoding.UTF8));
            foreach (object x in Json.Lista(o, "episodios"))
                try { episodios.Add(Episodio.Abrir(x as string ?? "")); } catch { }
            int i = Array.IndexOf(Duraciones, (int)Json.Numero(o, "segundos", 30));
            if (i >= 0) segDuracion.Seleccion = i;
            txtIndicaciones.Text = Json.Texto(o, "indicaciones");
            respuesta = Json.Texto(o, "respuesta");
            if (respuesta.Length > 0) clips = LogicaAnteriormente.Leer(respuesta, episodios);
        }
        catch { }
    }
}
