"""Generate the repository-owned geometric product icon (no external artwork/fonts)."""
from pathlib import Path
import struct

root = Path(__file__).resolve().parents[1] / "src/Ipc.Desktop/Assets"
root.mkdir(parents=True, exist_ok=True)
images = []
for size in (16, 32, 48, 64):
    pixels = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            u, v = (x + .5) / size, (y + .5) / size
            color = (20, 39, 61, 255)
            if .18 <= u <= .35 and .22 <= v <= .78: color = (31, 197, 201, 255)
            if .65 <= u <= .82 and .22 <= v <= .78: color = (240, 174, 56, 255)
            if .36 < u < .64 and .45 < v < .55: color = (255, 255, 255, 255)
            pixels.extend((color[2], color[1], color[0], color[3]))
    mask = bytes(((size + 31) // 32) * 4 * size)
    images.append(struct.pack("<IIIHHIIIIII", 40, size, size * 2, 1, 32, 0, len(pixels), 0, 0, 0, 0) + pixels + mask)
offset = 6 + 16 * len(images)
entries = []
for size, data in zip((16, 32, 48, 64), images):
    entries.append(struct.pack("<BBBBHHII", size, size, 0, 0, 1, 32, len(data), offset))
    offset += len(data)
(root / "importer-pc.ico").write_bytes(struct.pack("<HHH", 0, 1, len(images)) + b"".join(entries + images))
(root / "importer-pc.svg").write_text('<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100"><rect width="100" height="100" rx="8" fill="#14273d"/><path fill="#1fc5c9" d="M18 22h17v56H18z"/><path fill="#f0ae38" d="M65 22h17v56H65z"/><path fill="white" d="M36 45h28v10H36z"/></svg>\n', encoding="utf-8")
