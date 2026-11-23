"""Local CR regressions for the installed helper implementation."""
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parent.parent))
import core
import validate_data as data
import design_system


class ReviewRegressions(unittest.TestCase):
    def test_bm25_refit_does_not_keep_prior_document_statistics(self):
        index = core.BM25()
        index.fit([['old', 'old'], ['other']])
        index.fit([['new']])
        self.assertNotIn('old', index.idf)
        self.assertEqual(1, len(index.doc_lengths))
        index.fit([])
        self.assertEqual({}, index.idf)
        self.assertEqual([], index.doc_lengths)

    def test_stack_filter_and_ranking_share_one_snapshot(self):
        original = core._load_csv_snapshot
        loads = []
        def read(path, *args, **kwargs):
            loads.append(path)
            if len(loads) > 1:
                raise AssertionError('filter/ranking reopened the data')
            return original(path, *args, **kwargs)
        with patch.object(core, '_load_csv_snapshot', side_effect=read):
            result = core.search_stack('keyboard interaction accessibility', 'wpf')
        self.assertNotIn('error', result)
        self.assertEqual(1, len(loads))

    def test_malformed_csv_width_is_reported_and_excluded(self):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory, 'bad.csv')
            file.write_text('No,Text\n1\n2,ok,extra\n3,valid\n', encoding='utf-8')
            problems = []
            _, rows = data._check_file('fixture', file, ['Text'], ['No'], problems)
        self.assertEqual([{'No': '3', 'Text': 'valid'}], rows)
        self.assertEqual(2, sum('malformed row width' in problem for problem in problems))

    def test_font_weight_ranges_and_axis_positions_are_preserved(self):
        self.assertEqual({'400', '700'}, data._imported_weights(['Example:ital,wght@0,400;1,700']))
        weights = data._imported_weights(['Example:opsz,wght@12..72,100..900'])
        self.assertIn('500', weights)
        self.assertEqual(801, len(weights))
        self.assertEqual(set(), data._imported_weights(['Example:opsz@100..900']))

    def test_missing_summary_inputs_produce_diagnostics(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(data, 'DATA_DIR', Path(directory)):
            problems = []
            data._check_catalog_summary({}, {}, {}, problems)
        self.assertTrue(any('unavailable dataset' in problem for problem in problems))
        self.assertTrue(any('unavailable snapshot' in problem for problem in problems))

    def test_unhashable_catalog_identities_are_validation_errors(self):
        problems = []
        data._check_font_catalog([], {'families': [{'name': [], 'license': []}], 'excludedFamilies': [], 'familyCount': 0}, [], problems)
        self.assertTrue(any('invalid active family' in problem for problem in problems))

    def test_invalid_provenance_keys_are_reported_before_set_membership(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(data, 'DATA_DIR', Path(directory)):
            for kind, key in [('reasoning', 'UI_Category'), ('style', 'Style ID')]:
                record = dict(entityKind=kind, entityId='fixture', status=[], sla=[], sourceFile=[],
                              sourceKey={key: []}, appliesTo=[[]], sources=[dict(type=[], ref=[])])
                Path(directory, 'data-provenance.json').write_text(json.dumps(dict(schemaVersion=1, records=[record])), encoding='utf-8')
                problems = []
                data._check_provenance([], [], problems)
                self.assertTrue(any('invalid ' + key in problem for problem in problems))

    def test_dark_fallback_derives_semantic_foreground_and_background(self):
        selected = design_system._select_palette_for_mode([{'Background': '#FFFFFF', 'Primary': '#2563EB'}], 'dark')
        self.assertEqual('#0F172A', selected['Background'])
        self.assertEqual('#F8FAFC', selected['Foreground'])


if __name__ == '__main__':
    unittest.main()
