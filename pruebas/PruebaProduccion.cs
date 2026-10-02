// Pruebas de ProducirCapitulo y PasoFinal: analisis y propuestas, propuesta
// final, producir un episodio doble sobre el material de Cap1 (rearmado con
// la API falsa) y bajar el juego bajo la narracion (ver pruebas/ejecutar.sh).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

class VozFalsa : ISintetizador
{
    public string Nombre { get { return "voz falsa"; } }
    public void Decir(string texto, int velocidad, string wav)
    {
        double seg = LogicaPlan.Palabras(texto) * 60.0 / (150 * Math.Pow(Math.Pow(3, 0.1), velocidad));
        int n = (int)(seg * 16000);
        using (BinaryWriter w = new BinaryWriter(File.Create(wav)))
        {
            w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVE"));
            w.Write(Encoding.ASCII.GetBytes("fmt ")); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(16000); w.Write(32000);
            w.Write((short)2); w.Write((short)16);
            w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2); w.Write(new byte[n * 2]);
        }
    }
}

class PruebaProduccion
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    static Project Rearmar(string export)
    {
        object o = Json.Leer(File.ReadAllText(export, Encoding.UTF8));
        Project p = new Project();
        p.Length = new Timecode(Json.Numero(Json.Obj(o, "proyecto"), "duracion", 0) * 1000);
        Dictionary<string, Media> medios = new Dictionary<string, Media>();
        PlugInNode generador = new PlugInNode { Name = "Texto", UniqueID = "generado" };
        foreach (object pista in Json.Lista(o, "pistas"))
        {
            bool audio = Json.Texto(pista, "tipo") == "audio";
            int i = (int)Json.Numero(pista, "indice", 0);
            Track t = audio ? (Track)new AudioTrack(i, "") : new VideoTrack(i, "");
            foreach (object e in Json.Lista(pista, "eventos"))
            {
                Timecode a = new Timecode(Json.Numero(e, "inicio", 0) * 1000), l = new Timecode(Json.Numero(e, "duracion", 0) * 1000);
                TrackEvent ev = audio ? (TrackEvent)((AudioTrack)t).AddAudioEvent(a, l) : ((VideoTrack)t).AddVideoEvent(a, l);
                string archivo = Json.Texto(e, "archivo");
                Media m;
                if (Json.Valor(e, "generado") is bool && (bool)Json.Valor(e, "generado")) m = new Media(generador);
                else if (!medios.TryGetValue(archivo, out m)) { m = new Media(archivo); medios[archivo] = m; }
                ev.ActiveTake = new Take { Media = m, Offset = new Timecode(Json.Numero(e, "offset", 0) * 1000) };
                ev.PlaybackRate = Json.Numero(e, "velocidad", 1);
            }
            p.Tracks.Add(t);
        }
        return p;
    }

    const string RespAnalisis = @"{""resumen"": ""Llegan al server, Jason roba la flecha y aparece la mazmorra."", ""minutos_utiles"": 9,
 ""momentos"": [{""inicio"": 300, ""fin"": 306, ""tipo"": ""crisis"", ""texto"": ""Jason con la flecha"", ""fuerza"": 5, ""personajes"": [""Jason""]},
                {""inicio"": 20, ""fin"": 40, ""tipo"": ""llegada"", ""texto"": ""Llegada"", ""fuerza"": 3},
                {""inicio"": 9999, ""fin"": 10010, ""tipo"": ""gancho"", ""texto"": ""fuera"", ""fuerza"": 2}],
 ""hilos"": [""La mazmorra china queda pendiente""],
 ""doble"": {""recomendado"": true, ""motivo"": ""Dos crisis claras""},
 ""propuestas"": [
   {""id"": ""A"", ""nombre"": ""Etapa clásica"", ""tipo"": ""normal"", ""titulos"": [""La flecha""], ""minutos"": 16, ""cold_open"": ""Jason con la flecha"", ""cierre"": ""La mazmorra"", ""escaleta"": [""[0:20] llegada""], ""por_que"": ""Sigue la fórmula""},
   {""id"": ""A"", ""nombre"": ""Doble"", ""tipo"": ""doble"", ""titulos"": [""La flecha Parte 1"", ""La flecha Parte 2""], ""minutos"": 15, ""cold_open"": ""..."", ""cierre"": ""..."", ""escaleta"": [], ""por_que"": ""...""},
   {""id"": ""C"", ""nombre"": ""Especial de Jason"", ""tipo"": ""rara"", ""titulos"": [], ""minutos"": 15, ""cold_open"": ""..."", ""cierre"": ""..."", ""escaleta"": [], ""por_que"": ""...""},
   {""id"": ""D"", ""nombre"": ""Cuarta"", ""tipo"": ""normal""}]}";

    static string RespFinal(int musica)
    {
        return @"{""resumen"": ""Episodio doble de la flecha."", ""partes"": [
 {""titulo"": ""La flecha Parte 1"", ""etapa"": ""Etapa 1"",
  ""bloques"": [{""bloque"": ""cold_open"", ""clips"": [{""inicio"": 300, ""fin"": 330, ""nota"": ""Jason con la flecha""}]},
                {""bloque"": ""acto_a"", ""clips"": [{""inicio"": 0, ""fin"": 120}, {""inicio"": 150, ""fin"": 200}]},
                {""bloque"": ""acto_b"", ""clips"": [{""inicio"": 200, ""fin"": 290}, {""inicio"": 5, ""fin"": 5.2}]},
                {""bloque"": ""avance"", ""clips"": [{""inicio"": 400, ""fin"": 410}]}],
  ""textos"": [{""tipo"": ""lugar"", ""texto"": ""El server"", ""en"": 2}, {""tipo"": ""stats"", ""texto"": ""JASON · Poder A · Velocidad C"", ""bloque"": ""regancho""},
               {""tipo"": ""tiempo"", ""texto"": ""fuera"", ""en"": 140}],
  ""musica"": [{""id"": " + musica + @", ""en"": 0, ""motivo"": ""viaje""}, {""personaje"": ""Gerber"", ""en"": 60, ""motivo"": ""se luce""}, {""id"": 99999, ""en"": 10}],
  ""narracion"": [{""en"": 1, ""texto"": ""Nunca pensé que una flecha nos iba a causar tantos problemas.""}, {""en"": 160, ""texto"": ""Y ahí empezó todo.""}],
  ""recursos"": [{""en"": 100, ""duracion"": 3, ""clase"": ""imagen"", ""descripcion"": ""La flecha brillando""}]},
 {""titulo"": ""La flecha Parte 2"", ""etapa"": ""Etapa 1"",
  ""bloques"": [{""bloque"": ""cold_open"", ""clips"": [{""inicio"": 330, ""fin"": 360}]}, {""bloque"": ""acto_a"", ""clips"": [{""inicio"": 360, ""fin"": 500}]},
                {""bloque"": ""acto_b"", ""clips"": [{""inicio"": 500, ""fin"": 620}]}],
  ""textos"": [], ""musica"": [], ""narracion"": [{""en"": 365, ""texto"": ""Seguíamos atrapados.""}], ""recursos"": []}]}";
    }

    static int Main(string[] args)
    {
        string carpeta = args.Length > 0 ? args[0] : "../ejemplos/jojmania";
        string csv = args.Length > 1 ? args[1] : "../ejemplos/musica/musica.csv";

        // ------------------------------------------------ analisis y propuestas
        AnalisisCapitulo a = LogicaProduccion.LeerAnalisis(RespAnalisis, 635);
        Verificar(a.Propuestas.Count == 3 && a.Propuestas[1].Id == "B" && a.Propuestas[1].Doble && a.Propuestas[2].Tipo == "normal",
                  "Análisis: tres propuestas con id único (la repetida pasa a B) y su tipo");
        Verificar(a.Momentos.Count == 2 && a.Momentos[0].Inicio == 20 && a.DobleRecomendado && a.Hilos.Count == 1,
                  "Análisis: momentos en orden y dentro del material, doble recomendado, hilos");
        FormatoSerie f = FormatoSerie.Preset(FormatoSerie.TV);
        string ia = LogicaProduccion.InstruccionesAnalisis(f, "Gameplay", "Normal");
        Verificar(ia.Contains("TRES") && ia.Contains("~13 %") && ia.Contains("15–18") && ia.Contains("Parte 1"),
                  "Instrucciones: tres propuestas, estructura de referencia, duración y títulos de episodio doble");

        string dir = Path.Combine(Path.GetTempPath(), "vc-prod-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Biblioteca con archivos de prueba (los temas elegidos existen).
            string musDir = Path.Combine(dir, "musica");
            BibliotecaMusica bib = File.Exists(csv) ? BibliotecaMusica.Indexar(musDir, BibliotecaMusica.DesdeCsv(csv), null) : new BibliotecaMusica();
            bib.Carpeta = musDir;
            MusicaSerie ms = new MusicaSerie { Carpeta = musDir, Reparto = "Gerber: impulsivo\nJason: caótico" };
            List<ArchivoMusica> cand = LogicaProduccion.MusicaCandidata(bib, ms);
            int iCalm = cand.FindIndex(delegate (ArchivoMusica x) { return x.TemaAnime == "Calm Sightseeing"; });
            bool soloAnime = cand.TrueForAll(delegate (ArchivoMusica x) { return x.Momento != "opening" && x.Momento != "ending"; });
            Verificar(!File.Exists(csv) || (cand.Count > 100 && (cand[0].Parte == "SC" || cand[0].Parte == "GW") && iCalm >= 0 && soloAnime),
                      "Música para Gemini: " + cand.Count + " temas, primero SC y Golden Wind, sin openings ni endings");
            if (iCalm < 0) { Console.WriteLine("(sin listado de música: se salta la producción)"); return Fin(); }
            foreach (ArchivoMusica x in new ArchivoMusica[] { cand[iCalm] })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(musDir, x.Ruta)));
                File.WriteAllText(Path.Combine(musDir, x.Ruta), "x");
            }
            ms.Personajes["Gerber"] = new TemaAsignado { Archivo = cand[iCalm].Ruta };

            // ------------------------------------------------ propuesta final
            PlanFinal fin = LogicaProduccion.LeerFinal(RespFinal(iCalm), 635, cand.Count, 195);
            CapituloFinal p1 = fin.Partes[0];
            int clips = 0, temas = 0, narr = 0, rec = 0, nTextos = 0;
            foreach (ItemFinal i in p1.Items)
            {
                if (i.Tipo == "clip") clips++; else if (i.Tipo == "musica") temas++; else if (i.Tipo == "narracion") narr++;
                else if (i.Tipo == "recurso") rec++; else if (i.Tipo == "texto") nTextos++;
            }
            Verificar(fin.Partes.Count == 2 && p1.Titulo == "La flecha Parte 1" && clips == 5 && temas == 2 && narr == 2 && rec == 1 && nTextos == 3,
                      "Propuesta final: 2 partes, clips (sin los de menos de 0.5 s), música válida, narración, recursos y textos");
            Verificar(p1.Items.Find(delegate (ItemFinal i) { return i.Tipo == "narracion"; }).Id == "N01" &&
                      fin.Partes[1].Items.Find(delegate (ItemFinal i) { return i.Tipo == "narracion"; }).Id == "N03", "Narración numerada en todo el episodio");
            double d1 = LogicaProduccion.Duracion(p1, f.Tv);
            Verificar(Math.Abs(d1 - (30 + 170 + 90 + 10 + 47)) < 0.01, "Duración estimada: clips + bloques fijos (" + Formato.Tiempo(d1) + ")");
            string mf = LogicaProduccion.MensajeFinal(a, a.Propuestas[1], "más Gerber", cand, ms, LogicaProduccion.Reparto(ms), null, null, null);
            Verificar(mf.Contains("PROPUESTA ELEGIDA: B") && mf.Contains("BIBLIOTECA DE MÚSICA") && mf.Contains("Tema de Gerber") && mf.Contains("más Gerber"),
                      "Mensaje final: propuesta elegida, notas, biblioteca y temas de personajes");
            string inf = LogicaProduccion.InstruccionesFinal(f, "Normal", a.Propuestas[1]);
            Verificar(inf.Contains("DOBLE") && inf.Contains("cold_open") && inf.Contains("stats") && inf.Contains("195 palabras"),
                      "Instrucciones finales: bloques de la plantilla, tarjeta de stats y narración a tu velocidad");

            // Guardar y volver a abrir lo que se tenía.
            string veg = Path.Combine(dir, "S01E01 SCR BASE.veg");
            a.Propuestas[0].Notas = "me gusta el cold open";
            p1.Items[1].Elegido = false;
            LogicaProduccion.Guardar(veg, a, "B", "más Gerber", "notas", fin);
            AnalisisCapitulo a2; PlanFinal f2; string el, ind, no;
            Verificar(LogicaProduccion.Cargar(veg, 635, cand.Count, 195, out a2, out el, out ind, out no, out f2) && el == "B" && ind == "más Gerber" &&
                      a2.Propuestas[0].Notas == "me gusta el cold open" && !f2.Partes[0].Items[1].Elegido,
                      "Se guarda todo (propuesta elegida, notas y lo desmarcado) y se vuelve a abrir igual");
            p1.Items[1].Elegido = true;

            // ------------------------------------------------ producir
            string export = Path.Combine(carpeta, "Cap1.export.json"), tr = Path.Combine(carpeta, "Cap1.vegascut.json");
            if (!File.Exists(export)) { Console.WriteLine("(sin ejemplos/jojmania: se salta la producción)"); return Fin(); }
            Vegas v = new Vegas();
            v.Generators.Hijos.Add(new PlugInNode { Name = "VEGAS Títulos y texto", UniqueID = "{Svfx:com.vegascreativesoftware:titlesandtext}" });
            v.Project = Rearmar(export);
            v.Project.FilePath = veg;
            Project pr = v.Project;
            File.Copy(tr, Transcripcion.RutaPara(veg));
            Transcripcion t = Transcripcion.Cargar(tr);
            t.Ubicador = PistasVegas.Ubicador(pr, t);
            string op = Path.Combine(dir, "op.mp4");
            File.WriteAllText(op, "x");
            f.Tv.Kit["op"] = op;
            int eventosMaterial = 0;
            foreach (Track x in pr.Tracks) eventosMaterial += x.Events.Count;
            pr.Regions.Add(new Region(new Timecode(0), new Timecode(1000), "vieja"));

            List<string> pasos = new List<string>();
            ResultadoProduccion r = ArmarCapitulo.EnCopia(v, fin, f, ms, bib, cand, null, new VozFalsa(), 195,
                                                          delegate (string s, double x) { pasos.Add(s); });
            Console.WriteLine("      " + r.Texto());
            foreach (string w in r.Avisos) Console.WriteLine("      aviso: " + w);
            string cap = Path.Combine(dir, "S01E01 SCR CAP.veg");
            Verificar(pr.FilePath == cap && v.Guardados.Count == 2 && v.Guardados[0] == cap && v.Guardados[1] == cap,
                      "Producir: trabaja en «… CAP.veg» (sin el BASE) y la copia base no se toca");
            Verificar(File.Exists(Transcripcion.RutaPara(cap)) && File.Exists(Path.Combine(dir, "S01E01 SCR CAP.vegascut-guion.txt")),
                      "Producir: la copia lleva su transcripción y el guion");
            Verificar(r.Partes.Count == 2 && Math.Abs(r.Partes[0] - (30 + 5 + 3 + 170 + 6 + 90 + 3 + 15 + 10)) < 0.5,
                      "Producir: dos partes; la 1 dura " + Formato.Tiempo(r.Partes[0]) + " (clips + el OP con lo que dura su archivo + placeholders)");
            double minimo = double.MaxValue;
            bool quedaMaterial = false;
            foreach (Track x in pr.Tracks)
                foreach (TrackEvent e in x.Events)
                {
                    minimo = Math.Min(minimo, S(e.Start));
                    if (x.Name == "" && S(e.Start) > r.Partes[0] + r.Partes[1] + 15) quedaMaterial = true;
                }
            Verificar(Math.Abs(minimo) < 0.01 && !quedaMaterial, "Producir: el capítulo queda al inicio y el material se quita");
            Track principal = pr.Tracks.Find(delegate (Track x) { return x.Index == 7 && !x.IsAudio(); });
            bool coldOpen = false;
            foreach (TrackEvent e in principal.Events) if (S(e.Start) < 0.01 && Math.Abs(S(e.ActiveTake.Offset) - 0) >= 0) coldOpen = true;
            Verificar(coldOpen, "Producir: la pista principal empieza con el cold open");
            Verificar(pr.Regions.Exists(delegate (Region x) { return x.Label == "P1 · COLD OPEN" && S(x.Position) < 0.01; }) &&
                      pr.Regions.Exists(delegate (Region x) { return x.Label == "PARTE 2 · La flecha Parte 2"; }) &&
                      !pr.Regions.Exists(delegate (Region x) { return x.Label == "vieja"; }), "Regiones: un bloque por región y una por parte (las viejas se quitan)");
            Track kit = pr.Tracks.Find(delegate (Track x) { return x.Name == ArmarCapitulo.PistaKit; });
            Track kitA = pr.Tracks.Find(delegate (Track x) { return x.Name == ArmarCapitulo.PistaKitAudio; });
            int phKit = 0; bool stats = false;
            foreach (TrackEvent e in kit.Events) { string tx = GeneradorTexto.TextoDe(e); if (tx.StartsWith("[")) phKit++; if (tx.Contains("JASON · Poder A")) stats = true; }
            Verificar(kitA != null && kitA.Events.Count == 2 && phKit == 6 && stats,
                      "Kit: el OP con su archivo (video y audio) y placeholders para lo que falta, con la tarjeta de stats");
            Track textos = pr.Tracks.Find(delegate (Track x) { return x.Name == ArmarCapitulo.PistaTextos; });
            List<string> tt = new List<string>();
            foreach (TrackEvent e in textos.Events) tt.Add(GeneradorTexto.TextoDe(e));
            Verificar(tt.Contains("Etapa 1 · La flecha Parte 1") && tt.Contains("Etapa 1 · La flecha Parte 2") && tt.Contains("El server") && !tt.Contains("fuera"),
                      "Textos: título de cada parte y carteles en su momento (los que quedaron fuera no se ponen)");
            Track mus = pr.Tracks.Find(delegate (Track x) { return x.Name == ArmarCapitulo.PistaMusica; });
            Verificar(mus != null && mus.Events.Count == 2 && r.Temas == 2 && mus.Envelopes.Count == 0,
                      "Música: un tema por entrada (también el del personaje) y sin balancear");
            Track narrP = pr.Tracks.Find(delegate (Track x) { return x.Name == RitmoVegas.PistaNarracion; });
            Verificar(narrP != null && narrP.Events.Count == 3 && r.Narraciones == 3, "Narración: las frases con la voz provisional");
            Track ph = pr.Tracks.Find(delegate (Track x) { return x.Name == AplicarPlan.PistaPlaceholders; });
            Verificar(ph != null && ph.Events.Count == 1 && GeneradorTexto.TextoDe(ph.Events[0]).StartsWith("[R01] IMAGEN"), "Recursos: placeholder con su código");
            Verificar(r.SinLugar == 1, "Avisa de lo que quedó fuera (" + r.SinLugar + ")");
            Verificar(pasos.Count > 5, "Avisa qué está haciendo");

            // ------------------------------------------------ paso final
            Transcripcion t3 = Transcripcion.Cargar(Transcripcion.RutaPara(cap));
            t3.Ubicador = PistasVegas.Ubicador(pr, t3);
            int n = LogicaPasoFinal.BajarJuego(pr, t3, "Narrador", -10);
            Track grab = RitmoVegas.PistasGrabacion(pr, t3, "Narrador")[0];
            Envelope env = grab.Envelopes.FindByType(EnvelopeType.Volume);
            Verificar(n >= 3 && env != null && env.Points.Count > 3, "Paso final: el juego baja bajo la narración (" + n + " pistas)");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return Fin();
    }

    static int Fin()
    {
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos == 0 ? 0 : 1;
    }
}
