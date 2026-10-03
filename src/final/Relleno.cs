using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Rellenar los huecos de la pista de musica con temas de la biblioteca.
class VentanaRelleno : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion trans;
    readonly Configuracion config = Configuracion.Cargar();
    SerieProyecto serie;
    MusicaSerie musica;
    BibliotecaMusica biblioteca;
    List<ArchivoMusica> candidatos = new List<ArchivoMusica>();
    List<HuecoMusica> huecos = new List<HuecoMusica>();
    bool trabajando, cargando;

    Etiqueta lblInfo, lblEstado;
    CampoNumero numMin = new CampoNumero();
    Boton btnBuscar = new Boton("Buscar huecos", EstiloBoton.Secundario);
    Boton btnIA = new Boton("Elegir temas con IA", EstiloBoton.Primario);
    Lista lst = new Lista();
    Boton btnColocar = new Boton("Colocar", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaRelleno(Vegas vegas, Transcripcion trans) : base("Rellenar la música", 1000)
    {
        this.vegas = vegas; this.trans = trans;
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Rellenar la música", "Pone un tema en cada hueco de la pista de música según lo que pasa ahí. Lo que ya está no se toca.");
        int y = 92;
        lblInfo = Texto("", Tema.Normal, Tema.Texto, m, y, w - 540, 40);
        Texto("HUECOS DE AL MENOS", Tema.Pequena, Tema.TextoSuave, m + w - 530, y + 8, 130, 18);
        numMin.Sufijo = "s"; numMin.Minimo = 2; numMin.Maximo = 120; numMin.Paso = 1;
        Pos(numMin, m + w - 400, y, 80, 32);
        cargando = true; numMin.Valor = 8; cargando = false;
        Pos(btnBuscar, m + w - 310, y, 130, 32);
        Pos(btnIA, m + w - 170, y, 170, 32);
        y += 46;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Hueco", 130);
        lst.Columns.Add("Dura", 56);
        lst.Columns.Add("Bloque", 120);
        lst.Columns.Add("Qué pasa", w - 130 - 56 - 120 - 260 - sb);
        lst.Columns.Add("Tema", 260);
        Pos(lst, m, y, w, 400);
        y += 410;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 300, 40);
        Pos(btnColocar, m + w - 290, y, 150, 40);
        Pos(btnCerrar, m + w - 130, y, 130, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        try
        {
            Serie.DelProyecto(CopiaBase.Original(vegas.Project.FilePath ?? ""), out serie);
            musica = serie != null ? serie.Musica : null;
            if (musica != null && musica.Carpeta.Length > 0 && Directory.Exists(musica.Carpeta)) biblioteca = BibliotecaMusica.Cargar(musica.Carpeta);
        }
        catch { biblioteca = null; }
        if (biblioteca != null) candidatos = MusicaSerie.Candidatos(biblioteca);
        if (candidatos.Count > 300) candidatos = candidatos.GetRange(0, 300);
        lblInfo.Text = biblioteca == null ? "Sin biblioteca de música: elígela en Series → Música… (con la serie de este capítulo)."
                                          : "Biblioteca: " + candidatos.Count + " temas que sirven" + (trans == null ? " · sin transcripción (la IA solo ve los bloques)" : "");

        btnBuscar.Click += delegate { Buscar(); };
        btnIA.Click += delegate { ConIA(); };
        btnColocar.Click += delegate { Colocar(); };
        btnCerrar.Click += delegate { Close(); };
        lst.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando && e.Item.Tag != null) ((HuecoMusica)e.Item.Tag).Elegido = e.Item.Checked; };
        lst.DoubleClick += delegate
        {
            if (lst.SelectedIndices.Count == 0) return;
            HuecoMusica h = (HuecoMusica)lst.Items[lst.SelectedIndices[0]].Tag;
            try
            {
                vegas.Transport.CursorPosition = Timecode.FromMilliseconds(h.Inicio * 1000);
                vegas.Transport.SelectionStart = Timecode.FromMilliseconds(h.Inicio * 1000);
                vegas.Transport.SelectionLength = Timecode.FromMilliseconds(h.Duracion * 1000);
            }
            catch { }
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };
        Buscar();
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Habilitar()
    {
        btnBuscar.Enabled = !trabajando;
        btnIA.Enabled = !trabajando && huecos.Count > 0 && candidatos.Count > 0 && !String.IsNullOrEmpty(config.GeminiClave);
        btnColocar.Enabled = !trabajando && huecos.Exists(delegate (HuecoMusica h) { return h.Elegido && !h.Silencio && (h.Tema >= 0 || h.Personaje.Length > 0); });
        btnCerrar.Enabled = !trabajando;
    }

    void Buscar()
    {
        huecos = LogicaRelleno.Huecos(vegas.Project, trans, numMin.Valor);
        Llenar();
        double total = 0;
        foreach (HuecoMusica h in huecos) total += h.Duracion;
        Estado(huecos.Count == 0 ? "No hay huecos de " + numMin.Valor + " s o más en la pista de música." :
               huecos.Count + " huecos (" + Formato.Tiempo(total) + " sin música). Desmarca los que quieras en silencio y pulsa «Elegir temas con IA».", false);
        Habilitar();
    }

    void Llenar()
    {
        cargando = true;
        lst.Items.Clear();
        foreach (HuecoMusica h in huecos)
        {
            ListViewItem it = new ListViewItem(Formato.Tiempo(h.Inicio) + "–" + Formato.Tiempo(h.Fin));
            it.SubItems.Add(Math.Round(h.Duracion) + " s");
            it.SubItems.Add(h.Bloque);
            it.SubItems.Add(h.Dicho.Length > 0 ? h.Dicho : "(nadie habla)");
            string tema = LogicaRelleno.Nombre(h, candidatos);
            it.SubItems.Add(tema + (h.Motivo.Length > 0 ? " — " + h.Motivo : ""));
            it.Checked = h.Elegido;
            if (h.Silencio) it.ForeColor = Tema.TextoSuave;
            it.Tag = h;
            lst.Items.Add(it);
        }
        cargando = false;
    }

    void ConIA()
    {
        List<HuecoMusica> pedir = huecos.FindAll(delegate (HuecoMusica h) { return h.Elegido; });
        if (pedir.Count == 0) { Estado("Marca al menos un hueco.", true); return; }
        string instr = LogicaRelleno.Instrucciones();
        string msg = LogicaRelleno.Mensaje(pedir, candidatos, musica, LogicaRelleno.YaSuena(vegas.Project));
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        Habilitar();
        Estado("Gemini está eligiendo un tema para cada hueco…", false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(clave, modelo, instr, msg, true); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    if (error != null) { Estado("Gemini: " + error, true); Habilitar(); return; }
                    try
                    {
                        int n = LogicaRelleno.Leer(resp, pedir, candidatos, musica);
                        Llenar();
                        Estado("✔ " + n + " huecos con tema (o silencio). Revisa, desmarca lo que no quieras y pulsa «Colocar».", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                    Habilitar();
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Colocar()
    {
        List<string> avisos = new List<string>();
        int n;
        using (UndoBlock u = new UndoBlock("Rellenar la música"))
            n = LogicaRelleno.Colocar(vegas.Project, huecos, candidatos, musica, biblioteca, avisos);
        Estado("✔ " + n + " temas colocados en «" + LogicaRelleno.PistaMusica + "» (sin balancear)." +
               (avisos.Count > 0 ? " Avisos: " + String.Join(" ", avisos.ToArray()) : ""), avisos.Count > 0);
        foreach (HuecoMusica h in huecos) if (h.Elegido && !h.Silencio && (h.Tema >= 0 || h.Personaje.Length > 0)) h.Elegido = false;
        Llenar();
        Habilitar();
    }
}
