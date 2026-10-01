// MomentosIA.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Envia a Gemini la transcripcion del proyecto (hecha con Transcribir.cs) y la
// intensidad del sonido, y recibe: resumen, secciones, momentos destacados, un
// corte sugerido a la duracion que pidas, textos de resumen para lo que se
// salta, ideas de Shorts y titulos. Todo se revisa en listas con casillas y se
// puede convertir en regiones y marcadores, aplicar el corte o guardar un
// informe. Solo se envia texto: el audio no sale de la PC.
//
// Requiere la clave de Gemini en ConfigurarVegasCut.cs.
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/momentos/Momentos.cs ----

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string veg = vegas.Project.FilePath;
        string ruta = Transcripcion.RutaPara(veg);
        if (ruta == null || !File.Exists(ruta))
        {
            MessageBox.Show("Este proyecto a\u00fan no tiene transcripci\u00f3n.\n\nEjecuta primero \u201cTranscribir\u201d.", "Momentos con IA");
            return;
        }
        Transcripcion t;
        try { t = Transcripcion.Cargar(ruta); }
        catch (Exception ex) { MessageBox.Show("No se pudo leer la transcripci\u00f3n: " + ex.Message, "Momentos con IA"); return; }

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
        Encabezado("Reglas del canal", "Se aplican siempre, en todos los videos. Una regla por l\u00ednea.");
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

    Segmentado segTipo = new Segmentado(new string[] { "Gameplay", "Narraci\u00f3n", "Podcast", "Otro" });
    CampoNumero numMin = new CampoNumero(), numMax = new CampoNumero();
    Segmentado segAcelerar = new Segmentado(new string[] { "Cortar", "Acelerar" });
    Segmentado segAudio = new Segmentado(new string[] { "Mudo", "Acelerado" });
    List<CampoTexto> nombres = new List<CampoTexto>();
    CampoTexto txtInstrucciones = new CampoTexto();
    Boton btnReglas = new Boton("Reglas del canal\u2026", EstiloBoton.Secundario);
    Boton btnContexto = new Boton("Episodios anteriores\u2026", EstiloBoton.Secundario);
    Etiqueta lblContexto;
    Combo comboModelo = new Combo(true);
    Etiqueta lblModelo;
    Boton btnPedir = new Boton("Pedir a Gemini", EstiloBoton.Primario);
    Etiqueta lblEstado;

    Combo comboHistorial = new Combo();
    Segmentado pestanas = new Segmentado(new string[] { "Corte", "Momentos", "Textos", "Resumen", "Shorts y t\u00edtulos" });
    Lista lstCorte = new Lista(), lstMomentos = new Lista(), lstTextos = new Lista(), lstShorts = new Lista();
    CampoTexto txtResumen = new CampoTexto();
    Etiqueta lblCorte;

    Boton btnInforme = new Boton("Guardar informe", EstiloBoton.Secundario);
    Boton btnMarcar = new Boton("Crear regiones y marcadores", EstiloBoton.Secundario);
    Boton btnCortar = new Boton("Aplicar corte", EstiloBoton.Primario);
    Boton btnFijar = new Boton("Conservar selecci\u00f3n", EstiloBoton.Secundario);
    List<Tramo> fijos = new List<Tramo>();   // tramos elegidos a mano (selecci\u00f3n de tiempo)
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    static readonly string[] Tipos = { "Gameplay", "Narraci\u00f3n", "Podcast", "Otro" };

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
        Encabezado("Momentos con IA", "Gemini lee la transcripci\u00f3n y la intensidad del sonido, y sugiere qu\u00e9 conservar.");

        // Estado de la transcripcion
        string aviso = t.Sincronizar(total);
        if (aviso.StartsWith("Se detect")) { try { t.Guardar(ruta); } catch { } }
        int frases = t.SegmentosActuales().Count;
        Texto("Transcripci\u00f3n del " + t.Creada + ": " + frases + " frases \u00b7 proyecto de " +
            Formato.Tiempo(total) + (aviso.Length > 0 ? "\n" + aviso : ""), Tema.Pequena,
            aviso.Length > 0 && !aviso.StartsWith("Se detect") ? Tema.AcentoHover : Tema.TextoSuave, m, 88, w, 34);

        // ---------------- Columna izquierda: lo que se pide
        int y = 130, ci = 320;
        Texto("Tipo de video", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        Pos(segTipo, m, y + 22, ci, 34);
        y += 66;
        Texto("Duraci\u00f3n del corte", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        foreach (CampoNumero n in new CampoNumero[] { numMin, numMax })
        {
            n.Sufijo = "min"; n.Minimo = 1; n.Maximo = 600; n.Paso = 1;
        }
        Pos(numMin, m, y + 22, 92, 36);
        Texto("a", Tema.Normal, Tema.TextoSuave, m + 98, y + 30, 16, 20);
        Pos(numMax, m + 118, y + 22, 92, 36);
        Texto("m\u00ednimo y m\u00e1ximo", Tema.Pequena, Tema.TextoSuave, m + 218, y + 30, ci - 218, 20);
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
        lblCorte = Texto("", Tema.Negrita, Tema.Texto, dx, dy + alto + 8, dw, 22);
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
            Estado("Falta la clave de Gemini: ejecuta \u201cConfigurarVegasCut\u201d.", true);
        }
        MostrarResultado();
    }

    void ConfigurarListas()
    {
        lstCorte.Columns.Add("Inicio", 70); lstCorte.Columns.Add("Fin", 70); lstCorte.Columns.Add("Dura", 52);
        lstCorte.Columns.Add("Velocidad", 84); lstCorte.Columns.Add("Imp.", 40); lstCorte.Columns.Add("Tramo", 180);
        lstCorte.Columns.Add("Por qu\u00e9", 900);
        lstMomentos.Columns.Add("Nota", 60); lstMomentos.Columns.Add("Inicio", 70); lstMomentos.Columns.Add("Fin", 70);
        lstMomentos.Columns.Add("Momento", 200); lstMomentos.Columns.Add("Por qu\u00e9", 900);
        lstTextos.Columns.Add("D\u00f3nde", 70); lstTextos.Columns.Add("Texto", 280); lstTextos.Columns.Add("Qu\u00e9 se salta", 900);
        lstShorts.Columns.Add("Inicio", 70); lstShorts.Columns.Add("Fin", 70); lstShorts.Columns.Add("Short", 220);
        lstShorts.Columns.Add("Por qu\u00e9 funciona", 900);
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
            try { config.Guardar(); Estado("\u2714 Reglas del canal guardadas.", false); }
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
            ? "Lite es m\u00e1s barato pero sigue peor las reglas y la duraci\u00f3n. Para cortes largos usa flash o pro."
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
                texto = Json.Texto(o, "fecha") + "  \u00b7  " + Json.Texto(o, "modelo") + "  \u00b7  " + mn + "\u2013" + mx + " min" +
                        (Math.Abs(Json.Numero(o, "duracionProyecto", -1) - total) > 0.5 ? "  \u00b7  (proyecto distinto)" : "");
            }
            catch { }
            comboHistorial.Items.Add(texto);
        }
        if (comboHistorial.Items.Count == 0) comboHistorial.Items.Add("Todav\u00eda no hay respuestas");
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
                if (n > 0) nota = "revisi\u00f3n: " + n + (n == 1 ? " tramo quitado o recortado" : " tramos quitados o recortados");
            }
            catch { }
        }
        // Los tramos fijos se agregan despues de la revision (no cambian sus indices).
        if (Math.Abs(duracion - total) <= 0.5)
            foreach (Tramo f in fijos) r.AgregarFijo(f.Inicio, f.Fin, f.Titulo);
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
                Estado("Respuesta del " + Json.Texto(o, "fecha") + ": el proyecto cambi\u00f3 desde entonces (duraba " +
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
        string paso = "Preparando\u2026";
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
        Estado("\u2714 Listo. Revisa las pesta\u00f1as; desmarca lo que no quieras." +
               (nota.Length > 0 ? " Ajustes autom\u00e1ticos: " + nota + "." : ""), false);
        MostrarResultado();
    }

    // ------------------------------------------------------- mostrar

    static string T(double s) { return Formato.TiempoPreciso(s); }

    static string PorQue(Tramo t)
    {
        return t.Nota.Length > 0 ? "\u26a0 " + t.Nota + " \u00b7 " + t.Motivo : t.Motivo;
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
                sb.Append(T(t.Inicio) + "\u2013" + T(t.Fin) + "  " + t.Titulo + ": " + t.Motivo + "\r\n");
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
        return t.Acelerar ? "\u23e9 \u00d7" + t.Velocidad.ToString("0") + " (" + Formato.Tiempo(t.DuracionFinal) + ")" : "normal";
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
            lblCorte.Text = "T\u00edtulos: " + String.Join("  \u00b7  ", resultado.Titulos.ToArray());
        else ActualizarResumenCorte();
    }

    void ActualizarResumenCorte()
    {
        if (pestanas.Seleccion == 4) return;
        if (resultado == null) { lblCorte.Text = "Pide una sugerencia a Gemini para ver los resultados aqu\u00ed."; return; }
        double d = resultado.DuracionCorte;
        bool fuera = d < numMin.Valor * 60 - 0.5 || d > numMax.Valor * 60 + 0.5;
        lblCorte.ForeColor = fuera ? Tema.AcentoHover : Tema.Texto;
        lblCorte.Text = "Conserva " + Formato.Tiempo(d) + " de " + Formato.Tiempo(total) + " (" + numMin.Valor + "\u2013" +
                        numMax.Valor + " min" + (fuera ? ", fuera del rango" : "") + ") \u00b7 clic en Velocidad: cambiarla \u00b7 doble clic: ir";
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
            Estado("Primero selecciona en la l\u00ednea de tiempo el tramo que quieres completo (arrastra sobre la regla de tiempo) y luego pulsa \u201cConservar selecci\u00f3n\u201d.", true);
            return;
        }
        if (resultado != null && !vigente)
        {
            Estado("Esta respuesta es de antes de cambiar el proyecto. Deshaz el corte (Ctrl+Z), vuelve a abrir MomentosIA y elige la selecci\u00f3n.", true);
            return;
        }
        double b = Math.Min(total, a + largo);
        string titulo = "Elegido a mano (" + Formato.Tiempo(a) + "\u2013" + Formato.Tiempo(b) + ")";
        fijos = TramosFijos.Agregar(fijos, a, b, titulo);
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos);
        string nota = "";
        if (resultado != null)
        {
            resultado.AgregarFijo(a, b, titulo);
            nota = resultado.AjustarDuracion(Math.Min(numMin.Valor, numMax.Valor) * 60, Math.Max(numMin.Valor, numMax.Valor) * 60);
            MostrarResultado();
        }
        Estado("\u2714 " + Formato.Tiempo(a) + "\u2013" + Formato.Tiempo(b) + " se conserva completo" +
               (resultado != null ? " en el corte" : "") + " y se le avisa a Gemini en las pr\u00f3ximas peticiones." +
               (nota.Length > 0 ? " Ajustes: " + nota + "." : "") + " Desm\u00e1rcalo en la lista para quitarlo.", false);
    }

    void QuitarFijo(Tramo t)
    {
        t.Fijo = false;
        t.Nota = "Ya no es fijo";
        fijos.RemoveAll(delegate (Tramo f) { return f.Inicio < t.Fin - 0.05 && f.Fin > t.Inicio + 0.05; });
        TramosFijos.Guardar(vegas.Project.FilePath, total, fijos);
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
            Estado("\u2714 Informe guardado en " + rutaInforme, false);
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
                if (t.Elegido) { anclas.Add(Regiones(p, t.Inicio, t.Fin, (t.Acelerar ? "Acelerar \u00d7" + t.Velocidad.ToString("0") : "Conservar") + ": " + t.Titulo)); n++; }
            foreach (Tramo t in resultado.Momentos)
                if (t.Elegido) { anclas.Add(Marcador(p, t.Inicio, "\u2605" + t.Puntuacion.ToString("0") + " " + t.Titulo)); n++; }
            foreach (TextoResumen t in resultado.Textos)
                if (t.Elegido) { anclas.Add(Marcador(p, t.Posicion, "TEXTO: " + t.Texto)); n++; }
            foreach (Tramo t in resultado.Shorts)
                if (t.Elegido) { anclas.Add(Regiones(p, t.Inicio, t.Fin, "SHORT: " + t.Titulo)); n++; }
        }
        Anclas.Guardar(p.FilePath, anclas);
        Estado("\u2714 " + n + " regiones y marcadores creados y anclados a sus clips (Ctrl+Z los quita). " +
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
                "Se quitar\u00e1n " + Formato.Tiempo(quitado) + " en " + quitar.Count + " tramos" +
                (acelerar.Count > 0 ? " y se acelerar\u00e1n " + acelerar.Count + " tramos (" +
                    (silenciar ? "sin audio" : "con audio acelerado") + ")" : "") +
                ". El video quedar\u00e1 de " + Formato.Tiempo(total - quitado - ahorro) + ".\n\n" +
                "Se aplica en todas las pistas para mantener la sincron\u00eda (haz esto antes de poner m\u00fasica). " +
                "Los textos y momentos marcados quedan como marcadores anclados a sus clips.\n\n\u00bfAplicar? (Ctrl+Z lo deshace)",
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
                anclas.Add(Marcador(p, nuevo, "\u2605" + t.Puntuacion.ToString("0") + " " + t.Titulo));
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
        Estado("\u2714 Corte aplicado: el video dura ahora " + Formato.Tiempo(despues) + "." + aviso.Replace("\n", " ") +
               " Si lo deshaces (Ctrl+Z), al volver a abrir esta ventana la respuesta aparece lista otra vez.", false);
    }

    static bool Dentro(double t, List<Rango> rangos)
    {
        foreach (Rango r in rangos) if (t > r.Inicio && t < r.Fin) return true;
        return false;
    }
}

