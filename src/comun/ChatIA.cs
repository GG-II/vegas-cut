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

    // Saca el JSON de lo que devolvio el chat: tolera texto alrededor y bloques ```json.
    public static string ExtraerJson(string texto, string clave)
    {
        string s = (texto ?? "").Trim();
        if (s.Length == 0) throw new FormatException("está vacía.");
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
        if (clave.Length > 0 && Json.Lista(o, clave).Count == 0) throw new FormatException("no trae la lista \"" + clave + "\"" + (clave == "corte" ? " con los tramos a conservar." : "."));
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
    string problema = "", clave = "corte";

    public DialogoRespuestaIA(string carpeta) : this(carpeta, "corte") { }

    public DialogoRespuestaIA(string carpeta, string clave) : base("Importar respuesta", 760)
    {
        this.clave = clave;
        StartPosition = FormStartPosition.CenterParent;
        int m = Margen, w = Ancho;
        Encabezado("Importar respuesta", "Pega lo que respondió la IA (puede traer texto alrededor) o abre el archivo que te dio.");
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
                if (Directory.Exists(carpeta)) d.InitialDirectory = carpeta;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { txt.Text = File.ReadAllText(d.FileName, Encoding.UTF8); } catch (Exception ex) { Error("No se pudo leer: " + ex.Message); return; }
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
