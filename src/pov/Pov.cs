using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        using (VentanaPov v = new VentanaPov(vegas)) v.ShowDialog();
    }
}

class VentanaPov : VentanaBase
{
    readonly Vegas vegas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly string veg;
    Transcripcion trans;
    List<TramoPov> tramos = new List<TramoPov>();
    bool trabajando, cargando;

    Segmentado seg = new Segmentado(new string[] { "1 · Sincronizar (antes de quitar silencios)", "2 · Cambios de POV (ya cortado)" });
    List<Control> vista1 = new List<Control>(), vista2 = new List<Control>();
    // 1
    Combo cmbRef = new Combo(), cmbOtro = new Combo();
    Lista lstPistas = new Lista();
    CampoNumero numMax = new CampoNumero();
    Boton btnSinc = new Boton("Sincronizar", EstiloBoton.Primario);
    Etiqueta lblSinc;
    // 2
    Combo cmbPrincipal = new Combo(), cmbOtroVideo = new Combo(), cmbJuego = new Combo();
    CampoTexto txtJugador = new CampoTexto();
    CampoNumero numPct = new CampoNumero(), numEntre = new CampoNumero();
    Boton btnIA = new Boton("Elegir con IA", EstiloBoton.Primario);
    Lista lstTramos = new Lista();
    Boton btnAplicar = new Boton("Aplicar", EstiloBoton.Primario);
    Boton btnQuitar = new Boton("Quitar cambios", EstiloBoton.Secundario);