// ---- src/momentos/LogicaMomentos.cs ----

// =====================================================================
// Momentos con IA: que se le pide a Gemini y como se lee la respuesta.
// Todos los tiempos estan en la linea de tiempo ACTUAL (despues de los
// cortes que ya se hayan hecho).
//
// Videos cortos: una sola peticion. Videos largos (mas de ~35 min): por
// partes de ~20 min (cada parte elige candidatos y resume lo que pasa) y una
// pasada final que arma el corte completo cuidando la historia.
// =====================================================================

public class Tramo
{
    public double Inicio, Fin, Puntuacion;
    public string Titulo = "", Motivo = "";
    public bool Elegido = true;
    public string Nota = "";       // por que se marco o desmarco solo (revision, duracion)
    public bool PorRevision;       // desmarcado por incumplir reglas: no se vuelve a marcar solo
    public bool Acelerar;          // en el corte: se conserva pero mas rapido
    public double Velocidad = 1;   // 2 = el doble de rapido
    public bool Fijo;              // lo elegiste tu: ni la IA ni los ajustes lo quitan

    public double Duracion { get { return Fin - Inicio; } }
    public object MemberwiseCopia() { return MemberwiseClone(); }
    // Lo que dura en el video final.
    public double DuracionFinal { get { return Acelerar ? Duracion / Velocidad : Duracion; } }
}

public class TextoResumen
{
    public double Posicion;
    public string Texto = "", Motivo = "";
    public bool Elegido = true;
}

public class OpcionesIA
{
    public string Tipo = "Gameplay";
    public double MinutosMin = 11, MinutosMax = 15;
    public double MinutosObjetivo { get { return (MinutosMin + MinutosMax) / 2; } }
    public string ReglasCanal = PeticionIA.ReglasPorDefecto;
    public string Contexto = "";   // resumenes de episodios anteriores (opcional)
    public string Instrucciones = "";
    public bool PermitirAcelerar = true;   // transiciones aceleradas en vez de cortadas
    public bool SilenciarAcelerado = true; // audio mudo en lo acelerado
    public List<Tramo> Fijos = new List<Tramo>(); // tramos que el editor ya eligio
}

public class ResultadoIA
{
    public string Resumen = "";
    public List<Tramo> Secciones = new List<Tramo>();
    public List<Tramo> Momentos = new List<Tramo>();
    public List<Tramo> Corte = new List<Tramo>();
    public List<TextoResumen> Textos = new List<TextoResumen>();
    public List<Tramo> Shorts = new List<Tramo>();
    public List<string> Titulos = new List<string>();
    // Tramos que propusieron las partes (videos largos): sirven para completar
    // el corte si queda corto.
    public List<Tramo> Candidatos = new List<Tramo>();

    public double DuracionCorte
    {
        get
        {
            double d = 0;
            foreach (Tramo t in Corte) if (t.Elegido) d += t.DuracionFinal;
            return d;
        }
    }

    public static double LimitarVelocidad(double v)
    {
        if (double.IsNaN(v) || v < 1.5) return 2;
        return Math.Min(4, Math.Round(v));
    }

    static List<Tramo> Tramos(object o, string clave, double total)
    {
        List<Tramo> r = new List<Tramo>();
        foreach (object x in Json.Lista(o, clave))
        {
            Tramo t = new Tramo();
            t.Inicio = Math.Max(0, Json.Numero(x, "inicio", 0));
            t.Fin = Math.Min(total, Json.Numero(x, "fin", 0));
            t.Puntuacion = Json.Numero(x, "puntuacion", Json.Numero(x, "importancia", 0));
            t.Titulo = Json.Texto(x, "titulo");
            t.Motivo = Json.Texto(x, "motivo");
            if (t.Motivo.Length == 0) t.Motivo = Json.Texto(x, "descripcion");
            if (t.Motivo.Length == 0) t.Motivo = Json.Texto(x, "gancho");
            t.Acelerar = Json.Texto(x, "accion").ToLowerInvariant().StartsWith("aceler");
            t.Velocidad = t.Acelerar ? LimitarVelocidad(Json.Numero(x, "velocidad", 3)) : 1;
            if (t.Fin - t.Inicio >= 0.2) r.Add(t);
        }
        r.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
        return r;
    }

    // Lee la respuesta (JSON). "total" es la duracion actual del proyecto.
    public static ResultadoIA Leer(string json, double total)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        ResultadoIA r = new ResultadoIA();
        r.Resumen = Json.Texto(o, "resumen");
        r.Secciones = Tramos(o, "secciones", total);
        r.Momentos = Tramos(o, "momentos", total);
        r.Momentos.Sort(delegate (Tramo a, Tramo b) { return b.Puntuacion.CompareTo(a.Puntuacion); });
        r.Shorts = Tramos(o, "shorts", total);
        r.Corte = UnirSolapados(Tramos(o, "corte", total));
        r.Candidatos = Tramos(o, "candidatos", total);
        foreach (object x in Json.Lista(o, "textos"))
        {
            TextoResumen t = new TextoResumen();
            t.Posicion = Math.Max(0, Math.Min(total, Json.Numero(x, "posicion", 0)));
            t.Texto = Json.Texto(x, "texto");
            t.Motivo = Json.Texto(x, "motivo");
            if (t.Texto.Length > 0) r.Textos.Add(t);
        }
        r.Textos.Sort(delegate (TextoResumen a, TextoResumen b) { return a.Posicion.CompareTo(b.Posicion); });
        foreach (object x in Json.Lista(o, "titulos"))
            if (x is string && ((string)x).Length > 0) r.Titulos.Add((string)x);
        return r;
    }

    // Une tramos que se tocan con la misma accion; si se enciman con distinta
    // accion, el segundo empieza donde termina el primero.
    static List<Tramo> UnirSolapados(List<Tramo> l)
    {
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in l)
        {
            Tramo u = r.Count > 0 ? r[r.Count - 1] : null;
            if (u != null && t.Inicio <= u.Fin + 0.05)
            {
                if (u.Acelerar == t.Acelerar && u.Velocidad == t.Velocidad)
                {
                    u.Fin = Math.Max(u.Fin, t.Fin);
                    if (t.Titulo.Length > 0 && u.Titulo.IndexOf(t.Titulo) < 0) u.Titulo += " / " + t.Titulo;
                    continue;
                }
                t.Inicio = u.Fin;
                if (t.Fin - t.Inicio < 0.2) continue;
            }
            r.Add(t);
        }
        return r;
    }

    // Aplica la revision: {"tramos":[{"indice":n,"quitar":bool,"inicio":s,"fin":s,"motivo":"..."}]}.
    // Devuelve cuantos tramos cambio.
    public int AplicarRevision(string json)
    {
        int cambios = 0;
        List<Tramo> nuevos = new List<Tramo>();
        object o = Json.Leer(Gemini.QuitarCercas(json));
        foreach (object x in Json.Lista(o, "tramos"))
        {
            int i = (int)Json.Numero(x, "indice", -1);
            if (i < 0 || i >= Corte.Count) continue;
            Tramo t = Corte[i];
            if (t.Fijo) continue;
            string motivo = Json.Texto(x, "motivo");
            object quitar;
            Dictionary<string, object> d = x as Dictionary<string, object>;
            if (d != null && d.TryGetValue("quitar", out quitar) && quitar is bool && (bool)quitar)
            {
                // Si dice que parte quitar y es solo un pedazo del tramo, se
                // quita ese pedazo y el resto se queda.
                double qa = Json.Numero(x, "inicio", -1), qb = Json.Numero(x, "fin", -1);
                if (qb - qa >= 0.5)
                {
                    qa = Math.Max(qa, t.Inicio); qb = Math.Min(qb, t.Fin);
                    if (qb - qa < 0.5) continue; // no toca lo que queda del tramo
                    bool alInicio = qa <= t.Inicio + 0.5, alFinal = qb >= t.Fin - 0.5;
                    if (!(alInicio && alFinal))
                    {
                        string nota = "Recortado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                        if (alInicio) t.Inicio = qb;
                        else if (alFinal) t.Fin = qa;
                        else
                        {
                            Tramo resto = Pedazo(t, qb, t.Fin);
                            resto.Nota = nota;
                            nuevos.Add(resto);
                            t.Fin = qa;
                        }
                        t.Nota = nota;
                        cambios++;
                        continue;
                    }
                }
                t.Elegido = false;
                t.PorRevision = true;
                t.Nota = "Quitado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                cambios++;
                continue;
            }
            // Recorte: conservar solo una parte del tramo.
            double a = Json.Numero(x, "inicio", t.Inicio), b = Json.Numero(x, "fin", t.Fin);
            if ((a > t.Inicio + 0.5 || b < t.Fin - 0.5) && a >= t.Inicio - 0.01 && b <= t.Fin + 0.01 && b - a >= 2)
            {
                t.Inicio = a; t.Fin = b;
                t.Nota = "Recortado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                cambios++;
            }
        }
        if (nuevos.Count > 0)
        {
            Corte.AddRange(nuevos);
            Corte.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
        }
        return cambios;
    }

    // Agrega un tramo elegido a mano. Lo que la IA tenia adentro se absorbe;
    // lo que sobresale se conserva recortado.
    public void AgregarFijo(double a, double b, string titulo)
    {
        if (b - a < 0.5) return;
        Tramo n = new Tramo();
        n.Inicio = a; n.Fin = b; n.Puntuacion = 10; n.Fijo = true;
        n.Titulo = titulo;
        n.Motivo = "Lo elegiste t\u00fa: se conserva completo.";
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in Corte)
        {
            if (t.Fin <= a + 0.05 || t.Inicio >= b - 0.05) { r.Add(t); continue; }
            if (t.Fijo) { n.Inicio = Math.Min(n.Inicio, t.Inicio); n.Fin = Math.Max(n.Fin, t.Fin); continue; }
            if (t.Inicio < a - 0.5) r.Add(Pedazo(t, t.Inicio, a));
            if (t.Fin > b + 0.5) r.Add(Pedazo(t, b, t.Fin));
        }
        r.Add(n);
        r.Sort(delegate (Tramo x, Tramo y) { return x.Inicio.CompareTo(y.Inicio); });
        Corte = r;
    }

    static Tramo Pedazo(Tramo t, double a, double b)
    {
        Tramo p = (Tramo)t.MemberwiseCopia();
        p.Inicio = a; p.Fin = b;
        return p;
    }

    bool SeEncima(Tramo c)
    {
        foreach (Tramo t in Corte)
            if (t != c && t.Elegido && c.Inicio < t.Fin - 0.05 && c.Fin > t.Inicio + 0.05) return true;
        return false;
    }

    // Deja el corte entre el minimo y el maximo (segundos). Si sobra, desmarca
    // los tramos de menor importancia (nunca el primero ni el ultimo); si falta,
    // vuelve a marcar tramos desmarcados por duracion o agrega candidatos.
    // Devuelve un resumen de lo que hizo ("" si no hizo nada).
    public string AjustarDuracion(double minimo, double maximo)
    {
        int quitados = 0, agregados = 0;
        while (DuracionCorte > maximo + 0.5)
        {
            List<Tramo> elegidos = Corte.FindAll(delegate (Tramo t) { return t.Elegido; });
            Tramo peor = null;
            for (int i = 1; i < elegidos.Count - 1; i++)
            {
                Tramo t = elegidos[i];
                if (t.Fijo) continue;
                if (peor == null || t.Puntuacion < peor.Puntuacion ||
                    (t.Puntuacion == peor.Puntuacion && t.DuracionFinal > peor.DuracionFinal)) peor = t;
            }
            if (peor == null) break;
            peor.Elegido = false;
            peor.Nota = "Desmarcado para no pasar del m\u00e1ximo (importancia " + peor.Puntuacion.ToString("0") + ")";
            quitados++;
        }
        while (DuracionCorte < minimo - 0.5)
        {
            Tramo mejor = null;
            bool nuevo = false;
            foreach (Tramo t in Corte)
                if (!t.Elegido && !t.PorRevision && !SeEncima(t) && DuracionCorte + t.DuracionFinal <= maximo + 0.5 &&
                    (mejor == null || t.Puntuacion > mejor.Puntuacion)) mejor = t;
            if (mejor == null)
                foreach (Tramo c in Candidatos)
                    if (!Corte.Contains(c) && !SeEncima(c) && DuracionCorte + c.DuracionFinal <= maximo + 0.5 &&
                        (mejor == null || c.Puntuacion > mejor.Puntuacion)) { mejor = c; nuevo = true; }
            if (mejor == null) break;
            mejor.Elegido = true;
            mejor.Nota = "Agregado para llegar al m\u00ednimo";
            if (nuevo)
            {
                Corte.Add(mejor);
                Corte.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
            }
            agregados++;
        }
        List<string> partes = new List<string>();
        if (quitados > 0) partes.Add(quitados + (quitados == 1 ? " tramo desmarcado" : " tramos desmarcados") + " por pasar del m\u00e1ximo");
        if (agregados > 0) partes.Add(agregados + (agregados == 1 ? " tramo agregado" : " tramos agregados") + " para llegar al m\u00ednimo");
        return String.Join("; ", partes.ToArray());
    }

    // Lleva los bordes del corte al inicio/fin de la palabra que cortarian,
    // para no partir palabras a la mitad.
    public void AjustarAPalabras(List<Segmento> segmentos)
    {
        List<Palabra> palabras = new List<Palabra>();
        foreach (Segmento s in segmentos) palabras.AddRange(s.Palabras);
        foreach (Tramo t in Corte)
        {
            foreach (Palabra p in palabras)
            {
                if (t.Inicio > p.Inicio && t.Inicio < p.Fin) t.Inicio = p.Inicio;
                if (t.Fin > p.Inicio && t.Fin < p.Fin) t.Fin = p.Fin;
            }
        }
        Corte = UnirSolapados(Corte);
    }

    // Lo que se quita para quedarse solo con los tramos elegidos del corte.
    public List<Rango> Quitar(double total)
    {
        List<Rango> r = new List<Rango>();
        double cursor = 0;
        foreach (Tramo t in Corte)
        {
            if (!t.Elegido) continue;
            if (t.Inicio - cursor > 0.01) r.Add(new Rango(cursor, t.Inicio));
            cursor = Math.Max(cursor, t.Fin);
        }
        if (total - cursor > 0.01) r.Add(new Rango(cursor, total));
        return r;
    }

    // Instante despues de quitar los rangos (si cae adentro, queda en el corte).
    public static double TrasQuitar(double t, List<Rango> quitados)
    {
        double q = 0;
        foreach (Rango r in quitados)
        {
            if (t >= r.Fin) q += r.Fin - r.Inicio;
            else if (t > r.Inicio) q += t - r.Inicio;
        }
        return t - q;
    }

    // Tramos a acelerar, ya en la linea de tiempo que queda despues de quitar.
    public List<Acelerado> Acelerados(List<Rango> quitados)
    {
        List<Acelerado> r = new List<Acelerado>();
        foreach (Tramo t in Corte)
            if (t.Elegido && t.Acelerar && t.Velocidad > 1)
                r.Add(new Acelerado(TrasQuitar(t.Inicio, quitados), TrasQuitar(t.Fin, quitados), t.Velocidad));
        return r;
    }

    // ------------------------------------------------------------ informe

    public string Informe(string proyecto, OpcionesIA op, double total)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("# " + Path.GetFileNameWithoutExtension(proyecto) + "\n\n");
        sb.Append("Generado con vegas-cut y Gemini el " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                  ". Tipo: " + op.Tipo + ". Objetivo: " + op.MinutosMin + "\u2013" + op.MinutosMax + " min. Duraci\u00f3n original: " +
                  Formato.Tiempo(total) + ".\n\n");
        sb.Append("## Resumen\n\n" + Resumen + "\n\n");
        if (Secciones.Count > 0)
        {
            sb.Append("## Secciones\n\n");
            foreach (Tramo t in Secciones)
                sb.Append("- **" + Formato.Tiempo(t.Inicio) + "\u2013" + Formato.Tiempo(t.Fin) + " " + t.Titulo + "**: " + t.Motivo + "\n");
            sb.Append("\n");
        }
        sb.Append("## Corte sugerido (" + Formato.Tiempo(DuracionCorte) + ")\n\n");
        foreach (Tramo t in Corte)
            sb.Append("- [" + (t.Elegido ? "x" : " ") + "] " + Formato.Tiempo(t.Inicio) + "\u2013" + Formato.Tiempo(t.Fin) +
                      " (" + Formato.Tiempo(t.Duracion) + (t.Acelerar ? ", acelerado \u00d7" + t.Velocidad + " \u2192 " + Formato.Tiempo(t.DuracionFinal) : "") +
                      ") **" + t.Titulo + "**: " + t.Motivo + (t.Nota.Length > 0 ? " _(" + t.Nota + ")_" : "") + "\n");
        sb.Append("\n## Momentos destacados\n\n");
        foreach (Tramo t in Momentos)
            sb.Append("- " + t.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + "/10 \u00b7 " + Formato.Tiempo(t.Inicio) + "\u2013" +
                      Formato.Tiempo(t.Fin) + " **" + t.Titulo + "**: " + t.Motivo + "\n");
        if (Textos.Count > 0)
        {
            sb.Append("\n## Textos de resumen\n\n");
            foreach (TextoResumen t in Textos)
                sb.Append("- " + Formato.Tiempo(t.Posicion) + ": \u201c" + t.Texto + "\u201d" + (t.Motivo.Length > 0 ? " (" + t.Motivo + ")" : "") + "\n");
        }
        if (Shorts.Count > 0)
        {
            sb.Append("\n## Ideas para Shorts\n\n");
            foreach (Tramo t in Shorts)
                sb.Append("- " + Formato.Tiempo(t.Inicio) + "\u2013" + Formato.Tiempo(t.Fin) + " **" + t.Titulo + "**: " + t.Motivo + "\n");
        }
        if (Titulos.Count > 0)
        {
            sb.Append("\n## T\u00edtulos sugeridos\n\n");
            foreach (string t in Titulos) sb.Append("- " + t + "\n");
        }
        return sb.ToString();
    }
}

