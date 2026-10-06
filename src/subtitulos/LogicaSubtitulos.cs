using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// =====================================================================
// Subtitulos (.srt) desde la transcripcion, en los tiempos actuales (siguen
// los cortes). Gemini corrige lo que Whisper oyo mal sin reescribir, pregunta
// lo que no sabe y aprende un glosario (por serie o para todos los videos).
// =====================================================================

public class Subtitulo
{
    public int Id, Hablante;
    public double Inicio, Fin;
    public string Quien = "", Texto = "", Nota = "";
    public List<string> Dudosas = new List<string>();   // palabras que Whisper no oyo bien
}

public class PreguntaSub
{
    public List<int> Ids = new List<int>();
    public string Fragmento = "", Pregunta = "", Respuesta = "";
    public List<string> Opciones = new List<string>();
    public bool Recordar = true;
}

public class OpcionesSub
{
    public int MaxLinea = 42, Lineas = 2;
    public double MaxDuracion = 6, MinDuracion = 1, Pausa = 0.7;
    public bool Nombres;      // "Nombre: " cuando cambia quien habla
}

// Lo que se recuerda: indicaciones y correcciones ("mal" -> "bien").
public class MemoriaSub
{
    public string Indicaciones = "";
    public List<KeyValuePair<string, string>> Glosario = new List<KeyValuePair<string, string>>();

    public void Aprender(string mal, string bien)
    {
        mal = (mal ?? "").Trim(); bien = (bien ?? "").Trim();
        if (mal.Length == 0 || bien.Length == 0 || mal == bien) return;
        Glosario.RemoveAll(delegate (KeyValuePair<string, string> g) { return String.Equals(g.Key, mal, StringComparison.OrdinalIgnoreCase); });
        Glosario.Add(new KeyValuePair<string, string>(mal, bien));
    }

    // Una correccion por linea: "mal => bien".
    public string GlosarioTexto()
    {
        StringBuilder sb = new StringBuilder();
        foreach (KeyValuePair<string, string> g in Glosario) sb.Append(g.Key + " => " + g.Value + "\r\n");
        return sb.ToString();
    }

    public void LeerGlosario(string texto)
    {
        Glosario.Clear();
        foreach (string l in (texto ?? "").Replace("\r", "").Split('\n'))
        {
            int i = l.IndexOf("=>");
            if (i > 0) Aprender(l.Substring(0, i), l.Substring(i + 2));
        }
    }
}

public static class LogicaSubtitulos
{
    // ------------------------------------------------- armar

    public static List<Subtitulo> Armar(Transcripcion t, OpcionesSub op)
    {
        List<KeyValuePair<int, Palabra>> palabras = new List<KeyValuePair<int, Palabra>>();
        foreach (Segmento s in t.SegmentosActuales())
        {
            if (s.Palabras.Count > 0)
                foreach (Palabra p in s.Palabras) palabras.Add(new KeyValuePair<int, Palabra>(s.Hablante, p));
            else if (!String.IsNullOrEmpty(s.Texto))
                palabras.Add(new KeyValuePair<int, Palabra>(s.Hablante, new Palabra { Inicio = s.Inicio, Fin = s.Fin, Texto = " " + s.Texto, Prob = 1 }));
        }
        palabras.Sort(delegate (KeyValuePair<int, Palabra> a, KeyValuePair<int, Palabra> b) { return a.Value.Inicio.CompareTo(b.Value.Inicio); });

        List<Subtitulo> r = new List<Subtitulo>();
        Subtitulo cur = null;
        int maximo = op.MaxLinea * op.Lineas;
        foreach (KeyValuePair<int, Palabra> kv in palabras)
        {
            Palabra w = kv.Value;
            string txt = (w.Texto ?? "").Trim();
            if (txt.Length == 0) continue;
            bool nuevo = cur == null || kv.Key != cur.Hablante || w.Inicio - cur.Fin > op.Pausa ||
                         w.Fin - cur.Inicio > op.MaxDuracion || cur.Texto.Length + 1 + txt.Length > maximo ||
                         (Regex.IsMatch(cur.Texto, @"[.?!…]$") && cur.Texto.Length >= op.MaxLinea * 0.6);
            if (nuevo)
            {
                cur = new Subtitulo { Hablante = kv.Key, Inicio = w.Inicio, Fin = w.Fin, Texto = txt };
                if (kv.Key >= 0 && kv.Key < t.Hablantes.Count) cur.Quien = t.Hablantes[kv.Key].Nombre;
                r.Add(cur);
            }
            else { cur.Texto += " " + txt; cur.Fin = Math.Max(cur.Fin, w.Fin); }
            if (w.Prob > 0 && w.Prob < 0.5) cur.Dudosas.Add(txt.Trim('.', ',', '!', '?', '¡', '¿', ';', ':'));
        }
        // Que se alcance a leer, sin encimarse con el siguiente.
        for (int i = 0; i < r.Count; i++)
        {
            Subtitulo s = r[i];
            s.Id = i + 1;
            double siguiente = i + 1 < r.Count ? r[i + 1].Inicio : double.MaxValue;
            if (s.Fin - s.Inicio < op.MinDuracion) s.Fin = Math.Min(s.Inicio + op.MinDuracion, siguiente - 0.04);
            if (s.Fin > siguiente - 0.02) s.Fin = Math.Max(s.Inicio + 0.3, siguiente - 0.02);
        }
        return r;
    }

