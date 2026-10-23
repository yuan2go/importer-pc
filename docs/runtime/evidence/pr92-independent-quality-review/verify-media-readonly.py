from pathlib import Path
import hashlib, json
root = Path.cwd()
inventory = json.loads((root / 'docs/runtime/evidence/architecture-quality-remediation-01/package-inventory.json').read_text())
base = Path('D:/audits/importer-pc/AQA01-remediation-20261023')
checked = 0
for item in inventory:
    package = base / 'packages' / item['Package']
    assert hashlib.sha256(package.read_bytes()).hexdigest() == item['Sha256'], package
    target = Path(item['Path'])
    identity = json.loads((target / 'ASSEMBLY.json').read_bytes())
    assert identity['SourceCommit'] == item['SourceCommit'] and identity['BusinessVersion'] == item['Version']
    count = 0
    for line in (target / 'SHA256SUMS').read_text().splitlines():
        expected, name = line.split('  ', 1)
        assert hashlib.sha256((target / name).read_bytes()).hexdigest() == expected, name
        count += 1
    assert count == item['CheckedFiles']
    checked += count
print(json.dumps({'Result': 'PASS', 'FrozenPackages': len(inventory), 'CheckedManifestFiles': checked, 'SourceCommit': inventory[0]['SourceCommit'], 'ReadOnly': True}))
