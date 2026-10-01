using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

// =====================================================================
// Momentos con IA: que se le pide a Gemini y como se lee la respuesta.
// Todos los tiempos estan en la linea de tiempo ACTUAL (despues de los
// cortes que ya se hayan hecho).
//
// Videos cortos: una sola peticion. Videos largos (mas de ~35 min): por
// partes de ~20 min (cada parte elige candidatos y resume lo que pasa) y una
// pasada final que arma el corte completo cuidando la historia.
// =====================================================================

public class Tramo
{
    public double Inicio, Fin, Puntuacion;
    public string Titulo = "", Motivo = "";
    public bool Elegido = true;
    public string Nota = "";       // por que se marco o desmarco solo (revision, duracion)
    public bool PorRevision;       // desmarcado por incumplir reglas: no se vuelve a marcar solo
    public bool Acelerar;          // en el corte: se conserva pero mas rapido
    public double Velocidad = 1;   // 2 = el doble de rapido
    public bool Fijo;              // lo elegiste tu: ni la IA ni los ajustes lo quitan

    public double Duracion { get { return Fin - Inicio; } }
    public object MemberwiseCopia() { return MemberwiseClone(); }
    // Lo que dura en el video final.
    public double DuracionFinal { get { return Acelerar ? Duracion / Velocidad : Duracion; } }
}

public class TextoResumen
{
    public double Posicion;
    public string Texto = "", Motivo = "";
    public bool Elegido = true;
}

public class OpcionesIA
{
    public string Tipo = "Gameplay";
    public double MinutosMin = 11, MinutosMax = 15;
    public double MinutosObjetivo { get { return (MinutosMin + MinutosMax) / 2; } }
    public string ReglasCanal = PeticionIA.ReglasPorDefecto;
    public string Contexto = "";   // resumenes de episodios anteriores (opcional)
    public string Instrucciones = "";
    public bool PermitirAcelerar = true;   // transiciones aceleradas en vez de cortadas
    public bool SilenciarAcelerado = true; // audio mudo en lo acelerado
    public List<Tramo> Fijos = new List<Tramo>(); // tramos que el editor ya eligio
    public bool FijosCuentan = true;              // si cuentan para la duracion minima y maxima
}

public class ResultadoIA
{
    public string Resumen = "";
    public List<Tramo> Secciones = new List<Tramo>();
    public List<Tramo> Momentos = new List<Tramo>();
    public List<Tramo> Corte = new List<Tramo>();
    public List<TextoResumen> Textos = new List<TextoResumen>();
    public List<Tramo> Shorts = new List<Tramo>();
    public List<string> Titulos = new List<string>();
    // Tramos que propusieron las partes (videos largos): sirven para completar
    // el corte si queda corto.
    public List<Tramo> Candidatos = new List<Tramo>();

    public bool FijosCuentan = true;

    public double DuracionFijos
    {
        get
        {
            double d = 0;
            foreach (Tramo t in Corte) if (t.Elegido && t.Fijo) d += t.DuracionFinal;
            return d;
        }
    }

    // Lo que se compara con el minimo y el maximo.
    public double DuracionAjustable { get { return FijosCuentan ? DuracionCorte : DuracionCorte - DuracionFijos; } }

    public double DuracionCorte
    {
        get
        {
            double d = 0;
            foreach (Tramo t in Corte) if (t.Elegido) d += t.DuracionFinal;
            return d;
        }
    }

    public static double LimitarVelocidad(double v)
    {
        if (double.IsNaN(v) || v < 1.5) return 2;
        return Math.Min(4, Math.Round(v));
    }

    static List<Tramo> Tramos(object o, string clave, double total)
    {
        List<Tramo> r = new List<Tramo>();
        foreach (object x in Json.Lista(o, clave))
        {
            Tramo t = new Tramo();
            t.Inicio = Math.Max(0, Json.Numero(x, "inicio", 0));
            t.Fin = Math.Min(total, Json.Numero(x, "fin", 0));
            t.Puntuacion = Json.Numero(x, "puntuacion", Json.Numero(x, "importancia", 0));
            t.Titulo = Json.Texto(x, "titulo");
            t.Motivo = Json.Texto(x, "motivo");
            if (t.Motivo.Length == 0) t.Motivo = Json.Texto(x, "descripcion");
            if (t.Motivo.Length == 0) t.Motivo = Json.Texto(x, "gancho");
            t.Acelerar = Json.Texto(x, "accion").ToLowerInvariant().StartsWith("aceler");
            t.Velocidad = t.Acelerar ? LimitarVelocidad(Json.Numero(x, "velocidad", 3)) : 1;
            if (t.Fin - t.Inicio >= 0.2) r.Add(t);
        }
        r.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
        return r;
    }

