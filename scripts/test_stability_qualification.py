import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
from types import SimpleNamespace
from contextlib import ExitStack, redirect_stdout
import io

spec = importlib.util.spec_from_file_location('qualification', Path(__file__).with_name('stability-qualification.py'))
q = importlib.util.module_from_spec(spec); spec.loader.exec_module(q)


class QualificationChecks(unittest.TestCase):
    def test_short_collector_preserves_release_identity_and_has_an_independent_deadline(self):
        for unreadable in (False, True):
            with self.subTest(unreadable=unreadable), tempfile.TemporaryDirectory() as folder:
                root = Path(folder); release = root / 'release'; release.mkdir(); (release / 'SHA256SUMS').write_text('synthetic inventory')
                plan = dict(NormalHours=24, WarmupSeconds=1, MinimumFreeBytes=1, MaxEvidenceBytes=1048576,
                            SourceCommit='a' * 40, MaximumUiStallMs=5000, MaximumWorkerStallMs=5000,
                            MaximumQueueDepth=8, LeftHz=.5, RightHz=.5, MaximumRateDeficitPercent=1)
                tick = [100.0]; calls = [0]
                def read(path):
                    if Path(path).name == 'plan.json': return plan
                    calls[0] += 1
                    if unreadable and calls[0] > 1: raise OSError('Synthetic supervision unavailable')
                    return dict(Progress=dict(RuntimeId='test', ProcessId=1, At=q.utc(), Stopping=False, Background=[],
                        ManualCommands=dict(Positions=[dict(Position=p, Submitted=0, Completed=0) for p in ('left', 'right')]),
                        Workers=[dict(Position=p, Received=calls[0], State='Waiting', QueueDepth=0) for p in ('left', 'right')],
                        Ui=[dict(Id='window', HistogramCounts=[dict(UpperMs=2048, Count=calls[0])], MaximumDispatchMs=6000, LastDispatchAgeMs=0)]), Resource={})
                process = SimpleNamespace(executable=(release / 'desktop/Ipc.Desktop.exe').resolve(), alive=lambda: True, close=lambda: None)
                with ExitStack() as stack:
                    for target, value in [('platform.system', lambda: 'Windows'), ('ordinary', lambda p: Path(p).resolve()),
                                          ('validate_plan', lambda p: None), ('verify_release', lambda p: {'SourceCommit': 'a' * 40}),
                                          ('read_json', read), ('ProcessIdentity', lambda pid: process),
                                          ('time.monotonic', lambda: tick[0]), ('time.sleep', lambda seconds: tick.__setitem__(0, tick[0] + seconds))]:
                        owner, name = (q, target) if '.' not in target else (getattr(q, target.split('.')[0]), target.split('.')[1])
                        stack.enter_context(patch.object(owner, name, value))
                    stack.enter_context(redirect_stdout(io.StringIO()))
                    q.collect(SimpleNamespace(plan=root / 'plan.json', release=release, progress=root / 'progress.json', output=root / 'out', smoke_seconds=2))
                result = json.loads((root / 'out/collection.json').read_text())
                self.assertEqual(result['Status'], 'SMOKE_ONLY')
                self.assertEqual(result['SourceCommit'], 'a' * 40)
                self.assertLessEqual(tick[0], 108)
                if unreadable: self.assertIn('CollectionTimeBudgetExceeded', result['Failures'])
                else: self.assertIn('CompletedUiDispatchStall', result['Failures'])

    def test_plan_does_not_invent_target_qualification(self):
        plan = json.loads((Path(__file__).parents[1] / 'config/stability-target-plan.example.json').read_text())
        with self.assertRaisesRegex(ValueError, 'TargetHardware'):
            q.validate_plan(plan)
        plan.update(TargetHardware='Synthetic only: 4 cores / 8GiB', Combination='Synthetic loopback', SourceCommit='a' * 40,
                    DeviceEvidence='SYNTHETIC', RcsContractEvidence='Unknown held; no query contract', MinimumFreeBytes=1048576,
                    MaxEvidenceBytes=1048576, MaximumUiStallMs=1000, MaximumWorkerStallMs=5000, MaximumQueueDepth=8)
        q.validate_plan(plan)
        plan['NormalHours'] = 1
        with self.assertRaisesRegex(ValueError, '24..168'):
            q.validate_plan(plan)

    def test_long_run_samples_keep_calibration_and_resource_truth_without_repeating_frame_windows(self):
        original = dict(Resource=dict(PrivateMemorySize64=100, WalBytes=10), Progress=dict(
            Flow=dict(Sequence=99, Counts={'left': {'InputAdmitted': 1}}, Recent=[{'OperationId': 'original'}]),
            Execution=dict(ManagedExecutions=7), Diagnostics=dict(WrittenBytes=1000),
            RcsDispatch=dict(Capacity=7, Occupied=7, ActiveSends=7, LeftWaiting=1, RightWaiting=1),
            Connections=[dict(DeviceId='left', Epoch='one', Health='Ready',
                Acquisition=dict(RetainedNativeFrameBytes=0, TrayPresenceModel=dict(Calibrated=False, Presence='Unknown')),
                Performance=dict(OriginalFrames=dict(Frames=2, Bytes=2048, Leases=1, MaximumFrames=32, MaximumBytes=268435456),
                                 Acquisition=dict(FrameTraceCount=512, RecentFrames=[dict(Number=1)])))]))
        sample = q.compact_sample(original)
        self.assertNotIn('Recent', sample['Progress']['Flow'])
        self.assertEqual(sample['Progress']['Flow']['Sequence'], 99)
        self.assertNotIn('RecentFrames', sample['Progress']['Connections'][0]['Performance']['Acquisition'])
        self.assertEqual(original['Progress']['Connections'][0]['Performance']['Acquisition']['RecentFrames'][0]['Number'], 1)
        trends, cameras = {}, {}
        q.observe_resources(sample, trends, cameras)
        second = json.loads(json.dumps(sample)); second['Resource'].update(PrivateMemorySize64=80, WalBytes=30)
        second['Progress']['Connections'][0].update(Epoch='two')
        second['Progress']['Connections'][0]['Acquisition']['TrayPresenceModel']['Calibrated'] = True
        q.observe_resources(second, trends, cameras)
        self.assertEqual((trends['Process.PrivateMemorySize64']['Minimum'], trends['Process.PrivateMemorySize64']['Maximum']), (80, 100))
        self.assertEqual(trends['Process.HandleCount']['Samples'], 0)
        self.assertIsNone(trends['Process.HandleCount']['Start'])
        self.assertEqual(trends['Process.HandleCount']['Missing'], 2)
        self.assertEqual(trends['Camera.left.Original.Bytes']['Maximum'], 2048)
        self.assertEqual((cameras['left']['EpochChanges'], cameras['left']['CalibratedSamples'], cameras['left']['UncalibratedSamples'], cameras['left']['UnknownPresenceSamples']), (1, 1, 1, 2))

    def test_verifier_refusal_never_falls_back_to_python_format_rules(self):
        with tempfile.TemporaryDirectory() as folder:
            db = Path(folder) / 'backup.db'; db.write_bytes(b'not a current database')
            with patch.object(q.subprocess, 'run', return_value=SimpleNamespace(returncode=1, stdout='', stderr='actual schema differs')):
                with self.assertRaisesRegex(ValueError, 'verification refused'):
                    q.verify_backup(db, Path(folder) / 'verifier.dll')

    def test_json_duplicate_keys_and_bounded_input(self):
        with tempfile.TemporaryDirectory() as folder:
            file = Path(folder) / 'evidence.json'; file.write_text('{"Files":{},"files":{}}')
            with self.assertRaisesRegex(ValueError, 'Duplicate'):
                q.read_json(file)
            with self.assertRaisesRegex(ValueError, 'bounded'):
                q.read_json(file, 4)

    def test_source_buffer_write_cannot_stand_in_for_receipt(self):
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder); source = root / 'source.jsonl'; receipts = root / 'receipts.jsonl'
            source.write_text('\n'.join(json.dumps(dict(utc='2026-10-24T00:00:05+00:00', event='input_written_to_socket_buffer', position='left', barcode=code)) for code in ['SYNTH-1', 'SYNTH-2']))
            receipts.write_text(json.dumps(dict(Id='one', Position='left', Codes=['SYNTH-1'], State='Pending', Outcome='Pending')))
            report = q.reconcile_source(source, receipts, dict(NormalStart='2026-10-24T00:00:00+00:00', NormalEnd='2026-10-24T00:00:10+00:00'), root / 'reconciliation.db')
            self.assertEqual(report['Written'], 2)
            self.assertEqual(report['MissingApplicationReceipt'], 1)
            self.assertEqual(report['UnsettledInputs'], 1)


if __name__ == '__main__':
    unittest.main()
