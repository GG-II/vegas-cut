using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// =====================================================================
// Pone el plan en la linea de tiempo: gancho al inicio, regiones de
// secciones y recortes, avances ("Dia 3"), placeholders de recursos y la
// narracion provisional con el juego bajado mientras suena.
// =====================================================================

public class OpcionesPlan
{
    public bool Gancho = true, Regiones = true, Avances = true, Placeholders = true, Narracion = true, Bajar = true;
    public int BajaDb = -10;
}

public class ResultadoPlan
{
    public int Narraciones, Avances, Placeholders, Regiones, Copiados;
    public double Corrimiento, SegundosNarracion;
    public List<string> Avisos = new List<string>();
    public Dictionary<string, object> Aplicado = new Dictionary<string, object>();

    public string Texto()
    {
        List<string> l = new List<string>();
        if (Corrimiento > 0) l.Add("gancho de " + Formato.Tiempo(Corrimiento) + " al inicio");
        if (Narraciones > 0) l.Add(Narraciones + " frases de narración (" + Formato.Tiempo(SegundosNarracion) + ")");
        if (Avances > 0) l.Add(Avances + " avances");
        if (Placeholders > 0) l.Add(Placeholders + " placeholders");
        if (Regiones > 0) l.Add(Regiones + " regiones");
        return l.Count == 0 ? "No se aplicó nada." : "✔ " + String.Join(", ", l.ToArray()) + ".";
    }
}

public static class AplicarPlan
{
    public const string PistaNarracion = RitmoVegas.PistaNarracion;
    public const string PistaAvances = "vegas-cut · Avances";
    public const string PistaPlaceholders = "vegas-cut · Placeholders";
    static readonly string[] Prefijos = { "GANCHO", "SECCIÓN · ", "RECORTAR · ", "ACELERAR · " };

    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    public static ResultadoPlan Aplicar(Vegas vegas, Plan plan, OpcionesPlan op, ISintetizador voz, int ppm,
                                        Transcripcion t, VideoEvent plantilla, Action<string, double> estado)
    {
        return Aplicar(vegas, plan, op, voz, ppm, t, plantilla, "Narrador", estado);
    }