    // Lee la respuesta (JSON). "total" es la duracion actual del proyecto.
    public static ResultadoIA Leer(string json, double total)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        ResultadoIA r = new ResultadoIA();
        r.Resumen = Json.Texto(o, "resumen");
        r.Secciones = Tramos(o, "secciones", total);
        r.Momentos = Tramos(o, "momentos", total);
        r.Momentos.Sort(delegate (Tramo a, Tramo b) { return b.Puntuacion.CompareTo(a.Puntuacion); });
        r.Shorts = Tramos(o, "shorts", total);
        r.Corte = UnirSolapados(Tramos(o, "corte", total));
        r.Candidatos = Tramos(o, "candidatos", total);
        foreach (object x in Json.Lista(o, "textos"))
        {
            TextoResumen t = new TextoResumen();
            t.Posicion = Math.Max(0, Math.Min(total, Json.Numero(x, "posicion", 0)));
            t.Texto = Json.Texto(x, "texto");
            t.Motivo = Json.Texto(x, "motivo");
            if (t.Texto.Length > 0) r.Textos.Add(t);
        }
        r.Textos.Sort(delegate (TextoResumen a, TextoResumen b) { return a.Posicion.CompareTo(b.Posicion); });
        foreach (object x in Json.Lista(o, "titulos"))
            if (x is string && ((string)x).Length > 0) r.Titulos.Add((string)x);
        return r;
    }

    // Une tramos que se tocan con la misma accion; si se enciman con distinta
    // accion, el segundo empieza donde termina el primero.
    static List<Tramo> UnirSolapados(List<Tramo> l)
    {
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in l)
        {
            Tramo u = r.Count > 0 ? r[r.Count - 1] : null;
            if (u != null && t.Inicio <= u.Fin + 0.05)
            {
                if (u.Acelerar == t.Acelerar && u.Velocidad == t.Velocidad)
                {
                    u.Fin = Math.Max(u.Fin, t.Fin);
                    if (t.Titulo.Length > 0 && u.Titulo.IndexOf(t.Titulo) < 0) u.Titulo += " / " + t.Titulo;
                    continue;
                }
                t.Inicio = u.Fin;
                if (t.Fin - t.Inicio < 0.2) continue;
            }
            r.Add(t);
        }
        return r;
    }

    // Aplica la revision: {"tramos":[{"indice":n,"quitar":bool,"inicio":s,"fin":s,"motivo":"..."}]}.
    // Devuelve cuantos tramos cambio.
    public int AplicarRevision(string json)
    {
        int cambios = 0;
        List<Tramo> nuevos = new List<Tramo>();
        object o = Json.Leer(Gemini.QuitarCercas(json));
        foreach (object x in Json.Lista(o, "tramos"))
        {
            int i = (int)Json.Numero(x, "indice", -1);
            if (i < 0 || i >= Corte.Count) continue;
            Tramo t = Corte[i];
            if (t.Fijo) continue;
            string motivo = Json.Texto(x, "motivo");
            object quitar;
            Dictionary<string, object> d = x as Dictionary<string, object>;
            if (d != null && d.TryGetValue("quitar", out quitar) && quitar is bool && (bool)quitar)
            {
                // Si dice que parte quitar y es solo un pedazo del tramo, se
                // quita ese pedazo y el resto se queda.
                double qa = Json.Numero(x, "inicio", -1), qb = Json.Numero(x, "fin", -1);
                if (qb - qa >= 0.5)
                {
                    qa = Math.Max(qa, t.Inicio); qb = Math.Min(qb, t.Fin);
                    if (qb - qa < 0.5) continue; // no toca lo que queda del tramo
                    bool alInicio = qa <= t.Inicio + 0.5, alFinal = qb >= t.Fin - 0.5;
                    if (!(alInicio && alFinal))
                    {
                        string nota = "Recortado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                        if (alInicio) t.Inicio = qb;
                        else if (alFinal) t.Fin = qa;
                        else
                        {
                            Tramo resto = Pedazo(t, qb, t.Fin);
                            resto.Nota = nota;
                            nuevos.Add(resto);
                            t.Fin = qa;
                        }
                        t.Nota = nota;
                        cambios++;
                        continue;
                    }
                }
                t.Elegido = false;
                t.PorRevision = true;
                t.Nota = "Quitado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                cambios++;
                continue;
            }
            // Recorte: conservar solo una parte del tramo.
            double a = Json.Numero(x, "inicio", t.Inicio), b = Json.Numero(x, "fin", t.Fin);
            if ((a > t.Inicio + 0.5 || b < t.Fin - 0.5) && a >= t.Inicio - 0.01 && b <= t.Fin + 0.01 && b - a >= 2)
            {
                t.Inicio = a; t.Fin = b;
                t.Nota = "Recortado en la revisi\u00f3n" + (motivo.Length > 0 ? ": " + motivo : "");
                cambios++;
            }
        }
        if (nuevos.Count > 0)
        {
            Corte.AddRange(nuevos);
            Corte.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
        }
        return cambios;
    }

    // Agrega un tramo elegido a mano. Lo que la IA tenia adentro se absorbe;
    // lo que sobresale se conserva recortado.
    public void AgregarFijo(double a, double b, string titulo)
    {
        if (b - a < 0.5) return;
        Tramo n = new Tramo();
        n.Inicio = a; n.Fin = b; n.Puntuacion = 10; n.Fijo = true;
        n.Titulo = titulo;
        n.Motivo = "Lo elegiste t\u00fa: se conserva completo.";
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in Corte)
        {
            if (t.Fin <= a + 0.05 || t.Inicio >= b - 0.05) { r.Add(t); continue; }
            if (t.Fijo) { n.Inicio = Math.Min(n.Inicio, t.Inicio); n.Fin = Math.Max(n.Fin, t.Fin); continue; }
            if (t.Inicio < a - 0.5) r.Add(Pedazo(t, t.Inicio, a));
            if (t.Fin > b + 0.5) r.Add(Pedazo(t, b, t.Fin));
        }
        r.Add(n);
        r.Sort(delegate (Tramo x, Tramo y) { return x.Inicio.CompareTo(y.Inicio); });
        Corte = r;
    }

    static Tramo Pedazo(Tramo t, double a, double b)
    {
        Tramo p = (Tramo)t.MemberwiseCopia();
        p.Inicio = a; p.Fin = b;
        return p;
    }

    bool SeEncima(Tramo c)
    {
        foreach (Tramo t in Corte)
            if (t != c && t.Elegido && c.Inicio < t.Fin - 0.05 && c.Fin > t.Inicio + 0.05) return true;
        return false;
    }

    // Deja el corte entre el minimo y el maximo (segundos). Si sobra, desmarca
    // los tramos de menor importancia (nunca el primero ni el ultimo); si falta,
    // vuelve a marcar tramos desmarcados por duracion o agrega candidatos.
    // Devuelve un resumen de lo que hizo ("" si no hizo nada).
    public string AjustarDuracion(double minimo, double maximo)
    {
        int quitados = 0, agregados = 0;
        while (DuracionAjustable > maximo + 0.5)
        {
            List<Tramo> elegidos = Corte.FindAll(delegate (Tramo t) { return t.Elegido; });
            Tramo peor = null;
            for (int i = 1; i < elegidos.Count - 1; i++)
            {
                Tramo t = elegidos[i];
                if (t.Fijo) continue;
                if (peor == null || t.Puntuacion < peor.Puntuacion ||
                    (t.Puntuacion == peor.Puntuacion && t.DuracionFinal > peor.DuracionFinal)) peor = t;
            }
            if (peor == null) break;
            peor.Elegido = false;
            peor.Nota = "Desmarcado para no pasar del m\u00e1ximo (importancia " + peor.Puntuacion.ToString("0") + ")";
            quitados++;
        }
        while (DuracionAjustable < minimo - 0.5)
        {
            Tramo mejor = null;
            bool nuevo = false;
            foreach (Tramo t in Corte)
                if (!t.Elegido && !t.PorRevision && !SeEncima(t) && DuracionAjustable + t.DuracionFinal <= maximo + 0.5 &&
                    (mejor == null || t.Puntuacion > mejor.Puntuacion)) mejor = t;
            if (mejor == null)
                foreach (Tramo c in Candidatos)
                    if (!Corte.Contains(c) && !SeEncima(c) && DuracionAjustable + c.DuracionFinal <= maximo + 0.5 &&
                        (mejor == null || c.Puntuacion > mejor.Puntuacion)) { mejor = c; nuevo = true; }
            if (mejor == null) break;
            mejor.Elegido = true;
            mejor.Nota = "Agregado para llegar al m\u00ednimo";
            if (nuevo)
            {
                Corte.Add(mejor);
                Corte.Sort(delegate (Tramo a, Tramo b) { return a.Inicio.CompareTo(b.Inicio); });
            }
            agregados++;
        }
        List<string> partes = new List<string>();
        if (quitados > 0) partes.Add(quitados + (quitados == 1 ? " tramo desmarcado" : " tramos desmarcados") + " por pasar del m\u00e1ximo");
        if (agregados > 0) partes.Add(agregados + (agregados == 1 ? " tramo agregado" : " tramos agregados") + " para llegar al m\u00ednimo");
        return String.Join("; ", partes.ToArray());
    }

    // Lleva los bordes del corte al inicio/fin de la palabra que cortarian,
    // para no partir palabras a la mitad.
    public void AjustarAPalabras(List<Segmento> segmentos)
    {
        List<Palabra> palabras = new List<Palabra>();
        foreach (Segmento s in segmentos) palabras.AddRange(s.Palabras);
        foreach (Tramo t in Corte)
        {
            foreach (Palabra p in palabras)
            {
                if (t.Inicio > p.Inicio && t.Inicio < p.Fin) t.Inicio = p.Inicio;
                if (t.Fin > p.Inicio && t.Fin < p.Fin) t.Fin = p.Fin;
            }
        }
        Corte = UnirSolapados(Corte);
    }

    // Lo que se quita para quedarse solo con los tramos elegidos del corte.
    public List<Rango> Quitar(double total)
    {
        List<Rango> r = new List<Rango>();
        double cursor = 0;
        foreach (Tramo t in Corte)
        {
            if (!t.Elegido) continue;
            if (t.Inicio - cursor > 0.01) r.Add(new Rango(cursor, t.Inicio));
            cursor = Math.Max(cursor, t.Fin);
        }
        if (total - cursor > 0.01) r.Add(new Rango(cursor, total));
        return r;
    }

    // Instante despues de quitar los rangos (si cae adentro, queda en el corte).
    public static double TrasQuitar(double t, List<Rango> quitados)
    {
        double q = 0;
        foreach (Rango r in quitados)
        {
            if (t >= r.Fin) q += r.Fin - r.Inicio;
            else if (t > r.Inicio) q += t - r.Inicio;
        }
        return t - q;
    }

    // Tramos a acelerar, ya en la linea de tiempo que queda despues de quitar.
    public List<Acelerado> Acelerados(List<Rango> quitados)
    {
        List<Acelerado> r = new List<Acelerado>();
        foreach (Tramo t in Corte)
            if (t.Elegido && t.Acelerar && t.Velocidad > 1)
                r.Add(new Acelerado(TrasQuitar(t.Inicio, quitados), TrasQuitar(t.Fin, quitados), t.Velocidad));
        return r;
    }

    // ------------------------------------------------------------ informe

    public string Informe(string proyecto, OpcionesIA op, double total)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("# " + Path.GetFileNameWithoutExtension(proyecto) + "\n\n");
        sb.Append("Generado con vegas-cut y Gemini el " + DateTime.Now.ToString("yyyy-MM-dd HH:mm") +
                  ". Tipo: " + op.Tipo + ". Objetivo: " + op.MinutosMin + "\u2013" + op.MinutosMax + " min. Duraci\u00f3n original: " +
                  Formato.Tiempo(total) + ".\n\n");
        sb.Append("## Resumen\n\n" + Resumen + "\n\n");
        if (Secciones.Count > 0)
        {
            sb.Append("## Secciones\n\n");
            foreach (Tramo t in Secciones)
                sb.Append("- **" + Formato.Tiempo(t.Inicio) + "–" + Formato.Tiempo(t.Fin) + " " + t.Titulo + "**: " + t.Motivo + "\n");
            sb.Append("\n");
        }
        sb.Append("## Corte sugerido (" + Formato.Tiempo(DuracionCorte) + ")\n\n");
        foreach (Tramo t in Corte)
            sb.Append("- [" + (t.Elegido ? "x" : " ") + "] " + Formato.Tiempo(t.Inicio) + "–" + Formato.Tiempo(t.Fin) +
                      " (" + Formato.Tiempo(t.Duracion) + (t.Acelerar ? ", acelerado ×" + t.Velocidad + " → " + Formato.Tiempo(t.DuracionFinal) : "") +
                      ") **" + t.Titulo + "**: " + t.Motivo + (t.Nota.Length > 0 ? " _(" + t.Nota + ")_" : "") + "\n");
        sb.Append("\n## Momentos destacados\n\n");
        foreach (Tramo t in Momentos)
            sb.Append("- " + t.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + "/10 · " + Formato.Tiempo(t.Inicio) + "–" +
                      Formato.Tiempo(t.Fin) + " **" + t.Titulo + "**: " + t.Motivo + "\n");
        if (Textos.Count > 0)
        {
            sb.Append("\n## Textos de resumen\n\n");
            foreach (TextoResumen t in Textos)
                sb.Append("- " + Formato.Tiempo(t.Posicion) + ": “" + t.Texto + "”" + (t.Motivo.Length > 0 ? " (" + t.Motivo + ")" : "") + "\n");
        }
        if (Shorts.Count > 0)
        {
            sb.Append("\n## Ideas para Shorts\n\n");
            foreach (Tramo t in Shorts)
                sb.Append("- " + Formato.Tiempo(t.Inicio) + "–" + Formato.Tiempo(t.Fin) + " **" + t.Titulo + "**: " + t.Motivo + "\n");
        }
        if (Titulos.Count > 0)
        {
            sb.Append("\n## Títulos sugeridos\n\n");
            foreach (string t in Titulos) sb.Append("- " + t + "\n");
        }
        return sb.ToString();
    }
}

