"""Critical archive failure branches; temporary fixtures only."""
import importlib.util
import json
import tempfile
import unittest
import zipfile
from pathlib import Path

spec = importlib.util.spec_from_file_location('verify_archive', Path(__file__).with_name('verify_archive.py'))
archive = importlib.util.module_from_spec(spec)
spec.loader.exec_module(archive)


class ArchiveTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.root = Path(self.tmp.name)
        (self.root / 'docs').mkdir()
        (self.root / 'README.md').write_text('[Entry](docs/entry.md#目标)\n')
        (self.root / 'docs/entry.md').write_text('# 目标\n')
        self.refresh()

    def tearDown(self):
        self.tmp.cleanup()

    def refresh(self):
        files = []
        for p in sorted(self.root.rglob('*')):
            if p.is_file() and p.name not in ('manifest.json', 'manifest.sha256'):
                files.append({'path': p.relative_to(self.root).as_posix(), 'sha256': archive.digest(p.read_bytes()),
                              'size_bytes': p.stat().st_size, 'storage_scope': 'public_repository'})
        obj = {'files': files, 'self_verification': {'excluded_from_file_records': ['docs/manifest.json', 'docs/manifest.sha256']}}
        p = self.root / 'docs/manifest.json'
        p.write_text(json.dumps(obj))
        p.with_suffix('.sha256').write_text(archive.digest(p.read_bytes()) + '  manifest.json\n')

    def check_archive(self):
        return archive.verify(self.root, self.root / 'docs/manifest.json', 'public')

    def test_repeated_verification_is_read_only(self):
        (self.root / 'docs/page.html').write_text('<style>x{fill:url(#paint)}</style><script>new URL(a.href)</script>')
        self.refresh()
        before = {p: (p.read_bytes(), p.stat().st_mtime_ns) for p in self.root.rglob('*') if p.is_file()}
        first = self.check_archive()
        self.assertEqual(first['status'], 'PASS')
        self.assertEqual(first, self.check_archive())
        self.assertEqual(before, {p: (p.read_bytes(), p.stat().st_mtime_ns) for p in before})

    def test_hash_json_manifest_and_unlisted_fail(self):
        (self.root / 'docs/data.json').write_text('{invalid')
        self.refresh()
        (self.root / 'docs/entry.md').write_text('# changed\n')
        (self.root / 'extra').write_text('unlisted')
        codes = {x['code'] for x in self.check_archive()['errors']}
        self.assertTrue({'content_identity', 'unlisted_file', 'JSONDecodeError'} <= codes)
        p = self.root / 'docs/manifest.sha256'; p.write_text('0  manifest.json\n')
        self.assertIn('manifest_checksum', {x['code'] for x in self.check_archive()['errors']})

    def test_links_anchors_css_and_symlink_fail(self):
        (self.root / 'README.md').write_text('[bad](docs/entry.md#不存在)\n[missing](docs/absent.md)\n')
        (self.root / 'docs/a.css').write_text('x{background:url("absent.png")}')
        (self.root / 'docs/boards.json').write_text(json.dumps([{'width': 10, 'height': 10, 'objects': [
            {'type': 'svg', 'svg': '<svg><style>rect{fill:url(missing.png)}</style></svg>'}]}]))
        (self.root / 'docs/link').symlink_to(self.root / 'README.md')
        self.refresh()
        codes = {x['code'] for x in self.check_archive()['errors']}
        self.assertTrue({'missing_anchor', 'missing_or_unsafe_reference', 'symlink'} <= codes)
        self.assertTrue(any('boards.json -> missing.png' in x['path'] for x in self.check_archive()['errors']))

    def test_zip_traversal_link_crc_identity(self):
        p = self.root / 'bad.zip'
        for name, mode in [('../escape', 0), ('safe', 0o120777 << 16)]:
            with self.subTest(name=name):
                with zipfile.ZipFile(p, 'w') as z:
                    i = zipfile.ZipInfo(name); i.external_attr = mode; z.writestr(i, 'x')
                with self.assertRaises(ValueError): archive.zip_check(p)
        with zipfile.ZipFile(p, 'w') as z: z.writestr('safe', 'unique-crc-body')
        data = p.read_bytes(); p.write_bytes(data.replace(b'unique-crc-body', b'broken-crc-body', 1))
        with self.assertRaises(ValueError): archive.zip_check(p)
        with self.assertRaises(ValueError): archive.zip_check(p, '0' * 64)

    def test_registered_gap_has_reason_and_cannot_hide_unrelated_gap(self):
        (self.root / 'README.md').write_text('[old](absent.md)\n')
        policy = self.root / 'docs/registry.json'
        policy.write_text(json.dumps({'exceptions': [{'source': 'README.md', 'target': 'absent.md',
            'reason': 'Original historical attachment not supplied', 'status': 'MISSING'}]}))
        self.refresh()
        out = archive.verify(self.root, self.root / 'docs/manifest.json', 'public', policy)
        self.assertEqual(out['status'], 'PASS')
        self.assertEqual(len(out['registered_reference_gaps']), 1)
        (self.root / 'README.md').write_text('[new](different.md)\n')
        self.refresh()
        codes = {x['code'] for x in archive.verify(self.root, self.root / 'docs/manifest.json', 'public', policy)['errors']}
        self.assertTrue({'unused_exception', 'missing_or_unsafe_reference'} <= codes)

    def test_images_pptx_relations_and_public_secrets_fail(self):
        (self.root / 'docs/bad.png').write_bytes(b'not an image')
        (self.root / 'docs/secret.txt').write_text('-----BEGIN ' + 'PRIVATE KEY-----')
        with zipfile.ZipFile(self.root / 'docs/test.pptx', 'w') as z:
            z.writestr('_rels/.rels', '<Relationships><Relationship Target="https://example.invalid" TargetMode="External"/></Relationships>')
            z.writestr('ppt/fonts/font.odttf', b'font')
            z.writestr('ppt/media/icon.svg', '<svg style="fill:url(missing.png)"></svg>')
        self.refresh()
        codes = {x['code'] for x in self.check_archive()['errors']}
        self.assertTrue({'UnidentifiedImageError', 'secret_marker', 'pptx_external_relationship', 'embedded_font',
                         'pptx_required_parts', 'pptx_svg_external_asset'} <= codes)


if __name__ == '__main__':
    unittest.main()
