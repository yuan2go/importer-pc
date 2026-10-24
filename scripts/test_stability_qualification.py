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

    def test_backup_requires_complete_unchanged_original_set(self):
        with tempfile.TemporaryDirectory() as folder:
            db = Path(folder) / 'backup.db'; db.write_bytes(b'synthetic database bytes')
            recovery = Path(str(db) + '.recovery'); recovery.mkdir()
            config = recovery / 'configuration.json'; config.write_text('{}')
            manifest = recovery / 'COMPLETED.json'
            manifest.write_text(json.dumps(dict(Database=db.name, DatabaseHash=q.sha(db), Files={'configuration.json': q.sha(config)})))
            q.verify_backup(db)
            config.write_text('{"changed": true}')
            with self.assertRaisesRegex(ValueError, 'original differs'):
                q.verify_backup(db)

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
