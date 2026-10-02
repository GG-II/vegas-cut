# Cómo está armado un episodio de Stardust Crusaders (y qué usar en SCR)

Revisé a mano los 48 episodios de SC con las escenas de jojowiki, que traen su minuto. El
porcentaje es la posición dentro del capítulo: 0 % = justo después del opening y 100 % = el
ending; los valores negativos son el cold open. SC es la parte más parecida a SCR: un viaje por
etapas con un enemigo por parada.

## Los momentos de un episodio (E4–E48)

| Momento | Dónde cae (mediana) | Rango típico | Cómo se ve |
|---|---|---|---|
| Llegada / viaje | 0 % | −30 a 10 % | «Arrival in India», «Description of Luxor», un cartel del lugar, choque cultural y humor |
| Aparece el enemigo | **13 %** | 0–40 % | Algo raro («A mysterious driver», «A strange baby») antes de verlo |
| Se revela su stand (carta del tarot) | **35–40 %** | 15–50 % | «The tower card: Tower of Gray», «Presentation of Devo» |
| Crisis: el enemigo va ganando | **45 %** | 30–60 % | Atrapados, heridos, «Anne cornered», «The submarine hits the sea floor» |
| Giro: se entiende el poder o hay un truco | **58 %** | 45–70 % | «Avdol tricks Polnareff», «Joseph uses a TV set», «Polnareff's trick» |
| Derrota del enemigo | **77 %** | 50–90 % | ORAORAORA, «The Sun card, just another dumbass» |
| Remate cómico / descanso | 85–95 % | | «Joseph makes fun of Polnareff», «Iggy farts on Polnareff» |
| Gancho final | **95–99 %** | | Enemigo nuevo, la siguiente parada o un cliffhanger |

**Episodios dobles.** Desde el E14, la mitad de los enemigos ocupan dos episodios:
- **Parte 1:** llegada y humor (0–20 %), el enemigo (15–40 %), todo empeora y **termina en el
  peor momento** (98 %: «Pet Shop has killed Iggy?», «Kakyoin is behind!»).
- **Parte 2:** **recap del cliffhanger** en el cold open, la crisis sigue, el giro (50–60 %), la
  derrota (65–90 %), el remate y el anuncio del siguiente enemigo (98 %: «The "great" Alessi
  appears», «Khan is an enemy!»).

## Cómo abren (cold open, antes del opening)

Episodios de SC que tienen cold open, por tipo:

| Tipo | Episodios | Ejemplo |
|---|---|---|
| Recap del cliffhanger anterior | 13 | «Recap: Horus at full power» → Iggy escapa bajo el agua |
| Llegada / humor de viaje | 8 | «Polnareff argues with a policeman», «Trying coconut juice» |
| Escena del villano | 6 | «Enya discusses with DIO», «Hol Horse in DIO's Mansion» |
| En medio de la pelea | 4 | «Polnareff demonstrates Silver Chariot's power» |

En SBR el cold open es más largo (2–3 min). E02 abre con las consecuencias de la etapa
anterior. E03 abre con una escena tranquila que termina en un misterio («se acerca un hombre»).

## Cómo cierran (antes del ending)

| Tipo | Aprox. | Ejemplo |
|---|---|---|
| Cliffhanger en plena crisis | 1 de cada 4 | «Kakyoin is in a pickle», «Despair! Option #3» |
| Anuncio de un enemigo nuevo | 1 de cada 4 | «Polnareff reveals himself», «A beautiful lady appears» |
| Remate cómico o calma | 1 de cada 4 | «The Oingo Boingo bros. are defeated», «Boingo became more shy than ever» |
| Seguir el viaje (y el villano mirando) | 1 de cada 4 | «Arrival in Singapore», «DIO watches the Joestars» |

Además, casi siempre hay un **avance del próximo episodio** con música de pelea («Fists of
Platinum», en 42 episodios).

## Plantilla para un episodio de SCR (15–18 min)

