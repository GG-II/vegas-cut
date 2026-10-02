using System;
using System.Collections.Generic;
using System.Text;

// =====================================================================
// Tipos de capitulo: la formula con la que esta armado un capitulo (un
// juego, un misterio, una persecucion, el capitulo de un personaje...),
// aparte de su papel en la temporada (primero, clave, final...).
//
// Salen de revisar las escenas de los ~190 episodios del anime de JoJo
// (PB/BT, SC, DU, GW, SO y SBR) en jojowiki; docs/tipos-de-capitulo.md
// tiene la tabla con los ejemplos.
// =====================================================================

public class TipoCapitulo
{
    public string Clave, Nombre, Estructura, Senales, EnSerie, Ejemplos;

    public TipoCapitulo(string clave, string nombre, string estructura, string senales, string enSerie, string ejemplos)
    {
        Clave = clave; Nombre = nombre; Estructura = estructura; Senales = senales; EnSerie = enSerie; Ejemplos = ejemplos;
    }
}

// Lo que se recuerda de un capitulo ya producido, para los siguientes.
public class RegistroCapitulo
{
    public string Tipo = "", Forma = "", Titulo = "", Cierre = "";

    public bool Vacio { get { return Tipo.Length == 0 && Titulo.Length == 0 && Cierre.Length == 0; } }

    public Dictionary<string, object> Escribir()
    {
        Dictionary<string, object> d = new Dictionary<string, object>();
        d["tipo"] = Tipo; d["forma"] = Forma; d["titulo"] = Titulo; d["cierre"] = Cierre;
        return d;
    }

    public static RegistroCapitulo Leer(object o)
    {
        if (o == null) return null;
        RegistroCapitulo r = new RegistroCapitulo();
        r.Tipo = TiposCapitulo.Normalizar(Json.Texto(o, "tipo"));
        r.Forma = Json.Texto(o, "forma"); r.Titulo = Json.Texto(o, "titulo"); r.Cierre = Json.Texto(o, "cierre");
        return r.Vacio ? null : r;
    }

    public string Texto()
    {
        TipoCapitulo t = TiposCapitulo.Buscar(Tipo);
        List<string> l = new List<string>();
        if (Titulo.Length > 0) l.Add("«" + Titulo + "»");
        if (t != null) l.Add("tipo: " + t.Nombre);
        if (Forma.Length > 0 && Forma != "normal") l.Add(Forma.Replace("_", " "));
        if (Cierre.Length > 0) l.Add("cerró con: " + Cierre);
        return String.Join("; ", l.ToArray());
    }
}

public static class TiposCapitulo
{
    public const string Detectar = "Que lo detecte la IA";

