#!/usr/bin/env python3
"""Indice de la biblioteca de musica a partir de como se usa cada tema en el anime.

Empareja cada archivo del listado (musica.csv, sacado con PowerShell) con su tema
en las listas de musica por episodio de jojowiki (ejemplos/subs/*.md). Con las
escenas donde suena cada tema le pone estados de animo (pelea, tension,
victoria, viaje...), la parte del episodio donde suele ir y si es tema de un
personaje. Los archivos sin datos de uso se clasifican por su titulo.

Uso:  python3 herramientas/indexar_musica.py ejemplos/musica/musica.csv ejemplos/subs [salida.json]
"""
import csv, difflib, json, os, re, statistics, sys, unicodedata
from collections import Counter, defaultdict

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import analizar_anime as aa

# ------------------------------------------------------------------ utilidades

def norm(t):
    t = unicodedata.normalize('NFKD', t or '').encode('ascii', 'ignore').decode().lower()
    t = re.sub(r'^\s*\d{1,3}[\s.\-_]+', '', t)            # "03. Titulo"
    t = re.sub(r'\(.*?\)|\[.*?\]|~.*?~', ' ', t)
    t = re.sub(r'[^a-z0-9 ]', ' ', t)
    return re.sub(r'\s+', ' ', t).strip()

def segundos(d):
    try:
        p = [int(x) for x in d.split(':')]
        s = 0
        for x in p: s = s * 60 + x
        return s
    except Exception:
        return 0

# De que obra viene un album (para desempatar y para filtrar)
FUENTES = [
    (r'stardust crusaders', 'SC'), (r'golden wind|vento aureo|gioGio', 'GW'), (r'diamond is unbreakable|morioh', 'DU'),
    (r'stone ocean', 'SO'), (r'phantom blood.*o\.?s\.?t|battle tendency', 'PB/BT'), (r'steel ball run|gwinn', 'SBR fan'),
    (r'all star battle|eyes of heaven|ora ora overdrive|heritage for the future|rpg|stardust shooters|video game|diamond records|ps3', 'videojuego'),
    (r'\bova\b|2000', 'OVA'), (r'anthology|op\d? single|theme song|opening|ending', 'canción'),
]

def fuente(album, ruta):
    t = (album + ' ' + ruta).lower()
    if 'vento aureo soundtrack' in t: return 'videojuego'
    for pat, f in FUENTES:
        if re.search(pat, t, re.I): return f
    return 'otro'

# Album de jojowiki ("Stardust Crusaders (Departure)") -> palabra clave del album del archivo
def clave_ost(ost):
    o = ost.lower()
    for k in ['departure', 'journey', 'world', 'destination', 'overture', 'intermezzo', 'finale', 'good morning', 'good night',
              'future', 'destiny', 'musik', 'leicht', 'stone ocean']:
        if k in o: return k
    return ''

PARTE = {'SC': 'SC', 'GW': 'GW', 'DU': 'DU', 'SO': 'SO', 'PB/BT': 'PB/BT'}