    // Parte en lineas de hasta "max" letras, lo mas parejas posible.
    public static string Lineas(string texto, int max)
    {
        texto = Regex.Replace(texto ?? "", @"\s+", " ").Trim();
        if (texto.Length <= max) return texto;
        int medio = texto.Length / 2, mejor = -1;
        for (int d = 0; d <= medio; d++)
        {
            if (medio - d > 0 && texto[medio - d] == ' ') { mejor = medio - d; break; }
            if (medio + d < texto.Length && texto[medio + d] == ' ') { mejor = medio + d; break; }
        }
        if (mejor < 0) return texto;
        return texto.Substring(0, mejor) + "\r\n" + texto.Substring(mejor + 1);
    }

    public static string Tiempo(double s)
    {
        long ms = (long)Math.Round(Math.Max(0, s) * 1000);
        return (ms / 3600000).ToString("00") + ":" + (ms / 60000 % 60).ToString("00") + ":" + (ms / 1000 % 60).ToString("00") + "," + (ms % 1000).ToString("000");
    }

    public static string Srt(List<Subtitulo> subs, OpcionesSub op)
    {
        StringBuilder sb = new StringBuilder();
        int n = 0, antes = -1;
        foreach (Subtitulo s in subs)
        {
            string texto = s.Texto.Trim();
            if (texto.Length == 0) continue;
            if (op.Nombres && s.Hablante != antes && s.Quien.Length > 0) texto = s.Quien + ": " + texto;
            antes = s.Hablante;
            sb.Append(++n + "\r\n" + Tiempo(s.Inicio) + " --> " + Tiempo(s.Fin) + "\r\n" + Lineas(texto, op.MaxLinea) + "\r\n\r\n");
        }
        return sb.ToString();
    }

    // ------------------------------------------------- glosario y censura

    static string Patron(string mal) { return @"(?<![\p{L}\p{N}])" + Regex.Escape(mal) + @"(?![\p{L}\p{N}])"; }

    // Aplica lo aprendido antes de pedir nada. Devuelve cuantos subtitulos cambio.
    public static int AplicarGlosario(List<Subtitulo> subs, MemoriaSub m)
    {
        int n = 0;
        foreach (Subtitulo s in subs)
        {
            string antes = s.Texto;
            foreach (KeyValuePair<string, string> g in m.Glosario)
                s.Texto = Regex.Replace(s.Texto, Patron(g.Key), g.Value.Replace("$", "$$"), RegexOptions.IgnoreCase);
            if (s.Texto != antes) n++;
        }
        return n;
    }

    // Tapa las palabrotas de la lista de la censura: "p***".
    public static int Censurar(List<Subtitulo> subs, string lista)
    {
        List<string[]> patrones = LogicaCensura.Patrones(lista);
        int n = 0;
        foreach (Subtitulo s in subs)
        {
            string[] w = s.Texto.Split(' ');
            bool cambio = false;
            for (int i = 0; i < w.Length; i++)
                foreach (string[] pat in patrones)
                {
                    if (i + pat.Length > w.Length) continue;
                    bool ok = true;
                    for (int k = 0; k < pat.Length && ok; k++)
                    {
                        string x = LogicaCensura.Normalizar(w[i + k]), p = pat[k];
                        ok = p.EndsWith("*") ? x.StartsWith(p.Substring(0, p.Length - 1)) && x.Length > 0 : x == p;
                    }
                    if (!ok) continue;
                    for (int k = 0; k < pat.Length; k++) w[i + k] = Tapar(w[i + k]);
                    cambio = true;
                    break;
                }
            if (cambio) { s.Texto = String.Join(" ", w); n++; }
        }
        return n;
    }