    public static readonly TipoCapitulo[] Todos = {
        new TipoCapitulo("rival", "Rival de la semana",
            "llegada o viaje con humor (0–15 %) → algo raro, sin explicarlo (~13 %) → se revela qué es (~35 %) → crisis (~45 %) → " +
            "giro: el truco o la ayuda (~58 %) → derrota (~77 %) → remate cómico → gancho al siguiente.",
            "un solo problema (mob, jugador, trampa, jefe) que aparece y se resuelve dentro del material.",
            "un mob fuerte, un jefe, un jugador rival o una trampa de la etapa.",
            "SC 4 Tower of Gray, SC 7 Strength, SC 8 Devil, SC 13 Wheel of Fortune, GW 24–25 Notorious B.I.G."),
        new TipoCapitulo("arco_abre", "Abre un enfrentamiento largo (parte 1)",
            "llegada y humor largos (0–25 %) → el problema aparece tarde (~30 %) → todo empeora sin pausa → termina en el PEOR " +
            "momento (98 %), sin remate, con «continuará».",
            "el material termina a mitad de algo (la pelea o el reto no se resuelve) o el problema da para más de un capítulo.",
            "una grabación que corta a mitad del reto o de la pelea.",
            "SC 10 Emperor and Hanged Man 1 (muere Avdol), SC 38 Pet Shop 1, DU 28 Highway Star 1, GW 15 Grateful Dead 1."),
        new TipoCapitulo("arco_medio", "Parte del medio de un arco",
            "recap corto del cliffhanger → la pelea sigue → cambio de foco a otro grupo u otro hilo → un logro a medias y un nuevo " +
            "golpe → termina en otro cliffhanger.",
            "el capítulo anterior quedó a medias y este material tampoco lo cierra.",
            "la segunda sesión de un reto largo (un jefe, una construcción, una carrera de varias etapas).",
            "SC 43 Vanilla Ice 2, DU 4 Nijimura 2, DU 32–33 15 de julio 2–3, GW 31 Green Day 2."),
        new TipoCapitulo("arco_cierra", "Cierra un enfrentamiento (parte 2)",
            "cold open con el recap del cliffhanger → la crisis sigue → giro (50–60 %) → derrota (65–90 %) → remate → anuncio de " +
            "lo siguiente (98 %).",
            "el material retoma algo que quedó pendiente en el capítulo anterior y lo resuelve.",
            "la sesión que termina lo que quedó a medias.",
            "SC 11, SC 39 Pet Shop 2, DU 9 Yukako 2, DU 29 Highway Star 2, GW 16 Grateful Dead 2."),
        new TipoCapitulo("juego", "Juego o apuesta",
            "el reto y lo que está en juego (0–20 %) → las reglas explicadas (con texto en pantalla) → la primera ronda la pierde " +
            "el protagonista → trampas del rival → el protagonista apuesta todo o hace un farol → se revela su trampa (80–90 %) → " +
            "el perdedor humillado. Ritmo lento en las apuestas: silencios, caras, tensión.",
            "reglas, apuestas, marcador, rondas, «el que pierda…», minijuegos, PvP con reglas, tratos.",
            "minijuegos, apuestas entre amigos, PvP con reglas, parkour, carrera de recolección, tradeos.",
            "SC 34–35 D'Arby (póker), SC 40–42 D'Arby el jugador, SC 27 Oingo Boingo, DU 26 piedra, papel o tijera, DU 27 dados, SO 9 Marilyn Manson."),
        new TipoCapitulo("comedia", "Comedia o vida diaria",
            "situación cotidiana (0–15 %) → algo raro pero pequeño → malentendido que crece → las manías de un personaje en el " +
            "centro → clímax absurdo → se aclara todo y final feliz o un chiste. Sin peligro real; música de comedia y calma.",
            "mucha risa, bromas, nada en juego, un problema pequeño (una mascota, una casa, un bug, alguien perdido).",
            "construir una casa, una mascota, un aldeano, un bug, el que se pierde o se cae.",
            "DU 10 el restaurante de Tonio, DU 13 el bebé invisible, DU 20 Cinderella, DU 27 Mikitaka, SC 31 Mariah 2."),
        new TipoCapitulo("foco", "El capítulo de un personaje",
            "cold open con su pasado o un rasgo suyo → se queda solo con el problema → recuerda por qué es así (flashback, 30–45 %) " +
            "→ lo resuelve a su manera → los demás lo reconocen. Su tema musical en el momento clave.",
            "un jugador lleva casi todo el material o tiene su gran momento.",
            "el capítulo de un jugador: su reto, su base, su venganza.",
            "GW 6 Abbacchio, GW 8 Mista, GW 11 Narancia, GW 25 Trish, SO 6 Ermes, DU 6 Koichi, DU 17 Rohan, SC 38–39 Iggy."),
        new TipoCapitulo("villano", "Del lado del rival",
            "abre en la vida del rival (su rutina, sus manías, 0–20 %) → cómo ve a los héroes → los héroes casi lo descubren → " +
            "cierre inquietante: se escapa o gana esta vez.",
            "material de otro jugador o del equipo contrario, una traición, alguien que trama algo.",
            "el equipo rival, el amigo que traiciona, «mientras tanto» del otro lado.",
            "DU 21 Kira solo quiere vivir tranquilo, DU 30 Cats Love Kira, GW 10 el equipo de sicarios, GW 26–27 Doppio, SC 36 Hol Horse."),
        new TipoCapitulo("misterio", "Misterio o investigación",
            "algo no cuadra (0–10 %) → investigan con pistas (10–45 %) → sospecha falsa → la revelación (50–60 %: «son todos el " +
            "enemigo») → pelean con lo que ya entienden → la explicación final. Ritmo lento, silencios, música de misterio.",
            "buscar algo, preguntas sin respuesta, «¿quién fue?», ruidos, una estructura o base desconocida.",
            "buscar una estructura, una base abandonada, «¿quién robó el cofre?».",
            "SC 7 el barco vacío, SC 14 Justice (niebla y cadáver), DU 16 la caza de ratas, DU 17 el callejón, SO 7 «hay una de más»."),
        new TipoCapitulo("persecucion", "Persecución o carrera",
            "la salida o alguien huye → choque u obstáculo → el perseguidor gana terreno → escondite breve (respiro) → truco con " +
            "el terreno → final al límite. Rápido, con pausas cortas.",
            "correr, huir, carreras, cronómetro, «¡corre!», viajes con prisa.",
            "carrera de etapa, escapar de un mob o de la noche, ir a por alguien.",
            "SC 13 Wheel of Fortune, BT 19 carrera al precipicio, DU 28–29 Highway Star, GW 19 White Album, SBR 1–3."),
        new TipoCapitulo("entrenamiento", "Entrenamiento o prueba",
            "el mentor o el reto plantea algo imposible (0–15 %) → intentos fallidos con humor → entienden el truco (~55 %) → lo " +
            "superan al límite → reconocimiento y algo nuevo (poder, equipo, permiso).",
            "aprender una mecánica, practicar, farmear, preparar equipo, «a ver si puedes».",
            "aprender una mecánica, farmear, prepararse para un jefe.",
            "BT 4 Zeppeli, BT 16 Lisa Lisa (el pilar), GW 3 el examen de Polpo, SC 41 Jotaro aprende a jugar."),
        new TipoCapitulo("mision", "Misión u operación",
            "la orden o el objetivo con mapa o itinerario en pantalla (0–15 %) → el plan → el plan se tuerce (~40 %) → improvisan → " +
            "lo logran a medias o con un costo → la siguiente orden.",
            "un objetivo claro (ir a, conseguir, robar, escoltar), un plan hablado.",
            "ir al Nether, conseguir un objeto, matar al dragón, saquear una estructura.",
            "GW 5 la fortuna de Polpo, GW 9 la primera orden, GW 14 el tren a Florencia, SO 10–11 Operación Savage Garden, SO 24 la fuga."),
        new TipoCapitulo("duelo", "Duelo uno a uno",
            "el reto y las reglas de honor (0–15 %) → respeto mutuo → intercambio de golpes, cada uno con su truco → el rival casi " +
            "gana → el último truco → respeto al vencido.",
            "dos jugadores frente a frente, PvP, una competencia directa.",
            "PvP 1 contra 1 entre amigos, la final de un torneo.",
            "BT 21–23 la carrera de cuadrigas con Wamuu, GW 2 Giorno contra Bucciarati, DU 15 Josuke contra Rohan, SC 46–48 DIO."),
        new TipoCapitulo("pasado", "Flashback u origen",
            "abre en el pasado (otra música, otro color) → alterna pasado y presente → el pasado explica una decisión de ahora → " +
            "vuelve al presente con esa decisión.",
            "se habla mucho de algo que pasó antes; hay clips viejos o recuerdos.",
            "recuerdos de capítulos o temporadas anteriores, la historia de una base o de una pelea vieja.",
            "GW 26 Doppio, GW 20 el pasado de Bucciarati, BT 20 Caesar, BT 24 Elizabeth, SO 31 Heavy Weather 2."),
        new TipoCapitulo("despedida", "Muerte o despedida",
            "inicio cálido con quien va a caer (presagio) → el peligro → el sacrificio o la caída (hasta el 90 %) → silencio → " +
            "reacción del grupo → su última frase o la pista que deja. Tema triste y largo.",
            "una muerte en hardcore, alguien que se va de la serie, perder la base o una mascota.",
            "muerte en hardcore, un amigo que deja la serie, perder algo querido.",
            "BT 20 Caesar, SC 10 Avdol, SC 43 Iggy, SC 46 Kakyoin, DU 22 Shigechi, GW 28 Abbacchio, SO 22 F.F."),
        new TipoCapitulo("revelacion", "Revelación o traición",
            "arranque normal con pistas sembradas → el giro (40–60 % o al final) → repaso rápido de las pistas → el grupo decide " +
            "(«¿quién viene conmigo?») → cierre con el nuevo estado de cosas.",
            "un secreto, una traición, algo que cambia lo que se sabía.",
            "el aliado que traiciona, el secreto de un jugador, la regla oculta del reto.",
            "GW 20–21 el jefe traiciona, BT 23 Lisa Lisa es su madre, SC 22 Avdol vive, DU 35 Bites the Dust."),
        new TipoCapitulo("separados", "El grupo separado",
            "el grupo se separa (0–15 %) → se intercalan 2–3 historias cortando en los momentos de tensión (cada 1–3 min) con " +
            "carteles de lugar u hora → las historias se juntan al final (o no: cliffhanger).",
            "varios jugadores haciendo cosas distintas a la vez, en lugares distintos.",
            "cada amigo con su propia aventura (sus pistas de grabación) el mismo día.",
            "DU 31–34 15 de julio (jueves), SC 32–33 Alessi, GW 12–13 Pompeya, SO 25–26 Bohemian Rhapsody."),
        new TipoCapitulo("encierro", "Encierro o regla rara",
            "quedan atrapados con una regla rara (0–20 %) → la regla en pantalla → fallan por la regla → la entienden (50–60 %) → " +
            "la usan contra el problema → salen.",
            "atrapados, sin salida, sin comida, un bug, un reto con restricción.",
            "atrapados en una cueva o en el End, un reto con una restricción.",
            "SC 8 Devil (la habitación), SC 19–20 Death 13 (el sueño), SC 23–24 el submarino, GW 12–13 el espejo, DU 35–36 el bucle."),
        new TipoCapitulo("reclutamiento", "Alguien se une",
            "el nuevo aparece como rival o problema → pelea o prueba → se descubre por qué era así → se une → presentación (tarjeta " +
            "con su nombre) y chiste de bienvenida.",
            "un jugador nuevo, un aliado, una mascota que se queda.",
            "un amigo nuevo que entra a la serie.",
            "SC 5 Polnareff, SC 25 Iggy, DU 3–5 Okuyasu, GW 4–5 Giorno entra a la banda, SO 8 F.F., SO 15 Anasui."),
        new TipoCapitulo("poder", "Despertar o mejora",
            "el problema supera al grupo (0–40 %) → desesperación → tocan fondo → despierta el poder o llega la mejora (60–75 %) con " +
            "su tarjeta de stats y su tema → lo usa para ganar.",
            "conseguir algo clave (netherite, un encantamiento, el elytra) después de pasarla mal.",
            "la primera armadura buena, un encantamiento, el elytra, un beacon.",
            "DU 9 Echoes ACT2, DU 23 ACT3, GW 25 Spice Girl, GW 37 Requiem, SC 48 Jotaro detiene el tiempo."),
        new TipoCapitulo("transicion", "Puente o viaje",
            "consecuencias de lo anterior → recap del viaje con mapa → objetivo nuevo → viaje y llegada → primer vistazo del " +
            "arco nuevo al final.",
            "cambiar de lugar o de etapa, preparar el viaje, mudarse, una dimensión nueva.",
            "cambio de etapa, mudanza, el primer viaje al Nether o al End.",
            "SC 3 la partida, SC 24 por fin Egipto, SC 39 la mansión de DIO, GW 29 destino Roma, BT 10 la nueva generación."),
        new TipoCapitulo("epilogo", "Epílogo",
            "capítulo tranquilo después de la gran batalla: consecuencias → despedidas → la broma de siempre → una historia corta " +
            "aparte → «la vida sigue».",
            "material tranquilo después de algo grande, recuento, charla.",
            "después del jefe, el recuento de la temporada.",
            "SC 48 (segunda mitad), DU 39, GW 39 Sleeping Slaves, BT 26, SO 38."),
    };

