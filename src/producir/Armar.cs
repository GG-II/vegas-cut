using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// =====================================================================
// Producir: arma el capitulo en la linea de tiempo segun la propuesta final.
// Trabaja sobre una copia "<capitulo> CAP.veg" (la copia base no se toca):
// copia cada clip del material (todas las pistas, agrupados) en su lugar,
// pone el kit de la serie (o placeholders), titulo y carteles, la musica
// (sin balancear: eso es el paso final), la narracion provisional y los
// placeholders de recursos; despues quita el material y deja el capitulo al
// inicio. Un episodio doble queda como PARTE 1 y PARTE 2 en el mismo proyecto.
// =====================================================================

public class ResultadoProduccion
{
    public int Clips, Kit, Placeholders, Textos, Temas, Narraciones, Recursos, SinLugar, NarracionSinHueco, Respiros;
    public List<double> Partes = new List<double>();
    public List<string> Avisos = new List<string>();
    public StringBuilder Guion = new StringBuilder();

    public string Texto()
    {
        List<string> l = new List<string>();
        for (int i = 0; i < Partes.Count; i++) l.Add((Partes.Count > 1 ? "parte " + (i + 1) + ": " : "") + Formato.Tiempo(Partes[i]));
        return "✔ Capítulo armado (" + String.Join(", ", l.ToArray()) + "): " + Clips + " clips, " + Kit + " del kit" +
               (Placeholders > 0 ? " (" + Placeholders + " placeholders)" : "") + ", " + Textos + " textos, " + Temas + " temas, " +
               Narraciones + " frases de narración, " + Recursos + " recursos" + (Respiros > 0 ? ", " + Respiros + " respiros" : "") + "." +
               (SinLugar > 0 ? " " + SinLugar + " elementos quedaron fuera porque su momento no entró en ningún clip." : "") +
               (NarracionSinHueco > 0 ? " " + NarracionSinHueco + " frases de narración no cabían sin pisar voces (están en el guion)." : "");
    }
}

public static class ArmarCapitulo
{
    public const string PistaKit = "vegas-cut · Kit", PistaKitAudio = "vegas-cut · Kit (audio)", PistaTextos = "vegas-cut · Textos",
                        PistaMusica = "vegas-cut · Música";
    public const string Sufijo = " CAP";
    static readonly string[] Imagenes = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    public static string RutaPara(string veg)
    {
        string o = CopiaBase.Original(veg);
        return Path.Combine(Path.GetDirectoryName(o), Path.GetFileNameWithoutExtension(o) + Sufijo + ".veg");
    }

    class Tramo { public int Parte; public double A, B, Nuevo; }

    // Alarga lo que termina justo en "fin" (todas las pistas): como al material
    // se le quitaron los silencios, el archivo sigue con la pausa original.
    static void Respirar(Project p, double fin, double segundos)
    {
        foreach (Track t in p.Tracks)
            foreach (TrackEvent e in t.Events)
                if (Math.Abs(S(e.End) - fin) < 0.02 && S(e.Start) < fin) e.Length = TC(S(e.Length) + segundos);
    }

    // Primer hueco libre (sin voces ni otra narracion) de "largo" segundos
    // cerca de "desde": hasta 25 s despues o 5 s antes. NaN si no hay.
    public static double Hueco(List<Rango> ocupado, double desde, double largo, double min, double max)
    {
        List<Rango> o = Rangos.Unir(ocupado, 0.15);
        List<double> c = new List<double>();
        c.Add(desde);
        foreach (Rango x in o) if (x.Fin >= desde - 5 && x.Fin <= desde + 25) c.Add(x.Fin + 0.15);
        c.Sort(delegate (double a, double b) { return Math.Abs(a - desde).CompareTo(Math.Abs(b - desde)); });
        foreach (double t in c)
        {
            if (t < min || t + largo > max) continue;
            bool libre = true;
            foreach (Rango x in o) if (x.Inicio < t + largo + 0.1 && x.Fin > t - 0.1) { libre = false; break; }
            if (libre) return t;
        }
        return double.NaN;
    }

    // Donde queda en el capitulo un segundo del material (NaN si no entro).
    static double Ubicar(List<Tramo> tramos, int parte, double t)
    {
        foreach (Tramo x in tramos)
            if (x.Parte == parte && t >= x.A - 0.25 && t <= x.B + 0.25) return x.Nuevo + Math.Max(0, Math.Min(x.B - x.A, t - x.A));
        return double.NaN;
    }

