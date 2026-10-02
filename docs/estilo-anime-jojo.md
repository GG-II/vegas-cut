# Estructura del anime de JoJo (guía para SCR como serie de TV)

Datos sacados con `herramientas/analizar_anime.py` de `ejemplos/subs`. Los subtítulos (.ass/.srt)
dan los diálogos y la voz interna, el título, el eyecatch y el avance. Las listas de música de
jojowiki dan el opening, el ending, el avance y cada tema con su escena. Datos por episodio:
`docs/estructura-anime-jojo.json`.

Episodios analizados: Stardust Crusaders (48), Diamond is Unbreakable (14), Golden Wind (1) y
Steel Ball Run (3).

## Medianas por temporada

| | SC | DU | SBR |
|---|---|---|---|
| Duración | 23:44 | 23:42 | 23:35 (E01 doble: 47 min) |
| Cold open (antes del OP) | 1:32 | 1:38 | **2:16–3:02** |
| Opening | 1:29 | 1:14 | 1:29 (desde E02; E01 no tiene) |
| Eyecatch | 52 % del episodio | 54 % | **no tiene** |
| Ending | 1:28 | 1:08 | 1:28 |
| Después del ending | avance de 14 s con música | título del próximo (5 s) | **escena post-créditos** (E02) o nada |
| Diálogo | 14 líneas/min · 78 palabras/min | 15 · 86 | 16 · 90 |
| Tiempo con alguien hablando | 63 % | 71 % | 69 % |
| Voz interna / narrador | 7.8 % | 7.0 % | 0.3–1.4 % |
| Tramos sin diálogo ≥ 20 s | 3 | 1 | 2 (máx. ~1:30, acción) |
| Música | 81 % del episodio, 15 temas | 88 %, 18 | **90 %, 21 temas** |
| Duración típica de un tema | 64 s | 52 s | **35 s** |

## Cómo está armado SBR

1. **Cold open largo con gancho** (2–3 min): E02 abre con el final de la etapa anterior (Gyro
   cruza la meta, cómo quedó la clasificación). E03 abre con una escena tranquila (campamento,
   el pasado de Gyro) que termina en **«se acerca un hombre»**: el misterio cae justo antes del
   opening.
2. **Opening** («SPIN») y enseguida el **título del episodio** («El pedido del sheriff: Mountain
   Tim»).
3. **Carteles de lugar y tiempo**: «Carrera Steel Ball Run, de San Diego a Nueva York», «Dos horas
   antes», «6 horas más tarde», «Pozo de agua». Ordenan la carrera por etapas.
4. **Resultados y clasificación** de la etapa. Gyro es degradado y hay un nuevo ranking: la
   competencia se muestra en pantalla.
5. **Sin eyecatch**. En su lugar, **escalada continua**: un tema distinto cada ~35 s y el tema
   principal («Dance with STEEL BALL RUN») en cada momento clave: el duelo ganado, la largada,
   cuando alguien se adelanta.
6. **Final**: cliffhanger con amenaza («cuando los alcancemos…») y **«CONTINUARÁ»**, ending
   («Dead or Alive») y, a veces, una **escena post-créditos** que abre otro hilo (Mountain Tim
   sigue al asesino).
7. **Narrador mínimo**: Johnny enmarca la historia en primera persona al inicio y al final del E01
   («Todo comenzó cuando yo, Johnny Joestar, conocí al misterioso Gyro Zeppeli»). El resto lo
   cuentan los personajes.

En SC y DU, lo que cambia es:
- hay **eyecatch a la mitad** (52–54 %), con la tarjeta de stats del stand;
- hay **avance del próximo episodio** con música;
- hay más **voz interna** (7–15 %);
- en SC, una cuarta parte de los episodios abre con **recap**.

## Plantilla propuesta para SCR (YouTube)

Un episodio de anime dura 24 min y se ve de corrido. En YouTube los primeros 30 s deciden, y lo
que te funcionó fue 10–12 min. Plantilla de 15–18 min:

| Bloque | Duración | Qué es |
|---|---|---|
| Cold open | 0:30–1:00 | Lo más fuerte del capítulo o el misterio que lo abre. Termina en un gancho («se acerca un hombre»). |
| Opening propio | 15–25 s | Montaje de tu gameplay. El completo, solo en el capítulo 1 y en los especiales. |
| Título | 3 s | «Etapa N · título del episodio». |
| Acto A | ~45 % | Carteles de lugar y tiempo («6 horas más tarde»). |
| Re-gancho a la mitad | 5–8 s | Tarjeta de stats del stand de alguien (eyecatch de SC/DU) o el ranking de la etapa. Cubre la zona media. |
| Acto B | ~40 % | Clímax con el tema principal, resultado y clasificación de la etapa. |
| Cliffhanger | | «CONTINUARÁ». |
| Ending corto | 10–20 s | |
| Post-créditos o avance | 10–15 s | Una escena del próximo capítulo (ya grabado) o un hilo nuevo. |

Ritmo y sonido:
- **Narración** como la de Johnny: tú en primera persona, en pasado, enmarcando el inicio y el
  final, más frases cortas que unen. Menos que en JoJoMania (no el 30 %): en una serie de TV
  manda el diálogo.
- **Música** casi todo el tiempo (85–90 %), cambiando cada 35–60 s según la escena, con **un tema
  principal propio** que vuelve en cada momento clave de la serie.
- **Copyright**: el opening y el ending originales («SPIN», «Dead or Alive») y los OST oficiales
  pueden activar Content ID; el opening y el ending conviene hacerlos propios.

## Música: índice de la biblioteca

Las listas de jojowiki tienen **394 temas** con **3583 usos**, cada uno con la escena donde suena
(«Jotaro must stop time», «Gyro wins the duel», «DIO watches the Joestars»…). Con eso se puede
indexar `K:\Duwang\000_Recursos varios\Música General`:

1. Recorrer la carpeta y emparejar cada archivo con su tema de jojowiki por título.
2. Con las escenas de cada tema, Gemini le pone **estados de ánimo** (tensión, pelea, victoria,
   misterio, villano, comedia, calma, tristeza, épico) y anota si es opening, ending, eyecatch o
   avance.
3. Guardar `musica-indice.json` en esa carpeta, con archivo, tema, OST, temporada, duración,
   estados y escenas de ejemplo.
4. «Estructura y narración» pide un estado por sección y elige el tema, sin repetir los de los
   capítulos anteriores.

SBR casi no tiene títulos en la lista (su OST no ha salido): de SBR sirven los tiempos, no los
nombres.
