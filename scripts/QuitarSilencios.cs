// QuitarSilencios.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Detecta los silencios de una o varias pistas de voz (hay voz si suena
// cualquiera) y los elimina, silencia o marca en la linea de tiempo.
//
// Como funciona:
//   1. Vegas renderiza a un WAV temporal cada pista elegida (las demas se
//      silencian durante el render y se restauran despues). Asi se analiza el
//      audio tal como suena, con sus efectos, sin depender de ffmpeg.
//   2. Se mide el volumen cada 10 ms y se buscan los tramos bajo el umbral.
//   3. La ventana muestra la forma de onda con los silencios y permite ajustar
//      umbral, duraciones y margenes viendo el resultado al instante.
//   4. Al aplicar, todo queda en un solo paso de deshacer (Ctrl+Z).
//
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
// Los textos con acentos usan escapes \u para no depender de la codificacion.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

public class EntryPoint
{
    Vegas vegas;
    List<AudioTrack> pistas = new List<AudioTrack>();

    public void FromVegas(Vegas v)
    {
        vegas = v;
        Project proyecto = vegas.Project;

        List<string> nombres = new List<string>();
        List<string> completos = new List<string>();
        int sugerida = 0, mejor = -1;
        foreach (Track t in proyecto.Tracks)
        {
            AudioTrack a = t as AudioTrack;
            if (a == null) continue;
            string archivo;
            int eventos, flujo;
            ArchivoPrincipal(a, out archivo, out eventos, out flujo);
            string detalle = !String.IsNullOrEmpty(a.Name) ? a.Name : archivo ?? "vac\u00eda";
            string corto = detalle.Length > 22 ? detalle.Substring(0, 21) + "\u2026" : detalle;
            if (String.IsNullOrEmpty(a.Name) && flujo > 0) corto += " (audio " + (flujo + 1) + ")";
            nombres.Add("A" + (a.Index + 1) + " \u00b7 " + corto);
            completos.Add("Pista " + (a.Index + 1) + ": " + detalle +
                (flujo > 0 ? " (audio " + (flujo + 1) + ")" : "") + " \u00b7 " + eventos + " eventos");

            // Sugerir la pista de voz: la que tenga un archivo "mejorada" o, si
            // no hay, la que tenga mas eventos.
            int puntos = eventos + (archivo != null && archivo.ToLowerInvariant().Contains("mejorada") ? 100000 : 0);
            if (puntos > mejor) { mejor = puntos; sugerida = pistas.Count; }

            pistas.Add(a);
        }

        if (pistas.Count == 0)
        {
            MessageBox.Show("El proyecto no tiene pistas de audio.", "Quitar silencios");
            return;
        }

        bool haySeleccion = vegas.Transport.SelectionLength.ToMilliseconds() > 1;

        using (VentanaSilencios ventana = new VentanaSilencios(nombres.ToArray(), completos.ToArray(),
                   sugerida, haySeleccion, Analizar))
        {
            if (ventana.ShowDialog() != DialogResult.OK || ventana.Rangos.Count == 0) return;

            Dictionary<int, bool> analizadas = new Dictionary<int, bool>();
            foreach (int i in ventana.PistasElegidas) analizadas[pistas[i].Index] = true;
            List<Track> destino = new List<Track>();
            foreach (Track t in proyecto.Tracks)
                if (ventana.Ajustes.TodasLasPistas || analizadas.ContainsKey(t.Index)) destino.Add(t);

            double fps = proyecto.Video.FrameRate;
            List<Rango> rangos = Editor.AjustarAFotogramas(ventana.Rangos, fps);
            int n = rangos.Count;
            double total = 0;
            foreach (Rango r in rangos) total += r.Fin - r.Inicio;

            Modo modo = ventana.Ajustes.Modo;
            using (UndoBlock deshacer = new UndoBlock("Quitar silencios"))
            {
                if (modo == Modo.Eliminar)
                    Editor.Eliminar(proyecto, destino, rangos, true, ventana.Ajustes.TodasLasPistas);
                else if (modo == Modo.DejarHuecos)
                    Editor.Eliminar(proyecto, destino, rangos, false, false);
                else if (modo == Modo.Silenciar)
                    Editor.Silenciar(destino, rangos);
                else
                    Editor.Marcar(proyecto, rangos);
            }

            string hecho = modo == Modo.Eliminar ? "eliminados" :
                           modo == Modo.DejarHuecos ? "quitados dejando huecos" :
                           modo == Modo.Silenciar ? "silenciados" : "marcados como regiones";
            MessageBox.Show(
                n + " silencios " + hecho + " (" + Formato.Tiempo(total) + ").\n\n" +
                "Si no te convence, Ctrl+Z lo deshace todo de una vez.",
                "Quitar silencios");
        }
    }

    static void ArchivoPrincipal(Track t, out string archivo, out int eventos, out int flujo)
    {
        Dictionary<string, int> cuenta = new Dictionary<string, int>();
        archivo = null;
        flujo = 0;
        eventos = t.Events.Count;
        int max = 0;
        foreach (TrackEvent e in t.Events)
        {
            if (e.ActiveTake == null || e.ActiveTake.Media == null) continue;
            string f = Path.GetFileName(e.ActiveTake.Media.FilePath ?? "");
            int c;
            cuenta.TryGetValue(f, out c);
            cuenta[f] = ++c;
            if (c > max)
            {
                max = c;
                archivo = f;
                flujo = IndiceFlujo(e.ActiveTake);
            }
        }
    }

    // Indice del flujo de audio que usa la toma (OBS graba varias pistas de
    // audio en el mismo .mp4). Por reflexion para no depender de la API exacta.
    static int IndiceFlujo(Take toma)
    {
        try
        {
            object flujo = toma.GetType().GetProperty("MediaStream").GetValue(toma, null);
            object indice = flujo.GetType().GetProperty("Index").GetValue(flujo, null);
            return Convert.ToInt32(indice);
        }
        catch { return 0; }
    }

