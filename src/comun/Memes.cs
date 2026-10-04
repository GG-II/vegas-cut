using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;

// =====================================================================
// Memes: una carpeta de imagenes, videos y sonidos con lo que es cada uno
// (descripcion, tags, cuando usarlo) y cuando se uso, para ponerlos en el
// capitulo con ritmo y sin repetirlos entre videos.
// =====================================================================

public class UsoMeme
{
    public string Capitulo = "", Fecha = "";
}

public class Meme
{
    public string Ruta = "";          // relativa a la carpeta
    public string Tipo = "imagen";    // imagen, gif, video, sonido
    public string Descripcion = "", Uso = "";
    public List<string> Tags = new List<string>();
    public List<UsoMeme> Usos = new List<UsoMeme>();
    public bool Descrito { get { return Descripcion.Trim().Length > 0; } }
    public string Nombre { get { return Path.GetFileNameWithoutExtension(Ruta); } }
}

public class BibliotecaMemes
{
    public const string NombreIndice = "memes-indice.json";
    public static readonly string[] Imagenes = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };
    public static readonly string[] Gifs = { ".gif" };
    public static readonly string[] Videos = { ".mp4", ".mov", ".webm", ".avi", ".mkv", ".m4v" };
    public static readonly string[] Sonidos = { ".mp3", ".wav", ".ogg", ".m4a", ".flac" };

    public string Carpeta = "";
    public List<Meme> Memes = new List<Meme>();

    public static string TipoDe(string ruta)
    {
        string e = Path.GetExtension(ruta).ToLowerInvariant();
        if (Array.IndexOf(Imagenes, e) >= 0) return "imagen";
        if (Array.IndexOf(Gifs, e) >= 0) return "gif";
        if (Array.IndexOf(Videos, e) >= 0) return "video";
        if (Array.IndexOf(Sonidos, e) >= 0) return "sonido";
        return "";
    }

    public string Completa(Meme m) { return Path.Combine(Carpeta, m.Ruta); }

    public Meme Buscar(string ruta)
    {
        foreach (Meme m in Memes) if (String.Equals(m.Ruta, ruta, StringComparison.OrdinalIgnoreCase)) return m;
        return null;
    }

    // Lee la carpeta (y subcarpetas). Lo que ya estaba descrito se conserva; lo que ya no existe se quita.
    // Las subcarpetas cuentan como tags («Reacciones/Risa» → reacciones, risa).
    public int Escanear()
    {
        List<Meme> nuevos = new List<Meme>();
        int agregados = 0;
        string raiz = Carpeta.TrimEnd('\\', '/');
        foreach (string f in Directory.GetFiles(Carpeta, "*", SearchOption.AllDirectories))
        {
            string tipo = TipoDe(f);
            if (tipo.Length == 0) continue;
            string rel = f.Substring(raiz.Length).TrimStart('\\', '/');
            Meme m = Buscar(rel);
            if (m == null)
            {
                m = new Meme { Ruta = rel, Tipo = tipo };
                string dir = Path.GetDirectoryName(rel) ?? "";
                foreach (string parte in dir.Split('\\', '/'))
                    if (parte.Trim().Length > 0 && !m.Tags.Contains(parte.Trim().ToLowerInvariant())) m.Tags.Add(parte.Trim().ToLowerInvariant());
                agregados++;
            }
            nuevos.Add(m);
        }
        nuevos.Sort(delegate (Meme a, Meme b) { return String.Compare(a.Ruta, b.Ruta, StringComparison.OrdinalIgnoreCase); });
        Memes = nuevos;
        return agregados;
    }

    // ---------------------------------------------------------- guardar

    public void Guardar()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["formato"] = "vegas-cut-memes";
        List<object> l = new List<object>();
        foreach (Meme m in Memes)
        {
            Dictionary<string, object> x = new Dictionary<string, object>();
            x["ruta"] = m.Ruta; x["tipo"] = m.Tipo; x["descripcion"] = m.Descripcion; x["uso"] = m.Uso;
            x["tags"] = new List<object>(m.Tags.ToArray());
            List<object> us = new List<object>();
            foreach (UsoMeme u in m.Usos)
            {
                Dictionary<string, object> y = new Dictionary<string, object>();
                y["capitulo"] = u.Capitulo; y["fecha"] = u.Fecha;
                us.Add(y);
            }
            x["usos"] = us;
            l.Add(x);
        }
        d["memes"] = l;
        File.WriteAllText(Path.Combine(Carpeta, NombreIndice), Json.Escribir(d), new UTF8Encoding(false));
    }

    public static BibliotecaMemes Cargar(string carpeta)
    {
        BibliotecaMemes b = new BibliotecaMemes { Carpeta = carpeta };
        string ruta = Path.Combine(carpeta, NombreIndice);
        if (!File.Exists(ruta)) return b;
        object o = Json.Leer(File.ReadAllText(ruta, Encoding.UTF8));
        foreach (object x in Json.Lista(o, "memes"))
        {
            Meme m = new Meme();
            m.Ruta = Json.Texto(x, "ruta"); m.Tipo = Json.Texto(x, "tipo"); m.Descripcion = Json.Texto(x, "descripcion"); m.Uso = Json.Texto(x, "uso");
            if (m.Tipo.Length == 0) m.Tipo = TipoDe(m.Ruta);
            foreach (object y in Json.Lista(x, "tags")) if (y is string) m.Tags.Add((string)y);
            foreach (object y in Json.Lista(x, "usos")) m.Usos.Add(new UsoMeme { Capitulo = Json.Texto(y, "capitulo"), Fecha = Json.Texto(y, "fecha") });
            if (m.Ruta.Length > 0) b.Memes.Add(m);
        }
        return b;
    }

    public static List<string> LeerTags(string texto)
    {
        List<string> r = new List<string>();
        foreach (string t in (texto ?? "").Split(',', ';', '#'))
        {
            string x = t.Trim().ToLowerInvariant();
            if (x.Length > 0 && !r.Contains(x)) r.Add(x);
        }
        return r;
    }

    // ------------------------------------------------ describir con IA

    // Miniatura JPEG para mandarla a Gemini (null si no se pudo leer).
    public static byte[] Miniatura(string ruta, int lado)
    {
        try
        {
            using (Image im = Image.FromFile(ruta))
            {
                double f = Math.Min(1.0, (double)lado / Math.Max(im.Width, im.Height));
                int w = Math.Max(1, (int)(im.Width * f)), h = Math.Max(1, (int)(im.Height * f));
                using (Bitmap b = new Bitmap(w, h))
                {
                    using (Graphics g = Graphics.FromImage(b)) { g.Clear(Color.White); g.DrawImage(im, 0, 0, w, h); }
                    using (MemoryStream ms = new MemoryStream()) { b.Save(ms, ImageFormat.Jpeg); return ms.ToArray(); }
                }
            }
        }
        catch { return null; }
    }

    public List<Meme> SinDescribir()
    {
        return Memes.FindAll(delegate (Meme m) { return !m.Descrito; });
    }

    public static string InstruccionesDescribir()
    {
        return "Eres el editor de una serie de YouTube de Minecraft con amigos (estilo anime de JoJo, mucho humor). Te paso memes de " +
               "la carpeta del editor: las imágenes van adjuntas en el mismo orden que la lista; de los videos y sonidos solo tienes " +
               "el nombre del archivo y su carpeta. Para cada uno di:\n" +
               "- \"descripcion\": qué es y qué se ve/oye, en una frase (si es un meme conocido, cuál).\n" +
               "- \"tags\": 3 a 6 palabras (emoción, reacción, tipo de chiste…).\n" +
               "- \"uso\": en qué momento de un gameplay queda bien (tras un fallo, una muerte, una sorpresa, un chiste, una victoria...).\n" +
               "Si de un video o sonido no sabes qué es por su nombre, no lo pongas (mejor nada que inventar).\n" +
               "Responde SOLO con JSON: {\"memes\": [{\"id\": n, \"descripcion\": \"...\", \"tags\": [\"...\"], \"uso\": \"...\"}]}";
    }

    // Mensaje para un lote; "imagenes" recibe las miniaturas en el orden de la lista.
    public string MensajeDescribir(List<Meme> lote, List<KeyValuePair<string, byte[]>> imagenes)
    {
        StringBuilder sb = new StringBuilder("MEMES [id] tipo | archivo | carpeta | (imagen adjunta n)\n");
        int n = 0;
        for (int i = 0; i < lote.Count; i++)
        {
            Meme m = lote[i];
            string adj = "";
            if (m.Tipo == "imagen" || m.Tipo == "gif")
            {
                byte[] b = Miniatura(Completa(m), 384);
                if (b != null) { imagenes.Add(new KeyValuePair<string, byte[]>("image/jpeg", b)); adj = " | imagen adjunta " + (++n); }
            }
            sb.Append("[" + i + "] " + m.Tipo + " | " + m.Nombre + " | " + (Path.GetDirectoryName(m.Ruta) ?? "") + adj + "\n");
        }
        return sb.ToString();
    }

    public static int AplicarDescripciones(List<Meme> lote, string json)
    {
        object o = Json.Leer(Gemini.QuitarCercas(json));
        int n = 0;
        foreach (object x in Json.Lista(o, "memes"))
        {
            int id = (int)Json.Numero(x, "id", -1);
            if (id < 0 || id >= lote.Count) continue;
            string d = Json.Texto(x, "descripcion").Trim();
            if (d.Length == 0) continue;
            Meme m = lote[id];
            m.Descripcion = d;
            m.Uso = Json.Texto(x, "uso").Trim();
            foreach (object t in Json.Lista(x, "tags"))
            {
                string k = (t as string ?? "").Trim().ToLowerInvariant();
                if (k.Length > 0 && !m.Tags.Contains(k)) m.Tags.Add(k);
            }
            n++;
        }
        return n;
    }

    // ------------------------------------------------ historial de uso

    public void RegistrarUso(Meme m, string capitulo)
    {
        m.Usos.Add(new UsoMeme { Capitulo = capitulo, Fecha = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }

    // Capitulos distintos en los que se uso algo, del mas reciente al mas viejo.
    public List<string> CapitulosRecientes()
    {
        List<UsoMeme> todos = new List<UsoMeme>();
        foreach (Meme m in Memes) todos.AddRange(m.Usos);
        todos.Sort(delegate (UsoMeme a, UsoMeme b) { return String.Compare(b.Fecha, a.Fecha, StringComparison.Ordinal); });
        List<string> r = new List<string>();
        foreach (UsoMeme u in todos) if (!r.Contains(u.Capitulo)) r.Add(u.Capitulo);
        return r;
    }

    // Usado en alguno de los ultimos "n" capitulos (sin contar el actual).
    public bool UsadoHacePoco(Meme m, int n, string actual)
    {
        List<string> rec = CapitulosRecientes();
        rec.Remove(actual);
        if (rec.Count > n) rec = rec.GetRange(0, n);
        foreach (UsoMeme u in m.Usos) if (rec.Contains(u.Capitulo)) return true;
        return false;
    }
}
