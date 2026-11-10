using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

// Copia "<proyecto> BASE.veg": el episodio sin silencios y transcrito, para
// volver a partir de ahi (MomentosIA o ProducirCapitulo) sin repetir esos pasos.
public static class CopiaBase
{
    public const string Sufijo = " BASE";

    public static string RutaPara(string veg)
    {
        return Path.Combine(Path.GetDirectoryName(veg), Path.GetFileNameWithoutExtension(veg) + Sufijo + ".veg");
    }

    // El proyecto original de una copia base o de un corte de MomentosIA (o el mismo si no lo es).
    public static string Original(string veg)
    {
        string n = Path.GetFileNameWithoutExtension(veg);
        if (n.EndsWith(Sufijo)) return Path.Combine(Path.GetDirectoryName(veg), n.Substring(0, n.Length - Sufijo.Length) + ".veg");
        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(n, @"^(.*) MOM( \d+)?$");
        return m.Success ? Path.Combine(Path.GetDirectoryName(veg), m.Groups[1].Value + ".veg") : veg;
    }

    public static bool EsCorte(string veg) { return !String.IsNullOrEmpty(veg) && Original(veg) != veg && !EsBase(veg); }

    public static bool EsBase(string veg) { return !String.IsNullOrEmpty(veg) && Path.GetFileNameWithoutExtension(veg).EndsWith(Sufijo); }

    // Copia del corte de MomentosIA cuando se trabaja sobre la BASE: "<original> MOM.veg" (o MOM 2, 3...).
    public static string RutaCorte(string veg)
    {
        string o = Original(veg), dir = Path.GetDirectoryName(o), n = Path.GetFileNameWithoutExtension(o);
        string r = Path.Combine(dir, n + " MOM.veg");
        for (int i = 2; File.Exists(r); i++) r = Path.Combine(dir, n + " MOM " + i + ".veg");
        return r;
    }

    // Las rutas de "n" copias de corte seguidas ("MOM", "MOM 2"...), saltando las que ya existen.
    public static List<string> RutasCorte(string veg, int n)
    {
        string o = Original(veg), dir = Path.GetDirectoryName(o), nombre = Path.GetFileNameWithoutExtension(o);
        List<string> r = new List<string>();
        for (int i = 1; r.Count < n; i++)
        {
            string c = Path.Combine(dir, nombre + (i == 1 ? " MOM" : " MOM " + i) + ".veg");
            if (!File.Exists(c)) r.Add(c);
        }
        return r;
    }

