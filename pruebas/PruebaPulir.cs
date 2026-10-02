// Pruebas de PulirEpisodio, parte 2: plan de Gemini, aplicarlo sobre el Cap1
// de JoJoMania rearmado con la API falsa (gancho, regiones, avances,
// placeholders, narracion y bajada del juego) y reemplazar placeholders
// (ver pruebas/ejecutar.sh).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// Voz falsa: escribe silencio que dura lo que tardaria una voz de 150 ppm
// a esa velocidad.
class VozFalsa : ISintetizador
{
    public int Llamadas;
    public string Nombre { get { return "voz falsa"; } }
    public void Decir(string texto, int velocidad, string wav)
    {
        Llamadas++;
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

class PruebaPulir
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    static Project Rearmar(string export)
    {
        object o = Json.Leer(File.ReadAllText(export, Encoding.UTF8));
        Project p = new Project();
        p.FilePath = Json.Texto(Json.Obj(o, "proyecto"), "archivo");
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

    const string Respuesta = @"```json
{""resumen"": ""Llegamos al server y Jason roba la flecha."", ""estructura"": ""abre con el robo de la flecha, avanza por días y cierra con la mazmorra"",
 ""gancho"": {""inicio"": 300, ""fin"": 306, ""motivo"": ""Jason con la flecha""},
 ""secciones"": [{""inicio"": 0, ""fin"": 120, ""nombre"": ""Llegada"", ""proposito"": ""presentar""}, {""inicio"": 120, ""fin"": 9999, ""nombre"": ""Mazmorra"", ""proposito"": ""clímax""}],
 ""narracion"": [
   {""inicio"": 1, ""fin"": 4, ""texto"": ""Nunca pensé que una flecha nos iba a causar tantos problemas."", ""tipo"": ""gancho""},
   {""inicio"": 3, ""fin"": 8, ""texto"": ""Cada año hago un server con mis amigos con mods de JoJo, y este año no sería la excepción."", ""tipo"": ""contexto""},
   {""inicio"": 400, ""fin"": 404, ""texto"": ""Lo cual, claramente, no fue la mejor idea."", ""tipo"": ""re-gancho""},
   {""inicio"": 99999, ""texto"": ""fuera del video"", ""tipo"": ""cierre""},
   {""inicio"": 50, ""texto"": """", ""tipo"": ""cta""}],
 ""avances"": [{""inicio"": 20, ""texto"": ""Día 1""}, {""inicio"": 200, ""texto"": ""Día 2""}],
 ""recursos"": [{""inicio"": 410, ""duracion"": 3, ""clase"": ""imagen"", ""descripcion"": ""La flecha de JoJo brillando"", ""archivo"": ""flecha/brillo.png""},
                {""inicio"": 500, ""duracion"": 20, ""clase"": ""meme"", ""descripcion"": ""Meme de 'to be continued'"", ""archivo"": ""to be continued""}],
 ""recortes"": [{""inicio"": 360, ""fin"": 420, ""accion"": ""acelerar"", ""motivo"": ""exploración sola""}, {""inicio"": 10, ""fin"": 10.5, ""accion"": ""quitar"", ""motivo"": ""muy corto""}]}
```";

    static int Main(string[] args)
    {
        string carpeta = args.Length > 0 ? args[0] : "../ejemplos/jojmania";

        // ------------------------------------------------------- leer plan
        Plan plan = LogicaPlan.Leer(Respuesta, 635, 195);
        Verificar(plan.Items[0].Tipo == "gancho" && plan.Gancho.Duracion == 6, "Plan: el gancho va primero (6 s)");
        List<ItemPlan> narr = plan.De("narracion");
        Verificar(narr.Count == 3, "Plan: descarta narración vacía o fuera del video (" + narr.Count + ")");
        double esperado = LogicaPlan.Palabras(narr[0].Texto) * 60.0 / 195 + 0.35;
        Verificar(Math.Abs(narr[0].Duracion - esperado) < 0.01, "Plan: cada frase dura lo que tarda el narrador a 195 ppm (" + narr[0].Duracion.ToString("0.0") + " s)");
        Verificar(narr[1].Inicio >= narr[0].Fin + 0.19, "Plan: las frases no se enciman (la 2ª se corre a " + narr[1].Inicio.ToString("0.0") + " s)");
        Verificar(narr[0].Id == "N01" && narr[2].Id == "N03", "Plan: frases numeradas N01, N02…");
        List<ItemPlan> rec = plan.De("recurso");
        Verificar(rec.Count == 2 && rec[0].Id == "R01" && rec[0].Detalle == "flecha_brillo" && rec[1].Duracion == 6,
                  "Plan: recursos con código, nombre de archivo limpio y duración acotada");
        Verificar(plan.De("recorte").Count == 1 && plan.De("recorte")[0].Clase == "acelerar", "Plan: recortes (descarta los de menos de 1 s)");
        Verificar(plan.De("seccion")[1].Fin == 635, "Plan: secciones dentro del video");
        Verificar(plan.De("avance").Count == 2, "Plan: avances");

        FormatoSerie f = FormatoSerie.Preset("100 días");
        string instr = LogicaPlan.Instrucciones(f, "Gameplay", "Primer capítulo", PapelEpisodio.Reglas(f.Reglas, "Primer capítulo"));
        Verificar(instr.Contains("195 palabras por minuto") && instr.Contains("PRIMER capítulo") && instr.Contains("\"Día 1\"") && instr.Contains("cta"),
                  "Instrucciones: velocidad, papel, avance y CTA");
        string sinN = LogicaPlan.Instrucciones(FormatoSerie.Preset("Podcast"), "Podcast", "Normal", FormatoSerie.Preset("Podcast").Reglas);
        Verificar(sinN.Contains("no lleva narrador") && !sinN.Contains("AVANCES"), "Instrucciones: sin narrador ni avances en un podcast");

        string export = Path.Combine(carpeta, "Cap1.export.json"), trans = Path.Combine(carpeta, "Cap1.vegascut.json");
        if (!File.Exists(export)) { Console.WriteLine("(sin ejemplos/jojmania: se salta la aplicación)"); return Fin(); }

        string dir = Path.Combine(Path.GetTempPath(), "vc-pulir-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            Vegas v = new Vegas();
            v.Generators.Hijos.Add(new PlugInNode { Name = "VEGAS Títulos y texto", UniqueID = "{Svfx:com.vegascreativesoftware:titlesandtext}" });
            v.Project = Rearmar(export);
            v.Project.FilePath = Path.Combine(dir, "Cap1.veg");
            Project p = v.Project;
            Transcripcion t = Transcripcion.Cargar(trans);
            t.Ubicador = PistasVegas.Ubicador(p, t);

            List<Rango> pausas = LogicaPlan.Pausas(t, 635, 2.5);
            Verificar(pausas.Count > 5 && pausas.TrueForAll(delegate (Rango q) { return q.Fin - q.Inicio >= 2.5; }), "Pausas: " + pausas.Count + " huecos sin nadie hablando");
            Medicion med = RitmoVegas.Medir(p, t, "Narrador");
            Informe inf = LogicaPulir.Analizar(med, f, "Primer capítulo");
            string msg = LogicaPlan.Mensaje(t, 635, inf, "Serie: JoJoMania", "- Cap0: abre con un montaje", "Que se note la flecha", pausas);
            Verificar(msg.Contains("Narrador: Cada año") && msg.Contains("PAUSAS") && msg.Contains("no repetir") && msg.Contains("Que se note la flecha"),
                      "Mensaje: transcripción con tiempos, pausas, anteriores e indicaciones");

            // Lo que hay antes, para comparar.
            Track principal = null;
            foreach (Track x in p.Tracks) if (x.Index == 7 && !x.IsAudio()) principal = x;
            int eventosAntes = 0;
            foreach (Track x in p.Tracks) eventosAntes += x.Events.Count;
            double narrAntes = t.Mapear(0, 30);   // un segundo del narrador

            // Plantilla: un evento de imagen con efecto y fundido.
            VideoEvent plantilla = null;
            foreach (Track x in p.Tracks)
                if (!x.IsAudio() && x.Index == 0) plantilla = (VideoEvent)x.Events[0];
            plantilla.Effects.Add(new Effect(new PlugInNode { Name = "Sombra", UniqueID = "sombra" }));
            plantilla.FadeIn.Length = new Timecode(400);

            VozFalsa voz = new VozFalsa();
            List<string> estados = new List<string>();
            ResultadoPlan r = AplicarPlan.Aplicar(v, plan, new OpcionesPlan(), voz, 195, t, plantilla,
                                                  delegate (string s, double fr) { estados.Add(s); });
            Console.WriteLine("      " + r.Texto());
            double corr = r.Corrimiento;
            Verificar(Math.Abs(corr - 6.5) < 0.01 && r.Copiados > 0, "Gancho: corre todo 6.5 s y copia " + r.Copiados + " pedazos al inicio");
            bool alInicio = false;
            foreach (TrackEvent e in principal.Events) if (S(e.Start) < 0.01 && S(e.Length) <= 6.01) alInicio = true;
            Verificar(alInicio, "Gancho: la pista principal empieza con el momento elegido");
            Verificar(p.Regions.Exists(delegate (Region x) { return x.Label == "GANCHO"; }) &&
                      p.Regions.Exists(delegate (Region x) { return x.Label == "SECCIÓN · Llegada" && Math.Abs(S(x.Position) - 6.5) < 0.01; }) &&
                      p.Regions.Exists(delegate (Region x) { return x.Label.StartsWith("ACELERAR · "); }),
                      "Regiones: gancho, secciones y recortes (corridas con el gancho)");
            // El ubicador sigue encontrando la frase donde esta de verdad (no en la copia del gancho).
            Transcripcion t2 = Transcripcion.Cargar(trans);
            t2.Ubicador = PistasVegas.Ubicador(p, t2);
            double narrDespues = t2.Mapear(0, 30);
            Verificar(Math.Abs(narrDespues - (narrAntes + corr)) < 0.01, "Ubicador: la transcripción sigue al video corrido, no a la copia del gancho");

            Track avances = p.Tracks.Find(delegate (Track x) { return x.Name == AplicarPlan.PistaAvances; });
            Verificar(avances != null && avances.Events.Count == 2 && GeneradorTexto.TextoDe(avances.Events[0]) == "Día 1" &&
                      Math.Abs(S(avances.Events[0].Start) - 26.5) < 0.01, "Avances: «Día 1» y «Día 2» con el estilo del texto del proyecto");

            Track phs = p.Tracks.Find(delegate (Track x) { return x.Name == AplicarPlan.PistaPlaceholders; });
            VideoEvent ph = phs == null ? null : (VideoEvent)phs.Events[0];
            Verificar(ph != null && phs.Events.Count == 2 && GeneradorTexto.TextoDe(ph).StartsWith("[R01] IMAGEN: La flecha") &&
                      ph.Effects.Count == plantilla.Effects.Count && ph.FadeIn.Length.ms == 400 && Math.Abs(S(ph.Length) - 3) < 0.01,
                      "Placeholders: copian efectos y fundidos de la plantilla, con su código y descripción");

            Track narrP = p.Tracks.Find(delegate (Track x) { return x.Name == AplicarPlan.PistaNarracion; });
            Verificar(narrP != null && narrP.Events.Count == 3 && r.Narraciones == 3, "Narración: 3 frases en su pista");
            double d0 = S(narrP.Events[0].Length), obj0 = LogicaPlan.Segundos(narr[0].Texto, 195);
            Verificar(Math.Abs(d0 - obj0) / obj0 < 0.12, "Narración: la voz se ajusta a la velocidad del narrador (" + d0.ToString("0.00") + " de " + obj0.ToString("0.00") + " s)");
            Verificar(Math.Abs(S(narrP.Events[0].Start) - (narr[0].Inicio + corr)) < 0.01, "Narración: cada frase en su lugar (corrida con el gancho)");
            Verificar(File.Exists(Path.Combine(AplicarPlan.CarpetaNarracion(p.FilePath), "N01.wav")), "Narración: los WAV quedan junto al proyecto");

            List<Track> grab = RitmoVegas.PistasGrabacion(p, t, "Narrador");
            Envelope env = grab.Count > 0 ? grab[0].Envelopes.FindByType(EnvelopeType.Volume) : null;
            bool baja = false;
            if (env != null)
                foreach (EnvelopePoint pt in env.Points)
                    if (S(pt.X) >= S(narrP.Events[0].Start) - 0.01 && S(pt.X) <= S(narrP.Events[0].End) + 0.01 && pt.Y < 0.5) baja = true;
            bool narradorReal = grab.Exists(delegate (Track x) { foreach (TrackEvent e in x.Events) if ((e.ActiveTake.Media.FilePath ?? "").Contains("Resumen-mejorada")) return true; return false; });
            Verificar(grab.Count >= 3 && !narradorReal && !grab.Exists(delegate (Track x) { return x.Name == AplicarPlan.PistaNarracion; }) && baja,
                      "Bajada: las pistas de grabación (" + grab.Count + ") bajan mientras narra");

            Medicion med2 = RitmoVegas.Medir(p, t2, "Narrador");
            Verificar(med2.Narracion.Count > med.Narracion.Count, "Medidor: cuenta la narración provisional");

            // Guardar plan con lo aplicado y cargarlo.
            plan.De("recorte")[0].Elegido = false;
            Dictionary<string, object> extra = new Dictionary<string, object>();
            extra["aplicado"] = r.Aplicado;
            LogicaPlan.Guardar(p.FilePath, plan, extra);
            LogicaPlan.Guardar(p.FilePath, plan, null);   // guardar de nuevo no pierde lo aplicado
            Plan cargado = LogicaPlan.Cargar(p.FilePath, 635, 195);
            object ap = LogicaPlan.Aplicado(p.FilePath);
            Verificar(cargado != null && !cargado.De("recorte")[0].Elegido && cargado.De("narracion").Count == 3,
                      "Plan: se guarda con lo que descartaste");
            Verificar(ap != null && Json.Lista(ap, "narracion").Count == 3 && Json.Numero(ap, "corrimiento", 0) == 6.5,
                      "Plan: recuerda lo aplicado (para reemplazar la narración)");
            string guion = LogicaPlan.Guion(cargado, "Cap1", corr, 195);
            Verificar(guion.Contains("N01  [0:07.5]") && guion.Contains("R01") && guion.Contains("R01 flecha_brillo"), "Guion: frases con código y tiempo, y recursos");

            // Reemplazar placeholders.
            string recursos = Path.Combine(dir, "recursos");
            Directory.CreateDirectory(Path.Combine(recursos, "sub"));
            File.WriteAllText(Path.Combine(recursos, "sub", "r01 flecha.png"), "x");
            File.WriteAllText(Path.Combine(recursos, "R011 otra.png"), "x");
            List<string> faltan = new List<string>();
            int n = AplicarPlan.ReemplazarPlaceholders(p, recursos, faltan);
            Verificar(n == 1 && faltan.Count == 1 && faltan[0] == "R02" && ph.ActiveTake.Media.FilePath.EndsWith("r01 flecha.png") &&
                      ph.Effects.Count == plantilla.Effects.Count, "Reemplazar: pone la imagen (sin confundir R01 con R011) y conserva los efectos");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // Velocidad de la voz.
        Verificar(VozProvisional.Velocidad(6, 0, 3) > 5 && VozProvisional.Velocidad(3, 0, 3) == 0 && VozProvisional.Velocidad(1, 0, 3) < 0,
                  "Voz: acelera si dura de más y frena si dura de menos");
        return Fin();
    }

    static int Fin()
    {
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos == 0 ? 0 : 1;
    }
}
