using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// =====================================================================
// ProducirCapitulo: de la grabacion (copia BASE, sin silencios y
// transcrita) a un capitulo armado como serie.
//
// 1. AnalisisCapitulo + 3 propuestas: Gemini lee todo el material con el contexto
//    de la serie (formato, plantilla, capitulos anteriores y posteriores,
//    reparto y sus temas) y propone tres formas de hacer el capitulo
//    (incluido un episodio doble si da para eso).
// 2. Propuesta final: con la propuesta elegida y tus notas, la escaleta
//    completa por bloques: tramos del material, titulos y carteles, musica de
//    tu biblioteca, narracion y placeholders. Se ajusta con notas hasta que
//    este bien; despues se produce.
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
    public string Id = "", Nombre = "", Tipo = "normal", PorQue = "", ColdOpen = "", Cierre = "", Notas = "";
    public List<string> Titulos = new List<string>(), Escaleta = new List<string>();
    public double Minutos;
    public bool Doble { get { return Tipo == "doble"; } }
}

public class AnalisisCapitulo
{
    public string Resumen = "", Respuesta = "";
    public double MinutosUtiles;
    public bool DobleRecomendado;
    public string DobleMotivo = "";
    public List<MomentoMaterial> Momentos = new List<MomentoMaterial>();
    public List<string> Hilos = new List<string>();
    public List<Propuesta> Propuestas = new List<Propuesta>();
}

// Un elemento de la propuesta final.
public class ItemFinal
{
    public int Parte;                  // 0 o 1 (episodio doble)
    public string Bloque = "";         // clave del bloque de la plantilla
    public string Tipo = "";           // clip, texto, musica, narracion, recurso, kit
    public double Inicio, Fin;         // segundos del material (clips; los demas solo Inicio)
    public string Texto = "", Detalle = "", Clase = "";
    public int Musica = -1;            // id del candidato de musica
    public string Personaje = "";      // tema del personaje
    public string Id = "";             // N01, R01
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

public class CapituloFinal
{
    public string Titulo = "", Etapa = "";
    public List<ItemFinal> Items = new List<ItemFinal>();
}

public class PlanFinal
{
    public string Respuesta = "", Resumen = "";
    public List<CapituloFinal> Partes = new List<CapituloFinal>();

    public IEnumerable<ItemFinal> Todos()
    {
        foreach (CapituloFinal c in Partes) foreach (ItemFinal i in c.Items) yield return i;
    }
}

public static class LogicaProduccion
{
    static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    public static readonly string[] TiposMomento = { "llegada", "problema", "revelacion", "crisis", "giro", "resolucion", "comico", "emotivo", "gancho" };

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
        "(2–3 min) y los termina en un misterio justo antes del opening.\n" +
        "Cierres: cliffhanger en plena crisis, anuncio de un enemigo nuevo, remate cómico, o seguir el viaje con el rival mirando.\n" +
        "Episodio doble (la mitad de los enemigos en SC): la parte 1 termina en el peor momento de la crisis; la parte 2 abre con el " +
        "recap de ese cliffhanger, sigue la crisis, llega el giro y la resolución.\n";

    // Transcripcion del material con los tiempos actuales (de la copia base).
    public static string Material(Transcripcion t, double limite)
    {
        StringBuilder sb = new StringBuilder();
        if (t == null) return "(sin transcripción)\n";
        foreach (Segmento s in t.SegmentosActuales())
        {
            if (String.IsNullOrEmpty(s.Texto)) continue;
            string quien = s.Hablante >= 0 && s.Hablante < t.Hablantes.Count ? t.Hablantes[s.Hablante].Nombre : "?";
            sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + quien + ": " + s.Texto + "\n");
            if (sb.Length > limite) { sb.Append("…\n"); break; }
        }
        return sb.ToString();
    }

    public static string Reparto(MusicaSerie m)
    {
        if (m == null || String.IsNullOrEmpty(m.Reparto)) return "";
        return "REPARTO:\n" + m.Reparto.Trim() + "\n";
    }

    // ------------------------------------------------- 1. analisis + propuestas

