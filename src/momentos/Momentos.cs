using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string veg = vegas.Project.FilePath;
        string ruta = Transcripcion.RutaPara(veg);
        if (ruta == null || !File.Exists(ruta))
        {
            MessageBox.Show("Este proyecto aún no tiene transcripción.\n\nEjecuta primero “Transcribir”.", "Momentos con IA");
            return;
        }
        Transcripcion t;
        try { t = Transcripcion.Cargar(ruta); }
        catch (Exception ex) { MessageBox.Show("No se pudo leer la transcripción: " + ex.Message, "Momentos con IA"); return; }

        // Con las fuentes, la transcripcion sigue tambien las ediciones a mano.
        if (t.TieneFuentes) t.Ubicador = PistasVegas.Ubicador(vegas.Project, t);
        using (VentanaMomentos v = new VentanaMomentos(vegas, t, ruta)) v.ShowDialog();
    }
}

// Editor de las reglas del canal (se guardan para todos los proyectos).
class DialogoReglas : VentanaBase
{
    CampoTexto txt = new CampoTexto();
    public string Reglas { get { return txt.Text.Trim(); } }

    public DialogoReglas(string reglas) : base("Reglas del canal", 640)
    {
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Reglas del canal", "Se aplican siempre, en todos los videos. Una regla por línea.");
        txt.Multilinea = true;
        txt.Text = reglas;
        Pos(txt, m, 92, w, 300);
        Boton restaurar = new Boton("Restaurar las de siempre", EstiloBoton.Secundario);
        Boton cancelar = new Boton("Cancelar", EstiloBoton.Secundario);
        Boton guardar = new Boton("Guardar", EstiloBoton.Primario);
        Pos(restaurar, m, 408, 200, 38);
        Pos(cancelar, m + w - 250, 408, 110, 38);
        Pos(guardar, m + w - 130, 408, 130, 38);
        ClientSize = new Size(ClientSize.Width, 470);
        restaurar.Click += delegate { txt.Text = PeticionIA.ReglasPorDefecto; };
        cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        guardar.Click += delegate { DialogResult = DialogResult.OK; Close(); };
    }
}

