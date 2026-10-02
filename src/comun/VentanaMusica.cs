using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Plantilla de bloques y kit de una serie de TV: duracion de cada bloque y el
// archivo fijo de los que lo tienen (opening, re-gancho, continuara, ending).
// Sin archivo se usa un placeholder con esa duracion.
class VentanaPlantillaTV : VentanaBase
{
    public PlantillaTV Resultado;
    bool cargando;
    Lista lst = new Lista();
    CampoNumero numDur = new CampoNumero();
    Etiqueta lblDur, lblDesc, lblTotal;
    Boton btnArchivo = new Boton("Elegir archivo…", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Usar placeholder", EstiloBoton.Secundario);
    Boton btnDefecto = new Boton("Valores de partida", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);
    readonly double minimo, maximo;

    public VentanaPlantillaTV(PlantillaTV p, ReglasRitmo reglas) : base("Plantilla y kit", 860)
    {
        StartPosition = FormStartPosition.CenterParent;
        Resultado = p.Copia();
        minimo = reglas.DuracionMin * 60; maximo = reglas.DuracionMax * 60;
        int m = Margen, w = Ancho;
        Encabezado("Plantilla y kit", "Bloques de cada capítulo. Los del kit usan tu archivo; sin archivo, un placeholder de esa duración.");
        int y = 92;
        lst.CheckBoxes = false;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Bloque", 170);
        lst.Columns.Add("Qué es", 90);
        lst.Columns.Add("Duración", 120);
        lst.Columns.Add("Archivo", w - 170 - 90 - 120 - sb);
        Pos(lst, m, y, w, 250);
        y += 258;
        lblDesc = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 34);
        y += 40;
        lblDur = Texto("DURACIÓN", Tema.Pequena, Tema.TextoSuave, m, y, 160, 18);
        Pos(numDur, m, y + 18, 150, 32);
        numDur.Minimo = 0; numDur.Maximo = 600; numDur.Paso = 1;
        Pos(btnArchivo, m + 166, y + 18, 150, 32);
        Pos(btnQuitar, m + 324, y + 18, 150, 32);
        Pos(btnDefecto, m + w - 160, y + 18, 160, 32);
        y += 60;
        lblTotal = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 290, 40);
        Pos(btnCancelar, m + w - 280, y, 120, 40);
        Pos(btnGuardar, m + w - 150, y, 150, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        lst.SelectedIndexChanged += delegate { Elegido(); };
        numDur.Cambio += delegate
        {
            BloqueTV b = Actual();
            if (b == null || cargando) return;
            if (b.Tipo == "contenido" && b.Segundos == 0) b.Porcentaje = numDur.Valor; else b.Segundos = numDur.Valor;
            Llenar();
        };
        btnArchivo.Click += delegate
        {
            BloqueTV b = Actual();
            if (b == null) return;
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Archivo para «" + b.Nombre + "»";
                d.Filter = "Video, imagen o audio|*.mp4;*.mov;*.mkv;*.webm;*.avi;*.png;*.jpg;*.jpeg;*.gif;*.mp3;*.wav|Todos|*.*";
                string a = Resultado.Archivo(b.Clave);
                if (a.Length > 0 && Directory.Exists(Path.GetDirectoryName(a))) d.InitialDirectory = Path.GetDirectoryName(a);
                if (d.ShowDialog(this) != DialogResult.OK) return;
                Resultado.Kit[b.Clave] = d.FileName;
                if (b.Tipo != "kit") b.Tipo = "kit";
            }
            Llenar();
        };
        btnQuitar.Click += delegate
        {
            BloqueTV b = Actual();
            if (b == null) return;
            Resultado.Kit.Remove(b.Clave);
            Llenar();
        };
        btnDefecto.Click += delegate
        {
            Dictionary<string, string> kit = Resultado.Kit;
            Resultado = PlantillaTV.PorDefecto();
            foreach (KeyValuePair<string, string> kv in kit) Resultado.Kit[kv.Key] = kv.Value;
            Llenar();
        };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        btnGuardar.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        Llenar();
        if (lst.Items.Count > 0) lst.Items[0].Selected = true;
    }

    BloqueTV Actual() { return lst.SelectedIndices.Count == 0 ? null : Resultado.Bloques[lst.SelectedIndices[0]]; }

    static string Duracion(BloqueTV b)
    {
        return b.Segundos > 0 ? b.Segundos + " s" : b.Porcentaje + " % del resto";
    }

    void Llenar()
    {
        int sel = lst.SelectedIndices.Count > 0 ? lst.SelectedIndices[0] : -1;
        cargando = true;
        lst.Items.Clear();
        foreach (BloqueTV b in Resultado.Bloques)
        {
            ListViewItem it = new ListViewItem(b.Nombre);
            it.SubItems.Add(b.Tipo == "kit" ? "kit" : b.Tipo == "texto" ? "texto" : "del capítulo");
            it.SubItems.Add(Duracion(b));
            string a = Resultado.Archivo(b.Clave);
            it.SubItems.Add(b.Tipo != "kit" ? "—" : a.Length > 0 ? Path.GetFileName(a) + (File.Exists(a) ? "" : " (no se encuentra)") : "placeholder");
            if (b.Tipo == "kit" && a.Length == 0) it.ForeColor = Tema.AcentoHover;
            lst.Items.Add(it);
        }
        if (sel >= 0 && sel < lst.Items.Count) lst.Items[sel].Selected = true;
        cargando = false;
        double fijo = Resultado.Fijo();
        lblTotal.Text = "Lo fijo suma " + Formato.Tiempo(fijo) + ": para " + Formato.Tiempo(minimo) + "–" + Formato.Tiempo(maximo) +
                        " quedan " + Formato.Tiempo(Math.Max(0, minimo - fijo)) + "–" + Formato.Tiempo(Math.Max(0, maximo - fijo)) + " para los actos.";
        Elegido();
    }

    void Elegido()
    {
        BloqueTV b = Actual();
        btnArchivo.Enabled = btnQuitar.Enabled = numDur.Enabled = b != null;
        if (b == null) { lblDesc.Text = ""; return; }
        cargando = true;
        bool pct = b.Tipo == "contenido" && b.Segundos == 0;
        lblDur.Text = pct ? "PORCENTAJE DEL RESTO" : "DURACIÓN (S)";
        numDur.Sufijo = pct ? "%" : "s";
        numDur.Valor = (int)Math.Round(pct ? b.Porcentaje : b.Segundos);
        numDur.Invalidate();
        cargando = false;
        btnArchivo.Enabled = b.Tipo == "kit" || b.Tipo == "texto";
        btnQuitar.Enabled = Resultado.Archivo(b.Clave).Length > 0;
        lblDesc.Text = b.Descripcion;
    }
}

