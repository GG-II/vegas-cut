public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Transcripcion t = null;
        try
        {
            string r = Transcripcion.RutaPara(vegas.Project.FilePath);
            if (r != null && File.Exists(r))
            {
                t = Transcripcion.Cargar(r);
                if (t.TieneFuentes) t.Ubicador = PistasVegas.Ubicador(vegas.Project, t);
            }
        }
        catch { t = null; }
        if (t == null) { MessageBox.Show("Primero transcribe el proyecto (Transcribir).", "Subtítulos"); return; }
        using (VentanaSubtitulos v = new VentanaSubtitulos(vegas, t)) v.ShowDialog();
    }
}
