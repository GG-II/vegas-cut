using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

// =====================================================================
// Rellenar la musica: en el capitulo ya editado (cortado, reordenado),
// busca los huecos de la pista de musica y les pone un tema de la
// biblioteca segun lo que pasa ahi. Lo que ya esta en la pista no se toca.
// =====================================================================

public class HuecoMusica
{
    public int N;
    public double Inicio, Fin;
    public string Dicho = "", Bloque = "", Antes = "", Despues = "", Marcas = "";
    public int Tema = -1;              // id del candidato
    public ArchivoMusica Archivo;      // el tema elegido (cada pedido tiene su propia muestra: el id solo vale en ella)
    public string Personaje = "", Motivo = "";
    public bool Silencio;              // la IA prefiere dejarlo sin musica
    public bool Elegido = true;
    public double Duracion { get { return Fin - Inicio; } }
}

// Que musica se uso en cada capitulo, para no repetirla (por serie).
public class UsoTema { public string Capitulo = "", Fecha = "", Ruta = ""; }

public static class HistorialMusica
{
    static string Archivo { get { return Path.Combine(Configuracion.Carpeta, "musica-usos.json"); } }

    public static List<UsoTema> Cargar(string serie)
    {
        List<UsoTema> r = new List<UsoTema>();
        try
        {
            if (!File.Exists(Archivo)) return r;
            foreach (object x in Json.Lista(Json.Leer(File.ReadAllText(Archivo, Encoding.UTF8)), serie.Length == 0 ? "general" : "serie:" + serie))
                r.Add(new UsoTema { Capitulo = Json.Texto(x, "capitulo"), Fecha = Json.Texto(x, "fecha"), Ruta = Json.Texto(x, "ruta") });
        }
        catch { }
        return r;
    }

