#!/usr/bin/env python3
"""Build a fresh same-source reference package; cross-builds never attest Windows operation."""
import argparse
import hashlib
import json
import shutil
import subprocess
import zipfile
from composition import resolve
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--target', choices=['portable', 'win-x64'], required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--composition', choices=['core-reference', 'barcode-reference', 'integration-reference'], default='core-reference')
p.add_argument('--extension-key', type=Path)
p.add_argument('--extension-trust', type=Path)
p.add_argument('--mode', choices=['Simulation', 'Production'], default='Simulation')
p.add_argument('--product-definition', type=Path, default=Path('config/product.reference.json'))
p.add_argument('--no-restore', action='store_true')
a = p.parse_args()
root = Path(__file__).resolve().parent.parent
output = a.output.resolve()
composition = resolve(root / 'config/compositions.json', a.composition)
bundle = any(c['delivery'] == 'offline-bundle' for c in composition['components'])
if bundle and (a.extension_key is None or a.extension_trust is None):
    raise ValueError('Selected offline bundle needs an external signing key and independently provisioned reference trust policy')
if bundle and a.mode != 'Simulation':
    raise ValueError('The reference offline bundle is Simulation only; Production cannot contain test trust')
archive = Path(str(output) + '.zip')
if output.exists() or archive.exists():
    raise FileExistsError('Use a fresh output; prior packages retained')
source_changes = subprocess.check_output(['git', 'status', '--porcelain', '--', 'src', 'config', 'scripts', 'global.json', 'Directory.Build.props', 'NuGet.Config', 'ImporterPc.slnx', 'docs/runtime/operations.md', 'docs/extensions/developer-guide.md'], cwd=root, text=True).strip()
if source_changes:
    raise ValueError('Commit runtime/configuration/packaging sources before publication')
output.mkdir(parents=True)

def run(*args):
    subprocess.run(args, cwd=root, check=True)

projects = ['Ipc.Desktop', 'Ipc.Product'] if a.target == 'win-x64' else ['Ipc.Product']
if a.mode == 'Simulation':
    projects.append('Ipc.Fixture')
for project in projects:
    path = f'src/{project}/{project}.csproj'
    destination = output / ('fixture' if project == 'Ipc.Fixture' else 'app')
    target = ['-r', 'win-x64', '-p:NuGetLockFilePath=packages.win-x64.lock.json'] if a.target == 'win-x64' else []
    if not a.no_restore:
        run(a.dotnet, 'restore', path, '--locked-mode', *target)
    run(a.dotnet, 'publish', path, '-c', 'Release', '--no-restore', '-m:1', '-nr:false', '-p:EnableWindowsTargeting=true',
        '--self-contained', 'true' if a.target == 'win-x64' else 'false', *target, '-o', str(destination))
run('python3', str(root / 'scripts/seal.py'), '--require-clean-source', '--bin', str(output / 'app'),
    '--architecture', 'x64' if a.target == 'win-x64' else 'any', '--mode', a.mode, '--product-definition', str(a.product_definition))
for source, name in [('scripts/station.ps1', 'station.ps1'), ('scripts/provision-runtime.ps1', 'provision-runtime.ps1'), ('scripts/protect-release.ps1', 'protect-release.ps1'), ('scripts/provision-extensions.ps1', 'provision-extensions.ps1'), ('scripts/windows-reference-validation.ps1', 'windows-reference-validation.ps1'), ('docs/runtime/operations.md', 'OPERATIONS.md'), ('docs/extensions/developer-guide.md','EXTENSIONS.md'), ('global.json', 'global.json')]:
    shutil.copyfile(root / source, output / name)
composition['runtime'] = 'net10.0'
composition['rid'] = a.target
composition['host'] = json.loads((output / 'app/config/product.json').read_text())['Modules']
if bundle:
    (output / 'offline-extensions').mkdir()
    trust = json.loads(a.extension_trust.read_text())
    publishers = trust['Publishers']
    if trust['SchemaVersion'] != 1 or len(publishers) != 1 or publishers[0]['TestOnly'] is not True:
        raise ValueError('Reference composition requires an explicit separate TEST ONLY publisher')
    versions = {c['id']: c['version'] for c in composition['components']}
    package = output / f"offline-extensions/reference.barcode-{versions['reference.barcode']}.ipcx"
    run('python3', str(root / 'scripts/build-extension.py'), '--contract', str(output / 'app/Ipc.Platform.dll'), '--output', str(package),
        '--version', versions['reference.barcode'], '--key', str(a.extension_key.resolve()), '--publisher', publishers[0]['Id'], '--trust', str(a.extension_trust.resolve()), '--dotnet', a.dotnet)
    shutil.copyfile(a.extension_trust, output / 'offline-extensions/trust.Simulation.reference.json')
    composition['offline_bundles'] = [json.loads(Path(str(package) + '.identity.json').read_text())]
    if a.composition == 'integration-reference':
        package = output / f"offline-extensions/reference.integration-{versions['reference.integration']}.ipcx"
        run('python3', str(root / 'scripts/build-extension.py'), '--contract', str(output / 'app/Ipc.Platform.dll'), '--output', str(package),
            '--kind', 'integration', '--version', versions['reference.integration'], '--key', str(a.extension_key.resolve()), '--publisher', publishers[0]['Id'], '--trust', str(a.extension_trust.resolve()), '--dotnet', a.dotnet)
        composition['offline_bundles'].append(json.loads(Path(str(package) + '.identity.json').read_text()))
else:
    composition['offline_bundles'] = []
    if any('BarcodeExtension' in file.name for file in output.rglob('*')):
        raise ValueError('Unselected extension leaked into core composition')
(output / 'composition.lock.json').write_text(json.dumps(composition, indent=2) + '\n')
(output / 'dependencies').mkdir()
for lock in sorted((root / 'src').glob('*/packages*.lock.json')):
    shutil.copyfile(lock, output / 'dependencies' / (lock.parent.name + '-' + lock.name))
release = json.loads((output / 'app/release.json').read_text())
identity = dict(WorkPackage=release['WorkPackage'], SourceCommit=release['SourceCommit'], ReleaseVersion=release['ReleaseVersion'], StorageVersion=release['StorageVersion'],
                ContractVersion=release['ContractVersion'], SourceCodeSha256=release['SourceCodeSha256'], Target=a.target,
                WindowsRuntimeValidation='NOT_RUN', PhysicalIntegration='BLOCKED',
                Note='Portable requires pinned .NET runtime; win-x64 is self-contained and requires Windows validation.')
identity['Composition'] = a.composition
identity['Mode'] = a.mode
identity['ExtensionContract'] = 1
identity['MinimumReader'] = 3
identity['CompositionLockSha256'] = hashlib.sha256((output / 'composition.lock.json').read_bytes()).hexdigest()
(output / 'PACKAGE.json').write_text(json.dumps(identity, indent=2) + '\n')
entries = sorted(path for path in output.rglob('*') if path.is_file())
(output / 'SHA256SUMS').write_text(''.join(hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + path.relative_to(output).as_posix() + '\n' for path in entries))
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
    for path in sorted(output.rglob('*')):
        if path.is_file():
            z.write(path, path.relative_to(output).as_posix())
print(json.dumps(dict(Package=str(archive), Sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), **identity)))
