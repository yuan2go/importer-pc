"""Read-only custody and source verification for PR104/main integration."""
import argparse
import atexit
import functools
import hashlib
import json
import subprocess

OLD = '168c713d4fe2c332c9ee2964e7cfaa69de3e4b97'
MAIN = 'c425435c6f980ca537a29e4e8353d12be341df61'
BASE = '1dfb676a2970f80e80d635303d84dcec75676cf5'
SHARED = {'.gitattributes', 'docs/INDEX.md', 'docs/manifest.json', 'docs/manifest.sha256',
          'docs/runtime/win11-24x7-stability-remediation-01.md',
          'tests/Ipc.Application.Verification/Program.cs'}
OWNED = SHARED - {'docs/manifest.json', 'docs/manifest.sha256'} | {
    'docs/work-packages/WP-IPC-CODE-RELIABILITY-HARDENING-02.md',
    'docs/runtime/code-reliability-hardening-02.md',
    'docs/runtime/validation/pr104-main-integration-01.md',
    'scripts/check-pr104-integration.py'}


def git(*args, **kwargs):
    return subprocess.check_output(['git', *args], **kwargs)


_batch = None


def close_batch():
    if _batch is not None:
        _batch.stdin.close()
        _batch.wait(timeout=5)
        _batch.stdout.close()


atexit.register(close_batch)


@functools.lru_cache(maxsize=None)
def read(ref, path):
    global _batch
    if _batch is None:
        _batch = subprocess.Popen(['git', 'cat-file', '--batch'], stdin=subprocess.PIPE, stdout=subprocess.PIPE)
    _batch.stdin.write(f'{ref}:{path}\n'.encode('utf-8'))
    _batch.stdin.flush()
    header = _batch.stdout.readline().split()
    assert len(header) == 3 and header[1] == b'blob', (ref, path, header)
    data = _batch.stdout.read(int(header[2]))
    assert len(data) == int(header[2]) and _batch.stdout.read(1) == b'\n', (ref, path)
    return data


def record_map(manifest):
    records = {r['path']: r for r in manifest['files']}
    assert len(records) == len(manifest['files']), 'Duplicate manifest paths'
    return records


def verify(candidate):
    staged = candidate == ':'
    ref = '' if staged else candidate
    if not staged:
        for parent in (OLD, MAIN):
            assert subprocess.run(['git', 'merge-base', '--is-ancestor', parent, candidate], check=False).returncode == 0, parent
    for tree in ('src', 'config'):
        if staged:
            assert not git('diff', '--cached', '--name-only', OLD, '--', tree), tree
        else:
            assert git('rev-parse', f'{candidate}:{tree}') == git('rev-parse', f'{OLD}:{tree}'), tree

    main_delta = git('diff', '--name-only', BASE, MAIN).decode().splitlines()
    for path in set(main_delta) - SHARED:
        assert read(ref, path) == read(MAIN, path), f'PR103 changed: {path}'

    for directory in ('docs/runtime/evidence/code-reliability-hardening-02',
                      'docs/archive/pre-code-reliability-hardening-02'):
        for path in git('ls-tree', '-r', '--name-only', OLD, '--', directory).decode().splitlines():
            assert read(ref, path) == read(OLD, path), f'PR104 historical evidence changed: {path}'
    old_program = read(OLD, 'tests/Ipc.Application.Verification/Program.cs')
    program = read(ref, 'tests/Ipc.Application.Verification/Program.cs')
    added = [line for line in program.splitlines(keepends=True) if b'--retention-qualification' in line]
    assert len(added) == 1 and program.replace(added[0], b'') == old_program, 'Verification entry changed beyond the added retention route'
    for flag in ('--h02-crash-child', '--h02-crashes', '--reliability-02'):
        assert program.count(flag.encode()) == old_program.count(flag.encode()), flag

    counts = {}
    for directory in ('docs/runtime/evidence/code-reliability-hardening-02',
                      'docs/runtime/evidence/retention-maintenance-qualification-01',
                      'docs/runtime/evidence/pr104-main-integration-01'):
        listing = json.loads(read(ref, directory + '/FILES.json'))
        rows = listing['Files'] if isinstance(listing, dict) else listing
        for row in rows:
            data = read(ref, directory + '/' + row['path'])
            assert len(data) == row['bytes'], row['path']
            assert hashlib.sha256(data).hexdigest() == row['sha256'].lower(), row['path']
        counts[directory] = len(rows)

    old, main, current = [json.loads(read(r, 'docs/manifest.json')) for r in (OLD, MAIN, ref)]
    om, mm, cm = map(record_map, (old, main, current))
    assert set(om) | set(mm) <= set(cm), 'Manifest dropped a path'
    assert current['retention_maintenance_qualification_01'] == main['retention_maintenance_qualification_01'], 'Historical qualification metadata changed'
    historical = 0
    base_map = record_map(json.loads(read(BASE, 'docs/manifest.json')))
    for path in set(om) | set(mm):
        if path in OWNED:
            continue
        o, m, b = om.get(path), mm.get(path), base_map.get(path)
        expected = o if m == b or o == m else m
        assert o == b or m == b or o == m, ('Unreviewed manifest conflict', path)
        assert cm[path] == expected, ('Historical manifest record changed', path)
        historical += 1
    evidence_prefix = 'docs/runtime/evidence/pr104-main-integration-01/'
    checked = 0
    for path, row in cm.items():
        if path not in OWNED and not path.startswith(evidence_prefix):
            continue
        data = read(ref, path)
        assert row['sha256'].lower() == hashlib.sha256(data).hexdigest(), path
        assert row['size_bytes'] == len(data), path
        assert row['git_blob'] == hashlib.sha1(f'blob {len(data)}\0'.encode() + data).hexdigest(), path
        checked += 1
    manifest = read(ref, 'docs/manifest.json')
    assert read(ref, 'docs/manifest.sha256').decode().split()[0] == hashlib.sha256(manifest).hexdigest(), 'Canonical manifest checksum'
    for path in SHARED - {'docs/manifest.sha256'}:
        assert b'<<<<<<< HEAD' not in read(ref, path), path
    return {'Result':'PASS', 'Candidate':candidate, 'PreviousPrHead':OLD, 'BaselineMain':MAIN,
            'ProductAndConfig':'BYTE_IDENTICAL_TO_PR104', 'PreservedPr103Paths':len(set(main_delta)-SHARED),
            'OriginalEvidenceChecks':counts, 'UnchangedHistoricalManifestRecords':historical,
            'IntegrationManifestChecks':checked, 'RetentionQualification':'Original FAIL/NOT_RUN unchanged; PR105 excluded',
            'FieldQualification':'NOT_RUN/BLOCKED; no 24h/72h or target IPC qualification'}


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--candidate', default='HEAD', help='Commit, or : for the staged candidate')
    args = parser.parse_args()
    print(json.dumps(verify(args.candidate), ensure_ascii=False, indent=2))
