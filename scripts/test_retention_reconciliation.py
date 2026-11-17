import copy
import importlib.util
from pathlib import Path
import unittest

spec = importlib.util.spec_from_file_location('retention_reconciliation', Path(__file__).with_name('reconcile-retention-qualification.py'))
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


class CommandReceipts(unittest.TestCase):
    def test_original_command_is_required_before_its_timing_is_used(self):
        database = Path('synthetic.db').resolve()
        backup = Path('complete.db').resolve()
        receipt = dict(Name='expire-8', ExitCode=0, Seconds=1,
                       Arguments=['--action', 'expire-history', '--file', str(database),
                                  '--sha256', 'A' * 64, '--destination', str(backup)])
        def read(rows):
            return module.require_command_receipt(rows, 'expire-8', 'expire-history', database, backup, 'A' * 64)
        self.assertIs(receipt, read([receipt, dict(Name='unrelated', ExitCode=1)]))
        for mutation in ('duplicate', 'failed', 'database', 'backup', 'action', 'hash', 'duplicate-option'):
            invalid = copy.deepcopy(receipt)
            if mutation == 'duplicate':
                rows = [receipt, invalid]
            else:
                rows = [invalid]
                if mutation == 'failed':
                    invalid['ExitCode'] = 1
                elif mutation == 'duplicate-option':
                    invalid['Arguments'] += ['--file', str(database)]
                else:
                    indexes = dict(action=1, database=3, hash=5, backup=7)
                    invalid['Arguments'][indexes[mutation]] = 'wrong'
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                read(rows)


if __name__ == '__main__':
    unittest.main()
