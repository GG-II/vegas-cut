using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

// =====================================================================
// Mientras Gemini responde, cada ventana muestra arriba a la derecha cuanto
// lleva, si esta reintentando y un boton «Cancelar». Cerrar la ventana a
// mitad pregunta si cancelar la consulta (antes habia que esperar o matar Vegas).
// =====================================================================

partial class VentanaBase
{
    partial void Extras() { AvisoGemini.Enganchar(this); }
}

class AvisoGemini : ControlBase
{
    // A partir de aqui se avisa que esta tardando mas de lo normal.
    public const int Normal = 90;

    readonly Boton btn = new Boton("Cancelar", EstiloBoton.Secundario);
    string texto = "", detalle = "";
    bool lento;

    AvisoGemini()
    {
        Visible = false;
        Controls.Add(btn);
        btn.Click += delegate { Gemini.Cancelar(); btn.Enabled = false; btn.Text = "Cancelando…"; };
    }

    public static string Tiempo(double s)
    {
        int t = (int)s;
        return (t / 60) + ":" + (t % 60).ToString("00");
    }

    // Texto del aviso (aparte para probarlo).
    public static string Texto(double segundos, out bool lento)
    {
        lento = segundos >= Normal;
        return "Gemini pensando · " + Tiempo(segundos);
    }

    public static void Enganchar(VentanaBase v)
    {
        AvisoGemini a = new AvisoGemini();
        v.Controls.Add(a);
        bool cerrar = false;
        int intentosCerrar = 0;
        System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
        reloj.Interval = 500;
        reloj.Tick += delegate
        {
            bool ocupado = Gemini.Ocupado;
            if (ocupado)
            {
                if (!a.Visible)
                {
                    a.btn.Enabled = true; a.btn.Text = "Cancelar";
                    a.Visible = true;
                }
                a.SetBounds(v.ClientSize.Width - 24 - 420, 10, 420, 52);
                a.BringToFront();
                bool lento;
                a.texto = Texto(Gemini.Segundos, out lento);
                a.lento = lento;
                a.detalle = Gemini.Detalle;
                if (a.detalle.Length == 0)
                    a.detalle = a.lento ? "Tarda más de lo normal: puedes cancelar y pedirlo otra vez." : "Puede tardar uno o dos minutos.";
                a.Invalidate();
            }
            else if (a.Visible) a.Visible = false;

            // Se pidio cerrar a mitad: se cierra cuando la consulta ya se corto.
            if (cerrar && !ocupado)
            {
                if (++intentosCerrar > 20) { cerrar = false; return; }
                v.Close();
            }
        };
        v.FormClosing += delegate (object s, FormClosingEventArgs e)
        {
            if (!Gemini.Ocupado || e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            if (cerrar) return;
            if (MessageBox.Show(v, "Gemini todavía está respondiendo.\n\n¿Cancelar la consulta y cerrar?", "vegas-cut",
                                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                Gemini.Cancelar();
                cerrar = true; intentosCerrar = 0;
            }
        };
        v.FormClosed += delegate { reloj.Stop(); reloj.Dispose(); };
        reloj.Start();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        btn.SetBounds(Width - 100, (Height - 30) / 2, 90, 30);
        base.OnLayout(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        RectangleF r = new RectangleF(0.5f, 0.5f, Width - 1.5f, Height - 1.5f);
        using (GraphicsPath p = Tema.Redondeado(r, 8))
        {
            using (SolidBrush b = new SolidBrush(Tema.Panel)) g.FillPath(b, p);
            using (Pen pen = new Pen(lento ? Tema.Silencio : Tema.Acento)) g.DrawPath(pen, p);
        }
        // Puntito que late para que se note que sigue vivo.
        int fase = (int)(DateTime.Now.Millisecond / 500);
        using (SolidBrush b = new SolidBrush(fase == 0 ? Tema.Acento : Color.FromArgb(120, Tema.Acento))) g.FillEllipse(b, 12, 13, 9, 9);
        int ancho = Width - 130;
        TextRenderer.DrawText(g, texto, Tema.Negrita, new Rectangle(28, 6, ancho, 20), lento ? Tema.Silencio : Tema.Texto,
                              TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, detalle, Tema.Pequena, new Rectangle(28, 26, ancho, 20), Tema.TextoSuave,
                              TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
