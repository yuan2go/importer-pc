import argparse
from copy import deepcopy
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('performance', Path(__file__).with_name('dual-camera-performance.py'))
p = importlib.util.module_from_spec(spec)
spec.loader.exec_module(p)


def frame(seq, observation=None, skipped=False, decode=100):
    return dict(TraceSequence=seq, Number=seq, ObservationId=observation, DecodeAttempted=not skipped,
                SkippedBeforeDecode=skipped, RejectedFrame=False, Candidates=1 if observation else 0,
                Delivered=bool(observation), DecodeMs=decode, DecodeQueueMs=5, CopyMs=2, ReleaseMs=1,
                OriginalCopyMs=3, HandoffMs=4, ObservedToReceiverMs=110, ClearMs=1, HostDeliveryToDecodeMs=110)


def event(seq, kind, observation='obs-L', position='left', state=None, elapsed=None):
    return dict(Sequence=seq, Position=position, Kind=kind, ObservationId=observation,
                DeviceId='camera-L' if position == 'left' else 'camera-R', Epoch='epoch-1',
                SourceEventId='mvs.frame.1', OperationId='op-' + observation, RequestId='req-' + observation,
                State=state, LocalFailure=None, ElapsedMs=elapsed, SinceDecodeMs=150)


def sample(n, runtime='runtime-1', epoch='epoch-1', frames=None, events=None, presentation=None):
    frames = frames or []
    events = events or []
    connections = []
    for device in ('camera-L', 'camera-R'):
        traces = frames if device == 'camera-L' else []
        connections.append(dict(DeviceId=device, Epoch=epoch, Acquisition=dict(
            Frames=n, DecodeCompleted=n, DecodeStarted=n, SupersededDecodeFrames=n * 2,
            Delivered=n, RejectedFrames=0, Performance=dict(Acquisition=dict(
                FrameTraceCount=max([f['TraceSequence'] for f in traces] + [0]), RecentFrames=traces,
                Stages={'sdk.acquire.frame': dict(Count=n, TotalMs=n * 2, WindowCount=min(n, 512), P50Ms=2, P95Ms=2, P99Ms=2)})))))
    return dict(Phase='Measure', ElapsedSeconds=n, Snapshot=dict(Progress=dict(
        SchemaVersion=2, RuntimeId=runtime, At=f'2026-11-03T01:00:{n:02d}+00:00', RuntimeElapsedMs=n * 1000,
        SourceVersion='1.0.0+synthetic', Connections=connections, Workers=[],
        Flow=dict(Sequence=max([e['Sequence'] for e in events] + [0]), Counts={}, Recent=events),
        Presentation=dict(Total=len(presentation or []), Recent=presentation or [])),
        Resource=dict(CpuTotalMs=n * 500, LogicalProcessors=4, WorkingSet64=1000)))


