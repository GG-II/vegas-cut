using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// La biblioteca de memes: indexar la carpeta, describir con IA y corregir a mano.
class VentanaBibliotecaMemes : VentanaBase
{
    readonly Configuracion config;
    public BibliotecaMemes Biblioteca;
    bool trabajando, cargando;
    Meme actual;

    Etiqueta lblCarpeta, lblInfo, lblEstado;
    Boton btnCarpeta = new Boton("Elegir carpeta…", EstiloBoton.Secundario);
    Boton btnBuscar = new Boton("Buscar archivos", EstiloBoton.Secundario);
    Boton btnIA = new Boton("Describir con IA", EstiloBoton.Primario);
    Lista lst = new Lista();
    CampoTexto txtDesc = new CampoTexto(), txtTags = new CampoTexto(), txtUso = new CampoTexto();
    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaBibliotecaMemes(Configuracion config) : base("Biblioteca de memes", 1060)
    {
        this.config = config;
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Biblioteca de memes", "Imágenes, gifs, videos y sonidos con lo que es cada uno y cuándo usarlo. Doble clic para verlo.");
        int y = 92;
        lblCarpeta = Texto("", Tema.Normal, Tema.Texto, m, y + 6, w - 490, 20);
        Pos(btnCarpeta, m + w - 480, y, 150, 32);
        Pos(btnBuscar, m + w - 322, y, 150, 32);
        Pos(btnIA, m + w - 162, y, 162, 32);
        y += 38;
        lblInfo = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 24;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.CheckBoxes = false;
        lst.Columns.Add("Archivo", 230);
        lst.Columns.Add("Tipo", 60);
        lst.Columns.Add("Qué es", w - 230 - 60 - 200 - 60 - sb);
        lst.Columns.Add("Tags", 200);
        lst.Columns.Add("Usado", 60);
        Pos(lst, m, y, w, 330);
        y += 340;
        int mitad = (w - 16) / 2;
        Texto("QUÉ ES", Tema.Pequena, Tema.TextoSuave, m, y, mitad, 18);
        Texto("TAGS (separados por comas)", Tema.Pequena, Tema.TextoSuave, m + mitad + 16, y, mitad, 18);
        Pos(txtDesc, m, y + 20, mitad, 34);
        Pos(txtTags, m + mitad + 16, y + 20, mitad, 34);
        y += 62;
        Texto("CUÁNDO USARLO", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        Pos(txtUso, m, y + 20, w, 34);
        y += 66;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 300, 40);
        Pos(btnGuardar, m + w - 290, y, 150, 40);
        Pos(btnCerrar, m + w - 130, y, 130, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        if (config.CarpetaMemes.Length > 0 && Directory.Exists(config.CarpetaMemes))
            try { Biblioteca = BibliotecaMemes.Cargar(config.CarpetaMemes); } catch { Biblioteca = null; }

        btnCarpeta.Click += delegate { ElegirCarpeta(); };
        btnBuscar.Click += delegate { Buscar(); };
        btnIA.Click += delegate { Describir(); };
        btnGuardar.Click += delegate { LeerEdicion(); Guardar(); };
        btnCerrar.Click += delegate { LeerEdicion(); Close(); };
        lst.SelectedIndexChanged += delegate
        {
            LeerEdicion();
            actual = lst.SelectedIndices.Count > 0 ? (Meme)lst.Items[lst.SelectedIndices[0]].Tag : null;
            cargando = true;
            txtDesc.Text = actual != null ? actual.Descripcion : "";
            txtTags.Text = actual != null ? String.Join(", ", actual.Tags.ToArray()) : "";
            txtUso.Text = actual != null ? actual.Uso : "";
            cargando = false;
        };
        lst.DoubleClick += delegate
        {
            if (actual == null) return;
            try { Process.Start(Biblioteca.Completa(actual)); } catch { }
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };
        Mostrar();
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void LeerEdicion()
    {
        if (cargando || actual == null) return;
        actual.Descripcion = txtDesc.Text.Trim();
        actual.Tags = BibliotecaMemes.LeerTags(txtTags.Text);
        actual.Uso = txtUso.Text.Trim();
        foreach (ListViewItem it in lst.Items)
            if (it.Tag == actual) { it.SubItems[2].Text = actual.Descripcion; it.SubItems[3].Text = String.Join(", ", actual.Tags.ToArray()); }
    }

    void Mostrar()
    {
        lblCarpeta.Text = Biblioteca != null ? "Carpeta: " + Biblioteca.Carpeta : "Elige la carpeta de tus memes (se buscan también en subcarpetas).";
        btnBuscar.Enabled = Biblioteca != null && !trabajando;
        btnIA.Enabled = Biblioteca != null && !trabajando && !String.IsNullOrEmpty(config.GeminiClave) && Biblioteca.SinDescribir().Count > 0;
        lst.Items.Clear();
        if (Biblioteca == null) { lblInfo.Text = ""; return; }
        foreach (Meme m in Biblioteca.Memes)
        {
            ListViewItem it = new ListViewItem(m.Ruta);
            it.SubItems.Add(m.Tipo);
            it.SubItems.Add(m.Descripcion);
            it.SubItems.Add(String.Join(", ", m.Tags.ToArray()));
            it.SubItems.Add(m.Usos.Count > 0 ? m.Usos.Count.ToString() : "");
            if (!m.Descrito) it.ForeColor = Tema.TextoSuave;
            it.Tag = m;
            lst.Items.Add(it);
        }
        int sin = Biblioteca.SinDescribir().Count;
        lblInfo.Text = Biblioteca.Memes.Count + " memes · " + (Biblioteca.Memes.Count - sin) + " descritos" +
                       (sin > 0 ? " · " + sin + " sin describir (no se usan hasta que tengan descripción: «Describir con IA» o escríbela abajo)" : "");
    }

    void ElegirCarpeta()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta de memes (imágenes, gifs, videos y sonidos; también subcarpetas)";
            if (config.CarpetaMemes.Length > 0 && Directory.Exists(config.CarpetaMemes)) d.SelectedPath = config.CarpetaMemes;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            config.CarpetaMemes = d.SelectedPath;
        }
        try { config.Guardar(); } catch { }
        try { Biblioteca = BibliotecaMemes.Cargar(config.CarpetaMemes); } catch { Biblioteca = new BibliotecaMemes { Carpeta = config.CarpetaMemes }; }
        Buscar();
    }