    public static ResultadoPlan Aplicar(Vegas vegas, Plan plan, OpcionesPlan op, ISintetizador voz, int ppm,
                                        Transcripcion t, VideoEvent plantilla, string narrador, Action<string, double> estado)
    {
        Project p = vegas.Project;
        ResultadoPlan r = new ResultadoPlan();
        double corr = LogicaPlan.Corrimiento(plan, op.Gancho);

        // 1. Gancho: se corre todo y se copia ese momento al inicio.
        if (corr > 0)
        {
            estado("Poniendo el gancho al inicio…", 0.02);
            ItemPlan g = plan.Gancho;
            Editor.Desplazar(p, corr);
            r.Copiados = CopiarTramo(p, g.Inicio + corr, g.Fin + corr, 0);
            r.Corrimiento = corr;
        }

        // 2. Regiones (se quitan las de un plan anterior).
        if (op.Regiones)
        {
            QuitarRegiones(p);
            if (corr > 0) { p.Regions.Add(new Region(TC(0), TC(corr - 0.5), "GANCHO")); r.Regiones++; }
            foreach (ItemPlan i in plan.Items)
            {
                if (!i.Elegido) continue;
                string etiqueta = i.Tipo == "seccion" ? "SECCIÓN · " + i.Texto
                                : i.Tipo == "recorte" ? (i.Clase == "acelerar" ? "ACELERAR · " : "RECORTAR · ") + i.Texto : null;
                if (etiqueta == null) continue;
                p.Regions.Add(new Region(TC(i.Inicio + corr), TC(i.Duracion), etiqueta));
                r.Regiones++;
            }
        }

        // 3. Avances en pantalla con el estilo del texto que ya uses.
        if (op.Avances)
        {
            List<ItemPlan> avs = Elegidos(plan, "avance");
            if (avs.Count > 0)
            {
                Plantilla pt = GeneradorTexto.Buscar(vegas);
                VideoTrack pista = PistaVideo(p, PistaAvances);
                foreach (ItemPlan i in avs)
                {
                    try { GeneradorTexto.Crear(pista, pt, i.Inicio + corr, 3, i.Texto); r.Avances++; }
                    catch (Exception ex) { r.Avisos.Add("Avance «" + i.Texto + "»: " + ex.Message); break; }
                }
            }
        }

        // 4. Placeholders: copia del evento plantilla (efectos, movimiento y
        // fundidos) con un texto que dice que va ahi.
        List<object> phs = new List<object>();
        if (op.Placeholders)
        {
            List<ItemPlan> recs = Elegidos(plan, "recurso");
            if (recs.Count > 0)
            {
                Plantilla etiqueta = GeneradorTexto.PorDefecto(vegas);
                VideoTrack pista = PistaVideo(p, PistaPlaceholders);
                foreach (ItemPlan i in recs)
                {
                    string texto = "[" + i.Id + "] " + (i.Clase.Length > 0 ? i.Clase.ToUpperInvariant() + ": " : "") + i.Texto;
                    try
                    {
                        if (plantilla != null)
                        {
                            TrackEvent copia = plantilla.Copy(pista, TC(i.Inicio + corr));
                            copia.Length = TC(i.Duracion);
                            Media m = GeneradorTexto.Medio(etiqueta, texto);
                            copia.AddTake(m.Streams.GetItemByMediaType(MediaType.Video, 0), true);
                        }
                        else
                            GeneradorTexto.Crear(pista, etiqueta, i.Inicio + corr, i.Duracion, texto);
                        r.Placeholders++;
                        Dictionary<string, object> d = new Dictionary<string, object>();
                        d["id"] = i.Id; d["inicio"] = Math.Round(i.Inicio + corr, 3); d["clase"] = i.Clase;
                        d["descripcion"] = i.Texto; d["archivo"] = i.Detalle;
                        phs.Add(d);
                    }
                    catch (Exception ex) { r.Avisos.Add("Placeholder " + i.Id + ": " + ex.Message); break; }
                }
            }
        }
        r.Aplicado["placeholders"] = phs;

        // 5. Narracion provisional y el juego mas bajo mientras suena.
        List<object> lineas = new List<object>();
        if (op.Narracion)
        {
            List<ItemPlan> narr = Elegidos(plan, "narracion");
            if (narr.Count > 0 && voz == null)
                r.Avisos.Add("No encontré la voz de Windows (System.Speech): la narración quedó solo en el guion.");
            else if (narr.Count > 0)
            {
                string carpeta = CarpetaNarracion(p.FilePath);
                Directory.CreateDirectory(carpeta);
                AudioTrack pista = PistaAudio(p, PistaNarracion);
                List<Rango> suena = new List<Rango>();
                int velocidad = 2;
                for (int k = 0; k < narr.Count; k++)
                {
                    ItemPlan i = narr[k];
                    estado("Narración " + (k + 1) + " de " + narr.Count + " (" + voz.Nombre + ")…", 0.1 + 0.8 * k / narr.Count);
                    string wav = Path.Combine(carpeta, i.Id + ".wav");
                    double d;
                    try { d = VozProvisional.Generar(voz, i.Texto, LogicaPlan.Segundos(i.Texto, ppm), wav, ref velocidad); }
                    catch (Exception ex) { r.Avisos.Add(i.Id + ": " + ex.Message); continue; }
                    double a = i.Inicio + corr;
                    Media m = new Media(wav);
                    AudioEvent ev = pista.AddAudioEvent(TC(a), TC(d));
                    ev.AddTake(m.Streams.GetItemByMediaType(MediaType.Audio, 0));
                    suena.Add(new Rango(a, a + d));
                    r.Narraciones++;
                    r.SegundosNarracion += d;
                    Dictionary<string, object> x = new Dictionary<string, object>();
                    x["id"] = i.Id; x["texto"] = i.Texto; x["inicio"] = Math.Round(a, 3); x["fin"] = Math.Round(a + d, 3);
                    lineas.Add(x);
                }
                if (op.Bajar && suena.Count > 0)
                {
                    estado("Bajando el juego bajo la narración…", 0.95);
                    foreach (Track g in RitmoVegas.PistasGrabacion(p, t, narrador))
                        if (g != pista) Bajar(g, suena, op.BajaDb);
                }
            }
        }
        r.Aplicado["narracion"] = lineas;
        r.Aplicado["corrimiento"] = Math.Round(corr, 3);
        r.Aplicado["fecha"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        return r;
    }

    static List<ItemPlan> Elegidos(Plan plan, string tipo)
    {
        List<ItemPlan> l = new List<ItemPlan>();
        foreach (ItemPlan i in plan.Items) if (i.Tipo == tipo && i.Elegido) l.Add(i);
        return l;
    }

    public static string CarpetaNarracion(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-narracion");
    }

    static VideoTrack PistaVideo(Project p, string nombre)
    {
        foreach (Track t in p.Tracks) if (!t.IsAudio() && t.Name == nombre) return (VideoTrack)t;
        VideoTrack v = new VideoTrack(0, nombre);   // arriba de todo, para que se vea encima
        p.Tracks.Add(v);
        return v;
    }

    static AudioTrack PistaAudio(Project p, string nombre)
    {
        foreach (Track t in p.Tracks) if (t.IsAudio() && t.Name == nombre) return (AudioTrack)t;
        AudioTrack a = new AudioTrack(p.Tracks.Count, nombre);
        p.Tracks.Add(a);
        return a;
    }

    public static int QuitarRegiones(Project p)
    {
        List<Region> viejas = new List<Region>();
        foreach (Region x in p.Regions)
            foreach (string pre in Prefijos)
                if ((x.Label ?? "").StartsWith(pre)) { viejas.Add(x); break; }
        foreach (Region x in viejas) p.Regions.Remove(x);
        return viejas.Count;
    }

    // Baja la pista mientras suena la narracion (frases muy juntas, una sola bajada).
    static void Bajar(Track pista, List<Rango> suena, int db)
    {
        foreach (Rango x in Rangos.Unir(suena, 1.2))
        {
            double a = Math.Max(0, x.Inicio - 0.4), b = x.Fin + 0.8;
            List<PuntoVolumen> pts = LogicaMusica.Puntos(new List<Rango> { x }, a, b, db, 0.25, 0.4);
            LogicaMusica.Aplicar(pista, pts, a, b);
        }
    }

    // Copia lo que hay entre a y b (todas las pistas) a "destino". Los
    // pedazos que estaban agrupados quedan agrupados entre si.
    public static int CopiarTramo(Project p, double a, double b, double destino)
    {
        List<TrackEvent> origen = new List<TrackEvent>();
        foreach (Track t in p.Tracks)
            foreach (TrackEvent e in t.Events)
                if (S(e.Start) < b - 0.001 && S(e.End) > a + 0.001) origen.Add(e);
        Dictionary<TrackEventGroup, TrackEventGroup> grupos = new Dictionary<TrackEventGroup, TrackEventGroup>();
        int n = 0;
        foreach (TrackEvent e in origen)
        {
            double ini = Math.Max(a, S(e.Start)), fin = Math.Min(b, S(e.End));
            double recorte = ini - S(e.Start);
            double offset = e.ActiveTake != null ? S(e.ActiveTake.Offset) : 0;
            TrackEvent c = e.Copy(e.Track, TC(destino + ini - a));
            c.Length = TC(fin - ini);
            if (c.ActiveTake != null && recorte > 0) c.ActiveTake.Offset = TC(offset + recorte * e.PlaybackRate);
            try { c.FadeIn.Length = TC(0); c.FadeOut.Length = TC(0); } catch { }
            n++;
            if (e.IsGrouped)
            {
                TrackEventGroup g;
                if (!grupos.TryGetValue(e.Group, out g)) { g = Editor.NuevoGrupo(p); grupos[e.Group] = g; }
                if (!c.IsGrouped) g.Add(c);
            }
        }
        return n;
    }

    // ------------------------------------------------- reemplazar placeholders

    // Archivo de la carpeta (o una subcarpeta) cuyo nombre empieza con el
    // codigo: "R03 cadaver de steve.png", "r03.jpg"...
    public static string Buscar(List<string> archivos, string id)
    {
        foreach (string f in archivos)
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (n.Length < id.Length || !n.StartsWith(id, StringComparison.OrdinalIgnoreCase)) continue;
            if (n.Length == id.Length || !char.IsDigit(n[id.Length])) return f;
        }
        return null;
    }

