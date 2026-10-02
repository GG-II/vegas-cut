using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Estructura y narracion: Gemini propone, tu eliges que entra y se aplica.
class VentanaPlan : VentanaBase
{
    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly Informe inf;
    readonly Transcripcion trans;
    readonly SerieProyecto serie;
    readonly List<CapSerie> caps;
    readonly FormatoSerie formato;
    readonly string papel;
    readonly VideoEvent plantilla;
    Plan plan;
    bool cargando, trabajando;
    public bool Aplicado;

    CampoTexto txtIndicaciones = new CampoTexto();
    Boton btnPedir = new Boton("Pedir a Gemini", EstiloBoton.Primario);
    Etiqueta lblResumen, lblPlantilla, lblEstado;
    Lista lstItems = new Lista();
    Boton chipGancho = Chip("Gancho al inicio"), chipNarracion = Chip("Narración con voz"), chipBajar = Chip("Bajar el juego al narrar"),
          chipAvances = Chip("Avances en pantalla"), chipPlaceholders = Chip("Placeholders"), chipRegiones = Chip("Regiones");
    Boton btnGuion = new Boton("Exportar guion", EstiloBoton.Secundario);
    Boton btnAplicar = new Boton("Aplicar al proyecto", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    static Boton Chip(string t)
    {
        Boton b = new Boton(t, EstiloBoton.Chip);
        b.Activo = true;
        b.Click += delegate { b.Activo = !b.Activo; };
        return b;
    }

    public VentanaPlan(Vegas vegas, Informe inf, Transcripcion trans, SerieProyecto serie, List<CapSerie> caps,
                       FormatoSerie formato, string papel, VideoEvent plantilla) : base("Estructura y narración", 1040)
    {
        this.vegas = vegas; this.inf = inf; this.trans = trans; this.serie = serie; this.caps = caps;
        this.formato = formato; this.papel = papel; this.plantilla = plantilla;
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Estructura y narración", "Gemini propone gancho, secciones, narración, avances y recursos según la serie y el papel del capítulo.");
        int y = 92;
        Texto("INDICACIONES (opcional: qué destacar, qué no contar, chistes internos…)", Tema.Pequena, Tema.TextoSuave, m, y, w - 200, 18);
        txtIndicaciones.Multilinea = true;
        Pos(txtIndicaciones, m, y + 20, w - 196, 52);
        Pos(btnPedir, m + w - 180, y + 20, 180, 52);
        y += 82;
        lblResumen = Texto("", Tema.Normal, Tema.Texto, m, y, w, 40);
        y += 44;
        lstItems.Columns.Add("Tiempo", 96);
        lstItems.Columns.Add("Qué", 110);
        lstItems.Columns.Add("Detalle", w - 96 - 110 - SystemInformation.VerticalScrollBarWidth - 4);
        Pos(lstItems, m, y, w, 300);
        y += 310;
        int cx = m;
        foreach (Boton c in new Boton[] { chipGancho, chipNarracion, chipBajar, chipAvances, chipPlaceholders, chipRegiones })
        {
            int cw = TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 34;
            Pos(c, cx, y, cw, 30);
            cx += cw + 8;
        }
        y += 38;
        lblPlantilla = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 34);
        y += 38;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 520, 40);
        Pos(btnGuion, m + w - 510, y, 150, 40);
        Pos(btnAplicar, m + w - 350, y, 200, 40);
        Pos(btnCerrar, m + w - 140, y, 140, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        chipNarracion.Enabled = chipBajar.Enabled = formato.Narrador;
        if (!formato.Narrador) chipNarracion.Activo = chipBajar.Activo = false;
        chipAvances.Enabled = formato.Avance != "Ninguno";
        lblPlantilla.Text = plantilla != null
            ? "Placeholders: copian el evento que tenías seleccionado (en " + Nombre(plantilla.Track) + ", " + Formato.Tiempo(plantilla.Start.ToMilliseconds() / 1000.0) +
              "): sus efectos, movimiento y fundidos. Después, «Reemplazar placeholders» pone tus imágenes."
            : "Placeholders: texto simple. Para que copien tus efectos y movimiento, cierra, selecciona una imagen ya editada en la línea de tiempo y vuelve a abrir.";

        lstItems.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando && e.Item.Tag != null) ((ItemPlan)e.Item.Tag).Elegido = e.Item.Checked; };
        lstItems.DoubleClick += delegate
        {
            if (lstItems.SelectedIndices.Count == 0) return;
            ItemPlan i = (ItemPlan)lstItems.Items[lstItems.SelectedIndices[0]].Tag;
            try
            {
                vegas.Transport.CursorPosition = Timecode.FromMilliseconds(i.Inicio * 1000);
                vegas.Transport.SelectionStart = Timecode.FromMilliseconds(i.Inicio * 1000);
                vegas.Transport.SelectionLength = Timecode.FromMilliseconds(Math.Max(0.1, i.Duracion) * 1000);
            }
            catch { }
        };
        btnPedir.Click += delegate { Pedir(); };
        btnGuion.Click += delegate { ExportarGuion(true); };
        btnAplicar.Click += delegate { Aplicar(); };
        btnCerrar.Click += delegate { Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; else GuardarElecciones(); };

        plan = LogicaPlan.Cargar(vegas.Project.FilePath, inf.M.Duracion, inf.Reglas.PPM);
        Mostrar();
        if (plan != null)
            Estado("Plan guardado del " + (plan.Generado.Length > 0 ? plan.Generado : "proyecto") + ". «Pedir a Gemini» hace uno nuevo." +
                   (LogicaPlan.Aplicado(vegas.Project.FilePath) != null ? " Ya se aplicó una vez." : ""), false);
        else if (String.IsNullOrEmpty(config.GeminiClave))
            Estado("Falta la clave de Gemini: ejecuta «ConfigurarVegasCut».", true);
        else Estado("Pulsa «Pedir a Gemini».", false);
        if (trans == null)
            Estado("Este proyecto no tiene transcripción: Gemini no sabría qué se dice. Ejecuta Transcribir primero.", true);
        else if (!trans.TieneFuentes)
            Estado("La transcripción es de una versión vieja (sin fuentes): con el gancho al inicio dejaría de seguir al video. " +
                   "Vuelve a transcribir este proyecto antes de aplicar.", true);
    }

    static string Nombre(Track t) { return String.IsNullOrEmpty(t.Name) ? "la pista " + (t.Index + 1) : t.Name; }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Mostrar()
    {
        cargando = true;
        lstItems.Items.Clear();
        bool hay = plan != null;
        btnAplicar.Enabled = btnGuion.Enabled = hay;
        lblResumen.Text = hay ? plan.Resumen + (plan.Estructura.Length > 0 ? "\n" + plan.Estructura : "") : "";
        if (hay)
            foreach (ItemPlan i in plan.Items)
            {
                string tiempo = i.Tipo == "avance" ? Formato.Tiempo(i.Inicio) : Formato.Tiempo(i.Inicio) + "–" + Formato.Tiempo(i.Fin);
                ListViewItem it = new ListViewItem(tiempo);
                string que = LogicaPlan.NombreTipo(i.Tipo) + (i.Id.Length > 0 ? " " + i.Id : "");
                it.SubItems.Add(que);
                string det = i.Texto;
                if (i.Tipo == "narracion") det = (i.Clase.Length > 0 ? "(" + i.Clase + ") " : "") + "«" + i.Texto + "»";
                else if (i.Tipo == "recurso") det = i.Clase + ": " + i.Texto;
                else if (i.Tipo == "recorte") det = (i.Clase == "acelerar" ? "Acelerar: " : "Quitar: ") + i.Texto;
                else if (i.Tipo == "seccion" && i.Detalle.Length > 0) det = i.Texto + " — " + i.Detalle;
                else if (i.Tipo == "gancho") det = "Copiar al inicio: " + i.Texto;
                it.SubItems.Add(det);
                it.Checked = i.Elegido;
                it.ForeColor = i.Tipo == "narracion" ? Tema.Voz : i.Tipo == "recorte" ? Tema.Silencio :
                               i.Tipo == "gancho" || i.Tipo == "avance" ? Tema.Acento : Tema.Texto;
                it.Tag = i;
                lstItems.Items.Add(it);
            }
        cargando = false;
        chipGancho.Enabled = hay && plan.Gancho != null;
        if (hay && plan.Gancho == null) chipGancho.Activo = false;
    }

    void Habilitar(bool si)
    {
        foreach (Control c in new Control[] { btnPedir, btnAplicar, btnGuion, btnCerrar, lstItems, txtIndicaciones }) c.Enabled = si;
    }

    void Pedir()
    {
        if (String.IsNullOrEmpty(config.GeminiClave)) { Estado("Falta la clave de Gemini: ejecuta «ConfigurarVegasCut».", true); return; }
        string instr = LogicaPlan.Instrucciones(formato, serie != null ? serie.Tipo : "Gameplay", papel, inf.Reglas);
        string msg = LogicaPlan.Mensaje(trans, inf.M.Duracion, inf, serie != null ? Serie.Contexto(serie, caps) : "",
                                        LogicaPlan.Anteriores(caps), txtIndicaciones.Text, LogicaPlan.Pausas(trans, inf.M.Duracion, 2.5));
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        double dur = inf.M.Duracion;
        int ppm = inf.Reglas.PPM;
        trabajando = true;
        Habilitar(false);
        Estado("Gemini está armando la estructura (puede tardar un minuto)…", false);
        Thread hilo = new Thread(delegate ()
        {
            string respuesta = null, error = null;
            try { respuesta = Gemini.Generar(clave, modelo, instr, msg, true); }
            catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    Habilitar(true);
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        plan = LogicaPlan.Leer(respuesta, dur, ppm);
                        plan.Generado = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · " + modelo;
                        GuardarElecciones();
                        Mostrar();
                        Estado("✔ " + Contar() + ". Desmarca lo que no quieras y pulsa «Aplicar al proyecto».", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    string Contar()
    {
        List<string> l = new List<string>();
        foreach (string t in LogicaPlan.Tipos)
        {
            int n = plan.De(t).Count;
            if (n > 0) l.Add(n + " " + LogicaPlan.NombreTipo(t).ToLowerInvariant() + (n == 1 ? "" : t == "seccion" ? "es" : t == "narracion" ? "es" : "s"));
        }
        return String.Join(", ", l.ToArray());
    }

    void GuardarElecciones()
    {
        if (plan == null) return;
        try { LogicaPlan.Guardar(vegas.Project.FilePath, plan, null); } catch { }
    }

    string RutaGuion()
    {
        string veg = vegas.Project.FilePath;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-guion.txt");
    }

    void ExportarGuion(bool abrir)
    {
        if (plan == null) return;
        object ap = LogicaPlan.Aplicado(vegas.Project.FilePath);
        double corr = ap != null ? Json.Numero(ap, "corrimiento", 0) : LogicaPlan.Corrimiento(plan, chipGancho.Activo);
        try
        {
            File.WriteAllText(RutaGuion(), LogicaPlan.Guion(plan, Path.GetFileNameWithoutExtension(vegas.Project.FilePath), corr, inf.Reglas.PPM),
                              new UTF8Encoding(true));
            if (abrir) try { System.Diagnostics.Process.Start(RutaGuion()); } catch { }
            Estado("Guion guardado: " + RutaGuion(), false);
        }
        catch (Exception ex) { Estado("No se pudo guardar el guion: " + ex.Message, true); }
    }

    void Aplicar()
    {
        if (plan == null || trabajando) return;
        if (LogicaPlan.Aplicado(vegas.Project.FilePath) != null &&
            MessageBox.Show(this, "Este plan ya se aplicó. Si lo aplicas otra vez se agregan de nuevo la narración, los avances y los " +
                            "placeholders (y se corre otra vez el video si hay gancho). Lo normal es deshacer antes con Ctrl+Z.\n\n¿Aplicar de todos modos?",
                            "Estructura y narración", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        OpcionesPlan op = new OpcionesPlan();
        op.Gancho = chipGancho.Activo && chipGancho.Enabled;
        op.Narracion = chipNarracion.Activo && formato.Narrador;
        op.Bajar = chipBajar.Activo && formato.Narrador;
        op.Avances = chipAvances.Activo && chipAvances.Enabled;
        op.Placeholders = chipPlaceholders.Activo;
        op.Regiones = chipRegiones.Activo;

        ISintetizador voz = null;
        if (op.Narracion) try { voz = new SintetizadorWindows(); } catch { voz = null; }

        trabajando = true;
        Habilitar(false);
        ResultadoPlan r;
        try
        {
            using (UndoBlock u = new UndoBlock("Estructura y narración"))
                r = AplicarPlan.Aplicar(vegas, plan, op, voz, inf.Reglas.PPM, trans, plantilla, formato.NarradorNombre, delegate (string t, double f)
                {
                    Estado(t, false);
                    Application.DoEvents();
                });
        }
        catch (Exception ex)
        {
            trabajando = false;
            Habilitar(true);
            Estado("No se pudo aplicar: " + ex.Message, true);
            return;
        }
        trabajando = false;
        Habilitar(true);
        Aplicado = true;
        Dictionary<string, object> extra = new Dictionary<string, object>();
        extra["aplicado"] = r.Aplicado;
        try { LogicaPlan.Guardar(vegas.Project.FilePath, plan, extra); } catch { }
        ExportarGuion(false);
        Estado(r.Texto() + " Guion en " + Path.GetFileName(RutaGuion()) + "." +
               (r.Avisos.Count > 0 ? " " + String.Join(" ", r.Avisos.ToArray()) : "") + " Ctrl+Z lo deshace todo.", r.Avisos.Count > 0);
    }
}
