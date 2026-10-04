using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

// Memes en el capitulo ya editado: Gemini elige los momentos (por ritmo, no a
// cada rato) y el meme de la biblioteca que mejor queda, sin repetir los de
// los ultimos capitulos.
public class PropuestaMeme
{
    public double En, Duracion;
    public int Id = -1;
    public string Motivo = "", Dicho = "";
    public bool Elegido = true;
}

public static class LogicaMemes
{
    public const string PistaVideo = "vegas-cut · Memes", PistaAudio = "vegas-cut · Memes (audio)";

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static string F(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    // Los memes que se pueden usar: descritos y no usados en los ultimos capitulos.
    public static List<Meme> Candidatos(BibliotecaMemes b, int recientes, string capitulo)
    {
        List<Meme> r = new List<Meme>();
        if (b == null) return r;
        foreach (Meme m in b.Memes) if (m.Descrito && !b.UsadoHacePoco(m, recientes, capitulo)) r.Add(m);
        return r;
    }

    // Donde no va un meme: el kit (opening, ending...) y donde ya hay uno.
    public static List<Rango> Ocupado(Project p)
    {
        List<Rango> r = new List<Rango>();
        foreach (Track t in p.Tracks)
        {
            string n = t.Name ?? "";
            if (n.Contains("Kit") || n == PistaVideo || n == PistaAudio)
                foreach (TrackEvent e in t.Events) r.Add(new Rango(S(e.Start), S(e.End)));
        }
        return Rangos.Unir(r, 0.5);
    }

    public static string Instrucciones(int cadaSeg, int minimoEntre)
    {
        return "Eres el editor de una serie de YouTube de Minecraft con amigos, estilo anime de JoJo y con mucho humor. El capítulo " +
               "ya está editado. Pon MEMES de la biblioteca del editor (imágenes, gifs, videos cortos o sonidos) donde de verdad " +
               "suman: el remate de un chiste, una reacción a un fallo o una muerte, una sorpresa, algo absurdo, una victoria.\n" +
               "- Por RITMO, no a cada rato: en promedio uno cada ~" + cadaSeg + " s, nunca dos a menos de " + minimoEntre + " s, y " +
               "ninguno en momentos serios, tensos o emotivos ni encima de una explicación importante.\n" +
               "- \"en\": el segundo justo DESPUÉS de la frase o el momento (que no tape lo que se dice). \"duracion\" solo para " +
               "imágenes (1.5 a 4 s).\n" +
               "- Que el meme encaje con lo que pasa (su descripción, tags y \"uso\"). Varía: no repitas un meme en el capítulo y " +
               "prefiere los menos usados.\n" +
               "- No pongas nada en los tramos OCUPADOS.\n- Solo ids de la lista.\n" +
               "Responde SOLO con JSON: {\"memes\": [{\"en\": s, \"id\": n, \"duracion\": s, \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(Transcripcion t, Project p, List<Meme> candidatos, double duracion)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Duración del capítulo: " + Formato.Tiempo(duracion) + " (" + F(duracion) + " s)\n");
        List<Rango> oc = Ocupado(p);
        if (oc.Count > 0)
        {
            sb.Append("OCUPADOS (sin memes): ");
            foreach (Rango r in oc) sb.Append("[" + F(r.Inicio) + "-" + F(r.Fin) + "] ");
            sb.Append("\n");
        }
        List<string> marcas = new List<string>();
        foreach (Marker m in p.Markers) if (!(m is Region) && !String.IsNullOrEmpty(m.Label)) marcas.Add("[" + F(S(m.Position)) + "] " + m.Label);
        if (marcas.Count > 0) sb.Append("MARCADORES:\n" + String.Join("\n", marcas.ToArray()) + "\n");
        sb.Append("\nBIBLIOTECA [id] tipo | qué es | tags | cuándo usarlo | veces usado\n");
        for (int i = 0; i < candidatos.Count; i++)
        {
            Meme m = candidatos[i];
            sb.Append("[" + i + "] " + m.Tipo + " | " + m.Descripcion + " | " + String.Join(", ", m.Tags.ToArray()) + " | " + m.Uso +
                      " | " + m.Usos.Count + "\n");
        }
        sb.Append("\nTRANSCRIPCIÓN DEL CAPÍTULO [inicio-fin] persona: texto\n");
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

    // Lee la respuesta y aplica las reglas aunque Gemini no las cumpla: dentro del
    // video, fuera de lo ocupado, separados y sin repetir.
    public static List<PropuestaMeme> Leer(string json, List<Meme> candidatos, double duracion, int minimoEntre, List<Rango> ocupado,
                                           Transcripcion t)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        List<PropuestaMeme> todas = new List<PropuestaMeme>();
        foreach (object x in Json.Lista(o, "memes"))
        {
            PropuestaMeme pm = new PropuestaMeme();
            pm.En = Json.Numero(x, "en", -1); pm.Id = (int)Json.Numero(x, "id", -1);
            pm.Duracion = Math.Max(1.5, Math.Min(4, Json.Numero(x, "duracion", 2.5)));
            pm.Motivo = Json.Texto(x, "motivo");
            if (pm.En < 0 || pm.En > duracion - 1 || pm.Id < 0 || pm.Id >= candidatos.Count) continue;
            todas.Add(pm);
        }
        todas.Sort(delegate (PropuestaMeme a, PropuestaMeme b) { return a.En.CompareTo(b.En); });
        List<PropuestaMeme> r = new List<PropuestaMeme>();
        Dictionary<int, bool> usados = new Dictionary<int, bool>();
        List<Segmento> segs = t != null ? t.SegmentosActuales() : new List<Segmento>();
        foreach (PropuestaMeme pm in todas)
        {
            if (usados.ContainsKey(pm.Id)) continue;
            bool libre = true;
            foreach (Rango z in ocupado) if (pm.En >= z.Inicio - minimoEntre / 2.0 && pm.En <= z.Fin + 1) { libre = false; break; }
            if (!libre) continue;
            if (r.Count > 0 && pm.En - r[r.Count - 1].En < minimoEntre) continue;
            foreach (Segmento s in segs)
                if (s.Fin >= pm.En - 4 && s.Inicio <= pm.En + 0.5 && !String.IsNullOrEmpty(s.Texto)) pm.Dicho = s.Texto.Trim();
            usados[pm.Id] = true;
            r.Add(pm);
        }
        return r;
    }

