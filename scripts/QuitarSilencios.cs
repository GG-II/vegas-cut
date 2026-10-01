// QuitarSilencios.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Detecta los silencios de una o varias pistas de voz (hay voz si suena
// cualquiera) y los elimina, silencia o marca en la linea de tiempo.
//
// Como funciona:
//   1. Vegas renderiza a un WAV temporal cada pista elegida (las demas se
//      silencian durante el render y se restauran despues). Asi se analiza el
//      audio tal como suena, con sus efectos, sin depender de ffmpeg.
//   2. Se mide el volumen cada 10 ms. Cada pista recibe su propio umbral
//      (metodo de Otsu) y se buscan los tramos donde todas estan bajo el suyo.
//   3. La ventana muestra la forma de onda con los silencios y permite ajustar
//      umbral, duraciones y margenes viendo el resultado al instante.
//   4. Al aplicar, todo queda en un solo paso de deshacer (Ctrl+Z).
//
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
// Los textos con acentos usan escapes \u para no depender de la codificacion.
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Drawing.Drawing2D;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/silencios/EntryPoint.cs ----

public class EntryPoint
{
    Vegas vegas;
    List<InfoPista> pistas;

    public void FromVegas(Vegas v)
    {
        vegas = v;
        Project proyecto = vegas.Project;
        pistas = PistasVegas.Listar(proyecto);
        if (pistas.Count == 0)
        {
            MessageBox.Show("El proyecto no tiene pistas de audio.", "Quitar silencios");
            return;
        }

        string[] nombres = new string[pistas.Count], detalles = new string[pistas.Count];
        for (int i = 0; i < pistas.Count; i++) { nombres[i] = pistas[i].Nombre; detalles[i] = pistas[i].Detalle; }

        using (VentanaSilencios ventana = new VentanaSilencios(nombres, detalles,
                   PistasVegas.SugerirVoz(pistas), PistasVegas.HaySeleccion(vegas), Analizar))
        {
            if (ventana.ShowDialog() != DialogResult.OK || ventana.Rangos.Count == 0) return;

            Dictionary<int, bool> analizadas = new Dictionary<int, bool>();
            foreach (int i in ventana.PistasElegidas) analizadas[pistas[i].Pista.Index] = true;
            List<Track> destino = new List<Track>();
            foreach (Track t in proyecto.Tracks)
                if (ventana.Ajustes.TodasLasPistas || analizadas.ContainsKey(t.Index)) destino.Add(t);

            double fps = proyecto.Video.FrameRate;
            List<Rango> rangos = Editor.AjustarAFotogramas(ventana.Rangos, fps);
            int n = rangos.Count;
            double total = 0;
            foreach (Rango r in rangos) total += r.Fin - r.Inicio;

            Modo modo = ventana.Ajustes.Modo;
            double durAntes = proyecto.Length.ToMilliseconds() / 1000.0;
            double suavizado = ventana.Ajustes.SuavizadoMs / 1000.0;
            using (UndoBlock deshacer = new UndoBlock("Quitar silencios"))
            {
                if (modo == Modo.Eliminar)
                    Editor.Eliminar(proyecto, destino, rangos, true, ventana.Ajustes.TodasLasPistas, suavizado);
                else if (modo == Modo.DejarHuecos)
                    Editor.Eliminar(proyecto, destino, rangos, false, false, suavizado);
                else if (modo == Modo.Silenciar)
                    Editor.Silenciar(proyecto, destino, rangos, suavizado);
                else
                    Editor.Marcar(proyecto, rangos);
            }

            // Si el proyecto ya estaba transcrito, sus tiempos se corrigen para
            // que sigan cuadrando con la linea de tiempo.
            string aviso = "";
            if (modo == Modo.Eliminar && ventana.Ajustes.TodasLasPistas)
                aviso = Transcripcion.RegistrarCortes(proyecto.FilePath, rangos, durAntes,
                    proyecto.Length.ToMilliseconds() / 1000.0);

            string hecho = modo == Modo.Eliminar ? "eliminados" :
                           modo == Modo.DejarHuecos ? "quitados dejando huecos" :
                           modo == Modo.Silenciar ? "silenciados" : "marcados como regiones";
            MessageBox.Show(
                n + " silencios " + hecho + " (" + Formato.Tiempo(total) + ")." + aviso + "\n\n" +
                "Si no te convence, Ctrl+Z lo deshace todo de una vez.",
                "Quitar silencios");
        }
    }

    Analisis Analizar(int indicePista, bool usarSeleccion)
    {
        double inicio, duracion;
        PistasVegas.ObtenerRango(vegas, usarSeleccion, out inicio, out duracion);
        return PistasVegas.Niveles(vegas, pistas[indicePista].Pista, inicio, duracion);
    }
}

// ---- src/comun/PistasVegas.cs ----

// =====================================================================
// Pistas de audio del proyecto y render a WAV
// =====================================================================

public class InfoPista
{
    public AudioTrack Pista;
    public string Nombre;   // corto, para botones: "A3 \u00b7 voz.wav"
    public string Detalle;  // largo, para ayudas
    public string Etiqueta; // "A3"
    public string Archivo;  // archivo mas usado en la pista
    public int Eventos;
}

public static class PistasVegas
{
    public static List<InfoPista> Listar(Project proyecto)
    {
        List<InfoPista> lista = new List<InfoPista>();
        foreach (Track t in proyecto.Tracks)
        {
            AudioTrack a = t as AudioTrack;
            if (a == null) continue;
            InfoPista p = new InfoPista();
            int flujo;
            ArchivoPrincipal(a, out p.Archivo, out p.Eventos, out flujo);
            string detalle = !String.IsNullOrEmpty(a.Name) ? a.Name : p.Archivo ?? "vac\u00eda";
            string corto = detalle.Length > 22 ? detalle.Substring(0, 21) + "\u2026" : detalle;
            if (String.IsNullOrEmpty(a.Name) && flujo > 0) corto += " (audio " + (flujo + 1) + ")";
            p.Pista = a;
            p.Etiqueta = "A" + (a.Index + 1);
            p.Nombre = p.Etiqueta + " \u00b7 " + corto;
            p.Detalle = "Pista " + (a.Index + 1) + ": " + detalle +
                (flujo > 0 ? " (audio " + (flujo + 1) + ")" : "") + " \u00b7 " + p.Eventos + " eventos";
            lista.Add(p);
        }
        return lista;
    }

    // La pista de voz probable: la que tenga un archivo "mejorada" o, si no
    // hay, la que tenga mas eventos.
    public static int SugerirVoz(List<InfoPista> pistas)
    {
        int sugerida = 0, mejor = -1;
        for (int i = 0; i < pistas.Count; i++)
        {
            InfoPista p = pistas[i];
            int puntos = p.Eventos + (p.Archivo != null && p.Archivo.ToLowerInvariant().Contains("mejorada") ? 100000 : 0);
            if (puntos > mejor) { mejor = puntos; sugerida = i; }
        }
        return sugerida;
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
    public static int IndiceFlujo(Take toma)
    {
        try
        {
            object flujo = toma.GetType().GetProperty("MediaStream").GetValue(toma, null);
            object indice = flujo.GetType().GetProperty("Index").GetValue(flujo, null);
            return Convert.ToInt32(indice);
        }
        catch { return 0; }
    }

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Eventos de la pista con su archivo, para la transcripcion.
    public static List<Fuente> Fuentes(Track pista)
    {
        List<Fuente> r = new List<Fuente>();
        foreach (TrackEvent e in pista.Events)
        {
            Take toma = e.ActiveTake;
            if (toma == null || toma.Media == null || toma.Media.IsGenerated() || String.IsNullOrEmpty(toma.Media.FilePath)) continue;
            Fuente f = new Fuente();
            f.Inicio = S(e.Start); f.Fin = S(e.End);
            f.Desde = S(toma.Offset); f.Velocidad = e.PlaybackRate;
            f.Media = toma.Media.FilePath;
            f.Flujo = IndiceFlujo(toma);
            r.Add(f);
        }
        r.Sort(delegate (Fuente a, Fuente b) { return a.Inicio.CompareTo(b.Inicio); });
        return r;
    }

    // Donde suena ahora ese segundo de ese archivo (y flujo): pista e instante
    // de cada evento de audio que lo contiene.
    public class Lugar { public Track Pista; public double Tiempo, Velocidad; }

    public static List<Lugar> Donde(Project p, string media, int flujo, double segundo)
    {
        List<Lugar> r = new List<Lugar>();
        foreach (Track pista in p.Tracks)
        {
            if (!pista.IsAudio()) continue;
            foreach (TrackEvent e in pista.Events)
            {
                Take toma = e.ActiveTake;
                if (toma == null || toma.Media == null || e.Mute ||
                    !String.Equals(toma.Media.FilePath, media, StringComparison.OrdinalIgnoreCase) || IndiceFlujo(toma) != flujo) continue;
                double desde = S(toma.Offset), largo = (S(e.End) - S(e.Start)) * e.PlaybackRate;
                if (segundo < desde - 0.0005 || segundo >= desde + largo - 0.0005) continue;
                Lugar l = new Lugar();
                l.Pista = pista; l.Velocidad = e.PlaybackRate;
                l.Tiempo = S(e.Start) + (segundo - desde) / e.PlaybackRate;
                r.Add(l);
            }
        }
        return r;
    }

    // Ubicador para la transcripcion: cada palabra se busca por su archivo y
    // segundo en los eventos de audio actuales, asi sigue cualquier edicion
    // (tambien a mano). Si un archivo se repite, gana la primera aparicion.
    public static Func<int, double, double> Ubicador(Project p, Transcripcion t)
    {
        Dictionary<string, List<double[]>> eventos = new Dictionary<string, List<double[]>>();
        foreach (Track pista in p.Tracks)
        {
            if (!pista.IsAudio()) continue;
            foreach (TrackEvent e in pista.Events)
            {
                Take toma = e.ActiveTake;
                if (toma == null || toma.Media == null || String.IsNullOrEmpty(toma.Media.FilePath)) continue;
                string clave = toma.Media.FilePath.ToLowerInvariant() + "|" + IndiceFlujo(toma);
                List<double[]> l;
                if (!eventos.TryGetValue(clave, out l)) { l = new List<double[]>(); eventos[clave] = l; }
                double desde = S(toma.Offset), largo = (S(e.End) - S(e.Start)) * e.PlaybackRate;
                l.Add(new double[] { desde, desde + largo, S(e.Start), e.PlaybackRate });
            }
        }
        return delegate (int hablante, double tiempo)
        {
            Fuente f;
            double segundo;
            if (!t.AFuente(hablante, tiempo, out f, out segundo)) return double.NaN;
            List<double[]> l;
            if (!eventos.TryGetValue(f.Media.ToLowerInvariant() + "|" + f.Flujo, out l)) return double.NaN;
            double mejor = double.NaN;
            foreach (double[] x in l)
                if (segundo >= x[0] - 0.0005 && segundo < x[1] - 0.0005)
                {
                    double ahora = x[2] + (segundo - x[0]) / x[3];
                    if (double.IsNaN(mejor) || ahora < mejor) mejor = ahora;
                }
            return mejor;
        };
    }

    public static bool HaySeleccion(Vegas vegas)
    {
        return Math.Abs(vegas.Transport.SelectionLength.ToMilliseconds()) > 1;
    }

    // Rango a procesar, en segundos: todo el proyecto o la seleccion de tiempo.
    public static void ObtenerRango(Vegas vegas, bool usarSeleccion, out double inicio, out double duracion)
    {
        if (usarSeleccion)
        {
            inicio = vegas.Transport.SelectionStart.ToMilliseconds() / 1000.0;
            duracion = vegas.Transport.SelectionLength.ToMilliseconds() / 1000.0;
            if (duracion < 0) { inicio += duracion; duracion = -duracion; }
        }
        else
        {
            inicio = 0;
            duracion = vegas.Project.Length.ToMilliseconds() / 1000.0;
        }
        if (duracion < 0.1) throw new Exception("El rango a analizar est\u00e1 vac\u00edo.");
    }

    // Renderiza solo esa pista a un WAV temporal (las demas se silencian
    // durante el render y se restauran despues). Quien llama borra el archivo.
    public static string RenderizarWav(Vegas vegas, AudioTrack pista, double inicio, double duracion)
    {
        Project proyecto = vegas.Project;
        RenderTemplate plantilla = PlantillaWav(vegas);
        string wav = Path.Combine(Path.GetTempPath(), "vegas-cut-" + Guid.NewGuid().ToString("N") + ".wav");

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
            args.Start = Timecode.FromMilliseconds(inicio * 1000);
            args.Length = Timecode.FromMilliseconds(duracion * 1000);
            RenderStatus estado = vegas.Render(args);
            if (estado != RenderStatus.Complete)
                throw new Exception("El render del audio no termin\u00f3 (" + estado + ").");
        }
        finally
        {
            foreach (Track t in proyecto.Tracks)
                if (muteAntes.ContainsKey(t.Index)) t.Mute = muteAntes[t.Index];
        }
        return wav;
    }

