using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        using (VentanaConfiguracion v = new VentanaConfiguracion()) v.ShowDialog();
    }
}

class VentanaConfiguracion : VentanaBase
{
    Configuracion config = Configuracion.Cargar();

    CampoTexto txtClave = new CampoTexto();
    Boton btnVer = new Boton("Ver", EstiloBoton.Secundario);
    Combo comboModelo = new Combo(true);
    Boton btnProbarGemini = new Boton("Probar y listar modelos", EstiloBoton.Secundario);
    Etiqueta lblGemini;

    CampoTexto txtExe = new CampoTexto();
    Boton btnBuscar = new Boton("Buscar…", EstiloBoton.Secundario);
    Combo comboWhisper = new Combo(true);
    Segmentado segDispositivo = new Segmentado(new string[] { "Tarjeta", "Procesador" });
    Segmentado segPrecision = new Segmentado(new string[] { "int8", "float16", "auto" });
    CampoTexto txtIdioma = new CampoTexto();
    CampoTexto txtExtra = new CampoTexto();
    Boton btnProbarWhisper = new Boton("Probar Whisper", EstiloBoton.Secundario);
    Etiqueta lblWhisper;

    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);

    static readonly string[] Precisiones = { "int8", "float16", "auto" };

    public VentanaConfiguracion() : base("Configurar", 720)
    {
        int m = Margen, w = Ancho;
        Encabezado("Configurar vegas-cut", "Claves y programas que usan las herramientas de transcripción e IA.");

        // ---------------- Gemini
        int y = 96;
        Texto("Gemini (IA)", Tema.Seccion, Tema.Texto, m, y, 300, 22);
        Texto("Consigue una clave gratis en aistudio.google.com/apikey. Se guarda cifrada con tu usuario de Windows; " +
              "solo se envía texto (transcripción y niveles), nunca el audio.",
              Tema.Pequena, Tema.TextoSuave, m, y + 24, w, 32);
        y += 62;
        Texto("Clave de API", Tema.Negrita, Tema.Texto, m, y, 200, 20);
        txtClave.Oculto = true;
        txtClave.Text = config.GeminiClave;
        Pos(txtClave, m, y + 22, w - 70, 34);
        Pos(btnVer, m + w - 62, y + 22, 62, 34);
        y += 66;
        Texto("Modelo", Tema.Negrita, Tema.Texto, m, y, 200, 20);
        comboModelo.Items.AddRange(new object[] { "gemini-flash-latest", "gemini-flash-lite-latest", "gemini-pro-latest" });
        comboModelo.Text = config.GeminiModelo;
        Pos(comboModelo, m, y + 24, 320, 30);
        Pos(btnProbarGemini, m + 330, y + 22, 200, 34);
        lblGemini = Texto("Flash es rápido y barato; Pro razona mejor pero cuesta más. “Probar” trae los modelos de tu cuenta.",
              Tema.Pequena, Tema.TextoSuave, m, y + 62, w, 32);
        y += 104;

        Panel sep = new Panel();
        sep.BackColor = Tema.Borde;
        Pos(sep, m, y, w, 1);
        y += 18;

        // ---------------- Whisper
        Texto("Transcripción (Faster-Whisper-XXL)", Tema.Seccion, Tema.Texto, m, y, 400, 22);
        Texto("Corre en tu PC. La guía de instalación está en docs/instalar-whisper.md del repositorio.",
              Tema.Pequena, Tema.TextoSuave, m, y + 24, w, 18);
        y += 50;
        Texto("Programa (faster-whisper-xxl.exe)", Tema.Negrita, Tema.Texto, m, y, 300, 20);
        txtExe.Text = config.WhisperExe;
        Pos(txtExe, m, y + 22, w - 110, 34);
        Pos(btnBuscar, m + w - 102, y + 22, 102, 34);
        y += 66;

        int col = (w - 24) / 3;
        Texto("Modelo", Tema.Negrita, Tema.Texto, m, y, col, 20);
        comboWhisper.Items.AddRange(new object[] { "large-v3-turbo", "large-v3", "medium", "small" });
        comboWhisper.Text = config.WhisperModelo;
        Pos(comboWhisper, m, y + 24, col, 30);
        Texto("Dónde corre", Tema.Negrita, Tema.Texto, m + col + 12, y, col, 20);
        segDispositivo.Seleccion = config.WhisperDispositivo == "cpu" ? 1 : 0;
        Pos(segDispositivo, m + col + 12, y + 22, col, 34);
        Texto("Precisión", Tema.Negrita, Tema.Texto, m + (col + 12) * 2, y, col, 20);
        segPrecision.Seleccion = Math.Max(0, Array.IndexOf(Precisiones, config.WhisperPrecision));
        Pos(segPrecision, m + (col + 12) * 2, y + 22, col, 34);
        y += 64;
        Texto("Con una GTX 1650 (4 GB): large-v3-turbo + Tarjeta + int8. Si falta memoria, usa medium.",
              Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 28;

        Texto("Idioma", Tema.Negrita, Tema.Texto, m, y, 100, 20);
        txtIdioma.Text = config.Idioma;
        Pos(txtIdioma, m, y + 22, 90, 34);
        Texto("Opciones extra (avanzado)", Tema.Negrita, Tema.Texto, m + 106, y, 300, 20);
        txtExtra.Text = config.WhisperExtra;
        Pos(txtExtra, m + 106, y + 22, w - 106 - 160, 34);
        Pos(btnProbarWhisper, m + w - 150, y + 22, 150, 34);
        y += 64;
        lblWhisper = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 32);
        y += 44;

        Pos(btnCancelar, m + w - 300, y, 110, 40);
        Pos(btnGuardar, m + w - 180, y, 180, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        // ---------------- Eventos
        btnVer.Click += delegate { txtClave.Oculto = !txtClave.Oculto; btnVer.Text = txtClave.Oculto ? "Ver" : "Ocultar"; };
        btnBuscar.Click += delegate { BuscarExe(); };
        btnProbarGemini.Click += delegate { ProbarGemini(); };
        btnProbarWhisper.Click += delegate { ProbarWhisper(); };
        btnGuardar.Click += delegate { Guardar(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        if (!config.TieneWhisper && config.WhisperExe.Length == 0) lblWhisper.Text = "Falta elegir el programa.";
    }

    void LeerCampos()
    {
        config.GeminiClave = txtClave.Text.Trim();
        config.GeminiModelo = comboModelo.Text.Trim();
        config.WhisperExe = txtExe.Text.Trim().Trim('"');
        config.WhisperModelo = comboWhisper.Text.Trim();
        config.WhisperDispositivo = segDispositivo.Seleccion == 1 ? "cpu" : "cuda";
        config.WhisperPrecision = Precisiones[segPrecision.Seleccion];
        config.Idioma = txtIdioma.Text.Trim();
        config.WhisperExtra = txtExtra.Text.Trim();
    }

    void BuscarExe()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Elige faster-whisper-xxl.exe";
            d.Filter = "Programa (*.exe)|*.exe";
            if (File.Exists(txtExe.Text)) d.InitialDirectory = Path.GetDirectoryName(txtExe.Text);
            if (d.ShowDialog(this) == DialogResult.OK) txtExe.Text = d.FileName;
        }
    }

    // La prueba corre en otro hilo para que la ventana no se congele.
    void EnSegundoPlano(Boton boton, Etiqueta estado, string trabajando, Func<string> trabajo)
    {
        boton.Enabled = false;
        estado.ForeColor = Tema.TextoSuave;
        estado.Text = trabajando;
        Thread hilo = new Thread(delegate ()
        {
            string resultado;
            bool ok = true;
            try { resultado = trabajo(); }
            catch (Exception ex) { resultado = ex.Message; ok = false; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    boton.Enabled = true;
                    estado.ForeColor = ok ? Color.FromArgb(120, 220, 150) : Tema.Silencio;
                    estado.Text = (ok ? "✔ " : "✖ ") + resultado;
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void ProbarGemini()
    {
        LeerCampos();
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        if (clave.Length == 0) { lblGemini.ForeColor = Tema.Silencio; lblGemini.Text = "Pega primero la clave."; return; }
        List<string> modelos = null;
        EnSegundoPlano(btnProbarGemini, lblGemini, "Conectando con Gemini…", delegate ()
        {
            modelos = Gemini.ListarModelos(clave);
            string r = Gemini.Generar(clave, modelo, null, "Responde solo con la palabra OK.", false);
            BeginInvoke((MethodInvoker)delegate
            {
                string actual = comboModelo.Text;
                comboModelo.Items.Clear();
                foreach (string x in modelos) comboModelo.Items.Add(x);
                comboModelo.Text = actual;
            });
            return "Funciona. " + modelos.Count + " modelos disponibles (despliega la lista). Respuesta de " +
                   modelo + ": " + (r.Length > 40 ? r.Substring(0, 40) : r);
        });
    }

    void ProbarWhisper()
    {
        LeerCampos();
        Configuracion c = config;
        if (!c.TieneWhisper) { lblWhisper.ForeColor = Tema.Silencio; lblWhisper.Text = "No se encuentra el programa en esa ruta."; return; }
        EnSegundoPlano(btnProbarWhisper, lblWhisper, "Ejecutando Whisper…", delegate ()
        {
            ProcessStartInfo info = new ProcessStartInfo(c.WhisperExe, "--help");
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            using (Process p = Process.Start(info))
            {
                string salida = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                if (!p.WaitForExit(30000)) { try { p.Kill(); } catch { } throw new Exception("No respondió en 30 s."); }
                if (salida.IndexOf("--word_timestamps", StringComparison.OrdinalIgnoreCase) < 0)
                    throw new Exception("El programa respondió, pero no parece Faster-Whisper-XXL.");
                return "Faster-Whisper-XXL responde. El modelo se descarga solo la primera vez que transcribas.";
            }
        });
    }

    void Guardar()
    {
        LeerCampos();
        try
        {
            config.Guardar();
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo guardar: " + ex.Message, "Configurar vegas-cut");
        }
    }
}
