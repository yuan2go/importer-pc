"""Wrap a verified same-source Windows medium in one self-contained native installer."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]


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
        if any(r['SourceCommit'] != source for r in releases) or releases[0]['ApplicationId'] != releases[1]['ApplicationId']:
            raise ValueError('Installer and embedded medium must use the same frozen source')
        if releases[0]['ApplicationId'] not in ('sportmaster', 'direct-plc', 'wcs-to-plc') or 'desktop/ipc.desktop.exe' not in names:
            raise ValueError('Expected a complete supported Windows Application')
        with Path(path).open('rb') as stream:
            digest = hashlib.file_digest(stream, 'sha256').hexdigest()
        return dict(ApplicationId=releases[0]['ApplicationId'], SourceCommit=source,
                    BusinessVersion=releases[0]['BusinessVersion'], Sha256=digest,
                    ZipBytes=Path(path).stat().st_size, UnpackedBytes=total, FileCount=len(names))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--media', type=Path, required=True, help='Frozen win-x64 ZIP from package-application.py')
    parser.add_argument('--output', type=Path, required=True, help='New .exe filename; no sidecar files are required')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    output = args.output.resolve()
    if output.suffix.lower() != '.exe' or output.exists():
        raise ValueError('Choose a new immutable .exe output')
    if subprocess.check_output(['git', 'status', '--porcelain', '--', 'src', 'scripts', 'Directory.Build.props', 'global.json'], cwd=ROOT, text=True).strip():
        raise ValueError('Commit installer, payload runtime and packaging sources first')
    source = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip()
    descriptor = inspect_medium(args.media.resolve(), source)
    workspace = Path(tempfile.mkdtemp(prefix='ipc-installer-build-'))
    print('Installer build workspace: ' + str(workspace), flush=True)
    snapshot = workspace / 'source'; snapshot.mkdir()
    files = subprocess.check_output(['git', 'ls-tree', '-r', '--name-only', source, '--', 'src/Ipc.Installer'], cwd=ROOT, text=True).splitlines()
    files += ['Directory.Build.props', 'global.json', 'NuGet.Config', 'src/Ipc.Desktop/Assets/importer-pc.ico', 'src/Ipc.Desktop/Assets/cainiao.png']
    for name in files:
        target = snapshot / name; target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(subprocess.check_output(['git', 'show', source + ':' + name], cwd=ROOT))
    payload = workspace / 'payload.zip'; shutil.copyfile(args.media, payload)
    metadata = workspace / 'payload.json'; metadata.write_text(json.dumps(descriptor, ensure_ascii=False), encoding='utf-8')
    publish = workspace / 'publish'
    subprocess.run([args.dotnet, 'publish', 'src/Ipc.Installer/Ipc.Installer.csproj', '-c', 'Release', '-m:1', '-nr:false',
                    '-p:InstallerPayload=' + str(payload), '-p:InstallerDescriptor=' + str(metadata),
                    '-p:ArtifactsPath=' + str(workspace / 'intermediates'), '-p:SourceRevisionId=' + source,
                    '-p:EnableSourceControlManagerQueries=false', '-o', str(publish)], cwd=snapshot, check=True)
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
