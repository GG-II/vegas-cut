using System;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// Abre la ventana de MomentosIA para el proyecto (la usan MomentosIA y
// PrepararEpisodio). Con "pedir", le pide a Gemini apenas se abre.
public static class AbrirMomentos
{
    public static void Abrir(Vegas vegas, bool pedir)
    {
        string veg = vegas.Project.FilePath;
        string ruta = Transcripcion.RutaPara(veg);
        if (ruta == null || !File.Exists(ruta))
        {
            MessageBox.Show("Este proyecto aún no tiene transcripción.\n\nEjecuta primero “Transcribir”.", "Momentos con IA");
            return;
        }
        Transcripcion t;
        try { t = Transcripcion.Cargar(ruta); }
        catch (Exception ex) { MessageBox.Show("No se pudo leer la transcripción: " + ex.Message, "Momentos con IA"); return; }

        // Con las fuentes, la transcripcion sigue tambien las ediciones a mano.
        if (t.TieneFuentes) t.Ubicador = PistasVegas.Ubicador(vegas.Project, t);
        using (VentanaMomentos v = new VentanaMomentos(vegas, t, ruta))
        {
            v.PedirAlAbrir = pedir;
            v.ShowDialog();
        }
    }
}
