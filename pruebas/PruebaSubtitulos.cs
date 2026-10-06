using System;
using System.Collections.Generic;
using System.IO;

// Pruebas de Subtitulos: armar, glosario, censura, respuesta de Gemini y memoria.
class PruebaSubtitulos
{
    static int fallos;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }

    static Segmento Seg(int h, params object[] ws)
    {
        Segmento s = new Segmento { Hablante = h };
        for (int i = 0; i < ws.Length; i += 3)
            s.Palabras.Add(new Palabra { Texto = " " + (string)ws[i], Inicio = Convert.ToDouble(ws[i + 1]), Fin = Convert.ToDouble(ws[i + 2]), Prob = 0.9 });
        s.Inicio = s.Palabras[0].Inicio; s.Fin = s.Palabras[s.Palabras.Count - 1].Fin;
        return s;
    }

    static int Main()
    {
        Transcripcion t = new Transcripcion();
        t.Hablantes.Add(new Hablante { Etiqueta = "A2", Nombre = "Gerbert", Voz = true });
        t.Hablantes.Add(new Hablante { Etiqueta = "A3", Nombre = "Jason", Voz = true });
        t.Segmentos.Add(Seg(0, "Hola", 1.0, 1.3, "a", 1.3, 1.4, "todos.", 1.4, 1.8, "Hoy", 1.9, 2.1, "vamos", 2.1, 2.4, "a", 2.4, 2.5, "jugar", 2.5, 2.9, "con", 2.9, 3.0, "Steb.", 3.0, 3.4));
        t.Segmentos.Add(Seg(1, "Qué", 3.5, 3.7, "puta", 3.7, 3.9, "madre,", 3.9, 4.2, "me", 4.2, 4.3, "morí.", 4.3, 4.6));
        t.Segmentos.Add(Seg(0, "Ya", 9.0, 9.1));
        t.Segmentos[0].Palabras[8].Prob = 0.3;
        OpcionesSub op = new OpcionesSub();
        List<Subtitulo> subs = LogicaSubtitulos.Armar(t, op);
        Verificar(subs.Count == 3 && subs[0].Texto == "Hola a todos. Hoy vamos a jugar con Steb." && subs[0].Quien == "Gerbert" &&
                  subs[1].Quien == "Jason" && subs[0].Fin <= subs[1].Inicio && subs[0].Dudosas.Contains("Steb") &&
                  subs[2].Texto == "Ya" && Math.Abs(subs[2].Fin - 10.0) < 0.01,
                  "Subtítulos: uno por frase y por persona, sin encimarse, con lo dudoso marcado y al menos 1 s en pantalla");
        string l = LogicaSubtitulos.Lineas("Hola a todos. Hoy vamos a jugar con Steve y con los demás del servidor", 42);
        string srt = LogicaSubtitulos.Srt(subs, op);
        op.Nombres = true;
        string srtN = LogicaSubtitulos.Srt(subs, op);
        Verificar(l.Contains("\r\n") && l.Split('\n')[0].Length <= 43 && srt.StartsWith("1\r\n00:00:01,000 --> 00:00:03,400\r\n") &&
                  srt.Contains("3\r\n00:00:09,000 --> 00:00:10,000\r\nYa") && srtN.Contains("Jason: Qué puta madre") && LogicaSubtitulos.Tiempo(3661.5) == "01:01:01,500",
                  "Subtítulos: formato .srt, dos líneas parejas y el nombre cuando cambia quien habla");

        MemoriaSub m = new MemoriaSub();
        m.LeerGlosario("Steb => Steve\r\njojo mania=>JoJomanía\r\nsin flecha\r\n");
        int g = LogicaSubtitulos.AplicarGlosario(subs, m);
        List<Subtitulo> copia = new List<Subtitulo> { new Subtitulo { Texto = subs[1].Texto } };
        int tap = LogicaSubtitulos.Censurar(copia, OpcionesCensura.CargarPalabras());
        Verificar(m.Glosario.Count == 2 && g == 1 && subs[0].Texto.EndsWith("con Steve.") && tap == 1 && copia[0].Texto.StartsWith("Qué p*** m****,") &&
                  subs[1].Texto.Contains("puta madre"),
                  "Subtítulos: el glosario corrige antes de Gemini y la censura tapa las palabrotas solo en el .srt");

        string resp = "{\"cambios\": [{\"id\": 2, \"texto\": \"¡Qué puta madre, me morí!\"}, {\"id\": 1, \"texto\": \"Algo que no tiene nada que ver con lo que se dijo en el video\"}]," +
                      "\"preguntas\": [{\"ids\": [3], \"fragmento\": \"Ya\", \"pregunta\": \"¿Dice «Ya» o «Va»?\", \"opciones\": [\"Va\", \"Ya\"]}, {\"ids\": [99], \"fragmento\": \"x\"}]," +
                      "\"glosario\": [{\"mal\": \"Steb\", \"bien\": \"Steve\"}]}";
        List<PreguntaSub> pr = new List<PreguntaSub>();
        MemoriaSub apr = new MemoriaSub();
        int n = LogicaSubtitulos.Leer(resp, subs, pr, apr);
        pr[0].Respuesta = "Va";
        int resp1 = LogicaSubtitulos.Responder(pr[0], subs);
        Verificar(n == 1 && subs[1].Texto == "¡Qué puta madre, me morí!" && subs[1].Nota.StartsWith("antes:") && subs[0].Texto.EndsWith("Steve.") &&
                  pr.Count == 1 && pr[0].Opciones.Count == 2 && apr.Glosario.Count == 1 && resp1 == 1 && subs[2].Texto == "Va",
                  "Subtítulos: Gemini corrige sin reescribir (lo que cambia demasiado se ignora), pregunta lo dudoso y tu respuesta se aplica");

        m.Indicaciones = "Los jugadores son Gerbert y Jason.";
        LogicaSubtitulos.Guardar("SCR", m);
        LogicaSubtitulos.Guardar("", new MemoriaSub { Indicaciones = "Sin puntos al final." });
        MemoriaSub ms = LogicaSubtitulos.Cargar("SCR"), mg = LogicaSubtitulos.Cargar(""), mj = LogicaSubtitulos.Juntar(mg, ms), otra = LogicaSubtitulos.Cargar("Otra");
        string msg = LogicaSubtitulos.Mensaje(subs, new List<string> { "Gerbert", "Jason" }, "SCR: carrera", mj);
        Verificar(ms.Glosario.Count == 2 && ms.Indicaciones.StartsWith("Los jugadores") && mg.Glosario.Count == 0 && otra.Indicaciones == "" &&
                  mj.Indicaciones.Contains("Sin puntos") && mj.Indicaciones.Contains("Gerbert y Jason") &&
                  msg.Contains("GLOSARIO") && msg.Contains("[1] Gerbert:") && msg.Contains("PERSONAS: Gerbert, Jason") && msg.Contains("DUDOSAS: Steb"),
                  "Subtítulos: indicaciones y glosario se guardan por serie y para todos, y van a Gemini junto con lo dudoso");
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " pruebas fallaron.");
        return fallos == 0 ? 0 : 1;
    }
}
