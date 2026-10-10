"""make_ico.py: draws app.ico (a watch with a grid of app tiles) for Fit3Manager.exe: PNG-compressed 32, 64 and 256 pixel images from one 32x32 drawing.   python make_ico.py"""
import os, struct, sys, zlib
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'apps'))
import make_icons

m = make_icons.Img()
m.rect(11, 0, 10, 6); m.rect(11, 26, 10, 6)                       # straps
m.rect(6, 4, 20, 24); m.rect(7, 3, 18, 26); m.rect(8, 2, 16, 28)  # rounded body
m.rect(9, 7, 14, 18, 0)                                            # screen
for x, y in ((10, 8), (17, 8), (10, 15), (17, 15)): m.rect(x, y, 5, 6)   # four app tiles
m.rect(17, 15, 5, 6, 0); m.rect(18, 17, 3, 2); m.rect(19, 16, 1, 4)       # the last tile is a plus
TOP, BOT, INK = (18, 163, 154), (54, 89, 214), (255, 255, 255)

def render(size):
    sc = size // 32; rows = []
    for y in range(size):
        row = bytearray([0])
        for x in range(size):
            u = (x + y) / (2.0 * size); r = int(TOP[0] + (BOT[0] - TOP[0]) * u); g = int(TOP[1] + (BOT[1] - TOP[1]) * u); b = int(TOP[2] + (BOT[2] - TOP[2]) * u)
            # rounded square background
            rad = size * 0.22; cx = min(max(x, rad), size - 1 - rad); cy = min(max(y, rad), size - 1 - rad); inside = (x - cx) ** 2 + (y - cy) ** 2 <= rad * rad
            ink = m.p[y // sc][x // sc]
            row += bytes((INK + (255,)) if inside and ink else ((r, g, b, 255) if inside else (0, 0, 0, 0)))
        rows.append(bytes(row))
    def ch(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xffffffff)
    return b'\x89PNG\r\n\x1a\n' + ch(b'IHDR', struct.pack('>IIBBBBB', size, size, 8, 6, 0, 0, 0)) + ch(b'IDAT', zlib.compress(b''.join(rows), 9)) + ch(b'IEND', b'')

sizes = (32, 64, 256); pngs = [render(s) for s in sizes]; off = 6 + 16 * len(sizes); out = struct.pack('<HHH', 0, 1, len(sizes))
for s, p in zip(sizes, pngs):
    out += struct.pack('<BBBBHHII', s % 256, s % 256, 0, 0, 1, 32, len(p), off); off += len(p)
out += b''.join(pngs)
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'app.ico'), 'wb').write(out)
open(os.path.join(os.path.dirname(os.path.abspath(__file__)), 'app_preview.png'), 'wb').write(pngs[2])
print('app.ico', len(out), 'bytes')
