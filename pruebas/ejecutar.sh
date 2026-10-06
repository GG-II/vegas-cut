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
# -noconfig: sin System.Core ni otras bibliotecas que Vegas no le da a los
# scripts (HashSet, LINQ...), para que falle aqui y no en Vegas.
# Contra las referencias de .NET Framework 4.8 (lo que tienen Vegas 20 y Windows),
# no contra las de mono: mono trae metodos mas nuevos (p. ej. TrimEnd(char)) que en
# Windows dan "Metodo no encontrado" al ejecutar.
NET48=/usr/lib/mono/4.8-api
NET48_REF="-nostdlib -noconfig -r:$NET48/mscorlib.dll -r:$NET48/System.dll -r:$NET48/System.Drawing.dll -r:$NET48/System.Windows.Forms.dll"
VEGAS_REF="$NET48_REF -r:$T/ScriptPortal.Vegas.dll"
for s in ../scripts/QuitarSilencios.cs ../scripts/ConfigurarVegasCut.cs ../scripts/Transcribir.cs ../scripts/MomentosIA.cs ../scripts/ReubicarMarcadores.cs ../scripts/TextosDesdeMarcadores.cs ../scripts/MusicaAutomatica.cs ../scripts/CensurarPalabrotas.cs ../scripts/DesenlazarClips.cs ../scripts/Anteriormente.cs ../scripts/Series.cs ../scripts/PrepararEpisodio.cs ../scripts/PulirEpisodio.cs ../scripts/ProducirCapitulo.cs ../scripts/PasoFinal.cs ../scripts/LimpiarVegasCut.cs ../scripts/VariosPOV.cs ../scripts/LimpiarVoces.cs ../scripts/Subtitulos.cs; do
    mcs -langversion:5 -nowarn:414,169,649,219 -target:library $VEGAS_REF -out:"$T/$(basename "$s" .cs).dll" "$s"
done
# El programa aparte se compila igual que en Windows (csc de .NET Framework 4, C# 5).
mcs -langversion:5 -nowarn:414,169,649,219 -target:winexe $NET48_REF -out:"$T/Memes.exe" ../programas/Memes.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -out:"$T/silencios.exe" PruebaSilencios.cs ../scripts/QuitarSilencios.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/ia.exe" PruebaIA.cs ../scripts/MomentosIA.cs ../src/comun/Whisper.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -out:"$T/textos.exe" PruebaTextos.cs ../scripts/TextosDesdeMarcadores.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -out:"$T/musica.exe" PruebaMusica.cs ../scripts/MusicaAutomatica.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -out:"$T/censura.exe" PruebaCensura.cs ../scripts/CensurarPalabrotas.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/anteriormente.exe" PruebaAnteriormente.cs ../scripts/Anteriormente.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/ritmo.exe" PruebaRitmo.cs ../scripts/PulirEpisodio.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/pulir.exe" PruebaPulir.cs ../scripts/PulirEpisodio.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/serietv.exe" PruebaSerieTV.cs ../scripts/Series.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/produccion.exe" PruebaProduccion.cs ../scripts/ProducirCapitulo.cs ../src/final/LogicaPasoFinal.cs ../src/final/LogicaRelleno.cs ../src/final/LogicaMemes.cs ../src/comun/Memes.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/preparar.exe" PruebaPreparar.cs ../scripts/PrepararEpisodio.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/limpiar.exe" PruebaLimpiar.cs ../scripts/LimpiarVegasCut.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/pov.exe" PruebaPov.cs ../scripts/VariosPOV.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -out:"$T/voces.exe" PruebaVoces.cs ../scripts/LimpiarVoces.cs
mcs -langversion:5 -nowarn:414,169,649,219 $REF -r:System.Net.dll -out:"$T/subtitulos.exe" PruebaSubtitulos.cs ../scripts/Subtitulos.cs
EJEMPLOS="$(cd .. && pwd)/ejemplos/jojmania"
MUSICA="$(cd .. && pwd)/ejemplos/musica/musica.csv"
cd "$T"
echo "== Quitar silencios"; mono silencios.exe
echo "== Transcripcion, Whisper, Gemini y Momentos"; XDG_CONFIG_HOME="$T/config" mono ia.exe
echo "== Textos desde marcadores"; mono textos.exe
echo "== Musica que baja sola"; XDG_CONFIG_HOME="$T/config" mono musica.exe
echo "== Censurar palabrotas"; XDG_CONFIG_HOME="$T/config" mono censura.exe
echo "== Anteriormente"; XDG_CONFIG_HOME="$T/config" mono anteriormente.exe
echo "== Preparar episodio"; XDG_CONFIG_HOME="$T/config" mono preparar.exe
echo "== Ritmo y formato de series"; XDG_CONFIG_HOME="$T/config" mono ritmo.exe "$EJEMPLOS"
echo "== Pulir: estructura y narracion"; XDG_CONFIG_HOME="$T/config" mono pulir.exe "$EJEMPLOS"
echo "== Serie de TV y musica"; XDG_CONFIG_HOME="$T/config" mono serietv.exe "$MUSICA"
echo "== Producir capitulo y paso final"; XDG_CONFIG_HOME="$T/config" mono produccion.exe "$EJEMPLOS" "$MUSICA"
echo "== Limpiar"; XDG_CONFIG_HOME="$T/config" mono limpiar.exe
echo "== Varios POV"; XDG_CONFIG_HOME="$T/config" mono pov.exe
echo "== Limpiar voces"; XDG_CONFIG_HOME="$T/config" mono voces.exe
echo "== Subtitulos"; XDG_CONFIG_HOME="$T/config" mono subtitulos.exe
