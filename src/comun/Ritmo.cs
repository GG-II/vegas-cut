using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

// =====================================================================
// Medidor de ritmo: minuto a minuto, cuanto narra el narrador, cuantos
// cortes hay, cuantos recursos (textos, imagenes, memes, efectos de sonido)
// y cuantas veces cambia la musica. Se compara con las reglas de la serie
// para encontrar los "valles" donde la gente se suele ir.
// =====================================================================

public class ReglasRitmo
{
    public int PPM = 195;              // palabras por minuto del narrador
    public int NarradorCadaSeg = 90;   // como maximo, tanto sin narrador
    public int RecursosPorMin = 4;
    public int CortesMin = 15, CortesMax = 20;
    public int MusicaCadaSeg = 40;     // cambiar de musica al menos cada tanto
    public int ZonaCriticaSeg = 180;   // de 0:30 a aqui se decide si se quedan
    public double DuracionMin = 10, DuracionMax = 12;   // minutos

    public ReglasRitmo Copia() { return (ReglasRitmo)MemberwiseClone(); }

    public void Escribir(Dictionary<string, object> d)
    {
        d["ppm"] = PPM; d["narradorCada"] = NarradorCadaSeg; d["recursosMin"] = RecursosPorMin;
        d["cortesMin"] = CortesMin; d["cortesMax"] = CortesMax; d["musicaCada"] = MusicaCadaSeg;
        d["zonaCritica"] = ZonaCriticaSeg; d["duracionMin"] = DuracionMin; d["duracionMax"] = DuracionMax;
    }

    public static ReglasRitmo Leer(object o, ReglasRitmo base_)
    {
        ReglasRitmo r = base_.Copia();
        if (o == null) return r;
        r.PPM = (int)Json.Numero(o, "ppm", r.PPM);
        r.NarradorCadaSeg = (int)Json.Numero(o, "narradorCada", r.NarradorCadaSeg);
        r.RecursosPorMin = (int)Json.Numero(o, "recursosMin", r.RecursosPorMin);
        r.CortesMin = (int)Json.Numero(o, "cortesMin", r.CortesMin);
        r.CortesMax = (int)Json.Numero(o, "cortesMax", r.CortesMax);
        r.MusicaCadaSeg = (int)Json.Numero(o, "musicaCada", r.MusicaCadaSeg);
        r.ZonaCriticaSeg = (int)Json.Numero(o, "zonaCritica", r.ZonaCriticaSeg);
        r.DuracionMin = Json.Numero(o, "duracionMin", r.DuracionMin);
        r.DuracionMax = Json.Numero(o, "duracionMax", r.DuracionMax);
        return r;
    }
}

public class MinutoRitmo
{
    public int Minuto;
    public double Narrador;     // 0..1 del minuto con narracion
    public int Cortes, Recursos, Musica;
}

public class Medicion
{
    public double Duracion;
    public List<MinutoRitmo> Minutos = new List<MinutoRitmo>();
    public List<Rango> Narracion = new List<Rango>();
    public List<double> CambiosMusica = new List<double>();
    public double PPM;          // 0 si no hay narrador
    public int Palabras;
    public bool HayNarrador { get { return Narracion.Count > 0; } }
}

// Un aviso del medidor, con el tramo al que se refiere.
public class Valle
{
    public double Inicio, Fin;
    public string Tipo = "", Texto = "";
    public bool Critico;        // en la zona critica del inicio
}

