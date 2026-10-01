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
    'MomentosIA': ['momentos/cabecera.txt', 'momentos/Momentos.cs', 'momentos/LogicaMomentos.cs',
                   'comun/Editor.cs', 'comun/PistasVegas.cs', 'comun/Serie.cs', 'comun/VentanaSerie.cs', 'comun/Anclas.cs'] + COMUN_BASE + ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'ReubicarMarcadores': ['marcadores/cabecera.txt', 'marcadores/Reubicar.cs', 'comun/Anclas.cs', 'comun/Json.cs'],
    'TextosDesdeMarcadores': ['textos/cabecera.txt', 'textos/Textos.cs', 'textos/Generador.cs', 'comun/Rtf.cs',
                              'comun/Anclas.cs', 'comun/Audio.cs', 'comun/Json.cs', 'comun/Ui.cs'],
    'MusicaAutomatica': ['musica/cabecera.txt', 'musica/Musica.cs', 'musica/LogicaMusica.cs', 'comun/PistasVegas.cs'] +
                        COMUN_BASE + ['silencios/Deteccion.cs', 'comun/Ui.cs'],
    'Anteriormente': ['anteriormente/cabecera.txt', 'anteriormente/Anteriormente.cs', 'anteriormente/LogicaAnteriormente.cs',
                      'comun/Serie.cs', 'comun/VentanaSerie.cs', 'comun/PistasVegas.cs', 'comun/Editor.cs'] + COMUN_BASE +
                     ['comun/Configuracion.cs', 'comun/Gemini.cs', 'comun/Ui.cs'],
    'DesenlazarClips': ['grupos/cabecera.txt', 'grupos/Desenlazar.cs', 'comun/Editor.cs', 'comun/Audio.cs'],
    'CensurarPalabrotas': ['censura/cabecera.txt', 'censura/Censura.cs', 'censura/LogicaCensura.cs',
                           'comun/PistasVegas.cs', 'comun/Editor.cs'] + COMUN_BASE + ['comun/Ui.cs'],
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

def compilar(nombre, partes):
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
    destino = os.path.join(RAIZ, 'scripts', nombre + '.cs')
    with open(destino, 'w', encoding='ascii', newline='\n') as f:
        f.write(ascii(salida))
    return destino

if __name__ == '__main__':
    for nombre, partes in HERRAMIENTAS.items():
        print('compilado', os.path.relpath(compilar(nombre, partes), RAIZ))