// Musica de la serie: carpeta con su indice, reparto y el tema de cada uno.
class VentanaMusicaSerie : VentanaBase
{
    public MusicaSerie Resultado;
    readonly string clave, modelo, premisa;
    BibliotecaMusica biblioteca;
    bool trabajando;

    Etiqueta lblCarpeta, lblIndice, lblEstado;
    Boton btnCarpeta = new Boton("Elegir carpeta…", EstiloBoton.Secundario);
    Boton btnIndexar = new Boton("Indexar", EstiloBoton.Secundario);
    CampoTexto txtReparto = new CampoTexto();
    CampoTexto txtPreferencias = new CampoTexto();
    Lista lst = new Lista();
    Boton btnIA = new Boton("Elegir con IA", EstiloBoton.Primario);
    Boton btnCambiar = new Boton("Cambiar…", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Quitar", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);
    BarraProgreso barra = new BarraProgreso();

    public VentanaMusicaSerie(MusicaSerie m, string premisa, string clave, string modelo) : base("Música de la serie", 1000)
    {
        StartPosition = FormStartPosition.CenterParent;
        this.clave = clave; this.modelo = modelo; this.premisa = premisa ?? "";
        Resultado = MusicaSerie.Leer(m.Escribir());
        int x = Margen, w = Ancho;
        Encabezado("Música de la serie", "Tu biblioteca indexada por cómo se usa cada tema en el anime, y el tema de cada personaje.");
        int y = 92;
        lblCarpeta = Texto("", Tema.Normal, Tema.Texto, x, y + 6, w - 320, 20);
        Pos(btnCarpeta, x + w - 310, y, 150, 32);
        Pos(btnIndexar, x + w - 152, y, 152, 32);
        y += 38;
        lblIndice = Texto("", Tema.Pequena, Tema.TextoSuave, x, y, w, 18);
        Pos(barra, x, y + 22, w, 6);
        barra.Visible = false;
        y += 34;
        int mitad = (w - 16) / 2;
        Texto("REPARTO (uno por línea: «Nombre: cómo es»)", Tema.Pequena, Tema.TextoSuave, x, y, mitad, 18);
        Texto("PREFERENCIAS (opcional: «para Gerber algo de Golden Wind»…)", Tema.Pequena, Tema.TextoSuave, x + mitad + 16, y, mitad, 18);
        txtReparto.Multilinea = true; txtPreferencias.Multilinea = true;
        Pos(txtReparto, x, y + 20, mitad, 96);
        Pos(txtPreferencias, x + mitad + 16, y + 20, mitad, 96);
        y += 126;
        lst.CheckBoxes = false;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Para", 150);
        lst.Columns.Add("Tema", 220);
        lst.Columns.Add("Variantes", 74);
        lst.Columns.Add("Por qué", w - 150 - 220 - 74 - sb);
        Pos(lst, x, y, w, 200);
        y += 208;
        Pos(btnIA, x, y, 160, 34);
        Pos(btnCambiar, x + 168, y, 120, 34);
        Pos(btnQuitar, x + 296, y, 100, 34);
        y += 44;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, x, y, w - 290, 40);
        Pos(btnCancelar, x + w - 280, y, 120, 40);
        Pos(btnGuardar, x + w - 150, y, 150, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        txtReparto.Text = (Resultado.Reparto ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        btnCarpeta.Click += delegate { ElegirCarpeta(); };
        btnIndexar.Click += delegate { Indexar(); };
        btnIA.Click += delegate { ConIA(); };
        btnCambiar.Click += delegate { Cambiar(); };
        btnQuitar.Click += delegate
        {
            string k = Fila();
            if (k == null) return;
            if (k == "") Resultado.Principal = null; else Resultado.Personajes.Remove(k);
            Llenar();
        };
        txtReparto.Caja.Leave += delegate { Resultado.Reparto = txtReparto.Text.Trim(); Llenar(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        btnGuardar.Click += delegate { Resultado.Reparto = txtReparto.Text.Trim(); DialogResult = DialogResult.OK; Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };

        if (Resultado.Carpeta.Length > 0 && Directory.Exists(Resultado.Carpeta))
            try { biblioteca = BibliotecaMusica.Cargar(Resultado.Carpeta); } catch { biblioteca = null; }
        Mostrar();
        Llenar();
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Mostrar()
    {
        lblCarpeta.Text = Resultado.Carpeta.Length > 0 ? "Carpeta: " + Resultado.Carpeta : "Elige la carpeta de tu música.";
        btnIndexar.Enabled = Resultado.Carpeta.Length > 0;
        btnIndexar.Text = biblioteca == null ? "Indexar" : "Volver a indexar";
        if (biblioteca == null)
            lblIndice.Text = Resultado.Carpeta.Length > 0 ? "Sin índice todavía: pulsa «Indexar» (lee las etiquetas de cada archivo; tarda un poco la primera vez)." : "";
        else
        {
            int con = 0;
            foreach (ArchivoMusica a in biblioteca.Archivos) if (a.ConUso) con++;
            lblIndice.Text = biblioteca.Archivos.Count + " archivos · " + con + " con datos de cómo se usan en el anime.";
        }
        btnIA.Enabled = biblioteca != null && !String.IsNullOrEmpty(clave);
    }

    string Nombre(string ruta)
    {
        ArchivoMusica a = biblioteca != null ? biblioteca.Buscar(ruta) : null;
        return a != null ? a.Titulo + (a.Parte.Length > 0 ? " (" + a.Parte + ")" : "") : Path.GetFileNameWithoutExtension(ruta);
    }

    void Llenar()
    {
        lst.Items.Clear();
        List<KeyValuePair<string, TemaAsignado>> filas = new List<KeyValuePair<string, TemaAsignado>>();
        filas.Add(new KeyValuePair<string, TemaAsignado>("", Resultado.Principal));
        List<string> nombres = Resultado.Nombres();
        foreach (string n in nombres)
        {
            TemaAsignado t;
            Resultado.Personajes.TryGetValue(n, out t);
            filas.Add(new KeyValuePair<string, TemaAsignado>(n, t));
        }
        foreach (KeyValuePair<string, TemaAsignado> kv in Resultado.Personajes)
            if (!nombres.Contains(kv.Key)) filas.Add(kv);
        foreach (KeyValuePair<string, TemaAsignado> f in filas)
        {
            ListViewItem it = new ListViewItem(f.Key.Length == 0 ? "Tema principal" : f.Key);
            it.SubItems.Add(f.Value != null ? Nombre(f.Value.Archivo) : "—");
            it.SubItems.Add(f.Value != null && f.Value.Variantes.Count > 0 ? f.Value.Variantes.Count.ToString() : "");
            it.SubItems.Add(f.Value != null ? f.Value.Motivo : "");
            it.Tag = f.Key;
            if (f.Value == null) it.ForeColor = Tema.TextoSuave;
            lst.Items.Add(it);
        }
    }

    string Fila() { return lst.SelectedIndices.Count == 0 ? null : (string)lst.Items[lst.SelectedIndices[0]].Tag; }

    void ElegirCarpeta()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta de tu música (se busca también en subcarpetas)";
            if (Resultado.Carpeta.Length > 0 && Directory.Exists(Resultado.Carpeta)) d.SelectedPath = Resultado.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Resultado.Carpeta = d.SelectedPath;
        }
        try { biblioteca = BibliotecaMusica.Cargar(Resultado.Carpeta); } catch { biblioteca = null; }
        Mostrar();
        Llenar();
    }

    void Indexar()
    {
        trabajando = true;
        barra.Visible = true;
        foreach (Control c in new Control[] { btnIndexar, btnCarpeta, btnIA, btnGuardar }) c.Enabled = false;
        try
        {
            Action<string, double> av = delegate (string t, double f) { Estado(t, false); barra.Valor = f; Application.DoEvents(); };
            List<FilaMusica> filas = BibliotecaMusica.Escanear(Resultado.Carpeta, delegate (string t, double f) { av(t, f * 0.8); });
            biblioteca = BibliotecaMusica.Indexar(Resultado.Carpeta, filas, delegate (string t, double f) { av(t, 0.8 + f * 0.2); });
            biblioteca.Guardar(Path.Combine(Resultado.Carpeta, BibliotecaMusica.NombreIndice));
            Estado("✔ Índice guardado en " + BibliotecaMusica.NombreIndice + " (en la carpeta de la música).", false);
        }
        catch (Exception ex) { Estado("No se pudo indexar: " + ex.Message, true); }
        trabajando = false;
        barra.Visible = false;
        foreach (Control c in new Control[] { btnIndexar, btnCarpeta, btnGuardar }) c.Enabled = true;
        Mostrar();
        Llenar();
    }

    void ConIA()
    {
        Resultado.Reparto = txtReparto.Text.Trim();
        if (Resultado.Nombres().Count == 0) { Estado("Escribe el reparto: un personaje por línea.", true); return; }
        List<ArchivoMusica> cand = MusicaSerie.Candidatos(biblioteca);
        string instr = MusicaSerie.Instrucciones(), msg = MusicaSerie.Mensaje(cand, premisa, Resultado.Reparto, txtPreferencias.Text);
        string c = clave, mo = modelo;
        trabajando = true;
        foreach (Control x in new Control[] { btnIA, btnGuardar, btnIndexar }) x.Enabled = false;
        Estado("Gemini está escuchando tu biblioteca…", false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(c, mo, instr, msg, true); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    foreach (Control x in new Control[] { btnIA, btnGuardar, btnIndexar }) x.Enabled = true;
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        int n = Resultado.Aplicar(resp, cand);
                        Llenar();
                        Estado("✔ " + n + " temas elegidos. Cambia los que quieras y guarda: quedan fijos para toda la serie.", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer: " + ex.Message, true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Cambiar()
    {
        string k = Fila();
        if (k == null) { Estado("Elige una fila.", true); return; }
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Tema para " + (k.Length == 0 ? "la serie" : k);
            d.Filter = "Música|*.mp3;*.flac;*.wav;*.m4a;*.ogg;*.opus;*.aac;*.wma";
            if (Resultado.Carpeta.Length > 0 && Directory.Exists(Resultado.Carpeta)) d.InitialDirectory = Resultado.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            TemaAsignado t = new TemaAsignado();
            string raiz = Resultado.Carpeta.TrimEnd('\\', '/');
            t.Archivo = raiz.Length > 0 && d.FileName.StartsWith(raiz + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? d.FileName.Substring(raiz.Length + 1) : d.FileName;
            t.Motivo = "elegido a mano";
            ArchivoMusica a = biblioteca != null ? biblioteca.Buscar(t.Archivo) : null;
            if (a != null) t.Variantes.AddRange(a.Variantes);
            if (k.Length == 0) Resultado.Principal = t; else Resultado.Personajes[k] = t;
        }
        Llenar();
    }
}
