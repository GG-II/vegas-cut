using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

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
    public string Estructura = "";   // como abre, como avanza y como cierra (para no repetir la formula)
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
        f.Estructura = Json.Texto(o, "estructura");
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
        d["estructura"] = Estructura;
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
    public string Papel = "Normal";
    public bool Existe { get { return File.Exists(Veg); } }
    public bool Transcrito { get { return File.Exists(Transcripcion.RutaPara(Veg)); } }
    public bool TieneFicha { get { return File.Exists(Ficha.RutaPara(Veg)); } }
}

public class SerieProyecto
{
    public static readonly string[] Tipos = { "Gameplay", "Video ensayo", "Podcast", "Otro" };

    public string Ruta = "", Nombre = "", Tipo = "Gameplay", Notas = "", Carpeta = "";
    public FormatoSerie Formato = FormatoSerie.Preset("100 días");
    public MusicaSerie Musica = new MusicaSerie();
    public List<string> Episodios = new List<string>();   // rutas de los .veg, en orden
    // Papel de cada capitulo (primero, especial, final...) y su nota, por
    // ruta del .veg. Lo que no esta aqui es "Normal" (o "Primer capítulo").
    public Dictionary<string, string> Papeles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> NotasEpisodio = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
        object f = Json.Valor(o, "estructura");
        s.Formato = f != null ? FormatoSerie.Leer(f) : FormatoSerie.Preset(FormatoSerie.SegunTipo(s.Tipo));
        s.Musica = MusicaSerie.Leer(Json.Valor(o, "musica"));
        string dir = Path.GetDirectoryName(ruta);
        foreach (object x in Json.Lista(o, "episodios"))
        {
            string veg = Json.Texto(x, "veg"), rel = Json.Texto(x, "relativo");
            // Si se movio la carpeta (u otra letra de disco), se busca junto al archivo de la serie.
            if (!File.Exists(veg) && rel.Length > 0 && File.Exists(Path.Combine(dir, rel))) veg = Path.GetFullPath(Path.Combine(dir, rel));
            if (veg.Length == 0) continue;
            s.Episodios.Add(veg);
            string papel = Json.Texto(x, "papel");
            if (Array.IndexOf(PapelEpisodio.Papeles, papel) >= 0) s.Papeles[veg] = papel;
            string nota = Json.Texto(x, "nota");
            if (nota.Length > 0) s.NotasEpisodio[veg] = nota;
        }
        return s;
    }

    public void Guardar()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-serie";
        d["nombre"] = Nombre; d["tipo"] = Tipo; d["notas"] = Notas; d["carpeta"] = Carpeta;
        d["estructura"] = Formato.Escribir();
        d["musica"] = Musica.Escribir();
        List<object> l = new List<object>();
        foreach (string veg in Episodios)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["veg"] = veg;
            x["relativo"] = Relativa(Path.GetDirectoryName(Ruta), veg);
            string papel, nota;
            if (Papeles.TryGetValue(veg, out papel)) x["papel"] = papel;
            if (NotasEpisodio.TryGetValue(veg, out nota) && nota.Trim().Length > 0) x["nota"] = nota.Trim();
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

    // Papel del capitulo: el elegido o, si no, "Primer capítulo" para el
    // primero de la lista y "Normal" para los demas.
    public string Papel(string veg)
    {
        string p;
        if (!String.IsNullOrEmpty(veg) && Papeles.TryGetValue(veg, out p)) return p;
        return IndiceDe(veg ?? "") == 0 ? "Primer capítulo" : "Normal";
    }

    public void CambiarPapel(string veg, string papel)
    {
        if (Array.IndexOf(PapelEpisodio.Papeles, papel) >= 0) Papeles[veg] = papel;
    }

    public string NotaEpisodio(string veg)
    {
        string n;
        return !String.IsNullOrEmpty(veg) && NotasEpisodio.TryGetValue(veg, out n) ? n : "";
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
            if (Regex.IsMatch(Path.GetFileNameWithoutExtension(f), @"\s(BASE|CAP)$", RegexOptions.IgnoreCase)) continue;   // copias del mismo capitulo
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
            c.Papel = Papel(Episodios[i]);
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

// De que va la serie y como se edita: formato, premisa, como se marca el
// avance (Dia N, Parte N...), el narrador y las reglas de ritmo. Lo usan
// MomentosIA (contexto) y PulirEpisodio (medidor, estructura y narracion).
public class FormatoSerie
{
    public static readonly string[] Formatos = { "100 días", "Aventura por episodios", "Serie de TV / anime", "Retos / minijuegos",
                                                 "Video ensayo", "Top / lista", "Podcast", "Otro" };
    public const string TV = "Serie de TV / anime";
    public static readonly string[] Avances = { "Día N", "Parte N", "Etapa N", "Ronda N", "Acto N", "Número N", "Ninguno" };

    public string Nombre = "100 días";
    public string Premisa = "";            // de que va y que se busca (el objetivo de la serie)
    public string Avance = "Día N";
    public bool Narrador = true;
    public string NarradorNombre = "Narrador";   // hablante de la transcripcion
    public string EstiloNarrador = "";
    public string Aprendido = "";          // de que proyecto salieron las reglas
    public ReglasRitmo Reglas = new ReglasRitmo();
    public PlantillaTV Tv = PlantillaTV.PorDefecto();   // bloques y kit (formato Serie de TV)
    public bool EsTV { get { return Nombre == TV; } }

    public static string SegunTipo(string tipo)
    {
        switch (tipo)
        {
            case "Video ensayo": return "Video ensayo";
            case "Podcast": return "Podcast";
            case "Gameplay": return "100 días";
            default: return "Otro";
        }
    }

    // Valores de partida de cada formato. "100 días" sale de lo que funcionó
    // en JoJoMania (docs/estilo-jojomania.md).
    public static FormatoSerie Preset(string formato)
    {
        FormatoSerie f = new FormatoSerie();
        f.Nombre = Array.IndexOf(Formatos, formato) >= 0 ? formato : "Otro";
        ReglasRitmo r = f.Reglas;
        switch (f.Nombre)
        {
            case "100 días":
                f.Avance = "Día N";
                f.EstiloNarrador = "En pasado, como un cuento, con humor; deja ganchos de anticipación (\"lo cual seguramente no fue " +
                                   "la mejor idea\") y resume cada día en una o dos frases.";
                break;
            case "Aventura por episodios":
                f.Avance = "Parte N";
                f.EstiloNarrador = "En pasado, como un cuento: presenta el objetivo de la parte, los obstáculos y deja el gancho " +
                                   "para la siguiente.";
                r.NarradorCadaSeg = 90; r.DuracionMin = 12; r.DuracionMax = 16;
                break;
            case TV:
                f.Avance = "Etapa N";
                f.EstiloNarrador = "Como Johnny en Steel Ball Run: primera persona, en pasado; enmarca el inicio y el final del " +
                                   "capítulo y une escenas con frases cortas. Deja que los personajes cuenten la historia.";
                r.NarradorCadaSeg = 120; r.RecursosPorMin = 3; r.CortesMin = 14; r.CortesMax = 20; r.MusicaCadaSeg = 45;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 15; r.DuracionMax = 18;
                break;
            case "Retos / minijuegos":
                f.Avance = "Ronda N";
                f.EstiloNarrador = "Rápido y con energía: reglas del reto en una frase, marcador y quién va ganando.";
                r.NarradorCadaSeg = 60; r.RecursosPorMin = 5; r.CortesMin = 18; r.CortesMax = 25; r.MusicaCadaSeg = 30;
                r.DuracionMin = 10; r.DuracionMax = 14;
                break;
            case "Video ensayo":
                f.Avance = "Acto N";
                f.EstiloNarrador = "Primera persona, cercano; plantea una pregunta al inicio y la responde paso a paso, con ejemplos.";
                r.NarradorCadaSeg = 20; r.RecursosPorMin = 6; r.CortesMin = 8; r.CortesMax = 14; r.MusicaCadaSeg = 60;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 12; r.DuracionMax = 20;
                break;
            case "Top / lista":
                f.Avance = "Número N";
                f.EstiloNarrador = "Directo: presenta cada puesto con un dato que sorprenda; guarda el mejor para el final.";
                r.NarradorCadaSeg = 30; r.RecursosPorMin = 6; r.CortesMin = 12; r.CortesMax = 18; r.MusicaCadaSeg = 45;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 8; r.DuracionMax = 12;
                break;
            case "Podcast":
                f.Avance = "Ninguno";
                f.Narrador = false;
                r.NarradorCadaSeg = 300; r.RecursosPorMin = 1; r.CortesMin = 4; r.CortesMax = 10; r.MusicaCadaSeg = 300;
                r.ZonaCriticaSeg = 120; r.DuracionMin = 30; r.DuracionMax = 60;
                break;
            default:
                f.Avance = "Ninguno";
                break;
        }
        return f;
    }

    // Que busca cada formato (para Gemini).
    public static string Objetivo(string formato)
    {
        switch (formato)
        {
            case "100 días": return "sobrevivir y progresar día a día; cada día debe aportar un avance, un problema o una risa, y el video " +
                                    "termina con algo pendiente para el siguiente";
            case "Aventura por episodios": return "avanzar en una historia por partes; cada parte tiene un objetivo, obstáculos y un final con gancho";
            case TV: return "un capítulo de serie de TV al estilo del anime de JoJo: cold open con gancho, opening, actos con " +
                            "carteles de lugar y tiempo, crisis a la mitad, resolución con ranking y cliffhanger con «continuará»";
            case "Retos / minijuegos": return "competir en rondas; se entiende quién va ganando y la tensión sube hasta la última ronda";
            case "Video ensayo": return "responder una pregunta o defender una idea con argumentos y ejemplos, en actos claros";
            case "Top / lista": return "recorrer una lista de menor a mayor; cada puesto se justifica y el mejor queda para el final";
            case "Podcast": return "una charla con temas claros; se marcan los cambios de tema y los mejores momentos";
            default: return "";
        }
    }

    public FormatoSerie Copia()
    {
        FormatoSerie f = (FormatoSerie)MemberwiseClone();
        f.Reglas = Reglas.Copia();
        f.Tv = Tv.Copia();
        return f;
    }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = Nombre; d["premisa"] = Premisa; d["avance"] = Avance; d["narrador"] = Narrador;
        d["narradorNombre"] = NarradorNombre; d["estiloNarrador"] = EstiloNarrador; d["aprendido"] = Aprendido;
        Dictionary<string, object> r = new Dictionary<string, object>();
        Reglas.Escribir(r);
        d["ritmo"] = r;
        if (EsTV) d["tv"] = Tv.Escribir();
        return d;
    }

    public static FormatoSerie Leer(object o)
    {
        string nombre = Json.Texto(o, "formato");
        FormatoSerie f = Preset(nombre);
        f.Premisa = Json.Texto(o, "premisa");
        string av = Json.Texto(o, "avance");
        if (Array.IndexOf(Avances, av) >= 0) f.Avance = av;
        object n = Json.Valor(o, "narrador");
        if (n is bool) f.Narrador = (bool)n;
        string nn = Json.Texto(o, "narradorNombre");
        if (nn.Length > 0) f.NarradorNombre = nn;
        if (Json.Valor(o, "estiloNarrador") != null) f.EstiloNarrador = Json.Texto(o, "estiloNarrador");
        f.Aprendido = Json.Texto(o, "aprendido");
        f.Reglas = ReglasRitmo.Leer(Json.Valor(o, "ritmo"), f.Reglas);
        object tv = Json.Valor(o, "tv");
        if (tv != null) f.Tv = PlantillaTV.Leer(tv);
        return f;
    }

    // "Día 3", "Parte 3"... o "" si no se marca.
    public string Marca(int n) { return Avance == "Ninguno" ? "" : Avance.Replace("N", n.ToString()); }

    public string Texto()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Formato: " + Nombre);
        string obj = Objetivo(Nombre);
        if (obj.Length > 0) sb.Append(" (" + obj + ")");
        sb.Append("\n");
        if (Premisa.Trim().Length > 0) sb.Append("Premisa / objetivo de la serie: " + Premisa.Trim() + "\n");
        if (Avance != "Ninguno") sb.Append("El avance se marca con \"" + Avance + "\" en pantalla.\n");
        sb.Append(Narrador ? "Hay narrador (" + NarradorNombre + ")" + (EstiloNarrador.Trim().Length > 0 ? ": " + EstiloNarrador.Trim() : "") + "\n"
                           : "Sin narrador.\n");
        sb.Append("Duración objetivo: " + Reglas.DuracionMin + "–" + Reglas.DuracionMax + " min\n");
        return sb.ToString();
    }
}

