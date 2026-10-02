using System;
using System.IO;
using System.Reflection;

// =====================================================================
// Voz provisional del narrador con la sintesis de Windows (System.Speech).
// Se carga por reflexion porque Vegas no la referencia al compilar. Suena
// robotica a proposito: es solo para tener la duracion y el ritmo; despues
// se reemplaza por la grabacion.
// =====================================================================

public interface ISintetizador
{
    string Nombre { get; }
    // Escribe el WAV del texto a esa velocidad (-10 a 10).
    void Decir(string texto, int velocidad, string wav);
}

public class SintetizadorWindows : ISintetizador
{
    readonly object sintesis;
    readonly Type tipo;
    string nombre = "voz de Windows";

    public string Nombre { get { return nombre; } }

    public SintetizadorWindows()
    {
        Assembly a = Assembly.Load("System.Speech, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
        tipo = a.GetType("System.Speech.Synthesis.SpeechSynthesizer", true);
        sintesis = Activator.CreateInstance(tipo);
        ElegirEspanol();
    }

    // La primera voz en espanol instalada (Helena, Sabina, Laura...).
    void ElegirEspanol()
    {
        try
        {
            System.Collections.IEnumerable voces = (System.Collections.IEnumerable)tipo.GetMethod("GetInstalledVoices", Type.EmptyTypes).Invoke(sintesis, null);
            foreach (object v in voces)
            {
                object info = v.GetType().GetProperty("VoiceInfo").GetValue(v, null);
                System.Globalization.CultureInfo c = (System.Globalization.CultureInfo)info.GetType().GetProperty("Culture").GetValue(info, null);
                string n = (string)info.GetType().GetProperty("Name").GetValue(info, null);
                if (c != null && c.TwoLetterISOLanguageName == "es")
                {
                    tipo.GetMethod("SelectVoice", new Type[] { typeof(string) }).Invoke(sintesis, new object[] { n });
                    nombre = n;
                    return;
                }
            }
        }
        catch { }
    }

    public void Decir(string texto, int velocidad, string wav)
    {
        tipo.GetProperty("Rate").SetValue(sintesis, Math.Max(-10, Math.Min(10, velocidad)), null);
        tipo.GetMethod("SetOutputToWaveFile", new Type[] { typeof(string) }).Invoke(sintesis, new object[] { wav });
        try { tipo.GetMethod("Speak", new Type[] { typeof(string) }).Invoke(sintesis, new object[] { texto }); }
        finally { tipo.GetMethod("SetOutputToNull", Type.EmptyTypes).Invoke(sintesis, null); }
    }
}

public static class VozProvisional
{
    // Cada punto de "Rate" de Windows acelera o frena ~11.6 % (+10 = x3).
    static readonly double Paso = Math.Pow(3, 0.1);

    // Duracion de un WAV en segundos (lee la cabecera).
    public static double Duracion(string wav)
    {
        using (FileStream fs = new FileStream(wav, FileMode.Open, FileAccess.Read, FileShare.Read))
        using (BinaryReader br = new BinaryReader(fs))
        {
            if (new string(br.ReadChars(4)) != "RIFF") throw new Exception("El archivo no es WAV.");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new Exception("El archivo no es WAV.");
            int porSegundo = 0;
            long datos = -1;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(br.ReadChars(4));
                long tam = br.ReadUInt32();
                long siguiente = fs.Position + tam + (tam & 1);
                if (id == "fmt ") { br.ReadUInt16(); br.ReadUInt16(); br.ReadInt32(); porSegundo = br.ReadInt32(); }
                else if (id == "data") { datos = Math.Min(tam, fs.Length - fs.Position); break; }
                fs.Position = siguiente;
            }
            if (porSegundo <= 0 || datos < 0) throw new Exception("WAV sin datos.");
            return datos / (double)porSegundo;
        }
    }

    // Velocidad para que el texto dure "objetivo" segundos, sabiendo cuanto
    // duro a la velocidad "probada".
    public static int Velocidad(double duro, int probada, double objetivo)
    {
        if (duro <= 0 || objetivo <= 0) return probada;
        double v = probada + Math.Log(duro / objetivo) / Math.Log(Paso);
        return (int)Math.Max(-10, Math.Min(10, Math.Round(v)));
    }

    // Dice la frase y, si dura mas de un 12 % de lo que tomaria a la
    // velocidad del narrador, la repite con la velocidad corregida.
    // "inicial" es la ultima velocidad que funciono (se ajusta sola).
    public static double Generar(ISintetizador voz, string texto, double objetivo, string wav, ref int inicial)
    {
        voz.Decir(texto, inicial, wav);
        double d = Duracion(wav);
        if (Math.Abs(d - objetivo) / Math.Max(0.5, objetivo) > 0.12)
        {
            int v = Velocidad(d, inicial, objetivo);
            if (v != inicial)
            {
                voz.Decir(texto, v, wav);
                d = Duracion(wav);
                inicial = v;
            }
        }
        return d;
    }
}
