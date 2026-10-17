"""Standalone candidate guide links and fail-closed source identity."""
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import release_documents as docs


class ReleaseDocumentTests(unittest.TestCase):
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
