using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

public delegate Analisis FuncionAnalizar(int pista, bool usarSeleccion);

class Carril
{
    public string Etiqueta;
    public Analisis Datos;
    public Color Color;
    public double Umbral; // umbral efectivo (ya con la sensibilidad)
}

// Forma de onda por pista, con los silencios marcados y un umbral
// arrastrable en cada carril.
class VistaOnda : ControlBase
{
    const double MinDb = -80, MaxDb = 0;
    const int AnchoEtiqueta = 52;
    List<Carril> carriles = new List<Carril>();
    List<Rango> rangos = new List<Rango>();
    double inicio, duracion;
    int arrastrando = -1;
    int ratonX = -1;
    public string Mensaje = "Elige las pistas y pulsa Analizar.";

    // Carril arrastrado y su nuevo umbral efectivo.
    public int CarrilArrastrado;
    public double UmbralArrastrado;
    public event EventHandler UmbralCambiado;

    public static readonly Color[] Colores =
    {
        Color.FromArgb(120, 200, 255), Color.FromArgb(120, 225, 160),
        Color.FromArgb(200, 160, 255), Color.FromArgb(255, 210, 110),
        Color.FromArgb(255, 150, 190), Color.FromArgb(140, 230, 230),
    };

    public void Mostrar(List<Carril> c, List<Rango> r)
    {
        carriles = c ?? new List<Carril>();
        rangos = r;
        if (carriles.Count > 0)
        {
            inicio = carriles[0].Datos.Inicio;
            duracion = carriles[0].Datos.Duracion;
            foreach (Carril k in carriles) duracion = Math.Min(duracion, k.Datos.Duracion);
        }
        Invalidate();
    }

    Rectangle Area { get { return new Rectangle(14 + AnchoEtiqueta, 14, Width - 28 - AnchoEtiqueta, Height - 42); } }

    Rectangle AreaCarril(int i)
    {
        Rectangle a = Area;
        int alto = a.Height / Math.Max(1, carriles.Count);
        return new Rectangle(a.X, a.Y + i * alto, a.Width, alto);
    }

    float MitadAltura(Rectangle c, double db)
    {
        return (float)((Math.Max(MinDb, Math.Min(MaxDb, db)) - MinDb) / (MaxDb - MinDb)) * (c.Height - 6) / 2f;
    }

    int CarrilEn(int y)
    {
        if (carriles.Count == 0) return -1;
        return Math.Max(0, Math.Min(carriles.Count - 1, (y - Area.Y) / Math.Max(1, AreaCarril(0).Height)));
    }

