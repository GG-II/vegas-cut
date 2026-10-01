// Series.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Administra tus series (proyectos de varias partes: gameplays, video
// ensayos, podcast...): capitulos en orden, notas (personajes, apodos, de que
// va) y la ficha de cada capitulo hecha por Gemini. MomentosIA y Anteriormente
// usan la serie del proyecto abierto como contexto.
//
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;

// ---- src/series/Series.cs ----

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Configuracion config = Configuracion.Cargar();
        using (VentanaSeries v = new VentanaSeries(vegas.Project.FilePath, config.GeminiClave, config.GeminiModelo, false))
            v.ShowDialog();
    }
}

// ---- src/comun/Serie.cs ----

// =====================================================================
// Series: proyectos de varias partes (gameplays, video ensayos, podcast...)
//
// Una serie es un archivo "<nombre>.vegascut-serie.json" (normalmente en la
// carpeta de sus capitulos) con su nombre, tipo, notas y la lista de
// capitulos EN ORDEN. Se administra con el script Series; MomentosIA y
// Anteriormente reconocen a que serie pertenece el proyecto abierto. vegas-cut
// recuerda las series usadas en %APPDATA%\vegas-cut\series.json.
//
// Cada capitulo guarda junto a su .veg su ficha (<proyecto>.vegascut-ficha.json):
// resumen, hilos abiertos y frases clave, hecha una vez por Gemini.
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
        if (rt == null || !File.Exists(rt)) throw new Exception(e.Nombre + " no tiene transcripci\u00f3n (ejecuta Transcribir en ese proyecto).");
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

// Un capitulo de la serie (sin cargar su transcripcion).
public class CapSerie
{
    public string Veg = "", Nombre = "";
    public int Posicion;          // 1, 2, 3... en la serie
    public int Relacion = -1;     // -1 anterior, 0 el proyecto abierto, 1 posterior
    public bool Elegido = true;   // usarlo de contexto en este proyecto
    public bool Existe { get { return File.Exists(Veg); } }
    public bool Transcrito { get { return File.Exists(Transcripcion.RutaPara(Veg)); } }
    public bool TieneFicha { get { return File.Exists(Ficha.RutaPara(Veg)); } }
}

public class SerieProyecto
{
    public static readonly string[] Tipos = { "Gameplay", "Video ensayo", "Podcast", "Otro" };

    public string Ruta = "", Nombre = "", Tipo = "Gameplay", Notas = "", Carpeta = "";
    public List<string> Episodios = new List<string>();   // rutas de los .veg, en orden

    public static string Extension = ".vegascut-serie.json";

    public static string RutaPara(string carpeta, string nombre)
    {
        string limpio = nombre;
        foreach (char c in Path.GetInvalidFileNameChars()) limpio = limpio.Replace(c, '_');
        return Path.Combine(carpeta, limpio + Extension);
    }

    public static SerieProyecto Cargar(string ruta)
    {
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        if (Json.Texto(o, "formato") != "vegas-cut-serie") throw new Exception("No es un archivo de serie de vegas-cut.");
        SerieProyecto s = new SerieProyecto();
        s.Ruta = ruta;
        s.Nombre = Json.Texto(o, "nombre");
        s.Tipo = Json.Texto(o, "tipo");
        if (Array.IndexOf(Tipos, s.Tipo) < 0) s.Tipo = "Otro";
        s.Notas = Json.Texto(o, "notas");
        s.Carpeta = Json.Texto(o, "carpeta");
        string dir = Path.GetDirectoryName(ruta);
        foreach (object x in Json.Lista(o, "episodios"))
        {
            string veg = Json.Texto(x, "veg"), rel = Json.Texto(x, "relativo");
            // Si se movio la carpeta (u otra letra de disco), se busca junto al archivo de la serie.
            if (!File.Exists(veg) && rel.Length > 0 && File.Exists(Path.Combine(dir, rel))) veg = Path.GetFullPath(Path.Combine(dir, rel));
            if (veg.Length > 0) s.Episodios.Add(veg);
        }
        return s;
    }

    public void Guardar()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-serie";
        d["nombre"] = Nombre; d["tipo"] = Tipo; d["notas"] = Notas; d["carpeta"] = Carpeta;
        List<object> l = new List<object>();
        foreach (string veg in Episodios)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["veg"] = veg;
            x["relativo"] = Relativa(Path.GetDirectoryName(Ruta), veg);
            l.Add(x);
        }
        d["episodios"] = l;
        Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
        File.WriteAllText(Ruta, Json.Escribir(d), new UTF8Encoding(false));
        Registrar(Ruta);
    }

    // Ruta relativa si el capitulo esta dentro de la carpeta de la serie.
    static string Relativa(string dir, string veg)
    {
        string d = dir.TrimEnd(Path.DirectorySeparatorChar, '/') + Path.DirectorySeparatorChar;
        return veg.StartsWith(d, StringComparison.OrdinalIgnoreCase) ? veg.Substring(d.Length) : "";
    }

    public int IndiceDe(string veg)
    {
        for (int i = 0; i < Episodios.Count; i++)
            if (String.Equals(Episodios[i], veg, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    // Agrega un capitulo en su lugar: por temporada y numero si el nombre los
    // trae (S01E03), si no al final.
    public bool Agregar(string veg)
    {
        if (IndiceDe(veg) >= 0) return false;
        int orden = Serie.Orden(Path.GetFileNameWithoutExtension(veg));
        int i = Episodios.Count;
        if (orden < int.MaxValue)
            for (int k = 0; k < Episodios.Count; k++)
                if (Serie.Orden(Path.GetFileNameWithoutExtension(Episodios[k])) > orden) { i = k; break; }
        Episodios.Insert(i, veg);
        return true;
    }

    // Agrega los .veg de la carpeta (y subcarpetas) que parecen de esta serie:
    // mismo nombre con otro S01E02 que los capitulos que ya tiene, o, si no
    // tiene ninguno, todos los que traen S01E02. Devuelve cuantos agrego.
    public int BuscarEnCarpeta()
    {
        if (String.IsNullOrEmpty(Carpeta) || !Directory.Exists(Carpeta)) return 0;
        List<string> claves = new List<string>();
        foreach (string e in Episodios)
        {
            int t, n; string k;
            if (Serie.Clave(Path.GetFileNameWithoutExtension(e), out t, out n, out k) && !claves.Contains(k)) claves.Add(k);
        }
        int agregados = 0;
        foreach (string f in Serie.ArchivosVeg(Carpeta, 6))
        {
            int t, n; string k;
            if (!Serie.Clave(Path.GetFileNameWithoutExtension(f), out t, out n, out k)) continue;
            if (claves.Count > 0 && !claves.Contains(k)) continue;
            if (Agregar(f)) agregados++;
        }
        return agregados;
    }

    // Capitulos con su relacion al proyecto abierto (si no esta en la serie,
    // todos cuentan como anteriores).
    public List<CapSerie> Capitulos(string vegActual)
    {
        List<CapSerie> r = new List<CapSerie>();
        int actual = IndiceDe(vegActual ?? "");
        for (int i = 0; i < Episodios.Count; i++)
        {
            CapSerie c = new CapSerie();
            c.Veg = Episodios[i]; c.Nombre = Path.GetFileNameWithoutExtension(Episodios[i]); c.Posicion = i + 1;
            c.Relacion = actual < 0 ? -1 : i.CompareTo(actual);
            r.Add(c);
        }
        return r;
    }

    // ------------------------------------------------ series conocidas

    static string RutaRegistro
    {
        get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"), "series.json"); }
    }

    public static List<string> Registradas()
    {
        List<string> r = new List<string>();
        try
        {
            if (File.Exists(RutaRegistro))
                foreach (object x in Json.Lista(Json.Leer(File.ReadAllText(RutaRegistro, Encoding.UTF8)), "series"))
                    if (x is string && File.Exists((string)x) && !r.Contains((string)x)) r.Add((string)x);
        }
        catch { }
        return r;
    }

    static void GuardarRegistro(List<string> l)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(RutaRegistro));
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["series"] = new List<object>(l.ToArray());
            File.WriteAllText(RutaRegistro, Json.Escribir(d), new UTF8Encoding(false));
        }
        catch { }
    }

    public static void Registrar(string ruta)
    {
        List<string> l = Registradas();
        l.RemoveAll(delegate (string x) { return String.Equals(x, ruta, StringComparison.OrdinalIgnoreCase); });
        l.Insert(0, ruta);
        GuardarRegistro(l);
    }

    public static void Olvidar(string ruta)
    {
        List<string> l = Registradas();
        l.RemoveAll(delegate (string x) { return String.Equals(x, ruta, StringComparison.OrdinalIgnoreCase); });
        GuardarRegistro(l);
    }

    // La serie del proyecto: la que eligio para el (guardado junto al .veg)
    // o la primera conocida que lo tenga como capitulo.
    public static SerieProyecto DelProyecto(string veg)
    {
        string elegida = AjustesProyecto.Serie(veg);
        if (elegida.Length > 0 && File.Exists(elegida))
            try { return Cargar(elegida); } catch { }
        foreach (string r in Registradas())
            try
            {
                SerieProyecto s = Cargar(r);
                if (s.IndiceDe(veg) >= 0) return s;
            }
            catch { }
        return null;
    }
}

// Lo que cada proyecto recuerda de su serie (<proyecto>.vegascut-serie.json):
// que serie usa y que capitulos no quiere de contexto.
public static class AjustesProyecto
{
    static string Ruta(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-proyecto-serie.json");
    }

    static object Leer(string veg)
    {
        try { return File.Exists(Ruta(veg)) ? Json.Leer(File.ReadAllText(Ruta(veg), Encoding.UTF8)) : null; } catch { return null; }
    }

    public static string Serie(string veg) { return String.IsNullOrEmpty(veg) ? "" : Json.Texto(Leer(veg), "serie"); }

    public static List<string> Excluidos(string veg)
    {
        List<string> r = new List<string>();
        if (String.IsNullOrEmpty(veg)) return r;
        foreach (object x in Json.Lista(Leer(veg), "excluidos")) if (x is string) r.Add((string)x);
        return r;
    }

    public static void Guardar(string veg, string serie, List<CapSerie> caps)
    {
        if (String.IsNullOrEmpty(veg)) return;
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["serie"] = serie ?? "";
        List<object> ex = new List<object>();
        if (caps != null) foreach (CapSerie c in caps) if (!c.Elegido && c.Relacion != 0) ex.Add(c.Nombre);
        d["excluidos"] = ex;
        try { File.WriteAllText(Ruta(veg), Json.Escribir(d), new UTF8Encoding(false)); } catch { }
    }
}

public static class Serie
{
    public static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    static readonly Regex Patron = new Regex(@"S(\d{1,2})\s*[-_ ]?\s*E(\d{1,3})", RegexOptions.IgnoreCase);
    static readonly Regex Parte = new Regex(@"\b(?:parte|part|cap(?:itulo|\u00edtulo)?|ep(?:isodio)?|episode)\s*[-_ ]?\s*(\d{1,3})", RegexOptions.IgnoreCase);

    // "S01E02 SCR" -> temporada 1, capitulo 2, serie "# scr". Tambien
    // "Parte 3", "Cap 3" o "Ep 3" (temporada 1).
    public static bool Clave(string nombre, out int temporada, out int numero, out string serie)
    {
        temporada = 0; numero = 0; serie = "";
        nombre = nombre ?? "";
        Match m = Patron.Match(nombre);
        if (m.Success) { temporada = int.Parse(m.Groups[1].Value); numero = int.Parse(m.Groups[2].Value); }
        else
        {
            m = Parte.Match(nombre);
            if (!m.Success) return false;
            temporada = 1; numero = int.Parse(m.Groups[1].Value);
        }
        string resto = (nombre.Substring(0, m.Index) + "#" + nombre.Substring(m.Index + m.Length)).ToLowerInvariant();
        serie = Regex.Replace(resto, @"[\s_\-\.]+", " ").Trim();
        return true;
    }