    Etiqueta lblEstado;
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaPov(Vegas vegas) : base("Varios POV", 1000)
    {
        this.vegas = vegas;
        veg = vegas.Project.FilePath ?? "";
        int m = Margen, w = Ancho;
        Encabezado("Varios POV", "El video de otro jugador grabado al mismo tiempo: sincronizarlo y mostrarlo en los mejores momentos.");
        int y = 92;
        Pos(seg, m, y, w, 34);
        y += 46;

        // ---- 1. sincronizar
        int y1 = y;
        vista1.Add(Texto("Importa el video del otro jugador en pistas nuevas (su video y sus audios) ANTES de quitar silencios. Se compara una " +
                         "pista tuya con una suya que suene igual (la llamada de Discord) y se mueven sus pistas hasta que coincidan. Al quitar " +
                         "silencios, incluye su micrófono entre las voces: así se corta todo junto y sigue sincronizado.",
                         Tema.Pequena, Tema.TextoSuave, m, y1, w, 46));
        y1 += 54;
        int mitad = (w - 16) / 2;
        vista1.Add(Texto("TU POV (REFERENCIA): lo ideal, tu micrófono", Tema.Pequena, Tema.TextoSuave, m, y1, mitad, 18));
        vista1.Add(Texto("DEL OTRO POV, PARA COMPARAR: lo ideal, su llamada (ahí se oye tu voz)", Tema.Pequena, Tema.TextoSuave, m + mitad + 16, y1, mitad, 18));
        vista1.Add(Pos(cmbRef, m, y1 + 20, mitad, 30));
        vista1.Add(Pos(cmbOtro, m + mitad + 16, y1 + 20, mitad, 30));
        y1 += 60;
        vista1.Add(Texto("PISTAS DEL OTRO POV (se mueven juntas)", Tema.Pequena, Tema.TextoSuave, m, y1, w, 18));
        lstPistas.Columns.Add("Pista", w - SystemInformation.VerticalScrollBarWidth - 8);
        vista1.Add(Pos(lstPistas, m, y1 + 20, w, 200));
        y1 += 230;
        vista1.Add(Texto("BUSCAR HASTA ±", Tema.Pequena, Tema.TextoSuave, m, y1 + 8, 100, 18));
        numMax.Sufijo = "min"; numMax.Minimo = 1; numMax.Maximo = 120; numMax.Paso = 1;
        vista1.Add(Pos(numMax, m + 104, y1, 90, 32));
        vista1.Add(Pos(btnSinc, m + w - 180, y1, 180, 32));
        y1 += 42;
        lblSinc = Texto("", Tema.Normal, Tema.Texto, m, y1, w, 40);
        vista1.Add(lblSinc);
        y1 += 46;

        // ---- 2. cambios de POV
        int y2 = y;
        int tercio = (w - 32) / 3;
        vista2.Add(Texto("VIDEO PRINCIPAL", Tema.Pequena, Tema.TextoSuave, m, y2, tercio, 18));
        vista2.Add(Texto("VIDEO DEL OTRO POV", Tema.Pequena, Tema.TextoSuave, m + tercio + 16, y2, tercio, 18));
        vista2.Add(Texto("SONIDO DE SU JUEGO (opcional)", Tema.Pequena, Tema.TextoSuave, m + 2 * (tercio + 16), y2, tercio, 18));
        vista2.Add(Pos(cmbPrincipal, m, y2 + 20, tercio, 30));
        vista2.Add(Pos(cmbOtroVideo, m + tercio + 16, y2 + 20, tercio, 30));
        vista2.Add(Pos(cmbJuego, m + 2 * (tercio + 16), y2 + 20, tercio, 30));
        y2 += 60;
        vista2.Add(Texto("JUGADOR DEL OTRO POV", Tema.Pequena, Tema.TextoSuave, m, y2 + 8, 150, 18));
        vista2.Add(Pos(txtJugador, m + 154, y2, 200, 32));
        vista2.Add(Texto("MÁXIMO EN PANTALLA", Tema.Pequena, Tema.TextoSuave, m + 372, y2 + 8, 130, 18));
        numPct.Sufijo = "%"; numPct.Minimo = 1; numPct.Maximo = 60; numPct.Paso = 1;
        vista2.Add(Pos(numPct, m + 504, y2, 80, 32));
        vista2.Add(Texto("SEPARADOS", Tema.Pequena, Tema.TextoSuave, m + 600, y2 + 8, 76, 18));
        numEntre.Sufijo = "s"; numEntre.Minimo = 5; numEntre.Maximo = 600; numEntre.Paso = 5;
        vista2.Add(Pos(numEntre, m + 678, y2, 80, 32));
        vista2.Add(Pos(btnIA, m + w - 180, y2, 180, 32));
        y2 += 44;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lstTramos.Columns.Add("Tramo", 120);
        lstTramos.Columns.Add("Dura", 56);
        lstTramos.Columns.Add("Qué se dice", w - 120 - 56 - 280 - sb);
        lstTramos.Columns.Add("Por qué", 280);
        vista2.Add(Pos(lstTramos, m, y2, w, 250));
        y2 += 260;
        vista2.Add(Pos(btnQuitar, m + w - 350, y2, 160, 36));
        vista2.Add(Pos(btnAplicar, m + w - 180, y2, 180, 36));
        y2 += 46;

        int yb = Math.Max(y1, y2);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, yb, w - 160, 40);
        Pos(btnCerrar, m + w - 140, yb, 140, 40);
        ClientSize = new Size(ClientSize.Width, yb + 40 + 24);

        cargando = true;
        numMax.Valor = 10; numPct.Valor = 12; numEntre.Valor = 45;
        cargando = false;
        try
        {
            string r = Transcripcion.RutaPara(veg);
            if (r != null && File.Exists(r))
            {
                trans = Transcripcion.Cargar(r);
                if (trans.TieneFuentes) trans.Ubicador = PistasVegas.Ubicador(vegas.Project, trans);
            }
        }
        catch { trans = null; }
        Llenar();

