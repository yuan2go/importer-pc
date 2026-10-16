#!/usr/bin/env python3
"""Build a fresh same-source reference package; cross-builds never attest Windows operation."""
import argparse
import hashlib
import json
import shutil
import subprocess
import zipfile
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--target', choices=['portable', 'win-x64'], required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parent.parent
output = a.output.resolve()
archive = Path(str(output) + '.zip')
if output.exists() or archive.exists():
    raise FileExistsError('Use a fresh output; prior packages retained')
source_changes = subprocess.check_output(['git', 'status', '--porcelain', '--', 'src', 'config', 'scripts', 'global.json', 'Directory.Build.props', 'NuGet.Config', 'ImporterPc.slnx'], cwd=root, text=True).strip()
if source_changes:
    raise ValueError('Commit runtime/configuration/packaging sources before publication')
output.mkdir(parents=True)

def run(*args):
    subprocess.run(args, cwd=root, check=True)

for project in ['Ipc.Desktop', 'Ipc.Product', 'Ipc.Fixture'] if a.target == 'win-x64' else ['Ipc.Product', 'Ipc.Fixture']:
    path = f'src/{project}/{project}.csproj'
    destination = output / ('fixture' if project == 'Ipc.Fixture' else 'app')
    target = ['-r', 'win-x64', '-p:NuGetLockFilePath=packages.win-x64.lock.json'] if a.target == 'win-x64' else []
    run(a.dotnet, 'restore', path, '--locked-mode', *target)
    run(a.dotnet, 'publish', path, '-c', 'Release', '--no-restore', '-m:1', '-nr:false', '-p:EnableWindowsTargeting=true',
        '--self-contained', 'true' if a.target == 'win-x64' else 'false', *target, '-o', str(destination))
run('python3', str(root / 'scripts/seal.py'), '--require-clean-source', '--bin', str(output / 'app'),
    '--architecture', 'x64' if a.target == 'win-x64' else 'any')
for source, name in [('scripts/station.ps1', 'station.ps1'), ('scripts/provision-runtime.ps1', 'provision-runtime.ps1'), ('docs/runtime/operations.md', 'OPERATIONS.md'), ('global.json', 'global.json')]:
    shutil.copyfile(root / source, output / name)
(output / 'dependencies').mkdir()
for lock in sorted((root / 'src').glob('*/packages*.lock.json')):
    shutil.copyfile(lock, output / 'dependencies' / (lock.parent.name + '-' + lock.name))
release = json.loads((output / 'app/release.json').read_text())
identity = dict(SourceCommit=release['SourceCommit'], ReleaseVersion=release['ReleaseVersion'], StorageVersion=release['StorageVersion'],
                ContractVersion=release['ContractVersion'], SourceCodeSha256=release['SourceCodeSha256'], Target=a.target,
                WindowsRuntimeValidation='NOT_RUN', PhysicalIntegration='BLOCKED',
                Note='Portable requires pinned .NET runtime; win-x64 is self-contained and requires Windows validation.')
(output / 'PACKAGE.json').write_text(json.dumps(identity, indent=2) + '\n')
entries = sorted(path for path in output.rglob('*') if path.is_file())
(output / 'SHA256SUMS').write_text(''.join(hashlib.sha256(path.read_bytes()).hexdigest() + '  ' + path.relative_to(output).as_posix() + '\n' for path in entries))
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
    for path in sorted(output.rglob('*')):
        if path.is_file():
            z.write(path, path.relative_to(output).as_posix())
print(json.dumps(dict(Package=str(archive), Sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), **identity)))
