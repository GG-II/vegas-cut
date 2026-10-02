#!/usr/bin/env python3
"""Analiza la estructura de episodios de anime a partir de sus subtitulos (.ass/.srt)
y de las listas de musica por episodio de jojowiki (.md).

Saca por episodio: cold open, opening, eyecatch, ending, avance del proximo,
recap, dialogo por minuto, narrador/voz interna y musica (temas, cobertura,
duracion). Escribe un resumen en JSON para usarlo como plantilla de serie.

Uso:  python3 herramientas/analizar_anime.py ejemplos/subs [salida.json]
"""
import json, os, re, statistics, sys

TEMPORADAS = {'S01': 'PB/BT', 'S02': 'SC', 'S03': 'DU', 'S04': 'GW', 'S05': 'SO', 'S06': 'SBR'}

def seg(t):
    p = t.strip().replace(',', '.').split(':')
    s = 0.0
    for x in p:
        s = s * 60 + float(x)
    return s

# ------------------------------------------------------------------ subtitulos

def leer_ass(ruta):
    lineas, formato = [], None
    with open(ruta, encoding='utf-8-sig', errors='replace') as f:
        for l in f:
            if l.startswith('Format:') and formato is None and 'Text' in l and 'Start' in l:
                formato = [x.strip() for x in l[7:].split(',')]
            if not l.startswith('Dialogue:'):
                continue
            partes = l[9:].split(',', len(formato) - 1)
            d = dict(zip(formato, partes))
            texto = re.sub(r'\{[^}]*\}', '', d.get('Text', '')).replace('\\N', ' ').replace('\\n', ' ').strip()
            lineas.append({'ini': seg(d['Start']), 'fin': seg(d['End']), 'estilo': d.get('Style', '').strip(), 'texto': texto})
    return lineas

def leer_srt(ruta):
    lineas = []
    with open(ruta, encoding='utf-8-sig', errors='replace') as f:
        bloques = f.read().replace('\r', '').split('\n\n')
    for b in bloques:
        r = b.strip().split('\n')
        for i, x in enumerate(r):
            if '-->' in x:
                a, c = x.split('-->')
                texto = ' '.join(r[i + 1:]).strip()
                cursiva = texto.startswith('<i>')
                texto = re.sub(r'<[^>]*>', '', texto)
                lineas.append({'ini': seg(a), 'fin': seg(c.split()[0]), 'estilo': 'italica' if cursiva else 'main', 'texto': texto})
                break
    return lineas

def clase(estilo):
    e = estilo.lower()
    if 'karaoke' in e or re.search(r'\bop\b', e) or 'op ' in e or e.endswith(' op'): return 'op'
    if 'preview' in e or 'next' in e or 'avance' in e: return 'avance'
    if 'eyecatch' in e: return 'eyecatch'
    if 'eptitle' in e or 'maintitle' in e or e.startswith('title'): return 'titulo'
    if 'narrator' in e or 'internal' in e or 'italic' in e or e == 'italica': return 'narrador'
    if 'announcer' in e: return 'narrador'
    if e.startswith(('jojo-main', 'jojo-top', 'jojo-overlap', 'jojo-flashback', 'gen_main', 'main', 'default', 'español', 'jojo-netflix')): return 'dialogo'
    return 'cartel'

# ---------------------------------------------------------------- musica (md)

