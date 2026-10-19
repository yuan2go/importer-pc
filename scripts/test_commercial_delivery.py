import copy
import json
from pathlib import Path
import tempfile
import unittest
from commercial_delivery import digest, evaluate, plan, CASES, distribution_signature


class DeliveryGate(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.source, self.inventory = 'a' * 40, 'B' * 64
        self.report = plan(self.source, self.inventory)
        for row in self.report['Cases']:
            if CASES[row['Id']][0] == 'engineering':
                file = self.root / (row['Id'] + '.json')
                file.write_text(json.dumps(dict(Status='PASS', SourceCommit=self.source, MediumInventorySha256=self.inventory, ExitCode=0)))
                row.update(Status='PASS', Evidence=[dict(Path=file.name, Sha256=digest(file))])

    def check(self, report=None, level='engineering'):
        return evaluate(self.root, report or self.report, self.source, self.inventory, level)

    def test_engineering_never_promotes_missing_field_acceptance(self):
        self.assertEqual('PASS', self.check()['Status'])
        self.assertEqual('NOT_GRANTED_BY_THIS_TOOL', self.check()['ProductionApproval'])
        self.assertEqual(6, len(self.check(level='production')['Outstanding']))

    def test_fail_not_run_blocked_stop_required_scope(self):
        for status in ('FAIL', 'NOT_RUN', 'BLOCKED'):
            value = copy.deepcopy(self.report); value['Cases'][0]['Status'] = status
            self.assertEqual('FAIL', self.check(value)['Status'])

    def test_missing_duplicate_foreign_source_and_missing_evidence(self):
        for mutation in (
            lambda v: v['Cases'].pop(),
            lambda v: v['Cases'].__setitem__(1, v['Cases'][0]),
            lambda v: v.update(SourceCommit='c' * 40),
            lambda v: v['Cases'][0].update(Evidence=[]),
            lambda v: v['Cases'][0].update(Status='SKIPPED'),
            lambda v: v['Cases'][0]['Evidence'][0].update(Path='../outside.json'),
        ):
            value = copy.deepcopy(self.report); mutation(value)
            with self.assertRaises(ValueError): self.check(value)

    def test_receipt_identity_and_bytes_are_checked(self):
        file = self.root / 'core.json'
        file.write_text(file.read_text().replace(self.source, 'f' * 40))
        with self.assertRaises(ValueError): self.check()
        self.report['Cases'][0]['Evidence'][0]['Sha256'] = digest(file)
        with self.assertRaises(ValueError): self.check()

    def test_unsigned_manifest_or_manual_pass_cannot_substitute_signer(self):
        # No certificate was provided. Even manually completed cases cannot grant
        # Authenticode status or fabricate an approved publisher.
        self.assertEqual('BLOCKED', distribution_signature(self.root, None)['Status'])


if __name__ == '__main__': unittest.main()
