#!/usr/bin/env python3
"""Build a selected Application, its real WPF presentation, and its exact dependency closure."""
import argparse
import os
from mvs_runtime import publish as publish_mvs
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import tarfile
import zipfile
from release_build import frozen_export, build_properties, cleanup, archive_package
from composition import resolve
from application_compatibility import read_business
from application_bundles import read_packages, publish_packages
from release_documents import SOURCE_PATHS, publish as publish_guides
from release_dependencies import publish as publish_dependencies
from release_provenance import capture as capture_provenance, publish as publish_provenance

APPLICATIONS = {'sportmaster': 'Sportmaster', 'direct-plc': 'DirectPlc', 'wcs-to-plc': 'WcsToPlc'}
p = argparse.ArgumentParser()
p.add_argument('--application', choices=APPLICATIONS, required=True)
p.add_argument('--target', choices=['portable', 'win-x64'], required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--source-branch', help='Verifiable branch for detached release builds; otherwise detached CI records DETACHED')
p.add_argument('--composition', help='Existing composition matching the selected Application; defaults to its reference core')
p.add_argument('--extension-package', type=Path, action='append', default=[], help='Exact signed offline Driver/Component package; repeat for its complete dependency closure')
p.add_argument('--extension-trust', type=Path, help='Independent trust used only for static acceptance; never copied into the candidate')
p.add_argument('--extension-check-host', type=Path, help='Runnable Assembly with byte-identical public DLLs for a Windows target')
p.add_argument('--mvs-runtime', type=Path, default=os.environ.get('IPC_MVS_RUNTIME'), help='Build input: reviewed current x64 MVS GigE runtime; never an operator step')
p.add_argument('--verification-without-vendor-runtime', action='store_true', default=os.environ.get('IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME') == '1', help='Explicit non-delivery verification medium when vendor files are unavailable')
a = p.parse_args()
root = Path(__file__).resolve().parent.parent
output = a.output.resolve()
archive = Path(str(output) + '.zip')
if output.exists() or archive.exists():
    raise FileExistsError('Choose a new immutable output')
if subprocess.check_output(['git', 'status', '--porcelain', '--', *SOURCE_PATHS], cwd=root, text=True).strip():
    raise ValueError('Commit exact runtime, configuration, packaging and shipped guides before publication')
source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=root, text=True).strip()
provenance = capture_provenance(root, source, a.source_branch, a.application, '', a.target)
module = APPLICATIONS[a.application]
lock = 'packages.' + a.application + ('.win-x64' if a.target == 'win-x64' else '') + '.lock.json'
properties = ['-p:Application=' + module, '-p:NuGetLockFilePath=' + lock]
# Isolated artifacts avoid reusing another selection's intermediate WPF/assembly state.
workspace = Path(tempfile.mkdtemp(prefix='ipc-publication-'))
print('Retained build workspace on failure: ' + str(workspace), flush=True)
artifacts = workspace / 'build-intermediates'
# The SDK excludes its entire artifacts tree from default source globs.
build_source = workspace / 'source-snapshot'
extraction = frozen_export(root, source, build_source)
selection = resolve(build_source / 'config/compositions.json', a.composition or a.application + '-reference')
if selection['product']['business'] != a.application:
    raise ValueError('Composition must select the requested Application')
bundles = read_packages(a.extension_package, selection)
if bundles and a.extension_trust is None:
    raise ValueError('Selected offline packages require independent trust for target-host acceptance')