    // Renderiza solo la pista elegida a un WAV temporal y mide sus niveles.
    Analisis Analizar(int indicePista, bool usarSeleccion)
    {
        Project proyecto = vegas.Project;
        AudioTrack pista = pistas[indicePista];

        Timecode inicio, duracion;
        if (usarSeleccion)
        {
            inicio = vegas.Transport.SelectionStart;
            duracion = vegas.Transport.SelectionLength;
            if (duracion.ToMilliseconds() < 0)
            {
                inicio = inicio + duracion;
                duracion = Timecode.FromMilliseconds(-duracion.ToMilliseconds());
            }
        }
        else
        {
            inicio = Timecode.FromMilliseconds(0);
            duracion = proyecto.Length;
        }
        if (duracion.ToMilliseconds() < 100)
            throw new Exception("El rango a analizar est\u00e1 vac\u00edo.");

        RenderTemplate plantilla = PlantillaWav();
        string wav = Path.Combine(Path.GetTempPath(), "vegas-cut-silencios-" + Guid.NewGuid().ToString("N") + ".wav");

        // Las pistas se identifican por indice: Vegas puede devolver objetos
        // distintos para la misma pista.
        Dictionary<int, bool> muteAntes = new Dictionary<int, bool>();
        try
        {
            foreach (Track t in proyecto.Tracks)
            {
                if (!t.IsAudio()) continue;
                muteAntes[t.Index] = t.Mute;
                t.Mute = t.Index != pista.Index;
            }

            RenderArgs args = new RenderArgs();
            args.OutputFile = wav;
            args.RenderTemplate = plantilla;
            args.Start = inicio;
            args.Length = duracion;
            RenderStatus estado = vegas.Render(args);
            if (estado != RenderStatus.Complete)
                throw new Exception("El render del audio no termin\u00f3 (" + estado + ").");
        }
        finally
        {
            foreach (Track t in proyecto.Tracks)
                if (muteAntes.ContainsKey(t.Index)) t.Mute = muteAntes[t.Index];
        }

        try
        {
            Analisis a = WavNiveles.Leer(wav, Analisis.Paso);
            a.Inicio = inicio.ToMilliseconds() / 1000.0;
            return a;
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
    }

    RenderTemplate PlantillaWav()
    {
        RenderTemplate primera = null;
        foreach (Renderer r in vegas.Renderers)
        {
            string ext = (r.FileExtension ?? "").ToLowerInvariant();
            if (!ext.EndsWith(".wav")) continue;
            foreach (RenderTemplate t in r.Templates)
            {
                if (!t.IsValid()) continue;
                if (primera == null) primera = t;
                string n = t.Name ?? "";
                if (n.Contains("PCM") && n.Contains("16")) return t;
            }
        }
        if (primera == null)
            throw new Exception("No se encontr\u00f3 la plantilla de render WAV en Vegas.");
        return primera;
    }
}

// =====================================================================
// Edicion en la linea de tiempo
// =====================================================================

static class Editor
{
    const double Tolerancia = 0.0005; // segundos

    public static List<Rango> AjustarAFotogramas(List<Rango> rangos, double fps)
    {
        List<Rango> r = new List<Rango>();
        foreach (Rango x in rangos)
        {
            double a = Math.Round(x.Inicio * fps) / fps;
            double b = Math.Round(x.Fin * fps) / fps;
            if (b - a >= 1.0 / fps) r.Add(new Rango(a, b));
        }
        return r;
    }

    static Timecode TC(double s) { return Timecode.FromMilliseconds(s * 1000.0); }
    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Corta cada evento en los bordes de los rangos. Devuelve los eventos
    // resultantes de la pista.
    static List<TrackEvent> CortarEnBordes(Track pista, List<Rango> rangos)
    {
        List<double> bordes = new List<double>();
        foreach (Rango r in rangos) { bordes.Add(r.Inicio); bordes.Add(r.Fin); }
        bordes.Sort();

        List<TrackEvent> originales = new List<TrackEvent>();
        foreach (TrackEvent e in pista.Events) originales.Add(e);

        foreach (TrackEvent e in originales)
        {
            double ini = S(e.Start), fin = S(e.End);
            // De atras hacia adelante: el evento original conserva la parte izquierda.
            for (int i = bordes.Count - 1; i >= 0; i--)
            {
                double b = bordes[i];
                if (b > ini + Tolerancia && b < fin - Tolerancia)
                    e.Split(TC(b - ini));
            }
        }

        List<TrackEvent> todos = new List<TrackEvent>();
        foreach (TrackEvent e in pista.Events) todos.Add(e);
        return todos;
    }

    static bool DentroDeRango(TrackEvent e, List<Rango> rangos)
    {
        double medio = (S(e.Start) + S(e.End)) / 2;
        foreach (Rango r in rangos)
            if (medio > r.Inicio && medio < r.Fin) return true;
        return false;
    }

    static double QuitadoAntesDe(double t, List<Rango> rangos)
    {
        double q = 0;
        foreach (Rango r in rangos)
            if (r.Fin <= t + Tolerancia) q += r.Fin - r.Inicio;
        return q;
    }

    // Nueva posicion de un instante despues de quitar los rangos. Si cae
    // dentro de un rango, queda en el punto del corte.
    static double PosicionTrasQuitar(double t, List<Rango> rangos)
    {
        double q = QuitadoAntesDe(t, rangos);
        foreach (Rango r in rangos)
            if (t > r.Inicio && t < r.Fin - Tolerancia) q += t - r.Inicio;
        return t - q;
    }

    // Quita los tramos de los rangos. Con "juntar" mueve lo que sigue para
    // cerrar el hueco; sin el, deja el espacio vacio.
    public static void Eliminar(Project proyecto, List<Track> pistas, List<Rango> rangos, bool juntar, bool moverMarcadores)
    {
        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = CortarEnBordes(pista, rangos);
            foreach (TrackEvent e in eventos)
                if (DentroDeRango(e, rangos)) pista.Events.Remove(e);
            if (!juntar) continue;

            List<TrackEvent> restantes = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) restantes.Add(e);
            restantes.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });

            // De izquierda a derecha: cada evento se mueve a un espacio ya libre.
            foreach (TrackEvent e in restantes)
            {
                double q = QuitadoAntesDe(S(e.Start), rangos);
                if (q > 0) e.Start = TC(S(e.Start) - q);
            }
        }

        if (!moverMarcadores) return;
        List<Marker> marcadores = new List<Marker>();
        foreach (Marker m in proyecto.Markers) marcadores.Add(m);
        foreach (Region m in proyecto.Regions) marcadores.Add(m);
        foreach (Marker m in marcadores)
        {
            double t = S(m.Position), nuevo = PosicionTrasQuitar(t, rangos);
            if (nuevo < t - Tolerancia)
            {
                try { m.Position = TC(nuevo); } catch { }
            }
        }
    }

    public static void Silenciar(List<Track> pistas, List<Rango> rangos)
    {
        foreach (Track pista in pistas)
        {
            if (!pista.IsAudio()) continue;
            foreach (TrackEvent e in CortarEnBordes(pista, rangos))
                if (DentroDeRango(e, rangos)) e.Mute = true;
        }
    }

    public static void Marcar(Project proyecto, List<Rango> rangos)
    {
        foreach (Rango r in rangos)
            proyecto.Regions.Add(new Region(TC(r.Inicio), TC(r.Fin - r.Inicio), "Silencio"));
    }
}

// =====================================================================
// Analisis de audio y deteccion (sin dependencias de Vegas)
// =====================================================================

public struct Rango
{
    public double Inicio, Fin; // segundos en la linea de tiempo
    public Rango(double inicio, double fin) { Inicio = inicio; Fin = fin; }
}

public class Analisis
{
    public const double Paso = 0.01;  // 10 ms por medicion
    public float[] Db;                // nivel RMS de cada paso, en dBFS
    public double Inicio;             // segundo de la linea de tiempo del primer paso
    public double Duracion { get { return Db.Length * Paso; } }

    // Une varias pistas: en cada instante cuenta la que suene mas fuerte, asi
    // hay voz si habla cualquiera de ellas.
    public static Analisis Combinar(List<Analisis> pistas)
    {
        if (pistas.Count == 1) return pistas[0];
        int n = int.MaxValue;
        foreach (Analisis a in pistas) n = Math.Min(n, a.Db.Length);
        Analisis r = new Analisis();
        r.Inicio = pistas[0].Inicio;
        r.Db = new float[n];
        for (int i = 0; i < n; i++)
        {
            float m = -100;
            foreach (Analisis a in pistas) if (a.Db[i] > m) m = a.Db[i];
            r.Db[i] = m;
        }
        return r;
    }
}

