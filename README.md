# vegas-cut

Herramientas para agilizar la edición en VEGAS Pro 20, inspiradas en AutoCut.
El plan completo está en [`docs/plan.md`](docs/plan.md).

| Script | Para qué |
|---|---|
| `QuitarSilencios.cs` | Quita las pausas de una o varias pistas de voz. |
| `ConfigurarVegasCut.cs` | Clave de Gemini y ruta de Faster-Whisper-XXL (una sola vez). |
| `Transcribir.cs` | Texto con tiempos por palabra y nivel de sonido de cada pista, en tu PC. |
| `MomentosIA.cs` | Gemini sugiere resumen, momentos, un corte a la duración que pidas, textos y Shorts. |
| `ExportarProyecto.cs` | Exporta proyectos a JSON para estudiar patrones de edición. |

**Flujo sugerido para un gameplay:** *Quitar silencios* (opcional) → *Transcribir* →
*Momentos con IA* → revisar → *Aplicar corte*. La transcripción sigue los cortes que hacen
estas herramientas, incluso si los deshaces con Ctrl+Z.

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

1. Elige el **tipo de video**, la **duración objetivo**, los **nombres** de las personas
   y, si quieres, **indicaciones** ("es el episodio 3, que se entienda la historia").
   - **Transiciones: Cortar o Acelerar.** Con *Acelerar*, Gemini puede marcar tramos con poca
     conversación (viajar, minar, construir) para verse ×2–×4 en vez de desaparecer, así la historia
     no da saltos. **Audio de lo acelerado:** mudo (recomendado) o acelerado.
2. **Pedir a Gemini**: se envía solo texto (transcripción con tiempos e intensidad del sonido
   cada 5 s). Tarda uno o dos minutos.
   - **Videos largos (más de 35 min) se analizan por partes** de unos 20 min, cortando en la
     pausa más larga: cada parte elige candidatos (con importancia 1–10) y resume lo que pasa,
     sabiendo lo que pasó antes; una pasada final arma el corte completo cuidando la historia.
     Si una respuesta llega incompleta se reintenta, y si no cupo se pasa solo al modo por partes.
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

La respuesta se guarda en `<proyecto>.vegascut-ia.json` y se vuelve a mostrar al abrir la
herramienta mientras el proyecto no cambie. En el plan gratuito de Gemini, Google puede usar
lo que envías para mejorar sus productos.

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
