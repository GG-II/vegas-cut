#!/bin/sh
# Compila QuitarSilencios.cs como C# 5 contra una API falsa de Vegas y ejecuta las pruebas.
# Requiere mono (mcs) y python3.
set -e
cd "$(dirname "$0")"
T=$(mktemp -d)
python3 generar_wav.py && mv voz16.wav voz24.wav vozf32.wav "$T"/
mcs -langversion:5 -target:library -out:"$T/ScriptPortal.Vegas.dll" VegasFake.cs
mcs -langversion:5 -nowarn:414,169,649 -r:"$T/ScriptPortal.Vegas.dll" -r:System.Windows.Forms.dll -r:System.Drawing.dll \
    -out:"$T/pruebas.exe" PruebaSilencios.cs ../scripts/QuitarSilencios.cs
cp "$T/ScriptPortal.Vegas.dll" "$T/pruebas.exe" "$T"/ 2>/dev/null || true
cd "$T" && mono pruebas.exe