public static class WavNiveles
{
    public static Analisis Leer(string ruta, double paso)
    {
        using (FileStream fs = new FileStream(ruta, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
        using (BinaryReader br = new BinaryReader(fs))
        {
            if (new string(br.ReadChars(4)) != "RIFF") throw new Exception("El archivo no es WAV.");
            br.ReadUInt32();
            if (new string(br.ReadChars(4)) != "WAVE") throw new Exception("El archivo no es WAV.");

            int formato = 0, canales = 0, frecuencia = 0, bits = 0;
            long datos = -1, largo = 0;
            while (fs.Position + 8 <= fs.Length)
            {
                string id = new string(br.ReadChars(4));
                long tam = br.ReadUInt32();
                long siguiente = fs.Position + tam + (tam & 1);
                if (id == "fmt ")
                {
                    formato = br.ReadUInt16();
                    canales = br.ReadUInt16();
                    frecuencia = br.ReadInt32();
                    br.ReadInt32(); br.ReadUInt16();
                    bits = br.ReadUInt16();
                    if (formato == 0xFFFE && tam >= 40)
                    {
                        br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32();
                        formato = br.ReadUInt16(); // subformato: 1 PCM, 3 float
                    }
                }
                else if (id == "data")
                {
                    datos = fs.Position;
                    largo = Math.Min(tam, fs.Length - datos);
                    break;
                }
                fs.Position = siguiente;
            }
            if (datos < 0 || canales == 0) throw new Exception("WAV sin datos de audio.");
            if (!(formato == 1 && (bits == 16 || bits == 24 || bits == 32)) && !(formato == 3 && bits == 32))
                throw new Exception("Formato WAV no soportado (" + formato + ", " + bits + " bits).");

            int bytesMuestra = bits / 8;
            int bytesCuadro = bytesMuestra * canales;
            int cuadrosPorPaso = Math.Max(1, (int)Math.Round(frecuencia * paso));
            long cuadros = largo / bytesCuadro;
            int pasos = (int)(cuadros / cuadrosPorPaso);
            float[] db = new float[pasos];

            byte[] buf = new byte[cuadrosPorPaso * bytesCuadro];
            fs.Position = datos;
            for (int p = 0; p < pasos; p++)
            {
                int leidos = 0;
                while (leidos < buf.Length)
                {
                    int n = fs.Read(buf, leidos, buf.Length - leidos);
                    if (n <= 0) break;
                    leidos += n;
                }
                double suma = 0;
                int muestras = leidos / bytesMuestra;
                for (int i = 0; i < muestras; i++)
                {
                    int o = i * bytesMuestra;
                    double x;
                    if (formato == 3) x = BitConverter.ToSingle(buf, o);
                    else if (bits == 16) x = BitConverter.ToInt16(buf, o) / 32768.0;
                    else if (bits == 24) x = ((buf[o] | (buf[o + 1] << 8) | ((sbyte)buf[o + 2] << 16))) / 8388608.0;
                    else x = BitConverter.ToInt32(buf, o) / 2147483648.0;
                    suma += x * x;
                }
                double rms = muestras > 0 ? Math.Sqrt(suma / muestras) : 0;
                db[p] = rms > 1e-5 ? (float)(20 * Math.Log10(rms)) : -100f;
            }

            Analisis a = new Analisis();
            a.Db = db;
            return a;
        }
    }
}

public enum Modo { Eliminar, DejarHuecos, Silenciar, Marcar }

public class Ajustes
{
    public double UmbralDb = -40;
    public int SilencioMinMs = 500;
    public int HablaMinMs = 150;
    public int MargenAntesMs = 150;
    public int MargenDespuesMs = 250;
    public Modo Modo = Modo.Eliminar;
    public bool TodasLasPistas = true;
    public bool UmbralAutomatico = true;

    static string Ruta
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "vegas-cut", "silencios.ini");
        }
    }

    public static Ajustes Cargar()
    {
        Ajustes a = new Ajustes();
        try
        {
            if (!File.Exists(Ruta)) return a;
            foreach (string linea in File.ReadAllLines(Ruta))
            {
                int i = linea.IndexOf('=');
                if (i < 0) continue;
                string k = linea.Substring(0, i).Trim(), v = linea.Substring(i + 1).Trim();
                CultureInfo c = CultureInfo.InvariantCulture;
                switch (k)
                {
                    case "umbral": a.UmbralDb = double.Parse(v, c); break;
                    case "silencioMin": a.SilencioMinMs = int.Parse(v, c); break;
                    case "hablaMin": a.HablaMinMs = int.Parse(v, c); break;
                    case "margenAntes": a.MargenAntesMs = int.Parse(v, c); break;
                    case "margenDespues": a.MargenDespuesMs = int.Parse(v, c); break;
                    case "modo": a.Modo = (Modo)Enum.Parse(typeof(Modo), v); break;
                    case "todas": a.TodasLasPistas = v == "1"; break;
                    case "auto": a.UmbralAutomatico = v == "1"; break;
                }
            }
        }
        catch { }
        return a;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
            CultureInfo c = CultureInfo.InvariantCulture;
            File.WriteAllText(Ruta,
                "umbral=" + UmbralDb.ToString(c) + "\n" +
                "silencioMin=" + SilencioMinMs + "\n" +
                "hablaMin=" + HablaMinMs + "\n" +
                "margenAntes=" + MargenAntesMs + "\n" +
                "margenDespues=" + MargenDespuesMs + "\n" +
                "modo=" + Modo + "\n" +
                "todas=" + (TodasLasPistas ? "1" : "0") + "\n" +
                "auto=" + (UmbralAutomatico ? "1" : "0") + "\n");
        }
        catch { }
    }
}

public class Preset
{
    public string Nombre;
    public int SilencioMin, HablaMin, Antes, Despues;
    public Preset(string n, int s, int h, int a, int d) { Nombre = n; SilencioMin = s; HablaMin = h; Antes = a; Despues = d; }

    public static readonly Preset[] Todos = new Preset[]
    {
        new Preset("Calmado",  900, 200, 250, 350),
        new Preset("Medido",   500, 150, 150, 250),
        new Preset("Din\u00e1mico", 350, 120, 100, 150),
        new Preset("En\u00e9rgico", 250, 100,  60, 100),
        new Preset("Agresivo", 150,  80,  30,  60),
    };
}

public static class Detector
{
    // Umbral sugerido: entre el ruido de fondo y el nivel de voz.
    public static double UmbralAutomatico(float[] db)
    {
        List<float> v = new List<float>();
        foreach (float x in db) if (x > -95) v.Add(x);
        if (v.Count < 10) return -40;
        v.Sort();
        double ruido = v[(int)(v.Count * 0.10)];
        double voz = v[(int)(v.Count * 0.90)];
        double u = ruido + (voz - ruido) * 0.35;
        return Math.Max(-70, Math.Min(-15, Math.Round(u)));
    }