    public static string InstruccionesAnalisis(FormatoSerie f, string tipo, string papel)
    {
        ReglasRitmo r = PapelEpisodio.Reglas(f.Reglas, papel);
        return "Eres el director de " + Serie.QueEs(tipo) + " que se edita como una serie de TV estilo anime de JoJo's Bizarre " +
               "Adventure. Recibes TODO el material grabado de un capítulo (ya sin silencios) y el contexto de la serie. Analízalo y " +
               "propón TRES formas distintas de hacer el capítulo.\n\n" +
               "FORMATO: " + f.Nombre + ". " + FormatoSerie.Objetivo(f.Nombre) + "\n" +
               "PAPEL DEL CAPÍTULO: " + papel + ". " + PapelEpisodio.Instrucciones(papel) + "\n" +
               "DURACIÓN OBJETIVO: " + r.DuracionMin + "–" + r.DuracionMax + " min por capítulo.\n\n" + Guia + "\n" +
               "Análisis:\n- \"momentos\": los mejores momentos con su tipo (" + String.Join(", ", TiposMomento) + "), fuerza 1–5 y quién " +
               "participa. Usa solo tiempos de la transcripción.\n- \"hilos\": qué viene de capítulos anteriores o prepara algo de los " +
               "posteriores.\n- \"minutos_utiles\": cuánto material vale la pena.\n- \"doble\": si da para dos capítulos (más de ~" +
               (r.DuracionMax * 2 - 4) + " min útiles con dos crisis claras), dónde cortar y por qué.\n\n" +
               "Propuestas: tres distintas de verdad (por ejemplo una clásica, una que empiece por otro lado o se centre en alguien, y " +
               "una doble si el material lo amerita). Cada una con títulos al estilo JoJo (el nombre del rival, del lugar o de la " +
               "situación; si es doble, «… Parte 1» y «… Parte 2»), duración estimada, qué cold open y qué cierre usa, una escaleta " +
               "corta (5 a 8 pasos con tiempos del material) y por qué funciona.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"...\", \"minutos_utiles\": n,\n" +
               " \"momentos\": [{\"inicio\": s, \"fin\": s, \"tipo\": \"...\", \"texto\": \"...\", \"fuerza\": n, \"personajes\": [\"...\"]}],\n" +
               " \"hilos\": [\"...\"],\n" +
               " \"doble\": {\"recomendado\": true|false, \"motivo\": \"...\"},\n" +
               " \"propuestas\": [{\"id\": \"A\", \"nombre\": \"...\", \"tipo\": \"normal|doble|especial\", \"titulos\": [\"...\"], " +
               "\"minutos\": n, \"cold_open\": \"...\", \"cierre\": \"...\", \"escaleta\": [\"[mm:ss] ...\"], \"por_que\": \"...\"}]}";
    }

