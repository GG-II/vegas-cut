using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

// =====================================================================
// Biblioteca de musica: recorre la carpeta de musica, empareja cada archivo
// con su tema del anime (CatalogoAnime: escenas donde suena) y le pone
// estados de animo, la parte del episodio donde suele ir y de que personaje
// es tema. Se guarda en "<carpeta>\musica-indice.json".
// =====================================================================

public class ArchivoMusica
{
    public string Ruta = "", Titulo = "", Album = "", Fuente = "", TemaAnime = "", Parte = "";
    public double Duracion;
    public int Usos, LargoTipico;
    public List<string> Animos = new List<string>(), TemaDe = new List<string>(), Escenas = new List<string>(), Variantes = new List<string>();
    public string Momento = "";     // donde suena mas: inicio, medio, final, avance, eyecatch...
    public string Descripcion = ""; // como suena (de la IA, para lo que no es del anime)
    public bool Etiquetado;         // animos puestos por la IA (juegos, fanmade...)
    public bool ConUso { get { return Usos > 0; } }
    // Sirve para elegir musica: tiene datos del anime, es fanmade de SBR o ya esta etiquetado.
    public bool Sirve { get { return ConUso || Fuente == "SBR fan" || (Etiquetado && Animos.Count > 0); } }
}

// Lo que se sabe de un archivo antes de emparejarlo (de sus etiquetas o del CSV).
public class FilaMusica
{
    public string Ruta = "", Titulo = "", Album = "";
    public double Duracion;
}

public class BibliotecaMusica
{
    public string Carpeta = "";
    public List<ArchivoMusica> Archivos = new List<ArchivoMusica>();
    public const string NombreIndice = "musica-indice.json";
    public static readonly string[] Extensiones = { ".mp3", ".flac", ".wav", ".m4a", ".ogg", ".opus", ".aac", ".wma" };

    // ----------------------------------------------------------- normalizar

