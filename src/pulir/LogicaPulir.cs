using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// =====================================================================
// PulirEpisodio, parte 1: medir el episodio y compararlo con las reglas de
// su serie (ajustadas al papel del capitulo) para ver donde se puede caer
// la retencion.
// =====================================================================

// Un renglon del informe: lo medido contra lo que pide la serie.
public class Chequeo
{
    public string Que = "", Medido = "", Objetivo = "";
    public bool Ok;
}

public class Informe
{
    public Medicion M;
    public ReglasRitmo Reglas;
    public string Papel = "Normal";
    public bool ConNarrador;          // la serie lleva narrador y ya hay narracion
    public bool FaltaNarracion;       // la serie lleva narrador pero aun no se grabo
    public List<Valle> Valles = new List<Valle>();
    public List<Chequeo> Chequeos = new List<Chequeo>();
}

public static class LogicaPulir
{
    public const string PrefijoRegion = "VALLE · ";

    static string N(double x) { return x.ToString("0.#", CultureInfo.InvariantCulture); }

    // Minutos completos (el ultimo, si es muy corto, no cuenta para promedios).
    static double MinutosUtiles(Medicion m) { return Math.Max(1, m.Duracion / 60); }

    public static Informe Analizar(Medicion m, FormatoSerie f, string papel)
    {
        Informe r = new Informe();
        r.M = m;
        r.Papel = String.IsNullOrEmpty(papel) ? "Normal" : papel;
        r.Reglas = PapelEpisodio.Reglas(f.Reglas, r.Papel);
        r.ConNarrador = f.Narrador && m.HayNarrador;
        r.FaltaNarracion = f.Narrador && !m.HayNarrador;
        r.Valles = Ritmo.Valles(m, r.Reglas, r.ConNarrador);
        r.Chequeos = Chequear(r);
        return r;
    }

    static Chequeo C(string que, string medido, string objetivo, bool ok)
    {
        Chequeo c = new Chequeo();
        c.Que = que; c.Medido = medido; c.Objetivo = objetivo; c.Ok = ok;
        return c;
    }

    static List<Chequeo> Chequear(Informe inf)
    {
        Medicion m = inf.M;
        ReglasRitmo r = inf.Reglas;
        List<Chequeo> l = new List<Chequeo>();
        double min = m.Duracion / 60;
        l.Add(C("Duración", Formato.Tiempo(m.Duracion), N(r.DuracionMin) + "–" + N(r.DuracionMax) + " min",
                min >= r.DuracionMin - 0.25 && min <= r.DuracionMax + 0.5));

        int cortes = 0, recursos = 0;
        foreach (MinutoRitmo x in m.Minutos) { cortes += x.Cortes; recursos += x.Recursos; }
        double cpm = cortes / MinutosUtiles(m), rpm = recursos / MinutosUtiles(m);
        l.Add(C("Cortes por minuto", N(Math.Round(cpm, 1)), r.CortesMin + "–" + r.CortesMax, cpm >= r.CortesMin && cpm <= r.CortesMax + 5));
        l.Add(C("Recursos por minuto", N(Math.Round(rpm, 1)), "≥ " + r.RecursosPorMin, rpm >= r.RecursosPorMin));

        // Zona critica: lo que se ve antes de que decidan quedarse.
        double zona = Math.Min(r.ZonaCriticaSeg, m.Duracion);
        int criticos = 0;
        foreach (Valle v in inf.Valles) if (v.Critico) criticos++;
        l.Add(C("Inicio (hasta " + Formato.Tiempo(zona) + ")", criticos == 0 ? "sin valles" : criticos + (criticos == 1 ? " valle" : " valles"),
                "sin valles", criticos == 0));

        if (inf.FaltaNarracion)
            l.Add(C("Narrador", "aún no hay narración", "cada ≤ " + r.NarradorCadaSeg + " s", false));
        else if (inf.ConNarrador)
        {
            double narr = 0;
            foreach (Rango x in m.Narracion) narr += x.Fin - x.Inicio;
            int huecos = Ritmo.SinNarrador(m, r.NarradorCadaSeg).Count;
            l.Add(C("Narrador", Math.Round(narr / Math.Max(1, m.Duracion) * 100) + " % del video" +
                    (huecos > 0 ? " · " + huecos + (huecos == 1 ? " hueco" : " huecos") : ""),
                    "cada ≤ " + r.NarradorCadaSeg + " s", huecos == 0));
            if (m.PPM > 0)
                l.Add(C("Velocidad del narrador", m.PPM + " ppm", r.PPM + " ppm", Math.Abs(m.PPM - r.PPM) <= 25));
        }

        if (m.CambiosMusica.Count == 0)
            l.Add(C("Música", "no encontré música", "cambiar cada ~" + r.MusicaCadaSeg + " s", false));
        else
        {
            double cada = m.Duracion / m.CambiosMusica.Count;
            l.Add(C("Música", m.CambiosMusica.Count + " temas · cada " + Math.Round(cada) + " s", "cada ~" + r.MusicaCadaSeg + " s",
                    cada <= r.MusicaCadaSeg * 1.5));
        }
        return l;
    }

    // Pone cada valle como region "VALLE · ..." (quita las de una medicion anterior).
    public static int MarcarRegiones(Project p, List<Valle> valles)
    {
        QuitarRegiones(p);
        int n = 0;
        foreach (Valle v in valles)
        {
            if (v.Fin - v.Inicio < 1) continue;
            p.Regions.Add(new Region(Timecode.FromMilliseconds(v.Inicio * 1000), Timecode.FromMilliseconds((v.Fin - v.Inicio) * 1000),
                                     PrefijoRegion + v.Texto));
            n++;
        }
        return n;
    }

    public static int QuitarRegiones(Project p)
    {
        List<Region> viejas = new List<Region>();
        foreach (Region r in p.Regions) if ((r.Label ?? "").StartsWith(PrefijoRegion)) viejas.Add(r);
        foreach (Region r in viejas) p.Regions.Remove(r);
        return viejas.Count;
    }

    // El informe en texto (para el guion y para Gemini en la parte 2).
    public static string Texto(Informe inf)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Papel del capítulo: " + inf.Papel + "\n");
        foreach (Chequeo c in inf.Chequeos)
            sb.Append((c.Ok ? "[ok] " : "[!!] ") + c.Que + ": " + c.Medido + " (objetivo " + c.Objetivo + ")\n");
        if (inf.Valles.Count > 0)
        {
            sb.Append("Valles:\n");
            foreach (Valle v in inf.Valles)
                sb.Append("- " + Formato.Tiempo(v.Inicio) + "–" + Formato.Tiempo(v.Fin) + (v.Critico ? " (zona crítica) " : " ") + v.Texto + "\n");
        }
        sb.Append("Minuto a minuto:\n" + Ritmo.Tabla(inf.M));
        return sb.ToString();
    }
}
