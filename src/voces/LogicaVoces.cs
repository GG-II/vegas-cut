using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// =====================================================================
// Limpiar voces: quita el ruido (DeepFilterNet) y empareja el volumen
// (ffmpeg) SOLO de lo que quedo en el video. Por cada archivo de voz se
// sacan los pedazos que usan los eventos (con un margen), se limpian y se
// ponen como toma nueva de cada evento, en el mismo lugar: nada se mueve y
// el original queda como toma alternativa (tecla T en Vegas).
// =====================================================================

// Lo que un evento usa de su archivo (segundos del archivo).
public class UsoVoz
{
    public string Archivo = "";
    public int Flujo;          // pista de audio dentro del archivo (OBS graba varias)
    public double A, B;
    public string Pista = "";  // etiqueta de la pista de Vegas ("A2")
}

// Un pedazo continuo de un archivo que se limpia de una vez.
public class TrozoVoz
{
    public string Archivo = "", Pista = "";
    public int Flujo;
    public double A, B;
    public string Entrada = "", Sinruido = "", Nivelado = "", Final = "";
    public double Lufs = double.NaN;   // volumen medido despues de nivelar
    public double Duracion { get { return B - A; } }
}

public class OpcionesVoces
{
    public double LimiteRuido = 24;    // dB que puede bajar el ruido (0 = no quitar; 100 = todo)
    public bool Nivelar = true;        // emparejar frase por frase
    public double Objetivo = -16;      // LUFS de todas las voces
    public double Graves = 80;         // Hz: corta golpes y retumbes
}

public static class LogicaVoces
{
    static string F(double v) { return v.ToString("0.###", CultureInfo.InvariantCulture); }

    // Junta lo que usan los eventos en pedazos por archivo: con un margen a cada
    // lado (para que el filtro tenga contexto) y unidos si quedan cerca.
    public static List<TrozoVoz> Trozos(List<UsoVoz> usos, double margen, double unir)
    {
        Dictionary<string, List<UsoVoz>> porArchivo = new Dictionary<string, List<UsoVoz>>();
        foreach (UsoVoz u in usos)
        {
            if (u.B - u.A < 0.01) continue;
            string k = u.Pista + "|" + u.Archivo.ToLowerInvariant() + "|" + u.Flujo;
            if (!porArchivo.ContainsKey(k)) porArchivo[k] = new List<UsoVoz>();
            porArchivo[k].Add(u);
        }
        List<TrozoVoz> r = new List<TrozoVoz>();
        foreach (List<UsoVoz> l in porArchivo.Values)
        {
            l.Sort(delegate (UsoVoz x, UsoVoz y) { return x.A.CompareTo(y.A); });
            TrozoVoz t = null;
            foreach (UsoVoz u in l)
            {
                double a = Math.Max(0, u.A - margen), b = u.B + margen;
                if (t != null && a <= t.B + unir) { t.B = Math.Max(t.B, b); continue; }
                t = new TrozoVoz { Archivo = u.Archivo, Flujo = u.Flujo, Pista = u.Pista, A = a, B = b };
                r.Add(t);
            }
        }
        return r;
    }

    // El pedazo que contiene lo que usa un evento.
    public static TrozoVoz Buscar(List<TrozoVoz> trozos, UsoVoz u)
    {
        foreach (TrozoVoz t in trozos)
            if (t.Pista == u.Pista && t.Flujo == u.Flujo && String.Equals(t.Archivo, u.Archivo, StringComparison.OrdinalIgnoreCase) &&
                u.A >= t.A - 0.001 && u.B <= t.B + 0.001) return t;
        return null;
    }

    // Nombres de los archivos de cada pedazo, dentro de la carpeta de trabajo.
    public static void Nombrar(List<TrozoVoz> trozos, string carpeta)
    {
        for (int i = 0; i < trozos.Count; i++)
        {
            TrozoVoz t = trozos[i];
            string baseNombre = Limpio(t.Pista + " " + Path.GetFileNameWithoutExtension(t.Archivo)) + " " + (i + 1).ToString("000") +
                                " " + ((int)t.A) + "s";
            t.Entrada = Path.Combine(carpeta, "tmp", baseNombre + ".wav");
            t.Sinruido = Path.Combine(carpeta, "tmp", "df", baseNombre + ".wav");
            t.Nivelado = Path.Combine(carpeta, "tmp", baseNombre + " nivelado.wav");
            t.Final = Path.Combine(carpeta, baseNombre + ".wav");
        }
    }

