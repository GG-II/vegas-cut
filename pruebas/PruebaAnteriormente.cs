// Pruebas del script Anteriormente: lectura de episodios viejos, mensaje a
// Gemini, lectura de clips, pedazos de archivo e insercion con la API falsa.
using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

class PruebaAnteriormente
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static bool Cerca(double a, double b) { return Math.Abs(a - b) < 0.002; }
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    static Segmento Seg(int h, double t, string texto)
    {
        Segmento s = new Segmento();
        s.Hablante = h; s.Inicio = t; s.Texto = texto;
        foreach (string w in texto.Split(' '))
        {
            s.Palabras.Add(new Palabra { Inicio = t, Fin = t + 0.3, Prob = 1, Texto = " " + w });
            t += 0.4;
        }
        s.Fin = t;
        return s;
    }

    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "anteriormente-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string grabacion = Path.Combine(dir, "rec1.mp4"), discord = Path.Combine(dir, "discord.wav");
        File.WriteAllText(grabacion, ""); File.WriteAllText(discord, "");

        // Episodio 1: tu voz en el flujo 1 del mp4 (con un hueco de Quitar
        // silencios a los 100 s) y la llamada en otro archivo.
        Transcripcion t = new Transcripcion();
        Hablante yo = new Hablante { Etiqueta = "A2", Nombre = "AB Fann", Voz = true };
        yo.Fuentes.Add(new Fuente { Inicio = 0, Fin = 100, Desde = 0, Velocidad = 1, Media = grabacion, Flujo = 1 });
        yo.Fuentes.Add(new Fuente { Inicio = 100, Fin = 200, Desde = 150, Velocidad = 1, Media = grabacion, Flujo = 1 });
        Hablante amigos = new Hablante { Etiqueta = "A3", Nombre = "Amigos", Voz = true };
        amigos.Fuentes.Add(new Fuente { Inicio = 0, Fin = 200, Desde = 0, Velocidad = 1, Media = discord });
        t.Hablantes.Add(yo); t.Hablantes.Add(amigos);
        t.Segmentos.Add(Seg(0, 10, "Tenemos que encontrar la mesa del fin"));
        t.Segmentos.Add(Seg(1, 50, "esto es algo personal"));
        t.Segmentos.Add(Seg(0, 70, "¡Suscríbete al canal!"));
        t.Segmentos.Add(Seg(1, 98, "La mesa estaba en la cueva"));
        Edicion ed = new Edicion(); ed.Quitados.Add(new Rango(40, 60)); t.Ediciones.Add(ed);
        string veg1 = Path.Combine(dir, "S01E01.veg");
        t.Guardar(Transcripcion.RutaPara(veg1));

        Episodio e1 = Episodio.Abrir(veg1);
        Verificar(e1.Nombre == "S01E01" && e1.TieneFuentes, "Abre el episodio desde su .veg");
        Verificar(Episodio.Abrir(Transcripcion.RutaPara(veg1)).Veg == veg1, "También desde su transcripción");
        Verificar(e1.Publicado().Count == 2, "Solo lo que quedó en el video, sin frases inventadas");
        List<Episodio> eps = new List<Episodio> { e1 };
        string msg = LogicaAnteriormente.Mensaje(eps, "Buscan la mesa del fin en la cueva.", "Recordar la mesa", 20);
        Verificar(msg.Contains("EPISODIO 1: S01E01") && msg.Contains("[10.0-") && !msg.Contains("personal") &&
                  msg.Contains("Buscan la mesa") && msg.Contains("Recordar la mesa"), "Mensaje a Gemini");
        Verificar(LogicaAnteriormente.Instrucciones(30).Contains("entre 24 y 34 s"), "Instrucciones con la duración pedida");

        List<ClipAnterior> clips = LogicaAnteriormente.Leer(@"{""clips"": [
            {""episodio"": 1, ""inicio"": 10.2, ""fin"": 11.5, ""texto"": ""Tenemos que encontrar la mesa"", ""motivo"": ""el objetivo""},
            {""episodio"": 3, ""inicio"": 1, ""fin"": 3},
            {""episodio"": 1, ""inicio"": 98, ""fin"": 100.3, ""texto"": ""La mesa estaba en la cueva""}]}", eps);
        Verificar(clips.Count == 2 && Cerca(clips[0].Inicio, 9.85) && Cerca(clips[0].Fin, 11.65) && clips[0].Quien == "AB Fann",
            "Lee los clips y los ajusta a las palabras (" + (clips.Count > 0 ? clips[0].Inicio + "-" + clips[0].Fin : "") + ")");

        List<PiezaAnterior> pz = LogicaAnteriormente.Piezas(t, 98, 103, 5);
        Verificar(pz.Count == 3 && Cerca(pz[0].Desde, 98) && Cerca(pz[0].Hasta, 100) && Cerca(pz[0].En, 5) &&
                  Cerca(pz[1].Desde, 150) && Cerca(pz[1].En, 7) && pz[2].Hablante == 1 && Cerca(pz[2].Hasta, 103),
            "Pedazos de archivo: respeta los cortes que había al transcribir");

        // ------------------------------------------------------ insertar
        Vegas v = new Vegas();
        Project p = v.Project;
        VideoTrack video = new VideoTrack(0, "Video"); AudioTrack a2 = new AudioTrack(1, "Voz"), a3 = new AudioTrack(2, "Discord");
        p.Tracks.Add(video); p.Tracks.Add(a2); p.Tracks.Add(a3);
        foreach (Track pista in p.Tracks)
        {
            TrackEvent e = pista.IsAudio() ? (TrackEvent)((AudioTrack)pista).AddAudioEvent(TC(0), TC(300)) : video.AddVideoEvent(TC(0), TC(300));
            e.ActiveTake = new Take { Media = new Media { FilePath = "ep2.mp4" } };
        }
        p.Markers.Add(new Marker(TC(10), "★9 Algo"));
        string r = InsertarAnteriormente.Aplicar(p, eps, clips, 60, true, true);
        double total = clips[0].Duracion + clips[1].Duracion;
        Verificar(Cerca(S(p.Tracks[0].Events[0].Start), 60) && Cerca(S(p.Markers[0].Position), 70), "Corre todo el video 60 s");
        TrackEvent rv = null;
        foreach (TrackEvent e in video.Events) if (S(e.Start) < 1) rv = e;
        Verificar(rv != null && Cerca(S(rv.ActiveTake.Offset), clips[0].Inicio) && rv.ActiveTake.Media.FilePath == grabacion,
            "El video del clip sale de la grabación original, en su segundo");
        int enVoz = 0, enDiscord = 0;
        foreach (TrackEvent e in a2.Events) if (S(e.Start) < 60) enVoz++;
        foreach (TrackEvent e in a3.Events) if (S(e.Start) < 60) enDiscord++;
        Verificar(enVoz == 3 && enDiscord == 2, "Cada voz va a su pista del proyecto (por etiqueta), en pedazos si había cortes");
        Verificar(p.Groups.Count == 2 && p.Groups[0].Count == 3, "Cada clip queda agrupado (video + sus audios)");
        Verificar(p.Regions.Count == 1 && Cerca(S(p.Regions[0].Length), total) && p.Markers.Exists(delegate (Marker m) { return m.Label.StartsWith("TEXTO:"); }),
            "Región ANTERIORMENTE y marcador TEXTO");
        Verificar(r.Contains("se corrió 1:00"), "Avisa cuánto se corrió (" + r.Replace("\n", " ") + ")");

        // Si ya hay espacio, no se corre otra vez.
        Project p2 = new Project();
        AudioTrack b = new AudioTrack(0, "Voz"); p2.Tracks.Add(b);
        b.AddAudioEvent(TC(90), TC(100)).ActiveTake = new Take { Media = new Media { FilePath = "x.wav" } };
        InsertarAnteriormente.Aplicar(p2, eps, clips, 60, true, false);
        Verificar(Cerca(S(b.Events[0].Start), 90), "Si ya hay espacio al inicio no corre nada");
        Verificar(p2.Tracks.Count == 4, "Pistas nuevas: video y una por persona");

        // Grabación que ya no existe.
        File.Delete(discord);
        Project p3 = new Project();
        string r3 = InsertarAnteriormente.Aplicar(p3, eps, clips, 60, true, false);
        Verificar(r3.Contains("No se encontraron") && r3.Contains("discord.wav"), "Avisa si falta una grabación");

        // ------------------------------------------------------- series
        int st, sn; string ss1, ss2, ss3;
        Verificar(Serie.Clave("S01E02 SCR", out st, out sn, out ss1) && st == 1 && sn == 2 &&
                  Serie.Clave("s1e10_SCR", out st, out sn, out ss2) && sn == 10 && ss1 == ss2 &&
                  Serie.Clave("S01E03 Avatar", out st, out sn, out ss3) && ss3 != ss1 && !Serie.Clave("Enero", out st, out sn, out ss3),
            "Serie: reconoce S01E02 y la serie por el resto del nombre");
        string raiz = Path.Combine(Path.GetTempPath(), "serie-" + Guid.NewGuid().ToString("N"));
        // Un capitulo por carpeta, otro suelto mas adentro, y uno de otra serie.
        foreach (string f in new string[] { "S01E01/S01E01 SCR.veg", "S01E02/S01E02 SCR.veg", "S01E03/S01E03 SCR.veg",
                                             "viejos/2025/S01E00 SCR.veg", "S01E02/S01E02 SCR.veg.bak", "S01E01/S01E01 Avatar.veg" })
        {
            string ruta = Path.Combine(raiz, f.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(ruta));
            File.WriteAllText(ruta, "");
        }
        string actual = Path.Combine(raiz, "S01E02", "S01E02 SCR.veg");
        List<CapSerie> caps = Serie.Buscar(actual);
        string nombres = "";
        foreach (CapSerie c in caps) nombres += c.Codigo + (c.Relacion < 0 ? "-" : c.Relacion > 0 ? "+" : "=") + " ";
        Verificar(nombres == "S01E01- S01E02= S01E03+ ", "Serie: encuentra anteriores y posteriores en carpetas de al lado (" + nombres + ")");
        Verificar(Serie.Buscar(actual, Path.Combine(raiz, "viejos")).Count == 4, "Serie: con la carpeta elegida busca también en sus subcarpetas");

        Serie.Guardar(Path.Combine(raiz, "S01E01", "S01E01 SCR.veg"), "Gerber = Herbert", Path.Combine(raiz, "viejos"), Serie.Buscar(Path.Combine(raiz, "S01E01", "S01E01 SCR.veg")));
        string notas, carpeta;
        List<CapSerie> caps2 = Serie.Capitulos(actual, out notas, out carpeta);
        Verificar(notas == "Gerber = Herbert" && carpeta.EndsWith("viejos") && caps2.Count == 4, "Serie: un capítulo nuevo hereda notas y carpeta del anterior");
        caps2[0].Elegido = false;
        Serie.Guardar(actual, "notas propias", carpeta, caps2);
        caps2 = Serie.Capitulos(actual, out notas, out carpeta);
        Verificar(notas == "notas propias" && !caps2[0].Elegido && caps2[1].Elegido, "Serie: guarda por proyecto las notas y los capítulos que quitaste");

        Ficha fi = Ficha.Leer(@"{""resumen"": ""Ganaron la carrera."", ""hilos"": [""El yunque escondido""], ""recurrentes"": [""Gerber pierde su caballo""],
            ""frases"": [{""inicio"": 10, ""fin"": 12.5, ""quien"": ""AB Fann"", ""texto"": ""¡Ganamos!"", ""por"": ""el final""}]}");
        string s1 = Path.Combine(raiz, "S01E01", "S01E01 SCR.veg"), s3 = Path.Combine(raiz, "S01E03", "S01E03 SCR.veg");
        fi.Guardar(s1); fi.Guardar(s3);
        Ficha fi2 = Ficha.Cargar(s1);
        Verificar(fi2 != null && fi2.Hilos[0] == "El yunque escondido" && fi2.Frases.Count == 1 && fi2.Texto(true).Contains("[10.0-12.5] AB Fann: ¡Ganamos!"),
            "Ficha: se guarda junto al proyecto y se lee igual");
        string ctx = Serie.Contexto(Serie.Buscar(actual), "Steel Ball Run");
        Verificar(ctx.Contains("Notas de la serie") && ctx.Contains("Capítulos anteriores:\n- S01E01 SCR: Ganaron") &&
                  ctx.Contains("POSTERIORES") && ctx.Contains("- S01E03 SCR"), "Contexto de MomentosIA: notas, anteriores y posteriores");

        // Anteriormente: de los capitulos viejos solo la ficha; completos los 2 ultimos.
        Episodio viejo = new Episodio { Nombre = "S01E00", T = t, Ficha = fi };
        Episodio e2 = new Episodio { Nombre = "S01E01b", T = t };
        string ma = LogicaAnteriormente.Mensaje(new List<Episodio> { viejo, e1, e2 }, "x", "", 30, "Notas SBR");
        int bloque0 = ma.IndexOf("EPISODIO 1: S01E00"), bloque1 = ma.IndexOf("EPISODIO 2:");
        string parte0 = ma.Substring(bloque0, bloque1 - bloque0);
        Verificar(parte0.Contains("Ficha: Ganaron") && parte0.Contains("[10.0-12.5]") && !parte0.Contains("Transcripción") &&
                  ma.Substring(bloque1).Contains("Transcripción") && ma.Contains("NOTAS DE LA SERIE"),
            "Anteriormente: capítulos viejos solo con su ficha, los 2 últimos completos");
        try { Directory.Delete(raiz, true); } catch { }

        try { Directory.Delete(dir, true); } catch { }
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " pruebas fallaron.");
        return fallos == 0 ? 0 : 1;
    }
}
