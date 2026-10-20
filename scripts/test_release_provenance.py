import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from release_provenance import capture, installer_name, publish


class ReleaseProvenanceTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory(prefix='ipc-provenance-test-')
        self.root = Path(self.temporary.name)
        self.git('init', '-q', '-b', 'main')
        (self.root / 'source.txt').write_text('synthetic first source')
        self.git('add', 'source.txt')
        self.git('-c', 'user.name=Verification', '-c', 'user.email=verification@example.invalid', 'commit', '-qm', 'synthetic source')
        self.first = self.git('rev-parse', 'HEAD')
        self.git('branch', 'old-source')
        (self.root / 'source.txt').write_text('synthetic second source')
        self.git('add', 'source.txt')
        self.git('-c', 'user.name=Verification', '-c', 'user.email=verification@example.invalid', 'commit', '-qm', 'synthetic second source')
        self.source = self.git('rev-parse', 'HEAD')

    def tearDown(self):
        # Temporary Git objects can be readonly on Windows.
        import os, stat
        def writable(function, path, error):
            os.chmod(path, stat.S_IWRITE | stat.S_IREAD); function(path)
        import shutil
        assert self.root.resolve().parent == Path(tempfile.gettempdir()).resolve() and self.root.name.startswith('ipc-provenance-test-')
        shutil.rmtree(self.root, onexc=writable)
        self.temporary.cleanup()

    def git(self, *args):
        return subprocess.check_output(['git', *args], cwd=self.root, text=True).strip()

    def record(self, branch=None):
        return capture(self.root, self.source, branch, 'sportmaster', '1.4.1', 'win-x64')

    def test_actual_branch_and_complete_commit_recorded(self):
        record = self.record()
        self.assertEqual((record['SourceBranch'], record['SourceCommit']), ('main', self.source))
        self.assertTrue(record['SourceBranchVerified'])
        self.assertEqual((record['ApplicationId'], record['BusinessModuleShortName'], record['BusinessVersion']), ('sportmaster', 'SM', '1.4.1'))
        self.assertTrue(record['SourceCommitUtc'].endswith('+00:00'))

    def test_detached_never_mislabeled_as_main(self):
        self.git('switch', '--detach', '-q', self.source)
        self.assertEqual(self.record()['SourceBranch'], 'DETACHED')
        self.assertFalse(self.record()['SourceBranchVerified'])
        self.assertEqual(self.record('main')['SourceBranch'], 'main')
        self.assertTrue(self.record('main')['SourceBranchVerified'])

    def test_wrong_branch_and_source_refused(self):
        with self.assertRaisesRegex(ValueError, 'same commit'):
            self.record('old-source')
        with self.assertRaisesRegex(ValueError, 'same commit'):
            capture(self.root, self.first, 'main', 'sportmaster', '1.4.1', 'win-x64')
        for label in ('https://private.invalid/token', 'main\nsecret', '-main'):
            with self.assertRaises(ValueError): self.record(label)

    def test_records_are_identical_at_installed_entry_points(self):
        output = self.root / 'output'; (output / 'app').mkdir(parents=True); (output / 'desktop').mkdir()
        record = self.record(); publish(output, record)
        for directory in (output, output / 'app', output / 'desktop'):
            self.assertEqual(json.loads((directory / 'BUILD-INFO.json').read_bytes()), record)
            self.assertEqual((directory / 'BUILD-INFO.txt').read_bytes(), (output / 'BUILD-INFO.txt').read_bytes())
            self.assertIn(self.source, (directory / 'BUILD-INFO.txt').read_text())

    def test_english_filename_contains_version_without_commit(self):
        self.assertEqual(installer_name('sportmaster', '1.4.1'), 'CainiaoSupplyStation_SM_Setup_v1.4.1_win-x64.exe')
        self.assertNotIn(self.source[:8], installer_name('sportmaster', '1.4.1'))
        self.assertEqual(installer_name('direct-plc', '4.8.2'), 'CainiaoSupplyStation_DIRECTPLC_Setup_v4.8.2_win-x64.exe')
        with self.assertRaises(ValueError): installer_name('unknown', '1.4.1')
        for version in ('test', '../1.4.1', '1.4', '1.4.65536'):
            with self.assertRaises(ValueError): installer_name('sportmaster', version)


if __name__ == '__main__': unittest.main()