// =====================================================================
// Textos que se envian a Gemini
// =====================================================================

// Tramos fijos del proyecto (<proyecto>.vegascut-fijos.json), en tiempos de
// la linea de tiempo de cuando se eligieron. Solo valen mientras el proyecto
// dure lo mismo (antes de aplicar el corte).
public static class TramosFijos
{
    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-fijos.json");
    }

    public static bool Cuentan(string veg)
    {
        string ruta = RutaPara(veg);
        try
        {
            if (ruta != null && File.Exists(ruta))
                return Json.Texto(Json.Leer(File.ReadAllText(ruta, Encoding.UTF8)), "cuentan") != "False";
        }
        catch { }
        return true;
    }

    public static List<Tramo> Cargar(string veg, double duracion)
    {
        List<Tramo> r = new List<Tramo>();
        string ruta = RutaPara(veg);
        try
        {
            if (ruta == null || !File.Exists(ruta)) return r;
            object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
            if (Math.Abs(Json.Numero(o, "duracionProyecto", -1) - duracion) > 0.5) return r;
            foreach (object x in Json.Lista(o, "fijos"))
            {
                Tramo t = new Tramo();
                t.Inicio = Json.Numero(x, "inicio", 0); t.Fin = Json.Numero(x, "fin", 0);
                t.Titulo = Json.Texto(x, "titulo"); t.Fijo = true; t.Puntuacion = 10;
                if (t.Fin > t.Inicio) r.Add(t);
            }
        }
        catch { }
        return r;
    }

    public static void Guardar(string veg, double duracion, List<Tramo> fijos)
    {
        Guardar(veg, duracion, fijos, true);
    }

    public static void Guardar(string veg, double duracion, List<Tramo> fijos, bool cuentan)
    {
        string ruta = RutaPara(veg);
        if (ruta == null) return;
        List<object> l = new List<object>();
        foreach (Tramo t in fijos)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["inicio"] = Math.Round(t.Inicio, 3); d["fin"] = Math.Round(t.Fin, 3); d["titulo"] = t.Titulo;
            l.Add(d);
        }
        Dictionary<string, object> raiz = new Dictionary<string, object>();
        raiz["duracionProyecto"] = duracion;
        raiz["fijos"] = l;
        raiz["cuentan"] = cuentan;
        try { File.WriteAllText(ruta, Json.Escribir(raiz), new UTF8Encoding(false)); } catch { }
    }

    // Agrega un tramo a la lista, uniendo los que se enciman.
    public static List<Tramo> Agregar(List<Tramo> fijos, double a, double b, string titulo)
    {
        Tramo n = new Tramo();
        n.Inicio = a; n.Fin = b; n.Titulo = titulo; n.Fijo = true; n.Puntuacion = 10;
        List<Tramo> r = new List<Tramo>();
        foreach (Tramo t in fijos)
        {
            if (t.Fin < a - 0.05 || t.Inicio > b + 0.05) { r.Add(t); continue; }
            n.Inicio = Math.Min(n.Inicio, t.Inicio); n.Fin = Math.Max(n.Fin, t.Fin);
        }
        r.Add(n);
        r.Sort(delegate (Tramo x, Tramo y) { return x.Inicio.CompareTo(y.Inicio); });
        return r;
    }
}