// =====================================================================
// Textos que se envian a Gemini
// =====================================================================

// Tramos fijos del proyecto (<proyecto>.vegascut-fijos.json), en tiempos de
// la linea de tiempo de cuando se eligieron. Solo valen mientras el proyecto
// dure lo mismo (antes de aplicar el corte).
public static class TramosFijos
{
    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-fijos.json");
    }

    public static List<Tramo> Cargar(string veg, double duracion)
    {
        List<Tramo> r = new List<Tramo>();
        string ruta = RutaPara(veg);
        try
        {
            if (ruta == null || !File.Exists(ruta)) return r;
            object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
            if (Math.Abs(Json.Numero(o, "duracionProyecto", -1) - duracion) > 0.5) return r;
            foreach (object x in Json.Lista(o, "fijos"))
            {
                Tramo t = new Tramo();
                t.Inicio = Json.Numero(x, "inicio", 0); t.Fin = Json.Numero(x, "fin", 0);
                t.Titulo = Json.Texto(x, "titulo"); t.Fijo = true; t.Puntuacion = 10;
                if (t.Fin > t.Inicio) r.Add(t);
            }
        }
        catch { }
        return r;
    }

    public static void Guardar(string veg, double duracion, List<Tramo> fijos)
    {
        string ruta = RutaPara(veg);
        if (ruta == null) return;
        List<object> l = new List<object>();
        foreach (Tramo t in fijos)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["inicio"] = Math.Round(t.Inicio, 3); d["fin"] = Math.Round(t.Fin, 3); d["titulo"] = t.Titulo;
            l.Add(d);
        }
        Dictionary<string, object> raiz = new Dictionary<string, object>();
        raiz["duracionProyecto"] = duracion;
        raiz["fijos"] = l;
        try { File.WriteAllText(ruta, Json.Escribir(raiz), new UTF8Encoding(false)); } catch { }
    }

    // Agrega un tramo a la lista, uniendo los que se enciman.
    public static List<Tramo> Agregar(List<Tramo> fijos, double a, double b, string titulo)
    {
        Tramo n = new Tramo();
        n.Inicio = a; n.Fin = b; n.Titulo = titulo; n.Fijo = true; n.Puntuacion = 10;
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in fijos)
        {
            if (t.Fin < a - 0.05 || t.Inicio > b + 0.05) { r.Add(t); continue; }
            n.Inicio = Math.Min(n.Inicio, t.Inicio); n.Fin = Math.Max(n.Fin, t.Fin);
        }
        r.Add(n);
        r.Sort(delegate (Tramo x, Tramo y) { return x.Inicio.CompareTo(y.Inicio); });
        return r;
    }
}

public static class PeticionIA
{
    public static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    // Reglas editoriales que aplican siempre (se pueden editar en la ventana;
    // se guardan para todos los proyectos).
    public const string ReglasPorDefecto =
        "- Empieza directo en la acci\u00f3n o en un gancho: nada de saludos largos, \u201c\u00bfme escuchan?\u201d, cargas de mundo, problemas t\u00e9cnicos ni preparaci\u00f3n.\r\n" +
        "- Excluye conversaciones personales o privadas aunque sean graciosas: vida amorosa, parejas, ex, familia, salud, dinero, escuela o trabajo, y cualquier dato personal.\r\n" +
        "- Excluye charla que no tenga que ver con el juego ni con la historia, problemas t\u00e9cnicos (lag, micr\u00f3fono, Discord, OBS), silencios, AFK y grindeo repetitivo.\r\n" +
        "- Mant\u00e9n el hilo: antes de cambiar de lugar o de actividad, conserva de 1 a 3 frases que digan a d\u00f3nde van o qu\u00e9 van a hacer. Si no existen, prop\u00f3n un texto de resumen.\r\n" +
        "- No cortes a mitad de una idea, chiste o reacci\u00f3n: incluye el remate.\r\n" +
        "- Termina con el cl\u00edmax o con un cierre o suspenso claro.";

    const string Rol =
        "Eres un editor de video experto en contenido de YouTube en espa\u00f1ol (gameplays con amigos, " +
        "narraciones, video ensayos). Recibes la transcripci\u00f3n de un video con tiempos en segundos de la " +
        "l\u00ednea de tiempo y una tabla de intensidad de sonido. Tu trabajo es ayudar a editarlo.\n\n";

    const string ReglasCorte =
        "- \"corte\": tramos a CONSERVAR, en orden, sin solaparse. Deben contar la historia completa sin omitir " +
        "partes importantes (objetivos, decisiones, resultados, momentos graciosos o intensos). Empieza y termina " +
        "cada tramo en l\u00edmites de frase, nunca a mitad de una palabra. Prefiere tramos de 10 s a 3 min.\n" +
        "- Cada tramo del corte lleva \"importancia\" de 1 a 10 (10 = imprescindible para la historia; 1 = relleno). " +
        "Se usa para ajustar la duraci\u00f3n quitando primero lo menos importante.\n" +
        "- Las REGLAS DEL CANAL y las INDICACIONES DEL EPISODIO son obligatorias: un tramo que las incumple no va " +
        "en el corte aunque sea gracioso o intenso.\n" +
        "- Poca conversaci\u00f3n no significa que no pase nada: en carreras, peleas, persecuciones, exploraci\u00f3n o " +
        "construcci\u00f3n puede haber acci\u00f3n con poca voz. F\u00edjate en la intensidad de ambiente y en las indicaciones; " +
        "si piden mostrar una actividad completa, cons\u00e9rvala completa aunque hablen poco.\n" +
        "- Los TRAMOS FIJOS ya los eligi\u00f3 el editor: van completos en el corte (incl\u00fayelos tal cual) y cuentan " +
        "para la duraci\u00f3n, as\u00ed que el resto tiene que caber en lo que queda.\n";

    const string ReglasAcelerar =
        "- Cada tramo del corte lleva \"accion\": \"conservar\" (velocidad normal) o \"acelerar\" (se ve m\u00e1s r\u00e1pido, " +
        "sin audio): \u00fasalo para transiciones con poca conversaci\u00f3n que ayudan a entender el progreso (viajar, " +
        "minar, construir, preparar). \"velocidad\" de 2 a 4. Un tramo acelerado cuenta para la duraci\u00f3n como " +
        "duraci\u00f3n/velocidad. No aceleres tramos con di\u00e1logo importante.\n";

    const string SinAcelerar = "- Todos los tramos del corte llevan \"accion\": \"conservar\" (no se acelera nada).\n";

    const string EsquemaFinal =
        "Responde SOLO con un objeto JSON con exactamente estas claves:\n" +
        "{\n" +
        "  \"resumen\": \"qu\u00e9 pasa en el video, en orden, en 1 a 3 p\u00e1rrafos\",\n" +
        "  \"secciones\": [{\"inicio\": s, \"fin\": s, \"titulo\": \"...\", \"descripcion\": \"qu\u00e9 pasa\"}],\n" +
        "  \"momentos\": [{\"inicio\": s, \"fin\": s, \"puntuacion\": 1-10, \"titulo\": \"...\", \"motivo\": \"por qu\u00e9 es bueno\"}],\n" +
        "  \"corte\": [{\"inicio\": s, \"fin\": s, \"importancia\": 1-10, \"accion\": \"conservar\" o \"acelerar\", \"velocidad\": 1-4, \"titulo\": \"...\", \"motivo\": \"por qu\u00e9 se conserva\"}],\n" +
        "  \"textos\": [{\"posicion\": s, \"texto\": \"texto corto en pantalla\", \"motivo\": \"qu\u00e9 se salta\"}],\n" +
        "  \"shorts\": [{\"inicio\": s, \"fin\": s, \"titulo\": \"...\", \"gancho\": \"por qu\u00e9 funciona solo\"}],\n" +
        "  \"titulos\": [\"t\u00edtulo para el video\", \"...\"]\n" +
        "}\n\n";

