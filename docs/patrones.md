# Patrones de edición (análisis de 16 proyectos)

Datos sacados de los `.export.json` en `ejemplos/` (8 video ensayos con avatar,
8 videos de Minecraft). Todos son 1920x1080, casi todos a 59.94/60 fps, de 4 a 15 minutos.

## 1. Avatar (video ensayos)

- **Cambias la expresión cada ~3 s.** En `Enero` hay 143 cambios en 8 min (~18 por minuto).
  Se usan más de 15 imágenes de `Recursos\Avatar\` (`feliz hablando`, `feliz`, `feliz 2`,
  `hablando`, `normal`, `pregunta`, `cerrando ojos`…).
- **Siempre el mismo "rebote":** 127 de 143 eventos tienen exactamente 3 keyframes de
  Pan/Crop: zoom 1.80 → 1.66 → 1.75 en 0, 1 y 2 fotogramas (0, 0.033 s y 0.067 s), centro (548, 550).
  En proyectos más viejos (`Obsoletas`) es un solo keyframe fijo en 1.75.
- **Parpadeos a mano:** los pares `normal → cerrando ojos → normal` aparecen muchas veces.
- Los cambios de expresión **no** siguen los cortes de la voz (solo 25 de 143 coinciden),
  así que los decides por contenido. Pero *hablando*/*callado* y los parpadeos sí se pueden automatizar.

## 2. Cortes de la grabación principal

| Tipo | Cortes por minuto | Duración de cada pieza (mediana) | Lo que se quita entre piezas (mediana) |
|---|---|---|---|
| Voz del avatar (`*-mejorada-v2.wav`, OBS) | 6–9 | 4–7 s | **1–1.5 s** |
| Gameplay de Minecraft (OBS) | 5–7 | 2–8 s | 3–15 s, con saltos de minutos |

- En la **voz** lo que quitas son pausas y errores cortos: es exactamente **Silences**.
- En **Minecraft** se quitan entre 10 min y más de 7 h por video: es **elegir los momentos buenos**
  de grabaciones largas, no solo silencios. Además cortas a la vez 3 o 4 pistas de audio
  de la misma grabación (pistas separadas de OBS).
- Los fundidos de 0.01 s son los que Vegas pone por defecto; no son decisión tuya.

## 3. Zoom (Pan/Crop)

- **Gameplay:** zoom fijo **1.03** centrado en casi todos los clips de la grabación principal
  (150 eventos en `Cap1`, 164 en `Sin título`), y **1.2** en la segunda cámara (centro ~800, 439).
  Es el mismo ajuste repetido en cada pedazo.
- Aparecen zooms puntuales (1.5, 2.0, 2.8, 6.0) para remarcar momentos.
- Los clips de apoyo en los video ensayos usan zoom 1.5 / 1.8 / 1.78.

## 4. Textos

- **Plantilla de episodio** (serie JJK): "Episodio", número, título, "Próximo Episodio" y
  título siguiente, siempre con la fuente *Sudbury Book*.
- **Cuentas regresivas:** `topred`, del 10 al 1, con la fuente *AngryBirds*; también "Día 8, Día 9, Día 10…" con *Komika Jam*.
- **Frases partidas en varios textos** (intro de Minecraft): "CADA AÑO / HAGO UN SERVER /
  CON MIS AMIGOS…", con *TT Fellows Trial Black*. Es un estilo de subtítulo animado hecho a mano.

## 5. Otros

- **Presentaciones de imágenes:** `fase3rema2` tiene 123 imágenes numeradas (`1.jpg`, `2.jpg`…), cada una con zoom
  1.78 y fundido de 0.5 s.
- **Voces de personajes o narrador** (`ijichi`, `gojo`, `narrador`…) y efectos de sonido (`Vine Boom`, `menu confirm`)
  colocados a mano.
- **Efectos de evento:** poco uso. Los más comunes son `S_Shake`, `S_BlurMoCurves`,
  `S_DropShadow`, *Blanco y negro* + *Brillo y contraste* + *Vignette* (flashback) y la clave de croma.
- **Audio:** la cadena de pista es la de Vegas por defecto. Una pista de la grabación suele
  quedar en volumen 0.51 (−5.8 dB). La voz se mejora fuera de Vegas antes de importarla.

## Prioridades propuestas

1. **Avatar**: poner una expresión en el cursor con el rebote ya aplicado; después, automatizar la
   boca (*hablando*/*callado* según el volumen) y los parpadeos.
2. **Silences** para la voz de los video ensayos.
3. **Aplicar Pan/Crop a muchos clips a la vez** (1.03 / 1.2 en gameplay), por pista o por archivo.
4. **Generador de textos** desde una lista usando un texto existente como plantilla
   (episodio, cuenta regresiva, "Día N", frases partidas).
5. **Presentación de imágenes desde una carpeta** (duración, zoom y fundido fijos).
6. **Minecraft**: marcadores donde hay voz en la pista del micrófono, para encontrar rápido
   los momentos buenos en grabaciones largas.
