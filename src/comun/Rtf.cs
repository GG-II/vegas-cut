using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

// =====================================================================
// Texto enriquecido (RTF) de los eventos de Titulos y texto
//
// Vegas guarda el texto de "Titles & Text" como RTF: fuente, tamano, color y
// alineacion van como comandos (\f0\fs48\cf1...) antes del texto. Para usar
// un texto como plantilla se conserva todo lo que hay antes del primer
// caracter visible y despues del ultimo, y solo se cambia lo de en medio.
// =====================================================================

public static class Rtf
{
    // Grupos que no son texto visible: tablas de fuentes, colores, estilos...
    static readonly string[] Destinos = { "fonttbl", "colortbl", "stylesheet", "info", "generator", "pict",
                                          "header", "footer", "listtable", "listoverridetable", "themedata",
                                          "colorschememapping", "latentstyles", "datastore", "rsidtbl", "xmlnstbl" };

    // Una pieza del RTF: texto visible (Texto != null) o comando.
    class Pieza
    {
        public int Desde, Hasta;
        public string Texto;      // caracteres visibles que aporta
        public string Comando;    // nombre del comando (sin \)
    }

    static List<Pieza> Piezas(string rtf)
    {
        List<Pieza> r = new List<Pieza>();
        Stack<bool> pila = new Stack<bool>();
        bool oculto = false, inicioGrupo = false;
        int uc = 1, i = 0;
        while (i < rtf.Length)
        {
            char c = rtf[i];
            if (c == '{') { pila.Push(oculto); inicioGrupo = true; i++; continue; }
            if (c == '}') { if (pila.Count > 0) oculto = pila.Pop(); inicioGrupo = false; i++; continue; }
            if (c == '\r' || c == '\n') { i++; continue; }
            Pieza p = new Pieza();
            p.Desde = i;
            if (c != '\\')
            {
                p.Texto = c.ToString();
                i++;
            }
            else if (i + 1 < rtf.Length && char.IsLetter(rtf[i + 1]))
            {
                int j = i + 1;
                while (j < rtf.Length && char.IsLetter(rtf[j])) j++;
                string nombre = rtf.Substring(i + 1, j - i - 1);
                int k = j;
                if (k < rtf.Length && (rtf[k] == '-' || char.IsDigit(rtf[k]))) { k++; while (k < rtf.Length && char.IsDigit(rtf[k])) k++; }
                string num = rtf.Substring(j, k - j);
                if (k < rtf.Length && rtf[k] == ' ') k++;
                i = k;
                p.Comando = nombre;
                int n;
                bool hayNum = int.TryParse(num, NumberStyles.Integer, CultureInfo.InvariantCulture, out n);
                if (nombre == "uc" && hayNum) uc = n;
                if (nombre == "u" && hayNum)
                {
                    p.Texto = ((char)(n < 0 ? n + 65536 : n)).ToString();
                    p.Comando = null;
                    // Se salta el caracter de respaldo que sigue a \uN.
                    for (int s = 0; s < uc && i < rtf.Length; s++)
                    {
                        if (rtf[i] == '\\' && i + 3 < rtf.Length && rtf[i + 1] == '\'') i += 4;
                        else if (rtf[i] == '{' || rtf[i] == '}' || rtf[i] == '\\') break;
                        else i++;
                    }
                }
                else if (nombre == "par" || nombre == "line") p.Texto = "\n";
                else if (nombre == "tab") p.Texto = "\t";
                if (inicioGrupo && Array.IndexOf(Destinos, nombre) >= 0) oculto = true;
            }
            else if (i + 1 < rtf.Length)
            {
                char s = rtf[i + 1];
                if (s == '\'' && i + 3 < rtf.Length)
                {
                    int b;
                    if (int.TryParse(rtf.Substring(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b))
                        p.Texto = Ansi(b);
                    i += 4;
                }
                else
                {
                    if (s == '*' && inicioGrupo) oculto = true;
                    if (s == '\\' || s == '{' || s == '}') p.Texto = s.ToString();
                    else if (s == '~') p.Texto = "\u00a0";
                    else p.Comando = s.ToString();
                    i += 2;
                }
            }
            else i++;
            p.Hasta = i;
            inicioGrupo = false;
            if (!oculto) r.Add(p);
        }
        return r;
    }

    static string Ansi(int b)
    {
        if (b < 128) return ((char)b).ToString();
        try { return Encoding.GetEncoding(1252).GetString(new byte[] { (byte)b }); }
        catch { return ((char)b).ToString(); }
    }

    public static bool EsRtf(string s) { return s != null && s.TrimStart().StartsWith("{\\rtf"); }

    // Texto visible, con saltos de linea en \par.
    public static string TextoPlano(string rtf)
    {
        if (!EsRtf(rtf)) return rtf ?? "";
        StringBuilder sb = new StringBuilder();
        foreach (Pieza p in Piezas(rtf)) if (p.Texto != null) sb.Append(p.Texto);
        return sb.ToString().TrimEnd('\n', '\r', ' ');
    }

    // Cambia el texto visible conservando el formato de la plantilla (el del
    // primer caracter). Los saltos de linea del texto nuevo se vuelven \par.
    public static string ReemplazarTexto(string rtf, string nuevo)
    {
        if (!EsRtf(rtf)) return Simple(nuevo);
        List<Pieza> piezas = Piezas(rtf);
        int primera = -1, ultima = -1;
        for (int k = 0; k < piezas.Count; k++)
        {
            Pieza p = piezas[k];
            if (p.Texto == null || p.Texto == "\n") continue;
            if (primera < 0) primera = k;
            ultima = k;
        }
        if (primera < 0)
        {
            // Plantilla sin texto: se pone antes del ultimo \par o del cierre.
            int fin = rtf.LastIndexOf('}');
            for (int k = piezas.Count - 1; k >= 0; k--)
                if (piezas[k].Comando == "par") { fin = piezas[k].Desde; break; }
            if (fin < 0) return Simple(nuevo);
            return rtf.Substring(0, fin) + Escapar(nuevo) + rtf.Substring(fin);
        }
        int desde = piezas[primera].Desde, hasta = piezas[ultima].Hasta;
        string medio = Escapar(nuevo);
        return rtf.Substring(0, desde) + medio + rtf.Substring(hasta);
    }

    public static string Escapar(string texto)
    {
        StringBuilder sb = new StringBuilder();
        string t = (texto ?? "").Replace("\r\n", "\n").Replace('\r', '\n');
        for (int i = 0; i < t.Length; i++)
        {
            char c = t[i];
            if (c == '\\' || c == '{' || c == '}') sb.Append('\\').Append(c);
            else if (c == '\n') sb.Append("\\par ");
            else if (c == '\t') sb.Append("\\tab ");
            else if (c < 128) sb.Append(c);
            else sb.Append("\\u").Append(((int)(short)c).ToString(CultureInfo.InvariantCulture)).Append('?');
        }
        return sb.ToString();
    }

    // RTF basico centrado, por si la plantilla no trae texto enriquecido.
    public static string Simple(string texto)
    {
        return "{\\rtf1\\ansi\\ansicpg1252\\deff0{\\fonttbl{\\f0\\fnil Arial;}}\\uc1\\pard\\qc\\f0\\fs48 " +
               Escapar(texto) + "\\par\n}";
    }
}