    const string ReglasResto =
        "- \"textos\": frases muy cortas tipo \"Construimos la base\" o \"3 horas despu\u00e9s\u2026\" para explicar lo que " +
        "el corte se salta; \"posicion\" es el inicio del tramo conservado donde conviene mostrarlo. Solo donde " +
        "realmente ayude a no perderse.\n" +
        "- \"momentos\": los mejores 5 a 15 (risas, gritos, sorpresas, frases memorables, acci\u00f3n intensa). Usa la " +
        "intensidad: valores altos de voz suelen ser gritos o risas; de ambiente, explosiones o peleas.\n" +
        "- \"shorts\": 2 a 5 tramos de 15 a 60 s que se entiendan sin contexto.\n" +
        "- \"titulos\": 3 a 5 opciones atractivas.\n" +
        "- Todos los tiempos son segundos (n\u00famero) de la l\u00ednea de tiempo dada; no inventes tiempos fuera del video.\n" +
        "- Escribe todo en espa\u00f1ol natural. Usa los nombres de las personas.";

    // ---------------------------------------------------- una peticion

    public static string Instrucciones(OpcionesIA op)
    {
        return Rol + EsquemaFinal + "Reglas:\n" + ReglasCorte +
               "- El corte completo debe durar entre la duraci\u00f3n m\u00ednima y la m\u00e1xima (apunta a la ideal). Antes de " +
               "responder, suma las duraciones de los tramos (los acelerados cuentan duraci\u00f3n/velocidad) y corrige si te pasas.\n" +
               (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) + ReglasResto;
    }

    static void Cabecera(StringBuilder sb, Transcripcion t, double duracionActual, OpcionesIA op)
    {
        sb.Append("Tipo de video: " + op.Tipo + "\n");
        sb.Append("Duraci\u00f3n actual: " + S(duracionActual) + " s (" + Formato.Tiempo(duracionActual) + ")\n");
        sb.Append("Duraci\u00f3n del corte: m\u00ednimo " + S(op.MinutosMin * 60) + " s, m\u00e1ximo " + S(op.MinutosMax * 60) +
                  " s, ideal " + S(op.MinutosObjetivo * 60) + " s (" + op.MinutosMin + " a " + op.MinutosMax + " min)\n");
        if (!String.IsNullOrEmpty(op.ReglasCanal)) sb.Append("\nREGLAS DEL CANAL (siempre):\n" + ConSegundos(op.ReglasCanal.Trim()) + "\n");
        if (!String.IsNullOrEmpty(op.Instrucciones)) sb.Append("\nINDICACIONES DEL EPISODIO:\n" + ConSegundos(op.Instrucciones.Trim()) + "\n");
        Fijos_(sb, op);
        if (!String.IsNullOrEmpty(op.Contexto))
            sb.Append("\nCONTEXTO DE EPISODIOS ANTERIORES (solo para entender la historia; no los cortes):\n" + op.Contexto.Trim() + "\n");
        sb.Append("\nPersonas (cada una es una pista de audio):\n");
        foreach (Hablante h in t.Hablantes)
            if (h.Voz) sb.Append("- " + h.Nombre + (h.Nombre != h.Etiqueta ? " (" + h.Etiqueta + ")" : "") + "\n");
    }

    static void Fijos_(StringBuilder sb, OpcionesIA op)
    {
        if (op.Fijos.Count == 0) return;
        double total = 0;
        sb.Append("\nTRAMOS FIJOS (elegidos por el editor; van completos en el corte):\n");
        foreach (Tramo f in op.Fijos)
        {
            sb.Append("- [" + S(f.Inicio) + "-" + S(f.Fin) + "] " + f.Titulo + " (" + Formato.Tiempo(f.Duracion) + ")\n");
            total += f.Duracion;
        }
        sb.Append("Suman " + S(total) + " s; el resto del corte debe caber en lo que queda de la duraci\u00f3n.\n");
    }

    // Los tiempos escritos como 57:00 o 1:09:30 se acompa\u00f1an con su valor en
    // segundos, que es como estan los tiempos de la transcripcion.
    public static string ConSegundos(string texto)
    {
        return Regex.Replace(texto ?? "", @"(?<![\d:])(\d{1,2}):(\d{2})(?::(\d{2}))?(?![\d:])", delegate (Match m)
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
            double seg = m.Groups[3].Success ? a * 3600 + b * 60 + int.Parse(m.Groups[3].Value) : a * 60 + b;
            return m.Value + " (= " + S(seg) + " s)";
        });
    }

    static void Transcripcion_(StringBuilder sb, Transcripcion t, List<Segmento> segmentos, double desde, double hasta)
    {
        sb.Append("\nTranscripci\u00f3n [inicio-fin] persona: texto\n");
        foreach (Segmento s in segmentos)
            if (s.Fin > desde && s.Inicio < hasta)
                sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + t.Hablantes[s.Hablante].Nombre + ": " + s.Texto + "\n");
    }

    static void Intensidad_(StringBuilder sb, Transcripcion t, double duracionActual, double desde, double hasta)
    {
        sb.Append("\nIntensidad cada 5 s (0 = silencio, 10 = lo m\u00e1s fuerte de esa pista). Columnas: inicio;voz;ambiente\n");
        foreach (string linea in Intensidad(t, duracionActual, 5))
        {
            double inicio = double.Parse(linea.Substring(0, linea.IndexOf(';')), CultureInfo.InvariantCulture);
            if (inicio + 5 > desde && inicio < hasta) sb.Append(linea + "\n");
        }
    }

    // Mensaje con la transcripcion e intensidad en la linea de tiempo actual.
    public static string Mensaje(Transcripcion t, double duracionActual, OpcionesIA op)
    {
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        Transcripcion_(sb, t, t.SegmentosActuales(), 0, duracionActual);
        Intensidad_(sb, t, duracionActual, 0, duracionActual);
        return sb.ToString();
    }

    // ------------------------------------------------------- por partes

    // Divide el video en partes de ~"tamano" segundos, cortando en la pausa
    // mas larga entre frases cerca de cada limite.
    public static List<Rango> Partes(List<Segmento> segmentos, double total, double tamano)
    {
        List<Rango> r = new List<Rango>();
        int n = Math.Max(1, (int)Math.Round(total / tamano));
        double inicio = 0;
        for (int i = 1; i < n; i++)
        {
            double ideal = total * i / n, mejor = ideal, hueco = -1;
            for (int k = 0; k + 1 < segmentos.Count; k++)
            {
                double a = segmentos[k].Fin, b = segmentos[k + 1].Inicio;
                double medio = (a + b) / 2;
                if (Math.Abs(medio - ideal) > tamano * 0.15 || b - a <= hueco) continue;
                hueco = b - a;
                mejor = medio;
            }
            if (mejor <= inicio + 60) mejor = ideal;
            r.Add(new Rango(inicio, mejor));
            inicio = mejor;
        }
        r.Add(new Rango(inicio, total));
        return r;
    }

    public static string InstruccionesParte(OpcionesIA op)
    {
        return Rol +
            "Este video es largo, as\u00ed que lo recibes POR PARTES. Ahora te toca UNA parte. Elige candidatos para el " +
            "corte final (despu\u00e9s se elegir\u00e1 entre los candidatos de todas las partes) y resume lo que pasa.\n\n" +
            "Responde SOLO con un objeto JSON con exactamente estas claves:\n" +
            "{\n" +
            "  \"resumen\": \"qu\u00e9 pasa en esta parte, en orden, en 2 a 5 frases (con nombres y hechos concretos)\",\n" +
            "  \"candidatos\": [{\"inicio\": s, \"fin\": s, \"importancia\": 1-10, \"accion\": \"conservar\" o \"acelerar\", \"velocidad\": 1-4, \"titulo\": \"...\", \"motivo\": \"...\"}],\n" +
            "  \"momentos\": [{\"inicio\": s, \"fin\": s, \"puntuacion\": 1-10, \"titulo\": \"...\", \"motivo\": \"...\"}]\n" +
            "}\n\n" +
            "Reglas:\n" + ReglasCorte +
            "- Los candidatos de esta parte deben sumar cerca de la duraci\u00f3n sugerida para la parte (es generosa: " +
            "luego se recorta). \"importancia\": 10 = imprescindible para entender la historia o lo m\u00e1s gracioso; " +
            "1 = relleno prescindible.\n" +
            (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) +
            "- \"momentos\": los mejores de esta parte (0 a 6).\n" +
            "- Usa solo tiempos dentro de esta parte. Escribe en espa\u00f1ol natural y usa los nombres de las personas.";
    }

    public static string MensajeParte(Transcripcion t, List<Segmento> segmentos, double duracionActual, OpcionesIA op,
                                      int numero, List<Rango> partes, List<string> resumenesPrevios)
    {
        Rango p = partes[numero];
        double sugerido = op.MinutosObjetivo * 60 * (p.Fin - p.Inicio) / duracionActual * 1.5;
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        sb.Append("\nParte " + (numero + 1) + " de " + partes.Count + ": de " + S(p.Inicio) + " s a " + S(p.Fin) + " s (" +
                  Formato.Tiempo(p.Inicio) + "\u2013" + Formato.Tiempo(p.Fin) + ").\n");
        sb.Append("Duraci\u00f3n sugerida para los candidatos de esta parte: " + S(sugerido) + " s.\n");
        if (resumenesPrevios.Count > 0)
        {
            sb.Append("\nLo que pas\u00f3 en las partes anteriores:\n");
            for (int i = 0; i < resumenesPrevios.Count; i++) sb.Append("Parte " + (i + 1) + ": " + resumenesPrevios[i] + "\n");
        }
        Transcripcion_(sb, t, segmentos, p.Inicio, p.Fin);
        Intensidad_(sb, t, duracionActual, p.Inicio, p.Fin);
        return sb.ToString();
    }

    public static string InstruccionesFinal(OpcionesIA op)
    {
        return Rol +
            "Este video es largo y ya se analiz\u00f3 por partes. Recibes el resumen de cada parte y una lista de " +
            "CANDIDATOS (tramos posibles con su importancia). Arma el video final.\n\n" + EsquemaFinal + "Reglas:\n" +
            "- \"corte\": elige y ordena candidatos para que el video final dure entre la duraci\u00f3n m\u00ednima y la m\u00e1xima " +
            "(apunta a la ideal; suma antes de responder). Usa sus tiempos tal cual o rec\u00f3rtalos por dentro; no inventes tramos fuera de los candidatos. " +
            "Cada tramo lleva \"importancia\" de 1 a 10. Las REGLAS DEL CANAL y las INDICACIONES DEL EPISODIO son obligatorias. " +
            "Que la historia completa se entienda de principio a fin: no te saltes objetivos, decisiones ni " +
            "resultados importantes. Prefiere los de mayor importancia, pero mant\u00e9n el ritmo y la variedad.\n" +
            (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) +
            "- \"secciones\": cubren todo el video original (una por etapa de la historia).\n" + ReglasResto;
    }

    public static string MensajeFinal(Transcripcion t, double duracionActual, OpcionesIA op, List<Rango> partes,
                                      List<string> resumenes, List<Tramo> candidatos, List<Tramo> momentos)
    {
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        sb.Append("\nResumen por partes:\n");
        for (int i = 0; i < partes.Count; i++)
            sb.Append("Parte " + (i + 1) + " (" + S(partes[i].Inicio) + "-" + S(partes[i].Fin) + " s): " +
                      (i < resumenes.Count ? resumenes[i] : "") + "\n");
        double suma = 0;
        foreach (Tramo c in candidatos) suma += c.DuracionFinal;
        sb.Append("\nCandidatos [inicio-fin] importancia acci\u00f3n: t\u00edtulo \u2014 motivo (suman " + S(suma) + " s):\n");
        foreach (Tramo c in candidatos)
            sb.Append("[" + S(c.Inicio) + "-" + S(c.Fin) + "] " + c.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + " " +
                      (c.Acelerar ? "acelerar\u00d7" + c.Velocidad.ToString("0", CultureInfo.InvariantCulture) : "conservar") + ": " +
                      c.Titulo + " \u2014 " + c.Motivo + "\n");
        sb.Append("\nMomentos destacados encontrados [inicio-fin] puntuaci\u00f3n: t\u00edtulo \u2014 motivo:\n");
        foreach (Tramo m in momentos)
            sb.Append("[" + S(m.Inicio) + "-" + S(m.Fin) + "] " + m.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + ": " +
                      m.Titulo + " \u2014 " + m.Motivo + "\n");
        return sb.ToString();
    }

    // Contexto de episodios anteriores: el resumen y las secciones de sus
    // respuestas de MomentosIA (opcional; solo para entender la historia).
    public static string ContextoDe(List<string> rutas)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string ruta in rutas)
        {
            try
            {
                object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
                object r = Json.Leer(Gemini.QuitarCercas(Json.Texto(o, "respuesta")));
                string nombre = Path.GetFileName(ruta).Replace(".vegascut-ia.json", "");
                StringBuilder ep = new StringBuilder();
                ep.Append("- " + nombre + ": " + Json.Texto(r, "resumen").Replace("\n", " ") + "\n");
                foreach (object s in Json.Lista(r, "secciones"))
                    ep.Append("  \u00b7 " + Json.Texto(s, "titulo") + ": " + Json.Texto(s, "descripcion") + "\n");
                string texto = ep.ToString();
                if (texto.Length > 3000) texto = texto.Substring(0, 3000) + "\u2026\n";
                sb.Append(texto);
            }
            catch { }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------- revision

    public static string InstruccionesRevision()
    {
        return "Eres un revisor estricto de cortes de video. Recibes las REGLAS DEL CANAL, las INDICACIONES DEL " +
            "EPISODIO y la lista numerada de tramos que se van a conservar, con lo que se dice en cada uno.\n\n" +
            "Revisa cada tramo contra las reglas y las indicaciones:\n" +
            "- Si la mayor parte del tramo las incumple (por ejemplo, una conversaci\u00f3n personal o de vida amorosa), " +
            "m\u00e1rcalo con \"quitar\": true.\n" +
            "- Si solo una parte las incumple, deja \"quitar\": false y da \"inicio\" y \"fin\" (segundos) de la parte que " +
            "S\u00cd se puede conservar, dentro del tramo y en l\u00edmites de frase.\n" +
            "- Si cumple, no lo incluyas en la respuesta.\n\n" +
            "Responde SOLO con JSON: {\"tramos\": [{\"indice\": n, \"quitar\": true o false, \"inicio\": s, \"fin\": s, " +
            "\"motivo\": \"qu\u00e9 regla incumple\"}]}. Si todo cumple: {\"tramos\": []}.";
    }

    public static string MensajeRevision(Transcripcion t, ResultadoIA r, OpcionesIA op)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("REGLAS DEL CANAL:\n" + (op.ReglasCanal ?? "").Trim() + "\n");
        if (!String.IsNullOrEmpty(op.Instrucciones)) sb.Append("\nINDICACIONES DEL EPISODIO:\n" + op.Instrucciones.Trim() + "\n");
        sb.Append("\nTramos del corte:\n");
        List<Segmento> segmentos = t.SegmentosActuales();
        for (int i = 0; i < r.Corte.Count; i++)
        {
            Tramo c = r.Corte[i];
            if (!c.Elegido) continue;
            sb.Append("\n#" + i + " [" + S(c.Inicio) + "-" + S(c.Fin) + "] " + c.Titulo + "\n");
            StringBuilder texto = new StringBuilder();
            foreach (Segmento s in segmentos)
                if (s.Fin > c.Inicio && s.Inicio < c.Fin)
                    texto.Append("[" + S(s.Inicio) + "] " + t.Hablantes[s.Hablante].Nombre + ": " + s.Texto + "\n");
            string tx = texto.ToString();
            if (tx.Length > 2500) tx = tx.Substring(0, 2500) + "\u2026\n";
            sb.Append(tx);
        }
        return sb.ToString();
    }

    // Pico de cada bloque de "bloque" segundos, normalizado por pista, en la
    // linea de tiempo actual. Solo se listan los bloques con algo de sonido.
    public static List<string> Intensidad(Transcripcion t, double duracionActual, int bloque)
    {
        int n = (int)Math.Ceiling(duracionActual / bloque) + 1;
        double[] voz = new double[n], amb = new double[n];
        foreach (Hablante h in t.Hablantes)
        {
            if (h.Pico == null || h.Pico.Length == 0) continue;
            float[] orden = (float[])h.Pico.Clone();
            Array.Sort(orden);
            double bajo = orden[(int)(orden.Length * 0.10)], alto = orden[Math.Min(orden.Length - 1, (int)(orden.Length * 0.995))];
            if (alto - bajo < 3) continue;
            for (int s = 0; s < h.Pico.Length; s++)
            {
                double ahora = t.Mapear(t.Inicio + s);
                if (double.IsNaN(ahora)) continue;
                int b = (int)(ahora / bloque);
                if (b < 0 || b >= n) continue;
                double v = Math.Max(0, Math.Min(10, (h.Pico[s] - bajo) / (alto - bajo) * 10));
                if (h.Voz) voz[b] = Math.Max(voz[b], v); else amb[b] = Math.Max(amb[b], v);
            }
        }
        List<string> r = new List<string>();
        for (int b = 0; b < n; b++)
            if (voz[b] >= 1 || amb[b] >= 1)
                r.Add((b * bloque) + ";" + Math.Round(voz[b]) + ";" + Math.Round(amb[b]));
        return r;
    }
}

