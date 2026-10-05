using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

// =====================================================================
// Cliente minimo de la API de Gemini (REST generateContent).
// Solo se envia texto; el audio nunca sale de la PC.
// =====================================================================

public static class Gemini
{
    // Se puede cambiar solo para pruebas (servidor local que imita la API).
    public static string Base = "https://generativelanguage.googleapis.com/v1beta/";

    // Cuanto se espera cada intento y las pausas antes de reintentar (segundos).
    // Gemini a veces se queda colgado o contesta "saturado" (503/429): en vez de
    // esperar sin fin, se corta y se vuelve a pedir solo.
    public static int LimiteSegundos = 240;
    public static int[] Pausas = { 5, 15, 30 };

    // Estado de la consulta en curso, para el aviso con el reloj y «Cancelar».
    static readonly object candado = new object();
    static readonly List<HttpWebRequest> enCurso = new List<HttpWebRequest>();
    static int activas;
    static DateTime inicio, cancelada = DateTime.MinValue;
    static string detalle = "";

    public static bool Ocupado { get { lock (candado) return activas > 0; } }
    public static double Segundos { get { lock (candado) return activas > 0 ? (DateTime.Now - inicio).TotalSeconds : 0; } }
    public static string Detalle { get { lock (candado) return detalle; } }

    // Corta lo que se este pidiendo. Las consultas que se pidan en los
    // segundos siguientes (lotes de un mismo trabajo) tambien se cancelan.
    public static void Cancelar()
    {
        lock (candado)
        {
            cancelada = DateTime.Now;
            foreach (HttpWebRequest r in enCurso) try { r.Abort(); } catch { }
        }
    }

    static bool Cancelada(DateTime desde) { lock (candado) return cancelada >= desde || (DateTime.Now - cancelada).TotalSeconds < 3; }

    static void Empezar() { lock (candado) { if (activas == 0) inicio = DateTime.Now; activas++; detalle = ""; } }
    static void Terminar() { lock (candado) { activas = Math.Max(0, activas - 1); if (activas == 0) detalle = ""; } }
    static void Anotar(string d) { lock (candado) detalle = d; }

    static HttpWebRequest Peticion(string url, string clave, string metodo)
    {
        // Vegas corre en .NET Framework: hay que activar TLS 1.2 a mano.
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
        r.Method = metodo;
        r.Headers.Add("x-goog-api-key", clave);
        r.Timeout = LimiteSegundos * 1000;
        r.ReadWriteTimeout = LimiteSegundos * 1000;
        return r;
    }

