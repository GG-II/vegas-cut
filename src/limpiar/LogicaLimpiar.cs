using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// =====================================================================
// Limpiar: lo que se junta al trabajar y ya no usa ningun proyecto.
//  - Temporales de vegas-cut en %TEMP% (si Vegas se cerro a mitad).
//  - Narracion provisional (.vegascut-narracion) que ningun .veg usa.
//  - Renders de Vegas («Renderizar en nueva pista»...) que ningun .veg usa.
//  - Picos .sfk de archivos que ya no existen.
//  - Proxies (.sfvp0) y autoguardados (.bak) viejos: opcionales.
// Todo va a la Papelera de reciclaje.
// =====================================================================

public class ArchivoLimpiar
{
    public string Ruta = "", Tipo = "", PorQue = "";
    public long Bytes;
    public bool Carpeta, Marcado = true;
}

public static class LogicaLimpiar
{
    public const string Temporales = "Temporal de vegas-cut", Narracion = "Narración provisional", Render = "Render de Vegas",
                        Picos = "Picos .sfk sueltos", Proxy = "Proxy de video", Autoguardado = "Autoguardado viejo";

    static readonly string[] Medios = { ".wav", ".mp3", ".mp4", ".mov", ".avi", ".m4a", ".flac", ".ogg", ".mxf", ".mkv", ".aac", ".w64" };

    static long Tamano(string ruta, bool carpeta)
    {
        try
        {
            if (!carpeta) return new FileInfo(ruta).Length;
            long t = 0;
            foreach (string f in Directory.GetFiles(ruta, "*", SearchOption.AllDirectories)) try { t += new FileInfo(f).Length; } catch { }
            return t;
        }
        catch { return 0; }
    }

    public static string Bytes(long b)
    {
        if (b >= 1L << 30) return (b / (double)(1L << 30)).ToString("0.0") + " GB";
        if (b >= 1L << 20) return (b / (double)(1L << 20)).ToString("0.0") + " MB";
        return Math.Max(1, b / 1024) + " KB";
    }

    // Temporales de vegas-cut con mas de un dia (los de ahora pueden estar en uso).
    public static List<ArchivoLimpiar> TemporalesHuerfanos(string temp, DateTime ahora)
    {
        List<ArchivoLimpiar> r = new List<ArchivoLimpiar>();
        if (String.IsNullOrEmpty(temp) || !Directory.Exists(temp)) return r;
        try
        {
            foreach (string f in Directory.GetFiles(temp, "vegas-cut-*"))
                if (ahora - File.GetLastWriteTime(f) > TimeSpan.FromDays(1))
                    r.Add(new ArchivoLimpiar { Ruta = f, Tipo = Temporales, Bytes = Tamano(f, false), PorQue = "quedó de una transcripción que no terminó" });
            foreach (string d in Directory.GetDirectories(temp, "vegas-cut-*"))
                if (ahora - Directory.GetLastWriteTime(d) > TimeSpan.FromDays(1))
                    r.Add(new ArchivoLimpiar { Ruta = d, Carpeta = true, Tipo = Temporales, Bytes = Tamano(d, true), PorQue = "carpeta de Whisper que no se borró" });
        }
        catch { }
        return r;
    }

    // Todo lo que nombran los .veg de la carpeta (las rutas van en UTF-16 y a veces en UTF-8).
    public static string Referencias(List<string> vegs)
    {
        StringBuilder sb = new StringBuilder();
        foreach (string v in vegs)
        {
            try
            {
                byte[] b = File.ReadAllBytes(v);
                sb.Append(Encoding.Unicode.GetString(b)).Append('\n');
                sb.Append(Encoding.UTF8.GetString(b)).Append('\n');
            }
            catch { }
        }
        return sb.ToString().ToLowerInvariant();
    }

    // Lo usa algun proyecto: su nombre aparece en algun .veg (o en el proyecto abierto).
    public static bool Usado(string ruta, string referencias, List<string> abiertos) { return Usado(ruta, referencias, abiertos, true); }

    // exigirCarpeta: el nombre solo no basta (N01.wav se repite en cada capitulo).
    public static bool Usado(string ruta, string referencias, List<string> abiertos, bool exigirCarpeta)
    {
        string nombre = Path.GetFileName(ruta).ToLowerInvariant();
        string dir = Path.GetFileName(Path.GetDirectoryName(ruta) ?? "").ToLowerInvariant();
        foreach (string a in abiertos) if (String.Equals(a, ruta, StringComparison.OrdinalIgnoreCase)) return true;
        // Con su carpeta, para no confundir N01.wav de un capitulo con el de otro.
        if (dir.Length > 0 && (referencias.Contains(dir + "\\" + nombre) || referencias.Contains(dir + "/" + nombre))) return true;
        return (dir.Length == 0 || !exigirCarpeta) && referencias.Contains(nombre);
    }

    // Si se pudieron leer las rutas de los proyectos: cada medio del proyecto
    // abierto (ya guardado) tiene que aparecer en las referencias.
    public static bool Legible(string referencias, List<string> abiertosGuardados)
    {
        foreach (string a in abiertosGuardados)
            if (!referencias.Contains(Path.GetFileName(a).ToLowerInvariant())) return false;
        return true;
    }

    static bool EsRender(string nombre)
    {
        string n = nombre.ToLowerInvariant();
        return n.Contains("render") || n.Contains("renderizado") || n.Contains("rendered");
    }

