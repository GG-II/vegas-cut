using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Vista previa: la curva de volumen de la musica sobre los tramos con voz.
class GraficaMusica : ControlBase
{
    public List<Rango> Voz;
    public List<PuntoVolumen> Puntos;
    public double Inicio, Fin, BajaDb = -14;
    public string Mensaje = "";

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Panel)) g.FillPath(b, p);
            using (Pen pen = new Pen(Tema.Borde)) g.DrawPath(pen, p);
        }
        if (Puntos == null || Fin <= Inicio)
        {
            TextRenderer.DrawText(g, Mensaje, Tema.Normal, ClientRectangle, Tema.TextoSuave,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        float x0 = 52, x1 = Width - 14, arriba = 16, abajo = Height - 34;
        double piso = Math.Min(-6, BajaDb - 4);
        Func<double, float> X = delegate (double t) { return x0 + (float)((t - Inicio) / (Fin - Inicio)) * (x1 - x0); };
        Func<double, float> Y = delegate (double db) { return arriba + (float)(db / piso) * (abajo - arriba); };

        // Guias de 0 dB y del nivel bajo.
        using (Pen guia = new Pen(Tema.Borde) { DashStyle = DashStyle.Dash })
            foreach (double db in new double[] { 0, BajaDb })
            {
                g.DrawLine(guia, x0, Y(db), x1, Y(db));
                TextRenderer.DrawText(g, db.ToString("0") + " dB", Tema.Pequena, new Rectangle(4, (int)Y(db) - 8, 46, 16),
                    Tema.TextoSuave, TextFormatFlags.Right);
            }

        // Voz abajo, como en una linea de tiempo.
        using (SolidBrush b = new SolidBrush(Color.FromArgb(170, Tema.Voz)))
            foreach (Rango r in Voz)
                g.FillRectangle(b, X(r.Inicio), Height - 24, Math.Max(1, X(r.Fin) - X(r.Inicio)), 10);
        TextRenderer.DrawText(g, "voz", Tema.Pequena, new Rectangle(4, Height - 28, 46, 16), Tema.Voz, TextFormatFlags.Right);

        // Curva de la musica.
        if (Puntos.Count > 1)
        {
            List<PointF> pts = new List<PointF>();
            foreach (PuntoVolumen p in Puntos) pts.Add(new PointF(X(p.T), Y(p.Db)));
            using (Pen pen = new Pen(Tema.Acento, 2)) g.DrawLines(pen, pts.ToArray());
        }
    }
}