public static class PeticionIA
{
    public static string S(double t) { return t.ToString("0.0", CultureInfo.InvariantCulture); }

    // Reglas editoriales que aplican siempre (se pueden editar en la ventana;
    // se guardan para todos los proyectos).
    public const string ReglasPorDefecto =
        "- Empieza directo en la acci\u00f3n o en un gancho: nada de saludos largos, \u201c\u00bfme escuchan?\u201d, cargas de mundo, problemas t\u00e9cnicos ni preparaci\u00f3n.\r\n" +
        "- Excluye conversaciones personales o privadas aunque sean graciosas: vida amorosa, parejas, ex, familia, salud, dinero, escuela o trabajo, y cualquier dato personal.\r\n" +
        "- Excluye charla que no tenga que ver con el juego ni con la historia, problemas t\u00e9cnicos (lag, micr\u00f3fono, Discord, OBS), silencios, AFK y grindeo repetitivo.\r\n" +
        "- Mant\u00e9n el hilo: antes de cambiar de lugar o de actividad, conserva de 1 a 3 frases que digan a d\u00f3nde van o qu\u00e9 van a hacer. Si no existen, prop\u00f3n un texto de resumen.\r\n" +
        "- No cortes a mitad de una idea, chiste o reacci\u00f3n: incluye el remate.\r\n" +
        "- Termina con el cl\u00edmax o con un cierre o suspenso claro.";

    const string Rol =
        "Eres un editor de video experto en contenido de YouTube en español (gameplays con amigos, " +
        "narraciones, video ensayos). Recibes la transcripción de un video con tiempos en segundos de la " +
        "línea de tiempo y una tabla de intensidad de sonido. Tu trabajo es ayudar a editarlo.\n\n";

    const string ReglasCorte =
        "- \"corte\": tramos a CONSERVAR, en orden, sin solaparse. Deben contar la historia completa sin omitir " +
        "partes importantes (objetivos, decisiones, resultados, momentos graciosos o intensos). Empieza y termina " +
        "cada tramo en límites de frase, nunca a mitad de una palabra. Prefiere tramos de 10 s a 3 min.\n" +
        "- Cada tramo del corte lleva \"importancia\" de 1 a 10 (10 = imprescindible para la historia; 1 = relleno). " +
        "Se usa para ajustar la duración quitando primero lo menos importante.\n" +
        "- Las REGLAS DEL CANAL y las INDICACIONES DEL EPISODIO son obligatorias: un tramo que las incumple no va " +
        "en el corte aunque sea gracioso o intenso.\n" +
        "- Poca conversación no significa que no pase nada: en carreras, peleas, persecuciones, exploración o " +
        "construcción puede haber acción con poca voz. Fíjate en la intensidad de ambiente y en las indicaciones; " +
        "si piden mostrar una actividad completa, consérvala completa aunque hablen poco.\n" +
        "- Los TRAMOS FIJOS ya los eligió el editor: van completos en el corte (inclúyelos tal cual). Si dicen " +
        "que cuentan para la duración, el resto tiene que caber en lo que queda.\n";

