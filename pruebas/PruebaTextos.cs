// Pruebas de TextosDesdeMarcadores: RTF de Titulos y texto, lectura de los
// marcadores y creacion de eventos con la API falsa (ver pruebas/ejecutar.sh).
using System;
using System.Collections.Generic;
using ScriptPortal.Vegas;

class PruebaTextos
{
    static int fallos = 0;
    static void Verificar(bool ok, string que) { Console.WriteLine((ok ? "OK    " : "FALLA ") + que); if (!ok) fallos++; }
    static bool Cerca(double a, double b) { return Math.Abs(a - b) < 0.002; }
    static Timecode S(double s) { return Timecode.FromMilliseconds(s * 1000); }

    // Asi guarda Vegas 20 el texto de Titulos y texto.
    const string Plantilla = @"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang3082{\fonttbl{\f0\fnil\fcharset0 Montserrat Black;}}
{\colortbl ;\red255\green255\blue255;}
{\*\generator Riched20 10.0.19041}\viewkind4\uc1
\pard\qc\cf1\b\f0\fs96 Hola mundo\par
}
";

    static int Main()
    {
        // ------------------------------------------------------------ RTF
        Verificar(Rtf.TextoPlano(Plantilla) == "Hola mundo", "RTF: lee el texto visible sin fuentes ni colores");
        string nuevo = Rtf.ReemplazarTexto(Plantilla, "¡Creeper! {boom}");
        Verificar(nuevo.Contains(@"\b\f0\fs96 \u161?Creeper! \{boom\}\par"), "RTF: cambia el texto y conserva el formato");
        Verificar(nuevo.StartsWith(@"{\rtf1\ansi\ansicpg1252") && nuevo.Contains("Montserrat Black"), "RTF: conserva fuentes y colores");
        Verificar(Rtf.TextoPlano(nuevo) == "¡Creeper! {boom}", "RTF: acentos y llaves de ida y vuelta");
        Verificar(Rtf.TextoPlano(Rtf.ReemplazarTexto(Plantilla, "Línea 1\nLínea 2")) == "Línea 1\nLínea 2", "RTF: varias líneas con \\par");
        string dos = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\pard\qc\f0\fs48 Hola \b mundo\b0\par}";
        string r2 = Rtf.ReemplazarTexto(dos, "Adiós");
        Verificar(Rtf.TextoPlano(r2) == "Adiós" && r2.Contains(@"\fs48 Adi\u243?s\b0\par"), "RTF: con dos formatos queda el del primer caracter");
        Verificar(Rtf.TextoPlano(@"{\rtf1\ansi Canci\'f3n\par}") == "Canción", "RTF: lee acentos \\'xx");
        string vacio = @"{\rtf1\ansi{\fonttbl{\f0 Arial;}}\pard\qc\f0\fs48\par}";
        Verificar(Rtf.TextoPlano(Rtf.ReemplazarTexto(vacio, "Nuevo")) == "Nuevo", "RTF: plantilla sin texto");
        Verificar(Rtf.TextoPlano(Rtf.ReemplazarTexto("texto suelto", "Hola")) == "Hola", "RTF: si no es RTF arma uno nuevo");

        // ------------------------------------------------------ marcadores
        Vegas v = new Vegas();
        Project p = v.Project;
        p.Length = S(100);
        p.Markers.Add(new Marker(S(30), "texto: tercero"));
        p.Markers.Add(new Marker(S(10), "TEXTO: Primero"));
        p.Markers.Add(new Marker(S(12), "★8 Explosión"));
        p.Markers.Add(new Marker(S(11.5), "TEXTO:  Segundo "));
        p.Markers.Add(new Marker(S(50), "TEXTO:"));
        List<TextoMarcado> textos = LogicaTextos.Leer(p);
        Verificar(textos.Count == 3 && textos[0].Texto == "Primero" && textos[1].Texto == "Segundo" && textos[2].Texto == "tercero",
            "Marcadores: solo los TEXTO:, en orden y sin espacios");
        LogicaTextos.Duraciones(textos, 3, 100);
        Verificar(Cerca(textos[0].Fin, 11.4) && Cerca(textos[1].Fin, 14.5) && Cerca(textos[2].Fin, 33),
            "Duración: el texto termina antes si el siguiente empieza");
        textos[1].Elegido = false;
        LogicaTextos.Duraciones(textos, 3, 100);
        Verificar(Cerca(textos[0].Fin, 13) && LogicaTextos.Elegidos(textos) == 2, "Duración: los desmarcados no cuentan");

        // ------------------------------------------------------ plantilla
        PlugInNode titulos = new PlugInNode { Name = "VEGAS Títulos y texto", UniqueID = "{Svfx:com.vegascreativesoftware:titlesandtext}" };
        v.Generators.Hijos.Add(titulos);
        VideoTrack video = new VideoTrack(0, "Video");
        p.Tracks.Add(video);
        VideoEvent clip = video.AddVideoEvent(S(0), S(100));
        clip.ActiveTake = new Take { Media = new Media { FilePath = "partida.mp4" } };
        Verificar(!GeneradorTexto.EsTexto(clip), "Plantilla: un video normal no es texto");

        Plantilla sinTexto = GeneradorTexto.Buscar(v);
        Verificar(sinTexto.Evento == null && sinTexto.PlugIn == titulos, "Plantilla: sin textos usa Títulos y texto normal");

        VideoTrack pistaTextos = new VideoTrack(1, "Mis textos");
        p.Tracks.Add(pistaTextos);
        VideoEvent tpl = pistaTextos.AddVideoEvent(S(60), S(4));
        Media m = new Media(titulos);
        tpl.AddTake(m.Streams.GetItemByMediaType(MediaType.Video, 0));
        ((OFXStringParameter)m.Generator.OFXEffect.FindParameterByName("Text")).Value = Plantilla;
        ((OFXDoubleParameter)m.Generator.OFXEffect.FindParameterByName("Scale")).Value = 2.5;
        tpl.FadeIn.Length = S(0.5);
        PlugInNode sombra = new PlugInNode { Name = "Sombra", UniqueID = "sombra" };
        Effect fx = new Effect(sombra);
        ((OFXDoubleParameter)fx.OFXEffect.FindParameterByName("Blur")).Value = 7;
        tpl.Effects.Add(fx);
        VideoEvent otro = pistaTextos.AddVideoEvent(S(5), S(2));
        otro.AddTake(new Media(titulos).Streams.GetItemByMediaType(MediaType.Video, 0));

        v.Transport.CursorPosition = S(6);
        Verificar(GeneradorTexto.Buscar(v).Evento == otro, "Plantilla: sin selección, el texto más cercano al cursor");
        tpl.Selected = true;
        Plantilla pl = GeneradorTexto.Buscar(v);
        Verificar(pl.Evento == tpl && pl.Texto == "Hola mundo" && pl.PlugIn == titulos, "Plantilla: el texto seleccionado");

        VideoEvent hecho = GeneradorTexto.Crear(pistaTextos, pl, 10, 1.4, "¡Primero!");
        Media hm = hecho.ActiveTake.Media;
        string rtf = ((OFXStringParameter)hm.Generator.OFXEffect.FindParameterByName("Text")).Value;
        Verificar(Rtf.TextoPlano(rtf) == "¡Primero!" && rtf.Contains("Montserrat Black"), "Crear: texto nuevo con la fuente de la plantilla");
        Verificar(((OFXDoubleParameter)hm.Generator.OFXEffect.FindParameterByName("Scale")).Value == 2.5, "Crear: copia los demás parámetros");
        Verificar(Cerca(hecho.Start.ToMilliseconds() / 1000, 10) && Cerca(hecho.Length.ToMilliseconds() / 1000, 1.4) &&
                  Cerca(hecho.FadeIn.Length.ToMilliseconds() / 1000, 0.5), "Crear: tiempo, duración y fundido");
        Verificar(hecho.Effects.Count == 1 && ((OFXDoubleParameter)hecho.Effects[0].OFXEffect.FindParameterByName("Blur")).Value == 7,
            "Crear: copia los efectos del evento");
        Verificar(hm != m && hm.Generator.OFXEffect.Cambios > 0, "Crear: medio propio (editar uno no cambia los demás)");

        Console.WriteLine(fallos == 0 ? "\nTodo bien." : "\n" + fallos + " pruebas fallaron.");
        return fallos == 0 ? 0 : 1;
    }
}
