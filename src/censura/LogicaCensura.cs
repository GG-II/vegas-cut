using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// =====================================================================
// Censura de palabrotas
//
// Busca en la transcripcion las palabras de la lista (una por linea; "*" al
// final acepta cualquier terminacion: "ching*" = chingar, chingada...; varias
// palabras seguidas = frase: "puta madre"). Cada coincidencia se tapa de
// "Antes" ms antes a "Despues" ms despues, con un largo minimo porque Whisper
// a veces da palabras de 0 s.
// =====================================================================

public enum Tapar { Toda, Inicio, Final }
public enum Encaje { Ajustar, AlInicio, Centrado, AlFinal }

public class OpcionesCensura
{
    public int AntesMs = 60, DespuesMs = 60, MinimoMs = 250;
    public Tapar Tapar = Tapar.Toda;
    public Encaje Encaje = Encaje.Ajustar;
    public bool Silenciar = true;
    public int VolumenSfx = -6;
    public string Sfx = "";            // "" = pitido; "-" = sin sfx; si no, ruta del archivo
    public List<string> Recientes = new List<string>();

    public static string Carpeta
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"); }
    }
    static string Ruta { get { return Path.Combine(Carpeta, "censura.ini"); } }
    public static string RutaPalabras { get { return Path.Combine(Carpeta, "censura-palabras.txt"); } }

    public static OpcionesCensura Cargar()
    {
        OpcionesCensura o = new OpcionesCensura();
        try
        {
            if (!File.Exists(Ruta)) return o;
            foreach (string l in File.ReadAllLines(Ruta, Encoding.UTF8))
            {
                int i = l.IndexOf('=');
                if (i < 0) continue;
                string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                int n;
                bool num = int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                switch (k)
                {
                    case "antes": if (num) o.AntesMs = n; break;
                    case "despues": if (num) o.DespuesMs = n; break;
                    case "minimo": if (num) o.MinimoMs = n; break;
                    case "volumen": if (num) o.VolumenSfx = n; break;
                    case "tapar": try { o.Tapar = (Tapar)Enum.Parse(typeof(Tapar), v); } catch { } break;
                    case "encaje": try { o.Encaje = (Encaje)Enum.Parse(typeof(Encaje), v); } catch { } break;
                    case "silenciar": o.Silenciar = v == "1"; break;
                    case "sfx": o.Sfx = v; break;
                    case "reciente": if (v.Length > 0 && !o.Recientes.Contains(v)) o.Recientes.Add(v); break;
                }
            }
        }
        catch { }
        return o;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            StringBuilder sb = new StringBuilder();
            sb.Append("antes=" + AntesMs + "\ndespues=" + DespuesMs + "\nminimo=" + MinimoMs + "\nvolumen=" + VolumenSfx +
                      "\ntapar=" + Tapar + "\nencaje=" + Encaje + "\nsilenciar=" + (Silenciar ? "1" : "0") + "\nsfx=" + Sfx + "\n");
            foreach (string r in Recientes) sb.Append("reciente=" + r + "\n");
            File.WriteAllText(Ruta, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }

    public static string CargarPalabras()
    {
        try { if (File.Exists(RutaPalabras)) return File.ReadAllText(RutaPalabras, Encoding.UTF8); } catch { }
        return LogicaCensura.PalabrasPorDefecto;
    }

    public static void GuardarPalabras(string texto)
    {
        try { Directory.CreateDirectory(Carpeta); File.WriteAllText(RutaPalabras, texto, new UTF8Encoding(false)); } catch { }
    }
}

// Lugar actual de una coincidencia en la linea de tiempo.
public class LugarCensura
{
    public int Pista = -1;          // indice de la pista de voz (-1 = desconocida)
    public double Inicio, Fin;      // de la palabra, en la linea de tiempo actual
}

public class Coincidencia
{
    public int Hablante;
    public double Inicio, Fin, Prob;   // tiempos originales de la transcripcion
    public string Texto = "", Contexto = "";
    public bool Elegida = true;
    public List<LugarCensura> Lugares = new List<LugarCensura>();
}

public static class LogicaCensura
{
    // Lista inicial, pensada para gameplays en espanol (Mexico) con algo de
    // ingles. Las palabras con doble sentido (madre, huevos, perra, culo) no
    // van solas: "madre" solo cuenta en frases como "puta madre".
    public const string PalabrasPorDefecto =
        "# Una palabra o frase por línea. * al final = cualquier terminación.\n" +
        "# Las líneas con # se ignoran. Mayúsculas y acentos dan igual.\n" +
        "puta madre\nputa\nputas\nputo\nputos\nput\nputazo*\nputiza*\nputada*\n" +
        "mierda*\nverga*\nvergazo*\nvergueo\nverguero*\nching*\npinche*\npendej*\n" +
        "cabron\ncabrona*\ncabrones\nculer*\ncoño\ncarajo\njoder\njodan\njodido*\n" +
        "no mames\nmamada*\nmamon*\nmarica*\nmaricon*\nzorra*\nperra madre\n" +
        "fuck*\nmotherfucker*\nshit\nbitch*\n";