    void Buscar()
    {
        if (Biblioteca == null) return;
        try
        {
            int n = Biblioteca.Escanear();
            Biblioteca.Guardar();
            Estado("✔ " + n + " nuevos (las subcarpetas cuentan como tags). Índice en " + BibliotecaMemes.NombreIndice + ".", false);
        }
        catch (Exception ex) { Estado("No se pudo leer la carpeta: " + ex.Message, true); }
        Mostrar();
    }

    void Guardar()
    {
        if (Biblioteca == null) return;
        try { Biblioteca.Guardar(); Estado("✔ Guardado.", false); }
        catch (Exception ex) { Estado("No se pudo guardar: " + ex.Message, true); }
    }

    // En lotes de 20: las imagenes van como miniaturas; videos y sonidos, por su nombre.
    void Describir()
    {
        LeerEdicion();
        List<Meme> falta = Biblioteca.SinDescribir();
        if (falta.Count == 0) return;
        BibliotecaMemes b = Biblioteca;
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        Mostrar();
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
                    List<KeyValuePair<string, byte[]>> imgs = new List<KeyValuePair<string, byte[]>>();
                    string msg = b.MensajeDescribir(lote, imgs);
                    hechos += BibliotecaMemes.AplicarDescripciones(lote, Gemini.Generar(clave, modelo, BibliotecaMemes.InstruccionesDescribir(), msg, true, imgs));
                }
                catch (Exception ex) { error = ex.Message; }
            }
            try { b.Guardar(); } catch { }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    Mostrar();
                    Estado((error != null ? "Se cortó (" + error + "). " : "✔ ") + hechos + " de " + falta.Count + " descritos. Revísalos y corrige lo que haga falta.", error != null);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }
}

