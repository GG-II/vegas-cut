using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

// =====================================================================
// Serie de TV / anime: la plantilla de bloques de cada capitulo (cold open,
// opening, titulo, actos, re-gancho, continuara, ending, avance) y el kit de
// archivos fijos de la serie. Si falta un archivo del kit se pone un
// placeholder con la duracion del bloque.
// Salio de analizar Stardust Crusaders y Steel Ball Run
// (docs/estructura-episodio-sc.md).
// =====================================================================

public class BloqueTV
{
    public string Clave = "", Nombre = "";
    public string Tipo = "contenido";   // contenido (del capitulo), kit (archivo fijo), texto
    public double Segundos;             // duracion fija (cold open, kit, texto, avance)
    public double Porcentaje;           // de lo que queda (actos)
    public string Descripcion = "";
    public string Kit = "";             // archivo del kit que usa (vacio = el de su clave)
    public string Ritmo = "";           // lento, medio o rapido (lo propone la escaleta)

    public string ClaveKit { get { return Kit.Length > 0 ? Kit : Clave; } }

    public BloqueTV() { }
    public BloqueTV(string clave, string nombre, string tipo, double segundos, double porcentaje, string descripcion)
    {
        Clave = clave; Nombre = nombre; Tipo = tipo; Segundos = segundos; Porcentaje = porcentaje; Descripcion = descripcion;
    }

    public BloqueTV Copia() { return (BloqueTV)MemberwiseClone(); }
}

public class PlantillaTV
{
    public List<BloqueTV> Bloques = new List<BloqueTV>();
    public Dictionary<string, string> Kit = new Dictionary<string, string>();   // clave del bloque -> archivo

    public static PlantillaTV PorDefecto()
    {
        PlantillaTV p = new PlantillaTV();
        p.Bloques.Add(new BloqueTV("cold_open", "Cold open", "contenido", 45, 0,
            "Recap del cliffhanger anterior, llegada con humor o el rival tramando algo; termina en un gancho."));
        p.Bloques.Add(new BloqueTV("op", "Opening", "kit", 20, 0, "Opening propio (montaje de la serie)."));
        p.Bloques.Add(new BloqueTV("titulo", "Título y lugar", "texto", 3, 0, "«Etapa N · nombre del capítulo» y cartel del lugar o del tiempo."));
        p.Bloques.Add(new BloqueTV("acto_a", "Acto A", "contenido", 0, 55,
            "Viaje y llegada (0 %), aparece el problema (~13 %), se revela (~35 %) y la crisis (~45 %)."));
        p.Bloques.Add(new BloqueTV("regancho", "Re-gancho", "kit", 6, 0, "Tarjeta de stats del rival o ranking de la etapa (el eyecatch)."));
        p.Bloques.Add(new BloqueTV("acto_b", "Acto B", "contenido", 0, 45,
            "Giro (~58 %), resolución con ranking (~77 %), remate y gancho final (95–99 %)."));
        p.Bloques.Add(new BloqueTV("continuara", "Continuará", "kit", 3, 0, "Flecha «To Be Continued» sobre el cliffhanger."));
        p.Bloques.Add(new BloqueTV("ed", "Ending", "kit", 15, 0, "Ending corto."));
        p.Bloques.Add(new BloqueTV("avance", "Avance / post-créditos", "contenido", 12, 0,
            "Escena del próximo capítulo (ya grabado) o un hilo nuevo."));
        return p;
    }

    public PlantillaTV Copia()
    {
        PlantillaTV p = new PlantillaTV();
        foreach (BloqueTV b in Bloques) p.Bloques.Add(b.Copia());
        foreach (KeyValuePair<string, string> kv in Kit) p.Kit[kv.Key] = kv.Value;
        return p;
    }

    public BloqueTV Bloque(string clave)
    {
        foreach (BloqueTV b in Bloques) if (b.Clave == clave) return b;
        return null;
    }

    public string Archivo(string clave)
    {
        string a;
        return Kit.TryGetValue(clave, out a) ? a : "";
    }

    // Segundos de lo fijo (todo menos los actos).
    public double Fijo()
    {
        double s = 0;
        foreach (BloqueTV b in Bloques) s += b.Segundos;
        return s;
    }

