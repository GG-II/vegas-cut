using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// =====================================================================
// ProducirCapitulo: de la grabacion (copia BASE, sin silencios y
// transcrita) a un capitulo armado como serie.
//
// 1. Analisis + 3 propuestas: Gemini lee todo el material con el contexto
//    de la serie y propone tres formas de hacer el capitulo, cada una con su
//    estructura de bloques. Las propuestas se refinan con notas las veces que
//    haga falta (sin volver a analizar).
// 2. Propuesta final: la escaleta completa con su estructura (la plantilla
//    de la serie es solo el punto de partida: las notas mandan), tramos del
//    material, titulos y carteles, musica, narracion en las pausas y
//    placeholders. Se ajusta con cambios hasta que este bien.
// =====================================================================

public class MomentoMaterial
{
    public double Inicio, Fin;
    public string Tipo = "", Texto = "";
    public int Fuerza;
    public List<string> Personajes = new List<string>();
}

public class Propuesta
{
    public static readonly string[] Tipos = { "normal", "especial", "dos_partes", "doble_duracion" };

    public string Id = "", Nombre = "", Tipo = "normal", PorQue = "", ColdOpen = "", Cierre = "", Notas = "";
    public string TipoCap = "";        // clave de TiposCapitulo (juego, misterio, foco...)
    public List<string> Titulos = new List<string>(), Escaleta = new List<string>(), Estructura = new List<string>();
    public double Minutos;
    public bool Doble { get { return Tipo == "dos_partes"; } }             // dos videos
    public bool DobleDuracion { get { return Tipo == "doble_duracion"; } } // un video el doble de largo

    public string NombreTipo()
    {
        switch (Tipo)
        {
            case "dos_partes": return "DOS PARTES (dos videos)";
            case "doble_duracion": return "DOBLE DURACIÓN (un solo video)";
            case "especial": return "ESPECIAL";
            default: return "CAPÍTULO";
        }
    }

    public static string Normalizar(string tipo)
    {
        string t = (tipo ?? "").ToLowerInvariant().Replace(" ", "_").Replace("ó", "o");
        if (t == "doble" || t == "dos_episodios") return "dos_partes";
        if (t.StartsWith("doble_dur") || t == "largo") return "doble_duracion";
        return Array.IndexOf(Tipos, t) >= 0 ? t : "normal";
    }
}

// Las casillas de la ventana: cada una puede ser "que decida la IA" (-1),
// "sí" (1) o "no" (0), mas la duracion del video terminado.
public class OpcionesCapitulo
{
    // clave, nombre en la casilla, lo que se pide con "sí", lo que se pide con "no" ("" = nada)
    public static readonly string[][] Casillas = {
        new string[] { "cine", "Inicio cinematográfico",
            "INICIO CINEMATOGRÁFICO: abre con planos del mundo y del lugar sin diálogo (tramos de las PAUSAS donde nadie habla), " +
            "música, el título de la serie y la narración situando; ritmo lento; que se sienta un estreno", "" },
        new string[] { "presentar", "Presentar personajes",
            "PRESENTAR A CADA PERSONAJE la primera vez que aparece: un momento suyo que muestre cómo es y su tarjeta (texto " +
            "\"presentacion\": nombre y un rasgo, estilo JoJo)", "sin tarjetas de presentación" },
        new string[] { "explicar", "Contexto antes de cada escena",
            "NUNCA lanzar al espectador a una escena sin explicar antes de dónde viene: contexto (narración, cartel o un clip) " +
            "antes de cada escena nueva", "" },
        new string[] { "solo_juego", "Solo charla del juego",
            "CORTAR las conversaciones personales que no son del juego", "se pueden dejar charlas fuera del juego si son buenas" },
        new string[] { "cold_open", "Cold open", "lleva cold open antes del opening", "SIN cold open: empieza directo" },
        new string[] { "recap", "Recap del anterior", "abre con un recap corto del capítulo anterior", "SIN recap del capítulo anterior" },
        new string[] { "op", "Opening", "lleva opening", "SIN opening" },
        new string[] { "eyecatch", "Eyecatch", "lleva eyecatch (re-gancho) a la mitad", "SIN eyecatch" },
        new string[] { "continuara", "«Continuará»", "termina con «continuará»", "SIN «continuará»" },
        new string[] { "ed", "Ending", "lleva ending", "SIN ending" },
        new string[] { "avance", "Avance", "lleva avance del próximo capítulo", "SIN avance" },
        new string[] { "narrador", "Narración", "con narración en las pausas", "SIN narración" },
        new string[] { "carteles", "Carteles", "con carteles de lugar y tiempo", "SIN carteles de lugar y tiempo" },
        new string[] { "stats", "Stats", "con tarjetas de stats", "SIN tarjetas de stats" },
        new string[] { "doble", "Doble duración",
            "UN SOLO VIDEO DE DOBLE DURACIÓN (tipo doble_duracion): un opening, las mitades unidas por un eyecatch, sin segundo " +
            "opening ni segundo ending", "un video de duración normal (no doble)" },
    };

    public Dictionary<string, int> Estado = new Dictionary<string, int>();
    public int MinutosMin = 15, MinutosMax = 18;

    public int Valor(string clave) { int v; return Estado.TryGetValue(clave, out v) ? v : -1; }

    public static OpcionesCapitulo PorDefecto(string papel, int posicion, ReglasRitmo r)
    {
        OpcionesCapitulo o = new OpcionesCapitulo();
        o.MinutosMin = (int)Math.Round(r.DuracionMin); o.MinutosMax = (int)Math.Round(r.DuracionMax);
        if (papel == "Primer capítulo" || posicion == 1)
        {
            o.Estado["cine"] = 1; o.Estado["presentar"] = 1; o.Estado["explicar"] = 1; o.Estado["recap"] = 0;
        }
        else if (posicion >= 2 && posicion <= 3) o.Estado["cine"] = 1;
        return o;
    }

    // Lo que se le pide a Gemini (solo las casillas marcadas como sí o no).
    public string Texto(double duracionMaterial)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("OPCIONES ELEGIDAS POR EL EDITOR (obligatorias):\n");
        foreach (string[] c in Casillas)
        {
            int v = Valor(c[0]);
            if (v == 1) sb.Append("- " + c[2] + ".\n");
            else if (v == 0 && c[3].Length > 0) sb.Append("- " + c[3] + ".\n");
        }
        sb.Append("- DURACIÓN DEL VIDEO TERMINADO: entre " + MinutosMin + " y " + MinutosMax + " min" +
                  (Valor("doble") == 1 ? " (ya contando la doble duración)" : " por video") + ". " +
                  (duracionMaterial > 0 ? "El material dura " + Math.Round(duracionMaterial / 60) + " min: NO es la duración del video; " +
                                          "hay que elegir lo mejor y dejar fuera lo demás. " : "") +
                  "\"minutos\" de cada propuesta es lo que dura el video terminado.\n");
        return sb.ToString();
    }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        Dictionary<string, object> e = new Dictionary<string, object>();
        foreach (KeyValuePair<string, int> kv in Estado) if (kv.Value >= 0) e[kv.Key] = (double)kv.Value;
        d["casillas"] = e; d["min"] = (double)MinutosMin; d["max"] = (double)MinutosMax;
        return d;
    }

    public static OpcionesCapitulo Leer(object o)
    {
        if (o == null) return null;
        OpcionesCapitulo r = new OpcionesCapitulo();
        Dictionary<string, object> e = Json.Obj(o, "casillas");
        if (e != null) foreach (KeyValuePair<string, object> kv in e) r.Estado[kv.Key] = Convert.ToInt32(kv.Value);
        r.MinutosMin = (int)Json.Numero(o, "min", 15); r.MinutosMax = Math.Max(r.MinutosMin, (int)Json.Numero(o, "max", 18));
        return r;
    }
}

public class AnalisisCapitulo
{
    public string Resumen = "", Respuesta = "", RespuestaPropuestas = "";
    public double MinutosUtiles;
    public bool DobleRecomendado;
    public string DobleMotivo = "";
    // Tipo de capitulo y papel que Gemini ve en el material.
    public string TipoSugerido = "", TipoMotivo = "", PapelSugerido = "", PapelMotivo = "";
    public List<string> TiposAlternativos = new List<string>();
    public List<MomentoMaterial> Momentos = new List<MomentoMaterial>();
    public List<string> Hilos = new List<string>();
    public List<Propuesta> Propuestas = new List<Propuesta>();
}

