"""Derive timings and frozen bundle hashes from a terminal run without changing receipts."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
ROOT = Path('D:/projects/ipc-retention-ingress-full-retest-20261025')
run_file = ROOT / 'protected/PROTECTED-RESULT.json'
run = json.loads(run_file.read_text(encoding='utf-8-sig'))
assert run['Status'] != 'InProgress' and run.get('EndedUtc')
reconciliation_file = ROOT / 'RECONCILIATION-terminal.json'
reconciled = json.loads(reconciliation_file.read_text(encoding='utf-8-sig'))
assert reconciled['SourceCommit'] == run['SourceCommit']
checks = {item['Count']: item for item in reconciled['Cases']}
rows = []
for case in run['Cases']:
    count = case['Count']
    row = dict(Count=count, Status=case['Status'])
    if count in checks:
        row.update(checks[count])
    for name in ['OriginalBytes', 'BackupHash', 'IsolatedRestoreSeconds', 'RequiredEstimateBytes', 'AvailableBytes']:
        if name in case:
            row[name] = case[name]
    restores = [item for item in run['Runs'] if item['Name'] == 'reuse-restore-' + str(count)]
    if restores:
        assert len(restores) == 1
        row['ReuseRestoreSeconds'] = restores[0]['Seconds']
        if count in checks:
            row['ReuseRestoreAndPreInspectSeconds'] = restores[0]['Seconds'] + checks[count]['InspectSeconds']
    rows.append(row)
summary = dict(AtUtc=datetime.now(timezone.utc).isoformat(), RunStatus=run['Status'],
               SourceCommit=run['SourceCommit'], ProductSha256=run['ProductSha256'], FixtureSha256=run['FixtureSha256'],
               RunReceiptSha256=hashlib.sha256(run_file.read_bytes()).hexdigest(),
               ReconciliationSha256=hashlib.sha256(reconciliation_file.read_bytes()).hexdigest(),
               OriginalRunStatus=run['ReusedRun']['OriginalStatus'], OriginalRunSource=run['ReusedRun']['SourceCommit'],
               Cases=rows, ExactReconciliationChecks=len(reconciled['Checks']),
               MaintenanceBudgetScope=run['MaintenanceBudgetScope'],
               RestoreAll=False, Qualification='Isolated protected synthetic CLI only; no target IPC, real SDK/RCS, actual installer, 24h/72h or field acceptance')
target = ROOT / 'TERMINAL-SUMMARY.json'
with target.open('x', encoding='utf-8') as stream:
    stream.write(json.dumps(summary, indent=2) + '\n')
protected = Path(run['ProtectedRoot'])
files = []
for folder in ['cli', 'fixture']:
    for path in sorted((protected / folder).rglob('*')):
        if path.is_file() and path.suffix.lower() in {'.dll', '.exe', '.json'}:
            assert not path.is_symlink()
            with path.open('rb') as source:
                sha = hashlib.file_digest(source, 'sha256').hexdigest()
            files.append(dict(Path=path.relative_to(protected).as_posix(), Bytes=path.stat().st_size, Sha256=sha))
bundle = dict(SourceCommit=run['SourceCommit'], AtUtc=datetime.now(timezone.utc).isoformat(), Files=files,
              Scope='Frozen executed CLI and fixture bundle hash inventory only; no binary archived or installer acceptance')
with (ROOT / 'FROZEN-BUNDLE-IDENTITY.json').open('x', encoding='utf-8') as stream:
    stream.write(json.dumps(bundle, indent=2) + '\n')
print(json.dumps(dict(RunStatus=run['Status'], Cases=len(rows), ReconciliationChecks=len(reconciled['Checks']), FrozenFiles=len(files))))
