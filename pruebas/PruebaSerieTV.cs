// Pruebas del formato Serie de TV, la biblioteca de musica (con el listado real
// de ejemplos/musica) y los temas por personaje (ver pruebas/ejecutar.sh).
using System;
using System.Collections.Generic;
using System.IO;

class PruebaSerieTV
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }

    static ArchivoMusica Por(BibliotecaMusica b, string titulo, string album)
    {
        foreach (ArchivoMusica a in b.Archivos) if (a.Titulo == titulo && a.Album.Contains(album)) return a;
        return null;
    }

    static int Main(string[] args)
    {
        string csv = args.Length > 0 ? args[0] : "../ejemplos/musica/musica.csv";
        string dir = Path.Combine(Path.GetTempPath(), "vc-tv-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // ------------------------------------------------ formato y plantilla
            FormatoSerie f = FormatoSerie.Preset(FormatoSerie.TV);
            Verificar(f.EsTV && f.Avance == "Etapa N" && f.Reglas.DuracionMin == 15 && f.Reglas.DuracionMax == 18 && f.Narrador,
                      "Serie de TV: 15–18 min, «Etapa N» y narrador como Johnny");
            PlantillaTV p = f.Tv;
            Verificar(p.Bloques.Count == 9 && p.Bloques[0].Clave == "cold_open" && p.Bloque("op").Tipo == "kit" && p.Fijo() == 104,
                      "Plantilla: cold open, OP, título, actos, re-gancho, continuará, ED y avance (" + p.Fijo() + " s fijos)");
            double a = p.Acto("acto_a", 16 * 60), b = p.Acto("acto_b", 16 * 60);
            Verificar(Math.Abs(a + b + p.Fijo() - 960) < 0.01 && a > b, "Plantilla: los actos llenan el resto (" + Formato.Tiempo(a) + " + " + Formato.Tiempo(b) + ")");

            SerieProyecto s = new SerieProyecto();
            s.Nombre = "SCR"; s.Ruta = SerieProyecto.RutaPara(dir, "SCR");
            s.Formato = f;
            s.Formato.Tv.Kit["op"] = @"K:\SCR\op.mp4";
            s.Formato.Tv.Bloque("ed").Segundos = 20;
            s.Musica.Carpeta = @"K:\Musica";
            s.Musica.Reparto = "AB Fann: yo, el narrador, estratega\nGerber — impulsivo, siempre pelea\n- Cuevas - el Inge, construye todo\nKarta";
            s.Guardar();
            SerieProyecto l = SerieProyecto.Cargar(s.Ruta);
            Verificar(l.Formato.EsTV && l.Formato.Tv.Archivo("op") == @"K:\SCR\op.mp4" && l.Formato.Tv.Bloque("ed").Segundos == 20 &&
                      l.Formato.Tv.Archivo("regancho") == "", "Kit: el OP elegido se guarda; lo que falta queda como placeholder");
            List<string> nombres = l.Musica.Nombres();
            Verificar(nombres.Count == 4 && nombres[0] == "AB Fann" && nombres[1] == "Gerber" && nombres[2] == "Cuevas" && nombres[3] == "Karta",
                      "Reparto: nombres de «Nombre: cómo es» (" + String.Join(", ", nombres.ToArray()) + ")");
            Verificar(FormatoSerie.Preset("100 días").Escribir().ContainsKey("tv") == false, "Otros formatos no guardan plantilla de TV");

            // ------------------------------------------------- biblioteca
            if (!File.Exists(csv)) { Console.WriteLine("(sin ejemplos/musica: se salta la biblioteca)"); return Fin(); }
            List<FilaMusica> filas = BibliotecaMusica.DesdeCsv(csv);
            DateTime t0 = DateTime.Now;
            BibliotecaMusica bib = BibliotecaMusica.Indexar(dir, filas, null);
            double ms = (DateTime.Now - t0).TotalMilliseconds;
            int con = 0;
            foreach (ArchivoMusica x in bib.Archivos) if (x.ConUso) con++;
            Verificar(filas.Count == 1305 && con >= 370, "Biblioteca: " + con + " de " + filas.Count + " archivos con datos del anime (" + Math.Round(ms) + " ms)");
            ArchivoMusica calm = Por(bib, "Calm Sightseeing", "Departure"), fire = Por(bib, "Fire Shaman", "Departure");
            Verificar(calm != null && calm.Parte == "SC" && calm.Animos.Contains("viaje") && calm.Usos >= 10,
                      "Calm Sightseeing: viaje, " + (calm == null ? 0 : calm.Usos) + " escenas en SC");
            Verificar(fire != null && fire.TemaAnime == "The Magician of Fire", "Otra traducción: «Fire Shaman» es «The Magician of Fire»");
            ArchivoMusica det = Por(bib, "Determination", "Departure") ?? Por(bib, "Determination", "Stardust");
            Verificar(det == null || det.Parte == "SC", "El mismo nombre en otra parte no se mezcla");
            ArchivoMusica wind = Por(bib, "Wind In The Wilderness", "Departure");
            Verificar(wind != null && wind.TemaDe.Contains("hol horse"), "Tema de personaje: Wind in the Wilderness es de Hol Horse");
            ArchivoMusica fg = null;
            foreach (ArchivoMusica x in bib.Archivos) if (x.Titulo == "Fighting Gold (Instrumental)") fg = x;
            Verificar(fg != null && fg.Variantes.Count >= 2, "Variantes: Fighting Gold tiene " + (fg == null ? 0 : fg.Variantes.Count) + " versiones más");
            ArchivoMusica juego = null;
            foreach (ArchivoMusica x in bib.Archivos) if (x.Fuente == "videojuego" && x.Titulo.ToLowerInvariant().Contains("battle")) { juego = x; break; }
            Verificar(juego != null && !juego.ConUso && juego.Animos.Contains("pelea"), "Sin datos del anime: ánimo por el título (" + (juego == null ? "" : juego.Titulo) + ")");

            bib.Guardar(Path.Combine(dir, BibliotecaMusica.NombreIndice));
            BibliotecaMusica cb = BibliotecaMusica.Cargar(dir);
            ArchivoMusica calm2 = cb.Buscar(calm.Ruta);
            Verificar(cb.Archivos.Count == 1305 && calm2 != null && calm2.Usos == calm.Usos && calm2.Animos.Count == calm.Animos.Count &&
                      calm2.Escenas.Count > 0, "Índice: se guarda en la carpeta y se lee igual");

            // Escanear sin etiquetas (en Linux no hay Explorador): por el nombre del archivo.
            string m = Path.Combine(dir, "musica");
            Directory.CreateDirectory(Path.Combine(m, "SC Departure"));
            File.WriteAllText(Path.Combine(m, "SC Departure", "15. Calm Sightseeing.mp3"), "x");
            File.WriteAllText(Path.Combine(m, "notas.txt"), "x");
            List<FilaMusica> esc = BibliotecaMusica.Escanear(m, null);
            Verificar(esc.Count == 1 && esc[0].Ruta == Path.Combine("SC Departure", "15. Calm Sightseeing.mp3"),
                      "Escanear: solo música, con ruta relativa a la carpeta");

            // ---------------------------------------------- temas con Gemini
            List<ArchivoMusica> cand = MusicaSerie.Candidatos(bib);
            int conFg = 0;
            foreach (ArchivoMusica x in cand) if (x.TemaAnime == "Fighting Gold") conFg++;
            Verificar(cand.Count > 200 && conFg == 1, "Candidatos: " + cand.Count + " temas, cada uno una vez (sin sus variantes)");
            string msg = MusicaSerie.Mensaje(cand, "Carrera por etapas", l.Musica.Reparto, "para Gerber algo de Golden Wind");
            Verificar(msg.Contains("Gerber") && msg.Contains("[0]") && msg.Contains("Golden Wind") && msg.Contains("tema de"),
                      "Mensaje: reparto, preferencias y biblioteca con ids");
            int iCalm = cand.FindIndex(delegate (ArchivoMusica x) { return x.TemaAnime == "Calm Sightseeing"; });
            int iFg = cand.FindIndex(delegate (ArchivoMusica x) { return x.TemaAnime == "Fighting Gold"; });
            int iWind = cand.FindIndex(delegate (ArchivoMusica x) { return x.TemaAnime == "Wind in the Wilderness"; });
            string resp = "{\"principal\": {\"id\": " + iFg + ", \"motivo\": \"épico\"}, \"personajes\": [" +
                          "{\"nombre\": \"gerber\", \"id\": " + iWind + ", \"motivo\": \"vaquero impulsivo\"}," +
                          "{\"nombre\": \"Cuevas\", \"id\": " + iWind + ", \"motivo\": \"repetido\"}," +
                          "{\"nombre\": \"Karta\", \"id\": " + iCalm + ", \"motivo\": \"tranquila\"}," +
                          "{\"nombre\": \"Nadie\", \"id\": 1, \"motivo\": \"no está\"}, {\"nombre\": \"AB Fann\", \"id\": 99999}]}";
            int n = l.Musica.Aplicar(resp, cand);
            Verificar(n == 3 && l.Musica.Principal.Variantes.Count >= 2 && l.Musica.Personajes.ContainsKey("Gerber") &&
                      !l.Musica.Personajes.ContainsKey("Cuevas") && !l.Musica.Personajes.ContainsKey("Nadie") && l.Musica.Personajes.ContainsKey("Karta"),
                      "Gemini: principal con variantes, un tema por personaje, sin repetir ni inventar");
            l.Guardar();
            SerieProyecto l2 = SerieProyecto.Cargar(l.Ruta);
            Verificar(l2.Musica.Personajes["Gerber"].Archivo == cand[iWind].Ruta && l2.Musica.Principal.Archivo == cand[iFg].Ruta &&
                      l2.Musica.Principal.Variantes.Count == l.Musica.Principal.Variantes.Count, "Los temas quedan guardados en la serie");
            Verificar(MusicaSerie.Instrucciones().Contains("UN TEMA PARA CADA PERSONAJE"), "Instrucciones para Gemini");
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