    // Render + niveles cada 10 ms, sin dejar archivos.
    public static Analisis Niveles(Vegas vegas, AudioTrack pista, double inicio, double duracion)
    {
        string wav = RenderizarWav(vegas, pista, inicio, duracion);
        try
        {
            Analisis a = WavNiveles.Leer(wav, Analisis.Paso);
            a.Inicio = inicio;
            return a;
        }
        finally
        {
            try { File.Delete(wav); } catch { }
        }
    }

    static RenderTemplate PlantillaWav(Vegas vegas)
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

// ---- src/comun/Editor.cs ----

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

    // Fundido corto en el audio que empieza o termina en un corte, para que
    // no se oiga un chasquido.
    static void Suavizar(List<TrackEvent> eventos, List<Rango> rangos, double segundos)
    {
        if (segundos <= 0) return;
        foreach (TrackEvent e in eventos)
        {
            if (!(e is AudioEvent)) continue;
            double ini = S(e.Start), fin = S(e.End);
            double largo = Math.Min(segundos, (fin - ini) / 2);
            foreach (Rango r in rangos)
            {
                if (Math.Abs(ini - r.Fin) < Tolerancia) e.FadeIn.Length = TC(largo);
                if (Math.Abs(fin - r.Inicio) < Tolerancia) e.FadeOut.Length = TC(largo);
            }
        }
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
    public static double PosicionTrasQuitar(double t, List<Rango> rangos)
    {
        double q = QuitadoAntesDe(t, rangos);
        foreach (Rango r in rangos)
            if (t > r.Inicio && t < r.Fin - Tolerancia) q += t - r.Inicio;
        return t - q;
    }

    // Quita los tramos de los rangos. Con "juntar" mueve lo que sigue para
    // cerrar el hueco; sin el, deja el espacio vacio.
    public static void Eliminar(Project proyecto, List<Track> pistas, List<Rango> rangos, bool juntar,
                                bool moverMarcadores, double suavizado)
    {
        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = CortarEnBordes(pista, rangos);
            List<TrackEvent> quedan = new List<TrackEvent>();
            foreach (TrackEvent e in eventos)
                if (DentroDeRango(e, rangos)) pista.Events.Remove(e); else quedan.Add(e);
            Suavizar(quedan, rangos, suavizado);
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
        Reagrupar(proyecto, pistas);

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

    // Vegas no acepta velocidades de evento mayores a 4x.
    public const double VelocidadMaxima = 4;

    // Reproduce mas rapido los tramos indicados: corta en sus bordes, sube la
    // velocidad de cada evento de adentro (y acorta su duracion en la misma
    // proporcion) y corre hacia la izquierda lo que sigue. Con
    // "silenciarAudio" el audio de esos tramos queda mudo (acelerado suena raro).
    public static void Acelerar(Project proyecto, List<Track> pistas, List<Acelerado> tramos, bool silenciarAudio,
                                bool moverMarcadores, double suavizado)
    {
        List<Rango> bordes = new List<Rango>();
        foreach (Acelerado a in tramos) bordes.Add(new Rango(a.Inicio, a.Fin));

        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = CortarEnBordes(pista, bordes);
            eventos.Sort(delegate (TrackEvent x, TrackEvent y) { return S(x.Start).CompareTo(S(y.Start)); });

            // De izquierda a derecha: primero se acorta el evento y luego se
            // mueve, asi nunca se encima con el siguiente.
            foreach (TrackEvent e in eventos)
            {
                double ini = S(e.Start), fin = S(e.End), medio = (ini + fin) / 2;
                foreach (Acelerado a in tramos)
                {
                    if (medio <= a.Inicio || medio >= a.Fin) continue;
                    double antes = e.PlaybackRate;
                    double despues = Math.Min(VelocidadMaxima, antes * a.Factor);
                    e.PlaybackRate = despues;
                    e.Length = TC((fin - ini) * antes / despues);
                    if (silenciarAudio && e is AudioEvent) e.Mute = true;
                    break;
                }
                double nuevo = Acelerado.Posicion(ini, tramos);
                if (nuevo < ini - Tolerancia) e.Start = TC(nuevo);
            }

            // Fundido corto donde el audio normal se junta con el acelerado.
            if (suavizado > 0 && pista.IsAudio())
            {
                List<Rango> nuevosBordes = new List<Rango>();
                foreach (Acelerado a in tramos)
                    nuevosBordes.Add(new Rango(Acelerado.Posicion(a.Inicio, tramos), Acelerado.Posicion(a.Fin, tramos)));
                List<TrackEvent> todos = new List<TrackEvent>();
                foreach (TrackEvent e in pista.Events) todos.Add(e);
                Suavizar(todos, nuevosBordes, suavizado);
                // Los bordes de adentro del tramo tambien llevan fundido.
                List<Rango> invertidos = new List<Rango>();
                foreach (Rango r in nuevosBordes) invertidos.Add(new Rango(r.Fin, r.Inicio));
                Suavizar(todos, invertidos, suavizado);
            }
        }
        Reagrupar(proyecto, pistas);

        if (!moverMarcadores) return;
        List<Marker> marcadores = new List<Marker>();
        foreach (Marker m in proyecto.Markers) marcadores.Add(m);
        foreach (Region m in proyecto.Regions) marcadores.Add(m);
        foreach (Marker m in marcadores)
        {
            double t = S(m.Position), nuevo = Acelerado.Posicion(t, tramos);
            if (nuevo < t - Tolerancia)
            {
                try { m.Position = TC(nuevo); } catch { }
            }
        }
    }

    public static void Silenciar(Project proyecto, List<Track> pistas, List<Rango> rangos, double suavizado)
    {
        Silenciar(pistas, rangos, suavizado);
        Reagrupar(proyecto, pistas);
    }

    public static void Silenciar(List<Track> pistas, List<Rango> rangos, double suavizado)
    {
        foreach (Track pista in pistas)
        {
            if (!pista.IsAudio()) continue;
            List<TrackEvent> eventos = CortarEnBordes(pista, rangos);
            List<TrackEvent> suenan = new List<TrackEvent>();
            foreach (TrackEvent e in eventos)
                if (DentroDeRango(e, rangos)) e.Mute = true; else suenan.Add(e);
            Suavizar(suenan, rangos, suavizado);
        }
    }

    public static void Marcar(Project proyecto, List<Rango> rangos)
    {
        foreach (Rango r in rangos)
            proyecto.Regions.Add(new Region(TC(r.Inicio), TC(r.Fin - r.Inicio), "Silencio"));
    }

    // ------------------------------------------------------------ grupos

    // Al cortar con Split, Vegas deja cada pedazo nuevo en el mismo grupo que
    // el clip original: al final todos los pedazos quedan unidos y mover o
    // borrar uno mueve o borra todos. Aqui cada grupo asi se separa en
    // grupos chicos: los eventos que se enciman en el tiempo (el video y sus
    // audios del mismo pedazo) siguen juntos. Solo se tocan grupos con dos o
    // mas eventos en la misma pista, que es la marca de este problema.
    // Devuelve cuantos pedazos quedaron en su propio grupo.
    public static int Reagrupar(Project proyecto, IEnumerable<Track> pistas)
    {
        // Dictionary y no HashSet: Vegas compila sin System.Core.
        Dictionary<string, bool> vistos = new Dictionary<string, bool>();
        int separados = 0;
        foreach (Track pista in pistas)
        {
            List<TrackEvent> eventos = new List<TrackEvent>();
            foreach (TrackEvent e in pista.Events) eventos.Add(e);
            foreach (TrackEvent e in eventos)
            {
                if (vistos.ContainsKey(Clave(e))) continue;
                TrackEventGroup grupo = null;
                try { if (e.IsGrouped) grupo = e.Group; } catch { }
                if (grupo == null) continue;

                List<TrackEvent> miembros = new List<TrackEvent>();
                foreach (TrackEvent m in grupo) miembros.Add(m);
                Dictionary<int, bool> pistasDelGrupo = new Dictionary<int, bool>();
                bool roto = false;
                foreach (TrackEvent m in miembros)
                {
                    vistos[Clave(m)] = true;
                    if (pistasDelGrupo.ContainsKey(m.Track.Index)) roto = true;
                    pistasDelGrupo[m.Track.Index] = true;
                }
                if (!roto) continue;

                List<List<TrackEvent>> partes = Partes(miembros);
                // La primera parte se queda en el grupo original.
                for (int i = 1; i < partes.Count; i++)
                {
                    foreach (TrackEvent m in partes[i])
                        try { grupo.Remove(m); } catch { }
                    if (partes[i].Count > 1)
                    {
                        TrackEventGroup nuevo = NuevoGrupo(proyecto);
                        foreach (TrackEvent m in partes[i]) nuevo.Add(m);
                    }
                    separados++;
                }
            }
        }
        return separados;
    }

    public static int Reagrupar(Project proyecto)
    {
        return Reagrupar(proyecto, proyecto.Tracks);
    }

    // Segun la version de Vegas el grupo se crea sin argumentos o con el
    // proyecto; por reflexion sirve para ambas.
    public static TrackEventGroup NuevoGrupo(Project proyecto)
    {
        TrackEventGroup g;
        try { g = (TrackEventGroup)Activator.CreateInstance(typeof(TrackEventGroup)); }
        catch { g = (TrackEventGroup)Activator.CreateInstance(typeof(TrackEventGroup), proyecto); }
        proyecto.Groups.Add(g);
        return g;
    }

    static string Clave(TrackEvent e)
    {
        return e.Track.Index + ":" + Math.Round(e.Start.ToMilliseconds());
    }

    // Eventos que se enciman en el tiempo van juntos.
    static List<List<TrackEvent>> Partes(List<TrackEvent> eventos)
    {
        eventos.Sort(delegate (TrackEvent a, TrackEvent b) { return S(a.Start).CompareTo(S(b.Start)); });
        List<List<TrackEvent>> partes = new List<List<TrackEvent>>();
        double fin = double.MinValue;
        foreach (TrackEvent e in eventos)
        {
            if (partes.Count == 0 || S(e.Start) >= fin - 0.002)
            {
                partes.Add(new List<TrackEvent>());
                fin = S(e.End);
            }
            else fin = Math.Max(fin, S(e.End));
            partes[partes.Count - 1].Add(e);
        }
        return partes;
    }
}

// ---- src/comun/Audio.cs ----

// =====================================================================
// Analisis de audio y deteccion (sin dependencias de Vegas)
// =====================================================================

public struct Rango
{
    public double Inicio, Fin; // segundos en la linea de tiempo
    public Rango(double inicio, double fin) { Inicio = inicio; Fin = fin; }
}

// Tramo que se reproduce mas rapido (Factor 2 = el doble de rapido).
public class Acelerado
{
    public double Inicio, Fin, Factor;
    public Acelerado(double inicio, double fin, double factor) { Inicio = inicio; Fin = fin; Factor = factor; }