class VentanaMusica : VentanaBase
{
    readonly Vegas vegas;
    readonly List<InfoPista> pistas;
    readonly AjustesMusica ajustes = AjustesMusica.Cargar();
    List<Boton> chipsVoz = new List<Boton>(), chipsMusica = new List<Boton>();
    Segmentado segRango = new Segmentado(new string[] { "Todo el proyecto", "Selección de tiempo" });
    Deslizador desBaja = new Deslizador();
    Etiqueta lblBaja, lblEstado;
    CampoNumero numAnticipa = new CampoNumero(), numRecupera = new CampoNumero(), numPausa = new CampoNumero();
    GraficaMusica grafica = new GraficaMusica();
    Boton btnAnalizar = new Boton("Medir voces", EstiloBoton.Secundario);
    Boton btnAplicar = new Boton("Aplicar a la música", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    // Ultima medicion (se reutiliza al mover los ajustes).
    List<Analisis> analisis;
    List<double> umbrales;
    double inicio, fin;
    string clave = "";
    List<PuntoVolumen> puntos;

    public VentanaMusica(Vegas vegas, List<InfoPista> pistas) : base("Música que baja sola", 860)
    {
        this.vegas = vegas;
        this.pistas = pistas;
        int m = Margen, w = Ancho;
        Encabezado("Música que baja sola",
            "Baja la música cuando alguien habla y la sube en las pausas, con la envolvente de volumen de la pista.");

        int y = 92;
        Texto("VOCES", Tema.Pequena, Tema.TextoSuave, m, y, 60, 18);
        Texto("Se miden ahora mismo, así que da igual si editaste después de transcribir.", Tema.Pequena, Tema.TextoSuave, m + 64, y, w - 64, 18);
        y = Chips(chipsVoz, y + 22) + 12;
        Texto("MÚSICA", Tema.Pequena, Tema.TextoSuave, m, y, 60, 18);
        Texto("Las pistas que bajan (música, y si quieres el juego).", Tema.Pequena, Tema.TextoSuave, m + 64, y, w - 64, 18);
        y = Chips(chipsMusica, y + 22) + 16;
        Sugerir();
        for (int i = 0; i < pistas.Count; i++)
        {
            int k = i;
            chipsVoz[k].Click += delegate { chipsVoz[k].Activo = !chipsVoz[k].Activo; if (chipsVoz[k].Activo) chipsMusica[k].Activo = false; Cambio(); };
            chipsMusica[k].Click += delegate { chipsMusica[k].Activo = !chipsMusica[k].Activo; if (chipsMusica[k].Activo) chipsVoz[k].Activo = false; Cambio(); };
        }

        segRango.Seleccion = 0;
        segRango.Habilitar(1, PistasVegas.HaySeleccion(vegas));
        if (PistasVegas.HaySeleccion(vegas)) segRango.Seleccion = 1;
        Pos(segRango, m, y, 300, 34);
        segRango.Cambio += delegate { Cambio(); };
        y += 50;

        // Ajustes
        int c = (w - 3 * 16) / 4;
        Texto("BAJAR A", Tema.Pequena, Tema.TextoSuave, m, y, c - 80, 18);
        lblBaja = Texto("", Tema.Negrita, Tema.Texto, m + c - 70, y - 2, 70, 20);
        lblBaja.TextAlign = ContentAlignment.MiddleRight;
        Texto("BAJA ANTES DE HABLAR", Tema.Pequena, Tema.TextoSuave, m + (c + 16), y, c, 18);
        Texto("SUBE AL TERMINAR EN", Tema.Pequena, Tema.TextoSuave, m + 2 * (c + 16), y, c, 18);
        Texto("SOLO SUBE EN PAUSAS DE", Tema.Pequena, Tema.TextoSuave, m + 3 * (c + 16), y, c, 18);
        y += 22;
        desBaja.Minimo = -30; desBaja.Maximo = -3;
        desBaja.Valor = ajustes.BajaDb;
        Pos(desBaja, m - 8, y, c + 16, 34);
        Numero(numAnticipa, ajustes.AnticipaMs, 0, 2000, 50, m + (c + 16), y, c);
        Numero(numRecupera, ajustes.RecuperaMs, 0, 4000, 50, m + 2 * (c + 16), y, c);
        Numero(numPausa, ajustes.PausaMs, 200, 10000, 100, m + 3 * (c + 16), y, c);
        y += 50;
        desBaja.Cambio += delegate { Recalcular(); };
        numAnticipa.Cambio += delegate { Recalcular(); };
        numRecupera.Cambio += delegate { Recalcular(); };
        numPausa.Cambio += delegate { Recalcular(); };

        grafica.Mensaje = "Pulsa “Medir voces” para ver dónde baja la música.";
        Pos(grafica, m, y, w, 170);
        y += 180;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 36);
        y += 44;

        Pos(btnCerrar, m + w - 520, y, 110, 40);
        Pos(btnAnalizar, m + w - 400, y, 170, 40);
        Pos(btnAplicar, m + w - 220, y, 220, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        btnCerrar.Click += delegate { Close(); };
        btnAnalizar.Click += delegate { Medir(); };
        btnAplicar.Click += delegate { Aplicar(); };
        Cambio();
        Recalcular();
    }

    void Numero(CampoNumero n, int valor, int min, int max, int paso, int x, int y, int w)
    {
        n.Minimo = min; n.Maximo = max; n.Paso = paso; n.Valor = valor;
        Pos(n, x, y, w, 34);
    }

    int Chips(List<Boton> lista, int y)
    {
        int cx = Margen;
        foreach (InfoPista p in pistas)
        {
            Boton c = new Boton(p.Nombre, EstiloBoton.Chip);
            int w = Math.Min(Ancho, TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 26);
            if (cx + w > Margen + Ancho) { cx = Margen; y += 34; }
            Pos(c, cx, y, w, 28);
            lista.Add(c);
            cx += w + 6;
        }
        return y + 28;
    }

    // Voces: las que se transcribieron (o la sugerida). Musica: pistas con
    // nombre o archivo de musica.
    void Sugerir()
    {
        bool hayVoz = false;
        try
        {
            string ruta = Transcripcion.RutaPara(vegas.Project.FilePath);
            if (ruta != null && File.Exists(ruta))
                foreach (Hablante h in Transcripcion.Cargar(ruta).Hablantes)
                    for (int i = 0; i < pistas.Count; i++)
                        if (h.Voz && pistas[i].Etiqueta == h.Etiqueta) { chipsVoz[i].Activo = true; hayVoz = true; }
        }
        catch { }
        if (!hayVoz) chipsVoz[PistasVegas.SugerirVoz(pistas)].Activo = true;
        for (int i = 0; i < pistas.Count; i++)
            if (!chipsVoz[i].Activo && EsMusica(pistas[i])) chipsMusica[i].Activo = true;
    }

    public static bool EsMusica(InfoPista p)
    {
        string n = ((p.Pista.Name ?? "") + " " + (p.Archivo ?? "")).ToLowerInvariant();
        foreach (string s in new string[] { "music", "música", "musica", "bgm", "song", "canción", "cancion", "soundtrack", "ost" })
            if (n.Contains(s)) return true;
        string ext = Path.GetExtension(p.Archivo ?? "").ToLowerInvariant();
        return ext == ".mp3" || ext == ".ogg" || ext == ".flac" || ext == ".m4a" || ext == ".aac" || ext == ".opus";
    }

    List<int> Elegidas(List<Boton> chips)
    {
        List<int> r = new List<int>();
        for (int i = 0; i < chips.Count; i++) if (chips[i].Activo) r.Add(i);
        return r;
    }

    string Clave() { return String.Join(",", Elegidas(chipsVoz).ConvertAll(delegate (int i) { return i.ToString(); }).ToArray()) + "|" + segRango.Seleccion; }

    void Cambio()
    {
        // Si cambian las voces o el rango, la medicion anterior ya no sirve.
        if (analisis != null && Clave() != clave)
        {
            analisis = null; puntos = null;
            grafica.Puntos = null;
            grafica.Mensaje = "Cambiaste las voces o el rango: vuelve a medir.";
            grafica.Invalidate();
        }
        Actualizar();
    }

    void Actualizar()
    {
        int voces = Elegidas(chipsVoz).Count, musica = Elegidas(chipsMusica).Count;
        btnAnalizar.Enabled = voces > 0;
        btnAplicar.Enabled = voces > 0 && musica > 0;
        lblBaja.Text = desBaja.Valor.ToString("0") + " dB";
        if (voces == 0) Estado("Elige al menos una pista de voz.", Tema.Silencio);
        else if (musica == 0) Estado("Elige la pista de música que debe bajar.", Tema.Silencio);
        else if (puntos == null) Estado("“Aplicar” mide las voces si aún no lo hiciste. Ctrl+Z lo deshace.", Tema.TextoSuave);
    }

    void Estado(string t, Color c) { lblEstado.Text = t; lblEstado.ForeColor = c; }

    void Medir()
    {
        Cursor = Cursors.WaitCursor;
        btnAnalizar.Enabled = btnAplicar.Enabled = false;
        try
        {
            double duracion;
            PistasVegas.ObtenerRango(vegas, segRango.Seleccion == 1, out inicio, out duracion);
            fin = inicio + duracion;
            analisis = new List<Analisis>();
            umbrales = new List<double>();
            foreach (int i in Elegidas(chipsVoz))
            {
                Estado("Midiendo " + pistas[i].Nombre + "…", Tema.Texto);
                Application.DoEvents();
                Analisis a = PistasVegas.Niveles(vegas, pistas[i].Pista, inicio, duracion);
                analisis.Add(a);
                umbrales.Add(Detector.UmbralAutomatico(a.Db));
            }
            clave = Clave();
        }
        catch (Exception ex)
        {
            analisis = null;
            Estado("No se pudo medir: " + ex.Message, Tema.Silencio);
        }
        finally { Cursor = Cursors.Default; }
        Recalcular();
    }

    void Recalcular()
    {
        ajustes.BajaDb = (int)desBaja.Valor;
        ajustes.AnticipaMs = numAnticipa.Valor; ajustes.RecuperaMs = numRecupera.Valor; ajustes.PausaMs = numPausa.Valor;
        grafica.BajaDb = ajustes.BajaDb;
        if (analisis == null) { Actualizar(); return; }

        List<Rango> silencios = Detector.Detectar(analisis, umbrales, LogicaMusica.Deteccion(ajustes.PausaMs));
        List<Rango> voz = LogicaMusica.Voz(silencios, inicio, fin);
        puntos = LogicaMusica.Puntos(voz, inicio, fin, ajustes.BajaDb, ajustes.AnticipaMs / 1000.0, ajustes.RecuperaMs / 1000.0);
        double hablado = 0;
        foreach (Rango r in voz) hablado += r.Fin - r.Inicio;
        grafica.Voz = voz; grafica.Puntos = puntos; grafica.Inicio = inicio; grafica.Fin = fin;
        grafica.Invalidate();
        Actualizar();
        Estado("La música baja " + LogicaMusica.Bajadas(puntos) + " veces · hay voz el " +
            Math.Round(100 * hablado / Math.Max(0.001, fin - inicio)) + "% del tiempo (" + Formato.Tiempo(hablado) + " de " +
            Formato.Tiempo(fin - inicio) + "). Se reemplaza la envolvente de volumen en ese rango.", Tema.Texto);
    }

    void Aplicar()
    {
        if (analisis == null) Medir();
        if (puntos == null) return;
        ajustes.Guardar();
        List<string> nombres = new List<string>();
        using (UndoBlock deshacer = new UndoBlock("Música que baja sola"))
            foreach (int i in Elegidas(chipsMusica))
            {
                LogicaMusica.Aplicar(pistas[i].Pista, puntos, inicio, fin);
                nombres.Add(pistas[i].Etiqueta);
            }
        MessageBox.Show(this, "Listo: la música baja " + LogicaMusica.Bajadas(puntos) + " veces en " +
            String.Join(", ", nombres.ToArray()) + ".\n\nSe ve como una línea en la pista (envolvente de volumen); " +
            "puedes mover cualquier punto a mano. Ctrl+Z lo deshace.", "Música que baja sola");
        DialogResult = DialogResult.OK;
        Close();
    }
}