// =====================================================================
// Orquesta las peticiones (una sola o por partes) y reintenta si hace falta.
// =====================================================================

public delegate string LlamadaIA(string instrucciones, string mensaje);

public class AsistenteIA
{
    // Videos mas largos que esto se analizan por partes.
    public double UmbralPartes = 35 * 60;
    public double TamanoParte = 20 * 60;
    public Action<string> Progreso = delegate { };
    readonly LlamadaIA llamar;

    public AsistenteIA(LlamadaIA llamar) { this.llamar = llamar; }

    // Pide y comprueba que sea JSON valido; un reintento si sale roto.
    string PedirJson(string instrucciones, string mensaje)
    {
        for (int intento = 1; ; intento++)
        {
            string r = llamar(instrucciones, mensaje);
            try { Json.Leer(Gemini.QuitarCercas(r)); return r; }
            catch (FormatException)
            {
                if (intento >= 2) throw new Exception("Gemini devolvi\u00f3 una respuesta que no se pudo leer dos veces seguidas.");
                Progreso("La respuesta lleg\u00f3 incompleta; reintentando\u2026");
            }
        }
    }

    // Devuelve el JSON final con el mismo formato en ambos modos.
    public string Ejecutar(Transcripcion t, double total, OpcionesIA op)
    {
        if (total <= UmbralPartes)
        {
            try
            {
                Progreso("Gemini est\u00e1 analizando el video completo\u2026");
                return PedirJson(PeticionIA.Instrucciones(op), PeticionIA.Mensaje(t, total, op));
            }
            catch (RespuestaCortada)
            {
                Progreso("La respuesta no cupo completa: se analizar\u00e1 por partes.");
            }
        }
        return PorPartes(t, total, op);
    }

    string PorPartes(Transcripcion t, double total, OpcionesIA op)
    {
        List<Segmento> segmentos = t.SegmentosActuales();
        List<Rango> partes = PeticionIA.Partes(segmentos, total, TamanoParte);
        List<string> resumenes = new List<string>();
        List<Tramo> candidatos = new List<Tramo>(), momentos = new List<Tramo>();
        string instrucciones = PeticionIA.InstruccionesParte(op);

        for (int i = 0; i < partes.Count; i++)
        {
            Progreso("Analizando la parte " + (i + 1) + " de " + partes.Count + " (" +
                     Formato.Tiempo(partes[i].Inicio) + "\u2013" + Formato.Tiempo(partes[i].Fin) + ")\u2026");
            string r = PedirJson(instrucciones, PeticionIA.MensajeParte(t, segmentos, total, op, i, partes, resumenes));
            object o = Json.Leer(Gemini.QuitarCercas(r));
            resumenes.Add(Json.Texto(o, "resumen"));
            // Los candidatos y momentos se leen con el mismo lector del resultado
            // final, limitados a esta parte.
            ResultadoIA parte = ResultadoIA.Leer("{\"corte\": " + Json.Escribir(Json.Lista(o, "candidatos"), false) +
                                                  ", \"momentos\": " + Json.Escribir(Json.Lista(o, "momentos"), false) + "}", total);
            foreach (Tramo c in parte.Corte)
            {
                c.Inicio = Math.Max(c.Inicio, partes[i].Inicio);
                c.Fin = Math.Min(c.Fin, partes[i].Fin);
                if (c.Fin - c.Inicio >= 0.5) candidatos.Add(c);
            }
            momentos.AddRange(parte.Momentos);
        }

        Progreso("Armando el video final con " + candidatos.Count + " candidatos\u2026");
        string final = PedirJson(PeticionIA.InstruccionesFinal(op),
                                 PeticionIA.MensajeFinal(t, total, op, partes, resumenes, candidatos, momentos));
        // Se guardan los candidatos junto al resultado para poder completar el
        // corte si queda corto.
        Dictionary<string, object> d = Json.Leer(Gemini.QuitarCercas(final)) as Dictionary<string, object>;
        if (d == null) return final;
        List<object> lista = new List<object>();
        foreach (Tramo c in candidatos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["inicio"] = c.Inicio; x["fin"] = c.Fin; x["importancia"] = c.Puntuacion;
            x["accion"] = c.Acelerar ? "acelerar" : "conservar"; x["velocidad"] = c.Velocidad;
            x["titulo"] = c.Titulo; x["motivo"] = c.Motivo;
            lista.Add(x);
        }
        d["candidatos"] = lista;
        return Json.Escribir(d);
    }

    // Segunda opinion: revisa el corte contra las reglas. Devuelve el JSON de
    // la revision ("" si fallo; la revision es opcional).
    public string Revisar(Transcripcion t, ResultadoIA r, OpcionesIA op)
    {
        if (r.Corte.Count == 0) return "";
        Progreso("Revisando que el corte cumpla las reglas\u2026");
        try { return PedirJson(PeticionIA.InstruccionesRevision(), PeticionIA.MensajeRevision(t, r, op)); }
        catch (Exception ex) { Progreso("No se pudo revisar (" + ex.Message + ")."); return ""; }
    }
}

// ---- src/comun/Editor.cs ----

// =====================================================================
// Edicion en la linea de tiempo
// =====================================================================

static class Editor
{
    const double Tolerancia = 0.0005; // segundos

