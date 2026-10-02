using System;
using System.Collections.Generic;
using ScriptPortal.Vegas;

// Bajar el juego bajo la narracion (paso final).
public static class LogicaPasoFinal
{
    // Tramos donde suena la narracion: la pista provisional o la del narrador grabado.
    public static List<Rango> Narracion(Project p, Transcripcion t, string narrador)
    {
        List<Rango> r = new List<Rango>();
        foreach (Track pista in p.Tracks)
            if (pista.IsAudio() && (pista.Name == RitmoVegas.PistaNarracion || (pista.Name ?? "").ToLowerInvariant().StartsWith("narr")))
                foreach (TrackEvent e in pista.Events)
                    if (!e.Mute) r.Add(new Rango(e.Start.ToMilliseconds() / 1000.0, e.End.ToMilliseconds() / 1000.0));
        if (r.Count == 0 && t != null)
        {
            Medicion m = RitmoVegas.Medir(p, t, narrador);
            r.AddRange(m.Narracion);
        }
        return Rangos.Unir(r, 0.3);
    }

    public static int BajarJuego(Project p, Transcripcion t, string narrador, int db)
    {
        List<Rango> narr = Narracion(p, t, narrador);
        if (narr.Count == 0) return 0;
        int n = 0;
        foreach (Track g in RitmoVegas.PistasGrabacion(p, t, narrador))
        {
            if (g.Name == RitmoVegas.PistaNarracion) continue;
            AplicarPlan.Bajar(g, narr, db);
            n++;
        }
        return n;
    }
}
