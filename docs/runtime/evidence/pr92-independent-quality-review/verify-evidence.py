from pathlib import Path
import hashlib, json, subprocess

root = Path.cwd()
evidence = root / 'docs/runtime/evidence/architecture-quality-remediation-01'
count = 0
for line in (evidence / 'SHA256SUMS').read_text().splitlines():
    expected, name = line.split('  ', 1)
    actual = hashlib.sha256((evidence / name).read_bytes()).hexdigest()
    assert actual == expected, name
    count += 1
archive = root / 'docs/archive/pre-architecture-quality-remediation-01'
manifest = json.loads((archive / 'manifest.json').read_text(encoding='utf-8-sig'))
for item in manifest['Files']:
    assert hashlib.sha256((archive / item['Archive']).read_bytes()).hexdigest() == item['Sha256'], item['Archive']
    blob = subprocess.check_output(['git', 'show', manifest['SourceCommit'] + ':' + item['Path']])
    assert hashlib.sha256(blob).hexdigest() == item['GitBlobSha256'], item['Path']
review_archive = root / 'docs/archive/pre-aqa01-pr92-review'
review_manifest = json.loads((review_archive / 'manifest.json').read_text(encoding='utf-8-sig'))
preserved = (review_archive / 'application-architecture-22.md.txt').read_bytes()
assert hashlib.sha256(preserved).hexdigest() == review_manifest['CheckoutSha256']
review_blob = subprocess.check_output(['git', 'show', review_manifest['SourceCommit'] + ':' + review_manifest['SourcePath']])
assert preserved.replace(b'\r\n', b'\n') == review_blob.replace(b'\r\n', b'\n')
print(json.dumps({'Result': 'PASS', 'OriginalEvidenceFiles': count, 'OriginalPreservedDocuments': len(manifest['Files']), 'BaselineGitBlobs': len(manifest['Files']), 'AdditionalDesignCheckoutHashAndBaselineContent': True}))