    // Para ordenar capitulos: temporada y numero (los que no tienen, al final).
    public static int Orden(string nombre)
    {
        int t, n;
        string s;
        return Clave(nombre, out t, out n, out s) ? t * 1000 + n : int.MaxValue;
    }

    // .veg de la carpeta y de sus subcarpetas hasta "niveles" de hondo.
    public static List<string> ArchivosVeg(string carpeta, int niveles)
    {
        List<string> l = new List<string>();
        Agregar(l, carpeta, niveles);
        return l;
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

    // ------------------------------------------------------ fichas

    static string QueGuardar(string tipo)
    {
        switch (tipo)
        {
            case "Video ensayo":
                return "\"hilos\": [\"temas, preguntas o argumentos que quedan abiertos o que se retoman en otras partes\"],\n" +
                       " \"recurrentes\": [\"conceptos, ejemplos, personajes o frases que se repiten\"],\n";
            case "Podcast":
                return "\"hilos\": [\"temas pendientes, promesas, debates que siguen en otros episodios\"],\n" +
                       " \"recurrentes\": [\"secciones, chistes internos o frases que se repiten\"],\n";
            default:
                return "\"hilos\": [\"objetivos, promesas, conflictos, rivalidades, objetos o lugares que pueden volver a aparecer\"],\n" +
                       " \"recurrentes\": [\"chistes, apodos o frases que se repiten\"],\n";
        }
    }

    public static string QueEs(string tipo)
    {
        switch (tipo)
        {
            case "Video ensayo": return "una serie de video ensayos en espa\u00f1ol (varias partes)";
            case "Podcast": return "un podcast o serie de charlas en espa\u00f1ol";
            case "Gameplay": return "una serie de gameplays en espa\u00f1ol (con amigos)";
            default: return "una serie de videos de YouTube en espa\u00f1ol";
        }
    }

    public static string InstruccionesFicha() { return InstruccionesFicha("Gameplay"); }

    public static string InstruccionesFicha(string tipo)
    {
        return "Eres editor de " + QueEs(tipo) + ". Recibes la transcripci\u00f3n de lo que qued\u00f3 en un cap\u00edtulo. " +
               "Haz su ficha para usarla de contexto al editar los otros cap\u00edtulos.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"qu\u00e9 pasa en el cap\u00edtulo, en orden, en 3 a 6 frases\",\n " + QueGuardar(tipo) +
               " \"frases\": [{\"inicio\": s, \"fin\": s, \"quien\": \"nombre\", \"texto\": \"lo que se dice\", \"por\": \"por qu\u00e9 es clave\"}]}\n\n" +
               "Reglas:\n- \"frases\": de 5 a 15 frases cortas (2 a 7 s) que mejor cuentan lo importante del cap\u00edtulo; " +
               "sirven para un \"anteriormente\". Usa solo tiempos de la transcripci\u00f3n.\n" +
               "- Nada de datos personales ni charla t\u00e9cnica. Escribe en espa\u00f1ol natural, con los nombres de las personas.";
    }

    public static string MensajeFicha(Episodio e, string notas)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Cap\u00edtulo: " + e.Nombre + "\n");
        if (!String.IsNullOrEmpty(notas)) sb.Append("\nNOTAS DE LA SERIE:\n" + notas.Trim() + "\n");
        sb.Append("\nTranscripci\u00f3n [inicio-fin] persona: texto\n" + e.Transcrito());
        return sb.ToString();
    }

    // Contexto para MomentosIA: la serie, sus notas, anteriores y posteriores.
    public static string Contexto(SerieProyecto serie, List<CapSerie> caps)
    {
        StringBuilder sb = new StringBuilder();
        if (serie != null)
        {
            sb.Append("Serie: " + serie.Nombre + " (" + serie.Tipo + ")\n");
            if (!String.IsNullOrEmpty(serie.Notas)) sb.Append("Notas de la serie:\n" + serie.Notas.Trim() + "\n");
        }
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
                    sb.Append(rel < 0 ? "\nCap\u00edtulos anteriores:\n"
                                      : "\nCap\u00edtulos POSTERIORES (ya grabados): si algo de este cap\u00edtulo prepara lo que se retoma " +
                                        "despu\u00e9s, cons\u00e9rvalo aunque aqu\u00ed parezca menor; no adelantes lo que pasa despu\u00e9s.\n");
                    titulo = true;
                }
                sb.Append("- " + c.Nombre + ": " + f.Texto(false));
            }
        }
        return sb.ToString();
    }

    // La serie del proyecto con sus capitulos y lo que el proyecto excluyo.
    public static List<CapSerie> DelProyecto(string veg, out SerieProyecto serie)
    {
        serie = SerieProyecto.DelProyecto(veg);
        if (serie == null) return new List<CapSerie>();
        List<CapSerie> caps = serie.Capitulos(veg);
        List<string> ex = AjustesProyecto.Excluidos(veg);
        foreach (CapSerie c in caps) if (ex.Contains(c.Nombre)) c.Elegido = false;
        return caps;
    }
}

// ---- src/comun/VentanaSerie.cs ----

// Administrador de series: crear, abrir, ordenar capitulos, notas y fichas.
// Lo abre el script Series y, para elegir la serie del proyecto, MomentosIA
// y Anteriormente (con "Usar esta serie").
class VentanaSeries : VentanaBase
{
    readonly string veg, clave, modelo;
    readonly bool elegir;
    List<string> rutas = new List<string>();
    public SerieProyecto Serie_;
    public List<CapSerie> Caps = new List<CapSerie>();
    bool cargando, trabajando;

    Lista lstSeries = new Lista();
    Boton btnNueva = new Boton("Nueva\u2026", EstiloBoton.Secundario);
    Boton btnAbrir = new Boton("Abrir\u2026", EstiloBoton.Secundario);
    Boton btnOlvidar = new Boton("Quitar de la lista", EstiloBoton.Secundario);
    CampoTexto txtNombre = new CampoTexto();
    Segmentado segTipo = new Segmentado(SerieProyecto.Tipos);
    Etiqueta lblCarpeta, lblEstado;
    Boton btnCarpeta = new Boton("Elegir carpeta\u2026", EstiloBoton.Secundario);
    Boton btnBuscar = new Boton("Buscar cap\u00edtulos ah\u00ed", EstiloBoton.Secundario);
    Lista lstCaps = new Lista();
    Boton btnAgregar = new Boton("Agregar\u2026", EstiloBoton.Secundario);
    Boton btnEste = new Boton("Agregar este proyecto", EstiloBoton.Secundario);
    Boton btnSubir = new Boton("Subir", EstiloBoton.Secundario);
    Boton btnBajar = new Boton("Bajar", EstiloBoton.Secundario);
    Boton btnQuitar = new Boton("Quitar", EstiloBoton.Secundario);
    Boton btnFichas = new Boton("Hacer las fichas que faltan", EstiloBoton.Secundario);
    Boton btnRehacer = new Boton("Rehacer la elegida", EstiloBoton.Secundario);
    CampoTexto txtNotas = new CampoTexto();
    Boton btnSinSerie = new Boton("Sin serie", EstiloBoton.Secundario);
    Boton btnListo;

