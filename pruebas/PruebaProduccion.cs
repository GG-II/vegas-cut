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
        Verificar(a.Propuestas.Count == 3 && a.Propuestas[1].Id == "B" && a.Propuestas[1].Doble && a.Propuestas[2].Tipo == "normal" &&
                  Propuesta.Normalizar("doble duración") == "doble_duracion",
                  "Análisis: tres propuestas con id único (la repetida pasa a B) y su tipo");
        Verificar(a.Momentos.Count == 2 && a.Momentos[0].Inicio == 20 && a.DobleRecomendado && a.Hilos.Count == 1,
                  "Análisis: momentos en orden y dentro del material, doble recomendado, hilos");
        FormatoSerie f = FormatoSerie.Preset(FormatoSerie.TV);
        string ia = LogicaProduccion.InstruccionesAnalisis(f, "Gameplay", "Normal");
        Verificar(ia.Contains("TRES") && ia.Contains("~13 %") && ia.Contains("15–18") && ia.Contains("Parte 1"),
                  "Instrucciones: tres propuestas, estructura de referencia, duración y títulos de episodio doble");

        // ------------------------------------------------ tipo de capitulo
        AnalisisCapitulo at = LogicaProduccion.LeerAnalisis(@"{""resumen"": ""x"", ""tipo_capitulo"": {""sugerido"": ""Juego o apuesta"",
            ""alternativas"": [""foco"", ""juego"", ""inventado""], ""motivo"": ""apuestan diamantes""}, ""papel"": {""sugerido"": ""Capítulo clave"", ""motivo"": ""muere Gerber""},
            ""propuestas"": [{""id"": ""A"", ""nombre"": ""La apuesta"", ""tipo_capitulo"": ""juego""}, {""id"": ""B"", ""nombre"": ""Gerber"", ""tipo_capitulo"": ""otro""}]}", 600);
        Verificar(at.TipoSugerido == "juego" && at.TiposAlternativos.Count == 1 && at.TiposAlternativos[0] == "foco" && at.TipoMotivo == "apuestan diamantes" &&
                  at.PapelSugerido == "Capítulo clave" && at.Propuestas[0].TipoCap == "juego" && at.Propuestas[1].TipoCap == "",
                  "Análisis: detecta el tipo de capítulo (por clave o nombre), alternativas válidas y el papel sugerido");
        string iat = LogicaProduccion.InstruccionesAnalisis(f, "Gameplay", "Normal", "misterio");
        Verificar(TiposCapitulo.Todos.Length >= 20 && ia.Contains("TIPOS DE CAPÍTULO") && ia.Contains("ANTERIOR") && ia.Contains("tipos distintos") &&
                  iat.Contains("EL EDITOR PIDE QUE SEA: misterio") && !iat.Contains("tipos distintos"),
                  "Instrucciones: catálogo de " + TiposCapitulo.Todos.Length + " tipos, detectar según material, número, anterior y lo pedido");
        Verificar(LogicaProduccion.InstruccionesFinal(f, "Normal", at.Propuestas[0]).Contains("TIPO DE CAPÍTULO: Juego o apuesta") &&
                  TiposCapitulo.Opciones()[0] == TiposCapitulo.Detectar && TiposCapitulo.Normalizar("Misterio o investigación") == "misterio",
                  "Escaleta final: sigue el tipo de la propuesta elegida");

        // ------------------------------------------------ lo inventado
        Transcripcion tf = new Transcripcion();
        tf.Hablantes.Add(new Hablante { Nombre = "AB Fann" });
        tf.Segmentos.Add(new Segmento { Hablante = 0, Inicio = 0, Fin = 5, Texto = "Bienvenidos al salvaje oeste, aquí están los forajidos y los caballos." });
        Propuesta pf = new Propuesta { Nombre = "Elegía del Oeste", ColdOpen = "Vistas del Salvaje Oeste" };
        pf.Titulos.Add("Etapa 1 · El amanecer en la Palma del Diablo"); pf.Titulos.Add("Parte 1: Forajidos y Monturas");
        pf.Escaleta.Add("[00:00] AB Fann entrega el kit a Jason");
        List<string> nf = LogicaProduccion.NoEnMaterial(pf, tf, "Jason: caótico");
        string mrf = LogicaProduccion.MensajeRefinar(new AnalisisCapitulo { Propuestas = new List<Propuesta> { pf } }, "", "", tf);
        Verificar(nf.Contains("Palma") && nf.Contains("Diablo") && nf.Contains("Monturas") && !nf.Contains("Forajidos") && !nf.Contains("Oeste") &&
                  !nf.Contains("Jason") && !nf.Contains("Fann") && !nf.Contains("Etapa") && mrf.Contains("NO SALEN EN EL MATERIAL") &&
                  ia.Contains("FIDELIDAD") && ia.Contains("MISMO material"),
                  "Fidelidad: marca lo que no sale en el material (" + String.Join(", ", nf.ToArray()) + ") y Refinar lo corrige; las tres cubren lo mismo");

        // ------------------------------------------------ no repetir material
        PlanFinal rep = LogicaProduccion.LeerFinal(@"{""partes"": [{""titulo"": ""X"", ""estructura"": [{""bloque"": ""cold_open"", ""tipo"": ""contenido""},
            {""bloque"": ""acto_a"", ""tipo"": ""contenido""}, {""bloque"": ""avance"", ""tipo"": ""contenido""}],
            ""bloques"": [{""bloque"": ""acto_a"", ""clips"": [{""inicio"": 0, ""fin"": 60}, {""inicio"": 100, ""fin"": 130, ""respiro"": 2}, {""inicio"": 200, ""fin"": 201.5}]},
                          {""bloque"": ""cold_open"", ""clips"": [{""inicio"": 10, ""fin"": 40}, {""inicio"": 195, ""fin"": 205}]},
                          {""bloque"": ""avance"", ""clips"": [{""inicio"": 110, ""fin"": 120}]}]}]}", 600, 0, 195);
        List<ItemFinal> rc = rep.Partes[0].Items.FindAll(delegate (ItemFinal i) { return i.Tipo == "clip"; });
        Verificar(rc.Count == 5 && rc[0].Bloque == "cold_open" && rc[2].Inicio == 0 && rc[2].Fin == 10 && rc[3].Inicio == 40 && rc[3].Fin == 60 &&
                  rc[4].Inicio == 100 && rc[4].Respiro == 2 && rep.Partes[0].Bloque("avance").Tipo == "kit" && rep.Partes[0].Bloque("avance").Segundos > 0 &&
                  Math.Abs(rep.Repetido - 41.5) < 0.01,
                  "Escaleta: nada se repite (lo del cold open se recorta de los actos); el avance es del próximo capítulo y queda como placeholder");

        // ------------------------------------------------ quien habla
        Transcripcion tb = new Transcripcion(), to = new Transcripcion();
        tb.Hablantes.Add(new Hablante { Etiqueta = "A2", Nombre = "A2", Voz = true });
        tb.Hablantes.Add(new Hablante { Etiqueta = "A3", Nombre = "Jason", Voz = true });
        tb.Hablantes.Add(new Hablante { Etiqueta = "A5", Nombre = "A5", Voz = false });
        to.Hablantes.Add(new Hablante { Etiqueta = "A2", Nombre = "AB Fann", Voz = true });
        to.Hablantes.Add(new Hablante { Etiqueta = "A3", Nombre = "Otro", Voz = true });
        int cn2 = LogicaProduccion.CopiarNombres(tb, to);
        string qh = LogicaProduccion.MensajeAnalisis(tb, 60, "", "", "");
        Verificar(cn2 == 1 && tb.Hablantes[0].Nombre == "AB Fann" && tb.Hablantes[1].Nombre == "Jason" && qh.Contains("QUIÉN HABLA EN CADA PISTA") &&
                  qh.Contains("- A2: AB Fann") && !qh.Contains("A5"),
                  "Quién habla: los nombres de MomentosIA pasan a la copia BASE (sin pisar los que ya tiene) y Gemini los recibe por pista");

        // ------------------------------------------------ duracion pedida
        PlanFinal corta = LogicaProduccion.LeerFinal(@"{""partes"": [{""titulo"": ""X"", ""estructura"": [{""bloque"": ""acto_a"", ""tipo"": ""contenido""}],
            ""bloques"": [{""bloque"": ""acto_a"", ""clips"": [{""inicio"": 100, ""fin"": 220}, {""inicio"": 400, ""fin"": 580}]}]}]}", 3600, 0, 195);
        string cc = LogicaProduccion.Correccion(corta, 10, 12, PlantillaTV.PorDefecto(), 3600, null);
        PlanFinal justa = LogicaProduccion.LeerFinal(@"{""partes"": [{""titulo"": ""X"", ""estructura"": [{""bloque"": ""acto_a"", ""tipo"": ""contenido""}],
            ""bloques"": [{""bloque"": ""acto_a"", ""clips"": [{""inicio"": 0, ""fin"": 330}, {""inicio"": 400, ""fin"": 730}]}]}]}", 3600, 0, 195);
        Verificar(cc != null && cc.Contains("dura 5:00") && cc.Contains("FALTAN unos 6") && cc.Contains("[580.0-3600.0]") && cc.Contains("[0.0-100.0]") &&
                  LogicaProduccion.Correccion(justa, 10, 12, PlantillaTV.PorDefecto(), 3600, null) == null &&
                  LogicaProduccion.Correccion(justa, 3, 5, PlantillaTV.PorDefecto(), 3600, null).Contains("SOBRAN") &&
                  LogicaProduccion.InstruccionesFinal(f, "Normal", null).Contains("segundos_totales"),
                  "Duración: si la escaleta queda corta se pide alargarla con los tramos que no se usaron; si sobra, recortarla");

        // ------------------------------------------------ hueco para la narracion y charla sensible
        CapituloFinal cn = new CapituloFinal();
        cn.Estructura.Add(new BloqueTV("acto_a", "Acto A", "contenido", 0, 100, ""));
        cn.Items.Add(new ItemFinal { Tipo = "clip", Bloque = "acto_a", Inicio = 0, Fin = 30 });
        cn.Items.Add(new ItemFinal { Tipo = "clip", Bloque = "acto_a", Inicio = 50, Fin = 80 });
        ItemFinal na1 = new ItemFinal { Tipo = "narracion", Inicio = 5, Texto = "Así empezó todo, con cuatro amigos y un caballo." };
        ItemFinal na2 = new ItemFinal { Tipo = "narracion", Inicio = 55, Texto = "Corto." };
        cn.Items.Add(na1); cn.Items.Add(na2);
        List<Rango> vz = new List<Rango> { new Rango(0, 27), new Rango(50, 60), new Rango(68, 80) };
        Dictionary<ItemFinal, double> dn = new Dictionary<ItemFinal, double>();
        Dictionary<ItemFinal, double> hx = ArmarCapitulo.HuecosNarracion(cn, vz, 150, dn);
        double l1 = LogicaPlan.Segundos(na1.Texto, 150) + 0.4;
        Verificar(hx.Count == 1 && Math.Abs(hx[cn.Items[0]] - (l1 - 3)) < 0.01 && dn[na1] == 27 && !dn.ContainsKey(na2),
                  "Narración: si no hay pausa, el clip se alarga (el juego sigue sin voces) lo que falta; si cabe en una pausa, no");
        Verificar(LogicaProduccion.Sensible("oye, ¿cómo instalo el modpack?") == "técnico" && LogicaProduccion.Sensible("se me laggea, tengo lag") == "técnico" &&
                  LogicaProduccion.Sensible("mañana tengo examen en la uni") == "personal" && LogicaProduccion.Sensible("vamos a fabricar una espada") == "" &&
                  LogicaProduccion.Sensible("ese obstáculo") == "" && LogicaProduccion.Material(tf, 10000).Length > 0 && ia.Contains("PRIVACIDAD"),
                  "Charla técnica o personal: se marca en la transcripción para dejarla fuera");

        // ------------------------------------------------ opciones, estreno, tres propuestas
        OpcionesCapitulo opx = OpcionesCapitulo.PorDefecto("Primer capítulo", 1, PapelEpisodio.Reglas(f.Reglas, "Primer capítulo"));
        opx.Estado["op"] = 0; opx.Estado["doble"] = 1; opx.MinutosMin = 30; opx.MinutosMax = 40;
        string ot = opx.Texto(5622);
        OpcionesCapitulo op2 = OpcionesCapitulo.Leer(Json.Leer(Json.Escribir(opx.Escribir())));
        Verificar(ot.Contains("INICIO CINEMATOGRÁFICO") && ot.Contains("PRESENTAR A CADA PERSONAJE") && ot.Contains("SIN opening") &&
                  ot.Contains("SIN recap") && ot.Contains("DOBLE DURACIÓN") && ot.Contains("entre 30 y 40 min") && ot.Contains("El material dura 94 min") &&
                  !ot.Contains("Ending") && op2.Valor("op") == 0 && op2.Valor("cine") == 1 && op2.Valor("ed") == -1 && op2.MinutosMax == 40,
                  "Opciones: el primer capítulo empieza cinematográfico y presenta a cada uno; las casillas en sí/no se piden, las demás decide la IA");
        AnalisisCapitulo uno = LogicaProduccion.LeerAnalisis(@"{""resumen"": ""x"", ""minutos_utiles"": 5622, ""propuestas"": [{""id"": ""A"", ""nombre"": ""Sola"", ""minutos"": 93}]}", 5622);
        Verificar(Math.Abs(uno.MinutosUtiles - 93.7) < 0.1 && uno.Propuestas.Count == 1 &&
                  LogicaProduccion.InstruccionesCompletar(f, "Primer capítulo").Contains("TRES") && ia.Contains("SIEMPRE TRES") &&
                  TiposCapitulo.Buscar("estreno") != null && TiposCapitulo.Instrucciones("estreno").Contains("CINEMATOGRÁFICO"),
                  "Tres propuestas siempre (se piden las que faltan), minutos útiles en segundos se corrigen, tipo Estreno");

        a.Propuestas[1].Notas = "que sea un solo video de doble duración, sin opening en la segunda mitad, unido por eyecatch";
        string mr = LogicaProduccion.MensajeRefinar(a, "más intro", "es el primer capítulo", null);
        Verificar(mr.Contains("NOTAS DEL EDITOR: que sea un solo video") && mr.Contains("NOTAS DEL EDITOR: (ninguna)") && mr.Contains("más intro") &&
                  LogicaProduccion.InstruccionesRefinar(FormatoSerie.Preset(FormatoSerie.TV), "Primer capítulo").Contains("mandan"),
                  "Refinar: cada propuesta con sus notas; las notas mandan");
        LogicaProduccion.Refinar(a, "{\"propuestas\": [{\"id\": \"A\", \"nombre\": \"Clásica\"}, {\"id\": \"B\", \"nombre\": \"Doble duración\", " +
            "\"tipo\": \"doble_duracion\", \"estructura\": [\"Intro (3:00)\", \"Título\", \"Acto A\", \"Eyecatch\", \"Acto B\"]}, {\"id\": \"C\", \"nombre\": \"Especial\"}]}");
        Verificar(a.Propuestas.Count == 3 && a.Propuestas[1].DobleDuracion && a.Propuestas[1].Estructura.Count == 5 && a.Propuestas[1].Notas == "" &&
                  a.Momentos.Count == 2 && a.RespuestaPropuestas.Length > 0, "Refinar: cambian las propuestas, el análisis se queda (sin analizar de nuevo)");

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
            Transcripcion tm = File.Exists(Path.Combine(carpeta, "Cap1.vegascut.json")) ? Transcripcion.Cargar(Path.Combine(carpeta, "Cap1.vegascut.json")) : null;
            string mf = LogicaProduccion.MensajeFinal(a, a.Propuestas[1], "más Gerber", "primer capítulo sin opening", cand, ms, LogicaProduccion.Reparto(ms),
                                                      tm, 635, fin, "intro más larga");
            Verificar(mf.StartsWith("LO QUE PIDE EL EDITOR") && mf.Contains("CAMBIOS PEDIDOS AHORA: intro más larga") && mf.Contains("sin opening") &&
                      mf.Contains("PROPUESTA ELEGIDA:\nB · Doble duración") && mf.Contains("BIBLIOTECA DE MÚSICA") && mf.Contains("Tema de Gerber") &&
                      mf.Contains("PAUSAS DEL MATERIAL"), "Mensaje final: lo que pide el editor primero, propuesta, biblioteca, temas y pausas");
            string inf = LogicaProduccion.InstruccionesFinal(f, "Normal", a.Propuestas[1]);
            Verificar(inf.Contains("dos_partes") && inf.Contains("PRIORIDAD") && inf.Contains("eyecatch") && inf.Contains("NUNCA encima de las voces") &&
                      inf.Contains("respiro") && inf.Contains("ritmo"), "Instrucciones finales: notas primero, estructura libre, ritmo y narración en pausas");
            Verificar(inf.Contains("TÍTULOS al estilo JoJo") && inf.Contains("Curva de ritmo") && inf.Contains("antes del opening"),
                      "Instrucciones finales: títulos, frases de gancho y curva de ritmo");

            // La serie recuerda lo producido: el siguiente capitulo sabe como cerro el anterior.
            SerieProyecto sp = new SerieProyecto { Nombre = "SCR", Ruta = Path.Combine(dir, "SCR.vegascut-serie.json") };
            string e1 = Path.Combine(dir, "S01E01 SCR.veg"), e2 = Path.Combine(dir, "S01E02 SCR.veg"), e3 = Path.Combine(dir, "S01E03 SCR.veg");
            sp.Episodios.AddRange(new string[] { e1, e2, e3 });
            sp.Producidos[e1] = new RegistroCapitulo { Tipo = "arco_abre", Forma = "normal", Titulo = "La flecha", Cierre = "Jason cae a la lava" };
            sp.Producidos[e2] = new RegistroCapitulo { Tipo = "Juego o apuesta" };
            sp.NotasEpisodio[e3] = "que Steel brille";
            try { sp.Guardar(); } catch { }
            SerieProyecto sp2 = SerieProyecto.Cargar(sp.Ruta);
            string ctx3 = Serie.Contexto(sp2, sp2.Capitulos(e3)), ctx2 = Serie.Contexto(sp2, sp2.Capitulos(e2)), ctx1 = Serie.Contexto(sp2, sp2.Capitulos(e1));
            Verificar(sp2.Producido(e1) != null && sp2.Producido(e1).Cierre == "Jason cae a la lava" && sp2.Producido(e2).Tipo == "juego" &&
                      ctx2.Contains("CAPÍTULO ANTERIOR (1, S01E01 SCR, Primer capítulo)") && ctx2.Contains("cerró con: Jason cae a la lava") && ctx2.Contains("Abre un enfrentamiento") &&
                      ctx3.Contains("Este capítulo (3 de 3)") && ctx3.Contains("que Steel brille") && ctx3.Contains("Tipos de los últimos capítulos: 1: Abre un enfrentamiento largo (parte 1), 2: Juego o apuesta") &&
                      ctx1.Contains("es el primero de la serie"),
                      "Serie: guarda tipo, título y cierre de lo producido; el contexto da el número, el anterior y los tipos recientes");

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
            ResultadoProduccion r = ArmarCapitulo.EnCopia(v, fin, f, ms, bib, cand, null, new VozFalsa(), 195, t,
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
            Verificar(kitA != null && kitA.Events.Count == 2 && phKit == 7 && stats,
                      "Kit: el OP con su archivo (video y audio) y placeholders para lo que falta (también el avance de la parte 2, que es del próximo capítulo), con la tarjeta de stats");
            Track textos = pr.Tracks.Find(delegate (Track x) { return x.Name == ArmarCapitulo.PistaTextos; });
            List<string> tt = new List<string>();
            foreach (TrackEvent e in textos.Events) tt.Add(GeneradorTexto.TextoDe(e));
            Verificar(tt.Contains("Etapa 1 · La flecha Parte 1") && tt.Contains("Etapa 1 · La flecha Parte 2") && tt.Contains("El server") && !tt.Contains("fuera"),
                      "Textos: título de cada parte y carteles en su momento (los que quedaron fuera no se ponen)");
            Track mus = pr.Tracks.Find(delegate (Track x) { return x.Name == ArmarCapitulo.PistaMusica; });
            Verificar(mus != null && mus.Events.Count == 2 && r.Temas == 2 && mus.Envelopes.Count == 0,
                      "Música: un tema por entrada (también el del personaje) y sin balancear");
            Verificar(Math.Abs(((AudioTrack)mus).Volume - 0.0891) < 0.001, "Música: la pista queda a -21 dB");

            // ---- rellenar la musica en un capitulo ya editado
            Project pj = new Project();
            VideoTrack vt = new VideoTrack(0, "Video"); pj.Tracks.Add(vt);
            vt.AddVideoEvent(Timecode.FromMilliseconds(0), Timecode.FromMilliseconds(100000)).ActiveTake = new Take { Media = new Media(Path.Combine(dir, "juego.mp4")) };
            VideoTrack kt = new VideoTrack(1, ArmarCapitulo.PistaKit); pj.Tracks.Add(kt);
            kt.AddVideoEvent(Timecode.FromMilliseconds(0), Timecode.FromMilliseconds(10000));
            AudioTrack mt = new AudioTrack(2, LogicaRelleno.PistaMusica); pj.Tracks.Add(mt);
            AudioEvent ya = mt.AddAudioEvent(Timecode.FromMilliseconds(20000), Timecode.FromMilliseconds(30000));
            ya.ActiveTake = new Take { Media = new Media(Path.Combine(dir, "Ya suena.mp3")) };
            pj.Regions.Add(new Region(Timecode.FromMilliseconds(50000), Timecode.FromMilliseconds(50000), "ACTO A"));
            List<HuecoMusica> hm = LogicaRelleno.Huecos(pj, null, 8);
            string relleno = Path.Combine(dir, "Calma.mp3");
            File.WriteAllText(relleno, "x");
            List<ArchivoMusica> cr = new List<ArchivoMusica> { new ArchivoMusica { Ruta = relleno, Titulo = "Calma", Animos = new List<string> { "calma" } } };
            string mrel = LogicaRelleno.Mensaje(hm, cr, ms, LogicaRelleno.YaSuena(pj));
            int lr = LogicaRelleno.Leer("{\"huecos\": [{\"n\": 1, \"silencio\": true}, {\"n\": 2, \"id\": 0, \"motivo\": \"exploración\"}]}", hm, cr, ms);
            List<string> av = new List<string>();
            int col = LogicaRelleno.Colocar(pj, hm, cr, ms, null, av);
            Verificar(hm.Count == 2 && hm[0].Inicio == 10 && hm[0].Fin == 20 && hm[1].Inicio == 50 && hm[1].Fin == 100 && hm[1].Bloque == "ACTO A" &&
                      hm[0].Antes == "" && hm[0].Despues == "Ya suena" && mrel.Contains("YA SUENA EN EL CAPÍTULO: Ya suena") && lr == 2 &&
                      hm[0].Silencio && col == 1 && mt.Events.Count == 2 && S(mt.Events[0].Start) == 20 && S(mt.Events[0].Length) == 30 &&
                      S(mt.Events[1].Start) == 50 && av.Count == 0,
                      "Rellenar música: encuentra los huecos (sin el kit), la IA elige tema o silencio y se coloca sin tocar lo que ya estaba");

            Project pl = new Project();
            VideoTrack vl = new VideoTrack(0, "Video"); pl.Tracks.Add(vl);
            vl.AddVideoEvent(Timecode.FromMilliseconds(0), Timecode.FromMilliseconds(300000));
            pl.Regions.Add(new Region(Timecode.FromMilliseconds(110000), Timecode.FromMilliseconds(50000), "ACTO B"));
            List<HuecoMusica> hl = LogicaRelleno.Huecos(pl, null, 8);
            Verificar(hl.Count == 3 && hl[0].Inicio == 0 && hl[0].Fin == 110 && hl[2].Fin == 300 && hl[1].Duracion <= LogicaRelleno.MaximoHueco,
                      "Rellenar música: un hueco largo se parte en tramos de hasta 2 min (en el borde de un bloque si hay uno cerca)");

            // Video de MomentosIA: la musica esta en una pista con otro nombre, pero sus temas son de la biblioteca.
            Project pmo = new Project();
            VideoTrack vmo = new VideoTrack(0, "Video"); pmo.Tracks.Add(vmo);
            vmo.AddVideoEvent(Timecode.FromMilliseconds(0), Timecode.FromMilliseconds(60000));
            AudioTrack amo = new AudioTrack(1, "Audio 5"); pmo.Tracks.Add(amo);
            amo.AddAudioEvent(Timecode.FromMilliseconds(0), Timecode.FromMilliseconds(40000)).ActiveTake = new Take { Media = new Media(Path.Combine(musDir, "SC", "tema.mp3")) };
            LogicaRelleno.CarpetaMusica = "";
            int sinBib = LogicaRelleno.Huecos(pmo, null, 8).Count;
            LogicaRelleno.CarpetaMusica = musDir;
            List<HuecoMusica> hmo = LogicaRelleno.Huecos(pmo, null, 8);
            LogicaRelleno.CarpetaMusica = "";
            Verificar(sinBib == 1 && hmo.Count == 1 && hmo[0].Inicio == 40,
                      "Rellenar música en un video de MomentosIA: la pista con temas de la biblioteca cuenta como música aunque se llame «Audio 5»");

            // ---- etiquetar audios que no son del anime
            BibliotecaMusica be = new BibliotecaMusica { Carpeta = dir };
            be.Archivos.Add(new ArchivoMusica { Ruta = "Juegos/Minecraft/Sweden.mp3", Titulo = "Sweden", Album = "Minecraft - Volume Alpha" });
            be.Archivos.Add(new ArchivoMusica { Ruta = "Fanmade/Pelea/Remix.mp3", Titulo = "Remix" });
            int et = BibliotecaMusica.AplicarEtiquetas(be.PorEtiquetar(), "{\"temas\": [{\"id\": 0, \"animos\": [\"Calma\", \"inventado\"], \"momento\": \"exploración\", \"descripcion\": \"piano lento\"}]}");
            BibliotecaMusica be2 = new BibliotecaMusica { Carpeta = dir };
            be2.Archivos.Add(new ArchivoMusica { Ruta = "Juegos/Minecraft/Sweden.mp3", Titulo = "Sweden" });
            be2.ConservarEtiquetas(be);
            Verificar(et == 1 && be.Archivos[0].Sirve && be.Archivos[0].Animos.Count == 1 && be.Archivos[0].Animos[0] == "calma" &&
                      be2.Archivos[0].Etiquetado && be2.Archivos[0].Descripcion == "piano lento" &&
                      BibliotecaMusica.AnimosDeCarpeta(dir, "Fanmade/Pelea/Remix.mp3").Contains("pelea") &&
                      BibliotecaMusica.AnimosDeCarpeta(dir, "Audios/Studio/x.mp3").Count == 0 &&
                      BibliotecaMusica.InstruccionesEtiquetar().Contains("mejor nada que inventar"),
                      "Biblioteca: juegos y fanmade se etiquetan con IA (o por su subcarpeta) y no se pierde al volver a indexar");
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

            // Narracion: el primer hueco libre cerca de su momento.
            List<Rango> oc = new List<Rango> { new Rango(10, 14), new Rango(14.5, 20), new Rango(23, 30) };
            Verificar(ArmarCapitulo.Hueco(oc, 12, 2, 0, 100) == 20.15 && double.IsNaN(ArmarCapitulo.Hueco(oc, 12, 9, 0, 32)) &&
                      ArmarCapitulo.Hueco(oc, 5, 3, 0, 100) == 5, "Narración: va al hueco libre más cercano; si no cabe, no se pone");

            // Un solo video de doble duración: intro larga, sin opening, eyecatch entre mitades.
            string dd = @"{""resumen"": ""Doble duración"", ""partes"": [{""titulo"": ""El comienzo"", ""etapa"": ""Etapa 1"",
              ""estructura"": [{""bloque"": ""intro"", ""nombre"": ""Intro"", ""tipo"": ""contenido"", ""ritmo"": ""lento""},
                               {""bloque"": ""titulo"", ""tipo"": ""texto"", ""segundos"": 4},
                               {""bloque"": ""acto_a"", ""tipo"": ""contenido"", ""ritmo"": ""medio""},
                               {""bloque"": ""eyecatch"", ""nombre"": ""Eyecatch"", ""tipo"": ""kit"", ""segundos"": 5},
                               {""bloque"": ""acto_b"", ""tipo"": ""contenido"", ""ritmo"": ""rapido""},
                               {""bloque"": ""ed"", ""tipo"": ""kit""}],
              ""bloques"": [{""bloque"": ""intro"", ""clips"": [{""inicio"": 0, ""fin"": 90, ""respiro"": 2}]},
                            {""bloque"": ""acto_a"", ""clips"": [{""inicio"": 100, ""fin"": 200, ""respiro"": 9}]},
                            {""bloque"": ""acto_b"", ""clips"": [{""inicio"": 300, ""fin"": 400}]},
                            {""bloque"": ""final"", ""clips"": [{""inicio"": 500, ""fin"": 520}]}],
              ""narracion"": [{""en"": 1, ""texto"": ""Cada año hago un server con mis amigos.""}]}]}";
            PlanFinal pd = LogicaProduccion.LeerFinal(dd, 635, cand.Count, 195, f.Tv);
            CapituloFinal cd = pd.Partes[0];
            Verificar(pd.Partes.Count == 1 && cd.Bloque("op") == null && cd.Bloque("eyecatch").ClaveKit == "regancho" && cd.Bloque("intro").Ritmo == "lento" &&
                      cd.Bloque("titulo").Segundos == 4 && cd.Estructura.FindIndex(delegate (BloqueTV b) { return b.Clave == "final"; }) ==
                      cd.Estructura.FindIndex(delegate (BloqueTV b) { return b.Clave == "ed"; }) - 1,
                      "Estructura propia: sin opening, eyecatch con el kit del re-gancho, ritmo por bloque (y lo que falta va antes del cierre)");
            Verificar(Math.Abs(LogicaProduccion.Duracion(cd, f.Tv) - (92 + 4 + 103 + 5 + 100 + 15 + 20)) < 0.01, "Duración: con los respiros (máx. 3 s)");
            Vegas v2 = new Vegas();
            v2.Generators.Hijos.Add(new PlugInNode { Name = "VEGAS Títulos y texto", UniqueID = "{Svfx:com.vegascreativesoftware:titlesandtext}" });
            v2.Project = Rearmar(export);
            v2.Project.FilePath = Path.Combine(dir, "S01E02 SCR BASE.veg");
            Transcripcion t2 = Transcripcion.Cargar(tr);
            t2.Ubicador = PistasVegas.Ubicador(v2.Project, t2);
            ResultadoProduccion r2 = ArmarCapitulo.Producir(v2, pd, f, ms, bib, cand, null, new VozFalsa(), 195, t2, delegate (string x, double y) { });
            List<string> regs = new List<string>();
            foreach (Region x in v2.Project.Regions) regs.Add(x.Label);
            Verificar(r2.Partes.Count == 1 && regs.Contains("INTRO") && regs.Contains("EYECATCH") && !regs.Contains("OPENING") && r2.Respiros == 2,
                      "Producir doble duración: un solo video con intro, eyecatch y sin opening; " + r2.Respiros + " respiros");
            Verificar(Math.Abs(r2.Partes[0] - (92 + 4 + 103 + 5 + 100 + 15 + 20)) < 0.5, "Producir: el respiro alarga el clip con la pausa original (" + Formato.Tiempo(r2.Partes[0]) + ")");
            Track n2 = v2.Project.Tracks.Find(delegate (Track x) { return x.Name == RitmoVegas.PistaNarracion; });
            bool pisa = false;
            if (n2 != null)
                foreach (TrackEvent e in n2.Events)
                    foreach (Segmento sg in t2.SegmentosActuales())
                        if (sg.Inicio < 90 && sg.Fin > 0 && S(e.Start) < sg.Fin - 0.05 && S(e.End) > sg.Inicio + 0.05) pisa = true;
            Verificar((n2 != null && n2.Events.Count == 1 && !pisa) || r2.NarracionSinHueco == 1,
                      "Narración: no pisa las voces de los jugadores" + (r2.NarracionSinHueco > 0 ? " (no cabía: quedó en el guion)" : " (en " + Formato.Tiempo(S(n2.Events[0].Start)) + ")"));
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
