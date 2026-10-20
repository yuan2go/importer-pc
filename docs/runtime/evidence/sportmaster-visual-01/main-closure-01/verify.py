"""Verify this scoped closure without rewriting or granting historical qualification."""
from pathlib import Path
import argparse
import hashlib
import importlib.util
import json
import subprocess
import sys

p = argparse.ArgumentParser()
p.add_argument('--staged', action='store_true')
p.add_argument('--media', type=Path, help='Optional actual expanded candidate, next to its original ZIP')
a = p.parse_args()
root = Path(__file__).resolve().parents[5]
prefix = 'docs/runtime/evidence/sportmaster-visual-01/main-closure-01/'
base = 'e0beb5d32c6a24a7799ba698731566bca6a6833a'

def git(*args):
    return subprocess.check_output(['git', '-C', str(root), *args])

def blob(path):
    return git('show', (':' if a.staged else 'HEAD:') + path)

def sha(data):
    return hashlib.sha256(data).hexdigest()

manifest_bytes = blob('docs/manifest.json')
manifest = json.loads(manifest_bytes)
assert blob('docs/manifest.sha256').decode().split()[0] == sha(manifest_bytes)
media = json.loads(blob(prefix+'MEDIA.json'))
source = media['SourceCommit']
git('merge-base', '--is-ancestor', source, 'HEAD')
sys.path.insert(0, str(root/'scripts'))
from release_documents import SOURCE_PATHS
diff_args = ['diff', '--exit-code'] + (['--cached', source] if a.staged else [source, 'HEAD'])
git(*diff_args, '--', *SOURCE_PATHS, 'tests')
# Authority and original source evidence are immutable in this visual refinement.
git('diff', '--exit-code', base, 'HEAD', '--', 'docs/readiness', 'docs/reviews', 'docs/design/framework.md', 'docs/design/framework-decisions.md', 'docs/ui/ss-ui-vis-001-v1.1')
changed = git('diff', '--name-only', base, '--cached' if a.staged else 'HEAD').decode().splitlines()
assert {x for x in changed if x.startswith('src/')} == {'src/Ipc.Application.ParcelFlow.Desktop/DateTimeInput.cs'}
assert not any(x.startswith('docs/runtime/evidence/sportmaster-visual-01/') and not x.startswith(prefix) for x in changed)
records = {r['path']: r for r in manifest['files']}
for path in changed:
    if path in ('docs/manifest.json', 'docs/manifest.sha256'):
        continue
    data = blob(path)
    assert path in records and records[path]['sha256'] == sha(data) and records[path]['size_bytes'] == len(data), path
checks = json.loads(blob(prefix+'checks.json'))
for path, digest in checks.items():
    assert sha(blob(prefix+path)) == digest, path
for case in ('commercial-pages', 'visual'):
    result = json.loads(blob(prefix+case+'/RESULT.json'))
    assert result['Status'] == 'PASS' and result['SourceCommit'] == source
    assert result['MediumInventorySha256'].lower() == media['InventorySha256'].lower()
if a.media:
    spec = importlib.util.spec_from_file_location('release_windows', root/'scripts/release-windows.py')
    release = importlib.util.module_from_spec(spec); spec.loader.exec_module(release)
    actual = release.verify_package(a.media.resolve(), 'sportmaster', 'win-x64', source)
    assert all(actual[k] == media[k] for k in actual)
    assert sha((a.media/'SHA256SUMS').read_bytes()) == media['InventorySha256']
    for case in ('commercial-pages', 'visual'):
        for binary in json.loads(blob(prefix+case+'/RESULT.json'))['Binaries']:
            origin = next(r for r in media['NativeReferenceOrigins'] if Path(r['Path']).name == binary['File'])
            assert origin['Sha256'].upper() == binary['Sha256'].upper()
            assert sha((a.media/origin['Path']).read_bytes()).upper() == binary['Sha256'].upper()
print(json.dumps(dict(Status='PASS', Scope='Changed canonical Git records, preserved historical authority, frozen runtime/fixtures and native receipt identity', SourceCommit=source, ChangedPaths=len(changed), ActualMediumChecked=bool(a.media))))
