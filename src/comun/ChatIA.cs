using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

// =====================================================================
// Otra IA por chat (sin API): un archivo con todo para pegarlo o adjuntarlo
// en ChatGPT, Claude, Gemini web... y despues importar lo que responda.
// Lo usan MomentosIA y Rellenar la musica.
// =====================================================================

public static class ChatIA
{
    // Instrucciones y datos en un solo texto; "recuerda" va al final (lo que mas se olvida).
    public static string Archivo(string titulo, string instrucciones, string datos, string recuerda)
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("PEDIDO DE vegas-cut (" + titulo + ")\n");
        sb.Append("Lee TODO este archivo y responde con lo que piden las INSTRUCCIONES, usando los DATOS del final.\n");
        sb.Append("Tu respuesta se importa automáticamente: entrega SOLO el JSON (en un bloque ```json, o como archivo " +
                  "respuesta.json si puedes crear archivos), sin texto antes ni después.\n\n");
        sb.Append("==================== INSTRUCCIONES ====================\n\n");
        sb.Append(instrucciones.Trim());
        sb.Append("\n\n==================== DATOS ====================\n\n");
        sb.Append(datos.Trim());
        sb.Append("\n\n==================== RECUERDA ====================\n");
        sb.Append(recuerda.Trim() + "\n");
        return sb.ToString();
    }

    // Los objetos { … } de primer nivel que hay en el texto (con comillas y escapes respetados).
    static List<string> ObjetosJson(string t)
    {
        List<string> r = new List<string>();
        int prof = 0, ini = -1;
        bool enTexto = false, esc = false;
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (enTexto) { if (esc) esc = false; else if (c == '\\') esc = true; else if (c == '"') enTexto = false; continue; }
            if (c == '"' && prof > 0) { enTexto = true; continue; }
            if (c == '{') { if (prof == 0) ini = i; prof++; }
            else if (c == '}' && prof > 0) { prof--; if (prof == 0) r.Add(t.Substring(ini, i - ini + 1)); }
        }
        return r;
    }

    // Si la IA entrego VARIOS JSON (uno por capitulo, en bloques o archivos distintos) y se pegaron juntos, los une
    // en uno solo con "capitulos". Devuelve null si no hay al menos dos con "corte".
    public static string UnirRespuestas(string texto)
    {
        List<object> objs = new List<object>();
        foreach (string j in ObjetosJson(texto ?? ""))
        {
            try
            {
                object o = Json.Leer(j);
                if (Json.Lista(o, "corte").Count > 0) objs.Add(o);
                else if (Json.Lista(o, "capitulos").Count > 0) return null;   // ya viene unido
            }
            catch { }
        }
        if (objs.Count < 2) return null;
        List<object> caps = new List<object>(), secciones = new List<object>(), momentos = new List<object>(),
                     shorts = new List<object>(), textos = new List<object>(), titulos = new List<object>();
        StringBuilder resumen = new StringBuilder();
        for (int i = 0; i < objs.Count; i++)
        {
            object o = objs[i];
            string tit = Json.Texto(o, "titulo");
            List<object> ts = Json.Lista(o, "titulos");
            if (tit.Length == 0 && ts.Count > 0 && ts[0] is string) tit = (string)ts[0];
            if (tit.Length == 0) tit = "Cap\u00edtulo " + (i + 1);
            Dictionary<string, object> c = new Dictionary<string, object>();
            c["titulo"] = tit;
            c["resumen"] = Json.Texto(o, "resumen");
            c["corte"] = Json.Lista(o, "corte");
            caps.Add(c);
            if (resumen.Length > 0) resumen.Append("\n\n");
            resumen.Append(tit + ": " + Json.Texto(o, "resumen"));
            secciones.AddRange(Json.Lista(o, "secciones")); momentos.AddRange(Json.Lista(o, "momentos"));
            shorts.AddRange(Json.Lista(o, "shorts")); textos.AddRange(Json.Lista(o, "textos")); titulos.AddRange(ts);
        }
        Dictionary<string, object> r = new Dictionary<string, object>();
        r["resumen"] = resumen.ToString();
        r["secciones"] = secciones; r["momentos"] = momentos; r["shorts"] = shorts; r["textos"] = textos; r["titulos"] = titulos;
        r["corte"] = new List<object>();
        r["capitulos"] = caps;
        return Json.Escribir(r);
    }

    // Saca el JSON de lo que devolvio el chat: tolera texto alrededor y bloques ```json.
    public static string ExtraerJson(string texto, string clave)
    {
        string s = (texto ?? "").Trim();
        if (s.Length == 0) throw new FormatException("está vacía.");
        if (clave.Contains("capitulos")) { string unido = UnirRespuestas(s); if (unido != null) return unido; }
        int bloque = s.IndexOf("```");
        if (bloque >= 0)
        {
            int salto = s.IndexOf('\n', bloque);
            int fin = salto > 0 ? s.IndexOf("```", salto) : -1;
            if (salto > 0 && fin > salto) s = s.Substring(salto + 1, fin - salto - 1).Trim();
        }
        int a = s.IndexOf('{'), b = s.LastIndexOf('}');
        if (a < 0) throw new FormatException("no trae un objeto JSON ({ … }).");
        if (b <= a) throw new FormatException("está cortada: el JSON no termina (quizá se quedó sin espacio).");
        s = s.Substring(a, b - a + 1);
        object o;
        try { o = Json.Leer(s); }
        catch (Exception ex) { throw new FormatException("el JSON está mal formado o cortado (" + ex.Message + ")."); }
        // "clave" puede ser varias separadas por | (basta con que una traiga datos): "corte|capitulos".
        bool alguna = clave.Length == 0;
        foreach (string k in clave.Split('|')) if (k.Length > 0 && Json.Lista(o, k).Count > 0) alguna = true;
        if (!alguna) throw new FormatException("no trae la lista \"" + clave.Replace("|", "\" ni \"") + "\"" + (clave.StartsWith("corte") ? " con los tramos a conservar." : "."));
        return s;
    }

    // Lo que hay que decirle al chat para que corrija.
    public static string MensajeCorreccion(string problema)
    {
        return "Tu respuesta no se pudo importar: " + problema + " Mándala otra vez completa, SOLO como el objeto JSON " +
               "con el mismo formato que pedían las instrucciones (en un bloque ```json), sin texto antes ni después.";
    }
}

