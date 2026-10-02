// PulirEpisodio.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Mide el ritmo del episodio abierto (cortes, recursos, narrador y musica
// minuto a minuto) contra las reglas de su serie y el papel del capitulo, y
// muestra los valles donde se puede caer la retencion. Puede marcarlos como
// regiones en la linea de tiempo.
//
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
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/pulir/Pulir.cs ----

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        if (String.IsNullOrEmpty(vegas.Project.FilePath))
        {
            MessageBox.Show("Guarda el proyecto primero.", "Pulir episodio");
            return;
        }
        using (VentanaPulir v = new VentanaPulir(vegas)) v.ShowDialog();
    }
}

// Ritmo minuto a minuto: cortes, recursos, narracion, cambios de musica y
// los valles encima. Un clic lleva el cursor de Vegas a ese momento.
class GraficoRitmo : ControlBase
{
    public Informe Inf;
    public event Action<double> Buscar;
    const int Izq = 78, Der = 10, Arriba = 22;
    static readonly string[] Filas = { "Cortes", "Recursos", "Narrador", "M\u00fasica" };
    static readonly int[] Altos = { 64, 44, 18, 14 };

    public GraficoRitmo() { Cursor = Cursors.Hand; }

    double Dur { get { return Inf == null ? 1 : Math.Max(1, Inf.M.Duracion); } }
    float X(double t) { return Izq + (float)(t / Dur) * (Width - Izq - Der); }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Inf == null || e.X < Izq || Buscar == null) return;
        Buscar(Math.Max(0, Math.Min(Dur, (e.X - Izq) / (double)(Width - Izq - Der) * Dur)));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        if (Inf == null) return;
        Medicion m = Inf.M;
        ReglasRitmo r = Inf.Reglas;
        int abajo = Height - 8;

        // Zona critica y valles.
        using (SolidBrush b = new SolidBrush(Color.FromArgb(22, Tema.Acento)))
            g.FillRectangle(b, X(0), Arriba, X(Math.Min(r.ZonaCriticaSeg, Dur)) - X(0), abajo - Arriba);
        foreach (Valle v in Inf.Valles)
            using (SolidBrush b = new SolidBrush(Color.FromArgb(v.Critico ? 230 : 140, Tema.Silencio)))
                g.FillRectangle(b, X(v.Inicio), 6, Math.Max(2, X(v.Fin) - X(v.Inicio)), 6);

        // Minutos.
        for (int i = 0; i <= m.Minutos.Count; i++)
        {
            double t = Math.Min(i * 60, Dur);
            using (Pen pen = new Pen(Color.FromArgb(40, Tema.Texto))) g.DrawLine(pen, X(t), Arriba, X(t), abajo);
            if (i < m.Minutos.Count && (m.Minutos.Count <= 20 || i % 5 == 0))
                TextRenderer.DrawText(g, i + ":00", Tema.Pequena, new Point((int)X(t) + 2, Arriba), Tema.TextoSuave);
        }

        int y = Arriba + 18;
        int maxC = r.CortesMax + 8, maxR = Math.Max(r.RecursosPorMin * 3, 6);
        foreach (MinutoRitmo x in m.Minutos) { maxC = Math.Max(maxC, x.Cortes); maxR = Math.Max(maxR, x.Recursos); }
        for (int f = 0; f < Filas.Length; f++)
        {
            int h = Altos[f];
            TextRenderer.DrawText(g, Filas[f], Tema.Pequena, new Rectangle(8, y, Izq - 12, h), Tema.TextoSuave,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
            if (f <= 1)
            {
                foreach (MinutoRitmo x in m.Minutos)
                {
                    int valor = f == 0 ? x.Cortes : x.Recursos;
                    bool completo = (x.Minuto + 1) * 60 <= m.Duracion + 30;
                    bool bajo = completo && (f == 0 ? valor < r.CortesMin : valor < r.RecursosPorMin);
                    bool alto = f == 0 && valor > r.CortesMax + 5;
                    float bh = (float)valor / (f == 0 ? maxC : maxR) * (h - 2);
                    float x0 = X(x.Minuto * 60) + 3, x1 = X(Math.Min(Dur, (x.Minuto + 1) * 60)) - 3;
                    Color c = bajo ? Tema.Silencio : alto ? Tema.Voz : Tema.Acento;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(completo ? 220 : 120, c)))
                        g.FillRectangle(b, x0, y + h - bh, Math.Max(1, x1 - x0), bh);
                }
                float meta = (float)(f == 0 ? r.CortesMin : r.RecursosPorMin) / (f == 0 ? maxC : maxR) * (h - 2);
                using (Pen pen = new Pen(Color.FromArgb(150, Tema.Texto)) { DashStyle = DashStyle.Dash })
                    g.DrawLine(pen, Izq, y + h - meta, Width - Der, y + h - meta);
            }
            else if (f == 2)
            {
                using (SolidBrush b = new SolidBrush(Tema.Borde)) g.FillRectangle(b, Izq, y + 3, Width - Izq - Der, h - 6);
                using (SolidBrush b = new SolidBrush(Tema.Voz))
                    foreach (Rango n in m.Narracion) g.FillRectangle(b, X(n.Inicio), y + 3, Math.Max(1, X(n.Fin) - X(n.Inicio)), h - 6);
            }
            else
                using (Pen pen = new Pen(Tema.Texto, 2))
                    foreach (double t in m.CambiosMusica) g.DrawLine(pen, X(t), y + 2, X(t), y + h - 2);
            y += h + 6;
        }
    }
}

class VentanaPulir : VentanaBase
{
    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    Transcripcion trans;
    SerieProyecto serie;
    List<CapSerie> caps = new List<CapSerie>();
    FormatoSerie formato;
    Informe inf;
    bool cargando;