    static VideoTrack Video(Project p, string nombre)
    {
        foreach (Track t in p.Tracks) if (!t.IsAudio() && t.Name == nombre) return (VideoTrack)t;
        VideoTrack v = new VideoTrack(0, nombre);
        p.Tracks.Add(v);
        return v;
    }

    static AudioTrack Audio(Project p, string nombre)
    {
        foreach (Track t in p.Tracks) if (t.IsAudio() && t.Name == nombre) return (AudioTrack)t;
        AudioTrack a = new AudioTrack(p.Tracks.Count, nombre);
        p.Tracks.Add(a);
        return a;
    }

    // Un archivo del kit (video con su audio, o imagen). Devuelve cuanto dura.
    static double PonerArchivo(Project p, string archivo, double en, double porDefecto)
    {
        Media m = new Media(archivo);
        MediaStream v = m.Streams.GetItemByMediaType(MediaType.Video, 0), a = m.Streams.GetItemByMediaType(MediaType.Audio, 0);
        bool imagen = Array.IndexOf(Imagenes, Path.GetExtension(archivo).ToLowerInvariant()) >= 0;
        double largo = imagen ? porDefecto : S(m.Length);
        if (largo <= 0.1) largo = porDefecto;
        TrackEventGroup g = v != null && a != null ? Editor.NuevoGrupo(p) : null;
        if (v != null)
        {
            VideoEvent e = Video(p, PistaKit).AddVideoEvent(TC(en), TC(largo));
            e.AddTake(v);
            if (g != null) g.Add(e);
        }
        if (a != null)
        {
            AudioEvent e = Audio(p, PistaKitAudio).AddAudioEvent(TC(en), TC(largo));
            e.AddTake(a);
            if (g != null) g.Add(e);
        }
        return largo;
    }

    static void Texto(Vegas vegas, Plantilla pt, string pista, double en, double largo, string texto)
    {
        GeneradorTexto.Crear(Video(vegas.Project, pista), pt, en, largo, texto);
    }

