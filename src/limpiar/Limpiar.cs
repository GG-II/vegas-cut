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
        using (VentanaLimpiar v = new VentanaLimpiar(vegas)) v.ShowDialog();
    }
}

class VentanaLimpiar : VentanaBase
{
    readonly Vegas vegas;
    string carpeta = "";
    List<ArchivoLimpiar> lista = new List<ArchivoLimpiar>();
    bool cargando;

    Etiqueta lblCarpeta, lblTotal, lblEstado;
    Boton btnCarpeta = new Boton("Elegir carpeta…", EstiloBoton.Secundario);
    Boton btnBuscar = new Boton("Buscar", EstiloBoton.Secundario);
    Lista lst = new Lista();
    Boton btnLimpiar = new Boton("Mandar a la Papelera", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    public VentanaLimpiar(Vegas vegas) : base("Limpiar", 1000)
    {
        this.vegas = vegas;
        int m = Margen, w = Ancho;
        Encabezado("Limpiar", "Lo que se junta al trabajar y ya no usa ningún proyecto de la carpeta. Todo va a la Papelera de reciclaje.");
        int y = 92;
        lblCarpeta = Texto("", Tema.Normal, Tema.Texto, m, y + 6, w - 300, 20);
        Pos(btnCarpeta, m + w - 290, y, 150, 32);
        Pos(btnBuscar, m + w - 130, y, 130, 32);
        y += 44;
        int sb = SystemInformation.VerticalScrollBarWidth + 4;
        lst.Columns.Add("Qué", 170);
        lst.Columns.Add("Archivo", w - 170 - 80 - 300 - sb);
        lst.Columns.Add("Tamaño", 80);
        lst.Columns.Add("Por qué", 300);
        Pos(lst, m, y, w, 400);
        y += 408;
        lblTotal = Texto("", Tema.Normal, Tema.Texto, m, y, w, 20);
        y += 28;
        lblEstado = Texto("Lo que algún proyecto todavía usa no aparece. Proxies y autoguardados vienen sin marcar.", Tema.Pequena, Tema.TextoSuave, m, y, w - 360, 40);
        Pos(btnLimpiar, m + w - 350, y, 200, 40);
        Pos(btnCerrar, m + w - 140, y, 140, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        // La carpeta de la serie del proyecto abierto; si no, la del proyecto.
        string veg = vegas.Project.FilePath ?? "";
        try
        {
            SerieProyecto s;
            Serie.DelProyecto(veg, out s);
            if (s != null && s.Carpeta.Length > 0 && Directory.Exists(s.Carpeta)) carpeta = s.Carpeta;
        }
        catch { }
        if (carpeta.Length == 0 && veg.Length > 0) carpeta = Path.GetDirectoryName(veg);

        btnCarpeta.Click += delegate
        {
            using (FolderBrowserDialog d = new FolderBrowserDialog())
            {
                d.Description = "Carpeta de la serie o de los capítulos (se revisan las subcarpetas)";
                if (carpeta.Length > 0 && Directory.Exists(carpeta)) d.SelectedPath = carpeta;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                carpeta = d.SelectedPath;
            }
            Buscar();
        };
        btnBuscar.Click += delegate { Buscar(); };
        btnLimpiar.Click += delegate { Limpiar(); };
        btnCerrar.Click += delegate { Close(); };
        lst.ItemChecked += delegate (object s, ItemCheckedEventArgs e)
        {
            if (cargando || e.Item.Tag == null) return;
            ((ArchivoLimpiar)e.Item.Tag).Marcado = e.Item.Checked;
            Total();
        };
        Shown += delegate { Buscar(); };
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    List<string> MediosAbiertos()
    {
        List<string> r = new List<string>();
        try { foreach (Media m in vegas.Project.MediaPool) if (!String.IsNullOrEmpty(m.FilePath)) r.Add(m.FilePath); } catch { }
        return r;
    }

    void Buscar()
    {
        lblCarpeta.Text = carpeta.Length > 0 ? "Carpeta: " + carpeta : "Elige la carpeta de la serie o de los capítulos.";
        Cursor = Cursors.WaitCursor;
        lista = LogicaLimpiar.TemporalesHuerfanos(Path.GetTempPath(), DateTime.Now);
        string refs = "";
        List<string> abiertos = MediosAbiertos();
        if (carpeta.Length > 0) lista.AddRange(LogicaLimpiar.Buscar(carpeta, abiertos, DateTime.Now, out refs));
        Cursor = Cursors.Default;

        // Seguridad: si los medios del proyecto abierto (ya guardado y dentro de la carpeta) no
        // aparecen en las referencias, no se pudieron leer los .veg: no se ofrece nada de la carpeta.
        string veg = vegas.Project.FilePath ?? "";
        bool dentro = veg.Length > 0 && carpeta.Length > 0 && veg.StartsWith(carpeta, StringComparison.OrdinalIgnoreCase) && File.Exists(veg);
        List<string> guardados = new List<string>();
        foreach (string a in abiertos) if (File.Exists(a) && !a.Contains(".vegascut-narracion")) guardados.Add(a);
        if (refs == null || (dentro && guardados.Count > 0 && !LogicaLimpiar.Legible(refs, guardados.GetRange(0, Math.Min(5, guardados.Count)))))
        {
            lista.RemoveAll(delegate (ArchivoLimpiar a) { return a.Tipo != LogicaLimpiar.Temporales; });
            Estado("No pude leer qué archivos usan los proyectos de esta carpeta, así que solo ofrezco los temporales. Guarda el proyecto y vuelve a buscar.", true);
        }
        Llenar();
    }

    void Llenar()
    {
        cargando = true;
        lst.Items.Clear();
        foreach (ArchivoLimpiar a in lista)
        {
            string ruta = carpeta.Length > 0 && a.Ruta.StartsWith(carpeta, StringComparison.OrdinalIgnoreCase) ? a.Ruta.Substring(carpeta.Length).TrimStart('\\', '/') : a.Ruta;
            ListViewItem it = new ListViewItem(a.Tipo);
            it.SubItems.Add(ruta + (a.Carpeta ? " (carpeta)" : ""));
            it.SubItems.Add(LogicaLimpiar.Bytes(a.Bytes));
            it.SubItems.Add(a.PorQue);
            it.Checked = a.Marcado;
            it.Tag = a;
            lst.Items.Add(it);
        }
        cargando = false;
        Total();
        if (lista.Count == 0) Estado("No hay nada que limpiar.", false);
    }

    void Total()
    {
        long b = 0;
        int n = 0;
        foreach (ArchivoLimpiar a in lista) if (a.Marcado) { b += a.Bytes; n++; }
        lblTotal.Text = n == 0 ? "Nada marcado." : n + " elementos marcados · " + LogicaLimpiar.Bytes(b);
        btnLimpiar.Enabled = n > 0;
    }

    void Limpiar()
    {
        int marcados = lista.FindAll(delegate (ArchivoLimpiar a) { return a.Marcado; }).Count;
        if (MessageBox.Show(this, "Mandar " + marcados + " elementos a la Papelera de reciclaje?\n\nSe pueden recuperar desde la Papelera mientras no la vacíes.",
                            "Limpiar", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        List<string> errores = new List<string>();
        long liberado;
        int n = LogicaLimpiar.Limpiar(lista, LogicaLimpiar.Papelera, errores, out liberado);
        Buscar();
        Estado("✔ " + n + " a la Papelera (" + LogicaLimpiar.Bytes(liberado) + ")." + (errores.Count > 0 ? " No se pudo: " + String.Join("; ", errores.ToArray()) : ""), errores.Count > 0);
    }
}