        seg.Cambio += delegate { if (!cargando) Vista(seg.Seleccion); };
        cmbOtro.SelectedIndexChanged += delegate { if (!cargando) MarcarDelOtro(); };
        btnSinc.Click += delegate { Sincronizar(); };
        btnIA.Click += delegate { ConIA(); };
        btnAplicar.Click += delegate { Aplicar(false); };
        btnQuitar.Click += delegate { Aplicar(true); };
        btnCerrar.Click += delegate { Close(); };
        lstTramos.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando && e.Item.Tag != null) ((TramoPov)e.Item.Tag).Elegido = e.Item.Checked; };
        lstTramos.DoubleClick += delegate
        {
            if (lstTramos.SelectedIndices.Count == 0) return;
            TramoPov tp = (TramoPov)lstTramos.Items[lstTramos.SelectedIndices[0]].Tag;
            try
            {
                vegas.Transport.CursorPosition = Timecode.FromMilliseconds(tp.Inicio * 1000);
                vegas.Transport.SelectionStart = Timecode.FromMilliseconds(tp.Inicio * 1000);
                vegas.Transport.SelectionLength = Timecode.FromMilliseconds(tp.Duracion * 1000);
            }
            catch { }
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };
        // Si ya hay transcripcion, lo mas probable es que ya este cortado.
        Vista(trans != null && LogicaPov.Ajuste(veg, "otroVideo").Length > 0 ? 1 : 0);
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Vista(int i)
    {
        foreach (Control c in vista1) c.Visible = i == 0;
        foreach (Control c in vista2) c.Visible = i == 1;
        cargando = true; seg.Seleccion = i; cargando = false;
    }

    static string Archivo(TrackEvent e)
    {
        try { return e.ActiveTake != null && e.ActiveTake.Media != null ? Path.GetFileName(e.ActiveTake.Media.FilePath ?? "") : ""; } catch { return ""; }
    }

    static string NombrePista(Track t)
    {
        string a = "";
        foreach (TrackEvent e in t.Events) { a = Archivo(e); if (a.Length > 0) break; }
        return (t.Index + 1) + " · " + (t.IsAudio() ? "audio" : "video") + (String.IsNullOrEmpty(t.Name) ? "" : " · " + t.Name) + (a.Length > 0 ? " · " + a : "");
    }

    Track Pista(Combo c)
    {
        if (c.SelectedItem == null) return null;
        string n = (string)c.SelectedItem;
        foreach (Track t in vegas.Project.Tracks) if (NombrePista(t) == n) return t;
        return null;
    }

    void Seleccionar(Combo c, string nombre)
    {
        if (String.IsNullOrEmpty(nombre)) return;
        foreach (object o in c.Items) if (((string)o).EndsWith(nombre) || (string)o == nombre) { c.SelectedItem = o; return; }
    }

    void Llenar()
    {
        cargando = true;
        foreach (Combo c in new Combo[] { cmbRef, cmbOtro, cmbPrincipal, cmbOtroVideo, cmbJuego }) c.Items.Clear();
        cmbJuego.Items.Add("(no: solo cambia el video)");
        lstPistas.Items.Clear();
        foreach (Track t in vegas.Project.Tracks)
        {
            string n = NombrePista(t);
            if (t.IsAudio()) { cmbRef.Items.Add(n); cmbOtro.Items.Add(n); cmbJuego.Items.Add(n); }
            else { cmbPrincipal.Items.Add(n); cmbOtroVideo.Items.Add(n); }
            ListViewItem it = new ListViewItem(n);
            it.Tag = t.Index;
            lstPistas.Items.Add(it);
        }
        if (cmbRef.Items.Count > 0) cmbRef.SelectedIndex = 0;
        if (cmbOtro.Items.Count > 0) cmbOtro.SelectedIndex = cmbOtro.Items.Count - 1;
        // El principal: el video que mas dura (sin contar el del otro POV, si ya se sabe cual es).
        string otroGuardado = LogicaPov.Ajuste(veg, "otroVideo");
        List<Track> videos = new List<Track>();
        foreach (Track t in vegas.Project.Tracks) if (!t.IsAudio() && t.Events.Count > 0) videos.Add(t);
        videos.Sort(delegate (Track x, Track y2) { return Cubre(y2).CompareTo(Cubre(x)); });
        Track otroT = videos.Find(delegate (Track t) { return otroGuardado.Length > 0 && NombrePista(t).EndsWith(otroGuardado.Substring(otroGuardado.IndexOf(' ') + 1)); });
        Track princ = videos.Find(delegate (Track t) { return t != otroT; });
        if (otroT == null) otroT = videos.Find(delegate (Track t) { return t != princ; });
        if (princ != null) cmbPrincipal.SelectedItem = NombrePista(princ);
        if (otroT != null) cmbOtroVideo.SelectedItem = NombrePista(otroT);
        cmbJuego.SelectedIndex = 0;
        Seleccionar(cmbPrincipal, LogicaPov.Ajuste(veg, "principal"));
        Seleccionar(cmbJuego, LogicaPov.Ajuste(veg, "otroJuego"));
        txtJugador.Text = LogicaPov.Ajuste(veg, "jugador");
        cargando = false;
        MarcarDelOtro();
    }

    static double Cubre(Track t)
    {
        double c = 0;
        foreach (TrackEvent e in t.Events) c += e.Length.ToMilliseconds() / 1000.0;
        return c;
    }

    void MarcarDelOtro()
    {
        Track o = Pista(cmbOtro);
        List<Track> mismas = o != null ? LogicaPov.MismoArchivo(vegas.Project, o) : new List<Track>();
        cargando = true;
        foreach (ListViewItem it in lstPistas.Items)
            it.Checked = mismas.Exists(delegate (Track t) { return t.Index == (int)it.Tag; });
        cargando = false;
    }

    void Guardar(Dictionary<string, string> d)
    {
        Dictionary<string, string> todo = new Dictionary<string, string>();
        foreach (string k in new string[] { "principal", "otroVideo", "otroJuego", "jugador" }) todo[k] = LogicaPov.Ajuste(veg, k);
        foreach (KeyValuePair<string, string> kv in d) todo[kv.Key] = kv.Value;
        LogicaPov.GuardarAjustes(veg, todo);
    }

    void Sincronizar()
    {
        Track r = Pista(cmbRef), o = Pista(cmbOtro);
        if (r == null || o == null || r.Index == o.Index) { Estado("Elige una pista tuya y otra del otro POV.", true); return; }
        List<Track> mover = new List<Track>();
        foreach (ListViewItem it in lstPistas.Items)
            if (it.Checked) foreach (Track t in vegas.Project.Tracks) if (t.Index == (int)it.Tag) mover.Add(t);
        if (mover.Count == 0) { Estado("Marca las pistas del otro POV.", true); return; }
        if (mover.Exists(delegate (Track t) { return t.Index == r.Index; })) { Estado("Tu pista de referencia no puede estar entre las que se mueven.", true); return; }
        double dur = vegas.Project.Length.ToMilliseconds() / 1000.0;
        trabajando = true;
        Cursor = Cursors.WaitCursor;
        try
        {
            Estado("Escuchando tu pista (puede tardar un poco)…", false); Application.DoEvents();
            Analisis ar = PistasVegas.Niveles(vegas, (AudioTrack)r, 0, dur);
            Estado("Escuchando la del otro POV…", false); Application.DoEvents();
            Analisis ao = PistasVegas.Niveles(vegas, (AudioTrack)o, 0, dur);
            double conf;
            double d = LogicaPov.Desfase(LogicaPov.Envolvente(ar.Db, 2), LogicaPov.Envolvente(ao.Db, 2), 0.02, numMax.Valor * 60, out conf);
            Cursor = Cursors.Default;
            string calidad = conf >= 8 ? "alta" : conf >= 5 ? "media" : "baja";
            lblSinc.Text = "Desfase: " + (d >= 0 ? "+" : "−") + Math.Abs(d).ToString("0.00") + " s (confianza " + calidad + ", " + conf.ToString("0.0") + ")";
            lblSinc.ForeColor = conf >= 5 ? Tema.Texto : Tema.Silencio;
            if (MessageBox.Show(this, "El otro POV va " + Math.Abs(d).ToString("0.00") + " s " + (d >= 0 ? "antes" : "después") + " que el tuyo (confianza " + calidad + ").\n\n" +
                    (conf < 5 ? "La confianza es baja: revisa que la pista de comparación tenga voces que también suenan en la tuya (la llamada).\n\n" : "") +
                    "¿Mover " + mover.Count + " pistas del otro POV " + Math.Abs(d).ToString("0.00") + " s " + (d >= 0 ? "a la derecha" : "a la izquierda") + "?",
                    "Varios POV", MessageBoxButtons.YesNo) == DialogResult.Yes)
            {
                using (UndoBlock u = new UndoBlock("Sincronizar POV")) LogicaPov.Mover(mover, d);
                Track v = mover.Find(delegate (Track t) { return !t.IsAudio(); });
                if (v != null) Guardar(new Dictionary<string, string> { { "otroVideo", NombrePista(v) } });
                Estado("✔ Sincronizado (Ctrl+Z lo deshace). Ahora quita los silencios con su micrófono entre las voces y transcribe.", false);
                Llenar();
            }
            else Estado("No se movió nada.", false);
        }
        catch (Exception ex) { Cursor = Cursors.Default; Estado("No se pudo sincronizar: " + ex.Message, true); }
        trabajando = false;
    }

    void LlenarTramos()
    {
        cargando = true;
        lstTramos.Items.Clear();
        foreach (TramoPov tp in tramos)
        {
            ListViewItem it = new ListViewItem(Formato.Tiempo(tp.Inicio) + "–" + Formato.Tiempo(tp.Fin));
            it.SubItems.Add(Math.Round(tp.Duracion) + " s");
            it.SubItems.Add(tp.Dicho);
            it.SubItems.Add(tp.Motivo);
            it.Checked = tp.Elegido;
            it.Tag = tp;
            lstTramos.Items.Add(it);
        }
        cargando = false;
    }

    void ConIA()
    {
        if (trans == null) { Estado("Hace falta la transcripción (con el micrófono del otro jugador transcrito).", true); return; }
        if (String.IsNullOrEmpty(config.GeminiClave)) { Estado("Falta la clave de Gemini: ejecuta «ConfigurarVegasCut».", true); return; }
        string jugador = txtJugador.Text.Trim().Length > 0 ? txtJugador.Text.Trim() : "el otro jugador";
        Guardar(new Dictionary<string, string> { { "jugador", txtJugador.Text.Trim() }, { "principal", (string)cmbPrincipal.SelectedItem ?? "" },
                                                 { "otroVideo", (string)cmbOtroVideo.SelectedItem ?? "" }, { "otroJuego", cmbJuego.SelectedIndex > 0 ? (string)cmbJuego.SelectedItem : "" } });
        double dur = 0;
        Track pr = Pista(cmbPrincipal);
        if (pr != null) foreach (TrackEvent e in pr.Events) dur = Math.Max(dur, e.End.ToMilliseconds() / 1000.0);
        string instr = LogicaPov.Instrucciones(jugador, numPct.Valor, numEntre.Valor);
        string msg = LogicaPov.Mensaje(trans, vegas.Project, jugador, dur);
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        int pct = numPct.Valor, entre = numEntre.Valor;
        trabajando = true;
        btnIA.Enabled = btnAplicar.Enabled = false;
        Estado("Gemini está buscando los momentos para el POV de " + jugador + "…", false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(clave, modelo, instr, msg, true); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    btnIA.Enabled = btnAplicar.Enabled = true;
                    if (error != null) { Estado("Gemini: " + error, true); return; }
                    try
                    {
                        tramos = LogicaPov.Leer(resp, dur, pct, entre, trans);
                        LlenarTramos();
                        double t = 0;
                        foreach (TramoPov tp in tramos) t += tp.Duracion;
                        Estado("✔ " + tramos.Count + " cambios (" + Formato.Tiempo(t) + ", " + (dur > 0 ? Math.Round(100 * t / dur) : 0) + " % del video). " +
                               "Doble clic para ir; desmarca los que no quieras y pulsa «Aplicar».", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Aplicar(bool quitar)
    {
        Track pr = Pista(cmbPrincipal), ov = Pista(cmbOtroVideo), oj = cmbJuego.SelectedIndex > 0 ? Pista(cmbJuego) : null;
        if (pr == null || ov == null || pr.Index == ov.Index) { Estado("Elige el video principal y el del otro POV.", true); return; }
        int n;
        using (UndoBlock u = new UndoBlock(quitar ? "Quitar cambios de POV" : "Cambios de POV"))
            n = LogicaPov.Aplicar(pr, ov, oj, quitar ? new List<TramoPov>() : tramos);
        Estado(quitar ? "✔ Se ve solo el POV principal otra vez." :
               "✔ El POV de " + (txtJugador.Text.Trim().Length > 0 ? txtJugador.Text.Trim() : "el otro jugador") + " se ve en " + n +
               " tramos (los eventos fuera de esos tramos quedan silenciados, no borrados: puedes ajustarlos a mano). Ctrl+Z lo deshace.", false);
    }
}
