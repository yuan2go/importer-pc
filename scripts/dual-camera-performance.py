"""Passive runtime-progress collection and offline statistics; Python 3.10+, standard library only.

Never opens cameras, calls RCS, reads application DBs or changes deployment/configuration.
All artifacts are private local evidence. A scenario label is not hardware acceptance.
"""
import argparse
from collections import Counter, defaultdict
import csv
from datetime import datetime, timezone
import hashlib
import json
import math
import os
from pathlib import Path
import platform
import sqlite3
import time


COUNTERS = ('AcquireCalls', 'NoData', 'Frames', 'DecodeStarted', 'DecodeCompleted',
            'EmptyFrames', 'Candidates', 'Delivered', 'Rejected', 'RejectedFrames',
            'SupersededDecodeFrames', 'OriginalFrameCopies', 'SdkFrameNumberGaps', 'SdkGapEvents')
FRAME_METRICS = ('CopyMs', 'ReleaseMs', 'DecodeMs', 'DecodeQueueMs', 'OriginalCopyMs',
                 'ObservedToReceiverMs', 'HandoffMs', 'ClearMs', 'HostDeliveryToDecodeMs')


def dump(path, value):
    path.write_text(json.dumps(value, ensure_ascii=False, indent=2, allow_nan=False) + '\n', encoding='utf-8')


def new_root(path):
    if not path.is_absolute() or (os.name == 'nt' and path.resolve().drive.lower() != 'd:'):
        raise ValueError('Use a new absolute private D-drive output directory')
    path.mkdir(parents=True, exist_ok=False)
    return path


def utc():
    return datetime.now(timezone.utc).isoformat()


def timestamp(value):
    return datetime.fromisoformat(value.replace('Z', '+00:00')).timestamp()


def collect(args):
    if not args.progress.is_absolute() or args.progress.name != 'runtime-progress.json':
        raise ValueError('Explicit absolute runtime-progress.json required')
    if args.left_device == args.right_device:
        raise ValueError('Left and right device IDs must differ')
    if args.output.resolve().is_relative_to(args.progress.parent.resolve()):
        raise ValueError('Evidence output must be outside the application runtime directory')
    root = new_root(args.output)
    manifest = dict(Schema=1, StartedAt=utc(), Scenario=args.scenario,
                    Devices=dict(left=args.left_device, right=args.right_device),
                    ProgressFile=str(args.progress), DurationSeconds=args.duration,
                    WarmupSeconds=args.warmup, IntervalSeconds=args.interval,
                    MaxMiB=args.max_mib,
                    Platform=platform.platform(), Python=platform.python_version(),
                    CollectorSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                    Complete=False, Policy='Passive local evidence; never auto upload; no device/port/configuration operations')
    dump(root / 'capture.json', manifest)
    started = time.perf_counter()
    finish = started + args.warmup + args.duration
    count = errors = written = 0
    try:
        with (root / 'samples.jsonl').open('x', encoding='utf-8') as output:
            while True:
                before = time.perf_counter()
                entry = dict(CollectedAt=utc(), ElapsedSeconds=before - started,
                             Phase='Warmup' if before - started < args.warmup else 'Measure')
                try:
                    with args.progress.open('rb') as source:
                        raw = source.read(16 * 1024 * 1024 + 1)
                    if len(raw) > 16 * 1024 * 1024:
                        raise ValueError('Progress size quota')
                    snapshot = json.loads(raw.decode('utf-8-sig'))
                    if not isinstance(snapshot.get('Progress'), dict):
                        raise ValueError('Invalid progress envelope')
                    entry.update(SnapshotSha256=hashlib.sha256(raw).hexdigest(), Snapshot=snapshot)
                    count += 1
                except (OSError, ValueError, UnicodeError) as error:
                    entry['ReadError'] = type(error).__name__
                    errors += 1  # Never substitute the previous snapshot as a fresh sample.
                entry['CollectorReadMs'] = (time.perf_counter() - before) * 1000
                line = json.dumps(entry, ensure_ascii=False, allow_nan=False) + '\n'
                output.write(line)
                written += len(line.encode('utf-8'))
                output.flush()
                if written >= args.max_mib * 1024 * 1024:
                    manifest['QuotaReached'] = True
                    break
                if before >= finish:
                    break
                time.sleep(max(0, min(args.interval - (time.perf_counter() - before), finish - time.perf_counter())))
        manifest['Complete'] = not manifest.get('QuotaReached', False)
    except KeyboardInterrupt:
        manifest['Interrupted'] = True
    finally:
        manifest.update(EndedAt=utc(), Snapshots=count, ReadErrors=errors, Bytes=written,
                        SamplesSha256=sha_file(root / 'samples.jsonl'))
        dump(root / 'capture.json', manifest)
    print(str(root / 'capture.json'))
    return 0 if manifest['Complete'] else 130