    public static List<Rango> AjustarAFotogramas(List<Rango> rangos, double fps)
    {
        List<Rango> r = new List<Rango>();
        foreach (Rango x in rangos)
        {
            double a = Math.Round(x.Inicio * fps) / fps;
            double b = Math.Round(x.Fin * fps) / fps;
            if (b - a >= 1.0 / fps) r.Add(new Rango(a, b));
        }
        return r;
    }

    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000.0); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Corta cada evento en los bordes de los rangos. Devuelve los eventos
    // resultantes de la pista.
    static List<TrackEvent> CortarEnBordes(Track pista, List<Rango> rangos)
    {
        List<double> bordes = new List<double>();
        foreach (Rango r in rangos) { bordes.Add(r.Inicio); bordes.Add(r.Fin); }
        bordes.Sort();

        List<TrackEvent> originales = new List<TrackEvent>();
        foreach (TrackEvent e in pista.Events) originales.Add(e);

        foreach (TrackEvent e in originales)
        {
            double ini = S(e.Start), fin = S(e.End);
            // De atras hacia adelante: el evento original conserva la parte izquierda.
            for (int i = bordes.Count - 1; i >= 0; i--)
            {
                double b = bordes[i];
                if (b > ini + Tolerancia && b < fin - Tolerancia)
                    e.Split(TC(b - ini));
            }
        }

        List<TrackEvent> todos = new List<TrackEvent>();
        foreach (TrackEvent e in pista.Events) todos.Add(e);
        return todos;
    }

    // Fundido corto en el audio que empieza o termina en un corte, para que
    // no se oiga un chasquido.
    static void Suavizar(List<TrackEvent> eventos, List<Rango> rangos, double segundos)
    {
        if (segundos <= 0) return;
        foreach (TrackEvent e in eventos)
        {
            if (!(e is AudioEvent)) continue;
            double ini = S(e.Start), fin = S(e.End);
            double largo = Math.Min(segundos, (fin - ini) / 2);
            foreach (Rango r in rangos)
            {
                if (Math.Abs(ini - r.Fin) < Tolerancia) e.FadeIn.Length = TC(largo);
                if (Math.Abs(fin - r.Inicio) < Tolerancia) e.FadeOut.Length = TC(largo);
            }
        }
    }

    static bool DentroDeRango(TrackEvent e, List<Rango> rangos)
    {
        double medio = (S(e.Start) + S(e.End)) / 2;
        foreach (Rango r in rangos)
            if (medio > r.Inicio && medio < r.Fin) return true;
        return false;
    }

    static double QuitadoAntesDe(double t, List<Rango> rangos)
    {
        double q = 0;
        foreach (Rango r in rangos)
            if (r.Fin <= t + Tolerancia) q += r.Fin - r.Inicio;
        return q;
    }

    // Nueva posicion de un instante despues de quitar los rangos. Si cae
    // dentro de un rango, queda en el punto del corte.
    public static double PosicionTrasQuitar(double t, List<Rango> rangos)
    {
        double q = QuitadoAntesDe(t, rangos);
        foreach (Rango r in rangos)
            if (t > r.Inicio && t < r.Fin - Tolerancia) q += t - r.Inicio;
        return t - q;
    }

    // Quita los tramos de los rangos. Con "juntar" mueve lo que sigue para
    // cerrar el hueco; sin el, deja el espacio vacio.
    public static void Eliminar(Project proyecto, List<Track> pistas, List<Rango> rangos, bool juntar,
                                bool moverMarcadores, double suavizado)
    {
        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = CortarEnBordes(pista, rangos);
            List<TrackEvent> quedan = new List<TrackEvent>();
            foreach (TrackEvent e in eventos)
                if (DentroDeRango(e, rangos)) pista.Events.Remove(e); else quedan.Add(e);
            Suavizar(quedan, rangos, suavizado);
            if (!juntar) continue;

            List<TrackEvent> restantes = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) restantes.Add(e);
            restantes.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });

            // De izquierda a derecha: cada evento se mueve a un espacio ya libre.
            foreach (TrackEvent e in restantes)
            {
                double q = QuitadoAntesDe(S(e.Start), rangos);
                if (q > 0) e.Start = TC(S(e.Start) - q);
            }
        }
        Reagrupar(proyecto, pistas);

        if (!moverMarcadores) return;
        List<Marker> marcadores = new List<Marker>();
        foreach (Marker m in proyecto.Markers) marcadores.Add(m);
        foreach (Region m in proyecto.Regions) marcadores.Add(m);
        foreach (Marker m in marcadores)
        {
            double t = S(m.Position), nuevo = PosicionTrasQuitar(t, rangos);
            if (nuevo < t - Tolerancia)
            {
                try { m.Position = TC(nuevo); } catch { }
            }
        }
    }

    // Vegas no acepta velocidades de evento mayores a 4x.
    public const double VelocidadMaxima = 4;

    // Reproduce mas rapido los tramos indicados: corta en sus bordes, sube la
    // velocidad de cada evento de adentro (y acorta su duracion en la misma
    // proporcion) y corre hacia la izquierda lo que sigue. Con
    // "silenciarAudio" el audio de esos tramos queda mudo (acelerado suena raro).
    public static void Acelerar(Project proyecto, List<Track> pistas, List<Acelerado> tramos, bool silenciarAudio,
                                bool moverMarcadores, double suavizado)
    {
        List<Rango> bordes = new List<Rango>();
        foreach (Acelerado a in tramos) bordes.Add(new Rango(a.Inicio, a.Fin));

        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = CortarEnBordes(pista, bordes);
            eventos.Sort(delegate (TrackEvent x, TrackEvent y) { return S(x.Start).CompareTo(S(y.Start)); });

            // De izquierda a derecha: primero se acorta el evento y luego se
            // mueve, asi nunca se encima con el siguiente.
            foreach (TrackEvent e in eventos)
            {
                double ini = S(e.Start), fin = S(e.End), medio = (ini + fin) / 2;
                foreach (Acelerado a in tramos)
                {
                    if (medio <= a.Inicio || medio >= a.Fin) continue;
                    double antes = e.PlaybackRate;
                    double despues = Math.Min(VelocidadMaxima, antes * a.Factor);
                    e.PlaybackRate = despues;
                    e.Length = TC((fin - ini) * antes / despues);
                    if (silenciarAudio && e is AudioEvent) e.Mute = true;
                    break;
                }
                double nuevo = Acelerado.Posicion(ini, tramos);
                if (nuevo < ini - Tolerancia) e.Start = TC(nuevo);
            }

            // Fundido corto donde el audio normal se junta con el acelerado.
            if (suavizado > 0 && pista.IsAudio())
            {
                List<Rango> nuevosBordes = new List<Rango>();
                foreach (Acelerado a in tramos)
                    nuevosBordes.Add(new Rango(Acelerado.Posicion(a.Inicio, tramos), Acelerado.Posicion(a.Fin, tramos)));
                List<TrackEvent> todos = new List<TrackEvent>();
                foreach (TrackEvent e in pista.Events) todos.Add(e);
                Suavizar(todos, nuevosBordes, suavizado);
                // Los bordes de adentro del tramo tambien llevan fundido.
                List<Rango> invertidos = new List<Rango>();
                foreach (Rango r in nuevosBordes) invertidos.Add(new Rango(r.Fin, r.Inicio));
                Suavizar(todos, invertidos, suavizado);
            }
        }
        Reagrupar(proyecto, pistas);

        if (!moverMarcadores) return;
        List<Marker> marcadores = new List<Marker>();
        foreach (Marker m in proyecto.Markers) marcadores.Add(m);
        foreach (Region m in proyecto.Regions) marcadores.Add(m);
        foreach (Marker m in marcadores)
        {
            double t = S(m.Position), nuevo = Acelerado.Posicion(t, tramos);
            if (nuevo < t - Tolerancia)
            {
                try { m.Position = TC(nuevo); } catch { }
            }
        }
    }

    public static void Silenciar(Project proyecto, List<Track> pistas, List<Rango> rangos, double suavizado)
    {
        Silenciar(pistas, rangos, suavizado);
        Reagrupar(proyecto, pistas);
    }

    public static void Silenciar(List<Track> pistas, List<Rango> rangos, double suavizado)
    {
        foreach (Track pista in pistas)
        {
            if (!pista.IsAudio()) continue;
            List<TrackEvent> eventos = CortarEnBordes(pista, rangos);
            List<TrackEvent> suenan = new List<TrackEvent>();
            foreach (TrackEvent e in eventos)
                if (DentroDeRango(e, rangos)) e.Mute = true; else suenan.Add(e);
            Suavizar(suenan, rangos, suavizado);
        }
    }

    public static void Marcar(Project proyecto, List<Rango> rangos)
    {
        foreach (Rango r in rangos)
            proyecto.Regions.Add(new Region(TC(r.Inicio), TC(r.Fin - r.Inicio), "Silencio"));
    }

    // ------------------------------------------------------------ grupos

    // Al cortar con Split, Vegas deja cada pedazo nuevo en el mismo grupo que
    // el clip original: al final todos los pedazos quedan unidos y mover o
    // borrar uno mueve o borra todos. Aqui cada grupo asi se separa en
    // grupos chicos: los eventos que se enciman en el tiempo (el video y sus
    // audios del mismo pedazo) siguen juntos. Solo se tocan grupos con dos o
    // mas eventos en la misma pista, que es la marca de este problema.
    // Devuelve cuantos pedazos quedaron en su propio grupo.
    public static int Reagrupar(Project proyecto, IEnumerable<Track> pistas)
    {
        // Dictionary y no HashSet: Vegas compila sin System.Core.
        Dictionary<string, bool> vistos = new Dictionary<string, bool>();
        int separados = 0;
        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) eventos.Add(e);
            foreach (TrackEvent e in eventos)
            {
                if (vistos.ContainsKey(Clave(e))) continue;
                TrackEventGroup grupo = null;
                try { if (e.IsGrouped) grupo = e.Group; } catch { }
                if (grupo == null) continue;

                List<TrackEvent> miembros = new List<TrackEvent>();
                foreach (TrackEvent m in grupo) miembros.Add(m);
                Dictionary<int, bool> pistasDelGrupo = new Dictionary<int, bool>();
                bool roto = false;
                foreach (TrackEvent m in miembros)
                {
                    vistos[Clave(m)] = true;
                    if (pistasDelGrupo.ContainsKey(m.Track.Index)) roto = true;
                    pistasDelGrupo[m.Track.Index] = true;
                }
                if (!roto) continue;

                List<List<TrackEvent>> partes = Partes(miembros);
                // La primera parte se queda en el grupo original.
                for (int i = 1; i < partes.Count; i++)
                {
                    foreach (TrackEvent m in partes[i])
                        try { grupo.Remove(m); } catch { }
                    if (partes[i].Count > 1)
                    {
                        TrackEventGroup nuevo = NuevoGrupo(proyecto);
                        foreach (TrackEvent m in partes[i]) nuevo.Add(m);
                    }
                    separados++;
                }
            }
        }
        return separados;
    }

    public static int Reagrupar(Project proyecto)
    {
        return Reagrupar(proyecto, proyecto.Tracks);
    }

    // Segun la version de Vegas el grupo se crea sin argumentos o con el
    // proyecto; por reflexion sirve para ambas.
    static TrackEventGroup NuevoGrupo(Project proyecto)
    {
        TrackEventGroup g;
        try { g = (TrackEventGroup)Activator.CreateInstance(typeof(TrackEventGroup)); }
        catch { g = (TrackEventGroup)Activator.CreateInstance(typeof(TrackEventGroup), proyecto); }
        proyecto.Groups.Add(g);
        return g;
    }

    static string Clave(TrackEvent e)
    {
        return e.Track.Index + ":" + Math.Round(e.Start.ToMilliseconds());
    }

    // Eventos que se enciman en el tiempo van juntos.
    static List<List<TrackEvent>> Partes(List<TrackEvent> eventos)
    {
        eventos.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });
        List<List<TrackEvent>> partes = new List<List<TrackEvent>>();
        double fin = double.MinValue;
        foreach (TrackEvent e in eventos)
        {
            if (partes.Count == 0 || S(e.Start) >= fin - 0.002)
            {
                partes.Add(new List<TrackEvent>());
                fin = S(e.End);
            }
            else fin = Math.Max(fin, S(e.End));
            partes[partes.Count - 1].Add(e);
        }
        return partes;
    }
}

// ---- src/comun/Anclas.cs ----

// =====================================================================
// Marcadores anclados a los clips
//
// Vegas pone los marcadores en la linea de tiempo, no en los clips. Para que
// sigan a su clip, al crearlos se guarda en <proyecto>.vegascut-marcas.json
// a que archivo y a que segundo de ese archivo corresponden. Despues, el
// script ReubicarMarcadores busca el clip que tiene ese segundo y vuelve a
// poner el marcador encima, aunque hayas movido, cortado o reordenado clips.
// =====================================================================

public class Ancla
{
    public string Etiqueta = "", Media = "", MediaFin = "";
    public double Fuente, FuenteFin = -1;   // segundos dentro del archivo
    public bool Region;
}

public static class Anclas
{
    const double Tol = 0.0005;

    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-marcas.json");
    }

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Clip de video (si no hay, de audio) que esta en el instante t, con un
    // archivo real (no textos ni colores generados).
    static TrackEvent ClipEn(Project p, double t)
    {
        foreach (bool video in new bool[] { true, false })
            foreach (Track pista in p.Tracks)
            {
                if (pista.IsAudio() == video) continue;
                foreach (TrackEvent e in pista.Events)
                {
                    if (S(e.Start) > t + Tol || S(e.End) <= t + Tol) continue;
                    Take toma = e.ActiveTake;
                    if (toma == null || toma.Media == null || toma.Media.IsGenerated()) continue;
                    return e;
                }
            }
        return null;
    }

    static bool Fuente(Project p, double t, out string media, out double fuente)
    {
        media = ""; fuente = 0;
        TrackEvent e = ClipEn(p, t);
        if (e == null) return false;
        media = e.ActiveTake.Media.FilePath;
        fuente = S(e.ActiveTake.Offset) + (t - S(e.Start)) * e.PlaybackRate;
        return true;
    }

    // Crea el ancla de un marcador (fin < 0) o de una region.
    public static Ancla Crear(Project p, double t, double fin, string etiqueta)
    {
        Ancla a = new Ancla();
        a.Etiqueta = etiqueta;
        a.Region = fin >= 0;
        if (!Fuente(p, t, out a.Media, out a.Fuente)) return null;
        if (a.Region && !Fuente(p, Math.Max(t, fin - 0.001), out a.MediaFin, out a.FuenteFin)) a.FuenteFin = -1;
        return a;
    }

    public static List<Ancla> Cargar(string veg)
    {
        List<Ancla> r = new List<Ancla>();
        string ruta = RutaPara(veg);
        if (ruta == null || !File.Exists(ruta)) return r;
        try
        {
            foreach (object x in Json.Lista(Json.Leer(File.ReadAllText(ruta, Encoding.UTF8)), "anclas"))
            {
                Ancla a = new Ancla();
                a.Etiqueta = Json.Texto(x, "etiqueta");
                a.Media = Json.Texto(x, "media");
                a.MediaFin = Json.Texto(x, "mediaFin");
                a.Fuente = Json.Numero(x, "fuente", 0);
                a.FuenteFin = Json.Numero(x, "fuenteFin", -1);
                a.Region = Json.Texto(x, "region") == "True";
                r.Add(a);
            }
        }
        catch { }
        return r;
    }

    // Agrega anclas nuevas (reemplaza las que tengan la misma etiqueta).
    public static void Guardar(string veg, List<Ancla> nuevas)
    {
        string ruta = RutaPara(veg);
        if (ruta == null) return;
        List<Ancla> todas = Cargar(veg);
        foreach (Ancla n in nuevas)
        {
            if (n == null) continue;
            todas.RemoveAll(delegate (Ancla a) { return a.Etiqueta == n.Etiqueta && a.Region == n.Region; });
            todas.Add(n);
        }
        List<object> lista = new List<object>();
        foreach (Ancla a in todas)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["etiqueta"] = a.Etiqueta; d["region"] = a.Region;
            d["media"] = a.Media; d["fuente"] = Math.Round(a.Fuente, 3);
            if (a.Region) { d["mediaFin"] = a.MediaFin; d["fuenteFin"] = Math.Round(a.FuenteFin, 3); }
            lista.Add(d);
        }
        Dictionary<string, object> raiz = new Dictionary<string, object>();
        raiz["formato"] = "vegas-cut-marcas";
        raiz["anclas"] = lista;
        try { File.WriteAllText(ruta, Json.Escribir(raiz), new UTF8Encoding(false)); } catch { }
    }

    // Instantes de la linea de tiempo donde se ve ese segundo del archivo
    // (puede haber varios si el clip esta repetido).
    static List<double> Donde(Project p, string media, double fuente)
    {
        List<double> r = new List<double>();
        foreach (Track pista in p.Tracks)
            foreach (TrackEvent e in pista.Events)
            {
                Take toma = e.ActiveTake;
                if (toma == null || toma.Media == null || !String.Equals(toma.Media.FilePath, media, StringComparison.OrdinalIgnoreCase)) continue;
                double desde = S(toma.Offset), largo = (S(e.End) - S(e.Start)) * e.PlaybackRate;
                if (fuente >= desde - Tol && fuente < desde + largo - Tol)
                    r.Add(S(e.Start) + (fuente - desde) / e.PlaybackRate);
            }
        return r;
    }

    static double MasCerca(List<double> l, double t)
    {
        double mejor = l[0];
        foreach (double x in l) if (Math.Abs(x - t) < Math.Abs(mejor - t)) mejor = x;
        return mejor;
    }

    // Vuelve a poner cada marcador/region anclado sobre su clip. Devuelve
    // cuantos se movieron; "perdidos" son los que ya no tienen clip (esa
    // parte se borro).
    public static int Reubicar(Project p, string veg, out int perdidos, out int revisados)
    {
        perdidos = 0; revisados = 0;
        List<Ancla> anclas = Cargar(veg);
        Dictionary<string, Ancla> marcas = new Dictionary<string, Ancla>(), regiones = new Dictionary<string, Ancla>();
        foreach (Ancla a in anclas) (a.Region ? regiones : marcas)[a.Etiqueta] = a;
        int movidos = 0;

        List<Marker> lista = new List<Marker>();
        foreach (Marker m in p.Markers) lista.Add(m);
        foreach (Region r in p.Regions) lista.Add(r);
        foreach (Marker m in lista)
        {
            Region region = m as Region;
            Ancla a;
            if (!(region != null ? regiones : marcas).TryGetValue(m.Label ?? "", out a)) continue;
            revisados++;
            List<double> donde = Donde(p, a.Media, a.Fuente);
            if (donde.Count == 0) { perdidos++; continue; }
            double actual = S(m.Position), nuevo = MasCerca(donde, actual);
            bool cambio = Math.Abs(nuevo - actual) > 0.001;
            if (region != null && a.FuenteFin >= 0)
            {
                List<double> fines = Donde(p, a.MediaFin, a.FuenteFin);
                if (fines.Count > 0)
                {
                    double fin = MasCerca(fines, nuevo + S(region.Length));
                    if (fin > nuevo + 0.01 && Math.Abs((fin - nuevo) - S(region.Length)) > 0.001)
                    {
                        try { region.Length = Timecode.FromMilliseconds((fin - nuevo) * 1000); cambio = true; } catch { }
                    }
                }
            }
            if (Math.Abs(nuevo - actual) > 0.001)
            {
                try { m.Position = Timecode.FromMilliseconds(nuevo * 1000); } catch { }
            }
            if (cambio) movidos++;
        }
        return movidos;
    }
}

