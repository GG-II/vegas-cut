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

        using (VentanaMomentos v = new VentanaMomentos(vegas, t, ruta)) v.ShowDialog();
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

class VentanaMomentos : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion transcripcion;
    readonly string rutaTranscripcion, rutaIA, rutaInforme;
    readonly Configuracion config = Configuracion.Cargar();
    readonly double total;
    ResultadoIA resultado;
    OpcionesIA opciones = new OpcionesIA();
    bool cargando, aplicado;

    Segmentado segTipo = new Segmentado(new string[] { "Gameplay", "Narración", "Podcast", "Otro" });
    CampoNumero numMinutos = new CampoNumero();
    List<CampoTexto> nombres = new List<CampoTexto>();
    CampoTexto txtInstrucciones = new CampoTexto();
    Boton btnPedir = new Boton("Pedir a Gemini", EstiloBoton.Primario);
    Etiqueta lblEstado;

    Segmentado pestanas = new Segmentado(new string[] { "Corte", "Momentos", "Textos", "Resumen", "Shorts y títulos" });
    Lista lstCorte = new Lista(), lstMomentos = new Lista(), lstTextos = new Lista(), lstShorts = new Lista();
    CampoTexto txtResumen = new CampoTexto();
    Etiqueta lblCorte;

    Boton btnInforme = new Boton("Guardar informe", EstiloBoton.Secundario);
    Boton btnMarcar = new Boton("Crear regiones y marcadores", EstiloBoton.Secundario);
    Boton btnCortar = new Boton("Aplicar corte", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    static readonly string[] Tipos = { "Gameplay", "Narración", "Podcast", "Otro" };

    public VentanaMomentos(Vegas vegas, Transcripcion t, string ruta) : base("Momentos con IA", 1000)
    {
        this.vegas = vegas;
        transcripcion = t;
        rutaTranscripcion = ruta;
        string veg = vegas.Project.FilePath;
        string baseNombre = Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg));
        rutaIA = baseNombre + ".vegascut-ia.json";
        rutaInforme = baseNombre + ".vegascut-informe.md";
        total = vegas.Project.Length.ToMilliseconds() / 1000.0;

        int m = Margen, w = Ancho;
        Encabezado("Momentos con IA", "Gemini lee la transcripción y la intensidad del sonido, y sugiere qué conservar.");

        // Estado de la transcripcion
        string aviso = t.Sincronizar(total);
        if (aviso.StartsWith("Se detect")) { try { t.Guardar(ruta); } catch { } }
        int frases = t.SegmentosActuales().Count;
        Etiqueta lblTrans = Texto("Transcripción del " + t.Creada + ": " + frases + " frases · proyecto de " +
            Formato.Tiempo(total) + (aviso.Length > 0 ? "\n" + aviso : ""), Tema.Pequena,
            aviso.Length > 0 && !aviso.StartsWith("Se detect") ? Tema.AcentoHover : Tema.TextoSuave, m, 88, w, 34);

        // ---------------- Columna izquierda: lo que se pide
        int y = 130, ci = 300;
        Texto("Tipo de video", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        Pos(segTipo, m, y + 22, ci, 34);
        y += 68;
        Texto("Duración objetivo", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        numMinutos.Sufijo = "min"; numMinutos.Minimo = 1; numMinutos.Maximo = 600; numMinutos.Paso = 1;
        Pos(numMinutos, m, y + 22, 120, 36);
        y += 70;
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
        y += 6;
        Texto("Indicaciones (opcional)", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        txtInstrucciones.Multilinea = true;
        Pos(txtInstrucciones, m, y + 22, ci, 90);
        y += 122;
        Texto("Ej.: “Es el episodio 3 de una serie, que se entienda la historia” o “Conserva todas las peleas”.",
              Tema.Pequena, Tema.TextoSuave, m, y, ci, 32);
        y += 40;
        Pos(btnPedir, m, y, ci, 42);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 48, ci, 48);
        int fondoIzq = y + 100;

        // ---------------- Columna derecha: resultado
        int dx = m + ci + 24, dw = w - ci - 24, dy = 130;
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
        lblCorte = Texto("", Tema.Negrita, Tema.Texto, dx, dy + alto + 8, dw, 22);
        int fondo = Math.Max(fondoIzq, dy + alto + 40);

        Pos(btnInforme, m, fondo, 150, 40);
        Pos(btnMarcar, m + 160, fondo, 230, 40);
        Pos(btnCerrar, m + w - 300, fondo, 110, 40);
        Pos(btnCortar, m + w - 180, fondo, 180, 40);
        ClientSize = new Size(ClientSize.Width, fondo + 40 + 24);

        // Valores iniciales
        cargando = true;
        CargarOpciones();
        cargando = false;

        pestanas.Cambio += delegate { MostrarPestana(); };
        btnPedir.Click += delegate { Pedir(); };
        btnInforme.Click += delegate { GuardarInforme(); };
        btnMarcar.Click += delegate { CrearMarcas(); };
        btnCortar.Click += delegate { AplicarCorte(); };
        btnCerrar.Click += delegate { Close(); };
        lstCorte.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
        {
            if (cargando || resultado == null) return;
            ((Tramo)e.Item.Tag).Elegido = e.Item.Checked;
            ActualizarResumenCorte();
        };
        foreach (Lista l in new Lista[] { lstMomentos, lstShorts })
            l.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((Tramo)e.Item.Tag).Elegido = e.Item.Checked; };
        lstTextos.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((TextoResumen)e.Item.Tag).Elegido = e.Item.Checked; };
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts })
            l.DoubleClick += delegate (object s, EventArgs e) { IrA((ListView)s); };

        if (!config.TieneGemini)
        {
            btnPedir.Enabled = false;
            Estado("Falta la clave de Gemini: ejecuta “ConfigurarVegasCut”.", true);
        }
        CargarRespuestaGuardada();
        MostrarResultado();
    }

    void ConfigurarListas()
    {
        lstCorte.Columns.Add("Inicio", 70); lstCorte.Columns.Add("Fin", 70); lstCorte.Columns.Add("Dura", 60);
        lstCorte.Columns.Add("Tramo", 200); lstCorte.Columns.Add("Por qué", 900);
        lstMomentos.Columns.Add("Nota", 60); lstMomentos.Columns.Add("Inicio", 70); lstMomentos.Columns.Add("Fin", 70);
        lstMomentos.Columns.Add("Momento", 200); lstMomentos.Columns.Add("Por qué", 900);
        lstTextos.Columns.Add("Dónde", 70); lstTextos.Columns.Add("Texto", 280); lstTextos.Columns.Add("Qué se salta", 900);
        lstShorts.Columns.Add("Inicio", 70); lstShorts.Columns.Add("Fin", 70); lstShorts.Columns.Add("Short", 220);
        lstShorts.Columns.Add("Por qué funciona", 900);
    }

    // ------------------------------------------------------- opciones

    void CargarOpciones()
    {
        // Ultimas opciones usadas (se guardan junto a la respuesta).
        opciones.Tipo = "Gameplay";
        opciones.MinutosObjetivo = Math.Max(1, Math.Min(20, Math.Round(total / 60 / 3)));
        try
        {
            if (File.Exists(rutaIA))
            {
                object o = Json.Leer(File.ReadAllText(rutaIA, Encoding.UTF8));
                object op = Json.Obj(o, "opciones");
                if (op != null)
                {
                    opciones.Tipo = Json.Texto(op, "tipo");
                    opciones.MinutosObjetivo = Json.Numero(op, "minutos", opciones.MinutosObjetivo);
                    opciones.Instrucciones = Json.Texto(op, "instrucciones");
                }
            }
        }
        catch { }
        segTipo.Seleccion = Math.Max(0, Array.IndexOf(Tipos, opciones.Tipo));
        numMinutos.Valor = (int)opciones.MinutosObjetivo;
        txtInstrucciones.Text = opciones.Instrucciones;
    }

    void LeerOpciones()
    {
        opciones.Tipo = Tipos[segTipo.Seleccion];
        opciones.MinutosObjetivo = numMinutos.Valor;
        opciones.Instrucciones = txtInstrucciones.Text;
        foreach (CampoTexto c in nombres)
        {
            Hablante h = (Hablante)c.Tag;
            h.Nombre = c.Text.Trim().Length > 0 ? c.Text.Trim() : h.Etiqueta;
        }
    }

    // La respuesta anterior sirve mientras el proyecto no haya cambiado.
    void CargarRespuestaGuardada()
    {
        try
        {
            if (!File.Exists(rutaIA)) return;
            object o = Json.Leer(File.ReadAllText(rutaIA, Encoding.UTF8));
            double duracion = Json.Numero(o, "duracionProyecto", -1);
            if (Math.Abs(duracion - total) > 0.05)
            {
                Estado("Hay una respuesta anterior, pero el proyecto cambió desde entonces: pide una nueva.", false);
                return;
            }
            resultado = ResultadoIA.Leer(Json.Texto(o, "respuesta"), total);
            resultado.AjustarAPalabras(transcripcion.SegmentosActuales());
            Estado("Mostrando la respuesta del " + Json.Texto(o, "fecha") + " (" + Json.Texto(o, "modelo") + ").", false);
        }
        catch { }
    }

    // ------------------------------------------------------- Gemini

    void Pedir()
    {
        LeerOpciones();
        try { transcripcion.Guardar(rutaTranscripcion); } catch { } // guarda los nombres
        string mensaje = PeticionIA.Mensaje(transcripcion, total, opciones);
        string clave = config.GeminiClave, modelo = config.GeminiModelo;

        btnPedir.Enabled = false;
        DateTime inicio = DateTime.Now;
        System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        reloj.Interval = 500;
        reloj.Tick += delegate
        {
            Estado("Gemini está pensando… " + Formato.Tiempo((DateTime.Now - inicio).TotalSeconds) +
                   " (" + (mensaje.Length / 1000) + " mil caracteres enviados). Puede tardar 1 o 2 minutos.", false);
        };
        reloj.Start();

        Thread hilo = new Thread(delegate ()
        {
            string respuesta = null, error = null;
            try { respuesta = Gemini.Generar(clave, modelo, PeticionIA.Instrucciones, mensaje, true); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    reloj.Stop();
                    btnPedir.Enabled = true;
                    if (error != null) { Estado(error, true); return; }
                    Recibir(respuesta, modelo);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Recibir(string respuesta, string modelo)
    {
        try
        {
            resultado = ResultadoIA.Leer(respuesta, total);
            resultado.AjustarAPalabras(transcripcion.SegmentosActuales());
        }
        catch (Exception ex)
        {
            Estado("La respuesta de Gemini no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true);
            return;
        }

        Dictionary<string, object> guardar = new Dictionary<string, object>();
        guardar["fecha"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        guardar["modelo"] = modelo;
        guardar["duracionProyecto"] = total;
        Dictionary<string, object> op = new Dictionary<string, object>();
        op["tipo"] = opciones.Tipo;
        op["minutos"] = opciones.MinutosObjetivo;
        op["instrucciones"] = opciones.Instrucciones;
        guardar["opciones"] = op;
        guardar["respuesta"] = respuesta;
        try { File.WriteAllText(rutaIA, Json.Escribir(guardar), new UTF8Encoding(false)); } catch { }

        aplicado = false;
        Estado("✔ Listo. Revisa las pestañas; desmarca lo que no quieras.", false);
        MostrarResultado();
    }

    // ------------------------------------------------------- mostrar

    static string T(double s) { return Formato.TiempoPreciso(s); }

    void MostrarResultado()
    {
        cargando = true;
        foreach (Lista l in new Lista[] { lstCorte, lstMomentos, lstTextos, lstShorts }) l.Items.Clear();
        bool hay = resultado != null;
        if (hay)
        {
            foreach (Tramo t in resultado.Corte)
                Fila(lstCorte, t, t.Elegido, T(t.Inicio), T(t.Fin), Formato.Tiempo(t.Duracion), t.Titulo, t.Motivo);
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
        btnMarcar.Enabled = hay;
        btnCortar.Enabled = hay && !aplicado;
        ActualizarResumenCorte();
        MostrarPestana();
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
        lblCorte.Text = "El corte conserva " + Formato.Tiempo(resultado.DuracionCorte) + " de " + Formato.Tiempo(total) +
                        " (objetivo " + numMinutos.Valor + " min) · doble clic: ir a ese punto en Vegas";
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
        int n = 0;
        using (UndoBlock deshacer = new UndoBlock("Momentos con IA: marcas"))
        {
            foreach (Tramo t in resultado.Corte)
                if (t.Elegido) { Region(p, t.Inicio, t.Fin, "Conservar: " + t.Titulo); n++; }
            foreach (Tramo t in resultado.Momentos)
                if (t.Elegido) { Marcador(p, t.Inicio, "★" + t.Puntuacion.ToString("0") + " " + t.Titulo); n++; }
            foreach (TextoResumen t in resultado.Textos)
                if (t.Elegido) { Marcador(p, t.Posicion, "TEXTO: " + t.Texto); n++; }
            foreach (Tramo t in resultado.Shorts)
                if (t.Elegido) { Region(p, t.Inicio, t.Fin, "SHORT: " + t.Titulo); n++; }
        }
        Estado("✔ " + n + " regiones y marcadores creados (Ctrl+Z los quita).", false);
    }

    static void Marcador(Project p, double t, string texto)
    {
        p.Markers.Add(new Marker(Timecode.FromMilliseconds(t * 1000), texto));
    }

    static void Region(Project p, double a, double b, string texto)
    {
        p.Regions.Add(new ScriptPortal.Vegas.Region(Timecode.FromMilliseconds(a * 1000), Timecode.FromMilliseconds((b - a) * 1000), texto));
    }

    void AplicarCorte()
    {
        double fps = vegas.Project.Video.FrameRate;
        List<Rango> quitar = Editor.AjustarAFotogramas(resultado.Quitar(total), fps);
        double quitado = 0;
        foreach (Rango r in quitar) quitado += r.Fin - r.Inicio;
        if (quitar.Count == 0) { Estado("El corte no quita nada.", true); return; }
        if (MessageBox.Show(this,
                "Se quitarán " + Formato.Tiempo(quitado) + " en " + quitar.Count + " tramos y el video quedará de " +
                Formato.Tiempo(total - quitado) + ".\n\nSe corta en todas las pistas para mantener la sincronía. " +
                "Los textos y momentos marcados quedan como marcadores en su nuevo lugar.\n\n¿Aplicar? (Ctrl+Z lo deshace)",
                "Aplicar corte", MessageBoxButtons.OKCancel) != DialogResult.OK) return;

        Project p = vegas.Project;
        List<Track> todas = new List<Track>();
        foreach (Track t in p.Tracks) todas.Add(t);
        using (UndoBlock deshacer = new UndoBlock("Momentos con IA: corte"))
        {
            Editor.Eliminar(p, todas, quitar, true, true, 0.02);
            foreach (TextoResumen t in resultado.Textos)
                if (t.Elegido) Marcador(p, Editor.PosicionTrasQuitar(t.Posicion, quitar), "TEXTO: " + t.Texto);
            foreach (Tramo t in resultado.Momentos)
            {
                if (!t.Elegido) continue;
                double nuevo = Editor.PosicionTrasQuitar(t.Inicio, quitar);
                if (!Dentro(t.Inicio, quitar)) Marcador(p, nuevo, "★" + t.Puntuacion.ToString("0") + " " + t.Titulo);
            }
        }
        double despues = p.Length.ToMilliseconds() / 1000.0;
        string aviso = Transcripcion.RegistrarCortes(p.FilePath, quitar, total, despues);
        aplicado = true;
        btnCortar.Enabled = false;
        Estado("✔ Corte aplicado: el video dura ahora " + Formato.Tiempo(despues) + "." + aviso.Replace("\n", " "), false);
    }

    static bool Dentro(double t, List<Rango> rangos)
    {
        foreach (Rango r in rangos) if (t > r.Inicio && t < r.Fin) return true;
        return false;
    }
}