// Poner memes en el capitulo ya editado.
class VentanaMemes : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion trans;
    readonly Configuracion config = Configuracion.Cargar();
    readonly string capitulo;
    BibliotecaMemes biblioteca;
    List<Meme> candidatos = new List<Meme>();
    List<PropuestaMeme> propuestas = new List<PropuestaMeme>();
    bool trabajando, cargando;
    double duracion;

    Etiqueta lblInfo, lblEstado;
    Boton btnBiblioteca = new Boton("Biblioteca…", EstiloBoton.Secundario);
    CampoNumero numCada = new CampoNumero(), numEntre = new CampoNumero(), numMaxSin = new CampoNumero(), numRecientes = new CampoNumero();
    Boton btnIA = new Boton("Elegir con IA", EstiloBoton.Primario);
    Lista lst = new Lista();
    Boton btnColocar = new Boton("Colocar", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaMemes(Vegas vegas, Transcripcion trans) : base("Memes", 1000)
    {
        this.vegas = vegas; this.trans = trans;
        StartPosition = FormStartPosition.CenterParent;
        string veg = vegas.Project.FilePath ?? "";
        capitulo = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(CopiaBase.Original(veg)), @"\s+CAP$", "");
        int m = Margen, w = Ancho;
        Encabezado("Memes", "Gemini elige dónde queda un meme y cuál: por ritmo, repartidos y sin repetir. Para cualquier video, no solo gameplays.");
        int y = 92;
        lblInfo = Texto("", Tema.Normal, Tema.Texto, m, y, w - 170, 40);
        Pos(btnBiblioteca, m + w - 160, y, 160, 32);
        y += 46;
        Texto("UNO CADA ~", Tema.Pequena, Tema.TextoSuave, m, y + 8, 80, 18);
        numCada.Sufijo = "s"; numCada.Minimo = 20; numCada.Maximo = 600; numCada.Paso = 5;
        Pos(numCada, m + 82, y, 90, 32);
        Texto("SEPARADOS AL MENOS", Tema.Pequena, Tema.TextoSuave, m + 196, y + 8, 130, 18);
        numEntre.Sufijo = "s"; numEntre.Minimo = 5; numEntre.Maximo = 300; numEntre.Paso = 5;
        Pos(numEntre, m + 330, y, 90, 32);
        Texto("NUNCA MÁS DE", Tema.Pequena, Tema.TextoSuave, m + 444, y + 8, 84, 18);
        numMaxSin.Sufijo = "s"; numMaxSin.Minimo = 0; numMaxSin.Maximo = 900; numMaxSin.Paso = 15;
        Pos(numMaxSin, m + 530, y, 90, 32);
        Texto("SIN MEMES", Tema.Pequena, Tema.TextoSuave, m + 626, y + 8, 70, 18);
        Pos(btnIA, m + w - 160, y, 160, 32);
        y += 42;
        Texto("NO REPETIR LOS USADOS EN TUS ÚLTIMOS", Tema.Pequena, Tema.TextoSuave, m, y + 8, 240, 18);
        numRecientes.Sufijo = ""; numRecientes.Minimo = 0; numRecientes.Maximo = 50; numRecientes.Paso = 1;
        Pos(numRecientes, m + 244, y, 90, 32);
        Texto("VIDEOS", Tema.Pequena, Tema.TextoSuave, m + 340, y + 8, 60, 18);
        cargando = true; numCada.Valor = 75; numEntre.Valor = 25; numMaxSin.Valor = 180; numRecientes.Valor = 3; cargando = false;
        y += 44;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Momento", 80);
        lst.Columns.Add("Meme", 260);
        lst.Columns.Add("Qué se dice", w - 80 - 260 - 260 - sb);
        lst.Columns.Add("Por qué", 260);
        Pos(lst, m, y, w, 380);
        y += 390;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 300, 40);
        Pos(btnColocar, m + w - 290, y, 150, 40);
        Pos(btnCerrar, m + w - 130, y, 130, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        foreach (Track t in vegas.Project.Tracks)
            if (!t.IsAudio()) foreach (TrackEvent e in t.Events) duracion = Math.Max(duracion, e.End.ToMilliseconds() / 1000.0);
        Cargar();

        btnBiblioteca.Click += delegate
        {
            using (VentanaBibliotecaMemes v = new VentanaBibliotecaMemes(config)) v.ShowDialog(this);
            Cargar();
        };
        numRecientes.Cambio += delegate { if (!cargando) Cargar(); };
        btnIA.Click += delegate { ConIA(); };
        btnColocar.Click += delegate { Colocar(); };
        btnCerrar.Click += delegate { Close(); };
        lst.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando && e.Item.Tag != null) { ((PropuestaMeme)e.Item.Tag).Elegido = e.Item.Checked; Habilitar(); } };
        lst.DoubleClick += delegate
        {
            if (lst.SelectedIndices.Count == 0) return;
            PropuestaMeme pm = (PropuestaMeme)lst.Items[lst.SelectedIndices[0]].Tag;
            try { vegas.Transport.CursorPosition = Timecode.FromMilliseconds(pm.En * 1000); } catch { }
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Cargar()
    {
        biblioteca = null;
        if (config.CarpetaMemes.Length > 0 && Directory.Exists(config.CarpetaMemes))
            try { biblioteca = BibliotecaMemes.Cargar(config.CarpetaMemes); } catch { biblioteca = null; }
        candidatos = LogicaMemes.Candidatos(biblioteca, numRecientes.Valor, capitulo);
        int descritos = biblioteca == null ? 0 : biblioteca.Memes.Count - biblioteca.SinDescribir().Count;
        lblInfo.Text = biblioteca == null ? "Sin biblioteca de memes: pulsa «Biblioteca…» y elige tu carpeta." :
            candidatos.Count + " memes disponibles de " + descritos + " descritos" + (descritos > candidatos.Count ? " (" + (descritos - candidatos.Count) +
            " se usaron en tus últimos " + numRecientes.Valor + " videos)" : "") + " · video «" + capitulo + "»" +
            (biblioteca.Memes.Count > descritos ? "\n" + (biblioteca.Memes.Count - descritos) + " sin descripción no se usan: descríbelos en «Biblioteca…»." : "");
        Habilitar();
    }

    void Habilitar()
    {
        btnIA.Enabled = !trabajando && candidatos.Count > 0 && !String.IsNullOrEmpty(config.GeminiClave);
        btnColocar.Enabled = !trabajando && propuestas.Exists(delegate (PropuestaMeme p) { return p.Elegido; });
        btnCerrar.Enabled = btnBiblioteca.Enabled = !trabajando;
    }

    void Llenar()
    {
        cargando = true;
        lst.Items.Clear();
        foreach (PropuestaMeme pm in propuestas)
        {
            Meme mm = candidatos[pm.Id];
            ListViewItem it = new ListViewItem(Formato.Tiempo(pm.En));
            it.SubItems.Add(mm.Nombre + " (" + mm.Tipo + ")");
            it.SubItems.Add(pm.Dicho);
            it.SubItems.Add(pm.Motivo);
            it.Checked = pm.Elegido;
            it.Tag = pm;
            lst.Items.Add(it);
        }
        cargando = false;
    }

    void ConIA()
    {
        string instr = LogicaMemes.Instrucciones(numCada.Valor, numEntre.Valor, numMaxSin.Valor);
        string msg = LogicaMemes.Mensaje(trans, vegas.Project, candidatos, duracion);
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        List<Rango> ocupado = LogicaMemes.Ocupado(vegas.Project);
        int entre = numEntre.Valor, maxSin = numMaxSin.Valor;
        trabajando = true;
        Habilitar();
        Estado("Gemini está viendo el video y tus memes…", false);
        Thread hilo = new Thread(delegate ()
        {
            string error = null;
            List<PropuestaMeme> r = null;
            List<Rango> huecos = new List<Rango>();
            int primera = 0;
            try
            {
                r = LogicaMemes.Leer(Gemini.Generar(clave, modelo, instr, msg, true), candidatos, duracion, entre, ocupado, trans);
                primera = r.Count;
                huecos = LogicaMemes.HuecosLargos(r, duracion, maxSin, ocupado);
                // Quedaron tramos largos sin nada: una segunda vuelta solo para esos tramos.
                if (huecos.Count > 0)
                {
                    int n = huecos.Count;
                    try { BeginInvoke((MethodInvoker)delegate { Estado("Quedaron " + n + " tramos de más de " + maxSin + " s sin memes: Gemini los está mirando…", false); }); } catch { }
                    try
                    {
                        string resp2 = Gemini.Generar(clave, modelo, instr + LogicaMemes.InstruccionesHuecos(entre),
                                                      LogicaMemes.MensajeHuecos(msg, r, huecos), true);
                        r = LogicaMemes.LeerHuecos(resp2, r, huecos, candidatos, duracion, entre, ocupado, trans);
                        huecos = LogicaMemes.HuecosLargos(r, duracion, maxSin, ocupado);
                    }
                    catch (Exception ex)
                    {
                        // Si falla la segunda vuelta quedan los de la primera (salvo que se cancele).
                        ErrorGemini eg = ex as ErrorGemini;
                        if (eg != null && eg.Cancelado) throw;
                    }
                }
            }
            catch (ErrorGemini ex) { error = ex.Message; }
            catch (Exception ex) { error = "La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo."; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    if (error != null) { Estado(error, true); Habilitar(); return; }
                    propuestas = r;
                    Llenar();
                    string extra = r.Count > primera ? " (" + (r.Count - primera) + " en la segunda vuelta)" : "";
                    string quedan = "";
                    if (huecos.Count > 0)
                    {
                        List<string> l = new List<string>();
                        foreach (Rango h in huecos) l.Add(Formato.Tiempo(h.Inicio) + "–" + Formato.Tiempo(h.Fin));
                        quedan = " Sin memes a propósito (serio o sin uno que encaje): " + String.Join(", ", l.ToArray()) + ".";
                    }
                    Estado("✔ " + r.Count + " memes propuestos" + extra + ", uno cada ~" + (r.Count > 0 ? Math.Round(duracion / r.Count) : 0) +
                           " s. Doble clic para ir al momento; desmarca los que no quieras y pulsa «Colocar»." + quedan, false);
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
        using (UndoBlock u = new UndoBlock("Memes"))
            n = LogicaMemes.Colocar(vegas.Project, biblioteca, propuestas, candidatos, capitulo, avisos);
        try { biblioteca.Guardar(); } catch (Exception ex) { avisos.Add("No se guardó el historial: " + ex.Message); }
        Estado("✔ " + n + " memes en «" + LogicaMemes.PistaVideo + "» (los sonidos en «" + LogicaMemes.PistaAudio + "»). Quedan anotados para no repetirlos." +
               (avisos.Count > 0 ? " Avisos: " + String.Join(" ", avisos.ToArray()) : ""), avisos.Count > 0);
        propuestas.Clear();
        Llenar();
        Habilitar();
    }
}
