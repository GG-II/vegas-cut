#!/bin/sh
# Arma los scripts desde src/, los compila como C# 5 contra una API falsa de
# Vegas y ejecuta las pruebas. Requiere mono (mcs) y python3.
set -e
cd "$(dirname "$0")"
python3 ../herramientas/compilar.py > /dev/null
T=$(mktemp -d)
python3 generar_wav.py && mv voz16.wav voz24.wav vozf32.wav "$T"/
mcs -langversion:5 -target:library -out:"$T/ScriptPortal.Vegas.dll" VegasFake.cs
REF="-r:$T/ScriptPortal.Vegas.dll -r:System.Windows.Forms.dll -r:System.Drawing.dll"
# Los scripts armados desde src/ deben compilar como C# 5 (ExportarProyecto.cs
# usa partes de la API que la version falsa no imita).
for s in ../scripts/QuitarSilencios.cs ../scripts/ConfigurarVegasCut.cs ../scripts/Transcribir.cs ../scripts/MomentosIA.cs; do
    mcs -langversion:5 -nowarn:414,169,649,219 -target:library $REF -out:"$T/$(basename "$s" .cs).dll" "$s"
done
mcs -langversion:5 -nowarn:414,169,649,219 $REF -out:"$T/silencios.exe" PruebaSilencios.cs ../scripts/QuitarSilencios.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/ia.exe" PruebaIA.cs ../scripts/MomentosIA.cs ../src/comun/Whisper.cs
cd "$T"
echo "== Quitar silencios"; mono silencios.exe
echo "== Transcripcion, Whisper, Gemini y Momentos"; XDG_CONFIG_HOME="$T/config" mono ia.exe
