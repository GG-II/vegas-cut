// ExportarProyecto.cs
// Script para VEGAS Pro 20 (Tools > Scripting > Run Script...).
// Exporta el proyecto abierto a un archivo JSON con pistas, eventos, efectos,
// keyframes de Pan/Crop, textos, marcadores y regiones, para analizar
// patrones de edicion fuera de Vegas.
//
// El JSON se guarda junto al .veg como "<proyecto>.export.json".
// Si el proyecto no esta guardado, se guarda en Documentos.
//
// Escrito en C# 5 (sin interpolacion de cadenas ni "?.") porque Vegas compila
// los scripts con el compilador clasico de .NET Framework.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    const int FormatoVersion = 1;

    public void FromVegas(Vegas vegas)
    {
        Project proyecto = vegas.Project;

        Dictionary<string, object> raiz = new Dictionary<string, object>();
        raiz["formato"] = "vegas-cut-export";
        raiz["formatoVersion"] = FormatoVersion;
        raiz["vegasVersion"] = Propiedad(vegas, "Version");
        raiz["exportado"] = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        raiz["proyecto"] = ExportarConfiguracion(proyecto);
        raiz["media"] = ExportarMedia(proyecto);
        raiz["pistas"] = ExportarPistas(proyecto);
        raiz["marcadores"] = ExportarMarcadores(proyecto);
        raiz["regiones"] = ExportarRegiones(proyecto);

        string ruta = RutaSalida(proyecto);
        File.WriteAllText(ruta, Json.Serializar(raiz), new UTF8Encoding(false));

        int eventos = 0;
        foreach (Track pista in proyecto.Tracks) eventos += pista.Events.Count;
        MessageBox.Show(
            "Proyecto exportado.\n\n" +
            "Pistas: " + proyecto.Tracks.Count + "\n" +
            "Eventos: " + eventos + "\n\n" + ruta,
            "Exportar proyecto");
    }

    static string RutaSalida(Project proyecto)
    {
        string veg = proyecto.FilePath;
        if (!String.IsNullOrEmpty(veg))
            return Path.Combine(Path.GetDirectoryName(veg),
                Path.GetFileNameWithoutExtension(veg) + ".export.json");

        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Path.Combine(docs, "sin-titulo-" +
            DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".export.json");
    }

    // ---------------------------------------------------------------- proyecto

    static Dictionary<string, object> ExportarConfiguracion(Project proyecto)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["archivo"] = proyecto.FilePath;
        d["duracion"] = Seg(proyecto.Length);
        d["ancho"] = proyecto.Video.Width;
        d["alto"] = proyecto.Video.Height;
        d["fps"] = proyecto.Video.FrameRate;
        d["aspectoPixel"] = proyecto.Video.PixelAspectRatio;
        d["frecuenciaAudio"] = proyecto.Audio.SampleRate;
        return d;
    }

    static List<object> ExportarMedia(Project proyecto)
    {
        List<object> lista = new List<object>();
        foreach (Media media in proyecto.MediaPool)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["archivo"] = media.FilePath;
            d["generado"] = media.IsGenerated();
            d["duracion"] = Seg(media.Length);
            d["tieneVideo"] = media.HasVideo();
            d["tieneAudio"] = media.HasAudio();
            if (media.IsGenerated() && media.Generator != null)
                d["generador"] = ExportarEfecto(media.Generator);
            lista.Add(d);
        }
        return lista;
    }

    // ------------------------------------------------------------------ pistas

    static List<object> ExportarPistas(Project proyecto)
    {
        List<object> lista = new List<object>();
        foreach (Track pista in proyecto.Tracks)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["indice"] = pista.Index;
            d["tipo"] = pista.IsVideo() ? "video" : "audio";
            d["nombre"] = pista.Name;
            d["silenciada"] = pista.Mute;
            d["solo"] = pista.Solo;

            AudioTrack audio = pista as AudioTrack;
            if (audio != null)
            {
                d["volumen"] = audio.Volume;
                d["paneo"] = audio.PanX;
            }
            VideoTrack video = pista as VideoTrack;
            if (video != null)
                d["modoComposicion"] = video.CompositeMode.ToString();

            d["efectos"] = ExportarEfectos(Propiedad(pista, "Effects") as IEnumerable);
            d["envolventes"] = ExportarEnvolventes(Propiedad(pista, "Envelopes") as IEnumerable);

            List<object> eventos = new List<object>();
            foreach (TrackEvent evento in pista.Events)
                eventos.Add(ExportarEvento(evento));
            d["eventos"] = eventos;

            lista.Add(d);
        }
        return lista;
    }

    static Dictionary<string, object> ExportarEvento(TrackEvent evento)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["inicio"] = Seg(evento.Start);
        d["duracion"] = Seg(evento.Length);
        d["fin"] = Seg(evento.End);
        d["silenciado"] = evento.Mute;
        d["velocidad"] = evento.PlaybackRate;
        d["fadeIn"] = ExportarFade(evento.FadeIn);
        d["fadeOut"] = ExportarFade(evento.FadeOut);
        d["ganancia"] = evento.FadeIn.Gain;

        Take toma = evento.ActiveTake;
        if (toma != null)
        {
            d["toma"] = toma.Name;
            d["offset"] = Seg(toma.Offset);
            if (toma.Media != null)
            {
                d["archivo"] = toma.Media.FilePath;
                d["generado"] = toma.Media.IsGenerated();
                if (toma.Media.IsGenerated() && toma.Media.Generator != null)
                    d["generador"] = ExportarEfecto(toma.Media.Generator);
            }
        }
        d["numeroTomas"] = evento.Takes.Count;

        VideoEvent video = evento as VideoEvent;
        if (video != null)
        {
            d["efectos"] = ExportarEfectos(video.Effects);
            d["panCrop"] = ExportarPanCrop(video);
        }

        AudioEvent audio = evento as AudioEvent;
        if (audio != null)
        {
            d["normalizado"] = audio.Normalize;
            d["gananciaNormalizacion"] = audio.NormalizeGain;
        }

        d["envolventes"] = ExportarEnvolventes(Propiedad(evento, "Envelopes") as IEnumerable);
        return d;
    }

    static Dictionary<string, object> ExportarFade(Fade fade)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["duracion"] = Seg(fade.Length);
        d["curva"] = fade.Curve.ToString();
        return d;
    }

    // Keyframes de Pan/Crop. "zoom" = ancho del proyecto / ancho del recuadro:
    // mayor que 1 significa que el clip esta acercado.
    static List<object> ExportarPanCrop(VideoEvent evento)
    {
        List<object> lista = new List<object>();
        foreach (VideoMotionKeyframe k in evento.VideoMotion.Keyframes)
        {
            VideoMotionBounds b = k.Bounds;
            double ancho = Distancia(b.TopLeft, b.TopRight);
            double alto = Distancia(b.TopLeft, b.BottomLeft);

            Dictionary<string, object> d = new Dictionary<string, object>();
            d["posicion"] = Seg(k.Position);
            d["tipo"] = k.Type.ToString();
            d["suavidad"] = k.Smoothness;
            d["rotacion"] = k.Rotation;
            d["centroX"] = k.Center.X;
            d["centroY"] = k.Center.Y;
            d["ancho"] = ancho;
            d["alto"] = alto;
            d["zoom"] = ancho > 0 ? evento.Track.Project.Video.Width / ancho : 0;
            lista.Add(d);
        }
        return lista;
    }

    static double Distancia(VideoMotionVertex a, VideoMotionVertex b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    // ----------------------------------------------------------------- efectos

    static List<object> ExportarEfectos(IEnumerable efectos)
    {
        List<object> lista = new List<object>();
        if (efectos == null) return lista;
        foreach (object o in efectos)
        {
            Effect efecto = o as Effect;
            if (efecto != null) lista.Add(ExportarEfecto(efecto));
        }
        return lista;
    }

    static Dictionary<string, object> ExportarEfecto(Effect efecto)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        if (efecto.PlugIn != null)
        {
            d["nombre"] = efecto.PlugIn.Name;
            d["id"] = efecto.PlugIn.UniqueID;
        }
        d["desactivado"] = efecto.Bypass;
        d["preset"] = Propiedad(efecto, "Preset");

        // Parametros OFX (Titulos y Texto, Sapphire, Blanco y negro, etc.).
        // Se leen por reflexion para no depender de cada tipo de parametro.
        if (efecto.IsOFX)
        {
            List<object> parametros = new List<object>();
            try
            {
                foreach (object p in efecto.OFXEffect.Parameters)
                {
                    Dictionary<string, object> dp = new Dictionary<string, object>();
                    dp["nombre"] = Propiedad(p, "Name");
                    dp["tipo"] = p.GetType().Name;
                    dp["valor"] = ValorSimple(Propiedad(p, "Value"));
                    object animado = Propiedad(p, "IsAnimated");
                    if (animado is bool && (bool)animado)
                    {
                        dp["animado"] = true;
                        IEnumerable keyframes = Propiedad(p, "Keyframes") as IEnumerable;
                        dp["keyframes"] = Contar(keyframes);
                    }
                    parametros.Add(dp);
                }
            }
            catch (Exception ex)
            {
                d["errorParametros"] = ex.Message;
            }
            d["parametros"] = parametros;
        }
        return d;
    }

    // ------------------------------------------------------------- envolventes

    static List<object> ExportarEnvolventes(IEnumerable envolventes)
    {
        List<object> lista = new List<object>();
        if (envolventes == null) return lista;
        foreach (object o in envolventes)
        {
            Envelope env = o as Envelope;
            if (env == null) continue;
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["tipo"] = env.Type.ToString();
            List<object> puntos = new List<object>();
            foreach (EnvelopePoint p in env.Points)
            {
                Dictionary<string, object> dp = new Dictionary<string, object>();
                dp["posicion"] = Seg(p.X);
                dp["valor"] = p.Y;
                dp["curva"] = p.Curve.ToString();
                puntos.Add(dp);
            }
            d["puntos"] = puntos;
            lista.Add(d);
        }
        return lista;
    }

    // -------------------------------------------------- marcadores y regiones

    static List<object> ExportarMarcadores(Project proyecto)
    {
        List<object> lista = new List<object>();
        foreach (Marker m in proyecto.Markers)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["posicion"] = Seg(m.Position);
            d["etiqueta"] = m.Label;
            lista.Add(d);
        }
        return lista;
    }

    static List<object> ExportarRegiones(Project proyecto)
    {
        List<object> lista = new List<object>();
        foreach (Region r in proyecto.Regions)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["inicio"] = Seg(r.Position);
            d["duracion"] = Seg(r.Length);
            d["etiqueta"] = r.Label;
            lista.Add(d);
        }
        return lista;
    }

    // ------------------------------------------------------------- utilidades

    static double Seg(Timecode t)
    {
        return Object.ReferenceEquals(t, null) ? 0 : Math.Round(t.ToMilliseconds() / 1000.0, 4);
    }

    static int Contar(IEnumerable e)
    {
        int n = 0;
        if (e != null) foreach (object o in e) n++;
        return n;
    }

    // Lee una propiedad por nombre; devuelve null si no existe o falla.
    static object Propiedad(object obj, string nombre)
    {
        if (obj == null) return null;
        try
        {
            PropertyInfo p = obj.GetType().GetProperty(nombre);
            return p == null ? null : p.GetValue(obj, null);
        }
        catch
        {
            return null;
        }
    }

    // Convierte valores de parametros OFX a algo serializable: tipos simples
    // tal cual; estructuras (colores, puntos 2D) como sus campos numericos.
    static object ValorSimple(object v)
    {
        if (v == null || v is string || v is bool || v.GetType().IsPrimitive || v is Enum)
            return v is Enum ? v.ToString() : v;
        if (v is Timecode) return Seg((Timecode)v);

        Dictionary<string, object> d = new Dictionary<string, object>();
        foreach (PropertyInfo p in v.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (p.GetIndexParameters().Length > 0) continue;
            object x;
            try { x = p.GetValue(v, null); } catch { continue; }
            if (x == null || x is string || x.GetType().IsPrimitive) d[p.Name] = x;
            else if (x is Enum) d[p.Name] = x.ToString();
        }
        foreach (FieldInfo f in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            object x = f.GetValue(v);
            if (x == null || x is string || x.GetType().IsPrimitive) d[f.Name] = x;
        }
        return d.Count > 0 ? (object)d : v.ToString();
    }
}