| % | Bloque | En SCR |
|---|---|---|
| cold open | 30–60 s | Recap del cliffhanger anterior **o** llegada a la etapa con humor **o** el rival tramando algo |
| OP | 15–25 s | Opening propio |
| 0 % | Título y lugar | «Etapa N · nombre» + cartel del lugar o del tiempo («Día 3, 6 horas más tarde») |
| 0–15 % | Viaje | Llegada, objetivo de la etapa, humor del grupo. Música de **viaje / calma** |
| ~13 % | Aparece el problema | Rival, mob fuerte o trampa: primero algo raro, sin explicarlo. Música de **misterio / tensión** |
| ~35 % | Se revela | Qué es (stand, jefe, regla de la etapa) con una tarjeta tipo carta del tarot / stats. Música de **villano** |
| ~45 % | Crisis | Les va mal: muertes, pierden la carrera, se separan. **Re-gancho** de mitad de video |
| ~58 % | Giro | La idea, el truco o la ayuda. Música de **pelea** |
| ~77 % | Resolución | Ganan o pierden la etapa + **ranking**. Música **épica / victoria** (tu tema principal) |
| 85–95 % | Remate | Chiste del grupo o calma. Música de **comedia / calma** |
| 95–99 % | Gancho | Enemigo nuevo, siguiente etapa o cliffhanger + «CONTINUARÁ» |
| ED | 10–20 s | + avance o escena post-créditos del próximo capítulo |

Si una etapa da para mucho, conviene hacerla en **dos partes**, como SC: la primera termina en la
crisis y la segunda abre con el recap.

La música de cada bloque, con tus archivos, está en `docs/musica-sc-gw.md`.

## Según el papel del capítulo (todas las partes)

Revisé los primeros capítulos, los finales, los penúltimos y algunos capítulos clave de Phantom
Blood/Battle Tendency, SC, DU, GW, SO y SBR (escenas de jojowiki y medidas de los subtítulos). Cada
tipo se arma distinto; por eso cada capítulo de la serie tiene un **papel** (Series → combo junto a
«Quitar»), que cambia lo que se le pide a Gemini y las reglas de ritmo.

| Papel | Cómo lo hace JoJo | Ejemplos |
|---|---|---|
| **Primer capítulo** | **Ninguno abre con el opening.** Empieza mostrando el mundo con calma, presenta al protagonista con una escena que muestra cómo es, una escena de «mentor» explica las reglas, el poder se revela a la mitad y cierra con la amenaza. Mucho diálogo (18 líneas/min), más narrador (hasta 15 %) y casi sin silencios largos. SBR estrena con un episodio doble. | SC 1 (barco con el ataúd → Jotaro en la celda → Star Platinum al 54 % → DIO al final), DU 1 («Morioh-cho RADIO» → Josuke y su pelo → Jotaro explica los stands → «algo acecha en el pueblo»), GW 1 (la ciudad → Giorno roba dinero → Gold Experience → Bucciarati) |
| **Inicio de arco** | Arranque tranquilo con el grupo, giro o traición hacia el 40 %, eyecatch, flashback que explica por qué importa, cierre con una decisión del grupo o el nuevo rival. | GW 20–21 (la traición del jefe, el pasado de Bucciarati, el equipo decide) |
| **Capítulo clave** (muerte, revelación, llegada) | **Más lento:** 12–14 líneas/min, 4–5 silencios de más de 20 s, temas largos (mediana de 2 min en SC 46), voz interna hasta el 19 %. La muerte al 90 % como cierre, o a la mitad con sus consecuencias. | SC 10 (Avdol), SC 43 (Iggy), SC 46 (Kakyoin), SC 24 (por fin Egipto, con recap del viaje) |
| **Capítulo de respiro** | Sin pelea al inicio, humor y manías de los personajes, un problema pequeño que crece, final feliz. | DU 20 (el salón de Aya Tsuji), DU 27 (Mikitaka y los dados) |
| **Penúltimo** | Recap del cliffhanger, tensión máxima todo el capítulo, puede caer alguien importante, termina en el peor momento y sin remate. | SC 47 («DIO está totalmente sincronizado»), DU 38, SO 37 (muere Jotaro) |
| **Final** | El clímax se resuelve entre el 40 y el 75 % y queda un **epílogo largo** (25–50 %): despedidas, la broma de siempre, «la vida sigue», el ending sobre el epílogo; a veces sin opening. Termina con un nuevo estado de cosas. | SC 48 (DIO cae al 60 %; bromas de Joseph y vuelta a casa), DU 39 (Kira cae al 40 %; despedida de Reimi), GW 39 (Giorno nuevo jefe), PB/BT 26 (partida a Tokio) |

## Títulos

