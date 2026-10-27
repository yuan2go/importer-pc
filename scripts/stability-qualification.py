#!/usr/bin/env python3
"""Independent bounded observation and offline custody reconciliation; never controls equipment.

collect reads one already-running frozen Windows release. analyze reads an OFFLINE
complete backup. Neither command grants Production qualification or joins restarts.
"""
import argparse
import collections
import ctypes
import datetime as dt
import hashlib
import json
import math
import os
import re
from pathlib import Path
import platform
import shutil
import sqlite3
import time


def utc():
    return dt.datetime.now(dt.timezone.utc).isoformat()


def read_json(path, limit=2 * 1024 * 1024):
    with Path(path).open('rb') as f:
        value = f.read(limit + 1)
    if len(value) > limit:
        raise ValueError('JSON input exceeds bounded read')
    def unique(pairs):
        result = {}; seen = set()
        for key, item in pairs:
            if key.casefold() in seen:
                raise ValueError('Duplicate JSON key')
            seen.add(key.casefold()); result[key] = item
        return result
    return json.loads(value, object_pairs_hook=unique)


def ordinary(path):
    p = Path(os.path.abspath(path))
    if platform.system() == 'Windows' and p.drive.lower() != 'd:':
        raise ValueError('This project requires D: paths')
    for part in (p, *p.parents):
        if part.is_symlink() or getattr(part, 'is_junction', lambda: False)():
            raise ValueError('Linked paths are not accepted')
    return p.resolve()


def sha(path):
    with Path(path).open('rb') as f:
        return hashlib.file_digest(f, 'sha256').hexdigest()


def verify_release(root):
    root = ordinary(root)
    manifest = read_json(root / 'ASSEMBLY.json')
    files = {}
    inventory = root / 'SHA256SUMS'
    if inventory.stat().st_size > 4 * 1024 * 1024:
        raise ValueError('Release inventory exceeds quota')
    for line in inventory.read_text().splitlines():
        digest, name = line.split('  ', 1)
        p = ordinary(root / name)
        if not re.fullmatch('[a-fA-F0-9]{64}', digest) or not p.is_relative_to(root) or name.casefold() in files or sha(p).lower() != digest.lower():
            raise ValueError('Frozen release inventory differs: ' + name)
        files[name.casefold()] = digest
    if 'desktop/ipc.desktop.exe' not in files or not re.fullmatch('[a-f0-9]{40}', manifest['SourceCommit']):
        raise ValueError('A frozen desktop delivery is required')
    return manifest


class ProcessIdentity:
    """Keep a kernel handle: PID reuse cannot silently join another process."""
    def __init__(self, pid):
        from ctypes import wintypes as w
        self.kernel = ctypes.WinDLL('kernel32', use_last_error=True)
        self.kernel.OpenProcess.restype = w.HANDLE
        self.kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
        self.kernel.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
        self.kernel.CloseHandle.argtypes = [w.HANDLE]
        self.kernel.QueryFullProcessImageNameW.argtypes = [w.HANDLE, w.DWORD, w.LPWSTR, ctypes.POINTER(w.DWORD)]
        self.handle = self.kernel.OpenProcess(0x1000 | 0x100000, False, pid)
        if not self.handle:
            raise ctypes.WinError(ctypes.get_last_error())
        size = w.DWORD(32768); value = ctypes.create_unicode_buffer(size.value)
        if not self.kernel.QueryFullProcessImageNameW(self.handle, 0, value, ctypes.byref(size)):
            self.close(); raise ctypes.WinError(ctypes.get_last_error())
        self.executable = ordinary(value.value)

    def alive(self):
        return self.kernel.WaitForSingleObject(self.handle, 0) == 258

    def close(self):
        if self.handle:
            self.kernel.CloseHandle(self.handle); self.handle = None


