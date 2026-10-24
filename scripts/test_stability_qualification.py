import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('qualification', Path(__file__).with_name('stability-qualification.py'))
q = importlib.util.module_from_spec(spec); spec.loader.exec_module(q)


class QualificationChecks(unittest.TestCase):
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
