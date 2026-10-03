using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

// Una casilla de tres estados: que decida la IA (◌), sí (✔) o no (✖). Clic para cambiar.
class OpcionTriple : ControlBase
{
    public string Clave;
    int estado = -1;
    public event EventHandler Cambio;

    public OpcionTriple(string clave, string texto) { Clave = clave; Text = texto; Cursor = Cursors.Hand; Font = Tema.Pequena; }

    public int Estado
    {
        get { return estado; }
        set { if (value == estado) return; estado = value; Invalidate(); if (Cambio != null) Cambio(this, EventArgs.Empty); }
    }

    public int Ancho() { return TextRenderer.MeasureText(Text, Font).Width + 40; }

    protected override void OnClick(EventArgs e) { Estado = estado == -1 ? 1 : estado == 1 ? 0 : -1; base.OnClick(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        Color fondo = estado == 1 ? Color.FromArgb(60, Tema.Acento) : encima ? Tema.CampoHover : Tema.Campo;
        Color borde = estado == 1 ? Tema.Acento : estado == 0 ? Color.FromArgb(150, Tema.Silencio) : Tema.Borde;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), Height / 2f))
        {
            using (SolidBrush b = new SolidBrush(fondo)) g.FillPath(b, p);
            using (Pen pen = new Pen(borde)) g.DrawPath(pen, p);
        }
        string marca = estado == 1 ? "✔" : estado == 0 ? "✖" : "◌";
        TextRenderer.DrawText(g, marca, Font, new Rectangle(9, 0, 18, Height), estado == 1 ? Tema.Acento : estado == 0 ? Tema.Silencio : Tema.TextoSuave,
                              TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
        using (Font f = new Font(Font, estado == 0 ? FontStyle.Strikeout : FontStyle.Regular))
            TextRenderer.DrawText(g, Text, f, new Rectangle(27, 0, Width - 30, Height), estado == 0 ? Tema.TextoSuave : Tema.Texto,
                                  TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}

// La propuesta completa, para leerla entera (la tarjeta la corta).
static class VerTexto
{
    public static void Mostrar(IWin32Window duenio, string titulo, string texto)
    {
        using (Form f = new Form())
        {
            f.Text = titulo; f.StartPosition = FormStartPosition.CenterParent; f.ClientSize = new Size(640, 560);
            f.BackColor = Tema.Fondo; f.MinimizeBox = false; f.MaximizeBox = false; f.ShowIcon = false; f.KeyPreview = true;
            TextBox t = new TextBox();
            t.Multiline = true; t.ReadOnly = true; t.ScrollBars = ScrollBars.Vertical; t.BorderStyle = BorderStyle.None;
            t.BackColor = Tema.Panel; t.ForeColor = Tema.Texto; t.Font = Tema.Normal;
            t.Text = (texto ?? "").Replace("\r", "").Replace("\n", "\r\n");
            t.SetBounds(16, 16, 608, 528);
            f.Controls.Add(t);
            f.KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) f.Close(); };
            f.Shown += delegate { t.SelectionLength = 0; };
            f.ShowDialog(duenio);
        }
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
        Titulo.TextAlign = ContentAlignment.TopLeft;
        Titulo.AutoEllipsis = true;
        Cuerpo.Cursor = Cursors.Hand;
        Cuerpo.Click += delegate { if (P != null) VerTexto.Mostrar(FindForm(), Titulo.Text, completo); };
        Notas.Multilinea = true;
        Controls.Add(Titulo); Controls.Add(Cuerpo); Controls.Add(lblNotas); Controls.Add(Notas); Controls.Add(Elegir);
    }

    public bool Elegida { get { return elegida; } set { elegida = value; Elegir.Activo = value; Elegir.Text = value ? "✔ Elegida" : "Elegir esta"; Invalidate(); } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        int w = Width - 24;
        Titulo.SetBounds(12, 8, w, 42);
        Cuerpo.SetBounds(12, 52, w, Height - 52 - 130);
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

    string completo = "";

    public void Mostrar(Propuesta p) { Mostrar(p, 0, null); }

    // maximo: minutos que puede durar el video (0 = sin aviso); raras: lo que no sale en el material.
    public void Mostrar(Propuesta p, int maximo, List<string> raras)
    {
        P = p;
        Titulo.Text = p.Id + " · " + p.Nombre;
        StringBuilder sb = new StringBuilder();
        sb.Append((p.TipoCap.Length > 0 ? TiposCapitulo.Nombre(p.TipoCap).ToUpperInvariant() + " · " : "") +
                  (p.Tipo != "normal" || p.TipoCap.Length == 0 ? p.NombreTipo() + " · " : "") + "~" + Math.Round(p.Minutos) + " min" + (p.Doble ? " cada parte" : "") +
                  (maximo > 0 && p.Minutos > maximo * 1.15 ? "  ⚠ más largo que el objetivo (" + maximo + " min)" : "") + "\n");
        if (p.Titulos.Count > 0) sb.Append("«" + String.Join("» / «", p.Titulos.ToArray()) + "»\n");
        if (p.Estructura.Count > 0) sb.Append("\n" + String.Join(" → ", p.Estructura.ToArray()) + "\n");
        sb.Append("\nCold open: " + p.ColdOpen + "\nCierre: " + p.Cierre + "\n\n");
        foreach (string x in p.Escaleta) sb.Append("• " + x + "\n");
        sb.Append("\n" + p.PorQue);
        if (raras != null && raras.Count > 0)
            sb.Insert(0, "⚠ No salen en el material: " + String.Join(", ", raras.ToArray()) + " (Refinar lo corrige)\n");
        completo = sb.ToString();
        Cuerpo.Text = completo;
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
    Combo cmbTipo = new Combo();
    List<OpcionTriple> chips = new List<OpcionTriple>();
    CampoNumero numMin = new CampoNumero(), numMax = new CampoNumero(), numMusica = new CampoNumero();
    OpcionesCapitulo opc = new OpcionesCapitulo();
    Boton btnAnalizar = new Boton("Analizar y proponer", EstiloBoton.Primario);
    Boton btnRefinar = new Boton("Refinar propuestas", EstiloBoton.Secundario);
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
        vista1.Add(Texto("INDICACIONES (opcional: qué no puede faltar, qué cortar, a quién darle protagonismo…)", Tema.Pequena, Tema.TextoSuave, m, y1, w - 460, 18));
        txtIndicaciones.Multilinea = true;
        vista1.Add(Pos(txtIndicaciones, m, y1 + 20, w - 456, 48));
        vista1.Add(Texto("TIPO DE CAPÍTULO", Tema.Pequena, Tema.TextoSuave, m + w - 440, y1, 230, 18));
        foreach (string o in TiposCapitulo.Opciones()) cmbTipo.Items.Add(o);
        cmbTipo.SelectedIndex = 0;
        cmbTipo.DropDownWidth = 300;
        vista1.Add(Pos(cmbTipo, m + w - 440, y1 + 22, 228, 30));
        vista1.Add(Pos(btnAnalizar, m + w - 200, y1 + 20, 200, 48));
        y1 += 76;

        // ---- opciones (casillas de tres estados), duracion y musica
        vista1.Add(Texto("OPCIONES · clic para cambiar: ◌ que decida la IA · ✔ sí · ✖ no", Tema.Pequena, Tema.TextoSuave, m, y1 + 6, 460, 18));
        vista1.Add(Texto("DURACIÓN DEL VIDEO", Tema.Pequena, Tema.TextoSuave, m + w - 470, y1 + 6, 130, 18));
        numMin.Sufijo = "min"; numMax.Sufijo = "min"; numMusica.Sufijo = "dB";
        numMin.Minimo = 1; numMax.Minimo = 1; numMin.Maximo = 120; numMax.Maximo = 120; numMin.Paso = 1; numMax.Paso = 1;
        numMusica.Minimo = 0; numMusica.Maximo = 60; numMusica.Paso = 1;
        vista1.Add(Pos(numMin, m + w - 336, y1, 78, 30));
        vista1.Add(Texto("a", Tema.Pequena, Tema.TextoSuave, m + w - 252, y1 + 6, 14, 18));
        vista1.Add(Pos(numMax, m + w - 232, y1, 78, 30));
        vista1.Add(Texto("MÚSICA A −", Tema.Pequena, Tema.TextoSuave, m + w - 140, y1 + 6, 70, 18));
        vista1.Add(Pos(numMusica, m + w - 70, y1, 70, 30));
        int cx = m, cy = y1 + 38;
        foreach (string[] c in OpcionesCapitulo.Casillas)
        {
            OpcionTriple o = new OpcionTriple(c[0], c[1]);
            int ow = o.Ancho();
            if (cx + ow > m + w) { cx = m; cy += 32; }
            vista1.Add(Pos(o, cx, cy, ow, 26));
            cx += ow + 6;
            chips.Add(o);
            o.Cambio += delegate { CambioOpcion(o); };
        }
        y1 = cy + 36;
        numMin.Cambio += delegate { if (!cargando) opc.MinutosMin = numMin.Valor; };
        numMax.Cambio += delegate { if (!cargando) opc.MinutosMax = numMax.Valor; };
        numMusica.Cambio += delegate { if (!cargando && serie != null) serie.Musica.VolumenDb = -numMusica.Valor; };

        lblAnalisis = Texto("", Tema.Pequena, Tema.Texto, m, y1, w, 70);
        vista1.Add(lblAnalisis);
        y1 += 76;
        int tw = (w - 32) / 3;
        for (int i = 0; i < 3; i++)
        {
            TarjetaPropuesta t = tarjetas[i];
            vista1.Add(Pos(t, m + i * (tw + 16), y1, tw, 350));
            int k = i;
            t.Elegir.Click += delegate { Elegir(k); };
        }
        y1 += 360;
        vista1.Add(Texto("OTRAS NOTAS", Tema.Pequena, Tema.TextoSuave, m, y1, 200, 18));
        vista1.Add(Pos(txtNotas, m, y1 + 18, w - 446, 34));
        vista1.Add(Pos(btnRefinar, m + w - 430, y1 + 14, 200, 40));
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
        btnAnalizar.Click += delegate
        {
            if (analisis != null && MessageBox.Show(this, "Analizar de nuevo descarta las propuestas actuales. Para corregirlas con tus notas usa " +
                    "«Refinar propuestas».\n\n¿Analizar todo de nuevo?", "Producir capítulo", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            Analizar();
        };
        btnRefinar.Click += delegate { Refinar(); };
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
                                      PapelEpisodio.Reglas(formato.Reglas, papel).PPM, trans,
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
        Registrar();
        foreach (Control c in new Control[] { btnCerrar }) c.Enabled = true;
        Estado(r.Texto() + (r.Avisos.Count > 0 ? " Avisos: " + String.Join(" ", r.Avisos.ToArray()) : ""), r.Avisos.Count > 0);
        MessageBox.Show(this, r.Texto() + "\n\nAhora estás en «" + Path.GetFileName(cap) + "». El guion quedó junto al proyecto.\n\n" +
                        "Siguiente: revisa el capítulo, graba la narración, «Reemplazar placeholders» (PulirEpisodio) y al final «PasoFinal» " +
                        "(balance de música y censura)." + (r.Avisos.Count > 0 ? "\n\nAvisos:\n" + String.Join("\n", r.Avisos.ToArray()) : ""),
                        "Producir capítulo");
        Close();
    }

    // Lo que se produjo queda en la serie: el siguiente capitulo sabe de que tipo fue y como cerro.
    void Registrar()
    {
        Propuesta p = Elegida();
        if (serie == null || p == null || final == null || serie.IndiceDe(original) < 0) return;
        RegistroCapitulo rc = new RegistroCapitulo();
        rc.Tipo = p.TipoCap; rc.Forma = p.Tipo; rc.Cierre = p.Cierre;
        List<string> t = new List<string>();
        foreach (CapituloFinal c in final.Partes) if (c.Titulo.Length > 0) t.Add(c.Titulo);
        rc.Titulo = String.Join(" / ", t.ToArray());
        serie.Producidos[original] = rc;
        try { serie.Guardar(); } catch { }
    }

    void CambioOpcion(OpcionTriple o)
    {
        if (cargando) return;
        int antes = opc.Valor(o.Clave);
        opc.Estado[o.Clave] = o.Estado;
        // Doble duracion: la duracion del video se duplica (o vuelve a la normal).
        if (o.Clave == "doble" && (antes == 1) != (o.Estado == 1))
        {
            double f = o.Estado == 1 ? 2 : 0.5;
            opc.MinutosMin = Math.Max(1, (int)Math.Round(opc.MinutosMin * f)); opc.MinutosMax = Math.Max(1, (int)Math.Round(opc.MinutosMax * f));
            MostrarOpciones();
        }
    }

    void MostrarOpciones()
    {
        bool c = cargando;
        cargando = true;
        foreach (OpcionTriple o in chips) o.Estado = opc.Valor(o.Clave);
        numMin.Valor = opc.MinutosMin; numMax.Valor = opc.MinutosMax;
        numMusica.Valor = (int)Math.Round(-(serie != null ? serie.Musica.VolumenDb : -21));
        cargando = c;
    }

    // Lo que se le manda a Gemini como indicaciones: las casillas y la duracion, y lo escrito.
    string Indicaciones()
    {
        opc.MinutosMin = Math.Min(numMin.Valor, numMax.Valor); opc.MinutosMax = Math.Max(numMin.Valor, numMax.Valor);
        return opc.Texto(duracion) + (txtIndicaciones.Text.Trim().Length > 0 ? txtIndicaciones.Text.Trim() + "\n" : "");
    }

    // Lo que el editor dio (nombres del reparto, de la serie, indicaciones): no cuenta como inventado.
    string Vocabulario()
    {
        string v = txtIndicaciones.Text + " " + txtNotas.Text;
        if (serie != null) v += " " + serie.Nombre + " " + serie.Notas + " " + serie.Formato.Premisa + " " + serie.Musica.Reparto + " " + serie.NotaEpisodio(original);
        foreach (TarjetaPropuesta t in tarjetas) v += " " + t.Notas.Text;
        return v;
    }

    static string Corto(string t, int n) { t = (t ?? "").Replace("\n", " "); return t.Length > n ? t.Substring(0, n - 1) + "…" : t; }

    string TipoPedido()
    {
        return cmbTipo.SelectedIndex > 0 ? TiposCapitulo.Normalizar((string)cmbTipo.SelectedItem) : "";
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
        MostrarContexto();
        CapSerie este = caps.Find(delegate (CapSerie c) { return c.Relacion == 0; });
        opc = LogicaProduccion.Opciones(veg) ?? OpcionesCapitulo.PorDefecto(papel, este != null ? este.Posicion : 0, PapelEpisodio.Reglas(formato.Reglas, papel));
        MostrarOpciones();
        if (!formato.EsTV) Estado("La serie no tiene el formato «Serie de TV / anime»: se usa su plantilla por defecto.", false);

        string elegida, ind, notas;
        if (LogicaProduccion.Cargar(veg, duracion, musica.Count, formato.Reglas.PPM, formato.Tv, out analisis, out elegida, out ind, out notas, out final))
        {
            txtIndicaciones.Text = ind; txtNotas.Text = notas;
            string tp = TiposCapitulo.Nombre(LogicaProduccion.TipoPedido(veg));
            if (tp.Length > 0) cmbTipo.SelectedItem = tp;
            MostrarAnalisis();
            if (analisis != null)
                for (int i = 0; i < analisis.Propuestas.Count && i < 3; i++)
                    if (analisis.Propuestas[i].Id == elegida) Elegir(i);
            MostrarFinal();
            Estado("Se cargó lo que tenías de este capítulo.", false);
        }
        else
        {
            if (papel == "Primer capítulo") cmbTipo.SelectedItem = TiposCapitulo.Nombre("estreno");
            MostrarAnalisis();
        }
        Vista(final != null ? 1 : 0);
        segPaso.Habilitar(1, final != null);
    }

    void MostrarContexto()
    {
        CapSerie este = caps.Find(delegate (CapSerie c) { return c.Relacion == 0; });
        lblContexto.Text = (serie == null ? "Sin serie (uso el formato Serie de TV por defecto; elígela en Series)" :
                            "Serie " + serie.Nombre + " · " + formato.Nombre + (este != null ? " · capítulo " + este.Posicion + " de " + caps.Count : "") +
                            " · «" + papel + "»") +
                           "\nMaterial: " + Formato.Tiempo(duracion) + (veg != original ? " (copia base)" : "") +
                           (biblioteca != null ? " · música: " + musica.Count + " temas" : " · sin biblioteca de música (Series → Música…)");
    }

    void Guardar()
    {
        if (analisis == null) return;
        LeerNotas();
        Propuesta p = Elegida();
        try { LogicaProduccion.Guardar(veg, analisis, p != null ? p.Id : "", txtIndicaciones.Text, txtNotas.Text, final, TipoPedido(), opc); } catch { }
        if (serie != null) try { serie.Guardar(); } catch { }
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
            if (con) tarjetas[i].Mostrar(analisis.Propuestas[i], opc.MinutosMax, LogicaProduccion.NoEnMaterial(analisis.Propuestas[i], trans, Vocabulario()));
        }
        lblAnalisis.Text = !hay ? "Pulsa «Analizar y proponer»: Gemini lee todo el material con el contexto de la serie y propone tres formas de hacer el capítulo." :
            Corto(analisis.Resumen, 260) + "\n" + analisis.Momentos.Count + " momentos · ~" + Math.Round(analisis.MinutosUtiles) + " min útiles" +
            (analisis.TipoSugerido.Length > 0 ? "\nTipo: " + TiposCapitulo.Nombre(analisis.TipoSugerido).ToUpperInvariant() +
                (analisis.TipoMotivo.Length > 0 ? " — " + Corto(analisis.TipoMotivo, 140) : "") +
                (analisis.TiposAlternativos.Count > 0 ? " (también podría ser: " + String.Join(", ", analisis.TiposAlternativos.ConvertAll<string>(TiposCapitulo.Nombre).ToArray()) + ")" : "") : "") +
            (analisis.PapelSugerido.Length > 0 && analisis.PapelSugerido != papel ? " · sugiere el papel «" + analisis.PapelSugerido + "»" : "") +
            (analisis.DobleRecomendado ? " · recomienda más de un video: " + Corto(analisis.DobleMotivo, 100) : "") +
            (analisis.Hilos.Count > 0 ? " · hilos: " + Corto(String.Join("; ", analisis.Hilos.ToArray()), 120) : "");
        btnFinal.Enabled = Elegida() != null;
        if (hay && Elegida() == null) Estado("Elige una propuesta (clic en su texto para leerla completa; puedes escribir notas en cualquiera) y pulsa «Armar propuesta final».", false);
    }

    void Habilitar(bool si)
    {
        foreach (Control c in new Control[] { btnAnalizar, btnRefinar, btnFinal, btnAjustar, btnProducir, btnCerrar, segPaso }) c.Enabled = si;
        if (si) btnRefinar.Enabled = analisis != null;
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
        string instr = LogicaProduccion.InstruccionesAnalisis(formato, tipo, papel, TipoPedido());
        string msg = LogicaProduccion.MensajeAnalisis(trans, duracion, ctx, LogicaProduccion.Reparto(serie != null ? serie.Musica : null), Indicaciones());
        Pedir(instr, msg, "Gemini está viendo todo el material (puede tardar un par de minutos)…", delegate (string r)
        {
            analisis = LogicaProduccion.LeerAnalisis(r, duracion);
            MostrarAnalisis();
            Vista(0);
            if (analisis.Propuestas.Count < 3) { Completar(); return; }
            Estado("✔ " + analisis.Propuestas.Count + " propuestas. Elige una, escribe notas si quieres y arma la propuesta final.", false);
            CambiarPapel();
        });
    }

    // Gemini dio menos de tres propuestas: se piden las que faltan (sin volver a analizar).
    void Completar()
    {
        int n = analisis.Propuestas.Count;
        string instr = LogicaProduccion.InstruccionesCompletar(formato, papel);
        string msg = LogicaProduccion.MensajeRefinar(analisis, txtNotas.Text, Indicaciones(), trans);
        Pedir(instr, msg, "Gemini dio " + n + (n == 1 ? " propuesta" : " propuestas") + ": pidiendo las que faltan…", delegate (string r)
        {
            LogicaProduccion.Refinar(analisis, r);
            MostrarAnalisis();
            Estado("✔ " + analisis.Propuestas.Count + " propuestas. Elige una, escribe notas si quieres y arma la propuesta final.", false);
            CambiarPapel();
        });
    }

    // Si Gemini ve otro papel (alguien muere y estaba como «Normal»...), se ofrece cambiarlo.
    void CambiarPapel()
    {
        if (serie == null || analisis.PapelSugerido.Length == 0 || analisis.PapelSugerido == papel || serie.IndiceDe(original) < 0) return;
        if (MessageBox.Show(this, "Gemini sugiere que este capítulo sea «" + analisis.PapelSugerido + "» (ahora es «" + papel + "»)" +
                (analisis.PapelMotivo.Length > 0 ? ":\n\n" + analisis.PapelMotivo : ".") + "\n\n¿Cambiar el papel? (cambia el ritmo y lo que " +
                "se pide en la propuesta final; las propuestas se quedan como están)", "Producir capítulo", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        serie.CambiarPapel(original, analisis.PapelSugerido);
        try { serie.Guardar(); } catch { }
        papel = analisis.PapelSugerido;
        foreach (CapSerie c in caps) if (c.Relacion == 0) c.Papel = papel;
        MostrarContexto();
        MostrarAnalisis();
    }

    void Refinar()
    {
        LeerNotas();
        if (analisis == null) return;
        bool hayNotas = txtNotas.Text.Trim().Length > 0 || txtIndicaciones.Text.Trim().Length > 0;
        foreach (Propuesta p in analisis.Propuestas)
            if (p.Notas.Trim().Length > 0 || LogicaProduccion.NoEnMaterial(p, trans, Vocabulario()).Count > 0) hayNotas = true;
        if (!hayNotas) { Estado("Escribe notas en las propuestas (o en «Otras notas») para refinarlas.", true); return; }
        string elegida = Elegida() != null ? Elegida().Id : "";
        string instr = LogicaProduccion.InstruccionesRefinar(formato, papel);
        string msg = LogicaProduccion.MensajeRefinar(analisis, txtNotas.Text, Indicaciones(), trans);
        Pedir(instr, msg, "Gemini está corrigiendo las propuestas con tus notas…", delegate (string r)
        {
            LogicaProduccion.Refinar(analisis, r);
            txtNotas.Text = "";
            MostrarAnalisis();
            for (int i = 0; i < analisis.Propuestas.Count && i < 3; i++) if (analisis.Propuestas[i].Id == elegida) Elegir(i);
            Vista(0);
            Estado("✔ Propuestas corregidas con tus notas. Puedes seguir refinando o armar la propuesta final.", false);
        });
    }

    void Final(string cambios)
    {
        LeerNotas();
        Propuesta p = Elegida();
        if (analisis == null || p == null) { Estado("Elige una propuesta.", true); return; }
        string instr = LogicaProduccion.InstruccionesFinal(formato, papel, p);
        string msg = LogicaProduccion.MensajeFinal(analisis, p, txtNotas.Text, Indicaciones(), musica, serie != null ? serie.Musica : null,
                                                   LogicaProduccion.Reparto(serie != null ? serie.Musica : null), trans, duracion,
                                                   cambios != null ? final : null, cambios);
        Pedir(instr, msg, cambios == null ? "Gemini está armando la escaleta final…" : "Gemini está ajustando la escaleta…", delegate (string r)
        {
            final = LogicaProduccion.LeerFinal(r, duracion, musica.Count, formato.Reglas.PPM, formato.Tv);
            if (cambios != null) txtCambios.Text = "";
            MostrarFinal();
            segPaso.Habilitar(1, true);
            Vista(1);
            Estado("✔ Escaleta lista. Desmarca lo que no quieras, pide cambios con «Ajustar» o produce el capítulo.", false);
        });
    }

    static readonly Dictionary<string, string> NombreTipo = new Dictionary<string, string> {
        { "clip", "Clip" }, { "texto", "Texto" }, { "musica", "Música" }, { "narracion", "Narración" }, { "recurso", "Recurso" } };


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
                // Orden: por bloque de la estructura de esta parte; dentro, clips en su orden y lo demas por tiempo.
                foreach (BloqueTV b in c.Estructura)
                {
                    List<ItemFinal> del = c.Items.FindAll(delegate (ItemFinal i) { return i.Bloque == b.Clave || (i.Bloque.Length == 0 && i.Tipo != "clip" && b.Clave == BloqueDe(c, i)); });
                    ListViewItem cab = new ListViewItem((final.Partes.Count > 1 ? "P" + (k + 1) + " · " : "") + b.Nombre);
                    cab.SubItems.Add(b.Tipo == "contenido" ? Formato.Tiempo(LogicaProduccion.Bloque(c, b.Clave)) : b.Segundos + " s");
                    cab.SubItems.Add(b.Tipo == "kit" ? (formato.Tv.Archivo(b.ClaveKit).Length > 0 ? "kit" : "placeholder") :
                                     b.Ritmo.Length > 0 ? "ritmo " + b.Ritmo : "");
                    cab.SubItems.Add(b.Tipo == "texto" ? (c.Etapa.Length > 0 ? c.Etapa + " · " : "") + c.Titulo : b.Descripcion);
                    cab.Font = Tema.Negrita; cab.ForeColor = Tema.Acento; cab.Checked = true;
                    lstFinal.Items.Add(cab);
                    foreach (ItemFinal i in del)
                    {
                        ListViewItem it = new ListViewItem("   " + (i.Id.Length > 0 ? i.Id : ""));
                        it.SubItems.Add(i.Inicio < 0 ? "" : i.Tipo == "clip" ? Formato.Tiempo(i.Inicio) + "–" + Formato.Tiempo(i.Fin) : Formato.Tiempo(i.Inicio));
                        it.SubItems.Add(NombreTipo.ContainsKey(i.Tipo) ? NombreTipo[i.Tipo] + (i.Clase.Length > 0 ? " · " + i.Clase : "") : i.Tipo);
                        it.SubItems.Add(i.Tipo == "musica" ? Musica(i) + (i.Texto.Length > 0 ? " — " + i.Texto : "") :
                                        i.Tipo == "clip" ? Aviso(i) + (i.Texto.Length > 0 ? i.Texto : "(" + Formato.Tiempo(i.Duracion) + ")") +
                                                           (i.Respiro > 0 ? "  · respiro " + i.Respiro.ToString("0.#") + " s" : "") :
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

    // Clip con charla tecnica o personal (para revisarlo o desmarcarlo).
    string Aviso(ItemFinal i)
    {
        string x = LogicaProduccion.SensibleEn(trans, i.Inicio, i.Fin);
        return x.Length > 0 ? "⚠ charla " + x + " · " : "";
    }

    // Bloque de un texto, musica o narracion sin bloque: el del clip que contiene su tiempo.
    static string BloqueDe(CapituloFinal c, ItemFinal x)
    {
        foreach (ItemFinal i in c.Items)
            if (i.Tipo == "clip" && x.Inicio >= i.Inicio - 0.5 && x.Inicio <= i.Fin + 0.5) return i.Bloque;
        return c.Estructura.Count > 0 ? c.Estructura.Find(delegate (BloqueTV b) { return b.Tipo == "contenido"; }).Clave : "acto_a";
    }

    void Resumen()
    {
        if (final == null) { lblFinal.Text = ""; return; }
        ReglasRitmo r = PapelEpisodio.Reglas(formato.Reglas, papel);
        List<string> l = new List<string>();
        Propuesta p = Elegida();
        double x = 1;
        foreach (CapituloFinal c in final.Partes)
        {
            double d = LogicaProduccion.Duracion(c, formato.Tv);
            bool ok = d >= opc.MinutosMin * 60 * x - 30 && d <= opc.MinutosMax * 60 * x + 30;
            l.Add("«" + c.Titulo + "» " + Formato.Tiempo(d) + (ok ? " ✔" : " (objetivo " + opc.MinutosMin + "–" + opc.MinutosMax + " min)"));
        }
        lblFinal.Text = Corto(final.Resumen, 300) + "\n" + String.Join("   ·   ", l.ToArray()) +
                        (final.Repetido > 1 ? "   ·   quité " + Formato.Tiempo(final.Repetido) + " de material repetido" : "");
    }
}
