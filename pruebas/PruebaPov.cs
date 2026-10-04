using System;
using System.Collections.Generic;
using ScriptPortal.Vegas;

// Pruebas de VariosPOV: sincronizar por el audio y elegir cuando mostrar el otro POV.
class PruebaPov
{
    static int fallos;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }

    // Niveles cada 10 ms de una conversacion: rafagas de voz sobre ruido.
    static float[] Charla(Random r, int pasos)
    {
        float[] db = new float[pasos];
        for (int i = 0; i < pasos; i++) db[i] = -55 + (float)r.NextDouble() * 4;
        int t = 0;
        while (t < pasos)
        {
            t += 20 + r.Next(250);
            int largo = 30 + r.Next(200);
            for (int k = t; k < Math.Min(pasos, t + largo); k++) db[k] = -18 + (float)r.NextDouble() * 6;
            t += largo;
        }
        return db;
    }

    static int Main()
    {
        // ---- sincronizar: el otro POV empezo a grabar 7.3 s despues, con otro volumen y su propio ruido de juego
        Random r = new Random(7);
        float[] llamada = Charla(r, 90000);   // 15 min
        int retraso = 730;                      // 7.3 s
        float[] otro = new float[80000];
        Random r2 = new Random(11);
        for (int i = 0; i < otro.Length; i++)
        {
            float v = i + retraso < llamada.Length ? llamada[i + retraso] - 6 : -55;
            if (r2.NextDouble() < 0.02) v = Math.Max(v, -25);           // golpes del juego
            otro[i] = v + (float)r2.NextDouble() * 3;
        }
        double conf;
        double d = LogicaPov.Desfase(LogicaPov.Envolvente(llamada, 2), LogicaPov.Envolvente(otro, 2), 0.02, 600, out conf);
        Verificar(Math.Abs(d - 7.3) <= 0.02 && conf > 8, "Sincronizar: encuentra el desfase por el audio (" + d.ToString("0.00") + " s, confianza " + conf.ToString("0.0") + ")");
        double conf2;
        double d2 = LogicaPov.Desfase(LogicaPov.Envolvente(otro, 2), LogicaPov.Envolvente(llamada, 2), 0.02, 600, out conf2);
        Verificar(Math.Abs(d2 + 7.3) <= 0.02, "Sincronizar: al revés da el desfase negativo (" + d2.ToString("0.00") + " s)");
        double conf3;
        LogicaPov.Desfase(LogicaPov.Envolvente(llamada, 2), LogicaPov.Envolvente(Charla(new Random(99), 80000), 2), 0.02, 600, out conf3);
        Verificar(conf3 < 6, "Sincronizar: con audios que no tienen nada que ver, la confianza es baja (" + conf3.ToString("0.0") + ")");

        // ---- mover las pistas del otro POV
        Project p = new Project();
        VideoTrack vp = new VideoTrack(0, "Yo"); p.Tracks.Add(vp);
        VideoTrack vo = new VideoTrack(1, "Jason"); p.Tracks.Add(vo);
        AudioTrack ao = new AudioTrack(2, "Jason mic"); p.Tracks.Add(ao);
        Media mj = new Media("C:\\pov\\jason.mp4"), my = new Media("C:\\pov\\yo.mp4");
        vp.AddVideoEvent(TC(0), TC(600)).ActiveTake = new Take { Media = my };
        VideoEvent ev = vo.AddVideoEvent(TC(0), TC(500)); ev.ActiveTake = new Take { Media = mj, Offset = TC(0) };
        AudioEvent ea = ao.AddAudioEvent(TC(0), TC(500)); ea.ActiveTake = new Take { Media = mj, Offset = TC(0) };
        List<Track> mismas = LogicaPov.MismoArchivo(p, ao);
        LogicaPov.Mover(mismas, -2.5);
        Verificar(mismas.Count == 2 && mismas.Contains(vo) && !mismas.Contains(vp) && S(vo.Events[0].Start) == 0 && Math.Abs(S(vo.Events[0].Length) - 497.5) < 0.001 &&
                  Math.Abs(S(vo.Events[0].ActiveTake.Offset) - 2.5) < 0.001 && Math.Abs(S(ao.Events[0].ActiveTake.Offset) - 2.5) < 0.001,
                  "Mover: se mueven juntas las pistas del mismo archivo; lo que quedaría antes del cero se recorta");
        LogicaPov.Mover(mismas, 4);
        Verificar(S(vo.Events[0].Start) == 4 && S(ao.Events[0].Start) == 4, "Mover: hacia la derecha también");

        // ---- cambios de POV
        Transcripcion t = new Transcripcion();
        t.Hablantes.Add(new Hablante { Etiqueta = "A3", Nombre = "Jason", Voz = true });
        t.Segmentos.Add(new Segmento { Hablante = 0, Inicio = 100, Fin = 103, Texto = "¡Mira, mira mi pantalla!" });
        string msg = LogicaPov.Mensaje(t, p, "Jason", 600);
        string ins = LogicaPov.Instrucciones("Jason", 10, 45);
        List<TramoPov> tr = LogicaPov.Leer("{\"tramos\": [{\"inicio\": 99, \"fin\": 112, \"motivo\": \"pide que miren\"}, {\"inicio\": 130, \"fin\": 140}, " +
            "{\"inicio\": 200, \"fin\": 260, \"motivo\": \"muere\"}, {\"inicio\": 400, \"fin\": 401}, {\"inicio\": 300, \"fin\": 330}]}", 600, 15, 45, t);
        List<TramoPov> tr10 = LogicaPov.Leer("{\"tramos\": [{\"inicio\": 99, \"fin\": 112}, {\"inicio\": 200, \"fin\": 260}, {\"inicio\": 300, \"fin\": 330}]}", 600, 10, 45, t);
        Verificar(msg.Contains("Jason: ¡Mira, mira mi pantalla!") && ins.Contains("10 %") && ins.Contains("«mira»") &&
                  tr.Count == 3 && tr[0].Inicio == 99 && tr[0].Dicho.Contains("Mira") && tr[1].Inicio == 200 && tr[1].Fin == 225 && tr[2].Inicio == 300 && tr[2].Fin == 325 && tr10.Count == 2,
                  "Cambios de POV: tramos de 3–25 s, separados y sin pasar el máximo del video");

        Project q = new Project();
        VideoTrack qp = new VideoTrack(0, "Yo"); q.Tracks.Add(qp);
        VideoTrack qo = new VideoTrack(1, "Jason"); q.Tracks.Add(qo);
        AudioTrack qj = new AudioTrack(2, "Juego Jason"); q.Tracks.Add(qj);
        qp.AddVideoEvent(TC(0), TC(600)); qo.AddVideoEvent(TC(0), TC(600)); qj.AddAudioEvent(TC(0), TC(600));
        int n = LogicaPov.Aplicar(qp, qo, qj, tr);
        int visiblesOtro = 0, ocultosPrincipal = 0;
        foreach (TrackEvent e in qo.Events) if (!e.Mute) visiblesOtro++;
        foreach (TrackEvent e in qp.Events) if (e.Mute) ocultosPrincipal++;
        TrackEvent en100 = null;
        foreach (TrackEvent e in qo.Events) if (S(e.Start) == 99) en100 = e;
        Verificar(n == 3 && visiblesOtro == 3 && ocultosPrincipal == 3 && qp.Events.Count == 7 && en100 != null && !en100.Mute && S(en100.Length) == 13 &&
                  qj.Events.FindAll(delegate (TrackEvent e) { return !e.Mute; }).Count == 3,
                  "Aplicar: el otro POV (y su juego) se ve solo en esos tramos y el principal se silencia ahí");
        LogicaPov.Aplicar(qp, qo, qj, new List<TramoPov>());
        Verificar(qp.Events.TrueForAll(delegate (TrackEvent e) { return !e.Mute; }) && qo.Events.TrueForAll(delegate (TrackEvent e) { return e.Mute; }),
                  "Quitar cambios: vuelve a verse solo el principal");

        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos == 0 ? 0 : 1;
    }
}
