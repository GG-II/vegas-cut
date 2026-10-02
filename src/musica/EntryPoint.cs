using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        List<InfoPista> pistas = PistasVegas.Listar(vegas.Project);
        if (pistas.Count < 2)
        {
            MessageBox.Show("Hace falta al menos una pista con voz y otra con música.", "Música que baja sola");
            return;
        }
        using (VentanaMusica v = new VentanaMusica(vegas, pistas)) v.ShowDialog();
    }
}