    bool CercaUmbral(int y)
    {
        int i = CarrilEn(y);
        if (i < 0) return false;
        Rectangle c = AreaCarril(i);
        float centro = c.Y + c.Height / 2f, h = MitadAltura(c, carriles[i].Umbral);
        return Math.Abs(y - (centro - h)) < 6 || Math.Abs(y - (centro + h)) < 6;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (CercaUmbral(e.Y)) arrastrando = CarrilEn(e.Y);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ratonX = e.X;
        if (arrastrando >= 0)
        {
            Rectangle c = AreaCarril(arrastrando);
            float centro = c.Y + c.Height / 2f;
            double t = Math.Abs(e.Y - centro) / ((c.Height - 6) / 2.0);
            CarrilArrastrado = arrastrando;
            UmbralArrastrado = Math.Round(Math.Max(-75, Math.Min(-5, MinDb + t * (MaxDb - MinDb))));
            if (UmbralCambiado != null) UmbralCambiado(this, EventArgs.Empty);
        }
        Cursor = arrastrando >= 0 || CercaUmbral(e.Y) ? Cursors.SizeNS : Cursors.Default;
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { arrastrando = -1; base.OnMouseUp(e); }
    protected override void OnMouseLeave(EventArgs e) { ratonX = -1; base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
        {
            using (SolidBrush b = new SolidBrush(Tema.Panel)) g.FillPath(b, p);
            using (Pen pen = new Pen(Tema.Borde)) g.DrawPath(pen, p);
        }

        if (carriles.Count == 0 || duracion <= 0)
        {
            TextRenderer.DrawText(g, Mensaje, Tema.Normal, ClientRectangle, Tema.TextoSuave,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        Rectangle a = Area;
        g.SmoothingMode = SmoothingMode.None;

        // Silencios detectados: atraviesan todos los carriles.
        using (SolidBrush b = new SolidBrush(Color.FromArgb(50, Tema.Silencio)))
        using (SolidBrush linea = new SolidBrush(Tema.Silencio))
        {
            foreach (Rango r in rangos)
            {
                float x0 = a.X + (float)((r.Inicio - inicio) / duracion * a.Width);
                float x1 = a.X + (float)((r.Fin - inicio) / duracion * a.Width);
                float w = Math.Max(1, x1 - x0);
                g.FillRectangle(b, x0, a.Y, w, a.Height);
                g.FillRectangle(linea, x0, a.Bottom + 3, w, 3);
            }
        }

        for (int k = 0; k < carriles.Count; k++)
        {
            Carril carril = carriles[k];
            Rectangle c = AreaCarril(k);
            float centro = c.Y + c.Height / 2f;
            float[] db = carril.Datos.Db;
            int n = (int)Math.Min(db.Length, Math.Round(duracion / Analisis.Paso));

            if (k > 0)
                using (Pen sep = new Pen(Tema.Borde)) g.DrawLine(sep, a.X - AnchoEtiqueta, c.Y, a.Right, c.Y);

            // Nombre de la pista y su umbral
            TextRenderer.DrawText(g, carril.Etiqueta, Tema.Negrita,
                new Rectangle(14, (int)centro - 17, AnchoEtiqueta - 4, 18), carril.Color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, carril.Umbral.ToString("0") + " dB", Tema.Pequena,
                new Rectangle(14, (int)centro + 1, AnchoEtiqueta - 4, 16), Tema.Acento,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Onda: el nivel maximo de cada columna de pixeles, espejado.
            using (Pen voz = new Pen(carril.Color))
            using (Pen bajo = new Pen(Color.FromArgb(80, 86, 100)))
            {
                for (int x = 0; x < a.Width; x++)
                {
                    int i0 = (int)((long)x * n / a.Width);
                    int i1 = Math.Max(i0 + 1, (int)((long)(x + 1) * n / a.Width));
                    float m = -100;
                    for (int i = i0; i < i1 && i < n; i++) if (db[i] > m) m = db[i];
                    float h = MitadAltura(c, m);
                    if (h < 0.5f) continue;
                    g.DrawLine(m >= carril.Umbral ? voz : bajo, a.X + x, centro - h, a.X + x, centro + h);
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float hu = MitadAltura(c, carril.Umbral);
            using (Pen pen = new Pen(Color.FromArgb(arrastrando == k ? 255 : 200, Tema.Acento), arrastrando == k ? 2f : 1.2f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, a.X, centro - hu, a.Right, centro - hu);
                g.DrawLine(pen, a.X, centro + hu, a.Right, centro + hu);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        // Tiempos
        TextRenderer.DrawText(g, Formato.Tiempo(inicio), Tema.Pequena, new Point(a.X, a.Bottom + 8), Tema.TextoSuave);
        string fin = Formato.Tiempo(inicio + duracion);
        Size fs = TextRenderer.MeasureText(fin, Tema.Pequena);
        TextRenderer.DrawText(g, fin, Tema.Pequena, new Point(a.Right - fs.Width, a.Bottom + 8), Tema.TextoSuave);
        TextRenderer.DrawText(g, "Arrastra la l\u00ednea punteada de cada pista para ajustar su umbral.", Tema.Pequena,
            new Rectangle(a.X, a.Bottom + 6, a.Width, 18), Tema.TextoSuave, TextFormatFlags.HorizontalCenter);

        // Cursor del raton: tiempo y nivel de cada pista
        if (ratonX >= a.X && ratonX < a.Right && arrastrando < 0)
        {
            double t = (ratonX - a.X) / (double)a.Width * duracion;
            using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawLine(pen, ratonX, a.Y, ratonX, a.Bottom);
            string info = Formato.Tiempo(inicio + t);
            foreach (Carril k in carriles)
            {
                int i = Math.Min(k.Datos.Db.Length - 1, (int)(t / Analisis.Paso));
                info += "  \u00b7  " + k.Etiqueta + " " + k.Datos.Db[i].ToString("0") + " dB";
            }
            Size s = TextRenderer.MeasureText(info, Tema.Pequena);
            int xi = Math.Max(a.X, Math.Min(ratonX + 6, a.Right - s.Width - 6));
            TextRenderer.DrawText(g, info, Tema.Pequena, new Point(xi, a.Y + 2), Tema.Texto);
        }
    }
}


public class VentanaSilencios : Form
{
    readonly FuncionAnalizar analizar;
    readonly string[] nombres;
    // Analisis ya hechos por pista (indice en la lista de pistas de audio) y su
    // umbral base: el automatico o el que el usuario arrastro.
    Dictionary<int, Analisis> cache = new Dictionary<int, Analisis>();
    Dictionary<int, double> umbralBase = new Dictionary<int, double>();
    List<Perfil_> perfiles = new List<Perfil_>();
    bool cargando;

    public Ajustes Ajustes;
    public List<Rango> Rangos = new List<Rango>();

    List<Boton> chipsPista = new List<Boton>();
    Segmentado segRango = new Segmentado(new string[] { "Todo el proyecto", "Selecci\u00f3n de tiempo" });
    Boton btnAnalizar = new Boton("Analizar", EstiloBoton.Secundario);
    VistaOnda onda = new VistaOnda();
    Combo comboPerfil = new Combo();
    Boton btnGuardarPerfil = new Boton("Guardar como\u2026", EstiloBoton.Secundario);
    Boton btnBorrarPerfil = new Boton("Borrar", EstiloBoton.Secundario);
    Etiqueta lblPerfil = new Etiqueta("", Tema.Pequena, Tema.TextoSuave);
    Deslizador deslizador = new Deslizador();
    Etiqueta lblSensibilidad = new Etiqueta("", Tema.Negrita, Tema.Texto);
    Boton btnAuto = new Boton("Auto", EstiloBoton.Secundario);
    CampoNumero numSilencio = new CampoNumero(), numHabla = new CampoNumero();
    CampoNumero numAntes = new CampoNumero(), numDespues = new CampoNumero();
    CampoNumero numPedazo = new CampoNumero(), numSuavizado = new CampoNumero();
    Segmentado segModo = new Segmentado(new string[] { "Eliminar", "Dejar huecos", "Silenciar", "Solo marcar" });
    Segmentado segPistas = new Segmentado(new string[] { "Todas las pistas", "Solo las analizadas" });
    Etiqueta lblResumen = new Etiqueta("", Tema.Normal, Tema.TextoSuave);
    Etiqueta lblResumenGrande = new Etiqueta("", Tema.Fuente(12f, FontStyle.Bold), Tema.Texto);
    Boton btnAplicar = new Boton("Quitar silencios", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);
    ToolTip ayuda = new ToolTip();

    public List<int> PistasElegidas
    {
        get
        {
            List<int> r = new List<int>();
            for (int i = 0; i < chipsPista.Count; i++) if (chipsPista[i].Activo) r.Add(i);
            return r;
        }
    }

    public VentanaSilencios(string[] nombres, string[] detalles, int sugerida, bool haySeleccion, FuncionAnalizar analizar)
    {
        this.analizar = analizar;
        this.nombres = nombres;
        Ajustes = Ajustes.Cargar();

        Text = "Quitar silencios \u00b7 vegas-cut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        DoubleBuffered = true;
        KeyPreview = true;

        const int anchoVentana = 860;
        int m = 24, ancho = anchoVentana - m * 2;

        // Encabezado
        Controls.Add(Pos(new Etiqueta("Quitar silencios", Tema.Titulo, Tema.Texto), m, 18, 400, 32));
        Controls.Add(Pos(new Etiqueta("Detecta las pausas de la voz y las quita de la l\u00ednea de tiempo.",
            Tema.Normal, Tema.TextoSuave), m, 50, 600, 20));

        // Pistas que se escuchan
        int y = 90;
        Controls.Add(Pos(new Etiqueta("PISTAS DE VOZ", Tema.Pequena, Tema.TextoSuave), m, y, 96, 18));
        Controls.Add(Pos(new Etiqueta("Hay voz si suena cualquiera de las marcadas. Las dem\u00e1s (juego, m\u00fasica) no cuentan.",
            Tema.Pequena, Tema.TextoSuave), m + 100, y, ancho - 100, 18));
        y += 22;
        int cx = m;
        for (int i = 0; i < nombres.Length; i++)
        {
            Boton c = new Boton(nombres[i], EstiloBoton.Chip);
            c.Activo = i == sugerida;
            int w = Math.Min(ancho, TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 26);
            if (cx + w > m + ancho) { cx = m; y += 34; }
            Controls.Add(Pos(c, cx, y, w, 28));
            ayuda.SetToolTip(c, detalles[i]);
            c.Click += delegate
            {
                // Siempre queda al menos una pista marcada.
                if (c.Activo && PistasElegidas.Count == 1) return;
                c.Activo = !c.Activo;
                Recalcular();
            };
            chipsPista.Add(c);
            cx += w + 6;
        }
        y += 28 + 14;

        // Rango + analizar
        segRango.Seleccion = haySeleccion ? 1 : 0;
        segRango.Habilitar(1, haySeleccion);
        Controls.Add(Pos(segRango, m, y, 300, 34));
        Controls.Add(Pos(btnAnalizar, m + ancho - 170, y, 170, 34));
        y += 34 + 14;

        // Onda: un carril por pista
        Controls.Add(Pos(onda, m, y, ancho, 210));
        y += 210 + 20;

        // Perfil
        Controls.Add(Pos(new Etiqueta("Perfil", Tema.Seccion, Tema.Texto), m, y, 200, 22));
        Controls.Add(Pos(comboPerfil, m, y + 28, 230, 30));
        Controls.Add(Pos(btnGuardarPerfil, m + 238, y + 26, 130, 32));
        Controls.Add(Pos(btnBorrarPerfil, m + 374, y + 26, 76, 32));
        Controls.Add(Pos(lblPerfil, m, y + 62, 450, 32));
        lblPerfil.TextAlign = ContentAlignment.TopLeft;

        // Sensibilidad general
        int sx = m + 490;
        Controls.Add(Pos(new Etiqueta("Sensibilidad", Tema.Seccion, Tema.Texto), sx, y, 200, 22));
        Controls.Add(Pos(new Etiqueta("Sube todos los umbrales para cortar m\u00e1s; b\u00e1jalos para cortar menos.",
            Tema.Pequena, Tema.TextoSuave), sx, y + 62, ancho - 490, 32));
        deslizador.Minimo = -10; deslizador.Maximo = 10; deslizador.DesdeCentro = true;
        Controls.Add(Pos(deslizador, sx - 4, y + 28, 190, 30));
        Controls.Add(Pos(lblSensibilidad, sx + 190, y + 28, 56, 30));
        Controls.Add(Pos(btnAuto, sx + 250, y + 27, ancho - 490 - 250, 32));
        ayuda.SetToolTip(btnAuto, "Recalcula el umbral de cada pista seg\u00fan su ruido de fondo y su voz.");
        y += 104;

        // Tiempos
        int col = (ancho - 5 * 10) / 6;
        CampoTiempo(numSilencio, "Silencio m\u00ednimo", "Solo quita pausas m\u00e1s largas", m, y, col);
        CampoTiempo(numHabla, "Voz m\u00ednima", "Ignora ruidos m\u00e1s cortos", m + (col + 10), y, col);
        CampoTiempo(numAntes, "Margen antes", "Pausa antes de hablar", m + (col + 10) * 2, y, col);
        CampoTiempo(numDespues, "Margen despu\u00e9s", "Pausa al terminar", m + (col + 10) * 3, y, col);
        CampoTiempo(numPedazo, "Clip m\u00ednimo", "No deja clips m\u00e1s cortos", m + (col + 10) * 4, y, col);
        CampoTiempo(numSuavizado, "Suavizado", "Fundido del audio en cada corte", m + (col + 10) * 5, y, col);
        y += 100;

        // Separador + accion
        Panel sep = new Panel();
        sep.BackColor = Tema.Borde;
        Controls.Add(Pos(sep, m, y, ancho, 1));

        Controls.Add(Pos(new Etiqueta("QU\u00c9 HACER", Tema.Pequena, Tema.TextoSuave), m, y + 16, 200, 18));
        Controls.Add(Pos(segModo, m, y + 36, 440, 34));
        Controls.Add(Pos(new Etiqueta("D\u00d3NDE CORTAR", Tema.Pequena, Tema.TextoSuave), m + 456, y + 16, 200, 18));
        Controls.Add(Pos(segPistas, m + 456, y + 36, ancho - 456, 34));
        ayuda.SetToolTip(segPistas, "Todas: corta tambi\u00e9n video, juego y m\u00fasica para que todo siga sincronizado.");
        ayuda.SetToolTip(segModo, "Eliminar junta todo; Dejar huecos quita sin mover; Silenciar deja mudo; Solo marcar crea regiones.");

        Controls.Add(Pos(lblResumenGrande, m, y + 90, 400, 24));
        Controls.Add(Pos(lblResumen, m, y + 114, 460, 20));
        Controls.Add(Pos(btnCancelar, m + ancho - 300, y + 90, 110, 40));
        Controls.Add(Pos(btnAplicar, m + ancho - 180, y + 90, 180, 40));
        ClientSize = new Size(anchoVentana, y + 90 + 40 + 24);

        // Valores iniciales
        numSilencio.Maximo = 10000; numPedazo.Maximo = 10000;
        numAntes.Maximo = 2000; numDespues.Maximo = 2000;
        numSuavizado.Maximo = 200; numSuavizado.Paso = 5;
        cargando = true;
        MostrarValores(Ajustes);
        segModo.Seleccion = (int)Ajustes.Modo;
        segPistas.Seleccion = Ajustes.TodasLasPistas ? 0 : 1;
        cargando = false;
        LlenarPerfiles(Ajustes.Perfil);

        // Eventos
        btnAnalizar.Click += delegate { Analizar(); };
        deslizador.Cambio += delegate { if (!cargando) { ActualizarPerfil(); Recalcular(); } };
        onda.UmbralCambiado += delegate
        {
            int pista = PistasElegidas[onda.CarrilArrastrado];
            umbralBase[pista] = onda.UmbralArrastrado - deslizador.Valor;
            Recalcular();
        };
        btnAuto.Click += delegate { UmbralesAutomaticos(true); Recalcular(); };
        EventHandler recalc = delegate { if (!cargando) { ActualizarPerfil(); Recalcular(); } };
        numSilencio.Cambio += recalc; numHabla.Cambio += recalc; numAntes.Cambio += recalc;
        numDespues.Cambio += recalc; numPedazo.Cambio += recalc; numSuavizado.Cambio += recalc;
        comboPerfil.SelectedIndexChanged += delegate { if (!cargando) ElegirPerfil(); };
        btnGuardarPerfil.Click += delegate { GuardarPerfil(); };
        btnBorrarPerfil.Click += delegate { BorrarPerfil(); };
        segModo.Cambio += delegate { ActualizarTextoBoton(); };
        segPistas.Cambio += delegate { Recalcular(); };
        segRango.Cambio += delegate { cache.Clear(); umbralBase.Clear(); Recalcular(); };
        btnAplicar.Click += delegate { Aplicar(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        ActualizarTextoBoton();
        Recalcular();
    }

    static Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); return c; }

    void CampoTiempo(CampoNumero campo, string titulo, string texto, int x, int y, int w)
    {
        Controls.Add(Pos(new Etiqueta(titulo, Tema.Negrita, Tema.Texto), x, y, w, 20));
        Etiqueta e = new Etiqueta(texto, Tema.Pequena, Tema.TextoSuave);
        e.TextAlign = ContentAlignment.TopLeft;
        Controls.Add(Pos(e, x, y + 20, w, 30));
        Controls.Add(Pos(campo, x, y + 52, w, 36));
    }

    // ------------------------------------------------------------ perfiles

    void MostrarValores(Valores v)
    {
        bool antes = cargando;
        cargando = true;
        numSilencio.Valor = v.SilencioMinMs; numHabla.Valor = v.HablaMinMs;
        numAntes.Valor = v.MargenAntesMs; numDespues.Valor = v.MargenDespuesMs;
        numPedazo.Valor = v.PedazoMinMs; numSuavizado.Valor = v.SuavizadoMs;
        deslizador.Valor = v.Sensibilidad;
        lblSensibilidad.Text = (v.Sensibilidad > 0 ? "+" : "") + v.Sensibilidad + " dB";
        cargando = antes;
    }

    void LlenarPerfiles(string elegido)
    {
        cargando = true;
        perfiles.Clear();
        perfiles.AddRange(Perfil_.Incluidos);
        perfiles.AddRange(Perfil_.CargarPropios());
        comboPerfil.Items.Clear();
        int indice = -1;
        for (int i = 0; i < perfiles.Count; i++)
        {
            comboPerfil.Items.Add(perfiles[i].Nombre + (perfiles[i].Incluido ? "" : "  \u2605"));
            if (perfiles[i].Nombre == elegido) indice = i;
        }
        comboPerfil.SelectedIndex = indice >= 0 ? indice : 0;
        cargando = false;
        ActualizarPerfil();
    }

    Perfil_ PerfilElegido { get { return comboPerfil.SelectedIndex >= 0 ? perfiles[comboPerfil.SelectedIndex] : null; } }

    void ElegirPerfil()
    {
        if (PerfilElegido == null) return;
        MostrarValores(PerfilElegido);
        ActualizarPerfil();
        Recalcular();
    }

    // Muestra la descripcion del perfil, o avisa si los valores ya no coinciden.
    void ActualizarPerfil()
    {
        LeerAjustes();
        Perfil_ p = PerfilElegido;
        if (p == null) return;
        if (p.Igual(Ajustes))
        {
            lblPerfil.Text = p.Descripcion;
            lblPerfil.ForeColor = Tema.TextoSuave;
        }
        else
        {
            lblPerfil.Text = "Modificado. Usa \u201cGuardar como\u2026\u201d para crear un perfil con estos valores.";
            lblPerfil.ForeColor = Tema.AcentoHover;
        }
        btnBorrarPerfil.Enabled = !p.Incluido;
    }

    void GuardarPerfil()
    {
        Perfil_ actual = PerfilElegido;
        string sugerido = actual != null && !actual.Incluido ? actual.Nombre : "";
        using (DialogoNombre d = new DialogoNombre(sugerido))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string nombre = d.Nombre.Replace("[", "(").Replace("]", ")");
            foreach (Perfil_ inc in Perfil_.Incluidos)
                if (String.Equals(inc.Nombre, nombre, StringComparison.OrdinalIgnoreCase)) nombre += " (m\u00edo)";

            List<Perfil_> propios = Perfil_.CargarPropios();
            Perfil_ p = null;
            foreach (Perfil_ x in propios)
                if (String.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase)) p = x;
            if (p == null) { p = new Perfil_(); p.Nombre = nombre; propios.Add(p); }
            LeerAjustes();
            p.CopiarDe(Ajustes);
            Perfil_.GuardarPropios(propios);
            LlenarPerfiles(nombre);
        }
    }

    void BorrarPerfil()
    {
        Perfil_ p = PerfilElegido;
        if (p == null || p.Incluido) return;
        if (MessageBox.Show(this, "\u00bfBorrar el perfil \u201c" + p.Nombre + "\u201d?", "Borrar perfil",
                MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        List<Perfil_> propios = Perfil_.CargarPropios();
        propios.RemoveAll(delegate (Perfil_ x) { return x.Nombre == p.Nombre; });
        Perfil_.GuardarPropios(propios);
        LlenarPerfiles(Perfil_.Incluidos[0].Nombre);
        ElegirPerfil();
    }

    // ------------------------------------------------------------- analisis

    void ActualizarTextoBoton()
    {
        string[] textos = { "Quitar silencios", "Quitar sin mover", "Silenciar", "Marcar silencios" };
        btnAplicar.Text = textos[segModo.Seleccion];
        segPistas.Enabled = segModo.Seleccion != (int)Modo.Marcar;
    }

    void LeerAjustes()
    {
        Ajustes.SilencioMinMs = numSilencio.Valor;
        Ajustes.HablaMinMs = numHabla.Valor;
        Ajustes.MargenAntesMs = numAntes.Valor;
        Ajustes.MargenDespuesMs = numDespues.Valor;
        Ajustes.PedazoMinMs = numPedazo.Valor;
        Ajustes.SuavizadoMs = numSuavizado.Valor;
        Ajustes.Sensibilidad = (int)deslizador.Valor;
        Ajustes.Modo = (Modo)segModo.Seleccion;
        Ajustes.TodasLasPistas = segPistas.Seleccion == 0;
        if (PerfilElegido != null) Ajustes.Perfil = PerfilElegido.Nombre;
    }

    List<int> Faltantes()
    {
        List<int> f = new List<int>();
        foreach (int i in PistasElegidas) if (!cache.ContainsKey(i)) f.Add(i);
        return f;
    }

    void UmbralesAutomaticos(bool todas)
    {
        foreach (KeyValuePair<int, Analisis> kv in cache)
            if (todas || !umbralBase.ContainsKey(kv.Key))
                umbralBase[kv.Key] = Detector.UmbralAutomatico(kv.Value.Db);
    }

    void Analizar()
    {
        // Si ya estaba todo leido, el boton vuelve a leer las pistas marcadas.
        List<int> leer = Faltantes();
        if (leer.Count == 0) { foreach (int i in PistasElegidas) { cache.Remove(i); umbralBase.Remove(i); } leer = PistasElegidas; }

        btnAnalizar.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            for (int k = 0; k < leer.Count; k++)
            {
                btnAnalizar.Text = "Leyendo " + (k + 1) + " de " + leer.Count + "\u2026";
                onda.Mensaje = "Leyendo el audio de " + nombres[leer[k]] + "\u2026";
                onda.Mostrar(null, new List<Rango>());
                Application.DoEvents();
                cache[leer[k]] = analizar(leer[k], segRango.Seleccion == 1);
            }
            UmbralesAutomaticos(false);
        }
        catch (Exception ex)
        {
            onda.Mensaje = "No se pudo analizar: " + ex.Message;
            onda.Mostrar(null, new List<Rango>());
        }
        finally
        {
            Cursor = Cursors.Default;
            btnAnalizar.Enabled = true;
        }
        Recalcular();
    }

    void Recalcular()
    {
        LeerAjustes();
        lblSensibilidad.Text = (Ajustes.Sensibilidad > 0 ? "+" : "") + Ajustes.Sensibilidad + " dB";
        List<int> faltan = Faltantes();
        btnAnalizar.Text = faltan.Count == 0 ? "Reanalizar" :
            cache.Count == 0 ? "Analizar" : "Analizar " + faltan.Count + (faltan.Count == 1 ? " pista" : " pistas");
        btnAuto.Enabled = cache.Count > 0;

        if (faltan.Count > 0)
        {
            Rangos = new List<Rango>();
            if (cache.Count > 0) onda.Mensaje = "Pulsa Analizar para leer las pistas nuevas.";
            onda.Mostrar(null, Rangos);
            lblResumenGrande.Text = "Sin analizar";
            lblResumen.Text = "Analiza para ver los silencios.";
            btnAplicar.Enabled = false;
            return;
        }

        List<int> elegidas = PistasElegidas;
        List<Analisis> datos = new List<Analisis>();
        List<double> umbrales = new List<double>();
        List<Carril> carriles = new List<Carril>();
        for (int k = 0; k < elegidas.Count; k++)
        {
            int p = elegidas[k];
            datos.Add(cache[p]);
            umbrales.Add(umbralBase[p]);

            Carril c = new Carril();
            string n = nombres[p];
            int espacio = n.IndexOf(' ');
            c.Etiqueta = espacio > 0 ? n.Substring(0, espacio) : n;
            c.Datos = cache[p];
            c.Color = VistaOnda.Colores[k % VistaOnda.Colores.Length];
            c.Umbral = umbralBase[p] + Ajustes.Sensibilidad;
            carriles.Add(c);
        }
        Rangos = Detector.Detectar(datos, umbrales, Ajustes);
        onda.Mostrar(carriles, Rangos);

        double quitado = 0;
        foreach (Rango r in Rangos) quitado += r.Fin - r.Inicio;
        double dur = cache[elegidas[0]].Duracion;
        double pct = dur > 0 ? quitado / dur * 100 : 0;
        lblResumenGrande.Text = Rangos.Count + (Rangos.Count == 1 ? " silencio" : " silencios") +
                                " \u00b7 " + Formato.Tiempo(quitado);
        lblResumen.Text = Formato.Tiempo(dur) + " \u2192 " + Formato.Tiempo(dur - quitado) +
                          "  (\u2212" + pct.ToString("0") + " %)  \u00b7  Ctrl+Z lo deshace";
        btnAplicar.Enabled = Rangos.Count > 0;
    }

    void Aplicar()
    {
        LeerAjustes();
        Ajustes.Guardar();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Linea de acento bajo el encabezado
        using (SolidBrush b = new SolidBrush(Tema.Acento)) e.Graphics.FillRectangle(b, 24, 76, 36, 3);
    }
}