    public static List<Rango> Detectar(Analisis a, Ajustes aj)
    {
        float[] db = a.Db;
        int n = db.Length;
        double paso = Analisis.Paso;
        List<Rango> resultado = new List<Rango>();
        if (n == 0) return resultado;

        // 1. Tramos de voz (sobre el umbral).
        List<int[]> voz = new List<int[]>();
        int i = 0;
        while (i < n)
        {
            if (db[i] >= aj.UmbralDb)
            {
                int j = i;
                while (j < n && db[j] >= aj.UmbralDb) j++;
                voz.Add(new int[] { i, j });
                i = j;
            }
            else i++;
        }

        // 2. Descartar voz demasiado corta (clics, respiraciones).
        int hablaMin = (int)Math.Round(aj.HablaMinMs / 1000.0 / paso);
        List<int[]> vozBuena = new List<int[]>();
        foreach (int[] t in voz) if (t[1] - t[0] >= hablaMin) vozBuena.Add(t);

        // 3. Los huecos entre voz son silencios candidatos (incluye inicio y final).
        int silMin = (int)Math.Round(aj.SilencioMinMs / 1000.0 / paso);
        int antes = (int)Math.Round(aj.MargenAntesMs / 1000.0 / paso);
        int despues = (int)Math.Round(aj.MargenDespuesMs / 1000.0 / paso);
        int cursor = 0;
        for (int k = 0; k <= vozBuena.Count; k++)
        {
            int ini = cursor;
            int fin = k < vozBuena.Count ? vozBuena[k][0] : n;
            bool alInicio = k == 0, alFinal = k == vozBuena.Count;
            if (fin - ini >= silMin)
            {
                // Margen: se deja algo de silencio despues de la voz anterior y
                // antes de la siguiente para que los cortes no suenen bruscos.
                int a0 = ini + (alInicio ? 0 : despues);
                int b0 = fin - (alFinal ? 0 : antes);
                if (b0 - a0 >= 2)
                    resultado.Add(new Rango(a.Inicio + a0 * paso, a.Inicio + b0 * paso));
            }
            if (k < vozBuena.Count) cursor = vozBuena[k][1];
        }
        return resultado;
    }
}

public static class Formato
{
    public static string Tiempo(double s)
    {
        if (s < 0) s = 0;
        int t = (int)Math.Round(s);
        if (t >= 3600) return (t / 3600) + ":" + ((t / 60) % 60).ToString("00") + ":" + (t % 60).ToString("00");
        return (t / 60) + ":" + (t % 60).ToString("00");
    }
}

// =====================================================================
// Interfaz
// =====================================================================

public delegate Analisis FuncionAnalizar(int pista, bool usarSeleccion);

static class Tema
{
    public static readonly Color Fondo = Color.FromArgb(18, 19, 23);
    public static readonly Color Panel = Color.FromArgb(27, 28, 34);
    public static readonly Color Campo = Color.FromArgb(35, 37, 44);
    public static readonly Color CampoHover = Color.FromArgb(44, 46, 55);
    public static readonly Color Borde = Color.FromArgb(52, 54, 64);
    public static readonly Color Texto = Color.FromArgb(236, 237, 241);
    public static readonly Color TextoSuave = Color.FromArgb(150, 153, 164);
    public static readonly Color Acento = Color.FromArgb(255, 106, 43);
    public static readonly Color AcentoHover = Color.FromArgb(255, 132, 80);
    public static readonly Color Voz = Color.FromArgb(120, 200, 255);
    public static readonly Color Silencio = Color.FromArgb(255, 84, 84);

    public static Font Fuente(float tam, FontStyle estilo)
    {
        try { return new Font("Segoe UI", tam, estilo); }
        catch { return new Font(FontFamily.GenericSansSerif, tam, estilo); }
    }
    public static readonly Font Normal = Fuente(9f, FontStyle.Regular);
    public static readonly Font Negrita = Fuente(9f, FontStyle.Bold);
    public static readonly Font Pequena = Fuente(8f, FontStyle.Regular);
    public static readonly Font Titulo = Fuente(15f, FontStyle.Bold);
    public static readonly Font Seccion = Fuente(10f, FontStyle.Bold);

