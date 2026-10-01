using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// =====================================================================
// Series: capitulos de un mismo proyecto de varias partes
//
// Los capitulos se encuentran solos por el nombre ("S01E02 SCR.veg" es la
// temporada 1, capitulo 2 de la serie "SCR"), en la misma carpeta o en las
// carpetas de al lado. Cada capitulo guarda junto a su .veg:
//   <proyecto>.vegascut-ficha.json  resumen, hilos abiertos y frases clave
//                                   (lo hace Gemini una vez, de lo que quedo
//                                   en el video)
//   <proyecto>.vegascut-serie.json  notas de la serie (personajes, apodos,
//                                   premisa) y que capitulos usar; un
//                                   capitulo nuevo hereda las notas del
//                                   anterior.
// =====================================================================

// Un capitulo ya transcrito, con su transcripcion cargada.
public class Episodio
{
    public string Veg = "", Nombre = "";
    public Transcripcion T;
    public string Resumen = "";   // de su ficha o de su respuesta de MomentosIA
    public Ficha Ficha;
    public bool TieneFuentes { get { return T != null && T.TieneFuentes; } }

    public static Episodio Abrir(string ruta)
    {
        Episodio e = new Episodio();
        string veg = ruta;
        if (ruta.EndsWith(".vegascut.json", StringComparison.OrdinalIgnoreCase))
            veg = ruta.Substring(0, ruta.Length - ".vegascut.json".Length) + ".veg";
        e.Veg = veg;
        e.Nombre = Path.GetFileNameWithoutExtension(veg);
        string rt = Transcripcion.RutaPara(veg);
        if (rt == null || !File.Exists(rt)) throw new Exception(e.Nombre + " no tiene transcripción (ejecuta Transcribir en ese proyecto).");
        e.T = Transcripcion.Cargar(rt);
        e.Ficha = Ficha.Cargar(veg);
        if (e.Ficha != null) e.Resumen = e.Ficha.Resumen;
        else
            try
            {
                string ia = Path.Combine(Path.GetDirectoryName(veg), e.Nombre + ".vegascut-ia.json");
                if (File.Exists(ia))
                {
                    object o = Json.Leer(File.ReadAllText(ia, Encoding.UTF8));
                    e.Resumen = Json.Texto(Json.Leer(Gemini.QuitarCercas(Json.Texto(o, "respuesta"))), "resumen");
                }
            }
            catch { }
        return e;
    }

    // Frases que quedaron en el video (sin lo cortado con las herramientas),
    // con los tiempos originales de la transcripcion.
    public List<Segmento> Publicado()
    {
        List<Segmento> r = new List<Segmento>();
        foreach (Segmento s in T.Segmentos)
        {
            if (Transcripcion.Alucinacion(s.Texto) || String.IsNullOrEmpty(s.Texto)) continue;
            if (double.IsNaN(T.Mapear(s.Inicio)) || double.IsNaN(T.Mapear(s.Fin))) continue;
            r.Add(s);
        }
        return r;
    }

    public static string Nombre_(Transcripcion t, int h) { return h >= 0 && h < t.Hablantes.Count ? t.Hablantes[h].Nombre : "?"; }

    public string Transcrito()
    {
        StringBuilder sb = new StringBuilder();
        foreach (Segmento s in Publicado())
            sb.Append("[" + Serie.S(s.Inicio) + "-" + Serie.S(s.Fin) + "] " + Nombre_(T, s.Hablante) + ": " + s.Texto + "\n");
        return sb.ToString();
    }
}

public class FraseClave
{
    public double Inicio, Fin;
    public string Quien = "", Texto = "", Por = "";
}

