// Anteriormente.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Arma el "ANTERIORMENTE" de un episodio: eliges los proyectos de episodios
// pasados (ya transcritos) y la duracion (15 a 60 s), Gemini elige frases
// cortas de lo que quedo en esos videos que importan para este capitulo, y
// se traen al inicio directo de las grabaciones originales (video y audios),
// corriendo todo el video para dejar espacio libre para editarlo.
//
// Requiere la clave de Gemini en ConfigurarVegasCut.cs.
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/anteriormente/Anteriormente.cs ----

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
            r = new AudioTrack(p.Tracks.Count, "Anteriormente \u00b7 " + h.Nombre);
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
        if (eventos == 0) throw new Exception("No se pudo traer ning\u00fan clip." +
            (faltan.Count > 0 ? " No se encuentran las grabaciones: " + String.Join(", ", faltan.ToArray()) : ""));

        p.Regions.Add(new Region(TC(0), TC(total), "ANTERIORMENTE"));
        p.Markers.Add(new Marker(TC(0), "TEXTO: Anteriormente\u2026"));

        return "Anteriormente de " + Formato.Tiempo(total) + " al inicio (" + eventos + " eventos)." +
               (desplazado > 0 ? "\nTodo el video se corri\u00f3 " + Formato.Tiempo(desplazado) + " para dejar " +
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
    Boton btnAgregar = new Boton("Agregar episodios\u2026", EstiloBoton.Secundario);
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
        Encabezado("Anteriormente", "Un mini resumen con frases de los episodios pasados que importan para este cap\u00edtulo.");

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
        Texto("Duraci\u00f3n", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        Pos(segDuracion, m, y + 22, ci, 34);
        y += 66;
        Texto("Qu\u00e9 quieres que recuerde (opcional)", Tema.Negrita, Tema.Texto, m, y, ci, 20);
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
        lstClips.Columns.Add("Qui\u00e9n", 110);
        lstClips.Columns.Add("Dice", 230);
        lstClips.Columns.Add("Por qu\u00e9", Math.Max(150, dw - 44 - 50 - 110 - 230 - SystemInformation.VerticalScrollBarWidth - 4));
        int alto = Math.Max(300, fondoIzq - dy - 40);
        Pos(lstClips, dx, dy, dw, alto);
        lblTotal = Texto("", Tema.Negrita, Tema.Texto, dx, dy + alto + 8, dw, 22);
        int fondo = Math.Max(fondoIzq, dy + alto + 40);

        Texto("ESPACIO AL INICIO", Tema.Pequena, Tema.TextoSuave, m, fondo, 160, 18);
        numEspacio.Sufijo = "s"; numEspacio.Minimo = 5; numEspacio.Maximo = 600; numEspacio.Paso = 5; numEspacio.Valor = 60;
        Pos(numEspacio, m, fondo + 20, 110, 36);
        chipEspacio.Activo = true;
        Pos(chipEspacio, m + 122, fondo + 24, 260, 28);
        Texto("D\u00d3NDE", Tema.Pequena, Tema.TextoSuave, m + 400, fondo, 120, 18);
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
        if (!config.TieneGemini) { btnPedir.Enabled = false; Estado("Falta la clave de Gemini: ejecuta \u201cConfigurarVegasCut\u201d.", true); }
        else if (episodios.Count == 0) Estado("Agrega uno o m\u00e1s episodios anteriores (su .veg, ya transcrito).", false);
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
            it.SubItems.Add(e.TieneFuentes ? "s\u00ed" : "no: transcribir");
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
        reloj.Tick += delegate { Estado("Gemini est\u00e1 eligiendo los clips\u2026 " + Formato.Tiempo((DateTime.Now - inicio).TotalSeconds), false); };
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
                        Estado("\u2714 Listo. Desmarca lo que no quieras y pulsa \u201cInsertar al inicio\u201d.", false);
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
            it.SubItems.Add(e.TieneFuentes ? c.Motivo : "\u26a0 " + e.Nombre + " no tiene fuentes: vuelve a transcribirlo \u00b7 " + c.Motivo);
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
        lblTotal.Text = clips.Count == 0 ? "Pide a Gemini los clips para ver la propuesta aqu\u00ed."
            : n + " clips \u00b7 " + t.ToString("0.0") + " s (pediste " + Segundos + " s)";
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
        MessageBox.Show(this, r + "\n\nHay una regi\u00f3n \u201cANTERIORMENTE\u201d y un marcador \u201cTEXTO: Anteriormente\u2026\u201d (con " +
            "TextosDesdeMarcadores se vuelve t\u00edtulo). Ctrl+Z lo deshace todo.", "Anteriormente");
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

// ---- src/anteriormente/LogicaAnteriormente.cs ----

// =====================================================================
// "Anteriormente": mini resumen con clips de episodios pasados
//
// Gemini lee lo que quedo en los episodios anteriores (su transcripcion,
// sin lo que se corto) y de que trata el episodio actual, y elige frases
// cortas que sirven para entender este capitulo. Cada frase se lleva a su
// archivo de grabacion y segundo (fuentes de la transcripcion), asi que se
// trae al proyecto nuevo sin abrir el proyecto viejo.
// =====================================================================

public class Episodio
{
    public string Veg = "", Nombre = "";
    public Transcripcion T;
    public string Resumen = "";   // de su respuesta de MomentosIA, si hay
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
}

public class ClipAnterior
{
    public int Episodio;              // indice en la lista de episodios
    public double Inicio, Fin;        // tiempos originales de ese episodio
    public string Texto = "", Motivo = "", Quien = "";
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

// Un pedazo de archivo a poner en la linea de tiempo.
public class PiezaAnterior
{
    public int Hablante;              // del episodio viejo
    public string Media = "";
    public int Flujo;
    public double Desde, Hasta;       // segundos del archivo
    public double En;                 // segundos desde el inicio del "anteriormente"
}

public static class LogicaAnteriormente
{
    static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    public static string Instrucciones(int segundos)
    {
        return "Eres editor de una serie de YouTube en espa\u00f1ol (gameplays con amigos). Vas a armar el " +
               "\"ANTERIORMENTE\" que abre el episodio actual: unos " + segundos + " segundos con frases cortas, dichas " +
               "por las personas en episodios anteriores, que recuerden lo necesario para entender ESTE episodio.\n\n" +
               "Reglas:\n" +
               "- Elige lo que se retoma o importa en el episodio actual: objetivos, conflictos, promesas, rivalidades, " +
               "lugares u objetos que vuelven a aparecer. Un detalle peque\u00f1o de antes que aqu\u00ed se vuelve importante vale " +
               "m\u00e1s que un momento gracioso que no tiene que ver.\n" +
               "- Cada clip es una frase o dos completas, de 2 a 7 segundos, que se entienda sola. Empieza y termina en " +
               "l\u00edmites de frase.\n" +
               "- En orden cronol\u00f3gico (episodio y tiempo). La suma de los clips debe quedar cerca de " + segundos +
               " s (entre " + (int)(segundos * 0.8) + " y " + (int)(segundos * 1.15) + " s).\n" +
               "- Nada de conversaciones personales, problemas t\u00e9cnicos ni groser\u00edas fuertes.\n" +
               "- Usa solo tiempos que aparecen en la transcripci\u00f3n de ese episodio.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"clips\": [{\"episodio\": n, \"inicio\": s, \"fin\": s, \"texto\": \"lo que se dice\", " +
               "\"motivo\": \"por qu\u00e9 importa para este episodio\"}], \"resumen\": \"el anteriormente en una o dos frases\"}";
    }

    public static string Mensaje(List<Episodio> episodios, string actual, string indicaciones, int segundos)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Duraci\u00f3n del anteriormente: " + segundos + " s\n");
        if (!String.IsNullOrEmpty(indicaciones)) sb.Append("\nINDICACIONES DEL EDITOR:\n" + indicaciones.Trim() + "\n");
        sb.Append("\nEPISODIO ACTUAL (de qu\u00e9 trata; para saber qu\u00e9 recordar):\n" + (actual ?? "").Trim() + "\n");
        for (int i = 0; i < episodios.Count; i++)
        {
            Episodio e = episodios[i];
            sb.Append("\n==== EPISODIO " + (i + 1) + ": " + e.Nombre + " ====\n");
            if (e.Resumen.Length > 0) sb.Append("Resumen: " + e.Resumen.Trim() + "\n");
            sb.Append("Transcripci\u00f3n de lo que qued\u00f3 en el video [inicio-fin] persona: texto\n");
            foreach (Segmento s in e.Publicado())
                sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + Nombre(e.T, s.Hablante) + ": " + s.Texto + "\n");
        }
        return sb.ToString();
    }

    static string Nombre(Transcripcion t, int h) { return h >= 0 && h < t.Hablantes.Count ? t.Hablantes[h].Nombre : "?"; }

    // De que trata el episodio actual: el resumen de MomentosIA si hay; si
    // no, su transcripcion (recortada para no mandar de mas).
    public static string Actual(Transcripcion t, string resumenIA)
    {
        if (!String.IsNullOrEmpty(resumenIA)) return resumenIA;
        if (t == null) return "(sin transcripci\u00f3n del episodio actual)";
        StringBuilder sb = new StringBuilder();
        foreach (Segmento s in t.SegmentosActuales())
        {
            if (sb.Length > 40000) { sb.Append("\u2026\n"); break; }
            sb.Append(Nombre(t, s.Hablante) + ": " + s.Texto + "\n");
        }
        return sb.ToString();
    }

    // Lee los clips y los ajusta a las palabras (sin cortar a media palabra,
    // con un respiro de 0.15 s), sin pasar de 10 s cada uno.
    public static List<ClipAnterior> Leer(string json, List<Episodio> episodios)
    {
        List<ClipAnterior> r = new List<ClipAnterior>();
        object o = Json.Leer(Gemini.QuitarCercas(json));
        foreach (object x in Json.Lista(o, "clips"))
        {
            ClipAnterior c = new ClipAnterior();
            c.Episodio = (int)Json.Numero(x, "episodio", 0) - 1;
            if (c.Episodio < 0 || c.Episodio >= episodios.Count) continue;
            c.Inicio = Json.Numero(x, "inicio", -1);
            c.Fin = Json.Numero(x, "fin", -1);
            c.Texto = Json.Texto(x, "texto");
            c.Motivo = Json.Texto(x, "motivo");
            if (c.Inicio < 0 || c.Fin - c.Inicio < 0.5) continue;
            Ajustar(c, episodios[c.Episodio]);
            r.Add(c);
        }
        return r;
    }

    static void Ajustar(ClipAnterior c, Episodio e)
    {
        double a = c.Inicio, b = c.Fin;
        Palabra primera = null, ultima = null;
        foreach (Segmento s in e.T.Segmentos)
            foreach (Palabra p in s.Palabras)
            {
                if (p.Fin <= a || p.Inicio >= b) continue;
                if (primera == null || p.Inicio < primera.Inicio) { primera = p; c.Quien = Nombre(e.T, s.Hablante); }
                if (ultima == null || p.Fin > ultima.Fin) ultima = p;
            }
        if (primera != null) a = Math.Min(a, primera.Inicio);
        if (ultima != null) b = Math.Max(b, ultima.Fin);
        c.Inicio = Math.Max(0, a - 0.15);
        c.Fin = Math.Min(c.Inicio + 10, b + 0.15);
    }

    public static double Total(List<ClipAnterior> clips)
    {
        double d = 0;
        foreach (ClipAnterior c in clips) if (c.Elegido) d += c.Duracion;
        return d;
    }

    // Pedazos de archivo de un clip: uno por pista grabada (voces y
    // ambiente) y por cada evento que tenia esa pista en ese momento.
    public static List<PiezaAnterior> Piezas(Transcripcion t, double a, double b, double en)
    {
        List<PiezaAnterior> r = new List<PiezaAnterior>();
        for (int h = 0; h < t.Hablantes.Count; h++)
            foreach (Fuente f in t.Hablantes[h].Fuentes)
            {
                double x = Math.Max(a, f.Inicio), y = Math.Min(b, f.Fin);
                if (y - x < 0.05) continue;
                PiezaAnterior p = new PiezaAnterior();
                p.Hablante = h; p.Media = f.Media; p.Flujo = f.Flujo;
                p.Desde = f.Desde + (x - f.Inicio) * f.Velocidad;
                p.Hasta = p.Desde + (y - x) * f.Velocidad;
                p.En = en + (x - a);
                r.Add(p);
            }
        return r;
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
        return delegate (int hablante, double tiempo)
        {
            Fuente f;
            double segundo;
            if (!t.AFuente(hablante, tiempo, out f, out segundo)) return double.NaN;
            List<double[]> l;
            if (!eventos.TryGetValue(f.Media.ToLowerInvariant() + "|" + f.Flujo, out l)) return double.NaN;
            double mejor = double.NaN;
            foreach (double[] x in l)
                if (segundo >= x[0] - 0.0005 && segundo < x[1] - 0.0005)
                {
                    double ahora = x[2] + (segundo - x[0]) / x[3];
                    if (double.IsNaN(mejor) || ahora < mejor) mejor = ahora;
                }
            return mejor;
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

    public DialogoNombre(string sugerido)
    {
        Text = "Guardar perfil";
        ClientSize = new Size(380, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;

        Controls.Add(Pos(new Etiqueta("Nombre del perfil", Tema.Seccion, Tema.Texto), 20, 16, 340, 22));
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