    Etiqueta lblSerie, lblResumen, lblEstado;
    Boton btnSerie = new Boton("Serie\u2026", EstiloBoton.Secundario);
    Combo cmbNarrador = new Combo();
    GraficoRitmo grafico = new GraficoRitmo();
    Lista lstChequeos = new Lista();
    Lista lstValles = new Lista();
    Boton btnRegiones = new Boton("Marcar valles", EstiloBoton.Secundario);
    Boton btnQuitarRegiones = new Boton("Quitar regiones", EstiloBoton.Secundario);
    Boton btnMedir = new Boton("Volver a medir", EstiloBoton.Secundario);
    Boton btnPlan = new Boton("Estructura y narraci\u00f3n\u2026", EstiloBoton.Primario);
    Boton btnReemplazar = new Boton("Reemplazar placeholders\u2026", EstiloBoton.Secundario);
    VideoEvent plantilla;
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Primario);

    public VentanaPulir(Vegas vegas) : base("Pulir episodio", 1040)
    {
        this.vegas = vegas;
        int m = Margen, w = Ancho;
        Encabezado("Pulir episodio", "Mide el ritmo del episodio contra las reglas de su serie y marca d\u00f3nde se puede ir la gente.");
        int y = 92;
        lblSerie = Texto("", Tema.Normal, Tema.Texto, m, y, w - 420, 36);
        Pos(btnSerie, m + w - 410, y, 100, 32);
        Texto("NARRADOR", Tema.Pequena, Tema.TextoSuave, m + w - 296, y - 14, 200, 14);
        Pos(cmbNarrador, m + w - 296, y + 1, 296, 30);
        y += 44;
        lblResumen = Texto("", Tema.Seccion, Tema.Texto, m, y, w, 22);
        y += 28;
        Pos(grafico, m, y, w, 210);
        y += 220;

        int ci = 490;
        Texto("Contra las reglas de la serie", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        Texto("Valles (doble clic: ir y seleccionar en Vegas)", Tema.Negrita, Tema.Texto, m + ci + 16, y, w - ci - 16, 20);
        y += 24;
        lstChequeos.CheckBoxes = false;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lstChequeos.Columns.Add("", 26);
        lstChequeos.Columns.Add("Qu\u00e9", 140);
        lstChequeos.Columns.Add("Medido", ci - 26 - 140 - 104 - sb);
        lstChequeos.Columns.Add("Objetivo", 104);
        Pos(lstChequeos, m, y, ci, 200);
        lstValles.CheckBoxes = false;
        lstValles.Columns.Add("Tramo", 110);
        lstValles.Columns.Add("", 26);
        lstValles.Columns.Add("Qu\u00e9 pasa", w - ci - 16 - 110 - 26 - sb);
        Pos(lstValles, m + ci + 16, y, w - ci - 16, 200);
        y += 210;
        Pos(btnRegiones, m, y, 130, 34);
        Pos(btnQuitarRegiones, m + 138, y, 140, 34);
        Pos(btnMedir, m + 286, y, 130, 34);
        Pos(btnReemplazar, m + w - 562, y, 192, 34);
        Pos(btnPlan, m + w - 362, y, 220, 34);
        Pos(btnCerrar, m + w - 134, y, 134, 34);
        y += 42;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 34);
        ClientSize = new Size(ClientSize.Width, y + 34 + 16);

        grafico.Buscar += delegate (double t) { Ir(t, -1); };
        lstValles.DoubleClick += delegate
        {
            if (lstValles.SelectedIndices.Count == 0 || inf == null) return;
            Valle v = inf.Valles[lstValles.SelectedIndices[0]];
            Ir(v.Inicio, v.Fin);
        };
        cmbNarrador.SelectedIndexChanged += delegate { if (!cargando) Medir(); };
        btnSerie.Click += delegate
        {
            using (VentanaSeries d = new VentanaSeries(vegas.Project.FilePath, config.GeminiClave, config.GeminiModelo, true))
            {
                d.Medidor = delegate (string n) { return RitmoVegas.MedirAbierto(vegas, n); };
                d.ShowDialog(this);
            }
            CargarSerie();
            LlenarNarradores();
            Medir();
        };
        btnRegiones.Click += delegate
        {
            if (inf == null) return;
            int n;
            using (UndoBlock u = new UndoBlock("Marcar valles")) n = LogicaPulir.MarcarRegiones(vegas.Project, inf.Valles);
            Estado(n == 0 ? "No hay valles que marcar." : "\u2714 " + n + " regiones \u00ab" + LogicaPulir.PrefijoRegion.Trim() + "\u00bb en la l\u00ednea de tiempo (Ctrl+Z las quita).", false);
        };
        btnQuitarRegiones.Click += delegate
        {
            int n;
            using (UndoBlock u = new UndoBlock("Quitar valles")) n = LogicaPulir.QuitarRegiones(vegas.Project);
            Estado(n == 0 ? "No hab\u00eda regiones de valles." : "Se quitaron " + n + " regiones de valles.", false);
        };
        btnMedir.Click += delegate { CargarTranscripcion(); Medir(); };
        btnCerrar.Click += delegate { Close(); };
        btnPlan.Click += delegate
        {
            if (inf == null) return;
            using (VentanaPlan d = new VentanaPlan(vegas, inf, trans, serie, caps, formato, Papel(), plantilla))
            {
                d.ShowDialog(this);
                if (d.Aplicado) { CargarTranscripcion(); Medir(); }
            }
        };
        btnReemplazar.Click += delegate { Reemplazar(); };

        // Plantilla de los placeholders: la imagen o video seleccionado (no un texto).
        foreach (Track t in vegas.Project.Tracks)
        {
            if (t.IsAudio()) continue;
            foreach (TrackEvent e in t.Events)
                if (e.Selected && e is VideoEvent && !GeneradorTexto.EsTexto(e)) { plantilla = (VideoEvent)e; break; }
            if (plantilla != null) break;
        }

        CargarSerie();
        CargarTranscripcion();
        LlenarNarradores();
        Medir();
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void CargarSerie()
    {
        caps = Serie.DelProyecto(vegas.Project.FilePath, out serie);
        formato = serie != null ? serie.Formato : FormatoSerie.Preset("Otro");
        string papel = Papel();
        lblSerie.Text = serie == null
            ? "Sin serie: uso reglas generales. Con \u00abSerie\u2026\u00bb eliges la serie y su formato."
            : "Serie " + serie.Nombre + " \u00b7 " + formato.Nombre + " \u00b7 " + Path.GetFileNameWithoutExtension(vegas.Project.FilePath) +
              " es \u00ab" + papel + "\u00bb" + (serie.IndiceDe(vegas.Project.FilePath) < 0 ? " (no est\u00e1 en la serie)" : "");
    }

    string Papel() { return serie == null ? "Normal" : serie.Papel(vegas.Project.FilePath); }

    void CargarTranscripcion()
    {
        trans = null;
        try
        {
            string r = Transcripcion.RutaPara(vegas.Project.FilePath);
            if (r != null && File.Exists(r))
            {
                trans = Transcripcion.Cargar(r);
                if (trans.TieneFuentes) trans.Ubicador = PistasVegas.Ubicador(vegas.Project, trans);
            }
        }
        catch { trans = null; }
    }

    // "(sin narrador)" y las voces de la transcripcion; elige la del formato.
    void LlenarNarradores()
    {
        cargando = true;
        cmbNarrador.Items.Clear();
        cmbNarrador.Items.Add("(sin narrador)");
        int elegido = 0;
        if (trans != null)
            for (int i = 0; i < trans.Hablantes.Count; i++)
            {
                Hablante h = trans.Hablantes[i];
                if (!h.Voz) continue;
                cmbNarrador.Items.Add(h.Nombre);
                string n = (h.Nombre ?? "").Trim();
                if (String.Equals(n, formato.NarradorNombre.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    (elegido == 0 && n.ToLowerInvariant().StartsWith("narr")))
                    elegido = cmbNarrador.Items.Count - 1;
            }
        cmbNarrador.SelectedIndex = elegido;
        cargando = false;
    }

    int IndiceNarrador()
    {
        if (trans == null || cmbNarrador.SelectedIndex <= 0) return int.MaxValue;   // ninguno
        string n = (string)cmbNarrador.SelectedItem;
        for (int i = 0; i < trans.Hablantes.Count; i++) if (trans.Hablantes[i].Nombre == n) return i;
        return int.MaxValue;
    }

    void Medir()
    {
        try
        {
            Medicion med = RitmoVegas.Medir(vegas.Project, trans, IndiceNarrador());
            inf = LogicaPulir.Analizar(med, formato, Papel());
        }
        catch (Exception ex) { Estado("No se pudo medir: " + ex.Message, true); return; }
        lblResumen.Text = Ritmo.Resumen(inf.M);
        grafico.Inf = inf;
        grafico.Invalidate();

        lstChequeos.Items.Clear();
        foreach (Chequeo c in inf.Chequeos)
        {
            ListViewItem it = new ListViewItem(c.Ok ? "\u2714" : "\u2716");
            it.SubItems.Add(c.Que); it.SubItems.Add(c.Medido); it.SubItems.Add(c.Objetivo);
            it.ForeColor = c.Ok ? Tema.Texto : Tema.Silencio;
            lstChequeos.Items.Add(it);
        }
        lstValles.Items.Clear();
        foreach (Valle v in inf.Valles)
        {
            ListViewItem it = new ListViewItem(Formato.Tiempo(v.Inicio) + "\u2013" + Formato.Tiempo(v.Fin));
            it.SubItems.Add(v.Critico ? "!" : "");
            it.SubItems.Add(v.Texto);
            if (v.Critico) it.ForeColor = Tema.Silencio;
            lstValles.Items.Add(it);
        }
        string aviso = trans == null ? "Sin transcripci\u00f3n: no puedo ver al narrador (ejecuta Transcribir). " : "";
        if (inf.FaltaNarracion) aviso += "La serie lleva narrador pero a\u00fan no hay narraci\u00f3n: los huecos de narrador no se cuentan como valles todav\u00eda. ";
        Estado(aviso + (inf.Valles.Count == 0 ? "Sin valles: el ritmo cumple las reglas." :
                        inf.Valles.Count + " valles. Clic en el gr\u00e1fico o doble clic en un valle para ir ah\u00ed."), false);
    }

    void Reemplazar()
    {
        string carpeta;
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta con tus recursos. Cada archivo empieza con el c\u00f3digo del placeholder (\"R03 cad\u00e1ver.png\"); se busca tambi\u00e9n en subcarpetas.";
            string sugerida = Path.GetDirectoryName(vegas.Project.FilePath);
            if (Directory.Exists(sugerida)) d.SelectedPath = sugerida;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            carpeta = d.SelectedPath;
        }
        List<string> faltan = new List<string>();
        int n;
        using (UndoBlock u = new UndoBlock("Reemplazar placeholders")) n = AplicarPlan.ReemplazarPlaceholders(vegas.Project, carpeta, faltan);
        Estado(n == 0 && faltan.Count == 0 ? "No encontr\u00e9 placeholders (pista \u00ab" + AplicarPlan.PistaPlaceholders + "\u00bb)."
               : "\u2714 " + n + " placeholders reemplazados (conservan efectos y movimiento)." +
                 (faltan.Count > 0 ? " Faltan: " + String.Join(", ", faltan.ToArray()) + "." : ""), faltan.Count > 0 && n == 0);
    }

    // Lleva el cursor (y la seleccion, si hay fin) de Vegas a ese momento.
    void Ir(double a, double b)
    {
        try
        {
            vegas.Transport.CursorPosition = Timecode.FromMilliseconds(a * 1000);
            if (b > a)
            {
                vegas.Transport.SelectionStart = Timecode.FromMilliseconds(a * 1000);
                vegas.Transport.SelectionLength = Timecode.FromMilliseconds((b - a) * 1000);
            }
        }
        catch { }
    }
}

// ---- src/pulir/LogicaPulir.cs ----

// =====================================================================
// PulirEpisodio, parte 1: medir el episodio y compararlo con las reglas de
// su serie (ajustadas al papel del capitulo) para ver donde se puede caer
// la retencion.
// =====================================================================

// Un renglon del informe: lo medido contra lo que pide la serie.
public class Chequeo
{
    public string Que = "", Medido = "", Objetivo = "";
    public bool Ok;
}

public class Informe
{
    public Medicion M;
    public ReglasRitmo Reglas;
    public string Papel = "Normal";
    public bool ConNarrador;          // la serie lleva narrador y ya hay narracion
    public bool FaltaNarracion;       // la serie lleva narrador pero aun no se grabo
    public List<Valle> Valles = new List<Valle>();
    public List<Chequeo> Chequeos = new List<Chequeo>();
}

public static class LogicaPulir
{
    public const string PrefijoRegion = "VALLE \u00b7 ";

    static string N(double x) { return x.ToString("0.#", CultureInfo.InvariantCulture); }

    // Minutos completos (el ultimo, si es muy corto, no cuenta para promedios).
    static double MinutosUtiles(Medicion m) { return Math.Max(1, m.Duracion / 60); }

    public static Informe Analizar(Medicion m, FormatoSerie f, string papel)
    {
        Informe r = new Informe();
        r.M = m;
        r.Papel = String.IsNullOrEmpty(papel) ? "Normal" : papel;
        r.Reglas = PapelEpisodio.Reglas(f.Reglas, r.Papel);
        r.ConNarrador = f.Narrador && m.HayNarrador;
        r.FaltaNarracion = f.Narrador && !m.HayNarrador;
        r.Valles = Ritmo.Valles(m, r.Reglas, r.ConNarrador);
        r.Chequeos = Chequear(r);
        return r;
    }

    static Chequeo C(string que, string medido, string objetivo, bool ok)
    {
        Chequeo c = new Chequeo();
        c.Que = que; c.Medido = medido; c.Objetivo = objetivo; c.Ok = ok;
        return c;
    }

    static List<Chequeo> Chequear(Informe inf)
    {
        Medicion m = inf.M;
        ReglasRitmo r = inf.Reglas;
        List<Chequeo> l = new List<Chequeo>();
        double min = m.Duracion / 60;
        l.Add(C("Duraci\u00f3n", Formato.Tiempo(m.Duracion), N(r.DuracionMin) + "\u2013" + N(r.DuracionMax) + " min",
                min >= r.DuracionMin - 0.25 && min <= r.DuracionMax + 0.5));

        int cortes = 0, recursos = 0;
        foreach (MinutoRitmo x in m.Minutos) { cortes += x.Cortes; recursos += x.Recursos; }
        double cpm = cortes / MinutosUtiles(m), rpm = recursos / MinutosUtiles(m);
        l.Add(C("Cortes por minuto", N(Math.Round(cpm, 1)), r.CortesMin + "\u2013" + r.CortesMax, cpm >= r.CortesMin && cpm <= r.CortesMax + 5));
        l.Add(C("Recursos por minuto", N(Math.Round(rpm, 1)), "\u2265 " + r.RecursosPorMin, rpm >= r.RecursosPorMin));

        // Zona critica: lo que se ve antes de que decidan quedarse.
        double zona = Math.Min(r.ZonaCriticaSeg, m.Duracion);
        int criticos = 0;
        foreach (Valle v in inf.Valles) if (v.Critico) criticos++;
        l.Add(C("Inicio (hasta " + Formato.Tiempo(zona) + ")", criticos == 0 ? "sin valles" : criticos + (criticos == 1 ? " valle" : " valles"),
                "sin valles", criticos == 0));

        if (inf.FaltaNarracion)
            l.Add(C("Narrador", "a\u00fan no hay narraci\u00f3n", "cada \u2264 " + r.NarradorCadaSeg + " s", false));
        else if (inf.ConNarrador)
        {
            double narr = 0;
            foreach (Rango x in m.Narracion) narr += x.Fin - x.Inicio;
            int huecos = Ritmo.SinNarrador(m, r.NarradorCadaSeg).Count;
            l.Add(C("Narrador", Math.Round(narr / Math.Max(1, m.Duracion) * 100) + " % del video" +
                    (huecos > 0 ? " \u00b7 " + huecos + (huecos == 1 ? " hueco" : " huecos") : ""),
                    "cada \u2264 " + r.NarradorCadaSeg + " s", huecos == 0));
            if (m.PPM > 0)
                l.Add(C("Velocidad del narrador", m.PPM + " ppm", r.PPM + " ppm", Math.Abs(m.PPM - r.PPM) <= 25));
        }

        if (m.CambiosMusica.Count == 0)
            l.Add(C("M\u00fasica", "no encontr\u00e9 m\u00fasica", "cambiar cada ~" + r.MusicaCadaSeg + " s", false));
        else
        {
            double cada = m.Duracion / m.CambiosMusica.Count;
            l.Add(C("M\u00fasica", m.CambiosMusica.Count + " temas \u00b7 cada " + Math.Round(cada) + " s", "cada ~" + r.MusicaCadaSeg + " s",
                    cada <= r.MusicaCadaSeg * 1.5));
        }
        return l;
    }

    // Pone cada valle como region "VALLE \u00b7 ..." (quita las de una medicion anterior).
    public static int MarcarRegiones(Project p, List<Valle> valles)
    {
        QuitarRegiones(p);
        int n = 0;
        foreach (Valle v in valles)
        {
            if (v.Fin - v.Inicio < 1) continue;
            p.Regions.Add(new Region(Timecode.FromMilliseconds(v.Inicio * 1000), Timecode.FromMilliseconds((v.Fin - v.Inicio) * 1000),
                                     PrefijoRegion + v.Texto));
            n++;
        }
        return n;
    }

    public static int QuitarRegiones(Project p)
    {
        List<Region> viejas = new List<Region>();
        foreach (Region r in p.Regions) if ((r.Label ?? "").StartsWith(PrefijoRegion)) viejas.Add(r);
        foreach (Region r in viejas) p.Regions.Remove(r);
        return viejas.Count;
    }

    // El informe en texto (para el guion y para Gemini en la parte 2).
    public static string Texto(Informe inf)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Papel del cap\u00edtulo: " + inf.Papel + "\n");
        foreach (Chequeo c in inf.Chequeos)
            sb.Append((c.Ok ? "[ok] " : "[!!] ") + c.Que + ": " + c.Medido + " (objetivo " + c.Objetivo + ")\n");
        if (inf.Valles.Count > 0)
        {
            sb.Append("Valles:\n");
            foreach (Valle v in inf.Valles)
                sb.Append("- " + Formato.Tiempo(v.Inicio) + "\u2013" + Formato.Tiempo(v.Fin) + (v.Critico ? " (zona cr\u00edtica) " : " ") + v.Texto + "\n");
        }
        sb.Append("Minuto a minuto:\n" + Ritmo.Tabla(inf.M));
        return sb.ToString();
    }
}

// ---- src/pulir/VentanaPlan.cs ----

// Estructura y narracion: Gemini propone, tu eliges que entra y se aplica.
class VentanaPlan : VentanaBase
{
    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly Informe inf;
    readonly Transcripcion trans;
    readonly SerieProyecto serie;
    readonly List<CapSerie> caps;
    readonly FormatoSerie formato;
    readonly string papel;
    readonly VideoEvent plantilla;
    Plan plan;
    bool cargando, trabajando;
    public bool Aplicado;

