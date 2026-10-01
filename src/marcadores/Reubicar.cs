using System;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Project p = vegas.Project;
        if (String.IsNullOrEmpty(p.FilePath) || Anclas.Cargar(p.FilePath).Count == 0)
        {
            MessageBox.Show("Este proyecto no tiene marcadores anclados.\n\nSe crean con MomentosIA " +
                            "(“Crear regiones y marcadores” o “Aplicar corte”).", "Reubicar marcadores");
            return;
        }
        int perdidos, revisados, movidos;
        using (UndoBlock deshacer = new UndoBlock("Reubicar marcadores"))
            movidos = Anclas.Reubicar(p, p.FilePath, out perdidos, out revisados);
        MessageBox.Show(
            movidos + " de " + revisados + " marcadores y regiones vuelven a estar sobre su clip." +
            (perdidos > 0 ? "\n" + perdidos + " no encontraron su clip (esa parte del video ya no está) y se quedaron donde estaban." : "") +
            "\n\nCtrl+Z lo deshace.", "Reubicar marcadores");
    }
}
