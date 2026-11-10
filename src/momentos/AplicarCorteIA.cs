using System;
using System.Collections.Generic;
using System.IO;
using ScriptPortal.Vegas;

// =====================================================================
// Aplica el resultado de MomentosIA al proyecto abierto: un corte (quitar y
// acelerar, o armar en orden libre) o, si la IA partio el video en varios
// capitulos, un archivo por capitulo partiendo siempre del material sin cortar.
// =====================================================================

public static class AplicarCorteIA
{
    static Ancla Marcador(Project p, double t, string texto)
    {
        p.Markers.Add(new Marker(Timecode.FromMilliseconds(t * 1000), texto));
        return Anclas.Crear(p, t, -1, texto);
    }

    static bool Dentro(double t, List<Rango> rangos)
    {
        foreach (Rango r in rangos) if (t > r.Inicio && t < r.Fin) return true;
        return false;
    }

    // Aplica el corte elegido ("resultado.Corte") al proyecto abierto, sin preguntar nada.
    // "filtrar": solo los textos y momentos que caen en este corte (cuando hay varios capitulos).
    // Devuelve el aviso sobre la transcripcion ("" si no hay).
    public static string Aplicar(Vegas vegas, ResultadoIA resultado, Transcripcion transcripcion, double total, bool silenciar,
                                 bool filtrar, List<string> avisos, out double despues)
    {
        Project p = vegas.Project;
        if (resultado.Montaje)
        {
            List<Ancla> anclasM;
            double dM;
            using (UndoBlock u = new UndoBlock("Momentos con IA: montaje"))
                ArmarMontaje.Armar(vegas, resultado, transcripcion, silenciar, avisos, out anclasM, out dM);
            Anclas.Guardar(vegas.Project.FilePath, anclasM);
            despues = dM;
            return transcripcion.TieneFuentes ? "" : "La transcripción es de antes de que guardara de dónde sale cada clip: vuelve a transcribir para lo que sigue.";
        }

        double fps = p.Video.FrameRate;
        List<Rango> quitar = Editor.AjustarAFotogramas(resultado.Quitar(total), fps);
        List<Acelerado> acelerar = new List<Acelerado>();
        foreach (Acelerado a in resultado.Acelerados(quitar))
        {
            Acelerado f = new Acelerado(Math.Round(a.Inicio * fps) / fps, Math.Round(a.Fin * fps) / fps, a.Factor);
            if (f.Fin - f.Inicio >= 2 / fps) acelerar.Add(f);
        }
        if (quitar.Count == 0 && acelerar.Count == 0) { despues = p.Length.ToMilliseconds() / 1000.0; return "El corte no cambia nada."; }
        List<Track> todas = new List<Track>();
        foreach (Track t in p.Tracks) todas.Add(t);
        List<Ancla> anclas = new List<Ancla>();
        double trasCortar;
        using (UndoBlock deshacer = new UndoBlock("Momentos con IA: corte"))
        {
            if (quitar.Count > 0) Editor.Eliminar(p, todas, quitar, true, true, 0.02);
            trasCortar = p.Length.ToMilliseconds() / 1000.0;
            if (acelerar.Count > 0) Editor.Acelerar(p, todas, acelerar, silenciar, true, 0.02);
            foreach (TextoResumen t in resultado.Textos)
            {
                if (!t.Elegido || (filtrar && !resultado.EnCorte(t.Posicion))) continue;
                anclas.Add(Marcador(p, Acelerado.Posicion(Editor.PosicionTrasQuitar(t.Posicion, quitar), acelerar), "TEXTO: " + t.Texto));
            }
            foreach (Tramo t in resultado.Momentos)
            {
                if (!t.Elegido || Dentro(t.Inicio, quitar) || (filtrar && !resultado.EnCorte(t.Inicio))) continue;
                double nuevo = Acelerado.Posicion(Editor.PosicionTrasQuitar(t.Inicio, quitar), acelerar);
                anclas.Add(Marcador(p, nuevo, "★" + t.Puntuacion.ToString("0") + " " + t.Titulo));
            }
        }
        Anclas.Guardar(p.FilePath, anclas);
        despues = p.Length.ToMilliseconds() / 1000.0;
        string aviso = "";
        if (quitar.Count > 0) aviso = Transcripcion.RegistrarCortes(p.FilePath, quitar, total, trasCortar);
        if (acelerar.Count > 0) aviso = Transcripcion.RegistrarAceleracion(p.FilePath, acelerar, trasCortar, despues);
        return aviso.Replace("\n", " ");
    }

