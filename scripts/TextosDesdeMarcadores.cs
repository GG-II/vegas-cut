// TextosDesdeMarcadores.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Convierte los marcadores "TEXTO: ..." (los que crea MomentosIA o los que
// pongas tu) en eventos de Titulos y texto con el estilo de una plantilla:
// selecciona antes un texto ya hecho (fuente, color, tamano, animacion) y
// cada marcador se vuelve una copia con su propio texto. Antes de empezar
// reubica los marcadores anclados, por si moviste o recortaste clips.
//
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/textos/Textos.cs ----

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Project p = vegas.Project;
        // Primero los marcadores vuelven a su clip, por si se edito despues
        // de MomentosIA.
        int movidos = 0, perdidos = 0, revisados = 0;
        if (!String.IsNullOrEmpty(p.FilePath) && Anclas.Cargar(p.FilePath).Count > 0)
            using (UndoBlock deshacer = new UndoBlock("Reubicar marcadores"))
                movidos = Anclas.Reubicar(p, p.FilePath, out perdidos, out revisados);

        List<TextoMarcado> textos = LogicaTextos.Leer(p);
        if (textos.Count == 0)
        {
            MessageBox.Show("No hay marcadores de texto en el proyecto.\n\n" +
                "Cr\u00e9alos con MomentosIA (pesta\u00f1a Textos \u2192 \u201cCrear regiones y marcadores\u201d) o pon un marcador " +
                "con la etiqueta \u201cTEXTO: lo que quieras que diga\u201d.", "Textos desde marcadores");
            return;
        }
        Plantilla plantilla = GeneradorTexto.Buscar(vegas);
        string aviso = movidos > 0 ? movidos + " marcadores se reubicaron sobre su clip. " : "";
        if (perdidos > 0) aviso += perdidos + " ya no tienen clip (esa parte se borr\u00f3). ";
        using (VentanaTextos v = new VentanaTextos(vegas, textos, plantilla, aviso)) v.ShowDialog();
    }
}

public class TextoMarcado
{
    public double Inicio, Fin;
    public string Texto = "", Etiqueta = "";
    public bool Elegido = true;
}

public static class LogicaTextos
{
    public const string Prefijo = "TEXTO:";

    // Texto del marcador, o null si no es un marcador de texto.
    public static string Limpiar(string etiqueta)
    {
        string e = (etiqueta ?? "").Trim();
        if (!e.StartsWith(Prefijo, StringComparison.OrdinalIgnoreCase)) return null;
        return e.Substring(Prefijo.Length).Trim();
    }

    public static List<TextoMarcado> Leer(Project p)
    {
        List<TextoMarcado> r = new List<TextoMarcado>();
        foreach (Marker m in p.Markers)
        {
            string t = Limpiar(m.Label);
            if (String.IsNullOrEmpty(t)) continue;
            TextoMarcado x = new TextoMarcado();
            x.Inicio = m.Position.ToMilliseconds() / 1000.0;
            x.Texto = t;
            x.Etiqueta = m.Label;
            r.Add(x);
        }
        r.Sort(delegate (TextoMarcado a, TextoMarcado b) { return a.Inicio.CompareTo(b.Inicio); });
        return r;
    }

    // Cada texto dura "duracion", pero termina antes si el siguiente empieza
    // (para que no se encimen) o si se acaba el proyecto.
    public static void Duraciones(List<TextoMarcado> textos, double duracion, double finProyecto)
    {
        TextoMarcado anterior = null;
        foreach (TextoMarcado t in textos)
        {
            if (!t.Elegido) continue;
            t.Fin = t.Inicio + duracion;
            if (finProyecto > t.Inicio + 0.5) t.Fin = Math.Min(t.Fin, finProyecto);
            if (anterior != null && anterior.Fin > t.Inicio - 0.1)
                anterior.Fin = Math.Max(anterior.Inicio + 0.5, t.Inicio - 0.1);
            anterior = t;
        }
    }

