// Memes.cs
// Programa aparte (no es un script de Vegas) para la biblioteca de memes de
// vegas-cut: clasificar una carpeta deslizando tarjetas (como Tinder) y
// trabajar la biblioteca (buscar por tags, corregir, describir con IA).
// Lo compila Memes.bat (o usa Memes.exe ya compilado).
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;
using Microsoft.Win32;

// ---- src/memes/MemesApp.cs ----

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
                        tipo == "sonido" ? "<div style=\"color:#aaa;font:24px Segoe UI;margin-top:30%\">\u266a " + Path.GetFileNameWithoutExtension(f) + "</div><audio src=\"" + url + "\" autoplay loop" + ctl + "></audio>" :
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
        if (dx > 40) { sello.Text = "\u2714 MEME"; sello.BackColor = Color.FromArgb(40, 160, 90); BackColor = sello.BackColor; }
        else if (dx < -40) { sello.Text = "\u2716 NO"; sello.BackColor = Color.FromArgb(200, 60, 60); BackColor = sello.BackColor; }
        else if (dy < -60) { sello.Text = "\u2191 SALTAR"; sello.BackColor = Color.FromArgb(90, 95, 110); BackColor = sello.BackColor; }
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
    Tarjeta tarjeta = new Tarjeta();
    Etiqueta lblCuenta, lblArchivo, lblAyuda;
    Boton btnNo = new Boton("\u2716  No  (\u2190)", EstiloBoton.Secundario), btnSaltar = new Boton("\u2191  Saltar", EstiloBoton.Secundario);
    Boton btnSi = new Boton("\u2714  Meme  (\u2192)", EstiloBoton.Primario), btnDeshacer = new Boton("\u21ba  Deshacer  (Ctrl+Z)", EstiloBoton.Secundario);
    Boton btnOrigen = new Boton("Elegir\u2026", EstiloBoton.Secundario), btnDestino = new Boton("Elegir\u2026", EstiloBoton.Secundario);
    Boton btnAbrir = new Boton("Abrir aparte", EstiloBoton.Secundario);
    PanelTags tags1 = new PanelTags();
    CampoTexto txtTag1 = new CampoTexto(), txtDesc1 = new CampoTexto(), txtUso1 = new CampoTexto();
    Boton btnIA1 = new Boton("Describir con IA", EstiloBoton.Secundario);
    Boton btnGuardar1 = new Boton("Guardar y siguiente  (Ctrl+Enter)", EstiloBoton.Primario), btnNoEra = new Boton("\u2190 No era", EstiloBoton.Secundario);
    List<Control> panelTags1 = new List<Control>();

    // ---- biblioteca
    CampoTexto txtBuscar = new CampoTexto();
    Combo cmbTag = new Combo();
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
        lblOrigen = Texto("", Tema.Normal, Tema.Texto, m + 112, y1 + 6, 560, 20);
        vista1.Add(lblOrigen);
        vista1.Add(Pos(btnOrigen, m + 680, y1, 80, 30));
        y1 += 38;
        lblCuenta = Texto("", Tema.Pequena, Tema.TextoSuave, m, y1, 760, 18);
        vista1.Add(lblCuenta);
        y1 += 24;
        int mw = 760, mh = 440;
        mesa.BackColor = Tema.Fondo;
        vista1.Add(Pos(mesa, m, y1, mw, mh));
        mesa.Controls.Add(tarjeta);
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
        lblAyuda = Texto("Arrastra la tarjeta:\n\n\u2192  derecha: ES MEME (le pones tags y se guarda)\n\u2190  izquierda: NO ES (no se vuelve a mostrar)\n\u2191  arriba: SALTAR (vuelve al final)\n\n" +
                         "O usa las flechas del teclado. Ctrl+Z deshace la \u00faltima.\n\nLo que no es meme no se mueve ni se borra.", Tema.Normal, Tema.TextoSuave, x2, yt, w2, 220);
        vista1.Add(lblAyuda);
        panelTags1.Add(Texto("TAGS (clic para elegir; escribe uno nuevo y Enter)", Tema.Pequena, Tema.TextoSuave, x2, yt, w2, 18));
        panelTags1.Add(Pos(tags1, x2, yt + 20, w2, 170));
        panelTags1.Add(Pos(txtTag1, x2, yt + 196, w2, 32));
        panelTags1.Add(Texto("QU\u00c9 ES (se puede dejar para despu\u00e9s)", Tema.Pequena, Tema.TextoSuave, x2, yt + 238, w2, 18));
        panelTags1.Add(Pos(txtDesc1, x2, yt + 258, w2, 32));
        panelTags1.Add(Texto("CU\u00c1NDO USARLO", Tema.Pequena, Tema.TextoSuave, x2, yt + 298, w2, 18));
        panelTags1.Add(Pos(txtUso1, x2, yt + 318, w2, 32));
        panelTags1.Add(Pos(btnIA1, x2, yt + 360, w2, 32));
        panelTags1.Add(Pos(btnNoEra, x2, yt + 402, 110, 44));
        panelTags1.Add(Pos(btnGuardar1, x2 + 118, yt + 402, w2 - 118, 44));
        vista1.AddRange(panelTags1);

        // ================= biblioteca
        int y2 = y;
        int lw = 560;
        vista2.Add(Texto("BUSCAR", Tema.Pequena, Tema.TextoSuave, m, y2 + 8, 56, 18));
        vista2.Add(Pos(txtBuscar, m + 58, y2, lw - 288, 32));
        vista2.Add(Pos(cmbTag, m + lw - 220, y2 + 1, 220, 30));
        y2 += 40;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.CheckBoxes = false;
        lst.Columns.Add("Archivo", 170);
        lst.Columns.Add("Qu\u00e9 es", lw - 170 - 130 - 50 - sb);
        lst.Columns.Add("Tags", 130);
        lst.Columns.Add("Usado", 50);
        vista2.Add(Pos(lst, m, y2, lw, 520));
        lblInfo = Texto("", Tema.Pequena, Tema.TextoSuave, m, y2 + 526, lw, 18);
        vista2.Add(lblInfo);
        vista2.Add(Pos(btnIATodos, m, y2 + 548, lw, 36));
        int x3 = m + lw + 24, w3 = w - lw - 24, y3 = y;
        vista2.Add(Pos(visor2, x3, y3, w3, 300));
        y3 += 308;
        vista2.Add(Texto("TAGS (clic para elegir; escribe uno nuevo y Enter)", Tema.Pequena, Tema.TextoSuave, x3, y3, w3, 18));
        vista2.Add(Pos(tags2, x3, y3 + 20, w3, 96));
        vista2.Add(Pos(txtTag2, x3, y3 + 122, w3, 30));
        vista2.Add(Texto("QU\u00c9 ES", Tema.Pequena, Tema.TextoSuave, x3, y3 + 160, w3, 18));
        vista2.Add(Pos(txtDesc2, x3, y3 + 178, w3, 30));
        vista2.Add(Texto("CU\u00c1NDO USARLO", Tema.Pequena, Tema.TextoSuave, x3, y3 + 214, w3, 18));
        vista2.Add(Pos(txtUso2, x3, y3 + 232, w3, 30));
        int b3 = (w3 - 24) / 3;
        vista2.Add(Pos(btnQuitar, x3, y3 + 276, b3, 36));
        vista2.Add(Pos(btnIA2, x3 + b3 + 12, y3 + 276, b3, 36));
        vista2.Add(Pos(btnGuardar2, x3 + 2 * (b3 + 12), y3 + 276, b3, 36));

        int yb = Math.Max(y1, y + 640);
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
        lst.SelectedIndexChanged += delegate { Seleccionar(); };
        lst.DoubleClick += delegate { if (actual2 != null) try { Process.Start(biblioteca.Completa(actual2)); } catch { } };
        btnGuardar2.Click += delegate { GuardarEdicion(); };
        btnIA2.Click += delegate { if (actual2 != null) ConIA(biblioteca.Completa(actual2), false); };
        btnQuitar.Click += delegate { Quitar(); };
        btnIATodos.Click += delegate { DescribirTodos(); };
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
            d.Description = origen ? "Carpeta con los archivos por revisar (tambi\u00e9n subcarpetas)" : "Carpeta de memes (la que usa vegas-cut)";
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
            try { biblioteca = BibliotecaMemes.Cargar(config.CarpetaMemes); } catch (Exception ex) { Estado("No se pudo leer el \u00edndice: " + ex.Message, true); }
        if (biblioteca != null)
        {
            try { if (biblioteca.Escanear() > 0) biblioteca.Guardar(); } catch { }
            if (Directory.Exists(config.CarpetaMemesEntrada))
                try { pendientes = biblioteca.Pendientes(config.CarpetaMemesEntrada); } catch (Exception ex) { Estado("No se pudo leer la carpeta: " + ex.Message, true); }
        }
        indice = 0;
        cargando = true;
        cmbTag.Items.Clear();
        cmbTag.Items.Add("Todos los tags");
        if (biblioteca != null)
            foreach (string t in biblioteca.TagsUsados()) cmbTag.Items.Add(t + " (" + biblioteca.Filtrar(t, "").Count + ")");
        cmbTag.SelectedIndex = 0;
        cargando = false;
    }

    // ================================================== clasificar

    string Actual() { return indice >= 0 && indice < pendientes.Count ? pendientes[indice] : null; }

    void MostrarTarjeta()
    {
        string f = Actual();
        lblCuenta.Text = biblioteca == null ? "Elige tu carpeta de memes (arriba a la derecha) y la carpeta por revisar." :
            (pendientes.Count == 0 ? "No queda nada por revisar." : pendientes.Count + " por revisar") +
            " \u00b7 esta vez: " + guardados + " memes, " + descartados + " descartados \u00b7 biblioteca: " + biblioteca.Memes.Count;
        tarjeta.Visible = f != null;
        foreach (Control c in new Control[] { btnNo, btnSi, btnSaltar, btnAbrir }) c.Enabled = f != null && !etiquetando;
        btnDeshacer.Enabled = hechas.Count > 0 && !etiquetando;
        if (f == null) { lblArchivo.Text = ""; tarjeta.Visor.Detener(); return; }
        long bytes = 0;
        try { bytes = new FileInfo(f).Length; } catch { }
        string rel = config.CarpetaMemesEntrada.Length > 0 && f.StartsWith(config.CarpetaMemesEntrada, StringComparison.OrdinalIgnoreCase)
            ? f.Substring(config.CarpetaMemesEntrada.Length).TrimStart('\\', '/') : f;
        lblArchivo.Text = rel + "\n" + BibliotecaMemes.TipoDe(f) + " \u00b7 " + (bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, bytes / 1024) + " KB");
        tarjeta.Visor.Mostrar(f);
        string ext = Path.GetExtension(f).ToLowerInvariant();
        if (ext == ".webm" || ext == ".mkv") Estado("Este formato no se ve en la tarjeta: \u00abAbrir aparte\u00bb.", false);
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
            Estado("\u2716 " + Path.GetFileName(f), false);
        }
        else
        {
            hechas.Add(new Accion { Tipo = "saltar", Archivo = f, Indice = indice });
            pendientes.RemoveAt(indice);
            pendientes.Add(f);
            Estado("\u2191 Saltado: vuelve al final.", false);
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
            Meme mm = biblioteca.Agregar(f, txtDesc1.Text, tags1.Elegidos, txtUso1.Text);
            biblioteca.Guardar();
            hechas.Add(new Accion { Tipo = "si", Archivo = f, Indice = indice, Meme = mm });
            pendientes.RemoveAt(indice);
            if (indice >= pendientes.Count) indice = 0;
            guardados++;
            Estado("\u2714 \u00ab" + mm.Ruta + "\u00bb con " + mm.Tags.Count + " tags" + (mm.Descrito ? "." : " (sin descripci\u00f3n: descr\u00edbelo despu\u00e9s en Biblioteca)."), false);
        }
        catch (Exception ex) { Estado("No se pudo mover: " + ex.Message + " (\u00bfest\u00e1 abierto en otro programa?)", true); MostrarTarjeta(); return; }
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
            Estado("\u21ba Deshecho: " + Path.GetFileName(a.Archivo), false);
        }
        catch (Exception ex) { Estado("No se pudo deshacer: " + ex.Message, true); }
        MostrarTarjeta();
    }

    // ================================================== biblioteca

    void LlenarLista()
    {
        lst.Items.Clear();
        if (biblioteca == null) { lblInfo.Text = "Elige tu carpeta de memes (arriba a la derecha)."; return; }
        string tag = cmbTag.SelectedIndex > 0 ? ((string)cmbTag.SelectedItem).Substring(0, ((string)cmbTag.SelectedItem).LastIndexOf(" (")) : "";
        List<Meme> l = biblioteca.Filtrar(tag, txtBuscar.Text);
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
        lblInfo.Text = l.Count + " de " + biblioteca.Memes.Count + " memes" + (sin > 0 ? " \u00b7 " + sin + " sin describir (en gris; vegas-cut no los usa hasta que tengan descripci\u00f3n)" : "");
        btnIATodos.Enabled = sin > 0 && !trabajando && !String.IsNullOrEmpty(config.GeminiClave);
        btnIATodos.Text = "Describir con IA los que faltan (" + sin + ")";
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
        visor2.Mostrar(actual2 != null ? biblioteca.Completa(actual2) : null);
    }

    void GuardarEdicion()
    {
        if (actual2 == null) return;
        tags2.Agregar(txtTag2.Text); txtTag2.Text = "";
        actual2.Tags = new List<string>(tags2.Elegidos);
        actual2.Descripcion = txtDesc2.Text.Trim();
        actual2.Uso = txtUso2.Text.Trim();
        try { biblioteca.Guardar(); Estado("\u2714 Guardado.", false); } catch (Exception ex) { Estado("No se pudo guardar: " + ex.Message, true); }
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
        if (MessageBox.Show(this, "\u00bfQuitar \u00ab" + actual2.Ruta + "\u00bb de la biblioteca y mandar el archivo a la Papelera de reciclaje?", "Memes",
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
            Estado("\u2714 Quitado (est\u00e1 en la Papelera).", false);
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
        string msg = "Archivo: " + Path.GetFileName(f) + " (carpeta: " + Path.GetFileName(Path.GetDirectoryName(f)) + ")" + (adj == null ? " \u2014 sin adjunto" : "");
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        btnIA1.Enabled = btnIA2.Enabled = btnGuardar1.Enabled = btnGuardar2.Enabled = false;
        Estado("Gemini est\u00e1 " + (adj != null ? "viendo" : "leyendo el nombre de") + " " + Path.GetFileName(f) + "\u2026", false);
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
                        if (desc.Length == 0) { Estado("Gemini no sabe qu\u00e9 es: escr\u00edbelo t\u00fa.", true); return; }
                        CampoTexto d = clasificando ? txtDesc1 : txtDesc2, u = clasificando ? txtUso1 : txtUso2;
                        PanelTags pt = clasificando ? tags1 : tags2;
                        if (d.Text.Trim().Length == 0) d.Text = desc;
                        if (u.Text.Trim().Length == 0) u.Text = uso;
                        pt.Agregar(tg);
                        pt.Llenar(Tags());
                        Estado("\u2714 Revisa lo que puso Gemini y corrige lo que haga falta.", false);
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
                try { BeginInvoke((MethodInvoker)delegate { Estado("Gemini est\u00e1 viendo " + (ii + 1) + "\u2013" + (ii + lote.Count) + " de " + falta.Count + "\u2026", false); }); } catch { }
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
                    Estado((error != null ? "Se cort\u00f3 (" + error + "). " : "\u2714 ") + hechos + " de " + falta.Count + " descritos. Rev\u00edsalos.", error != null);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }
}

// ---- src/comun/Memes.cs ----

// =====================================================================
// Memes: una carpeta de imagenes, videos y sonidos con lo que es cada uno
// (descripcion, tags, cuando usarlo) y cuando se uso, para ponerlos en el
// capitulo con ritmo y sin repetirlos entre videos.
// =====================================================================

public class UsoMeme
{
    public string Capitulo = "", Fecha = "";
}

public class Meme
{
    public string Ruta = "";          // relativa a la carpeta
    public string Tipo = "imagen";    // imagen, gif, video, sonido
    public string Descripcion = "", Uso = "";
    public List<string> Tags = new List<string>();
    public List<UsoMeme> Usos = new List<UsoMeme>();
    public bool Descrito { get { return Descripcion.Trim().Length > 0; } }
    public string Nombre { get { return Path.GetFileNameWithoutExtension(Ruta); } }
}

public class BibliotecaMemes
{
    public const string NombreIndice = "memes-indice.json";
    public static readonly string[] Imagenes = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    public static readonly string[] Gifs = { ".gif" };
    public static readonly string[] Videos = { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".m4v" };
    public static readonly string[] Sonidos = { ".mp3", ".wav", ".ogg", ".m4a", ".flac" };

    public string Carpeta = "";
    public List<Meme> Memes = new List<Meme>();
    // Archivos que se revisaron y no son memes (rutas completas), para no volver a mostrarlos.
    public List<string> Descartados = new List<string>();

    public static string TipoDe(string ruta)
    {
        string e = Path.GetExtension(ruta).ToLowerInvariant();
        if (Array.IndexOf(Imagenes, e) >= 0) return "imagen";
        if (Array.IndexOf(Gifs, e) >= 0) return "gif";
        if (Array.IndexOf(Videos, e) >= 0) return "video";
        if (Array.IndexOf(Sonidos, e) >= 0) return "sonido";
        return "";
    }

    public string Completa(Meme m) { return Path.Combine(Carpeta, m.Ruta); }

    public Meme Buscar(string ruta)
    {
        foreach (Meme m in Memes) if (String.Equals(m.Ruta, ruta, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    // Lee la carpeta (y subcarpetas). Lo que ya estaba descrito se conserva; lo que ya no existe se quita.
    // Las subcarpetas cuentan como tags (\u00abReacciones/Risa\u00bb \u2192 reacciones, risa).
    public int Escanear()
    {
        List<Meme> nuevos = new List<Meme>();
        int agregados = 0;
        string raiz = Carpeta.TrimEnd('\\', '/');
        foreach (string f in Directory.GetFiles(Carpeta, "*", SearchOption.AllDirectories))
        {
            string tipo = TipoDe(f);
            if (tipo.Length == 0) continue;
            string rel = f.Substring(raiz.Length).TrimStart('\\', '/');
            Meme m = Buscar(rel);
            if (m == null)
            {
                m = new Meme { Ruta = rel, Tipo = tipo };
                string dir = Path.GetDirectoryName(rel) ?? "";
                foreach (string parte in dir.Split('\\', '/'))
                    if (parte.Trim().Length > 0 && !m.Tags.Contains(parte.Trim().ToLowerInvariant())) m.Tags.Add(parte.Trim().ToLowerInvariant());
                agregados++;
            }
            nuevos.Add(m);
        }
        nuevos.Sort(delegate (Meme a, Meme b) { return String.Compare(a.Ruta, b.Ruta, StringComparison.OrdinalIgnoreCase); });
        Memes = nuevos;
        return agregados;
    }

    // ---------------------------------------------------------- guardar

    public void Guardar()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-memes";
        List<object> l = new List<object>();
        foreach (Meme m in Memes)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["ruta"] = m.Ruta; x["tipo"] = m.Tipo; x["descripcion"] = m.Descripcion; x["uso"] = m.Uso;
            x["tags"] = new List<object>(m.Tags.ToArray());
            List<object> us = new List<object>();
            foreach (UsoMeme u in m.Usos)
            {
                Dictionary<string, object> y = new Dictionary<string, object>();
                y["capitulo"] = u.Capitulo; y["fecha"] = u.Fecha;
                us.Add(y);
            }
            x["usos"] = us;
            l.Add(x);
        }
        d["memes"] = l;
        d["descartados"] = new List<object>(Descartados.ToArray());
        File.WriteAllText(Path.Combine(Carpeta, NombreIndice), Json.Escribir(d), new UTF8Encoding(false));
    }

    public static BibliotecaMemes Cargar(string carpeta)
    {
        BibliotecaMemes b = new BibliotecaMemes { Carpeta = carpeta };
        string ruta = Path.Combine(carpeta, NombreIndice);
        if (!File.Exists(ruta)) return b;
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        foreach (object x in Json.Lista(o, "memes"))
        {
            Meme m = new Meme();
            m.Ruta = Json.Texto(x, "ruta"); m.Tipo = Json.Texto(x, "tipo"); m.Descripcion = Json.Texto(x, "descripcion"); m.Uso = Json.Texto(x, "uso");
            if (m.Tipo.Length == 0) m.Tipo = TipoDe(m.Ruta);
            foreach (object y in Json.Lista(x, "tags")) if (y is string) m.Tags.Add((string)y);
            foreach (object y in Json.Lista(x, "usos")) m.Usos.Add(new UsoMeme { Capitulo = Json.Texto(y, "capitulo"), Fecha = Json.Texto(y, "fecha") });
            if (m.Ruta.Length > 0) b.Memes.Add(m);
        }
        foreach (object y in Json.Lista(o, "descartados")) if (y is string) b.Descartados.Add((string)y);
        return b;
    }

    // ------------------------------------------- clasificar (programa aparte)

    // Archivos de "origen" que faltan revisar: los que son de un tipo que sirve y no se descartaron.
    public List<string> Pendientes(string origen)
    {
        List<string> r = new List<string>();
        if (String.IsNullOrEmpty(origen) || !Directory.Exists(origen)) return r;
        foreach (string f in Directory.GetFiles(origen, "*", SearchOption.AllDirectories))
        {
            if (TipoDe(f).Length == 0) continue;
            if (Descartados.Exists(delegate (string x) { return String.Equals(x, f, StringComparison.OrdinalIgnoreCase); })) continue;
            if (Carpeta.Length > 0 && f.StartsWith(Carpeta.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            r.Add(f);
        }
        r.Sort(StringComparer.OrdinalIgnoreCase);
        return r;
    }

    // Lleva el archivo a la carpeta de memes (con otro nombre si ya hay uno igual) y lo agrega al indice.
    public Meme Agregar(string archivo, string descripcion, List<string> tags, string uso)
    {
        Directory.CreateDirectory(Carpeta);
        string nombre = Path.GetFileNameWithoutExtension(archivo), ext = Path.GetExtension(archivo);
        string destino = Path.Combine(Carpeta, nombre + ext);
        for (int i = 2; File.Exists(destino); i++) destino = Path.Combine(Carpeta, nombre + " (" + i + ")" + ext);
        File.Move(archivo, destino);
        Meme m = new Meme { Ruta = Path.GetFileName(destino), Tipo = TipoDe(destino), Descripcion = (descripcion ?? "").Trim(), Uso = (uso ?? "").Trim() };
        foreach (string t in tags) { string k = t.Trim().ToLowerInvariant(); if (k.Length > 0 && !m.Tags.Contains(k)) m.Tags.Add(k); }
        Memes.Add(m);
        return m;
    }

    // Deshacer un \u00abes meme\u00bb: el archivo vuelve a donde estaba y sale del indice.
    public void DeshacerAgregar(Meme m, string original)
    {
        string actual = Completa(m);
        if (File.Exists(actual) && !File.Exists(original))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(original));
            File.Move(actual, original);
        }
        Memes.Remove(m);
    }

    public void QuitarDescartado(string archivo)
    {
        Descartados.RemoveAll(delegate (string x) { return String.Equals(x, archivo, StringComparison.OrdinalIgnoreCase); });
    }

    // Lo que tiene un tag (o todos si es ""), filtrado por texto en nombre, descripcion, uso y tags.
    public List<Meme> Filtrar(string tag, string texto)
    {
        string q = (texto ?? "").Trim().ToLowerInvariant();
        return Memes.FindAll(delegate (Meme m)
        {
            if (!String.IsNullOrEmpty(tag) && !m.Tags.Contains(tag)) return false;
            if (q.Length == 0) return true;
            return (m.Ruta + " " + m.Descripcion + " " + m.Uso + " " + String.Join(" ", m.Tags.ToArray())).ToLowerInvariant().Contains(q);
        });
    }

    public void Descartar(string archivo)
    {
        if (!Descartados.Exists(delegate (string x) { return String.Equals(x, archivo, StringComparison.OrdinalIgnoreCase); })) Descartados.Add(archivo);
    }

    // Tags de la biblioteca, de los mas usados a los menos.
    public List<string> TagsUsados()
    {
        Dictionary<string, int> n = new Dictionary<string, int>();
        foreach (Meme m in Memes) foreach (string t in m.Tags) { int c; n.TryGetValue(t, out c); n[t] = c + 1; }
        List<string> r = new List<string>(n.Keys);
        r.Sort(delegate (string a, string b) { int c = n[b].CompareTo(n[a]); return c != 0 ? c : String.Compare(a, b, StringComparison.CurrentCultureIgnoreCase); });
        return r;
    }

    // Lo que se le manda a Gemini de un archivo: miniatura (imagenes y gifs) o el archivo
    // entero (videos y sonidos de hasta "maxMb"). null si no se puede.
    public static KeyValuePair<string, byte[]>? Adjunto(string ruta, double maxMb)
    {
        string tipo = TipoDe(ruta), ext = Path.GetExtension(ruta).ToLowerInvariant();
        if (tipo == "imagen" || tipo == "gif")
        {
            byte[] b = Miniatura(ruta, 384);
            return b != null ? new KeyValuePair<string, byte[]>("image/jpeg", b) : (KeyValuePair<string, byte[]>?)null;
        }
        string mime = ext == ".mp4" || ext == ".m4v" ? "video/mp4" : ext == ".webm" ? "video/webm" : ext == ".mov" ? "video/quicktime" :
                      ext == ".avi" ? "video/x-msvideo" : ext == ".mkv" ? "video/x-matroska" : ext == ".mp3" ? "audio/mpeg" : ext == ".wav" ? "audio/wav" :
                      ext == ".ogg" ? "audio/ogg" : ext == ".m4a" ? "audio/mp4" : ext == ".flac" ? "audio/flac" : "";
        try
        {
            if (mime.Length == 0 || new FileInfo(ruta).Length > maxMb * 1024 * 1024) return null;
            return new KeyValuePair<string, byte[]>(mime, File.ReadAllBytes(ruta));
        }
        catch { return null; }
    }

    public static string InstruccionesUno(List<string> tagsExistentes)
    {
        return "Ayudas a un editor de videos de YouTube a ordenar su biblioteca de memes. La usa en videos de todo tipo (gameplays, " +
               "videos ensayo, vlogs, explicaciones, reacciones...), as\u00ed que describe el meme EN S\u00cd, no lo ates a un juego ni a un tema. Te paso UN archivo " +
               "que el editor marc\u00f3 como meme (adjunto: imagen, video o sonido; si no va adjunto, solo tienes su nombre). Di:\n" +
               "- \"descripcion\": qu\u00e9 es y qu\u00e9 se ve/oye, en una frase (si es un meme conocido, cu\u00e1l).\n" +
               "- \"tags\": 2 a 5 (emoci\u00f3n, reacci\u00f3n, tipo de chiste, de d\u00f3nde sale\u2026), generales y no de un juego. USA PRIMERO los que ya existen si alguno sirve: " + String.Join(", ", tagsExistentes.ToArray()) + ".\n" +
               "- \"uso\": en qu\u00e9 situaci\u00f3n de CUALQUIER video queda bien (la emoci\u00f3n o el momento, no un juego concreto).\n" +
               "Si no sabes qu\u00e9 es, deja la descripci\u00f3n vac\u00eda (mejor nada que inventar).\n" +
               "Responde SOLO con JSON: {\"descripcion\": \"...\", \"tags\": [\"...\"], \"uso\": \"...\"}";
    }

    // La respuesta para un archivo: descripcion, uso y los tags separados por comas.
    public static string RespuestaUno(string json, out string descripcion, out string uso)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        descripcion = Json.Texto(o, "descripcion").Trim();
        uso = Json.Texto(o, "uso").Trim();
        List<string> t = new List<string>();
        foreach (object x in Json.Lista(o, "tags")) { string k = (x as string ?? "").Trim().ToLowerInvariant(); if (k.Length > 0 && !t.Contains(k)) t.Add(k); }
        return String.Join(", ", t.ToArray());
    }

    public static List<string> LeerTags(string texto)
    {
        List<string> r = new List<string>();
        foreach (string t in (texto ?? "").Split(',', ';', '#'))
        {
            string x = t.Trim().ToLowerInvariant();
            if (x.Length > 0 && !r.Contains(x)) r.Add(x);
        }
        return r;
    }

    // ------------------------------------------------ describir con IA

    // Miniatura JPEG para mandarla a Gemini (null si no se pudo leer).
    public static byte[] Miniatura(string ruta, int lado)
    {
        try
        {
            using (Image im = Image.FromFile(ruta))
            {
                double f = Math.Min(1.0, (double)lado / Math.Max(im.Width, im.Height));
                int w = Math.Max(1, (int)(im.Width * f)), h = Math.Max(1, (int)(im.Height * f));
                using (Bitmap b = new Bitmap(w, h))
                {
                    using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.White); g.DrawImage(im, 0, 0, w, h); }
                    using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Jpeg); return ms.ToArray(); }
                }
            }
        }
        catch { return null; }
    }

    public List<Meme> SinDescribir()
    {
        return Memes.FindAll(delegate (Meme m) { return !m.Descrito; });
    }

    public static string InstruccionesDescribir()
    {
        return "Ayudas a un editor de videos de YouTube a ordenar su biblioteca de memes. La usa en videos de todo tipo (gameplays, " +
               "videos ensayo, vlogs, explicaciones, reacciones...), as\u00ed que describe el meme EN S\u00cd, no lo ates a un juego ni a un tema. Te paso memes de " +
               "la carpeta del editor: van adjuntos en el mismo orden que la lista (las im\u00e1genes como miniatura; los videos y sonidos " +
               "cortos enteros); de los que no tienen adjunto solo tienes el nombre y la carpeta. Para cada uno di:\n" +
               "- \"descripcion\": qu\u00e9 es y qu\u00e9 se ve/oye, en una frase (si es un meme conocido, cu\u00e1l).\n" +
               "- \"tags\": 3 a 6 palabras (emoci\u00f3n, reacci\u00f3n, tipo de chiste, de d\u00f3nde sale\u2026), generales y no de un juego.\n" +
               "- \"uso\": en qu\u00e9 situaci\u00f3n de CUALQUIER video queda bien (tras un fallo, una sorpresa, un chiste, algo obvio, una " +
               "contradicci\u00f3n, un dato absurdo, una victoria...), no atado a un juego concreto.\n" +
               "Si de uno sin adjunto no sabes qu\u00e9 es por su nombre, no lo pongas (mejor nada que inventar).\n" +
               "Responde SOLO con JSON: {\"memes\": [{\"id\": n, \"descripcion\": \"...\", \"tags\": [\"...\"], \"uso\": \"...\"}]}";
    }

    // Mensaje para un lote; "imagenes" recibe las miniaturas en el orden de la lista.
    public string MensajeDescribir(List<Meme> lote, List<KeyValuePair<string, byte[]>> imagenes)
    {
        StringBuilder sb = new StringBuilder("MEMES [id] tipo | archivo | carpeta | (adjunto n)\n");
        int n = 0;
        for (int i = 0; i < lote.Count; i++)
        {
            Meme m = lote[i];
            string adj = "";
            // Imagenes como miniatura; videos y sonidos cortos enteros (sin pasar ~15 MB por pedido).
            long total = 0;
            foreach (KeyValuePair<string, byte[]> x in imagenes) total += x.Value.Length;
            KeyValuePair<string, byte[]>? a = Adjunto(Completa(m), Math.Max(0, 15 - total / 1048576.0));
            if (a != null) { imagenes.Add(a.Value); adj = " | adjunto " + (++n); }
            sb.Append("[" + i + "] " + m.Tipo + " | " + m.Nombre + " | " + (Path.GetDirectoryName(m.Ruta) ?? "") + adj + "\n");
        }
        return sb.ToString();
    }

    public static int AplicarDescripciones(List<Meme> lote, string json)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        int n = 0;
        foreach (object x in Json.Lista(o, "memes"))
        {
            int id = (int)Json.Numero(x, "id", -1);
            if (id < 0 || id >= lote.Count) continue;
            string d = Json.Texto(x, "descripcion").Trim();
            if (d.Length == 0) continue;
            Meme m = lote[id];
            m.Descripcion = d;
            m.Uso = Json.Texto(x, "uso").Trim();
            foreach (object t in Json.Lista(x, "tags"))
            {
                string k = (t as string ?? "").Trim().ToLowerInvariant();
                if (k.Length > 0 && !m.Tags.Contains(k)) m.Tags.Add(k);
            }
            n++;
        }
        return n;
    }

    // ------------------------------------------------ historial de uso

    public void RegistrarUso(Meme m, string capitulo)
    {
        m.Usos.Add(new UsoMeme { Capitulo = capitulo, Fecha = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }

    // Capitulos distintos en los que se uso algo, del mas reciente al mas viejo.
    public List<string> CapitulosRecientes()
    {
        List<UsoMeme> todos = new List<UsoMeme>();
        foreach (Meme m in Memes) todos.AddRange(m.Usos);
        todos.Sort(delegate (UsoMeme a, UsoMeme b) { return String.Compare(b.Fecha, a.Fecha, StringComparison.Ordinal); });
        List<string> r = new List<string>();
        foreach (UsoMeme u in todos) if (!r.Contains(u.Capitulo)) r.Add(u.Capitulo);
        return r;
    }

    // Usado en alguno de los ultimos "n" capitulos (sin contar el actual).
    public bool UsadoHacePoco(Meme m, int n, string actual)
    {
        List<string> rec = CapitulosRecientes();
        rec.Remove(actual);
        if (rec.Count > n) rec = rec.GetRange(0, n);
        foreach (UsoMeme u in m.Usos) if (rec.Contains(u.Capitulo)) return true;
        return false;
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
    public string CarpetaMemesEntrada = "";      // carpeta con los archivos por revisar (ClasificarMemes)

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
        return Generar(clave, modelo, instrucciones, mensaje, json, null);
    }

    // Con imagenes (tipo MIME y bytes), por ejemplo para describir memes.
    public static string Generar(string clave, string modelo, string instrucciones, string mensaje, bool json,
                                 List<KeyValuePair<string, byte[]>> imagenes)
    {
        Dictionary<string, object> cuerpo = new Dictionary<string, object>();
        if (!String.IsNullOrEmpty(instrucciones))
            cuerpo["systemInstruction"] = Partes(instrucciones, null);
        Dictionary<string, object> usuario = Partes(mensaje, "user");
        if (imagenes != null)
            foreach (KeyValuePair<string, byte[]> im in imagenes)
            {
                Dictionary<string, object> dato = new Dictionary<string, object>();
                dato["mime_type"] = im.Key; dato["data"] = Convert.ToBase64String(im.Value);
                Dictionary<string, object> parte = new Dictionary<string, object>();
                parte["inline_data"] = dato;
                ((List<object>)usuario["parts"]).Add(parte);
            }
        cuerpo["contents"] = new List<object> { usuario };
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
