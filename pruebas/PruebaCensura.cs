// Pruebas de CensurarPalabrotas: busqueda en la transcripcion, tiempos,
// ubicacion tras editar a mano y aplicacion con la API falsa.
using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

class PruebaCensura
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static bool Cerca(double a, double b) { return Math.Abs(a - b) < 0.002; }
    static Timecode S(double s) { return Timecode.FromMilliseconds(s * 1000); }

    static Segmento Seg(int h, double t, string texto)
    {
        Segmento s = new Segmento();
        s.Hablante = h; s.Inicio = t; s.Texto = texto;
        foreach (string w in texto.Split(' '))
        {
            Palabra p = new Palabra();
            p.Inicio = t; p.Fin = t + 0.3; p.Prob = 0.9; p.Texto = " " + w;
            s.Palabras.Add(p);
            t += 0.4;
        }
        s.Fin = t;
        return s;
    }

    static int Main()
    {
        // ------------------------------------------------------- busqueda
        Verificar(LogicaCensura.Normalizar("¡Coño!") == "cono" && LogicaCensura.Normalizar("Cabrón,") == "cabron",
            "Normalizar: sin acentos, mayúsculas ni signos");
        Transcripcion t = new Transcripcion();
        Hablante yo = new Hablante(); yo.Etiqueta = "A2"; yo.Nombre = "Yo"; yo.Voz = true;
        Hablante amigos = new Hablante(); amigos.Etiqueta = "A3"; amigos.Nombre = "Amigos"; amigos.Voz = true;
        t.Hablantes.Add(yo); t.Hablantes.Add(amigos);
        t.Segmentos.Add(Seg(0, 10, "Me está siguiendo el hijo de su puta madre."));
        t.Segmentos.Add(Seg(0, 20, "¡Madre mía! Y nada, comí huevos."));
        t.Segmentos.Add(Seg(1, 25, "¡Shit! qué vergüenza con la computadora"));
        t.Segmentos.Add(Seg(1, 30, "No, sí, yo ching... ¡Cabrón! pinches creepers"));
        t.Segmentos.Add(Seg(0, 40, "Volvió a dar vergueo esta mierda, no mames."));
        List<Coincidencia> c = LogicaCensura.Buscar(t, LogicaCensura.PalabrasPorDefecto);
        List<string> dichas = new List<string>();
        foreach (Coincidencia x in c) dichas.Add(x.Texto);
        string todas = String.Join("|", dichas.ToArray());
        Verificar(todas == "puta madre|Shit|ching|Cabrón|pinches|vergueo|mierda|no mames", "Busca palabras, raíces y frases (" + todas + ")");
        Verificar(Cerca(c[0].Inicio, 12.8) && Cerca(c[0].Fin, 13.5) && c[0].Contexto.Contains("«puta madre.»"),
            "Frase: un solo tramo con contexto");
        Verificar(LogicaCensura.Buscar(t, "# nada\n\n").Count == 0, "Lista vacía o comentarios: nada");

        // ----------------------------------------------------- tiempos
        OpcionesCensura o = new OpcionesCensura();
        Rango r = LogicaCensura.Tapa(10, 10.4, o);
        Verificar(Cerca(r.Inicio, 9.94) && Cerca(r.Fin, 10.46), "Tapa: un poco antes y un poco después");
        r = LogicaCensura.Tapa(5, 5, o);
        Verificar(Cerca(r.Inicio, 4.875) && Cerca(r.Fin, 5.125), "Tapa: palabra de 0 s llega al mínimo, centrada");
        o.Tapar = Tapar.Inicio;
        r = LogicaCensura.Tapa(10, 11, o);
        Verificar(Cerca(r.Inicio, 9.94) && Cerca(r.Fin, 10.66), "Tapa: solo el inicio");
        o.Tapar = Tapar.Final;
        r = LogicaCensura.Tapa(10, 11, o);
        Verificar(Cerca(r.Inicio, 10.34) && Cerca(r.Fin, 11.06), "Tapa: solo el final");
        Rango tapa = new Rango(10, 10.5);
        Verificar(Cerca(LogicaCensura.Sfx(tapa, 1, Encaje.AlInicio).Fin, 11) && Cerca(LogicaCensura.Sfx(tapa, 1, Encaje.Centrado).Inicio, 9.75) &&
                  Cerca(LogicaCensura.Sfx(tapa, 1, Encaje.AlFinal).Inicio, 9.5) && Cerca(LogicaCensura.Sfx(tapa, 1, Encaje.Ajustar).Fin, 10.5),
            "Efecto entero: empieza, centrado o termina con la palabra");
        Verificar(LogicaCensura.Unir(new List<Rango> { new Rango(5, 6), new Rango(1, 2), new Rango(1.5, 3) }).Count == 2, "Une tapas que se tocan");

        // ------------------------------- ubicar tras editar a mano
        yo.Fuentes.Add(new Fuente { Inicio = 0, Fin = 100, Desde = 0, Velocidad = 1, Media = "K:/voz.mp4" });
        amigos.Fuentes.Add(new Fuente { Inicio = 0, Fin = 100, Desde = 0, Velocidad = 1, Media = "K:/discord.wav" });
        Vegas v = new Vegas();
        Project p = v.Project;
        p.Length = S(80);
        AudioTrack vozYo = new AudioTrack(1, "Voz"), vozAmigos = new AudioTrack(2, "Discord");
        p.Tracks.Add(new VideoTrack(0, "Video")); p.Tracks.Add(vozYo); p.Tracks.Add(vozAmigos);
        // A mano: se quitaron los segundos 8 a 20 de las dos pistas.
        foreach (AudioTrack pista in new AudioTrack[] { vozYo, vozAmigos })
        {
            Media m = new Media { FilePath = pista == vozYo ? "K:/voz.mp4" : "K:/discord.wav" };
            AudioEvent a = pista.AddAudioEvent(S(0), S(8)); a.ActiveTake = new Take { Media = m };
            AudioEvent b = pista.AddAudioEvent(S(8), S(80)); b.ActiveTake = new Take { Media = m, Offset = S(20) };
        }
        CensuraVegas.Ubicar(p, t, c);
        Verificar(c[0].Lugares.Count == 0, "Ubicar: lo que se cortó a mano ya no está");
        Verificar(c[1].Lugares.Count == 1 && c[1].Lugares[0].Pista == 2 && Cerca(c[1].Lugares[0].Inicio, 13) && Cerca(c[1].Lugares[0].Fin, 13.3),
            "Ubicar: encuentra la palabra en su nuevo lugar y en la pista de quien la dijo");

        string ruta = Path.Combine(Path.GetTempPath(), "censura-" + Guid.NewGuid().ToString("N") + ".json");
        t.Guardar(ruta);
        Transcripcion t2 = Transcripcion.Cargar(ruta);
        File.Delete(ruta);
        Verificar(t2.TieneFuentes && t2.Hablantes[1].Fuentes[0].Media == "K:/discord.wav", "Transcripción: guarda y lee las fuentes");

        // Transcripcion vieja (sin fuentes): sigue los cortes de las herramientas.
        Transcripcion vieja = new Transcripcion();
        vieja.Hablantes.Add(new Hablante { Etiqueta = "A2", Nombre = "Yo" });
        vieja.Segmentos.Add(Seg(0, 30, "qué pendejo"));
        Edicion ed = new Edicion(); ed.Quitados.Add(new Rango(8, 20)); vieja.Ediciones.Add(ed);
        List<Coincidencia> cv = LogicaCensura.Buscar(vieja, LogicaCensura.PalabrasPorDefecto);
        CensuraVegas.Ubicar(p, vieja, cv);
        Verificar(cv.Count == 1 && cv[0].Lugares.Count == 1 && cv[0].Lugares[0].Pista == 1 && Cerca(cv[0].Lugares[0].Inicio, 18.4),
            "Ubicar sin fuentes: con los cortes registrados y la pista por su etiqueta");

        // ----------------------------------------------------- aplicar
        foreach (Coincidencia x in c) x.Elegida = x.Texto != "pinches";
        o = new OpcionesCensura();
        int n = CensuraVegas.Aplicar(p, c, o);
        AudioTrack censura = (AudioTrack)p.Tracks[p.Tracks.Count - 1];
        Verificar(n == 4 && censura.Name == "Censura" && censura.Events.Count == 4, "Aplicar: un efecto por lugar (las seguidas se unen) en una pista nueva");
        Verificar(File.Exists(LogicaCensura.Pitido()) && WavNiveles.Leer(LogicaCensura.Pitido(), Analisis.Paso).Db[100] > -13,
            "Pitido: se genera un WAV de 1 kHz");
        Verificar(Cerca(censura.Events[0].Start.ToMilliseconds() / 1000, 12.94) && Cerca(censura.Events[0].Length.ToMilliseconds() / 1000, 0.42),
            "Aplicar: el efecto cubre la palabra con sus márgenes");
        Verificar(Math.Abs(censura.Volume - 0.501) < 0.01, "Aplicar: volumen del efecto (−6 dB)");
        int mudosAmigos = 0, mudosYo = 0;
        foreach (TrackEvent e in vozAmigos.Events) if (e.Mute) { mudosAmigos++; if (Cerca(e.Start.ToMilliseconds() / 1000, 12.94)) mudosAmigos += 100; }
        foreach (TrackEvent e in vozYo.Events) if (e.Mute) mudosYo++;
        Verificar(mudosAmigos >= 100 && mudosYo == 2, "Aplicar: silencia solo la pista de quien lo dijo (" + mudosAmigos + ", " + mudosYo + ")");

        // Efecto propio entero, centrado.
        string sfx = Path.Combine(Path.GetTempPath(), "cuack-" + Guid.NewGuid().ToString("N") + ".wav");
        File.Copy(LogicaCensura.Pitido(), sfx);
        Media.LargoFalso = 800;
        o.Sfx = sfx; o.Encaje = Encaje.Centrado; o.Silenciar = false;
        int antes = p.Tracks.Count;
        CensuraVegas.Aplicar(p, new List<Coincidencia> { c[1] }, o);
        TrackEvent cuack = p.Tracks[p.Tracks.Count - 1].Events[0];
        Verificar(p.Tracks.Count == antes + 1 && Cerca(cuack.Length.ToMilliseconds() / 1000, 0.8) && Cerca(cuack.Start.ToMilliseconds() / 1000, 12.75),
            "Aplicar: efecto entero centrado en la palabra");
        File.Delete(sfx);
        o.Sfx = "-";
        antes = p.Tracks.Count;
        o.Silenciar = true;
        CensuraVegas.Aplicar(p, new List<Coincidencia> { c[2] }, o);
        Verificar(p.Tracks.Count == antes, "Sin efecto: solo silencia, sin pista nueva");

        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " pruebas fallaron.");
        return fallos == 0 ? 0 : 1;
    }
}
