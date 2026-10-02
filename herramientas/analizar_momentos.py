#!/usr/bin/env python3
"""Momentos clave de cada episodio (enemigo, stand, explicacion, crisis, giro,
derrota, viaje...) a partir de las descripciones de escena de jojowiki, que
traen su minuto. Da la posicion tipica de cada momento (en % del episodio).

Uso:  python3 herramientas/analizar_momentos.py ejemplos/subs [temporada, p. ej. SC]
"""
import os, re, statistics, sys
from collections import Counter, defaultdict
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analizar_anime as aa

MOMENTOS = [
    ('viaje', r'arriv|depart|travel|setting off|sightseeing|board(s|ed)? |land(s|ed|ing)? |head(s|ing)? (to|for|toward)|the heroes (go|leave|cross|reach|continue)|on the road|desert|by car|by train|by boat'),
    ('enemigo', r'appears?\b|shows? up|reveals? himself|reveals? herself|introduc|is an enemy|enemy stand user|assassin|attacks? the heroes|ambush'),
    ('stand', r'the \w+ card|stand (appears|is revealed|revealed)|reveals? (his|her|its) stand|summons|manifest'),
    ('explicacion', r'explain|explanation|how .* works|ability|power is|figures? out how|deduc|analy[sz]'),
    ('pelea', r'fight|attack|battle|clash|duel|\bvs\.?\b|punch|pummel|ora|muda|chase|struggle'),
    ('crisis', r'cornered|trapped|in danger|danger|crisis|despair|can.t|losing|about to (die|be)|poison|wounded|injur|captured|caught|helpless|overwhelm|desperate'),
    ('giro', r'realiz|figures? out|trick|plan\b|turns? the tables|counter|the real|reveals? the truth|outsmart|bluff|actually|it was'),
    ('derrota', r'defeat|finish(es)? off|retire|beaten|beats? (him|her|the)|wins?\b|victory|is defeated|punish|ORA ?ORA|blown away'),
    ('humor', r'joke|laugh|funny|comedic|embarrass|argu|teas|silly|flirt|annoy|prank|antics'),
    ('drama', r'dies|death|mourn|sorrow|cr(y|ies)|grave|farewell|funeral|sacrific|flashback|past|memor|backstory'),
    ('gancho', r'next episode|new enemy|is watching|watches|another (enemy|assassin)|meanwhile|DIO (watches|learns|sends)|to be continued'),
]

def main():
    carpeta = sys.argv[1] if len(sys.argv) > 1 else 'ejemplos/subs'
    temporada = sys.argv[2] if len(sys.argv) > 2 else 'SC'
    clave = [k for k, v in aa.TEMPORADAS.items() if v == temporada][0]
    md = [f for f in os.listdir(carpeta) if f.endswith('.md') and aa.temporada_md(f) == clave][0]
    eps = aa.leer_musica(os.path.join(carpeta, md))
    pos = defaultdict(list)          # momento -> posiciones (0..1) de la primera aparicion
    todas = defaultdict(list)        # momento -> todas las posiciones
    secuencias = []
    for ep, ms in sorted(eps.items()):
        op = next((m for m in ms if re.search(r'\bopening\b', m['desc'], re.I)), None)
        ed = next((m for m in ms if re.search(r'\bending\b', m['desc'], re.I) and 'placeholder' not in m['desc'].lower()), None)
        ini = op['fin'] if op and op['ini'] < 400 else 0
        fin = ed['ini'] if ed else max(m['fin'] for m in ms)
        if fin - ini < 600: continue
        vistos, seq = set(), []
        for m in ms:
            d = m['desc']
            if re.search(r'opening|ending|next episode|recap|eyecatch', d, re.I): continue
            t = (m['ini'] - ini) / (fin - ini)
            if t < 0 or t > 1: continue
            for nombre, pat in MOMENTOS:
                if re.search(pat, d, re.I):
                    todas[nombre].append(t)
                    if nombre not in vistos:
                        vistos.add(nombre); pos[nombre].append(t); seq.append(nombre)
        secuencias.append(seq)
    n = len(secuencias)
    print('Temporada %s: %d episodios con datos' % (temporada, n))
    print('%-12s %8s %10s %10s %10s' % ('momento', 'en', '1a vez', 'cuartil1', 'cuartil3'))
    filas = []
    for nombre, _ in MOMENTOS:
        p = pos.get(nombre, [])
        if not p: continue
        q = statistics.quantiles(p, n=4) if len(p) >= 4 else [min(p), 0, max(p)]
        filas.append((statistics.median(p), nombre))
        print('%-12s %7d%% %9d%% %9d%% %9d%%' % (nombre, round(len(p) / n * 100), round(statistics.median(p) * 100), round(q[0] * 100), round(q[2] * 100)))
    print('\\nOrden típico (mediana de la primera aparición):', ' → '.join(x[1] for x in sorted(filas)))
    # Densidad por tramo del episodio
    print('\\nEn qué parte del episodio cae cada momento (todas las apariciones, por quintos):')
    for nombre, _ in MOMENTOS:
        v = todas.get(nombre, [])
        if not v: continue
        c = Counter(min(4, int(x * 5)) for x in v)
        print('%-12s %s' % (nombre, '  '.join('%3d%%' % round(c[i] / len(v) * 100) for i in range(5))))

if __name__ == '__main__':
    main()