def leer_musica(ruta):
    """{numero de episodio: [ {ini, fin, titulo, ost, desc} ]}"""
    eps, actual = {}, None
    fila = re.compile(r'^\|\s*(\d{1,2}:\d{2})\s*\|\s*(\d{1,2}:\d{2})\s*\|(.*?)\|(.*?)\|(.*?)\|\s*$')
    for l in open(ruta, encoding='utf-8', errors='replace'):
        m = re.search(r'Episode (\d+)\s*:', l)
        if m and not l.startswith('|'):
            actual = int(m.group(1))
            eps.setdefault(actual, [])
            continue
        m = fila.match(l.strip())
        if m and actual is not None:
            enlace = r'\(http[^()]*(?:\([^()]*\))?[^()]*\)'
            titulo = re.sub(r'\*|\[|\]|' + enlace, '', m.group(3)).strip().strip('|').strip().strip('\\').strip()
            ost = re.sub(r'\[|\]|' + enlace, '', m.group(4)).strip().strip('|').strip()
            eps[actual].append({'ini': seg(m.group(1)), 'fin': seg(m.group(2)), 'titulo': titulo, 'ost': ost,
                                'desc': m.group(5).strip()})
    return eps

def temporada_md(nombre):
    for k, v in TEMPORADAS.items():
        if nombre.endswith(' ' + v + '.md') or nombre.endswith(v + '.md'):
            return k
    return 'S01' if nombre == 'List of Music Tracks by Episode.md' else None

# ------------------------------------------------------------------- analisis

def analizar(lineas, musica):
    dur = max([l['fin'] for l in lineas] + [m['fin'] for m in musica] + [0])
    r = {'duracion': round(dur)}
    op = next((m for m in musica if re.search(r'\bopening\b', m['desc'], re.I)), None)
    ed = next((m for m in musica if re.search(r'\bending\b', m['desc'], re.I) and 'placeholder' not in m['desc'].lower()), None)
    prev = next((m for m in musica if re.search(r'next episode', m['desc'], re.I)), None)
    recap = [m for m in musica if re.search(r'recap', m['desc'], re.I)]
    op_sub = [l for l in lineas if clase(l['estilo']) == 'op']
    if op is None and op_sub:
        op = {'ini': min(l['ini'] for l in op_sub), 'fin': max(l['fin'] for l in op_sub)}
    if op: r['op'] = [round(op['ini']), round(op['fin'])]
    if ed: r['ed'] = [round(ed['ini']), round(ed['fin'])]
    av = [l for l in lineas if clase(l['estilo']) == 'avance']
    if prev: r['avance'] = [round(prev['ini']), round(prev['fin'])]
    elif av: r['avance'] = [round(min(l['ini'] for l in av)), round(max(l['fin'] for l in av))]
    if recap: r['recap'] = [round(recap[0]['ini']), round(recap[-1]['fin'])]
    ey = [l for l in lineas if clase(l['estilo']) == 'eyecatch' and l['ini'] > 240]
    if ey: r['eyecatch'] = round(min(l['ini'] for l in ey))
    tit = [l for l in lineas if clase(l['estilo']) == 'titulo']
    if tit: r['titulo'] = round(min(l['ini'] for l in tit))
    # cold open: lo que va antes del opening (o del titulo si no hay opening al inicio)
    if op and op['ini'] > 5: r['cold_open'] = round(op['ini'])
    elif op: r['cold_open'] = 0
    # ritmo de dialogo dentro del capitulo (sin OP/ED/avance)
    fuera = [x for x in [r.get('op'), r.get('ed'), r.get('avance')] if x]
    def dentro(t): return not any(a <= t < b for a, b in fuera)
    dial = [l for l in lineas if clase(l['estilo']) in ('dialogo', 'narrador') and dentro(l['ini'])]
    narr = [l for l in dial if clase(l['estilo']) == 'narrador']
    contenido = dur - sum(b - a for a, b in fuera)
    if dial and contenido > 60:
        r['lineas_min'] = round(len(dial) / (contenido / 60), 1)
        r['palabras_min'] = round(sum(len(l['texto'].split()) for l in dial) / (contenido / 60))
        hablado = sum(l['fin'] - l['ini'] for l in dial)
        r['hablado_pct'] = round(hablado / contenido * 100)
        r['narrador_lineas'] = len(narr)
        r['narrador_pct'] = round(sum(l['fin'] - l['ini'] for l in narr) / contenido * 100, 1)
        # silencio mas largo sin dialogo (escenas de accion o atmosfera)
        t = sorted(dial, key=lambda l: l['ini'])
        huecos = [t[i + 1]['ini'] - t[i]['fin'] for i in range(len(t) - 1)]
        r['hueco_max'] = round(max(huecos)) if huecos else 0
        r['huecos_20s'] = sum(1 for h in huecos if h >= 20)
    # musica
    bg = [m for m in musica if not re.search(r'opening|ending|next episode', m['desc'], re.I)]
    if bg and contenido > 60:
        r['temas'] = len(bg)
        r['musica_pct'] = round(sum(max(0, m['fin'] - m['ini']) for m in bg) / contenido * 100)
        r['tema_mediana'] = round(statistics.median([max(0, m['fin'] - m['ini']) for m in bg]))
    # ultimas lineas antes del ED: el cliffhanger
    corte = r['ed'][0] if 'ed' in r else (r['avance'][0] if 'avance' in r else dur)
    final = [l['texto'] for l in dial if corte - 45 <= l['ini'] < corte][-4:]
    if final: r['final'] = final
    return r