    public static string Norm(string t)
    {
        string s = (t ?? "").Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char c in s)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && c < 128) sb.Append(c);
        s = sb.ToString().ToLowerInvariant();
        s = Regex.Replace(s, @"^\s*\d{1,3}[\s.\-_]+", "");
        s = Regex.Replace(s, @"\(.*?\)|\[.*?\]|~.*?~", " ");
        s = Regex.Replace(s, @"[^a-z0-9 ]", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    // Titulo sin la version: "Rest ~Piano Ver.~", "Fighting Gold (Instrumental)" -> mismo nucleo.
    public static string Nucleo(string t)
    {
        string s = Regex.Replace(t ?? "", @"(?i)\s*[-–(\[~]?\s*(piano|instrumental|english|tv size|tv|full|short|extended|remix|giorno|diavolo|units|acoustic|orchestra)\s*(ver(sion)?\.?)?\s*[)\]~]?", " ");
        return Norm(s);
    }

    static readonly string[][] Fuentes = {
        new[] { "vento aureo soundtrack", "videojuego" },
        new[] { "stardust crusaders", "SC" }, new[] { "golden wind|vento aureo|giogio", "GW" }, new[] { "diamond is unbreakable|morioh", "DU" },
        new[] { "stone ocean", "SO" }, new[] { @"phantom blood.*o\.?s\.?t|battle tendency", "PB/BT" }, new[] { "steel ball run|gwinn", "SBR fan" },
        new[] { @"all star battle|eyes of heaven|ora ora overdrive|heritage for the future|\brpg\b|stardust shooters|video game|diamond records|ps3", "videojuego" },
        new[] { @"\bova\b|2000", "OVA" }, new[] { @"anthology|op\d? single|theme song|opening|ending", "canción" },
    };

    public static string Fuente(string album, string ruta)
    {
        string t = (album + " " + ruta).ToLowerInvariant();
        foreach (string[] f in Fuentes) if (Regex.IsMatch(t, f[0])) return f[1];
        return "otro";
    }

    static readonly string[] ClavesOst = { "departure", "journey", "world", "destination", "overture", "intermezzo", "finale",
                                           "good morning", "good night", "future", "destiny", "musik", "leicht", "stone ocean" };

    static readonly Dictionary<string, string> Animo = new Dictionary<string, string> {
        { "pelea", "battle|fight|clash|duel|assault|attack|vs|fist|rush|showdown|combat|pelea|batalla|combate|lucha|jefe" },
        { "tension", "tension|imminen|crisis|danger|threat|pursuit|approach|creeping|urgency|omen|foreboding|unease|anxiety|chase|tension|suspenso|peligro|persecucion" },
        { "villano", "dio|evil|dark|devil|villain|boss|kira|diavolo|pucci|killer|enemy|rebirth|malice|sinister|villano|malvado|oscuro" },
        { "misterio", "myster|strange|bizarre|enigma|secret|plot|mist|unknown|question|riddle|misterioso|misterio|cueva|cave" },
        { "comedia", "comic|funny|jolly|silly|comical|humor|playful|cheer|comedia|gracios|divertid" },
        { "viaje", "journey|travel|departure|sightseeing|wilderness|road|desert|wind|voyage|setting off|ride|horse|run|viaje|aventura|explora|overworld" },
        { "calma", "calm|rest|peace|gentle|repose|daily|morning|sunlight|quiet|serene|night|calma|tranquil|relaj|menu|lobby|ambient" },
        { "tristeza", "sad|sorrow|tears|requiem|farewell|grief|lament|memory|memories|hesitation|loneliness|triste|tristeza|melancol" },
        { "victoria", "victory|triumph|glory|hero|pride|proud|win|victoria|triunfo" },
        { "epico", "theme|crusaders|stardust|golden|giorno|decisive|final|vento|oro|awakening|platinum|fate|destiny|epico|epic|heroic" },
    };

    // ------------------------------------------------------------- catalogo

    class Tema
    {
        public string T, N, P, O;
        public int U, L;
        public List<string> A = new List<string>(), D = new List<string>(), E = new List<string>();
        public string M = "";
        public Dictionary<string, bool> Pares;
    }

    static List<Tema> catalogo;
    static Dictionary<string, string> alias;

    static void CargarCatalogo()
    {
        if (catalogo != null) return;
        catalogo = new List<Tema>();
        alias = new Dictionary<string, string>();
        object o = Json.Leer(CatalogoAnime.Json);
        foreach (object x in Json.Lista(o, "temas"))
        {
            Tema t = new Tema();
            t.T = Json.Texto(x, "t"); t.N = Json.Texto(x, "n"); t.P = Json.Texto(x, "p"); t.O = Json.Texto(x, "o");
            t.U = (int)Json.Numero(x, "u", 0); t.L = (int)Json.Numero(x, "l", 0);
            foreach (object a in Json.Lista(x, "a")) t.A.Add((string)a);
            foreach (object a in Json.Lista(x, "d")) t.D.Add((string)a);
            foreach (object a in Json.Lista(x, "e")) t.E.Add((string)a);
            int mejor = -1;
            Dictionary<string, object> m = Json.Obj(x, "m");
            if (m != null) foreach (KeyValuePair<string, object> kv in m) if (Convert.ToInt32(kv.Value) > mejor) { mejor = Convert.ToInt32(kv.Value); t.M = kv.Key; }
            t.Pares = Pares(t.N);
            catalogo.Add(t);
        }
        Dictionary<string, object> al = Json.Obj(o, "alias");
        if (al != null) foreach (KeyValuePair<string, object> kv in al) alias[kv.Key] = Norm((string)kv.Value);
    }

    // Pares de letras (para descartar rapido los que no se parecen nada).
    static Dictionary<string, bool> Pares(string s)
    {
        Dictionary<string, bool> d = new Dictionary<string, bool>();
        for (int i = 0; i + 1 < s.Length; i++) d[s.Substring(i, 2)] = true;
        return d;
    }

    static double Dice(Dictionary<string, bool> a, Dictionary<string, bool> b)
    {
        if (a.Count == 0 || b.Count == 0) return 0;
        int c = 0;
        foreach (string k in a.Keys) if (b.ContainsKey(k)) c++;
        return 2.0 * c / (a.Count + b.Count);
    }

    // Parecido entre dos textos (0..1), como difflib: 2*coincidencias/(largo total).
    public static double Parecido(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;
        return 2.0 * Coincidencias(a, 0, a.Length, b, 0, b.Length) / (a.Length + b.Length);
    }

    static int Coincidencias(string a, int a0, int a1, string b, int b0, int b1)
    {
        int mejor = 0, ia = 0, ib = 0;
        for (int i = a0; i < a1; i++)
            for (int j = b0; j < b1; j++)
            {
                int k = 0;
                while (i + k < a1 && j + k < b1 && a[i + k] == b[j + k]) k++;
                if (k > mejor) { mejor = k; ia = i; ib = j; }
            }
        if (mejor == 0) return 0;
        return mejor + Coincidencias(a, a0, ia, b, b0, ib) + Coincidencias(a, ia + mejor, a1, b, ib + mejor, b1);
    }

    static Tema Emparejar(string titulo, string album, string fuente)
    {
        string n = Norm(titulo);
        if (n.Length == 0) return null;
        string parte = fuente == "SC" || fuente == "GW" || fuente == "DU" || fuente == "SO" || fuente == "PB/BT" ? fuente : null;
        string objetivo = null;
        if ((fuente == "SC" || fuente == "GW") && alias.ContainsKey(n)) objetivo = alias[n];
        Tema mejor = null;
        double puntaje = 0;
        if (objetivo == null && fuente != "SC" && fuente != "GW" && fuente != "DU" && fuente != "SO" && fuente != "PB/BT" &&
            fuente != "canción" && fuente != "otro") return null;
        string alb = (album ?? "").ToLowerInvariant();
        Dictionary<string, bool> pn = Pares(n);
        foreach (Tema t in catalogo)
        {
            double s;
            if (objetivo != null) s = t.N == objetivo ? 1.5 : 0;
            else
            {
                if (Math.Abs(t.N.Length - n.Length) > Math.Max(t.N.Length, n.Length) / 2 + 3) continue;
                if (n != t.N && Dice(pn, t.Pares) < 0.5) continue;
                s = Parecido(n, t.N);
                if (t.O.Length > 0 && alb.Contains(t.O)) s += 0.15;
            }
            if (parte != null && t.P == parte) s += 0.001;     // desempata: el mismo nombre en su propia parte
            if (s > puntaje) { mejor = t; puntaje = s; }
        }
        return puntaje >= 0.86 ? mejor : null;
    }

    // ------------------------------------------------------------- indexar

    public static BibliotecaMusica Indexar(string carpeta, List<FilaMusica> filas, Action<string, double> avance)
    {
        CargarCatalogo();
        BibliotecaMusica b = new BibliotecaMusica();
        b.Carpeta = carpeta;
        for (int i = 0; i < filas.Count; i++)
        {
            FilaMusica f = filas[i];
            if (avance != null && i % 25 == 0) avance("Emparejando " + (i + 1) + " de " + filas.Count + "…", (double)i / filas.Count);
            ArchivoMusica a = new ArchivoMusica();
            a.Ruta = f.Ruta; a.Titulo = f.Titulo.Length > 0 ? f.Titulo : Path.GetFileNameWithoutExtension(f.Ruta);
            a.Album = f.Album; a.Duracion = f.Duracion;
            a.Fuente = Fuente(a.Album, a.Ruta);
            Tema t = Emparejar(a.Titulo, a.Album, a.Fuente);
            if (t != null)
            {
                a.TemaAnime = t.T; a.Parte = t.P; a.Usos = t.U; a.LargoTipico = t.L; a.Momento = t.M;
                a.Animos.AddRange(t.A); a.TemaDe.AddRange(t.D); a.Escenas.AddRange(t.E);
            }
            if (a.Animos.Count == 0)
                foreach (KeyValuePair<string, string> kv in Animo)
                    if (Regex.IsMatch(a.Titulo, kv.Value, RegexOptions.IgnoreCase) && a.Animos.Count < 2) a.Animos.Add(kv.Key);
            // Lo que no es del anime: la subcarpeta tambien dice el animo («Juegos/Pelea/...»).
            if (!a.ConUso && a.Fuente != "SBR fan")
            {
                List<string> porCarpeta = AnimosDeCarpeta(carpeta, a.Ruta);
                if (porCarpeta.Count > 0)
                {
                    a.Animos.Clear(); a.Animos.AddRange(porCarpeta);
                    a.Etiquetado = true; a.Descripcion = "por su carpeta";
                }
            }
            b.Archivos.Add(a);
        }
        b.MarcarVariantes();
        return b;
    }

    // Animos que dicen las subcarpetas (dentro de la carpeta de la musica), por palabra completa.
    public static List<string> AnimosDeCarpeta(string carpeta, string ruta)
    {
        List<string> r = new List<string>();
        string dir = Path.GetDirectoryName(ruta) ?? "";
        string raiz = (carpeta ?? "").TrimEnd('\\', '/');
        if (raiz.Length > 0 && dir.StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) dir = dir.Substring(raiz.Length);
        else if (Path.IsPathRooted(dir)) return r;
        dir = Norm(dir.Replace('\\', ' ').Replace('/', ' '));
        foreach (KeyValuePair<string, string> kv in Animo)
            if (Regex.IsMatch(dir, @"(?<![a-z])(" + kv.Value + ")", RegexOptions.IgnoreCase) && r.Count < 2) r.Add(kv.Key);
        return r;
    }

    // Variantes: el mismo tema en otra version o en otro album.
    public void MarcarVariantes()
    {
        Dictionary<string, List<ArchivoMusica>> grupos = new Dictionary<string, List<ArchivoMusica>>();
        foreach (ArchivoMusica a in Archivos)
        {
            string k = a.TemaAnime.Length > 0 ? "t:" + Norm(a.TemaAnime) + "|" + a.Parte : "n:" + Nucleo(a.Titulo);
            if (k.Length <= 2) continue;
            List<ArchivoMusica> l;
            if (!grupos.TryGetValue(k, out l)) { l = new List<ArchivoMusica>(); grupos[k] = l; }
            l.Add(a);
        }
        foreach (List<ArchivoMusica> l in grupos.Values)
            foreach (ArchivoMusica a in l)
            {
                a.Variantes.Clear();
                foreach (ArchivoMusica b in l) if (b != a) a.Variantes.Add(b.Ruta);
            }
    }

    // Archivos de la carpeta con sus etiquetas (titulo, album, duracion) leidas
    // por el Explorador de Windows; si no se puede, solo el nombre del archivo.
    public static List<FilaMusica> Escanear(string carpeta, Action<string, double> avance)
    {
        List<string> rutas = new List<string>();
        foreach (string f in Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories))
            if (Array.IndexOf(Extensiones, Path.GetExtension(f).ToLowerInvariant()) >= 0) rutas.Add(f);
        rutas.Sort(StringComparer.OrdinalIgnoreCase);
        object shell = null;
        Type tipo = null;
        try { tipo = Type.GetTypeFromProgID("Shell.Application"); if (tipo != null) shell = Activator.CreateInstance(tipo); } catch { shell = null; }
        Dictionary<string, object> carpetas = new Dictionary<string, object>();
        List<FilaMusica> filas = new List<FilaMusica>();
        string raiz = carpeta.TrimEnd('\\', '/');
        for (int i = 0; i < rutas.Count; i++)
        {
            if (avance != null && i % 20 == 0) avance("Leyendo " + (i + 1) + " de " + rutas.Count + "…", (double)i / Math.Max(1, rutas.Count));
            string r = rutas[i];
            FilaMusica f = new FilaMusica();
            f.Ruta = r.Substring(raiz.Length + 1);
            if (shell != null)
                try
                {
                    string dir = Path.GetDirectoryName(r);
                    object ns;
                    if (!carpetas.TryGetValue(dir, out ns))
                    {
                        ns = tipo.InvokeMember("NameSpace", BindingFlags.InvokeMethod, null, shell, new object[] { dir });
                        carpetas[dir] = ns;
                    }
                    object item = ns.GetType().InvokeMember("ParseName", BindingFlags.InvokeMethod, null, ns, new object[] { Path.GetFileName(r) });
                    f.Titulo = Detalle(ns, item, 21);
                    f.Album = Detalle(ns, item, 14);
                    f.Duracion = Segundos(Detalle(ns, item, 27));
                }
                catch { }
            filas.Add(f);
        }
        return filas;
    }

    static string Detalle(object ns, object item, int i)
    {
        object v = ns.GetType().InvokeMember("GetDetailsOf", BindingFlags.InvokeMethod, null, ns, new object[] { item, i });
        return (v as string ?? "").Replace("‎", "").Replace("‏", "").Trim();
    }

    public static double Segundos(string d)
    {
        double s = 0;
        foreach (string p in (d ?? "").Split(':'))
        {
            int x;
            if (!int.TryParse(p.Trim(), out x)) return 0;
            s = s * 60 + x;
        }
        return s;
    }

    // El listado de PowerShell (musica.csv), por si se prefiere.
    public static List<FilaMusica> DesdeCsv(string ruta)
    {
        List<FilaMusica> filas = new List<FilaMusica>();
        string[] lineas = File.ReadAllLines(ruta, Encoding.UTF8);
        if (lineas.Length == 0) return filas;
        List<string> cab = Csv(lineas[0]);
        int iR = cab.IndexOf("Ruta"), iT = cab.IndexOf("Titulo"), iA = cab.IndexOf("Album"), iD = cab.IndexOf("Duracion"), iN = cab.IndexOf("Archivo");
        for (int k = 1; k < lineas.Length; k++)
        {
            List<string> c = Csv(lineas[k]);
            if (iR < 0 || c.Count <= iR) continue;
            FilaMusica f = new FilaMusica();
            f.Ruta = c[iR];
            f.Titulo = iT >= 0 && iT < c.Count && c[iT].Length > 0 ? c[iT] : (iN >= 0 && iN < c.Count ? c[iN] : "");
            f.Album = iA >= 0 && iA < c.Count ? c[iA] : "";
            f.Duracion = iD >= 0 && iD < c.Count ? Segundos(c[iD]) : 0;
            filas.Add(f);
        }
        return filas;
    }

    static List<string> Csv(string l)
    {
        List<string> r = new List<string>();
        StringBuilder sb = new StringBuilder();
        bool comillas = false;
        for (int i = 0; i < l.Length; i++)
        {
            char c = l[i];
            if (comillas)
            {
                if (c == '"' && i + 1 < l.Length && l[i + 1] == '"') { sb.Append('"'); i++; }
                else if (c == '"') comillas = false;
                else sb.Append(c);
            }
            else if (c == '"') comillas = true;
            else if (c == ',') { r.Add(sb.ToString()); sb.Length = 0; }
            else sb.Append(c);
        }
        r.Add(sb.ToString());
        if (r.Count > 0) r[0] = r[0].TrimStart('﻿');
        return r;
    }

    // ------------------------------------------------------ guardar y cargar

    public void Guardar(string ruta)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-musica";
        d["carpeta"] = Carpeta;
        d["fecha"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        List<object> l = new List<object>();
        foreach (ArchivoMusica a in Archivos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["ruta"] = a.Ruta; x["titulo"] = a.Titulo; x["album"] = a.Album; x["duracion"] = Math.Round(a.Duracion);
            x["fuente"] = a.Fuente; x["animos"] = new List<object>(a.Animos.ToArray());
            if (a.ConUso)
            {
                x["tema_anime"] = a.TemaAnime; x["parte"] = a.Parte; x["usos"] = a.Usos; x["largo_tipico"] = a.LargoTipico;
                x["momento"] = a.Momento; x["escenas"] = new List<object>(a.Escenas.ToArray());
                if (a.TemaDe.Count > 0) x["tema_de"] = new List<object>(a.TemaDe.ToArray());
            }
            else if (a.Etiquetado)
            {
                x["ia"] = true; x["momento"] = a.Momento; x["descripcion"] = a.Descripcion;
            }
            if (a.Variantes.Count > 0) x["variantes"] = new List<object>(a.Variantes.ToArray());
            l.Add(x);
        }
        d["archivos"] = l;
        File.WriteAllText(ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    public static BibliotecaMusica Cargar(string carpeta)
    {
        string ruta = Path.Combine(carpeta, NombreIndice);
        if (!File.Exists(ruta)) return null;
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        BibliotecaMusica b = new BibliotecaMusica();
        b.Carpeta = carpeta;
        foreach (object x in Json.Lista(o, "archivos"))
        {
            ArchivoMusica a = new ArchivoMusica();
            a.Ruta = Json.Texto(x, "ruta"); a.Titulo = Json.Texto(x, "titulo"); a.Album = Json.Texto(x, "album");
            a.Duracion = Json.Numero(x, "duracion", 0); a.Fuente = Json.Texto(x, "fuente");
            a.TemaAnime = Json.Texto(x, "tema_anime"); a.Parte = Json.Texto(x, "parte");
            a.Usos = (int)Json.Numero(x, "usos", 0); a.LargoTipico = (int)Json.Numero(x, "largo_tipico", 0);
            a.Momento = Json.Texto(x, "momento"); a.Descripcion = Json.Texto(x, "descripcion");
            object ia = Json.Valor(x, "ia");
            a.Etiquetado = ia is bool && (bool)ia;
            foreach (object y in Json.Lista(x, "animos")) a.Animos.Add((string)y);
            foreach (object y in Json.Lista(x, "tema_de")) a.TemaDe.Add((string)y);
            foreach (object y in Json.Lista(x, "escenas")) a.Escenas.Add((string)y);
            foreach (object y in Json.Lista(x, "variantes")) a.Variantes.Add((string)y);
            b.Archivos.Add(a);
        }
        return b;
    }

    // ------------------------------------- etiquetar con IA (juegos, fanmade)

    public static readonly string[] Animos = { "calma", "viaje", "comedia", "misterio", "tension", "pelea", "villano", "epico",
                                               "victoria", "tristeza" };

    // Al volver a indexar, lo que la IA ya etiqueto se conserva.
    public void ConservarEtiquetas(BibliotecaMusica vieja)
    {
        if (vieja == null) return;
        foreach (ArchivoMusica a in Archivos)
        {
            if (a.ConUso) continue;
            ArchivoMusica v = vieja.Buscar(a.Ruta);
            if (v == null || !v.Etiquetado) continue;
            a.Etiquetado = true; a.Momento = v.Momento; a.Descripcion = v.Descripcion;
            a.Animos.Clear(); a.Animos.AddRange(v.Animos);
        }
    }

    // Lo que no tiene datos del anime ni etiquetas de la IA.
    public List<ArchivoMusica> PorEtiquetar()
    {
        List<ArchivoMusica> r = new List<ArchivoMusica>();
        foreach (ArchivoMusica a in Archivos) if (!a.ConUso && !a.Etiquetado && a.Fuente != "SBR fan") r.Add(a);
        return r;
    }

    public static string InstruccionesEtiquetar()
    {
        return "Eres supervisor musical de una serie de YouTube de Minecraft editada como un anime. Te paso archivos de música que " +
               "no son del anime (bandas sonoras de videojuegos, fanmade, remixes...) con su título, álbum y carpeta. Por lo que " +
               "sabes de cada tema (si lo conoces) o por su título, álbum y carpeta, di cómo suena y para qué escenas sirve.\n" +
               "- \"animos\": 1 a 3 de: " + String.Join(", ", Animos) + ".\n" +
               "- \"momento\": dónde queda mejor (inicio, exploración, construcción, pelea, jefe, cliffhanger, epílogo, menú...).\n" +
               "- \"descripcion\": cómo suena, en pocas palabras (instrumentos, tempo, energía).\n" +
               "- Si no tienes idea de cómo suena uno, no lo pongas (mejor nada que inventar).\n" +
               "Responde SOLO con JSON: {\"temas\": [{\"id\": n, \"animos\": [\"...\"], \"momento\": \"...\", \"descripcion\": \"...\"}]}";
    }

    public string MensajeEtiquetar(List<ArchivoMusica> lote)
    {
        StringBuilder sb = new StringBuilder("ARCHIVOS [id] título | álbum | carpeta | duración\n");
        for (int i = 0; i < lote.Count; i++)
        {
            ArchivoMusica a = lote[i];
            string dir = Path.GetDirectoryName(a.Ruta) ?? "";
            sb.Append("[" + i + "] " + a.Titulo + " | " + a.Album + " | " + dir + " | " + Math.Round(a.Duracion) + " s\n");
        }
        return sb.ToString();
    }

    // Pone las etiquetas de la respuesta; devuelve cuantos quedaron etiquetados.
    public static int AplicarEtiquetas(List<ArchivoMusica> lote, string json)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        int n = 0;
        foreach (object x in Json.Lista(o, "temas"))
        {
            int id = (int)Json.Numero(x, "id", -1);
            if (id < 0 || id >= lote.Count) continue;
            List<string> an = new List<string>();
            foreach (object y in Json.Lista(x, "animos"))
            {
                string k = Norm(y as string).Replace(" ", "");
                if (Array.IndexOf(Animos, k) >= 0 && !an.Contains(k)) an.Add(k);
            }
            if (an.Count == 0) continue;
            ArchivoMusica a = lote[id];
            a.Animos.Clear(); a.Animos.AddRange(an);
            a.Momento = Json.Texto(x, "momento"); a.Descripcion = Json.Texto(x, "descripcion");
            a.Etiquetado = true;
            n++;
        }
        return n;
    }

    public ArchivoMusica Buscar(string ruta)
    {
        foreach (ArchivoMusica a in Archivos) if (String.Equals(a.Ruta, ruta, StringComparison.OrdinalIgnoreCase)) return a;
        return null;
    }

    public string Completa(string ruta) { return Path.Combine(Carpeta, ruta); }
}