class VentanaMomentos : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion transcripcion;
    readonly string rutaTranscripcion, rutaIA, rutaInforme, rutaHistorial;
    readonly Configuracion config = Configuracion.Cargar();
    readonly double total;
    ResultadoIA resultado;
    OpcionesIA opciones = new OpcionesIA();
    List<string> contexto = new List<string>();   // respuestas de episodios anteriores
    List<string> historial = new List<string>();  // respuestas guardadas de este proyecto
    bool cargando, aplicado, vigente;

    Segmentado segTipo = new Segmentado(new string[] { "Gameplay", "Narración", "Podcast", "Otro" });
    CampoNumero numMin = new CampoNumero(), numMax = new CampoNumero();
    Segmentado segAcelerar = new Segmentado(new string[] { "Cortar", "Acelerar" });
    Segmentado segAudio = new Segmentado(new string[] { "Mudo", "Acelerado" });
    List<CampoTexto> nombres = new List<CampoTexto>();
    CampoTexto txtInstrucciones = new CampoTexto();
    Boton btnReglas = new Boton("Reglas del canal…", EstiloBoton.Secundario);
    Boton btnContexto = new Boton("Episodios anteriores…", EstiloBoton.Secundario);
    Etiqueta lblContexto;
    Combo comboModelo = new Combo(true);
    Etiqueta lblModelo;
    Boton btnPedir = new Boton("Pedir a Gemini", EstiloBoton.Primario);
    Etiqueta lblEstado;

    Combo comboHistorial = new Combo();
    Segmentado pestanas = new Segmentado(new string[] { "Corte", "Momentos", "Textos", "Resumen", "Shorts y títulos" });
    Lista lstCorte = new Lista(), lstMomentos = new Lista(), lstTextos = new Lista(), lstShorts = new Lista();
    CampoTexto txtResumen = new CampoTexto();
    Etiqueta lblCorte;

    Boton btnInforme = new Boton("Guardar informe", EstiloBoton.Secundario);
    Boton btnMarcar = new Boton("Crear regiones y marcadores", EstiloBoton.Secundario);
    Boton btnCortar = new Boton("Aplicar corte", EstiloBoton.Primario);
    Boton btnFijar = new Boton("Conservar selección", EstiloBoton.Secundario);
    Boton chipCuentan = new Boton("Los fijos cuentan en la duración", EstiloBoton.Chip);
    bool fijosCuentan = true;
    List<Tramo> fijos = new List<Tramo>();   // tramos elegidos a mano (selección de tiempo)
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    static readonly string[] Tipos = { "Gameplay", "Narración", "Podcast", "Otro" };

    public VentanaMomentos(Vegas vegas, Transcripcion t, string ruta) : base("Momentos con IA", 1040)
    {
        this.vegas = vegas;
        transcripcion = t;
        rutaTranscripcion = ruta;
        string veg = vegas.Project.FilePath;
        string baseNombre = Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg));
        rutaIA = baseNombre + ".vegascut-ia.json";
        rutaInforme = baseNombre + ".vegascut-informe.md";
        rutaHistorial = baseNombre + ".vegascut-ia-historial";
        total = vegas.Project.Length.ToMilliseconds() / 1000.0;

        int m = Margen, w = Ancho;
        Encabezado("Momentos con IA", "Gemini lee la transcripción y la intensidad del sonido, y sugiere qué conservar.");

        // Estado de la transcripcion
        string aviso = t.Sincronizar(total);
        if (aviso.StartsWith("Se detect")) { try { t.Guardar(ruta); } catch { } }
        if (t.Ubicador != null && !aviso.StartsWith("Se detect")) aviso = ""; // sigue las ediciones a mano
        int frases = t.SegmentosActuales().Count;
        Texto("Transcripción del " + t.Creada + ": " + frases + " frases · proyecto de " +
            Formato.Tiempo(total) + (aviso.Length > 0 ? "\n" + aviso : ""), Tema.Pequena,
            aviso.Length > 0 && !aviso.StartsWith("Se detect") ? Tema.AcentoHover : Tema.TextoSuave, m, 88, w, 34);

        // ---------------- Columna izquierda: lo que se pide
        int y = 130, ci = 320;
        Texto("Tipo de video", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        Pos(segTipo, m, y + 22, ci, 34);
        y += 66;
        Texto("Duración del corte", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        foreach (CampoNumero n in new CampoNumero[] { numMin, numMax })
        {
            n.Sufijo = "min"; n.Minimo = 1; n.Maximo = 600; n.Paso = 1;
        }
        Pos(numMin, m, y + 22, 92, 36);
        Texto("a", Tema.Normal, Tema.TextoSuave, m + 98, y + 30, 16, 20);
        Pos(numMax, m + 118, y + 22, 92, 36);
        Texto("mínimo y máximo", Tema.Pequena, Tema.TextoSuave, m + 218, y + 30, ci - 218, 20);
        y += 66;
        Texto("Transiciones", Tema.Negrita, Tema.Texto, m, y + 8, 110, 20);
        Pos(segAcelerar, m + 120, y, ci - 120, 34);
        y += 42;
        Texto("Audio acelerado", Tema.Negrita, Tema.Texto, m, y + 8, 120, 20);
        Pos(segAudio, m + 120, y, ci - 120, 34);
        y += 46;
        Texto("Nombres de las personas", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        y += 24;
        foreach (Hablante h in t.Hablantes)
        {
            if (!h.Voz) continue;
            Texto(h.Etiqueta, Tema.Negrita, Tema.Voz, m, y + 6, 48, 22);
            CampoTexto c = new CampoTexto();
            c.Text = h.Nombre;
            c.Tag = h;
            Pos(c, m + 52, y, ci - 52, 32);
            nombres.Add(c);
            y += 38;
        }
        y += 4;
        Texto("Indicaciones de este episodio", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        txtInstrucciones.Multilinea = true;
        Pos(txtInstrucciones, m, y + 22, ci, 84);
        y += 114;
        Pos(btnReglas, m, y, 150, 32);
        Pos(btnContexto, m + 158, y, ci - 158, 32);
        lblContexto = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 36, ci, 18);
        y += 60;
        Texto("Modelo", Tema.Negrita, Tema.Texto, m, y + 6, 70, 20);
        comboModelo.Items.Add(config.GeminiModelo);
        foreach (string x in new string[] { "gemini-flash-latest", "gemini-pro-latest", "gemini-flash-lite-latest" })
            if (!comboModelo.Items.Contains(x)) comboModelo.Items.Add(x);
        comboModelo.Text = config.GeminiModelo;
        Pos(comboModelo, m + 70, y + 2, ci - 70, 30);
        lblModelo = Texto("", Tema.Pequena, Tema.AcentoHover, m, y + 36, ci, 32);
        y += 72;
        Pos(btnPedir, m, y, ci, 42);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 48, ci, 48);
        int fondoIzq = y + 100;

        // ---------------- Columna derecha: resultado
        int dx = m + ci + 24, dw = w - ci - 24, dy = 130;
        Texto("Respuesta", Tema.Negrita, Tema.Texto, dx, dy + 6, 84, 20);
        Pos(comboHistorial, dx + 88, dy + 2, dw - 88, 30);
        dy += 42;
        Pos(pestanas, dx, dy, dw, 34);
        dy += 44;
        int alto = Math.Max(420, fondoIzq - dy - 40);
        ConfigurarListas();
        foreach (Control c in new Control[] { lstCorte, lstMomentos, lstTextos, lstShorts, txtResumen }) Pos(c, dx, dy, dw, alto);
        // La ultima columna ocupa el espacio que sobra, sin barra horizontal.
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts })
        {
            int usado = 0;
            for (int i = 0; i < l.Columns.Count - 1; i++) usado += l.Columns[i].Width;
            l.Columns[l.Columns.Count - 1].Width = Math.Max(150, dw - usado - SystemInformation.VerticalScrollBarWidth - 4);
        }
        txtResumen.Multilinea = true;
        txtResumen.Caja.ReadOnly = true;
        lblCorte = Texto("", Tema.Negrita, Tema.Texto, dx, dy + alto + 8, dw - 262, 22);
        Pos(chipCuentan, dx + dw - 254, dy + alto + 6, 254, 26);
        int fondo = Math.Max(fondoIzq, dy + alto + 40);

        Pos(btnInforme, m, fondo, 150, 40);
        Pos(btnMarcar, m + 160, fondo, 230, 40);
        Pos(btnFijar, m + 400, fondo, 190, 40);
        Pos(btnCerrar, m + w - 300, fondo, 110, 40);
        Pos(btnCortar, m + w - 180, fondo, 180, 40);
        ClientSize = new Size(ClientSize.Width, fondo + 40 + 24);

        // Eventos
        pestanas.Cambio += delegate { MostrarPestana(); };
        btnPedir.Click += delegate { Pedir(); };
        btnInforme.Click += delegate { GuardarInforme(); };
        btnMarcar.Click += delegate { CrearMarcas(); };
        btnFijar.Click += delegate { Fijar(); };
        chipCuentan.Click += delegate { CambiarCuentan(); };
        btnCortar.Click += delegate { AplicarCorte(); };
        btnCerrar.Click += delegate { Close(); };
        btnReglas.Click += delegate { EditarReglas(); };
        btnContexto.Click += delegate { ElegirContexto(); };
        comboModelo.TextChanged += delegate { AvisoModelo(); };
        comboHistorial.SelectedIndexChanged += delegate { if (!cargando && comboHistorial.SelectedIndex >= 0) CargarEntrada(historial[comboHistorial.SelectedIndex], false); };
        lstCorte.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
        {
            if (cargando || resultado == null) return;
            Tramo tc = (Tramo)e.Item.Tag;
            tc.Elegido = e.Item.Checked;
            if (!tc.Elegido && tc.Fijo) QuitarFijo(tc);
            ActualizarResumenCorte();
        };
        foreach (Lista l in new Lista[] { lstMomentos, lstShorts })
            l.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((Tramo)e.Item.Tag).Elegido = e.Item.Checked; };
        lstTextos.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((TextoResumen)e.Item.Tag).Elegido = e.Item.Checked; };
        lstCorte.MouseClick += delegate (object s, MouseEventArgs e) { CambiarVelocidad(e); };
        segAcelerar.Cambio += delegate { segAudio.Enabled = segAcelerar.Seleccion == 1; };
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts })
            l.DoubleClick += delegate (object s, EventArgs e) { IrA((ListView)s); };

        fijos = TramosFijos.Cargar(vegas.Project.FilePath, total);
        fijosCuentan = TramosFijos.Cuentan(vegas.Project.FilePath);
        chipCuentan.Activo = fijosCuentan;

        // Valores iniciales: los de la ultima respuesta guardada.
        cargando = true;
        LlenarHistorial();
        PonerOpciones(historial.Count > 0 ? historial[0] : null);
        cargando = false;
        if (historial.Count > 0) CargarEntrada(historial[0], true);
        AvisoModelo();
        if (!config.TieneGemini)
        {
            btnPedir.Enabled = false;
            Estado("Falta la clave de Gemini: ejecuta “ConfigurarVegasCut”.", true);
        }
        MostrarResultado();
    }

    void ConfigurarListas()
    {
        lstCorte.Columns.Add("Inicio", 70); lstCorte.Columns.Add("Fin", 70); lstCorte.Columns.Add("Dura", 52);
        lstCorte.Columns.Add("Velocidad", 84); lstCorte.Columns.Add("Imp.", 40); lstCorte.Columns.Add("Tramo", 180);
        lstCorte.Columns.Add("Por qué", 900);
        lstMomentos.Columns.Add("Nota", 60); lstMomentos.Columns.Add("Inicio", 70); lstMomentos.Columns.Add("Fin", 70);
        lstMomentos.Columns.Add("Momento", 200); lstMomentos.Columns.Add("Por qué", 900);
        lstTextos.Columns.Add("Dónde", 70); lstTextos.Columns.Add("Texto", 280); lstTextos.Columns.Add("Qué se salta", 900);
        lstShorts.Columns.Add("Inicio", 70); lstShorts.Columns.Add("Fin", 70); lstShorts.Columns.Add("Short", 220);
        lstShorts.Columns.Add("Por qué funciona", 900);
    }

    // ------------------------------------------------------- opciones

    void PonerOpciones(string archivo)
    {
        double mins = Math.Max(1, Math.Round(total / 60 / 4));
        opciones.MinutosMin = mins; opciones.MinutosMax = mins + 4;
        opciones.ReglasCanal = config.ReglasCanal.Length > 0 ? config.ReglasCanal : PeticionIA.ReglasPorDefecto;
        try
        {
            object op = archivo != null ? Json.Obj(Json.Leer(File.ReadAllText(archivo, Encoding.UTF8)), "opciones") : null;
            if (op != null)
            {
                opciones.Tipo = Json.Texto(op, "tipo");
                double viejo = Json.Numero(op, "minutos", -1); // respuestas de antes de min/max
                opciones.MinutosMin = Json.Numero(op, "minutosMin", viejo > 0 ? viejo : opciones.MinutosMin);
                opciones.MinutosMax = Json.Numero(op, "minutosMax", viejo > 0 ? viejo + 2 : opciones.MinutosMax);
                opciones.Instrucciones = Json.Texto(op, "instrucciones");
                opciones.PermitirAcelerar = Json.Texto(op, "acelerar") != "False";
                opciones.SilenciarAcelerado = Json.Texto(op, "silenciarAcelerado") != "False";
                contexto.Clear();
                foreach (object x in Json.Lista(op, "contexto")) if (x is string && File.Exists((string)x)) contexto.Add((string)x);
            }
        }
        catch { }
        segTipo.Seleccion = Math.Max(0, Array.IndexOf(Tipos, opciones.Tipo));
        numMin.Valor = (int)opciones.MinutosMin;
        numMax.Valor = (int)opciones.MinutosMax;
        txtInstrucciones.Text = opciones.Instrucciones;
        segAcelerar.Seleccion = opciones.PermitirAcelerar ? 1 : 0;
        segAudio.Seleccion = opciones.SilenciarAcelerado ? 0 : 1;
        segAudio.Enabled = opciones.PermitirAcelerar;
        MostrarContexto();
    }

    void LeerOpciones()
    {
        opciones.Tipo = Tipos[segTipo.Seleccion];
        opciones.MinutosMin = Math.Min(numMin.Valor, numMax.Valor);
        opciones.MinutosMax = Math.Max(numMin.Valor, numMax.Valor);
        opciones.Instrucciones = txtInstrucciones.Text;
        opciones.PermitirAcelerar = segAcelerar.Seleccion == 1;
        opciones.SilenciarAcelerado = segAudio.Seleccion == 0;
        opciones.ReglasCanal = config.ReglasCanal.Length > 0 ? config.ReglasCanal : PeticionIA.ReglasPorDefecto;
        opciones.Contexto = PeticionIA.ContextoDe(contexto);
        opciones.Fijos = new List<Tramo>(fijos);
        opciones.FijosCuentan = fijosCuentan;
        foreach (CampoTexto c in nombres)
        {
            Hablante h = (Hablante)c.Tag;
            h.Nombre = c.Text.Trim().Length > 0 ? c.Text.Trim() : h.Etiqueta;
        }
    }

    void EditarReglas()
    {
        string actuales = config.ReglasCanal.Length > 0 ? config.ReglasCanal : PeticionIA.ReglasPorDefecto;
        using (DialogoReglas d = new DialogoReglas(actuales))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            config.ReglasCanal = d.Reglas == PeticionIA.ReglasPorDefecto.Trim() ? "" : d.Reglas;
            try { config.Guardar(); Estado("✔ Reglas del canal guardadas.", false); }
            catch (Exception ex) { Estado("No se pudieron guardar las reglas: " + ex.Message, true); }
        }
    }

    void ElegirContexto()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Respuestas de MomentosIA de episodios anteriores (opcional)";
            d.Filter = "Respuestas de MomentosIA|*.vegascut-ia.json;*.json";
            d.Multiselect = true;
            string carpeta = Path.GetDirectoryName(Path.GetDirectoryName(vegas.Project.FilePath) ?? "");
            if (!String.IsNullOrEmpty(carpeta) && Directory.Exists(carpeta)) d.InitialDirectory = carpeta;
            DialogResult r = d.ShowDialog(this);
            if (r != DialogResult.OK) return;
            contexto.Clear();
            foreach (string f in d.FileNames) if (f != rutaIA && !f.StartsWith(rutaHistorial)) contexto.Add(f);
            MostrarContexto();
        }
    }

    void MostrarContexto()
    {
        if (contexto.Count == 0) { lblContexto.Text = "Sin contexto de episodios anteriores."; return; }
        List<string> n = new List<string>();
        foreach (string f in contexto) n.Add(Path.GetFileName(f).Replace(".vegascut-ia.json", ""));
        lblContexto.Text = "Contexto: " + String.Join(", ", n.ToArray());
    }

    void AvisoModelo()
    {
        string mo = comboModelo.Text.ToLowerInvariant();
        lblModelo.Text = mo.Contains("lite")
            ? "Lite es más barato pero sigue peor las reglas y la duración. Para cortes largos usa flash o pro."
            : "";
    }

    // ------------------------------------------------------- historial

    void LlenarHistorial()
    {
        historial.Clear();
        if (Directory.Exists(rutaHistorial))
        {
            string[] f = Directory.GetFiles(rutaHistorial, "*.json");
            Array.Sort(f);
            Array.Reverse(f);
            historial.AddRange(f);
        }
        if (historial.Count == 0 && File.Exists(rutaIA)) historial.Add(rutaIA);
        comboHistorial.Items.Clear();
        foreach (string f in historial)
        {
            string texto = Path.GetFileName(f);
            try
            {
                object o = Json.Leer(File.ReadAllText(f, Encoding.UTF8));
                object op = Json.Obj(o, "opciones");
                double mn = Json.Numero(op, "minutosMin", Json.Numero(op, "minutos", 0)), mx = Json.Numero(op, "minutosMax", mn);
                texto = Json.Texto(o, "fecha") + "  ·  " + Json.Texto(o, "modelo") + "  ·  " + mn + "–" + mx + " min" +
                        (Math.Abs(Json.Numero(o, "duracionProyecto", -1) - total) > 0.5 ? "  ·  (proyecto distinto)" : "");
            }
            catch { }
            comboHistorial.Items.Add(texto);
        }
        if (comboHistorial.Items.Count == 0) comboHistorial.Items.Add("Todavía no hay respuestas");
        comboHistorial.SelectedIndex = 0;
        comboHistorial.Enabled = historial.Count > 1;
    }

    // Arma el resultado desde la respuesta y la revision guardadas, con los
    // mismos pasos siempre (asi los indices de la revision cuadran).
    ResultadoIA Construir(string respuesta, string revision, double duracion, double min, double max, out string nota)
    {
        ResultadoIA r = ResultadoIA.Leer(respuesta, duracion);
        r.AjustarAPalabras(transcripcion.SegmentosActuales());
        nota = "";
        if (!String.IsNullOrEmpty(revision))
        {
            try
            {
                int n = r.AplicarRevision(revision);
                if (n > 0) nota = "revisión: " + n + (n == 1 ? " tramo quitado o recortado" : " tramos quitados o recortados");
            }
            catch { }
        }
        // Los tramos fijos se agregan despues de la revision (no cambian sus indices).
        if (Math.Abs(duracion - total) <= 0.5)
            foreach (Tramo f in fijos) r.AgregarFijo(f.Inicio, f.Fin, f.Titulo);
        r.FijosCuentan = fijosCuentan;
        string ajuste = r.AjustarDuracion(min * 60, max * 60);
        if (ajuste.Length > 0) nota += (nota.Length > 0 ? "; " : "") + ajuste;
        return r;
    }

    void CargarEntrada(string archivo, bool inicial)
    {
        try
        {
            object o = Json.Leer(File.ReadAllText(archivo, Encoding.UTF8));
            double duracion = Json.Numero(o, "duracionProyecto", total);
            object op = Json.Obj(o, "opciones");
            double mn = Json.Numero(op, "minutosMin", Json.Numero(op, "minutos", 1));
            double mx = Json.Numero(op, "minutosMax", Json.Numero(op, "minutos", 600) + 2);
            string nota;
            resultado = Construir(Json.Texto(o, "respuesta"), Json.Texto(o, "revision"), duracion, mn, mx, out nota);
            vigente = Math.Abs(duracion - total) <= 0.5;
            aplicado = false;
            if (vigente)
                Estado("Mostrando la respuesta del " + Json.Texto(o, "fecha") + " (" + Json.Texto(o, "modelo") + ")." +
                       (nota.Length > 0 ? " Ajustes: " + nota + "." : ""), false);
            else
                Estado("Respuesta del " + Json.Texto(o, "fecha") + ": el proyecto cambió desde entonces (duraba " +
                       Formato.Tiempo(duracion) + ", ahora " + Formato.Tiempo(total) + "). Puedes revisarla, pero para " +
                       "aplicarla deshaz los cambios (Ctrl+Z) o pide una nueva.", !inicial);
        }
        catch (Exception ex)
        {
            resultado = null;
            Estado("No se pudo abrir esa respuesta: " + ex.Message, true);
        }
        MostrarResultado();
    }

    // ------------------------------------------------------- Gemini

    void Pedir()
    {
        LeerOpciones();
        try { transcripcion.Guardar(rutaTranscripcion); } catch { } // guarda los nombres
        string clave = config.GeminiClave, modelo = comboModelo.Text.Trim();
        if (modelo.Length == 0) modelo = config.GeminiModelo;
        if (modelo != config.GeminiModelo) { config.GeminiModelo = modelo; try { config.Guardar(); } catch { } }
        OpcionesIA op = opciones;
        Transcripcion t = transcripcion;
        double duracion = total;

        btnPedir.Enabled = false;
        DateTime inicio = DateTime.Now;
        string paso = "Preparando…";
        System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        reloj.Interval = 500;
        reloj.Tick += delegate
        {
            Estado(paso + " " + Formato.Tiempo((DateTime.Now - inicio).TotalSeconds) +
                   (duracion > 35 * 60 ? "\nVideo largo: se analiza por partes (varios minutos)." : "\nPuede tardar 1 o 2 minutos."), false);
        };
        reloj.Start();

        AsistenteIA asistente = new AsistenteIA(delegate (string instrucciones, string mensaje)
        {
            return Gemini.Generar(clave, modelo, instrucciones, mensaje, true);
        });
        asistente.Progreso = delegate (string texto) { paso = texto; };

        Thread hilo = new Thread(delegate ()
        {
            string respuesta = null, revision = "", error = null;
            try
            {
                respuesta = asistente.Ejecutar(t, duracion, op);
                // Segunda pasada: revisa el corte contra las reglas.
                ResultadoIA previo = ResultadoIA.Leer(respuesta, duracion);
                previo.AjustarAPalabras(t.SegmentosActuales());
                revision = asistente.Revisar(t, previo, op);
            }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    reloj.Stop();
                    btnPedir.Enabled = true;
                    if (error != null) { Estado(error, true); return; }
                    Recibir(respuesta, revision, modelo);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Recibir(string respuesta, string revision, string modelo)
    {
        string nota;
        try
        {
            resultado = Construir(respuesta, revision, total, opciones.MinutosMin, opciones.MinutosMax, out nota);
        }
        catch (Exception ex)
        {
            Estado("La respuesta de Gemini no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true);
            return;
        }

        // Se guarda cada respuesta (historial) y la ultima aparte.
        Dictionary<string, object> guardar = new Dictionary<string, object>();
        guardar["fecha"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        guardar["modelo"] = modelo;
        guardar["duracionProyecto"] = total;
        Dictionary<string, object> op = new Dictionary<string, object>();
        op["tipo"] = opciones.Tipo;
        op["minutosMin"] = opciones.MinutosMin;
        op["minutosMax"] = opciones.MinutosMax;
        op["instrucciones"] = opciones.Instrucciones;
        op["acelerar"] = opciones.PermitirAcelerar;
        op["silenciarAcelerado"] = opciones.SilenciarAcelerado;
        op["reglasCanal"] = opciones.ReglasCanal;
        List<object> ctx = new List<object>();
        foreach (string f in contexto) ctx.Add(f);
        op["contexto"] = ctx;
        guardar["opciones"] = op;
        guardar["respuesta"] = respuesta;
        guardar["revision"] = revision;
        string texto = Json.Escribir(guardar);
        try
        {
            Directory.CreateDirectory(rutaHistorial);
            File.WriteAllText(Path.Combine(rutaHistorial, DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json"), texto, new UTF8Encoding(false));
            File.WriteAllText(rutaIA, texto, new UTF8Encoding(false));
        }
        catch { }
        cargando = true;
        LlenarHistorial();
        cargando = false;

        aplicado = false;
        vigente = true;
        Estado("✔ Listo. Revisa las pestañas; desmarca lo que no quieras." +
               (nota.Length > 0 ? " Ajustes automáticos: " + nota + "." : ""), false);
        MostrarResultado();
    }

    // ------------------------------------------------------- mostrar

    static string T(double s) { return Formato.TiempoPreciso(s); }

    static string PorQue(Tramo t)
    {
        return t.Nota.Length > 0 ? "⚠ " + t.Nota + " · " + t.Motivo : t.Motivo;
    }

    void MostrarResultado()
    {
        cargando = true;
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts }) l.Items.Clear();
        bool hay = resultado != null;
        if (hay)
        {
            foreach (Tramo t in resultado.Corte)
                Fila(lstCorte, t, t.Elegido, T(t.Inicio), T(t.Fin), Formato.Tiempo(t.Duracion), Velocidad(t),
                     t.Fijo ? "fijo" : t.Puntuacion > 0 ? t.Puntuacion.ToString("0") : "", t.Titulo, PorQue(t));
            foreach (Tramo t in resultado.Momentos)
                Fila(lstMomentos, t, t.Elegido, t.Puntuacion.ToString("0") + "/10", T(t.Inicio), T(t.Fin), t.Titulo, t.Motivo);
            foreach (TextoResumen t in resultado.Textos)
                Fila(lstTextos, t, t.Elegido, T(t.Posicion), t.Texto, t.Motivo);
            foreach (Tramo t in resultado.Shorts)
                Fila(lstShorts, t, t.Elegido, T(t.Inicio), T(t.Fin), t.Titulo, t.Motivo);

            StringBuilder sb = new StringBuilder();
            sb.Append(resultado.Resumen.Replace("\n", "\r\n") + "\r\n\r\n");
            if (resultado.Secciones.Count > 0) sb.Append("SECCIONES\r\n");
            foreach (Tramo t in resultado.Secciones)
                sb.Append(T(t.Inicio) + "–" + T(t.Fin) + "  " + t.Titulo + ": " + t.Motivo + "\r\n");
            txtResumen.Text = sb.ToString();
        }
        else txtResumen.Text = "";
        cargando = false;

        btnInforme.Enabled = hay;
        btnMarcar.Enabled = hay && vigente;
        btnCortar.Enabled = hay && vigente && !aplicado;
        ActualizarResumenCorte();
        MostrarPestana();
    }

    static string Velocidad(Tramo t)
    {
        return t.Acelerar ? "⏩ ×" + t.Velocidad.ToString("0") + " (" + Formato.Tiempo(t.DuracionFinal) + ")" : "normal";
    }

    // Clic en la columna Velocidad: normal -> x2 -> x3 -> x4 -> normal.
    void CambiarVelocidad(MouseEventArgs e)
    {
        ListViewHitTestInfo hit = lstCorte.HitTest(e.Location);
        if (hit.Item == null || hit.SubItem == null || hit.Item.SubItems.IndexOf(hit.SubItem) != 3) return;
        Tramo t = (Tramo)hit.Item.Tag;
        if (!t.Acelerar) { t.Acelerar = true; t.Velocidad = 2; }
        else if (t.Velocidad < Editor.VelocidadMaxima) t.Velocidad++;
        else { t.Acelerar = false; t.Velocidad = 1; }
        hit.SubItem.Text = Velocidad(t);
        ActualizarResumenCorte();
    }

    void Fila(Lista l, object dato, bool marcado, params string[] columnas)
    {
        ListViewItem it = new ListViewItem(columnas[0]);
        for (int i = 1; i < columnas.Length; i++) it.SubItems.Add(columnas[i]);
        it.Tag = dato;
        it.Checked = marcado;
        l.Items.Add(it);
    }

    void MostrarPestana()
    {
        Control[] paginas = { lstCorte, lstMomentos, lstTextos, txtResumen, lstShorts };
        for (int i = 0; i < paginas.Length; i++) paginas[i].Visible = i == pestanas.Seleccion;
        if (pestanas.Seleccion == 4 && resultado != null && resultado.Titulos.Count > 0)
            lblCorte.Text = "Títulos: " + String.Join("  ·  ", resultado.Titulos.ToArray());
        else ActualizarResumenCorte();
    }

    void ActualizarResumenCorte()
    {
        if (pestanas.Seleccion == 4) return;
        if (resultado == null) { lblCorte.Text = "Pide una sugerencia a Gemini para ver los resultados aquí."; return; }
        double d = resultado.DuracionCorte, ajustable = resultado.DuracionAjustable;
        bool fuera = ajustable < numMin.Valor * 60 - 0.5 || ajustable > numMax.Valor * 60 + 0.5;
        lblCorte.ForeColor = fuera ? Tema.AcentoHover : Tema.Texto;
        string aparte = Math.Abs(d - ajustable) > 0.5 ? ", " + Formato.Tiempo(d - ajustable) + " fijos aparte" : "";
        lblCorte.Text = "Conserva " + Formato.Tiempo(d) + " de " + Formato.Tiempo(total) + " (" + numMin.Valor + "–" +
                        numMax.Valor + " min" + aparte + (fuera ? ", fuera del rango" : "") + ") · doble clic: ir";
    }

    // ------------------------------------------------------- tramos fijos

    // La seleccion de tiempo de Vegas se conserva completa en el corte, diga
    // lo que diga la IA. Sirve para lo que la IA no puede ver (una carrera
    // con poca conversacion) sin volver a pedir.
    void Fijar()
    {
        double a = vegas.Transport.SelectionStart.ToMilliseconds() / 1000.0;
        double largo = vegas.Transport.SelectionLength.ToMilliseconds() / 1000.0;
        if (largo < 0) { a += largo; largo = -largo; }
        if (largo < 1)
        {
            Estado("Primero selecciona en la línea de tiempo el tramo que quieres completo (arrastra sobre la regla de tiempo) y luego pulsa “Conservar selección”.", true);
            return;
        }
        if (resultado != null && !vigente)
        {
            Estado("Esta respuesta es de antes de cambiar el proyecto. Deshaz el corte (Ctrl+Z), vuelve a abrir MomentosIA y elige la selección.", true);
            return;
        }
        double b = Math.Min(total, a + largo);
        string titulo = "Elegido a mano (" + Formato.Tiempo(a) + "–" + Formato.Tiempo(b) + ")";
        fijos = TramosFijos.Agregar(fijos, a, b, titulo);
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos, fijosCuentan);
        string nota = "";
        if (resultado != null)
        {
            resultado.AgregarFijo(a, b, titulo);
            nota = resultado.AjustarDuracion(Math.Min(numMin.Valor, numMax.Valor) * 60, Math.Max(numMin.Valor, numMax.Valor) * 60);
            MostrarResultado();
        }
        Estado("✔ " + Formato.Tiempo(a) + "–" + Formato.Tiempo(b) + " se conserva completo" +
               (resultado != null ? " en el corte" : "") + " y se le avisa a Gemini en las próximas peticiones." +
               (nota.Length > 0 ? " Ajustes: " + nota + "." : "") + " Desmárcalo en la lista para quitarlo.", false);
    }

    // Los tramos fijos cuentan o no para el minimo y el maximo del corte.
    void CambiarCuentan()
    {
        fijosCuentan = !fijosCuentan;
        chipCuentan.Activo = fijosCuentan;
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos, fijosCuentan);
        string nota = "";
        if (resultado != null)
        {
            resultado.FijosCuentan = fijosCuentan;
            nota = resultado.AjustarDuracion(Math.Min(numMin.Valor, numMax.Valor) * 60, Math.Max(numMin.Valor, numMax.Valor) * 60);
            MostrarResultado();
        }
        Estado(fijosCuentan
            ? "Los tramos fijos cuentan en la duración: todo el corte queda entre el mínimo y el máximo."
            : "Los tramos fijos van aparte: el mínimo y el máximo son solo para el resto del corte." +
              (nota.Length > 0 ? " Ajustes: " + nota + "." : ""), false);
    }

    void QuitarFijo(Tramo t)
    {
        t.Fijo = false;
        t.Nota = "Ya no es fijo";
        fijos.RemoveAll(delegate (Tramo f) { return f.Inicio < t.Fin - 0.05 && f.Fin > t.Inicio + 0.05; });
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos, fijosCuentan);
        Estado("Ese tramo ya no es fijo.", false);
    }

    void IrA(ListView l)
    {
        if (l.SelectedItems.Count == 0) return;
        object d = l.SelectedItems[0].Tag;
        double t = d is Tramo ? ((Tramo)d).Inicio : ((TextoResumen)d).Posicion;
        try { vegas.Transport.CursorPosition = Timecode.FromMilliseconds(t * 1000); } catch { }
    }

    void Estado(string texto, bool error)
    {
        lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave;
        lblEstado.Text = texto;
    }

    // ------------------------------------------------------- acciones

    void GuardarInforme()
    {
        LeerOpciones();
        try
        {
            File.WriteAllText(rutaInforme, resultado.Informe(vegas.Project.FilePath, opciones, total), new UTF8Encoding(false));
            Estado("✔ Informe guardado en " + rutaInforme, false);
        }
        catch (Exception ex) { Estado("No se pudo guardar el informe: " + ex.Message, true); }
    }

    void CrearMarcas()
    {
        Project p = vegas.Project;
        List<Ancla> anclas = new List<Ancla>();
        int n = 0;
        using (UndoBlock deshacer = new UndoBlock("Momentos con IA: marcas"))
        {
            foreach (Tramo t in resultado.Corte)
                if (t.Elegido) { anclas.Add(Regiones(p, t.Inicio, t.Fin, (t.Acelerar ? "Acelerar ×" + t.Velocidad.ToString("0") : "Conservar") + ": " + t.Titulo)); n++; }
            foreach (Tramo t in resultado.Momentos)
                if (t.Elegido) { anclas.Add(Marcador(p, t.Inicio, "★" + t.Puntuacion.ToString("0") + " " + t.Titulo)); n++; }
            foreach (TextoResumen t in resultado.Textos)
                if (t.Elegido) { anclas.Add(Marcador(p, t.Posicion, "TEXTO: " + t.Texto)); n++; }
            foreach (Tramo t in resultado.Shorts)
                if (t.Elegido) { anclas.Add(Regiones(p, t.Inicio, t.Fin, "SHORT: " + t.Titulo)); n++; }
        }
        Anclas.Guardar(p.FilePath, anclas);
        Estado("✔ " + n + " regiones y marcadores creados y anclados a sus clips (Ctrl+Z los quita). " +
               "Si mueves clips, ejecuta ReubicarMarcadores.", false);
    }

    static Ancla Marcador(Project p, double t, string texto)
    {
        p.Markers.Add(new Marker(Timecode.FromMilliseconds(t * 1000), texto));
        return Anclas.Crear(p, t, -1, texto);
    }

    static Ancla Regiones(Project p, double a, double b, string texto)
    {
        p.Regions.Add(new ScriptPortal.Vegas.Region(Timecode.FromMilliseconds(a * 1000), Timecode.FromMilliseconds((b - a) * 1000), texto));
        return Anclas.Crear(p, a, b, texto);
    }

    void AplicarCorte()
    {
        double fps = vegas.Project.Video.FrameRate;
        List<Rango> quitar = Editor.AjustarAFotogramas(resultado.Quitar(total), fps);
        List<Acelerado> acelerar = new List<Acelerado>();
        foreach (Acelerado a in resultado.Acelerados(quitar))
        {
            Acelerado f = new Acelerado(Math.Round(a.Inicio * fps) / fps, Math.Round(a.Fin * fps) / fps, a.Factor);
            if (f.Fin - f.Inicio >= 2 / fps) acelerar.Add(f);
        }
        double quitado = 0, ahorro = 0;
        foreach (Rango r in quitar) quitado += r.Fin - r.Inicio;
        foreach (Acelerado a in acelerar) ahorro += a.Ahorro;
        if (quitar.Count == 0 && acelerar.Count == 0) { Estado("El corte no cambia nada.", true); return; }
        bool silenciar = segAudio.Seleccion == 0;
        if (MessageBox.Show(this,
                "Se quitarán " + Formato.Tiempo(quitado) + " en " + quitar.Count + " tramos" +
                (acelerar.Count > 0 ? " y se acelerarán " + acelerar.Count + " tramos (" +
                    (silenciar ? "sin audio" : "con audio acelerado") + ")" : "") +
                ". El video quedará de " + Formato.Tiempo(total - quitado - ahorro) + ".\n\n" +
                "Se aplica en todas las pistas para mantener la sincronía (haz esto antes de poner música). " +
                "Los textos y momentos marcados quedan como marcadores anclados a sus clips.\n\n¿Aplicar? (Ctrl+Z lo deshace)",
                "Aplicar corte", MessageBoxButtons.OKCancel) != DialogResult.OK) return;

        Project p = vegas.Project;
        List<Track> todas = new List<Track>();
        foreach (Track t in p.Tracks) todas.Add(t);
        List<Ancla> anclas = new List<Ancla>();
        double trasCortar;
        using (UndoBlock deshacer = new UndoBlock("Momentos con IA: corte"))
        {
            if (quitar.Count > 0) Editor.Eliminar(p, todas, quitar, true, true, 0.02);
            trasCortar = p.Length.ToMilliseconds() / 1000.0;
            if (acelerar.Count > 0) Editor.Acelerar(p, todas, acelerar, silenciar, true, 0.02);
            foreach (TextoResumen t in resultado.Textos)
                if (t.Elegido) anclas.Add(Marcador(p, Acelerado.Posicion(Editor.PosicionTrasQuitar(t.Posicion, quitar), acelerar), "TEXTO: " + t.Texto));
            foreach (Tramo t in resultado.Momentos)
            {
                if (!t.Elegido || Dentro(t.Inicio, quitar)) continue;
                double nuevo = Acelerado.Posicion(Editor.PosicionTrasQuitar(t.Inicio, quitar), acelerar);
                anclas.Add(Marcador(p, nuevo, "★" + t.Puntuacion.ToString("0") + " " + t.Titulo));
            }
        }
        Anclas.Guardar(p.FilePath, anclas);
        double despues = p.Length.ToMilliseconds() / 1000.0;
        string aviso = "";
        if (quitar.Count > 0) aviso = Transcripcion.RegistrarCortes(p.FilePath, quitar, total, trasCortar);
        if (acelerar.Count > 0) aviso = Transcripcion.RegistrarAceleracion(p.FilePath, acelerar, trasCortar, despues);
        aplicado = true;
        btnCortar.Enabled = false;
        btnMarcar.Enabled = false;
        Estado("✔ Corte aplicado: el video dura ahora " + Formato.Tiempo(despues) + "." + aviso.Replace("\n", " ") +
               " Si lo deshaces (Ctrl+Z), al volver a abrir esta ventana la respuesta aparece lista otra vez.", false);
    }

    static bool Dentro(double t, List<Rango> rangos)
    {
        foreach (Rango r in rangos) if (t > r.Inicio && t < r.Fin) return true;
        return false;
    }
}
