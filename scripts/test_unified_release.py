"""Publication failures must remain detectable, including resealed mixed identity."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from release_build import archive_package, frozen_export

spec = importlib.util.spec_from_file_location('unified_release', Path(__file__).with_name('release-windows.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class ReleaseTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.output = Path(self.temp.name) / 'candidate'
        self.output.mkdir()
        (self.output / 'app').mkdir()
        self.source = release.git('rev-parse', 'HEAD')
        release.write(self.output / 'ASSEMBLY.json', dict(SourceCommit=self.source, Business='inspection'))
        release.write(self.output / 'DEPENDENCIES.json', dict(SourceCommit=self.source))
        release.write(self.output / 'app/Ipc.Assembly.deps.json', dict(libraries={'Ipc.Inspection/1.0.0': {}}))
        release.write(self.output / 'composition.lock.json', dict(components=[dict(id='inspection', version='1.0.0')]))

    def seal(self):
        archive = Path(str(self.output) + '.zip')
        if archive.exists():
            archive.unlink()
        (self.output / 'SHA256SUMS').unlink(missing_ok=True)
        archive_package(self.output, archive, release.ROOT, self.source)
        return archive

    def verify(self):
        return release.verify_package(self.output, 'inspection', 'portable', self.source)

    def test_stable_archive_across_timestamp_changes(self):
        first = release.sha(self.seal())
        import os
        for f in self.output.iterdir():
            os.utime(f, (1000000000, 1000000000))
        self.assertEqual(release.sha(self.seal()), first)
        self.assertEqual(self.verify()['SourceCommit'], self.source)

    def test_changed_missing_and_extra_files_rejected(self):
        self.seal()
        (self.output / 'extra.dll').write_bytes(b'unregistered')
        with self.assertRaises(ValueError):
            self.verify()
        (self.output / 'extra.dll').unlink()
        (self.output / 'DEPENDENCIES.json').write_text('{}')
        with self.assertRaises(ValueError):
            self.verify()
        (self.output / 'DEPENDENCIES.json').unlink()
        with self.assertRaises(ValueError):
            self.verify()

    def test_zip_mismatch_and_duplicate_rejected(self):
        archive = self.seal()
        with zipfile.ZipFile(archive, 'a') as z:
            z.writestr('injected.dll', b'bad')
        with self.assertRaises(ValueError):
            self.verify()
        archive = self.seal()
        with zipfile.ZipFile(archive, 'a') as z:
            z.writestr('assembly.JSON', b'case collision')
        with self.assertRaises(ValueError):
            self.verify()

    def test_resealed_wrong_application_and_source_rejected(self):
        release.write(self.output / 'ASSEMBLY.json', dict(SourceCommit=self.source, Business='supply'))
        self.seal()
        with self.assertRaises(ValueError):
            self.verify()
        release.write(self.output / 'ASSEMBLY.json', dict(SourceCommit='a' * 40, Business='inspection'))
        self.seal()
        with self.assertRaises(ValueError):
            self.verify()

    def test_path_escape_and_windows_alias_rejected(self):
        for name in ('../file', '/file', 'C:/file', 'app\\file', 'app//file', './file'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                release.safe_name(name)

    def test_resealed_wrong_legacy_version_rejected(self):
        release.write(self.output / 'app/Ipc.Assembly.deps.json', dict(libraries={'Ipc.Inspection/9.0.0': {}}))
        self.seal()
        with self.assertRaises(ValueError):
            self.verify()


if __name__ == '__main__':
    unittest.main()
