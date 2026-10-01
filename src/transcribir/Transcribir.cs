using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ScriptPortal.Vegas;

public class EntryPoint
{
    public void FromVegas(Vegas vegas)
    {
        Project proyecto = vegas.Project;
        if (String.IsNullOrEmpty(proyecto.FilePath))
        {
            MessageBox.Show("Guarda el proyecto primero: la transcripción se guarda junto al .veg.", "Transcribir");
            return;
        }
        List<InfoPista> pistas = PistasVegas.Listar(proyecto);
        if (pistas.Count == 0)
        {
            MessageBox.Show("El proyecto no tiene pistas de audio.", "Transcribir");
            return;
        }
        using (VentanaTranscribir v = new VentanaTranscribir(vegas, pistas)) v.ShowDialog();
    }
}

class BarraProgreso : ControlBase
{
    double valor;
    public double Valor { get { return valor; } set { valor = Math.Max(0, Math.Min(1, value)); Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f))
        using (SolidBrush b = new SolidBrush(Tema.Campo)) g.FillPath(b, p);
        if (valor > 0)
            using (GraphicsPath p = Tema.Redondeado(new RectangleF(0, 0, Math.Max(Height, (float)(Width - 1) * (float)valor), Height - 1), Height / 2f))
            using (SolidBrush b = new SolidBrush(Tema.Acento)) g.FillPath(b, p);
    }
}

class VentanaTranscribir : VentanaBase
{
    readonly Vegas vegas;
    readonly List<InfoPista> pistas;
    readonly Configuracion config = Configuracion.Cargar();
    readonly string ruta;

    List<Boton> chipsVoz = new List<Boton>(), chipsAmbiente = new List<Boton>();
    Segmentado segRango = new Segmentado(new string[] { "Todo el proyecto", "Selección de tiempo" });
    BarraProgreso barra = new BarraProgreso();
    Etiqueta lblEstado, lblDetalle, lblWhisper;
    CampoTexto vista = new CampoTexto();
    Boton btnTranscribir = new Boton("Transcribir", EstiloBoton.Primario);
    Boton btnCerrar = new Boton("Cerrar", EstiloBoton.Secundario);

    // Estado del trabajo
    Transcripcion resultado;
    List<int> colaVoces = new List<int>();          // indices de hablante pendientes
    Dictionary<int, string> wavs = new Dictionary<int, string>();
    TareaWhisper tarea;
    int hablanteActual = -1;
    double inicio, duracion, segundosHechos, segundosTotales;
    DateTime comienzo;
    System.Windows.Forms.Timer reloj = new System.Windows.Forms.Timer();
    bool trabajando;

