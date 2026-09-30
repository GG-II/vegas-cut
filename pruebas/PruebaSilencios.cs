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
        aj.UmbralDb = Detector.UmbralAutomatico(a.Db);
        Verificar(aj.UmbralDb < -30 && aj.UmbralDb > -55, "umbral autom\u00e1tico entre ruido y voz (" + aj.UmbralDb + ")");
        aj.SilencioMinMs = 500; aj.HablaMinMs = 150; aj.MargenAntesMs = 150; aj.MargenDespuesMs = 250;
        List<Rango> r = Detector.Detectar(a, aj);
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
        Editor.Eliminar(p, new List<Track>(p.Tracks), rf, true);

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
        Editor.Silenciar(new List<Track>(p2.Tracks), rf);
        int mudos = 0; foreach (TrackEvent e in a2.Events) if (e.Mute) mudos++;
        Verificar(mudos == 5 && a2.Events.Count == 9, "silenciar: 5 tramos mudos, nada se mueve");

        Editor.Marcar(p2, rf);
        Verificar(p2.Regions.Count == 5, "marcar: 5 regiones");

        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos;
    }
}
