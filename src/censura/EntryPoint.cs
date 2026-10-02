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