    static string Responder(HttpWebRequest r)
    {
        try
        {
            using (HttpWebResponse resp = (HttpWebResponse)r.GetResponse())
            using (StreamReader sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
        }
        catch (WebException ex) { throw Error(ex); }
    }

    static ErrorGemini Error(WebException ex)
    {
        if (ex.Status == WebExceptionStatus.RequestCanceled) return new ErrorGemini("cancelaste la consulta.", false, true);
        if (ex.Status == WebExceptionStatus.Timeout)
            return new ErrorGemini("no respondió en " + (LimiteSegundos >= 60 ? LimiteSegundos / 60 + " min." : LimiteSegundos + " s."), true, false);
        string detalle = ex.Message;
        int codigo = 0;
        HttpWebResponse h = ex.Response as HttpWebResponse;
        if (h != null)
        {
            codigo = (int)h.StatusCode;
            try
            {
                using (StreamReader sr = new StreamReader(h.GetResponseStream(), Encoding.UTF8))
                {
                    string msg = Json.Texto(Json.Obj(Json.Leer(sr.ReadToEnd()), "error"), "message");
                    if (msg.Length > 0) detalle = msg;
                }
            }
            catch { }
        }
        // Saturado, limite por minuto, error del servidor o la conexion se cayo: vale reintentar.
        bool reintentar = codigo == 429 || codigo == 500 || codigo == 502 || codigo == 503 || codigo == 504 ||
                          (h == null && ex.Status != WebExceptionStatus.TrustFailure);
        if (codigo == 503) detalle = "está saturado (" + detalle + ")";
        return new ErrorGemini(detalle, reintentar, false);
    }

    // Modelos disponibles para esta clave que sirven para generar texto.
    public static List<string> ListarModelos(string clave)
    {
        List<string> r = new List<string>();
        string pagina = "";
        do
        {
            string url = Base + "models?pageSize=200" + (pagina.Length > 0 ? "&pageToken=" + Uri.EscapeDataString(pagina) : "");
            object o = Json.Leer(Responder(Peticion(url, clave, "GET")));
            foreach (object m in Json.Lista(o, "models"))
            {
                bool genera = false;
                foreach (object metodo in Json.Lista(m, "supportedGenerationMethods"))
                    if ((metodo as string) == "generateContent") genera = true;
                string nombre = Json.Texto(m, "name");
                if (nombre.StartsWith("models/")) nombre = nombre.Substring(7);
                if (genera && nombre.StartsWith("gemini")) r.Add(nombre);
            }
            pagina = Json.Texto(o, "nextPageToken");
        } while (pagina.Length > 0);
        r.Sort(StringComparer.OrdinalIgnoreCase);
        return r;
    }

    // Pide una respuesta. Con "json" se exige que conteste solo JSON.
    public static string Generar(string clave, string modelo, string instrucciones, string mensaje, bool json)
    {
        return Generar(clave, modelo, instrucciones, mensaje, json, null);
    }

    // Con imagenes (tipo MIME y bytes), por ejemplo para describir memes.
    public static string Generar(string clave, string modelo, string instrucciones, string mensaje, bool json,
                                 List<KeyValuePair<string, byte[]>> imagenes)
    {
        Dictionary<string, object> cuerpo = new Dictionary<string, object>();
        if (!String.IsNullOrEmpty(instrucciones))
            cuerpo["systemInstruction"] = Partes(instrucciones, null);
        Dictionary<string, object> usuario = Partes(mensaje, "user");
        if (imagenes != null)
            foreach (KeyValuePair<string, byte[]> im in imagenes)
            {
                Dictionary<string, object> dato = new Dictionary<string, object>();
                dato["mime_type"] = im.Key; dato["data"] = Convert.ToBase64String(im.Value);
                Dictionary<string, object> parte = new Dictionary<string, object>();
                parte["inline_data"] = dato;
                ((List<object>)usuario["parts"]).Add(parte);
            }
        cuerpo["contents"] = new List<object> { usuario };
        Dictionary<string, object> config = new Dictionary<string, object>();
        config["temperature"] = 0.4;
        if (json) config["responseMimeType"] = "application/json";
        cuerpo["generationConfig"] = config;

        byte[] datos = Encoding.UTF8.GetBytes(Json.Escribir(cuerpo, false));
        string url = Base + "models/" + Uri.EscapeDataString(modelo) + ":generateContent";
        object resp = Json.Leer(Pedir(url, clave, datos));
        List<object> candidatos = Json.Lista(resp, "candidates");
        if (candidatos.Count == 0)
        {
            string motivo = Json.Texto(Json.Obj(resp, "promptFeedback"), "blockReason");
            throw new Exception("Gemini no devolvió respuesta" + (motivo.Length > 0 ? " (" + motivo + ")" : "") + ".");
        }
        StringBuilder texto = new StringBuilder();
        foreach (object parte in Json.Lista(Json.Obj(candidatos[0], "content"), "parts"))
        {
            object pensamiento;
            Dictionary<string, object> d = parte as Dictionary<string, object>;
            if (d != null && d.TryGetValue("thought", out pensamiento) && pensamiento is bool && (bool)pensamiento) continue;
            texto.Append(Json.Texto(parte, "text"));
        }
        // Si se acabo el espacio de respuesta, el JSON queda a medias.
        if (Json.Texto(candidatos[0], "finishReason") == "MAX_TOKENS")
            throw new RespuestaCortada();
        if (texto.Length == 0)
            throw new Exception("Gemini devolvió una respuesta vacía (" + Json.Texto(candidatos[0], "finishReason") + ").");
        return QuitarCercas(texto.ToString());
    }

    // Hace la peticion con reintentos; se puede cortar con Cancelar().
    static string Pedir(string url, string clave, byte[] datos)
    {
        DateTime desde = DateTime.Now;
        Empezar();
        try
        {
            for (int intento = 0; ; intento++)
            {
                if (Cancelada(desde)) throw new ErrorGemini("cancelaste la consulta.", false, true);
                HttpWebRequest r = Peticion(url, clave, "POST");
                r.ContentType = "application/json; charset=utf-8";
                r.ContentLength = datos.Length;
                lock (candado) enCurso.Add(r);
                try
                {
                    try { using (Stream s = r.GetRequestStream()) s.Write(datos, 0, datos.Length); }
                    catch (WebException ex) { throw Error(ex); }
                    return Responder(r);
                }
                catch (ErrorGemini e)
                {
                    if (e.Cancelado || Cancelada(desde)) throw new ErrorGemini("cancelaste la consulta.", false, true);
                    if (!e.Reintentable || intento >= Pausas.Length) throw;
                    int espera = Pausas[intento];
                    for (int t = espera; t > 0; t--)
                    {
                        Anotar(e.Motivo + " Reintento " + (intento + 1) + " de " + Pausas.Length + " en " + t + " s…");
                        for (int k = 0; k < 10; k++) { if (Cancelada(desde)) break; Thread.Sleep(100); }
                    }
                    Anotar("Reintento " + (intento + 1) + " de " + Pausas.Length + " (" + e.Motivo + ")");
                }
                finally { lock (candado) enCurso.Remove(r); }
            }
        }
        finally { Terminar(); }
    }

    static Dictionary<string, object> Partes(string texto, string rol)
    {
        Dictionary<string, object> parte = new Dictionary<string, object>();
        parte["text"] = texto;
        Dictionary<string, object> c = new Dictionary<string, object>();
        if (rol != null) c["role"] = rol;
        c["parts"] = new List<object> { parte };
        return c;
    }

    // Algunos modelos envuelven el JSON en ```json ... ```.
    public static string QuitarCercas(string t)
    {
        string s = t.Trim();
        if (s.StartsWith("```"))
        {
            int salto = s.IndexOf('\n');
            int fin = s.LastIndexOf("```");
            if (salto > 0 && fin > salto) s = s.Substring(salto + 1, fin - salto - 1).Trim();
        }
        return s;
    }
}

// Error de la API. "Reintentable": saturado, sin respuesta a tiempo o sin conexion.
public class ErrorGemini : Exception
{
    public readonly bool Reintentable, Cancelado;
    public readonly string Motivo;
    public ErrorGemini(string motivo, bool reintentable, bool cancelado) : base("Gemini: " + motivo)
    {
        Motivo = motivo; Reintentable = reintentable; Cancelado = cancelado;
    }
}

// La respuesta no cupo completa (finishReason MAX_TOKENS).
public class RespuestaCortada : Exception
{
    public RespuestaCortada() : base("La respuesta de Gemini sali\u00f3 cortada por ser demasiado larga.") { }
}
