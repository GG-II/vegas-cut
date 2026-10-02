using System;
using System.Collections.Generic;
using System.Drawing;
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
        if (String.IsNullOrEmpty(veg)) { MessageBox.Show("Guarda el proyecto primero.", "Producir capítulo"); return; }
        string tr = Transcripcion.RutaPara(veg);
        if (!File.Exists(tr))
        {
            string base_ = CopiaBase.RutaPara(veg);
            MessageBox.Show(File.Exists(base_)
                ? "Este proyecto no tiene transcripción, pero su copia base sí: abre «" + Path.GetFileName(base_) + "» y vuelve a ejecutar."
                : "Primero prepara el episodio (PrepararEpisodio: quitar silencios y transcribir).", "Producir capítulo");
            return;
        }
        using (VentanaProduccion v = new VentanaProduccion(vegas)) v.ShowDialog();
    }
}

// Una tarjeta de propuesta con su cuadro de notas.
class TarjetaPropuesta : Panel
{
    public Propuesta P;
    public Etiqueta Titulo = new Etiqueta("", Tema.Seccion, Tema.Texto), Cuerpo = new Etiqueta("", Tema.Pequena, Tema.TextoSuave);
    public CampoTexto Notas = new CampoTexto();
    public Boton Elegir = new Boton("Elegir esta", EstiloBoton.Chip);
    Etiqueta lblNotas = new Etiqueta("NOTAS", Tema.Pequena, Tema.TextoSuave);
    bool elegida;

    public TarjetaPropuesta()
    {
        BackColor = Tema.Panel;
        foreach (Label l in new Label[] { Titulo, Cuerpo, lblNotas }) l.BackColor = Tema.Panel;
        Cuerpo.TextAlign = ContentAlignment.TopLeft;
        Cuerpo.AutoEllipsis = true;
        Notas.Multilinea = true;
        Controls.Add(Titulo); Controls.Add(Cuerpo); Controls.Add(lblNotas); Controls.Add(Notas); Controls.Add(Elegir);
    }

    public bool Elegida { get { return elegida; } set { elegida = value; Elegir.Activo = value; Elegir.Text = value ? "✔ Elegida" : "Elegir esta"; Invalidate(); } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int w = Width - 24;
        Titulo.SetBounds(12, 10, w, 24);
        Cuerpo.SetBounds(12, 38, w, Height - 38 - 130);
        lblNotas.SetBounds(12, Height - 124, w, 16);
        Notas.SetBounds(12, Height - 106, w, 58);
        Elegir.SetBounds(12, Height - 42, w, 30);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (Pen p = new Pen(elegida ? Tema.Acento : Tema.Borde, elegida ? 2 : 1)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
    }

    public void Mostrar(Propuesta p)
    {
        P = p;
        Titulo.Text = p.Id + " · " + p.Nombre;
        StringBuilder sb = new StringBuilder();
        sb.Append((p.Doble ? "EPISODIO DOBLE" : p.Tipo == "especial" ? "ESPECIAL" : "CAPÍTULO") + " · ~" + Math.Round(p.Minutos) + " min" + (p.Doble ? " cada parte" : "") + "\n");
        if (p.Titulos.Count > 0) sb.Append("«" + String.Join("» / «", p.Titulos.ToArray()) + "»\n");
        sb.Append("\nCold open: " + p.ColdOpen + "\nCierre: " + p.Cierre + "\n\n");
        foreach (string x in p.Escaleta) sb.Append("• " + x + "\n");
        sb.Append("\n" + p.PorQue);
        Cuerpo.Text = sb.ToString();
        Notas.Text = p.Notas ?? "";
    }
}

class VentanaProduccion : VentanaBase
{
    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly string veg, original;
    Transcripcion trans;
    SerieProyecto serie;
    List<CapSerie> caps = new List<CapSerie>();
    FormatoSerie formato;
    string papel = "Normal";
    BibliotecaMusica biblioteca;
    List<ArchivoMusica> musica = new List<ArchivoMusica>();
    double duracion;
    AnalisisCapitulo analisis;
    PlanFinal final;
    bool trabajando, cargando;
    VideoEvent plantilla;   // imagen ya editada para los placeholders (la seleccionada al abrir)

