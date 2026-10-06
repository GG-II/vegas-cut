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
    public double Objetivo = -20;      // LUFS de cada voz (con juego y musica encima, mas bajo que -16)
    public double Pico = -6;           // dBFS: ningun pico pasa de aqui
    public double Graves = 80;         // Hz: corta golpes y retumbes
    public bool PorClip;               // false: un solo archivo limpio por pista; true: toma nueva en cada clip
}

// Un clip dentro de la pista limpia: de que archivo limpio sale y donde va.
public class PiezaPista
{
    public string Archivo = "";
    public double Desde;       // segundo dentro del archivo limpio
    public double En, Largo;   // donde empieza en la pista (desde su inicio) y cuanto dura
    public double FundidoEntrada, FundidoSalida;
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
        // aplastar gritos ni susurros; como mucho x5 (no sube el ruido de fondo).
        if (op.Nivelar) f.Add("dynaudnorm=f=200:g=31:p=0.5:m=5");
        f.Add("ebur128=framelog=quiet");
        return "-hide_banner -nostats -y -i " + Q(entrada) + " -af " + String.Join(",", f.ToArray()) +
               " -ar 48000 -c:a pcm_s16le " + Q(t.Nivelado);
    }

    // 4) La misma ganancia a todos los pedazos de la pista (para llegar al
    // objetivo), un tope para los picos y exactamente la duracion original.
    public static string ArgsFinal(TrozoVoz t, double ganancia, double pico)
    {
        double limite = Math.Max(0.0625, Math.Min(1, Math.Pow(10, pico / 20)));
        return "-hide_banner -v error -y -i " + Q(t.Nivelado) + " -af volume=" + F(ganancia) + "dB,alimiter=limit=" + F(limite) + ":level=false,apad" +
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
        return Math.Max(-20, Math.Min(20, objetivo - lufs));
    }

    // ------------------------------------------------- una sola pista

    const int Muestras = 48000;

    // Donde empiezan los datos de un WAV PCM de 16 bits (salta los otros bloques).
    static long DatosWav(FileStream f, out long bytes)
    {
        BinaryReader r = new BinaryReader(f);
        f.Position = 12;
        while (f.Position + 8 <= f.Length)
        {
            string id = new string(r.ReadChars(4));
            uint largo = r.ReadUInt32();
            if (id == "data") { bytes = Math.Min(largo, f.Length - f.Position); return f.Position; }
            f.Position += largo + (largo % 2);
        }
        throw new Exception("WAV sin datos: " + f.Name);
    }

    // Arma el archivo de la pista: cada clip en su lugar (mono, 48 kHz), con sus
    // fundidos; lo que se encima se suma. "largo" en segundos.
    public static void ArmarPista(string salida, List<PiezaPista> piezas, double largo)
    {
        long total = (long)Math.Ceiling(largo * Muestras);
        Dictionary<string, FileStream> abiertos = new Dictionary<string, FileStream>();
        Dictionary<string, long> inicioDatos = new Dictionary<string, long>(), finDatos = new Dictionary<string, long>();
        try
        {
            using (FileStream o = new FileStream(salida, FileMode.Create, FileAccess.Write))
            using (BinaryWriter w = new BinaryWriter(o))
            {
                w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write((uint)(36 + total * 2));
                w.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16u);
                w.Write((ushort)1); w.Write((ushort)1); w.Write((uint)Muestras); w.Write((uint)(Muestras * 2));
                w.Write((ushort)2); w.Write((ushort)16);
                w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write((uint)(total * 2));
                const int Bloque = Muestras * 10;
                float[] mezcla = new float[Bloque];
                byte[] lectura = new byte[Bloque * 2];
                for (long b0 = 0; b0 < total; b0 += Bloque)
                {
                    int n = (int)Math.Min(Bloque, total - b0);
                    Array.Clear(mezcla, 0, n);
                    foreach (PiezaPista p in piezas)
                    {
                        long ini = (long)Math.Round(p.En * Muestras), len = (long)Math.Round(p.Largo * Muestras);
                        long a = Math.Max(ini, b0), z = Math.Min(ini + len, b0 + n);
                        if (z <= a) continue;
                        FileStream f;
                        if (!abiertos.TryGetValue(p.Archivo, out f))
                        {
                            f = new FileStream(p.Archivo, FileMode.Open, FileAccess.Read, FileShare.Read);
                            abiertos[p.Archivo] = f;
                            long bytes;
                            inicioDatos[p.Archivo] = DatosWav(f, out bytes);
                            finDatos[p.Archivo] = inicioDatos[p.Archivo] + bytes;
                        }
                        long desde = (long)Math.Round(p.Desde * Muestras) + (a - ini);
                        long pos = inicioDatos[p.Archivo] + desde * 2;
                        int cuantas = (int)Math.Max(0, Math.Min(z - a, (finDatos[p.Archivo] - pos) / 2));
                        if (cuantas <= 0 || pos < inicioDatos[p.Archivo]) continue;
                        f.Position = pos;
                        int leidos = 0;
                        while (leidos < cuantas * 2) { int k = f.Read(lectura, leidos, cuantas * 2 - leidos); if (k <= 0) break; leidos += k; }
                        double fe = p.FundidoEntrada * Muestras, fs = p.FundidoSalida * Muestras;
                        for (int i = 0; i < leidos / 2; i++)
                        {
                            long enPieza = a - ini + i;
                            double g = 1;
                            if (fe > 1 && enPieza < fe) g = enPieza / fe;
                            if (fs > 1 && len - enPieza < fs) g = Math.Min(g, (len - enPieza) / fs);
                            mezcla[a - b0 + i] += (float)(g * (short)(lectura[2 * i] | (lectura[2 * i + 1] << 8)));
                        }
                    }
                    byte[] sal = new byte[n * 2];
                    for (int i = 0; i < n; i++)
                    {
                        int v = (int)Math.Round(Math.Max(-32768, Math.Min(32767, mezcla[i])));
                        sal[2 * i] = (byte)(v & 0xFF); sal[2 * i + 1] = (byte)((v >> 8) & 0xFF);
                    }
                    w.Write(sal);
                }
            }
        }
        finally { foreach (FileStream f in abiertos.Values) f.Dispose(); }
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