// No todos los capitulos son iguales: el primero presenta, uno intermedio
// avanza, un especial rompe el formato y el final cierra hilos. Cada papel
// cambia las reglas de ritmo y lo que se le pide a Gemini, para que la serie
// no parezca hecha en fabrica.
public static class PapelEpisodio
{
    // Salen de analizar los primeros capitulos, los finales y los capitulos
    // clave de todas las partes del anime de JoJo (docs/estructura-episodio-sc.md).
    public static readonly string[] Papeles = { "Primer capítulo", "Normal", "Inicio de arco", "Capítulo clave", "Capítulo de respiro",
                                                "Especial", "Penúltimo (clímax)", "Final de temporada", "Final de la serie" };

    public static string Instrucciones(string papel)
    {
        switch (papel)
        {
            case "Primer capítulo":
                return "Es el PRIMER capítulo. En todas las partes de JoJo el primer episodio NO abre con el opening: empieza mostrando " +
                       "el mundo con calma (la ciudad, la radio del pueblo, el barco con el ataúd), presenta al protagonista con una " +
                       "escena que muestra cómo es, tiene una escena de «mentor» que explica las reglas, revela el poder hacia la mitad y " +
                       "cierra presentando la amenaza o el rival. Aquí: presenta la premisa y a cada jugador (quién es, un rasgo) sin prisa " +
                       "(la intro puede durar 2–4 min), explica de qué va la serie y termina prometiendo lo que viene. Ritmo pausado al " +
                       "inicio; el opening puede ir después de la intro o no ir.";
            case "Inicio de arco":
                return "Empieza un ARCO nuevo (otra etapa, otro rival, otro objetivo). Como en Golden Wind 20–21: arranque tranquilo " +
                       "con el grupo, un giro o traición hacia el 40 %, el re-gancho o eyecatch, un flashback o explicación de por qué " +
                       "importa, y cierre con una decisión del grupo o el nuevo rival en escena.";
            case "Capítulo clave":
                return "Es un capítulo CLAVE (una muerte, una revelación grande, la llegada al destino). En JoJo estos capítulos van " +
                       "más LENTOS que la media: menos diálogo, silencios largos, temas de música largos y más voz interna. Dale aire " +
                       "al momento (respiros largos, ritmo lento alrededor) y ponlo hacia el final como cierre (la muerte de Avdol e " +
                       "Iggy está al 90 %) o a la mitad con sus consecuencias después.";
            case "Capítulo de respiro":
                return "Es un capítulo de RESPIRO (humor, vida diaria, exploración tranquila), como los de Diamond is Unbreakable: " +
                       "sin pelea al inicio, el foco en los personajes y sus manías, un pequeño problema que crece y un final feliz o " +
                       "cómico. Ritmo ligero, música de comedia y calma.";
            case "Especial":
                return "Es un capítulo ESPECIAL: puede romper el formato (otra estructura, otro ritmo, otro tipo de inicio). " +
                       "Que se note desde el inicio qué lo hace distinto; no repitas la fórmula de los capítulos normales.";
            case "Penúltimo (clímax)":
                return "Es el PENÚLTIMO capítulo: abre con el recap del cliffhanger anterior, la tensión está al máximo de principio a " +
                       "fin, puede caer alguien importante y termina en el PEOR momento («DIO está totalmente sincronizado»), sin remate " +
                       "cómico.";
            case "Final de temporada":
                return "Es el FINAL DE TEMPORADA. En los finales de JoJo el clímax se resuelve entre el 40 y el 75 % y después viene " +
                       "un EPÍLOGO largo (25–50 %): despedidas, la broma de siempre del grupo, «la vida sigue», con música tranquila y " +
                       "el ending sonando sobre el epílogo; a veces sin opening. Paga los hilos abiertos de la temporada y termina con " +
                       "un nuevo estado de cosas y un gancho pequeño para la próxima.";
            case "Final de la serie":
                return "Es el FINAL DE LA SERIE. Como los finales de JoJo: el clímax se resuelve entre el 40 y el 75 % y queda un " +
                       "EPÍLOGO largo con despedidas, recuerdos de capítulos anteriores, la broma de siempre del grupo y el ending " +
                       "sobre el epílogo; puede no tener opening. Cierra todos los hilos; no anuncies un próximo episodio.";
            default:
                return "Es un capítulo intermedio: recuerda en pocos segundos dónde quedó la historia (si el anterior terminó en " +
                       "cliffhanger, el cold open es su recap), avanza con algo nuevo (un logro, un problema, alguien nuevo) y termina " +
                       "con un pendiente para el siguiente. Varía el inicio y los recursos respecto a los capítulos anteriores.";
        }
    }

