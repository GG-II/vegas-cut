# vegas-cut

Herramientas para agilizar la edición en VEGAS Pro 20, inspiradas en AutoCut
(sin IA de contenido). El plan completo está en [`docs/plan.md`](docs/plan.md).

## Estructura

- `scripts/`: scripts `.cs` para *Tools → Scripting → Run Script…*
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