# Revisados a mano (SC y Golden Wind) leyendo las escenas donde suenan.
MANUAL = {
    'crepuscolo': ['misterio', 'tristeza'], 'passione': ['villano', 'tension'], 'attacco': ['pelea'], 'pace': ['calma', 'epico'],
    'male': ['tension', 'villano'], 'ristorante bar': ['comedia', 'calma'], 'aereo da caccia': ['pelea'], 'legame': ['epico', 'victoria'],
    'un sogno': ['epico', 'calma'], 'meraviglia': ['misterio', 'calma'], 'coercizione': ['tension', 'villano'], 'suspense': ['tension', 'misterio'],
    'spiritoso': ['comedia'], 'specchio': ['pelea', 'misterio'], 'di molto': ['villano', 'tension'], 'nervoso': ['tension'],
    'ghiaccio': ['pelea', 'villano'], 'vita': ['epico', 'victoria'], 'morte': ['tristeza'], 'dominazione': ['tension', 'misterio'],
    'carne': ['villano', 'pelea'], 'la battaglia finale': ['pelea', 'epico', 'villano'], 'permanenza': ['misterio', 'tristeza'],
    'ascensione': ['tristeza', 'epico'], 'assassinio': ['villano', 'tension'], 'teso': ['tension'],
    'determinazione': ['epico', 'pelea'], 'figlia': ['pelea'], 'canzoni preferite': ['comedia'], "il vento d'oro": ['epico'], 'tensione': ['tension'], 'misterioso': ['misterio'],
    'situazione difficile': ['pelea', 'tension'], 'squadra': ['pelea', 'villano'], 'sventura': ['villano', 'tristeza'],
    'pensare': ['tristeza', 'calma'], 'doppio': ['villano', 'misterio'], 'guardia': ['villano', 'misterio'],
    'The Magician of Fire': ['viaje', 'epico'], 'Noble Hierophant': ['victoria', 'pelea'], 'Hidden Thoughts': ['tristeza', 'calma'],
    'Sadness': ['tristeza'], 'Stardust Crusaders': ['victoria', 'epico'], 'Egypt Landing': ['comedia', 'misterio'],
    'The Curtain Rises': ['tension', 'villano'], 'Nightmare World': ['misterio'], 'Mad Dash': ['pelea', 'tension'],
    'Life-and-Death Matter': ['tension', 'epico'], 'Requiem': ['tristeza'], 'A Message to My Friends': ['tristeza', 'epico'],
    'Final Battle': ['pelea', 'epico'], 'Fear': ['villano', 'misterio'], 'Barbarism': ['tension', 'pelea'],
    'Dizziness': ['tension', 'misterio'], 'Fascination': ['misterio', 'tension'], 'Fun Friends': ['comedia', 'viaje'],
    'Sword Attack': ['pelea'], 'The Scheme': ['tension', 'misterio'], "The Prophecy That's Never Wrong": ['comedia', 'misterio'],
    'Uneasiness': ['tension', 'misterio'], 'Fists of Platinum': ['epico', 'pelea'], 'Calm Sightseeing': ['viaje', 'calma'],
    'Setting Off': ['viaje', 'calma'], 'Omen': ['tension', 'misterio'], 'Powerful Enemy': ['villano', 'tension'],
    'Dark Rebirth': ['villano'], 'Cheerful Journey': ['viaje', 'comedia'], 'Gentle Sunlight': ['calma'],
    'Wind in the Wilderness': ['viaje', 'epico'], 'Purple Thorns': ['viaje', 'explicacion'], 'Approach': ['tension'],
    'Hesitation': ['tristeza', 'calma'], 'Determination': ['pelea', 'epico'], 'The Travelers Rest': ['calma', 'viaje'],
    'The Travelers Return': ['viaje', 'tristeza'], 'Creeping Enemy': ['tension', 'villano'], 'Urgency': ['tension'],
    'The Battle Begins': ['pelea', 'tension'], 'Unfolding Crisis': ['tension', 'pelea'], 'Battle Between Equals': ['pelea'],
    'Close Match': ['pelea'], 'Throw': ['pelea', 'victoria'], 'Strange and Mysterious': ['misterio'],
}

# Mismo tema con otra traduccion (titulo del archivo -> titulo en jojowiki).
ALIAS = {
    # Stardust Crusaders
    'fire shaman': 'The Magician of Fire', 'noble pope': 'Noble Hierophant', 'imminence': 'Urgency',
    'increasing strength': 'Increasing Power', 'brutality': 'Barbarism', 'bizarre': 'Strange and Mysterious',
    'conspiracy': 'The Scheme', 'nightmare spell': 'Curse of Nightmares', 'head to head': 'Battle Between Equals',
    'fight to antagonize': 'Close Match', 'ken': 'Sword Attack', 'the off unexpected prophecy': "The Prophecy That's Never Wrong",
    'the kakero the bluff': 'Bet on a Bluff', 'blow throwing reverse play': 'Batting, Pitching, Turning the Tables',
    'the battle starts': 'The Battle Begins', 'rampage': 'Mad Dash', 'apparent crisis': 'Unfolding Crisis',
    'rhapsody of brothers': "Brothers' Rhapsody", 'awakening darkness of the world': "Darkness of The World's Awakening",
    'the return of travelers': 'The Travelers Return',
    # Golden Wind (Intermezzo: el archivo en ingles, jojowiki en italiano)
    'wonder': 'meraviglia', 'serenely': 'serenamente', 'witty': 'spiritoso', 'small': 'piccolo', 'mirror': 'specchio',
    'showdown': 'resa dei conti', 'growing old': 'invecchiare', 'fish': 'pesce', 'extremely': 'di molto', 'ice': 'ghiaccio',
    'the darkness': "l'oscurita", 'life': 'vita', 'another person': "un'altra persona", 'fierce fight': 'lotta feroce',
    'death': 'morte', 'tense': 'teso',
}

