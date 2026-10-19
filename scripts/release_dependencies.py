"""Record resolved package provenance and retain declared license texts with a candidate."""
import hashlib
import json
import os
from pathlib import Path
import shutil
import xml.etree.ElementTree as ET


def publish(root, output, source):
    deps = output / 'app/Ipc.Assembly.deps.json'
    libraries = json.loads(deps.read_text(encoding='utf-8'))['libraries']
    desktop = output / 'desktop/Ipc.Desktop.deps.json'
    if desktop.exists():
        libraries = {**libraries, **json.loads(desktop.read_text(encoding='utf-8'))['libraries']}
    packages = Path(os.environ.get('NUGET_PACKAGES', Path.home() / '.nuget/packages'))
    records = []
    for identity, item in sorted(libraries.items()):
        if item['type'] != 'package':
            continue
        name, version = identity.rsplit('/', 1)
        spec = packages / name.lower() / version.lower() / (name.lower() + '.nuspec')
        if not spec.is_file():
            raise ValueError('Resolved package license metadata missing: ' + identity)
        metadata = ET.parse(spec).getroot().find('{*}metadata')
        license = metadata.find('{*}license')
        if license is None or license.attrib.get('type') != 'expression' or license.text not in ('MIT', 'Apache-2.0'):
            raise ValueError('New package requires explicit redistribution review: ' + identity)
        records.append(dict(Id=name, Version=version, Sha512=item.get('sha512'), LicenseExpression=license.text,
                            Copyright=metadata.findtext('{*}copyright'),
                            Package='https://www.nuget.org/packages/' + name + '/' + version,
                            NuspecSha256=hashlib.sha256(spec.read_bytes()).hexdigest()))
    licenses = output / 'licenses'; licenses.mkdir()
    for name in sorted(set(r['LicenseExpression'] for r in records)):
        shutil.copyfile(root / 'docs/licenses' / (name + '.txt'), licenses / (name + '.txt'))
    record = dict(SchemaVersion=1, SourceCommit=source, Source='Resolved app deps.json and restored NuGet nuspec metadata',
                  Packages=records, RuntimeNoticeFiles=[p.relative_to(output).as_posix() for area in ('app', 'desktop') for p in (output / area).rglob('*')
                      if p.is_file() and p.name.lower() in ('license.txt', 'thirdpartynotices.txt')],
                  ExternalPrerequisites=['PowerShell 7 only for explicit advanced .ps1 scripts; native Setup/Run/Maintenance do not require it', 'Approved vendor SDK/driver/runtime only for a qualified real combination'],
                  VendorSdkRedistribution='BLOCKED: no approved vendor SDK or redistribution grant supplied',
                  ProductLicense='No project-wide commercial license grant is inferred from third-party package declarations')
    (output / 'DEPENDENCIES.json').write_text(json.dumps(record, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