def validate_plan(plan):
    for name in ('TargetHardware', 'Combination', 'SourceCommit', 'DeviceEvidence', 'RcsContractEvidence'):
        if not isinstance(plan.get(name), str) or not plan[name].strip() or plan[name].startswith('REQUIRED'):
            raise ValueError('Target plan must specify ' + name)
    for name in ('NormalHours', 'WarmupSeconds', 'MinimumFreeBytes', 'MaxEvidenceBytes',
                 'MaximumUiStallMs', 'MaximumWorkerStallMs', 'MaximumQueueDepth', 'LeftHz', 'RightHz'):
        value = plan.get(name)
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value <= 0:
            raise ValueError('Target plan must specify a positive finite ' + name)
    if not 24 <= plan['NormalHours'] <= 168 or plan['MaxEvidenceBytes'] < 1048576:
        raise ValueError('Normal segment must be 24..168 hours, evidence quota at least 1 MiB')
    if not 7 <= plan.get('RetentionDays', 0) <= 30 or plan['LeftHz'] + plan['RightHz'] > 1.00001:
        raise ValueError('Confirmed baseline: retention 7..30 days, combined peak up to 60/minute')
    if not 0 <= plan.get('MaximumRateDeficitPercent', -1) <= 10:
        raise ValueError('Set an explicit throughput deficit tolerance 0..10 percent')