    // Anota lo que se coloco en un capitulo (si ya habia algo de ese capitulo, se reemplaza).
    public static void Registrar(string serie, string capitulo, List<string> rutas)
    {
        Dictionary<string, object> todo = null;
        try { if (File.Exists(Archivo)) todo = Json.Leer(File.ReadAllText(Archivo, Encoding.UTF8)) as Dictionary<string, object>; } catch { }
        if (todo == null) todo = new Dictionary<string, object>();
        string clave = serie.Length == 0 ? "general" : "serie:" + serie;
        List<UsoTema> usos = Cargar(serie);
        usos.RemoveAll(delegate (UsoTema u) { return String.Equals(u.Capitulo, capitulo, StringComparison.OrdinalIgnoreCase); });
        string fecha = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        foreach (string r in rutas) usos.Add(new UsoTema { Capitulo = capitulo, Fecha = fecha, Ruta = r });
        if (rutas.Count == 0) usos.Add(new UsoTema { Capitulo = capitulo, Fecha = fecha, Ruta = "" });   // revisado, sin musica de la biblioteca
        List<object> l = new List<object>();
        foreach (UsoTema u in usos)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["capitulo"] = u.Capitulo; d["fecha"] = u.Fecha; d["ruta"] = u.Ruta;
            l.Add(d);
        }
        todo[clave] = l;
        Directory.CreateDirectory(Configuracion.Carpeta);
        File.WriteAllText(Archivo, Json.Escribir(todo), new UTF8Encoding(false));
    }

    // Rutas (en minusculas) usadas en los "n" capitulos anteriores al actual. Con el orden de la serie
    // son los n que van justo antes; sin el, los ultimos n que se trabajaron (por fecha).
    public static List<string> Recientes(List<UsoTema> usos, string actual, int n, List<string> orden)
    {
        List<string> capitulos = new List<string>();
        int i = orden == null ? -1 : orden.FindIndex(delegate (string x) { return String.Equals(x, actual, StringComparison.OrdinalIgnoreCase); });
        if (i >= 0)
            for (int k = i - 1; k >= 0 && capitulos.Count < n; k--) capitulos.Add(orden[k]);
        else
        {
            List<UsoTema> otros = usos.FindAll(delegate (UsoTema u) { return !String.Equals(u.Capitulo, actual, StringComparison.OrdinalIgnoreCase); });
            otros.Sort(delegate (UsoTema a, UsoTema b) { return String.CompareOrdinal(b.Fecha, a.Fecha); });
            foreach (UsoTema u in otros) { if (!capitulos.Contains(u.Capitulo) && capitulos.Count < n) capitulos.Add(u.Capitulo); }
        }
        List<string> r = new List<string>();
        foreach (UsoTema u in usos)
            if (u.Ruta.Length > 0 && capitulos.Exists(delegate (string c) { return String.Equals(c, u.Capitulo, StringComparison.OrdinalIgnoreCase); }))
                r.Add(u.Ruta.ToLowerInvariant());
        return r;
    }

    static readonly System.Text.RegularExpressions.Regex Audios = new System.Text.RegularExpressions.Regex(
        @"([^\\/:*?""<>|\x00-\x1f]*)[\\/]([^\\/:*?""<>|\x00-\x1f]+\.(?:mp3|wav|flac|ogg|m4a|aac|wma|opus))", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Que temas de la biblioteca usa cada proyecto, leyendo los .veg (guardan las rutas de sus archivos en UTF-16,
    // a veces en UTF-8). Es aproximado: tambien cuenta lo que quedo en los medios del proyecto sin usarse.
    public static Dictionary<string, List<string>> DeProyectos(List<string> vegs, List<ArchivoMusica> todos)
    {
        Dictionary<string, int> repetidos = new Dictionary<string, int>();
        foreach (ArchivoMusica a in todos)
        {
            string n = Path.GetFileName(a.Ruta).ToLowerInvariant();
            int c; repetidos.TryGetValue(n, out c); repetidos[n] = c + 1;
        }
        Dictionary<string, List<string>> r = new Dictionary<string, List<string>>();
        foreach (string veg in vegs)
        {
            List<string> usados = new List<string>();
            r[Path.GetFileNameWithoutExtension(SerieProyecto.SinCopia(veg))] = usados;
            Dictionary<string, List<string>> nombres = new Dictionary<string, List<string>>();
            try
            {
                byte[] b = File.ReadAllBytes(veg);
                foreach (string texto in new string[] { Encoding.Unicode.GetString(b), Encoding.UTF8.GetString(b) })
                    foreach (System.Text.RegularExpressions.Match m in Audios.Matches(texto))
                    {
                        string n = m.Groups[2].Value.ToLowerInvariant();
                        if (!nombres.ContainsKey(n)) nombres[n] = new List<string>();
                        nombres[n].Add(m.Groups[1].Value.ToLowerInvariant());
                    }
            }
            catch { continue; }
            foreach (ArchivoMusica a in todos)
            {
                string n = Path.GetFileName(a.Ruta).ToLowerInvariant();
                List<string> dirs;
                if (!nombres.TryGetValue(n, out dirs)) continue;
                // Un nombre que se repite en la biblioteca (01.mp3) solo cuenta si coincide su carpeta.
                string carpeta = Path.GetFileName(Path.GetDirectoryName(a.Ruta) ?? "").ToLowerInvariant();
                if (repetidos[n] > 1 && (carpeta.Length == 0 || !dirs.Exists(delegate (string d) { return d.EndsWith(carpeta); }))) continue;
                if (!usados.Contains(a.Ruta)) usados.Add(a.Ruta);
            }
        }
        return r;
    }

    // Cuantas veces se uso cada tema en los capitulos de la serie (sin contar el actual).
    public static Dictionary<string, int> Conteo(List<UsoTema> usos, string actual)
    {
        Dictionary<string, int> r = new Dictionary<string, int>();
        foreach (UsoTema u in usos)
        {
            if (u.Ruta.Length == 0 || String.Equals(u.Capitulo, actual, StringComparison.OrdinalIgnoreCase)) continue;
            string k = u.Ruta.ToLowerInvariant();
            int c; r.TryGetValue(k, out c); r[k] = c + 1;
        }
        return r;
    }
}

public static class LogicaRelleno
{
    public const int MuestraApi = 250, MuestraChat = 400;
    public const double Temperatura = 1.0;   // mas variado que el 0.4 de siempre

    static void Mezclar<T>(List<T> l, Random rnd)
    {
        for (int i = l.Count - 1; i > 0; i--) { int j = rnd.Next(i + 1); T t = l[i]; l[i] = l[j]; l[j] = t; }
    }

    // Los temas que se le ofrecen a la IA en este pedido. Antes siempre iban los mismos 300 en el mismo
    // orden, asi que elegia lo mismo. Ahora: sin lo que se uso hace poco o ya se propuso, con todos los
    // animos representados, los menos usados primero y en otro orden cada vez.
    public static List<ArchivoMusica> Muestra(List<ArchivoMusica> todos, List<string> evitar, Dictionary<string, int> usos, int max, Random rnd)
    {
        Dictionary<string, bool> fuera = new Dictionary<string, bool>();
        foreach (string e in evitar) fuera[e.ToLowerInvariant()] = true;
        List<ArchivoMusica> libres = new List<ArchivoMusica>(), evitados = new List<ArchivoMusica>();
        foreach (ArchivoMusica a in todos) (fuera.ContainsKey(a.Ruta.ToLowerInvariant()) ? evitados : libres).Add(a);
        // Si casi no queda nada, vuelven algunos de los evitados (mejor repetir que quedarse sin opciones).
        int minimo = Math.Min(60, todos.Count);
        Mezclar(evitados, rnd);
        while (libres.Count < minimo && evitados.Count > 0) { libres.Add(evitados[0]); evitados.RemoveAt(0); }

        Dictionary<string, List<ArchivoMusica>> grupos = new Dictionary<string, List<ArchivoMusica>>();
        foreach (ArchivoMusica a in libres)
        {
            string g = a.Animos.Count > 0 ? a.Animos[0] : "?";
            if (!grupos.ContainsKey(g)) grupos[g] = new List<ArchivoMusica>();
            grupos[g].Add(a);
        }
        List<List<ArchivoMusica>> listas = new List<List<ArchivoMusica>>();
        foreach (List<ArchivoMusica> g in grupos.Values)
        {
            // Al azar, y de ahi los menos usados primero.
            Dictionary<ArchivoMusica, double> suerte = new Dictionary<ArchivoMusica, double>();
            foreach (ArchivoMusica a in g) suerte[a] = rnd.NextDouble();
            g.Sort(delegate (ArchivoMusica a, ArchivoMusica b)
            {
                int ua, ub; usos.TryGetValue(a.Ruta.ToLowerInvariant(), out ua); usos.TryGetValue(b.Ruta.ToLowerInvariant(), out ub);
                return ua != ub ? ua.CompareTo(ub) : suerte[a].CompareTo(suerte[b]);
            });
            listas.Add(g);
        }
        Mezclar(listas, rnd);
        List<ArchivoMusica> r = new List<ArchivoMusica>();
        for (int i = 0; r.Count < max; i++)
        {
            bool alguno = false;
            foreach (List<ArchivoMusica> g in listas) if (i < g.Count && r.Count < max) { r.Add(g[i]); alguno = true; }
            if (!alguno) break;
        }
        Mezclar(r, rnd);
        return r;
    }

    // Lo que se va a colocar y se anota en el historial (los temas de personaje no: esos se repiten a proposito).
    public static List<string> Colocados(List<HuecoMusica> huecos, List<ArchivoMusica> candidatos)
    {
        List<string> r = new List<string>();
        foreach (HuecoMusica h in huecos)
            if (h.Elegido && !h.Silencio && h.Personaje.Length == 0)
            {
                ArchivoMusica a = h.Archivo != null ? h.Archivo : (h.Tema >= 0 && h.Tema < candidatos.Count ? candidatos[h.Tema] : null);
                if (a != null && !r.Contains(a.Ruta)) r.Add(a.Ruta);
            }
        return r;
    }

    public const string PistaMusica = "vegas-cut · Música";
    public const double MaximoHueco = 120;   // un hueco mas largo se parte: un tema dura 1–3 min

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static string F(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    static string Plano(string t)
    {
        string d = (t ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        foreach (char c in d) if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark) sb.Append(c);
        return sb.ToString();
    }

    // Pistas de musica: la de vegas-cut y las que se llaman "musica" / "music" / "ost".
    // Carpeta de la biblioteca de la serie: una pista cuyos audios vienen de ahi tambien es de musica
    // (en un video hecho con MomentosIA la pista puede llamarse de cualquier forma).
    public static string CarpetaMusica = "";

    public static List<Track> PistasMusica(Project p)
    {
        List<Track> r = new List<Track>();
        string raiz = (CarpetaMusica ?? "").TrimEnd('\\', '/');
        foreach (Track t in p.Tracks)
        {
            if (!t.IsAudio()) continue;
            string n = Plano(t.Name);
            if (t.Name == PistaMusica || n.Contains("musica") || n.Contains("music") || n.Contains(" ost") || n.StartsWith("ost") || n.Contains("bgm"))
            {
                r.Add(t);
                continue;
            }
            if (raiz.Length == 0) continue;
            int si = 0, total = 0;
            foreach (TrackEvent e in t.Events) { total++; if (Archivo(e).StartsWith(raiz, StringComparison.OrdinalIgnoreCase)) si++; }
            if (total > 0 && si * 2 >= total) r.Add(t);
        }
        return r;
    }

    static string Archivo(TrackEvent e)
    {
        try { return e.ActiveTake != null && e.ActiveTake.Media != null ? (e.ActiveTake.Media.FilePath ?? "") : ""; } catch { return ""; }
    }

    // Los huecos de al menos "minimo" segundos donde no suena musica ni el kit
    // (opening, ending, eyecatch), dentro de lo que dura el video.
    public static List<HuecoMusica> Huecos(Project p, Transcripcion t, double minimo)
    {
        double fin = 0;
        foreach (Track x in p.Tracks)
            if (!x.IsAudio()) foreach (TrackEvent e in x.Events) fin = Math.Max(fin, S(e.End));
        List<Rango> ocupado = new List<Rango>();
        List<TrackEvent> musica = new List<TrackEvent>();
        foreach (Track x in PistasMusica(p))
            foreach (TrackEvent e in x.Events) if (!e.Mute) { ocupado.Add(new Rango(S(e.Start), S(e.End))); musica.Add(e); }
        foreach (Track x in p.Tracks)
            if ((x.Name ?? "").Contains("Kit"))
                foreach (TrackEvent e in x.Events) ocupado.Add(new Rango(S(e.Start), S(e.End)));
        ocupado = Rangos.Unir(ocupado, 0.5);

        List<HuecoMusica> r = new List<HuecoMusica>();
        double desde = 0;
        List<Rango> libres = new List<Rango>();
        foreach (Rango o in ocupado)
        {
            if (o.Inicio - desde >= minimo && desde < fin) libres.Add(new Rango(desde, Math.Min(o.Inicio, fin)));
            desde = Math.Max(desde, o.Fin);
        }
        if (fin - desde >= minimo) libres.Add(new Rango(desde, fin));
        // Los huecos largos se parten (en el borde de un bloque si hay uno cerca).
        // Donde conviene cambiar de tema: inicio de bloques y marcadores (los ★ y TEXTO de MomentosIA).
        List<double> cortes = new List<double>();
        foreach (Region g in p.Regions) cortes.Add(S(g.Position));
        foreach (Marker mk in p.Markers) if (!(mk is Region)) cortes.Add(S(mk.Position));
        List<Rango> partidos = new List<Rango>();
        foreach (Rango l in libres)
        {
            double a = l.Inicio;
            while (l.Fin - a > MaximoHueco)
            {
                int n = (int)Math.Ceiling((l.Fin - a) / MaximoHueco);
                double corte = a + (l.Fin - a) / n;
                double mejor = double.MaxValue, elegido = corte;
                foreach (double ini in cortes)
                    if (ini > a + minimo && ini < l.Fin - minimo && Math.Abs(ini - corte) < 30 && Math.Abs(ini - corte) < mejor) { mejor = Math.Abs(ini - corte); elegido = ini; }
                corte = elegido;
                partidos.Add(new Rango(a, corte));
                a = corte;
            }
            partidos.Add(new Rango(a, l.Fin));
        }
        libres = partidos;

        List<Segmento> segs = t != null ? t.SegmentosActuales() : new List<Segmento>();
        foreach (Rango l in libres)
        {
            if (l.Fin - l.Inicio < minimo) continue;
            HuecoMusica h = new HuecoMusica();
            h.N = r.Count + 1; h.Inicio = l.Inicio; h.Fin = l.Fin;
            StringBuilder d = new StringBuilder();
            foreach (Segmento s in segs)
            {
                if (s.Fin <= l.Inicio || s.Inicio >= l.Fin || String.IsNullOrEmpty(s.Texto)) continue;
                string quien = s.Hablante >= 0 && s.Hablante < t.Hablantes.Count ? t.Hablantes[s.Hablante].Nombre : "?";
                d.Append(quien + ": " + s.Texto.Trim() + " ");
                if (d.Length > 700) { d.Append("…"); break; }
            }
            h.Dicho = d.ToString().Trim();
            // Bloque: la region mas corta que lo contiene (COLD OPEN, ACTO A...).
            double medio = (l.Inicio + l.Fin) / 2, menor = double.MaxValue;
            foreach (Region g in p.Regions)
            {
                double a = S(g.Position), b = a + S(g.Length);
                if (medio >= a && medio <= b && b - a < menor) { menor = b - a; h.Bloque = g.Label ?? ""; }
            }
            List<string> marcas = new List<string>();
            foreach (Marker mk in p.Markers)
            {
                if (mk is Region) continue;
                double x = S(mk.Position);
                if (x >= l.Inicio && x < l.Fin && !String.IsNullOrEmpty(mk.Label)) marcas.Add(mk.Label);
            }
            h.Marcas = String.Join(" | ", marcas.ToArray());
            foreach (TrackEvent e in musica)
            {
                if (Math.Abs(S(e.End) - l.Inicio) < 1.0) h.Antes = Path.GetFileNameWithoutExtension(Archivo(e));
                if (Math.Abs(S(e.Start) - l.Fin) < 1.0) h.Despues = Path.GetFileNameWithoutExtension(Archivo(e));
            }
            r.Add(h);
        }
        return r;
    }

    // Lo que ya suena en el capitulo (para no repetirlo).
    public static List<string> YaSuena(Project p)
    {
        List<string> r = new List<string>();
        foreach (Track x in PistasMusica(p))
            foreach (TrackEvent e in x.Events)
            {
                string n = Path.GetFileNameWithoutExtension(Archivo(e));
                if (n.Length > 0 && !r.Contains(n)) r.Add(n);
            }
        return r;
    }

    public static string Instrucciones()
    {
        return "Eres el supervisor musical de una serie de YouTube de Minecraft editada como un anime de JoJo. El capítulo ya está " +
               "editado y su pista de música tiene HUECOS. Para cada hueco elige un tema de la biblioteca según lo que pasa ahí (lo " +
               "que se dice), el bloque del capítulo y lo que suena antes y después.\n" +
               "- Que siga el ánimo de la escena: calma o viaje en exploración y charla, comedia en chistes, misterio y tensión " +
               "cuando algo no cuadra, pelea en la acción, épico o victoria en los logros, tristeza en lo emotivo.\n" +
               "- El tema de un personaje cuando ese personaje se luce (\"personaje\": nombre).\n" +
               "- No repitas lo que ya suena en el capítulo ni el mismo tema en dos huecos; que no sea igual al de antes o después.\n" +
               "- VARÍA: no elijas siempre los más obvios ni los del principio de la lista. Entre varios temas que sirven, prefiere los " +
               "que dicen menos \"usado N veces\" y alterna álbumes y fuentes; una elección menos esperada pero que le quede a la escena " +
               "vale más que la de siempre.\n" +
               "- Si un hueco queda mejor SIN música (un silencio dramático, el remate de un chiste, un momento muy hablado y " +
               "corto), pon \"silencio\": true.\n- Solo ids de la lista.\n" +
               "Responde SOLO con JSON: {\"huecos\": [{\"n\": n, \"id\": n, \"personaje\": \"\", \"silencio\": false, \"motivo\": \"...\"}]}";
    }

    public static string Mensaje(List<HuecoMusica> huecos, List<ArchivoMusica> candidatos, MusicaSerie m, List<string> yaSuena)
    {
        return Mensaje(huecos, candidatos, m, yaSuena, null);
    }

    public static string Mensaje(List<HuecoMusica> huecos, List<ArchivoMusica> candidatos, MusicaSerie m, List<string> yaSuena,
                                 Dictionary<string, int> usos)
    {
        StringBuilder sb = new StringBuilder();
        if (m != null)
        {
            if (!String.IsNullOrEmpty(m.Reparto)) sb.Append("REPARTO:\n" + m.Reparto.Trim() + "\n");
            foreach (KeyValuePair<string, TemaAsignado> kv in m.Personajes)
                sb.Append("Tema de " + kv.Key + ": " + Path.GetFileNameWithoutExtension(kv.Value.Archivo) + "\n");
        }
        if (yaSuena.Count > 0) sb.Append("\nYA SUENA EN EL CAPÍTULO: " + String.Join("; ", yaSuena.ToArray()) + "\n");
        sb.Append("\nHUECOS:\n");
        foreach (HuecoMusica h in huecos)
            sb.Append("[" + h.N + "] " + F(h.Inicio) + "–" + F(h.Fin) + " s (" + Math.Round(h.Duracion) + " s)" +
                      (h.Bloque.Length > 0 ? " · " + h.Bloque : "") + (h.Antes.Length > 0 ? " · antes suena: " + h.Antes : "") +
                      (h.Despues.Length > 0 ? " · después: " + h.Despues : "") + (h.Marcas.Length > 0 ? "\n    marcas: " + h.Marcas : "") + "\n    " + (h.Dicho.Length > 0 ? h.Dicho : "(nadie habla)") + "\n");
        sb.Append("\nBIBLIOTECA [id] título (de dónde) | ánimo | dónde suena / cómo suena | duración\n");
        for (int i = 0; i < candidatos.Count; i++)
        {
            ArchivoMusica a = candidatos[i];
            sb.Append("[" + i + "] " + a.Titulo + " (" + (a.Parte.Length > 0 ? a.Parte : a.Fuente) + ") | " + String.Join(", ", a.Animos.ToArray()) +
                      (a.Momento.Length > 0 ? " | " + a.Momento : "") + (!a.ConUso && a.Descripcion.Length > 0 ? " | " + a.Descripcion : "") +
                      (a.Duracion > 0 ? " | " + Math.Round(a.Duracion) + " s" : "") + UsadoTexto(a, usos) + "\n");
        }
        return sb.ToString();
    }

    static string UsadoTexto(ArchivoMusica a, Dictionary<string, int> usos)
    {
        int n;
        return usos != null && usos.TryGetValue(a.Ruta.ToLowerInvariant(), out n) && n > 0 ? " | usado " + n + (n == 1 ? " vez" : " veces") + " en la serie" : "";
    }

    // Pone en cada hueco el tema de la respuesta (sin repetir); devuelve cuantos.
    public static int Leer(string json, List<HuecoMusica> huecos, List<ArchivoMusica> candidatos, MusicaSerie m)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        Dictionary<int, bool> usados = new Dictionary<int, bool>();
        int n = 0;
        foreach (object x in Json.Lista(o, "huecos"))
        {
            int k = (int)Json.Numero(x, "n", -1);
            HuecoMusica h = huecos.Find(delegate (HuecoMusica y) { return y.N == k; });
            if (h == null) continue;
            h.Tema = -1; h.Archivo = null; h.Personaje = ""; h.Silencio = false;
            h.Motivo = Json.Texto(x, "motivo");
            object si = Json.Valor(x, "silencio");
            if (si is bool && (bool)si) { h.Silencio = true; h.Elegido = false; n++; continue; }
            string pj = Json.Texto(x, "personaje");
            if (pj.Length > 0 && m != null && m.Personajes.ContainsKey(pj)) { h.Personaje = pj; n++; continue; }
            int id = (int)Json.Numero(x, "id", -1);
            if (id < 0 || id >= candidatos.Count || usados.ContainsKey(id)) continue;
            usados[id] = true;
            h.Tema = id;
            h.Archivo = candidatos[id];
            n++;
        }
        return n;
    }

    public static string Ruta(HuecoMusica h, List<ArchivoMusica> candidatos, MusicaSerie m, BibliotecaMusica b)
    {
        string ruta = null;
        if (h.Personaje.Length > 0 && m != null && m.Personajes.ContainsKey(h.Personaje)) ruta = m.Personajes[h.Personaje].Archivo;
        else if (h.Archivo != null) ruta = h.Archivo.Ruta;
        else if (h.Tema >= 0 && h.Tema < candidatos.Count) ruta = candidatos[h.Tema].Ruta;
        if (ruta != null && !Path.IsPathRooted(ruta) && b != null) ruta = b.Completa(ruta);
        return ruta;
    }

    public static string Nombre(HuecoMusica h, List<ArchivoMusica> candidatos)
    {
        if (h.Silencio) return "(mejor en silencio)";
        if (h.Personaje.Length > 0) return "Tema de " + h.Personaje;
        if (h.Archivo != null) return h.Archivo.Titulo;
        if (h.Tema >= 0 && h.Tema < candidatos.Count) return candidatos[h.Tema].Titulo;
        return "";
    }

    // Coloca los temas elegidos en la pista de musica (sin tocar lo que ya hay).
    public static int Colocar(Project p, List<HuecoMusica> huecos, List<ArchivoMusica> candidatos, MusicaSerie m, BibliotecaMusica b,
                              List<string> avisos)
    {
        AudioTrack pista = null;
        foreach (Track x in p.Tracks) if (x.IsAudio() && x.Name == PistaMusica) { pista = (AudioTrack)x; break; }
        if (pista == null)
        {
            pista = new AudioTrack(p.Tracks.Count, PistaMusica);
            p.Tracks.Add(pista);
            pista.Volume = MusicaSerie.Lineal(m != null ? m.VolumenDb : -21);
        }
        int n = 0;
        foreach (HuecoMusica h in huecos)
        {
            if (!h.Elegido || h.Silencio) continue;
            string ruta = Ruta(h, candidatos, m, b);
            if (ruta == null) continue;
            if (!File.Exists(ruta)) { avisos.Add("No encontré " + Path.GetFileName(ruta) + "."); continue; }
            try
            {
                Media md = new Media(ruta);
                MediaStream s = md.Streams.GetItemByMediaType(MediaType.Audio, 0);
                double largo = Math.Min(h.Duracion, S(md.Length) > 1 ? S(md.Length) : h.Duracion);
                AudioEvent e = pista.AddAudioEvent(TC(h.Inicio), TC(largo));
                e.AddTake(s);
                e.FadeIn.Length = TC(Math.Min(1.0, largo / 4));
                e.FadeOut.Length = TC(Math.Min(2.0, largo / 4));
                n++;
            }
            catch (Exception ex) { avisos.Add("Hueco " + h.N + ": " + ex.Message); }
        }
        return n;
    }
}