    // Revisa la carpeta (y subcarpetas): lo que se puede borrar.
    public static List<ArchivoLimpiar> Buscar(string carpeta, List<string> abiertos, DateTime ahora)
    {
        string refs;
        return Buscar(carpeta, abiertos, ahora, out refs);
    }

    public static List<ArchivoLimpiar> Buscar(string carpeta, List<string> abiertos, DateTime ahora, out string refs)
    {
        List<ArchivoLimpiar> r = new List<ArchivoLimpiar>();
        refs = "";
        if (String.IsNullOrEmpty(carpeta) || !Directory.Exists(carpeta)) return r;
        List<string> todos = new List<string>();
        try { todos.AddRange(Directory.GetFiles(carpeta, "*", SearchOption.AllDirectories)); } catch { }
        List<string> vegs = todos.FindAll(delegate (string f) { return f.EndsWith(".veg", StringComparison.OrdinalIgnoreCase); });
        refs = Referencias(vegs);
        // Si los proyectos no dejan leer ni una ruta de medios, no se ofrece nada (podria estar todo en uso).
        bool algo = vegs.Count == 0;
        foreach (string m in Medios) if (refs.Contains(m)) { algo = true; break; }
        if (!algo) { refs = null; return r; }

        foreach (string f in todos)
        {
            string ext = Path.GetExtension(f).ToLowerInvariant();
            string dir = Path.GetFileName(Path.GetDirectoryName(f) ?? "");
            string nombre = Path.GetFileName(f);
            if (dir.EndsWith(".vegascut-narracion", StringComparison.OrdinalIgnoreCase) && ext == ".wav")
            {
                if (!Usado(f, refs, abiertos))
                    r.Add(new ArchivoLimpiar { Ruta = f, Tipo = Narracion, Bytes = Tamano(f, false), PorQue = "ningún proyecto la usa" });
            }
            else if (Array.IndexOf(Medios, ext) >= 0 && EsRender(nombre))
            {
                if (!Usado(f, refs, abiertos, false))
                    r.Add(new ArchivoLimpiar { Ruta = f, Tipo = Render, Bytes = Tamano(f, false), PorQue = "render que ningún proyecto usa" });
            }
            else if (ext == ".sfk")
            {
                string medio = f.Substring(0, f.Length - 4);
                if (!File.Exists(medio))
                    r.Add(new ArchivoLimpiar { Ruta = f, Tipo = Picos, Bytes = Tamano(f, false), PorQue = "su audio ya no existe" });
            }
            else if (ext == ".sfvp0" || ext == ".sfvp1")
                r.Add(new ArchivoLimpiar { Ruta = f, Tipo = Proxy, Bytes = Tamano(f, false), Marcado = false,
                                           PorQue = "Vegas lo regenera (editar va más lento mientras)" });
            else if (ext == ".bak" && ahora - File.GetLastWriteTime(f) > TimeSpan.FromDays(7))
                r.Add(new ArchivoLimpiar { Ruta = f, Tipo = Autoguardado, Bytes = Tamano(f, false), Marcado = false, PorQue = "de hace más de una semana" });
        }
        r.Sort(delegate (ArchivoLimpiar a, ArchivoLimpiar b)
        {
            int t = String.Compare(a.Tipo, b.Tipo, StringComparison.Ordinal);
            return t != 0 ? t : String.Compare(a.Ruta, b.Ruta, StringComparison.OrdinalIgnoreCase);
        });
        return r;
    }

    // Carpetas .vegascut-narracion que se quedan vacias despues de limpiar.
    public static List<string> CarpetasVacias(List<ArchivoLimpiar> borrados)
    {
        List<string> r = new List<string>();
        foreach (ArchivoLimpiar a in borrados)
        {
            string d = Path.GetDirectoryName(a.Ruta);
            if (a.Tipo != Narracion || r.Contains(d)) continue;
            try { if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0) r.Add(d); } catch { }
        }
        return r;
    }

    // ------------------------------------------------- a la Papelera

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string pTo;
        public ushort fFlags;
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    // A la Papelera de reciclaje (se puede recuperar).
    public static void Papelera(string ruta)
    {
        SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
        op.wFunc = 3;                         // FO_DELETE
        op.pFrom = ruta + "\0\0";
        op.fFlags = 0x0040 | 0x0010 | 0x0004 | 0x0400;   // ALLOWUNDO | NOCONFIRMATION | SILENT | NOERRORUI
        int e = SHFileOperation(ref op);
        if (e != 0 || op.fAnyOperationsAborted) throw new IOException("No se pudo mandar a la Papelera (código " + e + ").");
    }

    // Manda los marcados a la Papelera (o a donde diga "borrar", para las pruebas).
    public static int Limpiar(List<ArchivoLimpiar> lista, Action<string> borrar, List<string> errores, out long liberado)
    {
        int n = 0;
        liberado = 0;
        List<ArchivoLimpiar> hechos = new List<ArchivoLimpiar>();
        foreach (ArchivoLimpiar a in lista)
        {
            if (!a.Marcado) continue;
            try { borrar(a.Ruta); n++; liberado += a.Bytes; hechos.Add(a); }
            catch (Exception ex) { errores.Add(Path.GetFileName(a.Ruta) + ": " + ex.Message); }
        }
        foreach (string d in CarpetasVacias(hechos)) try { borrar(d); } catch { }
        return n;
    }
}