// Un elemento de la propuesta final.
public class ItemFinal
{
    public int Parte;                  // 0 o 1 (dos partes)
    public string Bloque = "";         // clave del bloque de la estructura
    public string Tipo = "";           // clip, texto, musica, narracion, recurso
    public double Inicio, Fin;         // segundos del material (clips; los demas solo Inicio)
    public string Texto = "", Detalle = "", Clase = "";
    public int Musica = -1;            // id del candidato de musica
    public string Personaje = "";      // tema del personaje
    public string Id = "";             // N01, R01
    public double Respiro;             // pausa original que se recupera al final del clip (s)
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

public class CapituloFinal
{
    public string Titulo = "", Etapa = "";
    public List<BloqueTV> Estructura = new List<BloqueTV>();   // la de este capitulo (puede no ser la de la plantilla)
    public List<ItemFinal> Items = new List<ItemFinal>();

    public BloqueTV Bloque(string clave)
    {
        foreach (BloqueTV b in Estructura) if (b.Clave == clave) return b;
        return null;
    }
}

public class PlanFinal
{
    public string Respuesta = "", Resumen = "";
    public List<CapituloFinal> Partes = new List<CapituloFinal>();
    public double Repetido;            // segundos de material repetido que se quitaron

    public IEnumerable<ItemFinal> Todos()
    {
        foreach (CapituloFinal c in Partes) foreach (ItemFinal i in c.Items) yield return i;
    }
}

public static class LogicaProduccion
{
    static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    public static readonly string[] TiposMomento = { "llegada", "problema", "revelacion", "crisis", "giro", "resolucion", "comico", "emotivo", "gancho" };

    public const string Prioridad =
        "PRIORIDAD: las NOTAS, INDICACIONES y CAMBIOS del editor mandan. Si contradicen la plantilla, la estructura de referencia, " +
        "las reglas de ritmo o la propuesta, sigue lo que pide el editor (por ejemplo: sin opening, una intro más larga, otro orden, " +
        "unir dos mitades con un eyecatch). Lo demás son guías, no obligaciones.\n" +
        "FIDELIDAD: todo lo que propongas (títulos, lugares, carteles, escaleta, cold open, cierre, narración) tiene que PASAR en " +
        "la TRANSCRIPCIÓN de este material. No inventes lugares, nombres ni hechos: ni del anime (Steel Ball Run, JoJo) ni de los " +
        "capítulos posteriores (esos son solo contexto para no cortar lo que se retoma). El estilo JoJo es la forma, no el " +
        "contenido. Si un título usa un lugar, que sea uno que se nombra o se ve en el material.\n" +
        "PRIVACIDAD Y SOPORTE: NUNCA uses conversaciones privadas o personales del mundo real (familia, pareja, salud, trabajo, " +
        "escuela, dinero, datos personales) ni soporte técnico (instalar o actualizar el juego o el modpack, launcher, Java, RAM, " +
        "lag, crasheos, Discord, micrófono, «¿me escuchan?», la grabación). Las líneas marcadas ⟨técnico⟩ o ⟨personal⟩ en la " +
        "transcripción probablemente lo son: déjalas fuera salvo que el editor las pida.\n" +
        "NADA SE REPITE: lo que sale al inicio no vuelve a salir al final. El RECAP es del capítulo ANTERIOR (en el primer " +
        "capítulo no hay recap) y el AVANCE es del PRÓXIMO capítulo: ninguno de los dos usa clips de este material (quedan como " +
        "placeholder para llenarlos después).\n";

    // Las tres propuestas: el mismo capitulo visto de tres maneras.
    public const string Variantes =
        "LAS TRES PROPUESTAS cubren el MISMO material y los mismos momentos fuertes (el mismo arco de este capítulo, de principio " +
        "a fin); lo que cambia es el ENFOQUE: cómo abre, qué se destaca, el orden, el ritmo, desde quién se cuenta y cómo cierra. " +
        "Ninguna cuenta otra historia ni deja fuera los momentos clave.\n";

    // Lo que salio de analizar los 48 episodios de Stardust Crusaders y SBR
    // (docs/estructura-episodio-sc.md), resumido para Gemini.
    public const string Guia =
        "ESTRUCTURA DE REFERENCIA (anime de JoJo, Stardust Crusaders y Steel Ball Run), en % del capítulo:\n" +
        "- 0 %: llegada o viaje, con un cartel del lugar o del tiempo y humor del grupo.\n" +
        "- ~13 %: aparece el problema (rival, trampa, jefe), primero como algo raro, sin explicarlo.\n" +
        "- ~35 %: se revela qué es (como la carta del tarot del stand).\n" +
        "- ~45 %: crisis: les va mal. Aquí va el re-gancho de mitad de capítulo.\n" +
        "- ~58 %: giro: la idea, el truco o la ayuda.\n" +
        "- ~77 %: resolución (ganan o pierden la etapa, ranking).\n" +
        "- 85–95 %: remate cómico o calma.\n" +
        "- 95–99 %: gancho final: enemigo nuevo, siguiente etapa o cliffhanger, y «continuará».\n" +
        "Cold opens: recap del cliffhanger anterior, llegada con humor, escena del rival o en medio de la pelea. SBR los hace largos " +
        "(2–3 min) para que se entienda la situación y los termina en un misterio justo antes del opening. Nunca tan cortos que no " +
        "dé tiempo a entender de qué va.\n" +
        "Cierres: cliffhanger en plena crisis, anuncio de un enemigo nuevo, remate cómico, o seguir el viaje con el rival mirando.\n" +
        "Última frase antes del opening: amenaza del rival, una pregunta o frase cortada por la sorpresa, la llegada a un lugar, " +
        "una decisión del grupo o el remate de un chiste. Última antes del ending: declaración del rival o amenaza nueva (+ " +
        "«continuará»), una frase emotiva en calma, un chiste, «seguimos el viaje», un golpe en plena pelea o una noticia que " +
        "cambia todo. Que sea una frase que se entienda sola.\n" +
        "Curva de ritmo (medida en SC, DU y SBR): primer 20 % con más diálogo y más voz interna o narrador para situar; del 30 al " +
        "70 % más pausas largas y escenas sin música (tensión); repunte de velocidad en la pelea (60–80 %); el último 10 % más " +
        "calmado (menos líneas por minuto) salvo en un cliffhanger.\n" +
        "TÍTULOS al estilo JoJo (cortos, en español): el nombre del rival o del peligro («El Creeper de la cueva»), apodo + nombre " +
        "(«El cazador, Steel»), «¡X contra Y!» o «X conoce a Y», una invitación («¡Vamos al Nether!», «Rumbo a la aldea»), algo " +
        "raro de un personaje («Steel no quiere bañarse»), un guiño a una canción o grupo, una fecha («Día 7»), «, parte N» en los " +
        "de varias partes. El primero lleva el nombre de la serie o del protagonista; el final, una despedida o «la vida sigue».\n" +
        "Tipos de capítulo:\n" +
        "- normal: un video con la plantilla de la serie.\n" +
        "- especial: rompe la fórmula (otra estructura, otro foco).\n" +
        "- dos_partes: DOS videos (como los enemigos de dos episodios de SC): la parte 1 termina en el peor momento; la parte 2 abre " +
        "con el recap de ese cliffhanger.\n" +
        "- doble_duracion: UN solo video del doble de largo (como el primer episodio de SBR, de 47 min): un solo opening, las mitades " +
        "unidas por un eyecatch, sin segundo opening ni segundo ending.\n";

    // Transcripcion del material con los tiempos actuales (de la copia base).
    public static string Material(Transcripcion t, double limite)
    {
        StringBuilder sb = new StringBuilder();
        if (t == null) return "(sin transcripción)\n";
        List<Rango> zonas = ZonasSensibles(t);
        foreach (Segmento s in t.SegmentosActuales())
        {
            if (String.IsNullOrEmpty(s.Texto)) continue;
            string quien = s.Hablante >= 0 && s.Hablante < t.Hablantes.Count ? t.Hablantes[s.Hablante].Nombre : "?";
            string sen = Sensible(s.Texto);
            if (sen.Length == 0)
                foreach (Rango z in zonas) if (s.Inicio >= z.Inicio && s.Fin <= z.Fin) { sen = "técnico/personal?"; break; }
            sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + quien + ": " + s.Texto + (sen.Length > 0 ? " ⟨" + sen + "⟩" : "") + "\n");
            if (sb.Length > limite) { sb.Append("…\n"); break; }
        }
        return sb.ToString();
    }

