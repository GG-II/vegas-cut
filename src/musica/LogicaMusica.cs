using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

// =====================================================================
// Musica que baja sola (ducking)
//
// Con los tramos de voz se arma la curva de volumen de la musica: baja un
// poco antes de que alguien hable ("anticipa"), se queda abajo mientras
// hablan y vuelve a subir despacio al terminar ("recupera"). Las pausas mas
// cortas que "pausa minima" no la suben, para que no suba y baje a cada rato.
// =====================================================================

public class PuntoVolumen
{
    public double T, Db;   // segundo de la linea de tiempo y ganancia en dB (0 = sin cambio)
    public PuntoVolumen(double t, double db) { T = t; Db = db; }
}

public class AjustesMusica
{
    public int BajaDb = -14, AnticipaMs = 250, RecuperaMs = 600, PausaMs = 1200;

    static string Ruta
    {
        get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"), "musica.ini"); }
    }

    public static AjustesMusica Cargar()
    {
        AjustesMusica a = new AjustesMusica();
        try
        {
            if (!File.Exists(Ruta)) return a;
            foreach (string l in File.ReadAllLines(Ruta))
            {
                int i = l.IndexOf('='), n;
                if (i < 0 || !int.TryParse(l.Substring(i + 1).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                switch (l.Substring(0, i).Trim())
                {
                    case "baja": a.BajaDb = n; break;
                    case "anticipa": a.AnticipaMs = n; break;
                    case "recupera": a.RecuperaMs = n; break;
                    case "pausa": a.PausaMs = n; break;
                }
            }
        }
        catch { }
        return a;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
            File.WriteAllText(Ruta, "baja=" + BajaDb + "\nanticipa=" + AnticipaMs + "\nrecupera=" + RecuperaMs +
                "\npausa=" + PausaMs + "\n", new UTF8Encoding(false));
        }
        catch { }
    }
}

public static class LogicaMusica
{
    // Valores de deteccion para encontrar la voz: aqui no se corta nada, asi
    // que no hay margenes ni pedazo minimo; la pausa minima decide cuando
    // la musica puede subir.
    public static Valores Deteccion(int pausaMs)
    {
        Valores v = new Valores();
        v.SilencioMinMs = pausaMs; v.HablaMinMs = 150;
        v.MargenAntesMs = 0; v.MargenDespuesMs = 0; v.PedazoMinMs = 0; v.Sensibilidad = 0;
        return v;
    }

    // Lo que no es silencio, dentro de inicio..fin.
    public static List<Rango> Voz(List<Rango> silencios, double inicio, double fin)
    {
        List<Rango> r = new List<Rango>();
        double cursor = inicio;
        foreach (Rango s in silencios)
        {
            if (s.Inicio > cursor + 0.001) r.Add(new Rango(cursor, Math.Min(s.Inicio, fin)));
            cursor = Math.Max(cursor, s.Fin);
        }
        if (fin > cursor + 0.001) r.Add(new Rango(cursor, fin));
        return r;
    }

