using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// =====================================================================
// Memes: programa aparte (fuera de Vegas) para la biblioteca de memes.
//  - Clasificar: una carpeta de videos e imagenes, tarjeta por tarjeta como
//    Tinder: a la derecha es meme (tags, que es, cuando usarlo y se mueve a
//    la biblioteca), a la izquierda no (no se vuelve a mostrar), arriba saltar.
//  - Biblioteca: buscar por texto o tag, ver, corregir, describir con IA y
//    quitar memes.
// Usa la configuracion de vegas-cut (la clave de Gemini y la carpeta de memes).
// =====================================================================

static class Programa
{
    [STAThread]
    static void Main()
    {
        Application.EnableVisualStyles();
        // El navegador integrado en modo IE11 (si no, no reproduce video).
        try
        {
            string exe = Path.GetFileName(Application.ExecutablePath);
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION"))
                if (k != null) k.SetValue(exe, 11001, RegistryValueKind.DWord);
        }
        catch { }
        Application.Run(new VentanaMemesApp());
    }
}

// Vista previa: imagenes en un cuadro; gifs, videos y sonidos en el navegador (en bucle).
class Visor : Panel
{
    WebBrowser nav;
    PictureBox img = new PictureBox();
    readonly bool interactivo;

    public Visor(bool interactivo)
    {
        this.interactivo = interactivo;
        BackColor = Color.Black;
        img.SizeMode = PictureBoxSizeMode.Zoom;
        img.BackColor = Color.Black;
        img.Dock = DockStyle.Fill;
        try
        {
            nav = new WebBrowser();
            nav.Dock = DockStyle.Fill;
            nav.ScrollBarsEnabled = false;
            nav.ScriptErrorsSuppressed = true;
            nav.IsWebBrowserContextMenuEnabled = false;
            // Deshabilitado deja pasar el raton a la tarjeta (para arrastrarla) y el video sigue sonando.
            if (!interactivo) nav.Enabled = false;
            Controls.Add(nav);
        }
        catch { nav = null; }
        Controls.Add(img);
    }

    public void Detener()
    {
        try { if (nav != null) nav.Navigate("about:blank"); } catch { }
        if (img.Image != null) { Image i = img.Image; img.Image = null; i.Dispose(); }
    }

    public void Mostrar(string f)
    {
        Detener();
        if (f == null || !File.Exists(f)) { img.Visible = true; if (nav != null) nav.Visible = false; return; }
        string tipo = BibliotecaMemes.TipoDe(f);
        if (tipo == "imagen" || nav == null)
        {
            img.Visible = true;
            if (nav != null) nav.Visible = false;
            try { using (Image im = Image.FromFile(f)) img.Image = new Bitmap(im); } catch { img.Image = null; }
            return;
        }
        img.Visible = false;
        nav.Visible = true;
        string url = new Uri(f).AbsoluteUri;
        string ctl = interactivo ? " controls" : "";
        string cuerpo = tipo == "gif" ? "<img src=\"" + url + "\" style=\"max-width:100%;max-height:100%\">" :
                        tipo == "sonido" ? "<div style=\"color:#aaa;font:24px Segoe UI;margin-top:30%\">♪ " + Path.GetFileNameWithoutExtension(f) + "</div><audio src=\"" + url + "\" autoplay loop" + ctl + "></audio>" :
                        "<video src=\"" + url + "\" autoplay loop" + ctl + " style=\"width:100%;height:100%\"></video>";
        string html = "<!DOCTYPE html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"utf-8\"></head>" +
                      "<body style=\"margin:0;background:#000;overflow:hidden\"><div style=\"width:100%;height:100%;text-align:center\">" + cuerpo + "</div></body></html>";
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), "vegas-cut-vista-" + (interactivo ? "b" : "c") + ".html");
            File.WriteAllText(tmp, html, new UTF8Encoding(false));
            nav.Navigate(tmp);
        }
        catch { }
    }
}

// Los tags como botones: clic para elegir; los elegidos van marcados.
class PanelTags : FlowLayoutPanel
{
    public List<string> Elegidos = new List<string>();

    public PanelTags() { AutoScroll = true; BackColor = Tema.Panel; Padding = new Padding(4); }

    public void Llenar(List<string> todos)
    {
        SuspendLayout();
        Controls.Clear();
        List<string> l = new List<string>(todos);
        foreach (string t in Elegidos) if (!l.Contains(t)) l.Insert(0, t);
        foreach (string t in l)
        {
            Boton b = new Boton(t, EstiloBoton.Chip);
            b.Font = Tema.Pequena;
            b.Size = new Size(TextRenderer.MeasureText(t, Tema.Pequena).Width + 26, 26);
            b.Margin = new Padding(3);
            b.Activo = Elegidos.Contains(t);
            string tag = t;
            b.Click += delegate
            {
                if (Elegidos.Contains(tag)) Elegidos.Remove(tag); else Elegidos.Add(tag);
                b.Activo = Elegidos.Contains(tag);
            };
            Controls.Add(b);
        }
        ResumeLayout();
    }

    public void Agregar(string texto)
    {
        foreach (string t in BibliotecaMemes.LeerTags(texto)) if (!Elegidos.Contains(t)) Elegidos.Add(t);
    }
}

// La tarjeta que se arrastra: derecha = meme, izquierda = no, arriba = saltar.
class Tarjeta : Panel
{
    public Visor Visor = new Visor(false);
    Label sello = new Label();
    Point origen, inicio;
    bool arrastrando;
    System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
    int vx, vy, salida;
    public bool Bloqueada;
    public event Action<int> Deslizada;   // -1 izquierda, 1 derecha, 2 arriba