    // Cuanto dura cada acto para que el capitulo dure "total" segundos.
    public double Acto(string clave, double total)
    {
        BloqueTV b = Bloque(clave);
        double pct = 0;
        foreach (BloqueTV x in Bloques) pct += x.Porcentaje;
        return b == null || pct <= 0 ? 0 : Math.Max(0, total - Fijo()) * b.Porcentaje / pct;
    }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        List<object> l = new List<object>();
        foreach (BloqueTV b in Bloques)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["clave"] = b.Clave; x["nombre"] = b.Nombre; x["tipo"] = b.Tipo; x["segundos"] = b.Segundos;
            x["porcentaje"] = b.Porcentaje; x["descripcion"] = b.Descripcion;
            string a = Archivo(b.Clave);
            if (a.Length > 0) x["archivo"] = a;
            l.Add(x);
        }
        d["bloques"] = l;
        return d;
    }

    public static PlantillaTV Leer(object o)
    {
        List<object> l = Json.Lista(o, "bloques");
        if (l.Count == 0) return PorDefecto();
        PlantillaTV p = new PlantillaTV();
        foreach (object x in l)
        {
            BloqueTV b = new BloqueTV(Json.Texto(x, "clave"), Json.Texto(x, "nombre"), Json.Texto(x, "tipo"),
                                      Json.Numero(x, "segundos", 0), Json.Numero(x, "porcentaje", 0), Json.Texto(x, "descripcion"));
            if (b.Clave.Length == 0) continue;
            p.Bloques.Add(b);
            string a = Json.Texto(x, "archivo");
            if (a.Length > 0) p.Kit[b.Clave] = a;
        }
        return p;
    }
}

// Tema asignado (principal o de un personaje), con sus variantes.
public class TemaAsignado
{
    public string Archivo = "", Motivo = "";
    public List<string> Variantes = new List<string>();

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["archivo"] = Archivo; d["motivo"] = Motivo; d["variantes"] = new List<object>(Variantes.ToArray());
        return d;
    }

    public static TemaAsignado Leer(object o)
    {
        if (o == null) return null;
        TemaAsignado t = new TemaAsignado();
        t.Archivo = Json.Texto(o, "archivo"); t.Motivo = Json.Texto(o, "motivo");
        foreach (object x in Json.Lista(o, "variantes")) if (x is string) t.Variantes.Add((string)x);
        return t.Archivo.Length > 0 ? t : null;
    }
}

// Musica de la serie: la carpeta (con su indice), el reparto y sus temas.
// Una vez elegidos se mantienen en todos los capitulos.
public class MusicaSerie
{
    public string Carpeta = "", Reparto = "";
    public double VolumenDb = -21;   // nivel de la pista de musica al producir (el balance fino va en el paso final)
    public TemaAsignado Principal;
    public Dictionary<string, TemaAsignado> Personajes = new Dictionary<string, TemaAsignado>();

    // Ganancia lineal de la pista (1 = 0 dB).
    public static float Lineal(double db) { return (float)Math.Pow(10, db / 20.0); }

