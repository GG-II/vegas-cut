using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// =====================================================================
// "Anteriormente": mini resumen con clips de episodios pasados
//
// Gemini lee lo que quedo en los episodios anteriores (su transcripcion,
// sin lo que se corto) y de que trata el episodio actual, y elige frases
// cortas que sirven para entender este capitulo. Cada frase se lleva a su
// archivo de grabacion y segundo (fuentes de la transcripcion), asi que se
// trae al proyecto nuevo sin abrir el proyecto viejo.
// =====================================================================

public class ClipAnterior
{
    public int Episodio;              // indice en la lista de episodios
    public double Inicio, Fin;        // tiempos originales de ese episodio
    public string Texto = "", Motivo = "", Quien = "";
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

// Un pedazo de archivo a poner en la linea de tiempo.
public class PiezaAnterior
{
    public int Hablante;              // del episodio viejo
    public string Media = "";
    public int Flujo;
    public double Desde, Hasta;       // segundos del archivo
    public double En;                 // segundos desde el inicio del "anteriormente"
}

public static class LogicaAnteriormente
{
    static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    public static string Instrucciones(int segundos)
    {
        return "Eres editor de una serie de YouTube en español (gameplays con amigos). Vas a armar el " +
               "\"ANTERIORMENTE\" que abre el episodio actual: unos " + segundos + " segundos con frases cortas, dichas " +
               "por las personas en episodios anteriores, que recuerden lo necesario para entender ESTE episodio.\n\n" +
               "Reglas:\n" +
               "- Elige lo que se retoma o importa en el episodio actual: objetivos, conflictos, promesas, rivalidades, " +
               "lugares u objetos que vuelven a aparecer. Un detalle pequeño de antes que aquí se vuelve importante vale " +
               "más que un momento gracioso que no tiene que ver.\n" +
               "- Cada clip es una frase o dos completas, de 2 a 7 segundos, que se entienda sola. Empieza y termina en " +
               "límites de frase.\n" +
               "- En orden cronológico (episodio y tiempo). La suma de los clips debe quedar cerca de " + segundos +
               " s (entre " + (int)(segundos * 0.8) + " y " + (int)(segundos * 1.15) + " s).\n" +
               "- Nada de conversaciones personales, problemas técnicos ni groserías fuertes.\n" +
               "- Usa solo tiempos que aparecen en la transcripción o en las frases clave de ese episodio. De los " +
               "capítulos más viejos solo tienes su ficha: puedes usar sus frases clave tal cual, con sus tiempos.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"clips\": [{\"episodio\": n, \"inicio\": s, \"fin\": s, \"texto\": \"lo que se dice\", " +
               "\"motivo\": \"por qué importa para este episodio\"}], \"resumen\": \"el anteriormente en una o dos frases\"}";
    }

    public static string Mensaje(List<Episodio> episodios, string actual, string indicaciones, int segundos)
    {
        return Mensaje(episodios, actual, indicaciones, segundos, "");
    }

    // Completa: los 2 capitulos mas cercanos (y los que no tienen ficha);
    // de los demas solo la ficha, para no mandar horas de transcripcion.
    public const int Completos = 2;