# ----------------------------------------------------------- estados de animo

ANIMOS = {
    'pelea': r'\bfight|attack|battle|punch|clash|duel|\bvs\b|beat(s|ing)? (up|down)|ora|muda|barrage|strikes?|shoot|stab|kick|chase|struggle|counter',
    'victoria': r'defeat(s|ed)? |wins?\b|victory|triumph|finish(es)? off|beaten|retire|is defeated|overcomes|saves',
    'tension': r'danger|threat|trap|realiz|approach|corner|ambush|stalk|hunt|crisis|surround|pursu|trouble|panic|cornered|desperate|in peril|caught',
    'villano': r'\bdio\b|enemy|villain|assassin|killer|diavolo|boss|pucci|kira|enya|vanilla|cioccolata|hitman|hit squad|la squadra',
    'misterio': r'mysterious|strange|investigat|discover|who is|identity|clue|suspicio|search|wonder|secret|reveals? (that|his)|learns? (that|about)',
    'revelacion': r'stand (appears|is revealed)|reveals? (his|her|the) stand|shows (his|her) stand|awaken|manifest|unleash|true power|transformation|power of',
    'comedia': r'joke|laugh|funny|comedic|embarrass|argu|teas|silly|antics|prank|flirt|annoy|gag|dog|iggy',
    'viaje': r'sightseeing|arriv|travel|journey|depart|\broad|desert|\bsea\b|ship|boat|plane|train\b|\bcar\b|drive|hotel|camp|setting off|cross(es|ing)? the|border|leave for|head(s|ing)? (to|for)',
    'calma': r'\brest|calm|relax|peaceful|meal|\beat|breakfast|sleep|bath|shopping|chat\b|lunch|dinner|morning',
    'explicacion': r'explain|explanation|describes|how .* works|ability|stand power|analy[sz]|deduc|figures? out|theor',
    'tristeza': r'dies|death|dead|mourn|sorrow|cr(y|ies)|grave|farewell|funeral|sacrific|memor|remember|flashback|past|lament|sad',
    'epico': r'final|decisive|climax|last|ultimate|requiem|time stop|za warudo|the world|stops time|full power|charge',
}
POR_TITULO = {
    'pelea': r'battle|fight|clash|duel|assault|attack|vs|fist|rush|showdown|combat',
    'tension': r'tension|imminen|crisis|danger|threat|pursuit|approach|creeping|urgency|omen|foreboding|unease|anxiety|chase',
    'villano': r'dio|evil|dark|devil|villain|boss|kira|diavolo|pucci|killer|enemy|rebirth|malice|sinister',
    'misterio': r'myster|strange|bizarre|enigma|secret|plot|mist|unknown|question|riddle|misterioso',
    'comedia': r'comic|funny|jolly|silly|comical|humor|playful|cheer',
    'viaje': r'journey|travel|departure|sightseeing|wilderness|road|desert|wind|voyage|setting off|ride|horse|run',
    'calma': r'calm|rest|peace|gentle|repose|daily|morning|sunlight|quiet|serene|night',
    'tristeza': r'sad|sorrow|tears|requiem|farewell|grief|lament|memory|memories|hesitation|loneliness|repose of souls',
    'victoria': r'victory|triumph|glory|hero|pride|proud|win',
    'epico': r'theme|crusaders|stardust|golden|giorno|decisive|final|vento|oro|awakening|platinum|fate|destiny',
}