    Segmentado segPaso = new Segmentado(new string[] { "1 · Análisis y propuestas", "2 · Propuesta final" });
    Etiqueta lblContexto, lblAnalisis, lblEstado, lblFinal;
    CampoTexto txtIndicaciones = new CampoTexto();
    Boton btnAnalizar = new Boton("Analizar y proponer", EstiloBoton.Primario);
    TarjetaPropuesta[] tarjetas = { new TarjetaPropuesta(), new TarjetaPropuesta(), new TarjetaPropuesta() };
    CampoTexto txtNotas = new CampoTexto();
    Boton btnFinal = new Boton("Armar propuesta final", EstiloBoton.Primario);
    Lista lstFinal = new Lista();
    CampoTexto txtCambios = new CampoTexto();
    Boton btnAjustar = new Boton("Ajustar", EstiloBoton.Secundario);
    Boton btnProducir = new Boton("Producir", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);
    List<Control> vista1 = new List<Control>(), vista2 = new List<Control>();

    public VentanaProduccion(Vegas vegas) : base("Producir capítulo", 1120)
    {
        this.vegas = vegas;
        veg = vegas.Project.FilePath;
        original = CopiaBase.Original(veg);
        int m = Margen, w = Ancho;
        Encabezado("Producir capítulo", "Del material grabado a un capítulo de serie: análisis, tres propuestas, la propuesta final y producirlo.");
        int y = 92;
        lblContexto = Texto("", Tema.Normal, Tema.Texto, m, y, w - 420, 40);
        Pos(segPaso, m + w - 410, y, 410, 34);
        y += 46;

        // ---- vista 1: analisis y propuestas
        int y1 = y;
        vista1.Add(Texto("INDICACIONES (opcional: qué no puede faltar, qué cortar, a quién darle protagonismo…)", Tema.Pequena, Tema.TextoSuave, m, y1, w - 220, 18));
        txtIndicaciones.Multilinea = true;
        vista1.Add(Pos(txtIndicaciones, m, y1 + 20, w - 216, 48));
        vista1.Add(Pos(btnAnalizar, m + w - 200, y1 + 20, 200, 48));
        y1 += 76;
        lblAnalisis = Texto("", Tema.Pequena, Tema.Texto, m, y1, w, 52);
        vista1.Add(lblAnalisis);
        y1 += 58;
        int tw = (w - 32) / 3;
        for (int i = 0; i < 3; i++)
        {
            TarjetaPropuesta t = tarjetas[i];
            vista1.Add(Pos(t, m + i * (tw + 16), y1, tw, 410));
            int k = i;
            t.Elegir.Click += delegate { Elegir(k); };
        }
        y1 += 420;
        vista1.Add(Texto("OTRAS NOTAS", Tema.Pequena, Tema.TextoSuave, m, y1, 200, 18));
        vista1.Add(Pos(txtNotas, m, y1 + 18, w - 236, 34));
        vista1.Add(Pos(btnFinal, m + w - 220, y1 + 14, 220, 40));
        y1 += 62;

        // ---- vista 2: propuesta final
        int y2 = y;
        lblFinal = Texto("", Tema.Normal, Tema.Texto, m, y2, w, 44);
        vista2.Add(lblFinal);
        y2 += 50;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lstFinal.Columns.Add("Bloque", 140);
        lstFinal.Columns.Add("Material", 120);
        lstFinal.Columns.Add("Qué", 120);
        lstFinal.Columns.Add("Detalle", w - 140 - 120 - 120 - sb);
        vista2.Add(Pos(lstFinal, m, y2, w, 440));
        y2 += 450;
        vista2.Add(Texto("CAMBIOS (lo que quieras distinto; «Ajustar» se lo pide a Gemini)", Tema.Pequena, Tema.TextoSuave, m, y2, w, 18));
        txtCambios.Multilinea = true;
        vista2.Add(Pos(txtCambios, m, y2 + 20, w - 336, 46));
        vista2.Add(Pos(btnAjustar, m + w - 320, y2 + 20, 140, 46));
        vista2.Add(Pos(btnProducir, m + w - 170, y2 + 20, 170, 46));
        y2 += 74;

        int yb = Math.Max(y1, y2);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, yb, w - 160, 40);
        Pos(btnCerrar, m + w - 140, yb, 140, 40);
        ClientSize = new Size(ClientSize.Width, yb + 40 + 20);