    public static GraphicsPath Redondeado(RectangleF r, float radio)
    {
        GraphicsPath p = new GraphicsPath();
        float d = Math.Min(radio * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

class ControlBase : Control
{
    protected bool encima;
    public ControlBase()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
    }
    protected override void OnMouseEnter(EventArgs e) { encima = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { encima = false; Invalidate(); base.OnMouseLeave(e); }
}

enum EstiloBoton { Primario, Secundario, Chip }

class Boton : ControlBase
{
    public EstiloBoton Estilo = EstiloBoton.Secundario;
    bool activo;
    public bool Activo { get { return activo; } set { activo = value; Invalidate(); } }

    public Boton(string texto, EstiloBoton estilo)
    {
        Text = texto;
        Estilo = estilo;
        Cursor = Cursors.Hand;
        if (estilo == EstiloBoton.Primario) Font = Tema.Fuente(10f, FontStyle.Bold);
    }

    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        Color fondo, borde, texto = Tema.Texto;
        if (Estilo == EstiloBoton.Primario)
        {
            fondo = !Enabled ? Color.FromArgb(90, 60, 48) : encima ? Tema.AcentoHover : Tema.Acento;
            borde = fondo;
            texto = Enabled ? Color.White : Color.FromArgb(170, 150, 140);
        }
        else if (Estilo == EstiloBoton.Chip && activo)
        {
            fondo = Color.FromArgb(60, Tema.Acento);
            borde = Tema.Acento;
        }
        else
        {
            fondo = encima && Enabled ? Tema.CampoHover : Tema.Campo;
            borde = Tema.Borde;
            if (!Enabled) texto = Tema.TextoSuave;
        }
        using (GraphicsPath p = Tema.Redondeado(r, Estilo == EstiloBoton.Chip ? Height / 2f : 8))
        {
            using (SolidBrush b = new SolidBrush(fondo)) g.FillPath(b, p);
            using (Pen pen = new Pen(borde)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, texto,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}

class Segmentado : ControlBase
{
    string[] opciones;
    int seleccion;
    int hover = -1;
    public event EventHandler Cambio;

    public Segmentado(string[] opciones) { this.opciones = opciones; Cursor = Cursors.Hand; }

    public int Seleccion
    {
        get { return seleccion; }
        set { if (value != seleccion) { seleccion = value; Invalidate(); if (Cambio != null) Cambio(this, EventArgs.Empty); } }
    }

    bool[] habilitadas;
    public void Habilitar(int i, bool si)
    {
        if (habilitadas == null) { habilitadas = new bool[opciones.Length]; for (int k = 0; k < opciones.Length; k++) habilitadas[k] = true; }
        habilitadas[i] = si;
        Invalidate();
    }
    bool Habilitada(int i) { return habilitadas == null || habilitadas[i]; }

    int Indice(int x) { return Math.Max(0, Math.Min(opciones.Length - 1, x * opciones.Length / Math.Max(1, Width))); }

    protected override void OnMouseMove(MouseEventArgs e) { int h = Indice(e.X); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int i = Indice(e.X); if (Habilitada(i)) Seleccion = i; base.OnMouseDown(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath p = Tema.Redondeado(r, 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(Tema.Borde)) g.DrawPath(pen, p);
        }
        float ancho = (Width - 6f) / opciones.Length;
        for (int i = 0; i < opciones.Length; i++)
        {
            RectangleF c = new RectangleF(3 + i * ancho, 3, ancho, Height - 7);
            if (i == seleccion)
                using (GraphicsPath p = Tema.Redondeado(c, 6))
                using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
            else if (i == hover && Habilitada(i))
                using (GraphicsPath p = Tema.Redondeado(c, 6))
                using (SolidBrush b = new SolidBrush(Tema.CampoHover)) g.FillPath(b, p);
            Color col = i == seleccion ? Color.White : Habilitada(i) ? Tema.Texto : Color.FromArgb(90, 92, 100);
            TextRenderer.DrawText(g, opciones[i], i == seleccion ? Tema.Negrita : Font, Rectangle.Round(c), col,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

class Deslizador : ControlBase
{
    public double Minimo = -70, Maximo = -10;
    double valor = -40;
    bool arrastrando;
    public event EventHandler Cambio;

    public Deslizador() { Cursor = Cursors.Hand; }

    public double Valor
    {
        get { return valor; }
        set
        {
            double v = Math.Max(Minimo, Math.Min(Maximo, Math.Round(value)));
            if (v != valor) { valor = v; Invalidate(); if (Cambio != null) Cambio(this, EventArgs.Empty); }
        }
    }

    float X(double v) { return 8 + (float)((v - Minimo) / (Maximo - Minimo)) * (Width - 16); }

    void Mover(int x) { Valor = Minimo + (x - 8) / (double)Math.Max(1, Width - 16) * (Maximo - Minimo); }

    protected override void OnMouseDown(MouseEventArgs e) { arrastrando = true; Mover(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (arrastrando) Mover(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { arrastrando = false; base.OnMouseUp(e); }
    protected override void OnMouseWheel(MouseEventArgs e) { Valor = valor + (e.Delta > 0 ? 1 : -1); base.OnMouseWheel(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        float cy = Height / 2f, x = X(valor);
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(8, cy - 3, Width - 16, 6), 3))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(8, cy - 3, Math.Max(6, x - 8), 6), 3))
        using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
        float rad = encima || arrastrando ? 9 : 8;
        using (SolidBrush b = new SolidBrush(Color.White)) g.FillEllipse(b, x - rad, cy - rad, rad * 2, rad * 2);
        using (Pen pen = new Pen(Tema.Acento, 3)) g.DrawEllipse(pen, x - rad + 1.5f, cy - rad + 1.5f, rad * 2 - 3, rad * 2 - 3);
    }
}

// Campo numerico con sufijo "ms": escribir, rueda del raton o flechas.
class CampoNumero : ControlBase
{
    TextBox caja = new TextBox();
    int valor;
    public int Minimo = 0, Maximo = 5000, Paso = 10;
    public event EventHandler Cambio;

    public CampoNumero()
    {
        caja.BorderStyle = BorderStyle.None;
        caja.BackColor = Tema.Campo;
        caja.ForeColor = Tema.Texto;
        caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        caja.TextAlign = HorizontalAlignment.Left;
        caja.KeyPress += delegate (object s, KeyPressEventArgs e) { if (!char.IsDigit(e.KeyChar) && !char.IsControl(e.KeyChar)) e.Handled = true; };
        caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) { Valor = valor + Paso; e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { Valor = valor - Paso; e.Handled = true; }
            else if (e.KeyCode == Keys.Enter) { Confirmar(); e.Handled = true; e.SuppressKeyPress = true; }
        };
        caja.Leave += delegate { Confirmar(); };
        caja.MouseWheel += delegate (object s, MouseEventArgs e) { Valor = valor + (e.Delta > 0 ? Paso : -Paso); };
        Controls.Add(caja);
        Cursor = Cursors.IBeam;
    }

    void Confirmar()
    {
        int v;
        if (int.TryParse(caja.Text, out v)) Valor = v; else caja.Text = valor.ToString();
    }

    public int Valor
    {
        get { return valor; }
        set
        {
            int v = Math.Max(Minimo, Math.Min(Maximo, value));
            caja.Text = v.ToString();
            if (v != valor) { valor = v; if (Cambio != null) Cambio(this, EventArgs.Empty); }
        }
    }

    protected override void OnMouseDown(MouseEventArgs e) { caja.Focus(); base.OnMouseDown(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        caja.SetBounds(12, (Height - caja.PreferredHeight) / 2 + 1, Width - 50, caja.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(caja.Focused ? Tema.Acento : Tema.Borde)) g.DrawPath(pen, p);
        }
        TextRenderer.DrawText(g, "ms", Tema.Pequena, new Rectangle(Width - 36, 0, 28, Height), Tema.TextoSuave,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

// Forma de onda con los silencios marcados y el umbral arrastrable.
// Un carril por pista analizada.
class Carril
{
    public string Etiqueta;
    public Analisis Datos;
    public Color Color;
}

// Forma de onda por pista, con los silencios marcados y el umbral arrastrable.
class VistaOnda : ControlBase
{
    const double MinDb = -80, MaxDb = 0;
    const int AnchoEtiqueta = 44;
    List<Carril> carriles = new List<Carril>();
    List<Rango> rangos = new List<Rango>();
    double umbral = -40, inicio, duracion;
    bool arrastrando;
    int ratonX = -1;
    public string Mensaje = "Elige las pistas y pulsa Analizar.";
    public event EventHandler UmbralArrastrado;

    public static readonly Color[] Colores =
    {
        Color.FromArgb(120, 200, 255), Color.FromArgb(120, 225, 160),
        Color.FromArgb(200, 160, 255), Color.FromArgb(255, 210, 110),
        Color.FromArgb(255, 150, 190), Color.FromArgb(140, 230, 230),
    };

    public void Mostrar(List<Carril> c, List<Rango> r, double u)
    {
        carriles = c ?? new List<Carril>();
        rangos = r;
        umbral = u;
        if (carriles.Count > 0)
        {
            inicio = carriles[0].Datos.Inicio;
            duracion = carriles[0].Datos.Duracion;
            foreach (Carril k in carriles) duracion = Math.Min(duracion, k.Datos.Duracion);
        }
        Invalidate();
    }

    public double Umbral { get { return umbral; } }

    Rectangle Area { get { return new Rectangle(14 + AnchoEtiqueta, 14, Width - 28 - AnchoEtiqueta, Height - 42); } }

    Rectangle AreaCarril(int i)
    {
        Rectangle a = Area;
        int alto = a.Height / Math.Max(1, carriles.Count);
        return new Rectangle(a.X, a.Y + i * alto, a.Width, alto);
    }

    float MitadAltura(Rectangle c, double db)
    {
        return (float)((Math.Max(MinDb, Math.Min(MaxDb, db)) - MinDb) / (MaxDb - MinDb)) * (c.Height - 6) / 2f;
    }

    bool CercaUmbral(int y)
    {
        for (int i = 0; i < carriles.Count; i++)
        {
            Rectangle c = AreaCarril(i);
            float centro = c.Y + c.Height / 2f, h = MitadAltura(c, umbral);
            if (Math.Abs(y - (centro - h)) < 6 || Math.Abs(y - (centro + h)) < 6) return true;
        }
        return false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (carriles.Count > 0 && CercaUmbral(e.Y)) arrastrando = true;
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ratonX = e.X;
        if (arrastrando)
        {
            // El umbral se lee en el carril donde esta el raton.
            int i = Math.Max(0, Math.Min(carriles.Count - 1, (e.Y - Area.Y) / Math.Max(1, AreaCarril(0).Height)));
            Rectangle c = AreaCarril(i);
            float centro = c.Y + c.Height / 2f;
            double t = Math.Abs(e.Y - centro) / ((c.Height - 6) / 2.0);
            umbral = Math.Round(Math.Max(-70, Math.Min(-10, MinDb + t * (MaxDb - MinDb))));
            if (UmbralArrastrado != null) UmbralArrastrado(this, EventArgs.Empty);
        }
        Cursor = arrastrando || (carriles.Count > 0 && CercaUmbral(e.Y)) ? Cursors.SizeNS : Cursors.Default;
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { arrastrando = false; base.OnMouseUp(e); }
    protected override void OnMouseLeave(EventArgs e) { ratonX = -1; base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 10))
        {
            using (SolidBrush b = new SolidBrush(Tema.Panel)) g.FillPath(b, p);
            using (Pen pen = new Pen(Tema.Borde)) g.DrawPath(pen, p);
        }

        if (carriles.Count == 0 || duracion <= 0)
        {
            TextRenderer.DrawText(g, Mensaje, Tema.Normal, ClientRectangle, Tema.TextoSuave,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        Rectangle a = Area;
        g.SmoothingMode = SmoothingMode.None;

        // Silencios detectados: atraviesan todos los carriles.
        using (SolidBrush b = new SolidBrush(Color.FromArgb(50, Tema.Silencio)))
        using (SolidBrush linea = new SolidBrush(Tema.Silencio))
        {
            foreach (Rango r in rangos)
            {
                float x0 = a.X + (float)((r.Inicio - inicio) / duracion * a.Width);
                float x1 = a.X + (float)((r.Fin - inicio) / duracion * a.Width);
                float w = Math.Max(1, x1 - x0);
                g.FillRectangle(b, x0, a.Y, w, a.Height);
                g.FillRectangle(linea, x0, a.Bottom + 3, w, 3);
            }
        }

        for (int k = 0; k < carriles.Count; k++)
        {
            Carril carril = carriles[k];
            Rectangle c = AreaCarril(k);
            float centro = c.Y + c.Height / 2f;
            float[] db = carril.Datos.Db;
            int n = (int)Math.Min(db.Length, Math.Round(duracion / Analisis.Paso));

            if (k > 0)
                using (Pen sep = new Pen(Tema.Borde)) g.DrawLine(sep, a.X - AnchoEtiqueta, c.Y, a.Right, c.Y);

            TextRenderer.DrawText(g, carril.Etiqueta, Tema.Negrita,
                new Rectangle(14, c.Y, AnchoEtiqueta - 6, c.Height), carril.Color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            // Onda: el nivel maximo de cada columna de pixeles, espejado.
            using (Pen voz = new Pen(carril.Color))
            using (Pen bajo = new Pen(Color.FromArgb(80, 86, 100)))
            {
                for (int x = 0; x < a.Width; x++)
                {
                    int i0 = (int)((long)x * n / a.Width);
                    int i1 = Math.Max(i0 + 1, (int)((long)(x + 1) * n / a.Width));
                    float m = -100;
                    for (int i = i0; i < i1 && i < n; i++) if (db[i] > m) m = db[i];
                    float h = MitadAltura(c, m);
                    if (h < 0.5f) continue;
                    g.DrawLine(m >= umbral ? voz : bajo, a.X + x, centro - h, a.X + x, centro + h);
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float hu = MitadAltura(c, umbral);
            using (Pen pen = new Pen(Color.FromArgb(200, Tema.Acento), 1.2f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, a.X, centro - hu, a.Right, centro - hu);
                g.DrawLine(pen, a.X, centro + hu, a.Right, centro + hu);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        // Etiqueta del umbral
        g.SmoothingMode = SmoothingMode.AntiAlias;
        string etiqueta = umbral.ToString("0") + " dB";
        Size ts = TextRenderer.MeasureText(etiqueta, Tema.Pequena);
        Rectangle c0 = AreaCarril(0);
        float yChip = c0.Y + c0.Height / 2f - MitadAltura(c0, umbral) - ts.Height - 4;
        RectangleF chip = new RectangleF(a.Right - ts.Width - 12, Math.Max(a.Y, yChip), ts.Width + 10, ts.Height + 2);
        using (GraphicsPath p = Tema.Redondeado(chip, 4))
        using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
        TextRenderer.DrawText(g, etiqueta, Tema.Pequena, Rectangle.Round(chip), Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        // Tiempos
        TextRenderer.DrawText(g, Formato.Tiempo(inicio), Tema.Pequena, new Point(a.X, a.Bottom + 8), Tema.TextoSuave);
        string fin = Formato.Tiempo(inicio + duracion);
        Size fs = TextRenderer.MeasureText(fin, Tema.Pequena);
        TextRenderer.DrawText(g, fin, Tema.Pequena, new Point(a.Right - fs.Width, a.Bottom + 8), Tema.TextoSuave);

        // Cursor del raton: tiempo y nivel de cada pista
        if (ratonX >= a.X && ratonX < a.Right && !arrastrando)
        {
            double t = (ratonX - a.X) / (double)a.Width * duracion;
            using (Pen pen = new Pen(Color.FromArgb(120, 255, 255, 255))) g.DrawLine(pen, ratonX, a.Y, ratonX, a.Bottom);
            string info = Formato.Tiempo(inicio + t);
            foreach (Carril k in carriles)
            {
                int i = Math.Min(k.Datos.Db.Length - 1, (int)(t / Analisis.Paso));
                info += "  \u00b7  " + k.Etiqueta + " " + k.Datos.Db[i].ToString("0") + " dB";
            }
            Size s = TextRenderer.MeasureText(info, Tema.Pequena);
            int xi = Math.Max(a.X, Math.Min(ratonX + 6, a.Right - s.Width - 6));
            TextRenderer.DrawText(g, info, Tema.Pequena, new Point(xi, a.Y + 2), Tema.Texto);
        }
    }
}

class Etiqueta : Label
{
    public Etiqueta(string texto, Font fuente, Color color)
    {
        Text = texto; Font = fuente; ForeColor = color;
        BackColor = Color.Transparent;
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleLeft;
    }
}

public class VentanaSilencios : Form
{
    readonly FuncionAnalizar analizar;
    readonly string[] nombres;
    // Analisis ya hechos por pista (indice en la lista de pistas de audio).
    Dictionary<int, Analisis> cache = new Dictionary<int, Analisis>();
    Analisis combinado;
    bool cargando;

    public Ajustes Ajustes;
    public List<Rango> Rangos = new List<Rango>();

    List<Boton> chipsPista = new List<Boton>();
    Segmentado segRango = new Segmentado(new string[] { "Todo el proyecto", "Selecci\u00f3n de tiempo" });
    Boton btnAnalizar = new Boton("Analizar", EstiloBoton.Secundario);
    VistaOnda onda = new VistaOnda();
    Deslizador deslizador = new Deslizador();
    Etiqueta lblUmbral = new Etiqueta("", Tema.Negrita, Tema.Texto);
    Boton btnAuto = new Boton("Auto", EstiloBoton.Chip);
    List<Boton> chips = new List<Boton>();
    CampoNumero numSilencio = new CampoNumero(), numHabla = new CampoNumero();
    CampoNumero numAntes = new CampoNumero(), numDespues = new CampoNumero();
    Segmentado segModo = new Segmentado(new string[] { "Eliminar", "Dejar huecos", "Silenciar", "Solo marcar" });
    Segmentado segPistas = new Segmentado(new string[] { "Todas las pistas", "Solo las analizadas" });
    Etiqueta lblResumen = new Etiqueta("", Tema.Normal, Tema.TextoSuave);
    Etiqueta lblResumenGrande = new Etiqueta("", Tema.Fuente(12f, FontStyle.Bold), Tema.Texto);
    Boton btnAplicar = new Boton("Quitar silencios", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);
    ToolTip ayuda = new ToolTip();

    public List<int> PistasElegidas
    {
        get
        {
            List<int> r = new List<int>();
            for (int i = 0; i < chipsPista.Count; i++) if (chipsPista[i].Activo) r.Add(i);
            return r;
        }
    }

    public VentanaSilencios(string[] nombres, string[] detalles, int sugerida, bool haySeleccion, FuncionAnalizar analizar)
    {
        this.analizar = analizar;
        this.nombres = nombres;
        Ajustes = Ajustes.Cargar();

        Text = "Quitar silencios \u00b7 vegas-cut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        DoubleBuffered = true;
        KeyPreview = true;

        const int anchoVentana = 860;
        int m = 24, ancho = anchoVentana - m * 2;

        // Encabezado
        Controls.Add(Pos(new Etiqueta("Quitar silencios", Tema.Titulo, Tema.Texto), m, 18, 400, 32));
        Controls.Add(Pos(new Etiqueta("Detecta las pausas de la voz y las quita de la l\u00ednea de tiempo.",
            Tema.Normal, Tema.TextoSuave), m, 50, 600, 20));

        // Pistas que se escuchan
        int y = 90;
        Controls.Add(Pos(new Etiqueta("PISTAS DE VOZ", Tema.Pequena, Tema.TextoSuave), m, y, 96, 18));
        Controls.Add(Pos(new Etiqueta("Hay voz si suena cualquiera de las marcadas. Las dem\u00e1s (juego, m\u00fasica) no cuentan.",
            Tema.Pequena, Tema.TextoSuave), m + 100, y, ancho - 100, 18));
        y += 22;
        int cx = m;
        for (int i = 0; i < nombres.Length; i++)
        {
            Boton c = new Boton(nombres[i], EstiloBoton.Chip);
            c.Activo = i == sugerida;
            int w = Math.Min(ancho, TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 26);
            if (cx + w > m + ancho) { cx = m; y += 34; }
            Controls.Add(Pos(c, cx, y, w, 28));
            ayuda.SetToolTip(c, detalles[i]);
            c.Click += delegate
            {
                // Siempre queda al menos una pista marcada.
                if (c.Activo && PistasElegidas.Count == 1) return;
                c.Activo = !c.Activo;
                Recalcular();
            };
            chipsPista.Add(c);
            cx += w + 6;
        }
        y += 28 + 14;

        // Rango + analizar
        segRango.Seleccion = haySeleccion ? 1 : 0;
        segRango.Habilitar(1, haySeleccion);
        Controls.Add(Pos(segRango, m, y, 300, 34));
        Controls.Add(Pos(btnAnalizar, m + ancho - 170, y, 170, 34));
        y += 34 + 14;

        // Onda: un carril por pista, mas alta si hay varias
        int altoOnda = 200;
        Controls.Add(Pos(onda, m, y, ancho, altoOnda));
        y += altoOnda + 20;

        // Deteccion
        Controls.Add(Pos(new Etiqueta("Umbral de ruido", Tema.Seccion, Tema.Texto), m, y, 200, 22));
        Controls.Add(Pos(new Etiqueta("Lo que quede debajo cuenta como silencio. Tambi\u00e9n puedes arrastrar la l\u00ednea.",
            Tema.Pequena, Tema.TextoSuave), m, y + 22, 480, 18));
        Controls.Add(Pos(deslizador, m - 4, y + 48, 330, 30));
        Controls.Add(Pos(lblUmbral, m + 334, y + 48, 64, 30));
        Controls.Add(Pos(btnAuto, m + 400, y + 50, 64, 26));

        Controls.Add(Pos(new Etiqueta("Ritmo", Tema.Seccion, Tema.Texto), m + 500, y, 200, 22));
        Controls.Add(Pos(new Etiqueta("Valores r\u00e1pidos; luego puedes afinar.", Tema.Pequena, Tema.TextoSuave), m + 500, y + 22, 312, 18));
        int px = m + 500, py = y + 48;
        for (int i = 0; i < Preset.Todos.Length; i++)
        {
            Boton c = new Boton(Preset.Todos[i].Nombre, EstiloBoton.Chip);
            Preset pr = Preset.Todos[i];
            c.Click += delegate { AplicarPreset(pr); };
            chips.Add(c);
            int w = TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 22;
            if (px + w > m + ancho) { px = m + 500; py += 34; }
            Controls.Add(Pos(c, px, py, w, 28));
            px += w + 6;
        }
        y += 130;

        // Tiempos
        int col = (ancho - 36) / 4;
        CampoTiempo(numSilencio, "Silencio m\u00ednimo", "Quita pausas m\u00e1s largas", m, y, col);
        CampoTiempo(numHabla, "Voz m\u00ednima", "Ignora ruidos m\u00e1s cortos", m + (col + 12), y, col);
        CampoTiempo(numAntes, "Margen antes", "Pausa que queda antes de hablar", m + (col + 12) * 2, y, col);
        CampoTiempo(numDespues, "Margen despu\u00e9s", "Pausa que queda al terminar", m + (col + 12) * 3, y, col);
        y += 100;

        // Separador + accion
        Panel sep = new Panel();
        sep.BackColor = Tema.Borde;
        Controls.Add(Pos(sep, m, y, ancho, 1));

        Controls.Add(Pos(new Etiqueta("QU\u00c9 HACER", Tema.Pequena, Tema.TextoSuave), m, y + 16, 200, 18));
        Controls.Add(Pos(segModo, m, y + 36, 440, 34));
        Controls.Add(Pos(new Etiqueta("D\u00d3NDE CORTAR", Tema.Pequena, Tema.TextoSuave), m + 456, y + 16, 200, 18));
        Controls.Add(Pos(segPistas, m + 456, y + 36, ancho - 456, 34));
        ayuda.SetToolTip(segPistas, "Todas: corta tambi\u00e9n video, juego y m\u00fasica para que todo siga sincronizado.");
        ayuda.SetToolTip(segModo, "Eliminar junta todo; Dejar huecos quita sin mover; Silenciar deja mudo; Solo marcar crea regiones.");

        Controls.Add(Pos(lblResumenGrande, m, y + 90, 400, 24));
        Controls.Add(Pos(lblResumen, m, y + 114, 460, 20));
        Controls.Add(Pos(btnCancelar, m + ancho - 300, y + 90, 110, 40));
        Controls.Add(Pos(btnAplicar, m + ancho - 180, y + 90, 180, 40));
        ClientSize = new Size(anchoVentana, y + 90 + 40 + 24);

        // Valores iniciales
        cargando = true;
        deslizador.Valor = Ajustes.UmbralDb;
        numSilencio.Valor = Ajustes.SilencioMinMs; numSilencio.Maximo = 10000;
        numHabla.Valor = Ajustes.HablaMinMs;
        numAntes.Valor = Ajustes.MargenAntesMs; numAntes.Maximo = 2000;
        numDespues.Valor = Ajustes.MargenDespuesMs; numDespues.Maximo = 2000;
        segModo.Seleccion = (int)Ajustes.Modo;
        segPistas.Seleccion = Ajustes.TodasLasPistas ? 0 : 1;
        btnAuto.Activo = Ajustes.UmbralAutomatico;
        cargando = false;

        // Eventos
        btnAnalizar.Click += delegate { Analizar(); };
        deslizador.Cambio += delegate { if (!cargando) { btnAuto.Activo = false; Recalcular(); } };
        onda.UmbralArrastrado += delegate { btnAuto.Activo = false; deslizador.Valor = onda.Umbral; };
        btnAuto.Click += delegate
        {
            btnAuto.Activo = true;
            if (combinado != null)
            {
                cargando = true;
                deslizador.Valor = Detector.UmbralAutomatico(combinado.Db);
                cargando = false;
            }
            Recalcular();
        };
        EventHandler recalc = delegate { if (!cargando) { MarcarPreset(); Recalcular(); } };
        numSilencio.Cambio += recalc; numHabla.Cambio += recalc; numAntes.Cambio += recalc; numDespues.Cambio += recalc;
        segModo.Cambio += delegate { ActualizarTextoBoton(); };
        segPistas.Cambio += delegate { Recalcular(); };
        segRango.Cambio += delegate { cache.Clear(); Recalcular(); };
        btnAplicar.Click += delegate { Aplicar(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        MarcarPreset();
        ActualizarTextoBoton();
        Recalcular();
    }

    static Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); return c; }

    void CampoTiempo(CampoNumero campo, string titulo, string texto, int x, int y, int w)
    {
        Controls.Add(Pos(new Etiqueta(titulo, Tema.Negrita, Tema.Texto), x, y, w, 20));
        Controls.Add(Pos(new Etiqueta(texto, Tema.Pequena, Tema.TextoSuave), x, y + 20, w, 18));
        Controls.Add(Pos(campo, x, y + 42, w, 36));
    }

    void AplicarPreset(Preset p)
    {
        cargando = true;
        numSilencio.Valor = p.SilencioMin; numHabla.Valor = p.HablaMin;
        numAntes.Valor = p.Antes; numDespues.Valor = p.Despues;
        cargando = false;
        MarcarPreset();
        Recalcular();
    }

    void MarcarPreset()
    {
        for (int i = 0; i < chips.Count; i++)
        {
            Preset p = Preset.Todos[i];
            chips[i].Activo = p.SilencioMin == numSilencio.Valor && p.HablaMin == numHabla.Valor &&
                              p.Antes == numAntes.Valor && p.Despues == numDespues.Valor;
        }
    }

    void ActualizarTextoBoton()
    {
        string[] textos = { "Quitar silencios", "Quitar sin mover", "Silenciar", "Marcar silencios" };
        btnAplicar.Text = textos[segModo.Seleccion];
        segPistas.Enabled = segModo.Seleccion != (int)Modo.Marcar;
    }

    void LeerAjustes()
    {
        Ajustes.UmbralDb = deslizador.Valor;
        Ajustes.UmbralAutomatico = btnAuto.Activo;
        Ajustes.SilencioMinMs = numSilencio.Valor;
        Ajustes.HablaMinMs = numHabla.Valor;
        Ajustes.MargenAntesMs = numAntes.Valor;
        Ajustes.MargenDespuesMs = numDespues.Valor;
        Ajustes.Modo = (Modo)segModo.Seleccion;
        Ajustes.TodasLasPistas = segPistas.Seleccion == 0;
    }

    List<int> Faltantes()
    {
        List<int> f = new List<int>();
        foreach (int i in PistasElegidas) if (!cache.ContainsKey(i)) f.Add(i);
        return f;
    }

    void Analizar()
    {
        // Si ya estaba todo leido, el boton vuelve a leer las pistas marcadas.
        List<int> leer = Faltantes();
        if (leer.Count == 0) { foreach (int i in PistasElegidas) cache.Remove(i); leer = PistasElegidas; }

        btnAnalizar.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            for (int k = 0; k < leer.Count; k++)
            {
                btnAnalizar.Text = "Leyendo " + (k + 1) + " de " + leer.Count + "\u2026";
                onda.Mensaje = "Leyendo el audio de " + nombres[leer[k]] + "\u2026";
                onda.Mostrar(null, new List<Rango>(), deslizador.Valor);
                Application.DoEvents();
                cache[leer[k]] = analizar(leer[k], segRango.Seleccion == 1);
            }
            if (btnAuto.Activo)
            {
                cargando = true;
                deslizador.Valor = Detector.UmbralAutomatico(Combinado().Db);
                cargando = false;
            }
        }
        catch (Exception ex)
        {
            onda.Mensaje = "No se pudo analizar: " + ex.Message;
            onda.Mostrar(null, new List<Rango>(), deslizador.Valor);
        }
        finally
        {
            Cursor = Cursors.Default;
            btnAnalizar.Enabled = true;
        }
        Recalcular();
    }

    Analisis Combinado()
    {
        List<Analisis> l = new List<Analisis>();
        foreach (int i in PistasElegidas) l.Add(cache[i]);
        return Analisis.Combinar(l);
    }

    void Recalcular()
    {
        LeerAjustes();
        lblUmbral.Text = Ajustes.UmbralDb.ToString("0") + " dB";
        List<int> faltan = Faltantes();
        btnAnalizar.Text = faltan.Count == 0 ? "Reanalizar" :
            cache.Count == 0 ? "Analizar" : "Analizar " + faltan.Count + (faltan.Count == 1 ? " pista" : " pistas");

        if (faltan.Count > 0)
        {
            combinado = null;
            Rangos = new List<Rango>();
            if (cache.Count > 0) onda.Mensaje = "Pulsa Analizar para leer las pistas nuevas.";
            onda.Mostrar(null, Rangos, Ajustes.UmbralDb);
            lblResumenGrande.Text = "Sin analizar";
            lblResumen.Text = "Analiza para ver los silencios.";
            btnAplicar.Enabled = false;
            return;
        }

        combinado = Combinado();
        Rangos = Detector.Detectar(combinado, Ajustes);

        List<Carril> carriles = new List<Carril>();
        List<int> elegidas = PistasElegidas;
        for (int k = 0; k < elegidas.Count; k++)
        {
            Carril c = new Carril();
            string n = nombres[elegidas[k]];
            int punto = n.IndexOf(' ');
            c.Etiqueta = punto > 0 ? n.Substring(0, punto) : n;
            c.Datos = cache[elegidas[k]];
            c.Color = VistaOnda.Colores[k % VistaOnda.Colores.Length];
            carriles.Add(c);
        }
        onda.Mostrar(carriles, Rangos, Ajustes.UmbralDb);

        double quitado = 0;
        foreach (Rango r in Rangos) quitado += r.Fin - r.Inicio;
        double dur = combinado.Duracion;
        double pct = dur > 0 ? quitado / dur * 100 : 0;
        lblResumenGrande.Text = Rangos.Count + (Rangos.Count == 1 ? " silencio" : " silencios") +
                                " \u00b7 " + Formato.Tiempo(quitado);
        lblResumen.Text = Formato.Tiempo(dur) + " \u2192 " + Formato.Tiempo(dur - quitado) +
                          "  (\u2212" + pct.ToString("0") + " %)  \u00b7  Ctrl+Z lo deshace";
        btnAplicar.Enabled = Rangos.Count > 0;
    }

    void Aplicar()
    {
        LeerAjustes();
        Ajustes.Guardar();
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        // Linea de acento bajo el encabezado
        using (SolidBrush b = new SolidBrush(Tema.Acento)) e.Graphics.FillRectangle(b, 24, 76, 36, 3);
    }
}