    public double Ahorro { get { return (Fin - Inicio) * (1 - 1 / Factor); } }

    // Nueva posicion de un instante despues de acelerar los tramos.
    public static double Posicion(double t, List<Acelerado> tramos)
    {
        double ahorro = 0;
        foreach (Acelerado a in tramos)
        {
            if (t >= a.Fin - 1e-6) ahorro += a.Ahorro;
            else if (t > a.Inicio) ahorro += (t - a.Inicio) * (1 - 1 / a.Factor);
        }
        return t - ahorro;
    }
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


public static class Formato
{
    public static string Tiempo(double s)
    {
        if (s < 0) s = 0;
        int t = (int)Math.Round(s);
        if (t >= 3600) return (t / 3600) + ":" + ((t / 60) % 60).ToString("00") + ":" + (t % 60).ToString("00");
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    // Con decimas: 1:02.5
    public static string TiempoPreciso(double s)
    {
        if (s < 0) s = 0;
        int d = (int)Math.Round(s * 10);
        int t = d / 10;
        string r = ((t / 60) % 60).ToString(t >= 3600 ? "00" : "0") + ":" + (t % 60).ToString("00") + "." + (d % 10);
        return t >= 3600 ? (t / 3600) + ":" + r : r;
    }
}

// ---- src/comun/Json.cs ----

// =====================================================================
// JSON minimo (Vegas no trae una libreria de JSON)
//   Objeto -> Dictionary<string, object>, lista -> List<object>,
//   numero -> double, texto -> string, true/false -> bool, null -> null.
// =====================================================================

public static class Json
{
    // ------------------------------------------------------------ escribir

    public static string Escribir(object valor) { return Escribir(valor, true); }

    public static string Escribir(object valor, bool sangria)
    {
        StringBuilder sb = new StringBuilder();
        Valor(sb, valor, 0, sangria);
        return sb.ToString();
    }

    static void Valor(StringBuilder sb, object v, int nivel, bool sangria)
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
        if (v.GetType().IsPrimitive) { sb.Append(Convert.ToString(v, CultureInfo.InvariantCulture)); return; }
        if (v is Enum) { Cadena(sb, v.ToString()); return; }

        IDictionary dic = v as IDictionary;
        if (dic != null)
        {
            if (dic.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            bool primero = true;
            foreach (DictionaryEntry e in dic)
            {
                if (!primero) sb.Append(',');
                primero = false;
                Salto(sb, nivel + 1, sangria);
                Cadena(sb, e.Key.ToString());
                sb.Append(sangria ? ": " : ":");
                Valor(sb, e.Value, nivel + 1, sangria);
            }
            Salto(sb, nivel, sangria);
            sb.Append('}');
            return;
        }

        IEnumerable lista = v as IEnumerable;
        if (lista != null)
        {
            // Listas de numeros en una sola linea (niveles de sonido): mucho mas compacto.
            bool simple = true, vacia = true;
            foreach (object o in lista) { vacia = false; if (o is IDictionary || (o is IEnumerable && !(o is string))) { simple = false; break; } }
            if (vacia) { sb.Append("[]"); return; }
            sb.Append('[');
            bool primero = true;
            foreach (object o in lista)
            {
                if (!primero) sb.Append(',');
                primero = false;
                if (!simple) Salto(sb, nivel + 1, sangria);
                Valor(sb, o, nivel + 1, sangria);
            }
            if (!simple) Salto(sb, nivel, sangria);
            sb.Append(']');
            return;
        }

        Cadena(sb, v.ToString());
    }

    static void Salto(StringBuilder sb, int nivel, bool sangria)
    {
        if (!sangria) return;
        sb.Append('\n');
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

    // --------------------------------------------------------------- leer

    public static object Leer(string texto)
    {
        int i = 0;
        object v = LeerValor(texto, ref i);
        Espacios(texto, ref i);
        if (i < texto.Length) throw new FormatException("JSON con texto de sobra en la posici\u00f3n " + i);
        return v;
    }

    static void Espacios(string s, ref int i)
    {
        while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
    }

    static Exception Error(string s, int i, string que)
    {
        return new FormatException("JSON inv\u00e1lido (" + que + ") en la posici\u00f3n " + i);
    }

    static object LeerValor(string s, ref int i)
    {
        Espacios(s, ref i);
        if (i >= s.Length) throw Error(s, i, "fin inesperado");
        char c = s[i];
        if (c == '{') return LeerObjeto(s, ref i);
        if (c == '[') return LeerLista(s, ref i);
        if (c == '"') return LeerCadena(s, ref i);
        if (c == 't' && Sigue(s, i, "true")) { i += 4; return true; }
        if (c == 'f' && Sigue(s, i, "false")) { i += 5; return false; }
        if (c == 'n' && Sigue(s, i, "null")) { i += 4; return null; }
        if (c == '-' || char.IsDigit(c)) return LeerNumero(s, ref i);
        throw Error(s, i, "car\u00e1cter '" + c + "'");
    }

    static bool Sigue(string s, int i, string palabra)
    {
        return String.CompareOrdinal(s, i, palabra, 0, palabra.Length) == 0;
    }

    static Dictionary<string, object> LeerObjeto(string s, ref int i)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        i++; // {
        Espacios(s, ref i);
        if (i < s.Length && s[i] == '}') { i++; return d; }
        while (true)
        {
            Espacios(s, ref i);
            if (i >= s.Length || s[i] != '"') throw Error(s, i, "se esperaba una clave");
            string clave = LeerCadena(s, ref i);
            Espacios(s, ref i);
            if (i >= s.Length || s[i] != ':') throw Error(s, i, "se esperaba ':'");
            i++;
            d[clave] = LeerValor(s, ref i);
            Espacios(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == '}') { i++; return d; }
            throw Error(s, i, "se esperaba ',' o '}'");
        }
    }

    static List<object> LeerLista(string s, ref int i)
    {
        List<object> l = new List<object>();
        i++; // [
        Espacios(s, ref i);
        if (i < s.Length && s[i] == ']') { i++; return l; }
        while (true)
        {
            l.Add(LeerValor(s, ref i));
            Espacios(s, ref i);
            if (i < s.Length && s[i] == ',') { i++; continue; }
            if (i < s.Length && s[i] == ']') { i++; return l; }
            throw Error(s, i, "se esperaba ',' o ']'");
        }
    }

    static string LeerCadena(string s, ref int i)
    {
        StringBuilder sb = new StringBuilder();
        i++; // "
        while (i < s.Length)
        {
            char c = s[i++];
            if (c == '"') return sb.ToString();
            if (c != '\\') { sb.Append(c); continue; }
            if (i >= s.Length) break;
            char e = s[i++];
            switch (e)
            {
                case 'n': sb.Append('\n'); break;
                case 'r': sb.Append('\r'); break;
                case 't': sb.Append('\t'); break;
                case 'b': sb.Append('\b'); break;
                case 'f': sb.Append('\f'); break;
                case 'u':
                    if (i + 4 > s.Length) throw Error(s, i, "escape \\u incompleto");
                    sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                default: sb.Append(e); break; // \" \\ \/
            }
        }
        throw Error(s, i, "texto sin cerrar");
    }

    static double LeerNumero(string s, ref int i)
    {
        int ini = i;
        if (s[i] == '-') i++;
        while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
        return double.Parse(s.Substring(ini, i - ini), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // ----------------------------------------------------- acceso comodo

    public static Dictionary<string, object> Obj(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        return d != null && d.TryGetValue(clave, out v) ? v as Dictionary<string, object> : null;
    }

    public static List<object> Lista(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        List<object> l = d != null && d.TryGetValue(clave, out v) ? v as List<object> : null;
        return l ?? new List<object>();
    }

    public static string Texto(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        if (d == null || !d.TryGetValue(clave, out v) || v == null) return "";
        return v is string ? (string)v : Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    public static double Numero(object o, string clave, double siNo)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        if (d == null || !d.TryGetValue(clave, out v) || v == null) return siNo;
        if (v is double) return (double)v;
        double r;
        return double.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), NumberStyles.Float,
            CultureInfo.InvariantCulture, out r) ? r : siNo;
    }
}

// ---- src/comun/Transcripcion.cs ----

// =====================================================================
// Transcripcion del proyecto (<proyecto>.vegascut.json junto al .veg)
//
// Guarda los tiempos tal como estaban al transcribir y una lista de los
// cortes que hicieron las herramientas despues. Asi los tiempos se pueden
// llevar a la linea de tiempo actual, y si deshaces un corte (Ctrl+Z) se
// nota porque la duracion del proyecto vuelve a la de antes.
// =====================================================================

public class Palabra
{
    public double Inicio, Fin, Prob;
    public string Texto;
}

public class Segmento
{
    public int Hablante;
    public double Inicio, Fin;
    public string Texto;
    public List<Palabra> Palabras = new List<Palabra>();
}

public class Hablante
{
    public string Etiqueta;  // "A11"
    public string Nombre;    // como se llama la persona (editable)
    public string Archivo;
    public bool Voz;         // true: se transcribio; false: solo niveles (juego, musica)
    public float[] Nivel;    // dB RMS por segundo
    public float[] Pico;     // dB maximo por segundo
    public List<Fuente> Fuentes = new List<Fuente>(); // de donde salia cada parte al transcribir
}

// Un evento de la pista al transcribir: que archivo (y flujo de audio) sonaba
// de Inicio a Fin y desde que segundo del archivo. Con esto una palabra se
// puede encontrar en la linea de tiempo aunque despues edites a mano.
public class Fuente
{
    public double Inicio, Fin, Desde, Velocidad = 1;
    public string Media = "";
    public int Flujo;
}

public class Edicion
{
    public List<Rango> Quitados = new List<Rango>();
    public List<Acelerado> Acelerados = new List<Acelerado>();
    public double Antes, Despues; // duracion del proyecto
}

public class Transcripcion
{
    public string Proyecto = "", Creada = "", Idioma = "es", Modelo = "";
    public double Inicio, Duracion;      // rango transcrito (tiempos originales)
    public double DuracionProyecto;      // al transcribir
    public List<Hablante> Hablantes = new List<Hablante>();
    public List<Segmento> Segmentos = new List<Segmento>();
    public List<Edicion> Ediciones = new List<Edicion>();

    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut.json");
    }

    // --------------------------------------------------- tiempos actuales

    // Si esta puesto, lleva (hablante, tiempo original) a la linea de tiempo
    // actual buscando el archivo y segundo de cada palabra (fuentes): asi la
    // transcripcion sigue cualquier edicion, tambien las hechas a mano. Lo
    // ponen las herramientas que tienen el proyecto de Vegas a mano.
    public Func<int, double, double> Ubicador;

