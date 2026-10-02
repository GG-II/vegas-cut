// ReubicarMarcadores.cs
// Script para VEGAS Pro 20 (Herramientas > Secuencias de comandos > Ejecutar).
// Vuelve a poner sobre su clip los marcadores y regiones que creo
// MomentosIA, aunque hayas movido, cortado o reordenado los clips.
// Cada marcador se guardo con el archivo y el segundo del archivo que tenia
// debajo (<proyecto>.vegascut-marcas.json); aqui se busca donde quedo ese
// segundo en la linea de tiempo. Conviene asignarle un atajo de teclado.
//
// Escrito en C# 5 porque Vegas compila los scripts con el compilador clasico.
//
// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.

using System.Collections.Generic;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using System;
using ScriptPortal.Vegas;
using Region = ScriptPortal.Vegas.Region;

// ---- src/marcadores/Reubicar.cs ----

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Project p = vegas.Project;
        if (String.IsNullOrEmpty(p.FilePath) || Anclas.Cargar(p.FilePath).Count == 0)
        {
            MessageBox.Show("Este proyecto no tiene marcadores anclados.\n\nSe crean con MomentosIA " +
                            "(\u201cCrear regiones y marcadores\u201d o \u201cAplicar corte\u201d).", "Reubicar marcadores");
            return;
        }
        int perdidos, revisados, movidos;
        using (UndoBlock deshacer = new UndoBlock("Reubicar marcadores"))
            movidos = Anclas.Reubicar(p, p.FilePath, out perdidos, out revisados);
        MessageBox.Show(
            movidos + " de " + revisados + " marcadores y regiones vuelven a estar sobre su clip." +
            (perdidos > 0 ? "\n" + perdidos + " no encontraron su clip (esa parte del video ya no est\u00e1) y se quedaron donde estaban." : "") +
            "\n\nCtrl+Z lo deshace.", "Reubicar marcadores");
    }
}

// ---- src/comun/Anclas.cs ----

// =====================================================================
// Marcadores anclados a los clips
//
// Vegas pone los marcadores en la linea de tiempo, no en los clips. Para que
// sigan a su clip, al crearlos se guarda en <proyecto>.vegascut-marcas.json
// a que archivo y a que segundo de ese archivo corresponden. Despues, el
// script ReubicarMarcadores busca el clip que tiene ese segundo y vuelve a
// poner el marcador encima, aunque hayas movido, cortado o reordenado clips.
// =====================================================================

public class Ancla
{
    public string Etiqueta = "", Media = "", MediaFin = "";
    public double Fuente, FuenteFin = -1;   // segundos dentro del archivo
    public bool Region;
}

public static class Anclas
{
    const double Tol = 0.0005;

