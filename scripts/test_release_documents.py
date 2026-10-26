"""Standalone candidate guide links and fail-closed source identity."""
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import patch
import release_documents as docs


class ReleaseDocumentTests(unittest.TestCase):
    def test_all_parcel_products_ship_only_current_operation_entry(self):
        root = Path(__file__).resolve().parent.parent
        self.assertEqual(docs.PARCEL_GUIDES['docs/runtime/lifecycle-ux-current-guidance-01.md'], 'LIFECYCLE-OPERATIONS.md')
        for old in ['docs/runtime/sportmaster-visual-productization-01.md', 'docs/runtime/validation/sportmaster-alternate-ui-windows.md', 'docs/runtime/ui-task-redesign-01.md', 'docs/runtime/parameter-refactor-01.md']:
            self.assertNotIn(old, docs.ALL_GUIDES)
        for mapping in [docs.PARCEL_GUIDES, docs.SPORTMASTER_GUIDES]:
            self.assertEqual(len(mapping), len(set(mapping.values())))
            for source in mapping:
                rendered = docs.render(root, source, 'a' * 40)
                self.assertTrue(rendered.strip(), source)
                for obsolete in ['进入帮助与支持', '打开帮助与支持', '点击帮助与支持', '在帮助与支持页', '全屏帮助与支持']:
                    self.assertNotIn(obsolete, rendered, source)
        generated = (root / 'scripts/package-application.py').read_text(encoding='utf-8')
        referenced = set(re.findall(r'\b[A-Z][A-Z0-9-]+\.md\b', generated))
        self.assertLessEqual(referenced, set(docs.SPORTMASTER_GUIDES.values()), 'Generated launch instructions must reference shipped current guides')

    def test_current_links_standalone_and_evidence_links_immutable(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary).resolve()
            (root / 'docs').mkdir()
            (root / 'scripts').mkdir()
            (root / 'docs/guide.md').write_text('[Local](other.md#start) [Evidence](../scripts/proof.py) [Online](https://example.invalid/test) [Anchor](#local)')
            (root / 'docs/other.md').write_text('# start')
            (root / 'scripts/proof.py').write_text('# proof')
            with patch.dict(docs.GUIDES, {'docs/other.md':'OTHER.md'}, clear=True):
                result = docs.render(root, 'docs/guide.md', 'a' * 40)
            self.assertIn('[Local](OTHER.md#start)', result)
            self.assertIn('/blob/' + 'a' * 40 + '/scripts/proof.py', result)
            self.assertIn('[Online](https://example.invalid/test)', result)
            self.assertIn('[Anchor](#local)', result)
            with self.assertRaises(ValueError): docs.render(root, 'docs/guide.md', 'working-tree')

    def test_missing_and_escaping_links_stop_publication(self):
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary).resolve()
            source=root / 'guide.md'
            for value in ['missing.md', '../outside.md']:
                source.write_text('[Broken](' + value + ')')
                with self.assertRaises(ValueError): docs.render(root, 'guide.md', 'b' * 40)


if __name__ == '__main__': unittest.main()
