"""Build a small original play-button icon using only the standard library."""
from pathlib import Path
import struct
size = 64
pixels = bytearray()
for y in range(size - 1, -1, -1):
    for x in range(size):
        inside = 5 <= x < 59 and 5 <= y < 59
        play = 24 <= x <= 44 and abs(y - 32) <= (44 - x) * .8
        r, g, b, a = (104, 215, 196, 255) if play else (24, 38, 42, 255) if inside else (0, 0, 0, 0)
        pixels.extend((b, g, r, a))
mask = bytes(((size + 31) // 32) * 4 * size)
dib = struct.pack('<IIIHHIIIIII', 40, size, size * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0) + pixels + mask
ico = struct.pack('<HHH', 0, 1, 1) + struct.pack('<BBBBHHII', size, size, 0, 0, 1, 32, len(dib), 22) + dib
target = Path(__file__).resolve().parents[1] / 'src/LightMediaPlayer/Assets/player.ico'
target.parent.mkdir(parents=True, exist_ok=True)
target.write_bytes(ico)
