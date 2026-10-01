using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        string ruta = Transcripcion.RutaPara(vegas.Project.FilePath);
        if (ruta == null || !File.Exists(ruta))
        {
            MessageBox.Show("Este proyecto aún no tiene transcripción.\n\nEjecuta primero “Transcribir”.", "Censurar palabrotas");
            return;
        }
        Transcripcion t;
        try { t = Transcripcion.Cargar(ruta); }
        catch (Exception ex) { MessageBox.Show("No se pudo leer la transcripción: " + ex.Message, "Censurar palabrotas"); return; }
        using (VentanaCensura v = new VentanaCensura(vegas, t)) v.ShowDialog();
    }
}

// Lo que toca la linea de tiempo de Vegas.
public static class CensuraVegas
{
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }

    // Donde esta ahora cada coincidencia. Con las fuentes de la transcripcion
    // se encuentra aunque hayas editado a mano; sin ellas (transcripciones
    // viejas) solo se siguen los cortes hechos con estas herramientas.
    public static void Ubicar(Project p, Transcripcion t, List<Coincidencia> lista)
    {
        foreach (Coincidencia c in lista)
        {
            c.Lugares.Clear();
            if (c.Hablante < 0 || c.Hablante >= t.Hablantes.Count) continue;
            Hablante h = t.Hablantes[c.Hablante];
            if (h.Fuentes.Count > 0)
            {
                Fuente f;
                double segundo;
                if (!t.AFuente(c.Hablante, c.Inicio, out f, out segundo)) continue;
                double dur = (c.Fin - c.Inicio) * f.Velocidad;
                foreach (PistasVegas.Lugar l in PistasVegas.Donde(p, f.Media, f.Flujo, segundo))
                {
                    LugarCensura x = new LugarCensura();
                    x.Pista = l.Pista.Index; x.Inicio = l.Tiempo; x.Fin = l.Tiempo + dur / l.Velocidad;
                    c.Lugares.Add(x);
                }
            }
            else
            {
                double a = t.Mapear(c.Inicio), b = t.Mapear(c.Fin);
                if (double.IsNaN(a) || double.IsNaN(b)) continue;
                LugarCensura x = new LugarCensura();
                x.Inicio = a; x.Fin = b;
                int n;
                if (h.Etiqueta.Length > 1 && int.TryParse(h.Etiqueta.Substring(1), out n))
                    foreach (Track pista in p.Tracks)
                        if (pista.IsAudio() && pista.Index == n - 1) x.Pista = pista.Index;
                c.Lugares.Add(x);
            }
        }
    }

    static Media Abrir(Project p, string ruta)
    {
        Media m = null;
        try { m = p.MediaPool.Find(ruta); } catch { }
        return m ?? new Media(ruta);
    }

    // Silencia la voz y pone el efecto. Devuelve cuantos lugares se taparon.
    public static int Aplicar(Project p, List<Coincidencia> lista, OpcionesCensura o)
    {
        Dictionary<int, List<Rango>> porPista = new Dictionary<int, List<Rango>>();
        List<Rango> todos = new List<Rango>();
        foreach (Coincidencia c in lista)
        {
            if (!c.Elegida) continue;
            foreach (LugarCensura l in c.Lugares)
            {
                Rango r = LogicaCensura.Tapa(l.Inicio, l.Fin, o);
                todos.Add(r);
                if (l.Pista < 0) continue;
                if (!porPista.ContainsKey(l.Pista)) porPista[l.Pista] = new List<Rango>();
                porPista[l.Pista].Add(r);
            }
        }
        List<Rango> tapas = LogicaCensura.Unir(todos);
        if (tapas.Count == 0) return 0;

        if (o.Silenciar)
            foreach (KeyValuePair<int, List<Rango>> kv in porPista)
                foreach (Track pista in p.Tracks)
                    if (pista.IsAudio() && pista.Index == kv.Key)
                        Editor.Silenciar(p, new List<Track> { pista }, LogicaCensura.Unir(kv.Value), 0.008);

        if (o.Sfx != "-")
        {
            string ruta = o.Sfx.Length == 0 ? LogicaCensura.Pitido() : o.Sfx;
            if (!File.Exists(ruta)) throw new Exception("No se encontró el efecto: " + ruta);
            Media media = Abrir(p, ruta);
            MediaStream flujo = media.Streams.GetItemByMediaType(MediaType.Audio, 0);
            if (flujo == null) throw new Exception("El efecto no tiene audio: " + Path.GetFileName(ruta));
            double largo = 0;
            try { largo = media.Length.ToMilliseconds() / 1000.0; } catch { }

            AudioTrack pista = new AudioTrack(p.Tracks.Count, "Censura");
            p.Tracks.Add(pista);
            try { pista.Volume = (float)Math.Pow(10, o.VolumenSfx / 20.0); } catch { }
            foreach (Rango tapa in tapas)
            {
                Rango s = LogicaCensura.Sfx(tapa, largo, o.Encaje);
                double dur = s.Fin - s.Inicio;
                if (largo > 0) dur = Math.Min(dur, largo);
                AudioEvent ev = pista.AddAudioEvent(TC(s.Inicio), TC(dur));
                ev.AddTake(flujo);
                try { ev.FadeIn.Length = TC(0.005); ev.FadeOut.Length = TC(Math.Min(0.02, dur / 4)); } catch { }
            }
        }
        return tapas.Count;
    }
}

