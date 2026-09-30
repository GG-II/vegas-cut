# Genera los WAV de prueba: voz sintetica (tono modulado a -20 dB) y ruido de fondo a -60 dB.
import math, random, struct
random.seed(1)
sr = 44100
plan = [('s',1.0),('v',2.0),('s',0.3),('v',1.5),('s',1.2),('v',0.05),('s',1.0),('v',2.5),('s',3.0),('v',1.0),('s',0.6),('v',2.0),('s',2.0)]
x = []
nf = 10 ** (-60 / 20)
for k, d in plan:
    for i in range(int(sr * d)):
        v = random.gauss(0, nf)
        if k == 'v':
            t = i / sr
            v += 0.25 * math.sin(2 * math.pi * 180 * t) * (0.6 + 0.4 * math.sin(2 * math.pi * 3 * t))
        x.append(v)

def escribir(nombre, fmt, bits, conv):
    datos = bytearray()
    for v in x:
        s = conv(max(-1, min(1, v)))
        datos += s + s
    f = struct.pack('<HHIIHH', fmt, 2, sr, sr * 2 * bits // 8, 2 * bits // 8, bits)
    with open(nombre, 'wb') as o:
        o.write(b'RIFF' + struct.pack('<I', 4 + 8 + len(f) + 8 + len(datos)) + b'WAVE' +
                b'fmt ' + struct.pack('<I', len(f)) + f + b'data' + struct.pack('<I', len(datos)) + bytes(datos))

escribir('voz16.wav', 1, 16, lambda v: struct.pack('<h', int(v * 32767)))
escribir('voz24.wav', 1, 24, lambda v: struct.pack('<i', int(v * 8388607))[:3])
escribir('vozf32.wav', 3, 32, lambda v: struct.pack('<f', v))
