using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

// =====================================================================
// ClasificarMemes: programa aparte (fuera de Vegas) para revisar una carpeta
// de videos e imagenes uno por uno: ¿es meme? → tags, que es y cuando usarlo
// → se mueve a la carpeta de memes y queda en su indice. Lo que no es se
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
    Boton btnOrigen = new Boton("Elegir…", EstiloBoton.Secundario), btnDestino = new Boton("Elegir…", EstiloBoton.Secundario);
    Panel vista = new Panel();
    WebBrowser navegador;
    PictureBox imagen = new PictureBox();
    Boton btnAbrir = new Boton("Abrir aparte", EstiloBoton.Secundario);
    // paso 1
    Boton btnSi = new Boton("✔  Es meme   (S)", EstiloBoton.Primario), btnNo = new Boton("✖  No es, siguiente   (N)", EstiloBoton.Secundario);
    Boton btnAnterior = new Boton("← Anterior", EstiloBoton.Secundario), btnSaltar = new Boton("Saltar (decidir después) →", EstiloBoton.Secundario);
    // paso 2
    Etiqueta lblTags, lblDesc, lblUso;
    FlowLayoutPanel chips = new FlowLayoutPanel();
    CampoTexto txtNuevoTag = new CampoTexto(), txtDesc = new CampoTexto(), txtUso = new CampoTexto();
    Boton btnIA = new Boton("Describir con IA", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar y siguiente   (Ctrl+Enter)", EstiloBoton.Primario), btnVolver = new Boton("← Volver", EstiloBoton.Secundario);
    List<Control> controles1 = new List<Control>(), controles2 = new List<Control>();

    public VentanaClasificar() : base("Clasificar memes", 1160)
    {
        MinimizeBox = true;
        int m = Margen, w = Ancho;
        Encabezado("Clasificar memes", "Uno por uno: ¿es meme? → tags, qué es y cuándo usarlo → se mueve a tu carpeta de memes.");
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
        lblPregunta = Texto("¿Es un meme?", Tema.Titulo, Tema.Texto, x2, y, w2, 34);
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
        lblDesc = Texto("QUÉ ES", Tema.Pequena, Tema.TextoSuave, x2, y + 218, w2, 18);
        controles2.Add(lblDesc);
        controles2.Add(Pos(txtDesc, x2, y + 238, w2, 32));
        lblUso = Texto("CUÁNDO USARLO", Tema.Pequena, Tema.TextoSuave, x2, y + 278, w2, 18);
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
            d.Description = origen ? "Carpeta con los archivos por revisar (se buscan también en subcarpetas)" : "Carpeta de memes (adonde se mueven los que sí son)";
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
            try { biblioteca = BibliotecaMemes.Cargar(config.CarpetaMemes); } catch (Exception ex) { Estado("No se pudo leer el índice: " + ex.Message, true); }
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
            " · esta vez: " + guardados + " memes guardados, " + descartados + " descartados · la biblioteca tiene " + biblioteca.Memes.Count + " memes";
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
        lblArchivo.Text = rel + "\n" + tipo + " · " + (bytes >= 1048576 ? (bytes / 1048576.0).ToString("0.0") + " MB" : Math.Max(1, bytes / 1024) + " KB");
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
            Estado("Este formato no se ve aquí: pulsa «Abrir aparte».", false);
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
        if (txtDesc.Text.Trim().Length == 0) { Estado("Escribe qué es (o pulsa «Describir con IA»): sin descripción no se usa.", true); txtDesc.Focus(); return; }
        Detener();   // que el visor suelte el archivo antes de moverlo
        try
        {
            Meme m = biblioteca.Agregar(f, txtDesc.Text, tagsElegidos, txtUso.Text);
            biblioteca.Guardar();
            guardados++;
            pendientes.RemoveAt(indice);
            if (indice >= pendientes.Count) indice = Math.Max(0, pendientes.Count - 1);
            Estado("✔ Guardado como «" + m.Ruta + "» con " + m.Tags.Count + " tags.", false);
        }
        catch (Exception ex) { Estado("No se pudo mover: " + ex.Message + " (¿está abierto en otro programa?)", true); Mostrar(); return; }
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
        string msg = "Archivo: " + Path.GetFileName(f) + " (carpeta: " + Path.GetFileName(Path.GetDirectoryName(f)) + ")" + (adj == null ? " — sin adjunto" : "");
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        btnIA.Enabled = btnGuardar.Enabled = false;
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
                    btnIA.Enabled = btnGuardar.Enabled = true;
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        string desc, uso;
                        string tags = BibliotecaMemes.RespuestaUno(resp, out desc, out uso);
                        if (desc.Length == 0) { Estado("Gemini no sabe qué es: escríbelo tú.", true); return; }
                        if (txtDesc.Text.Trim().Length == 0) txtDesc.Text = desc;
                        if (txtUso.Text.Trim().Length == 0) txtUso.Text = uso;
                        foreach (string t in BibliotecaMemes.LeerTags(tags)) if (!tagsElegidos.Contains(t)) tagsElegidos.Add(t);
                        Chips();
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
}
