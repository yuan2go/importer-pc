#!/usr/bin/env python3
"""Publish the registered business closure and its owned presentation selection."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile
from composition import resolve
from release_documents import SOURCE_PATHS, publish as publish_guides

p = argparse.ArgumentParser()
p.add_argument('--composition', choices=['inspection-reference', 'supply-assembly', 'sportmaster-reference', 'direct-plc-reference', 'wcs-to-plc-reference'], required=True)
p.add_argument('--target', choices=['portable', 'win-x64'], default='portable')
p.add_argument('--output', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
a = p.parse_args()
root = Path(__file__).resolve().parent.parent
if a.composition not in ['inspection-reference', 'supply-assembly']:
    subprocess.run(['python3', str(root / 'scripts/package-application.py'), '--application', a.composition.removesuffix('-reference'), '--target', a.target, '--output', str(a.output), '--dotnet', a.dotnet], cwd=root, check=True)
    raise SystemExit(0)
if a.target != 'portable':
    raise ValueError('Use package-reference.py for the legacy Supply Windows candidate; Inspection retains its portable assembly')
selected = resolve(root / 'config/compositions.json', a.composition)
output = a.output.resolve()
archive = Path(str(output) + '.zip')
if output.exists() or archive.exists():
    raise FileExistsError('Retain earlier candidates; choose a new output')
if subprocess.check_output(['git', 'status', '--porcelain', '--', *SOURCE_PATHS], cwd=root, text=True).strip():
    raise ValueError('Commit build inputs before creating a candidate')
source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
business = selected['product']['business']
module = {'inspection': 'Inspection', 'supply': 'Supply'}[business]
app = output / 'app'
subprocess.run([a.dotnet, 'publish', 'src/Ipc.Assembly/Ipc.Assembly.csproj', '-c', 'Release', '-m:1', '-nr:false',
                '-p:BusinessModule=' + module, '-p:NuGetLockFilePath=packages.' + business + '.lock.json',
                '-p:RestoreLockedMode=true', '--self-contained', 'false', '-o', str(app)], cwd=root, check=True)
if business == 'inspection':
    forbidden = list(app.glob('Ipc.Supply*.dll')) + list(app.glob('Ipc.Adapters*.dll')) + list(app.glob('Ipc.Product*.dll'))
    if forbidden:
        raise ValueError('Unselected implementation leaked into inspection closure')
    shutil.copytree(root / 'config/inspection', app / 'config')
else:
    subprocess.run(['python3', 'scripts/seal.py', '--require-clean-source', '--bin', str(app)], cwd=root, check=True)
selection = dict(SchemaVersion=1, Business=business, Views=selected['product']['views'], Layouts=selected['product']['layouts'],
                 EntryPoint='app/Ipc.Assembly.dll', SourceCommit=source, Windows='WINDOWS_NOT_RUN', Field='BLOCKED',
                 Binaries={f.name: hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted(app.glob('Ipc.*.dll'))})
(output / 'ASSEMBLY.json').write_text(json.dumps(selection, indent=2) + '\n')
selected['binaries'] = selection['Binaries']
(output / 'composition.lock.json').write_text(json.dumps(selected, indent=2) + '\n')
publish_guides(root, output, source)
entries = sorted(f for f in output.rglob('*') if f.is_file())
(output / 'SHA256SUMS').write_text(''.join(hashlib.sha256(f.read_bytes()).hexdigest() + '  ' + f.relative_to(output).as_posix() + '\n' for f in entries))
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
    for file in sorted(f for f in output.rglob('*') if f.is_file()):
        z.write(file, file.relative_to(output).as_posix())
print(json.dumps(dict(Package=str(archive), Sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), **selection)))
