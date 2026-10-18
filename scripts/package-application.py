#!/usr/bin/env python3
"""Build a selected Application, its real WPF presentation, and its exact dependency closure."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import zipfile
from composition import resolve
from release_documents import SOURCE_PATHS, publish as publish_guides

APPLICATIONS = {'sportmaster': 'Sportmaster', 'direct-plc': 'DirectPlc', 'wcs-to-plc': 'WcsToPlc'}
p = argparse.ArgumentParser()
p.add_argument('--application', choices=APPLICATIONS, required=True)
p.add_argument('--target', choices=['portable', 'win-x64'], required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
a = p.parse_args()
root = Path(__file__).resolve().parent.parent
output = a.output.resolve()
archive = Path(str(output) + '.zip')
if output.exists() or archive.exists():
    raise FileExistsError('Choose a new immutable output')
if subprocess.check_output(['git', 'status', '--porcelain', '--', *SOURCE_PATHS], cwd=root, text=True).strip():
    raise ValueError('Commit exact runtime, configuration, packaging and shipped guides before publication')
source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
module = APPLICATIONS[a.application]
lock = 'packages.' + a.application + ('.win-x64' if a.target == 'win-x64' else '') + '.lock.json'
properties = ['-p:Application=' + module, '-p:NuGetLockFilePath=' + lock]
# Isolated artifacts avoid reusing another selection's intermediate WPF/assembly state.
artifacts = output / 'build-intermediates'
for project, destination in [('Ipc.Assembly', 'app'), *([('Ipc.Desktop', 'desktop')] if a.target == 'win-x64' else [])]:
    command = [a.dotnet, 'publish', f'src/{project}/{project}.csproj', '-c', 'Release', '-m:1', '-nr:false',
               '-p:EnableWindowsTargeting=true', '-p:RestoreLockedMode=true',
               '-p:PathMap=' + str(root) + '=/_/src%2C' + str(artifacts) + '=/_/build', *properties,
               '--artifacts-path', str(artifacts), '--self-contained', 'true' if a.target == 'win-x64' else 'false', '-o', str(output / destination)]
    if a.target == 'win-x64': command += ['-r', 'win-x64']
    subprocess.run(command, cwd=root, check=True)
    shutil.copytree(root / 'config' / a.application, output / destination / 'config')
shutil.rmtree(artifacts)
# Portable intentionally has no Windows binaries. The Windows candidate always contains
# the actual selected Application-owned WPF page and shared window/navigation/maintenance.
for base in [output / 'app', output / 'desktop']:
    if not base.exists(): continue
    binaries = {f.name for f in base.glob('Ipc.Application.*.dll')}
    others = {name for key, name in APPLICATIONS.items() if key != a.application}
    if any(f'Ipc.Application.{name}.dll' in binaries or f'Ipc.Application.{name}.Desktop.dll' in binaries for name in others):
        raise ValueError('Unselected Application leaked into candidate')
    if a.application == 'direct-plc' and (base / 'Ipc.Integrations.dll').exists():
        raise ValueError('DirectPlc must not depend on a WCS implementation')
if a.target == 'win-x64' and not (output / 'desktop' / f'Ipc.Application.{module}.Desktop.dll').is_file():
    raise ValueError('Selected WPF presentation was not delivered')
selection = resolve(root / 'config/compositions.json', a.application + '-reference')
for directory in [output / 'app', output / 'desktop']:
    if not directory.exists(): continue
    files = {f.relative_to(directory).as_posix(): hashlib.sha256(f.read_bytes()).hexdigest().upper()
             for f in sorted(directory.rglob('*')) if f.is_file()}
    release = dict(SchemaVersion=1, ApplicationId=a.application, SourceCommit=source,
                   Views=selection['product']['views'], Layouts=selection['product']['layouts'], Files=files)
    (directory / 'application-release.json').write_text(json.dumps(release, indent=2) + '\n')
selection.update(SourceCommit=source, Target=a.target, EntryPoint='app/Ipc.Assembly.dll',
                 DesktopEntryPoint='desktop/Ipc.Desktop.exe' if a.target == 'win-x64' else None,
                 Windows='NOT_RUN', PhysicalIntegration='BLOCKED',
                 Binaries={str(f.relative_to(output)): hashlib.sha256(f.read_bytes()).hexdigest() for f in sorted(output.rglob('Ipc.*.dll'))})
(output / 'ASSEMBLY.json').write_text(json.dumps(selection, indent=2) + '\n')
publish_guides(root, output, source)
if a.target == 'win-x64':
    for script in ['provision-runtime.ps1', 'provision-extensions.ps1']:
        shutil.copyfile(root / 'scripts' / script, output / script)
(output / 'APPLICATION-START.txt').write_text(
    'SIMULATION REFERENCE ONLY\nConsole: dotnet app/Ipc.Assembly.dll --config app/config --data-root <new-isolated-root>\n'
    + ('Windows: desktop/Ipc.Desktop.exe --config desktop/config --data-root <new-isolated-root>\n' if a.target == 'win-x64' else '')
    + 'Use APPLICATIONS.md for reference camera/weight input, original-stage reconciliation and recovery.\n')
entries = sorted(f for f in output.rglob('*') if f.is_file())
(output / 'SHA256SUMS').write_text(''.join(hashlib.sha256(f.read_bytes()).hexdigest() + '  ' + f.relative_to(output).as_posix() + '\n' for f in entries))
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
    for file in sorted(f for f in output.rglob('*') if f.is_file()): z.write(file, file.relative_to(output).as_posix())
print(json.dumps(dict(Package=str(archive), Sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), SourceCommit=source, Application=a.application, Target=a.target)))