public static class Ritmo
{
    // Arma la medicion con lo que ya se sabe del proyecto.
    public static Medicion Medir(double duracion, List<double> cortes, List<double> recursos, List<double> cambiosMusica,
                                 List<Rango> narracion, int palabrasNarrador, double segundosHablados)
    {
        Medicion m = new Medicion();
        m.Duracion = duracion;
        m.Narracion = Rangos.Unir(narracion, 0.6);
        m.CambiosMusica = new List<double>(cambiosMusica);
        m.Palabras = palabrasNarrador;
        m.PPM = segundosHablados > 5 ? Math.Round(palabrasNarrador / segundosHablados * 60) : 0;
        int n = Math.Max(1, (int)Math.Ceiling(duracion / 60));
        for (int i = 0; i < n; i++) m.Minutos.Add(new MinutoRitmo { Minuto = i });
        foreach (double t in cortes) Sumar(m, t, 0);
        foreach (double t in recursos) Sumar(m, t, 1);
        foreach (double t in cambiosMusica) Sumar(m, t, 2);
        foreach (Rango r in m.Narracion)
        {
            double t = r.Inicio;
            while (t < r.Fin - 1e-6)
            {
                int i = (int)(t / 60);
                if (i >= n) break;
                double hasta = Math.Min(r.Fin, (i + 1) * 60.0);
                m.Minutos[i].Narrador += (hasta - t) / 60.0;
                t = hasta;
            }
        }
        return m;
    }

    static void Sumar(Medicion m, double t, int que)
    {
        int i = (int)(t / 60);
        if (i < 0 || i >= m.Minutos.Count) return;
        if (que == 0) m.Minutos[i].Cortes++;
        else if (que == 1) m.Minutos[i].Recursos++;
        else m.Minutos[i].Musica++;
    }

    // Tramos sin narrador mas largos que "maximo" segundos.
    public static List<Rango> SinNarrador(Medicion m, double maximo)
    {
        List<Rango> r = new List<Rango>();
        double cursor = 0;
        foreach (Rango n in m.Narracion)
        {
            if (n.Inicio - cursor > maximo) r.Add(new Rango(cursor, n.Inicio));
            cursor = Math.Max(cursor, n.Fin);
        }
        if (m.Duracion - cursor > maximo) r.Add(new Rango(cursor, m.Duracion));
        return r;
    }

    // Lo que no cumple las reglas, del mas importante al menos.
    public static List<Valle> Valles(Medicion m, ReglasRitmo reglas, bool conNarrador)
    {
        List<Valle> v = new List<Valle>();
        double zona = reglas.ZonaCriticaSeg;
        if (conNarrador)
            foreach (Rango r in SinNarrador(m, reglas.NarradorCadaSeg))
                v.Add(new Valle
                {
                    Inicio = r.Inicio, Fin = r.Fin, Tipo = "narrador", Critico = r.Inicio < zona,
                    Texto = Formato.Tiempo(r.Fin - r.Inicio) + " sin narrador (máximo " + reglas.NarradorCadaSeg + " s)"
                });
        // Minutos seguidos con pocos recursos o pocos cortes.
        Agrupar(m, v, delegate (MinutoRitmo x) { return x.Recursos < reglas.RecursosPorMin; }, "recursos",
                delegate (int a, int b) { return "pocos recursos (menos de " + reglas.RecursosPorMin + "/min)"; }, zona);
        Agrupar(m, v, delegate (MinutoRitmo x) { return x.Cortes < reglas.CortesMin && (x.Minuto + 1) * 60 <= m.Duracion + 30; }, "cortes",
                delegate (int a, int b) { return "ritmo lento (menos de " + reglas.CortesMin + " cortes/min)"; }, zona);
        // Musica que no cambia.
        List<double> cambios = new List<double>(m.CambiosMusica);
        cambios.Sort();
        if (cambios.Count > 0)
        {
            cambios.Add(m.Duracion);
            for (int i = 0; i + 1 < cambios.Count; i++)
                if (cambios[i + 1] - cambios[i] > reglas.MusicaCadaSeg * 1.5)
                    v.Add(new Valle
                    {
                        Inicio = cambios[i], Fin = cambios[i + 1], Tipo = "musica",
                        Texto = "la misma música " + Formato.Tiempo(cambios[i + 1] - cambios[i]) + " (cambiarla cada ~" + reglas.MusicaCadaSeg + " s)"
                    });
        }
        if (m.Duracion > reglas.DuracionMax * 60 + 30)
            v.Add(new Valle
            {
                Inicio = reglas.DuracionMax * 60, Fin = m.Duracion, Tipo = "duracion",
                Texto = "dura " + Formato.Tiempo(m.Duracion) + ": más que el objetivo (" + reglas.DuracionMin + "–" + reglas.DuracionMax + " min)"
            });
        v.Sort(delegate (Valle a, Valle b)
        {
            if (a.Critico != b.Critico) return a.Critico ? -1 : 1;
            return a.Inicio.CompareTo(b.Inicio);
        });
        return v;
    }