    public VentanaSeries(string vegActual, string clave, string modelo, bool elegir) : base("Series", 1040)
    {
        this.veg = vegActual ?? ""; this.clave = clave; this.modelo = modelo; this.elegir = elegir;
        if (elegir) StartPosition = FormStartPosition.CenterParent;
        btnListo = new Boton(elegir ? "Usar esta serie" : "Listo", EstiloBoton.Primario);
        int m = Margen, w = Ancho;
        Encabezado("Series", "Proyectos de varias partes: cap\u00edtulos en orden, notas y fichas. MomentosIA y Anteriormente los usan de contexto.");

        // Columna izquierda: series conocidas
        int y = 92, ci = 250;
        Texto("Tus series", Tema.Negrita, Tema.Texto, m, y, ci, 20);
        lstSeries.CheckBoxes = false;
        lstSeries.Columns.Add("Serie", ci - SystemInformation.VerticalScrollBarWidth - 4);
        Pos(lstSeries, m, y + 24, ci, 330);
        Pos(btnNueva, m, y + 362, (ci - 8) / 2, 32);
        Pos(btnAbrir, m + (ci + 8) / 2, y + 362, (ci - 8) / 2, 32);
        Pos(btnOlvidar, m, y + 400, ci, 32);

        // Columna derecha: la serie elegida
        int dx = m + ci + 24, dw = w - ci - 24;
        Texto("NOMBRE", Tema.Pequena, Tema.TextoSuave, dx, y, 200, 18);
        Texto("TIPO", Tema.Pequena, Tema.TextoSuave, dx + dw - 420, y, 200, 18);
        Pos(txtNombre, dx, y + 20, dw - 436, 34);
        Pos(segTipo, dx + dw - 420, y + 20, 420, 34);
        y += 64;
        lblCarpeta = Texto("", Tema.Pequena, Tema.TextoSuave, dx, y + 6, dw - 330, 20);
        Pos(btnCarpeta, dx + dw - 320, y, 150, 30);
        Pos(btnBuscar, dx + dw - 162, y, 162, 30);
        y += 38;
        lstCaps.Columns.Add("#", 36);
        lstCaps.Columns.Add("Cap\u00edtulo", dw - 36 - 90 - 84 - 84 - SystemInformation.VerticalScrollBarWidth - 4);
        lstCaps.Columns.Add("Es", 90);
        lstCaps.Columns.Add("Transcrito", 84);
        lstCaps.Columns.Add("Ficha", 84);
        Pos(lstCaps, dx, y, dw, 200);
        y += 208;
        int bx = dx;
        foreach (KeyValuePair<Boton, int> b in new KeyValuePair<Boton, int>[] {
            new KeyValuePair<Boton, int>(btnAgregar, 100), new KeyValuePair<Boton, int>(btnEste, 180),
            new KeyValuePair<Boton, int>(btnSubir, 80), new KeyValuePair<Boton, int>(btnBajar, 80), new KeyValuePair<Boton, int>(btnQuitar, 90) })
        {
            Pos(b.Key, bx, y, b.Value, 32);
            bx += b.Value + 8;
        }
        y += 40;
        Pos(btnFichas, dx, y, 230, 32);
        Pos(btnRehacer, dx + 238, y, 170, 32);
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, dx + 418, y - 2, dw - 418, 38);
        y += 44;
        Texto("Notas de la serie (personajes, apodos, lugares, de qu\u00e9 va)", Tema.Negrita, Tema.Texto, dx, y, dw, 20);
        txtNotas.Multilinea = true;
        Pos(txtNotas, dx, y + 22, dw, 90);
        y += 124;
        if (elegir) Pos(btnSinSerie, m + w - 300, y, 130, 40);
        Pos(btnListo, m + w - 160, y, 160, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        // Eventos
        lstSeries.SelectedIndexChanged += delegate
        {
            if (cargando || lstSeries.SelectedIndices.Count == 0) return;
            GuardarActual();
            Abrir(rutas[lstSeries.SelectedIndices[0]]);
        };
        btnNueva.Click += delegate { Nueva(); };
        btnAbrir.Click += delegate { AbrirArchivo(); };
        btnOlvidar.Click += delegate
        {
            if (Serie_ == null) return;
            SerieProyecto.Olvidar(Serie_.Ruta);
            Serie_ = null;
            LlenarSeries(null);
            Mostrar();
            Estado("La serie se quit\u00f3 de la lista (su archivo sigue en su carpeta; \u201cAbrir\u2026\u201d la trae de vuelta).", false);
        };
        btnCarpeta.Click += delegate { ElegirCarpeta(); };
        btnBuscar.Click += delegate
        {
            if (Serie_ == null) return;
            int n = Serie_.BuscarEnCarpeta();
            Cambio();
            Estado(n == 0 ? "No encontr\u00e9 cap\u00edtulos nuevos (busca .veg con S01E02, \u201cParte 2\u201d, \u201cCap 2\u201d\u2026 en el nombre)." : n + " cap\u00edtulos agregados.", false);
        };
        btnAgregar.Click += delegate { AgregarArchivos(); };
        btnEste.Click += delegate { if (Serie_ != null && veg.Length > 0) { Serie_.Agregar(veg); Cambio(); } };
        btnSubir.Click += delegate { Mover(-1); };
        btnBajar.Click += delegate { Mover(1); };
        btnQuitar.Click += delegate
        {
            int i = Elegido();
            if (i < 0) return;
            Serie_.Episodios.RemoveAt(i);
            Cambio();
        };
        btnFichas.Click += delegate { Fichas(false); };
        btnRehacer.Click += delegate { Fichas(true); };
        lstCaps.ItemCheck += delegate (object s, ItemCheckEventArgs e)
        {
            if (cargando) return;
            CapSerie c = (CapSerie)lstCaps.Items[e.Index].Tag;
            if (c.Relacion == 0 || !c.Transcrito) e.NewValue = CheckState.Unchecked;
        };
        lstCaps.ItemChecked += delegate (object s, ItemCheckedEventArgs e) { if (!cargando) ((CapSerie)e.Item.Tag).Elegido = e.Item.Checked; };
        btnSinSerie.Click += delegate { Serie_ = null; Caps = new List<CapSerie>(); AjustesProyecto.Guardar(veg, "", null); DialogResult = DialogResult.OK; Close(); };
        btnListo.Click += delegate
        {
            if (trabajando) return;
            GuardarActual();
            if (veg.Length > 0 && Serie_ != null) AjustesProyecto.Guardar(veg, Serie_.Ruta, Caps);
            DialogResult = DialogResult.OK;
            Close();
        };
        FormClosing += delegate (object s, FormClosingEventArgs e)
        {
            if (trabajando) { e.Cancel = true; return; }
            GuardarActual();
        };

        // Inicio: la serie del proyecto abierto, o la ultima usada.
        SerieProyecto delProyecto = veg.Length > 0 ? SerieProyecto.DelProyecto(veg) : null;
        LlenarSeries(delProyecto != null ? delProyecto.Ruta : null);
        if (delProyecto != null) Abrir(delProyecto.Ruta);
        else if (rutas.Count > 0 && !elegir) Abrir(rutas[0]);
        else Mostrar();
        if (Serie_ == null)
            Estado(rutas.Count == 0 ? "Crea tu primera serie con \u201cNueva\u2026\u201d." : "Elige una serie de la lista o crea una nueva.", false);
        else if (veg.Length > 0 && Serie_.IndiceDe(veg) < 0)
            Estado("Este proyecto no est\u00e1 en la serie: \u201cAgregar este proyecto\u201d lo pone en su lugar.", false);
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void LlenarSeries(string elegida)
    {
        cargando = true;
        rutas = SerieProyecto.Registradas();
        lstSeries.Items.Clear();
        foreach (string r in rutas)
        {
            string nombre = Path.GetFileName(r).Replace(SerieProyecto.Extension, "");
            try { nombre = SerieProyecto.Cargar(r).Nombre; } catch { }
            ListViewItem it = new ListViewItem(nombre);
            if (elegida != null && String.Equals(r, elegida, StringComparison.OrdinalIgnoreCase)) it.Selected = true;
            lstSeries.Items.Add(it);
        }
        cargando = false;
    }

    void Abrir(string ruta)
    {
        try
        {
            Serie_ = SerieProyecto.Cargar(ruta);
            SerieProyecto.Registrar(ruta);
        }
        catch (Exception ex) { Serie_ = null; Estado("No se pudo abrir la serie: " + ex.Message, true); }
        Mostrar();
    }

    // Pone en pantalla la serie elegida.
    void Mostrar()
    {
        bool hay = Serie_ != null;
        foreach (Control c in new Control[] { txtNombre, segTipo, btnCarpeta, btnBuscar, lstCaps, btnAgregar, btnEste, btnSubir,
                                              btnBajar, btnQuitar, btnFichas, btnRehacer, txtNotas, btnOlvidar })
            c.Enabled = hay;
        btnListo.Enabled = hay || !elegir;
        cargando = true;
        txtNombre.Text = hay ? Serie_.Nombre : "";
        segTipo.Seleccion = hay ? Math.Max(0, Array.IndexOf(SerieProyecto.Tipos, Serie_.Tipo)) : 0;
        txtNotas.Text = hay ? (Serie_.Notas ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n") : "";
        cargando = false;
        btnEste.Enabled = hay && veg.Length > 0 && Serie_.IndiceDe(veg) < 0;
        LlenarCapitulos();
    }

    void LlenarCapitulos()
    {
        List<string> fuera = AjustesProyecto.Excluidos(veg);
        foreach (CapSerie c in Caps) if (!c.Elegido && !fuera.Contains(c.Nombre)) fuera.Add(c.Nombre);
        Caps = Serie_ != null ? Serie_.Capitulos(veg) : new List<CapSerie>();
        foreach (CapSerie c in Caps) if (fuera.Contains(c.Nombre)) c.Elegido = false;
        cargando = true;
        lstCaps.Items.Clear();
        foreach (CapSerie c in Caps)
        {
            ListViewItem it = new ListViewItem(c.Posicion.ToString());
            it.SubItems.Add(c.Nombre);
            it.SubItems.Add(!c.Existe ? "no se encuentra" : c.Relacion < 0 ? (veg.Length > 0 && Serie_.IndiceDe(veg) >= 0 ? "anterior" : "\u2014") :
                            c.Relacion > 0 ? "posterior" : "este");
            it.SubItems.Add(c.Transcrito ? "s\u00ed" : "no");
            it.SubItems.Add(c.TieneFicha ? "s\u00ed" : c.Transcrito ? "falta" : "\u2014");
            it.Checked = c.Relacion != 0 && c.Transcrito && c.Elegido;
            if (c.Relacion == 0 || !c.Transcrito) it.ForeColor = Tema.TextoSuave;
            it.Tag = c;
            lstCaps.Items.Add(it);
        }
        cargando = false;
        lblCarpeta.Text = Serie_ == null ? "" : "Carpeta: " + (String.IsNullOrEmpty(Serie_.Carpeta) ? "(sin elegir)" : Serie_.Carpeta) +
                                                " \u00b7 " + Caps.Count + " cap\u00edtulos";
    }

    void Cambio()
    {
        GuardarActual();
        btnEste.Enabled = Serie_ != null && veg.Length > 0 && Serie_.IndiceDe(veg) < 0;
        LlenarCapitulos();
    }

    void GuardarActual()
    {
        if (Serie_ == null || cargando) return;
        Serie_.Nombre = txtNombre.Text.Trim().Length > 0 ? txtNombre.Text.Trim() : Serie_.Nombre;
        Serie_.Tipo = SerieProyecto.Tipos[Math.Max(0, segTipo.Seleccion)];
        Serie_.Notas = txtNotas.Text.Trim();
        try { Serie_.Guardar(); } catch (Exception ex) { Estado("No se pudo guardar la serie: " + ex.Message, true); }
    }

    int Elegido() { return lstCaps.SelectedIndices.Count == 0 ? -1 : lstCaps.SelectedIndices[0]; }

    void Mover(int d)
    {
        int i = Elegido(), j = i + d;
        if (i < 0 || j < 0 || j >= Serie_.Episodios.Count) return;
        string x = Serie_.Episodios[i];
        Serie_.Episodios[i] = Serie_.Episodios[j];
        Serie_.Episodios[j] = x;
        Cambio();
        lstCaps.Items[j].Selected = true;
    }

    // ------------------------------------------------- crear y abrir

    string CarpetaSugerida()
    {
        if (veg.Length == 0) return "";
        string dir = Path.GetDirectoryName(veg);
        int t, n; string k;
        // Si cada capitulo tiene su carpeta (S01E02/...), la serie va en la de arriba.
        return Serie.Clave(Path.GetFileName(dir), out t, out n, out k) ? Path.GetDirectoryName(dir) : dir;
    }

    void Nueva()
    {
        GuardarActual();
        string sugerido = "";
        int t, n; string k;
        if (veg.Length > 0 && Serie.Clave(Path.GetFileNameWithoutExtension(veg), out t, out n, out k))
            sugerido = k.Replace("#", "").Trim().ToUpperInvariant();
        string nombre;
        using (DialogoNombre d = new DialogoNombre(sugerido, "Nueva serie", "Nombre de la serie"))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            nombre = d.Nombre;
        }
        string carpeta;
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta de la serie (donde est\u00e1n o estar\u00e1n sus cap\u00edtulos). Ah\u00ed se guarda el archivo de la serie.";
            string s = CarpetaSugerida();
            if (s.Length > 0 && Directory.Exists(s)) d.SelectedPath = s;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            carpeta = d.SelectedPath;
        }
        SerieProyecto nueva = new SerieProyecto();
        nueva.Nombre = nombre;
        nueva.Carpeta = carpeta;
        nueva.Ruta = SerieProyecto.RutaPara(carpeta, nombre);
        if (File.Exists(nueva.Ruta)) { Estado("Ya hay una serie con ese nombre en esa carpeta: \u00e1brela con \u201cAbrir\u2026\u201d.", true); return; }
        if (veg.Length > 0) nueva.Agregar(veg);
        int encontrados = nueva.BuscarEnCarpeta();
        try { nueva.Guardar(); }
        catch (Exception ex) { Estado("No se pudo crear la serie: " + ex.Message, true); return; }
        LlenarSeries(nueva.Ruta);
        Abrir(nueva.Ruta);
        Estado("\u2714 Serie creada" + (encontrados > 0 ? " con " + Serie_.Episodios.Count + " cap\u00edtulos encontrados en la carpeta" : "") +
               ". Revisa el orden y escribe las notas.", false);
    }

