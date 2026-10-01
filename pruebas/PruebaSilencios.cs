// Pruebas de QuitarSilencios.cs sin Vegas (ver pruebas/ejecutar.sh).
using System; using System.Collections.Generic; using ScriptPortal.Vegas;

class PruebaSilencios
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static bool Cerca(double a, double b) { return Math.Abs(a - b) < 0.02; }

    static TrackEvent Evento(Track t, double ini, double dur)
    {
        TrackEvent e = t.IsAudio() ? (TrackEvent)new AudioEvent() : new VideoEvent();
        e.Start = new Timecode(ini * 1000); e.Length = new Timecode(dur * 1000); e.Track = t;
        t.Events.Add(e); return e;
    }

    // Nivel -20 dB donde hay voz y -60 dB en el resto, en pasos de 10 ms.
    static Analisis Sintetico(double segundos, double[] voz) { return Sintetico(segundos, voz, -20, -60); }

    static Analisis Sintetico(double segundos, double[] voz, float nivelVoz, float nivelRuido)
    {
        Analisis a = new Analisis();
        a.Db = new float[(int)(segundos / Analisis.Paso)];
        for (int i = 0; i < a.Db.Length; i++)
        {
            double t = i * Analisis.Paso;
            a.Db[i] = nivelRuido + (float)(Math.Sin(i * 1.7) * 2); // ruido que varia un poco
            for (int k = 0; k < voz.Length; k += 2) if (t >= voz[k] && t < voz[k + 1]) a.Db[i] = nivelVoz + (float)(Math.Sin(i * 0.9) * 3);
        }
        return a;
    }

    static int Main()
    {
        // Audio de prueba (generar_wav.py): voz 1-3, 3.3-4.8, clic 6.0-6.05, 7.05-9.55, 12.55-13.55, 14.15-16.15
        foreach (string f in new string[] { "voz16.wav", "voz24.wav", "vozf32.wav" })
        {
            Analisis x = WavNiveles.Leer(f, Analisis.Paso);
            Verificar(x.Db.Length == 1815 && Math.Abs(x.Db[150] + 20) < 0.5 && Math.Abs(x.Db[50] + 60) < 1, "lee " + f);
        }

        Analisis a = WavNiveles.Leer("voz16.wav", Analisis.Paso);
        Ajustes aj = new Ajustes();
        double umbral = Detector.UmbralAutomatico(a.Db);
        Verificar(umbral < -30 && umbral > -55, "umbral autom\u00e1tico entre ruido y voz (" + umbral + ")");
        aj.SilencioMinMs = 500; aj.HablaMinMs = 150; aj.MargenAntesMs = 150; aj.MargenDespuesMs = 250;
        aj.PedazoMinMs = 0; aj.SuavizadoMs = 0; aj.Sensibilidad = 0;
        List<Rango> r = Detector.Detectar(a, umbral, aj);
        double[,] esperado = { { 0, 0.85 }, { 5.05, 6.90 }, { 9.80, 12.40 }, { 13.80, 14.00 }, { 16.40, 18.15 } };
        bool bien = r.Count == 5;
        for (int i = 0; bien && i < 5; i++) bien = Cerca(r[i].Inicio, esperado[i, 0]) && Cerca(r[i].Fin, esperado[i, 1]);
        Verificar(bien, "detecta 5 silencios con m\u00e1rgenes, ignora la pausa de 0.3 s y el clic");

        // Proyecto falso: voz y video 0-18.15, un t\u00edtulo 5-8, marcador dentro de un silencio (12 s)
        Project p = new Project();
        VideoTrack vt = new VideoTrack(); vt.Index = 0; p.Tracks.Add(vt);
        AudioTrack at = new AudioTrack(); at.Index = 1; p.Tracks.Add(at);
        VideoTrack tt = new VideoTrack(); tt.Index = 2; p.Tracks.Add(tt);
        Evento(vt, 0, 18.15); Evento(at, 0, 18.15); Evento(tt, 5, 3);
        Marker m = new Marker(); m.Position = new Timecode(12000); p.Markers.Add(m);

        List<Rango> rf = Editor.AjustarAFotogramas(r, 59.94);
        double quitado = 0; foreach (Rango x in rf) quitado += x.Fin - x.Inicio;
        Editor.Eliminar(p, new List<Track>(p.Tracks), rf, true, true, 0);

        foreach (Track t in new Track[] { vt, at })
        {
            List<TrackEvent> l = new List<TrackEvent>(t.Events);
            l.Sort(delegate (TrackEvent x, TrackEvent y) { return x.Start.ms.CompareTo(y.Start.ms); });
            bool continuo = Cerca(l[0].Start.ms / 1000, 0);
            for (int i = 1; i < l.Count; i++) continuo &= Cerca(l[i].Start.ms, l[i - 1].End.ms);
            Verificar(l.Count == 4 && continuo && Cerca(l[l.Count - 1].End.ms / 1000, 18.15 - quitado),
                "pista " + t.Index + ": 4 piezas continuas que terminan en " + (18.15 - quitado).ToString("0.00"));
        }
        Verificar(Cerca(m.Position.ms / 1000, 7.09), "marcador dentro de un silencio queda en el corte");

        // Silenciar: no mueve nada, solo silencia audio
        Project p2 = new Project();
        AudioTrack a2 = new AudioTrack(); p2.Tracks.Add(a2); Evento(a2, 0, 18.15);
        Editor.Silenciar(new List<Track>(p2.Tracks), rf, 0);
        int mudos = 0; foreach (TrackEvent e in a2.Events) if (e.Mute) mudos++;
        Verificar(mudos == 5 && a2.Events.Count == 9, "silenciar: 5 tramos mudos, nada se mueve");

        Editor.Marcar(p2, rf);
        Verificar(p2.Regions.Count == 5, "marcar: 5 regiones");

        // Varias pistas: A habla 1-3 s, B habla 2.5-5 s y 8-9 s (se encima con A).
        // El juego suena siempre pero no se analiza. Hay voz si habla cualquiera.
        Analisis va = Sintetico(10, new double[] { 1, 3 });
        Analisis vb = Sintetico(10, new double[] { 2.5, 5, 8, 9 });
        Ajustes aj2 = new Ajustes();
        aj2.SilencioMinMs = 500; aj2.HablaMinMs = 150; aj2.MargenAntesMs = 0; aj2.MargenDespuesMs = 0;
        aj2.PedazoMinMs = 0; aj2.SuavizadoMs = 0; aj2.Sensibilidad = 0;
        List<Rango> r2 = Detector.Detectar(new List<Analisis> { va, vb }, new List<double> { -40, -40 }, aj2);
        Verificar(r2.Count == 3 && Cerca(r2[0].Fin, 1) && Cerca(r2[1].Inicio, 5) && Cerca(r2[1].Fin, 8) && Cerca(r2[2].Inicio, 9),
            "dos voces: silencios 0-1, 5-8 y 9-10; el tramo encimado 2.5-3 se conserva");
        List<Rango> soloA = Detector.Detectar(va, -40, aj2);
        Verificar(soloA.Count == 2 && Cerca(soloA[1].Inicio, 3), "solo la voz A: cortar\u00eda lo que dice B (por eso se marcan ambas)");

        // Dejar huecos: quita sin mover lo demas
        Project p3 = new Project();
        AudioTrack a3 = new AudioTrack(); p3.Tracks.Add(a3); Evento(a3, 0, 10);
        VideoTrack juego = new VideoTrack(); juego.Index = 1; p3.Tracks.Add(juego); Evento(juego, 0, 10);
        Editor.Eliminar(p3, new List<Track>(p3.Tracks), r2, false, false, 0);
        List<TrackEvent> l3 = new List<TrackEvent>(a3.Events);
        l3.Sort(delegate (TrackEvent x, TrackEvent y) { return x.Start.ms.CompareTo(y.Start.ms); });
        Verificar(l3.Count == 2 && Cerca(l3[0].Start.ms / 1000, 1) && Cerca(l3[1].Start.ms / 1000, 8) && juego.Events.Count == 2,
            "dejar huecos: quedan 1-5 y 8-9 en su lugar, tambi\u00e9n en la pista del juego");

        // Umbral por pista: A tiene voz a -20 y ruido a -60; B es mas baja, voz a -38
        // y ruido a -52 (con -40 fijo, B nunca tendria voz).
        Analisis pa = Sintetico(10, new double[] { 1, 3 }, -20, -60);
        Analisis pb = Sintetico(10, new double[] { 6, 8 }, -38, -52);
        double ua = Detector.UmbralAutomatico(pa.Db), ub = Detector.UmbralAutomatico(pb.Db);
        Verificar(ua > -60 && ua <= -20 && ub > -52 && ub <= -38, "umbral propio por pista (A " + ua + " dB, B " + ub + " dB)");
        List<Rango> r4 = Detector.Detectar(new List<Analisis> { pa, pb }, new List<double> { ua, ub }, aj2);
        Verificar(r4.Count == 3 && Cerca(r4[1].Inicio, 3) && Cerca(r4[1].Fin, 6), "con umbral por pista se respeta la voz baja de B");
        List<Rango> r5 = Detector.Detectar(new List<Analisis> { pa, pb }, new List<double> { -40, -40 }, aj2);
        Verificar(r5.Count == 2, "con un solo umbral (-40) se perder\u00eda la voz de B");
        aj2.Sensibilidad = 15; // sube todos los umbrales: B queda debajo del suyo
        Verificar(Detector.Detectar(new List<Analisis> { pa, pb }, new List<double> { ua, ub }, aj2).Count == 2, "la sensibilidad mueve todos los umbrales");
        aj2.Sensibilidad = 0;

        // Clip minimo: voz 1-1.3 s (clip de 0.3 s entre dos silencios) y 5-8 s
        Analisis pc = Sintetico(10, new double[] { 1, 1.3, 5, 8 }, -20, -60);
        aj2.HablaMinMs = 100; aj2.PedazoMinMs = 0;
        Verificar(Detector.Detectar(pc, -40, aj2).Count == 3, "sin clip m\u00ednimo: 3 silencios");
        aj2.PedazoMinMs = 800;
        List<Rango> r6 = Detector.Detectar(pc, -40, aj2);
        Verificar(r6.Count == 2 && Cerca(r6[0].Fin, 1) && Cerca(r6[1].Inicio, 8), "clip m\u00ednimo 0.8 s: no deja el clip de 0.3 s suelto");

        // Suavizado: fundidos en el audio en cada corte
        Project p4 = new Project();
        AudioTrack a4 = new AudioTrack(); p4.Tracks.Add(a4); Evento(a4, 0, 10);
        List<Rango> r7 = new List<Rango> { new Rango(3, 5) };
        Editor.Eliminar(p4, new List<Track>(p4.Tracks), r7, true, false, 0.02);
        List<TrackEvent> l4 = new List<TrackEvent>(a4.Events);
        l4.Sort(delegate (TrackEvent x, TrackEvent y) { return x.Start.ms.CompareTo(y.Start.ms); });
        Verificar(l4.Count == 2 && Cerca(l4[0].FadeOut.Length.ms, 20) && Cerca(l4[0].FadeIn.Length.ms, 0) &&
                  Cerca(l4[1].FadeIn.Length.ms, 20) && Cerca(l4[1].FadeOut.Length.ms, 0), "suavizado: fundido de 20 ms solo en los bordes del corte");

        // Perfiles: los incluidos tienen sentido y se guardan/leen igual
        bool orden = true;
        foreach (Perfil_ pf in Perfil_.Incluidos)
            orden &= pf.HablaMinMs < pf.SilencioMinMs && pf.MargenAntesMs + pf.MargenDespuesMs < pf.SilencioMinMs;
        Verificar(Perfil_.Incluidos.Length == 5 && orden, "perfiles incluidos: los m\u00e1rgenes caben en el silencio m\u00ednimo");
        // Grupos: video + 3 audios del mismo clip, agrupados como los pone Vegas.
        Project pg = new Project();
        List<TrackEvent> clip = new List<TrackEvent>();
        TrackEventGroup g0 = new TrackEventGroup(); pg.Groups.Add(g0);
        for (int i = 0; i < 4; i++)
        {
            Track t = i == 0 ? (Track)new VideoTrack(i, "") : new AudioTrack(i, "");
            pg.Tracks.Add(t);
            TrackEvent e = Evento(t, 0, 30);
            g0.Add(e);
        }
        Editor.Eliminar(pg, new List<Track>(pg.Tracks), new List<Rango> { new Rango(5, 6), new Rango(12, 14) }, true, false, 0);
        bool gruposBien = pg.Groups.Count == 3;
        foreach (Track t in pg.Tracks)
            foreach (TrackEvent e in t.Events)
            {
                int juntos = 0;
                foreach (TrackEvent o in e.Group) if (Cerca(o.Start.ms, e.Start.ms) && Cerca(o.End.ms, e.End.ms)) juntos++;
                gruposBien &= e.Group.Count == 4 && juntos == 4;
            }
        Verificar(gruposBien, "cada pedazo queda en su grupo con sus 3 audios (no todos unidos)");
        Verificar(Editor.Reagrupar(pg) == 0, "volver a desenlazar no cambia nada");

        // Proyecto ya roto (como lo dejaba la versión anterior): todo en un grupo.
        Project pr = new Project();
        TrackEventGroup todo = new TrackEventGroup(); pr.Groups.Add(todo);
        for (int i = 0; i < 2; i++)
        {
            Track t = i == 0 ? (Track)new VideoTrack(i, "") : new AudioTrack(i, "");
            pr.Tracks.Add(t);
            for (int k = 0; k < 3; k++) todo.Add(Evento(t, k * 10, 10));
        }
        Verificar(Editor.Reagrupar(pr) == 2 && pr.Groups.Count == 3 && todo.Count == 2, "desenlaza un proyecto ya unido de más");

        Perfil_ copia = new Perfil_();
        foreach (string linea in Perfil_.Incluidos[3].Texto().Split('\n'))
        {
            int i = linea.IndexOf('=');
            if (i > 0) copia.Leer(linea.Substring(0, i), linea.Substring(i + 1));
        }
        Verificar(copia.Igual(Perfil_.Incluidos[3]), "un perfil guardado se lee igual");

        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos;
    }
}
