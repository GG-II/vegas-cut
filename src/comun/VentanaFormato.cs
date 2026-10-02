using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

// Formato y ritmo de una serie: que tipo de video es, que busca, como se
// marca el avance, el narrador y las reglas de ritmo. "Aprender de este
// proyecto" mide el proyecto abierto (un episodio que funciono) y usa su
// ritmo como objetivo.
class VentanaFormato : VentanaBase
{
    public FormatoSerie Resultado;
    readonly Func<string, Medicion> medidor;
    readonly string proyecto;
    bool cargando;

    Combo cmbFormato = new Combo();
    Boton btnPreset = new Boton("Usar valores del formato", EstiloBoton.Secundario);
    CampoTexto txtPremisa = new CampoTexto();
    Combo cmbAvance = new Combo();
    Segmentado segNarrador = new Segmentado(new string[] { "Con narrador", "Sin narrador" });
    CampoTexto txtNarrador = new CampoTexto();
    CampoTexto txtEstilo = new CampoTexto();
    CampoNumero numPPM = Num("ppm", 80, 320, 5), numNarr = Num("s", 10, 600, 5), numRec = Num("/min", 0, 60, 1),
                numCMin = Num("/min", 1, 80, 1), numCMax = Num("/min", 1, 80, 1), numMus = Num("s", 10, 900, 5),
                numZona = Num("s", 30, 600, 10), numDMin = Num("min", 1, 240, 1), numDMax = Num("min", 1, 240, 1);
    Etiqueta lblAprendido, lblEstado;
    Boton btnAprender = new Boton("Aprender de este proyecto", EstiloBoton.Secundario);
    Boton btnGuardar = new Boton("Guardar", EstiloBoton.Primario);
    Boton btnCancelar = new Boton("Cancelar", EstiloBoton.Secundario);

    static CampoNumero Num(string sufijo, int min, int max, int paso)
    {
        CampoNumero n = new CampoNumero();
        n.Sufijo = sufijo; n.Minimo = min; n.Maximo = max; n.Paso = paso;
        return n;
    }