    void AbrirArchivo()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Archivo de una serie";
            d.Filter = "Series de vegas-cut|*" + SerieProyecto.Extension;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            GuardarActual();
            SerieProyecto.Registrar(d.FileName);
            LlenarSeries(d.FileName);
            Abrir(d.FileName);
        }
    }

    void ElegirCarpeta()
    {
        using (FolderBrowserDialog d = new FolderBrowserDialog())
        {
            d.Description = "Carpeta donde est\u00e1n los cap\u00edtulos (se busca tambi\u00e9n en sus subcarpetas)";
            if (!String.IsNullOrEmpty(Serie_.Carpeta) && Directory.Exists(Serie_.Carpeta)) d.SelectedPath = Serie_.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            Serie_.Carpeta = d.SelectedPath;
        }
        Cambio();
    }

    void AgregarArchivos()
    {
        using (OpenFileDialog d = new OpenFileDialog())
        {
            d.Title = "Cap\u00edtulos de la serie";
            d.Filter = "Proyectos de Vegas|*.veg";
            d.Multiselect = true;
            if (!String.IsNullOrEmpty(Serie_.Carpeta) && Directory.Exists(Serie_.Carpeta)) d.InitialDirectory = Serie_.Carpeta;
            if (d.ShowDialog(this) != DialogResult.OK) return;
            foreach (string f in d.FileNames) Serie_.Agregar(f);
        }
        Cambio();
    }

    // ------------------------------------------------------------ fichas

    void Fichas(bool rehacer)
    {
        if (String.IsNullOrEmpty(clave)) { Estado("Falta la clave de Gemini: ejecuta \u201cConfigurarVegasCut\u201d.", true); return; }
        GuardarActual();
        List<CapSerie> cola = new List<CapSerie>();
        if (rehacer)
        {
            int i = Elegido();
            if (i < 0) { Estado("Elige un cap\u00edtulo de la lista.", true); return; }
            if (!Caps[i].Transcrito) { Estado(Caps[i].Nombre + " no est\u00e1 transcrito.", true); return; }
            cola.Add(Caps[i]);
        }
        else
            foreach (CapSerie c in Caps) if (c.Transcrito && !c.TieneFicha) cola.Add(c);
        if (cola.Count == 0) { Estado("Todos los cap\u00edtulos transcritos ya tienen ficha.", false); return; }

        string notas = Serie_.Notas, tipo = Serie_.Tipo;
        trabajando = true;
        Habilitar(false);
        Thread hilo = new Thread(delegate ()
        {
            List<string> errores = new List<string>();
            for (int i = 0; i < cola.Count; i++)
            {
                CapSerie c = cola[i];
                Avisar("Haciendo la ficha de " + c.Nombre + " (" + (i + 1) + " de " + cola.Count + ")\u2026");
                try
                {
                    Episodio e = Episodio.Abrir(c.Veg);
                    Ficha f = Ficha.Leer(Gemini.Generar(clave, modelo, Serie.InstruccionesFicha(tipo), Serie.MensajeFicha(e, notas), true));
                    f.Generada = DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " \u00b7 " + modelo;
                    f.Guardar(c.Veg);
                }
                catch (Exception ex) { errores.Add(c.Nombre + ": " + ex.Message); }
            }
            try
            {
                BeginInvoke((MethodInvoker)delegate
                {
                    trabajando = false;
                    Habilitar(true);
                    LlenarCapitulos();
                    if (errores.Count > 0) Estado(String.Join("\n", errores.ToArray()), true);
                    else Estado("\u2714 Fichas listas.", false);
                });
            }
            catch { }
        });
        hilo.IsBackground = true;
        hilo.Start();
    }

    void Habilitar(bool si)
    {
        foreach (Control c in new Control[] { btnFichas, btnRehacer, btnListo, btnNueva, btnAbrir, lstSeries, btnAgregar, btnQuitar, btnSubir, btnBajar })
            c.Enabled = si;
    }

    void Avisar(string t)
    {
        try { BeginInvoke((MethodInvoker)delegate { Estado(t, false); }); } catch { }
    }

    // "Serie Steel Ball Run: 2 anteriores \u00b7 1 posterior \u00b7 1 sin ficha"
    public static string Resumen(SerieProyecto serie, List<CapSerie> caps)
    {
        if (serie == null) return "Sin serie (pulsa \u201cSerie\u2026\u201d para elegirla o crearla).";
        int ant = 0, pos = 0, sin = 0;
        bool esta = caps.Exists(delegate (CapSerie c) { return c.Relacion == 0; });
        foreach (CapSerie c in caps)
        {
            if (c.Relacion == 0 || !c.Elegido || !c.Transcrito) continue;
            if (c.Relacion < 0) ant++; else pos++;
            if (!c.TieneFicha) sin++;
        }
        return "Serie " + serie.Nombre + ": " + ant + (ant == 1 ? " anterior" : " anteriores") +
               (pos > 0 ? " \u00b7 " + pos + (pos == 1 ? " posterior" : " posteriores") : "") +
               (sin > 0 ? " \u00b7 " + sin + " sin ficha" : "") + (esta ? "" : " \u00b7 este proyecto no est\u00e1 en la serie");
    }
}

// ---- src/comun/Audio.cs ----

// =====================================================================
// Analisis de audio y deteccion (sin dependencias de Vegas)
// =====================================================================

public struct Rango
{
    public double Inicio, Fin; // segundos en la linea de tiempo
    public Rango(double inicio, double fin) { Inicio = inicio; Fin = fin; }
}

// Tramo que se reproduce mas rapido (Factor 2 = el doble de rapido).
public class Acelerado
{
    public double Inicio, Fin, Factor;
    public Acelerado(double inicio, double fin, double factor) { Inicio = inicio; Fin = fin; Factor = factor; }

    public double Ahorro { get { return (Fin - Inicio) * (1 - 1 / Factor); } }

    // Nueva posicion de un instante despues de acelerar los tramos.
    public static double Posicion(double t, List<Acelerado> tramos)
    {
        double ahorro = 0;
        foreach (Acelerado a in tramos)
        {
            if (t >= a.Fin - 1e-6) ahorro += a.Ahorro;
            else if (t > a.Inicio) ahorro += (t - a.Inicio) * (1 - 1 / a.Factor);
        }
        return t - ahorro;
    }
}

public class Analisis
{
    public const double Paso = 0.01;  // 10 ms por medicion
    public float[] Db;                // nivel RMS de cada paso, en dBFS
    public double Inicio;             // segundo de la linea de tiempo del primer paso
    public double Duracion { get { return Db.Length * Paso; } }

    // Une varias pistas: en cada instante cuenta la que suene mas fuerte, asi
    // hay voz si habla cualquiera de ellas.
    public static Analisis Combinar(List<Analisis> pistas)
    {
        if (pistas.Count == 1) return pistas[0];
        int n = int.MaxValue;
        foreach (Analisis a in pistas) n = Math.Min(n, a.Db.Length);
        Analisis r = new Analisis();
        r.Inicio = pistas[0].Inicio;
        r.Db = new float[n];
        for (int i = 0; i < n; i++)
        {
            float m = -100;
            foreach (Analisis a in pistas) if (a.Db[i] > m) m = a.Db[i];
            r.Db[i] = m;
        }
        return r;
    }
}

public static class WavNiveles
{
    public static Analisis Leer(string ruta, double paso)
    {
        using (FileStream fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        using (BinaryReader br = new BinaryReader(fs))
        {
            if (new string(br.ReadChars(4)) != "RIFF") throw new Exception("El archivo no es WAV.");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new Exception("El archivo no es WAV.");

            int formato = 0, canales = 0, frecuencia = 0, bits = 0;
            long datos = -1, largo = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(br.ReadChars(4));
                long tam = br.ReadUInt32();
                long siguiente = fs.Position + tam + (tam & 1);
                if (id == "fmt ")
                {
                    formato = br.ReadUInt16();
                    canales = br.ReadUInt16();
                    frecuencia = br.ReadInt32();
                    br.ReadInt32(); br.ReadUInt16();
                    bits = br.ReadUInt16();
                    if (formato == 0xFFFE && tam >= 40)
                    {
                        br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32();
                        formato = br.ReadUInt16(); // subformato: 1 PCM, 3 float
                    }
                }
                else if (id == "data")
                {
                    datos = fs.Position;
                    largo = Math.Min(tam, fs.Length - datos);
                    break;
                }
                fs.Position = siguiente;
            }
            if (datos < 0 || canales == 0) throw new Exception("WAV sin datos de audio.");
            if (!(formato == 1 && (bits == 16 || bits == 24 || bits == 32)) && !(formato == 3 && bits == 32))
                throw new Exception("Formato WAV no soportado (" + formato + ", " + bits + " bits).");

            int bytesMuestra = bits / 8;
            int bytesCuadro = bytesMuestra * canales;
            int cuadrosPorPaso = Math.Max(1, (int)Math.Round(frecuencia * paso));
            long cuadros = largo / bytesCuadro;
            int pasos = (int)(cuadros / cuadrosPorPaso);
            float[] db = new float[pasos];

            byte[] buf = new byte[cuadrosPorPaso * bytesCuadro];
            fs.Position = datos;
            for (int p = 0; p < pasos; p++)
            {
                int leidos = 0;
                while (leidos < buf.Length)
                {
                    int n = fs.Read(buf, leidos, buf.Length - leidos);
                    if (n <= 0) break;
                    leidos += n;
                }
                double suma = 0;
                int muestras = leidos / bytesMuestra;
                for (int i = 0; i < muestras; i++)
                {
                    int o = i * bytesMuestra;
                    double x;
                    if (formato == 3) x = BitConverter.ToSingle(buf, o);
                    else if (bits == 16) x = BitConverter.ToInt16(buf, o) / 32768.0;
                    else if (bits == 24) x = ((buf[o] | (buf[o + 1] << 8) | ((sbyte)buf[o + 2] << 16))) / 8388608.0;
                    else x = BitConverter.ToInt32(buf, o) / 2147483648.0;
                    suma += x * x;
                }
                double rms = muestras > 0 ? Math.Sqrt(suma / muestras) : 0;
                db[p] = rms > 1e-5 ? (float)(20 * Math.Log10(rms)) : -100f;
            }

            Analisis a = new Analisis();
            a.Db = db;
            return a;
        }
    }
}


public static class Formato
{
    public static string Tiempo(double s)
    {
        if (s < 0) s = 0;
        int t = (int)Math.Round(s);
        if (t >= 3600) return (t / 3600) + ":" + ((t / 60) % 60).ToString("00") + ":" + (t % 60).ToString("00");
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    // Con decimas: 1:02.5
    public static string TiempoPreciso(double s)
    {
        if (s < 0) s = 0;
        int d = (int)Math.Round(s * 10);
        int t = d / 10;
        string r = ((t / 60) % 60).ToString(t >= 3600 ? "00" : "0") + ":" + (t % 60).ToString("00") + "." + (d % 10);
        return t >= 3600 ? (t / 3600) + ":" + r : r;
    }
}

// ---- src/comun/Json.cs ----

// =====================================================================
// JSON minimo (Vegas no trae una libreria de JSON)
//   Objeto -> Dictionary<string, object>, lista -> List<object>,
//   numero -> double, texto -> string, true/false -> bool, null -> null.
// =====================================================================

public static class Json
{
    // ------------------------------------------------------------ escribir

    public static string Escribir(object valor) { return Escribir(valor, true); }

    public static string Escribir(object valor, bool sangria)
    {
        StringBuilder sb = new StringBuilder();
        Valor(sb, valor, 0, sangria);
        return sb.ToString();
    }

