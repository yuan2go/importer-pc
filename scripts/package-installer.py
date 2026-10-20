"""Wrap a verified same-source Windows medium in one self-contained native installer."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile
from release_provenance import capture as capture_provenance, installer_name, MODULE_SHORT_NAMES
from release_build import frozen_export, build_properties
from application_compatibility import APPLICATIONS

ROOT = Path(__file__).resolve().parents[1]

def verify_shared_binaries(media, binaries):
    """The UI is reused in-process; every shared DLL must equal the verified medium."""
    with zipfile.ZipFile(media) as archive:
        selected = [name for name in archive.namelist() if name.startswith('desktop/Ipc.') and name.endswith('.dll')]
        if not selected:
            raise ValueError('Missing selected desktop assembly closure')
        for name in selected:
            file = binaries / Path(name).name
            if not file.is_file() or hashlib.sha256(file.read_bytes()).digest() != hashlib.sha256(archive.read(name)).digest():
                raise ValueError('Installer UI and embedded desktop bytes differ: ' + name)


def safe_name(name):
    if not name or '\\' in name or name.startswith('/') or any(ord(c) < 32 or c in '<>:"|?*' for c in name):
        raise ValueError('Unsafe installer payload path: ' + name)
    for part in name.split('/'):
        stem = part.split('.')[0].upper()
        if not part or part in ('.', '..') or part.endswith(('.', ' ')) or stem in ('CON', 'PRN', 'AUX', 'NUL') or stem in {p + str(n) for p in ('COM', 'LPT') for n in range(1, 10)}:
            raise ValueError('Unsafe installer payload path: ' + name)
    return name


def inspect_medium(path, source):
    with zipfile.ZipFile(path) as archive:
        names = {}
        total = 0
        for entry in archive.infolist():
            if entry.orig_filename != entry.filename:
                raise ValueError('Unsafe installer payload path: ' + entry.orig_filename)
            name = safe_name(entry.filename)
            if name.casefold() in names or (entry.external_attr >> 16) & 0xF000 == 0xA000 or entry.external_attr & 0x400:
                raise ValueError('Duplicate or linked payload path: ' + name)
            names[name.casefold()] = name
            total += entry.file_size
        hashes = {}
        for line in archive.read('SHA256SUMS').decode('utf-8').splitlines():
            if len(line) < 67 or line[64:66] != '  ' or any(c not in '0123456789abcdefABCDEF' for c in line[:64]):
                raise ValueError('Invalid payload inventory')
            name = safe_name(line[66:])
            if name.casefold() in hashes or name.casefold() == 'sha256sums':
                raise ValueError('Duplicate inventory identity')
            hashes[name.casefold()] = line[:64].lower()
        if set(names) != set(hashes) | {'sha256sums'}:
            raise ValueError('Missing or unlisted payload files')
        for name, expected in hashes.items():
            with archive.open(names[name]) as stream:
                if hashlib.file_digest(stream, 'sha256').hexdigest() != expected:
                    raise ValueError('Payload hash mismatch: ' + name)
        releases = [json.loads(archive.read(area + '/application-release.json')) for area in ('app', 'desktop')]
        if any(r['SourceCommit'] != source for r in releases) or any(releases[0][key] != releases[1][key] for key in ('ApplicationId', 'BusinessVersion')):
            raise ValueError('Installer and embedded medium must use the same frozen source')
        if releases[0]['ApplicationId'] not in ('sportmaster', 'direct-plc', 'wcs-to-plc') or 'desktop/ipc.desktop.exe' not in names:
            raise ValueError('Expected a complete supported Windows Application')
        if 'build-info.json' not in names or archive.getinfo(names['build-info.json']).file_size > 65536:
            raise ValueError('Expected the packaged BUILD-INFO source record')
        provenance_bytes = archive.read(names['build-info.json'])
        provenance = json.loads(provenance_bytes)
        if (provenance.get('SourceCommit') != source or provenance.get('BusinessVersion') != releases[0]['BusinessVersion']
                or provenance.get('ApplicationId') != releases[0]['ApplicationId'] or provenance.get('Target') != 'win-x64'
                or provenance.get('BusinessModuleShortName') != MODULE_SHORT_NAMES[releases[0]['ApplicationId']]
                or not provenance.get('SourceBranch') or provenance.get('SourceBranchVerified') is not True
                or provenance.get('FrozenGitExport') is not True):
            raise ValueError('Packaged build source record differs from this release')
        for area in ('app', 'desktop'):
            if archive.read(area + '/BUILD-INFO.json') != provenance_bytes:
                raise ValueError('Packaged build source records disagree')
        with Path(path).open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        return dict(ApplicationId=releases[0]['ApplicationId'], SourceCommit=source, SourceBranch=provenance['SourceBranch'],
                    BusinessModuleShortName=provenance['BusinessModuleShortName'],
                    BusinessVersion=releases[0]['BusinessVersion'], Sha256=digest,
                    ZipBytes=Path(path).stat().st_size, UnpackedBytes=total, FileCount=len(names))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--media', type=Path, required=True, help='Frozen win-x64 ZIP from package-application.py')
    parser.add_argument('--output', type=Path, required=True, help='New delivery directory, or the canonical English versioned EXE path')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    if subprocess.check_output(['git', 'status', '--porcelain', '--', 'src', 'scripts', 'Directory.Build.props', 'global.json'], cwd=ROOT, text=True).strip():
        raise ValueError('Commit installer, payload runtime and packaging sources first')
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    descriptor = inspect_medium(args.media.resolve(), source)
    capture_provenance(ROOT, source, descriptor['SourceBranch'], descriptor['ApplicationId'], descriptor['BusinessVersion'], 'win-x64')
    name = installer_name(descriptor['ApplicationId'], descriptor['BusinessVersion'])
    output = args.output.resolve()
    if output.suffix.lower() == '.exe':
        if output.name != name:
            raise ValueError('Use the versioned English installer filename: ' + name)
    else:
        output = output / name
    if output.exists():
        raise ValueError('Choose a new immutable installer output')
    workspace = Path(tempfile.mkdtemp(prefix='ipc-installer-build-'))
    print('Installer build workspace: ' + str(workspace), flush=True)
    snapshot = workspace / 'source-snapshot'
    frozen_export(ROOT, source, snapshot)
    payload = workspace / 'payload.zip'; shutil.copyfile(args.media, payload)
    metadata = workspace / 'payload.json'; metadata.write_text(json.dumps(descriptor, ensure_ascii=False), encoding='utf-8')
    publish = workspace / 'publish'
    artifacts = workspace / 'intermediates'
    module = APPLICATIONS[descriptor['ApplicationId']]
    subprocess.run([args.dotnet, 'publish', 'src/Ipc.Installer/Ipc.Installer.csproj', '-c', 'Release', '-m:1', '-nr:false', '-r', 'win-x64', '--self-contained', 'true',
                    '-p:InstallerPayload=' + str(payload), '-p:InstallerDescriptor=' + str(metadata),
                    '-p:Application=' + module, '-p:NuGetLockFilePath=packages.' + descriptor['ApplicationId'] + '.win-x64.lock.json',
                    '-p:RestoreLockedMode=true', *build_properties(source, snapshot, artifacts),
                    '-p:CopyOutputSymbolsToPublishDirectory=false',
                    '-p:ArtifactsPath=' + str(artifacts), '-p:InstallerBusinessVersion=' + descriptor['BusinessVersion'],
                    '-o', str(publish)], cwd=snapshot, check=True)
    verify_shared_binaries(args.media, artifacts / 'bin/Ipc.Installer/release_win-x64')
    published = [file for file in publish.iterdir() if file.is_file()]
    if len(published) != 1 or published[0].name != 'Ipc.Installer.exe':
        raise ValueError('Installer is not a standalone EXE: ' + repr([p.name for p in published]))
    output.parent.mkdir(parents=True, exist_ok=True)
    # Exclusive destination creation prevents accidentally replacing a prior delivery.
    with published[0].open('rb') as source_file, output.open('xb') as target:
        shutil.copyfileobj(source_file, target)
    with output.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    print(json.dumps(dict(Installer=str(output), Sha256=digest, SourceCommit=source, Payload=descriptor,
                         DeliveryFiles=1, Authenticode='NOT_SIGNED', ActualWindowsInstallation='NOT_RUN'), ensure_ascii=False))


if __name__ == '__main__':
    main()
