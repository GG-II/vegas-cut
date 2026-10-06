using System;
using System.Collections.Generic;
using System.IO;

// Pruebas de LimpiarVoces: pedazos usados, comandos y volumen. Si hay ffmpeg,
// tambien de punta a punta (y con DeepFilterNet si DEEPFILTER apunta al .exe).
class PruebaVoces
{
    static int fallos;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static bool Cerca(double a, double b, double t) { return Math.Abs(a - b) <= t; }

    static int Main()
    {
        List<UsoVoz> usos = new List<UsoVoz>
        {
            new UsoVoz { Archivo = "C:/rec/partida.mkv", Flujo = 1, A = 10, B = 20, Pista = "A2" },
            new UsoVoz { Archivo = "C:/rec/partida.mkv", Flujo = 1, A = 21.5, B = 30, Pista = "A2" },
            new UsoVoz { Archivo = "C:/rec/partida.mkv", Flujo = 1, A = 100, B = 110, Pista = "A2" },
            new UsoVoz { Archivo = "C:/rec/partida.mkv", Flujo = 2, A = 10, B = 20, Pista = "A3" },
            new UsoVoz { Archivo = "C:/rec/partida.mkv", Flujo = 1, A = 0.3, B = 4, Pista = "A2" },
        };
        List<TrozoVoz> tr = LogicaVoces.Trozos(usos, 1, 3);
        tr.Sort(delegate (TrozoVoz a, TrozoVoz b) { int c = String.Compare(a.Pista, b.Pista); return c != 0 ? c : a.A.CompareTo(b.A); });
        Verificar(tr.Count == 4 && tr[0].A == 0 && tr[0].B == 5 && tr[1].A == 9 && tr[1].B == 31 && tr[2].A == 99 && tr[3].Pista == "A3" &&
                  LogicaVoces.Buscar(tr, usos[1]) == tr[1] && LogicaVoces.Buscar(tr, usos[3]) == tr[3],
                  "Voces: solo lo que usan los eventos, con margen, unido si está cerca y por pista de audio del archivo");
        LogicaVoces.Nombrar(tr, "C:/proy/Ep1.vegascut-voces");
        OpcionesVoces op = new OpcionesVoces();
        string ex = LogicaVoces.ArgsExtraer(tr[3]), dfa = LogicaVoces.ArgsDeepFilter(tr, 24, "C:/x/df"), niv = LogicaVoces.ArgsNivelar(tr[1].Entrada, tr[1], op);
        Verificar(ex.Contains("-ss 9 -t 12") && ex.Contains("-map 0:a:2") && ex.Contains("-ar 48000") && dfa.StartsWith("-D -a 24 -o") &&
                  niv.Contains("highpass=f=80") && niv.Contains("dynaudnorm") && niv.Contains("ebur128") &&
                  LogicaVoces.ArgsFinal(tr[1], -3.5, -6).Contains("volume=-3.5dB") && LogicaVoces.ArgsFinal(tr[1], 0, -6).Contains("-t 22") &&
                  LogicaVoces.ArgsFinal(tr[1], 0, -6).Contains("alimiter=limit=0.501"),
                  "Voces: comandos de ffmpeg y DeepFilterNet (pista correcta del archivo, sin retraso, duración exacta, tope de -6 dB)");
        double l1 = LogicaVoces.LeerLufs("  Integrated loudness:\n    I:         -23.4 LUFS\n    Threshold: -33.9 LUFS"), l2 = LogicaVoces.LeerLufs("I: -70.0 LUFS");
        tr[0].Lufs = -20; tr[1].Lufs = -30; tr[2].Lufs = double.NaN;
        double lp = LogicaVoces.LufsPista(new List<TrozoVoz> { tr[0], tr[1], tr[2] });
        Verificar(Cerca(l1, -23.4, 0.001) && double.IsNaN(l2) && lp < -20 && lp > -30 && LogicaVoces.Ganancia(-30, -16) == 14 &&
                  LogicaVoces.Ganancia(-50, -16) == 20 && LogicaVoces.Ganancia(double.NaN, -16) == 0,
                  "Voces: volumen medido (silencio = sin dato), promedio de la pista y ganancia con límites");

        // Una sola pista: cada clip en su lugar, con fundidos, desde su archivo limpio.
        string dirP = Path.Combine(Path.GetTempPath(), "vc-pista-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dirP);
        try
        {
            string fa = Path.Combine(dirP, "a.wav"), fb = Path.Combine(dirP, "b.wav"), sal = Path.Combine(dirP, "pista.wav");
            short[] ma = new short[48000 * 3], mb = new short[48000 * 2];
            for (int i = 0; i < ma.Length; i++) ma[i] = (short)(1000 + i / 48000 * 1000);   // 1000, 2000, 3000 por segundo
            for (int i = 0; i < mb.Length; i++) mb[i] = -500;
            Wav(fa, ma); Wav(fb, mb);
            List<PiezaPista> pz = new List<PiezaPista>
            {
                new PiezaPista { Archivo = fa, Desde = 1, En = 0.5, Largo = 1.0 },            // el segundo 1 de a, en 0.5
                new PiezaPista { Archivo = fb, Desde = 0, En = 2.0, Largo = 1.0, FundidoEntrada = 0.5 },
            };
            LogicaVoces.ArmarPista(sal, pz, 3.0);
            short[] r = Leer(sal);
            Verificar(r.Length == 48000 * 3 && r[0] == 0 && r[24000 + 10] == 2000 && r[72000 - 10] == 2000 && r[72000 + 10] == 0 &&
                      r[96000 + 1] > -20 && r[96000 + 36000] == -500 && r[143999] == -500,
                      "Voces: la pista limpia pone cada clip en su lugar, desde su parte del archivo, con sus fundidos y silencio entre clips");
        }
        finally { try { Directory.Delete(dirP, true); } catch { } }

        string ff = LogicaVoces.BuscarFfmpeg("", "");
        if (ff.Length == 0) { Console.WriteLine("(sin ffmpeg: se salta la prueba de punta a punta)"); return Fin(); }
        string dir = Path.Combine(Path.GetTempPath(), "vc-voces-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            // Un archivo con dos pistas de audio: una voz bajita (pista 0) y otra fuerte (pista 1), con ruido.
            string fuente = Path.Combine(dir, "partida.mka");
            int c;
            string voz = "sine=f=180:d=40,apulsator=hz=2.5:amount=1,volume={0}[v{1}];anoisesrc=d=40:c=pink:a=0.004[n{1}];[v{1}][n{1}]amix=inputs=2:normalize=0[a{1}]";
            LogicaVoces.Ejecutar(ff, "-hide_banner -v error -y -filter_complex \"" + String.Format(voz, "0.12", 0) + ";" + String.Format(voz, "0.6", 1) +
                                 "\" -map [a0] -map [a1] -c:a pcm_s16le " + "\"" + fuente + "\"", out c, null);
            List<UsoVoz> u2 = new List<UsoVoz>
            {
                new UsoVoz { Archivo = fuente, Flujo = 0, A = 5, B = 15, Pista = "A2" },
                new UsoVoz { Archivo = fuente, Flujo = 0, A = 25, B = 35, Pista = "A2" },
                new UsoVoz { Archivo = fuente, Flujo = 1, A = 5, B = 15, Pista = "A3" },
            };
            List<TrozoVoz> t2 = LogicaVoces.Trozos(u2, 1, 3);
            string carpeta = Path.Combine(dir, "Ep1.vegascut-voces");
            LogicaVoces.Nombrar(t2, carpeta);
            Directory.CreateDirectory(Path.Combine(carpeta, "tmp"));
            string df = Environment.GetEnvironmentVariable("DEEPFILTER") ?? "";
            bool conDf = df.Length > 0 && File.Exists(df);
            bool ok = c == 0;
            foreach (TrozoVoz t in t2) { LogicaVoces.Ejecutar(ff, LogicaVoces.ArgsExtraer(t), out c, null); ok &= c == 0; }
            if (conDf)
            {
                Directory.CreateDirectory(Path.Combine(Path.Combine(carpeta, "tmp"), "df"));
                LogicaVoces.Ejecutar(df, LogicaVoces.ArgsDeepFilter(t2, 24, Path.Combine(Path.Combine(carpeta, "tmp"), "df")), out c, null); ok &= c == 0;
            }
            foreach (TrozoVoz t in t2)
            {
                string sal = LogicaVoces.Ejecutar(ff, LogicaVoces.ArgsNivelar(conDf ? t.Sinruido : t.Entrada, t, op), out c, null);
                t.Lufs = LogicaVoces.LeerLufs(sal); ok &= c == 0;
            }
            Dictionary<string, double> g = new Dictionary<string, double>();
            foreach (string p in new string[] { "A2", "A3" })
                g[p] = LogicaVoces.Ganancia(LogicaVoces.LufsPista(t2.FindAll(delegate (TrozoVoz x) { return x.Pista == p; })), -20);
            List<string> medidas = new List<string>();
            bool duraciones = true, volumen = true, picos = true;
            foreach (TrozoVoz t in t2)
            {
                LogicaVoces.Ejecutar(ff, LogicaVoces.ArgsFinal(t, g[t.Pista], -6), out c, null); ok &= c == 0;
                string info = LogicaVoces.Ejecutar(ff, "-hide_banner -nostats -i \"" + t.Final + "\" -af ebur128=framelog=quiet:peak=sample -f null -", out c, null);
                System.Text.RegularExpressions.Match pk = System.Text.RegularExpressions.Regex.Match(info, @"Peak:\s*(-?[\d.]+|-inf)\s*dBFS");
                double picoMedido = pk.Success && pk.Groups[1].Value != "-inf" ? double.Parse(pk.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : -99;
                picos &= picoMedido <= -5.9;
                double lufs = LogicaVoces.LeerLufs(info);
                System.Text.RegularExpressions.Match d = System.Text.RegularExpressions.Regex.Match(info, @"Duration: (\d+):(\d+):(\d+\.\d+)");
                double dur = d.Success ? int.Parse(d.Groups[1].Value) * 3600 + int.Parse(d.Groups[2].Value) * 60 +
                             double.Parse(d.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture) : -1;
                medidas.Add(t.Pista + " " + lufs.ToString("0.0") + " LUFS pico " + picoMedido.ToString("0.0") + " " + dur.ToString("0.00") + " s");
                duraciones &= Cerca(dur, t.Duracion, 0.02);
                volumen &= Cerca(lufs, -20, 2);
            }
            Console.WriteLine("    " + String.Join(" · ", medidas.ToArray()) + " · ganancia A2 " + g["A2"].ToString("0.0") + " dB, A3 " + g["A3"].ToString("0.0") + " dB" +
                              (conDf ? " · con DeepFilterNet" : " · sin DeepFilterNet"));
            // DeepFilterNet borra el tono de prueba (no es voz): con el solo se miden las duraciones.
            Verificar(ok && t2.Count == 3 && duraciones && picos && (conDf || (volumen && g["A2"] > g["A3"] + 10)),
                      "Voces de punta a punta: la pista bajita sube y la fuerte baja hasta -20 LUFS, ningún pico pasa de -6 dB y cada pedazo dura lo mismo");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
        return Fin();
    }

    static void Wav(string ruta, short[] m)
    {
        using (BinaryWriter w = new BinaryWriter(File.Create(ruta)))
        {
            w.Write(new char[] { 'R', 'I', 'F', 'F' }); w.Write(36 + m.Length * 2 + 26);
            w.Write(new char[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
            w.Write((short)1); w.Write((short)1); w.Write(48000); w.Write(96000); w.Write((short)2); w.Write((short)16);
            w.Write(new char[] { 'L', 'I', 'S', 'T' }); w.Write(18); w.Write(new byte[18]);   // como los de ffmpeg: otro bloque antes de los datos
            w.Write(new char[] { 'd', 'a', 't', 'a' }); w.Write(m.Length * 2);
            foreach (short v in m) w.Write(v);
        }
    }

    static short[] Leer(string ruta)
    {
        byte[] b = File.ReadAllBytes(ruta);
        short[] r = new short[(b.Length - 44) / 2];
        for (int i = 0; i < r.Length; i++) r[i] = (short)(b[44 + 2 * i] | (b[45 + 2 * i] << 8));
        return r;
    }

    static int Fin()
    {
        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " pruebas fallaron.");
        return fallos == 0 ? 0 : 1;
    }
}