    public double Mapear(int hablante, double t)
    {
        if (Ubicador != null && hablante >= 0 && hablante < Hablantes.Count && Hablantes[hablante].Fuentes.Count > 0)
            return Ubicador(hablante, t);
        return Mapear(t);
    }

    // Lleva un tiempo original a la linea de tiempo actual. Devuelve NaN si
    // ese instante fue cortado.
    public double Mapear(double t)
    {
        foreach (Edicion e in Ediciones)
        {
            if (e.Acelerados.Count > 0) t = Acelerado.Posicion(t, e.Acelerados);
            double q = 0;
            foreach (Rango r in e.Quitados)
            {
                if (t >= r.Fin - 1e-6) q += r.Fin - r.Inicio;
                else if (t > r.Inicio + 1e-6) return double.NaN;
            }
            t -= q;
        }
        return t;
    }

    // Archivo, flujo y segundo del archivo que sonaba en el instante
    // original t en la pista de ese hablante.
    public bool AFuente(int hablante, double t, out Fuente f, out double segundo)
    {
        f = null; segundo = 0;
        if (hablante < 0 || hablante >= Hablantes.Count) return false;
        foreach (Fuente x in Hablantes[hablante].Fuentes)
            if (t >= x.Inicio - 1e-6 && t < x.Fin - 1e-6)
            {
                f = x;
                segundo = x.Desde + (t - x.Inicio) * x.Velocidad;
                return true;
            }
        return false;
    }

    // Frases que Whisper inventa en los silencios (vienen de los subtitulos de
    // YouTube con los que se entreno). No se le mandan a la IA.
    static readonly string[] Inventadas = { "suscribeteacanal", "suscribeteanuestrocanal", "suscribete", "graciasporver",
        "subtitulosrealizadosporlacomunidaddeamaraorg", "subtitulosporlacomunidaddeamaraorg", "amaraorg",
        "noolvidesdesuscribirte", "dalelike" };

    public static bool Alucinacion(string texto)
    {
        StringBuilder sb = new StringBuilder();
        foreach (char c in (texto ?? "").ToLowerInvariant().Normalize(NormalizationForm.FormD))
            if (c < 128 && char.IsLetterOrDigit(c)) sb.Append(c);
        string n = sb.ToString().Replace("suscribetealcanal", "suscribeteacanal");
        if (n.Length == 0) return false;
        foreach (string x in Inventadas)
            if (n == x || (x.Length >= 10 && n.Contains(x) && n.Length <= x.Length + 12)) return true;
        return false;
    }

    public bool TieneFuentes
    {
        get { foreach (Hablante h in Hablantes) if (h.Fuentes.Count > 0) return true; return false; }
    }

    // Segmentos con tiempos de la linea de tiempo actual, sin lo cortado.
    public List<Segmento> SegmentosActuales()
    {
        List<Segmento> r = new List<Segmento>();
        foreach (Segmento s in Segmentos)
        {
            if (Alucinacion(s.Texto)) continue;
            Segmento n = new Segmento();
            n.Hablante = s.Hablante;
            StringBuilder texto = new StringBuilder();
            if (s.Palabras.Count > 0)
            {
                foreach (Palabra p in s.Palabras)
                {
                    double a = Mapear(s.Hablante, p.Inicio), b = Mapear(s.Hablante, p.Fin);
                    if (double.IsNaN(a) && double.IsNaN(b)) continue;
                    if (double.IsNaN(a)) a = b - Math.Min(0.2, p.Fin - p.Inicio);
                    if (double.IsNaN(b)) b = a + Math.Min(0.2, p.Fin - p.Inicio);
                    Palabra q = new Palabra();
                    q.Inicio = a; q.Fin = b; q.Prob = p.Prob; q.Texto = p.Texto;
                    n.Palabras.Add(q);
                    texto.Append(p.Texto);
                }
                if (n.Palabras.Count == 0) continue;
                n.Inicio = n.Palabras[0].Inicio;
                n.Fin = n.Palabras[n.Palabras.Count - 1].Fin;
                n.Texto = texto.ToString().Trim();
            }
            else
            {
                n.Inicio = Mapear(s.Hablante, s.Inicio); n.Fin = Mapear(s.Hablante, s.Fin); n.Texto = s.Texto;
                if (double.IsNaN(n.Inicio) || double.IsNaN(n.Fin)) continue;
            }
            r.Add(n);
        }
        r.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
        return r;
    }

    public double DuracionEsperada
    {
        get { return Ediciones.Count > 0 ? Ediciones[Ediciones.Count - 1].Despues : DuracionProyecto; }
    }

    // Compara con la duracion actual del proyecto. Si deshiciste cortes
    // (Ctrl+Z), los quita de la lista. Devuelve "" si todo cuadra o un aviso.
    public string Sincronizar(double duracionActual)
    {
        const double tol = 0.05;
        bool cambio = false;
        while (Ediciones.Count > 0 && Math.Abs(DuracionEsperada - duracionActual) > tol &&
               Math.Abs(Ediciones[Ediciones.Count - 1].Antes - duracionActual) <= tol)
        {
            Ediciones.RemoveAt(Ediciones.Count - 1);
            cambio = true;
        }
        if (Math.Abs(DuracionEsperada - duracionActual) <= tol)
            return cambio ? "Se detect\u00f3 un Ctrl+Z: la transcripci\u00f3n se ajust\u00f3." : "";
        return "El proyecto cambi\u00f3 desde la transcripci\u00f3n (dura " + Formato.Tiempo(duracionActual) +
               ", se esperaba " + Formato.Tiempo(DuracionEsperada) + "). Si editaste a mano, los tiempos " +
               "pueden no cuadrar: vuelve a transcribir para m\u00e1s precisi\u00f3n.";
    }

    // La llaman las herramientas que cortan (Quitar silencios, Momentos).
    public static string RegistrarCortes(string veg, List<Rango> quitados, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Quitados.AddRange(quitados);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    public static string RegistrarAceleracion(string veg, List<Acelerado> tramos, double antes, double despues)
    {
        Edicion e = new Edicion();
        e.Acelerados.AddRange(tramos);
        e.Antes = antes;
        e.Despues = despues;
        return RegistrarEdicion(veg, e);
    }

    static string RegistrarEdicion(string veg, Edicion e)
    {
        string ruta = RutaPara(veg);
        if (ruta == null || !File.Exists(ruta)) return "";
        try
        {
            Transcripcion t = Cargar(ruta);
            t.Sincronizar(e.Antes);
            t.Ediciones.Add(e);
            t.Guardar(ruta);
            return "\nLa transcripci\u00f3n tambi\u00e9n se ajust\u00f3 a los cortes.";
        }
        catch (Exception ex)
        {
            return "\nNo se pudo ajustar la transcripci\u00f3n: " + ex.Message;
        }
    }

    // ------------------------------------------------- niveles por segundo

    public static void NivelesPorSegundo(Analisis a, out float[] nivel, out float[] pico)
    {
        int porSegundo = (int)Math.Round(1 / Analisis.Paso);
        int n = (a.Db.Length + porSegundo - 1) / porSegundo;
        nivel = new float[n];
        pico = new float[n];
        for (int s = 0; s < n; s++)
        {
            double energia = 0;
            float max = -100;
            int cuantos = 0;
            for (int i = s * porSegundo; i < Math.Min(a.Db.Length, (s + 1) * porSegundo); i++)
            {
                energia += Math.Pow(10, a.Db[i] / 10.0);
                if (a.Db[i] > max) max = a.Db[i];
                cuantos++;
            }
            nivel[s] = cuantos > 0 ? (float)Math.Round(10 * Math.Log10(Math.Max(1e-10, energia / cuantos))) : -100;
            pico[s] = (float)Math.Round(max);
        }
    }

    // ------------------------------------------------------ Whisper (JSON)

    // Agrega los segmentos de la salida JSON de Whisper (tiempos relativos al
    // WAV) desplazados al inicio del rango.
    public void AgregarWhisper(string json, int hablante, double desplazamiento)
    {
        object o = Json.Leer(json);
        foreach (object s in Json.Lista(o, "segments"))
        {
            Segmento seg = new Segmento();
            seg.Hablante = hablante;
            seg.Inicio = Json.Numero(s, "start", 0) + desplazamiento;
            seg.Fin = Json.Numero(s, "end", 0) + desplazamiento;
            seg.Texto = Json.Texto(s, "text").Trim();
            foreach (object w in Json.Lista(s, "words"))
            {
                Palabra p = new Palabra();
                p.Inicio = Json.Numero(w, "start", seg.Inicio - desplazamiento) + desplazamiento;
                p.Fin = Json.Numero(w, "end", seg.Fin - desplazamiento) + desplazamiento;
                p.Prob = Json.Numero(w, "probability", Json.Numero(w, "prob", 1));
                p.Texto = Json.Texto(w, "word");
                if (p.Texto.Length == 0) p.Texto = Json.Texto(w, "text");
                seg.Palabras.Add(p);
            }
            if (seg.Texto.Length > 0 || seg.Palabras.Count > 0) Segmentos.Add(seg);
        }
        Segmentos.Sort(delegate (Segmento x, Segmento y) { return x.Inicio.CompareTo(y.Inicio); });
    }

    // ---------------------------------------------------- guardar / cargar

    static double R(double v) { return Math.Round(v, 3); }

    public void Guardar(string ruta)
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-transcripcion";
        d["version"] = 1;
        d["proyecto"] = Proyecto;
        d["creada"] = Creada;
        d["idioma"] = Idioma;
        d["modelo"] = Modelo;
        d["inicio"] = R(Inicio);
        d["duracion"] = R(Duracion);
        d["duracionProyecto"] = R(DuracionProyecto);

        List<object> hs = new List<object>();
        foreach (Hablante h in Hablantes)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["etiqueta"] = h.Etiqueta;
            x["nombre"] = h.Nombre;
            x["archivo"] = h.Archivo;
            x["voz"] = h.Voz;
            x["nivel"] = h.Nivel ?? new float[0];
            x["pico"] = h.Pico ?? new float[0];
            List<object> fs = new List<object>();
            // Fuentes compactas: [inicio, fin, desde, velocidad, flujo, archivo]
            foreach (Fuente f in h.Fuentes)
                fs.Add(new List<object> { R(f.Inicio), R(f.Fin), R(f.Desde), Math.Round(f.Velocidad, 4), f.Flujo, f.Media });
            x["fuentes"] = fs;
            hs.Add(x);
        }
        d["hablantes"] = hs;

        List<object> ss = new List<object>();
        foreach (Segmento s in Segmentos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["h"] = s.Hablante;
            x["inicio"] = R(s.Inicio);
            x["fin"] = R(s.Fin);
            x["texto"] = s.Texto;
            List<object> ps = new List<object>();
            // Palabras compactas: [inicio, fin, probabilidad, texto]
            foreach (Palabra p in s.Palabras)
                ps.Add(new List<object> { R(p.Inicio), R(p.Fin), Math.Round(p.Prob, 2), p.Texto });
            x["palabras"] = ps;
            ss.Add(x);
        }
        d["segmentos"] = ss;