    // medidor: mide el proyecto abierto con el nombre del narrador (null si no se puede).
    public VentanaFormato(FormatoSerie f, string proyecto, Func<string, Medicion> medidor) : base("Formato y ritmo", 760)
    {
        this.medidor = medidor; this.proyecto = proyecto ?? "";
        StartPosition = FormStartPosition.CenterParent;
        Resultado = f.Copia();
        int m = Margen, w = Ancho;
        Encabezado("Formato y ritmo", "Qué tipo de video es, qué busca y a qué ritmo. PulirEpisodio lo usa para medir y proponer.");
        int y = 92;
        Texto("FORMATO", Tema.Pequena, Tema.TextoSuave, m, y, 200, 18);
        foreach (string x in FormatoSerie.Formatos) cmbFormato.Items.Add(x);
        Pos(cmbFormato, m, y + 20, 260, 30);
        Pos(btnPreset, m + 272, y + 18, 210, 32);
        y += 60;
        Texto("PREMISA Y OBJETIVO (de qué va la serie, a dónde quieres llevarla)", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        txtPremisa.Multilinea = true;
        Pos(txtPremisa, m, y + 20, w, 60);
        y += 90;
        Texto("AVANCE EN PANTALLA", Tema.Pequena, Tema.TextoSuave, m, y, 160, 18);
        foreach (string x in FormatoSerie.Avances) cmbAvance.Items.Add(x);
        Pos(cmbAvance, m, y + 20, 160, 30);
        Texto("NARRADOR", Tema.Pequena, Tema.TextoSuave, m + 176, y, 200, 18);
        Pos(segNarrador, m + 176, y + 18, 280, 34);
        Texto("SE LLAMA (en la transcripción)", Tema.Pequena, Tema.TextoSuave, m + 472, y, w - 472, 18);
        Pos(txtNarrador, m + 472, y + 18, w - 472, 34);
        y += 62;
        Texto("ESTILO DEL NARRADOR (cómo cuenta: tiempo verbal, humor, ganchos)", Tema.Pequena, Tema.TextoSuave, m, y, w, 18);
        txtEstilo.Multilinea = true;
        Pos(txtEstilo, m, y + 20, w, 52);
        y += 84;

        Texto("Ritmo", Tema.Seccion, Tema.Texto, m, y, 200, 22);
        y += 28;
        object[,] campos = {
            { "Velocidad del narrador", numPPM }, { "Máximo sin narrador", numNarr }, { "Recursos por minuto", numRec },
            { "Cortes por minuto, desde", numCMin }, { "hasta", numCMax }, { "Cambiar la música cada", numMus },
            { "Zona crítica del inicio", numZona }, { "Duración, desde", numDMin }, { "hasta", numDMax } };
        int cw = (w - 32) / 3;
        for (int i = 0; i < campos.GetLength(0); i++)
        {
            int cx = m + (i % 3) * (cw + 16), cy = y + (i / 3) * 58;
            Texto(((string)campos[i, 0]).ToUpperInvariant(), Tema.Pequena, Tema.TextoSuave, cx, cy, cw, 18);
            Pos((Control)campos[i, 1], cx, cy + 18, cw, 32);
        }
        y += 3 * 58 + 6;
        Pos(btnAprender, m, y, 230, 34);
        lblAprendido = Texto("", Tema.Pequena, Tema.TextoSuave, m + 242, y - 2, w - 242, 38);
        y += 44;
        lblEstado = Texto("", Tema.Pequena, Tema.TextoSuave, m, y, w - 290, 40);
        Pos(btnCancelar, m + w - 280, y, 120, 40);
        Pos(btnGuardar, m + w - 150, y, 150, 40);
        ClientSize = new Size(ClientSize.Width, y + 40 + 24);

        btnAprender.Enabled = medidor != null;
        if (medidor == null) Estado("Para aprender de un episodio, abre su proyecto en Vegas y ejecuta Series desde ahí.", false);

        Mostrar(Resultado);
        cmbFormato.SelectedIndexChanged += delegate { if (!cargando) Estado("Pulsa “Usar valores del formato” para cargar sus reglas de partida.", false); };
        btnPreset.Click += delegate
        {
            FormatoSerie p = FormatoSerie.Preset((string)cmbFormato.SelectedItem);
            p.Premisa = txtPremisa.Text.Trim();
            p.NarradorNombre = txtNarrador.Text.Trim().Length > 0 ? txtNarrador.Text.Trim() : p.NarradorNombre;
            Mostrar(p);
            Estado("Valores de partida de “" + p.Nombre + "”.", false);
        };
        btnAprender.Click += delegate { Aprender(); };
        btnCancelar.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
        btnGuardar.Click += delegate { Resultado = Leer(); DialogResult = DialogResult.OK; Close(); };
    }

    void Estado(string t, bool error) { lblEstado.Text = t; lblEstado.ForeColor = error ? Tema.Silencio : Tema.TextoSuave; }

    void Mostrar(FormatoSerie f)
    {
        cargando = true;
        cmbFormato.SelectedIndex = Math.Max(0, Array.IndexOf(FormatoSerie.Formatos, f.Nombre));
        txtPremisa.Text = (f.Premisa ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        cmbAvance.SelectedIndex = Math.Max(0, Array.IndexOf(FormatoSerie.Avances, f.Avance));
        segNarrador.Seleccion = f.Narrador ? 0 : 1;
        txtNarrador.Text = f.NarradorNombre;
        txtEstilo.Text = (f.EstiloNarrador ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n");
        MostrarReglas(f.Reglas);
        lblAprendido.Text = String.IsNullOrEmpty(f.Aprendido) ? "Las reglas salen del formato. Si tienes un episodio que funcionó bien, ábrelo y aprende de él."
                                                               : "Aprendido de " + f.Aprendido;
        Resultado.Aprendido = f.Aprendido;
        cargando = false;
    }

    void MostrarReglas(ReglasRitmo r)
    {
        numPPM.Valor = r.PPM; numNarr.Valor = r.NarradorCadaSeg; numRec.Valor = r.RecursosPorMin;
        numCMin.Valor = r.CortesMin; numCMax.Valor = r.CortesMax; numMus.Valor = r.MusicaCadaSeg;
        numZona.Valor = r.ZonaCriticaSeg; numDMin.Valor = (int)Math.Round(r.DuracionMin); numDMax.Valor = (int)Math.Round(r.DuracionMax);
    }

    FormatoSerie Leer()
    {
        FormatoSerie f = Resultado.Copia();
        f.Nombre = (string)cmbFormato.SelectedItem ?? "Otro";
        f.Premisa = txtPremisa.Text.Trim();
        f.Avance = (string)cmbAvance.SelectedItem ?? "Ninguno";
        f.Narrador = segNarrador.Seleccion == 0;
        f.NarradorNombre = txtNarrador.Text.Trim().Length > 0 ? txtNarrador.Text.Trim() : "Narrador";
        f.EstiloNarrador = txtEstilo.Text.Trim();
        ReglasRitmo r = f.Reglas;
        r.PPM = numPPM.Valor; r.NarradorCadaSeg = numNarr.Valor; r.RecursosPorMin = numRec.Valor;
        r.CortesMin = Math.Min(numCMin.Valor, numCMax.Valor); r.CortesMax = Math.Max(numCMin.Valor, numCMax.Valor);
        r.MusicaCadaSeg = numMus.Valor; r.ZonaCriticaSeg = numZona.Valor;
        r.DuracionMin = Math.Min(numDMin.Valor, numDMax.Valor); r.DuracionMax = Math.Max(numDMin.Valor, numDMax.Valor);
        return f;
    }

    void Aprender()
    {
        Medicion med;
        string narrador = txtNarrador.Text.Trim().Length > 0 ? txtNarrador.Text.Trim() : "Narrador";
        try { med = medidor(narrador); }
        catch (Exception ex) { Estado("No se pudo medir el proyecto: " + ex.Message, true); return; }
        if (med.Duracion < 60) { Estado("El proyecto dura menos de un minuto: abre un episodio terminado.", true); return; }
        FormatoSerie f = Leer();
        f.Reglas = Ritmo.Aprender(med, f.Reglas);
        if (med.HayNarrador) f.Narrador = true;
        f.Aprendido = (proyecto.Length > 0 ? Path.GetFileNameWithoutExtension(proyecto) : "el proyecto abierto") + " · " +
                      DateTime.Now.ToString("yyyy-MM-dd");
        Resultado.Aprendido = f.Aprendido;
        Mostrar(f);
        Estado("✔ " + Ritmo.Resumen(med) + (med.HayNarrador ? "" : " · no encontré la voz “" + narrador + "” en la transcripción, así " +
               "que no se aprendió nada del narrador.") + " Revisa los valores y guarda.", false);
    }
}