    static void Valor(StringBuilder sb, object v, int nivel, bool sangria)
    {
        if (v == null) { sb.Append("null"); return; }
        if (v is string) { Cadena(sb, (string)v); return; }
        if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
        if (v is double || v is float || v is decimal)
        {
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (Double.IsNaN(d) || Double.IsInfinity(d)) sb.Append("null");
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            return;
        }
        if (v.GetType().IsPrimitive) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
        if (v is Enum) { Cadena(sb, v.ToString()); return; }

        IDictionary dic = v as IDictionary;
        if (dic != null)
        {
            if (dic.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool primero = true;
            foreach (DictionaryEntry e in dic)
            {
                if (!primero) sb.Append(',');
                primero = false;
                Salto(sb, nivel + 1, sangria);
                Cadena(sb, e.Key.ToString());
                sb.Append(sangria ? ": " : ":");
                Valor(sb, e.Value, nivel + 1, sangria);
            }
            Salto(sb, nivel, sangria);
            sb.Append('}');
            return;
        }

        IEnumerable lista = v as IEnumerable;
        if (lista != null)
        {
            // Listas de numeros en una sola linea (niveles de sonido): mucho mas compacto.
            bool simple = true, vacia = true;
            foreach (object o in lista) { vacia = false; if (o is IDictionary || (o is IEnumerable && !(o is string))) { simple = false; break; } }
            if (vacia) { sb.Append("[]"); return; }
            sb.Append('[');
            bool primero = true;
            foreach (object o in lista)
            {
                if (!primero) sb.Append(',');
                primero = false;
                if (!simple) Salto(sb, nivel + 1, sangria);
                Valor(sb, o, nivel + 1, sangria);
            }
            if (!simple) Salto(sb, nivel, sangria);
            sb.Append(']');
            return;
        }

        Cadena(sb, v.ToString());
    }

    static void Salto(StringBuilder sb, int nivel, bool sangria)
    {
        if (!sangria) return;
        sb.Append('\n');
        sb.Append(' ', nivel * 2);
    }

    static void Cadena(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }

    // --------------------------------------------------------------- leer

    public static object Leer(string texto)
    {
        int i = 0;
        object v = LeerValor(texto, ref i);
        Espacios(texto, ref i);
        if (i < texto.Length) throw new FormatException("JSON con texto de sobra en la posici\u00f3n " + i);
        return v;
    }

    static void Espacios(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static Exception Error(string s, int i, string que)
    {
        return new FormatException("JSON inv\u00e1lido (" + que + ") en la posici\u00f3n " + i);
    }

    static object LeerValor(string s, ref int i)
    {
        Espacios(s, ref i);
        if (i >= s.Length) throw Error(s, i, "fin inesperado");
        char c = s[i];
        if (c == '{') return LeerObjeto(s, ref i);
        if (c == '[') return LeerLista(s, ref i);
        if (c == '"') return LeerCadena(s, ref i);
        if (c == 't' && Sigue(s, i, "true")) { i += 4; return true; }
        if (c == 'f' && Sigue(s, i, "false")) { i += 5; return false; }
        if (c == 'n' && Sigue(s, i, "null")) { i += 4; return null; }
        if (c == '-' || char.IsDigit(c)) return LeerNumero(s, ref i);
        throw Error(s, i, "car\u00e1cter '" + c + "'");
    }

    static bool Sigue(string s, int i, string palabra)
    {
        return String.CompareOrdinal(s, i, palabra, 0, palabra.Length) == 0;
    }

    static Dictionary<string, object> LeerObjeto(string s, ref int i)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        i++; // {
        Espacios(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (true)
        {
            Espacios(s, ref i);
            if (i >= s.Length || s[i] != '"') throw Error(s, i, "se esperaba una clave");
            string clave = LeerCadena(s, ref i);
            Espacios(s, ref i);
            if (i >= s.Length || s[i] != ':') throw Error(s, i, "se esperaba ':'");
            i++;
            d[clave] = LeerValor(s, ref i);
            Espacios(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; return d; }
            throw Error(s, i, "se esperaba ',' o '}'");
        }
    }

    static List<object> LeerLista(string s, ref int i)
    {
        List<object> l = new List<object>();
        i++; // [
        Espacios(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return l; }
        while (true)
        {
            l.Add(LeerValor(s, ref i));
            Espacios(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; return l; }
            throw Error(s, i, "se esperaba ',' o ']'");
        }
    }

    static string LeerCadena(string s, ref int i)
    {
        StringBuilder sb = new StringBuilder();
        i++; // "
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 > s.Length) throw Error(s, i, "escape \\u incompleto");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break; // \" \\ \/
            }
        }
        throw Error(s, i, "texto sin cerrar");
    }

    static double LeerNumero(string s, ref int i)
    {
        int ini = i;
        if (s[i] == '-') i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
        return double.Parse(s.Substring(ini, i - ini), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // ----------------------------------------------------- acceso comodo

    public static Dictionary<string, object> Obj(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        return d != null && d.TryGetValue(clave, out v) ? v as Dictionary<string, object> : null;
    }

    public static List<object> Lista(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        List<object> l = d != null && d.TryGetValue(clave, out v) ? v as List<object> : null;
        return l ?? new List<object>();
    }

    public static string Texto(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        if (d == null || !d.TryGetValue(clave, out v) || v == null) return "";
        return v is string ? (string)v : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public static double Numero(object o, string clave, double siNo)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        if (d == null || !d.TryGetValue(clave, out v) || v == null) return siNo;
        if (v is double) return (double)v;
        double r;
        return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out r) ? r : siNo;
    }
}

// ---- src/comun/Transcripcion.cs ----

// =====================================================================
// Transcripcion del proyecto (<proyecto>.vegascut.json junto al .veg)
//
// Guarda los tiempos tal como estaban al transcribir y una lista de los
// cortes que hicieron las herramientas despues. Asi los tiempos se pueden
// llevar a la linea de tiempo actual, y si deshaces un corte (Ctrl+Z) se
// nota porque la duracion del proyecto vuelve a la de antes.
// =====================================================================

public class Palabra
{
    public double Inicio, Fin, Prob;
    public string Texto;
}

public class Segmento
{
    public int Hablante;
    public double Inicio, Fin;
    public string Texto;
    public List<Palabra> Palabras = new List<Palabra>();
}

public class Hablante
{
    public string Etiqueta;  // "A11"
    public string Nombre;    // como se llama la persona (editable)
    public string Archivo;
    public bool Voz;         // true: se transcribio; false: solo niveles (juego, musica)
    public float[] Nivel;    // dB RMS por segundo
    public float[] Pico;     // dB maximo por segundo
    public List<Fuente> Fuentes = new List<Fuente>(); // de donde salia cada parte al transcribir
}

// Un evento de la pista al transcribir: que archivo (y flujo de audio) sonaba
// de Inicio a Fin y desde que segundo del archivo. Con esto una palabra se
// puede encontrar en la linea de tiempo aunque despues edites a mano.
public class Fuente
{
    public double Inicio, Fin, Desde, Velocidad = 1;
    public string Media = "";
    public int Flujo;
}

public class Edicion
{
    public List<Rango> Quitados = new List<Rango>();
    public List<Acelerado> Acelerados = new List<Acelerado>();
    public double Antes, Despues; // duracion del proyecto
}

public class Transcripcion
{
    public string Proyecto = "", Creada = "", Idioma = "es", Modelo = "";
    public double Inicio, Duracion;      // rango transcrito (tiempos originales)
    public double DuracionProyecto;      // al transcribir
    public List<Hablante> Hablantes = new List<Hablante>();
    public List<Segmento> Segmentos = new List<Segmento>();
    public List<Edicion> Ediciones = new List<Edicion>();

    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut.json");
    }

    // --------------------------------------------------- tiempos actuales

    // Si esta puesto, lleva (hablante, tiempo original) a la linea de tiempo
    // actual buscando el archivo y segundo de cada palabra (fuentes): asi la
    // transcripcion sigue cualquier edicion, tambien las hechas a mano. Lo
    // ponen las herramientas que tienen el proyecto de Vegas a mano.
    public Func<int, double, double> Ubicador;

    public double Mapear(int hablante, double t)
    {
        if (Ubicador != null && hablante >= 0 && hablante < Hablantes.Count && Hablantes[hablante].Fuentes.Count > 0)
            return Ubicador(hablante, t);
        return Mapear(t);
    }

    // Lleva un tiempo original a la linea de tiempo actual. Devuelve NaN si
    // ese instante fue cortado.
    public double Mapear(double t)
    {
        foreach (Edicion e in Ediciones)
        {
            if (e.Acelerados.Count > 0) t = Acelerado.Posicion(t, e.Acelerados);
            double q = 0;
            foreach (Rango r in e.Quitados)
            {
                if (t >= r.Fin - 1e-6) q += r.Fin - r.Inicio;
                else if (t > r.Inicio + 1e-6) return double.NaN;
            }
            t -= q;
        }
        return t;
    }

    // Archivo, flujo y segundo del archivo que sonaba en el instante
    // original t en la pista de ese hablante.
    public bool AFuente(int hablante, double t, out Fuente f, out double segundo)
    {
        f = null; segundo = 0;
        if (hablante < 0 || hablante >= Hablantes.Count) return false;
        foreach (Fuente x in Hablantes[hablante].Fuentes)
            if (t >= x.Inicio - 1e-6 && t < x.Fin - 1e-6)
            {
                f = x;
                segundo = x.Desde + (t - x.Inicio) * x.Velocidad;
                return true;
            }
        return false;
    }

    // Frases que Whisper inventa en los silencios (vienen de los subtitulos de
    // YouTube con los que se entreno). No se le mandan a la IA.
    static readonly string[] Inventadas = { "suscribeteacanal", "suscribeteanuestrocanal", "suscribete", "graciasporver",
        "subtitulosrealizadosporlacomunidaddeamaraorg", "subtitulosporlacomunidaddeamaraorg", "amaraorg",
        "noolvidesdesuscribirte", "dalelike" };

    public static bool Alucinacion(string texto)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (texto ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(c);
        string n = sb.ToString().Replace("suscribetealcanal", "suscribeteacanal");
        if (n.Length == 0) return false;
        foreach (string x in Inventadas)
            if (n == x || (x.Length >= 10 && n.Contains(x) && n.Length <= x.Length + 12)) return true;
        return false;
    }

    public bool TieneFuentes
    {
        get { foreach (Hablante h in Hablantes) if (h.Fuentes.Count > 0) return true; return false; }
    }

    // Segmentos con tiempos de la linea de tiempo actual, sin lo cortado.
    public List<Segmento> SegmentosActuales()
    {
        List<Segmento> r = new List<Segmento>();
        foreach (Segmento s in Segmentos)
        {
            if (Alucinacion(s.Texto)) continue;
            Segmento n = new Segmento();
            n.Hablante = s.Hablante;
            StringBuilder texto = new StringBuilder();
            if (s.Palabras.Count > 0)
            {
                foreach (Palabra p in s.Palabras)
                {
                    double a = Mapear(s.Hablante, p.Inicio), b = Mapear(s.Hablante, p.Fin);
                    if (double.IsNaN(a) && double.IsNaN(b)) continue;
                    if (double.IsNaN(a)) a = b - Math.Min(0.2, p.Fin - p.Inicio);
                    if (double.IsNaN(b)) b = a + Math.Min(0.2, p.Fin - p.Inicio);
                    Palabra q = new Palabra();
                    q.Inicio = a; q.Fin = b; q.Prob = p.Prob; q.Texto = p.Texto;
                    n.Palabras.Add(q);
                    texto.Append(p.Texto);
                }
                if (n.Palabras.Count == 0) continue;
                n.Inicio = n.Palabras[0].Inicio;
                n.Fin = n.Palabras[n.Palabras.Count - 1].Fin;
                n.Texto = texto.ToString().Trim();
            }
            else
            {
                n.Inicio = Mapear(s.Hablante, s.Inicio); n.Fin = Mapear(s.Hablante, s.Fin); n.Texto = s.Texto;
                if (double.IsNaN(n.Inicio) || double.IsNaN(n.Fin)) continue;
            }
            r.Add(n);
        }
        r.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
        return r;
    }

    public double DuracionEsperada
    {
        get { return Ediciones.Count > 0 ? Ediciones[Ediciones.Count - 1].Despues : DuracionProyecto; }
    }

    // Compara con la duracion actual del proyecto. Si deshiciste cortes
    // (Ctrl+Z), los quita de la lista. Devuelve "" si todo cuadra o un aviso.
    public string Sincronizar(double duracionActual)
    {
        const double tol = 0.05;
        bool cambio = false;
        while (Ediciones.Count > 0 && Math.Abs(DuracionEsperada - duracionActual) > tol &&
               Math.Abs(Ediciones[Ediciones.Count - 1].Antes - duracionActual) <= tol)
        {
            Ediciones.RemoveAt(Ediciones.Count - 1);
            cambio = true;
        }
        if (Math.Abs(DuracionEsperada - duracionActual) <= tol)
            return cambio ? "Se detect\u00f3 un Ctrl+Z: la transcripci\u00f3n se ajust\u00f3." : "";
        return "El proyecto cambi\u00f3 desde la transcripci\u00f3n (dura " + Formato.Tiempo(duracionActual) +
               ", se esperaba " + Formato.Tiempo(DuracionEsperada) + "). Si editaste a mano, los tiempos " +
               "pueden no cuadrar: vuelve a transcribir para m\u00e1s precisi\u00f3n.";
    }