    const string ReglasAcelerar =
        "- Cada tramo del corte lleva \"accion\": \"conservar\" (velocidad normal) o \"acelerar\" (se ve más rápido, " +
        "sin audio): úsalo para transiciones con poca conversación que ayudan a entender el progreso (viajar, " +
        "minar, construir, preparar). \"velocidad\" de 2 a 4. Un tramo acelerado cuenta para la duración como " +
        "duración/velocidad. No aceleres tramos con diálogo importante.\n";

    const string SinAcelerar = "- Todos los tramos del corte llevan \"accion\": \"conservar\" (no se acelera nada).\n";

    const string EsquemaFinal =
        "Responde SOLO con un objeto JSON con exactamente estas claves:\n" +
        "{\n" +
        "  \"resumen\": \"qué pasa en el video, en orden, en 1 a 3 párrafos\",\n" +
        "  \"secciones\": [{\"inicio\": s, \"fin\": s, \"titulo\": \"...\", \"descripcion\": \"qué pasa\"}],\n" +
        "  \"momentos\": [{\"inicio\": s, \"fin\": s, \"puntuacion\": 1-10, \"titulo\": \"...\", \"motivo\": \"por qué es bueno\"}],\n" +
        "  \"corte\": [{\"inicio\": s, \"fin\": s, \"importancia\": 1-10, \"accion\": \"conservar\" o \"acelerar\", \"velocidad\": 1-4, \"titulo\": \"...\", \"motivo\": \"por qué se conserva\"}],\n" +
        "  \"textos\": [{\"posicion\": s, \"texto\": \"texto corto en pantalla\", \"motivo\": \"qué se salta\"}],\n" +
        "  \"shorts\": [{\"inicio\": s, \"fin\": s, \"titulo\": \"...\", \"gancho\": \"por qué funciona solo\"}],\n" +
        "  \"titulos\": [\"título para el video\", \"...\"]\n" +
        "}\n\n";

    const string ReglasResto =
        "- \"textos\": frases muy cortas tipo \"Construimos la base\" o \"3 horas después…\" para explicar lo que " +
        "el corte se salta; \"posicion\" es el inicio del tramo conservado donde conviene mostrarlo. Solo donde " +
        "realmente ayude a no perderse.\n" +
        "- \"momentos\": los mejores 5 a 15 (risas, gritos, sorpresas, frases memorables, acción intensa). Usa la " +
        "intensidad: valores altos de voz suelen ser gritos o risas; de ambiente, explosiones o peleas.\n" +
        "- \"shorts\": 2 a 5 tramos de 15 a 60 s que se entiendan sin contexto.\n" +
        "- \"titulos\": 3 a 5 opciones atractivas.\n" +
        "- Todos los tiempos son segundos (número) de la línea de tiempo dada; no inventes tiempos fuera del video.\n" +
        "- Escribe todo en español natural. Usa los nombres de las personas.";

    // ---------------------------------------------------- una peticion

    public static string Instrucciones(OpcionesIA op)
    {
        return Rol + EsquemaFinal + "Reglas:\n" + ReglasCorte +
               "- El corte completo debe durar entre la duración mínima y la máxima (apunta a la ideal). Antes de " +
               "responder, suma las duraciones de los tramos (los acelerados cuentan duración/velocidad) y corrige si te pasas.\n" +
               (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) + ReglasResto;
    }

    static void Cabecera(StringBuilder sb, Transcripcion t, double duracionActual, OpcionesIA op)
    {
        sb.Append("Tipo de video: " + op.Tipo + "\n");
        sb.Append("Duración actual: " + S(duracionActual) + " s (" + Formato.Tiempo(duracionActual) + ")\n");
        sb.Append("Duración del corte: mínimo " + S(op.MinutosMin * 60) + " s, máximo " + S(op.MinutosMax * 60) +
                  " s, ideal " + S(op.MinutosObjetivo * 60) + " s (" + op.MinutosMin + " a " + op.MinutosMax + " min)\n");
        if (!String.IsNullOrEmpty(op.ReglasCanal)) sb.Append("\nREGLAS DEL CANAL (siempre):\n" + ConSegundos(op.ReglasCanal.Trim()) + "\n");
        if (!String.IsNullOrEmpty(op.Instrucciones)) sb.Append("\nINDICACIONES DEL EPISODIO:\n" + ConSegundos(op.Instrucciones.Trim()) + "\n");
        Fijos_(sb, op);
        if (!String.IsNullOrEmpty(op.Contexto))
            sb.Append("\nCONTEXTO DE LA SERIE (otros capítulos; solo para entender la historia):\n" + op.Contexto.Trim() + "\n");
        sb.Append("\nPersonas (cada una es una pista de audio):\n");
        foreach (Hablante h in t.Hablantes)
            if (h.Voz) sb.Append("- " + h.Nombre + (h.Nombre != h.Etiqueta ? " (" + h.Etiqueta + ")" : "") + "\n");
    }

    static void Fijos_(StringBuilder sb, OpcionesIA op)
    {
        if (op.Fijos.Count == 0) return;
        double total = 0;
        sb.Append("\nTRAMOS FIJOS (elegidos por el editor; van completos en el corte):\n");
        foreach (Tramo f in op.Fijos)
        {
            sb.Append("- [" + S(f.Inicio) + "-" + S(f.Fin) + "] " + f.Titulo + " (" + Formato.Tiempo(f.Duracion) + ")\n");
            total += f.Duracion;
        }
        if (op.FijosCuentan)
            sb.Append("Suman " + S(total) + " s y CUENTAN para la duración: el resto del corte debe caber en lo que queda.\n");
        else
            sb.Append("Suman " + S(total) + " s y NO cuentan para la duración: el mínimo y el máximo son solo para el resto " +
                      "del corte, aparte de estos tramos.\n");
    }