        segPaso.Cambio += delegate { if (!cargando) Vista(segPaso.Seleccion); };
        btnAnalizar.Click += delegate { Analizar(); };
        btnFinal.Click += delegate { Final(null); };
        btnAjustar.Click += delegate
        {
            if (txtCambios.Text.Trim().Length == 0) { Estado("Escribe qué cambiar.", true); return; }
            Final(txtCambios.Text.Trim());
        };
        btnProducir.Click += delegate { Producir(); };
        lstFinal.ItemCheck += delegate (object s, ItemCheckEventArgs e)
        {
            if (!cargando && lstFinal.Items[e.Index].Tag == null) e.NewValue = CheckState.Checked;   // los bloques siempre van
        };
        lstFinal.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando && e.Item.Tag != null) { ((ItemFinal)e.Item.Tag).Elegido = e.Item.Checked; Resumen(); } };
        lstFinal.DoubleClick += delegate
        {
            if (lstFinal.SelectedIndices.Count == 0) return;
            ItemFinal i = lstFinal.Items[lstFinal.SelectedIndices[0]].Tag as ItemFinal;
            if (i == null || i.Inicio < 0) return;
            try
            {
                vegas.Transport.CursorPosition = Timecode.FromMilliseconds(i.Inicio * 1000);
                vegas.Transport.SelectionStart = Timecode.FromMilliseconds(i.Inicio * 1000);
                vegas.Transport.SelectionLength = Timecode.FromMilliseconds(Math.Max(0.5, i.Fin - i.Inicio) * 1000);
            }
            catch { }
        };
        btnCerrar.Click += delegate { Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; else Guardar(); };

        foreach (Track t in vegas.Project.Tracks)
        {
            if (t.IsAudio() || plantilla != null) continue;
            foreach (TrackEvent e in t.Events)
                if (e.Selected && e is VideoEvent && !GeneradorTexto.EsTexto(e)) { plantilla = (VideoEvent)e; break; }
        }
        Cargar();
    }

    void Producir()
    {
        if (final == null || trabajando) return;
        string cap = ArmarCapitulo.RutaPara(veg);
        if (MessageBox.Show(this, "Se guarda una copia «" + Path.GetFileName(cap) + "» y el capítulo se arma ahí" +
                (final.Partes.Count > 1 ? " (las dos partes, una después de otra)" : "") + ". Este proyecto queda como está.\n\n" +
                "La música se coloca sin balancear: eso se hace al final con «PasoFinal», junto con la censura.\n\n¿Producir?",
                "Producir capítulo", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Guardar();
        ISintetizador voz = null;
        if (formato.Narrador) try { voz = new SintetizadorWindows(); } catch { voz = null; }
        trabajando = true;
        Habilitar(false);
        ResultadoProduccion r;
        try
        {
            r = ArmarCapitulo.EnCopia(vegas, final, formato, serie != null ? serie.Musica : null, biblioteca, musica, plantilla, voz,
                                      PapelEpisodio.Reglas(formato.Reglas, papel).PPM,
                                      delegate (string t, double f) { Estado(t, false); Application.DoEvents(); });
        }
        catch (Exception ex)
        {
            trabajando = false;
            Habilitar(true);
            Estado("No se pudo producir: " + ex.Message, true);
            return;
        }
        trabajando = false;
        foreach (Control c in new Control[] { btnCerrar }) c.Enabled = true;
        Estado(r.Texto() + (r.Avisos.Count > 0 ? " Avisos: " + String.Join(" ", r.Avisos.ToArray()) : ""), r.Avisos.Count > 0);
        MessageBox.Show(this, r.Texto() + "\n\nAhora estás en «" + Path.GetFileName(cap) + "». El guion quedó junto al proyecto.\n\n" +
                        "Siguiente: revisa el capítulo, graba la narración, «Reemplazar placeholders» (PulirEpisodio) y al final «PasoFinal» " +
                        "(balance de música y censura)." + (r.Avisos.Count > 0 ? "\n\nAvisos:\n" + String.Join("\n", r.Avisos.ToArray()) : ""),
                        "Producir capítulo");
        Close();
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Vista(int i)
    {
        foreach (Control c in vista1) c.Visible = i == 0;
        foreach (Control c in vista2) c.Visible = i == 1;
        cargando = true;
        segPaso.Seleccion = i;
        cargando = false;
    }

    void Cargar()
    {
        duracion = vegas.Project.Length.ToMilliseconds() / 1000.0;
        try
        {
            trans = Transcripcion.Cargar(Transcripcion.RutaPara(veg));
            if (trans.TieneFuentes) trans.Ubicador = PistasVegas.Ubicador(vegas.Project, trans);
        }
        catch (Exception ex) { Estado("No se pudo leer la transcripción: " + ex.Message, true); }
        caps = Serie.DelProyecto(original, out serie);
        formato = serie != null ? serie.Formato : FormatoSerie.Preset(FormatoSerie.TV);
        papel = serie != null ? serie.Papel(original) : "Normal";
        if (serie != null && serie.Musica.Carpeta.Length > 0 && Directory.Exists(serie.Musica.Carpeta))
            try { biblioteca = BibliotecaMusica.Cargar(serie.Musica.Carpeta); } catch { biblioteca = null; }
        musica = LogicaProduccion.MusicaCandidata(biblioteca, serie != null ? serie.Musica : null);
        lblContexto.Text = (serie == null ? "Sin serie (uso el formato Serie de TV por defecto; elígela en Series)" :
                            "Serie " + serie.Nombre + " · " + formato.Nombre + " · «" + papel + "»") +
                           "\nMaterial: " + Formato.Tiempo(duracion) + (veg != original ? " (copia base)" : "") +
                           (biblioteca != null ? " · música: " + musica.Count + " temas" : " · sin biblioteca de música (Series → Música…)");
        if (!formato.EsTV) Estado("La serie no tiene el formato «Serie de TV / anime»: se usa su plantilla por defecto.", false);

        string elegida, ind, notas;
        if (LogicaProduccion.Cargar(veg, duracion, musica.Count, formato.Reglas.PPM, out analisis, out elegida, out ind, out notas, out final))
        {
            txtIndicaciones.Text = ind; txtNotas.Text = notas;
            MostrarAnalisis();
            if (analisis != null)
                for (int i = 0; i < analisis.Propuestas.Count && i < 3; i++)
                    if (analisis.Propuestas[i].Id == elegida) Elegir(i);
            MostrarFinal();
            Estado("Se cargó lo que tenías de este capítulo.", false);
        }
        else MostrarAnalisis();
        Vista(final != null ? 1 : 0);
        segPaso.Habilitar(1, final != null);
    }

    void Guardar()
    {
        if (analisis == null) return;
        LeerNotas();
        Propuesta p = Elegida();
        try { LogicaProduccion.Guardar(veg, analisis, p != null ? p.Id : "", txtIndicaciones.Text, txtNotas.Text, final); } catch { }
    }

    void LeerNotas()
    {
        foreach (TarjetaPropuesta t in tarjetas) if (t.P != null) t.P.Notas = t.Notas.Text.Trim();
    }

    Propuesta Elegida()
    {
        foreach (TarjetaPropuesta t in tarjetas) if (t.Elegida && t.P != null) return t.P;
        return null;
    }

    void Elegir(int k)
    {
        for (int i = 0; i < 3; i++) tarjetas[i].Elegida = i == k && tarjetas[i].P != null;
        btnFinal.Enabled = Elegida() != null && !trabajando;
    }

    void MostrarAnalisis()
    {
        bool hay = analisis != null;
        for (int i = 0; i < 3; i++)
        {
            bool con = hay && i < analisis.Propuestas.Count;   // (Visible da false mientras la ventana no se muestra)
            tarjetas[i].Visible = con;
            if (con) tarjetas[i].Mostrar(analisis.Propuestas[i]);
        }
        lblAnalisis.Text = !hay ? "Pulsa «Analizar y proponer»: Gemini lee todo el material con el contexto de la serie y propone tres formas de hacer el capítulo." :
            analisis.Resumen + "\n" + analisis.Momentos.Count + " momentos · ~" + Math.Round(analisis.MinutosUtiles) + " min útiles" +
            (analisis.DobleRecomendado ? " · recomienda EPISODIO DOBLE: " + analisis.DobleMotivo : "") +
            (analisis.Hilos.Count > 0 ? " · hilos: " + String.Join("; ", analisis.Hilos.ToArray()) : "");
        btnFinal.Enabled = Elegida() != null;
        if (hay && Elegida() == null) Estado("Elige una propuesta (puedes escribir notas en cualquiera) y pulsa «Armar propuesta final».", false);
    }

    void Habilitar(bool si)
    {
        foreach (Control c in new Control[] { btnAnalizar, btnFinal, btnAjustar, btnProducir, btnCerrar, segPaso }) c.Enabled = si;
        if (si) { btnFinal.Enabled = Elegida() != null; segPaso.Habilitar(1, final != null); }
    }

    void Pedir(string instr, string msg, string que, Action<string> listo)
    {
        if (String.IsNullOrEmpty(config.GeminiClave)) { Estado("Falta la clave de Gemini: ejecuta «ConfigurarVegasCut».", true); return; }
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        Habilitar(false);
        Estado(que, false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(clave, modelo, instr, msg, true); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    Habilitar(true);
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try { listo(resp); Guardar(); }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Analizar()
    {
        string tipo = serie != null ? serie.Tipo : "Gameplay";
        string ctx = serie != null ? Serie.Contexto(serie, caps) : "";
        string instr = LogicaProduccion.InstruccionesAnalisis(formato, tipo, papel);
        string msg = LogicaProduccion.MensajeAnalisis(trans, duracion, ctx, LogicaProduccion.Reparto(serie != null ? serie.Musica : null), txtIndicaciones.Text);
        Pedir(instr, msg, "Gemini está viendo todo el material (puede tardar un par de minutos)…", delegate (string r)
        {
            analisis = LogicaProduccion.LeerAnalisis(r, duracion);
            MostrarAnalisis();
            Vista(0);
            Estado("✔ " + analisis.Propuestas.Count + " propuestas. Elige una, escribe notas si quieres y arma la propuesta final.", false);
        });
    }

    void Final(string cambios)
    {
        LeerNotas();
        Propuesta p = Elegida();
        if (analisis == null || p == null) { Estado("Elige una propuesta.", true); return; }
        string instr = LogicaProduccion.InstruccionesFinal(formato, papel, p);
        string msg = LogicaProduccion.MensajeFinal(analisis, p, txtNotas.Text, musica, serie != null ? serie.Musica : null,
                                                   LogicaProduccion.Reparto(serie != null ? serie.Musica : null), trans, cambios != null ? final : null, cambios);
        Pedir(instr, msg, cambios == null ? "Gemini está armando la escaleta final…" : "Gemini está ajustando la escaleta…", delegate (string r)
        {
            final = LogicaProduccion.LeerFinal(r, duracion, musica.Count, formato.Reglas.PPM);
            if (cambios != null) txtCambios.Text = "";
            MostrarFinal();
            segPaso.Habilitar(1, true);
            Vista(1);
            Estado("✔ Escaleta lista. Desmarca lo que no quieras, pide cambios con «Ajustar» o produce el capítulo.", false);
        });
    }

    static readonly Dictionary<string, string> NombreTipo = new Dictionary<string, string> {
        { "clip", "Clip" }, { "texto", "Texto" }, { "musica", "Música" }, { "narracion", "Narración" }, { "recurso", "Recurso" } };

    string NombreBloque(string clave)
    {
        BloqueTV b = formato.Tv.Bloque(clave);
        return b != null ? b.Nombre : clave;
    }

    string Musica(ItemFinal i)
    {
        if (i.Personaje.Length > 0 && serie != null && serie.Musica.Personajes.ContainsKey(i.Personaje))
            return "Tema de " + i.Personaje + ": " + Path.GetFileNameWithoutExtension(serie.Musica.Personajes[i.Personaje].Archivo);
        if (i.Musica >= 0 && i.Musica < musica.Count)
            return musica[i.Musica].Titulo + " (" + String.Join(", ", musica[i.Musica].Animos.ToArray()) + ")";
        return i.Personaje.Length > 0 ? "Tema de " + i.Personaje + " (sin asignar en Música…)" : "?";
    }

    void MostrarFinal()
    {
        cargando = true;
        lstFinal.Items.Clear();
        if (final != null)
        {
            for (int k = 0; k < final.Partes.Count; k++)
            {
                CapituloFinal c = final.Partes[k];
                // Orden: por bloque de la plantilla; dentro, clips en su orden y lo demas por tiempo.
                foreach (BloqueTV b in formato.Tv.Bloques)
                {
                    List<ItemFinal> del = c.Items.FindAll(delegate (ItemFinal i) { return i.Bloque == b.Clave || (i.Bloque.Length == 0 && i.Tipo != "clip" && b.Clave == BloqueDe(c, i)); });
                    ListViewItem cab = new ListViewItem((final.Partes.Count > 1 ? "P" + (k + 1) + " · " : "") + b.Nombre);
                    cab.SubItems.Add(b.Tipo == "contenido" ? Formato.Tiempo(LogicaProduccion.Bloque(c, b.Clave)) : b.Segundos + " s");
                    cab.SubItems.Add(b.Tipo == "kit" ? (formato.Tv.Archivo(b.Clave).Length > 0 ? "kit" : "placeholder") : "");
                    cab.SubItems.Add(b.Clave == "titulo" ? (c.Etapa.Length > 0 ? c.Etapa + " · " : "") + c.Titulo : b.Descripcion);
                    cab.Font = Tema.Negrita; cab.ForeColor = Tema.Acento; cab.Checked = true;
                    lstFinal.Items.Add(cab);
                    foreach (ItemFinal i in del)
                    {
                        ListViewItem it = new ListViewItem("   " + (i.Id.Length > 0 ? i.Id : ""));
                        it.SubItems.Add(i.Inicio < 0 ? "" : i.Tipo == "clip" ? Formato.Tiempo(i.Inicio) + "–" + Formato.Tiempo(i.Fin) : Formato.Tiempo(i.Inicio));
                        it.SubItems.Add(NombreTipo.ContainsKey(i.Tipo) ? NombreTipo[i.Tipo] + (i.Clase.Length > 0 ? " · " + i.Clase : "") : i.Tipo);
                        it.SubItems.Add(i.Tipo == "musica" ? Musica(i) + (i.Texto.Length > 0 ? " — " + i.Texto : "") :
                                        i.Tipo == "clip" ? (i.Texto.Length > 0 ? i.Texto : "(" + Formato.Tiempo(i.Duracion) + ")") :
                                        i.Tipo == "narracion" ? "«" + i.Texto + "»" : i.Texto);
                        it.Checked = i.Elegido;
                        it.ForeColor = i.Tipo == "narracion" ? Tema.Voz : i.Tipo == "musica" ? Color.FromArgb(190, 160, 255) : Tema.Texto;
                        it.Tag = i;
                        lstFinal.Items.Add(it);
                    }
                }
            }
        }
        cargando = false;
        Resumen();
    }

    // Bloque de un texto, musica o narracion sin bloque: el del clip que contiene su tiempo.
    static string BloqueDe(CapituloFinal c, ItemFinal x)
    {
        foreach (ItemFinal i in c.Items)
            if (i.Tipo == "clip" && x.Inicio >= i.Inicio - 0.5 && x.Inicio <= i.Fin + 0.5) return i.Bloque;
        return "acto_a";
    }

    void Resumen()
    {
        if (final == null) { lblFinal.Text = ""; return; }
        ReglasRitmo r = PapelEpisodio.Reglas(formato.Reglas, papel);
        List<string> l = new List<string>();
        foreach (CapituloFinal c in final.Partes)
        {
            double d = LogicaProduccion.Duracion(c, formato.Tv);
            bool ok = d >= r.DuracionMin * 60 - 30 && d <= r.DuracionMax * 60 + 30;
            l.Add("«" + c.Titulo + "» " + Formato.Tiempo(d) + (ok ? " ✔" : " (objetivo " + r.DuracionMin + "–" + r.DuracionMax + " min)"));
        }
        lblFinal.Text = final.Resumen + "\n" + String.Join("   ·   ", l.ToArray());
    }
}