    static VideoTrack PistaV(Project p)
    {
        foreach (Track t in p.Tracks) if (!t.IsAudio() && t.Name == PistaVideo) return (VideoTrack)t;
        VideoTrack v = new VideoTrack(0, PistaVideo);
        p.Tracks.Add(v);
        return v;
    }

    static AudioTrack PistaA(Project p)
    {
        foreach (Track t in p.Tracks) if (t.IsAudio() && t.Name == PistaAudio) return (AudioTrack)t;
        AudioTrack a = new AudioTrack(p.Tracks.Count, PistaAudio);
        p.Tracks.Add(a);
        return a;
    }

    // Pone los memes elegidos y anota en la biblioteca que se usaron en este capitulo.
    public static int Colocar(Project p, BibliotecaMemes b, List<PropuestaMeme> propuestas, List<Meme> candidatos, string capitulo,
                              List<string> avisos)
    {
        int n = 0;
        foreach (PropuestaMeme pm in propuestas)
        {
            if (!pm.Elegido) continue;
            Meme m = candidatos[pm.Id];
            string ruta = b.Completa(m);
            if (!File.Exists(ruta)) { avisos.Add("No encontré " + m.Ruta + "."); continue; }
            try
            {
                Media md = new Media(ruta);
                MediaStream v = md.Streams.GetItemByMediaType(MediaType.Video, 0), a = md.Streams.GetItemByMediaType(MediaType.Audio, 0);
                double largo = m.Tipo == "imagen" ? pm.Duracion : Math.Min(12, S(md.Length) > 0.2 ? S(md.Length) : pm.Duracion);
                if (v != null && m.Tipo != "sonido")
                {
                    VideoEvent ev = PistaV(p).AddVideoEvent(TC(pm.En), TC(largo));
                    ev.AddTake(v);
                    try { ev.FadeIn.Length = TC(0.12); ev.FadeOut.Length = TC(0.12); } catch { }
                }
                if (a != null && m.Tipo != "imagen")
                {
                    AudioEvent ea = PistaA(p).AddAudioEvent(TC(pm.En), TC(largo));
                    ea.AddTake(a);
                }
                b.RegistrarUso(m, capitulo);
                n++;
            }
            catch (Exception ex) { avisos.Add(m.Nombre + ": " + ex.Message); }
        }
        return n;
    }
}
