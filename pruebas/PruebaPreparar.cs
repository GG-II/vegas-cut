// Pruebas de PrepararEpisodio: quitar silencios y transcribir de una pasada,
// con la API falsa de Vegas y un Whisper falso (ver pruebas/ejecutar.sh).
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ScriptPortal.Vegas;

class PruebaPreparar
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000); }

    const string WhisperJson = @"{""segments"": [
      {""start"": 1.0, ""end"": 2.5, ""text"": "" Hola a todos."", ""words"": [
        {""start"": 1.0, ""end"": 1.4, ""word"": "" Hola"", ""probability"": 0.98},
        {""start"": 1.4, ""end"": 2.5, ""word"": "" todos."", ""probability"": 0.97}]}]}";

    static int Main()
    {
        string tmp = Path.Combine(Path.GetTempPath(), "preparar-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tmp);

        // Proyecto: video y voz de 18.15 s (la voz de prueba tiene 5 silencios).
        Vegas v = new Vegas();
        Project p = v.Project;
        p.FilePath = Path.Combine(tmp, "S01E01 Prueba.veg");
        p.Length = TC(18.15);
        VideoTrack video = new VideoTrack(0, "Video"); AudioTrack voz = new AudioTrack(1, "Voz");
        p.Tracks.Add(video); p.Tracks.Add(voz);
        Media grabacion = new Media { FilePath = Path.Combine(tmp, "rec.mp4") };
        video.AddVideoEvent(TC(0), TC(18.15)).ActiveTake = new Take { Media = grabacion };
        voz.AddAudioEvent(TC(0), TC(18.15)).ActiveTake = new Take { Media = grabacion };
        v.OnRender = delegate (RenderArgs a) { File.Copy("voz16.wav", a.OutputFile, true); return RenderStatus.Complete; };
        List<InfoPista> pistas = PistasVegas.Listar(p);

        // 1. Silencios con el perfil Gameplay.
        List<string> pasos = new List<string>();
        double quitado;
        Perfil_ perfil = Array.Find(Perfil_.Incluidos, delegate (Perfil_ x) { return x.Nombre == "Gameplay"; });
        int n = PasoSilencios.Ejecutar(v, new List<InfoPista> { pistas[0] }, perfil, delegate (string t, double f) { pasos.Add(t); }, out quitado);
        Verificar(n > 0 && quitado > 1 && voz.Events.Count > 1 && video.Events.Count == voz.Events.Count,
            "Paso 1: quita los silencios de todas las pistas (" + n + ", " + quitado.ToString("0.00") + " s)");
        Verificar(pasos.Count > 0 && pasos[0].StartsWith("Midiendo"), "Paso 1: avisa qué está haciendo");

        // 2. Transcripcion con un Whisper falso.
        string exe = Path.Combine(tmp, "faster-whisper-xxl.exe");
        File.WriteAllText(exe,
            "#!/bin/sh\n" +
            "out=''; prev=''; for a in \"$@\"; do if [ \"$prev\" = \"--output_dir\" ]; then out=\"$a\"; fi; prev=\"$a\"; done\n" +
            "echo '[00:01.000 --> 00:02.500]  Hola a todos.'\n" +
            "cat > \"$out/$(basename \"$1\" .wav).json\" <<'FIN'\n" + WhisperJson + "\nFIN\n");
        System.Diagnostics.Process.Start("chmod", "+x \"" + exe + "\"").WaitForExit();
        Configuracion c = new Configuracion();
        c.WhisperExe = exe;
        string ruta = Transcripcion.RutaPara(p.FilePath);
        ProcesoTranscripcion proc = new ProcesoTranscripcion(v, c, ruta);
        proc.Empezar(pistas, new List<int> { 0 }, new List<int>(), delegate (string t, double f) { });
        DateTime limite = DateTime.Now.AddSeconds(20);
        while (!proc.Terminado && DateTime.Now < limite) { proc.Revisar(); Thread.Sleep(100); }
        Verificar(proc.Terminado && proc.Error == null && File.Exists(ruta), "Paso 2: transcribe y guarda (" + proc.Error + ")");
        Transcripcion t2 = Transcripcion.Cargar(ruta);
        Verificar(t2.Segmentos.Count == 1 && t2.Segmentos[0].Texto.Contains("Hola") && t2.TieneFuentes &&
                  t2.Hablantes[0].Fuentes.Count == voz.Events.Count, "Paso 2: con las fuentes de los clips ya sin silencios");
        Verificar(proc.Texto.Contains("1 frases"), "Paso 2: resumen al terminar (" + proc.Texto + ")");

        // Cancelar a mitad.
        ProcesoTranscripcion otro = new ProcesoTranscripcion(v, c, Path.Combine(tmp, "otra.json"));
        otro.Empezar(pistas, new List<int> { 0 }, new List<int>(), delegate (string t, double f) { });
        otro.Cancelar();
        Verificar(otro.Terminado && otro.Error == "Cancelado." && !File.Exists(Path.Combine(tmp, "otra.json")), "Cancelar no guarda nada");

        // Copia base: sin silencios y transcrita, y se sigue en el original.
        File.WriteAllText(Path.Combine(tmp, "S01E01 Prueba.vegascut-proyecto-serie.json"), "{\"serie\": \"x\"}");
        string original = p.FilePath;
        string copia = CopiaBase.Guardar(v);
        Verificar(copia == Path.Combine(tmp, "S01E01 Prueba BASE.veg") && v.Guardados.Count == 2 && v.Guardados[0] == copia &&
                  p.FilePath == original, "Copia base: se guarda como «… BASE.veg» y se sigue en el original");
        Transcripcion tb = Transcripcion.Cargar(Transcripcion.RutaPara(copia));
        Verificar(tb.Segmentos.Count == 1 && tb.Proyecto == copia && File.Exists(Path.Combine(tmp, "S01E01 Prueba BASE.vegascut-proyecto-serie.json")),
                  "Copia base: con su transcripción y su serie");
        Verificar(CopiaBase.Original(copia) == original && CopiaBase.Guardar(new Vegas { Project = new Project { FilePath = copia } }) == null,
                  "Copia base: sabe cuál es su original y no copia una copia");
        int tt, nn; string kk, kb;
        Serie.Clave("S01E01 Prueba", out tt, out nn, out kk); Serie.Clave("S01E01 Prueba BASE", out tt, out nn, out kb);
        Verificar(kk == kb, "Copia base: cuenta como el mismo capítulo de la serie");

        try { Directory.Delete(tmp, true); } catch { }
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " pruebas fallaron.");
        return fallos == 0 ? 0 : 1;
    }
}
