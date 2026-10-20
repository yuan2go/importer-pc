"""Verify the frozen-media boundary of the single-EXE packager."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
from importlib.util import spec_from_file_location, module_from_spec

spec = spec_from_file_location('installer_package', Path(__file__).with_name('package-installer.py'))
installer = module_from_spec(spec)
spec.loader.exec_module(installer)


class InstallerPackageTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.path = Path(self.temporary.name) / 'medium.zip'
        self.source = 'a' * 40
        release = json.dumps(dict(ApplicationId='sportmaster', SourceCommit=self.source, BusinessVersion='1.4.1')).encode()
        self.files = {'app/application-release.json': release, 'desktop/application-release.json': release,
                      'desktop/Ipc.Desktop.exe': b'SYNTHETIC NEVER EXECUTED'}
        record = json.dumps(dict(ApplicationId='sportmaster', SourceCommit=self.source, BusinessVersion='1.4.1',
                                 SourceBranch='main', SourceBranchVerified=True, FrozenGitExport=True, Target='win-x64')).encode()
        for area in ('', 'app/', 'desktop/'):
            self.files[area + 'BUILD-INFO.json'] = record
            self.files[area + 'BUILD-INFO.txt'] = b'SYNTHETIC build source information'

    def tearDown(self):
        self.temporary.cleanup()

    def write(self, extra=None, tamper=False):
        inventory = ''.join(hashlib.sha256(value).hexdigest() + '  ' + name + '\n' for name, value in self.files.items())
        with zipfile.ZipFile(self.path, 'w') as archive:
            for name, content in self.files.items():
                archive.writestr(name, b'CHANGED' if tamper and name.endswith('.exe') else content)
            archive.writestr('SHA256SUMS', inventory)
            if extra:
                # Preserve raw hostile spelling: ZipInfo normally normalizes backslashes on Windows.
                info = zipfile.ZipInfo(); info.filename = extra
                archive.writestr(info, b'unlisted')

    def test_valid_same_source_medium(self):
        self.write()
        descriptor = installer.inspect_medium(self.path, self.source)
        self.assertEqual(descriptor['SourceCommit'], self.source)
        self.assertEqual(descriptor['FileCount'], 10)
        self.assertEqual(descriptor['SourceBranch'], 'main')
        self.assertEqual(descriptor['ZipBytes'], self.path.stat().st_size)

    def test_different_runtime_source_is_rejected(self):
        self.write()
        with self.assertRaisesRegex(ValueError, 'same frozen source'):
            installer.inspect_medium(self.path, 'b' * 40)

    def test_missing_detached_or_different_provenance_refused(self):
        original = dict(self.files)
        for change in ('missing', 'detached', 'different', 'divergent-copy'):
            self.files = dict(original)
            if change == 'missing': del self.files['BUILD-INFO.json']
            else:
                record = json.loads(self.files['BUILD-INFO.json'])
                if change == 'detached': record['SourceBranchVerified'] = False
                elif change == 'different': record['SourceCommit'] = 'b' * 40
                else: record['PackagedUtc'] = 'CHANGED'
                name = 'desktop/BUILD-INFO.json' if change == 'divergent-copy' else 'BUILD-INFO.json'
                self.files[name] = json.dumps(record).encode()
            self.write()
            with self.assertRaisesRegex(ValueError, 'source record|source records'):
                installer.inspect_medium(self.path, self.source)

    def test_corrupted_file_is_rejected(self):
        self.write(tamper=True)
        with self.assertRaisesRegex(ValueError, 'hash mismatch'):
            installer.inspect_medium(self.path, self.source)

    def test_unlisted_and_case_duplicate_files_rejected(self):
        for extra in ('unlisted.dll', 'DESKTOP/ipc.desktop.EXE'):
            self.write(extra=extra)
            with self.assertRaises(ValueError):
                installer.inspect_medium(self.path, self.source)

    def test_unsafe_archive_paths_rejected(self):
        for name in ('../escape', '/root', 'C:/drive', 'a\\bad', 'a//b', 'a/file.', 'a:stream', 'AUX.txt'):
            self.write(extra=name)
            with self.assertRaisesRegex(ValueError, 'Unsafe'):
                installer.inspect_medium(self.path, self.source)


if __name__ == '__main__':
    unittest.main()
