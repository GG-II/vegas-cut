using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;

// Cuadro de la serie: que capitulos usar como contexto, sus fichas, las
// notas de la serie y la carpeta donde buscar. Lo usan MomentosIA y
// Anteriormente; todo se guarda con el proyecto.
class DialogoSerie : VentanaBase
{
    readonly string veg, clave, modelo;
    public List<CapSerie> Caps;
    public string Notas, Carpeta;
    bool cargando, trabajando;

    Lista lst = new Lista();
    CampoTexto txtNotas = new CampoTexto();
    Etiqueta lblCarpeta, lblEstado;
    Boton btnCarpeta = new Boton("Elegir carpeta…", EstiloBoton.Secundario);
    Boton btnFichas = new Boton("Hacer las fichas que faltan", EstiloBoton.Secundario);
    Boton btnRehacer = new Boton("Rehacer la elegida", EstiloBoton.Secundario);
    Boton btnListo = new Boton("Listo", EstiloBoton.Primario);

    public DialogoSerie(string veg, string clave, string modelo) : base("Serie", 760)
    {
        this.veg = veg; this.clave = clave; this.modelo = modelo;
        StartPosition = FormStartPosition.CenterParent;
        Caps = Serie.Capitulos(veg, out Notas, out Carpeta);
        int m = Margen, w = Ancho;
        Encabezado("Serie", "Capítulos de la misma serie (por el nombre, como S01E02). Se guarda con este proyecto.");

        int y = 92;
        lblCarpeta = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 6, w - 170, 20);
        Pos(btnCarpeta, m + w - 160, y, 160, 30);
        y += 40;
        lst.Columns.Add("Capítulo", w - 330);
        lst.Columns.Add("Es", 90);
        lst.Columns.Add("Transcrito", 90);
        lst.Columns.Add("Ficha", 126 - SystemInformation.VerticalScrollBarWidth);
        Pos(lst, m, y, w, 200);
        y += 208;
        Pos(btnFichas, m, y, 230, 32);
        Pos(btnRehacer, m + 240, y, 170, 32);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m + 420, y, w - 420, 34);
        y += 44;
        Texto("Notas de la serie (personajes, apodos, lugares, de qué va)", Tema.Negrita, Tema.Texto, m, y, w, 20);
        txtNotas.Multilinea = true;
        txtNotas.Text = (Notas ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        Pos(txtNotas, m, y + 22, w, 100);
        y += 134;
        Pos(btnListo, m + w - 140, y, 140, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        lst.ItemCheck += delegate (object s, ItemCheckEventArgs e)
        {
            if (cargando) return;
            CapSerie c = (CapSerie)lst.Items[e.Index].Tag;
            if (c.Relacion == 0 || !c.Transcrito) e.NewValue = CheckState.Unchecked;
        };
        lst.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((CapSerie)e.Item.Tag).Elegido = e.Item.Checked; };
        btnCarpeta.Click += delegate { ElegirCarpeta(); };
        btnFichas.Click += delegate { Fichas(false); };
        btnRehacer.Click += delegate { Fichas(true); };
        btnListo.Click += delegate
        {
            if (trabajando) return;
            Notas = txtNotas.Text.Trim();
            Serie.Guardar(veg, Notas, Carpeta, Caps);
            DialogResult = DialogResult.OK;
            Close();
        };
        FormClosing += delegate (object s, FormClosingEventArgs e) { if (trabajando) e.Cancel = true; };
        Llenar();
    }

    void Llenar()
    {
        cargando = true;
        lst.Items.Clear();
        foreach (CapSerie c in Caps)
        {
            ListViewItem it = new ListViewItem(c.Nombre);
            it.SubItems.Add(c.Relacion < 0 ? "anterior" : c.Relacion > 0 ? "posterior" : "este");
            it.SubItems.Add(c.Transcrito ? "sí" : "no");
            it.SubItems.Add(c.TieneFicha ? "sí" : c.Transcrito ? "falta" : "—");
            it.Checked = c.Relacion != 0 && c.Transcrito && c.Elegido;
            if (c.Relacion == 0 || !c.Transcrito) it.ForeColor = Tema.TextoSuave;
            it.Tag = c;
            lst.Items.Add(it);
        }
        cargando = false;
        lblCarpeta.Text = "Busca junto a este proyecto" + (String.IsNullOrEmpty(Carpeta) ? "" : " y en " + Carpeta + " (con subcarpetas)") +
                          " · " + Caps.Count + " capítulos";
        if (Caps.Count <= 1)
            Estado("No encontré otros capítulos. Nómbralos como “S01E01 SCR.veg” o elige la carpeta donde están.", false);
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void ElegirCarpeta()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta donde están los capítulos (se busca también en sus subcarpetas)";
            if (!String.IsNullOrEmpty(Carpeta) && Directory.Exists(Carpeta)) d.SelectedPath = Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Carpeta = d.SelectedPath;
        }
        List<string> fuera = new List<string>();
        foreach (CapSerie c in Caps) if (!c.Elegido) fuera.Add(c.Nombre);
        Caps = Serie.Buscar(veg, Carpeta);
        foreach (CapSerie c in Caps) if (fuera.Contains(c.Nombre)) c.Elegido = false;
        Llenar();
    }

    // Hace con Gemini las fichas que faltan (o la del capitulo elegido).
    void Fichas(bool rehacer)
    {
        if (String.IsNullOrEmpty(clave)) { Estado("Falta la clave de Gemini: ejecuta “ConfigurarVegasCut”.", true); return; }
        List<CapSerie> cola = new List<CapSerie>();
        if (rehacer)
        {
            if (lst.SelectedItems.Count == 0) { Estado("Elige un capítulo de la lista.", true); return; }
            CapSerie c = (CapSerie)lst.SelectedItems[0].Tag;
            if (!c.Transcrito) { Estado(c.Nombre + " no está transcrito.", true); return; }
            cola.Add(c);
        }
        else
            foreach (CapSerie c in Caps) if (c.Elegido && c.Relacion != 0 && c.Transcrito && !c.TieneFicha) cola.Add(c);
        if (cola.Count == 0) { Estado("Todos los capítulos marcados ya tienen ficha.", false); return; }

        string notas = txtNotas.Text;
        trabajando = true;
        foreach (Boton b in new Boton[] { btnFichas, btnRehacer, btnCarpeta, btnListo }) b.Enabled = false;
        Thread hilo = new Thread(delegate ()
        {
            List<string> errores = new List<string>();
            for (int i = 0; i < cola.Count; i++)
            {
                CapSerie c = cola[i];
                Avisar("Haciendo la ficha de " + c.Nombre + " (" + (i + 1) + " de " + cola.Count + ")…");
                try
                {
                    Episodio e = Episodio.Abrir(c.Veg);
                    Ficha f = Ficha.Leer(Gemini.Generar(clave, modelo, Serie.InstruccionesFicha(), Serie.MensajeFicha(e, notas), true));
                    f.Generada = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · " + modelo;
                    f.Guardar(c.Veg);
                }
                catch (Exception ex) { errores.Add(c.Nombre + ": " + ex.Message); }
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    foreach (Boton b in new Boton[] { btnFichas, btnRehacer, btnCarpeta, btnListo }) b.Enabled = true;
                    Llenar();
                    if (errores.Count > 0) Estado(String.Join("\n", errores.ToArray()), true);
                    else Estado("✔ Fichas listas.", false);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Avisar(string t)
    {
        try { BeginInvoke((MethodInvoker)delegate { Estado(t, false); }); } catch { }
    }

    // "2 anteriores (1 sin ficha) · 1 posterior"
    public static string Resumen(List<CapSerie> caps)
    {
        int ant = 0, pos = 0, sin = 0;
        foreach (CapSerie c in caps)
        {
            if (c.Relacion == 0 || !c.Elegido || !c.Transcrito) continue;
            if (c.Relacion < 0) ant++; else pos++;
            if (!c.TieneFicha) sin++;
        }
        if (ant + pos == 0) return "Sin capítulos de la serie.";
        return "Serie: " + ant + (ant == 1 ? " anterior" : " anteriores") +
               (pos > 0 ? " · " + pos + (pos == 1 ? " posterior" : " posteriores") : "") +
               (sin > 0 ? " · " + sin + " sin ficha" : "");
    }
}
