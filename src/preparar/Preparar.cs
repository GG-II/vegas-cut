using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Project p = vegas.Project;
        if (String.IsNullOrEmpty(p.FilePath))
        {
            MessageBox.Show("Guarda el proyecto primero: la transcripción se guarda junto al .veg.", "Preparar episodio");
            return;
        }
        List<InfoPista> pistas = PistasVegas.Listar(p);
        if (pistas.Count == 0)
        {
            MessageBox.Show("El proyecto no tiene pistas de audio.", "Preparar episodio");
            return;
        }
        bool abrir, pedir;
        using (VentanaPreparar v = new VentanaPreparar(vegas, pistas))
        {
            v.ShowDialog();
            abrir = v.AbrirMomentos;
            pedir = v.Pedir;
        }
        if (abrir) AbrirMomentos.Abrir(vegas, pedir);
    }
}

// Lo que se recuerda entre episodios (%APPDATA%\vegas-cut\preparar.ini).
public class AjustesPreparar
{
    public List<string> Voces = new List<string>(), Ambiente = new List<string>();
    public string Perfil = "Gameplay";
    public bool Silencios = true, Transcribir = true, Pedir = false;

    static string Ruta
    {
        get { return Path.Combine(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "vegas-cut"), "preparar.ini"); }
    }

    static List<string> Lista(string v)
    {
        List<string> r = new List<string>();
        foreach (string x in v.Split(',')) if (x.Trim().Length > 0) r.Add(x.Trim());
        return r;
    }

    public static AjustesPreparar Cargar()
    {
        AjustesPreparar a = new AjustesPreparar();
        try
        {
            if (!File.Exists(Ruta)) return a;
            foreach (string l in File.ReadAllLines(Ruta, Encoding.UTF8))
            {
                int i = l.IndexOf('=');
                if (i < 0) continue;
                string k = l.Substring(0, i).Trim(), v = l.Substring(i + 1).Trim();
                switch (k)
                {
                    case "voces": a.Voces = Lista(v); break;
                    case "ambiente": a.Ambiente = Lista(v); break;
                    case "perfil": a.Perfil = v; break;
                    case "silencios": a.Silencios = v != "0"; break;
                    case "transcribir": a.Transcribir = v != "0"; break;
                    case "pedir": a.Pedir = v == "1"; break;
                }
            }
        }
        catch { }
        return a;
    }

    public void Guardar()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Ruta));
            File.WriteAllText(Ruta, "voces=" + String.Join(",", Voces.ToArray()) + "\nambiente=" + String.Join(",", Ambiente.ToArray()) +
                "\nperfil=" + Perfil + "\nsilencios=" + (Silencios ? "1" : "0") + "\ntranscribir=" + (Transcribir ? "1" : "0") +
                "\npedir=" + (Pedir ? "1" : "0") + "\n", new UTF8Encoding(false));
        }
        catch { }
    }
}

class VentanaPreparar : VentanaBase
{
    readonly Vegas vegas;
    readonly List<InfoPista> pistas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly AjustesPreparar ajustes = AjustesPreparar.Cargar();
    readonly string ruta;
    List<Perfil_> perfiles = new List<Perfil_>();
    public bool AbrirMomentos, Pedir;

    List<Boton> chipsVoz = new List<Boton>(), chipsAmbiente = new List<Boton>();
    Combo comboPerfil = new Combo();
    Etiqueta lblPerfil, lblPaso1, lblPaso2, lblPaso3, lblEstado, lblDetalle, lblWhisper;
    Boton chipSilencios = new Boton("Quitar silencios", EstiloBoton.Chip);
    Boton chipTranscribir = new Boton("Transcribir", EstiloBoton.Chip);
    Boton chipPedir = new Boton("Pedir a Gemini al abrir", EstiloBoton.Chip);
    BarraProgreso barra = new BarraProgreso();
    Boton btnEmpezar = new Boton("Empezar", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);
    System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
    ProcesoTranscripcion proceso;
    bool trabajando;
    DateTime comienzo;

