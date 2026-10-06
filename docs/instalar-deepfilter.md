# Instalar DeepFilterNet (quitar ruido de las voces)

*LimpiarVoces* usa **DeepFilterNet**, una IA gratuita que corre en tu PC y está hecha para voz:
quita ventiladores, teclado, zumbidos y eco de cuarto sin dejar la voz robótica. No se instala
nada: es un solo `.exe`.

1. Entra a <https://github.com/Rikorose/DeepFilterNet/releases/tag/v0.5.6>.
2. En **Assets** baja **`deep-filter-0.5.6-x86_64-pc-windows-msvc.exe`** (25.7 MB).
   Es el único para Windows; los demás son para Mac o Linux, y el `.dll` es un plugin para otros
   programas que no hace falta.
3. Ponlo en una carpeta fija, por ejemplo `C:\Herramientas\DeepFilterNet\`. Puedes dejarle el nombre
   o llamarlo `deep-filter.exe`.
4. Si Windows avisa «Windows protegió su PC» al usarlo: **Más información → Ejecutar de todas
   formas** (no está firmado).
5. En Vegas abre *LimpiarVoces* (o *PasoFinal → Limpiar voces…*) y en **QUITAR RUIDO** pulsa
   **Elegir…** y busca el `.exe`. Se recuerda.

**ffmpeg** también hace falta (saca el audio, empareja el volumen y deja todo al mismo nivel).
Si ya lo tienes en el PATH o viene con Faster-Whisper-XXL, se encuentra solo; si no, **Elegir…** en
la línea **FFMPEG**.

No usa la tarjeta de video: va con el procesador, varios pedazos a la vez. En una PC normal tarda
bastante menos que lo que dura lo que se limpia, y solo se limpia lo que quedó en el video.
