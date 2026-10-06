public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        using (VentanaVoces v = new VentanaVoces(vegas)) v.ShowDialog();
    }
}
