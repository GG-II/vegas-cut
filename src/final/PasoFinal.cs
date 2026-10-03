using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using ScriptPortal.Vegas;

// =====================================================================
// Paso final: lo ultimo antes de exportar, cuando el capitulo ya esta
// armado y con la narracion grabada.
//  1. Bajar el juego (voces y sonido de las grabaciones) bajo la narracion.
//  2. Balancear la musica (baja sola bajo las voces y la narracion).
//  3. Censurar palabrotas.
// =====================================================================

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        using (VentanaPasoFinal v = new VentanaPasoFinal(vegas)) v.ShowDialog();
    }
}

class VentanaPasoFinal : VentanaBase
{
    readonly Vegas vegas;
    Transcripcion trans;
    string narrador = "Narrador";
    Etiqueta lbl1, lbl2, lbl3, lbl0, lblEstado;
    CampoNumero numDb = new CampoNumero();
    Boton btn0 = new Boton("Rellenar la música…", EstiloBoton.Secundario);
    Boton btn1 = new Boton("Bajar el juego", EstiloBoton.Secundario);
    Boton btn2 = new Boton("Balancear la música…", EstiloBoton.Secundario);
    Boton btn3 = new Boton("Censurar palabrotas…", EstiloBoton.Secundario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Primario);

    public VentanaPasoFinal(Vegas vegas) : base("Paso final", 760)
    {
        this.vegas = vegas;
        int m = Margen, w = Ancho;
        Encabezado("Paso final", "Lo último antes de exportar, con la narración ya grabada: música, balance y censura.");
        int y = 96;
        lbl0 = Paso(1, "Rellenar la música", "Ya cortado y reordenado: pone OST en los huecos de la música según lo que pasa (no toca lo que ya está).", y);
        Pos(btn0, m + w - 210, y + 4, 210, 32);
        y += 86;
        lbl1 = Paso(2, "Bajar el juego bajo la narración", "Las voces y el sonido de las grabaciones bajan mientras narras (la pista del narrador no se toca).", y);
        numDb.Sufijo = "dB"; numDb.Minimo = -30; numDb.Maximo = 0; numDb.Paso = 1;
        Pos(numDb, m + w - 330, y + 4, 110, 32);
        numDb.Valor = -10;
        Pos(btn1, m + w - 210, y + 4, 210, 32);
        y += 86;
        lbl2 = Paso(3, "Balancear la música", "La música baja sola bajo las voces; incluye la pista de narración entre las voces.", y);
        Pos(btn2, m + w - 210, y + 4, 210, 32);
        y += 86;
        lbl3 = Paso(4, "Censurar palabrotas", "Busca las palabrotas en la transcripción y las tapa con el efecto que elijas.", y);
        Pos(btn3, m + w - 210, y + 4, 210, 32);
        y += 92;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 160, 40);
        Pos(btnCerrar, m + w - 140, y, 140, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        try
        {
            string r = Transcripcion.RutaPara(vegas.Project.FilePath);
            if (r != null && File.Exists(r))
            {
                trans = Transcripcion.Cargar(r);
                if (trans.TieneFuentes) trans.Ubicador = PistasVegas.Ubicador(vegas.Project, trans);
            }
        }
        catch { trans = null; }
        try
        {
            SerieProyecto s;
            Serie.DelProyecto(CopiaBase.Original(vegas.Project.FilePath ?? ""), out s);
            if (s != null) narrador = s.Formato.NarradorNombre;
        }
        catch { }
        btn3.Enabled = trans != null;
        if (trans == null) Hecho(lbl3, "Sin transcripción: ejecuta Transcribir primero.", false);

        btn1.Click += delegate
        {
            int n;
            using (UndoBlock u = new UndoBlock("Bajar el juego bajo la narración"))
                n = LogicaPasoFinal.BajarJuego(vegas.Project, trans, narrador, numDb.Valor);
            Hecho(lbl1, n == 0 ? "No encontré narración ni pistas de grabación." : "✔ " + n + " pistas bajan " + numDb.Valor + " dB mientras narras.", n > 0);
        };
        btn0.Click += delegate
        {
            using (VentanaRelleno v = new VentanaRelleno(vegas, trans)) v.ShowDialog(this);
            Hecho(lbl0, "Hecho (lo nuevo quedó en la pista de música, sin balancear).", true);
        };
        btn2.Click += delegate
        {
            List<InfoPista> pistas = PistasVegas.Listar(vegas.Project);
            if (pistas.Count < 2) { Hecho(lbl2, "Hace falta al menos una pista con voz y otra con música.", false); return; }
            using (VentanaMusica v = new VentanaMusica(vegas, pistas)) v.ShowDialog(this);
            Hecho(lbl2, "Hecho (revisa la envolvente de la pista de música).", true);
        };
        btn3.Click += delegate
        {
            using (VentanaCensura v = new VentanaCensura(vegas, trans)) v.ShowDialog(this);
            Hecho(lbl3, "Hecho.", true);
        };
        btnCerrar.Click += delegate { Close(); };
    }

    Etiqueta Paso(int n, string titulo, string detalle, int y)
    {
        int m = Margen;
        Texto(n.ToString(), Tema.Titulo, Tema.Acento, m, y, 30, 36);
        Texto(titulo, Tema.Seccion, Tema.Texto, m + 36, y, Ancho - 400, 22);
        Texto(detalle, Tema.Pequena, Tema.TextoSuave, m + 36, y + 22, Ancho - 400, 30);
        return Texto("", Tema.Pequena, Tema.TextoSuave, m + 36, y + 54, Ancho - 36, 18);
    }

    void Hecho(Etiqueta l, string t, bool ok)
    {
        l.Text = t;
        l.ForeColor = ok ? Color.FromArgb(120, 220, 150) : Tema.AcentoHover;
    }
}
