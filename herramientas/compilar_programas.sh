#!/bin/sh
# Arma programas/*.exe desde src/ (en Windows lo mismo lo hace programas/*.bat con el csc de Windows).
# Se compila contra las referencias de .NET Framework 4.8, no contra las de mono:
# si no, el .exe puede usar metodos que en Windows no existen ("Metodo no encontrado").
set -e
cd "$(dirname "$0")/.."
python3 herramientas/compilar.py > /dev/null
NET48=/usr/lib/mono/4.8-api
for f in programas/*.cs; do
    mcs -langversion:5 -target:winexe -optimize -nostdlib -noconfig -nowarn:414,169,649,219 \
        -r:$NET48/mscorlib.dll -r:$NET48/System.dll -r:$NET48/System.Drawing.dll -r:$NET48/System.Windows.Forms.dll \
        -out:"${f%.cs}.exe" "$f"
    echo "$f -> ${f%.cs}.exe"
done