Revisé los títulos de todas las partes. Se repiten estos patrones; el productor los usa para
proponer el título de cada capítulo.

| Patrón | Ejemplos de JoJo | En SCR |
|---|---|---|
| Nombre del rival o del peligro | «Tower of Gray», «Yellow Temperance» | «El Creeper de la cueva» |
| Apodo + nombre | «The Lovers», «The Emperor and the Hanged Man» | «El cazador, Steel» |
| «¡X contra Y!» / «X conoce a Y» | «Jotaro Kujo! Meets Josuke Higashikata» | «¡Steel contra el Wither!» |
| Invitación o rumbo | «Let's Go to the Manga Artist's House», «Let's Go Hunting!» | «¡Vamos al Nether!» |
| Algo raro de un personaje | «Yukako Yamagishi Falls in Love» | «Steel no quiere bañarse» |
| Guiño a una canción o grupo | stands con nombre de bandas | — |
| Fecha o tiempo | (en los carteles más que en el título) | «Día 7» |
| «, parte N» (hasta 4) | «Dark Blue Moon, Part 1» | en los capítulos de dos partes |
| Primer capítulo | el nombre de la serie o del protagonista («Dio the Invader», «Gold Experience») | el nombre de la serie |
| Final | una despedida («Goodbye, Morioh», «Farewell, My Friends») | «La vida sigue», despedida |

En SBR: «The Sheriff's Request to Mountain Tim», un título que dice quién pide qué a quién.

## La última frase antes del opening y del ending

Tipos de frase que cierran el cold open:
- la amenaza del villano;
- una pregunta o una frase cortada por la sorpresa;
- la llegada a un lugar, narrada;
- una decisión del grupo;
- el remate de un chiste.

Antes del ending:
- declaración del villano o una amenaza nueva, con «CONTINUARÁ»;
- una frase emotiva en calma;
- un chiste;
- «seguimos el viaje»;
- un golpe en plena pelea;
- una noticia que lo cambia todo;
- el narrador enmarcando.

Siempre es una frase que se entiende sola.

## Curva de ritmo dentro del capítulo

Medida con los subtítulos, por décimos del capítulo:

| Décimo | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 9 | 10 |
|---|---|---|---|---|---|---|---|---|---|---|
| SC: líneas/min | 14.4 | 14.5 | 14.4 | 14.0 | 13.1 | 13.7 | 14.1 | 13.2 | 13.7 | 13.3 |
| SC: % sin música | 24 | 24 | 15 | 18 | 14 | 15 | 16 | 20 | 25 | 17 |
| SC: % voz interna | 4.2 | 4.9 | 3.8 | 3.0 | 1.8 | 3.4 | 2.7 | 2.4 | 2.4 | 2.4 |
| SC: silencios ≥ 15 s | 7 | 15 | 13 | 19 | 17 | 16 | 22 | 16 | 14 | 12 |
| SBR: líneas/min | 18.4 | 17.8 | 14.9 | 15.5 | 16.9 | 20.0 | 18.9 | 18.3 | 15.5 | 13.8 |

Qué se ve en la tabla:
- **Al inicio** hay más diálogo y más voz interna (o narrador) para situar.
- **Entre el 30 y el 70 %** hay más pausas largas: es la tensión.
- **Entre el 60 y el 80 %** la pelea acelera (SBR llega a 20 líneas/min).
- **El último décimo** es más calmado. DU baja de ~15 a 12.8 líneas/min.

SBR casi siempre tiene música y no usa voz interna. SC deja más escenas en silencio.

## Qué más conviene analizar

Lo que más mejoraría el productor, de mayor a menor:
1. **Tus propios capítulos publicados con su retención** (YouTube Studio → curva de retención). Es lo
   único que dice qué funciona con *tu* público: dónde se va la gente, si el cold open retiene o
   cuánto aguanta un tramo sin narrador.
2. **Los episodios nuevos de SBR** a medida que salen. Es el estilo más cercano: carrera por etapas,
   cold opens largos y muchos personajes.
3. **Los subtítulos completos de Golden Wind.** Un equipo que viaja junto y tiene capítulos de
   «inicio de arco», así que sirve mucho para un grupo de amigos.
4. **Series de Minecraft exitosas en YouTube** (transcripciones de los auto-subs). Sirven para medir
   cada cuánto hay un chiste, un giro o una tarjeta en el formato que ve tu público.
