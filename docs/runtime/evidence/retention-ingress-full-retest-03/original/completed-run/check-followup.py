"""Verify committed follow-up bytes against merged PR105 without refreshing history."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess

parser = argparse.ArgumentParser()
parser.add_argument('--head', required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
BASE = 'ed42da8187be08d9a65e067313d3039e2f2e9ebd'
PRODUCT = 'b83998f18c56df2dd965630b25908feedc00a56f'
def git(*options):
    return subprocess.check_output(['git', *options])
def tree(ref):
    result = {}
    for line in git('ls-tree', '-r', '-z', ref).split(b'\0'):
        if line:
            meta, name = line.split(b'\t', 1)
            mode, kind, sha = meta.split()
            assert kind == b'blob'
            result[name.decode()] = sha.decode()
    return result
trees = {ref: tree(ref) for ref in [BASE, PRODUCT, args.head]}
before, after = trees[BASE], trees[args.head]
owned = {path for path in before.keys() | after.keys() if before.get(path) != after.get(path)}
assert not any(path.startswith(('src/', 'config/')) for path in owned)
assert all(after.get(path) == sha for path, sha in trees[PRODUCT].items() if path.startswith(('src/', 'config/')))
mutable = {f'docs/runtime/evidence/retention-ingress-full-retest-03/{name}' for name in ['FILES.json', 'CAPTURE.json', 'README.md']}
historical = {path: sha for path, sha in before.items() if path.startswith('docs/runtime/evidence/') and path not in mutable}
for path, sha in historical.items():
    assert after.get(path) == sha, path
needed = {before['docs/manifest.json'], after['docs/manifest.json'], after['docs/manifest.sha256']}
needed.update(after[path] for path in owned if path not in {'docs/manifest.json', 'docs/manifest.sha256'})
needed.add(before['docs/runtime/evidence/retention-ingress-full-retest-03/FILES.json'])
needed.update(sha for path, sha in after.items() if path.startswith('docs/runtime/evidence/retention-ingress-full-retest-03/'))
order = list(needed)
packed = subprocess.run(['git', 'cat-file', '--batch'], input=('\n'.join(order) + '\n').encode(), stdout=subprocess.PIPE, check=True).stdout
blobs, cursor = {}, 0
for sha in order:
    end = packed.index(b'\n', cursor)
    actual, kind, size = packed[cursor:end].split()
    cursor = end + 1
    assert actual.decode() == sha and kind == b'blob'
    size = int(size)
    blobs[sha] = packed[cursor:cursor + size]
    cursor += size + 1
assert cursor == len(packed)
old = json.loads(blobs[before['docs/manifest.json']])
manifest_bytes = blobs[after['docs/manifest.json']]
manifest = json.loads(manifest_bytes)
rows = {row['path']: row for row in manifest['files']}
assert len(rows) == len(manifest['files'])
owned -= {'docs/manifest.json', 'docs/manifest.sha256'}
for path in owned:
    data = blobs[after[path]]
    row = rows[path]
    assert row['git_blob'] == after[path] and row['sha256'] == hashlib.sha256(data).hexdigest() and row['size_bytes'] == len(data), path
assert [r for r in manifest['files'] if r['path'] not in owned] == [r for r in old['files'] if r['path'] not in owned]
for key, value in old.items():
    if key != 'files':
        assert manifest[key] == value, key
assert blobs[after['docs/manifest.sha256']].decode().split() == [hashlib.sha256(manifest_bytes).hexdigest(), 'manifest.json']
prefix = 'docs/runtime/evidence/retention-ingress-full-retest-03/'
inventory = json.loads(blobs[after[prefix + 'FILES.json']])['Files']
origin = json.loads(blobs[before[prefix + 'FILES.json']])['Files']
by_path = {row['path']: row for row in inventory}
assert len(by_path) == len(inventory)
for row in origin:
    assert by_path[row['path']] == row
for row in inventory:
    data = blobs[after[prefix + row['path']]]
    assert len(data) == row['bytes'] and hashlib.sha256(data).hexdigest() == row['sha256'], row['path']
result = dict(Status='PASS_COMMITTED_FOLLOWUP_IDENTITY', AtUtc=datetime.now(timezone.utc).isoformat(),
              Head=args.head, Main=BASE, FixedProductSource=PRODUCT, SourceAndConfigUnchanged=True,
              OwnedManifestRows=len(owned), HistoricalEvidenceEntriesUnchanged=len(historical),
              OriginalInventoryRowsUnchanged=len(origin), CurrentInventoryFiles=len(inventory),
              UnrelatedManifestRowsAndRootMetadataUnchanged=True,
              Scope='Committed synthetic evidence integrity only; no target IPC, real devices, 24h/72h or field acceptance')
assert not args.output.exists()
args.output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps(result))