// Resumen de un capitulo para usarlo de contexto en los demas.
public class Ficha
{
    public string Resumen = "", Generada = "";
    public List<string> Hilos = new List<string>(), Recurrentes = new List<string>();
    public List<FraseClave> Frases = new List<FraseClave>();

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-ficha.json");
    }

    public static Ficha Leer(string json)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        Ficha f = new Ficha();
        f.Resumen = Json.Texto(o, "resumen");
        f.Generada = Json.Texto(o, "generada");
        foreach (object x in Json.Lista(o, "hilos")) if (x is string) f.Hilos.Add((string)x);
        foreach (object x in Json.Lista(o, "recurrentes")) if (x is string) f.Recurrentes.Add((string)x);
        foreach (object x in Json.Lista(o, "frases"))
        {
            FraseClave c = new FraseClave();
            c.Inicio = Json.Numero(x, "inicio", -1); c.Fin = Json.Numero(x, "fin", -1);
            c.Quien = Json.Texto(x, "quien"); c.Texto = Json.Texto(x, "texto"); c.Por = Json.Texto(x, "por");
            if (c.Inicio >= 0 && c.Fin > c.Inicio) f.Frases.Add(c);
        }
        if (f.Resumen.Length == 0) throw new Exception("La ficha no trae resumen.");
        return f;
    }

    public static Ficha Cargar(string veg)
    {
        try
        {
            string r = RutaPara(veg);
            return File.Exists(r) ? Leer(File.ReadAllText(r, Encoding.UTF8)) : null;
        }
        catch { return null; }
    }

    public void Guardar(string veg)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["resumen"] = Resumen;
        d["generada"] = Generada;
        d["hilos"] = new List<object>(Hilos.ToArray());
        d["recurrentes"] = new List<object>(Recurrentes.ToArray());
        List<object> fs = new List<object>();
        foreach (FraseClave c in Frases)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["inicio"] = Math.Round(c.Inicio, 2); x["fin"] = Math.Round(c.Fin, 2);
            x["quien"] = c.Quien; x["texto"] = c.Texto; x["por"] = c.Por;
            fs.Add(x);
        }
        d["frases"] = fs;
        File.WriteAllText(RutaPara(veg), Json.Escribir(d), new UTF8Encoding(false));
    }

    // Para el contexto de MomentosIA (sin tiempos) o de Anteriormente (con
    // las frases clave y sus tiempos).
    public string Texto(bool frases)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(Resumen.Trim().Replace("\n", " ") + "\n");
        if (Hilos.Count > 0) sb.Append("  Hilos abiertos: " + String.Join("; ", Hilos.ToArray()) + "\n");
        if (Recurrentes.Count > 0) sb.Append("  Recurrente: " + String.Join("; ", Recurrentes.ToArray()) + "\n");
        if (frases)
            foreach (FraseClave c in Frases)
                sb.Append("  [" + Serie.S(c.Inicio) + "-" + Serie.S(c.Fin) + "] " + c.Quien + ": " + c.Texto +
                          (c.Por.Length > 0 ? " (" + c.Por + ")" : "") + "\n");
        return sb.ToString();
    }
}

// Un capitulo encontrado (sin cargar su transcripcion).
public class CapSerie
{
    public string Veg = "", Nombre = "";
    public int Temporada, Numero;
    public int Relacion;          // -1 anterior, 0 el actual, 1 posterior
    public bool Elegido = true;
    public bool Transcrito { get { return File.Exists(Transcripcion.RutaPara(Veg)); } }
    public bool TieneFicha { get { return File.Exists(Ficha.RutaPara(Veg)); } }
    public string Codigo { get { return "S" + Temporada.ToString("00") + "E" + Numero.ToString("00"); } }
}

public static class Serie
{
    public static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    static readonly Regex Patron = new Regex(@"S(\d{1,2})\s*[-_ ]?\s*E(\d{1,3})", RegexOptions.IgnoreCase);

    // "S01E02 SCR" -> temporada 1, capitulo 2, serie "# scr".
    public static bool Clave(string nombre, out int temporada, out int numero, out string serie)
    {
        temporada = 0; numero = 0; serie = "";
        Match m = Patron.Match(nombre ?? "");
        if (!m.Success) return false;
        temporada = int.Parse(m.Groups[1].Value);
        numero = int.Parse(m.Groups[2].Value);
        string resto = (nombre.Substring(0, m.Index) + "#" + nombre.Substring(m.Index + m.Length)).ToLowerInvariant();
        serie = Regex.Replace(resto, @"[\s_\-\.]+", " ").Trim();
        return true;
    }