    public static ResultadoProduccion Producir(Vegas vegas, PlanFinal final, FormatoSerie formato, MusicaSerie musicaSerie,
                                               BibliotecaMusica biblioteca, List<ArchivoMusica> candidatos, VideoEvent plantillaRecursos,
                                               ISintetizador voz, int ppm, Transcripcion trans, Action<string, double> estado)
    {
        // Donde habla alguien en el material (para no narrar encima).
        List<Rango> voces = new List<Rango>();
        if (trans != null)
            foreach (Segmento sg in trans.SegmentosActuales())
                if (sg.Fin > sg.Inicio) voces.Add(new Rango(sg.Inicio, sg.Fin));
        voces = Rangos.Unir(voces, 0.2);
        Project p = vegas.Project;
        ResultadoProduccion r = new ResultadoProduccion();
        PlantillaTV tv = formato.Tv;

        // Lo que habia (el material), para quitarlo al final.
        List<TrackEvent> material = new List<TrackEvent>();
        double finMaterial = 0;
        foreach (Track t in p.Tracks) foreach (TrackEvent e in t.Events) { material.Add(e); finMaterial = Math.Max(finMaterial, S(e.End)); }
        List<Marker> marcas = new List<Marker>();
        foreach (Marker m in p.Markers) marcas.Add(m);
        List<Region> regiones = new List<Region>();
        foreach (Region x in p.Regions) regiones.Add(x);
        double O = Math.Ceiling(finMaterial) + 60;   // el capitulo se arma despues del material

        Plantilla estiloTexto = GeneradorTexto.Buscar(vegas), etiqueta = GeneradorTexto.PorDefecto(vegas);
        List<Tramo> tramos = new List<Tramo>();
        List<Rango> kitRangos = new List<Rango>();
        double inicioParte = O;
        int nRec = 0;

        for (int k = 0; k < final.Partes.Count; k++)
        {
            CapituloFinal c = final.Partes[k];
            string pre = final.Partes.Count > 1 ? "P" + (k + 1) + " · " : "";
            double cursor = inicioParte;
            string stats = "";
            foreach (ItemFinal i in c.Items) if (i.Tipo == "texto" && i.Clase == "stats" && i.Elegido) stats = i.Texto;

            foreach (BloqueTV b in c.Estructura)
            {
                double ini = cursor;
                estado("Parte " + (k + 1) + " · " + b.Nombre + "…", 0.05 + 0.5 * k / final.Partes.Count);
                if (b.Tipo == "contenido")
                {
                    foreach (ItemFinal i in c.Items)
                    {
                        if (i.Tipo != "clip" || !i.Elegido || i.Bloque != b.Clave) continue;
                        r.Clips += AplicarPlan.CopiarTramo(p, i.Inicio, i.Fin, cursor) > 0 ? 1 : 0;
                        tramos.Add(new Tramo { Parte = k, A = i.Inicio, B = i.Fin, Nuevo = cursor });
                        cursor += i.Duracion;
                        if (i.Respiro > 0.05) { Respirar(p, cursor, i.Respiro); cursor += i.Respiro; r.Respiros++; }
                    }
                }
                else if (b.Tipo == "kit")
                {
                    string archivo = tv.Archivo(b.ClaveKit);
                    if (archivo.Length > 0 && File.Exists(archivo))
                    {
                        try { cursor += PonerArchivo(p, archivo, cursor, b.Segundos); }
                        catch (Exception ex) { r.Avisos.Add(b.Nombre + ": " + ex.Message); cursor += b.Segundos; }
                    }
                    else
                    {
                        string txt = "[" + b.Nombre.ToUpperInvariant() + "]" + (b.ClaveKit == "regancho" && stats.Length > 0 ? "\n" + stats : "");
                        try { Texto(vegas, etiqueta, PistaKit, cursor, b.Segundos, txt); r.Placeholders++; }
                        catch (Exception ex) { r.Avisos.Add(b.Nombre + ": " + ex.Message); }
                        cursor += b.Segundos;
                    }
                    r.Kit++;
                    kitRangos.Add(new Rango(ini, cursor));
                }
                else
                {
                    string titulo = (c.Etapa.Length > 0 ? c.Etapa + " · " : "") + c.Titulo;
                    try { Texto(vegas, estiloTexto, PistaTextos, cursor, b.Segundos, titulo); r.Textos++; }
                    catch (Exception ex) { r.Avisos.Add("Título: " + ex.Message); }
                    cursor += b.Segundos;
                    kitRangos.Add(new Rango(ini, cursor));
                }
                if (cursor > ini) p.Regions.Add(new Region(TC(ini), TC(cursor - ini), pre + b.Nombre.ToUpperInvariant()));
            }
            p.Regions.Add(new Region(TC(inicioParte), TC(cursor - inicioParte), (final.Partes.Count > 1 ? "PARTE " + (k + 1) + " · " : "") + c.Titulo));
            r.Partes.Add(cursor - inicioParte);
            double finParte = cursor;

            // Textos anclados al material (lugar, tiempo, ranking).
            foreach (ItemFinal i in c.Items)
            {
                if (i.Tipo != "texto" || !i.Elegido || i.Clase == "titulo" || i.Clase == "stats" || i.Clase == "continuara") continue;
                double en = Ubicar(tramos, k, i.Inicio);
                if (double.IsNaN(en)) { r.SinLugar++; continue; }
                try { Texto(vegas, estiloTexto, PistaTextos, en, i.Clase == "presentacion" ? 4 : 3, i.Texto); r.Textos++; }
                catch (Exception ex) { r.Avisos.Add("Texto: " + ex.Message); }
            }

            // Recursos: placeholders (copias de la plantilla si la hay).
            foreach (ItemFinal i in c.Items)
            {
                if (i.Tipo != "recurso" || !i.Elegido) continue;
                double en = Ubicar(tramos, k, i.Inicio);
                if (double.IsNaN(en)) { r.SinLugar++; continue; }
                string id = "R" + (++nRec).ToString("00");
                string texto = "[" + id + "] " + (i.Clase.Length > 0 ? i.Clase.ToUpperInvariant() + ": " : "") + i.Texto;
                try
                {
                    VideoTrack pista = Video(p, AplicarPlan.PistaPlaceholders);
                    if (plantillaRecursos != null)
                    {
                        TrackEvent copia = plantillaRecursos.Copy(pista, TC(en));
                        copia.Length = TC(i.Duracion);
                        copia.AddTake(GeneradorTexto.Medio(etiqueta, texto).Streams.GetItemByMediaType(MediaType.Video, 0), true);
                    }
                    else GeneradorTexto.Crear(pista, etiqueta, en, i.Duracion, texto);
                    r.Recursos++;
                    r.Guion.Append(id + "  [" + Formato.Tiempo(en - O) + "] " + i.Clase + ": " + i.Texto + "\n");
                }
                catch (Exception ex) { r.Avisos.Add(id + ": " + ex.Message); }
            }

            // Musica: cada tema desde su momento hasta el siguiente (sin pisar el kit).
            List<KeyValuePair<double, string>> temas = new List<KeyValuePair<double, string>>();
            foreach (ItemFinal i in c.Items)
            {
                if (i.Tipo != "musica" || !i.Elegido) continue;
                double en = i.Bloque.Length > 0 && c.Bloque(i.Bloque) != null && c.Bloque(i.Bloque).Tipo != "contenido" ? double.NaN : Ubicar(tramos, k, i.Inicio);
                if (double.IsNaN(en)) { r.SinLugar++; continue; }
                string ruta = null;
                if (i.Personaje.Length > 0 && musicaSerie != null && musicaSerie.Personajes.ContainsKey(i.Personaje)) ruta = musicaSerie.Personajes[i.Personaje].Archivo;
                else if (i.Musica >= 0 && i.Musica < candidatos.Count) ruta = candidatos[i.Musica].Ruta;
                if (ruta == null) continue;
                if (!Path.IsPathRooted(ruta) && biblioteca != null) ruta = biblioteca.Completa(ruta);
                temas.Add(new KeyValuePair<double, string>(en, ruta));
            }
            temas.Sort(delegate (KeyValuePair<double, string> a, KeyValuePair<double, string> b) { return a.Key.CompareTo(b.Key); });
            for (int j = 0; j < temas.Count; j++)
            {
                double a = temas[j].Key, b = j + 1 < temas.Count ? temas[j + 1].Key : finParte;
                foreach (Rango kr in kitRangos) if (kr.Inicio > a + 0.5 && kr.Inicio < b) b = kr.Inicio;
                if (b - a < 2) continue;
                if (!File.Exists(temas[j].Value)) { r.Avisos.Add("No encontré " + Path.GetFileName(temas[j].Value) + "."); continue; }
                try
                {
                    Media m = new Media(temas[j].Value);
                    MediaStream s = m.Streams.GetItemByMediaType(MediaType.Audio, 0);
                    double largo = Math.Min(b - a, S(m.Length) > 1 ? S(m.Length) : b - a);
                    AudioTrack pm = Audio(p, PistaMusica);
                    pm.Volume = MusicaSerie.Lineal(musicaSerie != null ? musicaSerie.VolumenDb : -21);
                    AudioEvent e = pm.AddAudioEvent(TC(a), TC(largo));
                    e.AddTake(s);
                    e.FadeIn.Length = TC(Math.Min(0.8, largo / 4));
                    e.FadeOut.Length = TC(Math.Min(1.5, largo / 4));
                    r.Temas++;
                }
                catch (Exception ex) { r.Avisos.Add("Música: " + ex.Message); }
            }

            // Narracion provisional (sin bajar el juego: eso es el paso final).
            List<ItemFinal> narr = c.Items.FindAll(delegate (ItemFinal i) { return i.Tipo == "narracion" && i.Elegido; });
            if (narr.Count > 0 && voz == null && k == 0) r.Avisos.Add("No encontré la voz de Windows: la narración quedó solo en el guion.");
            string carpeta = AplicarPlan.CarpetaNarracion(RutaPara(p.FilePath));
            int vel = 2;
            double ultimo = -1;
            // Voces de esta parte ya en la linea de tiempo nueva.
            List<Rango> ocupado = new List<Rango>();
            foreach (Tramo x in tramos)
            {
                if (x.Parte != k) continue;
                foreach (Rango v in voces)
                {
                    double a = Math.Max(v.Inicio, x.A), b = Math.Min(v.Fin, x.B);
                    if (b > a) ocupado.Add(new Rango(x.Nuevo + a - x.A, x.Nuevo + b - x.A));
                }
            }
            foreach (Rango kr in kitRangos) ocupado.Add(kr);          // tampoco sobre el opening, el ending...
            for (int j = 0; j < narr.Count; j++)
            {
                ItemFinal i = narr[j];
                double ancla = Ubicar(tramos, k, i.Inicio);
                if (double.IsNaN(ancla)) { r.SinLugar++; continue; }
                double largo = LogicaPlan.Segundos(i.Texto, ppm);
                double en = Hueco(ocupado, Math.Max(ancla, ultimo + 0.2), largo, inicioParte, finParte);
                if (double.IsNaN(en))
                {
                    r.NarracionSinHueco++;
                    r.Avisos.Add(i.Id + " no cabe sin pisar voces cerca de su momento: quedó solo en el guion.");
                    r.Guion.Append(i.Id + "  [sin lugar]\n    " + i.Texto + "\n\n");
                    continue;
                }
                r.Guion.Append(i.Id + "  [" + Formato.TiempoPreciso(en - O) + "]\n    " + i.Texto + "\n\n");
                if (voz == null) continue;
                estado("Narración " + i.Id + "…", 0.6 + 0.3 * j / narr.Count);
                try
                {
                    Directory.CreateDirectory(carpeta);
                    string wav = Path.Combine(carpeta, i.Id + ".wav");
                    double d = VozProvisional.Generar(voz, i.Texto, LogicaPlan.Segundos(i.Texto, ppm), wav, ref vel);
                    AudioEvent e = Audio(p, RitmoVegas.PistaNarracion).AddAudioEvent(TC(en), TC(d));
                    e.AddTake(new Media(wav).Streams.GetItemByMediaType(MediaType.Audio, 0));
                    ultimo = en + d;
                    ocupado.Add(new Rango(en, en + d));
                    r.Narraciones++;
                }
                catch (Exception ex) { r.Avisos.Add(i.Id + ": " + ex.Message); }
            }
            inicioParte = finParte + 10;
        }

        // Fuera el material; el capitulo pasa al inicio.
        estado("Quitando el material y dejando el capítulo al inicio…", 0.95);
        foreach (TrackEvent e in material) try { e.Track.Events.Remove(e); } catch { }
        foreach (Region x in regiones) try { p.Regions.Remove(x); } catch { }
        foreach (Marker m in marcas) try { p.Markers.Remove(m); } catch { }
        List<TrackEvent> nuevos = new List<TrackEvent>();
        foreach (Track t in p.Tracks) foreach (TrackEvent e in t.Events) nuevos.Add(e);
        nuevos.Sort(delegate (TrackEvent a, TrackEvent b) { return a.Start.ToMilliseconds().CompareTo(b.Start.ToMilliseconds()); });
        foreach (TrackEvent e in nuevos) e.Start = TC(Math.Max(0, S(e.Start) - O));
        foreach (Region x in p.Regions) try { x.Position = TC(Math.Max(0, S(x.Position) - O)); } catch { }
        return r;
    }