    public VentanaTranscribir(Vegas vegas, List<InfoPista> pistas) : base("Transcribir", 860)
    {
        this.vegas = vegas;
        this.pistas = pistas;
        ruta = Transcripcion.RutaPara(vegas.Project.FilePath);
        int m = Margen, w = Ancho;
        Encabezado("Transcribir", "Convierte la voz en texto con el tiempo de cada palabra y mide el sonido de cada pista.");

        int y = 92;
        if (File.Exists(ruta))
        {
            string info = "Este proyecto ya tiene una transcripción";
            try
            {
                Transcripcion previa = Transcripcion.Cargar(ruta);
                info += " (" + previa.Creada + ", " + previa.Segmentos.Count + " frases)";
            }
            catch { }
            Texto(info + ". Si transcribes otra vez, se reemplaza.", Tema.Pequena, Tema.AcentoHover, m, y, w, 18);
            y += 24;
        }

        // Voces y ambiente
        Texto("VOCES", Tema.Pequena, Tema.TextoSuave, m, y, 60, 18);
        Texto("Se transcriben. Marca tu micrófono, la llamada, etc.", Tema.Pequena, Tema.TextoSuave, m + 64, y, w - 64, 18);
        y = Chips(chipsVoz, y + 22, PistasVegas.SugerirVoz(pistas)) + 12;
        Texto("AMBIENTE", Tema.Pequena, Tema.TextoSuave, m, y, 80, 18);
        Texto("Solo se mide su sonido (juego, música): ayuda a encontrar explosiones y momentos intensos.",
              Tema.Pequena, Tema.TextoSuave, m + 84, y, w - 84, 18);
        y = Chips(chipsAmbiente, y + 22, -1) + 16;

        for (int i = 0; i < pistas.Count; i++)
        {
            int k = i;
            chipsVoz[k].Click += delegate { if (trabajando) return; chipsVoz[k].Activo = !chipsVoz[k].Activo; if (chipsVoz[k].Activo) chipsAmbiente[k].Activo = false; Actualizar(); };
            chipsAmbiente[k].Click += delegate { if (trabajando) return; chipsAmbiente[k].Activo = !chipsAmbiente[k].Activo; if (chipsAmbiente[k].Activo) chipsVoz[k].Activo = false; Actualizar(); };
        }

        segRango.Seleccion = 0;
        segRango.Habilitar(1, PistasVegas.HaySeleccion(vegas));
        Pos(segRango, m, y, 300, 34);
        lblWhisper = Texto("", Tema.Pequena, Tema.TextoSuave, m + 316, y, w - 316, 34);
        lblWhisper.TextAlign = ContentAlignment.MiddleLeft;
        y += 50;

        // Progreso
        lblEstado = Texto("Listo para transcribir.", Tema.Negrita, Tema.Texto, m, y, w, 22);
        Pos(barra, m, y + 28, w, 10);
        lblDetalle = Texto("", Tema.Pequena, Tema.TextoSuave, m, y + 44, w, 18);
        y += 72;

        vista.Multilinea = true;
        vista.Caja.ReadOnly = true;
        vista.Caja.Font = Tema.Fuente(9f, FontStyle.Regular);
        vista.Text = "Aquí aparecerá el texto cuando termine.";
        Pos(vista, m, y, w, 190);
        y += 206;

        Pos(btnCerrar, m + w - 300, y, 110, 40);
        Pos(btnTranscribir, m + w - 180, y, 180, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        btnTranscribir.Click += delegate { if (trabajando) Cancelar(); else Comenzar(); };
        btnCerrar.Click += delegate { Close(); };
        FormClosing += delegate (object s, FormClosingEventArgs e)
        {
            if (trabajando && MessageBox.Show(this, "¿Cancelar la transcripción?", "Transcribir",
                    MessageBoxButtons.YesNo) == DialogResult.No) { e.Cancel = true; return; }
            Cancelar();
        };
        reloj.Interval = 300;
        reloj.Tick += delegate { Revisar(); };
        Actualizar();
    }

    int Chips(List<Boton> lista, int y, int activa)
    {
        int cx = Margen;
        for (int i = 0; i < pistas.Count; i++)
        {
            Boton c = new Boton(pistas[i].Nombre, EstiloBoton.Chip);
            c.Activo = i == activa;
            int w = Math.Min(Ancho, TextRenderer.MeasureText(c.Text, Tema.Normal).Width + 26);
            if (cx + w > Margen + Ancho) { cx = Margen; y += 34; }
            Pos(c, cx, y, w, 28);
            lista.Add(c);
            cx += w + 6;
        }
        return y + 28;
    }

    void Actualizar()
    {
        int voces = 0;
        foreach (Boton b in chipsVoz) if (b.Activo) voces++;
        btnTranscribir.Enabled = trabajando || (voces > 0 && config.TieneWhisper);
        if (!config.TieneWhisper)
        {
            lblWhisper.ForeColor = Tema.Silencio;
            lblWhisper.Text = "Falta configurar Faster-Whisper-XXL: ejecuta “ConfigurarVegasCut”.";
        }
        else
        {
            lblWhisper.ForeColor = Tema.TextoSuave;
            lblWhisper.Text = "Whisper: " + config.WhisperModelo + " · " +
                (config.WhisperDispositivo == "cpu" ? "procesador" : "tarjeta") + " · " + config.WhisperPrecision +
                " · idioma " + config.Idioma + "  (se cambia en ConfigurarVegasCut)";
        }
    }

    // ------------------------------------------------------------ trabajo

    void Comenzar()
    {
        try
        {
            PistasVegas.ObtenerRango(vegas, segRango.Seleccion == 1, out inicio, out duracion);
        }
        catch (Exception ex) { Fallo(ex.Message); return; }

        trabajando = true;
        btnTranscribir.Text = "Cancelar";
        btnCerrar.Enabled = false;
        foreach (Boton b in chipsVoz) b.Enabled = false;
        foreach (Boton b in chipsAmbiente) b.Enabled = false;
        segRango.Enabled = false;
        comienzo = DateTime.Now;

        resultado = new Transcripcion();
        resultado.Proyecto = vegas.Project.FilePath;
        resultado.Creada = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        resultado.Idioma = config.Idioma;
        resultado.Modelo = config.WhisperModelo;
        resultado.Inicio = inicio;
        resultado.Duracion = duracion;
        resultado.DuracionProyecto = vegas.Project.Length.ToMilliseconds() / 1000.0;

        // Nombres que ya habias puesto en una transcripcion anterior.
        Dictionary<string, string> nombresPrevios = new Dictionary<string, string>();
        try
        {
            if (File.Exists(ruta))
                foreach (Hablante h in Transcripcion.Cargar(ruta).Hablantes) nombresPrevios[h.Etiqueta] = h.Nombre;
        }
        catch { }

        // 1. Render de cada pista (en el hilo de Vegas) y niveles por segundo.
        try
        {
            int total = 0, hechas = 0;
            for (int i = 0; i < pistas.Count; i++) if (chipsVoz[i].Activo || chipsAmbiente[i].Activo) total++;
            for (int i = 0; i < pistas.Count; i++)
            {
                if (!chipsVoz[i].Activo && !chipsAmbiente[i].Activo) continue;
                hechas++;
                Estado("Leyendo el audio de " + pistas[i].Nombre + " (" + hechas + " de " + total + ")…", 0.02 * hechas / total);
                string wav = PistasVegas.RenderizarWav(vegas, pistas[i].Pista, inicio, duracion);
                Analisis a = WavNiveles.Leer(wav, Analisis.Paso);

                Hablante h = new Hablante();
                h.Etiqueta = pistas[i].Etiqueta;
                string previo;
                h.Nombre = nombresPrevios.TryGetValue(h.Etiqueta, out previo) && previo.Length > 0 ? previo : h.Etiqueta;
                h.Archivo = pistas[i].Archivo ?? "";
                h.Voz = chipsVoz[i].Activo;
                Transcripcion.NivelesPorSegundo(a, out h.Nivel, out h.Pico);
                resultado.Hablantes.Add(h);

                if (h.Voz)
                {
                    wavs[resultado.Hablantes.Count - 1] = wav;
                    colaVoces.Add(resultado.Hablantes.Count - 1);
                }
                else
                {
                    try { File.Delete(wav); } catch { }
                }
            }
        }
        catch (Exception ex)
        {
            Limpiar();
            Fallo("No se pudo leer el audio: " + ex.Message);
            return;
        }

        // 2. Whisper, una voz tras otra, en segundo plano.
        segundosTotales = duracion * colaVoces.Count;
        segundosHechos = 0;
        SiguienteVoz();
    }

    void SiguienteVoz()
    {
        if (colaVoces.Count == 0) { Terminar(); return; }
        hablanteActual = colaVoces[0];
        colaVoces.RemoveAt(0);
        tarea = new TareaWhisper();
        try
        {
            tarea.Iniciar(config, wavs[hablanteActual], duracion);
            reloj.Start();
            Revisar();
        }
        catch (Exception ex)
        {
            Limpiar();
            Fallo("No se pudo iniciar Whisper: " + ex.Message);
        }
    }

    void Revisar()
    {
        if (tarea == null) return;
        Hablante h = resultado.Hablantes[hablanteActual];
        double hecho = (segundosHechos + tarea.Avance) / Math.Max(1, segundosTotales);
        TimeSpan pasado = DateTime.Now - comienzo;
        string resto = "";
        if (hecho > 0.03)
        {
            TimeSpan falta = TimeSpan.FromSeconds(pasado.TotalSeconds * (1 - hecho) / hecho);
            resto = " · faltan ~" + Formato.Tiempo(falta.TotalSeconds);
        }
        Estado("Transcribiendo " + h.Etiqueta + ": " + Formato.Tiempo(tarea.Avance) + " de " + Formato.Tiempo(duracion) +
               " · " + Formato.Tiempo(pasado.TotalSeconds) + " transcurrido" + resto, 0.02 + 0.98 * hecho);
        string linea = tarea.UltimaLinea;
        lblDetalle.Text = linea.Length > 140 ? linea.Substring(0, 140) + "…" : linea;

        if (!tarea.Terminada) return;
        reloj.Stop();
        TareaWhisper t = tarea;
        tarea = null;
        if (t.Cancelada) { t.Limpiar(); return; }
        try
        {
            resultado.AgregarWhisper(t.Resultado(), hablanteActual, inicio);
        }
        catch (Exception ex)
        {
            t.Limpiar();
            Limpiar();
            Fallo(ex.Message);
            return;
        }
        t.Limpiar();
        try { File.Delete(wavs[hablanteActual]); } catch { }
        wavs.Remove(hablanteActual);
        segundosHechos += duracion;
        SiguienteVoz();
    }

    void Terminar()
    {
        try
        {
            resultado.Guardar(ruta);
        }
        catch (Exception ex) { Fallo("No se pudo guardar la transcripción: " + ex.Message); return; }

        int palabras = 0;
        foreach (Segmento s in resultado.Segmentos) palabras += s.Palabras.Count;
        Listo();
        barra.Valor = 1;
        lblEstado.ForeColor = Color.FromArgb(120, 220, 150);
        lblEstado.Text = "✔ Listo: " + resultado.Segmentos.Count + " frases y " + palabras + " palabras en " +
                         Formato.Tiempo((DateTime.Now - comienzo).TotalSeconds) + ".";
        lblDetalle.Text = "Guardado en " + ruta;

        StringBuilder sb = new StringBuilder();
        foreach (Segmento s in resultado.Segmentos)
        {
            sb.Append("[" + Formato.Tiempo(s.Inicio) + "] " + resultado.Hablantes[s.Hablante].Nombre + ": " + s.Texto + "\r\n");
            if (sb.Length > 60000) { sb.Append("…"); break; }
        }
        vista.Text = sb.Length > 0 ? sb.ToString() : "No se detectó voz en las pistas marcadas.";
    }

    void Cancelar()
    {
        if (!trabajando) return;
        reloj.Stop();
        if (tarea != null) { tarea.Cancelar(); tarea.Limpiar(); tarea = null; }
        colaVoces.Clear();
        Limpiar();
        Listo();
        lblEstado.ForeColor = Tema.TextoSuave;
        lblEstado.Text = "Cancelado. No se guardó nada.";
        barra.Valor = 0;
    }

    void Estado(string texto, double fraccion)
    {
        lblEstado.ForeColor = Tema.Texto;
        lblEstado.Text = texto;
        barra.Valor = fraccion;
        Application.DoEvents();
    }

    void Fallo(string mensaje)
    {
        Listo();
        lblEstado.ForeColor = Tema.Silencio;
        lblEstado.Text = "✖ No se pudo transcribir.";
        vista.Text = mensaje;
    }

    void Listo()
    {
        trabajando = false;
        btnTranscribir.Text = "Transcribir";
        btnCerrar.Enabled = true;
        foreach (Boton b in chipsVoz) b.Enabled = true;
        foreach (Boton b in chipsAmbiente) b.Enabled = true;
        segRango.Enabled = true;
        Actualizar();
    }

    void Limpiar()
    {
        foreach (string w in wavs.Values) { try { File.Delete(w); } catch { } }
        wavs.Clear();
    }
}