// Editor de la lista de palabras.
class DialogoPalabras : VentanaBase
{
    CampoTexto txt = new CampoTexto();
    public string Palabras { get { return txt.Text.Trim() + "\n"; } }

    public DialogoPalabras(string palabras) : base("Palabras a censurar", 560)
    {
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Palabras a censurar", "Una por línea · ching* = chingar, chingada… · frases: puta madre");
        txt.Multilinea = true;
        txt.Text = palabras.Replace("\r\n", "\n").Replace("\n", "\r\n");
        Pos(txt, m, 92, w, 360);
        Boton restaurar = new Boton("Restaurar la lista inicial", EstiloBoton.Secundario);
        Boton cancelar = new Boton("Cancelar", EstiloBoton.Secundario);
        Boton guardar = new Boton("Guardar", EstiloBoton.Primario);
        Pos(restaurar, m, 468, 210, 38);
        Pos(cancelar, m + w - 250, 468, 110, 38);
        Pos(guardar, m + w - 130, 468, 130, 38);
        ClientSize = new Size(ClientSize.Width, 530);
        restaurar.Click += delegate { txt.Text = LogicaCensura.PalabrasPorDefecto.Replace("\n", "\r\n"); };
        cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        guardar.Click += delegate { DialogResult = DialogResult.OK; Close(); };
    }
}

class VentanaCensura : VentanaBase
{
    readonly Vegas vegas;
    readonly Transcripcion transcripcion;
    readonly OpcionesCensura op = OpcionesCensura.Cargar();
    string palabras = OpcionesCensura.CargarPalabras();
    List<Coincidencia> lista = new List<Coincidencia>();
    bool cargando;

    Lista lst = new Lista();
    Etiqueta lblCuenta, lblEstado;
    Boton btnPalabras = new Boton("Palabras…", EstiloBoton.Secundario);
    CampoNumero numAntes = new CampoNumero(), numDespues = new CampoNumero(), numMinimo = new CampoNumero();
    Segmentado segTapar = new Segmentado(new string[] { "Toda la palabra", "Solo el inicio", "Solo el final" });
    Combo comboSfx = new Combo();
    Segmentado segEncaje = new Segmentado(new string[] { "Al largo de la palabra", "Empieza con ella", "Centrado", "Termina con ella" });
    Deslizador desVolumen = new Deslizador();
    Etiqueta lblVolumen;
    Segmentado segOriginal = new Segmentado(new string[] { "Silenciar la voz", "Dejarla sonar debajo" });
    Boton btnCensurar = new Boton("Censurar", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);
    List<string> rutasSfx = new List<string>();   // paralelo a comboSfx

