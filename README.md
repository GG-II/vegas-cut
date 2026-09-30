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

## Scripts

### `QuitarSilencios.cs`

![Ventana de Quitar silencios](docs/img/quitar-silencios.png)

Detecta las pausas de una pista de voz y las quita de la línea de tiempo.

1. Guarda el proyecto y ejecuta el script (*Herramientas → Secuencias de comandos →
   Ejecutar secuencia de comandos…*).
2. Elige la **pista de voz** (sugiere la que tenga un archivo `-mejorada` o más eventos) y el
   **rango**: todo el proyecto o la selección de tiempo.
3. **Analizar**: Vegas renderiza solo esa pista a un WAV temporal y se mide el volumen cada 10 ms.
4. Ajusta mirando la onda (en rojo lo que se quitará):
   - **Umbral**: con el deslizador, arrastrando la línea en la onda o con *Auto*.
   - **Ritmo**: Calmado, Medido, Dinámico, Enérgico, Agresivo.
   - **Silencio mínimo**, **voz mínima** (ignora clics y respiraciones), **margen antes** y **después**.
5. Elige qué hacer: **Eliminar** (corta y junta todo), **Silenciar** (corta y deja mudo) o
   **Solo marcar** (crea regiones "Silencio" para revisarlas), y dónde cortar: **todas las pistas**
   (mantiene la sincronía) o **solo esa pista**.
6. Todo queda en un solo paso de deshacer: **Ctrl+Z** lo revierte.

Los ajustes se recuerdan en `%APPDATA%\vegas-cut\silencios.ini`.


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