class Performance(unittest.TestCase):
    def setUp(self):
        self.a = p.Analysis(':memory:', dict(left='camera-L', right='camera-R'))

    def tearDown(self):
        self.a.db.close()

    def test_exact_nearest_rank_and_pooled_quantiles(self):
        for i in range(1, 101):
            self.a.metric('left' if i <= 50 else 'right', 'DecodeMs', i, pooled=True)
        q = next(row for row in self.a.quantiles() if row['Scope'] == 'combined')
        self.assertEqual((q['Count'], q['P50'], q['P95'], q['P99']), (100, 50, 95, 99))

    def test_baseline_warmup_duplicate_overlap_and_skipped_not_zero_decode(self):
        warmup = sample(0, frames=[frame(1, decode=9999)])
        warmup['Phase'] = 'Warmup'
        self.a.feed(warmup)
        baseline = sample(1, frames=[frame(1, decode=9999)])
        self.a.feed(baseline)
        second = sample(2, frames=[frame(1, decode=9999), frame(2, skipped=True), frame(3, 'obs-L', decode=123)])
        self.a.feed(second)
        self.a.feed(deepcopy(second))
        self.a.feed(sample(3, frames=second['Snapshot']['Progress']['Connections'][0]['Acquisition']['Performance']['Acquisition']['RecentFrames']))
        q = next(row for row in self.a.quantiles() if row['Scope'] == 'left' and row['Metric'] == 'DecodeMs')
        self.assertEqual((q['Count'], q['Mean']), (1, 123))
        self.assertEqual(self.a.counters['left']['Frames'], 2)
        stage = next(v for k, v in self.a.stage_totals.items() if k[-1] == 'sdk.acquire.frame')
        self.assertEqual(stage[1]['Count'] - stage[0]['Count'], 2)
        self.assertEqual(self.a.unique, 3)

    def test_window_loss_epoch_runtime_and_counter_reset(self):
        self.a.feed(sample(0))
        self.a.feed(sample(1, frames=[frame(600)]))
        self.assertEqual(self.a.issues['FRAME_WINDOW_LOST_TRACES'], 599)
        self.a.feed(sample(2, epoch='epoch-2', frames=[frame(1)]))
        self.a.feed(sample(3, runtime='runtime-2', frames=[frame(1)]))
        self.assertEqual(self.a.issues['CAMERA_EPOCH_CHANGED'], 2)
        self.assertEqual(self.a.issues['RUNTIME_CHANGED'], 1)
        reset = sample(4, runtime='runtime-2')
        reset['Snapshot']['Progress']['Connections'][0]['Acquisition']['Frames'] = 0
        self.a.feed(reset)
        self.assertGreater(self.a.issues['COUNTER_RESET_WITHIN_EPOCH'], 0)

    def test_flow_frame_and_slot_join_never_crosses_position(self):
        self.a.feed(sample(0))
        events = [event(1, 'InputAdmitted'), event(2, 'RcsSubmitStarted'), event(3, 'RcsSubmitCompleted', state='Accepted', elapsed=40)]
        composed = dict(ObservationId='obs-L', PositionId='left', Stage='SlotComposed', ReadToCompositionMs=200)
        self.a.feed(sample(1, frames=[frame(1, 'obs-L')], events=events, presentation=[composed]))
        joined = list(self.a.correlations())
        self.assertTrue(joined[0]['FrameCorrelated'])
        self.assertEqual(joined[0]['RcsResultToSlotMs'], 50)
        self.assertEqual(joined[0]['HostFrameToRcsMs'], 260)
        self.assertEqual(joined[0]['HostFrameToSlotMs'], 310)
        self.a.db.execute("UPDATE presentation SET position='right'")
        bad = list(self.a.correlations())[0]
        self.assertIsNone(bad['DecodeToSlotMs'])
        self.assertEqual(self.a.issues['SLOT_POSITION_MISMATCH'], 1)

    def test_same_placement_query_and_deadline_are_separate(self):
        self.a.feed(sample(0))
        events = [event(1, 'SamePlacement'), event(2, 'RcsSubmitCompleted', state='Unknown', elapsed=5000),
                  event(3, 'RcsQueryCompleted', state='Accepted', elapsed=12)]
        events[1]['LocalFailure'] = 'DeadlineExpired'
        self.a.feed(sample(1, events=events))
        self.assertEqual(self.a.issues['RCS_UNKNOWN'], 1)
        self.assertEqual(self.a.issues['RCS_DEADLINE_EXPIRED'], 1)
        q = {(r['Scope'], r['Metric']): r for r in self.a.quantiles()}
        self.assertEqual(q['left', 'RcsSubmitCompletedMs']['Mean'], 5000)
        self.assertEqual(q['left', 'RcsQueryCompletedMs']['Mean'], 12)
        self.assertNotIn(('left', 'SamePlacementMs'), q)

    def test_legacy_and_read_errors_do_not_become_pass_or_zero_timings(self):
        old = sample(0)
        old['Snapshot']['Progress'].pop('SchemaVersion')
        old['Snapshot']['Progress'].pop('Flow')
        for c in old['Snapshot']['Progress']['Connections']:
            c['Acquisition'].pop('Performance')
        self.a.feed(old)
        self.a.feed(dict(Phase='Measure', ReadError='PermissionError'))
        self.assertEqual(self.a.issues['LEGACY_TELEMETRY'], 1)
        self.assertEqual(self.a.issues['FLOW_TIMINGS_UNAVAILABLE'], 1)
        self.assertEqual(self.a.issues['FRAME_TIMINGS_UNAVAILABLE'], 2)
        self.assertEqual(self.a.issues['COLLECTOR_READ_ERROR'], 1)
        self.assertEqual(self.a.quantiles(), [])

    def test_cpu_normalization_and_stale_progress(self):
        self.a.feed(sample(0))
        s = sample(1)
        self.a.feed(s)
        stale = deepcopy(s)
        stale['ElapsedSeconds'] = 8
        self.a.feed(stale)
        q = {(r['Scope'], r['Metric']): r for r in self.a.quantiles()}
        self.assertEqual(q['process', 'CpuOneCorePercent']['Mean'], 50)
        self.assertEqual(q['process', 'CpuCapacityPercent']['Mean'], 12.5)
        self.assertEqual(self.a.issues['STALE_PROGRESS'], 1)
        self.assertEqual(self.a.intervals['BothCamerasCapturedWithinInterval'], 1)

    def test_cli_collection_and_analysis_only_touch_explicit_output(self):
        with tempfile.TemporaryDirectory(prefix='ipc-performance-') as temp:
            root = Path(temp)
            (root / 'source').mkdir()
            progress = root / 'source/runtime-progress.json'
            p.dump(progress, sample(0)['Snapshot'])
            before = p.sha_file(progress)
            script = str(Path(p.__file__))
            subprocess.run([sys.executable, script, 'collect', '--progress', str(progress), '--output', str(root / 'capture'),
                            '--scenario', 'dual', '--left-device', 'camera-L', '--right-device', 'camera-R',
                            '--duration', '.3', '--warmup', '0', '--interval', '.2'], check=True, capture_output=True)
            subprocess.run([sys.executable, script, 'analyze', '--capture', str(root / 'capture'), '--output', str(root / 'analysis')],
                           check=True, capture_output=True)
            result = json.loads((root / 'analysis/summary.json').read_text())
            self.assertEqual(result['PerformanceAcceptance'], 'NOT_EVALUATED_NO_SITE_SLA')
            self.assertIn('INSUFFICIENT_DISTINCT_SNAPSHOTS', result['Anomalies'])
            self.assertEqual(p.sha_file(progress), before)
            (root / 'capture/samples.jsonl').write_text('{}\n')
            bad = subprocess.run([sys.executable, script, 'analyze', '--capture', str(root / 'capture'), '--output', str(root / 'bad')], capture_output=True)
            self.assertNotEqual(bad.returncode, 0)
            self.assertFalse((root / 'bad').exists())

    def test_flow_and_presentation_overflow_and_clock_rollback_are_visible(self):
        self.a.feed(sample(0))
        changed = sample(1, events=[event(600, 'InputRejected')])
        changed['Snapshot']['Progress']['Presentation']['Total'] = 700
        changed['Snapshot']['Progress']['At'] = '2026-11-02T00:00:00+00:00'
        self.a.feed(changed)
        self.assertEqual(self.a.issues['FLOW_WINDOW_LOST_EVENTS'], 599)
        self.assertEqual(self.a.issues['PRESENTATION_WINDOW_LOST_EVENTS'], 700)
        self.assertEqual(self.a.issues['UTC_CLOCK_CHANGE'], 1)
        self.assertEqual(next(q for q in self.a.quantiles() if q['Metric'] == 'CpuCapacityPercent')['Mean'], 12.5)

    def test_rejected_frames_do_not_pollute_successful_decode_quantiles(self):
        self.a.feed(sample(0))
        rejected = frame(1, decode=9876)
        rejected['RejectedFrame'] = True
        self.a.feed(sample(1, frames=[rejected]))
        self.assertEqual(self.a.issues['DECODE_REJECTED_FRAME'], 1)
        self.assertFalse(any(q['Metric'] == 'DecodeMs' for q in self.a.quantiles()))

    def test_debug_request_cannot_borrow_production_slot_of_the_same_frame(self):
        self.a.feed(sample(0))
        events = [event(1, 'RcsSubmitCompleted', state='Accepted', elapsed=40),
                  event(2, 'DebugRcsSubmitCompleted', state='Accepted', elapsed=2000)]
        self.a.feed(sample(1, frames=[frame(1, 'obs-L')], events=events))
        joined = list(self.a.correlations())
        self.assertEqual(len(joined), 1)
        self.assertEqual(joined[0]['RcsMs'], 40)
        self.assertEqual(self.a.issues['DEBUG_RCS_FLOW_EVENT'], 1)
        q = next(q for q in self.a.quantiles() if q['Scope'] == 'left' and q['Metric'] == 'RcsSubmitCompletedMs')
        self.assertEqual((q['Count'], q['Mean']), (1, 40))

    def test_numeric_invalidity_is_reported_instead_of_zero(self):
        for value in (-1, float('nan'), float('inf')):
            self.a.metric('left', 'DecodeMs', value)
        self.assertEqual(self.a.issues['INVALID_NUMERIC_METRIC'], 3)
        self.assertEqual(self.a.quantiles(), [])

    def test_counter_boundary_skew_is_unavailable_instead_of_negative_or_over_100_percent(self):
        self.a.counters['left'].update(DecodeCompleted=0, EmptyFrames=1, Frames=1, SupersededDecodeFrames=2)
        ratios = p.counter_ratios(self.a.counters, self.a.issues)['left']
        self.assertIsNone(ratios['FramesWithCandidates'])
        self.assertIsNone(ratios['CandidateFrameFractionOfDecoded'])
        self.assertIsNone(ratios['SupersededFractionOfCaptured'])
        self.assertEqual(self.a.issues['COUNTER_BOUNDARY_MISMATCH'], 2)

    def test_reconnect_transition_never_attributes_new_epoch_traces_to_old_epoch(self):
        self.a.feed(sample(0))
        transitioning = sample(1, frames=[frame(1, 'obs-L')])
        c = transitioning['Snapshot']['Progress']['Connections'][0]
        c['Performance'] = c['Acquisition'].pop('Performance')
        c['Performance']['Epoch'] = 'epoch-NEW'
        self.a.feed(transitioning)
        self.assertEqual(self.a.issues['EPOCH_SNAPSHOT_TRANSITION'], 1)
        self.assertEqual(self.a.db.execute('SELECT count(*) FROM frames').fetchone()[0], 0)

    def test_csv_formula_guard_and_quota_preserve_partial_evidence(self):
        with tempfile.TemporaryDirectory(prefix='ipc-performance-quota-') as temp:
            root = Path(temp)
            p.write_csv(root / 'safe.csv', [dict(Id='=SYNTH')], ['Id'])
            self.assertIn("'=SYNTH", (root / 'safe.csv').read_text(encoding='utf-8-sig'))
            (root / 'source').mkdir()
            progress = root / 'source/runtime-progress.json'
            p.dump(progress, sample(0)['Snapshot'])
            result = subprocess.run([sys.executable, str(Path(p.__file__)), 'collect', '--progress', str(progress),
                                     '--output', str(root / 'capture'), '--scenario', 'dual', '--left-device', 'camera-L',
                                     '--right-device', 'camera-R', '--duration', '1', '--warmup', '0', '--max-mib', '.00001'], capture_output=True)
            self.assertEqual(result.returncode, 130)
            manifest = json.loads((root / 'capture/capture.json').read_text())
            self.assertFalse(manifest['Complete'])
            self.assertTrue(manifest['QuotaReached'])
            self.assertEqual(manifest['SamplesSha256'], p.sha_file(root / 'capture/samples.jsonl'))