def sha_file(path):
    h = hashlib.sha256()
    with path.open('rb') as source:
        for block in iter(lambda: source.read(1024 * 1024), b''):
            h.update(block)
    return h.hexdigest()


class Analysis:
    """Disk-backed exact sample quantiles and correlation, independent of the application's DB."""
    def __init__(self, database, devices, stale_seconds=5):
        self.db = sqlite3.connect(database)
        self.db.executescript('''
            CREATE TABLE metrics(scope TEXT, name TEXT, value REAL);
            CREATE INDEX metric_values ON metrics(scope,name,value);
            CREATE TABLE frames(runtime TEXT, device TEXT, epoch TEXT, seq INTEGER, position TEXT,
                observation TEXT, data TEXT, PRIMARY KEY(runtime,device,epoch,seq));
            CREATE INDEX frame_observation ON frames(runtime,observation);
            CREATE TABLE events(runtime TEXT, seq INTEGER, position TEXT, kind TEXT, observation TEXT,
                operation TEXT, request TEXT, state TEXT, data TEXT, PRIMARY KEY(runtime,seq));
            CREATE INDEX event_observation ON events(runtime,observation,kind);
            CREATE TABLE presentation(runtime TEXT, observation TEXT, stage TEXT, position TEXT, data TEXT,
                PRIMARY KEY(runtime,observation,stage));
        ''')
        self.devices = {v: k for k, v in devices.items()}
        self.stale_seconds = stale_seconds
        self.issues = Counter()
        self.counters = defaultdict(Counter)
        self.connections = {}
        self.runtimes = {}
        self.flow_cursor = {}
        self.flow_counts = {}
        self.presentation_total = {}
        self.stage_totals = {}
        self.intervals = Counter()
        self.versions = set()
        self.configurations = defaultdict(set)
        self.frame_sizes = defaultdict(set)
        self.last_hash = None
        self.last_change = None
        self.unique = 0
        self.max_resource = {}

    def metric(self, scope, name, value, pooled=False):
        if value is None:
            return
        if not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
            self.issues['INVALID_NUMERIC_METRIC'] += 1
            return
        self.db.execute('INSERT INTO metrics VALUES(?,?,?)', (scope, name, value))
        if pooled:
            self.db.execute('INSERT INTO metrics VALUES(?,?,?)', ('combined', name, value))

    def feed(self, entry):
        if entry.get('Phase') != 'Measure':
            return
        if 'ReadError' in entry:
            self.issues['COLLECTOR_READ_ERROR'] += 1
            return
        elapsed = entry['ElapsedSeconds']
        snapshot = entry['Snapshot']
        p = snapshot['Progress']
        r = snapshot.get('Resource', {})
        identity = (p.get('RuntimeId'), p.get('At'))
        if identity == self.last_hash:
            if elapsed - self.last_change > self.stale_seconds:
                self.issues['STALE_PROGRESS'] += 1
            return
        self.last_hash, self.last_change = identity, elapsed
        initial = self.unique == 0
        self.unique += 1
        runtime = p['RuntimeId']
        previous = self.runtimes.get(runtime)
        if previous is None and not initial:
            self.issues['RUNTIME_CHANGED'] += 1
        duration = None
        if previous:
            if 'RuntimeElapsedMs' in p and 'RuntimeElapsedMs' in previous[0]:
                duration = (p['RuntimeElapsedMs'] - previous[0]['RuntimeElapsedMs']) / 1000
            else:
                duration = timestamp(p['At']) - timestamp(previous[0]['At'])
                self.issues['LEGACY_WALL_CLOCK_RATE'] += 1
            if duration <= 0:
                self.issues['NON_MONOTONIC_RUNTIME'] += 1
                duration = None
            elif abs(timestamp(p['At']) - timestamp(previous[0]['At']) - duration) > 2:
                self.issues['UTC_CLOCK_CHANGE'] += 1
            if duration and 'CpuTotalMs' in r and 'CpuTotalMs' in previous[1]:
                cpu = r['CpuTotalMs'] - previous[1]['CpuTotalMs']
                self.metric('process', 'CpuOneCorePercent', cpu / (duration * 10))
                self.metric('process', 'CpuCapacityPercent', cpu / (duration * 10 * max(1, r.get('LogicalProcessors', 1))))
        self.runtimes[runtime] = ({k: p[k] for k in ('At', 'RuntimeElapsedMs') if k in p},
                                  {k: r[k] for k in ('CpuTotalMs',) if k in r})
        self.versions.add(p.get('SourceVersion') or 'Unavailable')
        if p.get('SchemaVersion', 0) < 2:
            self.issues['LEGACY_TELEMETRY'] += 1
        if p.get('Failure') or p.get('SupervisionFailure'):
            self.issues['RUNTIME_OR_SUPERVISION_FAILURE'] += 1
        for name in ('WorkingSet64', 'PrivateMemorySize64', 'ManagedBytes', 'HandleCount', 'Threads', 'WalBytes', 'DatabaseBytes'):
            if isinstance(r.get(name), (int, float)):
                self.max_resource[name] = max(self.max_resource.get(name, 0), r[name])
        activity = {}
        for c in p.get('Connections', []):
            device = c['DeviceId']
            position = self.devices.get(device)
            if position is None:
                self.issues['UNMAPPED_CONNECTION'] += 1
                continue
            a = c.get('Acquisition')
            if not a:
                self.issues['ACQUISITION_DIAGNOSTICS_UNAVAILABLE'] += 1
                continue
            perf = c.get('Performance') or a.get('Performance') or {}
            if a.get('Epoch', c['Epoch']) != c['Epoch'] or perf.get('Epoch', c['Epoch']) != c['Epoch']:
                self.issues['EPOCH_SNAPSHOT_TRANSITION'] += 1
                continue
            key = (runtime, device, c['Epoch'])
            old = self.connections.get(key)
            if old is None and any(k[:2] == key[:2] for k in self.connections):
                self.issues['CAMERA_EPOCH_CHANGED'] += 1
            baseline = initial and old is None
            if old is not None or not baseline:
                delta = {}
                for name in COUNTERS:
                    if name not in a:
                        continue
                    value = a[name] - (old.get(name, 0) if old else 0)
                    if value < 0:
                        self.issues['COUNTER_RESET_WITHIN_EPOCH'] += 1
                        continue
                    delta[name] = value
                    self.counters[position][name] += value
                activity[position] = delta.get('Frames', 0) > 0
                if duration and old:
                    self.metric(position, 'CaptureFramesPerSecond', delta.get('Frames', 0) / duration)
                    self.metric(position, 'DecodeFramesPerSecond', delta.get('DecodeCompleted', 0) / duration)
            self.connections[key] = {name: a[name] for name in COUNTERS if name in a}
            self.connections[key]['Performance'] = dict(Acquisition=dict(FrameTraceCount=
                ((perf.get('Acquisition') or {}).get('FrameTraceCount', 0))))
            if a.get('Failure') or a.get('State') == 'Faulted':
                self.issues['CAMERA_FAULT'] += 1
            if a.get('DecodePaused'):
                self.issues['DECODE_PAUSED'] += 1
            if a.get('WaitingForOriginalCapacity'):
                self.issues['ORIGINAL_CAPACITY_WAIT'] += 1
            if perf.get('DecoderConfiguration'):
                self.configurations[device].add(json.dumps(perf['DecoderConfiguration'], sort_keys=True))
            timing = perf.get('Acquisition')
            if timing is None:
                self.issues['FRAME_TIMINGS_UNAVAILABLE'] += 1
                continue
            traces = timing.get('RecentFrames', [])
            old_timing = ((old or {}).get('Performance') or {}).get('Acquisition') or {}
            cursor = old_timing.get('FrameTraceCount', 0)
            for layer in ('Acquisition', 'Decoder'):
                for stage, values in (perf.get(layer) or {}).get('Stages', {}).items():
                    stage_key = (*key, layer, stage)
                    prior = self.stage_totals.get(stage_key)
                    if prior and (values['Count'] < prior[1]['Count'] or values['TotalMs'] < prior[1]['TotalMs']):
                        self.issues['STAGE_COUNTER_RESET'] += 1
                    first = values if baseline else dict(Count=0, TotalMs=0)
                    self.stage_totals[stage_key] = (prior[0] if prior else first, values)
            if baseline:
                continue
            new = [f for f in traces if f.get('TraceSequence', 0) > cursor]
            expected = timing.get('FrameTraceCount', 0) - cursor
            if expected < 0:
                self.issues['TRACE_COUNTER_RESET'] += 1
                continue
            if expected > len(new):
                self.issues['FRAME_WINDOW_LOST_TRACES'] += expected - len(new)
            for frame in new:
                if frame.get('Width', 0) > 0 and frame.get('Height', 0) > 0:
                    self.frame_sizes[position].add((frame['Width'], frame['Height']))
                self.db.execute('INSERT INTO frames VALUES(?,?,?,?,?,?,?)', (*key, frame['TraceSequence'], position,
                                frame.get('ObservationId'), json.dumps(frame)))
                # Superseded/paused/rejected traces are not zero-duration successful decodes.
                if frame.get('DecodeAttempted') and not frame.get('RejectedFrame') and not frame.get('SkippedBeforeDecode'):
                    for name in FRAME_METRICS:
                        if name in ('ObservedToReceiverMs', 'HandoffMs', 'OriginalCopyMs') and frame.get('Candidates', 0) == 0:
                            continue
                        self.metric(position, name, frame.get(name), pooled=True)
                if frame.get('RejectedFrame'):
                    self.issues['DECODE_REJECTED_FRAME'] += 1
        if previous:
            self.intervals['MeasuredIntervals'] += 1
            if activity.get('left') and activity.get('right'):
                self.intervals['BothCamerasCapturedWithinInterval'] += 1
        for worker in p.get('Workers', []):
            position = worker.get('Position', 'Unassigned')
            self.metric(position, 'ApplicationQueueDepth', worker.get('QueueDepth'))
            if worker.get('Failure'):
                self.issues['APPLICATION_WORKER_FAILURE'] += 1
        self.flow(runtime, p.get('Flow'), initial)
        self.presentation(runtime, p.get('Presentation') or {}, initial)
        self.db.commit()

    def flow(self, runtime, flow, baseline):
        if flow is None:
            self.issues['FLOW_TIMINGS_UNAVAILABLE'] += 1
            return
        cursor = self.flow_cursor.get(runtime, 0)
        self.flow_cursor[runtime] = flow['Sequence']
        for position, counts in flow['Counts'].items():
            key = (runtime, position)
            old = self.flow_counts.get(key, counts if baseline else {})
            for name, value in counts.items():
                delta = value - old.get(name, 0)
                if delta < 0:
                    self.issues['FLOW_COUNTER_RESET'] += 1
                else:
                    self.counters[position][name] += delta
            self.flow_counts[key] = counts
        if baseline:
            return
        events = [e for e in flow['Recent'] if e['Sequence'] > cursor]
        if flow['Sequence'] - cursor > len(events):
            self.issues['FLOW_WINDOW_LOST_EVENTS'] += flow['Sequence'] - cursor - len(events)
        for event in events:
            position, kind = event['Position'], event['Kind']
            self.db.execute('INSERT INTO events VALUES(?,?,?,?,?,?,?,?,?)',
                            (runtime, event['Sequence'], position, kind, event['ObservationId'], event.get('OperationId'),
                             event.get('RequestId'), event.get('State'), json.dumps(event)))
            device = event.get('DeviceId')
            if device and self.devices.get(device) != position:
                self.issues['FLOW_DEVICE_POSITION_MISMATCH'] += 1
            if device is None:
                self.issues['NON_CAMERA_FLOW_EVENT'] += 1
            if kind in ('IngressQueue', 'IngressProcessed', 'RcsSubmitCompleted', 'RcsQueryCompleted'):
                self.metric(position, kind + 'Ms', event.get('ElapsedMs'), pooled=True)
            if kind == 'RcsSubmitCompleted':
                self.metric(position, 'DecodeToRcsResultMs', event.get('SinceDecodeMs'), pooled=True)
                if event.get('State') != 'Accepted':
                    self.issues['RCS_' + str(event.get('State', 'Unknown')).upper()] += 1
                if event.get('LocalFailure') == 'DeadlineExpired':
                    self.issues['RCS_DEADLINE_EXPIRED'] += 1

    def presentation(self, runtime, presentation, baseline):
        new = 0
        for m in presentation.get('Recent', []):
            inserted = self.db.execute('INSERT OR IGNORE INTO presentation VALUES(?,?,?,?,?)',
                                      (runtime, m['ObservationId'], m['Stage'], m['PositionId'], json.dumps(m))).rowcount
            if inserted and not baseline:
                new += 1
                self.metric(m['PositionId'], m['Stage'] + 'Ms', m.get('ReadToCompositionMs'), pooled=True)
        total = presentation.get('Total')
        if total is not None:
            old = self.presentation_total.get(runtime, total if baseline else 0)
            if not baseline and total - old > new:
                self.issues['PRESENTATION_WINDOW_LOST_EVENTS'] += total - old - new
            self.presentation_total[runtime] = total
        elif not baseline:
            self.issues['PRESENTATION_COVERAGE_UNAVAILABLE'] += 1

    def quantiles(self):
        results = []
        for scope, name, count, minimum, mean, maximum in self.db.execute(
                'SELECT scope,name,count(*),min(value),avg(value),max(value) FROM metrics GROUP BY scope,name'):
            row = dict(Scope=scope, Metric=name, Count=count, Min=minimum, Mean=mean, Max=maximum)
            for label, fraction in (('P50', .50), ('P95', .95), ('P99', .99)):
                row[label] = self.db.execute('SELECT value FROM metrics WHERE scope=? AND name=? ORDER BY value LIMIT 1 OFFSET ?',
                                            (scope, name, max(0, math.ceil(count * fraction) - 1))).fetchone()[0]
            results.append(row)
        return results

    def correlations(self):
        # Stream out an exact join by runtime + observation, with device/epoch/frame checks.
        for runtime, seq, position, kind, obs, op, req, state, data in self.db.execute(
                "SELECT * FROM events WHERE kind='RcsSubmitCompleted' ORDER BY runtime,seq"):
            event = json.loads(data)
            frames = self.db.execute('SELECT device,epoch,position,data FROM frames WHERE runtime=? AND observation=?', (runtime, obs)).fetchall()
            metric = self.db.execute("SELECT position,data FROM presentation WHERE runtime=? AND observation=? AND stage='SlotComposed'", (runtime, obs)).fetchone()
            exact_frame = len(frames) == 1 and frames[0][0] == event.get('DeviceId') and frames[0][1] == event.get('Epoch') and frames[0][2] == position
            frame = json.loads(frames[0][3]) if exact_frame else None
            if frame and 'mvs.frame.' + str(frame['Number']) != event.get('SourceEventId'):
                exact_frame = False
            if not exact_frame:
                self.issues['RCS_FRAME_NOT_CORRELATED'] += 1
            if metric and metric[0] != position:
                self.issues['SLOT_POSITION_MISMATCH'] += 1
            read_to_slot = json.loads(metric[1])['ReadToCompositionMs'] if metric and metric[0] == position else None
            result_to_slot = read_to_slot - event['SinceDecodeMs'] if read_to_slot is not None and event.get('SinceDecodeMs') is not None else None
            if result_to_slot is not None and result_to_slot < 0:
                self.issues['SLOT_BEFORE_RESULT_OR_CLOCK_MISMATCH'] += 1
                result_to_slot = None
            self.metric(position, 'RcsResultToSlotComposedMs', result_to_slot, pooled=True)
            host_to_decode = frame.get('HostDeliveryToDecodeMs') if exact_frame else None
            host_to_rcs = host_to_decode + event['SinceDecodeMs'] if host_to_decode is not None and event.get('SinceDecodeMs') is not None else None
            host_to_slot = host_to_decode + read_to_slot if host_to_decode is not None and read_to_slot is not None else None
            self.metric(position, 'HostFrameToRcsResultMs', host_to_rcs, pooled=True)
            self.metric(position, 'HostFrameToSlotComposedMs', host_to_slot, pooled=True)
            yield dict(RuntimeId=runtime, Position=position, ObservationId=obs, OperationId=op, RequestId=req, State=state,
                       FrameCorrelated=exact_frame, FrameNumber=frame['Number'] if exact_frame else None,
                       DecodeMs=frame.get('DecodeMs') if exact_frame else None,
                       DecodeQueueMs=frame.get('DecodeQueueMs') if exact_frame else None,
                       HandoffMs=frame.get('HandoffMs') if exact_frame else None,
                       ObservedToReceiverMs=frame.get('ObservedToReceiverMs') if exact_frame else None,
                       HostFrameToRcsMs=host_to_rcs, HostFrameToSlotMs=host_to_slot,
                       RcsMs=event.get('ElapsedMs'), DecodeToRcsMs=event.get('SinceDecodeMs'), DecodeToSlotMs=read_to_slot,
                       RcsResultToSlotMs=result_to_slot, SlotComposed=read_to_slot is not None)


