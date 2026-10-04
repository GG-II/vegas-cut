using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

// =====================================================================
// Varios POV: el video de otro jugador grabado al mismo tiempo.
//  1. Sincronizar (ANTES de quitar silencios): se compara el audio de una
//     pista tuya con una del otro POV (la llamada de Discord suena en las
//     dos) y se mueve el otro POV hasta que coincidan. Despues, quitar
//     silencios corta todas las pistas a la vez y siguen sincronizadas.
//  2. Cambios de POV (despues de cortar): Gemini elige los pocos momentos en
//     que conviene ver el juego del otro (cuando dice «mira», cuando le pasa
//     algo, cuando el hace lo importante) y se muestran esos tramos.
// =====================================================================

public class TramoPov
{
    public double Inicio, Fin;
    public string Motivo = "", Dicho = "";
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

public static class LogicaPov
{
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static string F(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    // ------------------------------------------------------- sincronizar

    // Envolvente para comparar: niveles en dB (cada 10 ms) agrupados de a
    // "factor", con piso de -60 dB y sin la tendencia lenta (asi pesan las
    // frases y no el volumen general de cada grabacion).
    public static double[] Envolvente(float[] db, int factor)
    {
        int n = db.Length / factor;
        double[] e = new double[n];
        for (int i = 0; i < n; i++)
        {
            double s = 0;
            for (int k = 0; k < factor; k++) s += Math.Max(-60, (double)db[i * factor + k]);
            e[i] = s / factor;
        }
        // Quitar la media movil de ~4 s.
        int v = Math.Max(1, (int)(4.0 / (0.01 * factor)));
        double[] r = new double[n];
        double suma = 0;
        int cuenta = 0;
        for (int i = 0; i < n; i++)
        {
            suma += e[i]; cuenta++;
            if (i - v >= 0) { suma -= e[i - v]; cuenta--; }
            r[i] = e[i] - suma / cuenta;
        }
        double media = 0, dev = 0;
        foreach (double x in r) media += x;
        media /= Math.Max(1, n);
        foreach (double x in r) dev += (x - media) * (x - media);
        dev = Math.Sqrt(dev / Math.Max(1, n));
        if (dev < 1e-9) dev = 1;
        for (int i = 0; i < n; i++) r[i] = (r[i] - media) / dev;
        return r;
    }

    // FFT compleja en el lugar (n potencia de 2).
    static void Fft(double[] re, double[] im, bool inversa)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { double t = re[i]; re[i] = re[j]; re[j] = t; t = im[i]; im[i] = im[j]; im[j] = t; }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2 * Math.PI / len * (inversa ? 1 : -1);
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double xr = re[b] * cr - im[b] * ci, xi = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - xr; im[b] = im[a] - xi;
                    re[a] += xr; im[a] += xi;
                    double t = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = t;
                }
            }
        }
        if (inversa) for (int i = 0; i < n; i++) { re[i] /= n; im[i] /= n; }
    }

    // Cuanto hay que mover el otro POV (segundos, + = mas tarde) para que coincida con la
    // referencia; "confianza" es cuantas desviaciones sobresale el pico (más de ~8 es seguro).
    public static double Desfase(double[] referencia, double[] otro, double paso, double maximo, out double confianza)
    {
        int n = 1;
        while (n < referencia.Length + otro.Length) n <<= 1;
        double[] ar = new double[n], ai = new double[n], br = new double[n], bi = new double[n];
        Array.Copy(referencia, ar, referencia.Length);
        Array.Copy(otro, br, otro.Length);
        Fft(ar, ai, false);
        Fft(br, bi, false);
        // A * conj(B): c[k] = suma de ref[t + k] * otro[t]
        for (int i = 0; i < n; i++)
        {
            double r = ar[i] * br[i] + ai[i] * bi[i], im = ai[i] * br[i] - ar[i] * bi[i];
            ar[i] = r; ai[i] = im;
        }
        Fft(ar, ai, true);
        int lim = (int)(maximo / paso);
        double mejor = double.MinValue, suma = 0, suma2 = 0;
        int k0 = 0, cuantos = 0;
        for (int k = -lim; k <= lim; k++)
        {
            int idx = k >= 0 ? k : n + k;
            if (idx < 0 || idx >= n) continue;
            // Normalizado por cuanto se solapan, para no favorecer los desfases chicos.
            int solape = Math.Min(referencia.Length - Math.Max(0, k), otro.Length - Math.Max(0, -k));
            if (solape < 100) continue;
            double v = ar[idx] / solape;
            suma += v; suma2 += v * v; cuantos++;
            if (v > mejor) { mejor = v; k0 = k; }
        }
        double media = suma / Math.Max(1, cuantos), dev = Math.Sqrt(Math.Max(1e-12, suma2 / Math.Max(1, cuantos) - media * media));
        confianza = (mejor - media) / dev;
        return k0 * paso;
    }

    // Mueve los eventos de las pistas del otro POV; si quedarian antes del cero, se recortan.
    public static void Mover(List<Track> pistas, double delta)
    {
        foreach (Track t in pistas)
        {
            List<TrackEvent> evs = new List<TrackEvent>();
            foreach (TrackEvent e in t.Events) evs.Add(e);
            // Hacia adelante se mueven de atras para adelante (para no pisarse).
            evs.Sort(delegate (TrackEvent a, TrackEvent b) { return delta > 0 ? S(b.Start).CompareTo(S(a.Start)) : S(a.Start).CompareTo(S(b.Start)); });
            foreach (TrackEvent e in evs)
            {
                double ini = S(e.Start) + delta;
                if (ini < 0)
                {
                    double corte = -ini;
                    if (corte >= S(e.Length) - 0.05) { try { t.Events.Remove(e); } catch { } continue; }
                    if (e.ActiveTake != null) e.ActiveTake.Offset = TC(S(e.ActiveTake.Offset) + corte * e.PlaybackRate);
                    e.Length = TC(S(e.Length) - corte);
                    ini = 0;
                }
                e.Start = TC(ini);
            }
        }
    }

    static string Archivo(TrackEvent e)
    {
        try { return e.ActiveTake != null && e.ActiveTake.Media != null ? (e.ActiveTake.Media.FilePath ?? "") : ""; } catch { return ""; }
    }

    // Las pistas que usan los mismos archivos que "pista" (el video y los audios del otro POV).
    public static List<Track> MismoArchivo(Project p, Track pista)
    {
        Dictionary<string, bool> archivos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (TrackEvent e in pista.Events) { string a = Archivo(e); if (a.Length > 0) archivos[a] = true; }
        List<Track> r = new List<Track>();
        foreach (Track t in p.Tracks)
            foreach (TrackEvent e in t.Events)
                if (archivos.ContainsKey(Archivo(e))) { r.Add(t); break; }
        return r;
    }

    // ------------------------------------------------- cambios de POV

    public static string Instrucciones(string jugador, int maxPorcentaje, int minimoEntre)
    {
        return "Eres el editor de una serie de YouTube de Minecraft con amigos. El video sigue el POV principal; también está el " +
               "POV de " + jugador + ", grabado al mismo tiempo y sincronizado. Elige los POCOS momentos en que conviene cambiar a " +
               "la pantalla de " + jugador + ":\n" +
               "- cuando " + jugador + " pide que miren su juego («mira», «miren», «ven a ver», «look») o lo muestra;\n" +
               "- cuando a " + jugador + " le pasa algo (muere, cae, encuentra algo, lo atacan, consigue algo);\n" +
               "- cuando " + jugador + " es quien hace lo importante de ese momento y el POV principal no lo ve.\n" +
               "Reglas: el POV principal manda. En total como mucho el " + maxPorcentaje + " % del video; cada tramo de 4 a 20 s; " +
               "nunca dos tramos a menos de " + minimoEntre + " s. Empieza ~1 s ANTES de lo que pasa y termina tras la reacción. Si no " +
               "hay un buen motivo, no cambies.\n" +
               "Responde SOLO con JSON: {\"tramos\": [{\"inicio\": s, \"fin\": s, \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(Transcripcion t, Project p, string jugador, double duracion)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Duración: " + Formato.Tiempo(duracion) + " (" + F(duracion) + " s). Jugador del otro POV: " + jugador + ".\n");
        List<string> marcas = new List<string>();
        foreach (Marker m in p.Markers) if (!(m is Region) && !String.IsNullOrEmpty(m.Label)) marcas.Add("[" + F(S(m.Position)) + "] " + m.Label);
        if (marcas.Count > 0) sb.Append("MARCADORES:\n" + String.Join("\n", marcas.ToArray()) + "\n");
        sb.Append("\nTRANSCRIPCIÓN [inicio-fin] persona: texto\n");
        if (t != null)
            foreach (Segmento s in t.SegmentosActuales())
            {
                if (String.IsNullOrEmpty(s.Texto) || s.Inicio > duracion) continue;
                string quien = s.Hablante >= 0 && s.Hablante < t.Hablantes.Count ? t.Hablantes[s.Hablante].Nombre : "?";
                sb.Append("[" + F(s.Inicio) + "-" + F(s.Fin) + "] " + quien + ": " + s.Texto.Trim() + "\n");
                if (sb.Length > 300000) break;
            }
        return sb.ToString();
    }

    // Lee la respuesta y aplica las reglas: largo 3–25 s, separados, y no mas del maximo en total.
    public static List<TramoPov> Leer(string json, double duracion, int maxPorcentaje, int minimoEntre, Transcripcion t)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        List<TramoPov> todos = new List<TramoPov>();
        foreach (object x in Json.Lista(o, "tramos"))
        {
            TramoPov tp = new TramoPov();
            tp.Inicio = Math.Max(0, Json.Numero(x, "inicio", -1)); tp.Fin = Math.Min(duracion, Json.Numero(x, "fin", -1));
            tp.Motivo = Json.Texto(x, "motivo");
            if (tp.Fin - tp.Inicio < 2) continue;
            if (tp.Duracion > 25) tp.Fin = tp.Inicio + 25;
            todos.Add(tp);
        }
        todos.Sort(delegate (TramoPov a, TramoPov b) { return a.Inicio.CompareTo(b.Inicio); });
        List<TramoPov> r = new List<TramoPov>();
        double total = 0, max = duracion * maxPorcentaje / 100.0;
        List<Segmento> segs = t != null ? t.SegmentosActuales() : new List<Segmento>();
        foreach (TramoPov tp in todos)
        {
            if (r.Count > 0 && tp.Inicio - r[r.Count - 1].Fin < minimoEntre) continue;
            if (total + tp.Duracion > max) continue;
            StringBuilder d = new StringBuilder();
            foreach (Segmento s in segs)
                if (s.Fin > tp.Inicio && s.Inicio < tp.Fin && !String.IsNullOrEmpty(s.Texto)) { d.Append(s.Texto.Trim() + " "); if (d.Length > 160) break; }
            tp.Dicho = d.ToString().Trim();
            total += tp.Duracion;
            r.Add(tp);
        }
        return r;
    }

    // Parte los eventos de la pista en los bordes de los tramos.
    static void Partir(Track pista, List<double> bordes)
    {
        foreach (double b in bordes)
        {
            List<TrackEvent> evs = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) evs.Add(e);
            foreach (TrackEvent e in evs)
                if (S(e.Start) < b - 0.02 && S(e.End) > b + 0.02) { e.Split(TC(b - S(e.Start))); break; }
        }
    }

    static bool Dentro(TrackEvent e, List<TramoPov> tramos)
    {
        double m = (S(e.Start) + S(e.End)) / 2;
        foreach (TramoPov tp in tramos) if (tp.Elegido && m > tp.Inicio && m < tp.Fin) return true;
        return false;
    }

    // Muestra el otro POV en los tramos elegidos: su video (y el sonido de su juego, si se da)
    // suena solo ahi y el video principal se silencia ahi. Lo que habia de antes se rehace.
    public static int Aplicar(Track principal, Track otroVideo, Track otroJuego, List<TramoPov> tramos)
    {
        List<double> bordes = new List<double>();
        foreach (TramoPov tp in tramos) if (tp.Elegido) { bordes.Add(tp.Inicio); bordes.Add(tp.Fin); }
        Partir(principal, bordes);
        Partir(otroVideo, bordes);
        if (otroJuego != null) Partir(otroJuego, bordes);
        int n = 0;
        foreach (TrackEvent e in principal.Events) e.Mute = Dentro(e, tramos);
        foreach (TrackEvent e in otroVideo.Events) { e.Mute = !Dentro(e, tramos); if (!e.Mute) n++; }
        if (otroJuego != null) foreach (TrackEvent e in otroJuego.Events) e.Mute = !Dentro(e, tramos);
        return n;
    }

    // ---------------------------------------------------------- ajustes

    public static string RutaAjustes(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-pov.json");
    }

    public static void GuardarAjustes(string veg, Dictionary<string, string> valores)
    {
        if (String.IsNullOrEmpty(veg)) return;
        Dictionary<string, object> d = new Dictionary<string, object>();
        foreach (KeyValuePair<string, string> kv in valores) d[kv.Key] = kv.Value;
        try { File.WriteAllText(RutaAjustes(veg), Json.Escribir(d), new UTF8Encoding(false)); } catch { }
    }

    public static string Ajuste(string veg, string clave)
    {
        try
        {
            string r = RutaAjustes(CopiaBase.Original(veg));
            if (!File.Exists(r)) r = RutaAjustes(veg);
            return File.Exists(r) ? Json.Texto(Json.Leer(File.ReadAllText(r, Encoding.UTF8)), clave) : "";
        }
        catch { return ""; }
    }
}
