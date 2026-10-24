"""Reproduce the bundled font coverage inventory (development-only fontTools).

This is a source-character superset, not proof of arbitrary external-log coverage.
WPF GlyphRun evidence separately verifies actual rendering and fallback.
"""
import hashlib
import json
from pathlib import Path
import subprocess
import sys

from fontTools.ttLib import TTFont

root = Path(__file__).resolve().parents[1]
sample = "供包台 当前格口 包裹 异常 参数 保存核对 回执未包含 I l 1 | O 0 8 B S 5 Z 2 0123456789 ，。；：！？（）【】－·…全角ＡＢＣ０１２"
paths = sorted(p for p in (root / "src").rglob("*") if p.suffix in (".cs", ".xaml") and not {"bin", "obj"}.intersection(p.parts))
characters = set(sample)
for path in paths:
    characters.update(c for c in path.read_text(encoding="utf-8-sig") if "\u3400" <= c <= "\u9fff")
characters.discard(" ")
characters = sorted(characters, key=ord)
rows = []
for path in sorted((root / "src/Ipc.Presentation.Wpf/Fonts").glob("*.otf")):
    font = TTFont(path)
    cmap = font.getBestCmap()
    rows.append({"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest(),
                 "weight": font["OS/2"].usWeightClass, "version": font["name"].getDebugName(5),
                 "missing": [c for c in characters if ord(c) not in cmap],
                 "rocketAbsentForNativeFallbackProbe": 0x1F680 not in cmap})
result = {"SourceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=root, text=True).strip(),
          "Scope": "CJK source-character superset plus explicit mixed-script sample; comments included; external logs unbounded",
          "Sample": sample, "SourceFileCount": len(paths), "Characters": "".join(characters),
          "Codepoints": [f"U+{ord(c):04X}" for c in characters], "Fonts": rows}
target = Path(sys.argv[1])
target.parent.mkdir(parents=True, exist_ok=True)
target.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
print(f"{len(characters)} explicit characters across {len(paths)} source files; missing: {[r['missing'] for r in rows]}")
sys.exit(1 if any(r["missing"] for r in rows) else 0)