        List<object> es = new List<object>();
        foreach (Edicion e in Ediciones)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["antes"] = R(e.Antes);
            x["despues"] = R(e.Despues);
            List<object> qs = new List<object>();
            foreach (Rango r in e.Quitados) qs.Add(new List<object> { R(r.Inicio), R(r.Fin) });
            x["quitados"] = qs;
            if (e.Acelerados.Count > 0)
            {
                List<object> acs = new List<object>();
                foreach (Acelerado a in e.Acelerados) acs.Add(new List<object> { R(a.Inicio), R(a.Fin), a.Factor });
                x["acelerados"] = acs;
            }
            es.Add(x);
        }
        d["ediciones"] = es;

        File.WriteAllText(ruta, Json.Escribir(d), new UTF8Encoding(false));
    }

    public static Transcripcion Cargar(string ruta)
    {
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        if (Json.Texto(o, "formato") != "vegas-cut-transcripcion")
            throw new Exception("El archivo no es una transcripci\u00f3n de vegas-cut.");
        Transcripcion t = new Transcripcion();
        t.Proyecto = Json.Texto(o, "proyecto");
        t.Creada = Json.Texto(o, "creada");
        t.Idioma = Json.Texto(o, "idioma");
        t.Modelo = Json.Texto(o, "modelo");
        t.Inicio = Json.Numero(o, "inicio", 0);
        t.Duracion = Json.Numero(o, "duracion", 0);
        t.DuracionProyecto = Json.Numero(o, "duracionProyecto", 0);

        foreach (object x in Json.Lista(o, "hablantes"))
        {
            Hablante h = new Hablante();
            h.Etiqueta = Json.Texto(x, "etiqueta");
            h.Nombre = Json.Texto(x, "nombre");
            h.Archivo = Json.Texto(x, "archivo");
            object voz;
            Dictionary<string, object> dx = (Dictionary<string, object>)x;
            h.Voz = dx.TryGetValue("voz", out voz) && voz is bool && (bool)voz;
            h.Nivel = Numeros(Json.Lista(x, "nivel"));
            h.Pico = Numeros(Json.Lista(x, "pico"));
            foreach (object q in Json.Lista(x, "fuentes"))
            {
                List<object> l = q as List<object>;
                if (l == null || l.Count < 6) continue;
                Fuente f = new Fuente();
                f.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                f.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                f.Desde = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                f.Velocidad = Convert.ToDouble(l[3], CultureInfo.InvariantCulture);
                f.Flujo = Convert.ToInt32(l[4], CultureInfo.InvariantCulture);
                f.Media = l[5] as string ?? "";
                h.Fuentes.Add(f);
            }
            t.Hablantes.Add(h);
        }

        foreach (object x in Json.Lista(o, "segmentos"))
        {
            Segmento s = new Segmento();
            s.Hablante = (int)Json.Numero(x, "h", 0);
            s.Inicio = Json.Numero(x, "inicio", 0);
            s.Fin = Json.Numero(x, "fin", 0);
            s.Texto = Json.Texto(x, "texto");
            foreach (object p in Json.Lista(x, "palabras"))
            {
                List<object> l = p as List<object>;
                if (l == null || l.Count < 4) continue;
                Palabra w = new Palabra();
                w.Inicio = Convert.ToDouble(l[0], CultureInfo.InvariantCulture);
                w.Fin = Convert.ToDouble(l[1], CultureInfo.InvariantCulture);
                w.Prob = Convert.ToDouble(l[2], CultureInfo.InvariantCulture);
                w.Texto = l[3] as string ?? "";
                s.Palabras.Add(w);
            }
            t.Segmentos.Add(s);
        }

        foreach (object x in Json.Lista(o, "ediciones"))
        {
            Edicion e = new Edicion();
            e.Antes = Json.Numero(x, "antes", 0);
            e.Despues = Json.Numero(x, "despues", 0);
            foreach (object q in Json.Lista(x, "quitados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 2)
                    e.Quitados.Add(new Rango(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                             Convert.ToDouble(l[1], CultureInfo.InvariantCulture)));
            }
            foreach (object q in Json.Lista(x, "acelerados"))
            {
                List<object> l = q as List<object>;
                if (l != null && l.Count >= 3)
                    e.Acelerados.Add(new Acelerado(Convert.ToDouble(l[0], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[1], CultureInfo.InvariantCulture),
                                                   Convert.ToDouble(l[2], CultureInfo.InvariantCulture)));
            }
            t.Ediciones.Add(e);
        }
        return t;
    }

    static float[] Numeros(List<object> l)
    {
        float[] r = new float[l.Count];
        for (int i = 0; i < l.Count; i++) r[i] = (float)Convert.ToDouble(l[i], CultureInfo.InvariantCulture);
        return r;
    }
}

// ---- src/silencios/Deteccion.cs ----

public enum Modo { Eliminar, DejarHuecos, Silenciar, Marcar }

// Valores de deteccion. Un perfil es un conjunto de estos valores con nombre.
public class Valores
{
    public int SilencioMinMs = 500;   // solo se quitan pausas mas largas
    public int HablaMinMs = 150;      // sonidos mas cortos no cuentan como voz
    public int MargenAntesMs = 150;   // pausa que queda antes de hablar
    public int MargenDespuesMs = 250; // pausa que queda al terminar de hablar
    public int PedazoMinMs = 800;     // no deja clips mas cortos que esto
    public int SuavizadoMs = 20;      // fundido del audio en cada corte
    public int Sensibilidad = 0;      // dB que se suman al umbral de cada pista

    public bool Igual(Valores o)
    {
        return SilencioMinMs == o.SilencioMinMs && HablaMinMs == o.HablaMinMs &&
               MargenAntesMs == o.MargenAntesMs && MargenDespuesMs == o.MargenDespuesMs &&
               PedazoMinMs == o.PedazoMinMs && SuavizadoMs == o.SuavizadoMs && Sensibilidad == o.Sensibilidad;
    }

    public void CopiarDe(Valores o)
    {
        SilencioMinMs = o.SilencioMinMs; HablaMinMs = o.HablaMinMs;
        MargenAntesMs = o.MargenAntesMs; MargenDespuesMs = o.MargenDespuesMs;
        PedazoMinMs = o.PedazoMinMs; SuavizadoMs = o.SuavizadoMs; Sensibilidad = o.Sensibilidad;
    }

    public string Texto()
    {
        return "silencioMin=" + SilencioMinMs + "\n" + "hablaMin=" + HablaMinMs + "\n" +
               "margenAntes=" + MargenAntesMs + "\n" + "margenDespues=" + MargenDespuesMs + "\n" +
               "pedazoMin=" + PedazoMinMs + "\n" + "suavizado=" + SuavizadoMs + "\n" +
               "sensibilidad=" + Sensibilidad + "\n";
    }

    // Devuelve true si la clave era de estos valores.
    public bool Leer(string k, string v)
    {
        int n;
        if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
        switch (k)
        {
            case "silencioMin": SilencioMinMs = n; return true;
            case "hablaMin": HablaMinMs = n; return true;
            case "margenAntes": MargenAntesMs = n; return true;
            case "margenDespues": MargenDespuesMs = n; return true;
            case "pedazoMin": PedazoMinMs = n; return true;
            case "suavizado": SuavizadoMs = n; return true;
            case "sensibilidad": Sensibilidad = n; return true;
        }
        return false;
    }

    public static string Carpeta
    {
        get
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut");
        }
    }
}

public class Ajustes : Valores
{
    public Modo Modo = Modo.Eliminar;
    public bool TodasLasPistas = true;
    public string Perfil = "Narraci\u00f3n";

    static string Ruta { get { return Path.Combine(Carpeta, "silencios.ini"); } }

