"""Verify the frozen-media boundary of the single-EXE packager."""
import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
from importlib.util import spec_from_file_location, module_from_spec

spec = spec_from_file_location('installer_package', Path(__file__).with_name('package-installer.py'))
installer = module_from_spec(spec)
spec.loader.exec_module(installer)


class InstallerPackageTests(unittest.TestCase):
    def test_delivery_copy_is_published_only_after_complete_verification(self):
        source = self.path.parent / 'source.exe'; source.write_bytes(b'SYNTHETIC complete executable')
        output = self.path.parent / 'delivered.exe'
        def interrupted(incoming, outgoing):
            outgoing.write(b'partial')
            self.assertFalse(output.exists())
            raise OSError('synthetic destination full')
        with patch.object(installer.shutil, 'copyfileobj', side_effect=interrupted):
            with self.assertRaisesRegex(OSError, 'destination full'):
                installer.publish_installer(source, output)
        self.assertFalse(output.exists())
        self.assertEqual(list(self.path.parent.glob('.ipc-installer-*')), [])
        self.assertEqual(installer.publish_installer(source, output), hashlib.sha256(source.read_bytes()).hexdigest())
        self.assertEqual(output.read_bytes(), source.read_bytes())
        with self.assertRaises(FileExistsError):
            installer.publish_installer(source, output)
        self.assertEqual(output.read_bytes(), source.read_bytes())

    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.path = Path(self.temporary.name) / 'medium.zip'
        self.source = 'a' * 40
        release = json.dumps(dict(ApplicationId='sportmaster', SourceCommit=self.source, BusinessVersion='1.4.1')).encode()
        self.files = {'app/application-release.json': release, 'desktop/application-release.json': release,
                      'desktop/Ipc.Desktop.exe': b'SYNTHETIC NEVER EXECUTED'}
        record = json.dumps(dict(ApplicationId='sportmaster', SourceCommit=self.source, BusinessVersion='1.4.1',
                                 BusinessModuleShortName='SM',
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
        self.assertEqual(descriptor['BusinessModuleShortName'], 'SM')
        self.assertEqual(descriptor['ZipBytes'], self.path.stat().st_size)

    def test_different_runtime_source_is_rejected(self):
        self.write()
        with self.assertRaisesRegex(ValueError, 'same frozen source'):
            installer.inspect_medium(self.path, 'b' * 40)

    def test_test_channel_requires_matching_installer_and_executable(self):
        self.files['desktop/Ipc.Desktop.Test.exe'] = self.files.pop('desktop/Ipc.Desktop.exe')
        for name in ('BUILD-INFO.json', 'app/BUILD-INFO.json', 'desktop/BUILD-INFO.json'):
            record = json.loads(self.files[name])
            record['InstallationChannel'] = 'Test'
            self.files[name] = json.dumps(record).encode()
        self.write()
        self.assertEqual(installer.inspect_medium(self.path, self.source, True)['ApplicationId'], 'sportmaster')
        with self.assertRaises(ValueError):
            installer.inspect_medium(self.path, self.source, False)

    def test_build_variant_options(self):
        from release_build import resolve_channel, resolve_default_page

        self.assertEqual(resolve_channel(None, False), 'formal')
        self.assertEqual(resolve_channel(None, True), 'test')
        self.assertEqual(resolve_channel('test', True), 'test')
        with self.assertRaisesRegex(ValueError, 'conflicts'):
            resolve_channel('formal', True)
        self.assertEqual(resolve_default_page('sportmaster', None), 'supply-station')
        self.assertEqual(resolve_default_page('sportmaster', 'tote-station'), 'tote-station')
        self.assertEqual(resolve_default_page('direct-plc', None), 'classic')
        for application, page in [('sportmaster', 'invalid-page'), ('wcs-to-plc', 'tote-station')]:
            with self.subTest(application=application, page=page), self.assertRaisesRegex(ValueError, '--default-page'):
                resolve_default_page(application, page)

    def test_attests_media_channel_and_default_page_in_both_build_info_formats(self):
        from release_provenance import publish

        workspace = self.path.parent / 'verified'
        (workspace / 'app').mkdir(parents=True)
        (workspace / 'desktop').mkdir()
        base = json.loads(self.files['BUILD-INFO.json'])
        base.update(SourceCommitUtc='2026-11-28', PackagedUtc='2026-11-28', SourceRepository='synthetic',
                    InstallationChannel='Formal', DefaultWorkstationPage='tote-station')
        publish(workspace, base)
        for area in ('', 'app/', 'desktop/'):
            json_name = area + 'BUILD-INFO.json'
            self.files[json_name] = (workspace / json_name).read_bytes()
            info = json.loads(self.files[json_name])
            self.assertEqual((info['InstallationChannel'], info['DefaultWorkstationPage']), ('Formal', 'tote-station'))
            self.assertIn('DefaultWorkstationPage: tote-station', (workspace / area / 'BUILD-INFO.txt').read_text(encoding='utf-8'))
        self.write()
        self.assertEqual(installer.inspect_medium(self.path, self.source)['DefaultWorkstationPage'], 'tote-station')
        with self.assertRaisesRegex(ValueError, 'default page differs'):
            installer.inspect_medium(self.path, self.source, default_page='supply-station')

    def test_production_channel_cannot_be_wrapped_as_test(self):
        self.write()
        with self.assertRaises(ValueError):
            installer.inspect_medium(self.path, self.source, True)

    def test_reused_ui_closure_must_equal_payload_bytes(self):
        self.files['desktop/Ipc.Desktop.dll'] = b'SYNTHETIC selected desktop'
        self.files['desktop/Ipc.Assembly.dll'] = b'SYNTHETIC composition'
        self.files['desktop/Ipc.Operations.Desktop.dll'] = b'SYNTHETIC independent observer entry'
        self.files['desktop/Ipc.Presentation.Wpf.dll'] = b'SYNTHETIC shared native theme'
        self.write()
        binaries = self.path.parent / 'binaries'; binaries.mkdir()
        for name in ('Ipc.Desktop.dll', 'Ipc.Assembly.dll', 'Ipc.Presentation.Wpf.dll', 'Ipc.Operations.Desktop.dll'):
            (binaries / name).write_bytes(self.files['desktop/' + name])
        installer.verify_shared_binaries(self.path, binaries)
        (binaries / 'Ipc.Presentation.Wpf.dll').write_bytes(b'OTHER THEME')
        with self.assertRaisesRegex(ValueError, 'bytes differ'): installer.verify_shared_binaries(self.path, binaries)
        (binaries / 'Ipc.Presentation.Wpf.dll').write_bytes(self.files['desktop/Ipc.Presentation.Wpf.dll'])
        (binaries / 'Ipc.Assembly.dll').write_bytes(b'OTHER SOURCE')
        with self.assertRaisesRegex(ValueError, 'bytes differ'): installer.verify_shared_binaries(self.path, binaries)
        (binaries / 'Ipc.Assembly.dll').unlink()
        with self.assertRaisesRegex(ValueError, 'bytes differ'): installer.verify_shared_binaries(self.path, binaries)

    def test_missing_detached_or_different_provenance_refused(self):
        original = dict(self.files)
        for change in ('missing', 'detached', 'different', 'module', 'version', 'divergent-copy'):
            self.files = dict(original)
            if change == 'missing': del self.files['BUILD-INFO.json']
            else:
                record = json.loads(self.files['BUILD-INFO.json'])
                if change == 'detached': record['SourceBranchVerified'] = False
                elif change == 'different': record['SourceCommit'] = 'b' * 40
                elif change == 'module': record['BusinessModuleShortName'] = 'OTHER'
                elif change == 'version': record['BusinessVersion'] = '9.9.9'
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