    CampoTexto txtIndicaciones = new CampoTexto();
    Boton btnPedir = new Boton("Pedir a Gemini", EstiloBoton.Primario);
    Etiqueta lblResumen, lblPlantilla, lblEstado;
    Lista lstItems = new Lista();
    Boton chipGancho = Chip("Gancho al inicio"), chipNarracion = Chip("Narraci\u00f3n con voz"), chipBajar = Chip("Bajar el juego al narrar"),
          chipAvances = Chip("Avances en pantalla"), chipPlaceholders = Chip("Placeholders"), chipRegiones = Chip("Regiones");
    Boton btnGuion = new Boton("Exportar guion", EstiloBoton.Secundario);
    Boton btnAplicar = new Boton("Aplicar al proyecto", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    static Boton Chip(string t)
    {
        Boton b = new Boton(t, EstiloBoton.Chip);
        b.Activo = true;
        b.Click += delegate { b.Activo = !b.Activo; };
        return b;
    }

    public VentanaPlan(Vegas vegas, Informe inf, Transcripcion trans, SerieProyecto serie, List<CapSerie> caps,
                       FormatoSerie formato, string papel, VideoEvent plantilla) : base("Estructura y narraci\u00f3n", 1040)
    {
        this.vegas = vegas; this.inf = inf; this.trans = trans; this.serie = serie; this.caps = caps;
        this.formato = formato; this.papel = papel; this.plantilla = plantilla;
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Estructura y narraci\u00f3n", "Gemini propone gancho, secciones, narraci\u00f3n, avances y recursos seg\u00fan la serie y el papel del cap\u00edtulo.");
        int y = 92;
        Texto("INDICACIONES (opcional: qu\u00e9 destacar, qu\u00e9 no contar, chistes internos\u2026)", Tema.Pequena, Tema.TextoSuave, m, y, w - 200, 18);
        txtIndicaciones.Multilinea = true;
        Pos(txtIndicaciones, m, y + 20, w - 196, 52);
        Pos(btnPedir, m + w - 180, y + 20, 180, 52);
        y += 82;
        lblResumen = Texto("", Tema.Normal, Tema.Texto, m, y, w, 40);
        y += 44;
        lstItems.Columns.Add("Tiempo", 96);
        lstItems.Columns.Add("Qu\u00e9", 110);
        lstItems.Columns.Add("Detalle", w - 96 - 110 - SystemInformation.VerticalScrollBarWidth - 4);
        Pos(lstItems, m, y, w, 300);
        y += 310;
        int cx = m;
        foreach (Boton c in new Boton[] { chipGancho, chipNarracion, chipBajar, chipAvances, chipPlaceholders, chipRegiones })
        {
            int cw = TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 34;
            Pos(c, cx, y, cw, 30);
            cx += cw + 8;
        }
        y += 38;
        lblPlantilla = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 34);
        y += 38;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 520, 40);
        Pos(btnGuion, m + w - 510, y, 150, 40);
        Pos(btnAplicar, m + w - 350, y, 200, 40);
        Pos(btnCerrar, m + w - 140, y, 140, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        chipNarracion.Enabled = chipBajar.Enabled = formato.Narrador;
        if (!formato.Narrador) chipNarracion.Activo = chipBajar.Activo = false;
        chipAvances.Enabled = formato.Avance != "Ninguno";
        lblPlantilla.Text = plantilla != null
            ? "Placeholders: copian el evento que ten\u00edas seleccionado (en " + Nombre(plantilla.Track) + ", " + Formato.Tiempo(plantilla.Start.ToMilliseconds() / 1000.0) +
              "): sus efectos, movimiento y fundidos. Despu\u00e9s, \u00abReemplazar placeholders\u00bb pone tus im\u00e1genes."
            : "Placeholders: texto simple. Para que copien tus efectos y movimiento, cierra, selecciona una imagen ya editada en la l\u00ednea de tiempo y vuelve a abrir.";

        lstItems.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando && e.Item.Tag != null) ((ItemPlan)e.Item.Tag).Elegido = e.Item.Checked; };
        lstItems.DoubleClick += delegate
        {
            if (lstItems.SelectedIndices.Count == 0) return;
            ItemPlan i = (ItemPlan)lstItems.Items[lstItems.SelectedIndices[0]].Tag;
            try
            {
                vegas.Transport.CursorPosition = Timecode.FromMilliseconds(i.Inicio * 1000);
                vegas.Transport.SelectionStart = Timecode.FromMilliseconds(i.Inicio * 1000);
                vegas.Transport.SelectionLength = Timecode.FromMilliseconds(Math.Max(0.1, i.Duracion) * 1000);
            }
            catch { }
        };
        btnPedir.Click += delegate { Pedir(); };
        btnGuion.Click += delegate { ExportarGuion(true); };
        btnAplicar.Click += delegate { Aplicar(); };
        btnCerrar.Click += delegate { Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; else GuardarElecciones(); };

        plan = LogicaPlan.Cargar(vegas.Project.FilePath, inf.M.Duracion, inf.Reglas.PPM);
        Mostrar();
        if (plan != null)
            Estado("Plan guardado del " + (plan.Generado.Length > 0 ? plan.Generado : "proyecto") + ". \u00abPedir a Gemini\u00bb hace uno nuevo." +
                   (LogicaPlan.Aplicado(vegas.Project.FilePath) != null ? " Ya se aplic\u00f3 una vez." : ""), false);
        else if (String.IsNullOrEmpty(config.GeminiClave))
            Estado("Falta la clave de Gemini: ejecuta \u00abConfigurarVegasCut\u00bb.", true);
        else Estado("Pulsa \u00abPedir a Gemini\u00bb.", false);
        if (trans == null)
            Estado("Este proyecto no tiene transcripci\u00f3n: Gemini no sabr\u00eda qu\u00e9 se dice. Ejecuta Transcribir primero.", true);
        else if (!trans.TieneFuentes)
            Estado("La transcripci\u00f3n es de una versi\u00f3n vieja (sin fuentes): con el gancho al inicio dejar\u00eda de seguir al video. " +
                   "Vuelve a transcribir este proyecto antes de aplicar.", true);
    }

    static string Nombre(Track t) { return String.IsNullOrEmpty(t.Name) ? "la pista " + (t.Index + 1) : t.Name; }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Mostrar()
    {
        cargando = true;
        lstItems.Items.Clear();
        bool hay = plan != null;
        btnAplicar.Enabled = btnGuion.Enabled = hay;
        lblResumen.Text = hay ? plan.Resumen + (plan.Estructura.Length > 0 ? "\n" + plan.Estructura : "") : "";
        if (hay)
            foreach (ItemPlan i in plan.Items)
            {
                string tiempo = i.Tipo == "avance" ? Formato.Tiempo(i.Inicio) : Formato.Tiempo(i.Inicio) + "\u2013" + Formato.Tiempo(i.Fin);
                ListViewItem it = new ListViewItem(tiempo);
                string que = LogicaPlan.NombreTipo(i.Tipo) + (i.Id.Length > 0 ? " " + i.Id : "");
                it.SubItems.Add(que);
                string det = i.Texto;
                if (i.Tipo == "narracion") det = (i.Clase.Length > 0 ? "(" + i.Clase + ") " : "") + "\u00ab" + i.Texto + "\u00bb";
                else if (i.Tipo == "recurso") det = i.Clase + ": " + i.Texto;
                else if (i.Tipo == "recorte") det = (i.Clase == "acelerar" ? "Acelerar: " : "Quitar: ") + i.Texto;
                else if (i.Tipo == "seccion" && i.Detalle.Length > 0) det = i.Texto + " \u2014 " + i.Detalle;
                else if (i.Tipo == "gancho") det = "Copiar al inicio: " + i.Texto;
                it.SubItems.Add(det);
                it.Checked = i.Elegido;
                it.ForeColor = i.Tipo == "narracion" ? Tema.Voz : i.Tipo == "recorte" ? Tema.Silencio :
                               i.Tipo == "gancho" || i.Tipo == "avance" ? Tema.Acento : Tema.Texto;
                it.Tag = i;
                lstItems.Items.Add(it);
            }
        cargando = false;
        chipGancho.Enabled = hay && plan.Gancho != null;
        if (hay && plan.Gancho == null) chipGancho.Activo = false;
    }

    void Habilitar(bool si)
    {
        foreach (Control c in new Control[] { btnPedir, btnAplicar, btnGuion, btnCerrar, lstItems, txtIndicaciones }) c.Enabled = si;
    }

    void Pedir()
    {
        if (String.IsNullOrEmpty(config.GeminiClave)) { Estado("Falta la clave de Gemini: ejecuta \u00abConfigurarVegasCut\u00bb.", true); return; }
        string instr = LogicaPlan.Instrucciones(formato, serie != null ? serie.Tipo : "Gameplay", papel, inf.Reglas);
        string msg = LogicaPlan.Mensaje(trans, inf.M.Duracion, inf, serie != null ? Serie.Contexto(serie, caps) : "",
                                        LogicaPlan.Anteriores(caps), txtIndicaciones.Text, LogicaPlan.Pausas(trans, inf.M.Duracion, 2.5));
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        double dur = inf.M.Duracion;
        int ppm = inf.Reglas.PPM;
        trabajando = true;
        Habilitar(false);
        Estado("Gemini est\u00e1 armando la estructura (puede tardar un minuto)\u2026", false);
        Thread hilo = new Thread(delegate ()
        {
            string respuesta = null, error = null;
            try { respuesta = Gemini.Generar(clave, modelo, instr, msg, true); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    Habilitar(true);
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        plan = LogicaPlan.Leer(respuesta, dur, ppm);
                        plan.Generado = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " \u00b7 " + modelo;
                        GuardarElecciones();
                        Mostrar();
                        Estado("\u2714 " + Contar() + ". Desmarca lo que no quieras y pulsa \u00abAplicar al proyecto\u00bb.", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    string Contar()
    {
        List<string> l = new List<string>();
        foreach (string t in LogicaPlan.Tipos)
        {
            int n = plan.De(t).Count;
            if (n > 0) l.Add(n + " " + LogicaPlan.NombreTipo(t).ToLowerInvariant() + (n == 1 ? "" : t == "seccion" ? "es" : t == "narracion" ? "es" : "s"));
        }
        return String.Join(", ", l.ToArray());
    }

    void GuardarElecciones()
    {
        if (plan == null) return;
        try { LogicaPlan.Guardar(vegas.Project.FilePath, plan, null); } catch { }
    }

    string RutaGuion()
    {
        string veg = vegas.Project.FilePath;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-guion.txt");
    }

    void ExportarGuion(bool abrir)
    {
        if (plan == null) return;
        object ap = LogicaPlan.Aplicado(vegas.Project.FilePath);
        double corr = ap != null ? Json.Numero(ap, "corrimiento", 0) : LogicaPlan.Corrimiento(plan, chipGancho.Activo);
        try
        {
            File.WriteAllText(RutaGuion(), LogicaPlan.Guion(plan, Path.GetFileNameWithoutExtension(vegas.Project.FilePath), corr, inf.Reglas.PPM),
                              new UTF8Encoding(true));
            if (abrir) try { System.Diagnostics.Process.Start(RutaGuion()); } catch { }
            Estado("Guion guardado: " + RutaGuion(), false);
        }
        catch (Exception ex) { Estado("No se pudo guardar el guion: " + ex.Message, true); }
    }

    void Aplicar()
    {
        if (plan == null || trabajando) return;
        if (LogicaPlan.Aplicado(vegas.Project.FilePath) != null &&
            MessageBox.Show(this, "Este plan ya se aplic\u00f3. Si lo aplicas otra vez se agregan de nuevo la narraci\u00f3n, los avances y los " +
                            "placeholders (y se corre otra vez el video si hay gancho). Lo normal es deshacer antes con Ctrl+Z.\n\n\u00bfAplicar de todos modos?",
                            "Estructura y narraci\u00f3n", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        OpcionesPlan op = new OpcionesPlan();
        op.Gancho = chipGancho.Activo && chipGancho.Enabled;
        op.Narracion = chipNarracion.Activo && formato.Narrador;
        op.Bajar = chipBajar.Activo && formato.Narrador;
        op.Avances = chipAvances.Activo && chipAvances.Enabled;
        op.Placeholders = chipPlaceholders.Activo;
        op.Regiones = chipRegiones.Activo;

        ISintetizador voz = null;
        if (op.Narracion) try { voz = new SintetizadorWindows(); } catch { voz = null; }

        trabajando = true;
        Habilitar(false);
        ResultadoPlan r;
        try
        {
            using (UndoBlock u = new UndoBlock("Estructura y narraci\u00f3n"))
                r = AplicarPlan.Aplicar(vegas, plan, op, voz, inf.Reglas.PPM, trans, plantilla, formato.NarradorNombre, delegate (string t, double f)
                {
                    Estado(t, false);
                    Application.DoEvents();
                });
        }
        catch (Exception ex)
        {
            trabajando = false;
            Habilitar(true);
            Estado("No se pudo aplicar: " + ex.Message, true);
            return;
        }
        trabajando = false;
        Habilitar(true);
        Aplicado = true;
        Dictionary<string, object> extra = new Dictionary<string, object>();
        extra["aplicado"] = r.Aplicado;
        try { LogicaPlan.Guardar(vegas.Project.FilePath, plan, extra); } catch { }
        ExportarGuion(false);
        Estado(r.Texto() + " Guion en " + Path.GetFileName(RutaGuion()) + "." +
               (r.Avisos.Count > 0 ? " " + String.Join(" ", r.Avisos.ToArray()) : "") + " Ctrl+Z lo deshace todo.", r.Avisos.Count > 0);
    }
}

// ---- src/pulir/LogicaPlan.cs ----

// =====================================================================
// PulirEpisodio, parte 2: plan de estructura y narracion con Gemini
//
// Gemini recibe lo que se dice en el video (con los tiempos actuales), las
// pausas donde cabe narracion, el informe de ritmo y el contexto de la
// serie (formato, premisa, papel del capitulo, capitulos anteriores y como
// abrieron) y propone: un gancho al inicio, las secciones, la narracion
// (frases medidas a la velocidad del narrador), los avances en pantalla
// ("Dia 3"), los recursos que faltan (placeholders) y que recortar.
// =====================================================================

public class ItemPlan
{
    public string Tipo = "";        // gancho, seccion, narracion, avance, recurso, recorte
    public double Inicio, Fin;      // en la linea de tiempo de antes de aplicar
    public string Texto = "", Detalle = "", Clase = "";
    public string Id = "";          // N01 (narracion), R01 (recurso)
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

public class Plan
{
    public string Resumen = "", Estructura = "", Respuesta = "", Generado = "";
    public List<ItemPlan> Items = new List<ItemPlan>();

    public List<ItemPlan> De(string tipo)
    {
        List<ItemPlan> r = new List<ItemPlan>();
        foreach (ItemPlan i in Items) if (i.Tipo == tipo) r.Add(i);
        return r;
    }

    public ItemPlan Gancho { get { List<ItemPlan> g = De("gancho"); return g.Count > 0 ? g[0] : null; } }
}

public static class LogicaPlan
{
    static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    public static readonly string[] Tipos = { "gancho", "seccion", "narracion", "avance", "recurso", "recorte" };

    public static string NombreTipo(string t)
    {
        switch (t)
        {
            case "gancho": return "Gancho";
            case "seccion": return "Secci\u00f3n";
            case "narracion": return "Narraci\u00f3n";
            case "avance": return "Avance";
            case "recurso": return "Recurso";
            case "recorte": return "Recorte";
            default: return t;
        }
    }

    // Segundos que tarda en decirse un texto a "ppm" palabras por minuto.
    public static int Palabras(string texto)
    {
        int n = 0;
        foreach (string p in (texto ?? "").Split(new char[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            if (p.Trim(',', '.', ';', ':', '\u00a1', '!', '\u00bf', '?', '"', '\u2026', '-').Length > 0) n++;
        return n;
    }

    public static double Segundos(string texto, int ppm) { return Palabras(texto) * 60.0 / Math.Max(60, ppm) + 0.35; }

    // Pausas sin nadie hablando (en la linea de tiempo actual) de al menos "minimo" segundos.
    public static List<Rango> Pausas(Transcripcion t, double duracion, double minimo)
    {
        List<Rango> voz = new List<Rango>();
        if (t != null)
            foreach (Segmento s in t.SegmentosActuales())
                if (s.Fin > s.Inicio) voz.Add(new Rango(s.Inicio, s.Fin));
        voz = Rangos.Unir(voz, 0.25);
        List<Rango> r = new List<Rango>();
        double cursor = 0;
        foreach (Rango v in voz)
        {
            if (v.Inicio - cursor >= minimo) r.Add(new Rango(cursor, v.Inicio));
            cursor = Math.Max(cursor, v.Fin);
        }
        if (duracion - cursor >= minimo) r.Add(new Rango(cursor, duracion));
        return r;
    }

    // ------------------------------------------------------------ prompt

    public static string Instrucciones(FormatoSerie f, string tipo, string papel, ReglasRitmo r)
    {
        string narrador = f.Narrador
            ? "- NARRACI\u00d3N: frases del narrador (" + f.NarradorNombre + ") que van encima del video. Estilo: " +
              (f.EstiloNarrador.Trim().Length > 0 ? f.EstiloNarrador.Trim() : "cercano, en pasado, con humor") + ".\n" +
              "  \u00b7 Habla a " + r.PPM + " palabras por minuto: una frase de N palabras dura N\u00d760/" + r.PPM + " s. Cada frase de 4 a 30 " +
              "palabras; pon \"inicio\" y calcula \"fin\" con esa velocidad.\n" +
              "  \u00b7 Que nunca pasen m\u00e1s de " + r.NarradorCadaSeg + " s sin narrador (salvo un momento que se cuente solo).\n" +
              "  \u00b7 Ponlas en las PAUSAS (donde nadie habla). Si no hay pausa, encima de charla sin importancia (el juego se baja " +
              "mientras narras). Nunca encima de un chiste o una frase clave.\n" +
              "  \u00b7 Obligatorias: la primera frase entre 0 y 7 s (gancho: promete lo mejor del episodio sin contarlo); contexto " +
              "antes de los 30 s (de qu\u00e9 va la serie o d\u00f3nde qued\u00f3); un \"re-gancho\" antes de " + Formato.Tiempo(r.ZonaCriticaSeg) +
              " (anticipa algo que viene); una invitaci\u00f3n corta a suscribirse en el minuto 1 a 3 (tipo \"cta\"); y una frase " +
              "final que deje un pendiente para el siguiente cap\u00edtulo (tipo \"adelanto\"), salvo que sea el final de la serie.\n" +
              "  \u00b7 Usa ganchos de anticipaci\u00f3n (\"lo cual no fue la mejor idea\", \"esto nos iba a costar caro\").\n"
            : "- NARRACI\u00d3N: esta serie no lleva narrador; deja \"narracion\" vac\u00edo y usa textos en pantalla (recursos tipo texto).\n";
        string avance = f.Avance == "Ninguno" ? "" :
            "- AVANCES: d\u00f3nde poner en pantalla \"" + f.Avance + "\" (cuando cambia el d\u00eda, la parte o la etapa seg\u00fan lo que " +
            "dicen). Texto exacto, por ejemplo \"" + f.Marca(1) + "\".\n";
        return "Eres editor de " + Serie.QueEs(tipo) +
               ". Recibes un episodio YA CORTADO y propones c\u00f3mo estructurarlo para que la gente se quede hasta el final.\n\n" +
               "FORMATO: " + f.Nombre + (FormatoSerie.Objetivo(f.Nombre).Length > 0 ? " (" + FormatoSerie.Objetivo(f.Nombre) + ")" : "") + "\n" +
               "PAPEL DEL CAP\u00cdTULO: " + papel + ". " + PapelEpisodio.Instrucciones(papel) + "\n" +
               "DURACI\u00d3N OBJETIVO: " + r.DuracionMin + "\u2013" + r.DuracionMax + " min.\n\n" +
               "Prop\u00f3n:\n" +
               "- GANCHO: un momento fuerte del video (3 a 8 s, con inicio y fin de la transcripci\u00f3n) para mostrar al inicio, antes " +
               "de todo, como avance. Que no cuente el final. Puede ser null.\n" +
               "- SECCIONES: los bloques del episodio en orden (inicio, fin, nombre corto, para qu\u00e9 sirve).\n" +
               narrador + avance +
               "- RECURSOS: im\u00e1genes, memes, efectos o textos que faltan, sobre todo en los valles del informe y en la zona cr\u00edtica " +
               "(unos " + r.RecursosPorMin + " por minuto donde haya pocos). Cada uno con \"clase\" (imagen, meme, efecto, texto, zoom), " +
               "duraci\u00f3n de 1 a 5 s, una descripci\u00f3n concreta de qu\u00e9 mostrar y un nombre de archivo corto sugerido.\n" +
               "- RECORTES: si dura m\u00e1s que el objetivo o hay tramos largos sin inter\u00e9s (exploraci\u00f3n sola, peleas largas), qu\u00e9 quitar " +
               "o acelerar (accion \"quitar\" o \"acelerar\") y por qu\u00e9.\n\n" +
               "No repitas la forma de abrir de los cap\u00edtulos anteriores (se indica en el contexto). Usa solo tiempos dentro del video.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"el episodio en 2 frases\", \"estructura\": \"c\u00f3mo abre, avanza y cierra, en una frase\",\n" +
               " \"gancho\": {\"inicio\": s, \"fin\": s, \"motivo\": \"...\"} o null,\n" +
               " \"secciones\": [{\"inicio\": s, \"fin\": s, \"nombre\": \"...\", \"proposito\": \"...\"}],\n" +
               " \"narracion\": [{\"inicio\": s, \"fin\": s, \"texto\": \"...\", \"tipo\": \"gancho|contexto|avance|re-gancho|cta|transicion|cierre|adelanto\"}],\n" +
               " \"avances\": [{\"inicio\": s, \"texto\": \"" + (f.Avance == "Ninguno" ? "..." : f.Marca(2)) + "\"}],\n" +
               " \"recursos\": [{\"inicio\": s, \"duracion\": s, \"clase\": \"imagen|meme|efecto|texto|zoom\", \"descripcion\": \"...\", \"archivo\": \"...\"}],\n" +
               " \"recortes\": [{\"inicio\": s, \"fin\": s, \"accion\": \"quitar|acelerar\", \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(Transcripcion t, double duracion, Informe inf, string contextoSerie, string anteriores,
                                 string indicaciones, List<Rango> pausas)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Duraci\u00f3n del video: " + S(duracion) + " s (" + Formato.Tiempo(duracion) + ")\n");
        if (!String.IsNullOrEmpty(contextoSerie)) sb.Append("\nCONTEXTO DE LA SERIE:\n" + contextoSerie.Trim() + "\n");
        if (!String.IsNullOrEmpty(anteriores)) sb.Append("\nC\u00d3MO ABRIERON Y CERRARON LOS CAP\u00cdTULOS ANTERIORES (no repetir):\n" + anteriores.Trim() + "\n");
        if (!String.IsNullOrEmpty(indicaciones)) sb.Append("\nINDICACIONES DEL EDITOR:\n" + indicaciones.Trim() + "\n");
        if (inf != null) sb.Append("\nINFORME DE RITMO:\n" + LogicaPulir.Texto(inf));
        sb.Append("\nPAUSAS (nadie habla) [inicio-fin]:\n");
        foreach (Rango p in pausas) sb.Append("[" + S(p.Inicio) + "-" + S(p.Fin) + "] ");
        sb.Append("\n\nTRANSCRIPCI\u00d3N (tiempos del video actual) [inicio-fin] persona: texto\n");
        if (t != null)
            foreach (Segmento s in t.SegmentosActuales())
            {
                if (String.IsNullOrEmpty(s.Texto)) continue;
                string quien = s.Hablante >= 0 && s.Hablante < t.Hablantes.Count ? t.Hablantes[s.Hablante].Nombre : "?";
                sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + quien + ": " + s.Texto + "\n");
            }
        return sb.ToString();
    }

    // "S01E01: abre con ... \u00b7 cierra con ..." de las fichas de los capitulos anteriores.
    public static string Anteriores(List<CapSerie> caps)
    {
        StringBuilder sb = new StringBuilder();
        foreach (CapSerie c in caps)
        {
            if (c.Relacion >= 0) continue;
            Ficha f = Ficha.Cargar(c.Veg);
            if (f != null && f.Estructura.Trim().Length > 0) sb.Append("- " + c.Nombre + ": " + f.Estructura.Trim() + "\n");
        }
        return sb.ToString();
    }

    // ----------------------------------------------------------- respuesta

    static double Num(object x, string k, double siNo) { return Json.Numero(x, k, siNo); }

    public static Plan Leer(string json, double duracion, int ppm)
    {
        Plan p = new Plan();
        p.Respuesta = json;
        object o = Json.Leer(Gemini.QuitarCercas(json));
        p.Resumen = Json.Texto(o, "resumen");
        p.Estructura = Json.Texto(o, "estructura");

        object g = Json.Valor(o, "gancho");
        if (g is Dictionary<string, object>)
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "gancho";
            i.Inicio = Num(g, "inicio", -1); i.Fin = Num(g, "fin", -1);
            i.Texto = Json.Texto(g, "motivo");
            if (i.Inicio >= 0 && i.Fin > i.Inicio + 0.5 && i.Fin <= duracion + 0.5)
            {
                if (i.Duracion > 10) i.Fin = i.Inicio + 10;
                p.Items.Add(i);
            }
        }
        foreach (object x in Json.Lista(o, "secciones"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "seccion";
            i.Inicio = Num(x, "inicio", -1); i.Fin = Num(x, "fin", -1);
            i.Texto = Json.Texto(x, "nombre"); i.Detalle = Json.Texto(x, "proposito");
            if (i.Inicio >= 0 && i.Fin > i.Inicio && i.Texto.Length > 0) { i.Fin = Math.Min(i.Fin, duracion); p.Items.Add(i); }
        }
        List<ItemPlan> narr = new List<ItemPlan>();
        foreach (object x in Json.Lista(o, "narracion"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "narracion";
            i.Inicio = Num(x, "inicio", -1);
            i.Texto = Json.Texto(x, "texto").Trim();
            i.Clase = Json.Texto(x, "tipo");
            if (i.Inicio < 0 || i.Inicio > duracion || Palabras(i.Texto) == 0) continue;
            i.Fin = i.Inicio + Segundos(i.Texto, ppm);
            narr.Add(i);
        }
        Acomodar(narr, duracion);
        for (int k = 0; k < narr.Count; k++) narr[k].Id = "N" + (k + 1).ToString("00");
        p.Items.AddRange(narr);
        foreach (object x in Json.Lista(o, "avances"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "avance";
            i.Inicio = Num(x, "inicio", -1); i.Texto = Json.Texto(x, "texto").Trim();
            i.Fin = i.Inicio + 3;
            if (i.Inicio >= 0 && i.Inicio < duracion && i.Texto.Length > 0) p.Items.Add(i);
        }
        int nr = 0;
        foreach (object x in Json.Lista(o, "recursos"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "recurso";
            i.Inicio = Num(x, "inicio", -1);
            i.Fin = i.Inicio + Math.Max(1, Math.Min(6, Num(x, "duracion", 3)));
            i.Clase = Json.Texto(x, "clase");
            i.Texto = Json.Texto(x, "descripcion").Trim();
            i.Detalle = Limpio(Json.Texto(x, "archivo"));
            if (i.Inicio < 0 || i.Inicio >= duracion || i.Texto.Length == 0) continue;
            i.Fin = Math.Min(i.Fin, duracion);
            i.Id = "R" + (++nr).ToString("00");
            p.Items.Add(i);
        }
        foreach (object x in Json.Lista(o, "recortes"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "recorte";
            i.Inicio = Num(x, "inicio", -1); i.Fin = Num(x, "fin", -1);
            i.Clase = Json.Texto(x, "accion") == "acelerar" ? "acelerar" : "quitar";
            i.Texto = Json.Texto(x, "motivo");
            if (i.Inicio >= 0 && i.Fin > i.Inicio + 1) { i.Fin = Math.Min(i.Fin, duracion); p.Items.Add(i); }
        }
        p.Items.Sort(delegate (ItemPlan a, ItemPlan b)
        {
            int c = a.Inicio.CompareTo(b.Inicio);
            return c != 0 ? c : Array.IndexOf(Tipos, a.Tipo).CompareTo(Array.IndexOf(Tipos, b.Tipo));
        });
        // El gancho va primero siempre.
        ItemPlan gancho = p.Gancho;
        if (gancho != null) { p.Items.Remove(gancho); p.Items.Insert(0, gancho); }
        return p;
    }

    static string Limpio(string archivo)
    {
        string r = archivo ?? "";
        foreach (char c in Path.GetInvalidFileNameChars()) r = r.Replace(c, '_');
        r = r.Replace('\\', '_').Replace('/', '_').Trim();
        if (r.Contains(".")) r = r.Substring(0, r.LastIndexOf('.'));
        return r.Length > 40 ? r.Substring(0, 40) : r;
    }

    // Que las frases no se encimen: si una empieza antes de que termine la
    // anterior, se corre; si ya no cabe en el video, se quita.
    public static void Acomodar(List<ItemPlan> narr, double duracion)
    {
        narr.Sort(delegate (ItemPlan a, ItemPlan b) { return a.Inicio.CompareTo(b.Inicio); });
        double fin = 0;
        for (int k = 0; k < narr.Count; k++)
        {
            ItemPlan i = narr[k];
            double largo = i.Duracion;
            if (i.Inicio < fin + 0.2) i.Inicio = fin + 0.2;
            i.Fin = i.Inicio + largo;
            if (i.Fin > duracion + 0.5) { narr.RemoveAt(k); k--; continue; }
            fin = i.Fin;
        }
    }

    // Lo que hay que correr cada tiempo si se pone el gancho al inicio.
    public static double Corrimiento(Plan p, bool conGancho)
    {
        ItemPlan g = p.Gancho;
        return conGancho && g != null && g.Elegido ? Math.Round(g.Duracion + 0.5, 3) : 0;
    }

    // ---------------------------------------------------------- guion

    public static string Guion(Plan p, string proyecto, double corrimiento, int ppm)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("GUION DE NARRACI\u00d3N \u00b7 " + proyecto + "\n");
        sb.Append("Lee de corrido, con una pausa corta entre frases. Si una sale mal, rep\u00edtela entera: se usa la \u00faltima toma.\n");
        sb.Append("Velocidad objetivo: " + ppm + " palabras por minuto.\n\n");
        foreach (ItemPlan i in p.Items)
        {
            if (i.Tipo != "narracion" || !i.Elegido) continue;
            sb.Append(i.Id + "  [" + Formato.TiempoPreciso(i.Inicio + corrimiento) + "]" + (i.Clase.Length > 0 ? " (" + i.Clase + ")" : "") + "\n");
            sb.Append("    " + i.Texto + "\n\n");
        }
        List<ItemPlan> rec = p.De("recurso");
        if (rec.Count > 0)
        {
            sb.Append("\nRECURSOS (nombra el archivo empezando con su c\u00f3digo, por ejemplo \"R01 algo.png\")\n");
            foreach (ItemPlan i in rec)
                if (i.Elegido)
                    sb.Append(i.Id + "  [" + Formato.Tiempo(i.Inicio + corrimiento) + "] " + i.Clase + ": " + i.Texto +
                              (i.Detalle.Length > 0 ? "  \u2192 " + i.Id + " " + i.Detalle : "") + "\n");
        }
        return sb.ToString();
    }

    // ------------------------------------------------- guardar y cargar

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-plan.json");
    }

    public static void Guardar(string veg, Plan p, Dictionary<string, object> extra)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["generado"] = p.Generado;
        d["respuesta"] = p.Respuesta;
        List<object> fuera = new List<object>();
        foreach (ItemPlan i in p.Items) if (!i.Elegido) fuera.Add(i.Tipo + "|" + S(i.Inicio) + "|" + i.Texto);
        d["descartados"] = fuera;
        // Lo aplicado antes se conserva (sirve para reemplazar la narracion y los placeholders).
        object previo = Aplicado(veg);
        if (previo != null) d["aplicado"] = previo;
        if (extra != null) foreach (KeyValuePair<string, object> kv in extra) d[kv.Key] = kv.Value;
        File.WriteAllText(RutaPara(veg), Json.Escribir(d), new UTF8Encoding(false));
    }

    public static Plan Cargar(string veg, double duracion, int ppm)
    {
        try
        {
            string r = RutaPara(veg);
            if (!File.Exists(r)) return null;
            object o = Json.Leer(File.ReadAllText(r, Encoding.UTF8));
            Plan p = Leer(Json.Texto(o, "respuesta"), duracion, ppm);
            p.Generado = Json.Texto(o, "generado");
            List<string> fuera = new List<string>();
            foreach (object x in Json.Lista(o, "descartados")) if (x is string) fuera.Add((string)x);
            foreach (ItemPlan i in p.Items) if (fuera.Contains(i.Tipo + "|" + S(i.Inicio) + "|" + i.Texto)) i.Elegido = false;
            return p;
        }
        catch { return null; }
    }

    // Lo que ya se aplico (narracion y placeholders), para "Reemplazar".
    public static object Aplicado(string veg)
    {
        try
        {
            string r = RutaPara(veg);
            return File.Exists(r) ? Json.Obj(Json.Leer(File.ReadAllText(r, Encoding.UTF8)), "aplicado") : null;
        }
        catch { return null; }
    }
}

// ---- src/pulir/AplicarPlan.cs ----

// =====================================================================
// Pone el plan en la linea de tiempo: gancho al inicio, regiones de
// secciones y recortes, avances ("Dia 3"), placeholders de recursos y la
// narracion provisional con el juego bajado mientras suena.
// =====================================================================

public class OpcionesPlan
{
    public bool Gancho = true, Regiones = true, Avances = true, Placeholders = true, Narracion = true, Bajar = true;
    public int BajaDb = -10;
}

public class ResultadoPlan
{
    public int Narraciones, Avances, Placeholders, Regiones, Copiados;
    public double Corrimiento, SegundosNarracion;
    public List<string> Avisos = new List<string>();
    public Dictionary<string, object> Aplicado = new Dictionary<string, object>();

    public string Texto()
    {
        List<string> l = new List<string>();
        if (Corrimiento > 0) l.Add("gancho de " + Formato.Tiempo(Corrimiento) + " al inicio");
        if (Narraciones > 0) l.Add(Narraciones + " frases de narraci\u00f3n (" + Formato.Tiempo(SegundosNarracion) + ")");
        if (Avances > 0) l.Add(Avances + " avances");
        if (Placeholders > 0) l.Add(Placeholders + " placeholders");
        if (Regiones > 0) l.Add(Regiones + " regiones");
        return l.Count == 0 ? "No se aplic\u00f3 nada." : "\u2714 " + String.Join(", ", l.ToArray()) + ".";
    }
}

public static class AplicarPlan
{
    public const string PistaNarracion = RitmoVegas.PistaNarracion;
    public const string PistaAvances = "vegas-cut \u00b7 Avances";
    public const string PistaPlaceholders = "vegas-cut \u00b7 Placeholders";
    static readonly string[] Prefijos = { "GANCHO", "SECCI\u00d3N \u00b7 ", "RECORTAR \u00b7 ", "ACELERAR \u00b7 " };

    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    public static ResultadoPlan Aplicar(Vegas vegas, Plan plan, OpcionesPlan op, ISintetizador voz, int ppm,
                                        Transcripcion t, VideoEvent plantilla, Action<string, double> estado)
    {
        return Aplicar(vegas, plan, op, voz, ppm, t, plantilla, "Narrador", estado);
    }

    public static ResultadoPlan Aplicar(Vegas vegas, Plan plan, OpcionesPlan op, ISintetizador voz, int ppm,
                                        Transcripcion t, VideoEvent plantilla, string narrador, Action<string, double> estado)
    {
        Project p = vegas.Project;
        ResultadoPlan r = new ResultadoPlan();
        double corr = LogicaPlan.Corrimiento(plan, op.Gancho);

        // 1. Gancho: se corre todo y se copia ese momento al inicio.
        if (corr > 0)
        {
            estado("Poniendo el gancho al inicio\u2026", 0.02);
            ItemPlan g = plan.Gancho;
            Editor.Desplazar(p, corr);
            r.Copiados = CopiarTramo(p, g.Inicio + corr, g.Fin + corr, 0);
            r.Corrimiento = corr;
        }

        // 2. Regiones (se quitan las de un plan anterior).
        if (op.Regiones)
        {
            QuitarRegiones(p);
            if (corr > 0) { p.Regions.Add(new Region(TC(0), TC(corr - 0.5), "GANCHO")); r.Regiones++; }
            foreach (ItemPlan i in plan.Items)
            {
                if (!i.Elegido) continue;
                string etiqueta = i.Tipo == "seccion" ? "SECCI\u00d3N \u00b7 " + i.Texto
                                : i.Tipo == "recorte" ? (i.Clase == "acelerar" ? "ACELERAR \u00b7 " : "RECORTAR \u00b7 ") + i.Texto : null;
                if (etiqueta == null) continue;
                p.Regions.Add(new Region(TC(i.Inicio + corr), TC(i.Duracion), etiqueta));
                r.Regiones++;
            }
        }

        // 3. Avances en pantalla con el estilo del texto que ya uses.
        if (op.Avances)
        {
            List<ItemPlan> avs = Elegidos(plan, "avance");
            if (avs.Count > 0)
            {
                Plantilla pt = GeneradorTexto.Buscar(vegas);
                VideoTrack pista = PistaVideo(p, PistaAvances);
                foreach (ItemPlan i in avs)
                {
                    try { GeneradorTexto.Crear(pista, pt, i.Inicio + corr, 3, i.Texto); r.Avances++; }
                    catch (Exception ex) { r.Avisos.Add("Avance \u00ab" + i.Texto + "\u00bb: " + ex.Message); break; }
                }
            }
        }

        // 4. Placeholders: copia del evento plantilla (efectos, movimiento y
        // fundidos) con un texto que dice que va ahi.
        List<object> phs = new List<object>();
        if (op.Placeholders)
        {
            List<ItemPlan> recs = Elegidos(plan, "recurso");
            if (recs.Count > 0)
            {
                Plantilla etiqueta = GeneradorTexto.PorDefecto(vegas);
                VideoTrack pista = PistaVideo(p, PistaPlaceholders);
                foreach (ItemPlan i in recs)
                {
                    string texto = "[" + i.Id + "] " + (i.Clase.Length > 0 ? i.Clase.ToUpperInvariant() + ": " : "") + i.Texto;
                    try
                    {
                        if (plantilla != null)
                        {
                            TrackEvent copia = plantilla.Copy(pista, TC(i.Inicio + corr));
                            copia.Length = TC(i.Duracion);
                            Media m = GeneradorTexto.Medio(etiqueta, texto);
                            copia.AddTake(m.Streams.GetItemByMediaType(MediaType.Video, 0), true);
                        }
                        else
                            GeneradorTexto.Crear(pista, etiqueta, i.Inicio + corr, i.Duracion, texto);
                        r.Placeholders++;
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["id"] = i.Id; d["inicio"] = Math.Round(i.Inicio + corr, 3); d["clase"] = i.Clase;
                        d["descripcion"] = i.Texto; d["archivo"] = i.Detalle;
                        phs.Add(d);
                    }
                    catch (Exception ex) { r.Avisos.Add("Placeholder " + i.Id + ": " + ex.Message); break; }
                }
            }
        }
        r.Aplicado["placeholders"] = phs;

        // 5. Narracion provisional y el juego mas bajo mientras suena.
        List<object> lineas = new List<object>();
        if (op.Narracion)
        {
            List<ItemPlan> narr = Elegidos(plan, "narracion");
            if (narr.Count > 0 && voz == null)
                r.Avisos.Add("No encontr\u00e9 la voz de Windows (System.Speech): la narraci\u00f3n qued\u00f3 solo en el guion.");
            else if (narr.Count > 0)
            {
                string carpeta = CarpetaNarracion(p.FilePath);
                Directory.CreateDirectory(carpeta);
                AudioTrack pista = PistaAudio(p, PistaNarracion);
                List<Rango> suena = new List<Rango>();
                int velocidad = 2;
                for (int k = 0; k < narr.Count; k++)
                {
                    ItemPlan i = narr[k];
                    estado("Narraci\u00f3n " + (k + 1) + " de " + narr.Count + " (" + voz.Nombre + ")\u2026", 0.1 + 0.8 * k / narr.Count);
                    string wav = Path.Combine(carpeta, i.Id + ".wav");
                    double d;
                    try { d = VozProvisional.Generar(voz, i.Texto, LogicaPlan.Segundos(i.Texto, ppm), wav, ref velocidad); }
                    catch (Exception ex) { r.Avisos.Add(i.Id + ": " + ex.Message); continue; }
                    double a = i.Inicio + corr;
                    Media m = new Media(wav);
                    AudioEvent ev = pista.AddAudioEvent(TC(a), TC(d));
                    ev.AddTake(m.Streams.GetItemByMediaType(MediaType.Audio, 0));
                    suena.Add(new Rango(a, a + d));
                    r.Narraciones++;
                    r.SegundosNarracion += d;
                    Dictionary<string, object> x = new Dictionary<string, object>();
                    x["id"] = i.Id; x["texto"] = i.Texto; x["inicio"] = Math.Round(a, 3); x["fin"] = Math.Round(a + d, 3);
                    lineas.Add(x);
                }
                if (op.Bajar && suena.Count > 0)
                {
                    estado("Bajando el juego bajo la narraci\u00f3n\u2026", 0.95);
                    foreach (Track g in RitmoVegas.PistasGrabacion(p, t, narrador))
                        if (g != pista) Bajar(g, suena, op.BajaDb);
                }
            }
        }
        r.Aplicado["narracion"] = lineas;
        r.Aplicado["corrimiento"] = Math.Round(corr, 3);
        r.Aplicado["fecha"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        return r;
    }

    static List<ItemPlan> Elegidos(Plan plan, string tipo)
    {
        List<ItemPlan> l = new List<ItemPlan>();
        foreach (ItemPlan i in plan.Items) if (i.Tipo == tipo && i.Elegido) l.Add(i);
        return l;
    }

    public static string CarpetaNarracion(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-narracion");
    }

    static VideoTrack PistaVideo(Project p, string nombre)
    {
        foreach (Track t in p.Tracks) if (!t.IsAudio() && t.Name == nombre) return (VideoTrack)t;
        VideoTrack v = new VideoTrack(0, nombre);   // arriba de todo, para que se vea encima
        p.Tracks.Add(v);
        return v;
    }

    static AudioTrack PistaAudio(Project p, string nombre)
    {
        foreach (Track t in p.Tracks) if (t.IsAudio() && t.Name == nombre) return (AudioTrack)t;
        AudioTrack a = new AudioTrack(p.Tracks.Count, nombre);
        p.Tracks.Add(a);
        return a;
    }

    public static int QuitarRegiones(Project p)
    {
        List<Region> viejas = new List<Region>();
        foreach (Region x in p.Regions)
            foreach (string pre in Prefijos)
                if ((x.Label ?? "").StartsWith(pre)) { viejas.Add(x); break; }
        foreach (Region x in viejas) p.Regions.Remove(x);
        return viejas.Count;
    }

    // Baja la pista mientras suena la narracion (frases muy juntas, una sola bajada).
    static void Bajar(Track pista, List<Rango> suena, int db)
    {
        foreach (Rango x in Rangos.Unir(suena, 1.2))
        {
            double a = Math.Max(0, x.Inicio - 0.4), b = x.Fin + 0.8;
            List<PuntoVolumen> pts = LogicaMusica.Puntos(new List<Rango> { x }, a, b, db, 0.25, 0.4);
            LogicaMusica.Aplicar(pista, pts, a, b);
        }
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

    // ------------------------------------------------- reemplazar placeholders

    // Archivo de la carpeta (o una subcarpeta) cuyo nombre empieza con el
    // codigo: "R03 cadaver de steve.png", "r03.jpg"...
    public static string Buscar(List<string> archivos, string id)
    {
        foreach (string f in archivos)
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (n.Length < id.Length || !n.StartsWith(id, StringComparison.OrdinalIgnoreCase)) continue;
            if (n.Length == id.Length || !char.IsDigit(n[id.Length])) return f;
        }
        return null;
    }

    static readonly string[] Extensiones = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".mp4", ".mov", ".mkv", ".webm", ".avi" };

    public static List<string> Archivos(string carpeta)
    {
        List<string> l = new List<string>();
        try
        {
            foreach (string f in Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories))
                if (Array.IndexOf(Extensiones, Path.GetExtension(f).ToLowerInvariant()) >= 0) l.Add(f);
        }
        catch { }
        l.Sort(StringComparer.OrdinalIgnoreCase);
        return l;
    }

    // Pone el archivo real como toma activa de cada placeholder que lo tenga
    // (conserva efectos, movimiento y fundidos). Devuelve los que faltan.
    public static int ReemplazarPlaceholders(Project p, string carpeta, List<string> faltan)
    {
        List<string> archivos = Archivos(carpeta);
        int n = 0;
        foreach (Track t in p.Tracks)
        {
            if (t.IsAudio() || t.Name != PistaPlaceholders) continue;
            foreach (TrackEvent e in t.Events)
            {
                string texto = GeneradorTexto.TextoDe(e);
                if (!texto.StartsWith("[R")) continue;
                int cierra = texto.IndexOf(']');
                if (cierra < 0) continue;
                string id = texto.Substring(1, cierra - 1);
                string f = Buscar(archivos, id);
                if (f == null) { faltan.Add(id); continue; }
                Media m = new Media(f);
                MediaStream s = m.Streams.GetItemByMediaType(MediaType.Video, 0);
                if (s == null) { faltan.Add(id + " (sin video)"); continue; }
                e.AddTake(s, true);
                n++;
            }
        }
        return n;
    }
}

// ---- src/pulir/Voz.cs ----

// =====================================================================
// Voz provisional del narrador con la sintesis de Windows (System.Speech).
// Se carga por reflexion porque Vegas no la referencia al compilar. Suena
// robotica a proposito: es solo para tener la duracion y el ritmo; despues
// se reemplaza por la grabacion.
// =====================================================================

public interface ISintetizador
{
    string Nombre { get; }
    // Escribe el WAV del texto a esa velocidad (-10 a 10).
    void Decir(string texto, int velocidad, string wav);
}

public class SintetizadorWindows : ISintetizador
{
    readonly object sintesis;
    readonly Type tipo;
    string nombre = "voz de Windows";

    public string Nombre { get { return nombre; } }

    public SintetizadorWindows()
    {
        Assembly a = Assembly.Load("System.Speech, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
        tipo = a.GetType("System.Speech.Synthesis.SpeechSynthesizer", true);
        sintesis = Activator.CreateInstance(tipo);
        ElegirEspanol();
    }

    // La primera voz en espanol instalada (Helena, Sabina, Laura...).
    void ElegirEspanol()
    {
        try
        {
            System.Collections.IEnumerable voces = (System.Collections.IEnumerable)tipo.GetMethod("GetInstalledVoices", Type.EmptyTypes).Invoke(sintesis, null);
            foreach (object v in voces)
            {
                object info = v.GetType().GetProperty("VoiceInfo").GetValue(v, null);
                System.Globalization.CultureInfo c = (System.Globalization.CultureInfo)info.GetType().GetProperty("Culture").GetValue(info, null);
                string n = (string)info.GetType().GetProperty("Name").GetValue(info, null);
                if (c != null && c.TwoLetterISOLanguageName == "es")
                {
                    tipo.GetMethod("SelectVoice", new Type[] { typeof(string) }).Invoke(sintesis, new object[] { n });
                    nombre = n;
                    return;
                }
            }
        }
        catch { }
    }

    public void Decir(string texto, int velocidad, string wav)
    {
        tipo.GetProperty("Rate").SetValue(sintesis, Math.Max(-10, Math.Min(10, velocidad)), null);
        tipo.GetMethod("SetOutputToWaveFile", new Type[] { typeof(string) }).Invoke(sintesis, new object[] { wav });
        try { tipo.GetMethod("Speak", new Type[] { typeof(string) }).Invoke(sintesis, new object[] { texto }); }
        finally { tipo.GetMethod("SetOutputToNull", Type.EmptyTypes).Invoke(sintesis, null); }
    }
}

public static class VozProvisional
{
    // Cada punto de "Rate" de Windows acelera o frena ~11.6 % (+10 = x3).
    static readonly double Paso = Math.Pow(3, 0.1);

    // Duracion de un WAV en segundos (lee la cabecera).
    public static double Duracion(string wav)
    {
        using (FileStream fs = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader br = new BinaryReader(fs))
        {
            if (new string(br.ReadChars(4)) != "RIFF") throw new Exception("El archivo no es WAV.");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new Exception("El archivo no es WAV.");
            int porSegundo = 0;
            long datos = -1;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(br.ReadChars(4));
                long tam = br.ReadUInt32();
                long siguiente = fs.Position + tam + (tam & 1);
                if (id == "fmt ") { br.ReadUInt16(); br.ReadUInt16(); br.ReadInt32(); porSegundo = br.ReadInt32(); }
                else if (id == "data") { datos = Math.Min(tam, fs.Length - fs.Position); break; }
                fs.Position = siguiente;
            }
            if (porSegundo <= 0 || datos < 0) throw new Exception("WAV sin datos.");
            return datos / (double)porSegundo;
        }
    }

    // Velocidad para que el texto dure "objetivo" segundos, sabiendo cuanto
    // duro a la velocidad "probada".
    public static int Velocidad(double duro, int probada, double objetivo)
    {
        if (duro <= 0 || objetivo <= 0) return probada;
        double v = probada + Math.Log(duro / objetivo) / Math.Log(Paso);
        return (int)Math.Max(-10, Math.Min(10, Math.Round(v)));
    }

    // Dice la frase y, si dura mas de un 12 % de lo que tomaria a la
    // velocidad del narrador, la repite con la velocidad corregida.
    // "inicial" es la ultima velocidad que funciono (se ajusta sola).
    public static double Generar(ISintetizador voz, string texto, double objetivo, string wav, ref int inicial)
    {
        voz.Decir(texto, inicial, wav);
        double d = Duracion(wav);
        if (Math.Abs(d - objetivo) / Math.Max(0.5, objetivo) > 0.12)
        {
            int v = Velocidad(d, inicial, objetivo);
            if (v != inicial)
            {
                voz.Decir(texto, v, wav);
                d = Duracion(wav);
                inicial = v;
            }
        }
        return d;
    }
}

// ---- src/textos/Generador.cs ----

// =====================================================================
// Eventos de "Titulos y texto" a partir de una plantilla
//
// Cada evento de texto es un medio generado con sus propios parametros
// (OFX). Para copiar el estilo se crea un medio nuevo con el mismo generador
// y se le pasan, uno por uno, los valores de la plantilla; despues se cambia
// solo el texto (RTF), conservando su formato.
// =====================================================================

public class Plantilla
{
    public VideoEvent Evento;     // null: Titulos y texto con el estilo por defecto
    public PlugInNode PlugIn;
    public string TextoRtf = "";
    public string Origen = "";    // como se encontro, para mostrarlo
    public string Texto { get { return Rtf.TextoPlano(TextoRtf); } }
}

public static class GeneradorTexto
{
    static readonly string[] Ids = { "{Svfx:com.vegascreativesoftware:titlesandtext}",
                                     "{Svfx:com.sonycreativesoftware:titlesandtext}" };

    static OFXEffect Ofx(Effect e)
    {
        try { return e != null && e.IsOFX ? e.OFXEffect : null; } catch { return null; }
    }

    static OFXStringParameter ParametroTexto(Media m)
    {
        try
        {
            if (m == null || !m.IsGenerated()) return null;
            OFXEffect o = Ofx(m.Generator);
            return o == null ? null : o.FindParameterByName("Text") as OFXStringParameter;
        }
        catch { return null; }
    }

    static Media MediaDe(TrackEvent e)
    {
        return e == null || e.ActiveTake == null ? null : e.ActiveTake.Media;
    }

    public static bool EsTexto(TrackEvent e) { return e is VideoEvent && ParametroTexto(MediaDe(e)) != null; }

    // Plantilla: el texto seleccionado; si no hay, el texto mas cercano al
    // cursor; si no hay ninguno, Titulos y texto con su estilo normal.
    public static Plantilla Buscar(Vegas vegas)
    {
        double cursor = vegas.Transport.CursorPosition.ToMilliseconds() / 1000.0;
        VideoEvent elegido = null, cercano = null;
        double mejor = double.MaxValue;
        foreach (Track t in vegas.Project.Tracks)
        {
            if (t.IsAudio()) continue;
            foreach (TrackEvent e in t.Events)
            {
                if (!EsTexto(e)) continue;
                if (e.Selected && elegido == null) elegido = (VideoEvent)e;
                double d = Math.Abs(e.Start.ToMilliseconds() / 1000.0 - cursor);
                if (d < mejor) { mejor = d; cercano = (VideoEvent)e; }
            }
        }
        Plantilla p = new Plantilla();
        p.Evento = elegido ?? cercano;
        if (p.Evento != null)
        {
            p.Origen = elegido != null ? "el texto seleccionado" : "el texto m\u00e1s cercano al cursor";
            p.PlugIn = MediaDe(p.Evento).Generator.PlugIn;
            p.TextoRtf = ParametroTexto(MediaDe(p.Evento)).Value ?? "";
            return p;
        }
        p.Origen = "T\u00edtulos y texto con su estilo normal (no hay ning\u00fan texto en el proyecto)";
        foreach (string id in Ids)
        {
            try { p.PlugIn = vegas.Generators.GetChildByUniqueID(id); } catch { }
            if (p.PlugIn != null) break;
        }
        return p;
    }

    // Titulos y texto con su estilo normal (sin copiar de otro texto).
    public static Plantilla PorDefecto(Vegas vegas)
    {
        Plantilla p = new Plantilla();
        p.Origen = "T\u00edtulos y texto con su estilo normal";
        foreach (string id in Ids)
        {
            try { p.PlugIn = vegas.Generators.GetChildByUniqueID(id); } catch { }
            if (p.PlugIn != null) break;
        }
        return p;
    }

    // Texto plano de un evento de texto ("" si no lo es).
    public static string TextoDe(TrackEvent e)
    {
        OFXStringParameter t = ParametroTexto(MediaDe(e));
        return t == null ? "" : Rtf.TextoPlano(t.Value ?? "");
    }

    // Un medio de texto con el estilo de la plantilla, sin ponerlo en la linea de tiempo.
    public static Media Medio(Plantilla p, string texto)
    {
        if (p.PlugIn == null) throw new Exception("No se encontr\u00f3 el generador de T\u00edtulos y texto.");
        Media media = new Media(p.PlugIn);
        OFXEffect nuevo = Ofx(media.Generator);
        if (p.Evento != null) CopiarParametros(Ofx(MediaDe(p.Evento).Generator), nuevo);
        OFXStringParameter txt = ParametroTexto(media);
        if (txt != null) txt.Value = Rtf.ReemplazarTexto(p.TextoRtf, texto);
        if (nuevo != null) try { nuevo.AllParametersChanged(); } catch { }
        return media;
    }

    public static int PistaDe(Plantilla p) { return p.Evento == null ? -1 : p.Evento.Track.Index; }

    // Crea el evento de texto en la pista, de inicio a inicio+duracion.
    public static VideoEvent Crear(VideoTrack pista, Plantilla p, double inicio, double duracion, string texto)
    {
        if (p.PlugIn == null) throw new Exception("No se encontr\u00f3 el generador de T\u00edtulos y texto.");
        Media media = new Media(p.PlugIn);
        MediaStream flujo = media.Streams.GetItemByMediaType(MediaType.Video, 0);
        VideoEvent ev = pista.AddVideoEvent(Timecode.FromMilliseconds(inicio * 1000), Timecode.FromMilliseconds(duracion * 1000));
        ev.AddTake(flujo);

        OFXEffect nuevo = Ofx(media.Generator);
        if (p.Evento != null)
        {
            Media origen = MediaDe(p.Evento);
            CopiarParametros(Ofx(origen.Generator), nuevo);
            try { ev.FadeIn.Length = p.Evento.FadeIn.Length; ev.FadeOut.Length = p.Evento.FadeOut.Length; } catch { }
            CopiarEfectos(p.Evento, ev);
        }
        OFXStringParameter txt = ParametroTexto(media);
        if (txt != null) txt.Value = Rtf.ReemplazarTexto(p.TextoRtf, texto);
        if (nuevo != null) try { nuevo.AllParametersChanged(); } catch { }
        return ev;
    }

    // Efectos del evento (sombra, borde, movimiento...) con sus valores.
    static void CopiarEfectos(VideoEvent de, VideoEvent a)
    {
        try
        {
            foreach (Effect fx in de.Effects)
            {
                try
                {
                    Effect copia = new Effect(fx.PlugIn);
                    a.Effects.Add(copia);
                    copia.Bypass = fx.Bypass;
                    CopiarParametros(Ofx(fx), Ofx(copia));
                    OFXEffect o = Ofx(copia);
                    if (o != null) o.AllParametersChanged();
                }
                catch { }
            }
        }
        catch { }
    }

    // Copia cada parametro por nombre (por reflexion: cada tipo de parametro
    // tiene su propio Value). Si esta animado, tambien sus fotogramas clave.
    public static int CopiarParametros(OFXEffect de, OFXEffect a)
    {
        if (de == null || a == null) return 0;
        int n = 0;
        foreach (OFXParameter p in de.Parameters)
        {
            if (p.Name == "Text") continue;
            OFXParameter q;
            try { q = a.FindParameterByName(p.Name); } catch { continue; }
            if (q == null || q.GetType() != p.GetType()) continue;
            try
            {
                PropertyInfo valor = p.GetType().GetProperty("Value");
                if (valor == null || !valor.CanWrite) continue;
                valor.SetValue(q, valor.GetValue(p, null), null);
                n++;
                CopiarClaves(p, q);
            }
            catch { }
        }
        return n;
    }

    static void CopiarClaves(OFXParameter de, OFXParameter a)
    {
        Type t = de.GetType();
        PropertyInfo animado = t.GetProperty("IsAnimated");
        if (animado == null || !(bool)animado.GetValue(de, null)) return;
        PropertyInfo claves = t.GetProperty("Keyframes");
        MethodInfo poner = t.GetMethod("SetValueAtTime");
        if (claves == null || poner == null) return;
        if (animado.CanWrite) animado.SetValue(a, true, null);
        foreach (object k in (IEnumerable)claves.GetValue(de, null))
        {
            object tiempo = k.GetType().GetProperty("Time").GetValue(k, null);
            object v = k.GetType().GetProperty("Value").GetValue(k, null);
            poner.Invoke(a, new object[] { tiempo, v });
        }
    }
}

// ---- src/comun/Rtf.cs ----

// =====================================================================
// Texto enriquecido (RTF) de los eventos de Titulos y texto
//
// Vegas guarda el texto de "Titles & Text" como RTF: fuente, tamano, color y
// alineacion van como comandos (\f0\fs48\cf1...) antes del texto. Para usar
// un texto como plantilla se conserva todo lo que hay antes del primer
// caracter visible y despues del ultimo, y solo se cambia lo de en medio.
// =====================================================================

public static class Rtf
{
    // Grupos que no son texto visible: tablas de fuentes, colores, estilos...
    static readonly string[] Destinos = { "fonttbl", "colortbl", "stylesheet", "info", "generator", "pict",
                                          "header", "footer", "listtable", "listoverridetable", "themedata",
                                          "colorschememapping", "latentstyles", "datastore", "rsidtbl", "xmlnstbl" };

    // Una pieza del RTF: texto visible (Texto != null) o comando.
    class Pieza
    {
        public int Desde, Hasta;
        public string Texto;      // caracteres visibles que aporta
        public string Comando;    // nombre del comando (sin \)
    }

    static List<Pieza> Piezas(string rtf)
    {
        List<Pieza> r = new List<Pieza>();
        Stack<bool> pila = new Stack<bool>();
        bool oculto = false, inicioGrupo = false;
        int uc = 1, i = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            if (c == '{') { pila.Push(oculto); inicioGrupo = true; i++; continue; }
            if (c == '}') { if (pila.Count > 0) oculto = pila.Pop(); inicioGrupo = false; i++; continue; }
            if (c == '\r' || c == '\n') { i++; continue; }
            Pieza p = new Pieza();
            p.Desde = i;
            if (c != '\\')
            {
                p.Texto = c.ToString();
                i++;
            }
            else if (i + 1 < rtf.Length && char.IsLetter(rtf[i + 1]))
            {
                int j = i + 1;
                while (j < rtf.Length && char.IsLetter(rtf[j])) j++;
                string nombre = rtf.Substring(i + 1, j - i - 1);
                int k = j;
                if (k < rtf.Length && (rtf[k] == '-' || char.IsDigit(rtf[k]))) { k++; while (k < rtf.Length && char.IsDigit(rtf[k])) k++; }
                string num = rtf.Substring(j, k - j);
                if (k < rtf.Length && rtf[k] == ' ') k++;
                i = k;
                p.Comando = nombre;
                int n;
                bool hayNum = int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                if (nombre == "uc" && hayNum) uc = n;
                if (nombre == "u" && hayNum)
                {
                    p.Texto = ((char)(n < 0 ? n + 65536 : n)).ToString();
                    p.Comando = null;
                    // Se salta el caracter de respaldo que sigue a \uN.
                    for (int s = 0; s < uc && i < rtf.Length; s++)
                    {
                        if (rtf[i] == '\\' && i + 3 < rtf.Length && rtf[i + 1] == '\'') i += 4;
                        else if (rtf[i] == '{' || rtf[i] == '}' || rtf[i] == '\\') break;
                        else i++;
                    }
                }
                else if (nombre == "par" || nombre == "line") p.Texto = "\n";
                else if (nombre == "tab") p.Texto = "\t";
                if (inicioGrupo && Array.IndexOf(Destinos, nombre) >= 0) oculto = true;
            }
            else if (i + 1 < rtf.Length)
            {
                char s = rtf[i + 1];
                if (s == '\'' && i + 3 < rtf.Length)
                {
                    int b;
                    if (int.TryParse(rtf.Substring(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                        p.Texto = Ansi(b);
                    i += 4;
                }
                else
                {
                    if (s == '*' && inicioGrupo) oculto = true;
                    if (s == '\\' || s == '{' || s == '}') p.Texto = s.ToString();
                    else if (s == '~') p.Texto = "\u00a0";
                    else p.Comando = s.ToString();
                    i += 2;
                }
            }
            else i++;
            p.Hasta = i;
            inicioGrupo = false;
            if (!oculto) r.Add(p);
        }
        return r;
    }

    static string Ansi(int b)
    {
        if (b < 128) return ((char)b).ToString();
        try { return Encoding.GetEncoding(1252).GetString(new byte[] { (byte)b }); }
        catch { return ((char)b).ToString(); }
    }

    public static bool EsRtf(string s) { return s != null && s.TrimStart().StartsWith("{\\rtf"); }

    // Texto visible, con saltos de linea en \par.
    public static string TextoPlano(string rtf)
    {
        if (!EsRtf(rtf)) return rtf ?? "";
        StringBuilder sb = new StringBuilder();
        foreach (Pieza p in Piezas(rtf)) if (p.Texto != null) sb.Append(p.Texto);
        return sb.ToString().TrimEnd('\n', '\r', ' ');
    }

    // Cambia el texto visible conservando el formato de la plantilla (el del
    // primer caracter). Los saltos de linea del texto nuevo se vuelven \par.
    public static string ReemplazarTexto(string rtf, string nuevo)
    {
        if (!EsRtf(rtf)) return Simple(nuevo);
        List<Pieza> piezas = Piezas(rtf);
        int primera = -1, ultima = -1;
        for (int k = 0; k < piezas.Count; k++)
        {
            Pieza p = piezas[k];
            if (p.Texto == null || p.Texto == "\n") continue;
            if (primera < 0) primera = k;
            ultima = k;
        }
        if (primera < 0)
        {
            // Plantilla sin texto: se pone antes del ultimo \par o del cierre.
            int fin = rtf.LastIndexOf('}');
            for (int k = piezas.Count - 1; k >= 0; k--)
                if (piezas[k].Comando == "par") { fin = piezas[k].Desde; break; }
            if (fin < 0) return Simple(nuevo);
            return rtf.Substring(0, fin) + Escapar(nuevo) + rtf.Substring(fin);
        }
        int desde = piezas[primera].Desde, hasta = piezas[ultima].Hasta;
        string medio = Escapar(nuevo);
        return rtf.Substring(0, desde) + medio + rtf.Substring(hasta);
    }

    public static string Escapar(string texto)
    {
        StringBuilder sb = new StringBuilder();
        string t = (texto ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\par ");
            else if (c == '\t') sb.Append("\\tab ");
            else if (c < 128) sb.Append(c);
            else sb.Append("\\u").Append(((int)(short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
        }
        return sb.ToString();
    }

    // RTF basico centrado, por si la plantilla no trae texto enriquecido.
    public static string Simple(string texto)
    {
        return "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil Arial;}}\\uc1\\pard\\qc\\f0\\fs48 " +
               Escapar(texto) + "\\par\n}";
    }
}

// ---- src/musica/LogicaMusica.cs ----

// =====================================================================
// Musica que baja sola (ducking)
//
// Con los tramos de voz se arma la curva de volumen de la musica: baja un
// poco antes de que alguien hable ("anticipa"), se queda abajo mientras
// hablan y vuelve a subir despacio al terminar ("recupera"). Las pausas mas
// cortas que "pausa minima" no la suben, para que no suba y baje a cada rato.
// =====================================================================

public class PuntoVolumen
{
    public double T, Db;   // segundo de la linea de tiempo y ganancia en dB (0 = sin cambio)
    public PuntoVolumen(double t, double db) { T = t; Db = db; }
}

public class AjustesMusica
{
    public int BajaDb = -14, AnticipaMs = 250, RecuperaMs = 600, PausaMs = 1200;

    static string Ruta
    {
        get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"), "musica.ini"); }
    }

    public static AjustesMusica Cargar()
    {
        AjustesMusica a = new AjustesMusica();
        try
        {
            if (!File.Exists(Ruta)) return a;
            foreach (string l in File.ReadAllLines(Ruta))
            {
                int i = l.IndexOf('='), n;
                if (i < 0 || !int.TryParse(l.Substring(i + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                switch (l.Substring(0, i).Trim())
                {
                    case "baja": a.BajaDb = n; break;
                    case "anticipa": a.AnticipaMs = n; break;
                    case "recupera": a.RecuperaMs = n; break;
                    case "pausa": a.PausaMs = n; break;
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
            File.WriteAllText(Ruta, "baja=" + BajaDb + "\nanticipa=" + AnticipaMs + "\nrecupera=" + RecuperaMs +
                "\npausa=" + PausaMs + "\n", new UTF8Encoding(false));
        }
        catch { }
    }
}

public static class LogicaMusica
{
    // Valores de deteccion para encontrar la voz: aqui no se corta nada, asi
    // que no hay margenes ni pedazo minimo; la pausa minima decide cuando
    // la musica puede subir.
    public static Valores Deteccion(int pausaMs)
    {
        Valores v = new Valores();
        v.SilencioMinMs = pausaMs; v.HablaMinMs = 150;
        v.MargenAntesMs = 0; v.MargenDespuesMs = 0; v.PedazoMinMs = 0; v.Sensibilidad = 0;
        return v;
    }

    // Lo que no es silencio, dentro de inicio..fin.
    public static List<Rango> Voz(List<Rango> silencios, double inicio, double fin)
    {
        List<Rango> r = new List<Rango>();
        double cursor = inicio;
        foreach (Rango s in silencios)
        {
            if (s.Inicio > cursor + 0.001) r.Add(new Rango(cursor, Math.Min(s.Inicio, fin)));
            cursor = Math.Max(cursor, s.Fin);
        }
        if (fin > cursor + 0.001) r.Add(new Rango(cursor, fin));
        return r;
    }

    public static List<PuntoVolumen> Puntos(List<Rango> voz, double inicio, double fin, double bajaDb, double anticipa, double recupera)
    {
        // 1. Cada voz ocupa desde que empieza a bajar hasta que termina de subir.
        List<Rango> zonas = new List<Rango>();
        List<Rango> orden = new List<Rango>(voz);
        orden.Sort(delegate (Rango a, Rango b) { return a.Inicio.CompareTo(b.Inicio); });
        foreach (Rango v in orden)
        {
            Rango z = new Rango(v.Inicio - anticipa, v.Fin + recupera);
            if (zonas.Count > 0 && z.Inicio <= zonas[zonas.Count - 1].Fin)
            {
                Rango u = zonas[zonas.Count - 1];
                zonas[zonas.Count - 1] = new Rango(u.Inicio, Math.Max(u.Fin, z.Fin));
            }
            else zonas.Add(z);
        }

        // 2. Rampa de bajada, tramo abajo y rampa de subida.
        List<PuntoVolumen> todos = new List<PuntoVolumen>();
        foreach (Rango z in zonas)
        {
            todos.Add(new PuntoVolumen(z.Inicio, 0));
            todos.Add(new PuntoVolumen(z.Inicio + anticipa, bajaDb));
            if (z.Fin - recupera > z.Inicio + anticipa + 0.0005) todos.Add(new PuntoVolumen(z.Fin - recupera, bajaDb));
            todos.Add(new PuntoVolumen(z.Fin, 0));
        }

        // 3. Solo lo que cae en el rango, con un punto en cada borde.
        List<PuntoVolumen> r = new List<PuntoVolumen>();
        r.Add(new PuntoVolumen(inicio, Valor(todos, inicio)));
        foreach (PuntoVolumen p in todos)
            if (p.T > inicio + 0.0005 && p.T < fin - 0.0005) r.Add(p);
        r.Add(new PuntoVolumen(fin, Valor(todos, fin)));

        // 4. Sin puntos de sobra (tres seguidos con el mismo nivel).
        List<PuntoVolumen> limpio = new List<PuntoVolumen>();
        for (int i = 0; i < r.Count; i++)
        {
            bool sobra = i > 0 && i < r.Count - 1 &&
                         Math.Abs(r[i - 1].Db - r[i].Db) < 0.01 && Math.Abs(r[i + 1].Db - r[i].Db) < 0.01;
            if (!sobra) limpio.Add(r[i]);
        }
        return limpio;
    }

    // Nivel en el instante t (lineal entre puntos; 0 dB fuera de ellos).
    public static double Valor(List<PuntoVolumen> puntos, double t)
    {
        if (puntos.Count == 0 || t < puntos[0].T || t > puntos[puntos.Count - 1].T) return 0;
        if (puntos.Count == 1) return puntos[0].Db;
        for (int i = 1; i < puntos.Count; i++)
        {
            PuntoVolumen a = puntos[i - 1], b = puntos[i];
            if (t > b.T) continue;
            if (b.T - a.T < 1e-9) return b.Db;
            return a.Db + (b.Db - a.Db) * (t - a.T) / (b.T - a.T);
        }
        return 0;
    }

    public static int Bajadas(List<PuntoVolumen> puntos)
    {
        int n = 0;
        for (int i = 1; i < puntos.Count; i++) if (puntos[i].Db < puntos[i - 1].Db - 0.01 && puntos[i - 1].Db > -0.01) n++;
        if (puntos.Count > 0 && puntos[0].Db < -0.01) n++;
        return n;
    }

    // Ganancia lineal (1 = 0 dB), como la guarda la envolvente de volumen.
    public static double Ganancia(double db) { return db <= -90 ? 0 : Math.Pow(10, db / 20); }

    // Pone los puntos en la envolvente de volumen de la pista: quita los que
    // habia dentro del rango y agrega los nuevos.
    public static int Aplicar(Track pista, List<PuntoVolumen> puntos, double inicio, double fin)
    {
        Envelope env = pista.Envelopes.FindByType(EnvelopeType.Volume);
        if (env == null)
        {
            env = new Envelope(EnvelopeType.Volume);
            pista.Envelopes.Add(env);
        }
        List<EnvelopePoint> quitar = new List<EnvelopePoint>();
        foreach (EnvelopePoint p in env.Points)
        {
            double t = p.X.ToMilliseconds() / 1000.0;
            if (t >= inicio - 0.0005 && t <= fin + 0.0005) quitar.Add(p);
        }
        foreach (EnvelopePoint p in quitar)
            try { env.Points.Remove(p); } catch { } // el primer punto no se puede borrar

        int n = 0;
        foreach (PuntoVolumen p in puntos)
        {
            EnvelopePoint existente = null;
            foreach (EnvelopePoint e in env.Points)
                if (Math.Abs(e.X.ToMilliseconds() - p.T * 1000) < 0.5) { existente = e; break; }
            if (existente != null) existente.Y = Ganancia(p.Db);
            else env.Points.Add(new EnvelopePoint(Timecode.FromMilliseconds(p.T * 1000), Ganancia(p.Db)));
            n++;
        }
        return n;
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
        nombre = nombre ?? "";
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