    public VentanaCensura(Vegas vegas, Transcripcion t) : base("Censurar palabrotas", 980)
    {
        this.vegas = vegas;
        this.transcripcion = t;
        int m = Margen, w = Ancho;
        Encabezado("Censurar palabrotas", "Busca en la transcripción, tapa cada palabra con un efecto y silencia la voz justo ahí.");

        int y = 92;
        string aviso = t.TieneFuentes ? "" : AvisoSinFuentes();
        Etiqueta lblAviso = Texto(aviso, Tema.Pequena, Tema.AcentoHover, m, y, w, aviso.Length > 0 ? 34 : 0);
        if (aviso.Length > 0) y += 40;

        lblCuenta = Texto("", Tema.Negrita, Tema.Texto, m, y + 6, w - 140, 22);
        Pos(btnPalabras, m + w - 130, y, 130, 32);
        y += 42;
        lst.Columns.Add("Tiempo", 74);
        lst.Columns.Add("Quién", 120);
        lst.Columns.Add("Dice", 130);
        lst.Columns.Add("Contexto (doble clic: escuchar)", w - 74 - 120 - 130 - 64 - 24);
        lst.Columns.Add("Seguro", 64);
        Pos(lst, m, y, w, 230);
        y += 246;

        // Cuando tapar
        Texto("TAPAR", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        Texto("ANTES", Tema.Pequena, Tema.TextoSuave, m + 400, y, 110, 18);
        Texto("DESPUÉS", Tema.Pequena, Tema.TextoSuave, m + 526, y, 110, 18);
        Texto("MÍNIMO", Tema.Pequena, Tema.TextoSuave, m + 652, y, 110, 18);
        y += 20;
        segTapar.Seleccion = (int)op.Tapar;
        Pos(segTapar, m, y, 384, 34);
        Numero(numAntes, op.AntesMs, 0, 1000, 10, m + 400, y);
        Numero(numDespues, op.DespuesMs, 0, 1000, 10, m + 526, y);
        Numero(numMinimo, op.MinimoMs, 0, 2000, 50, m + 652, y);
        Texto("Para palabras que Whisper marca casi sin duración.", Tema.Pequena, Tema.TextoSuave,
              m + 780, y - 4, w - 780, 44);
        y += 50;

        // Con que
        Texto("EFECTO", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        Texto("DÓNDE VA EL EFECTO", Tema.Pequena, Tema.TextoSuave, m + 330, y, 300, 18);
        y += 20;
        LlenarSfx();
        Pos(comboSfx, m, y + 2, 314, 30);
        segEncaje.Seleccion = (int)op.Encaje;
        Pos(segEncaje, m + 330, y, w - 330, 34);
        y += 50;
        Texto("VOLUMEN DEL EFECTO", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        lblVolumen = Texto("", Tema.Negrita, Tema.Texto, m + 230, y - 2, 84, 20);
        lblVolumen.TextAlign = ContentAlignment.MiddleRight;
        Texto("LA VOZ ORIGINAL", Tema.Pequena, Tema.TextoSuave, m + 330, y, 300, 18);
        y += 20;
        desVolumen.Minimo = -24; desVolumen.Maximo = 0; desVolumen.Valor = op.VolumenSfx;
        Pos(desVolumen, m - 8, y, 330, 34);
        segOriginal.Seleccion = op.Silenciar ? 0 : 1;
        Pos(segOriginal, m + 330, y, 384, 34);
        y += 50;

        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 340, 40);
        Pos(btnCerrar, m + w - 330, y, 110, 40);
        Pos(btnCensurar, m + w - 210, y, 210, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        lst.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
        {
            if (cargando) return;
            ((Coincidencia)e.Item.Tag).Elegida = e.Item.Checked;
            Actualizar();
        };
        lst.DoubleClick += delegate { Escuchar(); };
        btnPalabras.Click += delegate
        {
            using (DialogoPalabras d = new DialogoPalabras(palabras))
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    palabras = d.Palabras;
                    OpcionesCensura.GuardarPalabras(palabras);
                    Buscar();
                }
        };
        comboSfx.SelectedIndexChanged += delegate { if (!cargando) ElegirSfx(); };
        segTapar.Cambio += delegate { Actualizar(); };
        segEncaje.Cambio += delegate { Actualizar(); };
        segOriginal.Cambio += delegate { Actualizar(); };
        desVolumen.Cambio += delegate { Actualizar(); };
        numAntes.Cambio += delegate { Actualizar(); };
        numDespues.Cambio += delegate { Actualizar(); };
        numMinimo.Cambio += delegate { Actualizar(); };
        btnCerrar.Click += delegate { Close(); };
        btnCensurar.Click += delegate { Censurar(); };
        Buscar();
    }

    string AvisoSinFuentes()
    {
        string s = "Esta transcripción es de antes de que vegas-cut guardara de qué archivo sale cada palabra: " +
                   "solo se siguen los cortes hechos con estas herramientas.";
        string sync = transcripcion.Sincronizar(vegas.Project.Length.ToMilliseconds() / 1000.0);
        return sync.Length > 0 && !sync.StartsWith("Se detect")
            ? s + " Como editaste a mano, vuelve a transcribir (un video ya editado tarda poco)."
            : s;
    }

    void Numero(CampoNumero n, int valor, int min, int max, int paso, int x, int y)
    {
        n.Minimo = min; n.Maximo = max; n.Paso = paso; n.Valor = valor;
        Pos(n, x, y, 110, 34);
    }

    // ------------------------------------------------------------- efecto

    void LlenarSfx()
    {
        cargando = true;
        comboSfx.Items.Clear(); rutasSfx.Clear();
        comboSfx.Items.Add("Pitido clásico (1 kHz)"); rutasSfx.Add("");
        foreach (string r in op.Recientes)
            if (File.Exists(r)) { comboSfx.Items.Add(Path.GetFileName(r)); rutasSfx.Add(r); }
        comboSfx.Items.Add("Sin efecto (solo silenciar)"); rutasSfx.Add("-");
        comboSfx.Items.Add("Elegir archivo…"); rutasSfx.Add("?");
        int i = rutasSfx.IndexOf(op.Sfx);
        comboSfx.SelectedIndex = i >= 0 ? i : 0;
        cargando = false;
    }

    void ElegirSfx()
    {
        string r = rutasSfx[Math.Max(0, comboSfx.SelectedIndex)];
        if (r == "?")
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Title = "Efecto para tapar las palabrotas";
                d.Filter = "Audio|*.wav;*.mp3;*.ogg;*.flac;*.m4a;*.aif;*.aiff|Todos|*.*";
                if (d.ShowDialog(this) == DialogResult.OK)
                {
                    op.Recientes.Remove(d.FileName);
                    op.Recientes.Insert(0, d.FileName);
                    while (op.Recientes.Count > 8) op.Recientes.RemoveAt(op.Recientes.Count - 1);
                    op.Sfx = d.FileName;
                }
            }
            LlenarSfx();
        }
        else op.Sfx = r;
        Actualizar();
    }

