"""Generate Windows icon sizes from user-authorized artwork (Pillow, dev only)."""
from pathlib import Path
from PIL import Image

root = Path(__file__).resolve().parents[1] / "src/Ipc.Desktop/Assets"
with Image.open(root / "cainiao.png") as source:
    if source.width != source.height:
        raise ValueError("Product artwork must be square; do not crop it silently")
    source.convert("RGBA").save(
        root / "importer-pc.ico", format="ICO",
        sizes=[(size, size) for size in (16, 20, 24, 32, 40, 48, 64, 128, 256)],
    )