    // ----------------------------------------- charla tecnica o personal

    // Principios de palabra (con espacio al final: palabra completa).
    static readonly string[] Tecnico = { "instal", "desinstal", "descarg", "launcher", "tlauncher", "curseforge", "modrinth", "java",
        "ram ", "gigas de ram", "forge ", "fabric ", "actualiz", "crashe", "crash", "lag ", "lagea", "ping ", "fps ", "discord",
        "microfono", "mic ", "me escuchan", "me escuchas", "no te escucho", "no se escucha", "se escucha bien", "se trabo",
        "reinicia", "la ip ", "obs ", "estoy grabando", "estas grabando", "grabacion", "la version", "drivers", "la compu se",
        "anydesk", "anidesk", "anidesc", "teamviewer", "prism", "lanzador", "te envio un link", "te paso el link", "minecraft premium" };
    static readonly string[] Personal = { "mi mama ", "mi papa ", "mi novia ", "mi novio ", "mi esposa ", "mi esposo ",
        "mi hermana ", "mi hermano ", "mi jefe ", "el trabajo ", "mi trabajo ", "la escuela ", "la universidad ", "la uni ", "examen",
        "doctor", "hospital", "enferm", "la renta ", "el sueldo ", "whatsapp", "mi numero ", "mi telefono ", "mi direccion ",
        "en la vida real", "irl " };

    // "técnico", "personal" o "" segun lo que parece la frase.
    public static string Sensible(string texto)
    {
        string t = " " + System.Text.RegularExpressions.Regex.Replace(Plano(texto), @"[^\p{L}\p{N}]+", " ").Trim() + " ";
        foreach (string k in Tecnico) if (t.Contains(" " + k)) return "técnico";
        foreach (string k in Personal) if (t.Contains(" " + k)) return "personal";
        return "";
    }

    // Tramos donde se juntan frases tecnicas o personales (3 o mas, a menos de
    // 30 s una de otra): las frases de en medio seguramente tambien lo son.
    public static List<Rango> ZonasSensibles(Transcripcion t)
    {
        List<Rango> r = new List<Rango>();
        if (t == null) return r;
        double ini = -1, fin = -1;
        int n = 0;
        foreach (Segmento s in t.SegmentosActuales())
        {
            if (String.IsNullOrEmpty(s.Texto) || Sensible(s.Texto).Length == 0) continue;
            if (n > 0 && s.Inicio - fin <= 30) { fin = s.Fin; n++; continue; }
            if (n >= 3) r.Add(new Rango(ini, fin));
            ini = s.Inicio; fin = s.Fin; n = 1;
        }
        if (n >= 3) r.Add(new Rango(ini, fin));
        return r;
    }

    // Lo sensible que se dice dentro de un tramo del material ("" si nada).
    public static string SensibleEn(Transcripcion t, double a, double b)
    {
        if (t == null) return "";
        foreach (Segmento s in t.SegmentosActuales())
        {
            if (s.Fin <= a || s.Inicio >= b || String.IsNullOrEmpty(s.Texto)) continue;
            string x = Sensible(s.Texto);
            if (x.Length > 0) return x;
        }
        return "";
    }

    public static string Pausas(Transcripcion t, double duracion)
    {
        StringBuilder sb = new StringBuilder();
        foreach (Rango p in LogicaPlan.Pausas(t, duracion, 2.0)) sb.Append("[" + S(p.Inicio) + "-" + S(p.Fin) + "] ");
        return sb.ToString();
    }

    public static string Reparto(MusicaSerie m)
    {
        if (m == null || String.IsNullOrEmpty(m.Reparto)) return "";
        return "REPARTO:\n" + m.Reparto.Trim() + "\n";
    }

    static string Plantilla(FormatoSerie f)
    {
        StringBuilder sb = new StringBuilder();
        foreach (BloqueTV b in f.Tv.Bloques)
            sb.Append("- " + b.Clave + " (" + b.Nombre + ", " + (b.Segundos > 0 ? b.Segundos + " s" : b.Porcentaje + " % de lo que queda") +
                      (b.Tipo == "kit" ? ", archivo fijo de la serie" : b.Tipo == "texto" ? ", texto en pantalla" : ", con clips del material") +
                      "): " + b.Descripcion + "\n");
        return sb.ToString();
    }

    // ------------------------------------------------- 1. analisis + propuestas

    static string FormatoPropuestas =
        " \"propuestas\": [{\"id\": \"A\", \"nombre\": \"...\", \"tipo\": \"normal|especial|dos_partes|doble_duracion\", " +
        "\"tipo_capitulo\": \"clave\", " +
        "\"titulos\": [\"...\"], \"minutos\": n, \"estructura\": [\"Cold open (2:00)\", \"Opening\", \"Título\", \"Acto A\", \"...\"], " +
        "\"cold_open\": \"...\", \"cierre\": \"...\", \"escaleta\": [\"[mm:ss] ...\"], \"por_que\": \"...\"}]";

    public static string InstruccionesAnalisis(FormatoSerie f, string tipo, string papel)
    {
        return InstruccionesAnalisis(f, tipo, papel, "");
    }

    // tipoPedido: clave del tipo de capitulo que pidio el editor ("" = que lo detecte).
    public static string InstruccionesAnalisis(FormatoSerie f, string tipo, string papel, string tipoPedido)
    {
        ReglasRitmo r = PapelEpisodio.Reglas(f.Reglas, papel);
        TipoCapitulo pedido = TiposCapitulo.Buscar(tipoPedido);
        return "Eres el director de " + Serie.QueEs(tipo) + " que se edita como una serie de TV estilo anime de JoJo's Bizarre " +
               "Adventure. Recibes TODO el material grabado de un capítulo (ya sin silencios) y el contexto de la serie. Analízalo y " +
               "propón TRES formas distintas de hacer el capítulo.\n\n" + Prioridad + Variantes + "\n" +
               "FORMATO: " + f.Nombre + ". " + FormatoSerie.Objetivo(f.Nombre) + "\n" +
               "PAPEL DEL CAPÍTULO: " + papel + ". " + PapelEpisodio.Instrucciones(papel) + "\n" +
               "DURACIÓN DE REFERENCIA: " + r.DuracionMin + "–" + r.DuracionMax + " min por video (el doble si es de doble duración).\n\n" +
               "PLANTILLA DE LA SERIE (punto de partida, se puede cambiar):\n" + Plantilla(f) + "\n" + Guia + "\n" +
               TiposCapitulo.Catalogo() + "\n" +
               "QUÉ CAPÍTULO ES: decide qué tipo de capítulo es este según (1) lo que pide el editor (indicaciones y nota del " +
               "capítulo), (2) lo que hay en el material, (3) el NÚMERO de capítulo y su papel y (4) cómo terminó el capítulo " +
               "ANTERIOR (si quedó a medias, este lo retoma) y qué tipos se usaron hace poco (varía). Si el papel elegido no encaja " +
               "con el material (por ejemplo, alguien muere y está como «Normal»), sugiere otro papel de: " +
               String.Join(", ", PapelEpisodio.Papeles) + ".\n" +
               (pedido != null ? "EL EDITOR PIDE QUE SEA: " + pedido.Clave + " (" + pedido.Nombre + "). Las tres propuestas son de ese " +
                                 "tipo, con variantes distintas (otro inicio, otro foco, otro cierre).\n"
                               : "Las tres propuestas pueden ser de tipos distintos si el material da para eso (por ejemplo, el mismo " +
                                 "material como juego, como el capítulo de un personaje o como misterio).\n") + "\n" +
               "Análisis:\n- \"momentos\": los mejores momentos (de 15 a 40 según lo largo del material, al menos uno cada ~4 min) con su tipo (" + String.Join(", ", TiposMomento) + "), fuerza 1–5 y quién " +
               "participa. Usa solo tiempos de la transcripción.\n- \"hilos\": qué viene de capítulos anteriores o prepara algo de los " +
               "posteriores.\n- \"minutos_utiles\": cuánto material vale la pena.\n- \"doble\": si da para más de un video normal, dilo " +
               "y si conviene dos partes o doble duración.\n\n" +
               "Propuestas: SIEMPRE TRES, distintas de verdad, aunque el editor fije el tipo, la duración o la estructura (entonces " +
               "son variantes: otro inicio, otro foco, otro orden, otro cierre). \"minutos\" es lo que dura el VIDEO TERMINADO, no " +
               "el material. Cada una con su tipo, su tipo_capitulo (clave), títulos al estilo JoJo (el nombre del rival, del lugar o de " +
               "la situación; en dos partes, «… Parte 1» y «… Parte 2»), duración, su ESTRUCTURA de bloques en orden (con la duración " +
               "de la intro o cold open; puede no tener opening, tener eyecatch entre mitades, etc.), qué cold open y qué cierre usa, una " +
               "escaleta corta (5 a 8 pasos con tiempos del material) y por qué funciona.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"...\", \"minutos_utiles\": n,\n" +
               " \"momentos\": [{\"inicio\": s, \"fin\": s, \"tipo\": \"...\", \"texto\": \"...\", \"fuerza\": n, \"personajes\": [\"...\"]}],\n" +
               " \"hilos\": [\"...\"],\n" +
               " \"doble\": {\"recomendado\": true|false, \"motivo\": \"...\"},\n" +
               " \"tipo_capitulo\": {\"sugerido\": \"clave\", \"alternativas\": [\"clave\"], \"motivo\": \"...\"},\n" +
               " \"papel\": {\"sugerido\": \"" + papel + "\", \"motivo\": \"...\"},\n" + FormatoPropuestas + "}";
    }