    static readonly string[] Extensiones = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".mp4", ".mov", ".mkv", ".webm", ".avi" };

    public static List<string> Archivos(string carpeta)
    {
        List<string> l = new List<string>();
        try
        {
            foreach (string f in Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories))
                if (Array.IndexOf(Extensiones, Path.GetExtension(f).ToLowerInvariant()) >= 0) l.Add(f);
        }
        catch { }
        l.Sort(StringComparer.OrdinalIgnoreCase);
        return l;
    }

    // Pone el archivo real como toma activa de cada placeholder que lo tenga
    // (conserva efectos, movimiento y fundidos). Devuelve los que faltan.
    public static int ReemplazarPlaceholders(Project p, string carpeta, List<string> faltan)
    {
        List<string> archivos = Archivos(carpeta);
        int n = 0;
        foreach (Track t in p.Tracks)
        {
            if (t.IsAudio() || t.Name != PistaPlaceholders) continue;
            foreach (TrackEvent e in t.Events)
            {
                string texto = GeneradorTexto.TextoDe(e);
                if (!texto.StartsWith("[R")) continue;
                int cierra = texto.IndexOf(']');
                if (cierra < 0) continue;
                string id = texto.Substring(1, cierra - 1);
                string f = Buscar(archivos, id);
                if (f == null) { faltan.Add(id); continue; }
                Media m = new Media(f);
                MediaStream s = m.Streams.GetItemByMediaType(MediaType.Video, 0);
                if (s == null) { faltan.Add(id + " (sin video)"); continue; }
                e.AddTake(s, true);
                n++;
            }
        }
        return n;
    }
}
