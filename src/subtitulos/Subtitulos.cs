using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Subtitulos: armar, corregir con Gemini (con preguntas) y guardar el .srt.
class VentanaSubtitulos : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion trans;
    readonly Configuracion config = Configuracion.Cargar();
    readonly string rutaSrt, nombreSerie = "", contexto = "";
    MemoriaSub general, deSerie;
    List<Subtitulo> subs;
    List<PreguntaSub> preguntas = new List<PreguntaSub>();
    readonly OpcionesSub op = new OpcionesSub();
    bool trabajando, cargando;

    Etiqueta lblInfo, lblGlosario, lblEstado;
    CampoTexto txtIndicaciones = new CampoTexto(), txtEditar = new CampoTexto();
    Segmentado segAmbito = new Segmentado(new string[] { "Esta serie", "Todos mis videos" });
    Boton btnGlosario = new Boton("Glosario…", EstiloBoton.Secundario);
    Boton chipNombres = new Boton("Nombre de quien habla", EstiloBoton.Chip), chipCensura = new Boton("Tapar palabrotas (p***)", EstiloBoton.Chip);
    Boton btnCorregir = new Boton("Corregir con Gemini", EstiloBoton.Primario);
    Boton btnPreguntas = new Boton("Preguntas…", EstiloBoton.Secundario);
    Boton btnCambiar = new Boton("Cambiar", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar .srt", EstiloBoton.Primario), btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);
    Lista lst = new Lista();

    public VentanaSubtitulos(Vegas vegas, Transcripcion trans) : base("Subtítulos", 1080)
    {
        this.vegas = vegas; this.trans = trans;
        StartPosition = FormStartPosition.CenterParent;
        string veg = vegas.Project.FilePath ?? "";
        rutaSrt = veg.Length > 0 ? Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".srt") : "";
        try
        {
            SerieProyecto s;
            Serie.DelProyecto(CopiaBase.Original(veg), out s);
            if (s != null) { nombreSerie = s.Nombre; contexto = s.Nombre + (s.Notas.Trim().Length > 0 ? ": " + s.Notas.Trim() : ""); }
        }
        catch { }
        general = LogicaSubtitulos.Cargar("");
        deSerie = nombreSerie.Length > 0 ? LogicaSubtitulos.Cargar(nombreSerie) : new MemoriaSub();

        int m = Margen, w = Ancho, ci = 360;
        Encabezado("Subtítulos", "De la transcripción a un .srt: Gemini corrige lo que se oyó mal y te pregunta lo que no sabe.");
        int y = 92;
        lblInfo = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 28;
        int y0 = y;
        Texto("Indicaciones", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        y += 24;
        Pos(segAmbito, m, y, ci, 30);
        segAmbito.Seleccion = nombreSerie.Length > 0 ? 0 : 1;
        segAmbito.Enabled = nombreSerie.Length > 0;
        y += 36;
        txtIndicaciones.Multilinea = true;
        Pos(txtIndicaciones, m, y, ci, 120);
        y += 126;
        Texto("Ej.: «Los jugadores son Gerbert, Jason y David. El servidor se llama SteelCraft. "
              + "“Steve” siempre va así. No pongas puntos al final.» Se guardan y se usan siempre.",
              Tema.Pequena, Tema.TextoSuave, m, y, ci, 48);
        y += 54;
        lblGlosario = Texto("", Tema.Normal, Tema.Texto, m, y + 8, ci - 130, 20);
        Pos(btnGlosario, m + ci - 120, y, 120, 32);
        y += 44;
        Pos(chipNombres, m, y, 200, 28);
        Pos(chipCensura, m + 206, y, ci - 206, 28);
        y += 40;
        Pos(btnCorregir, m, y, ci, 42);
        y += 48;
        Pos(btnPreguntas, m, y, ci, 34);
        y += 42;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, ci, 72);

        int dx = m + ci + 24, dw = w - ci - 24;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.CheckBoxes = false;
        lst.Columns.Add("Cuándo", 74);
        lst.Columns.Add("Quién", 90);
        lst.Columns.Add("Subtítulo", dw - 74 - 90 - 170 - sb);
        lst.Columns.Add("Cambio", 170);
        Pos(lst, dx, y0, dw, 470);
        Pos(txtEditar, dx, y0 + 478, dw - 120, 34);
        Pos(btnCambiar, dx + dw - 110, y0 + 478, 110, 34);
        int fondo = Math.Max(y + 76, y0 + 524);
        Pos(btnCerrar, m + w - 330, fondo, 130, 40);
        Pos(btnGuardar, m + w - 190, fondo, 190, 40);
        ClientSize = new Size(ClientSize.Width, fondo + 40 + 24);

        cargando = true;
        MostrarMemoria();
        cargando = false;
        subs = LogicaSubtitulos.Armar(trans, op);
        int g = LogicaSubtitulos.AplicarGlosario(subs, Memoria());
        Llenar();
        lblInfo.Text = subs.Count + " subtítulos de la transcripción del " + trans.Creada + " (siguen tus cortes)" +
                       (g > 0 ? " · el glosario ya corrigió " + g : "") + ". Si limpiaste las voces o cambiaste el audio, vuelve a transcribir antes.";
        btnCorregir.Enabled = config.TieneGemini;
        if (!config.TieneGemini) Estado("Sin clave de Gemini: puedes guardar el .srt tal cual (con tu glosario).", true);
        btnPreguntas.Visible = false;

        segAmbito.Cambio += delegate { if (!cargando) { LeerMemoria(true); MostrarMemoria(); } };
        btnGlosario.Click += delegate { EditarGlosario(); };
        chipNombres.Click += delegate { chipNombres.Activo = !chipNombres.Activo; };
        chipCensura.Click += delegate { chipCensura.Activo = !chipCensura.Activo; };
        btnCorregir.Click += delegate { Corregir(); };
        btnPreguntas.Click += delegate { Preguntar(); };
        lst.SelectedIndexChanged += delegate { Subtitulo s = Elegido(); txtEditar.Text = s != null ? s.Texto : ""; };
        lst.DoubleClick += delegate
        {
            Subtitulo s = Elegido();
            if (s != null) try { vegas.Transport.CursorPosition = Timecode.FromMilliseconds(s.Inicio * 1000); } catch { }
        };
        btnCambiar.Click += delegate
        {
            Subtitulo s = Elegido();
            if (s == null || txtEditar.Text.Trim().Length == 0) return;
            s.Texto = txtEditar.Text.Trim(); s.Nota = "a mano";
            int i = lst.SelectedIndices[0];
            Llenar();
            lst.Items[i].Selected = true; lst.EnsureVisible(i);
        };
        btnGuardar.Click += delegate { GuardarSrt(); };
        btnCerrar.Click += delegate { LeerMemoria(true); Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    // ------------------------------------------------- memoria

    bool EnSerie { get { return segAmbito.Seleccion == 0 && nombreSerie.Length > 0; } }
    MemoriaSub Actual { get { return EnSerie ? deSerie : general; } }
    MemoriaSub Memoria() { return nombreSerie.Length > 0 ? LogicaSubtitulos.Juntar(general, deSerie) : general; }

    // La memoria cuyas indicaciones estan en el cuadro.
    MemoriaSub enCuadro;

    void MostrarMemoria()
    {
        enCuadro = Actual;
        txtIndicaciones.Text = enCuadro.Indicaciones;
        int n = Memoria().Glosario.Count;
        lblGlosario.Text = n == 0 ? "Glosario vacío" : "Glosario: " + n + " correcciones";
    }

    void LeerMemoria(bool guardar)
    {
        if (enCuadro != null) enCuadro.Indicaciones = txtIndicaciones.Text.Trim();
        if (guardar) Guardar();
    }

    void Guardar()
    {
        try
        {
            LogicaSubtitulos.Guardar("", general);
            if (nombreSerie.Length > 0) LogicaSubtitulos.Guardar(nombreSerie, deSerie);
        }
        catch (Exception ex) { Estado("No se pudo guardar la memoria: " + ex.Message, true); }
    }

    void EditarGlosario()
    {
        using (DialogoTexto d = new DialogoTexto("Glosario" + (EnSerie ? " de «" + nombreSerie + "»" : " de todos tus videos"),
                                                  "Una corrección por línea: lo que oye mal => cómo va. Se aplica siempre, antes de Gemini.",
                                                  Actual.GlosarioTexto()))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Actual.LeerGlosario(d.Texto);
        }
        Guardar();
        int g = LogicaSubtitulos.AplicarGlosario(subs, Memoria());
        Llenar();
        MostrarMemoria();
        Estado("✔ Glosario guardado" + (g > 0 ? "; corrigió " + g + " subtítulos." : "."), false);
    }

    // ------------------------------------------------- lista

    Subtitulo Elegido() { return lst.SelectedIndices.Count > 0 ? (Subtitulo)lst.Items[lst.SelectedIndices[0]].Tag : null; }

    void Llenar()
    {
        lst.BeginUpdate();
        lst.Items.Clear();
        foreach (Subtitulo s in subs)
        {
            ListViewItem it = new ListViewItem(Formato.Tiempo(s.Inicio));
            it.SubItems.Add(s.Quien);
            it.SubItems.Add(s.Texto);
            it.SubItems.Add(s.Nota);
            if (s.Nota.Length > 0) it.ForeColor = Tema.Voz;
            else if (s.Dudosas.Count > 0) it.ForeColor = Tema.AcentoHover;
            it.Tag = s;
            lst.Items.Add(it);
        }
        lst.EndUpdate();
    }

    // ------------------------------------------------- Gemini

    void Corregir()
    {
        LeerMemoria(true);
        MemoriaSub mem = Memoria();
        List<string> personas = new List<string>();
        foreach (Hablante h in trans.Hablantes) if (h.Voz && !personas.Contains(h.Nombre)) personas.Add(h.Nombre);
        string instr = LogicaSubtitulos.Instrucciones(), msg = LogicaSubtitulos.Mensaje(subs, personas, contexto, mem);
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        foreach (Control c in new Control[] { btnCorregir, btnGuardar, btnCerrar, btnGlosario, btnPreguntas }) c.Enabled = false;
        Estado("Gemini está leyendo " + subs.Count + " subtítulos…", false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(clave, modelo, instr, msg, true); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    foreach (Control c in new Control[] { btnCorregir, btnGuardar, btnCerrar, btnGlosario, btnPreguntas }) c.Enabled = true;
                    if (error != null) { Estado(error, true); return; }
                    try
                    {
                        preguntas = new List<PreguntaSub>();
                        MemoriaSub aprendido = new MemoriaSub();
                        int n = LogicaSubtitulos.Leer(resp, subs, preguntas, aprendido);
                        foreach (KeyValuePair<string, string> g in aprendido.Glosario) Actual.Aprender(g.Key, g.Value);
                        Guardar();
                        Llenar();
                        MostrarMemoria();
                        btnPreguntas.Visible = preguntas.Count > 0;
                        btnPreguntas.Text = "Responder " + preguntas.Count + (preguntas.Count == 1 ? " pregunta…" : " preguntas…");
                        Estado("✔ " + n + " subtítulos corregidos (en azul; «Cambio» dice cómo era)" +
                               (aprendido.Glosario.Count > 0 ? ", " + aprendido.Glosario.Count + " correcciones nuevas al glosario" : "") +
                               (preguntas.Count > 0 ? ". Gemini tiene " + preguntas.Count + " preguntas." : ". Revisa y guarda el .srt."), false);
                        if (preguntas.Count > 0) Preguntar();
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Preguntar()
    {
        if (preguntas.Count == 0) return;
        using (DialogoPreguntasSub d = new DialogoPreguntasSub(preguntas, subs))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
        }
        int n = 0, aprendidas = 0;
        foreach (PreguntaSub p in preguntas)
        {
            n += LogicaSubtitulos.Responder(p, subs);
            if (p.Recordar && p.Respuesta.Trim().Length > 0 && !String.Equals(p.Respuesta.Trim(), p.Fragmento, StringComparison.Ordinal))
            {
                Actual.Aprender(p.Fragmento, p.Respuesta);
                aprendidas++;
            }
        }
        Guardar();
        preguntas.Clear();
        btnPreguntas.Visible = false;
        Llenar();
        MostrarMemoria();
        Estado("✔ " + n + " subtítulos con tus respuestas" + (aprendidas > 0 ? "; " + aprendidas + " quedan en el glosario para la próxima" : "") + ". Revisa y guarda el .srt.", false);
    }

    void GuardarSrt()
    {
        if (rutaSrt.Length == 0) { Estado("Guarda el proyecto primero.", true); return; }
        LeerMemoria(true);
        op.Nombres = chipNombres.Activo;
        List<Subtitulo> salida = new List<Subtitulo>();
        foreach (Subtitulo s in subs) salida.Add(new Subtitulo { Id = s.Id, Hablante = s.Hablante, Inicio = s.Inicio, Fin = s.Fin, Quien = s.Quien, Texto = s.Texto });
        int tapadas = chipCensura.Activo ? LogicaSubtitulos.Censurar(salida, OpcionesCensura.CargarPalabras()) : 0;
        try
        {
            File.WriteAllText(rutaSrt, LogicaSubtitulos.Srt(salida, op), new UTF8Encoding(true));
            Estado("✔ Guardado «" + Path.GetFileName(rutaSrt) + "» junto al proyecto (" + salida.Count + " subtítulos" +
                   (tapadas > 0 ? ", " + tapadas + " con palabrotas tapadas" : "") + "). Súbelo a YouTube en «Subtítulos».", false);
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + rutaSrt + "\""); } catch { }
        }
        catch (Exception ex) { Estado("No se pudo guardar: " + ex.Message, true); }
    }
}

