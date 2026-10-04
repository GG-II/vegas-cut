using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;

// =====================================================================
// Cliente minimo de la API de Gemini (REST generateContent).
// Solo se envia texto; el audio nunca sale de la PC.
// =====================================================================

public static class Gemini
{
    // Se puede cambiar solo para pruebas (servidor local que imita la API).
    public static string Base = "https://generativelanguage.googleapis.com/v1beta/";

    static HttpWebRequest Peticion(string url, string clave, string metodo)
    {
        // Vegas corre en .NET Framework: hay que activar TLS 1.2 a mano.
        ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
        HttpWebRequest r = (HttpWebRequest)WebRequest.Create(url);
        r.Method = metodo;
        r.Headers.Add("x-goog-api-key", clave);
        r.Timeout = 10 * 60 * 1000;
        r.ReadWriteTimeout = 10 * 60 * 1000;
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
        catch (WebException ex)
        {
            string detalle = ex.Message;
            if (ex.Response != null)
            {
                try
                {
                    using (StreamReader sr = new StreamReader(ex.Response.GetResponseStream(), Encoding.UTF8))
                    {
                        string cuerpo = sr.ReadToEnd();
                        string msg = Json.Texto(Json.Obj(Json.Leer(cuerpo), "error"), "message");
                        if (msg.Length > 0) detalle = msg;
                    }
                }
                catch { }
            }
            throw new Exception("Gemini: " + detalle);
        }
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

        HttpWebRequest r = Peticion(Base + "models/" + Uri.EscapeDataString(modelo) + ":generateContent", clave, "POST");
        r.ContentType = "application/json; charset=utf-8";
        byte[] datos = Encoding.UTF8.GetBytes(Json.Escribir(cuerpo, false));
        r.ContentLength = datos.Length;
        using (Stream s = r.GetRequestStream()) s.Write(datos, 0, datos.Length);

        object resp = Json.Leer(Responder(r));
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

// La respuesta no cupo completa (finishReason MAX_TOKENS).
public class RespuestaCortada : Exception
{
    public RespuestaCortada() : base("La respuesta de Gemini sali\u00f3 cortada por ser demasiado larga.") { }
}
