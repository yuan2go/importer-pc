"""Read-only reconciliation of protected synthetic CLI runs against their complete backups."""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import sqlite3
import struct
import ctypes
import os
from contextlib import contextmanager


@contextmanager
def closed_snapshots(paths):
    if os.name != 'nt':
        raise ValueError('Protected snapshot reconciliation requires Windows sharing locks')
    kernel = ctypes.WinDLL('kernel32', use_last_error=True)
    kernel.CreateFileW.argtypes = [ctypes.c_wchar_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p, ctypes.c_uint32, ctypes.c_uint32, ctypes.c_void_p]
    kernel.CreateFileW.restype = ctypes.c_void_p
    kernel.CloseHandle.argtypes = [ctypes.c_void_p]
    handles = []
    try:
        for path in paths:
            # GENERIC_READ + FILE_SHARE_READ: existing/new writers or replacement are refused.
            handle = kernel.CreateFileW(str(path), 0x80000000, 1, None, 3, 0x80, None)
            if handle == ctypes.c_void_p(-1).value:
                raise ctypes.WinError(ctypes.get_last_error())
            handles.append(handle)
        yield
    finally:
        for handle in handles:
            kernel.CloseHandle(handle)


def guid(kind, number):
    return f'{kind:02x}{number:030x}'


def fingerprint(db, sql):
    h = hashlib.sha256()
    for row in db.execute(sql):
        for item in row:
            b = item.encode('utf-8')
            h.update(struct.pack('<i', len(b)))
            h.update(b)
    return h.hexdigest().upper()


