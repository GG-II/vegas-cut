using System;
using System.Collections.Generic;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    Vegas vegas;
    List<InfoPista> pistas;

    public void FromVegas(Vegas v)
    {
        vegas = v;
        Project proyecto = vegas.Project;
        pistas = PistasVegas.Listar(proyecto);
        if (pistas.Count == 0)
        {
            MessageBox.Show("El proyecto no tiene pistas de audio.", "Quitar silencios");
            return;
        }

        string[] nombres = new string[pistas.Count], detalles = new string[pistas.Count];
        for (int i = 0; i < pistas.Count; i++) { nombres[i] = pistas[i].Nombre; detalles[i] = pistas[i].Detalle; }

        using (VentanaSilencios ventana = new VentanaSilencios(nombres, detalles,
                   PistasVegas.SugerirVoz(pistas), PistasVegas.HaySeleccion(vegas), Analizar))
        {
            if (ventana.ShowDialog() != DialogResult.OK || ventana.Rangos.Count == 0) return;

            Dictionary<int, bool> analizadas = new Dictionary<int, bool>();
            foreach (int i in ventana.PistasElegidas) analizadas[pistas[i].Pista.Index] = true;
            List<Track> destino = new List<Track>();
            foreach (Track t in proyecto.Tracks)
                if (ventana.Ajustes.TodasLasPistas || analizadas.ContainsKey(t.Index)) destino.Add(t);

            double fps = proyecto.Video.FrameRate;
            List<Rango> rangos = Editor.AjustarAFotogramas(ventana.Rangos, fps);
            int n = rangos.Count;
            double total = 0;
            foreach (Rango r in rangos) total += r.Fin - r.Inicio;

            Modo modo = ventana.Ajustes.Modo;
            double durAntes = proyecto.Length.ToMilliseconds() / 1000.0;
            double suavizado = ventana.Ajustes.SuavizadoMs / 1000.0;
            using (UndoBlock deshacer = new UndoBlock("Quitar silencios"))
            {
                if (modo == Modo.Eliminar)
                    Editor.Eliminar(proyecto, destino, rangos, true, ventana.Ajustes.TodasLasPistas, suavizado);
                else if (modo == Modo.DejarHuecos)
                    Editor.Eliminar(proyecto, destino, rangos, false, false, suavizado);
                else if (modo == Modo.Silenciar)
                    Editor.Silenciar(proyecto, destino, rangos, suavizado);
                else
                    Editor.Marcar(proyecto, rangos);
            }

            // Si el proyecto ya estaba transcrito, sus tiempos se corrigen para
            // que sigan cuadrando con la linea de tiempo.
            string aviso = "";
            if (modo == Modo.Eliminar && ventana.Ajustes.TodasLasPistas)
                aviso = Transcripcion.RegistrarCortes(proyecto.FilePath, rangos, durAntes,
                    proyecto.Length.ToMilliseconds() / 1000.0);

            string hecho = modo == Modo.Eliminar ? "eliminados" :
                           modo == Modo.DejarHuecos ? "quitados dejando huecos" :
                           modo == Modo.Silenciar ? "silenciados" : "marcados como regiones";
            MessageBox.Show(
                n + " silencios " + hecho + " (" + Formato.Tiempo(total) + ")." + aviso + "\n\n" +
                "Si no te convence, Ctrl+Z lo deshace todo de una vez.",
                "Quitar silencios");
        }
    }

    Analisis Analizar(int indicePista, bool usarSeleccion)
    {
        double inicio, duracion;
        PistasVegas.ObtenerRango(vegas, usarSeleccion, out inicio, out duracion);
        return PistasVegas.Niveles(vegas, pistas[indicePista].Pista, inicio, duracion);
    }
}