def write_csv(path, rows, fields):
    with path.open('x', encoding='utf-8-sig', newline='') as output:
        writer = csv.DictWriter(output, fieldnames=fields)
        writer.writeheader()
        for row in rows:
            # Prevent spreadsheet formulas in private device/version identifiers.
            writer.writerow({k: "'" + v if isinstance(v, str) and v.startswith(('=', '+', '-', '@')) else v for k, v in row.items()})


def analyze(args):
    source = args.capture
    manifest = json.loads((source / 'capture.json').read_text(encoding='utf-8-sig'))
    if sha_file(source / 'samples.jsonl') != manifest.get('SamplesSha256'):
        raise ValueError('Capture hash mismatch; do not silently analyze changed evidence')
    root = new_root(args.output)
    a = Analysis(root / 'analysis.sqlite', manifest['Devices'], args.stale_seconds)
    if not manifest.get('Complete'):
        a.issues['INCOMPLETE_CAPTURE'] += 1
    try:
        with (source / 'samples.jsonl').open(encoding='utf-8-sig') as stream:
            for number, line in enumerate(stream, 1):
                try:
                    a.feed(json.loads(line))
                except (ValueError, KeyError, TypeError) as error:
                    raise ValueError('Invalid sample line ' + str(number)) from error
        fields = ('RuntimeId', 'Position', 'ObservationId', 'OperationId', 'RequestId', 'State', 'FrameCorrelated',
                  'FrameNumber', 'DecodeMs', 'DecodeQueueMs', 'HandoffMs', 'ObservedToReceiverMs',
                  'HostFrameToRcsMs', 'HostFrameToSlotMs', 'RcsMs', 'DecodeToRcsMs', 'DecodeToSlotMs', 'RcsResultToSlotMs', 'SlotComposed')
        write_csv(root / 'correlations.csv', a.correlations(), fields)
        stages = []
        for key, (first, last) in a.stage_totals.items():
            count = last['Count'] - first['Count']
            total = last['TotalMs'] - first['TotalMs']
            stages.append(dict(RuntimeId=key[0], DeviceId=key[1], Epoch=key[2], Layer=key[3], Stage=key[4],
                               Count=count, MeanMs=total / count if count > 0 else None,
                               LatestWindowCount=last['WindowCount'], LatestWindowP50Ms=last['P50Ms'],
                               LatestWindowP95Ms=last['P95Ms'], LatestWindowP99Ms=last['P99Ms']))
        dump(root / 'stages.json', stages)
        stats = a.quantiles()
        write_csv(root / 'quantiles.csv', stats, ('Scope', 'Metric', 'Count', 'Min', 'Mean', 'Max', 'P50', 'P95', 'P99'))
        for position in ('left', 'right'):
            if not a.counters[position].get('Frames'):
                a.issues['NO_CAPTURE_PROGRESS_' + position.upper()] += 1
        if a.unique < 2:
            a.issues['INSUFFICIENT_DISTINCT_SNAPSHOTS'] += 1
        outstanding = a.db.execute("SELECT count(*) FROM events s WHERE kind='RcsSubmitStarted' AND NOT EXISTS(SELECT 1 FROM events e WHERE e.runtime=s.runtime AND e.request=s.request AND e.kind='RcsSubmitCompleted')").fetchone()[0]
        if outstanding:
            a.issues['RCS_STARTED_WITHOUT_COLLECTED_RESULT'] += outstanding
        computed = {}
        for position in ('left', 'right'):
            c = a.counters[position]
            decoded, frames = c.get('DecodeCompleted', 0), c.get('Frames', 0)
            coded = decoded - c['EmptyFrames'] if 'DecodeCompleted' in c and 'EmptyFrames' in c else None
            computed[position] = dict(FramesWithCandidates=coded,
                CandidateFrameFractionOfDecoded=coded / decoded if decoded and coded is not None else None,
                SupersededFractionOfCaptured=c['SupersededDecodeFrames'] / frames if frames and 'SupersededDecodeFrames' in c else None)
        summary = dict(Schema=1, Status='MEASURED_NOT_ACCEPTANCE', PerformanceAcceptance='NOT_EVALUATED_NO_SITE_SLA',
                       EvidenceQuality='REVIEW_ANOMALIES' if a.issues else 'NO_COLLECTION_ANOMALIES_DETECTED',
                       Scenario=manifest['Scenario'], SourceVersions=sorted(a.versions), UniqueSnapshots=a.unique,
                       DecoderConfigurations={k: [json.loads(v) for v in sorted(values)] for k, values in a.configurations.items()},
                       FrameSizes={k: sorted(values) for k, values in a.frame_sizes.items()},
                       Counters={k: dict(v) for k, v in a.counters.items()}, Computed= computed, ResourceMax=a.max_resource,
                       Intervals=dict(a.intervals), Anomalies=dict(sorted(a.issues.items())), Quantiles=stats,
                       ExposureFrameAge='Unavailable: SDK timestamps are not calibrated to host clock',
                       PackageRecognitionRate='Unavailable without independently recorded placements and verified expected codes',
                       Boundary='Frame replacement is not package loss; Accepted is not physical completion; CPU is whole-process',
                       CaptureSha256=sha_file(source / 'samples.jsonl'))
        dump(root / 'summary.json', summary)
        a.db.commit()
    finally:
        a.db.close()
    print(str(root / 'summary.json'))
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    c = sub.add_parser('collect')
    c.add_argument('--progress', type=Path, required=True)
    c.add_argument('--output', type=Path, required=True)
    c.add_argument('--scenario', choices=('left', 'right', 'dual'), required=True)
    c.add_argument('--left-device', required=True)
    c.add_argument('--right-device', required=True)
    c.add_argument('--duration', type=float, default=300)
    c.add_argument('--warmup', type=float, default=30)
    c.add_argument('--interval', type=float, default=1)
    c.add_argument('--max-mib', type=float, default=2048, help='Stop and retain partial capture at this output quota')
    c.set_defaults(run=collect)
    a = sub.add_parser('analyze')
    a.add_argument('--capture', type=Path, required=True)
    a.add_argument('--output', type=Path, required=True)
    a.add_argument('--stale-seconds', type=float, default=5, help='Diagnostic freshness flag, not a performance acceptance line')
    a.set_defaults(run=analyze)
    args = parser.parse_args()
    if args.command == 'collect' and not (0 < args.duration <= 86400 and 0 <= args.warmup <= 3600 and .2 <= args.interval <= 60 and 0 < args.max_mib <= 1048576):
        parser.error('duration (0,86400], warmup [0,3600], interval [.2,60], max-mib (0,1048576] required')
    if args.command == 'analyze' and args.stale_seconds <= 0:
        parser.error('stale-seconds must be positive')
    return args.run(args)


if __name__ == '__main__':
    raise SystemExit(main())