    // Reglas ajustadas al papel.
    public static ReglasRitmo Reglas(ReglasRitmo r, string papel)
    {
        ReglasRitmo x = r.Copia();
        switch (papel)
        {
            case "Primer capítulo":
                x.ZonaCriticaSeg = Math.Max(x.ZonaCriticaSeg, 180);
                x.NarradorCadaSeg = Math.Max(30, (int)(x.NarradorCadaSeg * 0.8));
                x.DuracionMax = Math.Round(x.DuracionMax * 1.3);      // SBR estreno con un episodio doble
                break;
            case "Capítulo clave":
                x.CortesMin = Math.Max(4, (int)(x.CortesMin * 0.75));
                x.CortesMax = Math.Max(x.CortesMin + 2, (int)(x.CortesMax * 0.8));
                x.MusicaCadaSeg = (int)(x.MusicaCadaSeg * 2);       // temas largos (E46 de SC: mediana de 2 min)
                break;
            case "Capítulo de respiro":
                x.MusicaCadaSeg = (int)(x.MusicaCadaSeg * 1.2);
                break;
            case "Especial":
                x.DuracionMin = Math.Max(1, Math.Round(x.DuracionMin * 0.7));
                x.DuracionMax = Math.Round(x.DuracionMax * 1.4);
                break;
            case "Penúltimo (clímax)":
                x.CortesMax = (int)(x.CortesMax * 1.15);
                break;
            case "Final de temporada":
            case "Final de la serie":
                x.DuracionMax = Math.Round(x.DuracionMax * 1.4);
                x.MusicaCadaSeg = (int)(x.MusicaCadaSeg * 1.3);   // temas mas largos en el climax y el epilogo
                break;
        }
        return x;
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
    static readonly Regex Parte = new Regex(@"\b(?:parte|part|cap(?:itulo|ítulo)?|ep(?:isodio)?|episode)\s*[-_ ]?\s*(\d{1,3})", RegexOptions.IgnoreCase);

    // "S01E02 SCR" -> temporada 1, capitulo 2, serie "# scr". Tambien
    // "Parte 3", "Cap 3" o "Ep 3" (temporada 1).
    public static bool Clave(string nombre, out int temporada, out int numero, out string serie)
    {
        temporada = 0; numero = 0; serie = "";
        nombre = Regex.Replace(nombre ?? "", @"\s+(BASE|CAP)$", "", RegexOptions.IgnoreCase);
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
            case "Video ensayo": return "una serie de video ensayos en español (varias partes)";
            case "Podcast": return "un podcast o serie de charlas en español";
            case "Gameplay": return "una serie de gameplays en español (con amigos)";
            default: return "una serie de videos de YouTube en español";
        }
    }

