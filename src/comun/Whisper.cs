using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// =====================================================================
// Faster-Whisper-XXL en segundo plano: lanza el .exe, sigue su avance
// leyendo las lineas "[00:01.000 --> 00:04.000] texto" y al final deja el
// JSON con los tiempos de cada palabra.
// =====================================================================

public class TareaWhisper
{
    Process proceso;
    readonly object candado = new object();
    readonly StringBuilder registro = new StringBuilder();
    string carpeta, wav;
    double duracion, avance;
    string ultimaLinea = "";

    public bool Terminada { get; private set; }
    public bool Cancelada { get; private set; }
    public int Codigo { get; private set; }

    // Segundos de audio ya procesados (segun lo que imprime Whisper).
    public double Avance { get { lock (candado) return avance; } }
    public double Fraccion { get { return duracion > 0 ? Math.Min(1, Avance / duracion) : 0; } }
    public string UltimaLinea { get { lock (candado) return ultimaLinea; } }
    public string Registro { get { lock (candado) return registro.ToString(); } }

    public static string Argumentos(Configuracion c, string wav, string carpeta)
    {
        string a = "\"" + wav + "\"" +
            " --model " + c.WhisperModelo +
            " --language " + c.Idioma +
            " --output_format json" +
            " --output_dir \"" + carpeta + "\"" +
            " --word_timestamps True" +
            " --vad_filter True" +
            " --device " + c.WhisperDispositivo +
            " --compute_type " + c.WhisperPrecision;
        if (!String.IsNullOrEmpty(c.WhisperExtra)) a += " " + c.WhisperExtra;
        return a;
    }

    public void Iniciar(Configuracion c, string wav, double duracionAudio)
    {
        if (!c.TieneWhisper) throw new Exception("Configura la ruta de Faster-Whisper-XXL en “Configurar vegas-cut”.");
        this.wav = wav;
        duracion = duracionAudio;
        carpeta = Path.Combine(Path.GetTempPath(), "vegas-cut-whisper-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(carpeta);

        ProcessStartInfo info = new ProcessStartInfo(c.WhisperExe, Argumentos(c, wav, carpeta));
        info.UseShellExecute = false;
        info.CreateNoWindow = true;
        info.RedirectStandardOutput = true;
        info.RedirectStandardError = true;
        info.StandardOutputEncoding = Encoding.UTF8;
        info.StandardErrorEncoding = Encoding.UTF8;
        info.WorkingDirectory = Path.GetDirectoryName(c.WhisperExe);

        proceso = new Process();
        proceso.StartInfo = info;
        proceso.EnableRaisingEvents = true;
        proceso.OutputDataReceived += delegate (object s, DataReceivedEventArgs e) { Linea(e.Data); };
        proceso.ErrorDataReceived += delegate (object s, DataReceivedEventArgs e) { Linea(e.Data); };
        proceso.Exited += delegate
        {
            try { proceso.WaitForExit(); Codigo = proceso.ExitCode; } catch { }
            Terminada = true;
        };
        proceso.Start();
        proceso.BeginOutputReadLine();
        proceso.BeginErrorReadLine();
    }

    static readonly Regex Marca = new Regex(@"-->\s*(?:(\d+):)?(\d+):(\d+(?:[.,]\d+)?)\]");

    void Linea(string l)
    {
        if (l == null) return;
        lock (candado)
        {
            registro.AppendLine(l);
            if (registro.Length > 200000) registro.Remove(0, 100000);
            if (l.Trim().Length > 0) ultimaLinea = l.Trim();
            Match m = Marca.Match(l);
            if (m.Success)
            {
                double h = m.Groups[1].Success ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
                double min = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                double s = double.Parse(m.Groups[3].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
                avance = Math.Max(avance, h * 3600 + min * 60 + s);
            }
        }
    }

    public void Cancelar()
    {
        Cancelada = true;
        try { if (proceso != null && !proceso.HasExited) proceso.Kill(); } catch { }
    }

    // JSON resultante; lanza un error con el final del registro si fallo.
    public string Resultado()
    {
        string esperado = Path.Combine(carpeta, Path.GetFileNameWithoutExtension(wav) + ".json");
        string ruta = File.Exists(esperado) ? esperado : null;
        if (ruta == null)
            foreach (string f in Directory.GetFiles(carpeta, "*.json")) { ruta = f; break; }
        if (ruta == null)
        {
            string r = Registro.Trim();
            if (r.Length > 1200) r = "…" + r.Substring(r.Length - 1200);
            throw new Exception("Whisper no generó la transcripción (código " + Codigo + ").\n\n" + r);
        }
        return File.ReadAllText(ruta, Encoding.UTF8);
    }

    public void Limpiar()
    {
        try { if (carpeta != null) Directory.Delete(carpeta, true); } catch { }
    }
}