// Serializador JSON minimo (Vegas no trae Newtonsoft.Json).
static class Json
{
    public static string Serializar(object valor)
    {
        StringBuilder sb = new StringBuilder();
        Escribir(sb, valor, 0);
        return sb.ToString();
    }

    static void Escribir(StringBuilder sb, object v, int nivel)
    {
        if (v == null) { sb.Append("null"); return; }
        if (v is string) { Cadena(sb, (string)v); return; }
        if (v is bool) { sb.Append((bool)v ? "true" : "false"); return; }
        if (v is double || v is float || v is decimal)
        {
            double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
            if (Double.IsNaN(d) || Double.IsInfinity(d)) sb.Append("null");
            else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
            return;
        }
        if (v.GetType().IsPrimitive)
        {
            sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture));
            return;
        }

        IDictionary dic = v as IDictionary;
        if (dic != null)
        {
            if (dic.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            bool primero = true;
            foreach (DictionaryEntry e in dic)
            {
                if (!primero) sb.Append(",\n");
                primero = false;
                Sangria(sb, nivel + 1);
                Cadena(sb, e.Key.ToString());
                sb.Append(": ");
                Escribir(sb, e.Value, nivel + 1);
            }
            sb.Append("\n");
            Sangria(sb, nivel);
            sb.Append("}");
            return;
        }

        IEnumerable lista = v as IEnumerable;
        if (lista != null)
        {
            bool vacia = true;
            foreach (object o in lista)
            {
                sb.Append(vacia ? "[\n" : ",\n");
                vacia = false;
                Sangria(sb, nivel + 1);
                Escribir(sb, o, nivel + 1);
            }
            if (vacia) { sb.Append("[]"); return; }
            sb.Append("\n");
            Sangria(sb, nivel);
            sb.Append("]");
            return;
        }

        Cadena(sb, v.ToString());
    }

    static void Sangria(StringBuilder sb, int nivel)
    {
        sb.Append(' ', nivel * 2);
    }

    static void Cadena(StringBuilder sb, string s)
    {
        sb.Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                    else sb.Append(c);
                    break;
            }
        }
        sb.Append('"');
    }
}