PERSONAJES = ['jotaro', 'joseph', 'avdol', 'kakyoin', 'polnareff', 'iggy', 'dio', 'giorno', 'bucciarati', 'bruno', 'mista', 'narancia',
              'fugo', 'abbacchio', 'trish', 'diavolo', 'doppio', 'josuke', 'okuyasu', 'koichi', 'rohan', 'kira', 'jolyne', 'pucci',
              'johnny', 'gyro', 'diego', 'valentine', 'holy', 'enya', 'hol horse', 'oingo', 'boingo']

def momento(m, dur):
    d = m['desc'].lower()
    if re.search(r'\bopening\b', d): return 'opening'
    if re.search(r'\bending\b', d): return 'ending'
    if 'next episode' in d: return 'avance'
    if 'eyecatch' in d: return 'eyecatch'
    if 'recap' in d: return 'recap'
    if not dur: return 'medio'
    f = m['ini'] / dur
    return 'inicio' if f < 0.15 else 'final' if f > 0.8 else 'medio'

# ---------------------------------------------------------------------- main

def main():
    csv_ruta = sys.argv[1] if len(sys.argv) > 1 else 'ejemplos/musica/musica.csv'
    subs = sys.argv[2] if len(sys.argv) > 2 else 'ejemplos/subs'
    salida = sys.argv[3] if len(sys.argv) > 3 else os.path.join(os.path.dirname(csv_ruta), 'musica-indice.json')

    # Usos de cada tema en el anime
    usos = defaultdict(list)          # titulo normalizado -> [uso]
    nombres, osts = {}, {}
    for f in os.listdir(subs):
        if not f.endswith('.md'): continue
        temp = aa.temporada_md(f)
        if not temp: continue
        for ep, ms in aa.leer_musica(os.path.join(subs, f)).items():
            dur = max([m['fin'] for m in ms] + [0])
            for m in ms:
                if not m['titulo']: continue
                n = norm(m['titulo'])
                if not n: continue
                nombres.setdefault(n, m['titulo'])
                osts.setdefault(n, m['ost'])
                usos[n].append({'temporada': aa.TEMPORADAS.get(temp, temp), 'ep': ep, 'ini': m['ini'],
                                'largo': max(0, m['fin'] - m['ini']), 'desc': m['desc'], 'momento': momento(m, dur)})
    titulos = list(usos.keys())

    filas = list(csv.DictReader(open(csv_ruta, encoding='utf-8-sig')))
    indice, sin = [], []
    for r in filas:
        titulo = r['Titulo'] or r['Archivo']
        album = r['Album'] or ''
        src = fuente(album, r['Ruta'])
        n = norm(titulo)
        mejor, puntaje = None, 0
        if src in ('SC', 'GW') and n in ALIAS and norm(ALIAS[n]) in usos:
            mejor, puntaje = norm(ALIAS[n]), 1
        elif src in ('SC', 'GW', 'DU', 'SO', 'PB/BT', 'canción', 'otro') and n:
            for t in titulos:
                s = difflib.SequenceMatcher(None, n, t).ratio()
                k = clave_ost(osts.get(t, ''))
                if k and k in album.lower(): s += 0.15
                if s > puntaje: mejor, puntaje = t, s
            if puntaje < 0.86: mejor = None
        e = {'ruta': r['Ruta'], 'titulo': titulo, 'album': album, 'duracion': segundos(r['Duracion']), 'fuente': src}
        animos = Counter()
        if mejor:
            u = usos[mejor]
            propios = [x for x in u if x['temporada'] == PARTE.get(src)]
            if propios: u = propios          # el mismo nombre puede ser otro tema en otra parte
            e['tema_anime'] = nombres[mejor]
            e['usos'] = len(u)
            e['temporadas'] = sorted(set(x['temporada'] for x in u))
            e['largo_tipico'] = round(statistics.median([x['largo'] for x in u]))
            e['momentos'] = dict(Counter(x['momento'] for x in u).most_common())
            for x in u:
                for a, pat in ANIMOS.items():
                    if re.search(pat, x['desc'], re.I): animos[a] += 1
            gente = Counter()
            for x in u:
                for p in PERSONAJES:
                    if re.search(r'\b' + p + r'\b', x['desc'], re.I): gente[p] += 1
            tema_de = [p for p, c in gente.most_common(2) if c >= 3 and c >= 0.4 * len(u)]
            if tema_de: e['tema_de'] = tema_de
            e['escenas'] = [x['temporada'] + ' ' + str(x['ep']) + ': ' + x['desc'] for x in u[:6]]
        # Cada uso pesa 1/usos (que un tema muy usado no aplaste al titulo).
        if mejor:
            n_usos = max(1, e['usos'])
            for a in list(animos): animos[a] = animos[a] * 6.0 / n_usos
        for a, pat in POR_TITULO.items():
            if re.search(pat, titulo, re.I): animos[a] += 3
        total = sum(animos.values())
        manual = {norm(k): v for k, v in MANUAL.items()}
        if mejor and mejor in manual:
            e['animos'] = manual[mejor]
            e['revisado'] = True
            indice.append(e)
            continue
        e['animos'] = [a for a, c in animos.most_common(3) if total and c >= 0.25 * total and c >= 1] or ['sin clasificar']
        indice.append(e)
        if not mejor and src in ('SC', 'GW', 'DU', 'SO'): sin.append(titulo + ' (' + album + ')')

    with open(salida, 'w', encoding='utf-8') as f:
        json.dump({'formato': 'vegas-cut-musica', 'archivos': indice}, f, ensure_ascii=False, indent=1)

    con = [e for e in indice if 'usos' in e]
    print('%d archivos, %d con datos de uso en el anime' % (len(indice), len(con)))
    print(Counter(e['fuente'] for e in indice).most_common())
    print('del anime sin emparejar (%d):' % len(sin))
    for x in sin: print('  ', x)