def reconcile(run_file, output, completed_only=False, simulated_authority=False):
    if output.exists():
        raise ValueError('A new reconciliation output is required')
    run = json.loads(run_file.read_text(encoding='utf-8-sig'))
    if run['Status'] == 'InProgress' and not completed_only:
        raise ValueError('Wait for the whole run, or explicitly use --completed-only')
    checks = []

    def check(value, reason):
        if not value:
            raise ValueError(reason)
        checks.append(reason)

    cases = []
    runs = {r['Name']: r for r in run.get('Runs', [])}
    for case in run['Cases']:
        expected_status = 'PASS_SIMULATED_AUTHORITY_SYNTHETIC' if simulated_authority else 'PASS_PROTECTED_CLI_SYNTHETIC'
        if case['Status'] != expected_status:
            continue
        count = case['Count']
        root = Path(case['CaseRoot'])
        parent = run['FixtureRoot'] if simulated_authority else run['ProtectedRoot']
        name = f'simulated-{count}' if simulated_authority else f'case-{count}'
        check(root.parent == Path(parent) and root.name == name, f'{count}: isolated synthetic root')
        fixture = json.loads((root / 'FIXTURE.json').read_text(encoding='utf-8-sig'))
        database = Path(fixture['Database'])
        backup = root / 'complete-before-expiry.db'
        restored = root / 'restored/Simulation/retention-qualification/sportmaster/sportmaster.db'
        snapshots = [database, backup]
        if 'IsolatedRestoreSeconds' in case:
            snapshots.append(restored)
        # Completed, closed snapshots only. Immutable reads avoid creating WAL sidecars in
        # the protected directory; a live/nonempty WAL must never be ignored.
        for path in snapshots:
            wal = Path(str(path) + '-wal')
            check(not wal.exists() or wal.stat().st_size == 0, f'{count}: closed snapshot has no nonempty WAL: {path.name}')
        sharing = closed_snapshots(snapshots)
        sharing.__enter__()
        db = None
        try:
            # Close the check/lock race: a writer may have closed after the initial
            # WAL check but before our sharing handles were acquired.
            for path in snapshots:
                wal = Path(str(path) + '-wal')
                check(not wal.exists() or wal.stat().st_size == 0, f'{count}: no nonempty WAL after writer exclusion: {path.name}')
            db = sqlite3.connect(database.as_uri() + '?mode=ro&immutable=1', uri=True)
            db.execute('PRAGMA query_only=ON')
            db.execute('ATTACH DATABASE ? AS original', (backup.as_uri() + '?mode=ro&immutable=1',))
            scalar = lambda sql, parameters=(): db.execute(sql, parameters).fetchone()[0]
            check(scalar('SELECT count(*) FROM original.parcel_archives') == count + 6, f'{count}: backup retains every original operation')
            check(scalar('SELECT count(*) FROM original.parcel_archived_receipts') == count + 5, f'{count}: backup retains every committed raw reply')
            check(scalar('SELECT count(*) FROM original.parcel_ingress') == count + 6, f'{count}: backup retains every original input')
            check(scalar('SELECT count(*) FROM parcel_retired_operations') == count, f'{count}: exact retired operation count')
            check(scalar('SELECT count(*) FROM parcel_retired_operations WHERE id<? OR id>?', (guid(1, 1), guid(1, count))) == 0, f'{count}: only eligible identity range retired')
            check(scalar('SELECT count(*) FROM parcel_retired_operations t LEFT JOIN original.parcel_archives a ON a.id=t.id WHERE a.id IS NULL OR a.body_hash!=t.body_hash') == 0, f'{count}: every retirement matches its original body hash')
            guards = {guid(1, n) for n in range(count + 1, count + 7)}
            retained = dict(db.execute('SELECT id,body FROM parcel_operations UNION ALL SELECT id,body FROM parcel_archives'))
            check(set(retained) == guards, f'{count}: exact six guarded owners retained')
            before = dict(db.execute('SELECT id,body FROM original.parcel_archives WHERE id>?', (guid(1, count),)))
            check(retained == before, f'{count}: guarded original bodies unchanged byte for byte')
            check(scalar('SELECT count(*) FROM parcel_retired_inputs') == count, f'{count}: exact retired input count')
            check(scalar('SELECT count(*) FROM parcel_retired_inputs t LEFT JOIN original.parcel_ingress i ON i.id=t.id WHERE i.id IS NULL OR t.body_hash!=i.body_hash OR t.state!=i.state OR i.state NOT IN (\'Processed\',\'Inspected\')') == 0, f'{count}: retired input hashes/states match originals')
            check(scalar('SELECT count(*) FROM parcel_ingress') == 6, f'{count}: guarded inputs remain')
            retained_inputs = {row[0]: row[1:] for row in db.execute('SELECT id,body,body_hash,state FROM parcel_ingress')}
            check(set(retained_inputs) == {guid(5, n) for n in range(count + 1, count + 7)}, f'{count}: exact six protected input identities remain')
            original_inputs = {row[0]: row[1:] for row in db.execute('SELECT id,body,body_hash,state FROM original.parcel_ingress WHERE id>?', (guid(5, count),))}
            check(retained_inputs == original_inputs, f'{count}: protected input bytes, hashes and dispositions unchanged')
            check(list(db.execute('SELECT * FROM parcel_commands ORDER BY id')) == list(db.execute('SELECT * FROM original.parcel_commands ORDER BY id')), f'{count}: every original pending command and receipt unchanged')
            check(list(db.execute('SELECT * FROM parcel_identity_conflicts ORDER BY id')) == list(db.execute('SELECT * FROM original.parcel_identity_conflicts ORDER BY id')), f'{count}: original unresolved conflict and acknowledgement state unchanged')
            retained_receipts = {row[0]: row[1:] for row in db.execute('SELECT * FROM parcel_receipts')}
            original_receipts = {row[0]: row[1:] for row in db.execute('SELECT * FROM original.parcel_receipts WHERE operation>?', (guid(1, count),))}
            check(retained_receipts == original_receipts and len(retained_receipts) == 6, f'{count}: all protected original receipt/request/state/hash rows unchanged')
            check(list(db.execute('SELECT * FROM parcel_ingress_audits ORDER BY input,seq')) == list(db.execute('SELECT * FROM original.parcel_ingress_audits WHERE input>? ORDER BY input,seq', (guid(5, count),))), f'{count}: protected input/audit custody associations unchanged')
            check(scalar('SELECT count(*) FROM parcel_archived_receipts') == 5, f'{count}: guarded original raw remains')
            check(scalar('SELECT count(*) FROM parcel_archived_receipts r LEFT JOIN original.parcel_archived_receipts a ON a.id=r.id WHERE a.id IS NULL OR r.body!=a.body OR r.raw_hash!=a.raw_hash') == 0, f'{count}: every protected raw BLOB and hash unchanged')
            check(fingerprint(db, 'SELECT id,admission,observation,position FROM parcel_history_index ORDER BY seq') == fixture['IdentityFingerprint'], f'{count}: complete permanent admission fingerprint unchanged')
            check(fingerprint(db, 'SELECT request,id,hash FROM parcel_reply_identities ORDER BY request,id') == fixture['ReplyFingerprint'], f'{count}: complete reply identity fingerprint unchanged')
            raw_bytes = scalar('SELECT sum(length(body)) FROM original.parcel_archived_receipts')
            operation_bytes = scalar('SELECT sum(length(body)) FROM original.parcel_archives')
            restore_hash = None
            if 'IsolatedRestoreSeconds' in case:
                marker = json.loads(restored.with_name('RESTORED-ISOLATED.json').read_text(encoding='utf-8-sig'))
                check(marker['Application'] == 'sportmaster' and marker['DatabaseHash'] == case['BackupHash'], f'{count}: isolated restore marker preserves the complete backup identity')
                # This reference fixture has no numeric-range retirement mutations.
                # Its restored DB must therefore match every original byte, not only counts.
                with backup.open('rb') as stream:
                    backup_hash = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
                with restored.open('rb') as stream:
                    restore_hash = hashlib.file_digest(stream, 'sha256').hexdigest().upper()
                check(backup_hash == case['BackupHash'] == restore_hash, f'{count}: isolated restored database matches the complete backup byte for byte')
                check(set(marker['Files']) == {'configuration.json'}, f'{count}: complete reference configuration is present in the restore marker')
                for filename, expected in marker['Files'].items():
                    check(Path(filename).name == filename and filename == 'configuration.json', f'{count}: only the expected reference configuration is inspected')
                    original_config_hash = hashlib.sha256(Path(str(backup) + '.recovery', filename).read_bytes()).hexdigest().upper()
                    check(hashlib.sha256(restored.with_name(filename).read_bytes()).hexdigest().upper() == expected == original_config_hash, f'{count}: restored frozen configuration matches the original backup and marker')
        finally:
            if db is not None:
                db.close()
            sharing.__exit__(None, None, None)
        if simulated_authority:
            receipt = json.loads((root / 'SIMULATED-AUTHORITY.json').read_text(encoding='utf-8-sig'))
            check(receipt['ActualProtectedCli'] is False and receipt['ActualElevatedAuthority'] is False,
                  f'{count}: explicit simulated authority cannot be reported as protected CLI')
            cases.append(dict(Count=count, ActualProtectedCli=False, ActualElevatedAuthority=False,
                              OperationBodyBytes=operation_bytes, OriginalRawBytes=raw_bytes,
                              SimulatedMaintenanceAndVerifySeconds=receipt['ElapsedSeconds']))
            continue
        expire = runs[f'expire-{count}']
        marker = Path(str(backup) + '.recovery/COMPLETED.json')
        started = datetime.fromisoformat(expire['StartedUtc']).timestamp()
        # Filesystem timestamp separates backup completion approximately; CLI total uses Stopwatch.
        before_backup = marker.stat().st_mtime - started
        restore_runs = []
        if 'IsolatedRestoreSeconds' in case:
            # Executed early helpers reused restore Names. Match their original arguments,
            # not a last-name-wins dictionary or the case-1000/case-10000 text prefix.
            restore_directory = str(restored.parent)
            for action, argument in (('recovery-plan', restore_directory), ('restore', restore_directory), ('maintenance-inspect', str(restored))):
                matches = [r for r in run['Runs'] if action in r['Arguments'] and argument in r['Arguments']]
                check(len(matches) == 1 and matches[0]['ExitCode'] == 0, f'{count}: exactly one successful isolated {action} command recorded')
                restore_runs.append(matches[0])
        cases.append(dict(Count=count, Action=case['Action'], DatabaseBytes=case['OriginalBytes'],
                          OperationBodyBytes=operation_bytes, OriginalRawBytes=raw_bytes,
                          InspectSeconds=runs[f'inspect-{count}']['Seconds'], ExpireSeconds=expire['Seconds'],
                          ApproxPreflightArchiveBackupSeconds=before_backup,
                          ApproxPostBackupRetentionVerifyCompactSeconds=expire['Seconds'] - before_backup,
                          PhaseTimingMethod='COMPLETED.json mtime vs UTC start; approximate only. CLI total is monotonic Stopwatch.',
                          CliPeakWorkingSet=expire['PeakWorkingSet'], PeakWalBytes=expire['PeakWalBytes'],
                          BackupBytes=case['BackupBytes'], PostBytes=case['PostBytes'],
                          InspectPlusExpirySeconds=runs[f'inspect-{count}']['Seconds'] + expire['Seconds'],
                          IsolatedRestoreSeconds=case.get('IsolatedRestoreSeconds'), RestoredDatabaseSHA256=restore_hash,
                          RestorePlanCopyInspectSeconds=sum(r['Seconds'] for r in restore_runs) if restore_runs else None,
                          TimingBoundary=run.get('MaintenanceBudgetScope', 'Historical two-hour product CTS starts after original hash/Repository construction; CLI total also includes those steps. Preceding inspection, fixture generation, restore drill, operator review and business takeover are separate.')))
    check(bool(cases), 'at least one completed case reconciled')
    result = dict(Status='PARTIAL_COMPLETED_CASES_VERIFIED' if run['Status'] == 'InProgress' else 'VERIFIED_COMPLETED_SYNTHETIC_CASES',
                  SourceCommit=run['SourceCommit'], ProductSha256=run['ProductSha256'], FixtureSha256=run['FixtureSha256'],
                  AtUtc=datetime.now(timezone.utc).isoformat(), SQLiteReadOnlyReconciler=sqlite3.sqlite_version,
                  Authority='SIMULATED_ONLY' if simulated_authority else 'PROTECTED_CLI_RECEIPTS',
                  SnapshotProtection='Windows read handles deny existing/new writers and replacement; nonempty WAL refused before immutable reads',
                  Cases=cases, Checks=checks, OriginalRunStatus=run['Status'],
                  Qualification='No production install, real SDK/RCS profile, target 8GiB, 24h/72h or field acceptance')
    output.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    return result


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--run', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--completed-only', action='store_true')
    parser.add_argument('--simulated-authority', action='store_true', help='Reconcile explicit Simulation fixtures; never assigns protected CLI or restore timing')
    args = parser.parse_args()
    value = reconcile(args.run, args.output, args.completed_only, args.simulated_authority)
    print(json.dumps({'Status': value['Status'], 'Cases': len(value['Cases']), 'Checks': len(value['Checks'])}))
