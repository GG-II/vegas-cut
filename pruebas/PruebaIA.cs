// Pruebas de Transcripcion, Whisper, Gemini y Momentos sin Vegas ni internet
// (ver pruebas/ejecutar.sh). Whisper se imita con un script falso y Gemini con
// un servidor local.
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using ScriptPortal.Vegas;

class PruebaIA
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static bool Cerca(double a, double b) { return Math.Abs(a - b) < 0.02; }

    // Salida tipica de Faster-Whisper-XXL (--output_format json --word_timestamps True)
    const string WhisperJson = @"{""text"": "" Hola a todos. Vamos a construir."", ""segments"": [
      {""id"": 0, ""start"": 1.0, ""end"": 2.5, ""text"": "" Hola a todos."", ""words"": [
        {""start"": 1.0, ""end"": 1.4, ""word"": "" Hola"", ""probability"": 0.98},
        {""start"": 1.4, ""end"": 1.6, ""word"": "" a"", ""probability"": 0.95},
        {""start"": 1.6, ""end"": 2.5, ""word"": "" todos."", ""probability"": 0.97}]},
      {""id"": 1, ""start"": 6.0, ""end"": 8.0, ""text"": "" Vamos a construir."", ""words"": [
        {""start"": 6.0, ""end"": 6.5, ""word"": "" Vamos"", ""probability"": 0.9},
        {""start"": 6.5, ""end"": 6.7, ""word"": "" a"", ""probability"": 0.9},
        {""start"": 6.7, ""end"": 8.0, ""word"": "" construir."", ""probability"": 0.9}]}],
      ""language"": ""es""}";

    static int Main()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "vegascut-pruebas-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        // ------------------------------------------------------------ JSON
        string raro = "comillas \" barra \\ salto\n acento á emoji 😀";
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["texto"] = raro; d["n"] = -1.5e3; d["lista"] = new List<object> { 1.0, true, null, "x" };
        object vuelta = Json.Leer(Json.Escribir(d));
        Verificar(Json.Texto(vuelta, "texto") == raro && Json.Numero(vuelta, "n", 0) == -1500 &&
                  Json.Lista(vuelta, "lista").Count == 4, "JSON: escribir y leer conserva textos raros y números");
        Verificar(Json.Texto(Json.Leer("{\"a\":\"\\u00e1\\n\"}"), "a") == "á\n", "JSON: escapes \\u");
        bool error = false;
        try { Json.Leer("{\"a\": }"); } catch (FormatException) { error = true; }
        Verificar(error, "JSON: un texto inválido da error claro");

        // ---------------------------------------------------- Transcripcion
        Transcripcion t = new Transcripcion();
        t.Inicio = 10; t.Duracion = 20; t.DuracionProyecto = 30;
        Hablante h = new Hablante(); h.Etiqueta = "A2"; h.Nombre = "Gerbert"; h.Voz = true;
        Analisis a = new Analisis(); a.Db = new float[2000];
        for (int i = 0; i < a.Db.Length; i++) a.Db[i] = i < 1000 ? -60 : -20;
        Transcripcion.NivelesPorSegundo(a, out h.Nivel, out h.Pico);
        Verificar(h.Nivel.Length == 20 && h.Nivel[0] == -60 && h.Nivel[15] == -20, "niveles por segundo");
        t.Hablantes.Add(h);
        Hablante juego = new Hablante(); juego.Etiqueta = "A3"; juego.Nombre = "A3"; juego.Voz = false;
        juego.Nivel = new float[20]; juego.Pico = new float[20];
        for (int i = 0; i < 20; i++) juego.Pico[i] = i == 7 ? -3 : -40;
        t.Hablantes.Add(juego);
        t.AgregarWhisper(WhisperJson, 0, 10); // el WAV empezaba en el segundo 10
        Verificar(t.Segmentos.Count == 2 && Cerca(t.Segmentos[0].Inicio, 11) && t.Segmentos[1].Palabras.Count == 3 &&
                  t.Segmentos[1].Palabras[2].Texto == " construir.", "lee la salida JSON de Whisper y suma el inicio del rango");

        string ruta = Path.Combine(tmp, "Ep1.vegascut.json");
        t.Guardar(ruta);
        Transcripcion t2 = Transcripcion.Cargar(ruta);
        Verificar(t2.Segmentos.Count == 2 && t2.Hablantes[0].Nombre == "Gerbert" && t2.Hablantes[0].Nivel.Length == 20 &&
                  Cerca(t2.Segmentos[1].Palabras[1].Inicio, 16.5), "guardar y cargar la transcripción");

        // Corte de 12 a 15 s (despues de "Hola a todos", antes de "Vamos")
        string veg = Path.Combine(tmp, "Ep1.veg");
        string msg = Transcripcion.RegistrarCortes(veg, new List<Rango> { new Rango(12, 15) }, 30, 27);
        Transcripcion t3 = Transcripcion.Cargar(ruta);
        List<Segmento> act = t3.SegmentosActuales();
        Verificar(msg.Length > 0 && t3.Ediciones.Count == 1 && Cerca(act[1].Inicio, 13) && Cerca(act[0].Inicio, 11),
                  "tras un corte, los tiempos se corren (16 s → 13 s)");
        Verificar(double.IsNaN(t3.Mapear(13)), "un instante cortado no tiene tiempo actual");
        // Corte que se lleva la palabra "a" (16.5-16.7 original = 13.5-13.7 actual)
        Transcripcion.RegistrarCortes(veg, new List<Rango> { new Rango(13.45, 13.75) }, 27, 26.7);
        Transcripcion t4 = Transcripcion.Cargar(ruta);
        Verificar(t4.SegmentosActuales()[1].Texto == "Vamos construir.", "una palabra cortada desaparece del texto");
        string s1 = t4.Sincronizar(27);
        Verificar(t4.Ediciones.Count == 1 && s1.StartsWith("Se detect"), "Ctrl+Z: si el proyecto vuelve a durar lo de antes, se descarta el último corte");
        Verificar(t4.Sincronizar(99).StartsWith("El proyecto cambi"), "aviso si el proyecto se editó a mano");

        // ------------------------------------------------- Whisper (falso)
        string exe = Path.Combine(tmp, "faster-whisper-xxl.exe");
        File.WriteAllText(exe,
            "#!/bin/sh\n" +
            "if [ \"$1\" = \"--help\" ]; then echo '--word_timestamps'; exit 0; fi\n" +
            "echo \"$@\" > \"" + Path.Combine(tmp, "args.txt") + "\"\n" +
            "out=''; prev=''; for a in \"$@\"; do if [ \"$prev\" = \"--output_dir\" ]; then out=\"$a\"; fi; prev=\"$a\"; done\n" +
            "echo '[00:01.000 --> 00:02.500]  Hola a todos.'\n" +
            "echo '[00:06.000 --> 00:08.000]  Vamos a construir.'\n" +
            "cat > \"$out/$(basename \"$1\" .wav).json\" <<'FIN'\n" + WhisperJson + "\nFIN\n");
        System.Diagnostics.Process.Start("chmod", "+x \"" + exe + "\"").WaitForExit();
        string wav = Path.Combine(tmp, "voz.wav");
        File.WriteAllText(wav, "");
        Configuracion c = new Configuracion();
        c.WhisperExe = exe;
        TareaWhisper tarea = new TareaWhisper();
        tarea.Iniciar(c, wav, 10);
        for (int i = 0; i < 100 && !tarea.Terminada; i++) Thread.Sleep(100);
        Thread.Sleep(200);
        string args = File.ReadAllText(Path.Combine(tmp, "args.txt"));
        Verificar(tarea.Terminada && args.Contains("--model large-v3-turbo") && args.Contains("--language es") &&
                  args.Contains("--word_timestamps True") && args.Contains("--device cuda") && args.Contains("--compute_type int8"),
                  "Whisper: argumentos (modelo, idioma, palabras, tarjeta, int8)");
        Verificar(Cerca(tarea.Avance, 8) && tarea.UltimaLinea.Contains("construir"), "Whisper: progreso leído de su salida (8 s)");
        Verificar(tarea.Resultado().Contains("construir"), "Whisper: devuelve el JSON generado");
        tarea.Limpiar();

        // ------------------------------------------------- Configuracion
        c.GeminiClave = "AIza-prueba"; c.GeminiModelo = "gemini-x"; c.WhisperDispositivo = "cpu";
        c.Guardar();
        string guardado = File.ReadAllText(Path.Combine(Configuracion.Carpeta, "config.json"));
        Configuracion c2 = Configuracion.Cargar();
        Verificar(c2.GeminiClave == "AIza-prueba" && c2.GeminiModelo == "gemini-x" && c2.WhisperDispositivo == "cpu" &&
                  !guardado.Contains("AIza-prueba"), "configuración: se guarda y la clave no queda en texto plano");

        // ---------------------------------------------------- Peticion IA
        OpcionesIA op = new OpcionesIA(); op.MinutosMin = 1; op.MinutosMax = 1; op.Instrucciones = "Conserva lo gracioso";
        string mensaje = PeticionIA.Mensaje(t4, 26.7, op);
        Verificar(mensaje.Contains("Gerbert: Hola a todos.") && mensaje.Contains("[13.0-") && mensaje.Contains("Conserva lo gracioso") &&
                  !mensaje.Contains("- A3"), "mensaje a Gemini: transcripción con tiempos actuales, nombres e indicaciones");
        List<string> inten = PeticionIA.Intensidad(t4, 26.7, 5);
        Verificar(inten.Contains("15;10;0") && inten.Contains("10;0;10"), "intensidad por bloques: voz y explosión del juego (" + String.Join(" ", inten.ToArray()) + ")");

        // ------------------------------------------------ Gemini (local)
        HttpListener servidor = new HttpListener();
        string url = "http://localhost:" + (18000 + new Random().Next(1000)) + "/v1beta/";
        servidor.Prefixes.Add(url);
        servidor.Start();
        string cuerpoRecibido = null, claveRecibida = null;
        string respuestaIA = "```json\n{\"resumen\":\"Construyen una base.\"," +
            "\"secciones\":[{\"inicio\":0,\"fin\":26,\"titulo\":\"Todo\",\"descripcion\":\"x\"}]," +
            "\"momentos\":[{\"inicio\":20,\"fin\":22,\"puntuacion\":6,\"titulo\":\"B\",\"motivo\":\"m\"},{\"inicio\":11,\"fin\":12,\"puntuacion\":9,\"titulo\":\"A\",\"motivo\":\"m\"}]," +
            "\"corte\":[{\"inicio\":11.2,\"fin\":12.5,\"titulo\":\"Saludo\",\"motivo\":\"m\"},{\"inicio\":12.4,\"fin\":13,\"titulo\":\"Sigue\",\"motivo\":\"m\"},{\"inicio\":13.2,\"fin\":20,\"titulo\":\"Base\",\"motivo\":\"m\"}]," +
            "\"textos\":[{\"posicion\":13.2,\"texto\":\"Después…\",\"motivo\":\"m\"}]," +
            "\"shorts\":[],\"titulos\":[\"T1\",\"T2\"]}\n```";
        Thread hilo = new Thread(delegate ()
        {
            for (int i = 0; i < 3; i++)
            {
                HttpListenerContext ctx = servidor.GetContext();
                claveRecibida = ctx.Request.Headers["x-goog-api-key"];
                string salida;
                if (ctx.Request.HttpMethod == "GET")
                    salida = "{\"models\":[{\"name\":\"models/gemini-b\",\"supportedGenerationMethods\":[\"generateContent\"]}," +
                             "{\"name\":\"models/embedding-1\",\"supportedGenerationMethods\":[\"embedContent\"]}," +
                             "{\"name\":\"models/gemini-a\",\"supportedGenerationMethods\":[\"generateContent\",\"countTokens\"]}]}";
                else
                {
                    using (StreamReader sr = new StreamReader(ctx.Request.InputStream, Encoding.UTF8)) { string leido = sr.ReadToEnd(); if (cuerpoRecibido == null) cuerpoRecibido = leido; }
                    Dictionary<string, object> parte = new Dictionary<string, object>(); parte["text"] = respuestaIA;
                    Dictionary<string, object> pensado = new Dictionary<string, object>(); pensado["text"] = "pensando..."; pensado["thought"] = true;
                    Dictionary<string, object> contenido = new Dictionary<string, object>(); contenido["parts"] = new List<object> { pensado, parte };
                    Dictionary<string, object> cand = new Dictionary<string, object>(); cand["content"] = contenido;
                    if (i == 2) cand["finishReason"] = "MAX_TOKENS"; // tercera llamada: respuesta cortada
                    Dictionary<string, object> r = new Dictionary<string, object>(); r["candidates"] = new List<object> { cand };
                    salida = Json.Escribir(r);
                }
                byte[] b = Encoding.UTF8.GetBytes(salida);
                ctx.Response.ContentType = "application/json";
                ctx.Response.OutputStream.Write(b, 0, b.Length);
                ctx.Response.Close();
            }
        });
        hilo.Start();
        Gemini.Base = url;
        List<string> modelos = Gemini.ListarModelos("clave-123");
        Verificar(modelos.Count == 2 && modelos[0] == "gemini-a" && claveRecibida == "clave-123",
                  "Gemini: lista solo modelos que generan texto y manda la clave en el encabezado");
        string respuesta = Gemini.Generar("clave-123", "gemini-a", PeticionIA.Instrucciones(op), mensaje, true);
        bool cortada = false;
        try { Gemini.Generar("clave-123", "gemini-a", "x", "y", true); } catch (RespuestaCortada) { cortada = true; }
        Verificar(cortada, "Gemini: detecta una respuesta cortada (MAX_TOKENS)");
        hilo.Join(2000);
        servidor.Stop();
        object cuerpo = Json.Leer(cuerpoRecibido);
        Verificar(Json.Texto(Json.Obj(cuerpo, "generationConfig"), "responseMimeType") == "application/json" &&
                  Json.Obj(cuerpo, "systemInstruction") != null && cuerpoRecibido.Contains("Gerbert: Hola a todos."),
                  "Gemini: pide JSON, manda instrucciones y transcripción");
        Verificar(respuesta.StartsWith("{") && !respuesta.Contains("pensando"), "Gemini: quita el razonamiento y las cercas ```json");

        // --------------------------------------------------- Respuesta IA
        ResultadoIA res = ResultadoIA.Leer(respuesta, 26.7);
        Verificar(res.Resumen == "Construyen una base." && res.Momentos[0].Titulo == "A" && res.Titulos.Count == 2 &&
                  res.Corte.Count == 2 && Cerca(res.Corte[0].Fin, 13), "respuesta: momentos por nota y tramos solapados unidos");
        List<Rango> quitar = res.Quitar(26.7);
        Verificar(quitar.Count == 3 && Cerca(quitar[0].Fin, 11.2) && Cerca(quitar[1].Inicio, 13) && Cerca(quitar[1].Fin, 13.2) &&
                  Cerca(quitar[2].Inicio, 20), "lo que se quita es el complemento del corte");
        res.Corte[1].Elegido = false;
        Verificar(res.Quitar(26.7).Count == 2 && Cerca(res.DuracionCorte, 1.8), "desmarcar un tramo lo quita tambi\u00e9n");
        res.Corte[1].Elegido = true;
        res.AjustarAPalabras(t4.SegmentosActuales());
        // "Hola" va de 11.0 a 11.4: el corte que empezaba en 11.2 se lleva a 11.0. Y 13.2 cae dentro
        // de "Vamos" (13.0-13.5): se lleva a 13.0 y los dos tramos quedan unidos.
        Verificar(res.Corte.Count == 1 && Cerca(res.Corte[0].Inicio, 11) && Cerca(res.Corte[0].Fin, 20),
                  "el corte no parte palabras (11.2 \u2192 11.0; 13.2 \u2192 13.0)");
        string informe = res.Informe(veg, op, 26.7);
        Verificar(informe.Contains("## Resumen") && informe.Contains("Construyen una base.") && informe.Contains("- [x] 0:11"),
                  "informe .md con resumen, corte y casillas");


        // ------------------------------------------------- acelerar tramos
        Project pa = new Project();
        VideoTrack va = new VideoTrack(); va.Index = 0; pa.Tracks.Add(va);
        AudioTrack aa = new AudioTrack(); aa.Index = 1; pa.Tracks.Add(aa);
        foreach (Track tr in new Track[] { va, aa })
        {
            TrackEvent e = tr.IsAudio() ? (TrackEvent)new AudioEvent() : new VideoEvent();
            e.Start = new Timecode(0); e.Length = new Timecode(30000); e.Track = tr; tr.Events.Add(e);
        }
        Marker mk = new Marker(new Timecode(25000), "m"); pa.Markers.Add(mk);
        Editor.Acelerar(pa, new List<Track>(pa.Tracks), new List<Acelerado> { new Acelerado(10, 20, 2) }, true, true, 0.02);
        bool bienAcel = true;
        foreach (Track tr in new Track[] { va, aa })
        {
            List<TrackEvent> l = new List<TrackEvent>(tr.Events);
            l.Sort(delegate (TrackEvent x, TrackEvent y) { return x.Start.ms.CompareTo(y.Start.ms); });
            bienAcel &= l.Count == 3 && Cerca(l[1].Start.ms / 1000, 10) && Cerca(l[1].Length.ms / 1000, 5) && l[1].PlaybackRate == 2 &&
                        Cerca(l[2].Start.ms / 1000, 15) && Cerca(l[2].End.ms / 1000, 25) && l[0].PlaybackRate == 1;
            if (tr.IsAudio()) bienAcel &= l[1].Mute && !l[0].Mute && Cerca(l[0].FadeOut.Length.ms, 20) && Cerca(l[1].FadeIn.Length.ms, 20);
            else bienAcel &= !l[1].Mute;
        }
        Verificar(bienAcel && Cerca(mk.Position.ms / 1000, 20),
                  "acelerar 10-20 s a ×2: dura 5 s, lo siguiente se corre, audio mudo con fundidos y el marcador se mueve");

        Transcripcion.RegistrarAceleracion(veg, new List<Acelerado> { new Acelerado(14, 20, 2) }, 26.7, 23.7);
        Transcripcion t5 = Transcripcion.Cargar(ruta);
        Verificar(t5.Ediciones.Count == 3 && t5.Ediciones[2].Acelerados.Count == 1 && Cerca(t5.Mapear(t5.Inicio + 15), 18.7) && Cerca(t5.Mapear(t5.Inicio + 10), 15.35),
                  "la transcripción sigue los tramos acelerados (y se guarda)");

        // -------------------------------------------- corte con velocidades
        string conAccion = "{\"corte\":[{\"inicio\":0,\"fin\":10,\"accion\":\"conservar\"},{\"inicio\":9,\"fin\":40,\"accion\":\"acelerar\",\"velocidad\":7}," +
                           "{\"inicio\":50,\"fin\":60,\"accion\":\"conservar\"}]}";
        ResultadoIA ra = ResultadoIA.Leer(conAccion, 100);
        List<Rango> qa = ra.Quitar(100);
        List<Acelerado> aca = ra.Acelerados(qa);
        Verificar(ra.Corte.Count == 3 && Cerca(ra.Corte[1].Inicio, 10) && ra.Corte[1].Velocidad == 4 && Cerca(ra.DuracionCorte, 10 + 7.5 + 10) &&
                  aca.Count == 1 && Cerca(aca[0].Inicio, 10) && Cerca(aca[0].Fin, 40),
                  "corte con tramos acelerados: velocidad máx. ×4, sin encimarse y en la línea de tiempo ya cortada");

        // --------------------------------------------------- video largo
        Transcripcion larga = new Transcripcion();
        larga.DuracionProyecto = 3600;
        Hablante hl = new Hablante(); hl.Etiqueta = "A1"; hl.Nombre = "Gerbert"; hl.Voz = true; larga.Hablantes.Add(hl);
        for (int i = 0; i < 360; i++)
        {
            // Frases cada 10 s; justo antes de 1250 s hay una pausa larga (mejor sitio para partir).
            Segmento sg = new Segmento(); sg.Hablante = 0; sg.Inicio = i * 10; sg.Fin = i * 10 + (i == 124 ? 2 : 8);
            sg.Texto = "frase " + i; larga.Segmentos.Add(sg);
        }
        List<Rango> partes = PeticionIA.Partes(larga.SegmentosActuales(), 3600, 1200);
        Verificar(partes.Count == 3 && Cerca(partes[0].Fin, 1246) && Cerca(partes[2].Fin, 3600),
                  "partes de ~20 min cortadas en la pausa más larga (" + PeticionIA.S(partes[0].Fin) + " s)");

        List<string> llamadas = new List<string>();
        AsistenteIA asis = new AsistenteIA(delegate (string ins, string men)
        {
            llamadas.Add(ins + "\n----\n" + men);
            if (ins.Contains("POR PARTES"))
            {
                int n = llamadas.Count; // 1, 2 o 3
                double a0 = (n - 1) * 1200 + 100;
                if (n == 2 && !llamadas[1].Contains("Lo que pasó en las partes anteriores")) return "{}";
                return "{\"resumen\":\"Parte " + n + " resumida\",\"candidatos\":[{\"inicio\":" + a0 + ",\"fin\":" + (a0 + 300) +
                       ",\"importancia\":8,\"accion\":\"conservar\",\"titulo\":\"C" + n + "\",\"motivo\":\"m\"}]," +
                       "\"momentos\":[{\"inicio\":" + a0 + ",\"fin\":" + (a0 + 5) + ",\"puntuacion\":9,\"titulo\":\"M" + n + "\",\"motivo\":\"m\"}]}";
            }
            if (llamadas.Count == 4 && men.StartsWith("Tipo")) return "esto no es JSON"; // fuerza un reintento
            return "{\"resumen\":\"Todo\",\"corte\":[{\"inicio\":100,\"fin\":400,\"accion\":\"conservar\"}],\"titulos\":[\"T\"]}";
        });
        List<string> pasos = new List<string>();
        asis.Progreso = delegate (string x) { pasos.Add(x); };
        OpcionesIA opl = new OpcionesIA(); opl.MinutosMin = 22; opl.MinutosMax = 26;
        string final = asis.Ejecutar(larga, 3600, opl);
        string ultima = llamadas[llamadas.Count - 1];
        Verificar(ResultadoIA.Leer(final, 3600).Candidatos.Count == 3, "por partes: los candidatos se guardan junto al resultado");
        Verificar(llamadas.Count == 5 && ResultadoIA.Leer(final, 3600).Corte.Count == 1 &&
                  ultima.Contains("Parte 2 (") && ultima.Contains("Parte 3 resumida") && ultima.Contains("C3") && ultima.Contains("M2") &&
                  llamadas[2].Contains("Parte 1: Parte 1 resumida") && pasos.Exists(delegate (string x) { return x.Contains("reintentando"); }),
                  "video de 1 h: 3 partes con resumen previo, pasada final con candidatos y un reintento si llega roto");
        Verificar(llamadas[0].Contains("sugerida para los candidatos de esta parte: 747.6 s"),
                  "cada parte pide una duraci\u00f3n proporcional y generosa (24 min \u00d7 1246/3600 \u00d7 1.5)");

        int llamadasCorto = 0;
        AsistenteIA cort = new AsistenteIA(delegate (string ins, string men)
        {
            llamadasCorto++;
            if (llamadasCorto == 1) throw new RespuestaCortada();
            if (ins.Contains("POR PARTES")) return "{\"resumen\":\"r\",\"candidatos\":[],\"momentos\":[]}";
            return "{\"corte\":[]}";
        });
        cort.Ejecutar(t4, 26.7, op);
        Verificar(llamadasCorto == 3, "si la respuesta sale cortada, se pasa solo al modo por partes");


        // ------------------------------------- reglas, contexto y revision
        string ctxRuta = Path.Combine(tmp, "S01E01 SCR.vegascut-ia.json");
        File.WriteAllText(ctxRuta, "{\"respuesta\":\"{\\\"resumen\\\":\\\"Llegan al pueblo vaquero.\\\",\\\"secciones\\\":[{\\\"titulo\\\":\\\"Llegada\\\",\\\"descripcion\\\":\\\"Exploran\\\"}]}\"}");
        OpcionesIA opr = new OpcionesIA(); opr.Instrucciones = "Sin vida amorosa"; opr.Contexto = PeticionIA.ContextoDe(new List<string> { ctxRuta });
        string msgr = PeticionIA.Mensaje(t4, 26.7, opr);
        Verificar(msgr.Contains("REGLAS DEL CANAL") && msgr.Contains("vida amorosa, parejas") && msgr.Contains("INDICACIONES DEL EPISODIO") &&
                  msgr.Contains("S01E01 SCR: Llegan al pueblo vaquero.") && msgr.Contains("Llegada: Exploran") && msgr.Contains("m\u00ednimo 660.0 s, m\u00e1ximo 900.0 s"),
                  "mensaje: reglas fijas del canal, indicaciones, contexto de episodios anteriores y duraci\u00f3n m\u00edn/m\u00e1x");
        Verificar(PeticionIA.Instrucciones(opr).Contains("\"importancia\": 1-10"), "el corte pide importancia por tramo");

        string seis = "{\"corte\":[" +
            "{\"inicio\":0,\"fin\":60,\"importancia\":9,\"titulo\":\"Inicio\"}," +
            "{\"inicio\":100,\"fin\":160,\"importancia\":3,\"titulo\":\"Relleno\"}," +
            "{\"inicio\":200,\"fin\":290,\"importancia\":5,\"titulo\":\"Red flags amorosas\"}," +
            "{\"inicio\":300,\"fin\":360,\"importancia\":8,\"titulo\":\"Pelea\"}," +
            "{\"inicio\":400,\"fin\":460,\"importancia\":2,\"titulo\":\"Final\"}]," +
            "\"candidatos\":[{\"inicio\":500,\"fin\":530,\"importancia\":7,\"titulo\":\"Extra\"},{\"inicio\":100,\"fin\":160,\"importancia\":3}]}";
        ResultadoIA rr = ResultadoIA.Leer(seis, 600);
        int cambios = rr.AplicarRevision("{\"tramos\":[{\"indice\":2,\"quitar\":true,\"motivo\":\"vida amorosa\"},{\"indice\":3,\"quitar\":false,\"inicio\":310,\"fin\":350}]}");
        Verificar(cambios == 2 && !rr.Corte[2].Elegido && rr.Corte[2].PorRevision && rr.Corte[2].Nota.Contains("vida amorosa") &&
                  Cerca(rr.Corte[3].Inicio, 310) && Cerca(rr.Corte[3].Fin, 350),
                  "revisi\u00f3n: quita el tramo de vida amorosa y recorta otro");
        // Ahora suman 60+60+40+60 = 220 s. M\u00e1ximo 150 s: se desmarca el de menor importancia (nunca el primero ni el \u00faltimo).
        string aj = rr.AjustarDuracion(100, 165);
        Verificar(!rr.Corte[1].Elegido && rr.Corte[0].Elegido && rr.Corte[4].Elegido && rr.DuracionCorte <= 165 && aj.Contains("desmarcado"),
                  "duraci\u00f3n: si sobra, se desmarca lo menos importante (" + Formato.Tiempo(rr.DuracionCorte) + ")");
        string aj2 = rr.AjustarDuracion(190, 200);
        Verificar(rr.Corte.Exists(delegate (Tramo x) { return x.Titulo == "Extra" && x.Elegido; }) && !rr.Corte[2].Elegido &&
                  rr.DuracionCorte >= 189.5 && aj2.Contains("agregado"),
                  "duraci\u00f3n: si falta, se agregan candidatos, pero nunca lo que quit\u00f3 la revisi\u00f3n (" + Formato.Tiempo(rr.DuracionCorte) + ")");

        AsistenteIA rev = new AsistenteIA(delegate (string ins, string men)
        {
            return men.Contains("Red flags amorosas") ? "{\"tramos\":[{\"indice\":2,\"quitar\":true}]}" : "{\"tramos\":[]}";
        });
        ResultadoIA rr2 = ResultadoIA.Leer(seis, 600);
        string jsonRev = rev.Revisar(t4, rr2, opr);
        Verificar(rr2.AplicarRevision(jsonRev) == 1 && !rr2.Corte[2].Elegido, "la revisi\u00f3n recibe los tramos y su texto, y su respuesta se aplica");

        // ------------------------------------------------ marcadores anclados
        Project pm = new Project(); pm.FilePath = Path.Combine(tmp, "Ep2.veg");
        VideoTrack vm = new VideoTrack(); vm.Index = 0; pm.Tracks.Add(vm);
        TrackEvent ev = new VideoEvent(); ev.Start = new Timecode(0); ev.Length = new Timecode(30000); ev.Track = vm;
        ev.ActiveTake = new Take(); ev.ActiveTake.Media = new Media(); ev.ActiveTake.Media.FilePath = "K:/grab/a.mp4";
        ev.ActiveTake.Offset = new Timecode(100000); vm.Events.Add(ev);
        Marker mm = new Marker(new Timecode(10000), "TEXTO: Llegan al pueblo"); pm.Markers.Add(mm);
        Region rg = new Region(new Timecode(5000), new Timecode(10000), "Conservar: Pelea"); pm.Regions.Add(rg);
        Ancla am = Anclas.Crear(pm, 10, -1, mm.Label), ar = Anclas.Crear(pm, 5, 15, rg.Label);
        Verificar(am != null && Cerca(am.Fuente, 110) && ar.Region && Cerca(ar.FuenteFin, 115), "ancla: archivo y segundo del archivo bajo el marcador");
        Anclas.Guardar(pm.FilePath, new List<Ancla> { am, ar });
        ev.Start = new Timecode(50000); // el usuario mueve el clip 50 s a la derecha
        int perd, revs;
        int mov = Anclas.Reubicar(pm, pm.FilePath, out perd, out revs);
        Verificar(mov == 2 && perd == 0 && Cerca(mm.Position.ms / 1000, 60) && Cerca(rg.Position.ms / 1000, 55) && Cerca(rg.Length.ms / 1000, 10),
                  "reubicar: el marcador y la regi\u00f3n siguen a su clip movido");
        ev.Split(new Timecode(8000)); // corta el clip: el segundo 110 queda en el segundo pedazo
        foreach (TrackEvent x in vm.Events) if (x != ev) x.Start = new Timecode(x.Start.ms + 20000);
        Anclas.Reubicar(pm, pm.FilePath, out perd, out revs);
        Verificar(Cerca(mm.Position.ms / 1000, 80), "reubicar: tambi\u00e9n si el clip se cort\u00f3 y se movi\u00f3 un pedazo");
        vm.Events.Clear();
        Anclas.Reubicar(pm, pm.FilePath, out perd, out revs);
        Verificar(perd == 2 && revs == 2, "reubicar: avisa si el clip ya no existe");

        // ------------------------------------------------- tramos fijos
        ResultadoIA rf = ResultadoIA.Leer(@"{""corte"": [
            {""inicio"": 0, ""fin"": 100, ""importancia"": 9, ""titulo"": ""Inicio""},
            {""inicio"": 3700, ""fin"": 3812, ""importancia"": 9, ""titulo"": ""Arranque""},
            {""inicio"": 4120, ""fin"": 4300, ""importancia"": 9, ""titulo"": ""Meta y regreso""},
            {""inicio"": 5000, ""fin"": 5100, ""importancia"": 5, ""titulo"": ""Relleno""},
            {""inicio"": 5700, ""fin"": 5800, ""importancia"": 9, ""titulo"": ""Final""}]}", 5815);
        rf.AgregarFijo(3420, 4170, "Carrera");
        string cortes = "";
        foreach (Tramo x in rf.Corte) cortes += x.Inicio + "-" + x.Fin + (x.Fijo ? "F" : "") + " ";
        Verificar(cortes == "0-100 3420-4170F 4170-4300 5000-5100 5700-5800 ", "Fijo: absorbe lo de adentro y recorta lo que sobresale (" + cortes + ")");
        rf.AjustarDuracion(10 * 60, 16 * 60);
        Tramo carrera = rf.Corte.Find(delegate (Tramo x) { return x.Fijo; });
        Verificar(carrera.Elegido && !rf.Corte[3].Elegido && rf.DuracionCorte <= 16 * 60 + 0.5,
            "Fijo: el ajuste de duración quita otros tramos, nunca el fijo");
        Verificar(rf.AplicarRevision(@"{""tramos"": [{""indice"": 1, ""quitar"": true, ""motivo"": ""x""}]}") == 0 && carrera.Elegido,
            "Fijo: la revisión no lo toca");
        List<Tramo> lf = TramosFijos.Agregar(new List<Tramo>(), 100, 200, "a");
        lf = TramosFijos.Agregar(lf, 150, 300, "b");
        lf = TramosFijos.Agregar(lf, 500, 600, "c");
        Verificar(lf.Count == 2 && lf[0].Inicio == 100 && lf[0].Fin == 300, "Fijos: se unen los que se enciman");
        string vegf = Path.Combine(tmp, "fijos.veg");
        TramosFijos.Guardar(vegf, 5815, lf);
        Verificar(TramosFijos.Cargar(vegf, 5815).Count == 2 && TramosFijos.Cargar(vegf, 1300).Count == 0,
            "Fijos: se guardan y solo valen si el proyecto dura lo mismo");
        OpcionesIA opf = new OpcionesIA();
        opf.Instrucciones = "La carrera va de 0:57:00 a 1:09:30, el gol en 12:05.";
        opf.Fijos = lf;
        string mf = PeticionIA.Mensaje(new Transcripcion(), 5815, opf);
        Verificar(mf.Contains("0:57:00 (= 3420.0 s)") && mf.Contains("1:09:30 (= 4170.0 s)") && mf.Contains("12:05 (= 725.0 s)"),
            "Indicaciones: los tiempos h:mm:ss también van en segundos");
        Verificar(mf.Contains("TRAMOS FIJOS") && mf.Contains("[100.0-300.0]"), "Los tramos fijos van en el mensaje a Gemini");

        ResultadoIA rp = ResultadoIA.Leer(@"{""corte"": [{""inicio"": 30, ""fin"": 130}, {""inicio"": 1580, ""fin"": 1716}, {""inicio"": 2000, ""fin"": 2100}]}", 5815);
        rp.AplicarRevision(@"{""tramos"": [{""indice"": 0, ""quitar"": false, ""inicio"": 30, ""fin"": 41},
            {""indice"": 0, ""quitar"": true, ""inicio"": 41, ""fin"": 129},
            {""indice"": 1, ""quitar"": true, ""inicio"": 1675, ""fin"": 1716},
            {""indice"": 2, ""quitar"": true, ""inicio"": 2040, ""fin"": 2050}]}");
        string revp = "";
        foreach (Tramo x in rp.Corte) revp += (x.Elegido ? "" : "!") + x.Inicio + "-" + x.Fin + " ";
        Verificar(revp == "30-41 1580-1675 2000-2040 2050-2100 ", "Revisión: quitar un pedazo deja el resto del tramo (" + revp + ")");

        // ------------------------------------------- frases inventadas
        Verificar(Transcripcion.Alucinacion("¡Suscríbete al canal!") && Transcripcion.Alucinacion(" Gracias por ver.") &&
                  !Transcripcion.Alucinacion("Gracias, güey") && !Transcripcion.Alucinacion("¡Corre, corre!"),
            "Detecta frases que Whisper inventa en silencio");
        Transcripcion ta = new Transcripcion();
        Segmento sa = new Segmento(); sa.Texto = "¡Suscríbete al canal!"; sa.Inicio = 1; sa.Fin = 2;
        Segmento sb2 = new Segmento(); sb2.Texto = "¡Vamos!"; sb2.Inicio = 3; sb2.Fin = 4;
        ta.Hablantes.Add(new Hablante { Etiqueta = "A2", Nombre = "Yo" });
        ta.Segmentos.Add(sa); ta.Segmentos.Add(sb2);
        Verificar(ta.SegmentosActuales().Count == 1, "No se le mandan a Gemini");

        try { Directory.Delete(tmp, true); } catch { }
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos;
    }
}
