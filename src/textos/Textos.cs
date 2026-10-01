using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using ScriptPortal.Vegas;

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
                "Créalos con MomentosIA (pestaña Textos → “Crear regiones y marcadores”) o pon un marcador " +
                "con la etiqueta “TEXTO: lo que quieras que diga”.", "Textos desde marcadores");
            return;
        }
        Plantilla plantilla = GeneradorTexto.Buscar(vegas);
        string aviso = movidos > 0 ? movidos + " marcadores se reubicaron sobre su clip. " : "";
        if (perdidos > 0) aviso += perdidos + " ya no tienen clip (esa parte se borró). ";
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
            "Cada marcador “TEXTO:” se vuelve un evento de Títulos y texto con el estilo de la plantilla.");

        int y = 92;
        Texto("PLANTILLA", Tema.Pequena, Tema.TextoSuave, m, y, 80, 18);
        string desc = plantilla.Evento != null
            ? "Se copia " + plantilla.Origen + " (pista " + (plantilla.Evento.Track.Index + 1) + "): “" +
              Corto(plantilla.Texto, 40) + "”. Fuente, color, tamaño, efectos y fundidos."
            : plantilla.PlugIn != null ? plantilla.Origen + "."
            : "No se encontró Títulos y texto: selecciona un texto ya hecho y vuelve a ejecutar.";
        Etiqueta lblPlantilla = Texto(desc, Tema.Pequena, plantilla.PlugIn != null ? Tema.Texto : Tema.Silencio, m + 84, y, w - 84, 18);
        y += 20;
        Texto("Para otro estilo: selecciona un texto en la línea de tiempo antes de ejecutar." +
              (aviso.Length > 0 ? "  " + aviso : ""), Tema.Pequena, Tema.TextoSuave, m + 84, y, w - 84, 18);
        y += 30;

        lista.Columns.Add("Texto", w - 180);
        lista.Columns.Add("Inicio", 80);
        lista.Columns.Add("Dura", 76);
        Pos(lista, m, y, w, 250);
        y += 262;

        Texto("Texto del marcador elegido (Enter = otra línea; no cambia el marcador)", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 20;
        txtEditar.Multilinea = true;
        Pos(txtEditar, m, y, w, 54);
        y += 70;

        Texto("DURACIÓN", Tema.Pequena, Tema.TextoSuave, m, y, 100, 18);
        Texto("DÓNDE", Tema.Pequena, Tema.TextoSuave, m + 150, y, 100, 18);
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
        return s.Length > n ? s.Substring(0, n - 1) + "…" : s;
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

    static string Dura(TextoMarcado t) { return t.Elegido ? (t.Fin - t.Inicio).ToString("0.0") + " s" : "—"; }

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
            MessageBox.Show(this, "No se pudo crear ningún texto." + (error != null ? "\n\n" + error : ""), "Textos desde marcadores");
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
