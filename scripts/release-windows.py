#!/usr/bin/env python3
"""Build one frozen Simulation release set. A failed set is never publishable."""
import argparse
import datetime
import contextlib
import hashlib
import json
import os
from pathlib import Path
import platform
import re
import subprocess
import sys
import uuid
import zipfile


def digest(path):
    with path.open('rb') as f:
        h = hashlib.file_digest(f, 'sha256')
    return h.hexdigest()


def verify(directory, source, application, target):
    archive = directory.with_suffix('.zip')
    files = {p.relative_to(directory).as_posix(): p for p in directory.rglob('*') if p.is_file()}
    if any(p.is_symlink() for p in directory.rglob('*')):
        raise ValueError('Linked media entry')
    inventory = {}
    for line in files['SHA256SUMS'].read_text().splitlines():
        if not re.fullmatch(r'[0-9a-fA-F]{64}  [^\r\n]+', line):
            raise ValueError('Malformed inventory')
        checksum, name = line.split('  ', 1)
        if name in inventory or name not in files:
            raise ValueError('Duplicate/missing inventory entry: ' + name)
        inventory[name] = checksum.lower()
    if set(inventory) != set(files) - {'SHA256SUMS'}:
        raise ValueError('Inventory does not cover exact media')
    if any(digest(files[n]) != h for n, h in inventory.items()):
        raise ValueError('Inventory hash mismatch')
    with zipfile.ZipFile(archive) as z:
        if len(z.namelist()) != len(set(z.namelist())) or set(z.namelist()) != set(files):
            raise ValueError('ZIP member set differs')
        for name in files:
            if hashlib.sha256(z.read(name)).hexdigest() != digest(files[name]):
                raise ValueError('ZIP differs from checked directory: ' + name)
        if z.testzip() is not None:
            raise ValueError('ZIP CRC failure')
    assembly = json.loads(files['ASSEMBLY.json'].read_bytes())
    if assembly['SourceCommit'] != source or assembly.get('ApplicationId', assembly.get('Business')) != application:
        raise ValueError('Wrong source/Application')
    if application in ('supply', 'inspection'):
        return dict(Application=application, Target=target, SourceCommit=source, Sha256=digest(archive), Bytes=archive.stat().st_size)
    if assembly['Target'] != target:
        raise ValueError('Wrong target')
    versions = {c['id']: c['version'] for c in assembly['components']}
    if versions['application.' + application] != assembly['BusinessVersion']:
        raise ValueError('Business version differs')
    for area in (['app', 'desktop'] if target == 'win-x64' else ['app']):
        release = json.loads(files[area + '/application-release.json'].read_bytes())
        if any(release[k] != assembly[k] for k in ('SourceCommit', 'ApplicationId', 'BusinessVersion', 'BusinessAssembly', 'CompatibilitySha256')):
            raise ValueError('Host identity differs')
        expected = {n[len(area)+1:] for n in files if n.startswith(area+'/')} - {'application-release.json'}
        if set(release['Files']) != expected:
            raise ValueError('Host inventory differs')
        for name, checksum in release['Files'].items():
            if digest(files[area+'/'+name]) != checksum.lower():
                raise ValueError('Host file differs')
        deps = json.loads(files[area + ('/Ipc.Assembly.deps.json' if area == 'app' else '/Ipc.Desktop.deps.json')].read_bytes())
        if 'Ipc.Application.Contracts/' + versions['application-contracts'] not in deps['libraries']:
            raise ValueError('Compiled ABI differs')
    if target == 'win-x64':
        for name in ('Setup.cmd', 'Maintenance.cmd', 'application.cmd', 'app/Ipc.Assembly.exe', 'desktop/Ipc.Desktop.exe'):
            if name not in files:
                raise ValueError('Missing installation/maintenance entry: '+name)
        if '--setup' not in files['Setup.cmd'].read_text() or '--maintenance' not in files['Maintenance.cmd'].read_text():
            raise ValueError('Wrong native entry dispatch')
        for name in files:
            peer = 'desktop/' + name[4:]
            if name.startswith('app/Ipc.') and name.endswith('.dll') and peer in files and digest(files[name]) != digest(files[peer]):
                raise ValueError('Shared DLL differs: '+name)
    for bundle in assembly.get('offline_bundles', []):
        if not bundle:
            raise ValueError('Empty extension identity')
    return dict(Application=application, Target=target, SourceCommit=source, BusinessVersion=assembly['BusinessVersion'],
                ContractVersion=versions['application-contracts'], OfflineBundles=assembly.get('offline_bundles', []),
                Sha256=digest(archive), Bytes=archive.stat().st_size)


