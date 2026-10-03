using System;
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