// Pegar (o abrir) la respuesta que dio otra IA en su chat.
class DialogoRespuestaIA : VentanaBase
{
    public string Json = "";
    readonly CampoTexto txt = new CampoTexto();
    readonly Etiqueta lblError;
    readonly Boton btnAbrir = new Boton("Abrir archivo…", EstiloBoton.Secundario);
    readonly Boton btnCorregir = new Boton("Copiar mensaje para que la corrija", EstiloBoton.Secundario);
    readonly Boton btnImportar = new Boton("Importar", EstiloBoton.Primario);
    readonly Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);
    string problema = "", clave = "corte|capitulos";

    public DialogoRespuestaIA(string carpeta) : this(carpeta, "corte|capitulos") { }

    public DialogoRespuestaIA(string carpeta, string clave) : base("Importar respuesta", 760)
    {
        this.clave = clave;
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Importar respuesta", "Pega lo que respondió la IA (puede traer texto alrededor) o abre el archivo que te dio. Si te dio varios (uno por capítulo), pégalos juntos o elígelos a la vez.");
        int y = 92;
        txt.Multilinea = true;
        Pos(txt, m, y, w, 330);
        y += 340;
        lblError = Texto("", Tema.Pequena, Tema.Silencio, m, y, w, 36);
        y += 42;
        Pos(btnAbrir, m, y, 150, 40);
        Pos(btnCorregir, m + 160, y, 260, 40);
        btnCorregir.Visible = false;
        Pos(btnCancelar, m + w - 270, y, 120, 40);
        Pos(btnImportar, m + w - 140, y, 140, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        btnAbrir.Click += delegate
        {
            using (OpenFileDialog d = new OpenFileDialog())
            {
                d.Filter = "Respuesta (*.json;*.txt;*.md)|*.json;*.txt;*.md|Todos|*.*";
                d.Multiselect = true;   // un archivo por capitulo: se unen
                if (Directory.Exists(carpeta)) d.InitialDirectory = carpeta;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    StringBuilder todo = new StringBuilder();
                    foreach (string f in d.FileNames) todo.Append(File.ReadAllText(f, Encoding.UTF8)).Append("\n\n");
                    txt.Text = todo.ToString();
                } catch (Exception ex) { Error("No se pudo leer: " + ex.Message); return; }
            }
            Importar();
        };
        btnCorregir.Click += delegate
        {
            try { Clipboard.SetText(ChatIA.MensajeCorreccion(problema)); lblError.Text = "Copiado: pégalo en el chat y luego pega aquí la respuesta nueva."; }
            catch { }
        };
        btnImportar.Click += delegate { Importar(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
    }

    void Error(string t) { lblError.Text = t; }

    void Importar()
    {
        try
        {
            Json = ChatIA.ExtraerJson(txt.Text, clave);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (FormatException ex)
        {
            problema = "La respuesta " + ex.Message;
            Error(problema + " Pídele a la IA que la corrija:");
            btnCorregir.Visible = true;
        }
    }
}
