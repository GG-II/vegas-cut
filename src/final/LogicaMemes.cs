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

    public static string Instrucciones(int cadaSeg, int minimoEntre, int maximoSin)
    {
        return "Eres el editor de un video de YouTube. Puede ser de cualquier tipo (un gameplay con amigos, un video ensayo, un " +
               "vlog, una explicación...): por la transcripción ves de qué va y cuál es su tono. El video ya está editado. Pon MEMES " +
               "de la biblioteca del editor (imágenes, gifs, videos cortos o sonidos) donde de verdad suman: el remate de un chiste, " +
               "una reacción a un fallo, una sorpresa, algo absurdo u obvio, una contradicción, una victoria.\n" +
               "- Adáptate al tono: en un video ensayo o una explicación, menos y solo para aliviar o subrayar un punto; en un " +
               "video de humor, más.\n" +
               "- Por RITMO, no a cada rato: en promedio uno cada ~" + cadaSeg + " s, nunca dos a menos de " + minimoEntre + " s, y " +
               "ninguno en momentos serios, tensos o emotivos ni encima de una explicación importante.\n" +
               (maximoSin > 0 ? "- Que no pasen más de " + maximoSin + " s seguidos sin ninguno, salvo en los tramos serios, tensos o " +
                                "emotivos (ahí mejor nada) y en los OCUPADOS. Repártelos por todo el video, no los juntes en una parte.\n" : "") +
               "- \"en\": el segundo justo DESPUÉS de la frase o el momento (que no tape lo que se dice). \"duracion\" solo para " +
               "imágenes (1.5 a 4 s).\n" +
               "- Que el meme encaje con lo que pasa (su descripción, tags y \"uso\"). Varía: no repitas un meme en el video y " +
               "prefiere los menos usados.\n" +
               "- No pongas nada en los tramos OCUPADOS.\n- Solo ids de la lista.\n" +
               "Responde SOLO con JSON: {\"memes\": [{\"en\": s, \"id\": n, \"duracion\": s, \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(Transcripcion t, Project p, List<Meme> candidatos, double duracion)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Duración del video: " + Formato.Tiempo(duracion) + " (" + F(duracion) + " s)\n");
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
        sb.Append("\nTRANSCRIPCIÓN DEL VIDEO [inicio-fin] persona: texto\n");
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
        return Agregar(new List<PropuestaMeme>(), Parsear(json, candidatos, duracion), minimoEntre, ocupado, t);
    }

    static List<PropuestaMeme> Parsear(string json, List<Meme> candidatos, double duracion)
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
        return todas;
    }

    // Suma las nuevas a las que ya estaban (que tienen prioridad) cumpliendo las reglas.
    static List<PropuestaMeme> Agregar(List<PropuestaMeme> previas, List<PropuestaMeme> nuevas, int minimoEntre, List<Rango> ocupado,
                                       Transcripcion t)
    {
        List<PropuestaMeme> r = new List<PropuestaMeme>(previas);
        Dictionary<int, bool> usados = new Dictionary<int, bool>();
        foreach (PropuestaMeme pm in r) usados[pm.Id] = true;
        List<Segmento> segs = t != null ? t.SegmentosActuales() : new List<Segmento>();
        foreach (PropuestaMeme pm in nuevas)
        {
            if (usados.ContainsKey(pm.Id)) continue;
            bool libre = true;
            foreach (Rango z in ocupado) if (pm.En >= z.Inicio - minimoEntre / 2.0 && pm.En <= z.Fin + 1) { libre = false; break; }
            foreach (PropuestaMeme otra in r) if (Math.Abs(pm.En - otra.En) < minimoEntre) { libre = false; break; }
            if (!libre) continue;
            foreach (Segmento s in segs)
                if (s.Fin >= pm.En - 4 && s.Inicio <= pm.En + 0.5 && !String.IsNullOrEmpty(s.Texto)) pm.Dicho = s.Texto.Trim();
            usados[pm.Id] = true;
            r.Add(pm);
        }
        r.Sort(delegate (PropuestaMeme a, PropuestaMeme b) { return a.En.CompareTo(b.En); });
        return r;
    }

    // Tramos de mas de "maximo" segundos sin memes (lo ocupado, como el opening, corta el tramo).
    public static List<Rango> HuecosLargos(List<PropuestaMeme> propuestas, double duracion, int maximo, List<Rango> ocupado)
    {
        List<Rango> r = new List<Rango>();
        if (maximo <= 0) return r;
        List<double> puntos = new List<double> { 0 };
        foreach (PropuestaMeme pm in propuestas) puntos.Add(pm.En);
        puntos.Add(duracion);
        puntos.Sort();
        for (int i = 0; i + 1 < puntos.Count; i++)
        {
            double a = puntos[i];
            foreach (Rango z in ocupado)
            {
                if (z.Fin <= a || z.Inicio >= puntos[i + 1]) continue;
                if (z.Inicio - a > maximo) r.Add(new Rango(a, z.Inicio));
                a = Math.Max(a, z.Fin);
            }
            if (puntos[i + 1] - a > maximo) r.Add(new Rango(a, puntos[i + 1]));
        }
        return r;
    }

    // Segunda vuelta: Gemini pone memes solo en los tramos que quedaron vacios.
    public static string InstruccionesHuecos(int minimoEntre)
    {
        return "\nSEGUNDA VUELTA: ya elegiste los memes de YA PUESTOS, pero quedaron TRAMOS SIN MEMES demasiado largos. Propón memes " +
               "SOLO dentro de esos tramos: uno por tramo (dos si es muy largo), a " + minimoEntre + " s o más de los ya puestos y sin " +
               "repetir sus ids. Si un tramo es serio, tenso o emotivo, déjalo sin meme (mejor nada que forzarlo). Devuelve solo los nuevos.";
    }

    public static string MensajeHuecos(string mensaje, List<PropuestaMeme> previas, List<Rango> huecos)
    {
        StringBuilder sb = new StringBuilder(mensaje);
        sb.Append("\nYA PUESTOS: ");
        foreach (PropuestaMeme pm in previas) sb.Append("[" + F(pm.En) + "] id " + pm.Id + "; ");
        sb.Append("\nTRAMOS SIN MEMES: ");
        foreach (Rango h in huecos) sb.Append("[" + F(h.Inicio) + "-" + F(h.Fin) + "] ");
        sb.Append("\n");
        return sb.ToString();
    }

    public static List<PropuestaMeme> LeerHuecos(string json, List<PropuestaMeme> previas, List<Rango> huecos, List<Meme> candidatos,
                                                 double duracion, int minimoEntre, List<Rango> ocupado, Transcripcion t)
    {
        List<PropuestaMeme> nuevas = Parsear(json, candidatos, duracion).FindAll(delegate (PropuestaMeme pm)
        {
            foreach (Rango h in huecos) if (pm.En >= h.Inicio && pm.En <= h.Fin) return true;
            return false;
        });
        return Agregar(previas, nuevas, minimoEntre, ocupado, t);
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
