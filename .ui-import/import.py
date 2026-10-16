"""One-time extraction of the hash-verified, user-requested design source bundle."""
import base64, hashlib, io, json, tarfile
from pathlib import Path, PurePosixPath
BASE = Path('docs/ui/ss-ui-vis-001-v1.1')
parts = [Path(f'.ui-import/part-{i:02}.b64').read_text() for i in range(9)]
data = base64.b64decode(''.join(parts), validate=True)
assert hashlib.sha256(data).hexdigest() == 'dcef7dd716abde1a34b8ecb385e0f9dee4622c93e1d2f0a7d85963e5196c0c85'
expected = json.loads(Path('.ui-import/source-files.json').read_text())
allowed = {x['path']: x for x in expected}
assert not BASE.exists(), 'Refuse to overwrite an existing design version'
BASE.mkdir(parents=True)
records = []
with tarfile.open(fileobj=io.BytesIO(data), mode='r:xz') as tar:
    members = tar.getmembers()
    assert len(members) == len(allowed) == 11
    assert len({m.name for m in members}) == 11
    for member in members:
        path = PurePosixPath(member.name)
        assert member.isfile() and not path.is_absolute() and '..' not in path.parts
        assert member.name in allowed
        raw = tar.extractfile(member).read()
        item = allowed[member.name]
        assert len(raw) == item['size']
        assert hashlib.sha256(raw).hexdigest() == item['sha256']
        target = 'README.source.md' if member.name == 'README.md' else member.name
        dest = BASE / target
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(raw)
        records.append({'source_path': member.name, 'path': target, 'sha256': item['sha256'], 'size_bytes': len(raw), 'byte_identical': True})
manifest = {
    'design': 'SS-UI-VIS-001', 'version': '1.1',
    'source_archive': '供包工作站_UIUX高保真交付_v1.1.zip',
    'source_archive_sha256': '191be3c69a75dd43dbe810f324e94697f216569e05c8ac5e280a381d364062fa',
    'version_basis': 'last complete UI delivery in the current design conversation; not a claim about an unexported Canva revision or a different conversation version',
    'publication_scope': 'user-requested generated final UI assets only; no raw customer PRD, private links, SDK or font binaries',
    'files': records,
    'original_screen_png_bytes_uploaded': False,
    'screen_provenance': 'PNG pages will be re-exported from the byte-identical original HTML; not historical screenshot bytes or Windows client evidence'
}
(BASE / 'source-manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n')
print('Verified and extracted 11 original design source files')