    public static Ajustes Cargar()
    {
        Ajustes a = new Ajustes();
        a.CopiarDe(Perfil_.Incluidos[0]);
        try
        {
            if (!File.Exists(Ruta)) return a;
            foreach (string linea in File.ReadAllLines(Ruta))
            {
                int i = linea.IndexOf('=');
                if (i < 0) continue;
                string k = linea.Substring(0, i).Trim(), v = linea.Substring(i + 1).Trim();
                if (a.Leer(k, v)) continue;
                switch (k)
                {
                    case "modo": a.Modo = (Modo)Enum.Parse(typeof(Modo), v); break;
                    case "todas": a.TodasLasPistas = v == "1"; break;
                    case "perfil": a.Perfil = v; break;
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
            Directory.CreateDirectory(Carpeta);
            File.WriteAllText(Ruta, Texto() +
                "modo=" + Modo + "\n" +
                "todas=" + (TodasLasPistas ? "1" : "0") + "\n" +
                "perfil=" + Perfil + "\n", new UTF8Encoding(false));
        }
        catch { }
    }
}

// Perfil_ (con guion bajo) para no chocar con nombres de la API de Vegas.
public class Perfil_ : Valores
{
    public string Nombre, Descripcion;
    public bool Incluido;

    static Perfil_ Nuevo(string nombre, string descripcion, int silencio, int voz, int antes, int despues,
                         int pedazo, int suavizado, int sensibilidad)
    {
        Perfil_ p = new Perfil_();
        p.Nombre = nombre; p.Descripcion = descripcion; p.Incluido = true;
        p.SilencioMinMs = silencio; p.HablaMinMs = voz; p.MargenAntesMs = antes; p.MargenDespuesMs = despues;
        p.PedazoMinMs = pedazo; p.SuavizadoMs = suavizado; p.Sensibilidad = sensibilidad;
        return p;
    }

    // Valores pensados para cada tipo de video. En tus video ensayos las pausas
    // que quitas a mano duran 1 a 1.5 s y los pedazos 4 a 7 s.
    public static readonly Perfil_[] Incluidos = new Perfil_[]
    {
        Nuevo("Narraci\u00f3n", "Voz en off y video ensayos: quita casi todas las pausas y deja la voz fluida.",
              350, 150, 100, 180, 700, 20, 0),
        Nuevo("Tutorial", "Explicaciones con pantalla: deja respirar para que se entienda cada paso.",
              600, 150, 150, 300, 1000, 25, 0),
        Nuevo("Podcast / charla", "Conversaci\u00f3n entre varios: solo quita pausas largas y conserva las reacciones.",
              900, 200, 200, 350, 1500, 30, 0),
        Nuevo("Gameplay", "Partidas con voz: quita los silencios largos, deja que el juego respire e ignora clics de teclado.",
              1200, 250, 250, 450, 2000, 30, -3),
        Nuevo("Shorts / r\u00e1pido", "Clips cortos y din\u00e1micos: corta hasta las pausas peque\u00f1as.",
              200, 100, 50, 80, 400, 15, 2),
    };

    static string Ruta { get { return Path.Combine(Carpeta, "perfiles.ini"); } }

    // Perfiles guardados por el usuario, en formato:
    //   [Nombre]
    //   silencioMin=...
    public static List<Perfil_> CargarPropios()
    {
        List<Perfil_> lista = new List<Perfil_>();
        try
        {
            if (!File.Exists(Ruta)) return lista;
            Perfil_ actual = null;
            foreach (string l in File.ReadAllLines(Ruta, Encoding.UTF8))
            {
                string linea = l.Trim();
                if (linea.StartsWith("[") && linea.EndsWith("]"))
                {
                    actual = new Perfil_();
                    actual.Nombre = linea.Substring(1, linea.Length - 2);
                    actual.Descripcion = "Perfil guardado por ti.";
                    lista.Add(actual);
                    continue;
                }
                int i = linea.IndexOf('=');
                if (actual != null && i > 0) actual.Leer(linea.Substring(0, i).Trim(), linea.Substring(i + 1).Trim());
            }
        }
        catch { }
        return lista;
    }

    public static void GuardarPropios(List<Perfil_> propios)
    {
        try
        {
            Directory.CreateDirectory(Carpeta);
            StringBuilder sb = new StringBuilder();
            foreach (Perfil_ p in propios) sb.Append("[" + p.Nombre + "]\n" + p.Texto() + "\n");
            File.WriteAllText(Ruta, sb.ToString(), new UTF8Encoding(false));
        }
        catch { }
    }
}

public static class Detector
{
    // Umbral de una pista por el metodo de Otsu: se hace un histograma de los
    // niveles (1 dB por barra) y se busca el corte que mejor separa los dos
    // grupos, ruido de fondo y voz. Asi cada pista tiene su propio umbral
    // aunque tengan volumenes distintos.
    public static double UmbralAutomatico(float[] db)
    {
        int[] h = new int[101];
        int total = 0;
        foreach (float x in db)
        {
            if (x <= -99) continue; // silencio digital
            int i = Math.Max(0, Math.Min(100, 100 + (int)Math.Round(x)));
            h[i]++;
            total++;
        }
        if (total < 50) return -40;

        double suma = 0;
        for (int i = 0; i <= 100; i++) suma += (double)i * h[i];
        double sumaFondo = 0, mejor = -1;
        long pesoFondo = 0;
        int corte = 60;
        for (int i = 0; i <= 100; i++)
        {
            pesoFondo += h[i];
            if (pesoFondo == 0) continue;
            long pesoVoz = total - pesoFondo;
            if (pesoVoz == 0) break;
            sumaFondo += (double)i * h[i];
            double mFondo = sumaFondo / pesoFondo, mVoz = (suma - sumaFondo) / pesoVoz;
            double entre = (double)pesoFondo * pesoVoz * (mFondo - mVoz) * (mFondo - mVoz);
            if (entre > mejor) { mejor = entre; corte = i; }
        }
        // Otsu solo separa los grupos; el umbral va a la mitad entre el borde
        // alto del ruido (percentil 90 del fondo) y el borde bajo de la voz
        // (percentil 20), para no quedar pegado al ruido.
        double bordeRuido = Percentil(h, 0, corte, 0.90) - 100;
        double bordeVoz = Percentil(h, corte + 1, 100, 0.20) - 100;
        double u = Math.Max(bordeRuido + 3, (bordeRuido + bordeVoz) / 2);
        return Math.Max(-70, Math.Min(-15, Math.Round(u)));
    }

    // Percentil de las barras desde..hasta del histograma (devuelve la barra).
    static int Percentil(int[] h, int desde, int hasta, double fraccion)
    {
        long total = 0;
        for (int i = desde; i <= hasta; i++) total += h[i];
        if (total == 0) return hasta;
        long objetivo = (long)Math.Ceiling(total * fraccion), acumulado = 0;
        for (int i = desde; i <= hasta; i++)
        {
            acumulado += h[i];
            if (acumulado >= objetivo) return i;
        }
        return hasta;
    }

    public static List<Rango> Detectar(Analisis a, double umbral, Valores v)
    {
        return Detectar(new List<Analisis> { a }, new List<double> { umbral }, v);
    }

    // Hay voz en un instante si cualquier pista supera su propio umbral
    // (mas la sensibilidad general).
    public static List<Rango> Detectar(List<Analisis> pistas, List<double> umbrales, Valores v)
    {
        List<Rango> resultado = new List<Rango>();
        if (pistas.Count == 0) return resultado;
        int n = int.MaxValue;
        foreach (Analisis p in pistas) n = Math.Min(n, p.Db.Length);
        if (n == 0) return resultado;
        double paso = Analisis.Paso, inicio = pistas[0].Inicio;

        bool[] hay = new bool[n];
        for (int k = 0; k < pistas.Count; k++)
        {
            float[] db = pistas[k].Db;
            double u = umbrales[k] + v.Sensibilidad;
            for (int i = 0; i < n; i++) if (db[i] >= u) hay[i] = true;
        }

        // 1. Tramos de voz.
        List<int[]> voz = new List<int[]>();
        int j = 0;
        while (j < n)
        {
            if (hay[j])
            {
                int f = j;
                while (f < n && hay[f]) f++;
                voz.Add(new int[] { j, f });
                j = f;
            }
            else j++;
        }

        // 2. Descartar voz demasiado corta (clics, respiraciones).
        int hablaMin = (int)Math.Round(v.HablaMinMs / 1000.0 / paso);
        List<int[]> vozBuena = new List<int[]>();
        foreach (int[] t in voz) if (t[1] - t[0] >= hablaMin) vozBuena.Add(t);

        // 3. Los huecos entre voz son silencios candidatos (incluye inicio y final).
        int silMin = (int)Math.Round(v.SilencioMinMs / 1000.0 / paso);
        int antes = (int)Math.Round(v.MargenAntesMs / 1000.0 / paso);
        int despues = (int)Math.Round(v.MargenDespuesMs / 1000.0 / paso);
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
                    resultado.Add(new Rango(inicio + a0 * paso, inicio + b0 * paso));
            }
            if (k < vozBuena.Count) cursor = vozBuena[k][1];
        }

        return PedazoMinimo(resultado, inicio, inicio + n * paso, v.PedazoMinMs / 1000.0);
    }

    // Si entre dos silencios queda un clip mas corto que el minimo, no se
    // corta el segundo silencio: el clip se une con lo que sigue.
    static List<Rango> PedazoMinimo(List<Rango> rangos, double inicio, double fin, double minimo)
    {
        if (minimo <= 0) return rangos;
        List<Rango> r = new List<Rango>();
        double ultimoFin = inicio;
        foreach (Rango x in rangos)
        {
            double pedazo = x.Inicio - ultimoFin;
            if (pedazo > 0.001 && pedazo < minimo) continue;
            r.Add(x);
            ultimoFin = x.Fin;
        }
        // El ultimo clip, entre el ultimo silencio y el final.
        while (r.Count > 0)
        {
            double pedazo = fin - r[r.Count - 1].Fin;
            if (pedazo > 0.001 && pedazo < minimo) r.RemoveAt(r.Count - 1);
            else break;
        }
        return r;
    }
}

// ---- src/comun/Ui.cs ----

// =====================================================================
// Interfaz
// =====================================================================

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
    public bool DesdeCentro;
    double valor = 0;
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
        float desde = DesdeCentro ? X((Minimo + Maximo) / 2) : 8;
        if (DesdeCentro)
            using (SolidBrush b = new SolidBrush(Tema.Borde)) g.FillRectangle(b, desde - 1, cy - 7, 2, 14);
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(Math.Min(desde, x), cy - 3, Math.Max(6, Math.Abs(x - desde)), 6), 3))
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
    public string Sufijo = "ms";
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
        TextRenderer.DrawText(g, Sufijo, Tema.Pequena, new Rectangle(Width - 36, 0, 28, Height), Tema.TextoSuave,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }
}

class Combo : ComboBox
{
    public Combo() : this(false) { }

    // Editable: se puede escribir un valor que no este en la lista.
    public Combo(bool editable)
    {
        DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList;
        DrawMode = DrawMode.OwnerDrawFixed;
        FlatStyle = FlatStyle.Flat;
        BackColor = Tema.Campo;
        ForeColor = Tema.Texto;
        Font = Tema.Fuente(10f, FontStyle.Regular);
        ItemHeight = 24;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        bool sel = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (SolidBrush b = new SolidBrush(sel ? Tema.Acento : Tema.Campo)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height),
            sel ? Color.White : Tema.Texto, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
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

// Pide el nombre para guardar un perfil.
class DialogoNombre : Form
{
    TextBox caja = new TextBox();
    public string Nombre { get { return caja.Text.Trim(); } }

    public DialogoNombre(string sugerido) : this(sugerido, "Guardar perfil", "Nombre del perfil") { }

    public DialogoNombre(string sugerido, string titulo, string etiqueta)
    {
        Text = titulo;
        ClientSize = new Size(380, 150);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;

        Controls.Add(Pos(new Etiqueta(etiqueta, Tema.Seccion, Tema.Texto), 20, 16, 340, 22));
        Panel marco = new Panel();
        marco.BackColor = Tema.Campo;
        marco.Padding = new Padding(10, 8, 10, 6);
        caja.BorderStyle = BorderStyle.None;
        caja.BackColor = Tema.Campo;
        caja.ForeColor = Tema.Texto;
        caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        caja.Dock = DockStyle.Fill;
        caja.Text = sugerido;
        marco.Controls.Add(caja);
        Controls.Add(Pos(marco, 20, 46, 340, 34));

        Boton guardar = new Boton("Guardar", EstiloBoton.Primario);
        Boton cancelar = new Boton("Cancelar", EstiloBoton.Secundario);
        Controls.Add(Pos(cancelar, 150, 100, 100, 34));
        Controls.Add(Pos(guardar, 260, 100, 100, 34));
        guardar.Click += delegate { if (Nombre.Length > 0) { DialogResult = DialogResult.OK; Close(); } };
        cancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        caja.KeyDown += delegate (object s, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter && Nombre.Length > 0) { DialogResult = DialogResult.OK; Close(); }
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        };
        Shown += delegate { caja.Focus(); caja.SelectAll(); };
    }

    static Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); return c; }
}

// Campo de texto oscuro con borde redondeado.
class CampoTexto : ControlBase
{
    public TextBox Caja = new TextBox();

    public CampoTexto()
    {
        Caja.BorderStyle = BorderStyle.None;
        Caja.BackColor = Tema.Campo;
        Caja.ForeColor = Tema.Texto;
        Caja.Font = Tema.Fuente(10f, FontStyle.Regular);
        Caja.GotFocus += delegate { Invalidate(); };
        Caja.LostFocus += delegate { Invalidate(); };
        Controls.Add(Caja);
        Cursor = Cursors.IBeam;
    }

    public override string Text { get { return Caja.Text; } set { Caja.Text = value; } }

    public bool Oculto { get { return Caja.UseSystemPasswordChar; } set { Caja.UseSystemPasswordChar = value; } }

    public bool Multilinea
    {
        get { return Caja.Multiline; }
        set { Caja.Multiline = value; Caja.ScrollBars = value ? ScrollBars.Vertical : ScrollBars.None; PerformLayout(); }
    }

    protected override void OnMouseDown(MouseEventArgs e) { Caja.Focus(); base.OnMouseDown(e); }

    protected override void OnLayout(LayoutEventArgs e)
    {
        if (Caja.Multiline) Caja.SetBounds(10, 8, Width - 20, Height - 16);
        else Caja.SetBounds(12, (Height - Caja.PreferredHeight) / 2 + 1, Width - 24, Caja.PreferredHeight);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f), 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
            using (Pen pen = new Pen(Caja.Focused ? Tema.Acento : Tema.Borde)) g.DrawPath(pen, p);
        }
    }
}

// Lista oscura con casillas (ListView con encabezado dibujado a mano).
class Lista : ListView
{
    public Lista()
    {
        View = View.Details;
        FullRowSelect = true;
        CheckBoxes = true;
        HideSelection = false;
        BorderStyle = BorderStyle.None;
        BackColor = Tema.Campo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        OwnerDraw = true;
        HeaderStyle = ColumnHeaderStyle.Nonclickable;
        DoubleBuffered = true;
    }