    // Nombres del reparto: "Nombre: como es" o "Nombre - como es", uno por linea.
    public List<string> Nombres()
    {
        List<string> r = new List<string>();
        foreach (string l in (Reparto ?? "").Replace("\r", "").Split('\n'))
        {
            string n = l.Split(new char[] { ':', '—', '–' }, 2)[0];
            int g = n.IndexOf(" - ");
            if (g > 0) n = n.Substring(0, g);
            n = n.Trim().TrimStart('-', '*', '•').Trim();
            if (n.Length > 0 && !r.Contains(n)) r.Add(n);
        }
        return r;
    }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["carpeta"] = Carpeta; d["reparto"] = Reparto; d["volumen_db"] = VolumenDb;
        if (Principal != null) d["principal"] = Principal.Escribir();
        Dictionary<string, object> p = new Dictionary<string, object>();
        foreach (KeyValuePair<string, TemaAsignado> kv in Personajes) p[kv.Key] = kv.Value.Escribir();
        d["personajes"] = p;
        return d;
    }

    public static MusicaSerie Leer(object o)
    {
        MusicaSerie m = new MusicaSerie();
        if (o == null) return m;
        m.Carpeta = Json.Texto(o, "carpeta"); m.Reparto = Json.Texto(o, "reparto");
        m.VolumenDb = Math.Max(-60, Math.Min(0, Json.Numero(o, "volumen_db", -21)));
        m.Principal = TemaAsignado.Leer(Json.Valor(o, "principal"));
        Dictionary<string, object> p = Json.Obj(o, "personajes");
        if (p != null)
            foreach (KeyValuePair<string, object> kv in p)
            {
                TemaAsignado t = TemaAsignado.Leer(kv.Value);
                if (t != null) m.Personajes[kv.Key] = t;
            }
        return m;
    }

    // ------------------------------------------- elegir los temas con Gemini

    // Un tema por grupo de variantes (para no ofrecer el mismo tema dos veces).
    public static List<ArchivoMusica> Candidatos(BibliotecaMusica b)
    {
        List<ArchivoMusica> r = new List<ArchivoMusica>();
        Dictionary<string, bool> vistos = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (ArchivoMusica a in b.Archivos)
        {
            bool sirve = a.ConUso || a.Fuente == "SBR fan";
            if (!sirve || vistos.ContainsKey(a.Ruta)) continue;
            vistos[a.Ruta] = true;
            foreach (string v in a.Variantes) vistos[v] = true;
            r.Add(a);
        }
        return r;
    }

    public static string Instrucciones()
    {
        return "Eres supervisor musical de una serie de YouTube de Minecraft con amigos, editada como un anime de JoJo's " +
               "Bizarre Adventure. Elige de la biblioteca el TEMA PRINCIPAL de la serie (suena en los momentos clave y en las " +
               "victorias) y UN TEMA PARA CADA PERSONAJE del reparto (suena cuando ese personaje se luce o entra en escena).\n\n" +
               "Reglas:\n- Usa la personalidad de cada personaje y la premisa de la serie. Prefiere temas que en el anime ya son " +
               "de un personaje parecido (\"tema de\") o que suenan en escenas que le quedan.\n" +
               "- Un tema distinto para cada personaje y distinto del principal.\n" +
               "- Respeta las preferencias del editor si las hay.\n" +
               "- Solo ids de la lista.\n\n" +
               "Responde SOLO con JSON:\n{\"principal\": {\"id\": n, \"motivo\": \"...\"}, " +
               "\"personajes\": [{\"nombre\": \"...\", \"id\": n, \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(List<ArchivoMusica> candidatos, string premisa, string reparto, string preferencias)
    {
        StringBuilder sb = new StringBuilder();
        if (!String.IsNullOrEmpty(premisa)) sb.Append("PREMISA DE LA SERIE:\n" + premisa.Trim() + "\n\n");
        sb.Append("REPARTO (nombre: cómo es):\n" + (reparto ?? "").Trim() + "\n\n");
        if (!String.IsNullOrEmpty(preferencias)) sb.Append("PREFERENCIAS DEL EDITOR:\n" + preferencias.Trim() + "\n\n");
        sb.Append("BIBLIOTECA [id] título (de dónde) | ánimo | tema de | dónde suena en el anime\n");
        for (int i = 0; i < candidatos.Count; i++)
        {
            ArchivoMusica a = candidatos[i];
            sb.Append("[" + i + "] " + a.Titulo + " (" + (a.Parte.Length > 0 ? a.Parte : a.Fuente) + ")");
            if (a.Animos.Count > 0) sb.Append(" | " + String.Join(", ", a.Animos.ToArray()));
            if (a.TemaDe.Count > 0) sb.Append(" | tema de " + String.Join(", ", a.TemaDe.ToArray()));
            if (a.Escenas.Count > 0) sb.Append(" | " + String.Join("; ", a.Escenas.GetRange(0, Math.Min(2, a.Escenas.Count)).ToArray()));
            sb.Append("\n");
        }
        return sb.ToString();
    }

    static TemaAsignado Asignar(List<ArchivoMusica> c, object x)
    {
        int id = (int)Json.Numero(x, "id", -1);
        if (id < 0 || id >= c.Count) return null;
        TemaAsignado t = new TemaAsignado();
        t.Archivo = c[id].Ruta; t.Motivo = Json.Texto(x, "motivo");
        t.Variantes.AddRange(c[id].Variantes);
        return t;
    }

    // Aplica la respuesta: solo los personajes del reparto y sin repetir temas.
    public int Aplicar(string json, List<ArchivoMusica> candidatos)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        int n = 0;
        TemaAsignado p = Asignar(candidatos, Json.Valor(o, "principal"));
        if (p != null) { Principal = p; n++; }
        List<string> nombres = Nombres();
        Dictionary<string, bool> usados = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (Principal != null) usados[Principal.Archivo] = true;
        foreach (object x in Json.Lista(o, "personajes"))
        {
            string nombre = Json.Texto(x, "nombre").Trim();
            string real = nombres.Find(delegate (string q) { return String.Equals(q, nombre, StringComparison.OrdinalIgnoreCase); });
            if (real == null) continue;
            TemaAsignado t = Asignar(candidatos, x);
            if (t == null || usados.ContainsKey(t.Archivo)) continue;
            usados[t.Archivo] = true;
            Personajes[real] = t;
            n++;
        }
        return n;
    }
}
