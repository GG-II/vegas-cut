// ClasificarMemes.cs
// Programa aparte (no es un script de Vegas): revisa una carpeta de videos e
// imagenes uno por uno, marcas si es meme, le pones tags, que es y cuando
// usarlo, y se mueve a tu carpeta de memes. Lo compila ClasificarMemes.bat.
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

// ---- src/clasificar/Clasificar.cs ----

// =====================================================================
// ClasificarMemes: programa aparte (fuera de Vegas) para revisar una carpeta
// de videos e imagenes uno por uno: \u00bfes meme? \u2192 tags, que es y cuando usarlo
// \u2192 se mueve a la carpeta de memes y queda en su indice. Lo que no es se
// recuerda para no volver a mostrarlo.
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
        Application.Run(new VentanaClasificar());
    }
}

class VentanaClasificar : VentanaBase
{
    readonly Configuracion config = Configuracion.Cargar();
    BibliotecaMemes biblioteca;
    List<string> pendientes = new List<string>();
    int indice;
    int guardados, descartados;
    bool paso2, trabajando;
    List<string> tagsElegidos = new List<string>();

    Etiqueta lblOrigen, lblDestino, lblCuenta, lblArchivo, lblEstado, lblPregunta;
    Boton btnOrigen = new Boton("Elegir\u2026", EstiloBoton.Secundario), btnDestino = new Boton("Elegir\u2026", EstiloBoton.Secundario);
    Panel vista = new Panel();
    WebBrowser navegador;
    PictureBox imagen = new PictureBox();
    Boton btnAbrir = new Boton("Abrir aparte", EstiloBoton.Secundario);
    // paso 1
    Boton btnSi = new Boton("\u2714  Es meme   (S)", EstiloBoton.Primario), btnNo = new Boton("\u2716  No es, siguiente   (N)", EstiloBoton.Secundario);
    Boton btnAnterior = new Boton("\u2190 Anterior", EstiloBoton.Secundario), btnSaltar = new Boton("Saltar (decidir despu\u00e9s) \u2192", EstiloBoton.Secundario);
    // paso 2
    Etiqueta lblTags, lblDesc, lblUso;
    FlowLayoutPanel chips = new FlowLayoutPanel();
    CampoTexto txtNuevoTag = new CampoTexto(), txtDesc = new CampoTexto(), txtUso = new CampoTexto();
    Boton btnIA = new Boton("Describir con IA", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar y siguiente   (Ctrl+Enter)", EstiloBoton.Primario), btnVolver = new Boton("\u2190 Volver", EstiloBoton.Secundario);
    List<Control> controles1 = new List<Control>(), controles2 = new List<Control>();

    public VentanaClasificar() : base("Clasificar memes", 1160)
    {
        MinimizeBox = true;
        int m = Margen, w = Ancho;
        Encabezado("Clasificar memes", "Uno por uno: \u00bfes meme? \u2192 tags, qu\u00e9 es y cu\u00e1ndo usarlo \u2192 se mueve a tu carpeta de memes.");
        int y = 92;
        Texto("DE (por revisar)", Tema.Pequena, Tema.TextoSuave, m, y + 8, 110, 18);
        lblOrigen = Texto("", Tema.Normal, Tema.Texto, m + 112, y + 6, w / 2 - 210, 20);
        Pos(btnOrigen, m + w / 2 - 92, y, 80, 30);
        Texto("A (memes)", Tema.Pequena, Tema.TextoSuave, m + w / 2 + 8, y + 8, 80, 18);
        lblDestino = Texto("", Tema.Normal, Tema.Texto, m + w / 2 + 90, y + 6, w / 2 - 180, 20);
        Pos(btnDestino, m + w - 80, y, 80, 30);
        y += 40;
        lblCuenta = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 26;

        int vw = 680, vh = 400;
        vista.BackColor = Color.Black;
        Pos(vista, m, y, vw, vh);
        imagen.SizeMode = PictureBoxSizeMode.Zoom;
        imagen.BackColor = Color.Black;
        imagen.Dock = DockStyle.Fill;
        try
        {
            navegador = new WebBrowser();
            navegador.Dock = DockStyle.Fill;
            navegador.ScrollBarsEnabled = false;
            navegador.ScriptErrorsSuppressed = true;
            navegador.IsWebBrowserContextMenuEnabled = false;
            vista.Controls.Add(navegador);
        }
        catch { navegador = null; }
        vista.Controls.Add(imagen);
        lblArchivo = Texto("", Tema.Normal, Tema.Texto, m, y + vh + 8, vw - 140, 40);
        Pos(btnAbrir, m + vw - 130, y + vh + 6, 130, 30);

        int x2 = m + vw + 24, w2 = w - vw - 24;
        // paso 1
        lblPregunta = Texto("\u00bfEs un meme?", Tema.Titulo, Tema.Texto, x2, y, w2, 34);
        controles1.Add(lblPregunta);
        controles1.Add(Pos(btnSi, x2, y + 50, w2, 56));
        controles1.Add(Pos(btnNo, x2, y + 116, w2, 44));
        controles1.Add(Pos(btnSaltar, x2, y + 170, w2, 36));
        controles1.Add(Pos(btnAnterior, x2, y + 216, w2, 36));
        controles1.Add(Texto("Lo que no es meme no se mueve ni se borra: solo no se vuelve a mostrar.", Tema.Pequena, Tema.TextoSuave, x2, y + 262, w2, 36));
        // paso 2
        lblTags = Texto("TAGS (clic para elegir; escribe uno nuevo y Enter)", Tema.Pequena, Tema.TextoSuave, x2, y, w2, 18);
        controles2.Add(lblTags);
        chips.AutoScroll = true;
        chips.BackColor = Tema.Panel;
        chips.Padding = new Padding(4);
        controles2.Add(Pos(chips, x2, y + 20, w2, 150));
        controles2.Add(Pos(txtNuevoTag, x2, y + 176, w2, 32));
        lblDesc = Texto("QU\u00c9 ES", Tema.Pequena, Tema.TextoSuave, x2, y + 218, w2, 18);
        controles2.Add(lblDesc);
        controles2.Add(Pos(txtDesc, x2, y + 238, w2, 32));
        lblUso = Texto("CU\u00c1NDO USARLO", Tema.Pequena, Tema.TextoSuave, x2, y + 278, w2, 18);
        controles2.Add(lblUso);
        controles2.Add(Pos(txtUso, x2, y + 298, w2, 32));
        controles2.Add(Pos(btnIA, x2, y + 340, w2, 32));
        controles2.Add(Pos(btnVolver, x2, y + 382, 110, 40));
        controles2.Add(Pos(btnGuardar, x2 + 118, y + 382, w2 - 118, 40));

        y += vh + 56;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 36);
        ClientSize = new Size(ClientSize.Width, y + 36 + 16);

        btnOrigen.Click += delegate { Elegir(true); };
        btnDestino.Click += delegate { Elegir(false); };
        btnAbrir.Click += delegate { if (Actual() != null) try { Process.Start(Actual()); } catch { } };
        btnSi.Click += delegate { Si(); };
        btnNo.Click += delegate { No(); };
        btnSaltar.Click += delegate { if (indice < pendientes.Count - 1) { indice++; Mostrar(); } };
        btnAnterior.Click += delegate { if (indice > 0) { indice--; Mostrar(); } };
        btnVolver.Click += delegate { Paso(false); };
        btnGuardar.Click += delegate { Guardar(); };
        btnIA.Click += delegate { ConIA(); };
        txtNuevoTag.Caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            foreach (string t in BibliotecaMemes.LeerTags(txtNuevoTag.Text)) if (!tagsElegidos.Contains(t)) tagsElegidos.Add(t);
            txtNuevoTag.Text = "";
            Chips();
        };
        KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (trabajando) return;
            if (!paso2 && e.KeyCode == Keys.S) { Si(); e.Handled = true; }
            else if (!paso2 && e.KeyCode == Keys.N) { No(); e.Handled = true; }
            else if (paso2 && e.KeyCode == Keys.Enter && e.Control) { Guardar(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; else Detener(); };

        Cargar();
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    string Actual() { return indice >= 0 && indice < pendientes.Count ? pendientes[indice] : null; }

    void Elegir(bool origen)
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = origen ? "Carpeta con los archivos por revisar (se buscan tambi\u00e9n en subcarpetas)" : "Carpeta de memes (adonde se mueven los que s\u00ed son)";
            string actual = origen ? config.CarpetaMemesEntrada : config.CarpetaMemes;
            if (actual.Length > 0 && Directory.Exists(actual)) d.SelectedPath = actual;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (origen) config.CarpetaMemesEntrada = d.SelectedPath; else config.CarpetaMemes = d.SelectedPath;
        }
        try { config.Guardar(); } catch { }
        Cargar();
    }

    void Cargar()
    {
        lblOrigen.Text = config.CarpetaMemesEntrada.Length > 0 ? config.CarpetaMemesEntrada : "(elige la carpeta por revisar)";
        lblDestino.Text = config.CarpetaMemes.Length > 0 ? config.CarpetaMemes : "(elige tu carpeta de memes)";
        biblioteca = null;
        pendientes.Clear();
        if (config.CarpetaMemes.Length > 0 && Directory.Exists(config.CarpetaMemes))
            try { biblioteca = BibliotecaMemes.Cargar(config.CarpetaMemes); } catch (Exception ex) { Estado("No se pudo leer el \u00edndice: " + ex.Message, true); }
        if (biblioteca != null && Directory.Exists(config.CarpetaMemesEntrada))
            try { pendientes = biblioteca.Pendientes(config.CarpetaMemesEntrada); } catch (Exception ex) { Estado("No se pudo leer la carpeta: " + ex.Message, true); }
        indice = 0;
        Paso(false);
        Mostrar();
    }

    void Cuenta()
    {
        lblCuenta.Text = biblioteca == null ? "Elige las dos carpetas para empezar." :
            (pendientes.Count == 0 ? "No queda nada por revisar." : (indice + 1) + " de " + pendientes.Count + " por revisar") +
            " \u00b7 esta vez: " + guardados + " memes guardados, " + descartados + " descartados \u00b7 la biblioteca tiene " + biblioteca.Memes.Count + " memes";
    }

    void Detener()
    {
        try { if (navegador != null) navegador.Navigate("about:blank"); } catch { }
        if (imagen.Image != null) { Image i = imagen.Image; imagen.Image = null; i.Dispose(); }
    }

    // Vista previa: imagenes en el cuadro; gifs, videos y sonidos en el navegador (se repiten solos).
    void Mostrar()
    {
        Cuenta();
        Detener();
        string f = Actual();
        foreach (Control c in new Control[] { btnSi, btnNo, btnSaltar, btnAnterior, btnAbrir }) c.Enabled = f != null;
        if (f == null) { lblArchivo.Text = ""; imagen.Visible = true; return; }
        string tipo = BibliotecaMemes.TipoDe(f);
        long bytes = 0;
        try { bytes = new FileInfo(f).Length; } catch { }
        string rel = config.CarpetaMemesEntrada.Length > 0 && f.StartsWith(config.CarpetaMemesEntrada, StringComparison.OrdinalIgnoreCase)
            ? f.Substring(config.CarpetaMemesEntrada.Length).TrimStart('\\', '/') : f;
        lblArchivo.Text = rel + "\n" + tipo + " \u00b7 " + (bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, bytes / 1024) + " KB");
        if (tipo == "imagen" || navegador == null)
        {
            imagen.Visible = true;
            if (navegador != null) navegador.Visible = false;
            try { using (Image im = Image.FromFile(f)) imagen.Image = new Bitmap(im); } catch { imagen.Image = null; }
            return;
        }
        imagen.Visible = false;
        navegador.Visible = true;
        string url = new Uri(f).AbsoluteUri;
        string cuerpo = tipo == "gif" ? "<img src=\"" + url + "\" style=\"max-width:100%;max-height:100%\">" :
                        tipo == "sonido" ? "<audio src=\"" + url + "\" autoplay loop controls></audio>" :
                        "<video src=\"" + url + "\" autoplay loop controls style=\"width:100%;height:100%\"></video>";
        string html = "<!DOCTYPE html><html><head><meta http-equiv=\"X-UA-Compatible\" content=\"IE=edge\"><meta charset=\"utf-8\"></head>" +
                      "<body style=\"margin:0;background:#000;height:100%;overflow:hidden;display:flex;align-items:center;justify-content:center\">" +
                      "<div style=\"width:100%;height:100%;text-align:center\">" + cuerpo + "</div></body></html>";
        try
        {
            string tmp = Path.Combine(Path.GetTempPath(), "vegas-cut-vista.html");
            File.WriteAllText(tmp, html, new UTF8Encoding(false));
            navegador.Navigate(tmp);
        }
        catch { }
        if (Path.GetExtension(f).ToLowerInvariant() == ".webm" || Path.GetExtension(f).ToLowerInvariant() == ".mkv")
            Estado("Este formato no se ve aqu\u00ed: pulsa \u00abAbrir aparte\u00bb.", false);
    }

    void Paso(bool segundo)
    {
        paso2 = segundo;
        foreach (Control c in controles1) c.Visible = !segundo;
        foreach (Control c in controles2) c.Visible = segundo;
        btnIA.Enabled = segundo && !String.IsNullOrEmpty(config.GeminiClave);
        if (segundo) { Chips(); txtDesc.Focus(); }
    }

    void Chips()
    {
        chips.SuspendLayout();
        chips.Controls.Clear();
        List<string> todos = biblioteca != null ? biblioteca.TagsUsados() : new List<string>();
        foreach (string t in tagsElegidos) if (!todos.Contains(t)) todos.Insert(0, t);
        foreach (string t in todos)
        {
            Boton b = new Boton(t, EstiloBoton.Chip);
            b.Font = Tema.Pequena;
            b.Size = new Size(TextRenderer.MeasureText(t, Tema.Pequena).Width + 26, 26);
            b.Margin = new Padding(3);
            b.Activo = tagsElegidos.Contains(t);
            string tag = t;
            b.Click += delegate
            {
                if (tagsElegidos.Contains(tag)) tagsElegidos.Remove(tag); else tagsElegidos.Add(tag);
                b.Activo = tagsElegidos.Contains(tag);
            };
            chips.Controls.Add(b);
        }
        chips.ResumeLayout();
    }

    void Si()
    {
        if (Actual() == null) return;
        tagsElegidos = new List<string>();
        txtDesc.Text = ""; txtUso.Text = "";
        Paso(true);
    }

    void No()
    {
        string f = Actual();
        if (f == null || biblioteca == null) return;
        biblioteca.Descartar(f);
        try { biblioteca.Guardar(); } catch (Exception ex) { Estado("No se pudo guardar: " + ex.Message, true); return; }
        descartados++;
        pendientes.RemoveAt(indice);
        if (indice >= pendientes.Count) indice = Math.Max(0, pendientes.Count - 1);
        Estado("Descartado: " + Path.GetFileName(f), false);
        Mostrar();
    }

    void Guardar()
    {
        string f = Actual();
        if (f == null || biblioteca == null) return;
        foreach (string t in BibliotecaMemes.LeerTags(txtNuevoTag.Text)) if (!tagsElegidos.Contains(t)) tagsElegidos.Add(t);
        txtNuevoTag.Text = "";
        if (txtDesc.Text.Trim().Length == 0) { Estado("Escribe qu\u00e9 es (o pulsa \u00abDescribir con IA\u00bb): sin descripci\u00f3n no se usa.", true); txtDesc.Focus(); return; }
        Detener();   // que el visor suelte el archivo antes de moverlo
        try
        {
            Meme m = biblioteca.Agregar(f, txtDesc.Text, tagsElegidos, txtUso.Text);
            biblioteca.Guardar();
            guardados++;
            pendientes.RemoveAt(indice);
            if (indice >= pendientes.Count) indice = Math.Max(0, pendientes.Count - 1);
            Estado("\u2714 Guardado como \u00ab" + m.Ruta + "\u00bb con " + m.Tags.Count + " tags.", false);
        }
        catch (Exception ex) { Estado("No se pudo mover: " + ex.Message + " (\u00bfest\u00e1 abierto en otro programa?)", true); Mostrar(); return; }
        Paso(false);
        Mostrar();
    }

    void ConIA()
    {
        string f = Actual();
        if (f == null) return;
        KeyValuePair<string, byte[]>? adj = BibliotecaMemes.Adjunto(f, 18);
        List<KeyValuePair<string, byte[]>> adjuntos = new List<KeyValuePair<string, byte[]>>();
        if (adj != null) adjuntos.Add(adj.Value);
        string instr = BibliotecaMemes.InstruccionesUno(biblioteca != null ? biblioteca.TagsUsados() : new List<string>());
        string msg = "Archivo: " + Path.GetFileName(f) + " (carpeta: " + Path.GetFileName(Path.GetDirectoryName(f)) + ")" + (adj == null ? " \u2014 sin adjunto" : "");
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        btnIA.Enabled = btnGuardar.Enabled = false;
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
                    btnIA.Enabled = btnGuardar.Enabled = true;
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        string desc, uso;
                        string tags = BibliotecaMemes.RespuestaUno(resp, out desc, out uso);
                        if (desc.Length == 0) { Estado("Gemini no sabe qu\u00e9 es: escr\u00edbelo t\u00fa.", true); return; }
                        if (txtDesc.Text.Trim().Length == 0) txtDesc.Text = desc;
                        if (txtUso.Text.Trim().Length == 0) txtUso.Text = uso;
                        foreach (string t in BibliotecaMemes.LeerTags(tags)) if (!tagsElegidos.Contains(t)) tagsElegidos.Add(t);
                        Chips();
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
        return "Eres el editor de una serie de YouTube de Minecraft con amigos (estilo anime de JoJo, mucho humor). Te paso UN archivo " +
               "que el editor marc\u00f3 como meme (adjunto: imagen, video o sonido; si no va adjunto, solo tienes su nombre). Di:\n" +
               "- \"descripcion\": qu\u00e9 es y qu\u00e9 se ve/oye, en una frase (si es un meme conocido, cu\u00e1l).\n" +
               "- \"tags\": 2 a 5. USA PRIMERO los que ya existen si alguno sirve: " + String.Join(", ", tagsExistentes.ToArray()) + ".\n" +
               "- \"uso\": en qu\u00e9 momento de un gameplay queda bien.\n" +
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
        return "Eres el editor de una serie de YouTube de Minecraft con amigos (estilo anime de JoJo, mucho humor). Te paso memes de " +
               "la carpeta del editor: van adjuntos en el mismo orden que la lista (las im\u00e1genes como miniatura; los videos y sonidos " +
               "cortos enteros); de los que no tienen adjunto solo tienes el nombre y la carpeta. Para cada uno di:\n" +
               "- \"descripcion\": qu\u00e9 es y qu\u00e9 se ve/oye, en una frase (si es un meme conocido, cu\u00e1l).\n" +
               "- \"tags\": 3 a 6 palabras (emoci\u00f3n, reacci\u00f3n, tipo de chiste\u2026).\n" +
               "- \"uso\": en qu\u00e9 momento de un gameplay queda bien (tras un fallo, una muerte, una sorpresa, un chiste, una victoria...).\n" +
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
