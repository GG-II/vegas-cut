# vegas-cut

Herramientas para agilizar la edición en VEGAS Pro 20, inspiradas en AutoCut.
El plan completo está en [`docs/plan.md`](docs/plan.md).

| Script | Para qué |
|---|---|
| `QuitarSilencios.cs` | Quita las pausas de una o varias pistas de voz. |
| `ConfigurarVegasCut.cs` | Clave de Gemini y ruta de Faster-Whisper-XXL (una sola vez). |
| `Transcribir.cs` | Texto con tiempos por palabra y nivel de sonido de cada pista, en tu PC. |
| `MomentosIA.cs` | Gemini sugiere resumen, momentos, un corte a la duración que pidas, textos y Shorts. |
| `ReubicarMarcadores.cs` | Vuelve a poner los marcadores de MomentosIA sobre su clip si moviste clips. |
| `TextosDesdeMarcadores.cs` | Convierte los marcadores `TEXTO:` en Títulos y texto con el estilo de una plantilla. |
| `MusicaAutomatica.cs` | Baja la música cuando alguien habla y la sube en las pausas (envolvente de volumen). |
| `DesenlazarClips.cs` | Arregla clips que quedaron unidos todos en un grupo tras cortar con versiones anteriores. |
| `CensurarPalabrotas.cs` | Tapa las palabrotas con un pitido o el efecto que elijas y silencia la voz justo ahí. |
| `ExportarProyecto.cs` | Exporta proyectos a JSON para estudiar patrones de edición. |

**Flujo sugerido para un gameplay:** *Quitar silencios* (opcional) → *Transcribir* →
*Momentos con IA* → revisar → *Aplicar corte* → tus ajustes a mano → *Textos desde marcadores*
→ música → *Música que baja sola* → *Censurar palabrotas*. La transcripción sigue los cortes que hacen estas
herramientas, incluso si los deshaces con Ctrl+Z. Si después editas a mano no hace falta volver a
transcribir: los textos usan los marcadores anclados y la música mide las voces en el momento.

## Estructura

- `scripts/`: los scripts listos para Vegas (*generados* desde `src/`, no se editan a mano)
- `src/`: código fuente; `src/comun` se comparte entre herramientas
- `herramientas/compilar.py`: arma `scripts/*.cs` desde `src/` (un archivo por herramienta)
- `ejemplos/`: proyectos `.veg` reales para estudiar patrones de edición
  - `avatar/`: video ensayos con avatar
  - `minecraft/`: gameplays y shorts de Minecraft
- `docs/`: plan, patrones de edición y notas
- `pruebas/`: pruebas sin Vegas (`sh pruebas/ejecutar.sh`, requiere mono)

## Instalación

Los scripts no se instalan: son archivos `.cs` que Vegas compila al ejecutarlos.

- **Sin instalar:** *Herramientas → Secuencias de comandos → Ejecutar secuencia de comandos…*
  y eliges el `.cs` en la carpeta `scripts/` del repositorio.
