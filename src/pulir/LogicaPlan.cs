using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// =====================================================================
// PulirEpisodio, parte 2: plan de estructura y narracion con Gemini
//
// Gemini recibe lo que se dice en el video (con los tiempos actuales), las
// pausas donde cabe narracion, el informe de ritmo y el contexto de la
// serie (formato, premisa, papel del capitulo, capitulos anteriores y como
// abrieron) y propone: un gancho al inicio, las secciones, la narracion
// (frases medidas a la velocidad del narrador), los avances en pantalla
// ("Dia 3"), los recursos que faltan (placeholders) y que recortar.
// =====================================================================

public class ItemPlan
{
    public string Tipo = "";        // gancho, seccion, narracion, avance, recurso, recorte
    public double Inicio, Fin;      // en la linea de tiempo de antes de aplicar
    public string Texto = "", Detalle = "", Clase = "";
    public string Id = "";          // N01 (narracion), R01 (recurso)
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

public class Plan
{
    public string Resumen = "", Estructura = "", Respuesta = "", Generado = "";
    public List<ItemPlan> Items = new List<ItemPlan>();

    public List<ItemPlan> De(string tipo)
    {
        List<ItemPlan> r = new List<ItemPlan>();
        foreach (ItemPlan i in Items) if (i.Tipo == tipo) r.Add(i);
        return r;
    }

    public ItemPlan Gancho { get { List<ItemPlan> g = De("gancho"); return g.Count > 0 ? g[0] : null; } }
}

public static class LogicaPlan
{
    static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    public static readonly string[] Tipos = { "gancho", "seccion", "narracion", "avance", "recurso", "recorte" };

    public static string NombreTipo(string t)
    {
        switch (t)
        {
            case "gancho": return "Gancho";
            case "seccion": return "Sección";
            case "narracion": return "Narración";
            case "avance": return "Avance";
            case "recurso": return "Recurso";
            case "recorte": return "Recorte";
            default: return t;
        }
    }

