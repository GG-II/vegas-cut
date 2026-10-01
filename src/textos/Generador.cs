using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using ScriptPortal.Vegas;

// =====================================================================
// Eventos de "Titulos y texto" a partir de una plantilla
//
// Cada evento de texto es un medio generado con sus propios parametros
// (OFX). Para copiar el estilo se crea un medio nuevo con el mismo generador
// y se le pasan, uno por uno, los valores de la plantilla; despues se cambia
// solo el texto (RTF), conservando su formato.
// =====================================================================

public class Plantilla
{
    public VideoEvent Evento;     // null: Titulos y texto con el estilo por defecto
    public PlugInNode PlugIn;
    public string TextoRtf = "";
    public string Origen = "";    // como se encontro, para mostrarlo
    public string Texto { get { return Rtf.TextoPlano(TextoRtf); } }
}

public static class GeneradorTexto
{
    static readonly string[] Ids = { "{Svfx:com.vegascreativesoftware:titlesandtext}",
                                     "{Svfx:com.sonycreativesoftware:titlesandtext}" };

    static OFXEffect Ofx(Effect e)
    {
        try { return e != null && e.IsOFX ? e.OFXEffect : null; } catch { return null; }
    }

    static OFXStringParameter ParametroTexto(Media m)
    {
        try
        {
            if (m == null || !m.IsGenerated()) return null;
            OFXEffect o = Ofx(m.Generator);
            return o == null ? null : o.FindParameterByName("Text") as OFXStringParameter;
        }
        catch { return null; }
    }

    static Media MediaDe(TrackEvent e)
    {
        return e == null || e.ActiveTake == null ? null : e.ActiveTake.Media;
    }

    public static bool EsTexto(TrackEvent e) { return e is VideoEvent && ParametroTexto(MediaDe(e)) != null; }

    // Plantilla: el texto seleccionado; si no hay, el texto mas cercano al
    // cursor; si no hay ninguno, Titulos y texto con su estilo normal.
    public static Plantilla Buscar(Vegas vegas)
    {
        double cursor = vegas.Transport.CursorPosition.ToMilliseconds() / 1000.0;
        VideoEvent elegido = null, cercano = null;
        double mejor = double.MaxValue;
        foreach (Track t in vegas.Project.Tracks)
        {
            if (t.IsAudio()) continue;
            foreach (TrackEvent e in t.Events)
            {
                if (!EsTexto(e)) continue;
                if (e.Selected && elegido == null) elegido = (VideoEvent)e;
                double d = Math.Abs(e.Start.ToMilliseconds() / 1000.0 - cursor);
                if (d < mejor) { mejor = d; cercano = (VideoEvent)e; }
            }
        }
        Plantilla p = new Plantilla();
        p.Evento = elegido ?? cercano;
        if (p.Evento != null)
        {
            p.Origen = elegido != null ? "el texto seleccionado" : "el texto más cercano al cursor";
            p.PlugIn = MediaDe(p.Evento).Generator.PlugIn;
            p.TextoRtf = ParametroTexto(MediaDe(p.Evento)).Value ?? "";
            return p;
        }
        p.Origen = "Títulos y texto con su estilo normal (no hay ningún texto en el proyecto)";
        foreach (string id in Ids)
        {
            try { p.PlugIn = vegas.Generators.GetChildByUniqueID(id); } catch { }
            if (p.PlugIn != null) break;
        }
        return p;
    }

    public static int PistaDe(Plantilla p) { return p.Evento == null ? -1 : p.Evento.Track.Index; }

    // Crea el evento de texto en la pista, de inicio a inicio+duracion.
    public static VideoEvent Crear(VideoTrack pista, Plantilla p, double inicio, double duracion, string texto)
    {
        if (p.PlugIn == null) throw new Exception("No se encontró el generador de Títulos y texto.");
        Media media = new Media(p.PlugIn);
        MediaStream flujo = media.Streams.GetItemByMediaType(MediaType.Video, 0);
        VideoEvent ev = pista.AddVideoEvent(Timecode.FromMilliseconds(inicio * 1000), Timecode.FromMilliseconds(duracion * 1000));
        ev.AddTake(flujo);

        OFXEffect nuevo = Ofx(media.Generator);
        if (p.Evento != null)
        {
            Media origen = MediaDe(p.Evento);
            CopiarParametros(Ofx(origen.Generator), nuevo);
            try { ev.FadeIn.Length = p.Evento.FadeIn.Length; ev.FadeOut.Length = p.Evento.FadeOut.Length; } catch { }
            CopiarEfectos(p.Evento, ev);
        }
        OFXStringParameter txt = ParametroTexto(media);
        if (txt != null) txt.Value = Rtf.ReemplazarTexto(p.TextoRtf, texto);
        if (nuevo != null) try { nuevo.AllParametersChanged(); } catch { }
        return ev;
    }

    // Efectos del evento (sombra, borde, movimiento...) con sus valores.
    static void CopiarEfectos(VideoEvent de, VideoEvent a)
    {
        try
        {
            foreach (Effect fx in de.Effects)
            {
                try
                {
                    Effect copia = new Effect(fx.PlugIn);
                    a.Effects.Add(copia);
                    copia.Bypass = fx.Bypass;
                    CopiarParametros(Ofx(fx), Ofx(copia));
                    OFXEffect o = Ofx(copia);
                    if (o != null) o.AllParametersChanged();
                }
                catch { }
            }
        }
        catch { }
    }

    // Copia cada parametro por nombre (por reflexion: cada tipo de parametro
    // tiene su propio Value). Si esta animado, tambien sus fotogramas clave.
    public static int CopiarParametros(OFXEffect de, OFXEffect a)
    {
        if (de == null || a == null) return 0;
        int n = 0;
        foreach (OFXParameter p in de.Parameters)
        {
            if (p.Name == "Text") continue;
            OFXParameter q;
            try { q = a.FindParameterByName(p.Name); } catch { continue; }
            if (q == null || q.GetType() != p.GetType()) continue;
            try
            {
                PropertyInfo valor = p.GetType().GetProperty("Value");
                if (valor == null || !valor.CanWrite) continue;
                valor.SetValue(q, valor.GetValue(p, null), null);
                n++;
                CopiarClaves(p, q);
            }
            catch { }
        }
        return n;
    }

    static void CopiarClaves(OFXParameter de, OFXParameter a)
    {
        Type t = de.GetType();
        PropertyInfo animado = t.GetProperty("IsAnimated");
        if (animado == null || !(bool)animado.GetValue(de, null)) return;
        PropertyInfo claves = t.GetProperty("Keyframes");
        MethodInfo poner = t.GetMethod("SetValueAtTime");
        if (claves == null || poner == null) return;
        if (animado.CanWrite) animado.SetValue(a, true, null);
        foreach (object k in (IEnumerable)claves.GetValue(de, null))
        {
            object tiempo = k.GetType().GetProperty("Time").GetValue(k, null);
            object v = k.GetType().GetProperty("Value").GetValue(k, null);
            poner.Invoke(a, new object[] { tiempo, v });
        }
    }
}
