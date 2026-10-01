using System;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        int n;
        using (UndoBlock deshacer = new UndoBlock("Desenlazar clips"))
            n = Editor.Reagrupar(vegas.Project);
        MessageBox.Show(n == 0
            ? "No había clips unidos de más: cada pedazo ya estaba en su propio grupo."
            : n + " pedazos quedaron en su propio grupo. El video y sus audios de cada pedazo siguen juntos." +
              "\n\nCtrl+Z lo deshace.", "Desenlazar clips");
    }
}
