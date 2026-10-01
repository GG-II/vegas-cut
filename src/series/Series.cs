using System;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Configuracion config = Configuracion.Cargar();
        using (VentanaSeries v = new VentanaSeries(vegas.Project.FilePath, config.GeminiClave, config.GeminiModelo, false))
            v.ShowDialog();
    }
}