- **En el menú (recomendado):** copia los `.cs` a `Documentos\Vegas Script Menu\` (crea la
  carpeta si no existe) y reinicia Vegas, o usa *Herramientas → Secuencias de comandos →
  Volver a examinar la carpeta del menú de secuencias de comandos*. Aparecen en
  *Herramientas → Secuencias de comandos*.
- **Atajo de teclado o botón:** *Opciones → Personalizar teclado*, busca el nombre del script y
  asígnale una tecla; o *Opciones → Personalizar barra de herramientas* para poner un botón.

Al actualizar el repositorio (`git pull`), vuelve a copiar los `.cs` si usas la carpeta del menú.

## Scripts

### `ConfigurarVegasCut.cs`

![Configurar vegas-cut](docs/img/configurar.png)

Se ejecuta una vez (y cuando quieras cambiar algo). Guarda en `%APPDATA%\vegas-cut\config.json`:

- **Clave de Gemini**, cifrada con tu usuario de Windows (DPAPI). Consíguela gratis en
  [aistudio.google.com/apikey](https://aistudio.google.com/apikey). *Probar y listar modelos*
  comprueba la clave y trae los modelos disponibles para tu cuenta.
- **Faster-Whisper-XXL**: ruta del `.exe`, modelo, tarjeta o procesador y precisión.
  Guía: [`docs/instalar-whisper.md`](docs/instalar-whisper.md).

### `Transcribir.cs`

![Transcribir](docs/img/transcribir.png)

1. Guarda el proyecto.
2. Marca las **voces** (se transcriben; cada pista es una persona) y el **ambiente** (juego,
   música: solo se mide su sonido para detectar explosiones y momentos intensos).
3. **Transcribir**: Vegas renderiza cada pista y Faster-Whisper-XXL la transcribe en segundo
   plano, con progreso y tiempo restante. Se puede cancelar.

Resultado: `<proyecto>.vegascut.json` junto al `.veg`, con frases, palabras con su tiempo,
quién habla y el nivel de sonido por segundo de cada pista. El audio no sale de tu PC.

### `MomentosIA.cs`

![Momentos con IA](docs/img/momentos-ia.png)

Necesita la transcripción y la clave de Gemini.

1. Elige el **tipo de video**, la **duración mínima y máxima**, los **nombres** de las personas
   y, si quieres, **indicaciones de este episodio** ("es el episodio 3, que se entienda la historia").
   - **Reglas del canal…**: reglas que aplican siempre, en todos los videos, para no escribirlas cada
     vez. Vienen unas de base (empezar sin perder tiempo, nada de conversaciones personales o de vida
     amorosa, nada de problemas técnicos, mantener el hilo entre escenas, no cortar chistes a la
     mitad, terminar con clímax o suspenso). Se editan y se guardan en `config.json`.
   - **Episodios anteriores…** (opcional): eliges respuestas de MomentosIA de otros videos de la
     serie y su resumen se envía como contexto para entender la historia.
   - **Modelo**: se elige aquí mismo. *Lite* es barato pero sigue peor las reglas y la duración.
   - **Transiciones: Cortar o Acelerar.** Con *Acelerar*, Gemini puede marcar tramos con poca
     conversación (viajar, minar, construir) para verse ×2–×4 en vez de desaparecer, así la historia
     no da saltos. **Audio de lo acelerado:** mudo (recomendado) o acelerado.
2. **Pedir a Gemini**: se envía solo texto (transcripción con tiempos e intensidad del sonido
   cada 5 s). Tarda uno o dos minutos.
   - **Videos largos (más de 35 min) se analizan por partes** de unos 20 min, cortando en la
     pausa más larga: cada parte elige candidatos (con importancia 1–10) y resume lo que pasa,
     sabiendo lo que pasó antes; una pasada final arma el corte completo cuidando la historia.
     Si una respuesta llega incompleta se reintenta, y si no cupo se pasa solo al modo por partes.
   - **Revisión:** una segunda pasada solo comprueba que cada tramo cumpla las reglas y las
     indicaciones, y quita o recorta los que no (aparecen desmarcados con ⚠ y el motivo).
   - **Duración garantizada:** si el corte pasa del máximo, se desmarcan los tramos de menor
     importancia (nunca el primero ni el último); si no llega al mínimo, se agregan los mejores
     candidatos que no se usaron. Todo queda visible y lo puedes cambiar.
3. Revisa las pestañas y desmarca lo que no quieras:
   - **Corte**: tramos a conservar, en orden, cerca de la duración objetivo. Los bordes se
     ajustan para no partir palabras. La columna **Velocidad** dice si un tramo va normal o
     acelerado; un clic en ella la cambia (normal → ×2 → ×3 → ×4 → normal).
   - **Momentos**: los mejores, con nota del 1 al 10 y por qué.
   - **Textos**: frases cortas ("3 horas después…") para lo que el corte se salta.
   - **Resumen** y secciones, **Shorts** y **títulos**.
   Doble clic en una fila mueve el cursor de Vegas a ese punto.
4. Acciones:
   - **Crear regiones y marcadores** para revisar en la línea de tiempo.
   - **Aplicar corte**: quita todo lo que no está marcado y acelera los tramos acelerados, en
     todas las pistas (hazlo antes de poner música), y deja los textos y momentos como
     marcadores en su nuevo lugar. Ctrl+Z lo deshace. Vegas permite hasta ×4 por evento.
   - **Guardar informe**: `<proyecto>.vegascut-informe.md` con todo lo anterior.

**Historial:** cada respuesta se guarda en `<proyecto>.vegascut-ia-historial/` (y la última en
`<proyecto>.vegascut-ia.json`). Al abrir se muestra la última, y en *Respuesta* puedes elegir
cualquier anterior. Si el proyecto cambió desde esa respuesta (por ejemplo, aplicaste el corte),
se puede revisar pero no aplicar; deshaz con Ctrl+Z y vuelve a abrir para aplicarla otra vez.

**Marcadores anclados:** los marcadores y regiones que crea MomentosIA guardan a qué archivo y a
qué segundo del archivo corresponden (`<proyecto>.vegascut-marcas.json`). Si mueves, cortas o
reordenas clips, ejecuta **ReubicarMarcadores** y vuelven a quedar sobre su clip. En el plan gratuito de Gemini, Google puede usar
lo que envías para mejorar sus productos.

### `TextosDesdeMarcadores.cs`

![Ventana de Textos desde marcadores](docs/img/textos.png)

Convierte cada marcador `TEXTO: ...` (los que crea MomentosIA, o uno que pongas tú con esa
etiqueta) en un evento de **Títulos y texto**.

1. Haz un texto con el estilo que quieras (fuente, color, tamaño, efectos del evento, fundidos)
   y **selecciónalo** en la línea de tiempo. Si no seleccionas ninguno se usa el texto más
   cercano al cursor; si el proyecto no tiene textos, Títulos y texto con su estilo normal.
2. Ejecuta el script. Primero reubica los marcadores anclados, por si moviste o recortaste clips.
3. Revisa la lista: desmarca los que no quieras y corrige el texto abajo (Enter = otra línea).
4. Elige la **duración** (por defecto la de la plantilla), si van en una **pista nueva arriba** o
   en la de la plantilla, y si se **quitan los marcadores** ya usados. Un texto termina antes si
   el siguiente empieza, para que no se encimen.
5. **Crear**: cada texto es un medio propio (editar uno no cambia los demás). Ctrl+Z lo deshace.

Se copian todos los parámetros de la plantilla y se cambia solo el texto, conservando el
formato del primer carácter. Si la plantilla tiene animación con fotogramas clave, conviene
que dure lo mismo que los textos nuevos.

### `MusicaAutomatica.cs`

![Ventana de Música que baja sola](docs/img/musica.png)

Baja la música mientras alguien habla y la vuelve a subir en las pausas (*ducking*), con puntos
en la **envolvente de volumen** de la pista, que después puedes mover a mano.

1. Pon la música en su pista (si el corte cambia, hazlo al final).
2. Marca las **voces** (se proponen las que transcribiste) y la **música** (se proponen las
   pistas con mp3, ogg… o con "música" en el nombre). También puedes bajar el juego.
3. **Medir voces**: renderiza cada voz y detecta cuándo hablan, con el mismo umbral por pista
   que *Quitar silencios*. Mide en ese momento, así que sirve aunque hayas editado.
4. Ajusta mirando la gráfica: **bajar a** (−14 dB por defecto), **baja antes de hablar**
   (250 ms), **sube al terminar en** (600 ms) y **solo sube en pausas de** (1200 ms: en pausas
   más cortas se queda abajo para que no suba y baje a cada rato).
5. **Aplicar a la música**: reemplaza la envolvente de volumen en el rango (todo el proyecto o la
   selección de tiempo); fuera del rango no se toca. Ctrl+Z lo deshace.

Los ajustes se recuerdan en `%APPDATA%\vegas-cut\musica.ini`.

### `CensurarPalabrotas.cs`

![Ventana de Censurar palabrotas](docs/img/censura.png)

Al final de la edición: busca palabrotas en la transcripción, las muestra para revisarlas y
las tapa.

1. Revisa la lista: cada coincidencia con quién la dijo, el contexto y qué tan seguro estaba
   Whisper. Desmarca las que no quieras; **doble clic** lleva el cursor ahí y reproduce.
2. **Palabras…** edita la lista (se guarda en `%APPDATA%\vegas-cut\censura-palabras.txt`):
   una por línea, `ching*` acepta cualquier terminación (chingar, chingados…) y varias palabras
   son una frase (`puta madre`). La lista inicial está pensada para tus gameplays y no incluye
   palabras con doble sentido sueltas (*madre*, *huevos*, *perra*, *culo*): «¡Madre mía!» no se
   censura, «puta madre» sí.
3. **Cuándo tapar**: toda la palabra, solo el inicio o solo el final; **antes** y **después**
   (ms de margen) y un **mínimo** (Whisper a veces marca palabras casi sin duración).
4. **Efecto**: pitido clásico de 1 kHz (lo genera el script), un archivo tuyo (*Elegir archivo…*,
   se recuerdan los últimos) o ninguno. Va **al largo de la palabra** (recortado) o entero:
   **empieza con ella**, **centrado** o **termina con ella**. Con su **volumen**.
5. **La voz original**: se silencia solo en la pista de quien la dijo (las demás voces siguen),
   o se deja sonar debajo del efecto.
6. **Censurar**: los efectos van en una pista nueva *Censura*. Palabrotas seguidas se tapan con
   un solo efecto. Ctrl+Z lo deshace todo.

**Funciona aunque edites a mano:** desde esta versión, *Transcribir* guarda de qué archivo y de
qué segundo sale cada parte de la voz, así que cada palabra se encuentra en su lugar actual
aunque muevas o recortes clips; si una parte se borró, aparece como *ya no está*. Las
transcripciones anteriores solo siguen los cortes hechos con estas herramientas: si editaste a
mano, vuelve a transcribir (un video ya editado de 20 min tarda poco).

### `DesenlazarClips.cs`

Al cortar por script, Vegas deja cada pedazo nuevo en el mismo grupo que el clip original, así
que con versiones anteriores de *Quitar silencios* y *MomentosIA* todos los pedazos quedaban
unidos: mover o borrar uno movía o borraba todos. Ahora las herramientas lo corrigen solas al
terminar de cortar. Para proyectos que ya quedaron así, ejecuta **DesenlazarClips**: cada
pedazo vuelve a su propio grupo y **el video sigue unido a sus audios** de ese pedazo (los
eventos que coinciden en el tiempo). Solo toca grupos con dos o más eventos en la misma pista.
Ctrl+Z lo deshace.

### `QuitarSilencios.cs`

![Ventana de Quitar silencios](docs/img/quitar-silencios.png)

Detecta las pausas de una o varias pistas de voz y las quita de la línea de tiempo.

1. Guarda el proyecto y ejecuta el script (ver *Instalación*).
2. Marca las **pistas de voz**: por ejemplo tu micrófono y la llamada. Hay voz si suena
   **cualquiera** de ellas, así que cuando hablan los dos a la vez se conserva todo. Las pistas
   sin marcar (juego, música) no cuentan para detectar, pero con *Todas las pistas* se cortan
   igual para que todo siga sincronizado. Pasa el ratón sobre una pista para ver su detalle.
   Si pones nombre a las pistas en Vegas ("Voz", "Llamada", "Juego") aparece ese nombre.
3. Elige el **rango** (todo el proyecto o la selección de tiempo) y pulsa **Analizar**: Vegas
   renderiza cada pista marcada a un WAV temporal y se mide el volumen cada 10 ms.
4. Ajusta mirando la onda (un carril por pista; en rojo lo que se quitará):
   - **Umbral por pista**: cada pista recibe su propio umbral según su ruido de fondo y su voz
     (se separan los dos grupos de niveles con el método de Otsu y el umbral queda entre ambos).
     Así una pista más baja o con más ruido no arruina la detección de las demás. Arrastra la
     línea punteada de un carril para ajustar solo esa pista; **Auto** los recalcula todos.
   - **Sensibilidad**: sube (corta más) o baja (corta menos) todos los umbrales a la vez.
   - **Perfil**: valores listos para cada tipo de video. Si cambias algo, *Guardar como…* crea
     un perfil tuyo (marcado con ★); los tuyos se pueden borrar.
   - **Silencio mínimo**, **voz mínima** (ignora clics y respiraciones), **margen antes** y
     **después**, **clip mínimo** (no deja pedazos más cortos, evita saltos) y **suavizado**
     (fundido corto del audio en cada corte para que no se oiga un chasquido).

   | Perfil | Silencio mín. | Voz mín. | Antes | Después | Clip mín. | Suavizado | Sensib. |
   |---|---|---|---|---|---|---|---|
   | Narración (video ensayos) | 350 | 150 | 100 | 180 | 700 | 20 | 0 |
   | Tutorial | 600 | 150 | 150 | 300 | 1000 | 25 | 0 |
   | Podcast / charla | 900 | 200 | 200 | 350 | 1500 | 30 | 0 |
   | Gameplay | 1200 | 250 | 250 | 450 | 2000 | 30 | −3 |
   | Shorts / rápido | 200 | 100 | 50 | 80 | 400 | 15 | +2 |

   Valores en ms (sensibilidad en dB).
5. Elige qué hacer: **Eliminar** (corta y junta todo), **Dejar huecos** (quita sin mover),
   **Silenciar** (corta y deja mudo) o **Solo marcar** (crea regiones "Silencio" para revisarlas),
   y dónde cortar: **todas las pistas** o **solo las analizadas**.
6. Todo queda en un solo paso de deshacer: **Ctrl+Z** lo revierte.

Los ajustes se recuerdan en `%APPDATA%\vegas-cut\silencios.ini` y los perfiles propios en `perfiles.ini`, en la misma carpeta.


### `ExportarProyecto.cs`

Exporta proyectos a JSON con:

- Configuración: resolución, fps, frecuencia de audio.
- Media usada, incluidos generadores (Títulos y Texto, color sólido).
- Pistas: volumen, paneo, efectos de pista y envolventes.
- Eventos: inicio, duración, parte del archivo original (`offset`), velocidad,
  fundidos, ganancia, efectos con sus parámetros y keyframes de Pan/Crop
  (con `zoom` calculado).
- Marcadores y regiones.

**Uso:** en Vegas 20, *Herramientas → Secuencias de comandos → Ejecutar
secuencia de comandos…* (*Tools → Scripting → Run Script…*) → elige
`ExportarProyecto.cs`. Pregunta el modo:

- **No → solo el proyecto abierto.** Crea `<proyecto>.export.json` junto al `.veg`.
- **Sí → carpeta completa.** Eliges una carpeta y abre cada `.veg` (con
  subcarpetas), crea su JSON junto al `.veg` y deja una copia de todos en
  `<carpeta>\_vegas-cut-export\`, con un `exportacion.log`. Salta los
  proyectos cuyo JSON ya está al día, así que se puede repetir sin perder tiempo.
  Guarda el proyecto abierto antes de empezar. Si Vegas avisa de archivos que no
  encuentra, elige ignorar para que siga.

Para tenerlo siempre en el menú, copia el `.cs` a
`Documentos\Vegas Script Menu\` y usa *Tools → Scripting → Rescan Script Menu Folder*.

Para analizar, sube la carpeta `_vegas-cut-export` (o los `.export.json`).

## Desarrollo

- El código vive en `src/`. Después de cambiarlo: `python3 herramientas/compilar.py`.
- Los scripts son C# 5 (el compilador de Vegas) y quedan en ASCII (acentos como `\uXXXX`).
- Pruebas sin Vegas: `sh pruebas/ejecutar.sh` (requiere mono y python3). Compila todo contra una
  API falsa de Vegas e imita Whisper y Gemini.