    // ------------------------------------------------------------- lista

    void Buscar()
    {
        lista = LogicaCensura.Buscar(transcripcion, palabras);
        CensuraVegas.Ubicar(vegas.Project, transcripcion, lista);
        cargando = true;
        lst.BeginUpdate();
        lst.Items.Clear();
        foreach (Coincidencia c in lista)
        {
            bool esta = c.Lugares.Count > 0;
            c.Elegida = esta;
            string quien = c.Hablante < transcripcion.Hablantes.Count ? transcripcion.Hablantes[c.Hablante].Nombre : "?";
            ListViewItem it = new ListViewItem(esta ? Formato.Tiempo(c.Lugares[0].Inicio) : "—");
            it.SubItems.Add(quien);
            it.SubItems.Add(c.Texto);
            it.SubItems.Add(esta ? c.Contexto : "(ya no está en el proyecto) " + c.Contexto);
            it.SubItems.Add(c.Prob >= 0.6 ? "sí" : c.Prob >= 0.3 ? "medio" : "dudoso");
            it.Checked = c.Elegida;
            if (!esta) it.ForeColor = Tema.TextoSuave;
            it.Tag = c;
            lst.Items.Add(it);
        }
        lst.EndUpdate();
        cargando = false;
        Actualizar();
    }

    void Escuchar()
    {
        if (lst.SelectedItems.Count == 0) return;
        Coincidencia c = (Coincidencia)lst.SelectedItems[0].Tag;
        if (c.Lugares.Count == 0) return;
        try
        {
            vegas.Transport.CursorPosition = Timecode.FromMilliseconds(Math.Max(0, c.Lugares[0].Inicio - 1.5) * 1000);
            vegas.Transport.Play();
        }
        catch { }
    }

    void Leer()
    {
        op.AntesMs = numAntes.Valor; op.DespuesMs = numDespues.Valor; op.MinimoMs = numMinimo.Valor;
        op.Tapar = (Tapar)segTapar.Seleccion;
        op.Encaje = (Encaje)segEncaje.Seleccion;
        op.Silenciar = segOriginal.Seleccion == 0;
        op.VolumenSfx = (int)desVolumen.Valor;
    }

    void Actualizar()
    {
        Leer();
        lblVolumen.Text = op.VolumenSfx + " dB";
        segEncaje.Enabled = op.Sfx != "-";
        int elegidas = 0, estan = 0;
        foreach (Coincidencia c in lista) { if (c.Lugares.Count > 0) estan++; if (c.Elegida && c.Lugares.Count > 0) elegidas++; }
        lblCuenta.Text = lista.Count == 0 ? "No se encontró ninguna palabra de la lista."
            : lista.Count + " encontradas" + (estan < lista.Count ? " · " + (lista.Count - estan) + " ya no están (se cortaron)" : "") +
              " · " + elegidas + " marcadas";
        bool algo = op.Silenciar || op.Sfx != "-";
        btnCensurar.Enabled = elegidas > 0 && algo;
        btnCensurar.Text = elegidas == 1 ? "Censurar 1" : "Censurar " + elegidas;
        lblEstado.ForeColor = algo ? Tema.TextoSuave : Tema.Silencio;
        lblEstado.Text = !algo ? "Sin efecto y sin silenciar no se haría nada."
            : "El efecto va en una pista nueva “Censura”" + (op.Silenciar ? " y la voz se silencia solo en esa pista" : "") +
              ". Ctrl+Z lo deshace todo de una vez.";
    }

    void Censurar()
    {
        Leer();
        op.Guardar();
        int n;
        try
        {
            using (UndoBlock deshacer = new UndoBlock("Censurar palabrotas"))
                n = CensuraVegas.Aplicar(vegas.Project, lista, op);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "No se pudo censurar: " + ex.Message, "Censurar palabrotas");
            return;
        }
        MessageBox.Show(this, n + (n == 1 ? " lugar censurado." : " lugares censurados.") +
            "\n\nRevisa escuchando; si algo quedó corto o largo, Ctrl+Z, ajusta Antes/Después y repite.",
            "Censurar palabrotas");
        DialogResult = DialogResult.OK;
        Close();
    }
}