    public static string InstruccionesFicha() { return InstruccionesFicha("Gameplay"); }

    public static string InstruccionesFicha(string tipo)
    {
        return "Eres editor de " + QueEs(tipo) + ". Recibes la transcripción de lo que quedó en un capítulo. " +
               "Haz su ficha para usarla de contexto al editar los otros capítulos.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"qué pasa en el capítulo, en orden, en 3 a 6 frases\",\n " + QueGuardar(tipo) +
 " \"estructura\": \"cómo abre (tipo de gancho), cómo avanza y cómo cierra el capítulo, en una frase\",\n" +
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

    // Contexto para MomentosIA: la serie, sus notas, anteriores y posteriores.
    public static string Contexto(SerieProyecto serie, List<CapSerie> caps)
    {
        StringBuilder sb = new StringBuilder();
        if (serie != null)
        {
            sb.Append("Serie: " + serie.Nombre + " (" + serie.Tipo + ")\n");
            sb.Append(serie.Formato.Texto());
            if (!String.IsNullOrEmpty(serie.Notas)) sb.Append("Notas de la serie:\n" + serie.Notas.Trim() + "\n");
            foreach (CapSerie c in caps)
            {
                if (c.Relacion != 0) continue;
                sb.Append("Este capítulo (" + c.Posicion + " de " + caps.Count + "): " + c.Papel + ". " + PapelEpisodio.Instrucciones(c.Papel) + "\n");
                string nota = serie.NotaEpisodio(c.Veg);
                if (nota.Length > 0) sb.Append("Nota del editor para este capítulo: " + nota + "\n");
            }
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
