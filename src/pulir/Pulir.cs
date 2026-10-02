using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

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
    static readonly string[] Filas = { "Cortes", "Recursos", "Narrador", "Música" };
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
    Boton btnSerie = new Boton("Serie…", EstiloBoton.Secundario);
    Combo cmbNarrador = new Combo();
    GraficoRitmo grafico = new GraficoRitmo();
    Lista lstChequeos = new Lista();
    Lista lstValles = new Lista();
    Boton btnRegiones = new Boton("Marcar valles como regiones", EstiloBoton.Secundario);
    Boton btnQuitarRegiones = new Boton("Quitar regiones", EstiloBoton.Secundario);
    Boton btnMedir = new Boton("Volver a medir", EstiloBoton.Secundario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Primario);

    public VentanaPulir(Vegas vegas) : base("Pulir episodio", 1040)
    {
        this.vegas = vegas;
        int m = Margen, w = Ancho;
        Encabezado("Pulir episodio", "Mide el ritmo del episodio contra las reglas de su serie y marca dónde se puede ir la gente.");
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
        lstChequeos.Columns.Add("Qué", 140);
        lstChequeos.Columns.Add("Medido", ci - 26 - 140 - 104 - sb);
        lstChequeos.Columns.Add("Objetivo", 104);
        Pos(lstChequeos, m, y, ci, 200);
        lstValles.CheckBoxes = false;
        lstValles.Columns.Add("Tramo", 110);
        lstValles.Columns.Add("", 26);
        lstValles.Columns.Add("Qué pasa", w - ci - 16 - 110 - 26 - sb);
        Pos(lstValles, m + ci + 16, y, w - ci - 16, 200);
        y += 210;
        Pos(btnRegiones, m, y, 230, 34);
        Pos(btnQuitarRegiones, m + 238, y, 140, 34);
        Pos(btnMedir, m + 386, y, 140, 34);
        Pos(btnCerrar, m + w - 140, y, 140, 34);
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
            Estado(n == 0 ? "No hay valles que marcar." : "✔ " + n + " regiones «" + LogicaPulir.PrefijoRegion.Trim() + "» en la línea de tiempo (Ctrl+Z las quita).", false);
        };
        btnQuitarRegiones.Click += delegate
        {
            int n;
            using (UndoBlock u = new UndoBlock("Quitar valles")) n = LogicaPulir.QuitarRegiones(vegas.Project);
            Estado(n == 0 ? "No había regiones de valles." : "Se quitaron " + n + " regiones de valles.", false);
        };
        btnMedir.Click += delegate { CargarTranscripcion(); Medir(); };
        btnCerrar.Click += delegate { Close(); };

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
            ? "Sin serie: uso reglas generales. Con «Serie…» eliges la serie y su formato."
            : "Serie " + serie.Nombre + " · " + formato.Nombre + " · " + Path.GetFileNameWithoutExtension(vegas.Project.FilePath) +
              " es «" + papel + "»" + (serie.IndiceDe(vegas.Project.FilePath) < 0 ? " (no está en la serie)" : "");
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
            ListViewItem it = new ListViewItem(c.Ok ? "✔" : "✖");
            it.SubItems.Add(c.Que); it.SubItems.Add(c.Medido); it.SubItems.Add(c.Objetivo);
            it.ForeColor = c.Ok ? Tema.Texto : Tema.Silencio;
            lstChequeos.Items.Add(it);
        }
        lstValles.Items.Clear();
        foreach (Valle v in inf.Valles)
        {
            ListViewItem it = new ListViewItem(Formato.Tiempo(v.Inicio) + "–" + Formato.Tiempo(v.Fin));
            it.SubItems.Add(v.Critico ? "!" : "");
            it.SubItems.Add(v.Texto);
            if (v.Critico) it.ForeColor = Tema.Silencio;
            lstValles.Items.Add(it);
        }
        string aviso = trans == null ? "Sin transcripción: no puedo ver al narrador (ejecuta Transcribir). " : "";
        if (inf.FaltaNarracion) aviso += "La serie lleva narrador pero aún no hay narración: los huecos de narrador no se cuentan como valles todavía. ";
        Estado(aviso + (inf.Valles.Count == 0 ? "Sin valles: el ritmo cumple las reglas." :
                        inf.Valles.Count + " valles. Clic en el gráfico o doble clic en un valle para ir ahí."), false);
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
