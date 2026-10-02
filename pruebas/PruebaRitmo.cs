// Pruebas del medidor de ritmo y del formato de las series. Rearma con la API
// falsa el Cap1 de JoJoMania (ejemplos/jojmania) desde su exportacion y lo
// mide (ver pruebas/ejecutar.sh).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ScriptPortal.Vegas;

class PruebaRitmo
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }

    // Proyecto falso con las pistas y eventos de un .export.json.
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
                ev.ActiveTake = new Take { Media = m };
            }
            p.Tracks.Add(t);
        }
        return p;
    }

    static int Main(string[] args)
    {
        string carpeta = args.Length > 0 ? args[0] : "../ejemplos/jojmania";

        // ----------------------------------------------------- medidor puro
        Medicion m = Ritmo.Medir(300, new List<double> { 1, 2, 3, 61, 62 }, new List<double> { 10, 200 }, new List<double> { 0, 150 },
                                 new List<Rango> { new Rango(0, 20), new Rango(20.3, 40), new Rango(200, 230) }, 160, 50);
        Verificar(m.Minutos.Count == 5 && m.Minutos[0].Cortes == 3 && m.Minutos[1].Cortes == 2 && m.Minutos[3].Recursos == 1,
                  "Medidor: cortes y recursos por minuto");
        Verificar(m.Narracion.Count == 2 && Math.Abs(m.Minutos[0].Narrador - 40 / 60.0) < 0.01 && m.PPM == 192,
                  "Medidor: une frases seguidas del narrador y calcula sus ppm (" + m.PPM + ")");
        List<Rango> sin = Ritmo.SinNarrador(m, 90);
        Verificar(sin.Count == 1 && sin[0].Inicio == 40 && sin[0].Fin == 200,
                  "Medidor: tramos largos sin narrador");
        List<Valle> v = Ritmo.Valles(m, new ReglasRitmo(), true);
        Verificar(v.Count > 0 && v[0].Critico, "Valles: primero los de la zona crítica (" + v[0].Texto + ")");
        Verificar(v.Exists(delegate (Valle x) { return x.Tipo == "musica"; }), "Valles: música que no cambia");

        // ------------------------------------------------- Cap1 de verdad
        string export = Path.Combine(carpeta, "Cap1.export.json"), trans = Path.Combine(carpeta, "Cap1.vegascut.json");
        if (!File.Exists(export)) { Console.WriteLine("(sin ejemplos/jojmania: se salta Cap1)"); }
        else
        {
            Project p = Rearmar(export);
            Transcripcion t = Transcripcion.Cargar(trans);
            Medicion c = RitmoVegas.Medir(p, t, "Narrador");
            Console.WriteLine("      " + Ritmo.Resumen(c));
            Console.Write(Ritmo.Tabla(c).Replace("\n", "\n      ").Insert(0, "      "));
            Console.WriteLine();
            int cortes = 0;
            double narr = 0;
            foreach (MinutoRitmo x in c.Minutos) { cortes += x.Cortes; narr += x.Narrador * 60; }
            double porMin = cortes / (c.Duracion / 60), pct = narr / c.Duracion * 100;
            Verificar(Math.Abs(c.Duracion - 635.1) < 0.5 && c.Minutos.Count == 11, "Cap1: dura 10:35");
            Verificar(porMin > 14 && porMin < 21, "Cap1: 14–21 cortes por minuto en la pista principal (" + porMin.ToString("0.0") + ")");
            Verificar(pct > 28 && pct < 38, "Cap1: el narrador ocupa ~33 % (" + pct.ToString("0") + " %)");
            Verificar(c.PPM > 160 && c.PPM < 230, "Cap1: velocidad del narrador (" + c.PPM + " ppm)");
            Verificar(c.Minutos[0].Recursos > c.Minutos[5].Recursos && c.Minutos[0].Recursos >= 10, "Cap1: el inicio va cargado de recursos");
            Verificar(c.CambiosMusica.Count >= 10, "Cap1: reconoce la pista de música (" + c.CambiosMusica.Count + " temas)");
            Verificar(c.Minutos[6].Narrador < 0.05 && c.Minutos[7].Narrador < 0.05, "Cap1: minutos 6 y 7 sin narrador");
            List<Valle> vc = Ritmo.Valles(c, FormatoSerie.Preset("100 días").Reglas, true);
            Verificar(vc.Exists(delegate (Valle x) { return x.Tipo == "narrador" && x.Inicio < 7 * 60 && x.Fin > 7 * 60; }),
                      "Cap1: valle sin narrador en el minuto 6–7");
            foreach (Valle x in vc) Console.WriteLine("      " + (x.Critico ? "! " : "  ") + Formato.Tiempo(x.Inicio) + "–" + Formato.Tiempo(x.Fin) + " " + x.Texto);
            ReglasRitmo ap = Ritmo.Aprender(c, new ReglasRitmo());
            Verificar(ap.CortesMin >= 12 && ap.CortesMax <= 25 && ap.CortesMin < ap.CortesMax && ap.DuracionMin == 10 && ap.DuracionMax == 12,
                      "Aprender: reglas de Cap1 (" + ap.CortesMin + "–" + ap.CortesMax + " cortes, " + ap.RecursosPorMin + " recursos, narrador cada " +
                      ap.NarradorCadaSeg + " s, " + ap.PPM + " ppm, música " + ap.MusicaCadaSeg + " s, " + ap.DuracionMin + "–" + ap.DuracionMax + " min)");
        }

        // ------------------------------------------------ formato de serie
        string dir = Path.Combine(Path.GetTempPath(), "vc-ritmo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            SerieProyecto s = new SerieProyecto();
            s.Nombre = "SBR"; s.Ruta = SerieProyecto.RutaPara(dir, "SBR");
            string e1 = Path.Combine(dir, "S01E01 SBR.veg"), e2 = Path.Combine(dir, "S01E02 SBR.veg"), e3 = Path.Combine(dir, "S01E03 SBR.veg");
            foreach (string e in new string[] { e1, e2, e3 }) { File.WriteAllText(e, ""); s.Agregar(e); }
            Verificar(s.Papel(e1) == "Primer capítulo" && s.Papel(e2) == "Normal", "Papel: el primero presenta, los demás son normales");
            s.Formato = FormatoSerie.Preset("Aventura por episodios");
            s.Formato.Premisa = "Recorrer la carrera de Steel Ball Run hasta el final";
            s.Formato.Reglas.PPM = 190;
            s.CambiarPapel(e3, "Final de temporada");
            s.NotasEpisodio[e2] = "Capítulo de la carrera de caballos";
            s.Guardar();
            SerieProyecto l = SerieProyecto.Cargar(s.Ruta);
            Verificar(l.Formato.Nombre == "Aventura por episodios" && l.Formato.Avance == "Parte N" && l.Formato.Reglas.PPM == 190 &&
                      l.Formato.Premisa.StartsWith("Recorrer"), "Formato: se guarda en la serie y se lee igual");
            Verificar(l.Papel(e3) == "Final de temporada" && l.NotaEpisodio(e2).Contains("caballos"), "Papel y nota de cada capítulo se guardan");
            ReglasRitmo fin = PapelEpisodio.Reglas(l.Formato.Reglas, "Final de temporada");
            Verificar(fin.DuracionMax > l.Formato.Reglas.DuracionMax, "Final de temporada: puede durar más (" + fin.DuracionMax + " min)");
            string ctx = Serie.Contexto(l, l.Capitulos(e2));
            Verificar(ctx.Contains("Aventura por episodios") && ctx.Contains("Recorrer la carrera") && ctx.Contains("Este capítulo (2 de 3): Normal") &&
                      ctx.Contains("carrera de caballos"), "Contexto: formato, premisa, papel y nota del capítulo");
            string ctx3 = Serie.Contexto(l, l.Capitulos(e3));
            Verificar(ctx3.Contains("FINAL DE TEMPORADA"), "Contexto: el final pide cerrar hilos");
            Verificar(FormatoSerie.Preset("100 días").Marca(4) == "Día 4" && FormatoSerie.Preset("Podcast").Marca(4) == "",
                      "Marca de avance: «Día 4» (y nada en un podcast)");

            // Una serie guardada antes de que existiera el formato.
            string vieja = Path.Combine(dir, "vieja" + SerieProyecto.Extension);
            File.WriteAllText(vieja, "{\"formato\": \"vegas-cut-serie\", \"nombre\": \"Ensayos\", \"tipo\": \"Video ensayo\", \"episodios\": []}");
            SerieProyecto ve = SerieProyecto.Cargar(vieja);
            Verificar(ve.Formato.Nombre == "Video ensayo" && ve.Formato.Narrador && ve.Formato.Reglas.NarradorCadaSeg == 20,
                      "Serie vieja: toma el formato según su tipo");
            Verificar(Serie.InstruccionesFicha("Gameplay").Contains("\"estructura\""), "Ficha: guarda cómo abre y cierra (para no repetir la fórmula)");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos == 0 ? 0 : 1;
    }
}