@contextlib.contextmanager
def exclusive(output):
    # Kernel locks release on process death. The marker is never a stale-lock bypass.
    with (output/'.release-lock').open('a+b') as handle:
        handle.seek(0, 2)
        if handle.tell() == 0: handle.write(b'0'); handle.flush()
        handle.seek(0)
        if os.name == 'nt':
            import msvcrt
            msvcrt.locking(handle.fileno(), msvcrt.LK_NBLCK, 1)
        else:
            import fcntl
            fcntl.flock(handle, fcntl.LOCK_EX | fcntl.LOCK_NB)
        try: yield
        finally:
            handle.seek(0)
            if os.name == 'nt': msvcrt.locking(handle.fileno(), msvcrt.LK_UNLCK, 1)
            else: fcntl.flock(handle, fcntl.LOCK_UN)


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--source', required=True)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--dotnet', default='dotnet')
    p.add_argument('--resume', action='store_true', help='Reverify completed packages and quarantine incomplete outputs; retain original attempts')
    a = p.parse_args()
    root = Path(__file__).resolve().parents[1]
    source = subprocess.check_output(['git', 'rev-parse', a.source+'^{commit}'], cwd=root, text=True).strip()
    if a.source != source:
        raise ValueError('Use a full frozen commit SHA')
    if subprocess.check_output(['git', 'status', '--porcelain', '--untracked-files=normal'], cwd=root, text=True).strip():
        raise ValueError('Commit all changes before release')
    output = a.output.resolve()
    if not a.resume: output.mkdir(parents=True, exist_ok=False)
    if not output.is_dir(): raise ValueError('Resume output missing')
    with exclusive(output):
        if a.resume:
            record = json.loads((output/'BUILD.json').read_bytes())
            if record['SourceCommit'] != source or (output/'RELEASE.json').exists():
                raise ValueError('Resume requires the same source and an incomplete set')
            attempt = uuid.uuid4().hex
            (output/('attempt-'+attempt+'-BUILD.json')).write_bytes((output/'BUILD.json').read_bytes())
            completed = {x['File']: x for x in record['Packages']}
            record.setdefault('Resumes', []).append(dict(AtUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(), PriorStatus=record['Status'], Attempt=attempt))
            record['Status'] = 'BUILDING'
        else:
            completed = {}; attempt = uuid.uuid4().hex
            record = dict(SourceCommit=source, StartedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                          Platform=platform.platform(), Python=sys.version, Status='BUILDING', Packages=[],
                          WindowsAcceptance='NOT_RUN', PhysicalIntegration='BLOCKED', CommercialAcceptance='NOT_APPROVED')
        record['OrchestratorScriptSha256'] = digest(Path(__file__))
        def receipt():
            (output/'BUILD.json').write_text(json.dumps(record, indent=2)+'\n')
        receipt()
        try:
            snapshot = output/'source'
            if not a.resume: snapshot.mkdir()
            subprocess.run(['git', '-C', str(snapshot), 'init', '-q'], check=True)
            subprocess.run(['git', '-C', str(snapshot), 'config', 'core.longpaths', 'true'], check=True)
            subprocess.run(['git', '-C', str(snapshot), 'fetch', '-q', str(root), source], check=True)
            if a.resume and (subprocess.check_output(['git','-C',str(snapshot),'rev-parse','HEAD'],text=True).strip() != source or subprocess.check_output(['git','-C',str(snapshot),'status','--porcelain'],text=True).strip()):
                raise ValueError('Frozen snapshot changed; refuse resume')
            if not a.resume: subprocess.run(['git', '-C', str(snapshot), 'reset', '--hard', '-q', source], check=True)
            sdk = subprocess.check_output([a.dotnet, '--version'], cwd=snapshot, text=True).strip()
            if sdk != json.loads((snapshot/'global.json').read_bytes())['sdk']['version']:
                raise ValueError('SDK differs from frozen global.json')
            if a.resume and record.get('Sdk') != sdk: raise ValueError('Resume SDK differs')
            record['Sdk'] = sdk
            record['SdkInfo'] = subprocess.check_output([a.dotnet, '--info'], cwd=snapshot, text=True)
            for application, target in [(app,t) for app in ('sportmaster','direct-plc','wcs-to-plc') for t in ('portable','win-x64')] + [('supply','portable'),('inspection','portable')]:
                name = application+'-'+target
                directory = output/name
                if name+'.zip' in completed:
                    checked = verify(directory, source, application, target)
                    if checked['Sha256'] != completed[name+'.zip']['Sha256']: raise ValueError('Completed package changed')
                    continue
                for path in (directory, directory.with_suffix('.zip'), output/(name+'.log')):
                    if path.exists(): path.rename(output/('interrupted-'+attempt+'-'+path.name))
                command = [sys.executable, 'scripts/package-application.py', '--application', application] if application not in ('supply','inspection') else [sys.executable,'scripts/package-business.py','--composition', 'supply-assembly' if application == 'supply' else 'inspection-reference']
                command += ['--target',target,'--output',str(directory),'--dotnet',a.dotnet]
                record['CurrentCommand'] = command; receipt()
                with (output/(name+'.log')).open('w') as log:
                    subprocess.run(command, cwd=snapshot, stdout=log, stderr=subprocess.STDOUT, check=True)
                package = verify(directory, source, application, target)
                package['File'] = name+'.zip'
                record['Packages'].append(package); receipt()
            record['Status'] = 'VERIFIED_CROSS_PLATFORM' if os.name != 'nt' else 'VERIFIED_BUILD_ONLY'
            record['CompletedUtc'] = datetime.datetime.now(datetime.timezone.utc).isoformat()
            receipt()
            (output/'RELEASE.json').write_text(json.dumps(record, indent=2)+'\n')
            (output/'SHA256SUMS').write_text(''.join(digest(output/n)+'  '+n+'\n' for n in ['RELEASE.json',*[x['File'] for x in record['Packages']]]))
        except BaseException as error:
            record.update(Status='FAILED', Error=str(error)); receipt()
            raise


if __name__ == '__main__':
    main()