    // Reabre un proyecto desde el script (sin preguntar por cambios). "motivo" dice por que no se pudo.
    public static bool Reabrir(Vegas vegas, string ruta, out string motivo)
    {
        motivo = "";
        try
        {
            System.Reflection.MethodInfo m = vegas.GetType().GetMethod("OpenProject", new Type[] { typeof(string), typeof(bool) });
            if (m != null) m.Invoke(vegas, new object[] { ruta, true });
            else
            {
                m = vegas.GetType().GetMethod("OpenProject", new Type[] { typeof(string) });
                if (m == null) { motivo = "esta versi\u00f3n de Vegas no tiene OpenProject"; return false; }
                m.Invoke(vegas, new object[] { ruta });
            }
            string ahora = vegas.Project.FilePath ?? "";
            if (String.Equals(Path.GetFileName(ahora), Path.GetFileName(ruta), StringComparison.OrdinalIgnoreCase)) return true;
            motivo = "OpenProject no abri\u00f3 el archivo (sigue abierto «" + Path.GetFileName(ahora) + "»)";
            return false;
        }
        catch (Exception ex)
        {
            Exception e = ex.InnerException ?? ex;
            motivo = e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    public static bool Reabrir(Vegas vegas, string ruta) { string m; return Reabrir(vegas, ruta, out m); }

    // Un archivo por capitulo ("<original> MOM", "MOM 2"...), cada uno cortado desde el material sin cortar
    // ("fuente": el proyecto abierto ahora, que no se modifica). Devuelve las rutas creadas; si no se pudo
    // reabrir el material entre uno y otro, solo las que alcanzaron.
    public static List<string> Capitulos(Vegas vegas, ResultadoIA resultado, Transcripcion transcripcion, double total, bool silenciar,
                                         List<string> avisos, List<double> duraciones)
    {
        return Capitulos(vegas, resultado, transcripcion, total, silenciar, avisos, duraciones, null);
    }

    // Cuantos archivos quedaron como copia sin cortar la ultima vez (si no se pudo reabrir el material).
    public static int sinCortar;

    // "destinos": los archivos de cada capitulo marcado (null: los que continuan la numeracion, ver CopiaBase.RutasCapitulos).
    public static List<string> Capitulos(Vegas vegas, ResultadoIA resultado, Transcripcion transcripcion, double total, bool silenciar,
                                         List<string> avisos, List<double> duraciones, List<string> destinos)
    {
        List<string> creados = new List<string>();
        string fuente = vegas.Project.FilePath;
        List<CapituloIA> caps = resultado.Capitulos.FindAll(delegate (CapituloIA c) { return c.Elegido; });
        if (destinos == null) { List<int> nums; destinos = CopiaBase.RutasCapitulos(fuente, caps.Count, out nums); }
        int actual = resultado.Actual;
        sinCortar = 0;
        for (int i = 0; i < caps.Count; i++)
        {
            string motivo;
            if (i > 0 && !Reabrir(vegas, fuente, out motivo))
            {
                // No se puede reabrir desde un script: los que faltan se dejan como copias del material sin cortar
                // (con la respuesta de la IA), para aplicarles su capitulo con «Solo este capitulo».
                List<string> copias = new List<string>();
                for (int j = i; j < caps.Count; j++)
                {
                    try { CopiaBase.CopiarSinCortar(fuente, destinos[j]); copias.Add(Path.GetFileNameWithoutExtension(destinos[j]) + " \u2192 cap\u00edtulo " + (resultado.Capitulos.IndexOf(caps[j]) + 1)); }
                    catch (Exception ex) { avisos.Add("No se pudo copiar para «" + caps[j].Titulo + "»: " + ex.Message); }
                }
                avisos.Add("Vegas no dej\u00f3 reabrir el material (" + motivo + "). " + copias.Count + " archivo(s) quedaron como copia SIN cortar: " +
                           String.Join(", ", copias.ToArray()) + ". \u00c1brelos, abre MomentosIA, elige ese cap\u00edtulo y usa \u00abSolo este cap\u00edtulo\u00bb");
                sinCortar = copias.Count;
                break;
            }
            resultado.Elegir(resultado.Capitulos.IndexOf(caps[i]));
            CopiaBase.GuardarComo(vegas, destinos[i]);
            double d;
            string aviso = Aplicar(vegas, resultado, transcripcion, total, silenciar, true, avisos, out d);
            if (aviso.Length > 0 && !avisos.Contains(aviso)) avisos.Add("«" + caps[i].Titulo + "»: " + aviso);
            vegas.SaveProject(destinos[i]);   // se guarda: el siguiente capitulo reabre el material
            creados.Add(destinos[i]);
            duraciones.Add(d);
        }
        // De vuelta en el material, para poder seguir.
        if (creados.Count > 1 || (creados.Count == 1 && caps.Count > 1)) Reabrir(vegas, fuente);
        resultado.Elegir(actual);
        return creados;
    }
}
