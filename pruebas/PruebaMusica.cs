// Pruebas de MusicaAutomatica: curva de volumen y envolvente con la API falsa
// (ver pruebas/ejecutar.sh).
using System;
using System.Collections.Generic;
using ScriptPortal.Vegas;

class PruebaMusica
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static bool Cerca(double a, double b) { return Math.Abs(a - b) < 0.02; }

    static string Ver(List<PuntoVolumen> l)
    {
        List<string> s = new List<string>();
        foreach (PuntoVolumen p in l) s.Add(p.T.ToString("0.00") + ":" + p.Db.ToString("0"));
        return String.Join(" ", s.ToArray());
    }

    static List<Rango> R(params double[] x)
    {
        List<Rango> l = new List<Rango>();
        for (int i = 0; i < x.Length; i += 2) l.Add(new Rango(x[i], x[i + 1]));
        return l;
    }

    static int Main()
    {
        // ------------------------------------------------------------ curva
        List<Rango> voz = LogicaMusica.Voz(R(0, 2, 5, 6), 0, 10);
        Verificar(voz.Count == 2 && Cerca(voz[0].Inicio, 2) && Cerca(voz[0].Fin, 5) && Cerca(voz[1].Inicio, 6) && Cerca(voz[1].Fin, 10),
            "Voz: lo que no es silencio");

        string a = Ver(LogicaMusica.Puntos(R(2, 5), 0, 10, -14, 0.25, 0.6));
        Verificar(a == "0.00:0 1.75:0 2.00:-14 5.00:-14 5.60:0 10.00:0", "Curva: baja antes de hablar y sube al terminar (" + a + ")");

        string b = Ver(LogicaMusica.Puntos(R(2, 5, 5.5, 7), 0, 10, -14, 0.25, 0.6));
        Verificar(b == "0.00:0 1.75:0 2.00:-14 7.00:-14 7.60:0 10.00:0", "Curva: no sube entre dos frases muy juntas (" + b + ")");

        string c = Ver(LogicaMusica.Puntos(R(0, 3), 0, 10, -14, 0.25, 0.6));
        Verificar(c == "0.00:-14 3.00:-14 3.60:0 10.00:0", "Curva: si ya hablan al empezar, empieza abajo (" + c + ")");

        string d = Ver(LogicaMusica.Puntos(R(2, 5), 4, 10, -14, 0.25, 0.6));
        Verificar(d == "4.00:-14 5.00:-14 5.60:0 10.00:0", "Curva: rango que empieza a media frase (" + d + ")");

        List<PuntoVolumen> sinVoz = LogicaMusica.Puntos(new List<Rango>(), 0, 10, -14, 0.25, 0.6);
        Verificar(Ver(sinVoz) == "0.00:0 10.00:0" && LogicaMusica.Bajadas(sinVoz) == 0, "Curva: sin voz queda plana");
        Verificar(LogicaMusica.Bajadas(LogicaMusica.Puntos(R(2, 3, 6, 7), 0, 10, -14, 0.25, 0.6)) == 2 &&
                  LogicaMusica.Bajadas(LogicaMusica.Puntos(R(0, 3), 0, 10, -14, 0.25, 0.6)) == 1, "Curva: cuenta las bajadas");
        Verificar(Cerca(LogicaMusica.Ganancia(0), 1) && Cerca(LogicaMusica.Ganancia(-14), 0.1995) && LogicaMusica.Ganancia(-100) == 0,
            "Ganancia lineal de la envolvente");

        // ---------------------------------------- deteccion con dos pistas
        // Pista 1 habla de 1.5 a 3 y de 3.5 a 5 (pausa corta); pista 2 de 7.5 a 8.5.
        Analisis p1 = new Analisis(), p2 = new Analisis();
        p1.Db = new float[1000]; p2.Db = new float[1000];
        for (int i = 0; i < 1000; i++)
        {
            double t = i * Analisis.Paso;
            p1.Db[i] = (t >= 1.5 && t < 3) || (t >= 3.5 && t < 5) ? -18f : -62f + (i % 7);
            p2.Db[i] = t >= 7.5 && t < 8.5 ? -25f : -70f + (i % 5);
        }
        List<Analisis> pistas = new List<Analisis> { p1, p2 };
        List<double> umbrales = new List<double> { Detector.UmbralAutomatico(p1.Db), Detector.UmbralAutomatico(p2.Db) };
        List<Rango> hablan = LogicaMusica.Voz(Detector.Detectar(pistas, umbrales, LogicaMusica.Deteccion(1200)), 0, 10);
        Verificar(hablan.Count == 2 && Cerca(hablan[0].Inicio, 1.5) && Cerca(hablan[0].Fin, 5) && Cerca(hablan[1].Inicio, 7.5) && Cerca(hablan[1].Fin, 8.5),
            "Detección: cada pista con su umbral y las pausas cortas no suben la música");
        List<Rango> corta = LogicaMusica.Voz(Detector.Detectar(pistas, umbrales, LogicaMusica.Deteccion(300)), 0, 10);
        Verificar(corta.Count == 3, "Detección: con pausa mínima corta sí sube entre frases");

        // ------------------------------------------------------ envolvente
        AudioTrack musica = new AudioTrack(3, "Música");
        List<PuntoVolumen> puntos = LogicaMusica.Puntos(R(2, 5), 0, 10, -14, 0.25, 0.6);
        LogicaMusica.Aplicar(musica, puntos, 0, 10);
        Envelope env = musica.Envelopes.FindByType(EnvelopeType.Volume);
        Verificar(env != null && env.Points.Count == puntos.Count && Cerca(env.Points[2].Y, 0.1995) && Cerca(env.Points[2].X.ToMilliseconds(), 2000),
            "Envolvente: se crea con un punto por cambio");
        LogicaMusica.Aplicar(musica, puntos, 0, 10);
        Verificar(env.Points.Count == puntos.Count, "Envolvente: aplicar dos veces no duplica puntos");

        // Solo cambia dentro del rango; lo de fuera (tus ajustes) se queda.
        env.Points.Add(new EnvelopePoint(Timecode.FromMilliseconds(30000), 0.5));
        env.Points.Add(new EnvelopePoint(Timecode.FromMilliseconds(45000), 0.7));
        LogicaMusica.Aplicar(musica, LogicaMusica.Puntos(R(), 20, 40, -14, 0.25, 0.6), 20, 40);
        bool queda45 = false, quedo30 = false;
        foreach (EnvelopePoint pt in env.Points)
        {
            if (Cerca(pt.X.ToMilliseconds(), 45000) && Cerca(pt.Y, 0.7)) queda45 = true;
            if (Cerca(pt.X.ToMilliseconds(), 30000)) quedo30 = true;
        }
        Verificar(queda45 && !quedo30 && Cerca(env.Points[2].Y, 0.1995), "Envolvente: solo reemplaza el rango elegido");

        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " pruebas fallaron.");
        return fallos == 0 ? 0 : 1;
    }
}
