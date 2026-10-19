#!/usr/bin/env python3
"""Build a selected Application, its real WPF presentation, and its exact dependency closure."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import zipfile
from composition import resolve
from application_compatibility import read_business
from application_bundles import read_packages, publish_packages
from release_documents import SOURCE_PATHS, publish as publish_guides
from release_dependencies import publish as publish_dependencies

APPLICATIONS = {'sportmaster': 'Sportmaster', 'direct-plc': 'DirectPlc', 'wcs-to-plc': 'WcsToPlc'}
p = argparse.ArgumentParser()
p.add_argument('--application', choices=APPLICATIONS, required=True)
p.add_argument('--target', choices=['portable', 'win-x64'], required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--composition', help='Existing composition matching the selected Application; defaults to its reference core')
p.add_argument('--extension-package', type=Path, action='append', default=[], help='Exact signed offline Driver/Component package; repeat for its complete dependency closure')
p.add_argument('--extension-trust', type=Path, help='Independent trust used only for static acceptance; never copied into the candidate')
p.add_argument('--extension-check-host', type=Path, help='Runnable Assembly with byte-identical public DLLs for a Windows target')
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
# The SDK excludes its entire artifacts tree from default source globs.
build_source = output / 'source-snapshot'
build_source.mkdir(parents=True)
export = subprocess.Popen(['git', 'archive', '--format=tar', source], cwd=root, stdout=subprocess.PIPE)
with tarfile.open(fileobj=export.stdout, mode='r|') as snapshot:
    # Historical evidence contains deep paths. Use the Windows extended path
    # syntax for archive I/O only; do not change machine LongPaths/UAC policies.
    extraction_root = ('\\\\?\\' + str(build_source)) if os.name == 'nt' else str(build_source)
    snapshot.extractall(extraction_root, filter='data')
if export.wait(): raise ValueError('Frozen source export failed')
selection = resolve(build_source / 'config/compositions.json', a.composition or a.application + '-reference')
if selection['product']['business'] != a.application:
    raise ValueError('Composition must select the requested Application')
bundles = read_packages(a.extension_package, selection)
if bundles and a.extension_trust is None:
    raise ValueError('Selected offline packages require independent trust for target-host acceptance')
business, compatibility_source = read_business(build_source, a.application, selection)
for project, destination in [('Ipc.Assembly', 'app'), *([('Ipc.Desktop', 'desktop')] if a.target == 'win-x64' else [])]:
    command = [a.dotnet, 'publish', f'src/{project}/{project}.csproj', '-c', 'Release', '-m:1', '-nr:false',
               '-p:EnableWindowsTargeting=true', '-p:RestoreLockedMode=true',
               '-p:PathMap=' + str(build_source) + '=/_/src%2C' + str(artifacts) + '=/_/build', '-p:SourceRevisionId=' + source, *properties,
               '--artifacts-path', str(artifacts), '--self-contained', 'true' if a.target == 'win-x64' else 'false', '-o', str(output / destination)]
    if a.target == 'win-x64': command += ['-r', 'win-x64']
    subprocess.run(command, cwd=build_source, check=True)
    published = json.loads((output / destination / (project + '.deps.json')).read_bytes())
    contract_version = next(c['version'] for c in selection['components'] if c['id'] == 'application-contracts')
    if 'Ipc.Application.Contracts/' + contract_version not in published['libraries']:
        raise ValueError('Published Application contract differs from composition version')
    shutil.copytree(build_source / 'config' / a.application, output / destination / 'config')
if a.target == 'portable':
    compiled = json.loads(subprocess.check_output([a.dotnet, str(output / 'app/Ipc.Assembly.dll'), '--action', 'business-identity'], text=True))
    if compiled['ApplicationId'] != a.application or any(compiled[key] != value for key, value in business.items()):
        raise ValueError('Compiled business version/declaration differs from publication inputs')
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
publish_packages(bundles, selection, output, a.dotnet, a.extension_trust, a.extension_check_host)
selection.update(business)
for directory in [output / 'app', output / 'desktop']:
    if not directory.exists(): continue
    shutil.copyfile(compatibility_source, directory / 'application-compatibility.json')
    files = {f.relative_to(directory).as_posix(): hashlib.sha256(f.read_bytes()).hexdigest().upper()
             for f in sorted(directory.rglob('*')) if f.is_file()}
    release = dict(SchemaVersion=2, ApplicationId=a.application, SourceCommit=source, **business,
                   Views=selection['product']['views'], Layouts=selection['product']['layouts'], Files=files)
    (directory / 'application-release.json').write_text(json.dumps(release, indent=2) + '\n')
selection.update(ApplicationId=a.application, SourceCommit=source, Target=a.target, EntryPoint='app/Ipc.Assembly.dll',
                 DesktopEntryPoint='desktop/Ipc.Desktop.exe' if a.target == 'win-x64' else None,
                 Windows='NOT_RUN', PhysicalIntegration='BLOCKED',
                 Binaries={str(f.relative_to(output)): hashlib.sha256(f.read_bytes()).hexdigest()
                           for directory in [output / 'app', output / 'desktop'] if directory.exists()
                           for f in sorted(directory.rglob('Ipc.*.dll'))})
(output / 'composition.lock.json').write_text(json.dumps(selection, indent=2) + '\n')
(output / 'ASSEMBLY.json').write_text(json.dumps(selection, indent=2) + '\n')
publish_guides(build_source, output, source, a.application)
publish_dependencies(build_source, output, source)
if a.target == 'win-x64':
    for script in ['protect-release.ps1', 'provision-runtime.ps1', 'provision-extensions.ps1', 'application.ps1', 'application.cmd', 'Setup.cmd', 'Maintenance.cmd']:
        shutil.copyfile(build_source / 'scripts' / script, output / script)
shutil.rmtree(artifacts)
if build_source.parent != output or build_source.name != 'source-snapshot':
    raise ValueError('Refuse cleanup outside the generated frozen snapshot')
shutil.rmtree(extraction_root)
(output / 'APPLICATION-START.txt').write_text(
    'SIMULATION REFERENCE ONLY\nConsole: dotnet app/Ipc.Assembly.dll --config app/config --data-root <new-isolated-root>\n'
    + ('Windows: double-click Setup.cmd; first launch from an unpacked package also opens the native installation wizard (no PowerShell required).\nChoose runtime account, original data root, configuration source and optional independent trust/offline packages; review then approve Windows UAC. Program files are installed side by side and data is retained.\nStart from the installed Start Menu shortcut. Offline startup repair: Maintenance.cmd or the installed startup-repair shortcut; business/devices do not need to start.\nDefault data: %LOCALAPPDATA%\\ImporterPc\\data; installer selections are retained for both desktop and console. Use PRODUCT-OPERATIONS.md for protected custom roots, first configuration repair, system maintenance, backup/migration and batch plans.\n' if a.target == 'win-x64' else '')
    + ('Sportmaster alternate native UI: desktop/Ipc.Desktop.exe --ui supply-station\nClassic default: omit --ui, or use --ui classic. Use SPORTMASTER-UI.md and SPORTMASTER-UI-WINDOWS.md.\n' if a.target == 'win-x64' and a.application == 'sportmaster' else '')
    + 'Use APPLICATIONS.md for reference camera/weight input, original-stage reconciliation and recovery.\n')
entries = sorted(f for f in output.rglob('*') if f.is_file())
(output / 'SHA256SUMS').write_text(''.join(hashlib.sha256(f.read_bytes()).hexdigest() + '  ' + f.relative_to(output).as_posix() + '\n' for f in entries))
with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED) as z:
    for file in sorted(f for f in output.rglob('*') if f.is_file()): z.write(file, file.relative_to(output).as_posix())
print(json.dumps(dict(Package=str(archive), Sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), SourceCommit=source, Application=a.application, Target=a.target, **business)))