    delegate bool Condicion(MinutoRitmo m);
    delegate string Descripcion(int desde, int hasta);

    static void Agrupar(Medicion m, List<Valle> v, Condicion mal, string tipo, Descripcion texto, double zona)
    {
        int i = 0;
        while (i < m.Minutos.Count)
        {
            if (!mal(m.Minutos[i])) { i++; continue; }
            int j = i;
            while (j + 1 < m.Minutos.Count && mal(m.Minutos[j + 1])) j++;
            if (j - i + 1 >= 2 || i * 60 < zona)   // un solo minuto flojo solo importa al inicio
                v.Add(new Valle
                {
                    Inicio = i * 60, Fin = Math.Min(m.Duracion, (j + 1) * 60), Tipo = tipo, Critico = i * 60 < zona,
                    Texto = "min " + i + (j > i ? "–" + j : "") + ": " + texto(i, j)
                });
            i = j + 1;
        }
    }

    static double Percentil(List<double> l, double p)
    {
        if (l.Count == 0) return 0;
        List<double> o = new List<double>(l);
        o.Sort();
        double x = p * (o.Count - 1);
        int a = (int)Math.Floor(x), b = Math.Min(o.Count - 1, a + 1);
        return o[a] + (o[b] - o[a]) * (x - a);
    }

    // Reglas sacadas de un episodio que funciono: su ritmo es el objetivo.
    public static ReglasRitmo Aprender(Medicion m, ReglasRitmo base_)
    {
        ReglasRitmo r = base_.Copia();
        List<double> cortes = new List<double>(), recursos = new List<double>();
        foreach (MinutoRitmo x in m.Minutos)
            if ((x.Minuto + 1) * 60 <= m.Duracion + 30) { cortes.Add(x.Cortes); recursos.Add(x.Recursos); }
        if (cortes.Count > 0)
        {
            r.CortesMin = (int)Math.Max(5, Math.Round(Percentil(cortes, 0.40)));
            r.CortesMax = (int)Math.Max(r.CortesMin + 2, Math.Round(Percentil(cortes, 0.80)));
            r.RecursosPorMin = (int)Math.Max(1, Math.Round(Percentil(recursos, 0.50)));
        }
        if (m.HayNarrador)
        {
            if (m.PPM > 60) r.PPM = (int)m.PPM;
            List<double> huecos = new List<double>();
            double cursor = 0;
            foreach (Rango n in m.Narracion) { if (n.Inicio > cursor) huecos.Add(n.Inicio - cursor); cursor = Math.Max(cursor, n.Fin); }
            if (huecos.Count > 2) r.NarradorCadaSeg = (int)Math.Max(45, Math.Min(150, Math.Round(Percentil(huecos, 0.75) / 5) * 5));
        }
        if (m.CambiosMusica.Count > 2)
        {
            List<double> c = new List<double>(m.CambiosMusica);
            c.Sort();
            List<double> d = new List<double>();
            for (int i = 1; i < c.Count; i++) d.Add(c[i] - c[i - 1]);
            r.MusicaCadaSeg = (int)Math.Max(15, Math.Round(Percentil(d, 0.50) / 5) * 5 + 10);
        }
        double min = m.Duracion / 60;
        r.DuracionMin = Math.Max(1, Math.Floor(min - 0.5));
        r.DuracionMax = Math.Ceiling(min + 1);
        return r;
    }

