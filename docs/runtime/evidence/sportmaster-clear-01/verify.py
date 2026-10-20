"""Verify this local frozen candidate; synthetic UI evidence is not field acceptance."""
import argparse
import hashlib
import json
from pathlib import Path
import zipfile

p = argparse.ArgumentParser()
p.add_argument("--media", type=Path, required=True)
a = p.parse_args()
here = Path(__file__).resolve().parent
record = json.loads((here / "MEDIA.json").read_text(encoding="utf-8"))
media = a.media.resolve()
sha = lambda path: hashlib.sha256(path.read_bytes()).hexdigest().upper()
assert sha(media / "SHA256SUMS") == record["InventorySha256"]
release = json.loads((media / "app/application-release.json").read_text())
assert release["SourceCommit"] == record["SourceCommit"]
count = 0
for line in (media / "SHA256SUMS").read_text().splitlines():
    digest, relative = line.split("  ", 1)
    path = (media / relative).resolve()
    assert path.is_relative_to(media) and sha(path) == digest.upper(), relative
    count += 1
assert count == record["InventoryCount"]
archive = Path(str(media) + ".zip")
assert archive.stat().st_size == record["ZipBytes"]
assert sha(archive) == record["ZipSha256"]
with zipfile.ZipFile(archive) as z:
    assert z.testzip() is None
result = json.loads((here / "frozen-result.json").read_text(encoding="utf-8-sig"))
assert result["Status"] == "PASS" and result["ExitCode"] == 0
assert result["SourceCommit"] == result["FixtureSourceCommit"] == record["SourceCommit"]
assert sum(line.startswith("PASS ") for line in (here / "frozen-visual.log").read_text().splitlines()) == 100
print(f"PASS frozen source, {count} inventory files, ZIP hash/CRC and 100 native fixture checks")
