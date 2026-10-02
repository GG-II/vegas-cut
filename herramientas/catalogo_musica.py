#!/usr/bin/env python3
"""Genera src/comun/CatalogoAnime.cs: los temas del anime con como se usan
(estados de animo, parte del episodio, personaje, escenas), para que la
Biblioteca de musica de vegas-cut indexe la carpeta de musica desde Vegas sin
Python. Usa las mismas reglas y revisiones a mano que indexar_musica.py.

Uso:  python3 herramientas/catalogo_musica.py ejemplos/subs
"""
import json, os, re, statistics, sys
from collections import Counter, defaultdict
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analizar_anime as aa
import indexar_musica as im

def main():
    subs = sys.argv[1] if len(sys.argv) > 1 else 'ejemplos/subs'
    raiz = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    usos = defaultdict(list)          # (titulo normalizado, parte) -> usos
    nombres, osts = {}, {}
    for f in os.listdir(subs):
        if not f.endswith('.md'): continue
        temp = aa.temporada_md(f)
        if not temp: continue
        parte = aa.TEMPORADAS.get(temp, temp)
        for ep, ms in aa.leer_musica(os.path.join(subs, f)).items():
            dur = max([m['fin'] for m in ms] + [0])
            for m in ms:
                n = im.norm(m['titulo'])
                if not n: continue
                nombres.setdefault(n, m['titulo'])
                osts.setdefault(n, m['ost'])
                usos[(n, parte)].append({'ep': ep, 'largo': max(0, m['fin'] - m['ini']), 'desc': m['desc'], 'momento': im.momento(m, dur)})
    manual = {im.norm(k): v for k, v in im.MANUAL.items()}
    temas = []
    for (n, parte), u in sorted(usos.items()):
        animos = Counter()
        for x in u:
            for a, pat in im.ANIMOS.items():
                if re.search(pat, x['desc'], re.I): animos[a] += 1
        for a in list(animos): animos[a] = animos[a] * 6.0 / max(1, len(u))
        for a, pat in im.POR_TITULO.items():
            if re.search(pat, nombres[n], re.I): animos[a] += 3
        total = sum(animos.values())
        an = manual.get(n) if parte in ('SC', 'GW') and n in manual else \
            [a for a, c in animos.most_common(3) if total and c >= 0.25 * total and c >= 1]
        gente = Counter()
        for x in u:
            for p in im.PERSONAJES:
                if re.search(r'\b' + p + r'\b', x['desc'], re.I): gente[p] += 1
        temas.append({
            't': nombres[n], 'n': n, 'p': parte, 'o': im.clave_ost(osts.get(n, '')), 'u': len(u),
            'l': round(statistics.median([x['largo'] for x in u])),
            'a': an or [], 'm': dict(Counter(x['momento'] for x in u).most_common()),
            'd': [p for p, c in gente.most_common(2) if c >= 3 and c >= 0.4 * len(u)],
            'e': [x['desc'] for x in u[:4]],
        })
    datos = json.dumps({'temas': temas, 'alias': im.ALIAS}, ensure_ascii=False, separators=(',', ':'))
    trozos = [datos[i:i + 2000] for i in range(0, len(datos), 2000)]
    cs = ['using System;', '', '// GENERADO con herramientas/catalogo_musica.py desde las listas de musica de jojowiki',
          '// (ejemplos/subs): ' + str(len(temas)) + ' temas del anime con como se usan. No editar a mano.',
          'public static class CatalogoAnime', '{', '    public static readonly string Json = String.Concat(new string[] {']
    for t in trozos:
        cs.append('        "' + t.replace('\\', '\\\\').replace('"', '\\"') + '",')
    cs += ['    });', '}', '']
    salida = os.path.join(raiz, 'src', 'comun', 'CatalogoAnime.cs')
    open(salida, 'w', encoding='utf-8').write('\n'.join(cs))
    print('%d temas, %d KB -> %s' % (len(temas), len(datos) // 1024, os.path.relpath(salida, raiz)))

if __name__ == '__main__':
    main()