// ---- src/comun/Audio.cs ----

// =====================================================================
// Analisis de audio y deteccion (sin dependencias de Vegas)
// =====================================================================

public struct Rango
{
    public double Inicio, Fin; // segundos en la linea de tiempo
    public Rango(double inicio, double fin) { Inicio = inicio; Fin = fin; }
}

// Tramo que se reproduce mas rapido (Factor 2 = el doble de rapido).
public class Acelerado
{
    public double Inicio, Fin, Factor;
    public Acelerado(double inicio, double fin, double factor) { Inicio = inicio; Fin = fin; Factor = factor; }

    public double Ahorro { get { return (Fin - Inicio) * (1 - 1 / Factor); } }

    // Nueva posicion de un instante despues de acelerar los tramos.
    public static double Posicion(double t, List<Acelerado> tramos)
    {
        double ahorro = 0;
        foreach (Acelerado a in tramos)
        {
            if (t >= a.Fin - 1e-6) ahorro += a.Ahorro;
            else if (t > a.Inicio) ahorro += (t - a.Inicio) * (1 - 1 / a.Factor);
        }
        return t - ahorro;
    }
}

public class Analisis
{
    public const double Paso = 0.01;  // 10 ms por medicion
    public float[] Db;                // nivel RMS de cada paso, en dBFS
    public double Inicio;             // segundo de la linea de tiempo del primer paso
    public double Duracion { get { return Db.Length * Paso; } }

    // Une varias pistas: en cada instante cuenta la que suene mas fuerte, asi
    // hay voz si habla cualquiera de ellas.
    public static Analisis Combinar(List<Analisis> pistas)
    {
        if (pistas.Count == 1) return pistas[0];
        int n = int.MaxValue;
        foreach (Analisis a in pistas) n = Math.Min(n, a.Db.Length);
        Analisis r = new Analisis();
        r.Inicio = pistas[0].Inicio;
        r.Db = new float[n];
        for (int i = 0; i < n; i++)
        {
            float m = -100;
            foreach (Analisis a in pistas) if (a.Db[i] > m) m = a.Db[i];
            r.Db[i] = m;
        }
        return r;
    }
}

public static class WavNiveles
{
    public static Analisis Leer(string ruta, double paso)
    {
        using (FileStream fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        using (BinaryReader br = new BinaryReader(fs))
        {
            if (new string(br.ReadChars(4)) != "RIFF") throw new Exception("El archivo no es WAV.");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new Exception("El archivo no es WAV.");

            int formato = 0, canales = 0, frecuencia = 0, bits = 0;
            long datos = -1, largo = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(br.ReadChars(4));
                long tam = br.ReadUInt32();
                long siguiente = fs.Position + tam + (tam & 1);
                if (id == "fmt ")
                {
                    formato = br.ReadUInt16();
                    canales = br.ReadUInt16();
                    frecuencia = br.ReadInt32();
                    br.ReadInt32(); br.ReadUInt16();
                    bits = br.ReadUInt16();
                    if (formato == 0xFFFE && tam >= 40)
                    {
                        br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32();
                        formato = br.ReadUInt16(); // subformato: 1 PCM, 3 float
                    }
                }
                else if (id == "data")
                {
                    datos = fs.Position;
                    largo = Math.Min(tam, fs.Length - datos);
                    break;
                }
                fs.Position = siguiente;
            }
            if (datos < 0 || canales == 0) throw new Exception("WAV sin datos de audio.");
            if (!(formato == 1 && (bits == 16 || bits == 24 || bits == 32)) && !(formato == 3 && bits == 32))
                throw new Exception("Formato WAV no soportado (" + formato + ", " + bits + " bits).");

            int bytesMuestra = bits / 8;
            int bytesCuadro = bytesMuestra * canales;
            int cuadrosPorPaso = Math.Max(1, (int)Math.Round(frecuencia * paso));
            long cuadros = largo / bytesCuadro;
            int pasos = (int)(cuadros / cuadrosPorPaso);
            float[] db = new float[pasos];

            byte[] buf = new byte[cuadrosPorPaso * bytesCuadro];
            fs.Position = datos;
            for (int p = 0; p < pasos; p++)
            {
                int leidos = 0;
                while (leidos < buf.Length)
                {
                    int n = fs.Read(buf, leidos, buf.Length - leidos);
                    if (n <= 0) break;
                    leidos += n;
                }
                double suma = 0;
                int muestras = leidos / bytesMuestra;
                for (int i = 0; i < muestras; i++)
                {
                    int o = i * bytesMuestra;
                    double x;
                    if (formato == 3) x = BitConverter.ToSingle(buf, o);
                    else if (bits == 16) x = BitConverter.ToInt16(buf, o) / 32768.0;
                    else if (bits == 24) x = ((buf[o] | (buf[o + 1] << 8) | ((sbyte)buf[o + 2] << 16))) / 8388608.0;
                    else x = BitConverter.ToInt32(buf, o) / 2147483648.0;
                    suma += x * x;
                }
                double rms = muestras > 0 ? Math.Sqrt(suma / muestras) : 0;
                db[p] = rms > 1e-5 ? (float)(20 * Math.Log10(rms)) : -100f;
            }

            Analisis a = new Analisis();
            a.Db = db;
            return a;
        }
    }
}