    public static string RutaPara(string veg)
    {
        if (String.IsNullOrEmpty(veg)) return null;
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + ".vegascut-marcas.json");
    }

    static double S(Timecode t) { return t.ToMilliseconds() / 1000.0; }

    // Clip de video (si no hay, de audio) que esta en el instante t, con un
    // archivo real (no textos ni colores generados).
    static TrackEvent ClipEn(Project p, double t)
    {
        foreach (bool video in new bool[] { true, false })
            foreach (Track pista in p.Tracks)
            {
                if (pista.IsAudio() == video) continue;
                foreach (TrackEvent e in pista.Events)
                {
                    if (S(e.Start) > t + Tol || S(e.End) <= t + Tol) continue;
                    Take toma = e.ActiveTake;
                    if (toma == null || toma.Media == null || toma.Media.IsGenerated()) continue;
                    return e;
                }
            }
        return null;
    }

    static bool Fuente(Project p, double t, out string media, out double fuente)
    {
        media = ""; fuente = 0;
        TrackEvent e = ClipEn(p, t);
        if (e == null) return false;
        media = e.ActiveTake.Media.FilePath;
        fuente = S(e.ActiveTake.Offset) + (t - S(e.Start)) * e.PlaybackRate;
        return true;
    }

    // Crea el ancla de un marcador (fin < 0) o de una region.
    public static Ancla Crear(Project p, double t, double fin, string etiqueta)
    {
        Ancla a = new Ancla();
        a.Etiqueta = etiqueta;
        a.Region = fin >= 0;
        if (!Fuente(p, t, out a.Media, out a.Fuente)) return null;
        if (a.Region && !Fuente(p, Math.Max(t, fin - 0.001), out a.MediaFin, out a.FuenteFin)) a.FuenteFin = -1;
        return a;
    }

    public static List<Ancla> Cargar(string veg)
    {
        List<Ancla> r = new List<Ancla>();
        string ruta = RutaPara(veg);
        if (ruta == null || !File.Exists(ruta)) return r;
        try
        {
            foreach (object x in Json.Lista(Json.Leer(File.ReadAllText(ruta, Encoding.UTF8)), "anclas"))
            {
                Ancla a = new Ancla();
                a.Etiqueta = Json.Texto(x, "etiqueta");
                a.Media = Json.Texto(x, "media");
                a.MediaFin = Json.Texto(x, "mediaFin");
                a.Fuente = Json.Numero(x, "fuente", 0);
                a.FuenteFin = Json.Numero(x, "fuenteFin", -1);
                a.Region = Json.Texto(x, "region") == "True";
                r.Add(a);
            }
        }
        catch { }
        return r;
    }

    // Agrega anclas nuevas (reemplaza las que tengan la misma etiqueta).
    public static void Guardar(string veg, List<Ancla> nuevas)
    {
        string ruta = RutaPara(veg);
        if (ruta == null) return;
        List<Ancla> todas = Cargar(veg);
        foreach (Ancla n in nuevas)
        {
            if (n == null) continue;
            todas.RemoveAll(delegate (Ancla a) { return a.Etiqueta == n.Etiqueta && a.Region == n.Region; });
            todas.Add(n);
        }
        List<object> lista = new List<object>();
        foreach (Ancla a in todas)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["etiqueta"] = a.Etiqueta; d["region"] = a.Region;
            d["media"] = a.Media; d["fuente"] = Math.Round(a.Fuente, 3);
            if (a.Region) { d["mediaFin"] = a.MediaFin; d["fuenteFin"] = Math.Round(a.FuenteFin, 3); }
            lista.Add(d);
        }
        Dictionary<string, object> raiz = new Dictionary<string, object>();
        raiz["formato"] = "vegas-cut-marcas";
        raiz["anclas"] = lista;
        try { File.WriteAllText(ruta, Json.Escribir(raiz), new UTF8Encoding(false)); } catch { }
    }

    // Instantes de la linea de tiempo donde se ve ese segundo del archivo
    // (puede haber varios si el clip esta repetido).
    static List<double> Donde(Project p, string media, double fuente)
    {
        List<double> r = new List<double>();
        foreach (Track pista in p.Tracks)
            foreach (TrackEvent e in pista.Events)
            {
                Take toma = e.ActiveTake;
                if (toma == null || toma.Media == null || !String.Equals(toma.Media.FilePath, media, StringComparison.OrdinalIgnoreCase)) continue;
                double desde = S(toma.Offset), largo = (S(e.End) - S(e.Start)) * e.PlaybackRate;
                if (fuente >= desde - Tol && fuente < desde + largo - Tol)
                    r.Add(S(e.Start) + (fuente - desde) / e.PlaybackRate);
            }
        return r;
    }

    static double MasCerca(List<double> l, double t)
    {
        double mejor = l[0];
        foreach (double x in l) if (Math.Abs(x - t) < Math.Abs(mejor - t)) mejor = x;
        return mejor;
    }

    // Vuelve a poner cada marcador/region anclado sobre su clip. Devuelve
    // cuantos se movieron; "perdidos" son los que ya no tienen clip (esa
    // parte se borro).
    public static int Reubicar(Project p, string veg, out int perdidos, out int revisados)
    {
        perdidos = 0; revisados = 0;
        List<Ancla> anclas = Cargar(veg);
        Dictionary<string, Ancla> marcas = new Dictionary<string, Ancla>(), regiones = new Dictionary<string, Ancla>();
        foreach (Ancla a in anclas) (a.Region ? regiones : marcas)[a.Etiqueta] = a;
        int movidos = 0;

        List<Marker> lista = new List<Marker>();
        foreach (Marker m in p.Markers) lista.Add(m);
        foreach (Region r in p.Regions) lista.Add(r);
        foreach (Marker m in lista)
        {
            Region region = m as Region;
            Ancla a;
            if (!(region != null ? regiones : marcas).TryGetValue(m.Label ?? "", out a)) continue;
            revisados++;
            List<double> donde = Donde(p, a.Media, a.Fuente);
            if (donde.Count == 0) { perdidos++; continue; }
            double actual = S(m.Position), nuevo = MasCerca(donde, actual);
            bool cambio = Math.Abs(nuevo - actual) > 0.001;
            if (region != null && a.FuenteFin >= 0)
            {
                List<double> fines = Donde(p, a.MediaFin, a.FuenteFin);
                if (fines.Count > 0)
                {
                    double fin = MasCerca(fines, nuevo + S(region.Length));
                    if (fin > nuevo + 0.01 && Math.Abs((fin - nuevo) - S(region.Length)) > 0.001)
                    {
                        try { region.Length = Timecode.FromMilliseconds((fin - nuevo) * 1000); cambio = true; } catch { }
                    }
                }
            }
            if (Math.Abs(nuevo - actual) > 0.001)
            {
                try { m.Position = Timecode.FromMilliseconds(nuevo * 1000); } catch { }
            }
            if (cambio) movidos++;
        }
        return movidos;
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

    // El valor tal cual (o null): para booleanos o lo que pueda faltar.
    public static object Valor(object o, string clave)
    {
        Dictionary<string, object> d = o as Dictionary<string, object>;
        object v;
        return d != null && d.TryGetValue(clave, out v) ? v : null;
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