    public static TipoCapitulo Buscar(string x)
    {
        if (String.IsNullOrEmpty(x)) return null;
        foreach (TipoCapitulo t in Todos)
            if (String.Equals(t.Clave, x, StringComparison.OrdinalIgnoreCase) || String.Equals(t.Nombre, x, StringComparison.OrdinalIgnoreCase)) return t;
        return null;
    }

    // Clave del tipo ("" si no es ninguno).
    public static string Normalizar(string x)
    {
        TipoCapitulo t = Buscar((x ?? "").Trim());
        return t != null ? t.Clave : "";
    }

    public static string Nombre(string clave)
    {
        TipoCapitulo t = Buscar(clave);
        return t != null ? t.Nombre : "";
    }

    // Para el combo: el primero es "que lo detecte".
    public static string[] Opciones()
    {
        List<string> l = new List<string>();
        l.Add(Detectar);
        foreach (TipoCapitulo t in Todos) l.Add(t.Nombre);
        return l.ToArray();
    }

    // Como se mezclan a lo largo de una temporada (SC, DU, GW).
    public const string Mezcla =
        "Cómo se mezclan en una temporada: los primeros capítulos presentan y reclutan (GW 1–11: casi todos son el capítulo de " +
        "un personaje que se une); luego la columna es el rival de la semana o la misión, con enfrentamientos de dos partes cuando " +
        "el material no se resuelve; un respiro de comedia cada 4–6 capítulos (DU los intercala entre los arcos serios); el villano " +
        "tiene su propio capítulo hacia los 2/3 (DU 21, GW 26); los juegos se agrupan cerca del final (SC 27–41); un puente a mitad " +
        "de temporada (SC 24, por fin Egipto); las muertes y revelaciones se concentran en el último tercio. No más de dos " +
        "seguidos del mismo tipo, salvo las partes de un mismo enfrentamiento.\n";

    // Todos, para que Gemini elija.
    public static string Catalogo()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("TIPOS DE CAPÍTULO (cómo varía el anime de JoJo; usa la clave):\n");
        foreach (TipoCapitulo t in Todos)
            sb.Append("- " + t.Clave + " (" + t.Nombre + "): " + t.Estructura + " Se nota en: " + t.Senales + " En la serie: " + t.EnSerie + "\n");
        sb.Append(Mezcla);
        return sb.ToString();
    }

    // El elegido, completo, para la escaleta final.
    public static string Instrucciones(string clave)
    {
        TipoCapitulo t = Buscar(clave);
        if (t == null) return "";
        return "TIPO DE CAPÍTULO: " + t.Nombre + ". Cómo lo arma JoJo: " + t.Estructura + " En la serie: " + t.EnSerie +
               " (ejemplos: " + t.Ejemplos + "). Úsalo como guía; las notas del editor mandan.\n";
    }
}