public static class Formato
{
    public static string Tiempo(double s)
    {
        if (s < 0) s = 0;
        int t = (int)Math.Round(s);
        if (t >= 3600) return (t / 3600) + ":" + ((t / 60) % 60).ToString("00") + ":" + (t % 60).ToString("00");
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    // Con decimas: 1:02.5
    public static string TiempoPreciso(double s)
    {
        if (s < 0) s = 0;
        int d = (int)Math.Round(s * 10);
        int t = d / 10;
        string r = ((t / 60) % 60).ToString(t >= 3600 ? "00" : "0") + ":" + (t % 60).ToString("00") + "." + (d % 10);
        return t >= 3600 ? (t / 3600) + ":" + r : r;
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

// ---- src/comun/Transcripcion.cs ----

// =====================================================================
// Transcripcion del proyecto (<proyecto>.vegascut.json junto al .veg)
//
// Guarda los tiempos tal como estaban al transcribir y una lista de los
// cortes que hicieron las herramientas despues. Asi los tiempos se pueden
// llevar a la linea de tiempo actual, y si deshaces un corte (Ctrl+Z) se
// nota porque la duracion del proyecto vuelve a la de antes.
// =====================================================================

public class Palabra
{
    public double Inicio, Fin, Prob;
    public string Texto;
}

public class Segmento
{
    public int Hablante;
    public double Inicio, Fin;
    public string Texto;
    public List<Palabra> Palabras = new List<Palabra>();
}

public class Hablante
{
    public string Etiqueta;  // "A11"
    public string Nombre;    // como se llama la persona (editable)
    public string Archivo;
    public bool Voz;         // true: se transcribio; false: solo niveles (juego, musica)
    public float[] Nivel;    // dB RMS por segundo
    public float[] Pico;     // dB maximo por segundo
    public List<Fuente> Fuentes = new List<Fuente>(); // de donde salia cada parte al transcribir
}

// Un evento de la pista al transcribir: que archivo (y flujo de audio) sonaba
// de Inicio a Fin y desde que segundo del archivo. Con esto una palabra se
// puede encontrar en la linea de tiempo aunque despues edites a mano.
public class Fuente
{
    public double Inicio, Fin, Desde, Velocidad = 1;
    public string Media = "";
    public int Flujo;
}

public class Edicion
{
    public List<Rango> Quitados = new List<Rango>();
    public List<Acelerado> Acelerados = new List<Acelerado>();
    public double Antes, Despues; // duracion del proyecto
}

public class Transcripcion
{
    public string Proyecto = "", Creada = "", Idioma = "es", Modelo = "";
    public double Inicio, Duracion;      // rango transcrito (tiempos originales)
    public double DuracionProyecto;      // al transcribir
    public List<Hablante> Hablantes = new List<Hablante>();
    public List<Segmento> Segmentos = new List<Segmento>();
    public List<Edicion> Ediciones = new List<Edicion>();

    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut.json");
    }

    // --------------------------------------------------- tiempos actuales

    // Lleva un tiempo original a la linea de tiempo actual. Devuelve NaN si
    // ese instante fue cortado.
    public double Mapear(double t)
    {
        foreach (Edicion e in Ediciones)
        {
            if (e.Acelerados.Count > 0) t = Acelerado.Posicion(t, e.Acelerados);
            double q = 0;
            foreach (Rango r in e.Quitados)
            {
                if (t >= r.Fin - 1e-6) q += r.Fin - r.Inicio;
                else if (t > r.Inicio + 1e-6) return double.NaN;
            }
            t -= q;
        }
        return t;
    }

    // Archivo, flujo y segundo del archivo que sonaba en el instante
    // original t en la pista de ese hablante.
    public bool AFuente(int hablante, double t, out Fuente f, out double segundo)
    {
        f = null; segundo = 0;
        if (hablante < 0 || hablante >= Hablantes.Count) return false;
        foreach (Fuente x in Hablantes[hablante].Fuentes)
            if (t >= x.Inicio - 1e-6 && t < x.Fin - 1e-6)
            {
                f = x;
                segundo = x.Desde + (t - x.Inicio) * x.Velocidad;
                return true;
            }
        return false;
    }

    // Frases que Whisper inventa en los silencios (vienen de los subtitulos de
    // YouTube con los que se entreno). No se le mandan a la IA.
    static readonly string[] Inventadas = { "suscribeteacanal", "suscribeteanuestrocanal", "suscribete", "graciasporver",
        "subtitulosrealizadosporlacomunidaddeamaraorg", "subtitulosporlacomunidaddeamaraorg", "amaraorg",
        "noolvidesdesuscribirte", "dalelike" };

    public static bool Alucinacion(string texto)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (texto ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(c);
        string n = sb.ToString().Replace("suscribetealcanal", "suscribeteacanal");
        if (n.Length == 0) return false;
        foreach (string x in Inventadas)
            if (n == x || (x.Length >= 10 && n.Contains(x) && n.Length <= x.Length + 12)) return true;
        return false;
    }

    public bool TieneFuentes
    {
        get { foreach (Hablante h in Hablantes) if (h.Fuentes.Count > 0) return true; return false; }
    }

    // Segmentos con tiempos de la linea de tiempo actual, sin lo cortado.
    public List<Segmento> SegmentosActuales()
    {
        List<Segmento> r = new List<Segmento>();
        foreach (Segmento s in Segmentos)
        {
            if (Alucinacion(s.Texto)) continue;
            Segmento n = new Segmento();
            n.Hablante = s.Hablante;
            StringBuilder texto = new StringBuilder();
            if (s.Palabras.Count > 0)
            {
                foreach (Palabra p in s.Palabras)
                {
                    double a = Mapear(p.Inicio), b = Mapear(p.Fin);
                    if (double.IsNaN(a) && double.IsNaN(b)) continue;
                    if (double.IsNaN(a)) a = b - Math.Min(0.2, p.Fin - p.Inicio);
                    if (double.IsNaN(b)) b = a + Math.Min(0.2, p.Fin - p.Inicio);
                    Palabra q = new Palabra();
                    q.Inicio = a; q.Fin = b; q.Prob = p.Prob; q.Texto = p.Texto;
                    n.Palabras.Add(q);
                    texto.Append(p.Texto);
                }
                if (n.Palabras.Count == 0) continue;
                n.Inicio = n.Palabras[0].Inicio;
                n.Fin = n.Palabras[n.Palabras.Count - 1].Fin;
                n.Texto = texto.ToString().Trim();
            }
            else
            {
                n.Inicio = Mapear(s.Inicio); n.Fin = Mapear(s.Fin); n.Texto = s.Texto;
                if (double.IsNaN(n.Inicio) || double.IsNaN(n.Fin)) continue;
            }
            r.Add(n);
        }
        r.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
        return r;
    }

    public double DuracionEsperada
    {
        get { return Ediciones.Count > 0 ? Ediciones[Ediciones.Count - 1].Despues : DuracionProyecto; }
    }

    // Compara con la duracion actual del proyecto. Si deshiciste cortes
    // (Ctrl+Z), los quita de la lista. Devuelve "" si todo cuadra o un aviso.
    public string Sincronizar(double duracionActual)
    {
        const double tol = 0.05;
        bool cambio = false;
        while (Ediciones.Count > 0 && Math.Abs(DuracionEsperada - duracionActual) > tol &&
               Math.Abs(Ediciones[Ediciones.Count - 1].Antes - duracionActual) <= tol)
        {
            Ediciones.RemoveAt(Ediciones.Count - 1);
            cambio = true;
        }
        if (Math.Abs(DuracionEsperada - duracionActual) <= tol)
            return cambio ? "Se detect\u00f3 un Ctrl+Z: la transcripci\u00f3n se ajust\u00f3." : "";
        return "El proyecto cambi\u00f3 desde la transcripci\u00f3n (dura " + Formato.Tiempo(duracionActual) +
               ", se esperaba " + Formato.Tiempo(DuracionEsperada) + "). Si editaste a mano, los tiempos " +
               "pueden no cuadrar: vuelve a transcribir para m\u00e1s precisi\u00f3n.";
    }

    // La llaman las herramientas que cortan (Quitar silencios, Momentos).
    public static string RegistrarCortes(string veg, List<Rango> quitados, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Quitados.AddRange(quitados);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    public static string RegistrarAceleracion(string veg, List<Acelerado> tramos, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Acelerados.AddRange(tramos);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    static string RegistrarEdicion(string veg, Edicion e)
    {
        string ruta = RutaPara(veg);
        if (ruta == null || !File.Exists(ruta)) return "";
        try
        {
            Transcripcion t = Cargar(ruta);
            t.Sincronizar(e.Antes);
            t.Ediciones.Add(e);
            t.Guardar(ruta);
            return "\nLa transcripci\u00f3n tambi\u00e9n se ajust\u00f3 a los cortes.";
        }
        catch (Exception ex)
        {
            return "\nNo se pudo ajustar la transcripci\u00f3n: " + ex.Message;
        }
    }

    // ------------------------------------------------- niveles por segundo

    public static void NivelesPorSegundo(Analisis a, out float[] nivel, out float[] pico)
    {
        int porSegundo = (int)Math.Round(1 / Analisis.Paso);
        int n = (a.Db.Length + porSegundo - 1) / porSegundo;
        nivel = new float[n];
        pico = new float[n];
        for (int s = 0; s < n; s++)
        {
            double energia = 0;
            float max = -100;
            int cuantos = 0;
            for (int i = s * porSegundo; i < Math.Min(a.Db.Length, (s + 1) * porSegundo); i++)
            {
                energia += Math.Pow(10, a.Db[i] / 10.0);
                if (a.Db[i] > max) max = a.Db[i];
                cuantos++;
            }
            nivel[s] = cuantos > 0 ? (float)Math.Round(10 * Math.Log10(Math.Max(1e-10, energia / cuantos))) : -100;
            pico[s] = (float)Math.Round(max);
        }
    }

    // ------------------------------------------------------ Whisper (JSON)

    // Agrega los segmentos de la salida JSON de Whisper (tiempos relativos al
    // WAV) desplazados al inicio del rango.
    public void AgregarWhisper(string json, int hablante, double desplazamiento)
    {
        object o = Json.Leer(json);
        foreach (object s in Json.Lista(o, "segments"))
        {
            Segmento seg = new Segmento();
            seg.Hablante = hablante;
            seg.Inicio = Json.Numero(s, "start", 0) + desplazamiento;
            seg.Fin = Json.Numero(s, "end", 0) + desplazamiento;
            seg.Texto = Json.Texto(s, "text").Trim();
            foreach (object w in Json.Lista(s, "words"))
            {
                Palabra p = new Palabra();
                p.Inicio = Json.Numero(w, "start", seg.Inicio - desplazamiento) + desplazamiento;
                p.Fin = Json.Numero(w, "end", seg.Fin - desplazamiento) + desplazamiento;
                p.Prob = Json.Numero(w, "probability", Json.Numero(w, "prob", 1));
                p.Texto = Json.Texto(w, "word");
                if (p.Texto.Length == 0) p.Texto = Json.Texto(w, "text");
                seg.Palabras.Add(p);
            }
            if (seg.Texto.Length > 0 || seg.Palabras.Count > 0) Segmentos.Add(seg);
        }
        Segmentos.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
    }

    // ---------------------------------------------------- guardar / cargar

    static double R(double v) { return Math.Round(v, 3); }

    public void Guardar(string ruta)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-transcripcion";
        d["version"] = 1;
        d["proyecto"] = Proyecto;
        d["creada"] = Creada;
        d["idioma"] = Idioma;
        d["modelo"] = Modelo;
        d["inicio"] = R(Inicio);
        d["duracion"] = R(Duracion);
        d["duracionProyecto"] = R(DuracionProyecto);

        List<object> hs = new List<object>();
        foreach (Hablante h in Hablantes)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["etiqueta"] = h.Etiqueta;
            x["nombre"] = h.Nombre;
            x["archivo"] = h.Archivo;
            x["voz"] = h.Voz;
            x["nivel"] = h.Nivel ?? new float[0];
            x["pico"] = h.Pico ?? new float[0];
            List<object> fs = new List<object>();
            // Fuentes compactas: [inicio, fin, desde, velocidad, flujo, archivo]
            foreach (Fuente f in h.Fuentes)
                fs.Add(new List<object> { R(f.Inicio), R(f.Fin), R(f.Desde), Math.Round(f.Velocidad, 4), f.Flujo, f.Media });
            x["fuentes"] = fs;
            hs.Add(x);
        }
        d["hablantes"] = hs;

        List<object> ss = new List<object>();
        foreach (Segmento s in Segmentos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["h"] = s.Hablante;
            x["inicio"] = R(s.Inicio);
            x["fin"] = R(s.Fin);
            x["texto"] = s.Texto;
            List<object> ps = new List<object>();
            // Palabras compactas: [inicio, fin, probabilidad, texto]
            foreach (Palabra p in s.Palabras)
                ps.Add(new List<object> { R(p.Inicio), R(p.Fin), Math.Round(p.Prob, 2), p.Texto });
            x["palabras"] = ps;
            ss.Add(x);
        }
        d["segmentos"] = ss;

        List<object> es = new List<object>();
        foreach (Edicion e in Ediciones)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["antes"] = R(e.Antes);
            x["despues"] = R(e.Despues);
            List<object> qs = new List<object>();
            foreach (Rango r in e.Quitados) qs.Add(new List<object> { R(r.Inicio), R(r.Fin) });
            x["quitados"] = qs;
            if (e.Acelerados.Count > 0)
            {
                List<object> acs = new List<object>();
                foreach (Acelerado a in e.Acelerados) acs.Add(new List<object> { R(a.Inicio), R(a.Fin), a.Factor });
                x["acelerados"] = acs;
            }
            es.Add(x);
        }
        d["ediciones"] = es;

        File.WriteAllText(ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    public static Transcripcion Cargar(string ruta)
    {
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        if (Json.Texto(o, "formato") != "vegas-cut-transcripcion")
            throw new Exception("El archivo no es una transcripci\u00f3n de vegas-cut.");
        Transcripcion t = new Transcripcion();
        t.Proyecto = Json.Texto(o, "proyecto");
        t.Creada = Json.Texto(o, "creada");
        t.Idioma = Json.Texto(o, "idioma");
        t.Modelo = Json.Texto(o, "modelo");
        t.Inicio = Json.Numero(o, "inicio", 0);
        t.Duracion = Json.Numero(o, "duracion", 0);
        t.DuracionProyecto = Json.Numero(o, "duracionProyecto", 0);

        foreach (object x in Json.Lista(o, "hablantes"))
        {
            Hablante h = new Hablante();
            h.Etiqueta = Json.Texto(x, "etiqueta");
            h.Nombre = Json.Texto(x, "nombre");
            h.Archivo = Json.Texto(x, "archivo");
            object voz;
            Dictionary<string, object> dx = (Dictionary<string, object>)x;
            h.Voz = dx.TryGetValue("voz", out voz) && voz is bool && (bool)voz;
            h.Nivel = Numeros(Json.Lista(x, "nivel"));
            h.Pico = Numeros(Json.Lista(x, "pico"));
            foreach (object q in Json.Lista(x, "fuentes"))
            {
                List<object> l = q as List<object>;
                if (l == null || l.Count < 6) continue;
                Fuente f = new Fuente();
                f.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                f.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                f.Desde = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                f.Velocidad = Convert.ToDouble(l[3], CultureInfo.InvariantCulture);
                f.Flujo = Convert.ToInt32(l[4], CultureInfo.InvariantCulture);
                f.Media = l[5] as string ?? "";
                h.Fuentes.Add(f);
            }
            t.Hablantes.Add(h);
        }

        foreach (object x in Json.Lista(o, "segmentos"))
        {
            Segmento s = new Segmento();
            s.Hablante = (int)Json.Numero(x, "h", 0);
            s.Inicio = Json.Numero(x, "inicio", 0);
            s.Fin = Json.Numero(x, "fin", 0);
            s.Texto = Json.Texto(x, "texto");
            foreach (object p in Json.Lista(x, "palabras"))
            {
                List<object> l = p as List<object>;
                if (l == null || l.Count < 4) continue;
                Palabra w = new Palabra();
                w.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                w.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                w.Prob = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                w.Texto = l[3] as string ?? "";
                s.Palabras.Add(w);
            }
            t.Segmentos.Add(s);
        }

        foreach (object x in Json.Lista(o, "ediciones"))
        {
            Edicion e = new Edicion();
            e.Antes = Json.Numero(x, "antes", 0);
            e.Despues = Json.Numero(x, "despues", 0);
            foreach (object q in Json.Lista(x, "quitados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 2)
                    e.Quitados.Add(new Rango(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                             Convert.ToDouble(l[1], CultureInfo.InvariantCulture)));
            }
            foreach (object q in Json.Lista(x, "acelerados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 3)
                    e.Acelerados.Add(new Acelerado(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[1], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[2], CultureInfo.InvariantCulture)));
            }
            t.Ediciones.Add(e);
        }
        return t;
    }

    static float[] Numeros(List<object> l)
    {
        float[] r = new float[l.Count];
        for (int i = 0; i < l.Count; i++) r[i] = (float)Convert.ToDouble(l[i], CultureInfo.InvariantCulture);
        return r;
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
        Dictionary<string, object> cuerpo = new Dictionary<string, object>();
        if (!String.IsNullOrEmpty(instrucciones))
            cuerpo["systemInstruction"] = Partes(instrucciones, null);
        cuerpo["contents"] = new List<object> { Partes(mensaje, "user") };
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

    public DialogoNombre(string sugerido)
    {
        Text = "Guardar perfil";
        ClientSize = new Size(380, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;

        Controls.Add(Pos(new Etiqueta("Nombre del perfil", Tema.Seccion, Tema.Texto), 20, 16, 340, 22));
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