    public static string Normalizar(string s)
    {
        string d = (s ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char c in d)
        {
            UnicodeCategory cat = CharUnicodeInfo.GetUnicodeCategory(c);
            if (cat == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    // Cada linea en palabras normalizadas; las mas largas primero para que
    // "puta madre" gane sobre "puta".
    public static List<string[]> Patrones(string lista)
    {
        List<string[]> r = new List<string[]>();
        foreach (string l in (lista ?? "").Replace("\r", "").Split('\n'))
        {
            string t = l.Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;
            List<string> ps = new List<string>();
            foreach (string w in t.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string n = Normalizar(w);
                if (n.Length == 0) continue;
                ps.Add(w.EndsWith("*") ? n + "*" : n);
            }
            if (ps.Count > 0) r.Add(ps.ToArray());
        }
        r.Sort(delegate (string[] a, string[] b) { return b.Length.CompareTo(a.Length); });
        return r;
    }

    static bool Coincide(string palabra, string patron)
    {
        if (patron.EndsWith("*")) return palabra.StartsWith(patron.Substring(0, patron.Length - 1));
        return palabra == patron;
    }

    public static List<Coincidencia> Buscar(Transcripcion t, string lista)
    {
        List<string[]> patrones = Patrones(lista);
        List<Coincidencia> r = new List<Coincidencia>();
        foreach (Segmento s in t.Segmentos)
        {
            List<Palabra> ws = new List<Palabra>();
            List<string> ns = new List<string>();
            foreach (Palabra p in s.Palabras)
            {
                string n = Normalizar(p.Texto);
                if (n.Length == 0) continue;
                ws.Add(p); ns.Add(n);
            }
            int i = 0;
            while (i < ws.Count)
            {
                int largo = 0;
                foreach (string[] pat in patrones)
                {
                    if (i + pat.Length > ws.Count) continue;
                    bool ok = true;
                    for (int k = 0; k < pat.Length && ok; k++) ok = Coincide(ns[i + k], pat[k]);
                    if (ok) { largo = pat.Length; break; }
                }
                if (largo == 0) { i++; continue; }
                Coincidencia c = new Coincidencia();
                c.Hablante = s.Hablante;
                c.Inicio = ws[i].Inicio;
                c.Fin = ws[i + largo - 1].Fin;
                c.Prob = 1;
                StringBuilder txt = new StringBuilder(), ctx = new StringBuilder();
                for (int k = i; k < i + largo; k++) { txt.Append(ws[k].Texto); c.Prob = Math.Min(c.Prob, ws[k].Prob); }
                c.Texto = txt.ToString().Trim().Trim('.', ',', '!', '?', '¡', '¿', ';', ':');
                for (int k = Math.Max(0, i - 5); k < Math.Min(ws.Count, i + largo + 5); k++)
                {
                    if (k == i) ctx.Append(" «");
                    ctx.Append(k == i ? ws[k].Texto.TrimStart() : ws[k].Texto);
                    if (k == i + largo - 1) ctx.Append("»");
                }
                c.Contexto = ctx.ToString().Trim();
                r.Add(c);
                i += largo;
            }
        }
        r.Sort(delegate (Coincidencia a, Coincidencia b) { return a.Inicio.CompareTo(b.Inicio); });
        return r;
    }

    // Lo que se tapa de una palabra que va de ini a fin.
    public static Rango Tapa(double ini, double fin, OpcionesCensura o)
    {
        double dur = Math.Max(0, fin - ini), parte = dur * 0.6;
        if (o.Tapar == Tapar.Inicio) fin = ini + parte;
        else if (o.Tapar == Tapar.Final) ini = fin - parte;
        double a = ini - o.AntesMs / 1000.0, b = fin + o.DespuesMs / 1000.0, min = o.MinimoMs / 1000.0;
        if (b - a < min)
        {
            double falta = min - (b - a);
            if (o.Tapar == Tapar.Inicio) b += falta;
            else if (o.Tapar == Tapar.Final) a -= falta;
            else { a -= falta / 2; b += falta / 2; }
        }
        return new Rango(Math.Max(0, a), b);
    }

    // Donde va el efecto: ajustado al rango o entero (largo del archivo).
    public static Rango Sfx(Rango tapa, double largoSfx, Encaje e)
    {
        if (e == Encaje.Ajustar || largoSfx <= 0) return tapa;
        double ini = e == Encaje.AlInicio ? tapa.Inicio
                   : e == Encaje.AlFinal ? tapa.Fin - largoSfx
                   : (tapa.Inicio + tapa.Fin) / 2 - largoSfx / 2;
        ini = Math.Max(0, ini);
        return new Rango(ini, ini + largoSfx);
    }

    // Une rangos que se tocan (dos palabrotas seguidas = un solo pitido).
    public static List<Rango> Unir(List<Rango> rangos)
    {
        List<Rango> l = new List<Rango>(rangos), r = new List<Rango>();
        l.Sort(delegate (Rango a, Rango b) { return a.Inicio.CompareTo(b.Inicio); });
        foreach (Rango x in l)
        {
            if (r.Count > 0 && x.Inicio <= r[r.Count - 1].Fin + 0.001)
                r[r.Count - 1] = new Rango(r[r.Count - 1].Inicio, Math.Max(r[r.Count - 1].Fin, x.Fin));
            else r.Add(x);
        }
        return r;
    }

    // Pitido clasico: seno de 1 kHz, 48 kHz mono 16 bits, con fundidos de 5 ms.
    public static string Pitido()
    {
        string ruta = Path.Combine(Path.Combine(OpcionesCensura.Carpeta, "sfx"), "pitido-1khz.wav");
        if (File.Exists(ruta)) return ruta;
        Directory.CreateDirectory(Path.GetDirectoryName(ruta));
        const int hz = 48000;
        int n = hz * 5;
        using (BinaryWriter w = new BinaryWriter(File.Create(ruta)))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1);
            w.Write(hz); w.Write(hz * 2); w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
            int fundido = hz / 200;
            for (int i = 0; i < n; i++)
            {
                double env = Math.Min(1, Math.Min(i, n - 1 - i) / (double)fundido);
                w.Write((short)Math.Round(Math.Sin(2 * Math.PI * 1000 * i / hz) * 0.35 * env * 32767));
            }
        }
        return ruta;
    }
}