    // La llaman las herramientas que cortan (Quitar silencios, Momentos).
    public static string RegistrarCortes(string veg, List<Rango> quitados, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Quitados.AddRange(quitados);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    public static string RegistrarAceleracion(string veg, List<Acelerado> tramos, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Acelerados.AddRange(tramos);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    static string RegistrarEdicion(string veg, Edicion e)
    {
        string ruta = RutaPara(veg);
        if (ruta == null || !File.Exists(ruta)) return "";
        try
        {
            Transcripcion t = Cargar(ruta);
            t.Sincronizar(e.Antes);
            t.Ediciones.Add(e);
            t.Guardar(ruta);
            return "\nLa transcripci\u00f3n tambi\u00e9n se ajust\u00f3 a los cortes.";
        }
        catch (Exception ex)
        {
            return "\nNo se pudo ajustar la transcripci\u00f3n: " + ex.Message;
        }
    }

    // ------------------------------------------------- niveles por segundo

    public static void NivelesPorSegundo(Analisis a, out float[] nivel, out float[] pico)
    {
        int porSegundo = (int)Math.Round(1 / Analisis.Paso);
        int n = (a.Db.Length + porSegundo - 1) / porSegundo;
        nivel = new float[n];
        pico = new float[n];
        for (int s = 0; s < n; s++)
        {
            double energia = 0;
            float max = -100;
            int cuantos = 0;
            for (int i = s * porSegundo; i < Math.Min(a.Db.Length, (s + 1) * porSegundo); i++)
            {
                energia += Math.Pow(10, a.Db[i] / 10.0);
                if (a.Db[i] > max) max = a.Db[i];
                cuantos++;
            }
            nivel[s] = cuantos > 0 ? (float)Math.Round(10 * Math.Log10(Math.Max(1e-10, energia / cuantos))) : -100;
            pico[s] = (float)Math.Round(max);
        }
    }

    // ------------------------------------------------------ Whisper (JSON)

    // Agrega los segmentos de la salida JSON de Whisper (tiempos relativos al
    // WAV) desplazados al inicio del rango.
    public void AgregarWhisper(string json, int hablante, double desplazamiento)
    {
        object o = Json.Leer(json);
        foreach (object s in Json.Lista(o, "segments"))
        {
            Segmento seg = new Segmento();
            seg.Hablante = hablante;
            seg.Inicio = Json.Numero(s, "start", 0) + desplazamiento;
            seg.Fin = Json.Numero(s, "end", 0) + desplazamiento;
            seg.Texto = Json.Texto(s, "text").Trim();
            foreach (object w in Json.Lista(s, "words"))
            {
                Palabra p = new Palabra();
                p.Inicio = Json.Numero(w, "start", seg.Inicio - desplazamiento) + desplazamiento;
                p.Fin = Json.Numero(w, "end", seg.Fin - desplazamiento) + desplazamiento;
                p.Prob = Json.Numero(w, "probability", Json.Numero(w, "prob", 1));
                p.Texto = Json.Texto(w, "word");
                if (p.Texto.Length == 0) p.Texto = Json.Texto(w, "text");
                seg.Palabras.Add(p);
            }
            if (seg.Texto.Length > 0 || seg.Palabras.Count > 0) Segmentos.Add(seg);
        }
        Segmentos.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
    }

    // ---------------------------------------------------- guardar / cargar

    static double R(double v) { return Math.Round(v, 3); }

    public void Guardar(string ruta)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-transcripcion";
        d["version"] = 1;
        d["proyecto"] = Proyecto;
        d["creada"] = Creada;
        d["idioma"] = Idioma;
        d["modelo"] = Modelo;
        d["inicio"] = R(Inicio);
        d["duracion"] = R(Duracion);
        d["duracionProyecto"] = R(DuracionProyecto);

        List<object> hs = new List<object>();
        foreach (Hablante h in Hablantes)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["etiqueta"] = h.Etiqueta;
            x["nombre"] = h.Nombre;
            x["archivo"] = h.Archivo;
            x["voz"] = h.Voz;
            x["nivel"] = h.Nivel ?? new float[0];
            x["pico"] = h.Pico ?? new float[0];
            List<object> fs = new List<object>();
            // Fuentes compactas: [inicio, fin, desde, velocidad, flujo, archivo]
            foreach (Fuente f in h.Fuentes)
                fs.Add(new List<object> { R(f.Inicio), R(f.Fin), R(f.Desde), Math.Round(f.Velocidad, 4), f.Flujo, f.Media });
            x["fuentes"] = fs;
            hs.Add(x);
        }
        d["hablantes"] = hs;

        List<object> ss = new List<object>();
        foreach (Segmento s in Segmentos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["h"] = s.Hablante;
            x["inicio"] = R(s.Inicio);
            x["fin"] = R(s.Fin);
            x["texto"] = s.Texto;
            List<object> ps = new List<object>();
            // Palabras compactas: [inicio, fin, probabilidad, texto]
            foreach (Palabra p in s.Palabras)
                ps.Add(new List<object> { R(p.Inicio), R(p.Fin), Math.Round(p.Prob, 2), p.Texto });
            x["palabras"] = ps;
            ss.Add(x);
        }
        d["segmentos"] = ss;

        List<object> es = new List<object>();
        foreach (Edicion e in Ediciones)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["antes"] = R(e.Antes);
            x["despues"] = R(e.Despues);
            List<object> qs = new List<object>();
            foreach (Rango r in e.Quitados) qs.Add(new List<object> { R(r.Inicio), R(r.Fin) });
            x["quitados"] = qs;
            if (e.Acelerados.Count > 0)
            {
                List<object> acs = new List<object>();
                foreach (Acelerado a in e.Acelerados) acs.Add(new List<object> { R(a.Inicio), R(a.Fin), a.Factor });
                x["acelerados"] = acs;
            }
            es.Add(x);
        }
        d["ediciones"] = es;