    // .veg de la carpeta y de sus subcarpetas hasta "niveles" de hondo.
    // Para ordenar capitulos: temporada y numero (los que no tienen, al final).
    public static int Orden(string nombre)
    {
        int t, n;
        string s;
        return Clave(nombre, out t, out n, out s) ? t * 1000 + n : int.MaxValue;
    }

    static void Agregar(List<string> l, string carpeta, int niveles)
    {
        try
        {
            if (String.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta) || l.Count > 5000) return;
            foreach (string f in Directory.GetFiles(carpeta, "*.veg"))
                if (Path.GetExtension(f).Equals(".veg", StringComparison.OrdinalIgnoreCase) && !l.Contains(f)) l.Add(f);
            if (niveles > 0)
                foreach (string d in Directory.GetDirectories(carpeta)) Agregar(l, d, niveles - 1);
        }
        catch { }
    }

    // Los capitulos de la misma serie, en orden; marca cual es el actual.
    // Busca en la carpeta del proyecto, sus subcarpetas, la carpeta de
    // arriba y las carpetas de al lado; y si eligiste una carpeta, en ella y
    // todas sus subcarpetas.
    public static List<CapSerie> Buscar(string veg) { return Buscar(veg, ""); }

    public static List<CapSerie> Buscar(string veg, string carpeta)
    {
        List<CapSerie> r = new List<CapSerie>();
        int t0, n0;
        string serie;
        if (String.IsNullOrEmpty(veg) || !Clave(Path.GetFileNameWithoutExtension(veg), out t0, out n0, out serie)) return r;
        string dir = Path.GetDirectoryName(veg);
        List<string> archivos = new List<string>();
        Agregar(archivos, dir, 1);
        Agregar(archivos, Path.GetDirectoryName(dir), 1);
        if (!String.IsNullOrEmpty(carpeta)) Agregar(archivos, carpeta, 6);
        foreach (string f in archivos)
        {
            int t, n;
            string s;
            if (!Clave(Path.GetFileNameWithoutExtension(f), out t, out n, out s) || s != serie) continue;
            if (r.Exists(delegate (CapSerie c) { return c.Temporada == t && c.Numero == n; }) && !String.Equals(f, veg, StringComparison.OrdinalIgnoreCase)) continue;
            r.RemoveAll(delegate (CapSerie c) { return c.Temporada == t && c.Numero == n; });
            CapSerie cap = new CapSerie();
            cap.Veg = f; cap.Nombre = Path.GetFileNameWithoutExtension(f);
            cap.Temporada = t; cap.Numero = n;
            int cmp = (t * 1000 + n).CompareTo(t0 * 1000 + n0);
            cap.Relacion = String.Equals(f, veg, StringComparison.OrdinalIgnoreCase) || cmp == 0 ? 0 : cmp;
            r.Add(cap);
        }
        r.Sort(delegate (CapSerie a, CapSerie b) { return (a.Temporada * 1000 + a.Numero).CompareTo(b.Temporada * 1000 + b.Numero); });
        return r;
    }

    // ------------------------------------------- ajustes por proyecto

    static string RutaAjustes(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-serie.json");
    }

    // Ajustes de este proyecto; si no tiene, las notas y la carpeta del
    // capitulo anterior mas cercano que los tenga.
    public static string Cargar(string veg, out string carpeta, out List<string> excluidos)
    {
        excluidos = new List<string>();
        carpeta = "";
        try
        {
            if (File.Exists(RutaAjustes(veg)))
            {
                object o = Json.Leer(File.ReadAllText(RutaAjustes(veg), Encoding.UTF8));
                foreach (object x in Json.Lista(o, "excluidos")) if (x is string) excluidos.Add((string)x);
                carpeta = Json.Texto(o, "carpeta");
                return Json.Texto(o, "notas");
            }
            List<CapSerie> caps = Buscar(veg);
            for (int i = caps.Count - 1; i >= 0; i--)
            {
                if (caps[i].Relacion >= 0 || !File.Exists(RutaAjustes(caps[i].Veg))) continue;
                object o = Json.Leer(File.ReadAllText(RutaAjustes(caps[i].Veg), Encoding.UTF8));
                carpeta = Json.Texto(o, "carpeta");
                return Json.Texto(o, "notas");
            }
        }
        catch { }
        return "";
    }

    public static void Guardar(string veg, string notas, string carpeta, List<CapSerie> caps)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["notas"] = notas ?? "";
        d["carpeta"] = carpeta ?? "";
        List<object> ex = new List<object>();
        foreach (CapSerie c in caps) if (!c.Elegido && c.Relacion != 0) ex.Add(c.Nombre);
        d["excluidos"] = ex;
        try { File.WriteAllText(RutaAjustes(veg), Json.Escribir(d), new UTF8Encoding(false)); } catch { }
    }

    // Capitulos con los ajustes del proyecto ya aplicados (carpeta y
    // capitulos que quitaste).
    public static List<CapSerie> Capitulos(string veg, out string notas, out string carpeta)
    {
        List<string> excluidos;
        notas = Cargar(veg, out carpeta, out excluidos);
        List<CapSerie> caps = Buscar(veg, carpeta);
        foreach (CapSerie c in caps) if (excluidos.Contains(c.Nombre)) c.Elegido = false;
        return caps;
    }

    // ------------------------------------------------------ fichas

    public static string InstruccionesFicha()
    {
        return "Eres editor de una serie de YouTube en español (gameplays con amigos). Recibes la transcripción de lo " +
               "que quedó en un capítulo. Haz su ficha para usarla de contexto al editar los otros capítulos.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"qué pasa en el capítulo, en orden, en 3 a 6 frases\",\n" +
               " \"hilos\": [\"objetivos, promesas, conflictos, rivalidades, objetos o lugares que pueden volver a aparecer\"],\n" +
               " \"recurrentes\": [\"chistes, apodos o frases que se repiten\"],\n" +
               " \"frases\": [{\"inicio\": s, \"fin\": s, \"quien\": \"nombre\", \"texto\": \"lo que se dice\", \"por\": \"por qué es clave\"}]}\n\n" +
               "Reglas:\n- \"frases\": de 5 a 15 frases cortas (2 a 7 s) que mejor cuentan lo importante del capítulo; " +
               "sirven para un \"anteriormente\". Usa solo tiempos de la transcripción.\n" +
               "- Nada de datos personales ni charla técnica. Escribe en español natural, con los nombres de las personas.";
    }

    public static string MensajeFicha(Episodio e, string notas)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Capítulo: " + e.Nombre + "\n");
        if (!String.IsNullOrEmpty(notas)) sb.Append("\nNOTAS DE LA SERIE:\n" + notas.Trim() + "\n");
        sb.Append("\nTranscripción [inicio-fin] persona: texto\n" + e.Transcrito());
        return sb.ToString();
    }

    // Contexto para MomentosIA: notas, anteriores y posteriores.
    public static string Contexto(List<CapSerie> caps, string notas)
    {
        StringBuilder sb = new StringBuilder();
        if (!String.IsNullOrEmpty(notas)) sb.Append("Notas de la serie:\n" + notas.Trim() + "\n");
        foreach (int rel in new int[] { -1, 1 })
        {
            bool titulo = false;
            foreach (CapSerie c in caps)
            {
                if (c.Relacion != rel || !c.Elegido) continue;
                Ficha f = Ficha.Cargar(c.Veg);
                if (f == null) continue;
                if (!titulo)
                {
                    sb.Append(rel < 0 ? "\nCapítulos anteriores:\n"
                                      : "\nCapítulos POSTERIORES (ya grabados): si algo de este capítulo prepara lo que se retoma " +
                                        "después, consérvalo aunque aquí parezca menor; no adelantes lo que pasa después.\n");
                    titulo = true;
                }
                sb.Append("- " + c.Nombre + ": " + f.Texto(false));
            }
        }
        return sb.ToString();
    }
}