def main():
    carpeta = sys.argv[1] if len(sys.argv) > 1 else 'ejemplos/subs'
    salida = sys.argv[2] if len(sys.argv) > 2 else None
    musica = {}
    for f in os.listdir(carpeta):
        if f.endswith('.md'):
            t = temporada_md(f)
            if t: musica[t] = leer_musica(os.path.join(carpeta, f))
    eps = {}
    for f in sorted(os.listdir(carpeta)):
        if 'Forzados' in f or not f.endswith(('.ass', '.srt')):
            continue
        m = re.search(r'S(\d\d)E(\d\d)', f)
        if not m:
            continue
        clave = 'S%sE%s' % m.groups()
        ruta = os.path.join(carpeta, f)
        lineas = leer_ass(ruta) if f.endswith('.ass') else leer_srt(ruta)
        mus = musica.get('S' + m.group(1), {}).get(int(m.group(2)), [])
        eps[clave] = analizar(lineas, mus)
        eps[clave]['temporada'] = TEMPORADAS.get('S' + m.group(1), '?')
    res = {'episodios': eps, 'resumen': {}}
    for temp in sorted(set(e['temporada'] for e in eps.values())):
        lst = [e for e in eps.values() if e['temporada'] == temp]
        def med(k, f=lambda x: x):
            v = [f(e[k]) for e in lst if k in e]
            return round(statistics.median(v), 1) if v else None
        res['resumen'][temp] = {
            'episodios': len(lst),
            'duracion': med('duracion'), 'cold_open': med('cold_open'),
            'op_dur': med('op', lambda x: x[1] - x[0]), 'ed_dur': med('ed', lambda x: x[1] - x[0]),
            'eyecatch': med('eyecatch'), 'eyecatch_frac': med('eyecatch', lambda x: x) and round(statistics.median(
                [e['eyecatch'] / e['duracion'] for e in lst if 'eyecatch' in e]), 2),
            'avance_dur': med('avance', lambda x: x[1] - x[0]),
            'con_recap': sum(1 for e in lst if 'recap' in e),
            'lineas_min': med('lineas_min'), 'palabras_min': med('palabras_min'), 'hablado_pct': med('hablado_pct'),
            'narrador_pct': med('narrador_pct'), 'hueco_max': med('hueco_max'), 'huecos_20s': med('huecos_20s'),
            'temas': med('temas'), 'musica_pct': med('musica_pct'), 'tema_mediana': med('tema_mediana'),
        }
    texto = json.dumps(res, ensure_ascii=False, indent=1)
    if salida:
        open(salida, 'w', encoding='utf-8').write(texto)
    print(json.dumps(res['resumen'], ensure_ascii=False, indent=1))

if __name__ == '__main__':
    main()
