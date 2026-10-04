#!/usr/bin/env python3
"""Arma los scripts de Vegas (scripts/*.cs) a partir de src/.

Vegas ejecuta un solo archivo .cs por herramienta, asi que el codigo comun
(src/comun) se copia dentro de cada script. Ademas:
  - junta y ordena los "using" de todos los archivos,
  - pasa todo a ASCII (los acentos quedan como escapes \\uXXXX) para que Vegas
    los lea bien sin importar la codificacion.

Uso:  python3 herramientas/compilar.py
"""
import os, re, sys

RAIZ = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

COMUN_BASE = ['comun/Audio.cs', 'comun/Json.cs', 'comun/Transcripcion.cs']

HERRAMIENTAS = {
    'QuitarSilencios': ['silencios/cabecera.txt', 'silencios/EntryPoint.cs', 'comun/PistasVegas.cs',
                        'comun/Editor.cs'] + COMUN_BASE + ['silencios/Deteccion.cs', 'comun/Ui.cs',
                        'silencios/Ventana.cs'],
    'ConfigurarVegasCut': ['configuracion/cabecera.txt', 'configuracion/Configurar.cs', 'comun/Json.cs',
                           'comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'Transcribir': ['transcribir/cabecera.txt', 'transcribir/Transcribir.cs', 'comun/PistasVegas.cs'] +
                   COMUN_BASE + ['comun/Configuracion.cs', 'comun/Whisper.cs', 'comun/Ui.cs'],
    'MomentosIA': ['momentos/cabecera.txt', 'momentos/EntryPoint.cs', 'momentos/Entrada.cs', 'momentos/Momentos.cs', 'momentos/LogicaMomentos.cs',
                   'comun/Editor.cs', 'comun/PistasVegas.cs', 'comun/Serie.cs', 'comun/VentanaSerie.cs', 'comun/VentanaFormato.cs', 'comun/SerieTV.cs', 'comun/BibliotecaMusica.cs', 'comun/CatalogoAnime.cs', 'comun/VentanaMusica.cs', 'comun/Ritmo.cs', 'comun/Anclas.cs'] + COMUN_BASE + ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'ReubicarMarcadores': ['marcadores/cabecera.txt', 'marcadores/Reubicar.cs', 'comun/Anclas.cs', 'comun/Json.cs'],
    'TextosDesdeMarcadores': ['textos/cabecera.txt', 'textos/Textos.cs', 'textos/Generador.cs', 'comun/Rtf.cs',
                              'comun/Anclas.cs', 'comun/Audio.cs', 'comun/Json.cs', 'comun/Ui.cs'],
    'MusicaAutomatica': ['musica/cabecera.txt', 'musica/EntryPoint.cs', 'musica/Musica.cs', 'musica/LogicaMusica.cs', 'comun/PistasVegas.cs'] +
                        COMUN_BASE + ['silencios/Deteccion.cs', 'comun/Ui.cs'],
    'Anteriormente': ['anteriormente/cabecera.txt', 'anteriormente/Anteriormente.cs', 'anteriormente/LogicaAnteriormente.cs',
                      'comun/Serie.cs', 'comun/VentanaSerie.cs', 'comun/VentanaFormato.cs', 'comun/SerieTV.cs', 'comun/BibliotecaMusica.cs', 'comun/CatalogoAnime.cs', 'comun/VentanaMusica.cs', 'comun/Ritmo.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs'] + COMUN_BASE +
                     ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'VariosPOV': ['pov/cabecera.txt', 'pov/Pov.cs', 'pov/LogicaPov.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs', 'comun/CopiaBase.cs'] +
                 COMUN_BASE + ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'LimpiarVegasCut': ['limpiar/cabecera.txt', 'limpiar/Limpiar.cs', 'limpiar/LogicaLimpiar.cs', 'comun/Serie.cs', 'comun/SerieTV.cs',
                        'comun/BibliotecaMusica.cs', 'comun/CatalogoAnime.cs', 'comun/Ritmo.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs'] +
                       COMUN_BASE + ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'Series': ['series/cabecera.txt', 'series/Series.cs', 'comun/Serie.cs', 'comun/VentanaSerie.cs', 'comun/VentanaFormato.cs', 'comun/SerieTV.cs', 'comun/BibliotecaMusica.cs', 'comun/CatalogoAnime.cs', 'comun/VentanaMusica.cs', 'comun/Ritmo.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs'] + COMUN_BASE +
              ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'PrepararEpisodio': ['preparar/cabecera.txt', 'preparar/Preparar.cs', 'comun/Proceso.cs', 'momentos/Entrada.cs',
                         'momentos/Momentos.cs', 'momentos/LogicaMomentos.cs', 'comun/Editor.cs', 'comun/PistasVegas.cs',
                         'comun/Serie.cs', 'comun/VentanaSerie.cs', 'comun/VentanaFormato.cs', 'comun/SerieTV.cs', 'comun/BibliotecaMusica.cs', 'comun/CatalogoAnime.cs', 'comun/VentanaMusica.cs', 'comun/Ritmo.cs', 'comun/Anclas.cs'] + COMUN_BASE +
                        ['silencios/Deteccion.cs', 'comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Whisper.cs', 'comun/Ui.cs'],
    'DesenlazarClips': ['grupos/cabecera.txt', 'grupos/Desenlazar.cs', 'comun/Editor.cs', 'comun/Audio.cs'],
    'CensurarPalabrotas': ['censura/cabecera.txt', 'censura/EntryPoint.cs', 'censura/Censura.cs', 'censura/LogicaCensura.cs',
                           'comun/PistasVegas.cs', 'comun/Editor.cs'] + COMUN_BASE + ['comun/Ui.cs'],
    'PulirEpisodio': ['pulir/cabecera.txt', 'pulir/Pulir.cs', 'pulir/LogicaPulir.cs', 'pulir/VentanaPlan.cs', 'pulir/LogicaPlan.cs',
                      'pulir/AplicarPlan.cs', 'pulir/Voz.cs', 'textos/Generador.cs', 'comun/Rtf.cs', 'musica/LogicaMusica.cs',
                      'silencios/Deteccion.cs', 'comun/Ritmo.cs', 'comun/Serie.cs',
                      'comun/VentanaSerie.cs', 'comun/VentanaFormato.cs', 'comun/SerieTV.cs', 'comun/BibliotecaMusica.cs',
                      'comun/CatalogoAnime.cs', 'comun/VentanaMusica.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs'] + COMUN_BASE +
                     ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'ProducirCapitulo': ['producir/cabecera.txt', 'producir/Produccion.cs', 'producir/LogicaProduccion.cs', 'producir/Armar.cs',
                         'pulir/LogicaPlan.cs', 'pulir/AplicarPlan.cs', 'pulir/Voz.cs', 'pulir/LogicaPulir.cs', 'textos/Generador.cs',
                         'comun/Rtf.cs', 'musica/LogicaMusica.cs', 'comun/Proceso.cs', 'comun/Ritmo.cs', 'comun/Serie.cs', 'comun/SerieTV.cs',
                         'comun/BibliotecaMusica.cs', 'comun/CatalogoAnime.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs',
                         'silencios/Deteccion.cs'] + COMUN_BASE + ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Whisper.cs', 'comun/Ui.cs'],
    'PasoFinal': ['final/cabecera.txt', 'final/PasoFinal.cs', 'final/LogicaPasoFinal.cs', 'final/Relleno.cs', 'final/LogicaRelleno.cs', 'final/Memes.cs', 'final/LogicaMemes.cs', 'comun/Memes.cs', 'musica/Musica.cs', 'musica/LogicaMusica.cs', 'censura/Censura.cs',
                  'censura/LogicaCensura.cs', 'pulir/AplicarPlan.cs', 'pulir/LogicaPlan.cs', 'pulir/LogicaPulir.cs', 'pulir/Voz.cs',
                  'textos/Generador.cs', 'comun/Rtf.cs', 'comun/Ritmo.cs', 'comun/Serie.cs', 'comun/SerieTV.cs', 'comun/BibliotecaMusica.cs',
                  'comun/CatalogoAnime.cs', 'comun/Proceso.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs', 'silencios/Deteccion.cs'] +
                 COMUN_BASE + ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Whisper.cs', 'comun/Ui.cs'],
}

# La copia base va con el proceso (y con MomentosIA, que la guarda antes de cortar).
for _nombre, _partes in HERRAMIENTAS.items():
    if ('comun/Proceso.cs' in _partes or _nombre == 'MomentosIA') and 'comun/CopiaBase.cs' not in _partes:
        _partes.append('comun/CopiaBase.cs')

# Los tipos de capitulo van con la serie en todos los scripts que la usan.
for _partes in HERRAMIENTAS.values():
    if 'comun/Serie.cs' in _partes and 'comun/TiposCapitulo.cs' not in _partes:
        _partes.insert(_partes.index('comun/Serie.cs') + 1, 'comun/TiposCapitulo.cs')

# Programas aparte (fuera de Vegas): se compilan con el csc de Windows (ver programas/*.bat).
PROGRAMAS = {
    'Memes': ['memes/cabecera.txt', 'memes/MemesApp.cs', 'comun/Memes.cs', 'comun/Json.cs',
              'comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
}

def separar_usings(texto):
    usings, cuerpo, en_cabeza = [], [], True
    for linea in texto.split('\n'):
        if en_cabeza and linea.startswith('using ') and linea.rstrip().endswith(';'):
            usings.append(linea.strip())
            continue
        if en_cabeza and linea.strip() == '':
            continue
        en_cabeza = False
        cuerpo.append(linea)
    return usings, '\n'.join(cuerpo).rstrip() + '\n'

def orden_using(u):
    alias = '=' in u
    sistema = u.startswith('using System')
    return (alias, not sistema, u)

def ascii(texto):
    return ''.join(c if ord(c) < 128 else '\\u%04x' % ord(c) for c in texto)

def compilar(nombre, partes, carpeta='scripts'):
    cabecera = ''
    usings, cuerpos = set(), []
    for parte in partes:
        ruta = os.path.join(RAIZ, 'src', parte)
        texto = open(ruta, encoding='utf-8').read()
        if parte.endswith('.txt'):
            cabecera = texto.rstrip() + '\n'
            continue
        u, c = separar_usings(texto)
        usings.update(u)
        cuerpos.append('// ---- src/%s ----\n\n%s' % (parte, c))
    salida = (cabecera +
              '//\n// GENERADO desde src/ con herramientas/compilar.py: no editar este archivo a mano.\n\n' +
              '\n'.join(sorted(usings, key=orden_using)) + '\n\n' +
              '\n'.join(cuerpos))
    os.makedirs(os.path.join(RAIZ, carpeta), exist_ok=True)
    destino = os.path.join(RAIZ, carpeta, nombre + '.cs')
    with open(destino, 'w', encoding='ascii', newline='\n') as f:
        f.write(ascii(salida))
    return destino

if __name__ == '__main__':
    for nombre, partes in HERRAMIENTAS.items():
        print('compilado', os.path.relpath(compilar(nombre, partes), RAIZ))
    for nombre, partes in PROGRAMAS.items():
        print('compilado', os.path.relpath(compilar(nombre, partes, 'programas'), RAIZ))