    // Guarda la copia CAP, la arma y la vuelve a guardar. La copia base queda como estaba.
    public static ResultadoProduccion EnCopia(Vegas vegas, PlanFinal final, FormatoSerie formato, MusicaSerie musicaSerie,
                                              BibliotecaMusica biblioteca, List<ArchivoMusica> candidatos, VideoEvent plantillaRecursos,
                                              ISintetizador voz, int ppm, Transcripcion trans, Action<string, double> estado)
    {
        string desde = vegas.Project.FilePath, cap = RutaPara(desde);
        vegas.SaveProject(cap);
        string t = Transcripcion.RutaPara(desde);
        if (File.Exists(t))
        {
            Transcripcion tr = Transcripcion.Cargar(t);
            tr.Proyecto = cap;
            tr.Guardar(Transcripcion.RutaPara(cap));
        }
        string dir = Path.GetDirectoryName(desde);
        string serie = Path.Combine(dir, Path.GetFileNameWithoutExtension(desde) + ".vegascut-proyecto-serie.json");
        if (File.Exists(serie)) File.Copy(serie, Path.Combine(dir, Path.GetFileNameWithoutExtension(cap) + ".vegascut-proyecto-serie.json"), true);
        ResultadoProduccion r;
        using (UndoBlock u = new UndoBlock("Producir capítulo"))
            r = Producir(vegas, final, formato, musicaSerie, biblioteca, candidatos, plantillaRecursos, voz, ppm, trans, estado);
        StringBuilder g = new StringBuilder();
        g.Append("GUION · " + Path.GetFileNameWithoutExtension(cap) + "\n");
        foreach (CapituloFinal c in final.Partes) g.Append((c.Etapa.Length > 0 ? c.Etapa + " · " : "") + c.Titulo + "\n");
        g.Append("Lee de corrido, con una pausa corta entre frases. Si una sale mal, repítela entera.\n\n");
        g.Append(r.Guion);
        File.WriteAllText(Path.Combine(dir, Path.GetFileNameWithoutExtension(cap) + ".vegascut-guion.txt"), g.ToString(), new UTF8Encoding(true));
        vegas.SaveProject(cap);
        return r;
    }
}
