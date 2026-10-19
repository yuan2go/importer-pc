"""Negative release gates: never accept tampering or stale ZIPs as a release."""
import hashlib
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile

spec = importlib.util.spec_from_file_location('release_windows', Path(__file__).with_name('release-windows.py'))
release = importlib.util.module_from_spec(spec); spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def make(self, root):
        media = root/'supply-portable'; media.mkdir()
        (media/'ASSEMBLY.json').write_text(json.dumps(dict(SourceCommit='a'*40, Business='supply')))
        (media/'original.bin').write_bytes(b'original')
        self.seal(media)
        return media

    def seal(self, media):
        (media/'SHA256SUMS').write_text(''.join(release.digest(p)+'  '+p.name+'\n' for p in sorted(media.iterdir()) if p.name != 'SHA256SUMS'))
        with zipfile.ZipFile(media.with_suffix('.zip'),'w') as z:
            for p in media.iterdir(): z.write(p,p.name)

    def test_hash_and_exact_inventory_and_zip(self):
        with tempfile.TemporaryDirectory() as temporary:
            media = self.make(Path(temporary))
            self.assertEqual(release.verify(media,'a'*40,'supply','portable')['Application'],'supply')
            (media/'original.bin').write_bytes(b'tampered')
            with self.assertRaisesRegex(ValueError,'hash mismatch'): release.verify(media,'a'*40,'supply','portable')
            self.seal(media)
            (media/'injection.dll').write_bytes(b'extra')
            with self.assertRaisesRegex(ValueError,'exact media'): release.verify(media,'a'*40,'supply','portable')

    def test_zip_cannot_be_stale_or_duplicated(self):
        with tempfile.TemporaryDirectory() as temporary:
            media = self.make(Path(temporary))
            with zipfile.ZipFile(media.with_suffix('.zip'),'a') as z: z.writestr('extra.dll', b'extra')
            with self.assertRaisesRegex(ValueError,'ZIP member'): release.verify(media,'a'*40,'supply','portable')

    def test_identity_cannot_be_relabelled(self):
        with tempfile.TemporaryDirectory() as temporary:
            media = self.make(Path(temporary))
            for source,app in [('b'*40,'supply'),('a'*40,'inspection')]:
                with self.assertRaisesRegex(ValueError,'Wrong source'): release.verify(media,source,app,'portable')


if __name__ == '__main__': unittest.main()