    static string Limpio(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c.ToString(), "");
        foreach (char c in "<>:\"|?*") s = s.Replace(c.ToString(), "");
        return s.Trim();
    }

    static string Q(string ruta) { return "\"" + ruta + "\""; }

    // 1) Sacar el pedazo: mono, 48 kHz (lo que usa DeepFilterNet).
    public static string ArgsExtraer(TrozoVoz t)
    {
        return "-hide_banner -v error -y -ss " + F(t.A) + " -t " + F(t.Duracion) + " -i " + Q(t.Archivo) +
               " -map 0:a:" + t.Flujo + " -ac 1 -ar 48000 -c:a pcm_s16le " + Q(t.Entrada);
    }

    // 2) Quitar el ruido (varios archivos de una vez; -D: sin retraso).
    public static string ArgsDeepFilter(List<TrozoVoz> trozos, double limite, string salida)
    {
        StringBuilder sb = new StringBuilder("-D -a " + F(limite) + " -o " + Q(salida));
        foreach (TrozoVoz t in trozos) sb.Append(" " + Q(t.Entrada));
        return sb.ToString();
    }

    // 3) Graves fuera y volumen parejo frase por frase; al final mide el volumen.
    public static string ArgsNivelar(string entrada, TrozoVoz t, OpcionesVoces op)
    {
        List<string> f = new List<string>();
        if (op.Graves > 0) f.Add("highpass=f=" + F(op.Graves));
        // Ventanas de ~0.2 s suavizadas en ~6 s: sube lo bajo y baja lo alto sin
        // aplastar gritos ni susurros; como mucho x8 (no sube el ruido de fondo).
        if (op.Nivelar) f.Add("dynaudnorm=f=200:g=31:p=0.9:m=8");
        f.Add("ebur128=framelog=quiet");
        return "-hide_banner -nostats -y -i " + Q(entrada) + " -af " + String.Join(",", f.ToArray()) +
               " -ar 48000 -c:a pcm_s16le " + Q(t.Nivelado);
    }

    // 4) La misma ganancia a todos los pedazos de la pista (para llegar al
    // objetivo), un tope para los picos y exactamente la duracion original.
    public static string ArgsFinal(TrozoVoz t, double ganancia)
    {
        return "-hide_banner -v error -y -i " + Q(t.Nivelado) + " -af volume=" + F(ganancia) + "dB,alimiter=limit=0.84:level=false,apad" +
               " -t " + F(t.Duracion) + " -ar 48000 -c:a pcm_s16le " + Q(t.Final);
    }

    // El "I: -23.4 LUFS" del resumen de ebur128.
    public static double LeerLufs(string salida)
    {
        MatchCollection ms = Regex.Matches(salida ?? "", @"I:\s*(-?\d+(?:\.\d+)?)\s*LUFS");
        if (ms.Count == 0) return double.NaN;
        double v = double.Parse(ms[ms.Count - 1].Groups[1].Value, CultureInfo.InvariantCulture);
        return v <= -69 ? double.NaN : v;
    }

    // Volumen de toda la pista: promedio de energia pesado por duracion.
    public static double LufsPista(List<TrozoVoz> trozos)
    {
        double e = 0, d = 0;
        foreach (TrozoVoz t in trozos)
        {
            if (double.IsNaN(t.Lufs)) continue;
            e += t.Duracion * Math.Pow(10, t.Lufs / 10);
            d += t.Duracion;
        }
        return d <= 0 ? double.NaN : 10 * Math.Log10(e / d);
    }

    // Cuanto subir o bajar para llegar al objetivo (con limites razonables).
    public static double Ganancia(double lufs, double objetivo)
    {
        if (double.IsNaN(lufs)) return 0;
        return Math.Max(-20, Math.Min(30, objetivo - lufs));
    }

    // ------------------------------------------------- programas externos

    // ffmpeg: el configurado, el que viene con Whisper o el del PATH.
    public static string BuscarFfmpeg(string configurado, string whisperExe)
    {
        List<string> c = new List<string>();
        if (!String.IsNullOrEmpty(configurado)) c.Add(configurado);
        if (!String.IsNullOrEmpty(whisperExe))
        {
            string d = Path.GetDirectoryName(whisperExe) ?? "";
            c.Add(Path.Combine(d, "ffmpeg.exe"));
            c.Add(Path.Combine(Path.Combine(d, "_xxl_data"), "ffmpeg.exe"));
        }
        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (dir.Trim().Length == 0) continue;
            try { c.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg.exe")); c.Add(Path.Combine(dir.Trim().Trim('"'), "ffmpeg")); } catch { }
        }
        foreach (string f in c) if (File.Exists(f)) return f;
        return "";
    }

    // Ejecuta un programa sin ventana; devuelve lo que escribio (salida y errores).
    public static string Ejecutar(string exe, string args, out int codigo, Func<bool> cancelar)
    {
        ProcessStartInfo info = new ProcessStartInfo(exe, args);
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardError = true;
        info.RedirectStandardOutput = true;
        StringBuilder sb = new StringBuilder();
        using (Process p = new Process())
        {
            p.StartInfo = info;
            p.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { if (e.Data != null) lock (sb) sb.AppendLine(e.Data); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            while (!p.WaitForExit(200))
                if (cancelar != null && cancelar()) { try { p.Kill(); } catch { } p.WaitForExit(); codigo = -1; return "cancelado"; }
            p.WaitForExit();
            codigo = p.ExitCode;
        }
        lock (sb) return sb.ToString();
    }
}