    public static int Elegidos(List<TextoMarcado> textos)
    {
        int n = 0;
        foreach (TextoMarcado t in textos) if (t.Elegido) n++;
        return n;
    }
}

class VentanaTextos : VentanaBase
{
    readonly Vegas vegas;
    readonly List<TextoMarcado> textos;
    readonly Plantilla plantilla;
    bool cargando;

    Lista lista = new Lista();
    CampoTexto txtEditar = new CampoTexto();
    CampoNumero numDuracion = new CampoNumero();
    Segmentado segPista = new Segmentado(new string[] { "Pista nueva arriba", "Pista de la plantilla" });
    Boton chipQuitar = new Boton("Quitar los marcadores usados", EstiloBoton.Chip);
    Boton btnCrear = new Boton("Crear textos", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaTextos(Vegas vegas, List<TextoMarcado> textos, Plantilla plantilla, string aviso)
        : base("Textos desde marcadores", 820)
    {
        this.vegas = vegas;
        this.textos = textos;
        this.plantilla = plantilla;
        int m = Margen, w = Ancho;
        Encabezado("Textos desde marcadores",
            "Cada marcador \u201cTEXTO:\u201d se vuelve un evento de T\u00edtulos y texto con el estilo de la plantilla.");

        int y = 92;
        Texto("PLANTILLA", Tema.Pequena, Tema.TextoSuave, m, y, 80, 18);
        string desc = plantilla.Evento != null
            ? "Se copia " + plantilla.Origen + " (pista " + (plantilla.Evento.Track.Index + 1) + "): \u201c" +
              Corto(plantilla.Texto, 40) + "\u201d. Fuente, color, tama\u00f1o, efectos y fundidos."
            : plantilla.PlugIn != null ? plantilla.Origen + "."
            : "No se encontr\u00f3 T\u00edtulos y texto: selecciona un texto ya hecho y vuelve a ejecutar.";
        Etiqueta lblPlantilla = Texto(desc, Tema.Pequena, plantilla.PlugIn != null ? Tema.Texto : Tema.Silencio, m + 84, y, w - 84, 18);
        y += 20;
        Texto("Para otro estilo: selecciona un texto en la l\u00ednea de tiempo antes de ejecutar." +
              (aviso.Length > 0 ? "  " + aviso : ""), Tema.Pequena, Tema.TextoSuave, m + 84, y, w - 84, 18);
        y += 30;

        lista.Columns.Add("Texto", w - 180);
        lista.Columns.Add("Inicio", 80);
        lista.Columns.Add("Dura", 76);
        Pos(lista, m, y, w, 250);
        y += 262;

        Texto("Texto del marcador elegido (Enter = otra l\u00ednea; no cambia el marcador)", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 20;
        txtEditar.Multilinea = true;
        Pos(txtEditar, m, y, w, 54);
        y += 70;

        Texto("DURACI\u00d3N", Tema.Pequena, Tema.TextoSuave, m, y, 100, 18);
        Texto("D\u00d3NDE", Tema.Pequena, Tema.TextoSuave, m + 150, y, 100, 18);
        y += 20;
        numDuracion.Sufijo = "s";
        numDuracion.Minimo = 1; numDuracion.Maximo = 30; numDuracion.Paso = 1;
        double durPlantilla = plantilla.Evento != null ? plantilla.Evento.Length.ToMilliseconds() / 1000.0 : 3;
        numDuracion.Valor = (int)Math.Max(1, Math.Min(10, Math.Round(durPlantilla)));
        Pos(numDuracion, m, y, 130, 34);
        segPista.Seleccion = 0;
        segPista.Habilitar(1, plantilla.Evento != null);
        Pos(segPista, m + 150, y, 330, 34);
        chipQuitar.Activo = true;
        Pos(chipQuitar, m + 500, y + 3, 230, 28);
        y += 52;

        Pos(btnCerrar, m + w - 330, y, 110, 40);
        Pos(btnCrear, m + w - 210, y, 210, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        Llenar();
        lista.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
        {
            if (cargando) return;
            ((TextoMarcado)e.Item.Tag).Elegido = e.Item.Checked;
            Recalcular();
        };
        lista.SelectedIndexChanged += delegate
        {
            TextoMarcado t = Elegido();
            cargando = true;
            txtEditar.Text = t == null ? "" : t.Texto.Replace("\n", "\r\n");
            txtEditar.Enabled = t != null;
            cargando = false;
        };
        txtEditar.Caja.TextChanged += delegate
        {
            TextoMarcado t = Elegido();
            if (cargando || t == null) return;
            t.Texto = txtEditar.Text.Replace("\r\n", "\n").Trim();
            lista.SelectedItems[0].Text = t.Texto.Replace("\n", " / ");
        };
        numDuracion.Cambio += delegate { Recalcular(); };
        chipQuitar.Click += delegate { chipQuitar.Activo = !chipQuitar.Activo; };
        btnCerrar.Click += delegate { Close(); };
        btnCrear.Click += delegate { Crear(); };
        if (lista.Items.Count > 0) lista.Items[0].Selected = true;
    }

    static string Corto(string s, int n)
    {
        s = (s ?? "").Replace("\n", " / ");
        return s.Length > n ? s.Substring(0, n - 1) + "\u2026" : s;
    }

    TextoMarcado Elegido()
    {
        return lista.SelectedItems.Count == 0 ? null : (TextoMarcado)lista.SelectedItems[0].Tag;
    }

    double FinProyecto { get { return vegas.Project.Length.ToMilliseconds() / 1000.0; } }

    void Llenar()
    {
        cargando = true;
        LogicaTextos.Duraciones(textos, numDuracion.Valor, FinProyecto);
        foreach (TextoMarcado t in textos)
        {
            ListViewItem it = new ListViewItem(t.Texto);
            it.SubItems.Add(Formato.Tiempo(t.Inicio));
            it.SubItems.Add(Dura(t));
            it.Checked = t.Elegido;
            it.Tag = t;
            lista.Items.Add(it);
        }
        cargando = false;
        Recalcular();
    }

    static string Dura(TextoMarcado t) { return t.Elegido ? (t.Fin - t.Inicio).ToString("0.0") + " s" : "\u2014"; }

    void Recalcular()
    {
        LogicaTextos.Duraciones(textos, numDuracion.Valor, FinProyecto);
        foreach (ListViewItem it in lista.Items) it.SubItems[2].Text = Dura((TextoMarcado)it.Tag);
        int n = LogicaTextos.Elegidos(textos);
        btnCrear.Text = n == 1 ? "Crear 1 texto" : "Crear " + n + " textos";
        btnCrear.Enabled = n > 0 && plantilla.PlugIn != null;
    }

    void Crear()
    {
        Project p = vegas.Project;
        int hechos = 0;
        string error = null;
        using (UndoBlock deshacer = new UndoBlock("Textos desde marcadores"))
        {
            VideoTrack pista = null;
            if (segPista.Seleccion == 1 && plantilla.Evento != null)
                foreach (Track t in p.Tracks)
                    if (t.Index == plantilla.Evento.Track.Index) pista = t as VideoTrack;
            if (pista == null)
            {
                pista = new VideoTrack(0, "Textos");
                p.Tracks.Add(pista);
            }
            foreach (TextoMarcado t in textos)
            {
                if (!t.Elegido || t.Texto.Length == 0) continue;
                try
                {
                    GeneradorTexto.Crear(pista, plantilla, t.Inicio, t.Fin - t.Inicio, t.Texto);
                    hechos++;
                    if (chipQuitar.Activo) QuitarMarcador(p, t);
                }
                catch (Exception ex) { if (error == null) error = ex.Message; }
            }
        }
        if (hechos == 0)
        {
            MessageBox.Show(this, "No se pudo crear ning\u00fan texto." + (error != null ? "\n\n" + error : ""), "Textos desde marcadores");
            return;
        }
        MessageBox.Show(this, hechos + (hechos == 1 ? " texto creado." : " textos creados.") +
            (error != null ? "\nAlgunos fallaron: " + error : "") + "\n\nCtrl+Z lo deshace todo de una vez.",
            "Textos desde marcadores");
        DialogResult = DialogResult.OK;
        Close();
    }

    static void QuitarMarcador(Project p, TextoMarcado t)
    {
        Marker quitar = null;
        foreach (Marker m in p.Markers)
            if (m.Label == t.Etiqueta && Math.Abs(m.Position.ToMilliseconds() / 1000.0 - t.Inicio) < 0.002) { quitar = m; break; }
        if (quitar != null) p.Markers.Remove(quitar);
    }
}

// ---- src/textos/Generador.cs ----

// =====================================================================
// Eventos de "Titulos y texto" a partir de una plantilla
//
// Cada evento de texto es un medio generado con sus propios parametros
// (OFX). Para copiar el estilo se crea un medio nuevo con el mismo generador
// y se le pasan, uno por uno, los valores de la plantilla; despues se cambia
// solo el texto (RTF), conservando su formato.
// =====================================================================

public class Plantilla
{
    public VideoEvent Evento;     // null: Titulos y texto con el estilo por defecto
    public PlugInNode PlugIn;
    public string TextoRtf = "";
    public string Origen = "";    // como se encontro, para mostrarlo
    public string Texto { get { return Rtf.TextoPlano(TextoRtf); } }
}

public static class GeneradorTexto
{
    static readonly string[] Ids = { "{Svfx:com.vegascreativesoftware:titlesandtext}",
                                     "{Svfx:com.sonycreativesoftware:titlesandtext}" };

    static OFXEffect Ofx(Effect e)
    {
        try { return e != null && e.IsOFX ? e.OFXEffect : null; } catch { return null; }
    }

    static OFXStringParameter ParametroTexto(Media m)
    {
        try
        {
            if (m == null || !m.IsGenerated()) return null;
            OFXEffect o = Ofx(m.Generator);
            return o == null ? null : o.FindParameterByName("Text") as OFXStringParameter;
        }
        catch { return null; }
    }

    static Media MediaDe(TrackEvent e)
    {
        return e == null || e.ActiveTake == null ? null : e.ActiveTake.Media;
    }

    public static bool EsTexto(TrackEvent e) { return e is VideoEvent && ParametroTexto(MediaDe(e)) != null; }

    // Plantilla: el texto seleccionado; si no hay, el texto mas cercano al
    // cursor; si no hay ninguno, Titulos y texto con su estilo normal.
    public static Plantilla Buscar(Vegas vegas)
    {
        double cursor = vegas.Transport.CursorPosition.ToMilliseconds() / 1000.0;
        VideoEvent elegido = null, cercano = null;
        double mejor = double.MaxValue;
        foreach (Track t in vegas.Project.Tracks)
        {
            if (t.IsAudio()) continue;
            foreach (TrackEvent e in t.Events)
            {
                if (!EsTexto(e)) continue;
                if (e.Selected && elegido == null) elegido = (VideoEvent)e;
                double d = Math.Abs(e.Start.ToMilliseconds() / 1000.0 - cursor);
                if (d < mejor) { mejor = d; cercano = (VideoEvent)e; }
            }
        }
        Plantilla p = new Plantilla();
        p.Evento = elegido ?? cercano;
        if (p.Evento != null)
        {
            p.Origen = elegido != null ? "el texto seleccionado" : "el texto m\u00e1s cercano al cursor";
            p.PlugIn = MediaDe(p.Evento).Generator.PlugIn;
            p.TextoRtf = ParametroTexto(MediaDe(p.Evento)).Value ?? "";
            return p;
        }
        p.Origen = "T\u00edtulos y texto con su estilo normal (no hay ning\u00fan texto en el proyecto)";
        foreach (string id in Ids)
        {
            try { p.PlugIn = vegas.Generators.GetChildByUniqueID(id); } catch { }
            if (p.PlugIn != null) break;
        }
        return p;
    }

    public static int PistaDe(Plantilla p) { return p.Evento == null ? -1 : p.Evento.Track.Index; }

    // Crea el evento de texto en la pista, de inicio a inicio+duracion.
    public static VideoEvent Crear(VideoTrack pista, Plantilla p, double inicio, double duracion, string texto)
    {
        if (p.PlugIn == null) throw new Exception("No se encontr\u00f3 el generador de T\u00edtulos y texto.");
        Media media = new Media(p.PlugIn);
        MediaStream flujo = media.Streams.GetItemByMediaType(MediaType.Video, 0);
        VideoEvent ev = pista.AddVideoEvent(Timecode.FromMilliseconds(inicio * 1000), Timecode.FromMilliseconds(duracion * 1000));
        ev.AddTake(flujo);

        OFXEffect nuevo = Ofx(media.Generator);
        if (p.Evento != null)
        {
            Media origen = MediaDe(p.Evento);
            CopiarParametros(Ofx(origen.Generator), nuevo);
            try { ev.FadeIn.Length = p.Evento.FadeIn.Length; ev.FadeOut.Length = p.Evento.FadeOut.Length; } catch { }
            CopiarEfectos(p.Evento, ev);
        }
        OFXStringParameter txt = ParametroTexto(media);
        if (txt != null) txt.Value = Rtf.ReemplazarTexto(p.TextoRtf, texto);
        if (nuevo != null) try { nuevo.AllParametersChanged(); } catch { }
        return ev;
    }

    // Efectos del evento (sombra, borde, movimiento...) con sus valores.
    static void CopiarEfectos(VideoEvent de, VideoEvent a)
    {
        try
        {
            foreach (Effect fx in de.Effects)
            {
                try
                {
                    Effect copia = new Effect(fx.PlugIn);
                    a.Effects.Add(copia);
                    copia.Bypass = fx.Bypass;
                    CopiarParametros(Ofx(fx), Ofx(copia));
                    OFXEffect o = Ofx(copia);
                    if (o != null) o.AllParametersChanged();
                }
                catch { }
            }
        }
        catch { }
    }

    // Copia cada parametro por nombre (por reflexion: cada tipo de parametro
    // tiene su propio Value). Si esta animado, tambien sus fotogramas clave.
    public static int CopiarParametros(OFXEffect de, OFXEffect a)
    {
        if (de == null || a == null) return 0;
        int n = 0;
        foreach (OFXParameter p in de.Parameters)
        {
            if (p.Name == "Text") continue;
            OFXParameter q;
            try { q = a.FindParameterByName(p.Name); } catch { continue; }
            if (q == null || q.GetType() != p.GetType()) continue;
            try
            {
                PropertyInfo valor = p.GetType().GetProperty("Value");
                if (valor == null || !valor.CanWrite) continue;
                valor.SetValue(q, valor.GetValue(p, null), null);
                n++;
                CopiarClaves(p, q);
            }
            catch { }
        }
        return n;
    }

    static void CopiarClaves(OFXParameter de, OFXParameter a)
    {
        Type t = de.GetType();
        PropertyInfo animado = t.GetProperty("IsAnimated");
        if (animado == null || !(bool)animado.GetValue(de, null)) return;
        PropertyInfo claves = t.GetProperty("Keyframes");
        MethodInfo poner = t.GetMethod("SetValueAtTime");
        if (claves == null || poner == null) return;
        if (animado.CanWrite) animado.SetValue(a, true, null);
        foreach (object k in (IEnumerable)claves.GetValue(de, null))
        {
            object tiempo = k.GetType().GetProperty("Time").GetValue(k, null);
            object v = k.GetType().GetProperty("Value").GetValue(k, null);
            poner.Invoke(a, new object[] { tiempo, v });
        }
    }
}

// ---- src/comun/Rtf.cs ----

// =====================================================================
// Texto enriquecido (RTF) de los eventos de Titulos y texto
//
// Vegas guarda el texto de "Titles & Text" como RTF: fuente, tamano, color y
// alineacion van como comandos (\f0\fs48\cf1...) antes del texto. Para usar
// un texto como plantilla se conserva todo lo que hay antes del primer
// caracter visible y despues del ultimo, y solo se cambia lo de en medio.
// =====================================================================

public static class Rtf
{
    // Grupos que no son texto visible: tablas de fuentes, colores, estilos...
    static readonly string[] Destinos = { "fonttbl", "colortbl", "stylesheet", "info", "generator", "pict",
                                          "header", "footer", "listtable", "listoverridetable", "themedata",
                                          "colorschememapping", "latentstyles", "datastore", "rsidtbl", "xmlnstbl" };

    // Una pieza del RTF: texto visible (Texto != null) o comando.
    class Pieza
    {
        public int Desde, Hasta;
        public string Texto;      // caracteres visibles que aporta
        public string Comando;    // nombre del comando (sin \)
    }

    static List<Pieza> Piezas(string rtf)
    {
        List<Pieza> r = new List<Pieza>();
        Stack<bool> pila = new Stack<bool>();
        bool oculto = false, inicioGrupo = false;
        int uc = 1, i = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            if (c == '{') { pila.Push(oculto); inicioGrupo = true; i++; continue; }
            if (c == '}') { if (pila.Count > 0) oculto = pila.Pop(); inicioGrupo = false; i++; continue; }
            if (c == '\r' || c == '\n') { i++; continue; }
            Pieza p = new Pieza();
            p.Desde = i;
            if (c != '\\')
            {
                p.Texto = c.ToString();
                i++;
            }
            else if (i + 1 < rtf.Length && char.IsLetter(rtf[i + 1]))
            {
                int j = i + 1;
                while (j < rtf.Length && char.IsLetter(rtf[j])) j++;
                string nombre = rtf.Substring(i + 1, j - i - 1);
                int k = j;
                if (k < rtf.Length && (rtf[k] == '-' || char.IsDigit(rtf[k]))) { k++; while (k < rtf.Length && char.IsDigit(rtf[k])) k++; }
                string num = rtf.Substring(j, k - j);
                if (k < rtf.Length && rtf[k] == ' ') k++;
                i = k;
                p.Comando = nombre;
                int n;
                bool hayNum = int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                if (nombre == "uc" && hayNum) uc = n;
                if (nombre == "u" && hayNum)
                {
                    p.Texto = ((char)(n < 0 ? n + 65536 : n)).ToString();
                    p.Comando = null;
                    // Se salta el caracter de respaldo que sigue a \uN.
                    for (int s = 0; s < uc && i < rtf.Length; s++)
                    {
                        if (rtf[i] == '\\' && i + 3 < rtf.Length && rtf[i + 1] == '\'') i += 4;
                        else if (rtf[i] == '{' || rtf[i] == '}' || rtf[i] == '\\') break;
                        else i++;
                    }
                }
                else if (nombre == "par" || nombre == "line") p.Texto = "\n";
                else if (nombre == "tab") p.Texto = "\t";
                if (inicioGrupo && Array.IndexOf(Destinos, nombre) >= 0) oculto = true;
            }
            else if (i + 1 < rtf.Length)
            {
                char s = rtf[i + 1];
                if (s == '\'' && i + 3 < rtf.Length)
                {
                    int b;
                    if (int.TryParse(rtf.Substring(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                        p.Texto = Ansi(b);
                    i += 4;
                }
                else
                {
                    if (s == '*' && inicioGrupo) oculto = true;
                    if (s == '\\' || s == '{' || s == '}') p.Texto = s.ToString();
                    else if (s == '~') p.Texto = "\u00a0";
                    else p.Comando = s.ToString();
                    i += 2;
                }
            }
            else i++;
            p.Hasta = i;
            inicioGrupo = false;
            if (!oculto) r.Add(p);
        }
        return r;
    }

    static string Ansi(int b)
    {
        if (b < 128) return ((char)b).ToString();
        try { return Encoding.GetEncoding(1252).GetString(new byte[] { (byte)b }); }
        catch { return ((char)b).ToString(); }
    }

    public static bool EsRtf(string s) { return s != null && s.TrimStart().StartsWith("{\\rtf"); }

    // Texto visible, con saltos de linea en \par.
    public static string TextoPlano(string rtf)
    {
        if (!EsRtf(rtf)) return rtf ?? "";
        StringBuilder sb = new StringBuilder();
        foreach (Pieza p in Piezas(rtf)) if (p.Texto != null) sb.Append(p.Texto);
        return sb.ToString().TrimEnd('\n', '\r', ' ');
    }

    // Cambia el texto visible conservando el formato de la plantilla (el del
    // primer caracter). Los saltos de linea del texto nuevo se vuelven \par.
    public static string ReemplazarTexto(string rtf, string nuevo)
    {
        if (!EsRtf(rtf)) return Simple(nuevo);
        List<Pieza> piezas = Piezas(rtf);
        int primera = -1, ultima = -1;
        for (int k = 0; k < piezas.Count; k++)
        {
            Pieza p = piezas[k];
            if (p.Texto == null || p.Texto == "\n") continue;
            if (primera < 0) primera = k;
            ultima = k;
        }
        if (primera < 0)
        {
            // Plantilla sin texto: se pone antes del ultimo \par o del cierre.
            int fin = rtf.LastIndexOf('}');
            for (int k = piezas.Count - 1; k >= 0; k--)
                if (piezas[k].Comando == "par") { fin = piezas[k].Desde; break; }
            if (fin < 0) return Simple(nuevo);
            return rtf.Substring(0, fin) + Escapar(nuevo) + rtf.Substring(fin);
        }
        int desde = piezas[primera].Desde, hasta = piezas[ultima].Hasta;
        string medio = Escapar(nuevo);
        return rtf.Substring(0, desde) + medio + rtf.Substring(hasta);
    }

    public static string Escapar(string texto)
    {
        StringBuilder sb = new StringBuilder();
        string t = (texto ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\par ");
            else if (c == '\t') sb.Append("\\tab ");
            else if (c < 128) sb.Append(c);
            else sb.Append("\\u").Append(((int)(short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
        }
        return sb.ToString();
    }

    // RTF basico centrado, por si la plantilla no trae texto enriquecido.
    public static string Simple(string texto)
    {
        return "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil Arial;}}\\uc1\\pard\\qc\\f0\\fs48 " +
               Escapar(texto) + "\\par\n}";
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

    // El valor tal cual (o null): para booleanos o lo que pueda faltar.
    public static object Valor(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        return d != null && d.TryGetValue(clave, out v) ? v : null;
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

    public DialogoNombre(string sugerido) : this(sugerido, "Guardar perfil", "Nombre del perfil") { }

    public DialogoNombre(string sugerido, string titulo, string etiqueta)
    {
        Text = titulo;
        ClientSize = new Size(380, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;

        Controls.Add(Pos(new Etiqueta(etiqueta, Tema.Seccion, Tema.Texto), 20, 16, 340, 22));
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

// Barra de progreso redondeada.
class BarraProgreso : ControlBase
{
    double valor;
    public double Valor { get { return valor; } set { valor = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        if (valor > 0)
            using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Math.Max(Height, (float)(Width - 1) * (float)valor), Height - 1), Height / 2f))
            using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
    }
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
