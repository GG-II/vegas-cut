using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Rellenar los huecos de la pista de musica con temas de la biblioteca.
class VentanaRelleno : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion trans;
    readonly Configuracion config = Configuracion.Cargar();
    SerieProyecto serie;
    MusicaSerie musica;
    BibliotecaMusica biblioteca;
    List<ArchivoMusica> candidatos = new List<ArchivoMusica>();
    List<HuecoMusica> huecos = new List<HuecoMusica>();
    bool trabajando, cargando;
    // Variedad: lo que se uso en capitulos anteriores y lo que ya se propuso en esta ventana no se vuelve a ofrecer.
    List<ArchivoMusica> todos = new List<ArchivoMusica>();
    List<UsoTema> historial = new List<UsoTema>();
    Dictionary<string, int> conteo = new Dictionary<string, int>();
    List<string> propuestos = new List<string>(), orden = new List<string>();
    readonly Random azar = new Random();
    string claveSerie = "", capitulo = "";
    CampoNumero numRecientes = new CampoNumero();

    Etiqueta lblInfo, lblEstado;
    CampoNumero numMin = new CampoNumero();
    Boton btnBuscar = new Boton("Buscar huecos", EstiloBoton.Secundario);
    Boton btnIA = new Boton("Elegir temas con IA", EstiloBoton.Primario);
    Lista lst = new Lista();
    Boton btnColocar = new Boton("Colocar", EstiloBoton.Primario);
    // Sin API: un archivo para el chat de otra IA y luego importar lo que responda.
    Boton btnExportar = new Boton("Archivo para otra IA…", EstiloBoton.Secundario);
    Boton btnImportar = new Boton("Importar respuesta…", EstiloBoton.Secundario);
    readonly string rutaPedido = "", rutaPedidoInfo = "";
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaRelleno(Vegas vegas, Transcripcion trans) : base("Rellenar la música", 1000)
    {
        this.vegas = vegas; this.trans = trans;
        StartPosition = FormStartPosition.CenterParent;
        string veg = vegas.Project.FilePath ?? "";
        if (veg.Length > 0)
        {
            string b = Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg));
            rutaPedido = b + ".vegascut-pedido-musica.txt";
            rutaPedidoInfo = b + ".vegascut-pedido-musica.json";
        }
        int m = Margen, w = Ancho;
        Encabezado("Rellenar la música", "Pone un tema en cada hueco de la pista de música según lo que pasa ahí. Lo que ya está no se toca.");
        int y = 92;
        lblInfo = Texto("", Tema.Normal, Tema.Texto, m, y, w - 540, 40);
        Texto("HUECOS DE AL MENOS", Tema.Pequena, Tema.TextoSuave, m + w - 530, y + 8, 130, 18);
        numMin.Sufijo = "s"; numMin.Minimo = 2; numMin.Maximo = 120; numMin.Paso = 1;
        Pos(numMin, m + w - 400, y, 80, 32);
        cargando = true; numMin.Valor = 8; cargando = false;
        Pos(btnBuscar, m + w - 310, y, 130, 32);
        Pos(btnIA, m + w - 170, y, 170, 32);
        y += 40;
        Texto("NO REPETIR LOS TEMAS DE LOS", Tema.Pequena, Tema.TextoSuave, m, y + 8, 180, 18);
        numRecientes.Sufijo = ""; numRecientes.Minimo = 0; numRecientes.Maximo = 30; numRecientes.Paso = 1;
        Pos(numRecientes, m + 184, y, 80, 32);
        Texto("CAPÍTULOS ANTERIORES (los menos usados salen primero)", Tema.Pequena, Tema.TextoSuave, m + 270, y + 8, 420, 18);
        cargando = true; numRecientes.Valor = 3; cargando = false;
        y += 42;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Hueco", 130);
        lst.Columns.Add("Dura", 56);
        lst.Columns.Add("Bloque", 120);
        lst.Columns.Add("Qué pasa", w - 130 - 56 - 120 - 260 - sb);
        lst.Columns.Add("Tema", 260);
        Pos(lst, m, y, w, 400);
        y += 410;
        Pos(btnExportar, m, y, 190, 40);
        Pos(btnImportar, m + 198, y, 180, 40);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m + 392, y, w - 300 - 392, 40);
        Pos(btnColocar, m + w - 290, y, 150, 40);
        Pos(btnCerrar, m + w - 130, y, 130, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        try
        {
            Serie.DelProyecto(CopiaBase.Original(vegas.Project.FilePath ?? ""), out serie);
            musica = serie != null ? serie.Musica : null;
            if (musica != null && musica.Carpeta.Length > 0 && Directory.Exists(musica.Carpeta)) biblioteca = BibliotecaMusica.Cargar(musica.Carpeta);
        }
        catch { biblioteca = null; }
        if (biblioteca != null) todos = MusicaSerie.Candidatos(biblioteca);
        LogicaRelleno.CarpetaMusica = musica != null ? musica.Carpeta : "";
        claveSerie = serie != null ? serie.Nombre : "";
        capitulo = NombreCap(veg);
        if (serie != null) foreach (string ep in serie.Episodios) orden.Add(NombreCap(ep));
        historial = HistorialMusica.Cargar(claveSerie);
        Variedad();
        candidatos = Muestra(LogicaRelleno.MuestraApi);
        numRecientes.Cambio += delegate { if (!cargando) { Variedad(); } };
        Shown += delegate { AprenderDeProyectos(); };

        btnBuscar.Click += delegate { Buscar(); };
        btnIA.Click += delegate { ConIA(); };
        btnExportar.Click += delegate { Exportar(); };
        btnImportar.Click += delegate { Importar(); };
        btnColocar.Click += delegate { Colocar(); };
        btnCerrar.Click += delegate { Close(); };
        lst.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando && e.Item.Tag != null) ((HuecoMusica)e.Item.Tag).Elegido = e.Item.Checked; };
        lst.DoubleClick += delegate
        {
            if (lst.SelectedIndices.Count == 0) return;
            HuecoMusica h = (HuecoMusica)lst.Items[lst.SelectedIndices[0]].Tag;
            try
            {
                vegas.Transport.CursorPosition = Timecode.FromMilliseconds(h.Inicio * 1000);
                vegas.Transport.SelectionStart = Timecode.FromMilliseconds(h.Inicio * 1000);
                vegas.Transport.SelectionLength = Timecode.FromMilliseconds(h.Duracion * 1000);
            }
            catch { }
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };
        Buscar();
    }

    static string NombreCap(string veg) { return Path.GetFileNameWithoutExtension(SerieProyecto.SinCopia(veg ?? "")); }

    // Lo que no se ofrece: lo de los capitulos anteriores y lo que ya se propuso en esta ventana.
    List<string> Evitar()
    {
        List<string> r = HistorialMusica.Recientes(historial, capitulo, numRecientes.Valor, orden);
        r.AddRange(propuestos);
        return r;
    }

    List<ArchivoMusica> Muestra(int max) { return LogicaRelleno.Muestra(todos, Evitar(), conteo, max, azar); }

    void Variedad()
    {
        conteo = HistorialMusica.Conteo(historial, capitulo);
        if (biblioteca == null) { lblInfo.Text = "Sin biblioteca de música: elígela en Series → Música… (con la serie de este capítulo)."; return; }
        int fuera = 0;
        List<string> rec = Evitar();
        foreach (ArchivoMusica a in todos) if (rec.Contains(a.Ruta.ToLowerInvariant())) fuera++;
        lblInfo.Text = "Biblioteca: " + todos.Count + " temas que sirven" + (fuera > 0 ? " (" + fuera + " ya usados o propuestos, no se ofrecen)" : "") +
                       (trans == null ? " · sin transcripción (la IA solo ve los bloques)" : "");
    }

    // Los capitulos de la serie que nunca se leyeron: sus .veg dicen que musica llevan (una sola vez).
    void AprenderDeProyectos()
    {
        if (biblioteca == null || serie == null || todos.Count == 0) return;
        List<string> faltan = new List<string>();
        foreach (string ep in serie.Episodios)
        {
            string n = NombreCap(ep);
            if (n == capitulo || !File.Exists(ep)) continue;
            if (historial.Exists(delegate (UsoTema u) { return String.Equals(u.Capitulo, n, StringComparison.OrdinalIgnoreCase); })) continue;
            faltan.Add(ep);
        }
        if (faltan.Count == 0) return;
        Estado("Leyendo qué música llevan " + faltan.Count + " capítulos anteriores (solo esta vez)…", false);
        Cursor = Cursors.WaitCursor;
        Application.DoEvents();
        int temas = 0;
        try
        {
            foreach (KeyValuePair<string, List<string>> kv in HistorialMusica.DeProyectos(faltan, todos))
            {
                HistorialMusica.Registrar(claveSerie, kv.Key, kv.Value);
                temas += kv.Value.Count;
            }
        }
        catch { }
        Cursor = Cursors.Default;
        historial = HistorialMusica.Cargar(claveSerie);
        Variedad();
        Estado("✔ " + faltan.Count + " capítulos anteriores leídos: " + temas + " temas que ya usaste" + (temas > 0 ? " (se ofrecen menos)." : "."), false);
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Habilitar()
    {
        btnBuscar.Enabled = !trabajando;
        btnIA.Enabled = !trabajando && huecos.Count > 0 && candidatos.Count > 0 && !String.IsNullOrEmpty(config.GeminiClave);
        btnExportar.Enabled = !trabajando && huecos.Count > 0 && candidatos.Count > 0 && rutaPedido.Length > 0;
        btnImportar.Enabled = !trabajando && rutaPedido.Length > 0;
        btnColocar.Enabled = !trabajando && huecos.Exists(delegate (HuecoMusica h) { return h.Elegido && !h.Silencio && (h.Tema >= 0 || h.Personaje.Length > 0); });
        btnCerrar.Enabled = !trabajando;
    }

    void Buscar()
    {
        huecos = LogicaRelleno.Huecos(vegas.Project, trans, numMin.Valor);
        propuestos.Clear();
        btnIA.Text = "Elegir temas con IA";
        Variedad();
        Llenar();
        double total = 0;
        foreach (HuecoMusica h in huecos) total += h.Duracion;
        Estado(huecos.Count == 0 ? "No hay huecos de " + numMin.Valor + " s o más en la pista de música." :
               huecos.Count + " huecos (" + Formato.Tiempo(total) + " sin música). Desmarca los que quieras en silencio y pulsa «Elegir temas con IA».", false);
        Habilitar();
    }

    void Llenar()
    {
        cargando = true;
        lst.Items.Clear();
        foreach (HuecoMusica h in huecos)
        {
            ListViewItem it = new ListViewItem(Formato.Tiempo(h.Inicio) + "–" + Formato.Tiempo(h.Fin));
            it.SubItems.Add(Math.Round(h.Duracion) + " s");
            it.SubItems.Add(h.Bloque);
            it.SubItems.Add(h.Dicho.Length > 0 ? h.Dicho : "(nadie habla)");
            string tema = LogicaRelleno.Nombre(h, candidatos);
            it.SubItems.Add(tema + (h.Motivo.Length > 0 ? " — " + h.Motivo : ""));
            it.Checked = h.Elegido;
            if (h.Silencio) it.ForeColor = Tema.TextoSuave;
            it.Tag = h;
            lst.Items.Add(it);
        }
        cargando = false;
    }

    void ConIA()
    {
        List<HuecoMusica> pedir = huecos.FindAll(delegate (HuecoMusica h) { return h.Elegido; });
        if (pedir.Count == 0) { Estado("Marca al menos un hueco.", true); return; }
        // Otra muestra cada vez, sin lo que ya se propuso: asi re-elegir da temas distintos.
        candidatos = Muestra(LogicaRelleno.MuestraApi);
        string instr = LogicaRelleno.Instrucciones();
        string msg = LogicaRelleno.Mensaje(pedir, candidatos, musica, LogicaRelleno.YaSuena(vegas.Project), conteo);
        string clave = config.GeminiClave, modelo = config.GeminiModelo;
        trabajando = true;
        Habilitar();
        Estado("Gemini está eligiendo un tema para cada hueco…", false);
        Thread hilo = new Thread(delegate ()
        {
            string resp = null, error = null;
            try { resp = Gemini.Generar(clave, modelo, instr, msg, true, null, LogicaRelleno.Temperatura); } catch (Exception ex) { error = ex.Message; }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    if (error != null) { Estado("Gemini: " + error, true); Habilitar(); return; }
                    try
                    {
                        int n = LogicaRelleno.Leer(resp, pedir, candidatos, musica);
                        Propuestos(pedir);
                        Llenar();
                        Estado("✔ " + n + " huecos con tema (o silencio). Revisa, desmarca lo que no quieras y pulsa «Colocar». " +
                               "Si no te convencen, «Elegir otros temas» ofrece otros distintos.", false);
                    }
                    catch (Exception ex) { Estado("La respuesta no se pudo leer (" + ex.Message + "). Intenta de nuevo.", true); }
                    Habilitar();
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    // Lo propuesto no vuelve a salir al re-elegir (en esta ventana, hasta «Buscar huecos»).
    void Propuestos(List<HuecoMusica> pedir)
    {
        foreach (HuecoMusica h in pedir)
            if (h.Archivo != null && !propuestos.Contains(h.Archivo.Ruta.ToLowerInvariant())) propuestos.Add(h.Archivo.Ruta.ToLowerInvariant());
        btnIA.Text = "Elegir otros temas";
        Variedad();
    }

    // ------------------------------------------------- otra IA (chat)

    double Largo()
    {
        double d = 0;
        foreach (Track t in vegas.Project.Tracks) foreach (TrackEvent e in t.Events) d = Math.Max(d, e.End.ToMilliseconds() / 1000.0);
        return d;
    }

    void Exportar()
    {
        List<HuecoMusica> pedir = huecos.FindAll(delegate (HuecoMusica h) { return h.Elegido; });
        if (pedir.Count == 0) { Estado("Marca al menos un hueco.", true); return; }
        candidatos = Muestra(LogicaRelleno.MuestraChat);
        string texto = ChatIA.Archivo("Rellenar la música", LogicaRelleno.Instrucciones(),
                                      LogicaRelleno.Mensaje(pedir, candidatos, musica, LogicaRelleno.YaSuena(vegas.Project), conteo),
                                      "Responde SOLO con el JSON {\"huecos\": [...]}: un elemento por hueco con su \"n\", el \"id\" del tema " +
                                      "de la BIBLIOTECA (o \"personaje\", o \"silencio\": true) y el \"motivo\". Solo ids de la lista.");
        try
        {
            File.WriteAllText(rutaPedido, texto, new System.Text.UTF8Encoding(false));
            Dictionary<string, object> info = new Dictionary<string, object>();
            info["duracionProyecto"] = Largo();
            info["minimo"] = numMin.Valor;
            List<object> ns = new List<object>();
            foreach (HuecoMusica h in pedir) ns.Add((double)h.N);
            info["huecos"] = ns;
            // Los temas ofrecidos, en orden: el id de la respuesta es la posicion en esta lista.
            List<object> temas = new List<object>();
            foreach (ArchivoMusica a in candidatos) temas.Add(a.Ruta);
            info["temas"] = temas;
            File.WriteAllText(rutaPedidoInfo, Json.Escribir(info), new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) { Estado("No se pudo guardar el archivo: " + ex.Message, true); return; }
        bool copiado = false;
        try { Clipboard.SetText(texto); copiado = true; } catch { }
        try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + rutaPedido + "\""); } catch { }
        Estado("✔ «" + Path.GetFileName(rutaPedido) + "»" + (copiado ? " (también copiado)" : "") +
               ". Adjúntalo o pégalo en el chat de tu IA y luego «Importar respuesta…».", false);
    }

    void Importar()
    {
        string json;
        using (DialogoRespuestaIA d = new DialogoRespuestaIA(Path.GetDirectoryName(rutaPedido), "huecos"))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            json = d.Json;
        }
        // Los huecos se vuelven a buscar igual que al pedir (los numeros tienen que coincidir).
        List<int> pedidos = new List<int>();
        try
        {
            if (File.Exists(rutaPedidoInfo))
            {
                object info = Json.Leer(File.ReadAllText(rutaPedidoInfo, System.Text.Encoding.UTF8));
                double dur = Json.Numero(info, "duracionProyecto", Largo());
                if (Math.Abs(dur - Largo()) > 0.5)
                {
                    Estado("El proyecto cambió desde que generaste el archivo (duraba " + Formato.Tiempo(dur) + ", ahora " +
                           Formato.Tiempo(Largo()) + "): los huecos ya no son los mismos. Genera el archivo otra vez.", true);
                    return;
                }
                cargando = true; numMin.Valor = (int)Json.Numero(info, "minimo", numMin.Valor); cargando = false;
                foreach (object x in Json.Lista(info, "huecos")) if (x is double) pedidos.Add((int)(double)x);
                // Los temas que se ofrecieron al exportar (la muestra de ahora seria otra).
                List<ArchivoMusica> ofrecidos = new List<ArchivoMusica>();
                foreach (object x in Json.Lista(info, "temas"))
                {
                    ArchivoMusica a = todos.Find(delegate (ArchivoMusica y) { return String.Equals(y.Ruta, x as string, StringComparison.OrdinalIgnoreCase); });
                    if (a != null) ofrecidos.Add(a);
                }
                if (ofrecidos.Count > 0) candidatos = ofrecidos;
            }
        }
        catch { }
        huecos = LogicaRelleno.Huecos(vegas.Project, trans, numMin.Valor);
        List<HuecoMusica> pedir = huecos.FindAll(delegate (HuecoMusica h) { return pedidos.Count == 0 || pedidos.Contains(h.N); });
        foreach (HuecoMusica h in huecos) h.Elegido = pedir.Contains(h);
        try
        {
            int n = LogicaRelleno.Leer(json, pedir, candidatos, musica);
            Propuestos(pedir);
            Llenar();
            Estado("✔ " + n + " huecos con tema (o silencio) de la otra IA. Revisa, desmarca lo que no quieras y pulsa «Colocar».", false);
        }
        catch (Exception ex) { Llenar(); Estado("La respuesta no se pudo leer (" + ex.Message + ").", true); }
        Habilitar();
    }

    void Colocar()
    {
        List<string> avisos = new List<string>();
        int n;
        List<string> usadas = LogicaRelleno.Colocados(huecos, candidatos);
        using (UndoBlock u = new UndoBlock("Rellenar la música"))
            n = LogicaRelleno.Colocar(vegas.Project, huecos, candidatos, musica, biblioteca, avisos);
        // Se anota lo de este capitulo para no repetirlo en los siguientes (lo de antes de este capitulo se conserva).
        try
        {
            List<string> previas = new List<string>();
            foreach (UsoTema u in historial) if (String.Equals(u.Capitulo, capitulo, StringComparison.OrdinalIgnoreCase) && u.Ruta.Length > 0) previas.Add(u.Ruta);
            foreach (string r in usadas) if (!previas.Contains(r)) previas.Add(r);
            if (n > 0) HistorialMusica.Registrar(claveSerie, capitulo, previas);
            historial = HistorialMusica.Cargar(claveSerie);
            Variedad();
        }
        catch { }
        Estado("✔ " + n + " temas colocados en «" + LogicaRelleno.PistaMusica + "» (sin balancear)." +
               (avisos.Count > 0 ? " Avisos: " + String.Join(" ", avisos.ToArray()) : ""), avisos.Count > 0);
        foreach (HuecoMusica h in huecos) if (h.Elegido && !h.Silencio && (h.Tema >= 0 || h.Personaje.Length > 0)) h.Elegido = false;
        Llenar();
        Habilitar();
    }
}