    static string Tapar(string palabra)
    {
        Match m = Regex.Match(palabra, @"^([^\p{L}]*)(\p{L})([\p{L}\p{N}]*)(.*)$");
        if (!m.Success) return palabra;
        return m.Groups[1].Value + m.Groups[2].Value + new string('*', Math.Max(2, m.Groups[3].Value.Length)) + m.Groups[4].Value;
    }

    // ------------------------------------------------- Gemini

    public static string Instrucciones()
    {
        return "Corriges subtítulos en español de un video de YouTube (gameplays con amigos, video ensayos...). Vienen de una " +
               "transcripción automática (Whisper): a veces oye mal nombres, términos del juego o palabras sueltas.\n" +
               "- Corrige SOLO lo que se oyó mal, la ortografía, los acentos, los signos (¿? ¡!) y las mayúsculas. NO reescribas, " +
               "no resumas, no cambies el estilo ni las muletillas, no traduzcas y no censures (eso es aparte).\n" +
               "- Usa el GLOSARIO, las INDICACIONES y el contexto (nombres de las personas, de qué va el video) para nombres propios " +
               "y términos del juego.\n" +
               "- Las palabras marcadas como DUDOSAS son las que Whisper no oyó bien: revísalas con el contexto.\n" +
               "- Si no estás seguro de qué se dijo, NO adivines: haz una pregunta. \"fragmento\" es el texto tal cual aparece en " +
               "el subtítulo; \"opciones\", 1 a 3 posibilidades. Máximo 15 preguntas, las que más importan.\n" +
               "- Si una corrección se repite (un nombre que siempre sale mal), ponla también en \"glosario\" (mal -> bien).\n" +
               "- Solo los subtítulos que cambian, con su id. No juntes ni partas subtítulos (los tiempos son fijos).\n" +
               "Responde SOLO con JSON: {\"cambios\": [{\"id\": n, \"texto\": \"...\"}], \"preguntas\": [{\"ids\": [n], \"fragmento\": \"...\", " +
               "\"pregunta\": \"...\", \"opciones\": [\"...\"]}], \"glosario\": [{\"mal\": \"...\", \"bien\": \"...\"}]}";
    }

    public static string Mensaje(List<Subtitulo> subs, List<string> personas, string contexto, MemoriaSub m)
    {
        StringBuilder sb = new StringBuilder();
        if (personas.Count > 0) sb.Append("PERSONAS: " + String.Join(", ", personas.ToArray()) + "\n");
        if (!String.IsNullOrEmpty(contexto)) sb.Append("DE QUÉ VA: " + contexto.Trim() + "\n");
        if (m.Indicaciones.Trim().Length > 0) sb.Append("\nINDICACIONES:\n" + m.Indicaciones.Trim() + "\n");
        if (m.Glosario.Count > 0)
        {
            sb.Append("\nGLOSARIO (mal -> bien):\n");
            foreach (KeyValuePair<string, string> g in m.Glosario) sb.Append("- " + g.Key + " -> " + g.Value + "\n");
        }
        sb.Append("\nSUBTÍTULOS [id] persona: texto (DUDOSAS: ...)\n");
        foreach (Subtitulo s in subs)
            sb.Append("[" + s.Id + "] " + s.Quien + ": " + s.Texto + (s.Dudosas.Count > 0 ? "  (DUDOSAS: " + String.Join(", ", s.Dudosas.ToArray()) + ")" : "") + "\n");
        return sb.ToString();
    }