    public static string MensajeAnalisis(Transcripcion t, double duracion, string contextoSerie, string reparto, string indicaciones)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Material: " + Formato.Tiempo(duracion) + " (" + S(duracion) + " s)\n");
        if (!String.IsNullOrEmpty(indicaciones)) sb.Append("\nINDICACIONES DEL EDITOR (mandan):\n" + indicaciones.Trim() + "\n");
        if (!String.IsNullOrEmpty(contextoSerie)) sb.Append("\nCONTEXTO DE LA SERIE:\n" + contextoSerie.Trim() + "\n");
        if (!String.IsNullOrEmpty(reparto)) sb.Append("\n" + reparto);
        sb.Append("\nTRANSCRIPCIÓN DEL MATERIAL [inicio-fin] persona: texto\n" + Material(t, 400000));
        return sb.ToString();
    }

    static Propuesta LeerPropuesta(object x)
    {
        Propuesta p = new Propuesta();
        p.Id = Json.Texto(x, "id");
        p.Nombre = Json.Texto(x, "nombre"); p.Tipo = Propuesta.Normalizar(Json.Texto(x, "tipo"));
        p.TipoCap = TiposCapitulo.Normalizar(Json.Texto(x, "tipo_capitulo"));
        foreach (object y in Json.Lista(x, "titulos")) if (y is string) p.Titulos.Add((string)y);
        foreach (object y in Json.Lista(x, "estructura")) if (y is string) p.Estructura.Add((string)y);
        p.Minutos = Json.Numero(x, "minutos", 0);
        p.ColdOpen = Json.Texto(x, "cold_open"); p.Cierre = Json.Texto(x, "cierre"); p.PorQue = Json.Texto(x, "por_que");
        foreach (object y in Json.Lista(x, "escaleta")) if (y is string) p.Escaleta.Add((string)y);
        return p;
    }

    static List<Propuesta> LeerPropuestas(object o)
    {
        List<Propuesta> r = new List<Propuesta>();
        string[] ids = { "A", "B", "C", "D" };
        foreach (object x in Json.Lista(o, "propuestas"))
        {
            Propuesta p = LeerPropuesta(x);
            if (p.Nombre.Length == 0) continue;
            if (p.Id.Length == 0 || r.Exists(delegate (Propuesta q) { return q.Id == p.Id; })) p.Id = ids[Math.Min(3, r.Count)];
            r.Add(p);
            if (r.Count == 3) break;
        }
        return r;
    }

    public static AnalisisCapitulo LeerAnalisis(string json, double duracion)
    {
        AnalisisCapitulo a = new AnalisisCapitulo();
        a.Respuesta = json;
        object o = Json.Leer(Gemini.QuitarCercas(json));
        a.Resumen = Json.Texto(o, "resumen");
        a.MinutosUtiles = Json.Numero(o, "minutos_utiles", 0);
        if (duracion > 0 && a.MinutosUtiles > duracion / 60 + 1) a.MinutosUtiles /= 60;   // lo dio en segundos
        object d = Json.Valor(o, "doble");
        if (d != null)
        {
            object rec = Json.Valor(d, "recomendado");
            a.DobleRecomendado = rec is bool && (bool)rec;
            a.DobleMotivo = Json.Texto(d, "motivo");
        }
        foreach (object x in Json.Lista(o, "momentos"))
        {
            MomentoMaterial m = new MomentoMaterial();
            m.Inicio = Json.Numero(x, "inicio", -1); m.Fin = Json.Numero(x, "fin", -1);
            m.Tipo = Json.Texto(x, "tipo"); m.Texto = Json.Texto(x, "texto"); m.Fuerza = (int)Json.Numero(x, "fuerza", 3);
            foreach (object p in Json.Lista(x, "personajes")) if (p is string) m.Personajes.Add((string)p);
            if (m.Inicio >= 0 && m.Fin > m.Inicio && m.Inicio < duracion) { m.Fin = Math.Min(m.Fin, duracion); a.Momentos.Add(m); }
        }
        a.Momentos.Sort(delegate (MomentoMaterial x, MomentoMaterial y) { return x.Inicio.CompareTo(y.Inicio); });
        foreach (object x in Json.Lista(o, "hilos")) if (x is string) a.Hilos.Add((string)x);
        object tc = Json.Valor(o, "tipo_capitulo");
        if (tc != null)
        {
            a.TipoSugerido = TiposCapitulo.Normalizar(Json.Texto(tc, "sugerido"));
            a.TipoMotivo = Json.Texto(tc, "motivo");
            foreach (object x in Json.Lista(tc, "alternativas"))
            {
                string k = TiposCapitulo.Normalizar(x as string);
                if (k.Length > 0 && k != a.TipoSugerido && !a.TiposAlternativos.Contains(k)) a.TiposAlternativos.Add(k);
            }
        }
        object pa = Json.Valor(o, "papel");
        if (pa != null && Array.IndexOf(PapelEpisodio.Papeles, Json.Texto(pa, "sugerido")) >= 0)
        {
            a.PapelSugerido = Json.Texto(pa, "sugerido");
            a.PapelMotivo = Json.Texto(pa, "motivo");
        }
        a.Propuestas = LeerPropuestas(o);
        if (a.Propuestas.Count == 0) throw new Exception("La respuesta no trae propuestas.");
        return a;
    }

    static string PropuestaTexto(Propuesta p)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append(p.Id + " · " + p.Nombre + " (" + p.Tipo + (p.TipoCap.Length > 0 ? ", tipo_capitulo " + p.TipoCap : "") +
                  ", ~" + Math.Round(p.Minutos) + " min)\n");
        if (p.Titulos.Count > 0) sb.Append("  Títulos: " + String.Join(" / ", p.Titulos.ToArray()) + "\n");
        if (p.Estructura.Count > 0) sb.Append("  Estructura: " + String.Join(" → ", p.Estructura.ToArray()) + "\n");
        sb.Append("  Cold open: " + p.ColdOpen + "\n  Cierre: " + p.Cierre + "\n");
        foreach (string e in p.Escaleta) sb.Append("  - " + e + "\n");
        sb.Append("  Por qué: " + p.PorQue + "\n");
        return sb.ToString();
    }

    // Refinar: las mismas propuestas corregidas con las notas (sin volver a analizar).
    public static string InstruccionesRefinar(FormatoSerie f, string papel)
    {
        return "Eres el director de una serie de YouTube editada como un anime de JoJo. Ya propusiste tres formas de hacer un capítulo " +
               "y el editor dejó NOTAS. Rehaz las propuestas siguiéndolas:\n" +
               "- Una propuesta con notas se corrige según sus notas (manteniendo lo que no se pide cambiar).\n" +
               "- Una propuesta sin notas se deja igual, salvo que las notas generales digan otra cosa o tenga cosas que NO SALEN EN EL " +
               "MATERIAL (esas se corrigen siempre con lo que sí pasa).\n" +
               "- Si una nota pide combinar propuestas («la B con el cold open de la A»), hazlo en esa propuesta.\n" +
               "- Mantén los ids.\n- Si una nota pide otro tipo de capítulo («que sea un juego»), cambia su tipo_capitulo y su " +
               "estructura a ese tipo.\n\n" + Prioridad + Variantes + "\n" + TiposCapitulo.Catalogo() + "\n" +
               "PAPEL DEL CAPÍTULO: " + papel + ". " + PapelEpisodio.Instrucciones(papel) + "\n\n" + Guia + "\n" +
               "PLANTILLA DE LA SERIE (punto de partida):\n" + Plantilla(f) + "\n" +
               "Responde SOLO con JSON:\n{" + FormatoPropuestas + "}";
    }

    public static string MensajeRefinar(AnalisisCapitulo a, string notasGenerales, string indicaciones, Transcripcion t)
    {
        StringBuilder sb = new StringBuilder();
        if (!String.IsNullOrEmpty(indicaciones)) sb.Append("INDICACIONES DEL EDITOR (mandan):\n" + indicaciones.Trim() + "\n\n");
        sb.Append("PROPUESTAS ACTUALES Y SUS NOTAS:\n");
        foreach (Propuesta p in a.Propuestas)
        {
            sb.Append(PropuestaTexto(p) + "  NOTAS DEL EDITOR: " + (p.Notas.Trim().Length > 0 ? p.Notas.Trim() : "(ninguna)") + "\n");
            List<string> raras = NoEnMaterial(p, t, (indicaciones ?? "") + " " + (notasGenerales ?? "") + " " + p.Notas);
            if (raras.Count > 0) sb.Append("  NO SALEN EN EL MATERIAL (corrígelo aunque no tenga notas): " + String.Join(", ", raras.ToArray()) + "\n");
            sb.Append("\n");
        }
        if (!String.IsNullOrEmpty(notasGenerales)) sb.Append("NOTAS GENERALES: " + notasGenerales.Trim() + "\n\n");
        sb.Append("ANÁLISIS: " + a.Resumen + "\nMomentos:\n");
        foreach (MomentoMaterial x in a.Momentos)
            sb.Append("[" + S(x.Inicio) + "-" + S(x.Fin) + "] " + x.Tipo + " (" + x.Fuerza + "): " + x.Texto + "\n");
        sb.Append("\nTRANSCRIPCIÓN DEL MATERIAL [inicio-fin] persona: texto\n" + Material(t, 300000));
        return sb.ToString();
    }

    // ---------------------------------------------- lo que no sale en el material

    static readonly string[] Generales = { "etapa", "parte", "acto", "opening", "ending", "eyecatch", "intro", "introduccion", "titulo",
        "continuara", "avance", "capitulo", "cold", "open", "regancho", "gancho", "cierre", "recap", "epilogo", "prologo", "episodio",
        "jojo", "minecraft", "narrador", "narracion", "estreno", "especial", "doble", "duracion" };

    static string Plano(string w)
    {
        string d = (w ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    static void Vocabulario(Dictionary<string, bool> v, string texto)
    {
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(Plano(texto), @"\p{L}+"))
        {
            string w = m.Value;
            v[w] = true;
            for (int n = 5; n < w.Length; n++) v[w.Substring(0, n)] = true;   // forajido / forajidos
        }
    }

    // Nombres propios de la propuesta (palabras con mayuscula que no abren la
    // frase) que no aparecen en el material ni en lo que dio el editor: lo mas
    // probable es que Gemini los haya inventado o sacado del anime.
    public static List<string> NoEnMaterial(Propuesta p, Transcripcion t, string extra)
    {
        Dictionary<string, bool> v = new Dictionary<string, bool>();
        if (t != null)
        {
            foreach (Segmento x in t.Segmentos) Vocabulario(v, x.Texto);
            foreach (Hablante h in t.Hablantes) Vocabulario(v, h.Nombre);
        }
        Vocabulario(v, extra);
        foreach (string g in Generales) v[g] = true;
        foreach (TipoCapitulo tc in TiposCapitulo.Todos) Vocabulario(v, tc.Nombre);
        List<string> textos = new List<string>(p.Titulos);
        textos.Add(p.Nombre); textos.Add(p.ColdOpen); textos.Add(p.Cierre);
        textos.AddRange(p.Escaleta);
        List<string> r = new List<string>();
        foreach (string texto in textos)
            foreach (string trozo in System.Text.RegularExpressions.Regex.Split(texto ?? "", @"[.:;!?¡¿«»""“”/·\-–—\[\]()]"))
            {
                System.Text.RegularExpressions.MatchCollection ms = System.Text.RegularExpressions.Regex.Matches(trozo, @"\p{L}+");
                for (int i = 1; i < ms.Count; i++)   // la primera palabra del trozo va con mayuscula de todos modos
                {
                    string w = ms[i].Value;
                    if (w.Length < 4 || !char.IsUpper(w[0])) continue;
                    string pl = Plano(w);
                    if (v.ContainsKey(pl) || (pl.Length > 5 && v.ContainsKey(pl.Substring(0, pl.Length - 1)))) continue;
                    if (!r.Contains(w)) r.Add(w);
                }
            }
        return r;
    }

    // Completar: cuando Gemini devolvio menos de tres propuestas.
    public static string InstruccionesCompletar(FormatoSerie f, string papel)
    {
        return "Eres el director de una serie de YouTube editada como un anime de JoJo. Propusiste formas de hacer un capítulo pero " +
               "faltan propuestas: tiene que haber TRES. Devuelve las que ya hay SIN cambios (mismo id) y agrega las que faltan, " +
               "distintas de verdad (otro tipo de capítulo, otro inicio, otro foco, otro cierre), respetando lo que pide el " +
               "editor.\n\n" + Prioridad + Variantes + "\nPAPEL DEL CAPÍTULO: " + papel + ". " + PapelEpisodio.Instrucciones(papel) + "\n\n" +
               TiposCapitulo.Catalogo() + "\n" + Guia + "\nPLANTILLA DE LA SERIE (punto de partida):\n" + Plantilla(f) + "\n" +
               "Responde SOLO con JSON:\n{" + FormatoPropuestas + "}";
    }

    // Pone las propuestas refinadas en el analisis; las notas aplicadas se vacian.
    public static void Refinar(AnalisisCapitulo a, string json)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        List<Propuesta> nuevas = LeerPropuestas(o);
        if (nuevas.Count == 0) throw new Exception("La respuesta no trae propuestas.");
        a.Propuestas = nuevas;
        a.RespuestaPropuestas = json;
    }

    // ------------------------------------------------------ 2. propuesta final

    // Musica que se le ofrece a Gemini: SC y Golden Wind (viaje, desierto,
    // pandilla) y lo demas con datos del anime.
    public static List<ArchivoMusica> MusicaCandidata(BibliotecaMusica b, MusicaSerie m)
    {
        List<ArchivoMusica> r = new List<ArchivoMusica>();
        if (b == null) return r;
        Dictionary<string, bool> vistos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        List<ArchivoMusica> orden = new List<ArchivoMusica>();
        foreach (ArchivoMusica a in b.Archivos) if (a.Parte == "SC" || a.Parte == "GW") orden.Add(a);
        foreach (ArchivoMusica a in b.Archivos) if (a.ConUso && a.Parte != "SC" && a.Parte != "GW") orden.Add(a);
        foreach (ArchivoMusica a in orden)
        {
            if (vistos.ContainsKey(a.Ruta) || a.Momento == "opening" || a.Momento == "ending") continue;
            vistos[a.Ruta] = true;
            foreach (string v in a.Variantes) vistos[v] = true;
            r.Add(a);
            if (r.Count >= 220) break;
        }
        return r;
    }

    public static string InstruccionesFinal(FormatoSerie f, string papel, Propuesta p)
    {
        ReglasRitmo r = PapelEpisodio.Reglas(f.Reglas, papel);
        string tipo = p != null ? p.Tipo : "normal";
        return "Eres el director y editor de una serie de YouTube editada como un anime de JoJo. Con el análisis, la propuesta elegida " +
               "y las notas del editor, arma la ESCALETA FINAL del capítulo (tipo: " + tipo + ").\n\n" + Prioridad +
               "Revisa cada nota y cambio del editor y cumple todos; no metas elementos que una nota pide quitar.\n\n" +
               "PAPEL DEL CAPÍTULO: " + papel + ". " + PapelEpisodio.Instrucciones(papel) + "\n" +
               (p != null ? TiposCapitulo.Instrucciones(p.TipoCap) : "") +
               "PLANTILLA DE LA SERIE (punto de partida; la ESTRUCTURA de cada parte la decides tú según la propuesta y las notas):\n" +
               Plantilla(f) +
               "Bloques posibles: los de la plantilla (cold_open, op, titulo, acto_a, regancho, acto_b, continuara, ed, avance) y otros " +
               "que necesites (intro, acto_c, eyecatch…). Cada bloque tiene \"tipo\": \"contenido\" (clips del material), \"kit\" " +
               "(archivo fijo de la serie: \"kit\" dice cuál: op, regancho, continuara o ed) o \"texto\" (título en pantalla), y " +
               "\"segundos\" para los de kit y texto.\n" +
               "- normal / especial: una parte. dos_partes: dos partes (dos videos). doble_duracion: UNA sola parte larga con un " +
               "solo opening y las mitades unidas por un bloque eyecatch (kit regancho).\n" +
               "- La intro o cold open debe durar lo suficiente para entender de qué va (en un primer capítulo, presentar la premisa " +
               "y a los jugadores con calma antes del opening).\n\n" + Guia + "\n" +
               "REFERENCIA de duración: " + r.DuracionMin + "–" + r.DuracionMax + " min por video (el doble si es doble_duracion).\n\n" +
               "Para cada bloque de contenido da los CLIPS en el orden en que se verán (inicio y fin del material, de 2 a 90 s; corta " +
               "charla sin interés y repeticiones). Un momento puede usarse fuera de orden (cold open, avance).\n" +
               "RITMO: el material ya no tiene silencios, así que sin cuidado todo queda acelerado. El ritmo debe cambiar a lo largo del " +
               "capítulo según lo que pasa: rápido en acción, persecuciones y humor encadenado; medio en exploración y charla; lento en " +
               "llegadas, revelaciones, momentos emotivos, el cliffhanger y justo después de un chiste fuerte o un golpe. Cada bloque " +
               "lleva \"ritmo\": lento|medio|rapido. En los clips que necesitan aire pon \"respiro\": 0.5 a 3 s (se recupera la pausa " +
               "original de la grabación al final del clip). Ni todo rápido ni todo lento.\n" +
               "NO REPITAS MATERIAL: cada tramo se usa UNA sola vez en cada parte (lo que va en el cold open no vuelve a salir en " +
               "los actos ni al final). El recap y el avance van sin clips de este material (placeholder).\n" +
               "TEXTOS en pantalla: \"presentacion\" (nombre y un rasgo del personaje cuando aparece por primera vez), \"titulo\" (\"" + (f.Avance != "Ninguno" ? f.Marca(1) + " · " : "") + "nombre del capítulo\"), " +
               "\"lugar\" o \"tiempo\" («6 horas más tarde»), \"ranking\", \"stats\" (tarjeta del rival: nombre y 4–6 atributos con " +
               "letra A–E, para el re-gancho o eyecatch) y \"continuara\". Cada uno con \"en\": segundo del material, o \"bloque\" si va " +
               "en un bloque de kit o texto.\n" +
               "MÚSICA: un tema por escena, cambiando cada ~" + r.MusicaCadaSeg + " s o cuando cambia el ánimo, con \"id\" de la " +
               "biblioteca y \"en\": segundo del material. Tema de un personaje cuando se luce (\"personaje\": nombre) y el principal en " +
               "el momento clave. Sin repetir tema dentro de la misma parte.\n" +
               (f.Narrador ? "NARRACIÓN: " + (f.EstiloNarrador.Length > 0 ? f.EstiloNarrador : "primera persona, en pasado") + ". Frases " +
                             "de 4 a 30 palabras a " + r.PPM + " palabras por minuto (N palabras duran N×60/" + r.PPM + " s). SOLO dentro de " +
                             "las PAUSAS listadas (donde nadie habla) y que quepan enteras: NUNCA encima de las voces de los jugadores. " +
                             "\"en\": segundo del material donde empieza.\n" : "") +
               "RECURSOS: imágenes, memes o efectos que faltan (\"clase\", \"descripcion\", \"duracion\" 1–5 s, \"en\").\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"...\", \"partes\": [{\"titulo\": \"...\", \"etapa\": \"...\",\n" +
               "  \"estructura\": [{\"bloque\": \"cold_open\", \"nombre\": \"Cold open\", \"tipo\": \"contenido\", \"ritmo\": \"lento\"}, " +
               "{\"bloque\": \"op\", \"nombre\": \"Opening\", \"tipo\": \"kit\", \"kit\": \"op\", \"segundos\": 20}, ...],\n" +
               "  \"bloques\": [{\"bloque\": \"cold_open\", \"clips\": [{\"inicio\": s, \"fin\": s, \"respiro\": s, \"nota\": \"...\"}]}],\n" +
               "  \"textos\": [{\"tipo\": \"titulo|presentacion|lugar|tiempo|ranking|stats|continuara\", \"texto\": \"...\", \"en\": s, \"bloque\": \"...\"}],\n" +
               "  \"musica\": [{\"id\": n, \"en\": s, \"personaje\": \"...\", \"motivo\": \"...\"}],\n" +
               "  \"narracion\": [{\"en\": s, \"texto\": \"...\"}],\n" +
               "  \"recursos\": [{\"en\": s, \"duracion\": s, \"clase\": \"...\", \"descripcion\": \"...\"}]}]}";
    }

    public static string MensajeFinal(AnalisisCapitulo a, Propuesta elegida, string notasGenerales, List<ArchivoMusica> musica, MusicaSerie m,
                                      string reparto, Transcripcion t, PlanFinal anterior, string cambios)
    {
        return MensajeFinal(a, elegida, notasGenerales, "", musica, m, reparto, t, 0, anterior, cambios);
    }

    public static string MensajeFinal(AnalisisCapitulo a, Propuesta elegida, string notasGenerales, string indicaciones, List<ArchivoMusica> musica,
                                      MusicaSerie m, string reparto, Transcripcion t, double duracion, PlanFinal anterior, string cambios)
    {
        StringBuilder sb = new StringBuilder();
        // Lo que pide el editor va primero.
        StringBuilder notas = new StringBuilder();
        if (!String.IsNullOrEmpty(indicaciones)) notas.Append("- " + indicaciones.Trim() + "\n");
        foreach (Propuesta p in a.Propuestas)
            if (p.Notas.Trim().Length > 0) notas.Append("- Sobre " + p.Id + " (" + p.Nombre + "): " + p.Notas.Trim() + "\n");
        if (!String.IsNullOrEmpty(notasGenerales) && notasGenerales.Trim().Length > 0) notas.Append("- " + notasGenerales.Trim() + "\n");
        if (anterior != null && !String.IsNullOrEmpty(cambios)) notas.Append("- CAMBIOS PEDIDOS AHORA: " + cambios.Trim() + "\n");
        if (notas.Length > 0) sb.Append("LO QUE PIDE EL EDITOR (cumplir todo, manda sobre todo lo demás):\n" + notas + "\n");

        sb.Append("PROPUESTA ELEGIDA:\n" + PropuestaTexto(elegida));
        List<string> raras = NoEnMaterial(elegida, t, (indicaciones ?? "") + " " + (notasGenerales ?? "") + " " + elegida.Notas + " " + reparto);
        if (raras.Count > 0) sb.Append("  NO SALEN EN EL MATERIAL (no los uses; cámbialos por lo que sí pasa): " + String.Join(", ", raras.ToArray()) + "\n");
        sb.Append("\n");
        sb.Append("ANÁLISIS: " + a.Resumen + "\n");
        if (a.Hilos.Count > 0) sb.Append("Hilos: " + String.Join("; ", a.Hilos.ToArray()) + "\n");
        sb.Append("Momentos:\n");
        foreach (MomentoMaterial x in a.Momentos)
            sb.Append("[" + S(x.Inicio) + "-" + S(x.Fin) + "] " + x.Tipo + " (" + x.Fuerza + "): " + x.Texto + "\n");
        if (!String.IsNullOrEmpty(reparto)) sb.Append("\n" + reparto);
        if (m != null)
        {
            if (m.Principal != null) sb.Append("Tema principal de la serie: " + Path.GetFileNameWithoutExtension(m.Principal.Archivo) + "\n");
            foreach (KeyValuePair<string, TemaAsignado> kv in m.Personajes)
                sb.Append("Tema de " + kv.Key + ": " + Path.GetFileNameWithoutExtension(kv.Value.Archivo) + "\n");
        }
        if (musica.Count > 0)
        {
            sb.Append("\nBIBLIOTECA DE MÚSICA [id] título (parte) | ánimo | dónde suena en el anime\n");
            for (int i = 0; i < musica.Count; i++)
                sb.Append("[" + i + "] " + musica[i].Titulo + " (" + (musica[i].Parte.Length > 0 ? musica[i].Parte : musica[i].Fuente) + ") | " +
                          String.Join(", ", musica[i].Animos.ToArray()) + (musica[i].Momento.Length > 0 ? " | " + musica[i].Momento : "") + "\n");
        }
        if (anterior != null && !String.IsNullOrEmpty(cambios))
            sb.Append("\nESCALETA ANTERIOR (JSON): rehazla aplicando los CAMBIOS PEDIDOS AHORA y manteniendo lo demás.\n" +
                      Gemini.QuitarCercas(anterior.Respuesta) + "\n");
        if (duracion > 0) sb.Append("\nPAUSAS DEL MATERIAL (nadie habla) [inicio-fin]:\n" + Pausas(t, duracion) + "\n");
        sb.Append("\nTRANSCRIPCIÓN DEL MATERIAL [inicio-fin] persona: texto\n" + Material(t, 300000));
        return sb.ToString();
    }

    static BloqueTV LeerBloque(object x, PlantillaTV tv)
    {
        string clave = Json.Texto(x, "bloque");
        if (clave.Length == 0) return null;
        BloqueTV base_ = tv.Bloque(clave);
        BloqueTV b = base_ != null ? base_.Copia() : new BloqueTV(clave, clave, "contenido", 0, 0, "");
        string nombre = Json.Texto(x, "nombre"), tipo = Json.Texto(x, "tipo"), kit = Json.Texto(x, "kit");
        string ritmo = Json.Texto(x, "ritmo");
        if (ritmo == "lento" || ritmo == "medio" || ritmo == "rapido" || ritmo == "rápido") b.Ritmo = ritmo.Replace("á", "a");
        if (nombre.Length > 0) b.Nombre = nombre;
        if (tipo == "contenido" || tipo == "kit" || tipo == "texto") b.Tipo = tipo;
        if (kit.Length > 0) b.Kit = kit;
        if (b.Tipo == "kit" && b.Kit.Length == 0 && tv.Bloque(clave) == null) b.Kit = clave.Contains("eye") ? "regancho" : clave;
        double seg = Json.Numero(x, "segundos", -1);
        if (b.Tipo != "contenido") b.Segundos = seg > 0 ? seg : (b.Segundos > 0 ? b.Segundos : (b.Tipo == "texto" ? 3 : 6));
        else b.Segundos = 0;
        return b;
    }

    public static PlanFinal LeerFinal(string json, double duracion, int musicas, int ppm)
    {
        return LeerFinal(json, duracion, musicas, ppm, PlantillaTV.PorDefecto());
    }

    public static PlanFinal LeerFinal(string json, double duracion, int musicas, int ppm, PlantillaTV tv)
    {
        PlanFinal p = new PlanFinal();
        p.Respuesta = json;
        object o = Json.Leer(Gemini.QuitarCercas(json));
        p.Resumen = Json.Texto(o, "resumen");
        int nn = 0, nr = 0;
        foreach (object x in Json.Lista(o, "partes"))
        {
            CapituloFinal c = new CapituloFinal();
            int parte = p.Partes.Count;
            c.Titulo = Json.Texto(x, "titulo"); c.Etapa = Json.Texto(x, "etapa");
            foreach (object b in Json.Lista(x, "estructura"))
            {
                BloqueTV bl = LeerBloque(b, tv);
                if (bl != null && c.Bloque(bl.Clave) == null) c.Estructura.Add(bl);
            }
            if (c.Estructura.Count == 0) foreach (BloqueTV b in tv.Bloques) c.Estructura.Add(b.Copia());
            foreach (object b in Json.Lista(x, "bloques"))
            {
                string bloque = Json.Texto(b, "bloque");
                // Clips de un bloque que no esta en la estructura: se agrega antes del cierre.
                if (bloque.Length > 0 && c.Bloque(bloque) == null)
                {
                    BloqueTV nb = new BloqueTV(bloque, bloque, "contenido", 0, 0, "");
                    int i = c.Estructura.FindIndex(delegate (BloqueTV q) { return q.Clave == "continuara" || q.Clave == "ed"; });
                    if (i < 0) c.Estructura.Add(nb); else c.Estructura.Insert(i, nb);
                }
                foreach (object y in Json.Lista(b, "clips"))
                {
                    ItemFinal i = new ItemFinal();
                    i.Parte = parte; i.Bloque = bloque; i.Tipo = "clip";
                    i.Inicio = Math.Max(0, Json.Numero(y, "inicio", -1)); i.Fin = Math.Min(duracion, Json.Numero(y, "fin", -1));
                    i.Texto = Json.Texto(y, "nota");
                    i.Respiro = Math.Max(0, Math.Min(3, Json.Numero(y, "respiro", 0)));
                    if (i.Fin - i.Inicio >= 0.5) c.Items.Add(i);
                }
            }
            foreach (object y in Json.Lista(x, "textos"))
            {
                ItemFinal i = new ItemFinal();
                i.Parte = parte; i.Tipo = "texto"; i.Clase = Json.Texto(y, "tipo"); i.Texto = Json.Texto(y, "texto").Trim();
                i.Inicio = Json.Numero(y, "en", -1); i.Bloque = Json.Texto(y, "bloque");
                if (i.Texto.Length > 0) { i.Fin = i.Inicio + 3; c.Items.Add(i); }
            }
            foreach (object y in Json.Lista(x, "musica"))
            {
                ItemFinal i = new ItemFinal();
                i.Parte = parte; i.Tipo = "musica"; i.Musica = (int)Json.Numero(y, "id", -1);
                i.Inicio = Json.Numero(y, "en", -1); i.Bloque = Json.Texto(y, "bloque");
                i.Personaje = Json.Texto(y, "personaje"); i.Texto = Json.Texto(y, "motivo");
                if ((i.Musica >= 0 && i.Musica < musicas) || i.Personaje.Length > 0) c.Items.Add(i);
            }
            foreach (object y in Json.Lista(x, "narracion"))
            {
                ItemFinal i = new ItemFinal();
                i.Parte = parte; i.Tipo = "narracion"; i.Texto = Json.Texto(y, "texto").Trim();
                i.Inicio = Json.Numero(y, "en", -1);
                if (i.Texto.Length == 0 || i.Inicio < 0) continue;
                i.Fin = i.Inicio + LogicaPlan.Segundos(i.Texto, ppm);
                i.Id = "N" + (++nn).ToString("00");
                c.Items.Add(i);
            }
            foreach (object y in Json.Lista(x, "recursos"))
            {
                ItemFinal i = new ItemFinal();
                i.Parte = parte; i.Tipo = "recurso"; i.Clase = Json.Texto(y, "clase"); i.Texto = Json.Texto(y, "descripcion").Trim();
                i.Inicio = Json.Numero(y, "en", -1);
                i.Fin = i.Inicio + Math.Max(1, Math.Min(6, Json.Numero(y, "duracion", 3)));
                if (i.Texto.Length == 0 || i.Inicio < 0) continue;
                i.Id = "R" + (++nr).ToString("00");
                c.Items.Add(i);
            }
            p.Partes.Add(c);
            if (p.Partes.Count == 2) break;
        }
        if (p.Partes.Count == 0) throw new Exception("La respuesta no trae la escaleta.");
        for (int k = 0; k < p.Partes.Count; k++) p.Repetido += QuitarRepetidos(p.Partes[k], k > 0, k < p.Partes.Count - 1);
        return p;
    }

    // Bloques que no son de este capitulo: el recap (del anterior) y el avance (del proximo).
    public static bool EsDeOtroCapitulo(string bloque)
    {
        string b = (bloque ?? "").ToLowerInvariant();
        return Avance(b) || b.Contains("recap") || b.Contains("anteriormente") || b.Contains("resumen");
    }

    static bool Avance(string bloque)
    {
        string b = (bloque ?? "").ToLowerInvariant();
        return b.Contains("avance") || b.Contains("preview") || b.Contains("proximo") || b.Contains("próximo");
    }

    // Cada tramo del material se ve una sola vez por parte: en el orden de la
    // estructura, lo que ya salio se recorta de los clips siguientes (tambien
    // en el recap y el avance, que son de otros capitulos: si se quedan sin
    // clips pasan a placeholder). Devuelve los segundos quitados.
    public static double QuitarRepetidos(CapituloFinal c) { return QuitarRepetidos(c, false, false); }

    // recapPropio: el recap puede usar este material (parte 2 de dos, recap de la parte 1).
    // avancePropio: el avance puede usar este material (parte 1 de dos, avance de la parte 2).
    public static double QuitarRepetidos(CapituloFinal c, bool recapPropio, bool avancePropio)
    {
        List<Rango> usados = new List<Rango>();
        List<ItemFinal> clips = new List<ItemFinal>();
        double quitado = 0;
        List<string> orden = new List<string>();
        foreach (BloqueTV b in c.Estructura) orden.Add(b.Clave);
        foreach (ItemFinal i in c.Items) if (i.Tipo == "clip" && !orden.Contains(i.Bloque)) orden.Add(i.Bloque);
        foreach (string bloque in orden)
            foreach (ItemFinal i in c.Items)
            {
                if (i.Tipo != "clip" || i.Bloque != bloque) continue;
                if (EsDeOtroCapitulo(bloque) && !(Avance(bloque) ? avancePropio : recapPropio)) { quitado += i.Duracion; continue; }
                Rango original = new Rango(i.Inicio, i.Fin);
                double respiro = i.Respiro;
                List<Rango> partes = new List<Rango>();
                partes.Add(original);
                foreach (Rango u in usados)
                {
                    List<Rango> sig = new List<Rango>();
                    foreach (Rango x in partes)
                    {
                        if (u.Fin <= x.Inicio || u.Inicio >= x.Fin) { sig.Add(x); continue; }
                        if (u.Inicio > x.Inicio) sig.Add(new Rango(x.Inicio, u.Inicio));
                        if (u.Fin < x.Fin) sig.Add(new Rango(u.Fin, x.Fin));
                    }
                    partes = sig;
                }
                double queda = 0;
                List<Rango> validas = partes.FindAll(delegate (Rango x) { return x.Fin - x.Inicio >= 2; });
                foreach (Rango x in validas) queda += x.Fin - x.Inicio;
                quitado += i.Duracion - queda;
                for (int k = 0; k < validas.Count; k++)
                {
                    ItemFinal n = k == 0 ? i : new ItemFinal { Parte = i.Parte, Bloque = i.Bloque, Tipo = "clip", Texto = i.Texto, Elegido = i.Elegido };
                    n.Inicio = validas[k].Inicio; n.Fin = validas[k].Fin;
                    n.Respiro = k == validas.Count - 1 && Math.Abs(validas[k].Fin - original.Fin) < 0.01 ? respiro : 0;
                    clips.Add(n);
                }
                usados.Add(original);
            }
        List<ItemFinal> resto = c.Items.FindAll(delegate (ItemFinal i) { return i.Tipo != "clip"; });
        c.Items = clips;
        c.Items.AddRange(resto);
        // El recap (del anterior) y el avance (del proximo) quedan como placeholder si se quedaron sin clips.
        foreach (BloqueTV b in c.Estructura)
        {
            if (b.Tipo != "contenido" || !EsDeOtroCapitulo(b.Clave)) continue;
            string clave = b.Clave;
            if (c.Items.Exists(delegate (ItemFinal i) { return i.Tipo == "clip" && i.Bloque == clave; })) continue;
            b.Tipo = "kit";
            if (b.Segundos <= 0) b.Segundos = Avance(clave) ? 12 : 20;
        }
        return quitado;
    }

    // Duracion estimada de una parte: clips elegidos + bloques fijos de su estructura.
    public static double Duracion(CapituloFinal c, PlantillaTV tv)
    {
        double d = 0;
        foreach (ItemFinal i in c.Items) if (i.Tipo == "clip" && i.Elegido) d += i.Duracion + i.Respiro;
        foreach (BloqueTV b in c.Estructura) if (b.Tipo != "contenido") d += b.Segundos;
        return d;
    }

    // Clips de un bloque, en orden.
    public static double Bloque(CapituloFinal c, string bloque)
    {
        double d = 0;
        foreach (ItemFinal i in c.Items) if (i.Tipo == "clip" && i.Elegido && i.Bloque == bloque) d += i.Duracion + i.Respiro;
        return d;
    }

    // ------------------------------------------------------ guardar y cargar

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-produccion.json");
    }

    static string Clave(ItemFinal i) { return i.Parte + "|" + i.Tipo + "|" + S(i.Inicio) + "|" + i.Texto; }

    public static void Guardar(string veg, AnalisisCapitulo a, string elegida, string indicaciones, string notas, PlanFinal f)
    {
        Guardar(veg, a, elegida, indicaciones, notas, f, "");
    }

    public static void Guardar(string veg, AnalisisCapitulo a, string elegida, string indicaciones, string notas, PlanFinal f, string tipoPedido)
    {
        Guardar(veg, a, elegida, indicaciones, notas, f, tipoPedido, null);
    }

    public static void Guardar(string veg, AnalisisCapitulo a, string elegida, string indicaciones, string notas, PlanFinal f, string tipoPedido,
                               OpcionesCapitulo opciones)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-produccion";
        d["tipo_pedido"] = tipoPedido ?? "";
        if (opciones != null) d["opciones"] = opciones.Escribir();
        d["indicaciones"] = indicaciones ?? "";
        d["notas"] = notas ?? "";
        if (a != null)
        {
            d["analisis"] = a.Respuesta;
            if (a.RespuestaPropuestas.Length > 0) d["propuestas"] = a.RespuestaPropuestas;
            Dictionary<string, object> np = new Dictionary<string, object>();
            foreach (Propuesta p in a.Propuestas) np[p.Id] = p.Notas;
            d["notas_propuestas"] = np;
        }
        d["elegida"] = elegida ?? "";
        if (f != null)
        {
            d["final"] = f.Respuesta;
            List<object> fuera = new List<object>();
            foreach (ItemFinal i in f.Todos()) if (!i.Elegido) fuera.Add(Clave(i));
            d["descartados"] = fuera;
        }
        File.WriteAllText(RutaPara(veg), Json.Escribir(d), new UTF8Encoding(false));
    }

    // El tipo de capitulo que pidio el editor ("" = que lo detecte).
    public static string TipoPedido(string veg)
    {
        try { return File.Exists(RutaPara(veg)) ? TiposCapitulo.Normalizar(Json.Texto(Json.Leer(File.ReadAllText(RutaPara(veg), Encoding.UTF8)), "tipo_pedido")) : ""; }
        catch { return ""; }
    }

    // Las casillas guardadas (null si no hay).
    public static OpcionesCapitulo Opciones(string veg)
    {
        try { return File.Exists(RutaPara(veg)) ? OpcionesCapitulo.Leer(Json.Valor(Json.Leer(File.ReadAllText(RutaPara(veg), Encoding.UTF8)), "opciones")) : null; }
        catch { return null; }
    }

    public static bool Cargar(string veg, double duracion, int musicas, int ppm, out AnalisisCapitulo a, out string elegida,
                              out string indicaciones, out string notas, out PlanFinal f)
    {
        return Cargar(veg, duracion, musicas, ppm, PlantillaTV.PorDefecto(), out a, out elegida, out indicaciones, out notas, out f);
    }

    public static bool Cargar(string veg, double duracion, int musicas, int ppm, PlantillaTV tv, out AnalisisCapitulo a, out string elegida,
                              out string indicaciones, out string notas, out PlanFinal f)
    {
        a = null; f = null; elegida = ""; indicaciones = ""; notas = "";
        string r = RutaPara(veg);
        if (!File.Exists(r)) return false;
        try
        {
            object o = Json.Leer(File.ReadAllText(r, Encoding.UTF8));
            indicaciones = Json.Texto(o, "indicaciones"); notas = Json.Texto(o, "notas"); elegida = Json.Texto(o, "elegida");
            string an = Json.Texto(o, "analisis");
            if (an.Length > 0)
            {
                a = LeerAnalisis(an, duracion);
                string pr = Json.Texto(o, "propuestas");
                if (pr.Length > 0) Refinar(a, pr);
                Dictionary<string, object> np = Json.Obj(o, "notas_propuestas");
                if (np != null) foreach (Propuesta p in a.Propuestas) if (np.ContainsKey(p.Id)) p.Notas = Convert.ToString(np[p.Id]);
            }
            string fi = Json.Texto(o, "final");
            if (fi.Length > 0)
            {
                f = LeerFinal(fi, duracion, musicas, ppm, tv);
                List<string> fuera = new List<string>();
                foreach (object x in Json.Lista(o, "descartados")) if (x is string) fuera.Add((string)x);
                foreach (ItemFinal i in f.Todos()) if (fuera.Contains(Clave(i))) i.Elegido = false;
            }
            return true;
        }
        catch { return false; }
    }
}