    static readonly System.Text.RegularExpressions.Regex NumTemporada = new System.Text.RegularExpressions.Regex(
        @"(S\d{1,2}\s*[-_ ]?\s*E)(\d{1,3})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    static readonly System.Text.RegularExpressions.Regex NumParte = new System.Text.RegularExpressions.Regex(
        @"(\b(?:parte|part|cap(?:itulo|ítulo)?|ep(?:isodio)?|episode)\s*[-_ ]?\s*)(\d{1,3})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // Los archivos de "n" capitulos seguidos que salen de un video partido: continuan la numeracion del proyecto
    // («S02E03 SCR» -> «S02E03 SCR MOM», «S02E04 SCR MOM», «S02E05 SCR MOM»; tambien «Parte 3», «Cap 3»…). Con el
    // sufijo MOM la serie los reconoce como el capitulo 3, 4 y 5. Si el nombre no trae numero: MOM, MOM 2...
    // Los que ya existen se saltan (agregando « 2»). "numeros": los numeros usados, en orden (null: no se sabe).
    public static List<string> RutasCapitulos(string veg, int n, out List<int> numeros)
    {
        numeros = null;
        string o = Original(veg), dir = Path.GetDirectoryName(o), nombre = Path.GetFileNameWithoutExtension(o);
        System.Text.RegularExpressions.Match m = NumTemporada.Match(nombre);
        if (!m.Success) m = NumParte.Match(nombre);
        if (!m.Success) return RutasCorte(veg, n);
        int primero = int.Parse(m.Groups[2].Value), ancho = Math.Max(2, m.Groups[2].Value.Length);
        List<string> r = new List<string>();
        numeros = new List<int>();
        // La carpeta cuenta solo si trae el mismo numero que el video (mismo patron: S02E03 o Parte 3).
        string carpeta = Path.GetFileName(dir);
        System.Text.RegularExpressions.Match mc = m.Value.StartsWith("S", StringComparison.OrdinalIgnoreCase) && NumTemporada.IsMatch(nombre)
            ? NumTemporada.Match(carpeta) : NumParte.Match(carpeta);
        if (!mc.Success || int.Parse(mc.Groups[2].Value) != primero || String.IsNullOrEmpty(Path.GetDirectoryName(dir))) carpeta = null;
        for (int k = 0; k < n; k++)
        {
            string num = (primero + k).ToString().PadLeft(ancho, '0');
            string nom = nombre.Substring(0, m.Groups[2].Index) + num + nombre.Substring(m.Groups[2].Index + m.Groups[2].Length);
            // Si el proyecto vive en una carpeta con el mismo numero de episodio (aunque se llame distinto al
            // video: «S02E03» / «S02E03 SCR»), cada capitulo va en su carpeta hermana con su numero.
            string dirK = dir;
            if (k > 0 && carpeta != null)
                dirK = Path.Combine(Path.GetDirectoryName(dir),
                    carpeta.Substring(0, mc.Groups[2].Index) + num.PadLeft(Math.Max(2, mc.Groups[2].Value.Length), '0') +
                    carpeta.Substring(mc.Groups[2].Index + mc.Groups[2].Length));
            string c = Path.Combine(dirK, nom + " MOM.veg");
            for (int i = 2; File.Exists(c) || r.Contains(c); i++) c = Path.Combine(dirK, nom + " MOM " + i + ".veg");
            r.Add(c);
            numeros.Add(primero + k);
        }
        return r;
    }

    // La transcripcion y el enlace a la serie del proyecto "desde" pasan a "hacia".
    static void CopiarAnexos(string desde, string hacia)
    {
        string t = Transcripcion.RutaPara(desde);
        if (File.Exists(t))
        {
            Transcripcion tr = Transcripcion.Cargar(t);
            tr.Proyecto = hacia;
            tr.Guardar(Transcripcion.RutaPara(hacia));
        }
        string dir = Path.GetDirectoryName(desde);
        string serie = Path.Combine(dir, Path.GetFileNameWithoutExtension(desde) + ".vegascut-proyecto-serie.json");
        if (File.Exists(serie))
            File.Copy(serie, Path.Combine(Path.GetDirectoryName(hacia), Path.GetFileNameWithoutExtension(hacia) + ".vegascut-proyecto-serie.json"), true);
    }

    // Guarda el proyecto abierto como "destino" (con su transcripcion y su serie) y se queda en el.
    public static void GuardarComo(Vegas vegas, string destino)
    {
        string desde = vegas.Project.FilePath;
        string carp = Path.GetDirectoryName(destino);
        if (!String.IsNullOrEmpty(carp) && !Directory.Exists(carp)) Directory.CreateDirectory(carp);
        vegas.SaveProject(destino);
        CopiarAnexos(desde, destino);
    }

    // Guarda la copia (con su transcripcion y su serie) y vuelve al proyecto original.
    public static string Guardar(Vegas vegas)
    {
        string original = vegas.Project.FilePath;
        if (String.IsNullOrEmpty(original) || Path.GetFileNameWithoutExtension(original).EndsWith(Sufijo)) return null;
        string base_ = RutaPara(original);
        vegas.SaveProject(base_);
        try { CopiarAnexos(original, base_); }
        finally { vegas.SaveProject(original); }
        return base_;
    }
}