def runtime_log(seq, source, event, detail, runtime='runtime-1'):
    return dict(Format='ImporterPc.RuntimeLog.v1', Application='sportmaster', RuntimeId=runtime,
                At='2026-11-09T01:00:00+00:00', ElapsedMs=seq * 100, Sequence=seq,
                Source=source, Event=event, Level='信息', OperationId=None, Detail=detail)


def scan_window(seq, position, stages, **detail):
    return runtime_log(seq, '扫码节奏', '扫码阶段耗时与队列汇总',
                       dict(PositionId=position, MaximumStageMs=stages, **detail))


def log_export(record):
    return dict(Location=dict(FileId='file-1', Line=record['Sequence'], Hash='synthetic', File='运行/runtime.jsonl'),
                Source=record['Source'], At=record['At'], Level=record['Level'], Text=json.dumps(record),
                EventCode=record['Event'], OperationId=None)


def write_jsonl(path, rows):
    path.write_text(''.join(json.dumps(row, ensure_ascii=False) + '\n' for row in rows), encoding='utf-8-sig')


class ScanLogAnalysis(unittest.TestCase):
    def setUp(self):
        directory = tempfile.TemporaryDirectory(prefix='ipc-scan-logs-')
        self.addCleanup(directory.cleanup)
        self.root = Path(directory.name)

    def analyze(self, *paths):
        output = self.root / 'analysis'
        before = [p.sha_file(path) for path in paths]
        subprocess.run([sys.executable, str(Path(p.__file__)), 'analyze-logs',
                        '--logs', *map(str, paths), '--output', str(output)], check=True, capture_output=True)
        self.assertEqual(before, [p.sha_file(path) for path in paths])
        self.assertCountEqual([path.name for path in output.iterdir()], ['summary.json', 'timeline.jsonl'])
        return (json.loads((output / 'summary.json').read_text(encoding='utf-8')),
                [json.loads(line) for line in (output / 'timeline.jsonl').read_text(encoding='utf-8').splitlines()])

    def test_raw_and_export_windows_and_waiting_samples_are_separated_by_position(self):
        left = dict(Position='left', State='WaitingBusinessGate', ProducerStage='WaitingCapacity',
                    ProducerStageMs=5500, QueueDepth=4, OldestWaiting='2026-11-09T00:59:55+00:00')
        right = dict(Position='right', State='Processing', ProducerStage='Idle', ProducerStageMs=0, QueueDepth=0)
        evidence = dict(ObservationId='synthetic-observation', IngressId='input-1', Epoch='session-1',
                        SourceEventId='mvs.frame.5', Candidates=['SYNTHETIC-NOT-FOR-OUTPUT'])
        initial = scan_window(1, 'left', dict(CameraAcquire=75, Decode=70, DecodeQueue=5, CapacityWait=5500,
                                             IngressPersistence=5501, BusinessGate=200, BusinessQueue=5000),
                              Count=4, WindowMs=5000, MaximumDepth=6, First=evidence, Last=evidence,
                              Slowest=dict(evidence, TotalMs=5600, OldestWaitingMs=5000, QueueDepth=3,
                                           Stages=dict(IngressPersistence=10, BusinessQueue=5000)),
                              Workers=[right, left])
        blocked = runtime_log(3, '扫码节奏', '扫码队列等待采样', dict(Workers=[right, left]))
        later = scan_window(5, 'left', dict(BusinessQueue=8000, BusinessGate=2700), MaximumDepth=7, Workers=[left, right])
        other = scan_window(4, 'right', dict(Decode=100, BusinessQueue=0), MaximumDepth=0, Workers=[left, right])
        status = runtime_log(2, '供包状态', '供包台状态快照', dict(Journal=dict(Dropped=1, DebugDropped=0)))
        rotation, export = self.root / 'runtime.jsonl.1', self.root / 'software-logs.jsonl'
        write_jsonl(rotation, [initial, status, blocked])
        write_jsonl(export, [dict(Format='ImporterPc.SoftwareLog.v1', AllInTimeRange=True),
                            *map(log_export, [later, other, blocked])])
        summary, timeline = self.analyze(export, rotation)
        l, r = (summary['Positions'][side] for side in ('left', 'right'))
        self.assertEqual((l['SlowestScanStage']['Stage'], l['SlowestScanStage']['Ms'], l['MaximumDepth']),
                         ('BusinessQueue', 8000, 7))
        self.assertEqual((r['SlowestScanStage']['Stage'], r['SlowestScanStage']['Ms'], r['MaximumDepth']), ('Decode', 100, 0))
        self.assertEqual([row['Sequence'] for row in timeline], [1, 3, 3, 4, 5])
        self.assertEqual(timeline[0]['MaximumStageMs']['IngressPersistence'], 5501)
        self.assertEqual(timeline[0]['Slowest']['Stages']['IngressPersistence'], 10)
        self.assertEqual(timeline[0]['First']['SourceEventId'], 'mvs.frame.5')
        self.assertEqual(timeline[0]['Last']['ObservationId'], 'synthetic-observation')
        self.assertEqual(timeline[0]['Worker']['ProducerStageMs'], 5500)
        self.assertEqual(timeline[2]['Worker']['ProducerStageMs'], 0)
        self.assertEqual(timeline[3]['MaximumStageMs']['BusinessQueue'], 0)
        self.assertNotIn('SYNTHETIC-NOT-FOR-OUTPUT', json.dumps(timeline))
        self.assertEqual(summary['LogNotices']['DUPLICATE_RUNTIME_RECORD'], 1)
        runtime = summary['Runtimes']['runtime-1']
        self.assertEqual(runtime['UnobservedSequencesBetweenRecords'], 0)
        self.assertEqual(runtime['JournalDropCountersObserved'], dict(Dropped=1, DebugDropped=0))

    def test_rcs_timing_uses_nested_detail_and_keeps_unmatched_evidence(self):
        def rcs(seq, position, start, runtime='runtime-1', **values):
            detail = dict(PositionId=position, OperationId='operation', RequestId='request', **values)
            return runtime_log(seq, 'RCS请求' if start else 'RCS结果', '开始请求格口' if start else '取格口结果',
                               detail if start else json.dumps(detail), runtime)

        source = self.root / 'runtime.jsonl'
        write_jsonl(source, [rcs(1, 'left', True, Query=False), rcs(2, 'right', True, Query=True),
                            rcs(3, 'right', False, Result='Unknown', ElapsedMs=6500),
                            rcs(5, 'left', False, runtime='runtime-2', Result='Accepted', ElapsedMs=44),
                            rcs(6, 'right', False, Result='Accepted', ElapsedMs=-1)])
        summary, timeline = self.analyze(source)
        l, r = (summary['Positions'][side] for side in ('left', 'right'))
        self.assertEqual(l['EvidenceGaps']['RcsStartsWithoutCollectedResult'], 1)
        self.assertIsNone(l['SlowestRcsResult']['CollectedStartSequence'])
        self.assertEqual(r['EvidenceGaps']['RcsResultsWithoutElapsedMs'], 1)
        self.assertEqual(r['RcsTimedResults'], 1)
        self.assertEqual(r['SlowestRcsResult']['CollectedStartSequence'], 2)
        self.assertEqual(r['SlowestRcsResult']['ElapsedMs'], 6500)
        self.assertEqual(r['SlowestRcsResult']['Result'], 'Unknown')
        self.assertIs(r['SlowestRcsResult']['Query'], True)
        self.assertIsNone(r['SlowestScanStage'])
        self.assertIsNone(timeline[3]['ElapsedMs'])
        self.assertEqual(summary['Runtimes']['runtime-1']['UnobservedSequencesBetweenRecords'], 2)

    def test_missing_and_invalid_fields_are_reported_without_fabricating_values(self):
        invalid = dict(CameraAcquire=-1, Decode=float('nan'), IngressPersistence=True, CapacityWait='5000',
                       BusinessQueue=float('inf'), BusinessGate=10**400, FrameCopy=None)
        source = self.root / 'software-logs.jsonl'
        write_jsonl(source, [dict(Format='ImporterPc.SoftwareLog.v1', AllInTimeRange=False),
                            dict(At='2026-11-09T01:00:00+00:00', Source='旧日志', Detail='no timing'),
                            log_export(scan_window(2, 'left', invalid, MaximumDepth=True)),
                            log_export(scan_window(4, 'left', None)),
                            log_export(scan_window(5, 'unassigned', dict(Decode=99999)))])
        with source.open('ab') as output:
            output.write(b'{"unfinished"\n\xff\n')
        summary, timeline = self.analyze(source)
        l, r = (summary['Positions'][side] for side in ('left', 'right'))
        self.assertEqual(l['Windows'], 2)
        self.assertEqual(l['EvidenceGaps']['WindowsWithoutStageMaxima'], 2)
        self.assertEqual(l['EvidenceGaps']['INVALID_BusinessGate'], 1)
        self.assertEqual(l['EvidenceGaps']['INVALID_Decode'], 1)
        self.assertEqual(l['EvidenceGaps']['INVALID_IngressPersistence'], 1)
        self.assertEqual(l['EvidenceGaps']['MISSING_Slowest'], 2)
        self.assertIsNone(l['MaximumDepth'])
        self.assertIsNone(l['SlowestScanStage'])
        self.assertEqual(r['Windows'], 0)
        self.assertIsNone(r['SlowestRcsResult'])
        self.assertEqual(summary['LogNotices']['FILTERED_EXPORT'], 1)
        self.assertEqual(summary['LogNotices']['INVALID_JSON_LINE'], 2)
        self.assertEqual(summary['LogNotices']['UNKNOWN_Window_POSITION'], 1)
        self.assertEqual([row['MaximumStageMs'] for row in timeline], [{}, {}])

    def test_old_log_without_timing_reports_unavailable_and_diagnostic_zip_is_rejected(self):
        source = self.root / 'runtime.jsonl'
        write_jsonl(source, [dict(At='2026-11-09T01:00:00+00:00', Source='旧日志', Detail='no timing')])
        summary, timeline = self.analyze(source)
        self.assertEqual(summary['LogNotices']['NO_SM_RUNTIME_RECORDS'], 1)
        self.assertEqual(summary['Runtimes'], {})
        self.assertEqual(timeline, [])
        for side in ('left', 'right'):
            self.assertIsNone(summary['Positions'][side]['SlowestScanStage'])

        result = subprocess.run([sys.executable, str(Path(p.__file__)), 'analyze-logs', '--logs',
                                 str(self.root / 'diagnostics.zip'), '--output', str(self.root / 'unsupported')],
                                capture_output=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn(b'diagnostic ZIPs omit timing details', result.stderr)
        self.assertFalse((self.root / 'unsupported').exists())


if __name__ == '__main__':
    unittest.main()