business, compatibility_source = read_business(build_source, a.application, selection)
provenance['BusinessVersion'] = business['BusinessVersion']
frozen_properties = build_properties(source, build_source, artifacts)
for project, destination in [('Ipc.Assembly', 'app'), *([('Ipc.Desktop', 'desktop')] if a.target == 'win-x64' else [])]:
    command = [a.dotnet, 'publish', f'src/{project}/{project}.csproj', '-c', 'Release', '-m:1', '-nr:false',
               '-p:EnableWindowsTargeting=true', '-p:RestoreLockedMode=true',
               *frozen_properties, *properties,
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
# Portable uses the framework-dependent DLL entry; a Windows build may also emit an apphost.
# The self-contained Windows candidate always contains
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
if a.target == 'win-x64':
    for binary in (output / 'app').glob('Ipc.*.dll'):
        peer = output / 'desktop' / binary.name
        if peer.exists() and hashlib.sha256(binary.read_bytes()).digest() != hashlib.sha256(peer.read_bytes()).digest():
            raise ValueError('Console/helper and desktop bytes differ: ' + binary.name)
if a.application == 'sportmaster' and a.target == 'win-x64':
    provenance['VendorRuntime'] = publish_mvs(build_source, output, a.mvs_runtime, a.verification_without_vendor_runtime)
publish_packages(bundles, selection, output, a.dotnet, a.extension_trust, a.extension_check_host)
publish_provenance(output, provenance)
selection.update(business)
for directory in [output / 'app', output / 'desktop']:
    if not directory.exists(): continue
    shutil.copyfile(compatibility_source, directory / 'application-compatibility.json')
    files = {f.relative_to(directory).as_posix(): hashlib.sha256(f.read_bytes()).hexdigest().upper()
             for f in sorted(directory.rglob('*')) if f.is_file()}
    release = dict(SchemaVersion=2, ApplicationId=a.application, SourceCommit=source, **business,
                   Views=selection['product']['views'], Layouts=selection['product']['layouts'], Files=files)
    (directory / 'application-release.json').write_text(json.dumps(release, indent=2) + '\n')
selection.update(ApplicationId=a.application, SourceCommit=source, Target=a.target, ApplicationContractVersion=next(c['version'] for c in selection['components'] if c['id'] == 'application-contracts'), DatabaseFormat=10, EntryPoint='app/Ipc.Assembly.dll',
                 DesktopEntryPoint='desktop/Ipc.Desktop.exe' if a.target == 'win-x64' else None,
                 Windows='NOT_RUN', PhysicalIntegration='BLOCKED',
                 Binaries={str(f.relative_to(output)): hashlib.sha256(f.read_bytes()).hexdigest()
                           for directory in [output / 'app', output / 'desktop'] if directory.exists()
                           for f in sorted(directory.rglob('Ipc.*.dll'))})
(output / 'composition.lock.json').write_text(json.dumps(selection, indent=2) + '\n')
(output / 'ASSEMBLY.json').write_text(json.dumps(selection, indent=2) + '\n')
publish_guides(build_source, output, source, a.application)
publish_dependencies(build_source, output, source)
shutil.copyfile(build_source / 'scripts/stability-qualification.py', output / 'stability-qualification.py')
shutil.copyfile(build_source / 'config/stability-target-plan.example.json', output / 'stability-target-plan.example.json')
if a.application == 'sportmaster':
    shutil.copyfile(build_source / 'scripts/diagnose-camera-network.ps1', output / 'diagnose-camera-network.ps1')
if a.target == 'win-x64':
    for script in ['protect-release.ps1', 'provision-runtime.ps1', 'provision-extensions.ps1', 'application.ps1', 'application.cmd', 'Setup.cmd', 'Maintenance.cmd']:
        shutil.copyfile(build_source / 'scripts' / script, output / script)
cleanup(build_source, extraction, artifacts, workspace)
(output / 'APPLICATION-START.txt').write_text(
    'Installed Sportmaster: Production first configuration; unpacked CLI configuration remains an explicit Simulation reference.\nConsole: dotnet app/Ipc.Assembly.dll --config app/config --data-root <new-isolated-root>\n'
    + ('Windows: double-click Setup.cmd; first launch from an unpacked package also opens the native installation wizard (no PowerShell required).\nChoose install/data folders and shortcuts; review then approve Windows UAC. Initial drivers are selected by the verified medium; no account name or trust-file entry is required. Program files are installed side by side and data is retained.\nStart from the installed Start Menu shortcut. Offline startup repair: Maintenance.cmd or the installed startup-repair shortcut; business/devices do not need to start.\nThe installer displays the new data directory under the configured machine storage root; same-format overwrite installs retain the registered data root; non-current formats require a fresh data directory. Installer selections are retained for both desktop and console. Use PRODUCT-OPERATIONS.md for protected custom roots, first configuration repair, system maintenance, current-format backup/restore and batch plans.\n' if a.target == 'win-x64' else '')
    + ('Sportmaster default native UI: desktop/Ipc.Desktop.exe (supply-station)\nUse LIFECYCLE-OPERATIONS.md for current native workflows and validation boundaries.\n' if a.target == 'win-x64' and a.application == 'sportmaster' else '')
    + 'Use APPLICATIONS.md for reference camera/weight input, original-stage reconciliation and recovery.\n')
archive_package(output, archive, root, source)
print(json.dumps(dict(Package=str(archive), Sha256=hashlib.sha256(archive.read_bytes()).hexdigest(), SourceCommit=source, Application=a.application, Target=a.target, **business)))