    // Los tiempos escritos como 57:00 o 1:09:30 se acompañan con su valor en
    // segundos, que es como estan los tiempos de la transcripcion.
    public static string ConSegundos(string texto)
    {
        return Regex.Replace(texto ?? "", @"(?<![\d:])(\d{1,2}):(\d{2})(?::(\d{2}))?(?![\d:])", delegate (Match m)
        {
            int a = int.Parse(m.Groups[1].Value), b = int.Parse(m.Groups[2].Value);
            double seg = m.Groups[3].Success ? a * 3600 + b * 60 + int.Parse(m.Groups[3].Value) : a * 60 + b;
            return m.Value + " (= " + S(seg) + " s)";
        });
    }

    static void Transcripcion_(StringBuilder sb, Transcripcion t, List<Segmento> segmentos, double desde, double hasta)
    {
        sb.Append("\nTranscripción [inicio-fin] persona: texto\n");
        foreach (Segmento s in segmentos)
            if (s.Fin > desde && s.Inicio < hasta)
                sb.Append("[" + S(s.Inicio) + "-" + S(s.Fin) + "] " + t.Hablantes[s.Hablante].Nombre + ": " + s.Texto + "\n");
    }

    static void Intensidad_(StringBuilder sb, Transcripcion t, double duracionActual, double desde, double hasta)
    {
        sb.Append("\nIntensidad cada 5 s (0 = silencio, 10 = lo más fuerte de esa pista). Columnas: inicio;voz;ambiente\n");
        foreach (string linea in Intensidad(t, duracionActual, 5))
        {
            double inicio = double.Parse(linea.Substring(0, linea.IndexOf(';')), CultureInfo.InvariantCulture);
            if (inicio + 5 > desde && inicio < hasta) sb.Append(linea + "\n");
        }
    }

    // Mensaje con la transcripcion e intensidad en la linea de tiempo actual.
    public static string Mensaje(Transcripcion t, double duracionActual, OpcionesIA op)
    {
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        Transcripcion_(sb, t, t.SegmentosActuales(), 0, duracionActual);
        Intensidad_(sb, t, duracionActual, 0, duracionActual);
        return sb.ToString();
    }

    // ------------------------------------------------------- por partes

    // Divide el video en partes de ~"tamano" segundos, cortando en la pausa
    // mas larga entre frases cerca de cada limite.
    public static List<Rango> Partes(List<Segmento> segmentos, double total, double tamano)
    {
        List<Rango> r = new List<Rango>();
        int n = Math.Max(1, (int)Math.Round(total / tamano));
        double inicio = 0;
        for (int i = 1; i < n; i++)
        {
            double ideal = total * i / n, mejor = ideal, hueco = -1;
            for (int k = 0; k + 1 < segmentos.Count; k++)
            {
                double a = segmentos[k].Fin, b = segmentos[k + 1].Inicio;
                double medio = (a + b) / 2;
                if (Math.Abs(medio - ideal) > tamano * 0.15 || b - a <= hueco) continue;
                hueco = b - a;
                mejor = medio;
            }
            if (mejor <= inicio + 60) mejor = ideal;
            r.Add(new Rango(inicio, mejor));
            inicio = mejor;
        }
        r.Add(new Rango(inicio, total));
        return r;
    }

    public static string InstruccionesParte(OpcionesIA op)
    {
        return Rol +
            "Este video es largo, así que lo recibes POR PARTES. Ahora te toca UNA parte. Elige candidatos para el " +
            "corte final (después se elegirá entre los candidatos de todas las partes) y resume lo que pasa.\n\n" +
            "Responde SOLO con un objeto JSON con exactamente estas claves:\n" +
            "{\n" +
            "  \"resumen\": \"qué pasa en esta parte, en orden, en 2 a 5 frases (con nombres y hechos concretos)\",\n" +
            "  \"candidatos\": [{\"inicio\": s, \"fin\": s, \"importancia\": 1-10, \"accion\": \"conservar\" o \"acelerar\", \"velocidad\": 1-4, \"titulo\": \"...\", \"motivo\": \"...\"}],\n" +
            "  \"momentos\": [{\"inicio\": s, \"fin\": s, \"puntuacion\": 1-10, \"titulo\": \"...\", \"motivo\": \"...\"}]\n" +
            "}\n\n" +
            "Reglas:\n" + ReglasCorte +
            "- Los candidatos de esta parte deben sumar cerca de la duración sugerida para la parte (es generosa: " +
            "luego se recorta). \"importancia\": 10 = imprescindible para entender la historia o lo más gracioso; " +
            "1 = relleno prescindible.\n" +
            (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) +
            "- \"momentos\": los mejores de esta parte (0 a 6).\n" +
            "- Usa solo tiempos dentro de esta parte. Escribe en español natural y usa los nombres de las personas.";
    }

    public static string MensajeParte(Transcripcion t, List<Segmento> segmentos, double duracionActual, OpcionesIA op,
                                      int numero, List<Rango> partes, List<string> resumenesPrevios)
    {
        Rango p = partes[numero];
        double sugerido = op.MinutosObjetivo * 60 * (p.Fin - p.Inicio) / duracionActual * 1.5;
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        sb.Append("\nParte " + (numero + 1) + " de " + partes.Count + ": de " + S(p.Inicio) + " s a " + S(p.Fin) + " s (" +
                  Formato.Tiempo(p.Inicio) + "–" + Formato.Tiempo(p.Fin) + ").\n");
        sb.Append("Duración sugerida para los candidatos de esta parte: " + S(sugerido) + " s.\n");
        if (resumenesPrevios.Count > 0)
        {
            sb.Append("\nLo que pasó en las partes anteriores:\n");
            for (int i = 0; i < resumenesPrevios.Count; i++) sb.Append("Parte " + (i + 1) + ": " + resumenesPrevios[i] + "\n");
        }
        Transcripcion_(sb, t, segmentos, p.Inicio, p.Fin);
        Intensidad_(sb, t, duracionActual, p.Inicio, p.Fin);
        return sb.ToString();
    }