    // Aplica los cambios; devuelve las preguntas y lo que propone aprender.
    public static int Leer(string json, List<Subtitulo> subs, List<PreguntaSub> preguntas, MemoriaSub aprender)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        Dictionary<int, Subtitulo> porId = new Dictionary<int, Subtitulo>();
        foreach (Subtitulo s in subs) porId[s.Id] = s;
        int n = 0;
        foreach (object x in Json.Lista(o, "cambios"))
        {
            Subtitulo s;
            string texto = Json.Texto(x, "texto").Trim();
            if (!porId.TryGetValue((int)Json.Numero(x, "id", -1), out s) || texto.Length == 0 || texto == s.Texto) continue;
            // Si cambia demasiado, no es una correccion sino otra frase: se ignora.
            if (Distancia(texto, s.Texto) > Math.Max(12, s.Texto.Length * 0.6)) continue;
            s.Nota = "antes: " + s.Texto;
            s.Texto = texto;
            n++;
        }
        foreach (object x in Json.Lista(o, "preguntas"))
        {
            PreguntaSub p = new PreguntaSub();
            foreach (object i in Json.Lista(x, "ids")) if (i is double && porId.ContainsKey((int)(double)i)) p.Ids.Add((int)(double)i);
            p.Fragmento = Json.Texto(x, "fragmento").Trim();
            p.Pregunta = Json.Texto(x, "pregunta").Trim();
            foreach (object op in Json.Lista(x, "opciones")) if (op is string && ((string)op).Trim().Length > 0) p.Opciones.Add(((string)op).Trim());
            if (p.Ids.Count == 0 || p.Fragmento.Length == 0) continue;
            p.Respuesta = p.Opciones.Count > 0 ? p.Opciones[0] : p.Fragmento;
            preguntas.Add(p);
        }
        foreach (object x in Json.Lista(o, "glosario")) aprender.Aprender(Json.Texto(x, "mal"), Json.Texto(x, "bien"));
        return n;
    }

    // Tu respuesta reemplaza el fragmento en esos subtitulos. Devuelve en cuantos lo encontro.
    public static int Responder(PreguntaSub p, List<Subtitulo> subs)
    {
        if (p.Respuesta.Trim().Length == 0 || p.Respuesta.Trim() == p.Fragmento) return 0;
        int n = 0;
        foreach (Subtitulo s in subs)
        {
            if (!p.Ids.Contains(s.Id)) continue;
            int i = s.Texto.IndexOf(p.Fragmento, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            s.Texto = s.Texto.Substring(0, i) + p.Respuesta.Trim() + s.Texto.Substring(i + p.Fragmento.Length);
            s.Nota = "respondiste: " + p.Respuesta.Trim();
            n++;
        }
        return n;
    }

    static int Distancia(string a, string b)
    {
        int[] prev = new int[b.Length + 1], cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1));
            int[] t = prev; prev = cur; cur = t;
        }
        return prev[b.Length];
    }

    // ------------------------------------------------- memoria

    static string Ruta { get { return Path.Combine(Configuracion.Carpeta, "subtitulos.json"); } }

    // "" = para todos los videos; si no, el nombre de la serie.
    public static MemoriaSub Cargar(string ambito)
    {
        MemoriaSub m = new MemoriaSub();
        try
        {
            if (!File.Exists(Ruta)) return m;
            object o = Json.Obj(Json.Leer(File.ReadAllText(Ruta, Encoding.UTF8)), ambito.Length == 0 ? "general" : "serie:" + ambito);
            if (o == null) return m;
            m.Indicaciones = Json.Texto(o, "indicaciones");
            foreach (object g in Json.Lista(o, "glosario")) m.Aprender(Json.Texto(g, "mal"), Json.Texto(g, "bien"));
        }
        catch { }
        return m;
    }

    public static void Guardar(string ambito, MemoriaSub m)
    {
        Dictionary<string, object> todo = null;
        try { if (File.Exists(Ruta)) todo = Json.Leer(File.ReadAllText(Ruta, Encoding.UTF8)) as Dictionary<string, object>; } catch { }
        if (todo == null) todo = new Dictionary<string, object>();
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["indicaciones"] = m.Indicaciones;
        List<object> g = new List<object>();
        foreach (KeyValuePair<string, string> kv in m.Glosario)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["mal"] = kv.Key; x["bien"] = kv.Value;
            g.Add(x);
        }
        d["glosario"] = g;
        todo[ambito.Length == 0 ? "general" : "serie:" + ambito] = d;
        Directory.CreateDirectory(Configuracion.Carpeta);
        File.WriteAllText(Ruta, Json.Escribir(todo), new UTF8Encoding(false));
    }

    // Lo de todos los videos mas lo de la serie (lo de la serie gana).
    public static MemoriaSub Juntar(MemoriaSub general, MemoriaSub serie)
    {
        MemoriaSub r = new MemoriaSub();
        r.Indicaciones = (general.Indicaciones.Trim() + "\n" + serie.Indicaciones.Trim()).Trim();
        foreach (KeyValuePair<string, string> g in general.Glosario) r.Aprender(g.Key, g.Value);
        foreach (KeyValuePair<string, string> g in serie.Glosario) r.Aprender(g.Key, g.Value);
        return r;
    }
}
