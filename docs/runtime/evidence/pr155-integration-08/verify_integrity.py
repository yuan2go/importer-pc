import hashlib
import json
import subprocess
from pathlib import Path

def git(*args):
    return subprocess.check_output(['git', *args])

root = Path.cwd()
out = root / '.artifacts/pr155-integration-08'
head = git('rev-parse', 'HEAD').decode().strip()
sources = {'155': '9eb90018f5683b3b7e63abf7b00b275169ec6c61',
           '156': '219fd5dfff4a5c23578abe52f407e08cc6fec05e',
           '158': '0ce83993ede9d96735690126b7ffc70430e1985c'}
proof = {'SourceCommit': head, 'parents': sources, 'preserved_production_files': [], 'archives': []}
for pr in ('155', '156'):
    base = '9df3901ec20d73512d48920b4671b529ebc58a9e' if pr == '155' else 'f870ffc00abcba78497b3d01d83251d786052ae3'
    for name in git('diff', '--name-only', base, sources[pr], '--', 'src').decode().splitlines():
        expected = git('rev-parse', sources[pr] + ':' + name).decode().strip()
        actual = git('rev-parse', head + ':' + name).decode().strip()
        assert actual == expected, (pr, name, expected, actual)
        proof['preserved_production_files'].append({'pr': int(pr), 'path': name, 'git_blob': actual})
print('PASS exact production blobs preserved:', len(proof['preserved_production_files']))
archive = root / 'docs/archive/pre-mvs-latency-01'
manifest = json.loads((archive / 'manifest.json').read_text(encoding='utf-8'))
assert git('rev-parse', manifest['FirstChangingCommit'] + '^').decode().strip() == manifest['Files'][0]['SourceCommit']
checkout = out / 'autocrlf-true-checkout'
checkout.mkdir(exist_ok=True)
paths = ['docs/archive/pre-mvs-latency-01/' + row['Snapshot'] for row in manifest['Files']]
git('-c', 'core.autocrlf=true', 'checkout-index', '--prefix=' + checkout.as_posix() + '/', '--', *paths)
for row, name in zip(manifest['Files'], paths):
    original = git('cat-file', 'blob', row['SourceCommit'] + ':' + row['OriginalPath'])
    canonical = git('cat-file', 'blob', head + ':' + name)
    working = (root / name).read_bytes()
    independent = (checkout / name).read_bytes()
    assert original == canonical == working == independent, name
    assert len(original) == row['SizeBytes']
    assert hashlib.sha256(original).hexdigest() == row['Sha256']
    assert git('rev-parse', head + ':' + name).decode().strip() == row['GitBlob']
    proof['archives'].append(row)
print('PASS two original archives: parent relation, Git blobs, bytes, SHA256, length, independent autocrlf=true checkout')
for name in ('docs/runtime/mvs-software-reader-01.md', 'docs/runtime/mvs-latency-01.md'):
    assert git('cat-file', 'blob', head + ':' + name) == git('cat-file', 'blob', sources['155'] + ':' + name), name
print('PASS current MVS guidance and latency report unchanged')
evidence = root / 'docs/runtime/evidence/sm-debug-closure-07'
rows = json.loads((evidence / 'evidence-sha256.json').read_text(encoding='utf-8'))['files']
for row in rows:
    raw = (evidence / row['path']).read_bytes()
    assert len(raw) == row['bytes'] and hashlib.sha256(raw).hexdigest() == row['sha256'], row['path']
proof['closure07_evidence_files'] = len(rows)
print('PASS closure07 evidence exact hashes:', len(rows))
program = (root / 'tests/Ipc.Application.Verification/Program.cs').read_text(encoding='utf-8-sig')
for command in ('--camera-rounds', '--installer-storage-checks', '--installer-storage-matrix'):
    assert program.count('args.Contains("' + command + '")') == 1, command
print('PASS merged test dispatch retains both feature suites')
(out / 'integrity.json').write_text(json.dumps(proof, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