    public static string InstruccionesFinal(OpcionesIA op)
    {
        return Rol +
            "Este video es largo y ya se analizó por partes. Recibes el resumen de cada parte y una lista de " +
            "CANDIDATOS (tramos posibles con su importancia). Arma el video final.\n\n" + EsquemaFinal + "Reglas:\n" +
            "- \"corte\": elige y ordena candidatos para que el video final dure entre la duración mínima y la máxima " +
            "(apunta a la ideal; suma antes de responder). Usa sus tiempos tal cual o recórtalos por dentro; no inventes tramos fuera de los candidatos. " +
            "Cada tramo lleva \"importancia\" de 1 a 10. Las REGLAS DEL CANAL y las INDICACIONES DEL EPISODIO son obligatorias. " +
            "Que la historia completa se entienda de principio a fin: no te saltes objetivos, decisiones ni " +
            "resultados importantes. Prefiere los de mayor importancia, pero mantén el ritmo y la variedad.\n" +
            (op.PermitirAcelerar ? ReglasAcelerar : SinAcelerar) +
            "- \"secciones\": cubren todo el video original (una por etapa de la historia).\n" + ReglasResto;
    }

    public static string MensajeFinal(Transcripcion t, double duracionActual, OpcionesIA op, List<Rango> partes,
                                      List<string> resumenes, List<Tramo> candidatos, List<Tramo> momentos)
    {
        StringBuilder sb = new StringBuilder();
        Cabecera(sb, t, duracionActual, op);
        sb.Append("\nResumen por partes:\n");
        for (int i = 0; i < partes.Count; i++)
            sb.Append("Parte " + (i + 1) + " (" + S(partes[i].Inicio) + "-" + S(partes[i].Fin) + " s): " +
                      (i < resumenes.Count ? resumenes[i] : "") + "\n");
        double suma = 0;
        foreach (Tramo c in candidatos) suma += c.DuracionFinal;
        sb.Append("\nCandidatos [inicio-fin] importancia acción: título — motivo (suman " + S(suma) + " s):\n");
        foreach (Tramo c in candidatos)
            sb.Append("[" + S(c.Inicio) + "-" + S(c.Fin) + "] " + c.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + " " +
                      (c.Acelerar ? "acelerar×" + c.Velocidad.ToString("0", CultureInfo.InvariantCulture) : "conservar") + ": " +
                      c.Titulo + " — " + c.Motivo + "\n");
        sb.Append("\nMomentos destacados encontrados [inicio-fin] puntuación: título — motivo:\n");
        foreach (Tramo m in momentos)
            sb.Append("[" + S(m.Inicio) + "-" + S(m.Fin) + "] " + m.Puntuacion.ToString("0", CultureInfo.InvariantCulture) + ": " +
                      m.Titulo + " — " + m.Motivo + "\n");
        return sb.ToString();
    }

    // Contexto de episodios anteriores: el resumen y las secciones de sus
    // respuestas de MomentosIA (opcional; solo para entender la historia).
    public static string ContextoDe(List<string> rutas)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string ruta in rutas)
        {
            try
            {
                object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
                object r = Json.Leer(Gemini.QuitarCercas(Json.Texto(o, "respuesta")));
                string nombre = Path.GetFileName(ruta).Replace(".vegascut-ia.json", "");
                StringBuilder ep = new StringBuilder();
                ep.Append("- " + nombre + ": " + Json.Texto(r, "resumen").Replace("\n", " ") + "\n");
                foreach (object s in Json.Lista(r, "secciones"))
                    ep.Append("  \u00b7 " + Json.Texto(s, "titulo") + ": " + Json.Texto(s, "descripcion") + "\n");
                string texto = ep.ToString();
                if (texto.Length > 3000) texto = texto.Substring(0, 3000) + "\u2026\n";
                sb.Append(texto);
            }
            catch { }
        }
        return sb.ToString();
    }

    // ------------------------------------------------------- revision

    public static string InstruccionesRevision()
    {
        return "Eres un revisor estricto de cortes de video. Recibes las REGLAS DEL CANAL, las INDICACIONES DEL " +
            "EPISODIO y la lista numerada de tramos que se van a conservar, con lo que se dice en cada uno.\n\n" +
            "Revisa cada tramo contra las reglas y las indicaciones:\n" +
            "- Si la mayor parte del tramo las incumple (por ejemplo, una conversaci\u00f3n personal o de vida amorosa), " +
            "m\u00e1rcalo con \"quitar\": true.\n" +
            "- Si solo una parte las incumple, deja \"quitar\": false y da \"inicio\" y \"fin\" (segundos) de la parte que " +
            "S\u00cd se puede conservar, dentro del tramo y en l\u00edmites de frase.\n" +
            "- Si cumple, no lo incluyas en la respuesta.\n\n" +
            "Responde SOLO con JSON: {\"tramos\": [{\"indice\": n, \"quitar\": true o false, \"inicio\": s, \"fin\": s, " +
            "\"motivo\": \"qu\u00e9 regla incumple\"}]}. Si todo cumple: {\"tramos\": []}.";
    }

    public static string MensajeRevision(Transcripcion t, ResultadoIA r, OpcionesIA op)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("REGLAS DEL CANAL:\n" + (op.ReglasCanal ?? "").Trim() + "\n");
        if (!String.IsNullOrEmpty(op.Instrucciones)) sb.Append("\nINDICACIONES DEL EPISODIO:\n" + op.Instrucciones.Trim() + "\n");
        sb.Append("\nTramos del corte:\n");
        List<Segmento> segmentos = t.SegmentosActuales();
        for (int i = 0; i < r.Corte.Count; i++)
        {
            Tramo c = r.Corte[i];
            if (!c.Elegido) continue;
            sb.Append("\n#" + i + " [" + S(c.Inicio) + "-" + S(c.Fin) + "] " + c.Titulo + "\n");
            StringBuilder texto = new StringBuilder();
            foreach (Segmento s in segmentos)
                if (s.Fin > c.Inicio && s.Inicio < c.Fin)
                    texto.Append("[" + S(s.Inicio) + "] " + t.Hablantes[s.Hablante].Nombre + ": " + s.Texto + "\n");
            string tx = texto.ToString();
            if (tx.Length > 2500) tx = tx.Substring(0, 2500) + "\u2026\n";
            sb.Append(tx);
        }
        return sb.ToString();
    }

    // Pico de cada bloque de "bloque" segundos, normalizado por pista, en la
    // linea de tiempo actual. Solo se listan los bloques con algo de sonido.
    public static List<string> Intensidad(Transcripcion t, double duracionActual, int bloque)
    {
        int n = (int)Math.Ceiling(duracionActual / bloque) + 1;
        double[] voz = new double[n], amb = new double[n];
        for (int ih = 0; ih < t.Hablantes.Count; ih++)
        {
            Hablante h = t.Hablantes[ih];
            if (h.Pico == null || h.Pico.Length == 0) continue;
            float[] orden = (float[])h.Pico.Clone();
            Array.Sort(orden);
            double bajo = orden[(int)(orden.Length * 0.10)], alto = orden[Math.Min(orden.Length - 1, (int)(orden.Length * 0.995))];
            if (alto - bajo < 3) continue;
            for (int s = 0; s < h.Pico.Length; s++)
            {
                double ahora = t.Mapear(ih, t.Inicio + s);
                if (double.IsNaN(ahora)) continue;
                int b = (int)(ahora / bloque);
                if (b < 0 || b >= n) continue;
                double v = Math.Max(0, Math.Min(10, (h.Pico[s] - bajo) / (alto - bajo) * 10));
                if (h.Voz) voz[b] = Math.Max(voz[b], v); else amb[b] = Math.Max(amb[b], v);
            }
        }
        List<string> r = new List<string>();
        for (int b = 0; b < n; b++)
            if (voz[b] >= 1 || amb[b] >= 1)
                r.Add((b * bloque) + ";" + Math.Round(voz[b]) + ";" + Math.Round(amb[b]));
        return r;
    }
}