        File.WriteAllText(ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    public static Transcripcion Cargar(string ruta)
    {
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        if (Json.Texto(o, "formato") != "vegas-cut-transcripcion")
            throw new Exception("El archivo no es una transcripci\u00f3n de vegas-cut.");
        Transcripcion t = new Transcripcion();
        t.Proyecto = Json.Texto(o, "proyecto");
        t.Creada = Json.Texto(o, "creada");
        t.Idioma = Json.Texto(o, "idioma");
        t.Modelo = Json.Texto(o, "modelo");
        t.Inicio = Json.Numero(o, "inicio", 0);
        t.Duracion = Json.Numero(o, "duracion", 0);
        t.DuracionProyecto = Json.Numero(o, "duracionProyecto", 0);

        foreach (object x in Json.Lista(o, "hablantes"))
        {
            Hablante h = new Hablante();
            h.Etiqueta = Json.Texto(x, "etiqueta");
            h.Nombre = Json.Texto(x, "nombre");
            h.Archivo = Json.Texto(x, "archivo");
            object voz;
            Dictionary<string, object> dx = (Dictionary<string, object>)x;
            h.Voz = dx.TryGetValue("voz", out voz) && voz is bool && (bool)voz;
            h.Nivel = Numeros(Json.Lista(x, "nivel"));
            h.Pico = Numeros(Json.Lista(x, "pico"));
            foreach (object q in Json.Lista(x, "fuentes"))
            {
                List<object> l = q as List<object>;
                if (l == null || l.Count < 6) continue;
                Fuente f = new Fuente();
                f.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                f.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                f.Desde = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                f.Velocidad = Convert.ToDouble(l[3], CultureInfo.InvariantCulture);
                f.Flujo = Convert.ToInt32(l[4], CultureInfo.InvariantCulture);
                f.Media = l[5] as string ?? "";
                h.Fuentes.Add(f);
            }
            t.Hablantes.Add(h);
        }

        foreach (object x in Json.Lista(o, "segmentos"))
        {
            Segmento s = new Segmento();
            s.Hablante = (int)Json.Numero(x, "h", 0);
            s.Inicio = Json.Numero(x, "inicio", 0);
            s.Fin = Json.Numero(x, "fin", 0);
            s.Texto = Json.Texto(x, "texto");
            foreach (object p in Json.Lista(x, "palabras"))
            {
                List<object> l = p as List<object>;
                if (l == null || l.Count < 4) continue;
                Palabra w = new Palabra();
                w.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                w.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                w.Prob = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                w.Texto = l[3] as string ?? "";
                s.Palabras.Add(w);
            }
            t.Segmentos.Add(s);
        }

        foreach (object x in Json.Lista(o, "ediciones"))
        {
            Edicion e = new Edicion();
            e.Antes = Json.Numero(x, "antes", 0);
            e.Despues = Json.Numero(x, "despues", 0);
            foreach (object q in Json.Lista(x, "quitados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 2)
                    e.Quitados.Add(new Rango(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                             Convert.ToDouble(l[1], CultureInfo.InvariantCulture)));
            }
            foreach (object q in Json.Lista(x, "acelerados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 3)
                    e.Acelerados.Add(new Acelerado(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[1], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[2], CultureInfo.InvariantCulture)));
            }
            t.Ediciones.Add(e);
        }
        return t;
    }

    static float[] Numeros(List<object> l)
    {
        float[] r = new float[l.Count];
        for (int i = 0; i < l.Count; i++) r[i] = (float)Convert.ToDouble(l[i], CultureInfo.InvariantCulture);
        return r;
    }
}

// ---- src/comun/Configuracion.cs ----

// =====================================================================
// Configuracion compartida por todas las herramientas
// (%APPDATA%\vegas-cut\config.json). La clave de Gemini se guarda cifrada
// con DPAPI: solo tu usuario de Windows en esta PC puede leerla.
// =====================================================================

public class Configuracion
{
    public string GeminiClave = "";
    public string GeminiModelo = "gemini-flash-latest";
    public string WhisperExe = "";
    public string WhisperModelo = "large-v3-turbo";
    public string WhisperDispositivo = "cuda";   // cuda (tarjeta NVIDIA) o cpu
    public string WhisperPrecision = "int8";     // int8 usa menos memoria de video
    public string Idioma = "es";
    public string WhisperExtra = "";             // opciones extra para el .exe
    public string ReglasCanal = "";              // reglas fijas de MomentosIA ("" = las de siempre)

    public static string Carpeta
    {
        get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"); }
    }

    static string Ruta { get { return Path.Combine(Carpeta, "config.json"); } }

    public bool TieneGemini { get { return GeminiClave.Length > 0; } }

    public bool TieneWhisper { get { return WhisperExe.Length > 0 && File.Exists(WhisperExe); } }

    public static Configuracion Cargar()
    {
        Configuracion c = new Configuracion();
        try
        {
            if (!File.Exists(Ruta)) return c;
            object o = Json.Leer(File.ReadAllText(Ruta, Encoding.UTF8));
            c.GeminiClave = Descifrar(Json.Texto(o, "geminiClave"));
            c.GeminiModelo = Valor(Json.Texto(o, "geminiModelo"), c.GeminiModelo);
            c.WhisperExe = Json.Texto(o, "whisperExe");
            c.WhisperModelo = Valor(Json.Texto(o, "whisperModelo"), c.WhisperModelo);
            c.WhisperDispositivo = Valor(Json.Texto(o, "whisperDispositivo"), c.WhisperDispositivo);
            c.WhisperPrecision = Valor(Json.Texto(o, "whisperPrecision"), c.WhisperPrecision);
            c.Idioma = Valor(Json.Texto(o, "idioma"), c.Idioma);
            c.WhisperExtra = Json.Texto(o, "whisperExtra");
            c.ReglasCanal = Json.Texto(o, "reglasCanal");
        }
        catch { }
        return c;
    }

    static string Valor(string v, string siVacio) { return String.IsNullOrEmpty(v) ? siVacio : v; }

    public void Guardar()
    {
        Directory.CreateDirectory(Carpeta);
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["geminiClave"] = Cifrar(GeminiClave);
        d["geminiModelo"] = GeminiModelo;
        d["whisperExe"] = WhisperExe;
        d["whisperModelo"] = WhisperModelo;
        d["whisperDispositivo"] = WhisperDispositivo;
        d["whisperPrecision"] = WhisperPrecision;
        d["idioma"] = Idioma;
        d["whisperExtra"] = WhisperExtra;
        d["reglasCanal"] = ReglasCanal;
        File.WriteAllText(Ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    // ------------------------------------------------------------- DPAPI

    [StructLayout(LayoutKind.Sequential)]
    struct Blob { public int Largo; public IntPtr Datos; }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptProtectData(ref Blob entrada, string descripcion, IntPtr entropia,
        IntPtr reservado, IntPtr aviso, int banderas, ref Blob salida);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CryptUnprotectData(ref Blob entrada, IntPtr descripcion, IntPtr entropia,
        IntPtr reservado, IntPtr aviso, int banderas, ref Blob salida);

    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr p);

    const int SinInterfaz = 0x1;

    static byte[] Dpapi(byte[] datos, bool cifrar)
    {
        Blob entrada = new Blob(), salida = new Blob();
        GCHandle h = GCHandle.Alloc(datos, GCHandleType.Pinned);
        try
        {
            entrada.Largo = datos.Length;
            entrada.Datos = h.AddrOfPinnedObject();
            bool ok = cifrar
                ? CryptProtectData(ref entrada, "vegas-cut", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, SinInterfaz, ref salida)
                : CryptUnprotectData(ref entrada, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, SinInterfaz, ref salida);
            if (!ok) throw new Exception("DPAPI fall\u00f3 (" + Marshal.GetLastWin32Error() + ")");
            byte[] r = new byte[salida.Largo];
            Marshal.Copy(salida.Datos, r, 0, salida.Largo);
            return r;
        }
        finally
        {
            h.Free();
            if (salida.Datos != IntPtr.Zero) LocalFree(salida.Datos);
        }
    }

    // "dpapi:..." si se pudo cifrar; "b64:..." solo como respaldo fuera de Windows.
    static string Cifrar(string texto)
    {
        if (String.IsNullOrEmpty(texto)) return "";
        byte[] b = Encoding.UTF8.GetBytes(texto);
        try { return "dpapi:" + Convert.ToBase64String(Dpapi(b, true)); }
        catch { return "b64:" + Convert.ToBase64String(b); }
    }

    static string Descifrar(string guardado)
    {
        try
        {
            if (guardado.StartsWith("dpapi:"))
                return Encoding.UTF8.GetString(Dpapi(Convert.FromBase64String(guardado.Substring(6)), false));
            if (guardado.StartsWith("b64:"))
                return Encoding.UTF8.GetString(Convert.FromBase64String(guardado.Substring(4)));
        }
        catch { }
        return "";
    }
}

// ---- src/comun/Gemini.cs ----

// =====================================================================
// Cliente minimo de la API de Gemini (REST generateContent).
// Solo se envia texto; el audio nunca sale de la PC.
// =====================================================================

public static class Gemini
{
    // Se puede cambiar solo para pruebas (servidor local que imita la API).
    public static string Base = "https://generativelanguage.googleapis.com/v1beta/";

    static HttpWebRequest Peticion(string url, string clave, string metodo)
    {
        // Vegas corre en .NET Framework: hay que activar TLS 1.2 a mano.
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
        r.Method = metodo;
        r.Headers.Add("x-goog-api-key", clave);
        r.Timeout = 10 * 60 * 1000;
        r.ReadWriteTimeout = 10 * 60 * 1000;
        return r;
    }

    static string Responder(HttpWebRequest r)
    {
        try
        {
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
        catch (WebException ex)
        {
            string detalle = ex.Message;
            if (ex.Response != null)
            {
                try
                {
                    using (StreamReader sr = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        string cuerpo = sr.ReadToEnd();
                        string msg = Json.Texto(Json.Obj(Json.Leer(cuerpo), "error"), "message");
                        if (msg.Length > 0) detalle = msg;
                    }
                }
                catch { }
            }
            throw new Exception("Gemini: " + detalle);
        }
    }

    // Modelos disponibles para esta clave que sirven para generar texto.
    public static List<string> ListarModelos(string clave)
    {
        List<string> r = new List<string>();
        string pagina = "";
        do
        {
            string url = Base + "models?pageSize=200" + (pagina.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(pagina) : "");
            object o = Json.Leer(Responder(Peticion(url, clave, "GET")));
            foreach (object m in Json.Lista(o, "models"))
            {
                bool genera = false;
                foreach (object metodo in Json.Lista(m, "supportedGenerationMethods"))
                    if ((metodo as string) == "generateContent") genera = true;
                string nombre = Json.Texto(m, "name");
                if (nombre.StartsWith("models/")) nombre = nombre.Substring(7);
                if (genera && nombre.StartsWith("gemini")) r.Add(nombre);
            }
            pagina = Json.Texto(o, "nextPageToken");
        } while (pagina.Length > 0);
        r.Sort(StringComparer.OrdinalIgnoreCase);
        return r;
    }

    // Pide una respuesta. Con "json" se exige que conteste solo JSON.
    public static string Generar(string clave, string modelo, string instrucciones, string mensaje, bool json)
    {
        Dictionary<string, object> cuerpo = new Dictionary<string, object>();
        if (!String.IsNullOrEmpty(instrucciones))
            cuerpo["systemInstruction"] = Partes(instrucciones, null);
        cuerpo["contents"] = new List<object> { Partes(mensaje, "user") };
        Dictionary<string, object> config = new Dictionary<string, object>();
        config["temperature"] = 0.4;
        if (json) config["responseMimeType"] = "application/json";
        cuerpo["generationConfig"] = config;

        HttpWebRequest r = Peticion(Base + "models/" + Uri.EscapeDataString(modelo) + ":generateContent", clave, "POST");
        r.ContentType = "application/json; charset=utf-8";
        byte[] datos = Encoding.UTF8.GetBytes(Json.Escribir(cuerpo, false));
        r.ContentLength = datos.Length;
        using (Stream s = r.GetRequestStream()) s.Write(datos, 0, datos.Length);

        object resp = Json.Leer(Responder(r));
        List<object> candidatos = Json.Lista(resp, "candidates");
        if (candidatos.Count == 0)
        {
            string motivo = Json.Texto(Json.Obj(resp, "promptFeedback"), "blockReason");
            throw new Exception("Gemini no devolvi\u00f3 respuesta" + (motivo.Length > 0 ? " (" + motivo + ")" : "") + ".");
        }
        StringBuilder texto = new StringBuilder();
        foreach (object parte in Json.Lista(Json.Obj(candidatos[0], "content"), "parts"))
        {
            object pensamiento;
            Dictionary<string, object> d = parte as Dictionary<string, object>;
            if (d != null && d.TryGetValue("thought", out pensamiento) && pensamiento is bool && (bool)pensamiento) continue;
            texto.Append(Json.Texto(parte, "text"));
        }
        // Si se acabo el espacio de respuesta, el JSON queda a medias.
        if (Json.Texto(candidatos[0], "finishReason") == "MAX_TOKENS")
            throw new RespuestaCortada();
        if (texto.Length == 0)
            throw new Exception("Gemini devolvi\u00f3 una respuesta vac\u00eda (" + Json.Texto(candidatos[0], "finishReason") + ").");
        return QuitarCercas(texto.ToString());
    }

    static Dictionary<string, object> Partes(string texto, string rol)
    {
        Dictionary<string, object> parte = new Dictionary<string, object>();
        parte["text"] = texto;
        Dictionary<string, object> c = new Dictionary<string, object>();
        if (rol != null) c["role"] = rol;
        c["parts"] = new List<object> { parte };
        return c;
    }

    // Algunos modelos envuelven el JSON en ```json ... ```.
    public static string QuitarCercas(string t)
    {
        string s = t.Trim();
        if (s.StartsWith("```"))
        {
            int salto = s.IndexOf('\n');
            int fin = s.LastIndexOf("```");
            if (salto > 0 && fin > salto) s = s.Substring(salto + 1, fin - salto - 1).Trim();
        }
        return s;
    }
}

// La respuesta no cupo completa (finishReason MAX_TOKENS).
public class RespuestaCortada : Exception
{
    public RespuestaCortada() : base("La respuesta de Gemini sali\u00f3 cortada por ser demasiado larga.") { }
}

// ---- src/comun/Ui.cs ----

// =====================================================================
// Interfaz
// =====================================================================

static class Tema
{
    public static readonly Color Fondo = Color.FromArgb(18, 19, 23);
    public static readonly Color Panel = Color.FromArgb(27, 28, 34);
    public static readonly Color Campo = Color.FromArgb(35, 37, 44);
    public static readonly Color CampoHover = Color.FromArgb(44, 46, 55);
    public static readonly Color Borde = Color.FromArgb(52, 54, 64);
    public static readonly Color Texto = Color.FromArgb(236, 237, 241);
    public static readonly Color TextoSuave = Color.FromArgb(150, 153, 164);
    public static readonly Color Acento = Color.FromArgb(255, 106, 43);
    public static readonly Color AcentoHover = Color.FromArgb(255, 132, 80);
    public static readonly Color Voz = Color.FromArgb(120, 200, 255);
    public static readonly Color Silencio = Color.FromArgb(255, 84, 84);

    public static Font Fuente(float tam, FontStyle estilo)
    {
        try { return new Font("Segoe UI", tam, estilo); }
        catch { return new Font(FontFamily.GenericSansSerif, tam, estilo); }
    }
    public static readonly Font Normal = Fuente(9f, FontStyle.Regular);
    public static readonly Font Negrita = Fuente(9f, FontStyle.Bold);
    public static readonly Font Pequena = Fuente(8f, FontStyle.Regular);
    public static readonly Font Titulo = Fuente(15f, FontStyle.Bold);
    public static readonly Font Seccion = Fuente(10f, FontStyle.Bold);

    public static GraphicsPath Redondeado(RectangleF r, float radio)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

class ControlBase : Control
{
    protected bool encima;
    public ControlBase()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
    }
    protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }
}

enum EstiloBoton { Primario, Secundario, Chip }

class Boton : ControlBase
{
    public EstiloBoton Estilo = EstiloBoton.Secundario;
    bool activo;
    public bool Activo { get { return activo; } set { activo = value; Invalidate(); } }

    public Boton(string texto, EstiloBoton estilo)
    {
        Text = texto;
        Estilo = estilo;
        Cursor = Cursors.Hand;
        if (estilo == EstiloBoton.Primario) Font = Tema.Fuente(10f, FontStyle.Bold);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fondo, borde, texto = Tema.Texto;
        if (Estilo == EstiloBoton.Primario)
        {
            fondo = !Enabled ? Color.FromArgb(90, 60, 48) : encima ? Tema.AcentoHover : Tema.Acento;
            borde = fondo;
            texto = Enabled ? Color.White : Color.FromArgb(170, 150, 140);
        }
        else if (Estilo == EstiloBoton.Chip && activo)
        {
            fondo = Color.FromArgb(60, Tema.Acento);
            borde = Tema.Acento;
        }
        else
        {
            fondo = encima && Enabled ? Tema.CampoHover : Tema.Campo;
            borde = Tema.Borde;
            if (!Enabled) texto = Tema.TextoSuave;
        }
        using (GraphicsPath p = Tema.Redondeado(r, Estilo == EstiloBoton.Chip ? Height / 2f : 8))
        {
            using (SolidBrush b = new SolidBrush(fondo)) g.FillPath(b, p);
            using (Pen pen = new Pen(borde)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, texto,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class Segmentado : ControlBase
{
    string[] opciones;
    int seleccion;
    int hover = -1;
    public event EventHandler Cambio;

    public Segmentado(string[] opciones) { this.opciones = opciones; Cursor = Cursors.Hand; }

    public int Seleccion
    {
        get { return seleccion; }
        set { if (value != seleccion) { seleccion = value; Invalidate(); if (Cambio != null) Cambio(this, EventArgs.Empty); } }
    }

    bool[] habilitadas;
    public void Habilitar(int i, bool si)
    {
        if (habilitadas == null) { habilitadas = new bool[opciones.Length]; for (int k = 0; k < opciones.Length; k++) habilitadas[k] = true; }
        habilitadas[i] = si;
        Invalidate();
    }
    bool Habilitada(int i) { return habilitadas == null || habilitadas[i]; }

    int Indice(int x) { return Math.Max(0, Math.Min(opciones.Length - 1, x * opciones.Length / Math.Max(1, Width))); }

    protected override void OnMouseMove(MouseEventArgs e) { int h = Indice(e.X); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int i = Indice(e.X); if (Habilitada(i)) Seleccion = i; base.OnMouseDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath p = Tema.Redondeado(r, 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(Tema.Borde)) g.DrawPath(pen, p);
        }
        float ancho = (Width - 6f) / opciones.Length;
        for (int i = 0; i < opciones.Length; i++)
        {
            RectangleF c = new RectangleF(3 + i * ancho, 3, ancho, Height - 7);
            if (i == seleccion)
                using (GraphicsPath p = Tema.Redondeado(c, 6))
                using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
            else if (i == hover && Habilitada(i))
                using (GraphicsPath p = Tema.Redondeado(c, 6))
                using (SolidBrush b = new SolidBrush(Tema.CampoHover)) g.FillPath(b, p);
            Color col = i == seleccion ? Color.White : Habilitada(i) ? Tema.Texto : Color.FromArgb(90, 92, 100);
            TextRenderer.DrawText(g, opciones[i], i == seleccion ? Tema.Negrita : Font, Rectangle.Round(c), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

class Deslizador : ControlBase
{
    public double Minimo = -70, Maximo = -10;
    public bool DesdeCentro;
    double valor = 0;
    bool arrastrando;
    public event EventHandler Cambio;

    public Deslizador() { Cursor = Cursors.Hand; }

    public double Valor
    {
        get { return valor; }
        set
        {
            double v = Math.Max(Minimo, Math.Min(Maximo, Math.Round(value)));
            if (v != valor) { valor = v; Invalidate(); if (Cambio != null) Cambio(this, EventArgs.Empty); }
        }
    }

    float X(double v) { return 8 + (float)((v - Minimo) / (Maximo - Minimo)) * (Width - 16); }

    void Mover(int x) { Valor = Minimo + (x - 8) / (double)Math.Max(1, Width - 16) * (Maximo - Minimo); }

    protected override void OnMouseDown(MouseEventArgs e) { arrastrando = true; Mover(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (arrastrando) Mover(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { arrastrando = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Valor = valor + (e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f, x = X(valor);
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(8, cy - 3, Width - 16, 6), 3))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        float desde = DesdeCentro ? X((Minimo + Maximo) / 2) : 8;
        if (DesdeCentro)
            using (SolidBrush b = new SolidBrush(Tema.Borde)) g.FillRectangle(b, desde - 1, cy - 7, 2, 14);
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(Math.Min(desde, x), cy - 3, Math.Max(6, Math.Abs(x - desde)), 6), 3))
        using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
        float rad = encima || arrastrando ? 9 : 8;
        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, x - rad, cy - rad, rad * 2, rad * 2);
        using (Pen pen = new Pen(Tema.Acento, 3)) g.DrawEllipse(pen, x - rad + 1.5f, cy - rad + 1.5f, rad * 2 - 3, rad * 2 - 3);
    }
}

// Campo numerico con sufijo "ms": escribir, rueda del raton o flechas.
class CampoNumero : ControlBase
{
    TextBox caja = new TextBox();
    int valor;
    public int Minimo = 0, Maximo = 5000, Paso = 10;
    public string Sufijo = "ms";
    public event EventHandler Cambio;

    public CampoNumero()
    {
        caja.BorderStyle = BorderStyle.None;
        caja.BackColor = Tema.Campo;
        caja.ForeColor = Tema.Texto;
        caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        caja.TextAlign = HorizontalAlignment.Left;
        caja.KeyPress += delegate (object s, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
        caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Valor = valor + Paso; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Valor = valor - Paso; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { Confirmar(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        caja.Leave += delegate { Confirmar(); };
        caja.MouseWheel += delegate (object s, MouseEventArgs e) { Valor = valor + (e.Delta > 0 ? Paso : -Paso); };
        Controls.Add(caja);
        Cursor = Cursors.IBeam;
    }

    void Confirmar()
    {
        int v;
        if (int.TryParse(caja.Text, out v)) Valor = v; else caja.Text = valor.ToString();
    }

    public int Valor
    {
        get { return valor; }
        set
        {
            int v = Math.Max(Minimo, Math.Min(Maximo, value));
            caja.Text = v.ToString();
            if (v != valor) { valor = v; if (Cambio != null) Cambio(this, EventArgs.Empty); }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e) { caja.Focus(); base.OnMouseDown(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        caja.SetBounds(12, (Height - caja.PreferredHeight) / 2 + 1, Width - 50, caja.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(caja.Focused ? Tema.Acento : Tema.Borde)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Sufijo, Tema.Pequena, new Rectangle(Width - 36, 0, 28, Height), Tema.TextoSuave,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

class Combo : ComboBox
{
    public Combo() : this(false) { }

    // Editable: se puede escribir un valor que no este en la lista.
    public Combo(bool editable)
    {
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Tema.Campo;
        ForeColor = Tema.Texto;
        Font = Tema.Fuente(10f, FontStyle.Regular);
        ItemHeight = 24;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (SolidBrush b = new SolidBrush(sel ? Tema.Acento : Tema.Campo)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            sel ? Color.White : Tema.Texto, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class Etiqueta : Label
{
    public Etiqueta(string texto, Font fuente, Color color)
    {
        Text = texto; Font = fuente; ForeColor = color;
        BackColor = Color.Transparent;
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleLeft;
    }
}

// Pide el nombre para guardar un perfil.
class DialogoNombre : Form
{
    TextBox caja = new TextBox();
    public string Nombre { get { return caja.Text.Trim(); } }

    public DialogoNombre(string sugerido) : this(sugerido, "Guardar perfil", "Nombre del perfil") { }

    public DialogoNombre(string sugerido, string titulo, string etiqueta)
    {
        Text = titulo;
        ClientSize = new Size(380, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;

        Controls.Add(Pos(new Etiqueta(etiqueta, Tema.Seccion, Tema.Texto), 20, 16, 340, 22));
        Panel marco = new Panel();
        marco.BackColor = Tema.Campo;
        marco.Padding = new Padding(10, 8, 10, 6);
        caja.BorderStyle = BorderStyle.None;
        caja.BackColor = Tema.Campo;
        caja.ForeColor = Tema.Texto;
        caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        caja.Dock = DockStyle.Fill;
        caja.Text = sugerido;
        marco.Controls.Add(caja);
        Controls.Add(Pos(marco, 20, 46, 340, 34));

        Boton guardar = new Boton("Guardar", EstiloBoton.Primario);
        Boton cancelar = new Boton("Cancelar", EstiloBoton.Secundario);
        Controls.Add(Pos(cancelar, 150, 100, 100, 34));
        Controls.Add(Pos(guardar, 260, 100, 100, 34));
        guardar.Click += delegate { if (Nombre.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
        cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && Nombre.Length > 0) { DialogResult = DialogResult.OK; Close(); }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
        Shown += delegate { caja.Focus(); caja.SelectAll(); };
    }

    static Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); return c; }
}

// Campo de texto oscuro con borde redondeado.
class CampoTexto : ControlBase
{
    public TextBox Caja = new TextBox();

    public CampoTexto()
    {
        Caja.BorderStyle = BorderStyle.None;
        Caja.BackColor = Tema.Campo;
        Caja.ForeColor = Tema.Texto;
        Caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        Caja.GotFocus += delegate { Invalidate(); };
        Caja.LostFocus += delegate { Invalidate(); };
        Controls.Add(Caja);
        Cursor = Cursors.IBeam;
    }

    public override string Text { get { return Caja.Text; } set { Caja.Text = value; } }

    public bool Oculto { get { return Caja.UseSystemPasswordChar; } set { Caja.UseSystemPasswordChar = value; } }

    public bool Multilinea
    {
        get { return Caja.Multiline; }
        set { Caja.Multiline = value; Caja.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None; PerformLayout(); }
    }

    protected override void OnMouseDown(MouseEventArgs e) { Caja.Focus(); base.OnMouseDown(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Caja.Multiline) Caja.SetBounds(10, 8, Width - 20, Height - 16);
        else Caja.SetBounds(12, (Height - Caja.PreferredHeight) / 2 + 1, Width - 24, Caja.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(Caja.Focused ? Tema.Acento : Tema.Borde)) g.DrawPath(pen, p);
        }
    }
}

// Lista oscura con casillas (ListView con encabezado dibujado a mano).
class Lista : ListView
{
    public Lista()
    {
        View = View.Details;
        FullRowSelect = true;
        CheckBoxes = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        BackColor = Tema.Campo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        OwnerDraw = true;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        DoubleBuffered = true;
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using (SolidBrush b = new SolidBrush(Tema.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, Tema.Pequena,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Tema.TextoSuave,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) { e.DrawDefault = true; }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) { e.DrawDefault = true; }
}

// Barra de progreso redondeada.
class BarraProgreso : ControlBase
{
    double valor;
    public double Valor { get { return valor; } set { valor = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        if (valor > 0)
            using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Math.Max(Height, (float)(Width - 1) * (float)valor), Height - 1), Height / 2f))
            using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
    }
}

// Ventana base con el tema oscuro y la linea de acento bajo el titulo.
class VentanaBase : Form
{
    protected const int Margen = 24;

    public VentanaBase(string titulo, int ancho)
    {
        Text = titulo + " \u00b7 vegas-cut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(ancho, 400);
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    protected int Ancho { get { return ClientSize.Width - Margen * 2; } }

    protected Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); Controls.Add(c); return c; }

    protected Etiqueta Texto(string t, Font f, Color c, int x, int y, int w, int h)
    {
        Etiqueta e = new Etiqueta(t, f, c);
        if (h > 22) e.TextAlign = ContentAlignment.TopLeft;
        Pos(e, x, y, w, h);
        return e;
    }

    protected void Encabezado(string titulo, string subtitulo)
    {
        Texto(titulo, Tema.Titulo, Tema.Texto, Margen, 18, Ancho, 32);
        Texto(subtitulo, Tema.Normal, Tema.TextoSuave, Margen, 50, Ancho, 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (SolidBrush b = new SolidBrush(Tema.Acento)) e.Graphics.FillRectangle(b, Margen, 76, 36, 3);
    }
}
