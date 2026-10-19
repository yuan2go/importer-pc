"""Verify this package's Git custody, historical preservation and frozen evidence.

Run from the repository root. --staged verifies the reviewable index before commit.
This is documentation/identity verification, never a Windows/field qualification.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[4]
BASE = 'f3059d499cd4dbc60b443f0a54cac939b2e5db2f'
START = '939462bee40c9eebe8eb2aa60860217afd95fcdd'
FREEZE = 'e45e9871b2ad3b85796ae74b4ac3e716996d4554'


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT)


def main():
    p = argparse.ArgumentParser(description=__doc__); p.add_argument('--staged', action='store_true'); args = p.parse_args()
    ref = '' if args.staged else 'HEAD'
    def blob(path): return git('show', ref + ':' + path)
    manifest = json.loads(blob('docs/manifest.json'))
    old = json.loads(git('show', BASE + ':docs/manifest.json'))
    changed = set(git('diff', *(['--cached'] if args.staged else []), '--name-only', BASE, *([] if args.staged else ['HEAD'])).decode().splitlines())
    excluded = {'docs/manifest.json', 'docs/manifest.sha256'}
    records = {row['path']: row for row in manifest['files']}
    for path in changed - excluded:
        data = blob(path); row = records[path]
        assert row['sha256'] == hashlib.sha256(data).hexdigest(), path
        assert row['size_bytes'] == len(data), path
        actual_blob = subprocess.check_output(['git', 'hash-object', '--stdin'], input=data, cwd=ROOT).decode().strip()
        assert row['git_blob'] == actual_blob, path
    for row in old['files']:
        if row['path'] not in changed:
            assert records[row['path']] == row, row['path']
    for key, value in old.items():
        if key not in {'files', 'work_package', 'last_updated', 'framework_design_state'}:
            assert manifest[key] == value, key
    expected = blob('docs/manifest.sha256').decode().split()[0]
    assert hashlib.sha256(blob('docs/manifest.json')).hexdigest() == expected
    for path in ('docs/INDEX.md', 'README.md'):
        assert blob('docs/archive/pre-sportmaster-commercial-delivery-01/' + Path(path).name + '.txt') == git('show', START + ':' + path)
    for path in ('docs/reviews/STATUS.md', 'docs/reviews/issues.json', 'docs/readiness/decision-pack.md'):
        assert blob(path) == git('show', BASE + ':' + path), path
    sys.path.insert(0, str(ROOT / 'scripts'))
    from release_documents import SOURCE_PATHS
    from commercial_delivery import evaluate
    delta = git('diff', *(['--cached'] if args.staged else []), '--name-only', FREEZE, *([] if args.staged else ['HEAD']), '--', *SOURCE_PATHS, 'tests').decode().strip()
    assert not delta, 'Freeze input changed: ' + delta
    folder = ROOT / 'docs/runtime/evidence/sportmaster-commercial-delivery-01/final'
    report = json.loads(blob('docs/runtime/evidence/sportmaster-commercial-delivery-01/final/acceptance.json'))
    result = evaluate(folder, report, FREEZE, report['MediumInventorySha256'], 'engineering')
    assert result['Status'] == 'PASS'
    assert evaluate(folder, report, FREEZE, report['MediumInventorySha256'], 'production')['Status'] == 'FAIL'
    production = json.loads(blob('docs/runtime/evidence/sportmaster-commercial-delivery-01/final/production-gate.json'))
    assert production['Status'] == 'FAIL' and production['SourceCommit'] == FREEZE and production['Level'] == 'production'
    print(json.dumps(dict(Status='PASS', Scope='Changed Git records, original bytes, historical objects, unchanged D/RV, frozen inputs, actual evidence hashes and bounded engineering gate', SourceCommit=FREEZE, ChangedFiles=len(changed))))


if __name__ == '__main__': main()