// Texto largo editable (glosario).
class DialogoTexto : VentanaBase
{
    readonly CampoTexto txt = new CampoTexto();
    public string Texto { get { return txt.Text; } }

    public DialogoTexto(string titulo, string ayuda, string texto) : base(titulo, 620)
    {
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado(titulo, ayuda);
        txt.Multilinea = true;
        txt.Text = texto;
        Pos(txt, m, 92, w, 300);
        Boton ok = new Boton("Guardar", EstiloBoton.Primario), no = new Boton("Cancelar", EstiloBoton.Secundario);
        Pos(no, m + w - 270, 404, 120, 40);
        Pos(ok, m + w - 140, 404, 140, 40);
        ClientSize = new Size(ClientSize.Width, 404 + 40 + 24);
        ok.Click += delegate { DialogResult = DialogResult.OK; Close(); };
        no.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
    }
}

// Las preguntas de Gemini: cada una con su contexto, opciones y «Recordar».
class DialogoPreguntasSub : VentanaBase
{
    public DialogoPreguntasSub(List<PreguntaSub> preguntas, List<Subtitulo> subs) : base("Preguntas de Gemini", 820)
    {
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Preguntas de Gemini", "Elige o escribe lo que se dice. «Recordar» lo agrega al glosario para los próximos videos.");
        Panel panel = new Panel();
        panel.AutoScroll = true;
        panel.BackColor = Tema.Fondo;
        Pos(panel, m, 92, w, 430);
        Dictionary<int, Subtitulo> porId = new Dictionary<int, Subtitulo>();
        foreach (Subtitulo s in subs) porId[s.Id] = s;
        int y = 0, pw = w - SystemInformation.VerticalScrollBarWidth - 8;
        List<KeyValuePair<PreguntaSub, KeyValuePair<Combo, Boton>>> campos = new List<KeyValuePair<PreguntaSub, KeyValuePair<Combo, Boton>>>();
        foreach (PreguntaSub p in preguntas)
        {
            Subtitulo s = porId[p.Ids[0]];
            Etiqueta q = new Etiqueta(p.Pregunta.Length > 0 ? p.Pregunta : "¿Qué se dice?", Tema.Negrita, Tema.Texto);
            q.SetBounds(0, y, pw, 20); panel.Controls.Add(q);
            Etiqueta c = new Etiqueta("Se oyó «" + p.Fragmento + "» en " + Formato.Tiempo(s.Inicio) + " · " + s.Quien + ": «" + s.Texto + "»" +
                                      (p.Ids.Count > 1 ? " (y " + (p.Ids.Count - 1) + " más)" : ""),
                                      Tema.Pequena, Tema.TextoSuave);
            c.SetBounds(0, y + 20, pw, 32); c.TextAlign = ContentAlignment.TopLeft; panel.Controls.Add(c);
            Combo cb = new Combo(true);
            foreach (string o in p.Opciones) cb.Items.Add(o);
            if (!cb.Items.Contains(p.Fragmento)) cb.Items.Add(p.Fragmento);
            cb.Text = p.Respuesta;
            cb.SetBounds(0, y + 54, pw - 150, 30); panel.Controls.Add(cb);
            Boton rec = new Boton("Recordar", EstiloBoton.Chip);
            rec.Activo = p.Recordar;
            rec.SetBounds(pw - 140, y + 55, 140, 28); panel.Controls.Add(rec);
            rec.Click += delegate { rec.Activo = !rec.Activo; };
            campos.Add(new KeyValuePair<PreguntaSub, KeyValuePair<Combo, Boton>>(p, new KeyValuePair<Combo, Boton>(cb, rec)));
            y += 100;
        }
        Boton ok = new Boton("Aplicar respuestas", EstiloBoton.Primario), no = new Boton("Después", EstiloBoton.Secundario);
        Pos(no, m + w - 340, 534, 130, 40);
        Pos(ok, m + w - 200, 534, 200, 40);
        ClientSize = new Size(ClientSize.Width, 534 + 40 + 24);
        ok.Click += delegate
        {
            foreach (KeyValuePair<PreguntaSub, KeyValuePair<Combo, Boton>> kv in campos)
            {
                kv.Key.Respuesta = kv.Value.Key.Text.Trim();
                kv.Key.Recordar = kv.Value.Value.Activo;
            }
            DialogResult = DialogResult.OK; Close();
        };
        no.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
    }
}