    public Tarjeta()
    {
        BackColor = Tema.Borde;
        Padding = new Padding(3);
        Visor.Dock = DockStyle.Fill;
        sello.AutoSize = false;
        sello.TextAlign = ContentAlignment.MiddleCenter;
        sello.Font = Tema.Fuente(22f, FontStyle.Bold);
        sello.ForeColor = Color.White;
        sello.Visible = false;
        Controls.Add(sello);
        Controls.Add(Visor);
        sello.BringToFront();
        Enganchar(this);
        reloj.Interval = 15;
        reloj.Tick += delegate { Paso(); };
    }

    void Enganchar(Control c)
    {
        c.MouseDown += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) Empezar(); };
        c.MouseMove += delegate { Mover(); };
        c.MouseUp += delegate { Soltar(); };
        foreach (Control h in c.Controls) Enganchar(h);
    }

    public void Fijar(Point p) { origen = p; Location = p; Marcar(0, 0); }

    void Empezar()
    {
        if (Bloqueada || reloj.Enabled) return;
        arrastrando = true;
        inicio = Cursor.Position;
        Capture = true;
    }

    void Mover()
    {
        if (!arrastrando) return;
        int dx = Cursor.Position.X - inicio.X, dy = Math.Min(0, Cursor.Position.Y - inicio.Y);
        Location = new Point(origen.X + dx, origen.Y + dy / 2);
        Marcar(dx, dy);
    }

    void Soltar()
    {
        if (!arrastrando) return;
        arrastrando = false;
        Capture = false;
        int dx = Cursor.Position.X - inicio.X, dy = Cursor.Position.Y - inicio.Y;
        if (dx > 120) Salir(1);
        else if (dx < -120) Salir(-1);
        else if (dy < -120 && Math.Abs(dx) < 120) Salir(2);
        else { salida = 0; reloj.Start(); }
    }

    // El sello y el borde segun hacia donde va.
    public void Marcar(int dx, int dy)
    {
        if (dx > 40) { sello.Text = "✔ MEME"; sello.BackColor = Color.FromArgb(40, 160, 90); BackColor = sello.BackColor; }
        else if (dx < -40) { sello.Text = "✖ NO"; sello.BackColor = Color.FromArgb(200, 60, 60); BackColor = sello.BackColor; }
        else if (dy < -60) { sello.Text = "↑ SALTAR"; sello.BackColor = Color.FromArgb(90, 95, 110); BackColor = sello.BackColor; }
        else { sello.Visible = false; BackColor = Tema.Borde; return; }
        sello.SetBounds(Width / 2 - 120, 16, 240, 46);
        sello.Visible = true;
    }

    // Se va de la pantalla hacia ese lado (tambien desde los botones o el teclado).
    public void Salir(int dir)
    {
        if (Bloqueada || reloj.Enabled) return;
        salida = dir;
        vx = dir == 1 ? 60 : dir == -1 ? -60 : 0;
        vy = dir == 2 ? -50 : 0;
        Marcar(dir == 1 ? 100 : dir == -1 ? -100 : 0, dir == 2 ? -100 : 0);
        reloj.Start();
    }

    void Paso()
    {
        if (salida == 0)
        {
            // Volver al centro.
            int nx = Location.X + (origen.X - Location.X) / 3, ny = Location.Y + (origen.Y - Location.Y) / 3;
            if (Math.Abs(nx - origen.X) <= 2 && Math.Abs(ny - origen.Y) <= 2) { reloj.Stop(); Fijar(origen); return; }
            Location = new Point(nx, ny);
            return;
        }
        Location = new Point(Location.X + vx, Location.Y + vy);
        Control p = Parent;
        if (p == null || Right < -50 || Left > p.Width + 50 || Bottom < -50)
        {
            reloj.Stop();
            int d = salida;
            salida = 0;
            Fijar(origen);
            if (Deslizada != null) Deslizada(d);
        }
    }
}

class VentanaMemesApp : VentanaBase
{
    readonly Configuracion config = Configuracion.Cargar();
    BibliotecaMemes biblioteca;
    bool trabajando, cargando;

    class Accion { public string Tipo = "", Archivo = ""; public int Indice; public Meme Meme; }
    List<Accion> hechas = new List<Accion>();

    Segmentado seg = new Segmentado(new string[] { "Clasificar (swipe)", "Biblioteca" });
    List<Control> vista1 = new List<Control>(), vista2 = new List<Control>();
    Etiqueta lblEstado, lblOrigen, lblDestino;

    // ---- clasificar
    List<string> pendientes = new List<string>();
    int indice, guardados, descartados;
    bool etiquetando;
    Panel mesa = new Panel();
    // En la mesa vacia: elegir la carpeta que falte o seguir con otra carpeta por revisar.
    Boton btnVacia = new Boton("", EstiloBoton.Primario);
    Boton btnMover = new Boton("Mover los etiquetados a otra carpeta…", EstiloBoton.Secundario);
    Tarjeta tarjeta = new Tarjeta();
    Etiqueta lblCuenta, lblArchivo, lblAyuda;
    Boton btnNo = new Boton("✖  No  (←)", EstiloBoton.Secundario), btnSaltar = new Boton("↑  Saltar", EstiloBoton.Secundario);
    Boton btnSi = new Boton("✔  Meme  (→)", EstiloBoton.Primario), btnDeshacer = new Boton("↺  Deshacer  (Ctrl+Z)", EstiloBoton.Secundario);
    Boton btnOrigen = new Boton("Elegir…", EstiloBoton.Secundario), btnDestino = new Boton("Elegir…", EstiloBoton.Secundario);
    Boton btnAbrir = new Boton("Abrir aparte", EstiloBoton.Secundario);
    PanelTags tags1 = new PanelTags();
    CampoTexto txtTag1 = new CampoTexto(), txtDesc1 = new CampoTexto(), txtUso1 = new CampoTexto();
    Boton btnIA1 = new Boton("Describir con IA", EstiloBoton.Secundario);
    Boton btnGuardar1 = new Boton("Guardar y siguiente  (Ctrl+Enter)", EstiloBoton.Primario), btnNoEra = new Boton("← No era", EstiloBoton.Secundario);
    List<Control> panelTags1 = new List<Control>();