// =====================================================================
// Orquesta las peticiones (una sola o por partes) y reintenta si hace falta.
// =====================================================================

public delegate string LlamadaIA(string instrucciones, string mensaje);

public class AsistenteIA
{
    // Videos mas largos que esto se analizan por partes.
    public double UmbralPartes = 35 * 60;
    public double TamanoParte = 20 * 60;
    public Action<string> Progreso = delegate { };
    readonly LlamadaIA llamar;

    public AsistenteIA(LlamadaIA llamar) { this.llamar = llamar; }

    // Pide y comprueba que sea JSON valido; un reintento si sale roto.
    string PedirJson(string instrucciones, string mensaje)
    {
        for (int intento = 1; ; intento++)
        {
            string r = llamar(instrucciones, mensaje);
            try { Json.Leer(Gemini.QuitarCercas(r)); return r; }
            catch (FormatException)
            {
                if (intento >= 2) throw new Exception("Gemini devolvió una respuesta que no se pudo leer dos veces seguidas.");
                Progreso("La respuesta llegó incompleta; reintentando…");
            }
        }
    }

    // Devuelve el JSON final con el mismo formato en ambos modos.
    public string Ejecutar(Transcripcion t, double total, OpcionesIA op)
    {
        if (total <= UmbralPartes)
        {
            try
            {
                Progreso("Gemini está analizando el video completo…");
                return PedirJson(PeticionIA.Instrucciones(op), PeticionIA.Mensaje(t, total, op));
            }
            catch (RespuestaCortada)
            {
                Progreso("La respuesta no cupo completa: se analizará por partes.");
            }
        }
        return PorPartes(t, total, op);
    }

    string PorPartes(Transcripcion t, double total, OpcionesIA op)
    {
        List<Segmento> segmentos = t.SegmentosActuales();
        List<Rango> partes = PeticionIA.Partes(segmentos, total, TamanoParte);
        List<string> resumenes = new List<string>();
        List<Tramo> candidatos = new List<Tramo>(), momentos = new List<Tramo>();
        string instrucciones = PeticionIA.InstruccionesParte(op);

        for (int i = 0; i < partes.Count; i++)
        {
            Progreso("Analizando la parte " + (i + 1) + " de " + partes.Count + " (" +
                     Formato.Tiempo(partes[i].Inicio) + "–" + Formato.Tiempo(partes[i].Fin) + ")…");
            string r = PedirJson(instrucciones, PeticionIA.MensajeParte(t, segmentos, total, op, i, partes, resumenes));
            object o = Json.Leer(Gemini.QuitarCercas(r));
            resumenes.Add(Json.Texto(o, "resumen"));
            // Los candidatos y momentos se leen con el mismo lector del resultado
            // final, limitados a esta parte.
            ResultadoIA parte = ResultadoIA.Leer("{\"corte\": " + Json.Escribir(Json.Lista(o, "candidatos"), false) +
                                                  ", \"momentos\": " + Json.Escribir(Json.Lista(o, "momentos"), false) + "}", total);
            foreach (Tramo c in parte.Corte)
            {
                c.Inicio = Math.Max(c.Inicio, partes[i].Inicio);
                c.Fin = Math.Min(c.Fin, partes[i].Fin);
                if (c.Fin - c.Inicio >= 0.5) candidatos.Add(c);
            }
            momentos.AddRange(parte.Momentos);
        }

        Progreso("Armando el video final con " + candidatos.Count + " candidatos\u2026");
        string final = PedirJson(PeticionIA.InstruccionesFinal(op),
                                 PeticionIA.MensajeFinal(t, total, op, partes, resumenes, candidatos, momentos));
        // Se guardan los candidatos junto al resultado para poder completar el
        // corte si queda corto.
        Dictionary<string, object> d = Json.Leer(Gemini.QuitarCercas(final)) as Dictionary<string, object>;
        if (d == null) return final;
        List<object> lista = new List<object>();
        foreach (Tramo c in candidatos)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["inicio"] = c.Inicio; x["fin"] = c.Fin; x["importancia"] = c.Puntuacion;
            x["accion"] = c.Acelerar ? "acelerar" : "conservar"; x["velocidad"] = c.Velocidad;
            x["titulo"] = c.Titulo; x["motivo"] = c.Motivo;
            lista.Add(x);
        }
        d["candidatos"] = lista;
        return Json.Escribir(d);
    }

    // Segunda opinion: revisa el corte contra las reglas. Devuelve el JSON de
    // la revision ("" si fallo; la revision es opcional).
    public string Revisar(Transcripcion t, ResultadoIA r, OpcionesIA op)
    {
        if (r.Corte.Count == 0) return "";
        Progreso("Revisando que el corte cumpla las reglas\u2026");
        try { return PedirJson(PeticionIA.InstruccionesRevision(), PeticionIA.MensajeRevision(t, r, op)); }
        catch (Exception ex) { Progreso("No se pudo revisar (" + ex.Message + ")."); return ""; }
    }
}