    public static List<PuntoVolumen> Puntos(List<Rango> voz, double inicio, double fin, double bajaDb, double anticipa, double recupera)
    {
        // 1. Cada voz ocupa desde que empieza a bajar hasta que termina de subir.
        List<Rango> zonas = new List<Rango>();
        List<Rango> orden = new List<Rango>(voz);
        orden.Sort(delegate (Rango a, Rango b) { return a.Inicio.CompareTo(b.Inicio); });
        foreach (Rango v in orden)
        {
            Rango z = new Rango(v.Inicio - anticipa, v.Fin + recupera);
            if (zonas.Count > 0 && z.Inicio <= zonas[zonas.Count - 1].Fin)
            {
                Rango u = zonas[zonas.Count - 1];
                zonas[zonas.Count - 1] = new Rango(u.Inicio, Math.Max(u.Fin, z.Fin));
            }
            else zonas.Add(z);
        }

        // 2. Rampa de bajada, tramo abajo y rampa de subida.
        List<PuntoVolumen> todos = new List<PuntoVolumen>();
        foreach (Rango z in zonas)
        {
            todos.Add(new PuntoVolumen(z.Inicio, 0));
            todos.Add(new PuntoVolumen(z.Inicio + anticipa, bajaDb));
            if (z.Fin - recupera > z.Inicio + anticipa + 0.0005) todos.Add(new PuntoVolumen(z.Fin - recupera, bajaDb));
            todos.Add(new PuntoVolumen(z.Fin, 0));
        }

        // 3. Solo lo que cae en el rango, con un punto en cada borde.
        List<PuntoVolumen> r = new List<PuntoVolumen>();
        r.Add(new PuntoVolumen(inicio, Valor(todos, inicio)));
        foreach (PuntoVolumen p in todos)
            if (p.T > inicio + 0.0005 && p.T < fin - 0.0005) r.Add(p);
        r.Add(new PuntoVolumen(fin, Valor(todos, fin)));

        // 4. Sin puntos de sobra (tres seguidos con el mismo nivel).
        List<PuntoVolumen> limpio = new List<PuntoVolumen>();
        for (int i = 0; i < r.Count; i++)
        {
            bool sobra = i > 0 && i < r.Count - 1 &&
                         Math.Abs(r[i - 1].Db - r[i].Db) < 0.01 && Math.Abs(r[i + 1].Db - r[i].Db) < 0.01;
            if (!sobra) limpio.Add(r[i]);
        }
        return limpio;
    }

    // Nivel en el instante t (lineal entre puntos; 0 dB fuera de ellos).
    public static double Valor(List<PuntoVolumen> puntos, double t)
    {
        if (puntos.Count == 0 || t < puntos[0].T || t > puntos[puntos.Count - 1].T) return 0;
        if (puntos.Count == 1) return puntos[0].Db;
        for (int i = 1; i < puntos.Count; i++)
        {
            PuntoVolumen a = puntos[i - 1], b = puntos[i];
            if (t > b.T) continue;
            if (b.T - a.T < 1e-9) return b.Db;
            return a.Db + (b.Db - a.Db) * (t - a.T) / (b.T - a.T);
        }
        return 0;
    }

    public static int Bajadas(List<PuntoVolumen> puntos)
    {
        int n = 0;
        for (int i = 1; i < puntos.Count; i++) if (puntos[i].Db < puntos[i - 1].Db - 0.01 && puntos[i - 1].Db > -0.01) n++;
        if (puntos.Count > 0 && puntos[0].Db < -0.01) n++;
        return n;
    }

    // Ganancia lineal (1 = 0 dB), como la guarda la envolvente de volumen.
    public static double Ganancia(double db) { return db <= -90 ? 0 : Math.Pow(10, db / 20); }

    // Pone los puntos en la envolvente de volumen de la pista: quita los que
    // habia dentro del rango y agrega los nuevos.
    public static int Aplicar(Track pista, List<PuntoVolumen> puntos, double inicio, double fin)
    {
        Envelope env = pista.Envelopes.FindByType(EnvelopeType.Volume);
        if (env == null)
        {
            env = new Envelope(EnvelopeType.Volume);
            pista.Envelopes.Add(env);
        }
        List<EnvelopePoint> quitar = new List<EnvelopePoint>();
        foreach (EnvelopePoint p in env.Points)
        {
            double t = p.X.ToMilliseconds() / 1000.0;
            if (t >= inicio - 0.0005 && t <= fin + 0.0005) quitar.Add(p);
        }
        foreach (EnvelopePoint p in quitar)
            try { env.Points.Remove(p); } catch { } // el primer punto no se puede borrar

        int n = 0;
        foreach (PuntoVolumen p in puntos)
        {
            EnvelopePoint existente = null;
            foreach (EnvelopePoint e in env.Points)
                if (Math.Abs(e.X.ToMilliseconds() - p.T * 1000) < 0.5) { existente = e; break; }
            if (existente != null) existente.Y = Ganancia(p.Db);
            else env.Points.Add(new EnvelopePoint(Timecode.FromMilliseconds(p.T * 1000), Ganancia(p.Db)));
            n++;
        }
        return n;
    }
}