    // "10:35 · 17 cortes/min · 6 recursos/min · narrador 33 % a 196 ppm · música cada 30 s"
    public static string Resumen(Medicion m)
    {
        double min = Math.Max(1, m.Duracion / 60);
        int cortes = 0, recursos = 0;
        double narr = 0;
        foreach (MinutoRitmo x in m.Minutos) { cortes += x.Cortes; recursos += x.Recursos; narr += x.Narrador * 60; }
        string r = Formato.Tiempo(m.Duracion) + " · " + Math.Round(cortes / min) + " cortes/min · " + Math.Round(recursos / min) + " recursos/min";
        if (m.HayNarrador) r += " · narrador " + Math.Round(narr / Math.Max(1, m.Duracion) * 100) + " %" + (m.PPM > 0 ? " a " + m.PPM + " ppm" : "");
        if (m.CambiosMusica.Count > 1) r += " · música cada " + Math.Round(m.Duracion / m.CambiosMusica.Count) + " s";
        return r;
    }

    // Tabla en texto (para el informe y para Gemini).
    public static string Tabla(Medicion m)
    {
        StringBuilder sb = new StringBuilder("min;narrador%;cortes;recursos;cambiosMusica\n");
        foreach (MinutoRitmo x in m.Minutos)
            sb.Append(x.Minuto + ";" + Math.Round(x.Narrador * 100) + ";" + x.Cortes + ";" + x.Recursos + ";" + x.Musica + "\n");
        return sb.ToString();
    }
}

public static class Rangos
{
    public static List<Rango> Unir(List<Rango> l, double hueco)
    {
        List<Rango> o = new List<Rango>(l), r = new List<Rango>();
        o.Sort(delegate (Rango a, Rango b) { return a.Inicio.CompareTo(b.Inicio); });
        foreach (Rango x in o)
        {
            if (r.Count > 0 && x.Inicio <= r[r.Count - 1].Fin + hueco)
                r[r.Count - 1] = new Rango(r[r.Count - 1].Inicio, Math.Max(r[r.Count - 1].Fin, x.Fin));
            else r.Add(x);
        }
        return r;
    }
}

// Lee el proyecto de Vegas y reconoce para que es cada pista.
public static class RitmoVegas
{
    // Pista donde PulirEpisodio pone la narracion provisional.
    public const string PistaNarracion = "vegas-cut · Narración provisional";

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Nombre del archivo con barras de Windows o de las otras.
    static string NombreArchivo(string ruta)
    {
        ruta = ruta ?? "";
        int i = Math.Max(ruta.LastIndexOf('\\'), ruta.LastIndexOf('/'));
        return i >= 0 ? ruta.Substring(i + 1) : ruta;
    }

    static string Archivo(TrackEvent e)
    {
        try { return e.ActiveTake != null && e.ActiveTake.Media != null ? (e.ActiveTake.Media.FilePath ?? "") : ""; } catch { return ""; }
    }

    static bool Generado(TrackEvent e)
    {
        try { return e.ActiveTake != null && e.ActiveTake.Media != null && e.ActiveTake.Media.IsGenerated(); } catch { return false; }
    }

    static double Cubierto(List<Rango> l, double a, double b)
    {
        double c = 0;
        foreach (Rango r in l) c += Math.Max(0, Math.Min(b, r.Fin) - Math.Max(a, r.Inicio));
        return c;
    }

    static double Mediana(List<double> l)
    {
        if (l.Count == 0) return 0;
        l.Sort();
        return l[l.Count / 2];
    }

    // Pistas de audio con las grabaciones (voces y sonido del juego): las que
    // tienen sobre todo archivos de la pista principal o de la transcripcion.
    // La del narrador (por su nombre o "Narr...") no cuenta.
    public static List<Track> PistasGrabacion(Project p, Transcripcion t, string narrador)
    {
        Dictionary<string, bool> grab = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Track principal = null;
        double mejor = 0;
        foreach (Track pista in p.Tracks)
        {
            if (pista.IsAudio()) continue;
            double cubre = 0;
            foreach (TrackEvent e in pista.Events) if (!Generado(e)) cubre += S(e.Length);
            if (cubre > mejor) { mejor = cubre; principal = pista; }
        }
        if (principal != null) foreach (TrackEvent e in principal.Events) grab[NombreArchivo(Archivo(e))] = true;
        if (t != null)
            foreach (Hablante h in t.Hablantes)
            {
                string n = (h.Nombre ?? "").Trim();
                if (String.Equals(n, (narrador ?? "").Trim(), StringComparison.OrdinalIgnoreCase) || n.ToLowerInvariant().StartsWith("narr")) continue;
                if (!String.IsNullOrEmpty(h.Archivo)) grab[NombreArchivo(h.Archivo)] = true;
                foreach (Fuente f in h.Fuentes) grab[NombreArchivo(f.Media)] = true;
            }
        List<Track> r = new List<Track>();
        foreach (Track pista in p.Tracks)
        {
            if (!pista.IsAudio()) continue;
            int si = 0, total = 0;
            foreach (TrackEvent e in pista.Events) { total++; if (grab.ContainsKey(NombreArchivo(Archivo(e)))) si++; }
            if (total > 0 && si * 2 >= total) r.Add(pista);
        }
        return r;
    }