    protected override void OnDrawColumnHeader(DrawListViewColumnHeaderEventArgs e)
    {
        using (SolidBrush b = new SolidBrush(Tema.Panel)) e.Graphics.FillRectangle(b, e.Bounds);
        TextRenderer.DrawText(e.Graphics, e.Header.Text, Tema.Pequena,
            new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 6, e.Bounds.Height), Tema.TextoSuave,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    protected override void OnDrawItem(DrawListViewItemEventArgs e) { e.DrawDefault = true; }
    protected override void OnDrawSubItem(DrawListViewSubItemEventArgs e) { e.DrawDefault = true; }
}

// Barra de progreso redondeada.
class BarraProgreso : ControlBase
{
    double valor;
    public double Valor { get { return valor; } set { valor = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        if (valor > 0)
            using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Math.Max(Height, (float)(Width - 1) * (float)valor), Height - 1), Height / 2f))
            using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
    }
}

// Ventana base con el tema oscuro y la linea de acento bajo el titulo.
class VentanaBase : Form
{
    protected const int Margen = 24;

    public VentanaBase(string titulo, int ancho)
    {
        Text = titulo + " \u00b7 vegas-cut";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Tema.Fondo;
        ForeColor = Tema.Texto;
        Font = Tema.Normal;
        DoubleBuffered = true;
        KeyPreview = true;
        ClientSize = new Size(ancho, 400);
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };
    }

    protected int Ancho { get { return ClientSize.Width - Margen * 2; } }

    protected Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); Controls.Add(c); return c; }

    protected Etiqueta Texto(string t, Font f, Color c, int x, int y, int w, int h)
    {
        Etiqueta e = new Etiqueta(t, f, c);
        if (h > 22) e.TextAlign = ContentAlignment.TopLeft;
        Pos(e, x, y, w, h);
        return e;
    }

    protected void Encabezado(string titulo, string subtitulo)
    {
        Texto(titulo, Tema.Titulo, Tema.Texto, Margen, 18, Ancho, 32);
        Texto(subtitulo, Tema.Normal, Tema.TextoSuave, Margen, 50, Ancho, 20);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using (SolidBrush b = new SolidBrush(Tema.Acento)) e.Graphics.FillRectangle(b, Margen, 76, 36, 3);
    }
}

// ---- src/silencios/Ventana.cs ----

public delegate Analisis FuncionAnalizar(int pista, bool usarSeleccion);

class Carril
{
    public string Etiqueta;
    public Analisis Datos;
    public Color Color;
    public double Umbral; // umbral efectivo (ya con la sensibilidad)
}

// Forma de onda por pista, con los silencios marcados y un umbral
// arrastrable en cada carril.
class VistaOnda : ControlBase
{
    const double MinDb = -80, MaxDb = 0;
    const int AnchoEtiqueta = 52;
    List<Carril> carriles = new List<Carril>();
    List<Rango> rangos = new List<Rango>();
    double inicio, duracion;
    int arrastrando = -1;
    int ratonX = -1;
    public string Mensaje = "Elige las pistas y pulsa Analizar.";

    // Carril arrastrado y su nuevo umbral efectivo.
    public int CarrilArrastrado;
    public double UmbralArrastrado;
    public event EventHandler UmbralCambiado;

    public static readonly Color[] Colores =
    {
        Color.FromArgb(120, 200, 255), Color.FromArgb(120, 225, 160),
        Color.FromArgb(200, 160, 255), Color.FromArgb(255, 210, 110),
        Color.FromArgb(255, 150, 190), Color.FromArgb(140, 230, 230),
    };

    public void Mostrar(List<Carril> c, List<Rango> r)
    {
        carriles = c ?? new List<Carril>();
        rangos = r;
        if (carriles.Count > 0)
        {
            inicio = carriles[0].Datos.Inicio;
            duracion = carriles[0].Datos.Duracion;
            foreach (Carril k in carriles) duracion = Math.Min(duracion, k.Datos.Duracion);
        }
        Invalidate();
    }

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

    int CarrilEn(int y)
    {
        if (carriles.Count == 0) return -1;
        return Math.Max(0, Math.Min(carriles.Count - 1, (y - Area.Y) / Math.Max(1, AreaCarril(0).Height)));
    }

    bool CercaUmbral(int y)
    {
        int i = CarrilEn(y);
        if (i < 0) return false;
        Rectangle c = AreaCarril(i);
        float centro = c.Y + c.Height / 2f, h = MitadAltura(c, carriles[i].Umbral);
        return Math.Abs(y - (centro - h)) < 6 || Math.Abs(y - (centro + h)) < 6;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (CercaUmbral(e.Y)) arrastrando = CarrilEn(e.Y);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        ratonX = e.X;
        if (arrastrando >= 0)
        {
            Rectangle c = AreaCarril(arrastrando);
            float centro = c.Y + c.Height / 2f;
            double t = Math.Abs(e.Y - centro) / ((c.Height - 6) / 2.0);
            CarrilArrastrado = arrastrando;
            UmbralArrastrado = Math.Round(Math.Max(-75, Math.Min(-5, MinDb + t * (MaxDb - MinDb))));
            if (UmbralCambiado != null) UmbralCambiado(this, EventArgs.Empty);
        }
        Cursor = arrastrando >= 0 || CercaUmbral(e.Y) ? Cursors.SizeNS : Cursors.Default;
        Invalidate();
        base.OnMouseMove(e);
    }

    protected override void OnMouseUp(MouseEventArgs e) { arrastrando = -1; base.OnMouseUp(e); }
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

            // Nombre de la pista y su umbral
            TextRenderer.DrawText(g, carril.Etiqueta, Tema.Negrita,
                new Rectangle(14, (int)centro - 17, AnchoEtiqueta - 4, 18), carril.Color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, carril.Umbral.ToString("0") + " dB", Tema.Pequena,
                new Rectangle(14, (int)centro + 1, AnchoEtiqueta - 4, 16), Tema.Acento,
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
                    g.DrawLine(m >= carril.Umbral ? voz : bajo, a.X + x, centro - h, a.X + x, centro + h);
                }
            }

            g.SmoothingMode = SmoothingMode.AntiAlias;
            float hu = MitadAltura(c, carril.Umbral);
            using (Pen pen = new Pen(Color.FromArgb(arrastrando == k ? 255 : 200, Tema.Acento), arrastrando == k ? 2f : 1.2f))
            {
                pen.DashStyle = DashStyle.Dash;
                g.DrawLine(pen, a.X, centro - hu, a.Right, centro - hu);
                g.DrawLine(pen, a.X, centro + hu, a.Right, centro + hu);
            }
            g.SmoothingMode = SmoothingMode.None;
        }

        // Tiempos
        TextRenderer.DrawText(g, Formato.Tiempo(inicio), Tema.Pequena, new Point(a.X, a.Bottom + 8), Tema.TextoSuave);
        string fin = Formato.Tiempo(inicio + duracion);
        Size fs = TextRenderer.MeasureText(fin, Tema.Pequena);
        TextRenderer.DrawText(g, fin, Tema.Pequena, new Point(a.Right - fs.Width, a.Bottom + 8), Tema.TextoSuave);
        TextRenderer.DrawText(g, "Arrastra la l\u00ednea punteada de cada pista para ajustar su umbral.", Tema.Pequena,
            new Rectangle(a.X, a.Bottom + 6, a.Width, 18), Tema.TextoSuave, TextFormatFlags.HorizontalCenter);

        // Cursor del raton: tiempo y nivel de cada pista
        if (ratonX >= a.X && ratonX < a.Right && arrastrando < 0)
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


public class VentanaSilencios : Form
{
    readonly FuncionAnalizar analizar;
    readonly string[] nombres;
    // Analisis ya hechos por pista (indice en la lista de pistas de audio) y su
    // umbral base: el automatico o el que el usuario arrastro.
    Dictionary<int, Analisis> cache = new Dictionary<int, Analisis>();
    Dictionary<int, double> umbralBase = new Dictionary<int, double>();
    List<Perfil_> perfiles = new List<Perfil_>();
    bool cargando;

    public Ajustes Ajustes;
    public List<Rango> Rangos = new List<Rango>();

    List<Boton> chipsPista = new List<Boton>();
    Segmentado segRango = new Segmentado(new string[] { "Todo el proyecto", "Selecci\u00f3n de tiempo" });
    Boton btnAnalizar = new Boton("Analizar", EstiloBoton.Secundario);
    VistaOnda onda = new VistaOnda();
    Combo comboPerfil = new Combo();
    Boton btnGuardarPerfil = new Boton("Guardar como\u2026", EstiloBoton.Secundario);
    Boton btnBorrarPerfil = new Boton("Borrar", EstiloBoton.Secundario);
    Etiqueta lblPerfil = new Etiqueta("", Tema.Pequena, Tema.TextoSuave);
    Deslizador deslizador = new Deslizador();
    Etiqueta lblSensibilidad = new Etiqueta("", Tema.Negrita, Tema.Texto);
    Boton btnAuto = new Boton("Auto", EstiloBoton.Secundario);
    CampoNumero numSilencio = new CampoNumero(), numHabla = new CampoNumero();
    CampoNumero numAntes = new CampoNumero(), numDespues = new CampoNumero();
    CampoNumero numPedazo = new CampoNumero(), numSuavizado = new CampoNumero();
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

        // Onda: un carril por pista
        Controls.Add(Pos(onda, m, y, ancho, 210));
        y += 210 + 20;

        // Perfil
        Controls.Add(Pos(new Etiqueta("Perfil", Tema.Seccion, Tema.Texto), m, y, 200, 22));
        Controls.Add(Pos(comboPerfil, m, y + 28, 230, 30));
        Controls.Add(Pos(btnGuardarPerfil, m + 238, y + 26, 130, 32));
        Controls.Add(Pos(btnBorrarPerfil, m + 374, y + 26, 76, 32));
        Controls.Add(Pos(lblPerfil, m, y + 62, 450, 32));
        lblPerfil.TextAlign = ContentAlignment.TopLeft;

        // Sensibilidad general
        int sx = m + 490;
        Controls.Add(Pos(new Etiqueta("Sensibilidad", Tema.Seccion, Tema.Texto), sx, y, 200, 22));
        Controls.Add(Pos(new Etiqueta("Sube todos los umbrales para cortar m\u00e1s; b\u00e1jalos para cortar menos.",
            Tema.Pequena, Tema.TextoSuave), sx, y + 62, ancho - 490, 32));
        deslizador.Minimo = -10; deslizador.Maximo = 10; deslizador.DesdeCentro = true;
        Controls.Add(Pos(deslizador, sx - 4, y + 28, 190, 30));
        Controls.Add(Pos(lblSensibilidad, sx + 190, y + 28, 56, 30));
        Controls.Add(Pos(btnAuto, sx + 250, y + 27, ancho - 490 - 250, 32));
        ayuda.SetToolTip(btnAuto, "Recalcula el umbral de cada pista seg\u00fan su ruido de fondo y su voz.");
        y += 104;

        // Tiempos
        int col = (ancho - 5 * 10) / 6;
        CampoTiempo(numSilencio, "Silencio m\u00ednimo", "Solo quita pausas m\u00e1s largas", m, y, col);
        CampoTiempo(numHabla, "Voz m\u00ednima", "Ignora ruidos m\u00e1s cortos", m + (col + 10), y, col);
        CampoTiempo(numAntes, "Margen antes", "Pausa antes de hablar", m + (col + 10) * 2, y, col);
        CampoTiempo(numDespues, "Margen despu\u00e9s", "Pausa al terminar", m + (col + 10) * 3, y, col);
        CampoTiempo(numPedazo, "Clip m\u00ednimo", "No deja clips m\u00e1s cortos", m + (col + 10) * 4, y, col);
        CampoTiempo(numSuavizado, "Suavizado", "Fundido del audio en cada corte", m + (col + 10) * 5, y, col);
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
        numSilencio.Maximo = 10000; numPedazo.Maximo = 10000;
        numAntes.Maximo = 2000; numDespues.Maximo = 2000;
        numSuavizado.Maximo = 200; numSuavizado.Paso = 5;
        cargando = true;
        MostrarValores(Ajustes);
        segModo.Seleccion = (int)Ajustes.Modo;
        segPistas.Seleccion = Ajustes.TodasLasPistas ? 0 : 1;
        cargando = false;
        LlenarPerfiles(Ajustes.Perfil);

