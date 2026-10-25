"""Append closed synthetic receipts; never overwrite original evidence or copy databases."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path('D:/projects/ipc-retention-ingress-full-retest-20261025')
DEST = Path('docs/runtime/evidence/retention-ingress-full-retest-03')
RUN = ROOT / 'protected/PROTECTED-RESULT.json'
run = json.loads(RUN.read_text(encoding='utf-8-sig'))
assert run['Status'] != 'InProgress' and run.get('EndedUtc'), 'Terminal protected run required'
assert run['SourceCommit'] == 'b83998f18c56df2dd965630b25908feedc00a56f'
assert run['ProtectedRoot'] == 'D:\\ImporterPc.Deployment\\Qualification\\retention-94f8bd49d7cd4f768117480983e66d2b'
assert run['ProductSha256'] == '379560D5C546393B503BE8E748183DA138BC936CD17CFD729B895B273FFE2ADE'
assert run['FixtureSha256'] == 'F4B29860D22D3AA991CBBA75D9F4C8C7D1891D9B099AA215F6944852AC590BC5'
observations = [json.loads(row) for row in (ROOT / 'RUN-OBSERVATIONS.jsonl').read_text(encoding='utf-8-sig').splitlines()]
assert observations[-1].get('RunStatus') == run['Status'], 'Wait for observer terminal receipt'
target = DEST / 'original/completed-run'
assert not target.exists(), 'Append once into a fresh evidence prefix'
inventory = json.loads((DEST / 'FILES.json').read_text(encoding='utf-8'))
entries = inventory['Files']
for entry in entries:
    data = (DEST / entry['path']).read_bytes()
    assert len(data) == entry['bytes'] and hashlib.sha256(data).hexdigest() == entry['sha256']
sources = {}
def add(source, relative):
    assert source.is_file() and not source.is_symlink()
    assert source.suffix.lower() in {'.json', '.jsonl', '.log', '.py', '.ps1'}
    assert relative not in sources
    sources[relative] = source

names = [
    'BACKUP-CONTENT-IDENTITY.json', 'INTERMEDIATE-CI-b83998f1.json',
    'PR105-CLOSURE-CHECK.json', 'DIRECT-GITHUB-HEAD-CHECK.json',
    'INTEGRATION-f86f61a9.json', 'OBSERVE-RUN.ps1', 'OBSERVER-LAUNCH.json',
    'RUN-OBSERVATIONS.jsonl', 'capture-terminal-evidence.py',
    'check-followup.py', 'refresh-followup-manifest.py', 'summarize-terminal.py',
    'RECONCILIATION-terminal.json', 'TERMINAL-SUMMARY.json',
    'FROZEN-BUNDLE-IDENTITY.json',
]
for name in names:
    add(ROOT / name, name)
component = Path(re.search(r'EvidenceRoot=(.+)', (ROOT / 'retention-ingress.log').read_text(encoding='utf-8-sig')).group(1).strip())
assert str(component.resolve()).lower().startswith('d:\\devcache\\importer-pc\\temp\\')
assert json.loads((component / 'RETENTION-INGRESS.json').read_text(encoding='utf-8-sig'))['ProductSha256'] == run['ProductSha256']
process_count = 0
for path in sorted(component.rglob('*.json')):
    if path.name.endswith(('.process.json', '.boundary.json')):
        add(path, 'components/retention-ingress/' + path.relative_to(component).as_posix())
        process_count += 1
assert process_count == 4, 'Two completed process-kill scenarios with boundary and exit receipts required'
for path in (ROOT / 'protected').iterdir():
    if path.is_file():
        add(path, 'protected/' + path.name)
for case in run['Cases']:
    if 'CaseRoot' not in case:
        continue
    parent = Path(case['CaseRoot'])
    assert parent.parent == Path(run['ProtectedRoot']) and parent.name == 'case-' + str(case['Count'])
    for name in ['FIXTURE.json', 'VERIFIED.json']:
        path = parent / name
        if path.exists():
            add(path, 'cases/' + parent.name + '/' + name)
    for child in ['complete-before-expiry.db.recovery', 'complete-second-maintenance.db.recovery',
                  'data/Simulation/retention-qualification/sportmaster',
                  'restored/Simulation/retention-qualification/sportmaster']:
        folder = parent / child
        for name in ['COMPLETED.json', 'RESTORED-ISOLATED.json', 'configuration.json']:
            source = folder / name
            if source.exists():
                add(source, 'cases/' + parent.name + '/' + child + '/' + name)
target.mkdir(parents=True)
for relative, source in sorted(sources.items()):
    data = source.read_bytes()
    output = target / relative
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open('xb') as stream:
        stream.write(data)
    assert source.read_bytes() == data == output.read_bytes(), source
    entries.append({'path': output.relative_to(DEST).as_posix(), 'bytes': len(data),
                    'sha256': hashlib.sha256(data).hexdigest()})
entries.sort(key=lambda row: row['path'])
assert len({row['path'] for row in entries}) == len(entries)
(DEST / 'FILES.json').write_text(json.dumps(inventory, indent=2) + '\n', encoding='utf-8')
capture = json.loads((DEST / 'CAPTURE.json').read_text(encoding='utf-8'))
capture.update(Files=len(entries), CaptureScope='42 earlier closed originals plus terminal protected run, independent reconciliation and passive observations; append only',
               TerminalStatus=run['Status'], EndedUtc=run['EndedUtc'], CapturedUtc=datetime.now(timezone.utc).isoformat(),
               DatabasesOrBinariesCopied=False)
(DEST / 'CAPTURE.json').write_text(json.dumps(capture, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'AppendedFiles': len(sources), 'TotalFiles': len(entries), 'RunStatus': run['Status']}))