def collect(args):
    if platform.system() != 'Windows':
        raise RuntimeError('NOT_RUN: target Windows required')
    plan = read_json(ordinary(args.plan)); validate_plan(plan)
    release = ordinary(args.release); identity = verify_release(release)
    if identity['SourceCommit'] != plan['SourceCommit']:
        raise ValueError('Plan and frozen release SourceCommit differ')
    progress_file = ordinary(args.progress); output = ordinary(args.output)
    output.mkdir(parents=True, exist_ok=False)
    initial = read_json(progress_file)['Progress']; runtime = initial['RuntimeId']; pid = initial['ProcessId']
    process = ProcessIdentity(pid)
    if process.executable != (release / 'desktop/Ipc.Desktop.exe').resolve():
        process.close(); raise ValueError('Target process is not this frozen desktop executable')
    started = time.monotonic(); last = started; normal_start = None; normal_end = None; normal_tick = None; last_normal_tick = None
    failures = collections.Counter(); samples = 0; written = 0; first_normal = None; last_normal = None
    windows = {}; window_start = {}; window_last = {}; resource_start = None; resource_end = None
    metadata = dict(Started=utc(), RuntimeId=runtime, ProcessId=pid, Executable=str(process.executable),
                    SourceCommit=identity['SourceCommit'], ReleaseManifestSha256=sha(release / 'SHA256SUMS'),
                    Plan=plan, OS=platform.platform(), Scope='ACTUAL_SINGLE_PROCESS_OBSERVATION; no automatic business/qualification PASS')
    (output / 'identity.json').write_text(json.dumps(metadata, indent=2), encoding='utf-8')
    normal_seconds = args.smoke_seconds or plan['NormalHours'] * 3600
    warmup = 0 if args.smoke_seconds else plan['WarmupSeconds']
    try:
        with (output / 'samples.jsonl').open('x', encoding='utf-8') as log:
            while True:
                now = time.monotonic()
                if not process.alive():
                    failures['ProcessExited'] += 1; break
                if now - last > 5:
                    failures['CollectorGapOver5s'] += 1
                last = now
                if now - started > warmup + normal_seconds + 5:
                    failures['CollectionTimeBudgetExceeded'] += 1; break
                if shutil.disk_usage(output).free < plan['MinimumFreeBytes']:
                    failures['EvidenceVolumeReserve'] += 1; break
                try:
                    sample = read_json(progress_file); progress = sample['Progress']
                    if progress['RuntimeId'] != runtime or progress['ProcessId'] != pid:
                        failures['RuntimeIdentityChanged'] += 1; break
                    stale = (dt.datetime.now(dt.timezone.utc) - dt.datetime.fromisoformat(progress['At'])).total_seconds()
                    if stale < -1 or stale > 5:
                        failures['SupervisionStaleOrClockChanged'] += 1
                    is_normal = now - started >= warmup
                    if is_normal:
                        if normal_start is None:
                            normal_start = utc(); first_normal = progress; resource_start = sample['Resource']; normal_tick = now
                        normal_end = utc(); last_normal = progress; resource_end = sample['Resource']; last_normal_tick = now
                        if progress['Stopping'] or progress.get('Failure') or progress.get('SupervisionFailure'):
                            failures['RuntimeFaultOrStopping'] += 1
                        if not progress['Ui']:
                            failures['NoUiProbe'] += 1
                        for ui in progress['Ui']:
                            windows[ui['Id']] = ui
                            bins = {b['UpperMs']: b['Count'] for b in ui['HistogramCounts']}
                            window_start.setdefault(ui['Id'], bins.copy())
                            previous = window_last.get(ui['Id'], bins)
                            if any(count > previous.get(upper, 0) and
                                   (ui['MaximumDispatchMs'] if upper == 2048 else upper) > plan['MaximumUiStallMs']
                                   for upper, count in bins.items()):
                                failures['CompletedUiDispatchStall'] += 1
                            window_last[ui['Id']] = bins
                            if ui['LastDispatchAgeMs'] > plan['MaximumUiStallMs']:
                                failures['UiStall'] += 1
                        for worker in progress['Workers']:
                            if worker['State'] in ('Faulted', 'Stopped') or worker.get('Failure'):
                                failures['WorkerTerminalOrFault'] += 1
                            if worker['QueueDepth'] > plan['MaximumQueueDepth']:
                                failures['QueueBudget'] += 1
                            if worker.get('OldestWaiting') and (dt.datetime.now(dt.timezone.utc) - dt.datetime.fromisoformat(worker['OldestWaiting'])).total_seconds() * 1000 > plan['MaximumWorkerStallMs']:
                                failures['OldestInputStall'] += 1
                            if worker.get('ProcessingSince') and (dt.datetime.now(dt.timezone.utc) - dt.datetime.fromisoformat(worker['ProcessingSince'])).total_seconds() * 1000 > plan['MaximumWorkerStallMs']:
                                failures['InFlightWorkerStall'] += 1
                        for task in progress['Background']:
                            if task['State'] in ('Faulted', 'Canceled', 'RanToCompletion') or task['Detail'].startswith('Faulted:'):
                                failures['BackgroundTerminalOrFault'] += 1
                            if not task.get('LastProgress') or (dt.datetime.now(dt.timezone.utc) - dt.datetime.fromisoformat(task['LastProgress'])).total_seconds() * 1000 > plan['MaximumWorkerStallMs']:
                                failures['BackgroundProgressStall'] += 1
                    line = json.dumps(dict(CollectedAt=utc(), ElapsedSeconds=now-started, Normal=is_normal, **sample), separators=(',', ':')) + '\n'
                    written += len(line.encode('utf-8'))
                    if written > plan['MaxEvidenceBytes']:
                        failures['EvidenceQuotaReached'] += 1; break
                    log.write(line); log.flush(); samples += 1
                    if normal_tick is not None and now - normal_tick >= normal_seconds:
                        break
                except (OSError, ValueError, KeyError) as error:
                    failures['UnreadableSupervision:' + type(error).__name__] += 1
                time.sleep(max(0.01, 1 - (time.monotonic() - now)))
    except KeyboardInterrupt:
        failures['InterruptedByOperator'] += 1
    finally:
        process.close()
        actual = max(0, (last_normal_tick or started) - (normal_tick or started))
        rates = {}
        normal_ui = []
        for window_id, last_ui in windows.items():
            bins = {upper: count - window_start[window_id].get(upper, 0) for upper, count in window_last[window_id].items()}
            bins = {upper: count for upper, count in bins.items() if count > 0}; total = sum(bins.values())
            def percentile(fraction):
                cumulative = 0
                for upper, count in sorted(bins.items()):
                    cumulative += count
                    if cumulative >= math.ceil(total * fraction):
                        return last_ui['MaximumDispatchMs'] if upper == 2048 else upper
                return None
            normal_ui.append(dict(Id=window_id, Samples=total, P95UpperBoundMs=percentile(.95), P99UpperBoundMs=percentile(.99),
                                  MaximumUpperBoundMs=percentile(1), Scope='Normal-segment histogram delta; overflow uses conservative lifetime maximum'))
        if first_normal and last_normal and actual:
            for position, target in [('left', plan['LeftHz']), ('right', plan['RightHz'])]:
                before = next((w for w in first_normal['Workers'] if w['Position'] == position), None)
                after = next((w for w in last_normal['Workers'] if w['Position'] == position), None)
                if not before or not after:
                    failures['MissingPosition:' + position] += 1; continue
                rate = (after['Received'] - before['Received']) / actual; rates[position] = rate
                if rate < target * (1 - plan['MaximumRateDeficitPercent'] / 100):
                    failures['InputRateBelowPlan:' + position] += 1
        report = dict(Status='SMOKE_ONLY' if args.smoke_seconds else 'COLLECTED_REQUIRES_RECONCILIATION' if actual >= normal_seconds and not failures else 'NOT_QUALIFIED',
                      ActualNormalSeconds=actual, RequiredNormalSeconds=plan['NormalHours'] * 3600, WarmupSeconds=warmup, ObservedInputHz=rates,
                      NormalStart=normal_start, NormalEnd=normal_end, Samples=samples, Bytes=written, Failures=dict(failures),
                      RuntimeId=runtime, SourceCommit=identity['SourceCommit'], FirstNormal=first_normal, LastNormal=last_normal,
                      Ui=normal_ui, ResourceStart=resource_start, ResourceEnd=resource_end,
                      Qualification='REVIEW_REQUIRED; source-to-receipt, target hardware, real devices/contracts and recovery evidence are independent')
        (output / 'collection.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps(report, ensure_ascii=False))


def verify_backup(database):
    root = ordinary(str(database) + '.recovery'); manifest_file = ordinary(root / 'COMPLETED.json')
    manifest = read_json(manifest_file, 16 * 1024 * 1024)
    if manifest['Database'] != database.name or manifest['DatabaseHash'].lower() != sha(database) or 'configuration.json' not in manifest['Files'] or len(manifest['Files']) > 100000:
        raise ValueError('Incomplete backup identity')
    expected = set()
    for name, digest in manifest['Files'].items():
        p = ordinary(root / name)
        if not p.is_relative_to(root) or not re.fullmatch('[a-fA-F0-9]{64}', digest) or sha(p) != digest.lower():
            raise ValueError('Backup original differs: ' + name)
        expected.add(p.relative_to(root).as_posix())
    actual = set()
    for p in root.rglob('*'):
        ordinary(p)
        if p.is_file() and p.name != 'COMPLETED.json':
            actual.add(p.relative_to(root).as_posix())
        if len(actual) > 100000:
            raise ValueError('Backup inventory quota exceeded')
    if actual != expected:
        raise ValueError('Backup file inventory differs')
    return sha(manifest_file)


def json_lines(path):
    with path.open('rb') as stream:
        while True:
            line = stream.readline(2 * 1024 * 1024 + 1)
            if not line:
                return
            if len(line) > 2 * 1024 * 1024:
                raise ValueError('Evidence line exceeds bounded read')
            yield json.loads(line)


def reconcile_source(source, dispositions, collection, destination):
    """Loopback fixture's unique code is a correlation key, never entity admission."""
    if destination.exists():
        raise FileExistsError('Preserve original reconciliation')
    start = dt.datetime.fromisoformat(collection['NormalStart']); end = dt.datetime.fromisoformat(collection['NormalEnd'])
    db = sqlite3.connect(destination); issues = collections.Counter()
    deadline = time.monotonic() + 600
    db.set_progress_handler(lambda: time.monotonic() > deadline, 1000)
    db.execute('PRAGMA max_page_count=524288')  # bounded 2 GiB analysis database
    db.execute('CREATE TABLE source(position TEXT,code TEXT,writes INTEGER,PRIMARY KEY(position,code))')
    db.execute('CREATE TABLE receipts(id TEXT PRIMARY KEY,position TEXT,code TEXT,state TEXT,outcome TEXT)')
    db.execute('CREATE INDEX receipt_source ON receipts(position,code)')
    try:
        for item in json_lines(source):
            if time.monotonic() > deadline:
                raise TimeoutError('Source reconciliation exceeded ten minute budget')
            at = dt.datetime.fromisoformat(item['utc'])
            if not start <= at <= end:
                continue
            event = item.get('event')
            if event in ('input_scheduled', 'input_written_to_socket_buffer'):
                position, code = item['position'], item['barcode']
                if len(code) > 4096 or position not in ('left', 'right'):
                    raise ValueError('Source correlation contract differs')
                db.execute('INSERT INTO source VALUES(?,?,?) ON CONFLICT(position,code) DO UPDATE SET writes=writes+excluded.writes',
                           (position, code, int(event == 'input_written_to_socket_buffer')))
            elif event in ('input_not_written', 'generator_slots_missed', 'injected_camera_disconnect', 'injected_rcs_drop_after_receive', 'evidence_quota_exceeded', 'fixture_failed'):
                issues[event] += 1
        for item in json_lines(dispositions):
            codes = item.get('Codes')
            if not isinstance(codes, list) or len(codes) != 1:
                issues['ReceiptWithoutUniqueSyntheticCode'] += 1; continue
            db.execute('INSERT INTO receipts VALUES(?,?,?,?,?)', (item['Id'], item['Position'], codes[0], item['State'], item['Outcome']))
        db.commit()
        scalar = lambda sql: db.execute(sql).fetchone()[0]
        report = dict(SourceSha256=sha(source), Correlation='Independent loopback journal + unique position/barcode; socket write is NOT application acceptance',
                      Scheduled=scalar('SELECT count(*) FROM source'), Written=scalar('SELECT count(*) FROM source WHERE writes=1'),
                      DuplicateWrites=scalar('SELECT count(*) FROM source WHERE writes>1'), NotWritten=scalar('SELECT count(*) FROM source WHERE writes=0'),
                      MissingApplicationReceipt=scalar('SELECT count(*) FROM source s WHERE writes>0 AND NOT EXISTS(SELECT 1 FROM receipts r WHERE r.position=s.position AND r.code=s.code)'),
                      UnmatchedApplicationReceipt=scalar('SELECT count(*) FROM receipts r WHERE NOT EXISTS(SELECT 1 FROM source s WHERE r.position=s.position AND r.code=s.code AND s.writes>0)'),
                      DuplicateApplicationReceipts=scalar('SELECT count(*) FROM (SELECT position,code FROM receipts GROUP BY position,code HAVING count(*)>1)'),
                      UnsettledInputs=scalar("SELECT count(*) FROM receipts WHERE state IN ('Pending','Interrupted')"), Issues=dict(issues),
                      BoundaryPolicy='Inspect source/application delivery crossing normal start/end; never silently exclude mismatches')
        return report
    finally:
        db.close()


def analyze(args):
    collection = read_json(ordinary(args.collection) / 'collection.json')
    database = ordinary(args.database); output = ordinary(args.output)
    if output.exists():
        raise FileExistsError('Preserve prior analysis')
    # Explicit offline copy only. A live WAL database must use the application's complete backup first.
    if Path(str(database) + '-wal').exists():
        raise ValueError('Use a completed offline backup with its complete custody set, not a live WAL file')
    backup_identity = verify_backup(database)
    db = sqlite3.connect(database.as_uri() + '?mode=ro', uri=True)
    db.execute('PRAGMA query_only=ON'); deadline = time.monotonic() + 300
    db.set_progress_handler(lambda: time.monotonic() > deadline, 1000)
    try:
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('Database integrity failed; preserve originals')
        if db.execute('SELECT max(version) FROM platform_migrations').fetchone()[0] != 10:
            raise ValueError('Requires current DB10 backup; use a fresh data directory')
        if not collection.get('NormalStart') or not collection.get('NormalEnd'):
            raise ValueError('No complete observed normal interval to analyze')
        params = (collection['RuntimeId'].replace('-', ''), collection['NormalStart'], collection['NormalEnd'])
        # ISO strings are normalized to UTC by the runtime, but compare instants explicitly.
        where = 'session=? AND julianday(received_at)>=julianday(?) AND julianday(received_at)<=julianday(?)'
        counts = db.execute('SELECT position,state,count(*) FROM parcel_ingress WHERE ' + where + ' GROUP BY position,state', params).fetchall()
        latencies = {}
        for position in ('left', 'right'):
            timing = {}
            for metric in ('queue_ms', 'processing_ms'):
                n, maximum = db.execute(f'SELECT count({metric}),max({metric}) FROM parcel_ingress WHERE {where} AND position=?', (*params, position)).fetchone()
                timing[metric] = dict(Samples=n, Maximum=maximum)
                for label, fraction in [('P95', .95), ('P99', .99)]:
                    row = db.execute(f'SELECT {metric} FROM parcel_ingress WHERE {where} AND position=? AND {metric} IS NOT NULL ORDER BY {metric} LIMIT 1 OFFSET ?',
                                     (*params, position, max(0, math.ceil(n * fraction) - 1))).fetchone()
                    timing[metric][label] = row[0] if row else None
            latencies[position] = timing
        inventory = collections.Counter()
        with (output.parent / (output.stem + '-input-dispositions.jsonl')).open('x', encoding='utf-8') as lines:
            for identity, position, state, body, digest, detail in db.execute('SELECT id,position,state,body,body_hash,detail FROM parcel_ingress WHERE ' + where + ' ORDER BY received_at,id', params):
                if hashlib.sha256(body.encode('utf-8')).hexdigest().upper() != digest:
                    raise ValueError('Input original hash differs')
                decoded = json.loads(body); observation = decoded.get('Observation', {})
                outcome = json.loads(detail).get('Disposition') if state == 'Processed' and detail else state
                inventory[position + ':' + str(outcome)] += 1
                lines.write(json.dumps(dict(Id=identity, Position=position, State=state, Outcome=outcome,
                                           SourceEvent=observation.get('SourceEventId'), Epoch=observation.get('Epoch'),
                                           Sequence=observation.get('Sequence'), Codes=observation.get('Codes'), OriginalSha256=digest), ensure_ascii=False) + '\n')
        reconciliation = reconcile_source(ordinary(args.source_events), output.parent / (output.stem + '-input-dispositions.jsonl'), collection, output.with_suffix('.reconciliation.db'))
        report = dict(Status='ANALYZED_REVIEW_REQUIRED', SourceCommit=collection['SourceCommit'], DatabaseSha256=sha(database), BackupManifestSha256=backup_identity,
                      ActualNormalSeconds=collection['ActualNormalSeconds'], CollectionStatus=collection['Status'], Counts=counts,
                      Dispositions=dict(inventory), LatencyMilliseconds=latencies, Ui=collection['Ui'],
                      SourceDeliveryReconciliation=reconciliation,
                      Qualification='NOT_GRANTED; this analysis does not authenticate device/contract approvals or replace recovery tests')
        output.write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding='utf-8'); print(json.dumps(report, ensure_ascii=False))
    finally:
        db.close()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('collect')
    for name in ('plan', 'release', 'progress', 'output'):
        p.add_argument('--' + name, required=True)
    p.add_argument('--smoke-seconds', type=int, choices=range(1, 121), metavar='1..120', help='Explicit short script check; always SMOKE_ONLY')
    p = commands.add_parser('analyze')
    for name in ('collection', 'database', 'output', 'source-events'):
        p.add_argument('--' + name, required=True)
    args = parser.parse_args()
    (collect if args.command == 'collect' else analyze)(args)


if __name__ == '__main__':
    main()
