"""Publication failures must remain detectable, including resealed mixed identity."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import zipfile
import subprocess
import sys
from unittest.mock import patch
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

    def test_reuse_requires_original_source_hash_and_exact_expanded_inventory(self):
        archive = self.seal()
        row = dict(Application='inspection', Target='portable', SourceCommit=self.source,
                   Zip=archive.name, ZipSha256=release.sha(archive))
        manifest = self.output.parent / 'reuse.json'
        release.write(manifest, [row])
        self.assertEqual(release.reuse_candidates(self.output.parent, manifest, self.source),
                         {('inspection', 'portable'): self.output})
        release.write(manifest, [dict(row, SourceCommit='b' * 40)])
        with self.assertRaises(ValueError):
            release.reuse_candidates(self.output.parent, manifest, self.source)
        release.write(manifest, [dict(row, ZipSha256='A' * 64)])
        with self.assertRaises(ValueError):
            release.reuse_candidates(self.output.parent, manifest, self.source)
        release.write(manifest, [row])
        (self.output / 'injection.dll').write_bytes(b'extra')
        with self.assertRaises(ValueError):
            release.reuse_candidates(self.output.parent, manifest, self.source)

    def test_reuse_refuses_duplicate_or_escaping_original_names(self):
        archive = self.seal()
        row = dict(Application='inspection', Target='portable', SourceCommit=self.source,
                   Zip=archive.name, ZipSha256=release.sha(archive))
        manifest = self.output.parent / 'reuse.json'
        for records in ([row, row], [dict(row, Zip='../candidate.zip')]):
            release.write(manifest, records)
            with self.assertRaises(ValueError):
                release.reuse_candidates(self.output.parent, manifest, self.source)

    def test_cli_reuse_options_parse_without_duplicate_source_flag(self):
        result = subprocess.run([sys.executable, str(Path(__file__).with_name('release-windows.py')), '--help'], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('--reuse-directory', result.stdout)

    def test_source_pin_refuses_runtime_and_functional_packager_differences(self):
        path = 'scripts/package-application.py'
        def query(*args):
            if args[0] == 'diff': return path
            return 'x = 1' if args[1].startswith('old:') else '# comment only\nx = 1'
        with patch.object(release, 'git', side_effect=query):
            self.assertEqual(release.source_input_compatibility('old', 'new')[0]['Kind'], 'SCRIPT_TEXT_ONLY_IDENTICAL_AST')
        with patch.object(release, 'git', side_effect=lambda *a: path if a[0] == 'diff' else ('x=1' if a[1].startswith('old:') else 'x=2')):
            with self.assertRaises(ValueError): release.source_input_compatibility('old', 'new')
        with patch.object(release, 'git', return_value='src/Ipc.Platform/Runtime.cs'):
            with self.assertRaises(ValueError): release.source_input_compatibility('old', 'new')

    def test_generated_checkout_cleanup_handles_windows_readonly_git_objects(self):
        import stat
        workspace = Path(tempfile.mkdtemp(prefix='ipc-unified-frozen-'))
        pack = workspace / 'source/.git/objects/pack/frozen.idx'
        pack.parent.mkdir(parents=True)
        pack.write_bytes(b'git-like immutable object')
        pack.chmod(stat.S_IREAD)
        # Exercise the real Windows long-path tree as well as a readonly Git object.
        deep = workspace / 'source' / ('nested-evidence-' * 12) / 'child' / 'leaf.txt'
        extended = Path('\\\\?\\' + str(deep)) if sys.platform == 'win32' else deep
        extended.parent.mkdir(parents=True)
        extended.write_bytes(b'long tracked path')
        release.cleanup_frozen_workspace(workspace)
        self.assertFalse(workspace.exists())
        with self.assertRaises(ValueError):
            release.cleanup_frozen_workspace(self.output)
        self.assertTrue(self.output.exists())


class Pr48ReleaseTests(unittest.TestCase):
    def make(self, root):
        media = root/'supply-portable'; media.mkdir()
        (media/'ASSEMBLY.json').write_text(json.dumps(dict(SourceCommit='a'*40, Business='supply')))
        (media/'original.bin').write_bytes(b'original')
        (media/'DEPENDENCIES.json').write_text(json.dumps(dict(SourceCommit='a'*40)))
        (media/'composition.lock.json').write_text(json.dumps(dict(components=[dict(id='supply', version='1.0.0')])))
        (media/'app').mkdir()
        (media/'app/Ipc.Assembly.deps.json').write_text(json.dumps(dict(libraries={'Ipc.Supply/1.0.0': {}})))
        self.seal(media)
        return media

    def seal(self, media):
        (media/'SHA256SUMS').write_text(''.join(release.sha(p)+'  '+p.relative_to(media).as_posix()+'\n' for p in sorted(media.rglob('*')) if p.is_file() and p.name != 'SHA256SUMS'))
        with zipfile.ZipFile(media.with_suffix('.zip'),'w') as z:
            for p in media.rglob('*'):
                if p.is_file(): z.write(p,p.relative_to(media).as_posix())

    def check(self, media, source, application, target):
        return release.verify_package(media, application, target, source)

    def test_hash_and_exact_inventory_and_zip(self):
        with tempfile.TemporaryDirectory() as temporary:
            media = self.make(Path(temporary))
            self.assertEqual(self.check(media,'a'*40,'supply','portable')['Application'],'supply')
            (media/'original.bin').write_bytes(b'tampered')
            with self.assertRaisesRegex(ValueError,'File differs'): self.check(media,'a'*40,'supply','portable')
            self.seal(media)
            (media/'injection.dll').write_bytes(b'extra')
            with self.assertRaisesRegex(ValueError,'Incomplete package inventory'): self.check(media,'a'*40,'supply','portable')

    def test_zip_cannot_be_stale_or_duplicated(self):
        with tempfile.TemporaryDirectory() as temporary:
            media = self.make(Path(temporary))
            with zipfile.ZipFile(media.with_suffix('.zip'),'a') as z: z.writestr('extra.dll', b'extra')
            with self.assertRaisesRegex(ValueError,'ZIP members'): self.check(media,'a'*40,'supply','portable')

    def test_identity_cannot_be_relabelled(self):
        with tempfile.TemporaryDirectory() as temporary:
            media = self.make(Path(temporary))
            for source,app in [('b'*40,'supply'),('a'*40,'inspection')]:
                with self.assertRaisesRegex(ValueError,'Wrong package source/application'): self.check(media,source,app,'portable')


if __name__ == '__main__':
    unittest.main()