    public static string MensajeAnalisis(Transcripcion t, double duracion, string contextoSerie, string reparto, string indicaciones)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Material: " + Formato.Tiempo(duracion) + " (" + S(duracion) + " s)\n");
        if (!String.IsNullOrEmpty(contextoSerie)) sb.Append("\nCONTEXTO DE LA SERIE:\n" + contextoSerie.Trim() + "\n");
        if (!String.IsNullOrEmpty(reparto)) sb.Append("\n" + reparto);
        if (!String.IsNullOrEmpty(indicaciones)) sb.Append("\nINDICACIONES DEL EDITOR:\n" + indicaciones.Trim() + "\n");
        sb.Append("\nTRANSCRIPCIÓN DEL MATERIAL [inicio-fin] persona: texto\n" + Material(t, 400000));
        return sb.ToString();
    }

    public static AnalisisCapitulo LeerAnalisis(string json, double duracion)
    {
        AnalisisCapitulo a = new AnalisisCapitulo();
        a.Respuesta = json;
        object o = Json.Leer(Gemini.QuitarCercas(json));
        a.Resumen = Json.Texto(o, "resumen");
        a.MinutosUtiles = Json.Numero(o, "minutos_utiles", 0);
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
        string[] ids = { "A", "B", "C", "D" };
        foreach (object x in Json.Lista(o, "propuestas"))
        {
            Propuesta p = new Propuesta();
            p.Id = Json.Texto(x, "id");
            if (p.Id.Length == 0 || a.Propuestas.Exists(delegate (Propuesta q) { return q.Id == p.Id; })) p.Id = ids[Math.Min(3, a.Propuestas.Count)];
            p.Nombre = Json.Texto(x, "nombre"); p.Tipo = Json.Texto(x, "tipo");
            if (p.Tipo != "doble" && p.Tipo != "especial") p.Tipo = "normal";
            foreach (object y in Json.Lista(x, "titulos")) if (y is string) p.Titulos.Add((string)y);
            p.Minutos = Json.Numero(x, "minutos", 0);
            p.ColdOpen = Json.Texto(x, "cold_open"); p.Cierre = Json.Texto(x, "cierre"); p.PorQue = Json.Texto(x, "por_que");
            foreach (object y in Json.Lista(x, "escaleta")) if (y is string) p.Escaleta.Add((string)y);
            if (p.Nombre.Length > 0) a.Propuestas.Add(p);
            if (a.Propuestas.Count == 3) break;
        }
        if (a.Propuestas.Count == 0) throw new Exception("La respuesta no trae propuestas.");
        return a;
    }

    // ------------------------------------------------------ 2. propuesta final

    // Musica que se le ofrece a Gemini: primero los temas de la serie, despues
    // SC y Golden Wind (viaje, desierto, pandilla) y lo demas con datos del anime.
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
        PlantillaTV tv = f.Tv;
        StringBuilder bloques = new StringBuilder();
        foreach (BloqueTV b in tv.Bloques)
            bloques.Append("- " + b.Clave + " (" + b.Nombre + ", " + (b.Segundos > 0 ? b.Segundos + " s" : b.Porcentaje + " % de lo que queda") +
                           (b.Tipo == "kit" ? ", archivo fijo de la serie" : b.Tipo == "texto" ? ", texto en pantalla" : ", con clips del material") +
                           "): " + b.Descripcion + "\n");
        return "Eres el director y editor de una serie de YouTube editada como un anime de JoJo. Con el análisis, la propuesta elegida " +
               "y las notas del editor, arma la ESCALETA FINAL del capítulo" + (p != null && p.Doble ? " (episodio DOBLE: dos partes)" : "") +
               ".\n\nBLOQUES DE CADA CAPÍTULO, en este orden:\n" + bloques +
               "\nDURACIÓN: " + r.DuracionMin + "–" + r.DuracionMax + " min por capítulo, sumando los bloques fijos y los clips.\n\n" +
               "Para cada bloque con material da los CLIPS en el orden en que se verán (inicio y fin del material, de 2 a 60 s; " +
               "corta charla sin interés, repeticiones y silencios). Puedes usar un momento fuera de orden (cold open, avance).\n" +
               "TEXTOS en pantalla: \"titulo\" (\"" + (f.Avance != "Ninguno" ? f.Marca(1) + " · " : "") + "nombre del capítulo\"), " +
               "\"lugar\" o \"tiempo\" («6 horas más tarde»), \"ranking\" de la etapa, \"stats\" (tarjeta del rival para el re-gancho: " +
               "nombre y 4–6 atributos con letra A–E, como las cartas de stand) y \"continuara\". Cada uno con \"en\": segundo del " +
               "material donde aparece (o el bloque si va en un bloque fijo).\n" +
               "MÚSICA: un tema por escena, cambiando cada ~" + r.MusicaCadaSeg + " s o cuando cambia el ánimo, con \"id\" de la " +
               "biblioteca y \"en\": segundo del material. Usa el tema de un personaje cuando se luce (\"personaje\": nombre) y el tema " +
               "principal en el momento clave. No repitas el mismo tema dentro del capítulo.\n" +
               (f.Narrador ? "NARRACIÓN: " + (f.EstiloNarrador.Length > 0 ? f.EstiloNarrador : "primera persona, en pasado") + ". Frases de 4 a 30 " +
                             "palabras a " + r.PPM + " palabras por minuto, en pausas; al inicio, al final y para unir saltos. \"en\": segundo del " +
                             "material.\n" : "") +
               "RECURSOS: imágenes, memes o efectos que faltan (\"clase\", \"descripcion\", \"duracion\" 1–5 s, \"en\").\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"...\", \"partes\": [{\"titulo\": \"...\", \"etapa\": \"...\",\n" +
               "  \"bloques\": [{\"bloque\": \"cold_open\", \"clips\": [{\"inicio\": s, \"fin\": s, \"nota\": \"...\"}]}],\n" +
               "  \"textos\": [{\"tipo\": \"titulo|lugar|tiempo|ranking|stats|continuara\", \"texto\": \"...\", \"en\": s, \"bloque\": \"...\"}],\n" +
               "  \"musica\": [{\"id\": n, \"en\": s, \"bloque\": \"...\", \"personaje\": \"...\", \"motivo\": \"...\"}],\n" +
               "  \"narracion\": [{\"en\": s, \"texto\": \"...\"}],\n" +
               "  \"recursos\": [{\"en\": s, \"duracion\": s, \"clase\": \"...\", \"descripcion\": \"...\"}]}]}";
    }

    public static string MensajeFinal(AnalisisCapitulo a, Propuesta elegida, string notasGenerales, List<ArchivoMusica> musica, MusicaSerie m,
                                      string reparto, Transcripcion t, PlanFinal anterior, string cambios)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("ANÁLISIS: " + a.Resumen + "\n");
        if (a.Hilos.Count > 0) sb.Append("Hilos: " + String.Join("; ", a.Hilos.ToArray()) + "\n");
        sb.Append("Momentos:\n");
        foreach (MomentoMaterial x in a.Momentos)
            sb.Append("[" + S(x.Inicio) + "-" + S(x.Fin) + "] " + x.Tipo + " (" + x.Fuerza + "): " + x.Texto + "\n");
        sb.Append("\nPROPUESTA ELEGIDA: " + elegida.Id + " · " + elegida.Nombre + " (" + elegida.Tipo + ")\n");
        if (elegida.Titulos.Count > 0) sb.Append("Títulos: " + String.Join(" / ", elegida.Titulos.ToArray()) + "\n");
        sb.Append("Cold open: " + elegida.ColdOpen + "\nCierre: " + elegida.Cierre + "\nEscaleta:\n");
        foreach (string e in elegida.Escaleta) sb.Append("- " + e + "\n");
        StringBuilder notas = new StringBuilder();
        foreach (Propuesta p in a.Propuestas)
            if (p.Notas.Trim().Length > 0) notas.Append("- Sobre " + p.Id + " (" + p.Nombre + "): " + p.Notas.Trim() + "\n");
        if (notasGenerales.Trim().Length > 0) notas.Append("- " + notasGenerales.Trim() + "\n");
        if (notas.Length > 0) sb.Append("\nNOTAS DEL EDITOR:\n" + notas);
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
            sb.Append("\nESCALETA ANTERIOR (JSON), ajústala con estos CAMBIOS: " + cambios.Trim() + "\n" + Gemini.QuitarCercas(anterior.Respuesta) + "\n");
        sb.Append("\nTRANSCRIPCIÓN DEL MATERIAL [inicio-fin] persona: texto\n" + Material(t, 300000));
        return sb.ToString();
    }

    public static PlanFinal LeerFinal(string json, double duracion, int musicas, int ppm)
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
            foreach (object b in Json.Lista(x, "bloques"))
            {
                string bloque = Json.Texto(b, "bloque");
                foreach (object y in Json.Lista(b, "clips"))
                {
                    ItemFinal i = new ItemFinal();
                    i.Parte = parte; i.Bloque = bloque; i.Tipo = "clip";
                    i.Inicio = Math.Max(0, Json.Numero(y, "inicio", -1)); i.Fin = Math.Min(duracion, Json.Numero(y, "fin", -1));
                    i.Texto = Json.Texto(y, "nota");
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
        return p;
    }

    // Duracion estimada de una parte: clips elegidos + bloques fijos.
    public static double Duracion(CapituloFinal c, PlantillaTV tv)
    {
        double d = 0;
        foreach (ItemFinal i in c.Items) if (i.Tipo == "clip" && i.Elegido) d += i.Duracion;
        foreach (BloqueTV b in tv.Bloques) if (b.Tipo != "contenido") d += b.Segundos;
        return d;
    }

    // Clips de un bloque, en orden.
    public static double Bloque(CapituloFinal c, string bloque)
    {
        double d = 0;
        foreach (ItemFinal i in c.Items) if (i.Tipo == "clip" && i.Elegido && i.Bloque == bloque) d += i.Duracion;
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
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-produccion";
        d["indicaciones"] = indicaciones ?? "";
        d["notas"] = notas ?? "";
        if (a != null)
        {
            d["analisis"] = a.Respuesta;
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

    public static bool Cargar(string veg, double duracion, int musicas, int ppm, out AnalisisCapitulo a, out string elegida,
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
                Dictionary<string, object> np = Json.Obj(o, "notas_propuestas");
                if (np != null) foreach (Propuesta p in a.Propuestas) if (np.ContainsKey(p.Id)) p.Notas = Convert.ToString(np[p.Id]);
            }
            string fi = Json.Texto(o, "final");
            if (fi.Length > 0)
            {
                f = LeerFinal(fi, duracion, musicas, ppm);
                List<string> fuera = new List<string>();
                foreach (object x in Json.Lista(o, "descartados")) if (x is string) fuera.Add((string)x);
                foreach (ItemFinal i in f.Todos()) if (fuera.Contains(Clave(i))) i.Elegido = false;
            }
            return true;
        }
        catch { return false; }
    }
}