        // Eventos
        btnAnalizar.Click += delegate { Analizar(); };
        deslizador.Cambio += delegate { if (!cargando) { ActualizarPerfil(); Recalcular(); } };
        onda.UmbralCambiado += delegate
        {
            int pista = PistasElegidas[onda.CarrilArrastrado];
            umbralBase[pista] = onda.UmbralArrastrado - deslizador.Valor;
            Recalcular();
        };
        btnAuto.Click += delegate { UmbralesAutomaticos(true); Recalcular(); };
        EventHandler recalc = delegate { if (!cargando) { ActualizarPerfil(); Recalcular(); } };
        numSilencio.Cambio += recalc; numHabla.Cambio += recalc; numAntes.Cambio += recalc;
        numDespues.Cambio += recalc; numPedazo.Cambio += recalc; numSuavizado.Cambio += recalc;
        comboPerfil.SelectedIndexChanged += delegate { if (!cargando) ElegirPerfil(); };
        btnGuardarPerfil.Click += delegate { GuardarPerfil(); };
        btnBorrarPerfil.Click += delegate { BorrarPerfil(); };
        segModo.Cambio += delegate { ActualizarTextoBoton(); };
        segPistas.Cambio += delegate { Recalcular(); };
        segRango.Cambio += delegate { cache.Clear(); umbralBase.Clear(); Recalcular(); };
        btnAplicar.Click += delegate { Aplicar(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        KeyDown += delegate (object s, KeyEventArgs e) { if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); } };

        ActualizarTextoBoton();
        Recalcular();
    }

    static Control Pos(Control c, int x, int y, int w, int h) { c.SetBounds(x, y, w, h); return c; }

    void CampoTiempo(CampoNumero campo, string titulo, string texto, int x, int y, int w)
    {
        Controls.Add(Pos(new Etiqueta(titulo, Tema.Negrita, Tema.Texto), x, y, w, 20));
        Etiqueta e = new Etiqueta(texto, Tema.Pequena, Tema.TextoSuave);
        e.TextAlign = ContentAlignment.TopLeft;
        Controls.Add(Pos(e, x, y + 20, w, 30));
        Controls.Add(Pos(campo, x, y + 52, w, 36));
    }

    // ------------------------------------------------------------ perfiles

    void MostrarValores(Valores v)
    {
        bool antes = cargando;
        cargando = true;
        numSilencio.Valor = v.SilencioMinMs; numHabla.Valor = v.HablaMinMs;
        numAntes.Valor = v.MargenAntesMs; numDespues.Valor = v.MargenDespuesMs;
        numPedazo.Valor = v.PedazoMinMs; numSuavizado.Valor = v.SuavizadoMs;
        deslizador.Valor = v.Sensibilidad;
        lblSensibilidad.Text = (v.Sensibilidad > 0 ? "+" : "") + v.Sensibilidad + " dB";
        cargando = antes;
    }

    void LlenarPerfiles(string elegido)
    {
        cargando = true;
        perfiles.Clear();
        perfiles.AddRange(Perfil_.Incluidos);
        perfiles.AddRange(Perfil_.CargarPropios());
        comboPerfil.Items.Clear();
        int indice = -1;
        for (int i = 0; i < perfiles.Count; i++)
        {
            comboPerfil.Items.Add(perfiles[i].Nombre + (perfiles[i].Incluido ? "" : "  \u2605"));
            if (perfiles[i].Nombre == elegido) indice = i;
        }
        comboPerfil.SelectedIndex = indice >= 0 ? indice : 0;
        cargando = false;
        ActualizarPerfil();
    }

    Perfil_ PerfilElegido { get { return comboPerfil.SelectedIndex >= 0 ? perfiles[comboPerfil.SelectedIndex] : null; } }

    void ElegirPerfil()
    {
        if (PerfilElegido == null) return;
        MostrarValores(PerfilElegido);
        ActualizarPerfil();
        Recalcular();
    }

    // Muestra la descripcion del perfil, o avisa si los valores ya no coinciden.
    void ActualizarPerfil()
    {
        LeerAjustes();
        Perfil_ p = PerfilElegido;
        if (p == null) return;
        if (p.Igual(Ajustes))
        {
            lblPerfil.Text = p.Descripcion;
            lblPerfil.ForeColor = Tema.TextoSuave;
        }
        else
        {
            lblPerfil.Text = "Modificado. Usa \u201cGuardar como\u2026\u201d para crear un perfil con estos valores.";
            lblPerfil.ForeColor = Tema.AcentoHover;
        }
        btnBorrarPerfil.Enabled = !p.Incluido;
    }

    void GuardarPerfil()
    {
        Perfil_ actual = PerfilElegido;
        string sugerido = actual != null && !actual.Incluido ? actual.Nombre : "";
        using (DialogoNombre d = new DialogoNombre(sugerido))
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            string nombre = d.Nombre.Replace("[", "(").Replace("]", ")");
            foreach (Perfil_ inc in Perfil_.Incluidos)
                if (String.Equals(inc.Nombre, nombre, StringComparison.OrdinalIgnoreCase)) nombre += " (m\u00edo)";

            List<Perfil_> propios = Perfil_.CargarPropios();
            Perfil_ p = null;
            foreach (Perfil_ x in propios)
                if (String.Equals(x.Nombre, nombre, StringComparison.OrdinalIgnoreCase)) p = x;
            if (p == null) { p = new Perfil_(); p.Nombre = nombre; propios.Add(p); }
            LeerAjustes();
            p.CopiarDe(Ajustes);
            Perfil_.GuardarPropios(propios);
            LlenarPerfiles(nombre);
        }
    }

    void BorrarPerfil()
    {
        Perfil_ p = PerfilElegido;
        if (p == null || p.Incluido) return;
        if (MessageBox.Show(this, "\u00bfBorrar el perfil \u201c" + p.Nombre + "\u201d?", "Borrar perfil",
                MessageBoxButtons.OKCancel) != DialogResult.OK) return;
        List<Perfil_> propios = Perfil_.CargarPropios();
        propios.RemoveAll(delegate (Perfil_ x) { return x.Nombre == p.Nombre; });
        Perfil_.GuardarPropios(propios);
        LlenarPerfiles(Perfil_.Incluidos[0].Nombre);
        ElegirPerfil();
    }

    // ------------------------------------------------------------- analisis

    void ActualizarTextoBoton()
    {
        string[] textos = { "Quitar silencios", "Quitar sin mover", "Silenciar", "Marcar silencios" };
        btnAplicar.Text = textos[segModo.Seleccion];
        segPistas.Enabled = segModo.Seleccion != (int)Modo.Marcar;
    }

    void LeerAjustes()
    {
        Ajustes.SilencioMinMs = numSilencio.Valor;
        Ajustes.HablaMinMs = numHabla.Valor;
        Ajustes.MargenAntesMs = numAntes.Valor;
        Ajustes.MargenDespuesMs = numDespues.Valor;
        Ajustes.PedazoMinMs = numPedazo.Valor;
        Ajustes.SuavizadoMs = numSuavizado.Valor;
        Ajustes.Sensibilidad = (int)deslizador.Valor;
        Ajustes.Modo = (Modo)segModo.Seleccion;
        Ajustes.TodasLasPistas = segPistas.Seleccion == 0;
        if (PerfilElegido != null) Ajustes.Perfil = PerfilElegido.Nombre;
    }

    List<int> Faltantes()
    {
        List<int> f = new List<int>();
        foreach (int i in PistasElegidas) if (!cache.ContainsKey(i)) f.Add(i);
        return f;
    }

    void UmbralesAutomaticos(bool todas)
    {
        foreach (KeyValuePair<int, Analisis> kv in cache)
            if (todas || !umbralBase.ContainsKey(kv.Key))
                umbralBase[kv.Key] = Detector.UmbralAutomatico(kv.Value.Db);
    }

    void Analizar()
    {
        // Si ya estaba todo leido, el boton vuelve a leer las pistas marcadas.
        List<int> leer = Faltantes();
        if (leer.Count == 0) { foreach (int i in PistasElegidas) { cache.Remove(i); umbralBase.Remove(i); } leer = PistasElegidas; }

        btnAnalizar.Enabled = false;
        Cursor = Cursors.WaitCursor;
        try
        {
            for (int k = 0; k < leer.Count; k++)
            {
                btnAnalizar.Text = "Leyendo " + (k + 1) + " de " + leer.Count + "\u2026";
                onda.Mensaje = "Leyendo el audio de " + nombres[leer[k]] + "\u2026";
                onda.Mostrar(null, new List<Rango>());
                Application.DoEvents();
                cache[leer[k]] = analizar(leer[k], segRango.Seleccion == 1);
            }
            UmbralesAutomaticos(false);
        }
        catch (Exception ex)
        {
            onda.Mensaje = "No se pudo analizar: " + ex.Message;
            onda.Mostrar(null, new List<Rango>());
        }
        finally
        {
            Cursor = Cursors.Default;
            btnAnalizar.Enabled = true;
        }
        Recalcular();
    }

    void Recalcular()
    {
        LeerAjustes();
        lblSensibilidad.Text = (Ajustes.Sensibilidad > 0 ? "+" : "") + Ajustes.Sensibilidad + " dB";
        List<int> faltan = Faltantes();
        btnAnalizar.Text = faltan.Count == 0 ? "Reanalizar" :
            cache.Count == 0 ? "Analizar" : "Analizar " + faltan.Count + (faltan.Count == 1 ? " pista" : " pistas");
        btnAuto.Enabled = cache.Count > 0;

        if (faltan.Count > 0)
        {
            Rangos = new List<Rango>();
            if (cache.Count > 0) onda.Mensaje = "Pulsa Analizar para leer las pistas nuevas.";
            onda.Mostrar(null, Rangos);
            lblResumenGrande.Text = "Sin analizar";
            lblResumen.Text = "Analiza para ver los silencios.";
            btnAplicar.Enabled = false;
            return;
        }

        List<int> elegidas = PistasElegidas;
        List<Analisis> datos = new List<Analisis>();
        List<double> umbrales = new List<double>();
        List<Carril> carriles = new List<Carril>();
        for (int k = 0; k < elegidas.Count; k++)
        {
            int p = elegidas[k];
            datos.Add(cache[p]);
            umbrales.Add(umbralBase[p]);

            Carril c = new Carril();
            string n = nombres[p];
            int espacio = n.IndexOf(' ');
            c.Etiqueta = espacio > 0 ? n.Substring(0, espacio) : n;
            c.Datos = cache[p];
            c.Color = VistaOnda.Colores[k % VistaOnda.Colores.Length];
            c.Umbral = umbralBase[p] + Ajustes.Sensibilidad;
            carriles.Add(c);
        }
        Rangos = Detector.Detectar(datos, umbrales, Ajustes);
        onda.Mostrar(carriles, Rangos);

        double quitado = 0;
        foreach (Rango r in Rangos) quitado += r.Fin - r.Inicio;
        double dur = cache[elegidas[0]].Duracion;
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
