import copy
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('ipc_composition', ROOT / 'scripts/composition.py')
composition = importlib.util.module_from_spec(spec)
spec.loader.exec_module(composition)


class CompositionTests(unittest.TestCase):
    def test_distinct_selected_closures(self):
        core = composition.resolve(ROOT / 'config/compositions.json', 'core-reference')
        extended = composition.resolve(ROOT / 'config/compositions.json', 'barcode-reference')
        self.assertEqual({x['id'] for x in core['components']}, {'platform', 'platform-storage', 'application-contracts', 'reference-protocol', 'drivers', 'integrations', 'supply-contracts', 'supply', 'adapters', 'product'})
        self.assertEqual({x['id'] for x in extended['components']} - {x['id'] for x in core['components']}, {'reference.barcode'})

    def test_non_supply_is_a_real_selected_closure(self):
        selected = composition.resolve(ROOT / 'config/compositions.json', 'inspection-reference')
        self.assertEqual({x['id'] for x in selected['components']}, {'platform', 'inspection', 'assembly.inspection'})
        self.assertEqual(selected['product']['business'], 'inspection')
        self.assertEqual(selected['product']['views'], ['inspection', 'inspection-history'])

    def test_missing_cycle_and_core_removal_refused(self):
        original = json.loads((ROOT / 'config/compositions.json').read_text())
        for change in ('missing', 'cycle', 'core'):
            value = copy.deepcopy(original)
            if change == 'missing':
                value['components']['product']['dependencies'] = ['absent']
            elif change == 'cycle':
                value['components']['platform']['dependencies'] = ['product']
            else:
                value['compositions']['core-reference'] = ['platform']
            with self.subTest(change=change), tempfile.TemporaryDirectory() as directory:
                file = Path(directory) / 'composition.json'
                file.write_text(json.dumps(value))
                with self.assertRaises(ValueError):
                    composition.resolve(file, 'core-reference')


if __name__ == '__main__':
    unittest.main()
