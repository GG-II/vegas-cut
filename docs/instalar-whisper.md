# Instalar Faster-Whisper-XXL

`Transcribir.cs` usa **Faster-Whisper-XXL**: un programa para Windows que transcribe en tu PC,
sin Python y sin enviar el audio a internet. Ya trae todo lo necesario para usar la tarjeta NVIDIA.

> **Ojo:** no es el archivo *"faster-whisper source code.zip"* de SYSTRAN. Ese es el código
> fuente de la librería para Python y no trae ningún `.exe`.

## 1. Descargar

1. Entra a <https://github.com/Purfview/whisper-standalone-win/releases>.
2. Busca la versión **Faster-Whisper-XXL** más reciente (por ejemplo `Faster-Whisper-XXL r245.4`).
3. En **Assets**, descarga el archivo para Windows: `Faster-Whisper-XXL_r…_windows.7z`
   (pesa más de 1 GB porque incluye las librerías de NVIDIA).

## 2. Descomprimir

1. Descomprímelo con **7-Zip** o WinRAR en una carpeta fija, de preferencia con una ruta corta
   y sin acentos. Por ejemplo `D:\Programas\Faster-Whisper-XXL\`.
2. Dentro debe quedar `faster-whisper-xxl.exe` junto con varias `.dll`.
   No muevas el `.exe` fuera de esa carpeta.

## 3. Configurarlo en Vegas

1. Copia `scripts/ConfigurarVegasCut.cs` a `Documentos\Vegas Script Menu\` (igual que los demás
   scripts) y ejecútalo desde *Herramientas → Secuencias de comandos*.
2. En **Programa**, pulsa *Buscar…* y elige `faster-whisper-xxl.exe`.
3. Para tu equipo (GTX 1650 de 4 GB, Ryzen 7 5700X, 32 GB de RAM):
   - **Modelo:** `large-v3-turbo`
   - **Dónde corre:** *Tarjeta*
   - **Precisión:** `int8` (usa menos memoria de video)
   - **Idioma:** `es`
4. Pulsa **Probar Whisper**: debe decir que responde.
5. **Guardar**.

## 4. Primera transcripción

- **La primera vez descarga el modelo** (unos 1.6 GB para `large-v3-turbo`) a la carpeta `_models`
  junto al `.exe`. Necesita internet y tarda unos minutos; las siguientes veces ya no.
- Empieza con un video corto para ver cuánto tarda en tu PC.

## Si algo falla

| Síntoma | Qué hacer |
|---|---|
| Error de memoria de video (*CUDA out of memory*) | Cambia el modelo a `medium`, o deja `int8` si estabas en `float16`. |
| Error de CUDA o de librerías | Actualiza el controlador de NVIDIA (GeForce Experience o nvidia.com). Si sigue, usa *Procesador*: es más lento, pero funciona. |
| Muy lento | Revisa que diga *Tarjeta* y no *Procesador*. En procesador, `medium` es más rápido que `large-v3-turbo`. |
| Nombres mal escritos (Ijichi, Gojo…) | Es normal en Whisper. Por ahora no afecta a Momentos con IA, porque Gemini entiende el contexto. |
| "Whisper no generó la transcripción" | La ventana muestra el final de lo que imprimió el programa: cópialo y revísalo con quien te ayude. |

Las **opciones extra** de la configuración se pasan tal cual al `.exe`, por si algún día hace falta
una opción avanzada de Faster-Whisper-XXL.