    public VentanaPreparar(Vegas vegas, List<InfoPista> pistas) : base("Preparar episodio", 820)
    {
        this.vegas = vegas;
        this.pistas = pistas;
        ruta = Transcripcion.RutaPara(vegas.Project.FilePath);
        int m = Margen, w = Ancho;
        Encabezado("Preparar episodio", "Quita los silencios y transcribe de una pasada; al final abre MomentosIA.");

        int y = 92;
        if (File.Exists(ruta))
        {
            Texto("Este proyecto ya tiene transcripción: si transcribes otra vez, se reemplaza.", Tema.Pequena, Tema.AcentoHover, m, y, w, 18);
            y += 24;
        }
        Texto("VOCES", Tema.Pequena, Tema.TextoSuave, m, y, 60, 18);
        Texto("Se usan para detectar los silencios y se transcriben.", Tema.Pequena, Tema.TextoSuave, m + 64, y, w - 64, 18);
        y = Chips(chipsVoz, y + 22) + 12;
        Texto("AMBIENTE", Tema.Pequena, Tema.TextoSuave, m, y, 80, 18);
        Texto("Solo se mide su sonido (juego, música) para encontrar momentos intensos.", Tema.Pequena, Tema.TextoSuave, m + 84, y, w - 84, 18);
        y = Chips(chipsAmbiente, y + 22) + 16;
        Elegir();
        for (int i = 0; i < pistas.Count; i++)
        {
            int k = i;
            chipsVoz[k].Click += delegate { if (trabajando) return; chipsVoz[k].Activo = !chipsVoz[k].Activo; if (chipsVoz[k].Activo) chipsAmbiente[k].Activo = false; Actualizar(); };
            chipsAmbiente[k].Click += delegate { if (trabajando) return; chipsAmbiente[k].Activo = !chipsAmbiente[k].Activo; if (chipsAmbiente[k].Activo) chipsVoz[k].Activo = false; Actualizar(); };
        }

        Texto("PERFIL DE SILENCIOS", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        perfiles.AddRange(Perfil_.Incluidos);
        perfiles.AddRange(Perfil_.CargarPropios());
        foreach (Perfil_ p in perfiles) comboPerfil.Items.Add(p.Nombre);
        int ip = perfiles.FindIndex(delegate (Perfil_ p) { return p.Nombre == ajustes.Perfil; });
        comboPerfil.SelectedIndex = ip >= 0 ? ip : 3;
        Pos(comboPerfil, m, y + 22, 260, 30);
        lblPerfil = Texto("", Tema.Pequena, Tema.TextoSuave, m + 276, y + 20, w - 276, 36);
        y += 66;

        // Pasos
        Texto("PASOS", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        y += 22;
        chipSilencios.Activo = ajustes.Silencios;
        chipTranscribir.Activo = ajustes.Transcribir;
        chipPedir.Activo = ajustes.Pedir;
        lblPaso1 = Paso(chipSilencios, "1", y); y += 34;
        lblPaso2 = Paso(chipTranscribir, "2", y); y += 34;
        lblPaso3 = Paso(chipPedir, "3", y); y += 40;
        lblPaso3.Text = "Al final se abre MomentosIA. Activa esto para que además le pida a Gemini solo.";
        lblWhisper = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        y += 26;

        lblEstado = Texto("", Tema.Negrita, Tema.Texto, m, y, w, 22);
        Pos(barra, m, y + 28, w, 10);
        lblDetalle = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 44, w, 18);
        y += 76;
        Pos(btnCerrar, m + w - 330, y, 110, 40);
        Pos(btnEmpezar, m + w - 210, y, 210, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        foreach (Boton c in new Boton[] { chipSilencios, chipTranscribir, chipPedir })
        {
            Boton b = c;
            b.Click += delegate { if (!trabajando) { b.Activo = !b.Activo; Actualizar(); } };
        }
        comboPerfil.SelectedIndexChanged += delegate { Actualizar(); };
        btnEmpezar.Click += delegate { if (trabajando) Cancelar(); else Empezar(); };
        btnCerrar.Click += delegate { Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e)
        {
            if (trabajando && MessageBox.Show(this, "¿Cancelar la preparación?", "Preparar episodio", MessageBoxButtons.YesNo) == DialogResult.No)
            { e.Cancel = true; return; }
            if (trabajando) Cancelar();
        };
        reloj.Interval = 300;
        reloj.Tick += delegate { Revisar(); };
        Actualizar();
    }

    Etiqueta Paso(Boton chip, string n, int y)
    {
        Pos(chip, Margen, y, 210, 28);
        return Texto("", Tema.Pequena, Tema.TextoSuave, Margen + 222, y + 4, Ancho - 222, 20);
    }

    int Chips(List<Boton> lista, int y)
    {
        int cx = Margen;
        foreach (InfoPista p in pistas)
        {
            Boton c = new Boton(p.Nombre, EstiloBoton.Chip);
            int w = Math.Min(Ancho, TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 26);
            if (cx + w > Margen + Ancho) { cx = Margen; y += 34; }
            Pos(c, cx, y, w, 28);
            lista.Add(c);
            cx += w + 6;
        }
        return y + 28;
    }

    // Las pistas de la vez pasada (por etiqueta); si no hay, la voz sugerida.
    void Elegir()
    {
        bool alguna = false;
        for (int i = 0; i < pistas.Count; i++)
        {
            chipsVoz[i].Activo = ajustes.Voces.Contains(pistas[i].Etiqueta);
            chipsAmbiente[i].Activo = !chipsVoz[i].Activo && ajustes.Ambiente.Contains(pistas[i].Etiqueta);
            alguna |= chipsVoz[i].Activo;
        }
        if (!alguna) chipsVoz[PistasVegas.SugerirVoz(pistas)].Activo = true;
    }

    List<int> Elegidas(List<Boton> chips)
    {
        List<int> r = new List<int>();
        for (int i = 0; i < chips.Count; i++) if (chips[i].Activo) r.Add(i);
        return r;
    }

    Perfil_ PerfilElegido { get { return perfiles[Math.Max(0, comboPerfil.SelectedIndex)]; } }

    void Actualizar()
    {
        lblPerfil.Text = PerfilElegido.Descripcion;
        lblPaso1.Text = chipSilencios.Activo ? "Mide las voces y quita las pausas de todas las pistas, con el perfil elegido." : "Se salta (ya están quitados).";
        lblPaso2.Text = chipTranscribir.Activo ? "Whisper transcribe las voces; tarda según la duración y tu tarjeta." : "Se salta (usa la transcripción que ya hay).";
        bool voces = Elegidas(chipsVoz).Count > 0;
        if (chipTranscribir.Activo && !config.TieneWhisper)
        {
            lblWhisper.ForeColor = Tema.Silencio;
            lblWhisper.Text = "Falta configurar Faster-Whisper-XXL: ejecuta “ConfigurarVegasCut”.";
        }
        else if (!chipTranscribir.Activo && !File.Exists(ruta))
        {
            lblWhisper.ForeColor = Tema.Silencio;
            lblWhisper.Text = "Sin transcribir no hay transcripción para MomentosIA: activa “Transcribir”.";
        }
        else
        {
            lblWhisper.ForeColor = Tema.TextoSuave;
            lblWhisper.Text = "Whisper: " + config.WhisperModelo + " · " + (config.WhisperDispositivo == "cpu" ? "procesador" : "tarjeta") +
                              " · idioma " + config.Idioma;
        }
        btnEmpezar.Enabled = trabajando || (voces && (!chipTranscribir.Activo || config.TieneWhisper) &&
                                            (chipTranscribir.Activo || File.Exists(ruta)));
    }

    void Estado(string texto, double fraccion)
    {
        lblEstado.ForeColor = Tema.Texto;
        lblEstado.Text = texto;
        barra.Valor = fraccion;
        Application.DoEvents();
    }

    void Bloquear(bool si)
    {
        trabajando = si;
        btnEmpezar.Text = si ? "Cancelar" : "Empezar";
        btnCerrar.Enabled = !si;
        comboPerfil.Enabled = !si;
        foreach (Boton b in chipsVoz) b.Enabled = !si;
        foreach (Boton b in chipsAmbiente) b.Enabled = !si;
        foreach (Boton b in new Boton[] { chipSilencios, chipTranscribir, chipPedir }) b.Enabled = !si;
    }

    void Empezar()
    {
        // Se recuerda lo elegido.
        ajustes.Voces.Clear(); ajustes.Ambiente.Clear();
        foreach (int i in Elegidas(chipsVoz)) ajustes.Voces.Add(pistas[i].Etiqueta);
        foreach (int i in Elegidas(chipsAmbiente)) ajustes.Ambiente.Add(pistas[i].Etiqueta);
        ajustes.Perfil = PerfilElegido.Nombre;
        ajustes.Silencios = chipSilencios.Activo; ajustes.Transcribir = chipTranscribir.Activo; ajustes.Pedir = chipPedir.Activo;
        ajustes.Guardar();

        Bloquear(true);
        comienzo = DateTime.Now;
        if (chipSilencios.Activo)
        {
            lblPaso1.ForeColor = Tema.Texto;
            try
            {
                List<InfoPista> voces = new List<InfoPista>();
                foreach (int i in Elegidas(chipsVoz)) voces.Add(pistas[i]);
                double quitado;
                int n = PasoSilencios.Ejecutar(vegas, voces, PerfilElegido, delegate (string t, double f) { Estado("1 · " + t, f * 0.1); }, out quitado);
                Hecho(lblPaso1, n == 0 ? "No había silencios que quitar." : "✔ " + n + " silencios quitados (" + Formato.Tiempo(quitado) + ").");
            }
            catch (Exception ex) { Fallo(lblPaso1, "No se pudieron quitar los silencios: " + ex.Message); return; }
        }
        if (!chipTranscribir.Activo) { Terminar(); return; }

        lblPaso2.ForeColor = Tema.Texto;
        proceso = new ProcesoTranscripcion(vegas, config, ruta);
        try
        {
            proceso.Empezar(pistas, Elegidas(chipsVoz), Elegidas(chipsAmbiente), delegate (string t, double f) { Estado("2 · " + t, 0.1 + f * 0.9); });
        }
        catch (Exception ex) { Fallo(lblPaso2, ex.Message); return; }
        reloj.Start();
    }

    void Revisar()
    {
        if (proceso == null) return;
        proceso.Revisar();
        if (!proceso.Terminado)
        {
            Estado("2 · " + proceso.Texto, 0.1 + 0.9 * proceso.Fraccion);
            string d = proceso.Detalle ?? "";
            lblDetalle.Text = d.Length > 140 ? d.Substring(0, 140) + "…" : d;
            return;
        }
        reloj.Stop();
        ProcesoTranscripcion p = proceso;
        proceso = null;
        if (p.Error != null) { Fallo(lblPaso2, p.Error); return; }
        Hecho(lblPaso2, "✔ " + p.Texto);
        Terminar();
    }

    void Hecho(Etiqueta l, string texto)
    {
        l.ForeColor = Color.FromArgb(120, 220, 150);
        l.Text = texto;
        Application.DoEvents();
    }

    void Fallo(Etiqueta l, string mensaje)
    {
        reloj.Stop();
        Bloquear(false);
        Actualizar();
        l.ForeColor = Tema.Silencio;
        l.Text = "✖ " + mensaje;
        lblEstado.ForeColor = Tema.Silencio;
        lblEstado.Text = "Se detuvo. Lo que ya se hizo se puede deshacer con Ctrl+Z.";
    }

    void Terminar()
    {
        Bloquear(false);
        barra.Valor = 1;
        lblEstado.Text = "✔ Listo en " + Formato.Tiempo((DateTime.Now - comienzo).TotalSeconds) + ". Abriendo MomentosIA…";
        Application.DoEvents();
        AbrirMomentos = true;
        Pedir = chipPedir.Activo;
        DialogResult = DialogResult.OK;
        Close();
    }

    void Cancelar()
    {
        if (proceso != null) { reloj.Stop(); proceso.Cancelar(); proceso = null; }
        Fallo(lblPaso2, "Transcripción cancelada; no se guardó.");
    }
}
