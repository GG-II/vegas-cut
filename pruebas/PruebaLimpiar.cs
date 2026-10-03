using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

// Pruebas de LimpiarVegasCut: que solo ofrezca lo que ningun proyecto usa.
class PruebaLimpiar
{
    static int fallos;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }

    static string Crear(string ruta, int bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ruta));
        File.WriteAllBytes(ruta, new byte[bytes]);
        return ruta;
    }

    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "vc-limpiar-" + Guid.NewGuid().ToString("N"));
        string temp = Path.Combine(dir, "temp"), serie = Path.Combine(dir, "Serie");
        try
        {
            string cap = Path.Combine(serie, "Cap1"), narr = Path.Combine(cap, "S01E01 CAP.vegascut-narracion");
            string n1 = Crear(Path.Combine(narr, "N01.wav"), 100), n2 = Crear(Path.Combine(narr, "N02.wav"), 200);
            string otraNarr = Path.Combine(serie, "Cap2", "S01E02 CAP.vegascut-narracion");
            string m1 = Crear(Path.Combine(otraNarr, "N01.wav"), 50);
            string r1 = Crear(Path.Combine(cap, "Render 1.wav"), 300), r2 = Crear(Path.Combine(cap, "Rendered 2.mp4"), 400);
            string grab = Crear(Path.Combine(cap, "grabacion.wav"), 500);
            string sfkSuelto = Crear(Path.Combine(cap, "viejo.wav.sfk"), 10), sfkBien = Crear(Path.Combine(cap, "grabacion.wav.sfk"), 10);
            string proxy = Crear(Path.Combine(cap, "grabacion.mp4.sfvp0"), 1000);
            string bakViejo = Crear(Path.Combine(cap, "S01E01.veg.bak"), 20), bakNuevo = Crear(Path.Combine(cap, "S01E01 CAP.veg.bak"), 20);
            File.SetLastWriteTime(bakViejo, DateTime.Now.AddDays(-10));
            // El proyecto guarda sus rutas en UTF-16.
            string veg = Path.Combine(cap, "S01E01 CAP.veg");
            File.WriteAllBytes(veg, Encoding.Unicode.GetBytes("xx K:\\Serie\\Cap1\\S01E01 CAP.vegascut-narracion\\N01.wav yy K:\\Serie\\Cap1\\Render 1.wav K:\\Serie\\Cap1\\grabacion.wav"));

            string tv = Crear(Path.Combine(temp, "vegas-cut-viejo.wav"), 700), tn = Crear(Path.Combine(temp, "vegas-cut-nuevo.wav"), 700);
            string tw = Path.Combine(temp, "vegas-cut-whisper-abc"); Crear(Path.Combine(tw, "x.json"), 5);
            Crear(Path.Combine(temp, "otro.wav"), 5);
            File.SetLastWriteTime(tv, DateTime.Now.AddDays(-2));
            Directory.SetLastWriteTime(tw, DateTime.Now.AddDays(-3));

            List<ArchivoLimpiar> l = LogicaLimpiar.TemporalesHuerfanos(temp, DateTime.Now);
            string refs;
            l.AddRange(LogicaLimpiar.Buscar(serie, new List<string>(), DateTime.Now, out refs));
            List<string> rutas = new List<string>();
            foreach (ArchivoLimpiar a in l) rutas.Add(a.Ruta);
            Verificar(rutas.Contains(tv) && rutas.Contains(tw) && !rutas.Contains(tn) && l.Count(LogicaLimpiar.Temporales) == 2,
                      "Temporales: solo los de vegas-cut con más de un día (los de ahora pueden estar en uso)");
            Verificar(rutas.Contains(n2) && !rutas.Contains(n1) && rutas.Contains(m1),
                      "Narración provisional: la que ningún .veg usa (N01 de otro capítulo no cuenta como usada)");
            Verificar(rutas.Contains(r2) && !rutas.Contains(r1) && !rutas.Contains(grab), "Renders: los que ningún .veg usa; las grabaciones nunca");
            Verificar(rutas.Contains(sfkSuelto) && !rutas.Contains(sfkBien), "Picos .sfk: solo los de archivos que ya no existen");
            ArchivoLimpiar ap = l.Find(delegate (ArchivoLimpiar a) { return a.Ruta == proxy; });
            ArchivoLimpiar ab = l.Find(delegate (ArchivoLimpiar a) { return a.Ruta == bakViejo; });
            Verificar(ap != null && !ap.Marcado && ab != null && !ab.Marcado && !rutas.Contains(bakNuevo) && !rutas.Contains(veg),
                      "Proxies y autoguardados de más de una semana: se ofrecen sin marcar; los proyectos nunca");
            Verificar(LogicaLimpiar.Legible(refs, new List<string> { grab }) && !LogicaLimpiar.Legible(refs, new List<string> { Path.Combine(cap, "nada.mp4") }) &&
                      LogicaLimpiar.Usado(n2, refs, new List<string> { n2 }),
                      "Seguridad: si los medios del proyecto abierto no aparecen en los .veg, no se ofrece nada de la carpeta; lo abierto cuenta como usado");

            string ilegible = Path.Combine(dir, "Ilegible");
            Crear(Path.Combine(ilegible, "S01E09 CAP.vegascut-narracion", "N01.wav"), 10);
            File.WriteAllBytes(Path.Combine(ilegible, "S01E09 CAP.veg"), new byte[] { 1, 2, 3, 4 });
            string ri;
            Verificar(LogicaLimpiar.Buscar(ilegible, new List<string>(), DateTime.Now, out ri).Count == 0 && ri == null,
                      "Seguridad: si los .veg no dejan leer ninguna ruta, no se ofrece nada de esa carpeta");

            List<string> errores = new List<string>();
            long liberado;
            int n = LogicaLimpiar.Limpiar(l, delegate (string r) { if (Directory.Exists(r)) Directory.Delete(r, true); else File.Delete(r); }, errores, out liberado);
            Verificar(!File.Exists(n2) && File.Exists(n1) && Directory.Exists(narr) && !Directory.Exists(otraNarr) && File.Exists(proxy) && File.Exists(grab) &&
                      File.Exists(veg) && errores.Count == 0 && n == 6 && liberado == 700 + 5 + 200 + 50 + 400 + 10,
                      "Limpiar: borra solo lo marcado y la carpeta de narración que queda vacía (" + LogicaLimpiar.Bytes(liberado) + ")");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " fallos.");
        return fallos == 0 ? 0 : 1;
    }
}

static class Ext
{
    public static int Count(this List<ArchivoLimpiar> l, string tipo) { return l.FindAll(delegate (ArchivoLimpiar a) { return a.Tipo == tipo; }).Count; }
}