    public static string Mensaje(List<Episodio> episodios, string actual, string indicaciones, int segundos, string notas)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Duración del anteriormente: " + segundos + " s\n");
        if (!String.IsNullOrEmpty(notas)) sb.Append("\nNOTAS DE LA SERIE:\n" + notas.Trim() + "\n");
        if (!String.IsNullOrEmpty(indicaciones)) sb.Append("\nINDICACIONES DEL EDITOR:\n" + indicaciones.Trim() + "\n");
        sb.Append("\nEPISODIO ACTUAL (de qué trata; para saber qué recordar):\n" + (actual ?? "").Trim() + "\n");
        for (int i = 0; i < episodios.Count; i++)
        {
            Episodio e = episodios[i];
            sb.Append("\n==== EPISODIO " + (i + 1) + ": " + e.Nombre + " ====\n");
            bool completo = e.Ficha == null || i >= episodios.Count - Completos;
            if (e.Ficha != null) sb.Append("Ficha: " + e.Ficha.Texto(!completo));
            else if (e.Resumen.Length > 0) sb.Append("Resumen: " + e.Resumen.Trim() + "\n");
            if (!completo) continue;
            sb.Append("Transcripción de lo que quedó en el video [inicio-fin] persona: texto\n");
            foreach (Segmento s in e.Publicado())
                sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + Nombre(e.T, s.Hablante) + ": " + s.Texto + "\n");
        }
        return sb.ToString();
    }

    static string Nombre(Transcripcion t, int h) { return h >= 0 && h < t.Hablantes.Count ? t.Hablantes[h].Nombre : "?"; }

    // De que trata el episodio actual: el resumen de MomentosIA si hay; si
    // no, su transcripcion (recortada para no mandar de mas).
    public static string Actual(Transcripcion t, string resumenIA)
    {
        if (!String.IsNullOrEmpty(resumenIA)) return resumenIA;
        if (t == null) return "(sin transcripción del episodio actual)";
        StringBuilder sb = new StringBuilder();
        foreach (Segmento s in t.SegmentosActuales())
        {
            if (sb.Length > 40000) { sb.Append("…\n"); break; }
            sb.Append(Nombre(t, s.Hablante) + ": " + s.Texto + "\n");
        }
        return sb.ToString();
    }

    // Lee los clips y los ajusta a las palabras (sin cortar a media palabra,
    // con un respiro de 0.15 s), sin pasar de 10 s cada uno.
    public static List<ClipAnterior> Leer(string json, List<Episodio> episodios)
    {
        List<ClipAnterior> r = new List<ClipAnterior>();
        object o = Json.Leer(Gemini.QuitarCercas(json));
        foreach (object x in Json.Lista(o, "clips"))
        {
            ClipAnterior c = new ClipAnterior();
            c.Episodio = (int)Json.Numero(x, "episodio", 0) - 1;
            if (c.Episodio < 0 || c.Episodio >= episodios.Count) continue;
            c.Inicio = Json.Numero(x, "inicio", -1);
            c.Fin = Json.Numero(x, "fin", -1);
            c.Texto = Json.Texto(x, "texto");
            c.Motivo = Json.Texto(x, "motivo");
            if (c.Inicio < 0 || c.Fin - c.Inicio < 0.5) continue;
            Ajustar(c, episodios[c.Episodio]);
            r.Add(c);
        }
        return r;
    }

    static void Ajustar(ClipAnterior c, Episodio e)
    {
        double a = c.Inicio, b = c.Fin;
        Palabra primera = null, ultima = null;
        foreach (Segmento s in e.T.Segmentos)
            foreach (Palabra p in s.Palabras)
            {
                if (p.Fin <= a || p.Inicio >= b) continue;
                if (primera == null || p.Inicio < primera.Inicio) { primera = p; c.Quien = Nombre(e.T, s.Hablante); }
                if (ultima == null || p.Fin > ultima.Fin) ultima = p;
            }
        if (primera != null) a = Math.Min(a, primera.Inicio);
        if (ultima != null) b = Math.Max(b, ultima.Fin);
        c.Inicio = Math.Max(0, a - 0.15);
        c.Fin = Math.Min(c.Inicio + 10, b + 0.15);
    }

    public static double Total(List<ClipAnterior> clips)
    {
        double d = 0;
        foreach (ClipAnterior c in clips) if (c.Elegido) d += c.Duracion;
        return d;
    }

    // Pedazos de archivo de un clip: uno por pista grabada (voces y
    // ambiente) y por cada evento que tenia esa pista en ese momento.
    public static List<PiezaAnterior> Piezas(Transcripcion t, double a, double b, double en)
    {
        List<PiezaAnterior> r = new List<PiezaAnterior>();
        for (int h = 0; h < t.Hablantes.Count; h++)
            foreach (Fuente f in t.Hablantes[h].Fuentes)
            {
                double x = Math.Max(a, f.Inicio), y = Math.Min(b, f.Fin);
                if (y - x < 0.05) continue;
                PiezaAnterior p = new PiezaAnterior();
                p.Hablante = h; p.Media = f.Media; p.Flujo = f.Flujo;
                p.Desde = f.Desde + (x - f.Inicio) * f.Velocidad;
                p.Hasta = p.Desde + (y - x) * f.Velocidad;
                p.En = en + (x - a);
                r.Add(p);
            }
        return r;
    }
}