    // Segundos que tarda en decirse un texto a "ppm" palabras por minuto.
    public static int Palabras(string texto)
    {
        int n = 0;
        foreach (string p in (texto ?? "").Split(new char[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
            if (p.Trim(',', '.', ';', ':', '¡', '!', '¿', '?', '"', '…', '-').Length > 0) n++;
        return n;
    }

    public static double Segundos(string texto, int ppm) { return Palabras(texto) * 60.0 / Math.Max(60, ppm) + 0.35; }

    // Pausas sin nadie hablando (en la linea de tiempo actual) de al menos "minimo" segundos.
    public static List<Rango> Pausas(Transcripcion t, double duracion, double minimo)
    {
        List<Rango> voz = new List<Rango>();
        if (t != null)
            foreach (Segmento s in t.SegmentosActuales())
                if (s.Fin > s.Inicio) voz.Add(new Rango(s.Inicio, s.Fin));
        voz = Rangos.Unir(voz, 0.25);
        List<Rango> r = new List<Rango>();
        double cursor = 0;
        foreach (Rango v in voz)
        {
            if (v.Inicio - cursor >= minimo) r.Add(new Rango(cursor, v.Inicio));
            cursor = Math.Max(cursor, v.Fin);
        }
        if (duracion - cursor >= minimo) r.Add(new Rango(cursor, duracion));
        return r;
    }

    // ------------------------------------------------------------ prompt

    public static string Instrucciones(FormatoSerie f, string tipo, string papel, ReglasRitmo r)
    {
        string narrador = f.Narrador
            ? "- NARRACIÓN: frases del narrador (" + f.NarradorNombre + ") que van encima del video. Estilo: " +
              (f.EstiloNarrador.Trim().Length > 0 ? f.EstiloNarrador.Trim() : "cercano, en pasado, con humor") + ".\n" +
              "  · Habla a " + r.PPM + " palabras por minuto: una frase de N palabras dura N×60/" + r.PPM + " s. Cada frase de 4 a 30 " +
              "palabras; pon \"inicio\" y calcula \"fin\" con esa velocidad.\n" +
              "  · Que nunca pasen más de " + r.NarradorCadaSeg + " s sin narrador (salvo un momento que se cuente solo).\n" +
              "  · Ponlas en las PAUSAS (donde nadie habla). Si no hay pausa, encima de charla sin importancia (el juego se baja " +
              "mientras narras). Nunca encima de un chiste o una frase clave.\n" +
              "  · Obligatorias: la primera frase entre 0 y 7 s (gancho: promete lo mejor del episodio sin contarlo); contexto " +
              "antes de los 30 s (de qué va la serie o dónde quedó); un \"re-gancho\" antes de " + Formato.Tiempo(r.ZonaCriticaSeg) +
              " (anticipa algo que viene); una invitación corta a suscribirse en el minuto 1 a 3 (tipo \"cta\"); y una frase " +
              "final que deje un pendiente para el siguiente capítulo (tipo \"adelanto\"), salvo que sea el final de la serie.\n" +
              "  · Usa ganchos de anticipación (\"lo cual no fue la mejor idea\", \"esto nos iba a costar caro\").\n"
            : "- NARRACIÓN: esta serie no lleva narrador; deja \"narracion\" vacío y usa textos en pantalla (recursos tipo texto).\n";
        string avance = f.Avance == "Ninguno" ? "" :
            "- AVANCES: dónde poner en pantalla \"" + f.Avance + "\" (cuando cambia el día, la parte o la etapa según lo que " +
            "dicen). Texto exacto, por ejemplo \"" + f.Marca(1) + "\".\n";
        return "Eres editor de " + Serie.QueEs(tipo) +
               ". Recibes un episodio YA CORTADO y propones cómo estructurarlo para que la gente se quede hasta el final.\n\n" +
               "FORMATO: " + f.Nombre + (FormatoSerie.Objetivo(f.Nombre).Length > 0 ? " (" + FormatoSerie.Objetivo(f.Nombre) + ")" : "") + "\n" +
               "PAPEL DEL CAPÍTULO: " + papel + ". " + PapelEpisodio.Instrucciones(papel) + "\n" +
               "DURACIÓN OBJETIVO: " + r.DuracionMin + "–" + r.DuracionMax + " min.\n\n" +
               "Propón:\n" +
               "- GANCHO: un momento fuerte del video (3 a 8 s, con inicio y fin de la transcripción) para mostrar al inicio, antes " +
               "de todo, como avance. Que no cuente el final. Puede ser null.\n" +
               "- SECCIONES: los bloques del episodio en orden (inicio, fin, nombre corto, para qué sirve).\n" +
               narrador + avance +
               "- RECURSOS: imágenes, memes, efectos o textos que faltan, sobre todo en los valles del informe y en la zona crítica " +
               "(unos " + r.RecursosPorMin + " por minuto donde haya pocos). Cada uno con \"clase\" (imagen, meme, efecto, texto, zoom), " +
               "duración de 1 a 5 s, una descripción concreta de qué mostrar y un nombre de archivo corto sugerido.\n" +
               "- RECORTES: si dura más que el objetivo o hay tramos largos sin interés (exploración sola, peleas largas), qué quitar " +
               "o acelerar (accion \"quitar\" o \"acelerar\") y por qué.\n\n" +
               "No repitas la forma de abrir de los capítulos anteriores (se indica en el contexto). Usa solo tiempos dentro del video.\n\n" +
               "Responde SOLO con JSON:\n" +
               "{\"resumen\": \"el episodio en 2 frases\", \"estructura\": \"cómo abre, avanza y cierra, en una frase\",\n" +
               " \"gancho\": {\"inicio\": s, \"fin\": s, \"motivo\": \"...\"} o null,\n" +
               " \"secciones\": [{\"inicio\": s, \"fin\": s, \"nombre\": \"...\", \"proposito\": \"...\"}],\n" +
               " \"narracion\": [{\"inicio\": s, \"fin\": s, \"texto\": \"...\", \"tipo\": \"gancho|contexto|avance|re-gancho|cta|transicion|cierre|adelanto\"}],\n" +
               " \"avances\": [{\"inicio\": s, \"texto\": \"" + (f.Avance == "Ninguno" ? "..." : f.Marca(2)) + "\"}],\n" +
               " \"recursos\": [{\"inicio\": s, \"duracion\": s, \"clase\": \"imagen|meme|efecto|texto|zoom\", \"descripcion\": \"...\", \"archivo\": \"...\"}],\n" +
               " \"recortes\": [{\"inicio\": s, \"fin\": s, \"accion\": \"quitar|acelerar\", \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(Transcripcion t, double duracion, Informe inf, string contextoSerie, string anteriores,
                                 string indicaciones, List<Rango> pausas)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("Duración del video: " + S(duracion) + " s (" + Formato.Tiempo(duracion) + ")\n");
        if (!String.IsNullOrEmpty(contextoSerie)) sb.Append("\nCONTEXTO DE LA SERIE:\n" + contextoSerie.Trim() + "\n");
        if (!String.IsNullOrEmpty(anteriores)) sb.Append("\nCÓMO ABRIERON Y CERRARON LOS CAPÍTULOS ANTERIORES (no repetir):\n" + anteriores.Trim() + "\n");
        if (!String.IsNullOrEmpty(indicaciones)) sb.Append("\nINDICACIONES DEL EDITOR:\n" + indicaciones.Trim() + "\n");
        if (inf != null) sb.Append("\nINFORME DE RITMO:\n" + LogicaPulir.Texto(inf));
        sb.Append("\nPAUSAS (nadie habla) [inicio-fin]:\n");
        foreach (Rango p in pausas) sb.Append("[" + S(p.Inicio) + "-" + S(p.Fin) + "] ");
        sb.Append("\n\nTRANSCRIPCIÓN (tiempos del video actual) [inicio-fin] persona: texto\n");
        if (t != null)
            foreach (Segmento s in t.SegmentosActuales())
            {
                if (String.IsNullOrEmpty(s.Texto)) continue;
                string quien = s.Hablante >= 0 && s.Hablante < t.Hablantes.Count ? t.Hablantes[s.Hablante].Nombre : "?";
                sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + quien + ": " + s.Texto + "\n");
            }
        return sb.ToString();
    }

    // "S01E01: abre con ... · cierra con ..." de las fichas de los capitulos anteriores.
    public static string Anteriores(List<CapSerie> caps)
    {
        StringBuilder sb = new StringBuilder();
        foreach (CapSerie c in caps)
        {
            if (c.Relacion >= 0) continue;
            Ficha f = Ficha.Cargar(c.Veg);
            if (f != null && f.Estructura.Trim().Length > 0) sb.Append("- " + c.Nombre + ": " + f.Estructura.Trim() + "\n");
        }
        return sb.ToString();
    }

    // ----------------------------------------------------------- respuesta

    static double Num(object x, string k, double siNo) { return Json.Numero(x, k, siNo); }

    public static Plan Leer(string json, double duracion, int ppm)
    {
        Plan p = new Plan();
        p.Respuesta = json;
        object o = Json.Leer(Gemini.QuitarCercas(json));
        p.Resumen = Json.Texto(o, "resumen");
        p.Estructura = Json.Texto(o, "estructura");

        object g = Json.Valor(o, "gancho");
        if (g is Dictionary<string, object>)
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "gancho";
            i.Inicio = Num(g, "inicio", -1); i.Fin = Num(g, "fin", -1);
            i.Texto = Json.Texto(g, "motivo");
            if (i.Inicio >= 0 && i.Fin > i.Inicio + 0.5 && i.Fin <= duracion + 0.5)
            {
                if (i.Duracion > 10) i.Fin = i.Inicio + 10;
                p.Items.Add(i);
            }
        }
        foreach (object x in Json.Lista(o, "secciones"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "seccion";
            i.Inicio = Num(x, "inicio", -1); i.Fin = Num(x, "fin", -1);
            i.Texto = Json.Texto(x, "nombre"); i.Detalle = Json.Texto(x, "proposito");
            if (i.Inicio >= 0 && i.Fin > i.Inicio && i.Texto.Length > 0) { i.Fin = Math.Min(i.Fin, duracion); p.Items.Add(i); }
        }
        List<ItemPlan> narr = new List<ItemPlan>();
        foreach (object x in Json.Lista(o, "narracion"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "narracion";
            i.Inicio = Num(x, "inicio", -1);
            i.Texto = Json.Texto(x, "texto").Trim();
            i.Clase = Json.Texto(x, "tipo");
            if (i.Inicio < 0 || i.Inicio > duracion || Palabras(i.Texto) == 0) continue;
            i.Fin = i.Inicio + Segundos(i.Texto, ppm);
            narr.Add(i);
        }
        Acomodar(narr, duracion);
        for (int k = 0; k < narr.Count; k++) narr[k].Id = "N" + (k + 1).ToString("00");
        p.Items.AddRange(narr);
        foreach (object x in Json.Lista(o, "avances"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "avance";
            i.Inicio = Num(x, "inicio", -1); i.Texto = Json.Texto(x, "texto").Trim();
            i.Fin = i.Inicio + 3;
            if (i.Inicio >= 0 && i.Inicio < duracion && i.Texto.Length > 0) p.Items.Add(i);
        }
        int nr = 0;
        foreach (object x in Json.Lista(o, "recursos"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "recurso";
            i.Inicio = Num(x, "inicio", -1);
            i.Fin = i.Inicio + Math.Max(1, Math.Min(6, Num(x, "duracion", 3)));
            i.Clase = Json.Texto(x, "clase");
            i.Texto = Json.Texto(x, "descripcion").Trim();
            i.Detalle = Limpio(Json.Texto(x, "archivo"));
            if (i.Inicio < 0 || i.Inicio >= duracion || i.Texto.Length == 0) continue;
            i.Fin = Math.Min(i.Fin, duracion);
            i.Id = "R" + (++nr).ToString("00");
            p.Items.Add(i);
        }
        foreach (object x in Json.Lista(o, "recortes"))
        {
            ItemPlan i = new ItemPlan();
            i.Tipo = "recorte";
            i.Inicio = Num(x, "inicio", -1); i.Fin = Num(x, "fin", -1);
            i.Clase = Json.Texto(x, "accion") == "acelerar" ? "acelerar" : "quitar";
            i.Texto = Json.Texto(x, "motivo");
            if (i.Inicio >= 0 && i.Fin > i.Inicio + 1) { i.Fin = Math.Min(i.Fin, duracion); p.Items.Add(i); }
        }
        p.Items.Sort(delegate (ItemPlan a, ItemPlan b)
        {
            int c = a.Inicio.CompareTo(b.Inicio);
            return c != 0 ? c : Array.IndexOf(Tipos, a.Tipo).CompareTo(Array.IndexOf(Tipos, b.Tipo));
        });
        // El gancho va primero siempre.
        ItemPlan gancho = p.Gancho;
        if (gancho != null) { p.Items.Remove(gancho); p.Items.Insert(0, gancho); }
        return p;
    }

    static string Limpio(string archivo)
    {
        string r = archivo ?? "";
        foreach (char c in Path.GetInvalidFileNameChars()) r = r.Replace(c, '_');
        r = r.Replace('\\', '_').Replace('/', '_').Trim();
        if (r.Contains(".")) r = r.Substring(0, r.LastIndexOf('.'));
        return r.Length > 40 ? r.Substring(0, 40) : r;
    }

    // Que las frases no se encimen: si una empieza antes de que termine la
    // anterior, se corre; si ya no cabe en el video, se quita.
    public static void Acomodar(List<ItemPlan> narr, double duracion)
    {
        narr.Sort(delegate (ItemPlan a, ItemPlan b) { return a.Inicio.CompareTo(b.Inicio); });
        double fin = 0;
        for (int k = 0; k < narr.Count; k++)
        {
            ItemPlan i = narr[k];
            double largo = i.Duracion;
            if (i.Inicio < fin + 0.2) i.Inicio = fin + 0.2;
            i.Fin = i.Inicio + largo;
            if (i.Fin > duracion + 0.5) { narr.RemoveAt(k); k--; continue; }
            fin = i.Fin;
        }
    }

    // Lo que hay que correr cada tiempo si se pone el gancho al inicio.
    public static double Corrimiento(Plan p, bool conGancho)
    {
        ItemPlan g = p.Gancho;
        return conGancho && g != null && g.Elegido ? Math.Round(g.Duracion + 0.5, 3) : 0;
    }

    // ---------------------------------------------------------- guion

    public static string Guion(Plan p, string proyecto, double corrimiento, int ppm)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("GUION DE NARRACIÓN · " + proyecto + "\n");
        sb.Append("Lee de corrido, con una pausa corta entre frases. Si una sale mal, repítela entera: se usa la última toma.\n");
        sb.Append("Velocidad objetivo: " + ppm + " palabras por minuto.\n\n");
        foreach (ItemPlan i in p.Items)
        {
            if (i.Tipo != "narracion" || !i.Elegido) continue;
            sb.Append(i.Id + "  [" + Formato.TiempoPreciso(i.Inicio + corrimiento) + "]" + (i.Clase.Length > 0 ? " (" + i.Clase + ")" : "") + "\n");
            sb.Append("    " + i.Texto + "\n\n");
        }
        List<ItemPlan> rec = p.De("recurso");
        if (rec.Count > 0)
        {
            sb.Append("\nRECURSOS (nombra el archivo empezando con su código, por ejemplo \"R01 algo.png\")\n");
            foreach (ItemPlan i in rec)
                if (i.Elegido)
                    sb.Append(i.Id + "  [" + Formato.Tiempo(i.Inicio + corrimiento) + "] " + i.Clase + ": " + i.Texto +
                              (i.Detalle.Length > 0 ? "  → " + i.Id + " " + i.Detalle : "") + "\n");
        }
        return sb.ToString();
    }

    // ------------------------------------------------- guardar y cargar

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-plan.json");
    }

    public static void Guardar(string veg, Plan p, Dictionary<string, object> extra)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["generado"] = p.Generado;
        d["respuesta"] = p.Respuesta;
        List<object> fuera = new List<object>();
        foreach (ItemPlan i in p.Items) if (!i.Elegido) fuera.Add(i.Tipo + "|" + S(i.Inicio) + "|" + i.Texto);
        d["descartados"] = fuera;
        // Lo aplicado antes se conserva (sirve para reemplazar la narracion y los placeholders).
        object previo = Aplicado(veg);
        if (previo != null) d["aplicado"] = previo;
        if (extra != null) foreach (KeyValuePair<string, object> kv in extra) d[kv.Key] = kv.Value;
        File.WriteAllText(RutaPara(veg), Json.Escribir(d), new UTF8Encoding(false));
    }

    public static Plan Cargar(string veg, double duracion, int ppm)
    {
        try
        {
            string r = RutaPara(veg);
            if (!File.Exists(r)) return null;
            object o = Json.Leer(File.ReadAllText(r, Encoding.UTF8));
            Plan p = Leer(Json.Texto(o, "respuesta"), duracion, ppm);
            p.Generado = Json.Texto(o, "generado");
            List<string> fuera = new List<string>();
            foreach (object x in Json.Lista(o, "descartados")) if (x is string) fuera.Add((string)x);
            foreach (ItemPlan i in p.Items) if (fuera.Contains(i.Tipo + "|" + S(i.Inicio) + "|" + i.Texto)) i.Elegido = false;
            return p;
        }
        catch { return null; }
    }

    // Lo que ya se aplico (narracion y placeholders), para "Reemplazar".
    public static object Aplicado(string veg)
    {
        try
        {
            string r = RutaPara(veg);
            return File.Exists(r) ? Json.Obj(Json.Leer(File.ReadAllText(r, Encoding.UTF8)), "aplicado") : null;
        }
        catch { return null; }
    }
}