    // Mide el proyecto abierto en Vegas con su transcripcion (si la tiene),
    // siguiendo las ediciones hechas despues de transcribir.
    public static Medicion MedirAbierto(Vegas vegas, string narrador)
    {
        Transcripcion t = null;
        string ruta = String.IsNullOrEmpty(vegas.Project.FilePath) ? null : Transcripcion.RutaPara(vegas.Project.FilePath);
        if (ruta != null && File.Exists(ruta))
        {
            t = Transcripcion.Cargar(ruta);
            if (t.TieneFuentes) t.Ubicador = PistasVegas.Ubicador(vegas.Project, t);
        }
        return Medir(vegas.Project, t, narrador);
    }

    // El narrador por su nombre en la transcripcion.
    public static Medicion Medir(Project p, Transcripcion t, string narrador)
    {
        int i = -1;
        if (t != null)
            for (int k = 0; k < t.Hablantes.Count; k++)
                if (String.Equals((t.Hablantes[k].Nombre ?? "").Trim(), (narrador ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) { i = k; break; }
        return Medir(p, t, i);
    }

    // narrador: indice del hablante narrador en la transcripcion (-1 = el que
    // se llame "Narrador", o ninguno).
    public static Medicion Medir(Project p, Transcripcion t, int narrador)
    {
        double duracion = S(p.Length);

        // 1. Pista principal: la de video con mas tiempo cubierto por grabaciones.
        Track principal = null;
        double mejor = 0;
        foreach (Track pista in p.Tracks)
        {
            if (pista.IsAudio()) continue;
            double cubre = 0;
            foreach (TrackEvent e in pista.Events) if (!Generado(e)) cubre += S(e.Length);
            if (cubre > mejor) { mejor = cubre; principal = pista; }
        }
        List<double> cortes = new List<double>();
        Dictionary<string, bool> deJuego = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (principal != null)
            foreach (TrackEvent e in principal.Events) { cortes.Add(S(e.Start)); deJuego[Archivo(e)] = true; }

        // 2. Narracion (de la transcripcion) y su archivo, para no contarlo como efecto.
        if (narrador < 0 && t != null)
            for (int i = 0; i < t.Hablantes.Count; i++)
                if ((t.Hablantes[i].Nombre ?? "").ToLowerInvariant().StartsWith("narr")) { narrador = i; break; }
        List<Rango> narracion = new List<Rango>();
        int palabras = 0;
        double hablado = 0;
        string archivoNarrador = "";
        // Grabaciones: lo de la pista principal, lo transcrito y todo archivo
        // que pase de 45 s en el proyecto (POV, voces). Cortadas a lo largo
        // del video no son "recursos": solo cuenta cuando aparecen.
        Dictionary<string, double> uso = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (Track pista in p.Tracks)
            foreach (TrackEvent e in pista.Events)
            {
                if (Generado(e)) continue;
                string f = NombreArchivo(Archivo(e));
                double u;
                uso.TryGetValue(f, out u);
                uso[f] = u + S(e.Length);
            }
        Dictionary<string, bool> grabacion = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (string f in deJuego.Keys) grabacion[NombreArchivo(f)] = true;
        foreach (KeyValuePair<string, double> u in uso) if (u.Value > 45) grabacion[u.Key] = true;
        if (t != null)
            foreach (Hablante h in t.Hablantes)
            {
                if (!String.IsNullOrEmpty(h.Archivo)) grabacion[NombreArchivo(h.Archivo)] = true;
                foreach (Fuente f in h.Fuentes) grabacion[NombreArchivo(f.Media ?? "")] = true;
            }
        if (t != null && narrador >= 0 && narrador < t.Hablantes.Count)
        {
            archivoNarrador = NombreArchivo(t.Hablantes[narrador].Archivo ?? "");
            // Donde esta de verdad el archivo de narracion (en esa pista tambien
            // puede haber memes, que Whisper transcribe como si fueran del narrador).
            List<Rango> donde = new List<Rango>();
            if (archivoNarrador.Length > 0)
                foreach (Track pista in p.Tracks)
                    if (pista.IsAudio())
                        foreach (TrackEvent e in pista.Events)
                            if (String.Equals(NombreArchivo(Archivo(e)), archivoNarrador, StringComparison.OrdinalIgnoreCase))
                                donde.Add(new Rango(S(e.Start), S(e.Start) + S(e.Length)));
            foreach (Segmento s in t.SegmentosActuales())
            {
                if (s.Hablante != narrador) continue;
                if (donde.Count > 0 && Cubierto(donde, s.Inicio, s.Fin) < 0.5 * (s.Fin - s.Inicio)) continue;
                narracion.Add(new Rango(s.Inicio, s.Fin));
                palabras += s.Palabras.Count;
                if (s.Palabras.Count > 0) hablado += s.Palabras[s.Palabras.Count - 1].Fin - s.Palabras[0].Inicio;
            }
        }

        // La narracion provisional (voz de Windows) cuenta como narrador.
        foreach (Track pista in p.Tracks)
            if (pista.IsAudio() && pista.Name == PistaNarracion)
                foreach (TrackEvent e in pista.Events) if (!e.Mute) narracion.Add(new Rango(S(e.Start), S(e.Start) + S(e.Length)));

        // 3. Lo demas: recursos (video encima, efectos cortos) y musica (audio largo).
        List<double> recursos = new List<double>(), musica = new List<double>();
        foreach (Track pista in p.Tracks)
        {
            if (pista == principal || (principal != null && pista.Index == principal.Index)) continue;
            if (pista.Name == PistaNarracion) continue;
            List<TrackEvent> eventos = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) eventos.Add(e);
            if (eventos.Count == 0) continue;
            eventos.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });
            if (pista.IsAudio())
            {
                // Las voces y el sonido del juego no cuentan; lo que queda es musica o efectos.
                List<TrackEvent> otros = new List<TrackEvent>();
                foreach (TrackEvent e in eventos) if (!grabacion.ContainsKey(NombreArchivo(Archivo(e)))) otros.Add(e);
                if (otros.Count == 0) continue;
                List<double> largos = new List<double>();
                foreach (TrackEvent e in otros) largos.Add(S(e.Length));
                if (Mediana(largos) >= 15)
                {
                    string previo = null;
                    foreach (TrackEvent e in otros)
                    {
                        string f = Archivo(e);
                        if (f != previo) musica.Add(S(e.Start));
                        previo = f;
                    }
                }
                else foreach (TrackEvent e in otros) recursos.Add(S(e.Start));
                continue;
            }
            double finPrevio = -10;
            foreach (TrackEvent e in eventos)
            {
                double ini = S(e.Start);
                bool seguido = ini <= finPrevio + 0.5;
                finPrevio = Math.Max(finPrevio, ini + S(e.Length));
                if (Generado(e)) { if (S(e.Length) <= 20) recursos.Add(ini); continue; }   // un "Día N" fijo no cuenta
                if (grabacion.ContainsKey(NombreArchivo(Archivo(e))) && seguido) continue;   // la misma ventana de POV, cortada
                recursos.Add(ini);
            }
        }
        return Ritmo.Medir(duracion, cortes, recursos, musica, narracion, palabras, hablado);
    }
}
