# Plugin para Vegas Pro (inspirado en AutoCut)

Proyecto: crear una herramienta para Vegas Pro con funciones similares a las de AutoCut (Premiere / DaVinci Resolve), sin depender de IA que analice el contenido del video.

> Nota: implementación propia. No copiar código ni comportamiento interno de AutoCut, que es un producto comercial.

---

## 1. Cómo se extiende Vegas Pro

Vegas no tiene plugins como Premiere, pero ofrece:

- **Scripts (.cs / .vb / .js):** se guardan en la carpeta de scripts de Vegas y se ejecutan desde *Tools → Scripting*. Son los más rápidos de hacer y probar.
- **Extensiones (.dll):** librerías .NET que se colocan en la carpeta *Application Extensions*. Permiten ventanas acopladas con botones, sliders y opciones.
- **API:** se referencia `ScriptPortal.Vegas.dll` (viene en la carpeta de instalación de Vegas). Permite:
  - Recorrer pistas y eventos
  - Cortar y borrar clips
  - Agregar marcadores
  - Crear keyframes de zoom/pan (`VideoMotion`)
  - Insertar textos

**Decidido:** VEGAS Pro 20 → API `ScriptPortal.Vegas`, proyectos en .NET Framework 4.8. Los scripts `.cs` se escriben en C# 5 (compilador clásico de Vegas).

### Limitación importante

La API de Vegas no es buena para leer muestras de audio directamente. Arquitectura recomendada:

1. **Analizador externo** (ffmpeg, Whisper o programa propio) que procesa el audio y devuelve un **JSON con tiempos**.
2. **Script o extensión de Vegas** que lee ese JSON y edita la línea de tiempo.

---

## 2. Funciones de AutoCut y su viabilidad

### Grupo A: sin IA (audio + API de Vegas)

| Función | Cómo se haría |
|---|---|
| **Silences** | ffmpeg `silencedetect` (o análisis de volumen propio). El script corta y borra los tramos. |
| **Podcast** | Silences + Angles en un solo paso. |
| **Angles** | Comparar volumen de cada micrófono y alternar a la cámara de quien habla, con tiempo mínimo entre cambios para evitar saltos. |
| **AutoZoom** | Keyframes de zoom en cada corte, en picos de volumen o a intervalos fijos. |

Totalmente viables y cubren lo más útil del plugin.

### Grupo B: solo transcripción (Whisper local, no "entiende" el video)

| Función | Comentario |
|---|---|
| **Captions** | Whisper da texto con tiempos por palabra. Lo difícil es el estilo animado en Vegas (palabra por palabra, resaltado). |
| **Profanity Filter** | Whisper con tiempos por palabra + lista de groserías + tono o silencio sobre esos tramos. Lógica simple. |
| **Repeat (malas tomas)** | Detectar frases repetidas comparando transcripciones y conservar la última toma. La más frágil; riesgo de falsos positivos. |

Whisper corre local y gratis. Si se quiere evitar cualquier modelo de IA, este grupo queda fuera.

### Grupo C: requieren IA de contenido (descartadas)

- **AutoViral:** decidir momentos "virales" y reencuadrar a vertical con seguimiento de cara.
- **AutoB-Rolls:** entender de qué habla cada tramo; además depende de licencia de StoryBlocks.
- **AutoChapters:** resumir y segmentar por temas. Versión básica posible: marcadores en pausas largas (no equivalente).

---

## 3. Plan de trabajo

1. [ ] Definir versión de Vegas y .NET; preparar proyecto en Visual Studio.
2. [x] **Silences**: `scripts/QuitarSilencios.cs` (falta probarlo en Vegas 20 real).
3. [x] Ventana con parámetros: umbral en dB, duración mínima, margen antes/después.
4. [ ] **AutoZoom**: keyframes en cortes o picos de volumen.
5. [ ] **Angles**: alternar cámaras según el micrófono activo.
6. [ ] **Podcast**: combinar Silences + Angles.
7. [ ] **Profanity Filter**: primer uso de Whisper.
8. [ ] **Captions**: estilo animado (al final, más trabajo visual).
9. [ ] Empaquetar como extensión `.dll` para distribución.

---

## 4. Decisiones abiertas

- Analizador: ¿C# puro o ffmpeg como ejecutable auxiliar?
- ¿Script primero y extensión después, o directo a extensión?
- ¿Se incluirá el grupo B (Whisper) o el proyecto se mantiene 100% sin IA?
- Formato del JSON intermedio (propuesta: lista de rangos `{start, end}` en segundos + tipo de evento).

---

## 5. Notas

- Empezar por Silences: es el núcleo y lo que más valor aporta.
- Angles y AutoZoom reutilizan el mismo análisis de audio.

---

## 6. Transcripción e IA (en curso)

- [x] `ConfigurarVegasCut.cs`: clave de Gemini (cifrada) y Faster-Whisper-XXL.
- [x] `Transcribir.cs`: Whisper local + niveles de sonido por pista → `<proyecto>.vegascut.json`.
- [x] `MomentosIA.cs`: resumen, momentos, corte a duración objetivo, textos, Shorts y títulos con Gemini.
- [x] Probado en Vegas 20: Transcribir (GTX 1650, large-v3-turbo) y Momentos (5 min → 2 min con buen ritmo).
- [x] Videos largos (2 h → 24 min): análisis por partes + pasada final; reintentos.
- [x] Transiciones aceleradas (×2–×4) en vez de cortadas.
- [x] Probado con un gameplay de 1 h (S01E02): funciona; se corrigieron reglas ignoradas y duración.
- [x] Reglas del canal, duración mín/máx garantizada, revisión, contexto de episodios, modelo e historial.
- [x] Marcadores anclados a los clips + ReubicarMarcadores.
- [x] `TextosDesdeMarcadores.cs`: marcadores `TEXTO:` → Títulos y texto con estilo de plantilla.
- [x] `MusicaAutomatica.cs`: la música baja sola con la voz (envolvente de volumen).
- [x] `CensurarPalabrotas.cs`: palabrotas desde la transcripción, pitido o efecto propio, voz silenciada.
- [x] La transcripción guarda las fuentes (archivo + segundo) para seguir ediciones a mano.
- [ ] Probar en Vegas 20: Títulos y texto por script, envolvente de volumen y censura.
- [ ] Zoom de impacto en ★ y picos, Shorts 9:16 desde regiones `SHORT:`.
- [ ] Subtítulos desde la transcripción (pospuesto).
