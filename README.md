# vegas-cut

Herramientas para agilizar la edición en VEGAS Pro 20, inspiradas en AutoCut
(sin IA de contenido). El plan completo está en [`docs/plan.md`](docs/plan.md).

## Estructura

- `scripts/`: scripts `.cs` para *Tools → Scripting → Run Script…*
- `ejemplos/`: proyectos `.veg` reales para estudiar patrones de edición
  - `avatar/`: video ensayos con avatar
  - `minecraft/`: gameplays y shorts de Minecraft
- `docs/`: plan y notas

## Scripts

### `ExportarProyecto.cs`

Exporta el proyecto abierto a `<proyecto>.export.json` (junto al `.veg`) con:

- Configuración: resolución, fps, frecuencia de audio.
- Media usada, incluidos generadores (Títulos y Texto, color sólido).
- Pistas: volumen, paneo, efectos de pista y envolventes.
- Eventos: inicio, duración, parte del archivo original (`offset`), velocidad,
  fundidos, ganancia, efectos con sus parámetros y keyframes de Pan/Crop
  (con `zoom` calculado).
- Marcadores y regiones.

**Uso:** abre el proyecto en Vegas 20 → *Tools → Scripting → Run Script…* →
elige `ExportarProyecto.cs`. Al terminar muestra la ruta del JSON.

Para tenerlo siempre en el menú, copia el `.cs` a
`Documentos\Vegas Script Menu\` y usa *Tools → Scripting → Rescan Script Menu Folder*.

Sube los `.export.json` a `ejemplos/` junto a su `.veg` para analizarlos.