# ------------------------------------------------------------ guia en markdown

NOMBRES = {'viaje': 'Viaje y desierto', 'calma': 'Calma', 'comedia': 'Comedia', 'tension': 'Tensión', 'misterio': 'Misterio',
           'villano': 'Villano / enemigo', 'pelea': 'Pelea', 'epico': 'Épico', 'victoria': 'Victoria', 'tristeza': 'Tristeza',
           'explicacion': 'Explicación'}

def guia(indice_ruta, salida, fuentes=('SC', 'GW')):
    idx = json.load(open(indice_ruta, encoding='utf-8'))['archivos']
    vistos, sel = set(), []
    for e in idx:
        if e['fuente'] in fuentes and 'usos' in e and e['ruta'] not in vistos:
            vistos.add(e['ruta']); sel.append(e)
    l = ['# Música de Stardust Crusaders y Golden Wind para SCR', '',
         'Generado con `herramientas/indexar_musica.py` desde tu biblioteca (`musica.csv`) y las escenas donde suena cada',
         'tema en el anime (jojowiki). **Usos** = escenas en el anime; **dónde** = parte del episodio donde más suena.', '']
    for animo in ['viaje', 'calma', 'comedia', 'misterio', 'tension', 'villano', 'pelea', 'epico', 'victoria', 'tristeza', 'explicacion']:
        g = sorted([e for e in sel if e['animos'] and e['animos'][0] == animo], key=lambda e: -e['usos'])
        if not g: continue
        l += ['## ' + NOMBRES[animo], '', '| Tema | Archivo | Usos | Dónde | También | Escenas |', '|---|---|---|---|---|---|']
        for e in g:
            m = e.get('momentos', {})
            donde = max(m, key=m.get) if m else ''
            esc = '; '.join(x.split(': ', 1)[1] for x in e.get('escenas', [])[:3]).replace('|', '/')
            extra = ', '.join(e['animos'][1:]) + ((' · tema de ' + ', '.join(e['tema_de'])) if e.get('tema_de') else '')
            l.append('| %s | `%s` | %d | %s | %s | %s |' % (e['tema_anime'], e['ruta'].split('\\')[-1], e['usos'], donde, extra, esc))
        l.append('')
    open(salida, 'w', encoding='utf-8').write('\n'.join(l))


if __name__ == '__main__':
    main()
    if len(sys.argv) > 4:
        guia(sys.argv[3], sys.argv[4])