    // ---- biblioteca
    CampoTexto txtBuscar = new CampoTexto();
    Combo cmbTag = new Combo();
    // Orden de lo que falta revisar y de la biblioteca; subcarpeta donde se guarda (se puede escribir una nueva).
    Combo cmbOrden1 = new Combo(), cmbOrden2 = new Combo(), cmbCarpeta1 = new Combo(true), cmbCarpeta2 = new Combo(true);
    static readonly string[] Ordenes1 = { "recientes", "nombre" }, Ordenes2 = { "recientes", "nombre", "usados" };
    Lista lst = new Lista();
    Etiqueta lblInfo;
    Visor visor2 = new Visor(true);
    PanelTags tags2 = new PanelTags();
    CampoTexto txtTag2 = new CampoTexto(), txtDesc2 = new CampoTexto(), txtUso2 = new CampoTexto();
    Boton btnGuardar2 = new Boton("Guardar cambios", EstiloBoton.Primario), btnIA2 = new Boton("Describir con IA", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Quitar (a la Papelera)", EstiloBoton.Secundario), btnIATodos = new Boton("Describir con IA los que faltan", EstiloBoton.Secundario);
    Meme actual2;

    public VentanaMemesApp() : base("Memes", 1200)
    {
        MinimizeBox = true;
        int m = Margen, w = Ancho;
        Encabezado("Memes", "Tu biblioteca de memes para vegas-cut: clasifica una carpeta deslizando y corrige lo que ya tienes.");
        int y = 92;
        Pos(seg, m, y, 420, 34);
        Texto("A (memes)", Tema.Pequena, Tema.TextoSuave, m + 440, y + 9, 70, 18);
        lblDestino = Texto("", Tema.Normal, Tema.Texto, m + 512, y + 7, w - 512 - 90, 20);
        Pos(btnDestino, m + w - 80, y + 2, 80, 30);
        y += 46;

        // ================= clasificar
        int y1 = y;
        vista1.Add(Texto("DE (por revisar)", Tema.Pequena, Tema.TextoSuave, m, y1 + 8, 110, 18));
        lblOrigen = Texto("", Tema.Normal, Tema.Texto, m + 112, y1 + 6, 360, 20);
        vista1.Add(lblOrigen);
        cmbOrden1.Items.AddRange(new object[] { "Lo más reciente primero", "Por nombre" });
        vista1.Add(Pos(cmbOrden1, m + 480, y1 + 1, 190, 30));
        vista1.Add(Pos(btnOrigen, m + 680, y1, 80, 30));
        y1 += 38;
        lblCuenta = Texto("", Tema.Pequena, Tema.TextoSuave, m, y1, 760, 18);
        vista1.Add(lblCuenta);
        y1 += 24;
        int mw = 760, mh = 440;
        mesa.BackColor = Tema.Fondo;
        vista1.Add(Pos(mesa, m, y1, mw, mh));
        mesa.Controls.Add(tarjeta);
        mesa.Controls.Add(btnVacia);
        btnVacia.SetBounds((mw - 340) / 2, (mh - 48) / 2, 340, 48);
        btnVacia.Visible = false;
        tarjeta.Size = new Size(640, 400);
        tarjeta.Fijar(new Point((mw - 640) / 2, 20));
        tarjeta.Deslizada += delegate (int d) { Decidir(d); };
        y1 += mh + 6;
        lblArchivo = Texto("", Tema.Normal, Tema.Texto, m, y1, mw - 150, 40);
        vista1.Add(lblArchivo);
        vista1.Add(Pos(btnAbrir, m + mw - 140, y1, 140, 30));
        y1 += 46;
        int bw = (mw - 3 * 12) / 4;
        vista1.Add(Pos(btnNo, m, y1, bw, 44));
        vista1.Add(Pos(btnDeshacer, m + bw + 12, y1, bw, 44));
        vista1.Add(Pos(btnSaltar, m + 2 * (bw + 12), y1, bw, 44));
        vista1.Add(Pos(btnSi, m + 3 * (bw + 12), y1, bw, 44));
        y1 += 56;

        int x2 = m + mw + 24, w2 = w - mw - 24, yt = y + 62;
        lblAyuda = Texto("Arrastra la tarjeta:\n\n→  derecha: ES MEME (le pones tags y se guarda)\n←  izquierda: NO ES (no se vuelve a mostrar)\n↑  arriba: SALTAR (vuelve al final)\n\n" +
                         "O usa las flechas del teclado. Ctrl+Z deshace la última.\n\nLo que no es meme no se mueve ni se borra.", Tema.Normal, Tema.TextoSuave, x2, yt, w2, 220);
        vista1.Add(lblAyuda);
        panelTags1.Add(Texto("TAGS (clic para elegir; escribe uno nuevo y Enter)", Tema.Pequena, Tema.TextoSuave, x2, yt, w2, 18));
        panelTags1.Add(Pos(tags1, x2, yt + 20, w2, 170));
        panelTags1.Add(Pos(txtTag1, x2, yt + 196, w2, 32));
        panelTags1.Add(Texto("QUÉ ES (se puede dejar para después)", Tema.Pequena, Tema.TextoSuave, x2, yt + 238, w2, 18));
        panelTags1.Add(Pos(txtDesc1, x2, yt + 258, w2, 32));
        panelTags1.Add(Texto("CUÁNDO USARLO", Tema.Pequena, Tema.TextoSuave, x2, yt + 298, w2, 18));
        panelTags1.Add(Pos(txtUso1, x2, yt + 318, w2, 32));
        panelTags1.Add(Texto("SUBCARPETA (opcional: vacía = tu carpeta de memes)", Tema.Pequena, Tema.TextoSuave, x2, yt + 358, w2, 18));
        panelTags1.Add(Pos(cmbCarpeta1, x2, yt + 378, w2, 30));
        panelTags1.Add(Pos(btnIA1, x2, yt + 418, w2, 32));
        panelTags1.Add(Pos(btnNoEra, x2, yt + 460, 110, 44));
        panelTags1.Add(Pos(btnGuardar1, x2 + 118, yt + 460, w2 - 118, 44));
        vista1.AddRange(panelTags1);

        // ================= biblioteca
        int y2 = y;
        int lw = 560;
        vista2.Add(Texto("BUSCAR", Tema.Pequena, Tema.TextoSuave, m, y2 + 8, 56, 18));
        vista2.Add(Pos(txtBuscar, m + 58, y2, lw - 58 - 316, 32));
        vista2.Add(Pos(cmbTag, m + lw - 308, y2 + 1, 150, 30));
        cmbOrden2.Items.AddRange(new object[] { "Más recientes", "Por nombre", "Más usados" });
        vista2.Add(Pos(cmbOrden2, m + lw - 150, y2 + 1, 150, 30));
        y2 += 40;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.CheckBoxes = false;
        lst.Columns.Add("Archivo", 170);
        lst.Columns.Add("Qué es", lw - 170 - 130 - 50 - sb);
        lst.Columns.Add("Tags", 130);
        lst.Columns.Add("Usado", 50);
        vista2.Add(Pos(lst, m, y2, lw, 520));
        lblInfo = Texto("", Tema.Pequena, Tema.TextoSuave, m, y2 + 526, lw, 18);
        vista2.Add(lblInfo);
        int bm = (lw - 12) / 2;
        vista2.Add(Pos(btnIATodos, m, y2 + 548, bm, 36));
        vista2.Add(Pos(btnMover, m + bm + 12, y2 + 548, lw - bm - 12, 36));
        int x3 = m + lw + 24, w3 = w - lw - 24, y3 = y;
        vista2.Add(Pos(visor2, x3, y3, w3, 300));
        y3 += 308;
        vista2.Add(Texto("TAGS (clic para elegir; escribe uno nuevo y Enter)", Tema.Pequena, Tema.TextoSuave, x3, y3, w3, 18));
        vista2.Add(Pos(tags2, x3, y3 + 20, w3, 96));
        vista2.Add(Pos(txtTag2, x3, y3 + 122, w3, 30));
        vista2.Add(Texto("QUÉ ES", Tema.Pequena, Tema.TextoSuave, x3, y3 + 160, w3, 18));
        vista2.Add(Pos(txtDesc2, x3, y3 + 178, w3, 30));
        vista2.Add(Texto("CUÁNDO USARLO", Tema.Pequena, Tema.TextoSuave, x3, y3 + 214, w3, 18));
        vista2.Add(Pos(txtUso2, x3, y3 + 232, w3, 30));
        vista2.Add(Texto("SUBCARPETA (opcional: cámbiala solo si quieres moverlo)", Tema.Pequena, Tema.TextoSuave, x3, y3 + 268, w3, 18));
        vista2.Add(Pos(cmbCarpeta2, x3, y3 + 286, w3, 30));
        int b3 = (w3 - 24) / 3;
        vista2.Add(Pos(btnQuitar, x3, y3 + 326, b3, 36));
        vista2.Add(Pos(btnIA2, x3 + b3 + 12, y3 + 326, b3, 36));
        vista2.Add(Pos(btnGuardar2, x3 + 2 * (b3 + 12), y3 + 326, b3, 36));

        int yb = Math.Max(y1, y + 680);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, yb, w, 36);
        ClientSize = new Size(ClientSize.Width, yb + 36 + 12);

        // ---- eventos
        seg.Cambio += delegate { if (!cargando) Vista(seg.Seleccion); };
        btnOrigen.Click += delegate { Elegir(true); };
        btnDestino.Click += delegate { Elegir(false); };
        btnAbrir.Click += delegate { string f = Actual(); if (f != null) try { Process.Start(f); } catch { } };
        btnNo.Click += delegate { if (!etiquetando) tarjeta.Salir(-1); };
        btnSi.Click += delegate { if (!etiquetando) tarjeta.Salir(1); };
        btnSaltar.Click += delegate { if (!etiquetando) tarjeta.Salir(2); };
        btnDeshacer.Click += delegate { Deshacer(); };
        btnNoEra.Click += delegate { Etiquetar(false); };
        btnGuardar1.Click += delegate { GuardarMeme(); };
        btnIA1.Click += delegate { ConIA(Actual(), true); };
        txtTag1.Caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            tags1.Agregar(txtTag1.Text); txtTag1.Text = "";
            tags1.Llenar(Tags());
        };
        txtTag2.Caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            tags2.Agregar(txtTag2.Text); txtTag2.Text = "";
            tags2.Llenar(Tags());
        };
        txtBuscar.Caja.TextChanged += delegate { if (!cargando) LlenarLista(); };
        cmbTag.SelectedIndexChanged += delegate { if (!cargando) LlenarLista(); };
        cmbOrden1.SelectedIndexChanged += delegate
        {
            if (cargando || cmbOrden1.SelectedIndex < 0) return;
            config.MemesOrden = Ordenes1[cmbOrden1.SelectedIndex];
            try { config.Guardar(); } catch { }
            if (biblioteca == null || etiquetando) return;
            try { pendientes = biblioteca.Pendientes(config.CarpetaMemesEntrada, config.MemesOrden == "recientes"); } catch { }
            indice = 0;
            MostrarTarjeta();
        };
        cmbOrden2.SelectedIndexChanged += delegate
        {
            if (cargando || cmbOrden2.SelectedIndex < 0) return;
            config.MemesOrdenLista = Ordenes2[cmbOrden2.SelectedIndex];
            try { config.Guardar(); } catch { }
            LlenarLista();
        };
        lst.SelectedIndexChanged += delegate { Seleccionar(); };
        lst.DoubleClick += delegate { if (actual2 != null) try { Process.Start(biblioteca.Completa(actual2)); } catch { } };
        btnGuardar2.Click += delegate { GuardarEdicion(); };
        btnIA2.Click += delegate { if (actual2 != null) ConIA(biblioteca.Completa(actual2), false); };
        btnQuitar.Click += delegate { Quitar(); };
        btnIATodos.Click += delegate { DescribirTodos(); };
        btnMover.Click += delegate { MoverEtiquetados(); };
        btnVacia.Click += delegate { Elegir(biblioteca != null); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; else { tarjeta.Visor.Detener(); visor2.Detener(); } };

        Cargar();
        Vista(0);
    }

    // Teclas: flechas para deslizar, Ctrl+Z deshacer, Ctrl+Enter guardar. Escape no cierra el programa.
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape) { if (etiquetando) Etiquetar(false); return true; }
        if (seg.Seleccion == 0 && !trabajando)
        {
            bool escribiendo = ActiveControl is CampoTexto || (ActiveControl != null && ActiveControl.GetType().Name == "TextBox");
            if (keyData == (Keys.Control | Keys.Z) && !escribiendo) { Deshacer(); return true; }
            if (keyData == (Keys.Control | Keys.Enter) && etiquetando) { GuardarMeme(); return true; }
            if (!etiquetando && !escribiendo)
            {
                if (keyData == Keys.Right) { tarjeta.Salir(1); return true; }
                if (keyData == Keys.Left) { tarjeta.Salir(-1); return true; }
                if (keyData == Keys.Up) { tarjeta.Salir(2); return true; }
            }
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Vista(int i)
    {
        cargando = true; seg.Seleccion = i; cargando = false;
        foreach (Control c in vista1) c.Visible = i == 0;
        foreach (Control c in vista2) c.Visible = i == 1;
        if (i == 0) { visor2.Detener(); Etiquetar(etiquetando); }
        else { tarjeta.Visor.Detener(); LlenarLista(); }
    }

    List<string> Tags() { return biblioteca != null ? biblioteca.TagsUsados() : new List<string>(); }

    void Elegir(bool origen)
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = origen ? "Carpeta con los archivos por revisar (también subcarpetas)" : "Carpeta de memes (la que usa vegas-cut)";
            string a = origen ? config.CarpetaMemesEntrada : config.CarpetaMemes;
            if (a.Length > 0 && Directory.Exists(a)) d.SelectedPath = a;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (origen) config.CarpetaMemesEntrada = d.SelectedPath; else config.CarpetaMemes = d.SelectedPath;
        }
        try { config.Guardar(); } catch { }
        Cargar();
        Vista(seg.Seleccion);
    }

    void Cargar()
    {
        lblOrigen.Text = config.CarpetaMemesEntrada.Length > 0 ? config.CarpetaMemesEntrada : "(elige la carpeta por revisar)";
        lblDestino.Text = config.CarpetaMemes.Length > 0 ? config.CarpetaMemes : "(elige tu carpeta de memes)";
        biblioteca = null;
        pendientes.Clear();
        hechas.Clear();
        if (config.CarpetaMemes.Length > 0 && Directory.Exists(config.CarpetaMemes))
            try { biblioteca = BibliotecaMemes.Cargar(config.CarpetaMemes); } catch (Exception ex) { Estado("No se pudo leer el índice: " + ex.Message, true); }
        if (biblioteca != null)
        {
            try { if (biblioteca.Escanear() > 0) biblioteca.Guardar(); } catch { }
            if (Directory.Exists(config.CarpetaMemesEntrada))
                try { pendientes = biblioteca.Pendientes(config.CarpetaMemesEntrada, config.MemesOrden == "recientes"); } catch (Exception ex) { Estado("No se pudo leer la carpeta: " + ex.Message, true); }
        }
        indice = 0;
        cargando = true;
        cmbTag.Items.Clear();
        cmbTag.Items.Add("Todos los tags");
        if (biblioteca != null)
            foreach (string t in biblioteca.TagsUsados()) cmbTag.Items.Add(t + " (" + biblioteca.Filtrar(t, "").Count + ")");
        cmbTag.SelectedIndex = 0;
        cmbOrden1.SelectedIndex = Math.Max(0, Array.IndexOf(Ordenes1, config.MemesOrden));
        cmbOrden2.SelectedIndex = Math.Max(0, Array.IndexOf(Ordenes2, config.MemesOrdenLista));
        LlenarCarpetas();
        cmbCarpeta1.Text = config.MemesSubcarpeta;
        cargando = false;
    }

    void LlenarCarpetas()
    {
        string t1 = cmbCarpeta1.Text, t2 = cmbCarpeta2.Text;
        cmbCarpeta1.Items.Clear(); cmbCarpeta2.Items.Clear();
        if (biblioteca != null)
            foreach (string d in biblioteca.Subcarpetas()) { cmbCarpeta1.Items.Add(d); cmbCarpeta2.Items.Add(d); }
        cmbCarpeta1.Text = t1; cmbCarpeta2.Text = t2;
    }

    // ================================================== clasificar

    string Actual() { return indice >= 0 && indice < pendientes.Count ? pendientes[indice] : null; }

    void MostrarTarjeta()
    {
        string f = Actual();
        lblCuenta.Text = biblioteca == null ? "Elige tu carpeta de memes (arriba a la derecha) y la carpeta por revisar." :
            (pendientes.Count == 0 ? "No queda nada por revisar." : pendientes.Count + " por revisar") +
            " · esta vez: " + guardados + " memes, " + descartados + " descartados · biblioteca: " + biblioteca.Memes.Count;
        tarjeta.Visible = f != null;
        btnVacia.Visible = f == null && !etiquetando;
        btnVacia.Text = biblioteca == null ? "Elegir tu carpeta de memes" :
            Directory.Exists(config.CarpetaMemesEntrada) ? "Seguir con otra carpeta por revisar…" : "Elegir la carpeta por revisar…";
        foreach (Control c in new Control[] { btnNo, btnSi, btnSaltar, btnAbrir }) c.Enabled = f != null && !etiquetando;
        btnDeshacer.Enabled = hechas.Count > 0 && !etiquetando;
        if (f == null) { lblArchivo.Text = ""; tarjeta.Visor.Detener(); return; }
        long bytes = 0;
        try { bytes = new FileInfo(f).Length; } catch { }
        string rel = config.CarpetaMemesEntrada.Length > 0 && f.StartsWith(config.CarpetaMemesEntrada, StringComparison.OrdinalIgnoreCase)
            ? f.Substring(config.CarpetaMemesEntrada.Length).TrimStart('\\', '/') : f;
        lblArchivo.Text = rel + "\n" + BibliotecaMemes.TipoDe(f) + " · " + (bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, bytes / 1024) + " KB");
        tarjeta.Visor.Mostrar(f);
        string ext = Path.GetExtension(f).ToLowerInvariant();
        if (ext == ".webm" || ext == ".mkv") Estado("Este formato no se ve en la tarjeta: «Abrir aparte».", false);
    }

    void Decidir(int dir)
    {
        string f = Actual();
        if (f == null || biblioteca == null) return;
        if (dir == 1) { Etiquetar(true); return; }
        if (dir == -1)
        {
            biblioteca.Descartar(f);
            try { biblioteca.Guardar(); } catch (Exception ex) { Estado("No se pudo guardar: " + ex.Message, true); return; }
            hechas.Add(new Accion { Tipo = "no", Archivo = f, Indice = indice });
            pendientes.RemoveAt(indice);
            descartados++;
            Estado("✖ " + Path.GetFileName(f), false);
        }
        else
        {
            hechas.Add(new Accion { Tipo = "saltar", Archivo = f, Indice = indice });
            pendientes.RemoveAt(indice);
            pendientes.Add(f);
            Estado("↑ Saltado: vuelve al final.", false);
        }
        if (indice >= pendientes.Count) indice = 0;
        MostrarTarjeta();
    }

    void Etiquetar(bool si)
    {
        etiquetando = si;
        tarjeta.Bloqueada = si;
        if (si) tarjeta.Marcar(100, 0); else tarjeta.Marcar(0, 0);
        bool v = seg.Seleccion == 0;
        lblAyuda.Visible = v && !si;
        foreach (Control c in panelTags1) c.Visible = v && si;
        if (si)
        {
            tags1.Elegidos = new List<string>();
            tags1.Llenar(Tags());
            txtDesc1.Text = ""; txtUso1.Text = ""; txtTag1.Text = "";
            btnIA1.Enabled = !String.IsNullOrEmpty(config.GeminiClave);
            txtTag1.Focus();
        }
        MostrarTarjeta();
    }

    void GuardarMeme()
    {
        string f = Actual();
        if (f == null || biblioteca == null || !etiquetando) return;
        tags1.Agregar(txtTag1.Text); txtTag1.Text = "";
        tarjeta.Visor.Detener();   // que el visor suelte el archivo antes de moverlo
        try
        {
            string sub = BibliotecaMemes.LimpiarSubcarpeta(cmbCarpeta1.Text);
            Meme mm = biblioteca.Agregar(f, txtDesc1.Text, tags1.Elegidos, txtUso1.Text, sub);
            biblioteca.Guardar();
            if (sub != config.MemesSubcarpeta) { config.MemesSubcarpeta = sub; try { config.Guardar(); } catch { } }
            if (sub.Length > 0 && !cmbCarpeta1.Items.Contains(sub)) LlenarCarpetas();
            cmbCarpeta1.Text = sub;
            hechas.Add(new Accion { Tipo = "si", Archivo = f, Indice = indice, Meme = mm });
            pendientes.RemoveAt(indice);
            if (indice >= pendientes.Count) indice = 0;
            guardados++;
            Estado("✔ «" + mm.Ruta + "» con " + mm.Tags.Count + " tags" + (mm.Descrito ? "." : " (sin descripción: descríbelo después en Biblioteca)."), false);
        }
        catch (Exception ex) { Estado("No se pudo mover: " + ex.Message + " (¿está abierto en otro programa?)", true); MostrarTarjeta(); return; }
        Etiquetar(false);
    }

    void Deshacer()
    {
        if (hechas.Count == 0 || etiquetando || biblioteca == null) return;
        Accion a = hechas[hechas.Count - 1];
        hechas.RemoveAt(hechas.Count - 1);
        try
        {
            if (a.Tipo == "no") { biblioteca.QuitarDescartado(a.Archivo); descartados--; }
            else if (a.Tipo == "si") { tarjeta.Visor.Detener(); biblioteca.DeshacerAgregar(a.Meme, a.Archivo); guardados--; }
            else pendientes.Remove(a.Archivo);
            biblioteca.Guardar();
            pendientes.Insert(Math.Min(a.Indice, pendientes.Count), a.Archivo);
            indice = Math.Min(a.Indice, pendientes.Count - 1);
            Estado("↺ Deshecho: " + Path.GetFileName(a.Archivo), false);
        }
        catch (Exception ex) { Estado("No se pudo deshacer: " + ex.Message, true); }
        MostrarTarjeta();
    }

    // ================================================== biblioteca

    // Los que ya tienen tags o descripcion se van a otra carpeta de memes (que pasa a ser
    // la de vegas-cut); los que no, se quedan y esa carpeta queda como «por revisar».
    void MoverEtiquetados()
    {
        if (biblioteca == null || trabajando) return;
        int n = biblioteca.Etiquetados().Count, resto = biblioteca.Memes.Count - n;
        if (n == 0) { Estado("Todavía no hay memes con tags o descripción en esta carpeta.", true); return; }
        string destino;
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta donde quedarán tus memes etiquetados (puedes crear una nueva)";
            d.ShowNewFolderButton = true;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            destino = d.SelectedPath;
        }
        string problema = biblioteca.ProblemaDestino(destino);
        if (problema.Length > 0) { Estado("No se puede: " + problema, true); return; }
        string origen = biblioteca.Carpeta;
        if (MessageBox.Show(this, "Se moverán " + n + " archivos con tags o descripción a:\n" + destino +
                            "\n\n(con sus subcarpetas, tags e historial de uso). Los " + resto + " sin etiquetar se quedan en:\n" + origen +
                            "\n\nDesde ahora vegas-cut usará la carpeta nueva, y la de ahora queda para seguir revisando lo que falta. ¿Seguir?",
                            "Memes", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        visor2.Detener(); tarjeta.Visor.Detener();
        List<string> errores = new List<string>();
        int movidos = 0;
        try
        {
            BibliotecaMemes nueva = BibliotecaMemes.Cargar(destino);
            try { nueva.Escanear(); } catch { }
            movidos = biblioteca.Trasladar(nueva, errores);
            nueva.Guardar();
            biblioteca.Guardar();
        }
        catch (Exception ex) { errores.Add(ex.Message); }
        if (movidos > 0)
        {
            config.CarpetaMemes = destino;
            config.CarpetaMemesEntrada = origen;
            try { config.Guardar(); } catch { }
        }
        Cargar();
        Vista(movidos > 0 ? 0 : 1);
        Estado("✔ " + movidos + " memes movidos a «" + destino + "». En Clasificar siguen los que quedaron sin etiquetar." +
               (errores.Count > 0 ? " No se pudo: " + String.Join("; ", errores.ToArray()) : ""), errores.Count > 0);
    }

    void LlenarLista()
    {
        lst.Items.Clear();
        if (biblioteca == null) { lblInfo.Text = "Elige tu carpeta de memes (arriba a la derecha)."; return; }
        string tag = cmbTag.SelectedIndex > 0 ? ((string)cmbTag.SelectedItem).Substring(0, ((string)cmbTag.SelectedItem).LastIndexOf(" (")) : "";
        List<Meme> l = biblioteca.Filtrar(tag, txtBuscar.Text);
        biblioteca.Ordenar(l, config.MemesOrdenLista);
        foreach (Meme mm in l)
        {
            ListViewItem it = new ListViewItem(mm.Ruta);
            it.SubItems.Add(mm.Descripcion);
            it.SubItems.Add(String.Join(", ", mm.Tags.ToArray()));
            it.SubItems.Add(mm.Usos.Count > 0 ? mm.Usos.Count.ToString() : "");
            if (!mm.Descrito) it.ForeColor = Tema.TextoSuave;
            it.Tag = mm;
            lst.Items.Add(it);
        }
        int sin = biblioteca.SinDescribir().Count;
        lblInfo.Text = l.Count + " de " + biblioteca.Memes.Count + " memes" + (sin > 0 ? " · " + sin + " sin describir (en gris; vegas-cut no los usa hasta que tengan descripción)" : "");
        btnIATodos.Enabled = sin > 0 && !trabajando && !String.IsNullOrEmpty(config.GeminiClave);
        btnIATodos.Text = "Describir con IA los que faltan (" + sin + ")";
        btnMover.Enabled = !trabajando && biblioteca.Etiquetados().Count > 0;
        Seleccionar();
    }

    void Seleccionar()
    {
        actual2 = lst.SelectedIndices.Count > 0 ? (Meme)lst.Items[lst.SelectedIndices[0]].Tag : null;
        foreach (Control c in new Control[] { btnGuardar2, btnIA2, btnQuitar }) c.Enabled = actual2 != null && !trabajando;
        if (btnIA2.Enabled) btnIA2.Enabled = !String.IsNullOrEmpty(config.GeminiClave);
        tags2.Elegidos = actual2 != null ? new List<string>(actual2.Tags) : new List<string>();
        tags2.Llenar(Tags());
        txtDesc2.Text = actual2 != null ? actual2.Descripcion : "";
        txtUso2.Text = actual2 != null ? actual2.Uso : "";
        cmbCarpeta2.Text = actual2 != null ? BibliotecaMemes.SubcarpetaDe(actual2) : "";
        cmbCarpeta2.Enabled = actual2 != null;
        visor2.Mostrar(actual2 != null ? biblioteca.Completa(actual2) : null);
    }

    void GuardarEdicion()
    {
        if (actual2 == null) return;
        tags2.Agregar(txtTag2.Text); txtTag2.Text = "";
        actual2.Tags = new List<string>(tags2.Elegidos);
        actual2.Descripcion = txtDesc2.Text.Trim();
        actual2.Uso = txtUso2.Text.Trim();
        string movido = "";
        if (!String.Equals(BibliotecaMemes.LimpiarSubcarpeta(cmbCarpeta2.Text), BibliotecaMemes.SubcarpetaDe(actual2), StringComparison.OrdinalIgnoreCase))
        {
            visor2.Detener();   // que el visor suelte el archivo antes de moverlo
            try { biblioteca.Mover(actual2, cmbCarpeta2.Text); movido = " Movido a «" + actual2.Ruta + "»."; LlenarCarpetas(); }
            catch (Exception ex) { movido = " No se pudo mover: " + ex.Message + " (¿está abierto en otro programa?)"; }
        }
        try { biblioteca.Guardar(); Estado("✔ Guardado." + movido, movido.Contains("No se pudo")); } catch (Exception ex) { Estado("No se pudo guardar: " + ex.Message, true); }
        Meme sel = actual2;
        LlenarLista();
        foreach (ListViewItem it in lst.Items) if (it.Tag == sel) { it.Selected = true; it.EnsureVisible(); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string pTo; public ushort fFlags;
        public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    void Quitar()
    {
        if (actual2 == null) return;
        if (MessageBox.Show(this, "¿Quitar «" + actual2.Ruta + "» de la biblioteca y mandar el archivo a la Papelera de reciclaje?", "Memes",
                            MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        visor2.Detener();
        try
        {
            string f = biblioteca.Completa(actual2);
            if (File.Exists(f))
            {
                SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
                op.wFunc = 3; op.pFrom = f + "\0\0"; op.fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400;
                if (SHFileOperation(ref op) != 0) throw new IOException("no se pudo mandar a la Papelera");
            }
            biblioteca.Memes.Remove(actual2);
            biblioteca.Guardar();
            Estado("✔ Quitado (está en la Papelera).", false);
        }
        catch (Exception ex) { Estado("No se pudo quitar: " + ex.Message, true); }
        LlenarLista();
    }

    // ================================================== IA

    void ConIA(string f, bool clasificando)
    {
        if (f == null || String.IsNullOrEmpty(config.GeminiClave)) return;
        KeyValuePair<string, byte[]>? adj = BibliotecaMemes.Adjunto(f, 18);
        List<KeyValuePair<string, byte[]>> adjuntos = new List<KeyValuePair<string, byte[]>>();
        if (adj != null) adjuntos.Add(adj.Value);
        string instr = BibliotecaMemes.InstruccionesUno(Tags());
        string msg = "Archivo: " + Path.GetFileName(f) + " (carpeta: " + Path.GetFileName(Path.GetDirectoryName(f)) + ")" + (adj == null ? " — sin adjunto" : "");
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        btnIA1.Enabled = btnIA2.Enabled = btnGuardar1.Enabled = btnGuardar2.Enabled = false;
        Estado("Gemini está " + (adj != null ? "viendo" : "leyendo el nombre de") + " " + Path.GetFileName(f) + "…", false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(clave, modelo, instr, msg, true, adjuntos); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    btnIA1.Enabled = btnIA2.Enabled = btnGuardar1.Enabled = btnGuardar2.Enabled = true;
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        string desc, uso;
                        string tg = BibliotecaMemes.RespuestaUno(resp, out desc, out uso);
                        if (desc.Length == 0) { Estado("Gemini no sabe qué es: escríbelo tú.", true); return; }
                        CampoTexto d = clasificando ? txtDesc1 : txtDesc2, u = clasificando ? txtUso1 : txtUso2;
                        PanelTags pt = clasificando ? tags1 : tags2;
                        if (d.Text.Trim().Length == 0) d.Text = desc;
                        if (u.Text.Trim().Length == 0) u.Text = uso;
                        pt.Agregar(tg);
                        pt.Llenar(Tags());
                        Estado("✔ Revisa lo que puso Gemini y corrige lo que haga falta.", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + ").", true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void DescribirTodos()
    {
        List<Meme> falta = biblioteca.SinDescribir();
        if (falta.Count == 0) return;
        BibliotecaMemes b = biblioteca;
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        btnIATodos.Enabled = false;
        Thread hilo = new Thread(delegate ()
        {
            int hechos = 0;
            string error = null;
            for (int i = 0; i < falta.Count && error == null; i += 20)
            {
                List<Meme> lote = falta.GetRange(i, Math.Min(20, falta.Count - i));
                int ii = i;
                try { BeginInvoke((MethodInvoker)delegate { Estado("Gemini está viendo " + (ii + 1) + "–" + (ii + lote.Count) + " de " + falta.Count + "…", false); }); } catch { }
                try
                {
                    List<KeyValuePair<string, byte[]>> adj = new List<KeyValuePair<string, byte[]>>();
                    string msg = b.MensajeDescribir(lote, adj);
                    hechos += BibliotecaMemes.AplicarDescripciones(lote, Gemini.Generar(clave, modelo, BibliotecaMemes.InstruccionesDescribir(), msg, true, adj));
                }
                catch (Exception ex) { error = ex.Message; }
            }
            try { b.Guardar(); } catch { }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    LlenarLista();
                    Estado((error != null ? "Se cortó (" + error + "). " : "✔ ") + hechos + " de " + falta.Count + " descritos. Revísalos.", error != null);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }
}
