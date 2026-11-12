"""Standalone candidate guide links and fail-closed source identity."""
from pathlib import Path
import re
import tempfile
import unittest
from unittest.mock import patch
import release_documents as docs
from application_compatibility import read_storage_format


class ReleaseDocumentTests(unittest.TestCase):
    def test_all_parcel_products_ship_only_current_operation_entry(self):
        root = Path(__file__).resolve().parent.parent
        self.assertEqual(docs.PARCEL_GUIDES['docs/runtime/lifecycle-ux-current-guidance-01.md'], 'LIFECYCLE-OPERATIONS.md')
        for product, mapping in [('direct-plc', docs.PARCEL_GUIDES), ('wcs-to-plc', docs.PARCEL_GUIDES), ('sportmaster', docs.SPORTMASTER_GUIDES)]:
            self.assertEqual(len(mapping), len(set(mapping.values())))
            with tempfile.TemporaryDirectory() as temporary:
                output = Path(temporary)
                docs.publish(root, output, 'a' * 40, product)
                self.assertEqual(set(mapping.values()), {path.name for path in output.iterdir()})
                for path in output.iterdir():
                    rendered = path.read_text(encoding='utf-8')
                    self.assertTrue(rendered.strip(), path.name)
                    for target in re.findall(r'\]\(([^)]+)\)', rendered):
                        if not target.startswith(('https://', 'http://', '#', 'mailto:')):
                            self.assertTrue((output / target.split('#')[0]).is_file(), (path.name, target))
        generated = (root / 'scripts/package-application.py').read_text(encoding='utf-8')
        referenced = set(re.findall(r'\b[A-Z][A-Z0-9-]+\.md\b', generated))
        self.assertLessEqual(referenced, set(docs.SPORTMASTER_GUIDES.values()), 'Generated launch instructions must reference shipped current guides')

    def test_current_storage_and_arrival_guides_are_shipped(self):
        root = Path(__file__).resolve().parent.parent
        current = str(read_storage_format(root))
        for mapping in [docs.PARCEL_GUIDES, docs.SPORTMASTER_GUIDES]:
            source = next(source for source, filename in mapping.items() if filename == f'PARCEL{current}-OPERATIONS.md')
            title = docs.render(root, source, 'a' * 40).splitlines()[0]
            self.assertEqual([current], re.findall(r'\bParcel(\d+)\b', title, flags=re.IGNORECASE))
        self.assertIn('docs/design/sm-vehicle-arrival-rounds.md', docs.SPORTMASTER_GUIDES)
        self.assertIn('docs/design/sm-parcel-rounds.md', docs.SPORTMASTER_GUIDES)

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
