#!/usr/bin/env python3
"""Build and verify one immutable, same-source Windows reference release set."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import platform
import subprocess
import sys
import zipfile
from release_documents import SOURCE_PATHS

ROOT = Path(__file__).resolve().parents[1]
MERGES = {'44': 'fea535c94c12a50c9559928a2de0bf680e09dba7',
          '45': 'dbf53853eadd65c284fcd648d36096e4c0a69b38',
          '46': 'b8f449ec91f8f7efd8bc5079ca38e19bc877ebf5'}
PARCEL = ('sportmaster', 'direct-plc', 'wcs-to-plc')
MATRIX = [(a, t) for a in PARCEL for t in ('portable', 'win-x64')] + [('supply', 'portable'), ('inspection', 'portable')]


def sha(path):
    with Path(path).open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest().upper()


def read(path):
    return json.loads(Path(path).read_bytes())


def write(path, value):
    Path(path).write_text(json.dumps(value, ensure_ascii=False, indent=2) + '\n', encoding='utf-8', newline='\n')


def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True, encoding='utf-8').strip()


def safe_name(name):
    p = PurePosixPath(name)
    if not name or p.is_absolute() or any(x in ('', '.', '..') for x in name.split('/')) or '\\' in name or ':' in name:
        raise ValueError('Unsafe package path: ' + name)
    return name


def verify_package(directory, application, target, source):
    directory = Path(directory)
    inventory = {}
    for line in (directory / 'SHA256SUMS').read_text(encoding='utf-8').splitlines():
        digest, name = line.split('  ', 1)
        safe_name(name)
        if len(digest) != 64 or any(c not in '0123456789abcdefABCDEF' for c in digest) or name.casefold() in {n.casefold() for n in inventory}:
            raise ValueError('Duplicate/invalid inventory')
        inventory[name] = digest.upper()
    actual = {p.relative_to(directory).as_posix() for p in directory.rglob('*') if p.is_file()}
    if actual != set(inventory) | {'SHA256SUMS'}:
        raise ValueError('Incomplete package inventory')
    for name, digest in inventory.items():
        if sha(directory / name) != digest:
            raise ValueError('File differs: ' + name)
    for p in directory.rglob('*'):
        if p.is_symlink() or p.is_junction():
            raise ValueError('Linked media refused')
    archive = Path(str(directory) + '.zip')
    with zipfile.ZipFile(archive) as z:
        names = z.namelist()
        if len({n.casefold() for n in names}) != len(names) or set(names) != actual:
            raise ValueError('ZIP members differ/duplicate')
        for n in names:
            safe_name(n)
            if hashlib.sha256(z.read(n)).hexdigest().upper() != sha(directory / n):
                raise ValueError('ZIP content differs: ' + n)
        if z.testzip() is not None:
            raise ValueError('ZIP CRC failed')
    identity = read(directory / 'ASSEMBLY.json')
    if identity.get('SourceCommit') != source or identity.get('ApplicationId', identity.get('Business')) != application:
        raise ValueError('Wrong package source/application')
    dependencies = read(directory / 'DEPENDENCIES.json')
    if dependencies['SourceCommit'] != source:
        raise ValueError('Wrong dependency provenance')
    result = dict(Application=application, Target=target, File=archive.name, Sha256=sha(archive), Bytes=archive.stat().st_size,
                  Files=len(actual), SourceCommit=source, OfflineBundles=identity.get('offline_bundles', []))
    if application in PARCEL:
        lock = read(directory / 'composition.lock.json')
        if lock != identity or identity['Target'] != target:
            raise ValueError('Composition identity differs')
        result.update({k: identity[k] for k in ('BusinessVersion', 'ApplicationContractVersion', 'DatabaseFormat', 'CompatibilitySha256') if k in identity})
        components = {c['id']: c['version'] for c in identity['components']}
        if components['application.' + application] != identity['BusinessVersion']:
            raise ValueError('Business version and composition differ')
        result['ApplicationContractVersion'] = components['application-contracts']
        for area in ('app', 'desktop') if target == 'win-x64' else ('app',):
            base = directory / area
            release = read(base / 'application-release.json')
            if release['SourceCommit'] != source or release['ApplicationId'] != application:
                raise ValueError('Wrong executable release identity')
            if {p.relative_to(base).as_posix() for p in base.rglob('*') if p.is_file()} != set(release['Files']) | {'application-release.json'}:
                raise ValueError('Incomplete executable release')
            for n, digest in release['Files'].items():
                safe_name(n)
                if sha(base / n) != digest.upper():
                    raise ValueError('Executable release hash differs')
            if sha(base / 'application-compatibility.json') != release['CompatibilitySha256']:
                raise ValueError('Compatibility identity differs')
            deps = read(base / ('Ipc.Assembly.deps.json' if area == 'app' else 'Ipc.Desktop.deps.json'))
            if 'Ipc.Application.Contracts/' + components['application-contracts'] not in deps['libraries'] or 'Ipc.Application.' + {'sportmaster': 'Sportmaster', 'direct-plc': 'DirectPlc', 'wcs-to-plc': 'WcsToPlc'}[application] + '/' + identity['BusinessVersion'] not in deps['libraries']:
                raise ValueError('Compiled deps business/contract differs')
        if target == 'win-x64':
            for name in ('Setup.cmd', 'Maintenance.cmd', 'application.cmd', 'app/Ipc.Assembly.exe', 'desktop/Ipc.Desktop.exe'):
                if name not in inventory:
                    raise ValueError('Missing native entry: ' + name)
            peers = []
            for dll in (directory / 'app').glob('Ipc.*.dll'):
                peer = directory / 'desktop' / dll.name
                if peer.exists():
                    if sha(dll) != sha(peer):
                        raise ValueError('Helper/desktop bytes differ: ' + dll.name)
                    peers.append(dll.name)
            result['IdenticalSharedDlls'] = peers
    else:
        if target != 'portable':
            raise ValueError('Legacy assembly does not have this Windows installer')
        deps = read(directory / 'app/Ipc.Assembly.deps.json')['libraries']
        assembly = 'Ipc.Supply' if application == 'supply' else 'Ipc.Inspection'
        versions = [n.rsplit('/', 1)[1] for n in deps if n.startswith(assembly + '/')]
        expected = next(c['version'] for c in read(directory / 'composition.lock.json')['components'] if c['id'] == application)
        if versions != [expected]:
            raise ValueError('Legacy compiled business and composition version differ')
        result['BusinessVersion'] = versions[0]
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--dotnet', default='dotnet')
    p.add_argument('--verify-only', action='store_true')
    a = p.parse_args()
    output = a.output.resolve()
    if a.verify_only:
        for line in (output / 'SHA256SUMS').read_text(encoding='utf-8').splitlines():
            digest, name = line.split('  ', 1)
            safe_name(name)
            if sha(output / name) != digest.upper():
                raise ValueError('Release receipt/log/archive changed: ' + name)
        receipt = read(output / 'RELEASE.json')
        actual = [verify_package(output / Path(safe_name(r['File'])).stem, r['Application'], r['Target'], receipt['SourceCommit']) for r in receipt['Packages']]
        if actual != receipt['Packages'] or {(r['Application'], r['Target']) for r in actual} != set(MATRIX):
            raise ValueError('Release set differs')
        print(json.dumps(dict(Status='PASS', SourceCommit=receipt['SourceCommit'], Packages=len(actual))))
        return
    if output.exists():
        raise FileExistsError('Choose a new immutable release directory; failures are retained')
    if git('status', '--porcelain', '--', *SOURCE_PATHS):
        raise ValueError('Commit exact runtime/configuration/packaging/guides before building')
    source = git('rev-parse', 'HEAD')
    for commit in MERGES.values():
        subprocess.run(['git', 'merge-base', '--is-ancestor', commit, source], cwd=ROOT, check=True)
    expected = read(ROOT / 'global.json')['sdk']['version']
    sdk = subprocess.check_output([a.dotnet, '--version'], cwd=ROOT, text=True).strip()
    if sdk != expected:
        raise ValueError('Pinned SDK required: ' + expected)
    output.mkdir(parents=True)
    record = dict(SchemaVersion=1, State='BUILDING', SourceCommit=source, SourceTree=git('rev-parse', 'HEAD^{tree}'),
                  IncludedMergeCommits=MERGES, Sdk=sdk, Python=platform.python_version(), OS=platform.platform(),
                  StartedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat(),
                  WindowsAcceptance='NOT_RUN', PhysicalIntegration='BLOCKED', Assurance='SIMULATION_REFERENCE_ONLY',
                  Authenticode='NOT_SIGNED_NO_CERTIFICATE_PROVIDED',
                  Inputs={str(f.relative_to(ROOT)).replace('\\', '/'): sha(f) for f in sorted(ROOT.glob('src/*/packages*.lock.json'))}, Commands=[], Packages=[])
    record['Toolchain'] = subprocess.check_output([a.dotnet, '--info'], cwd=ROOT, text=True, encoding='utf-8')
    write(output / 'build-record.json', record)
    env = dict(os.environ, PYTHONUTF8='1', DOTNET_CLI_TELEMETRY_OPTOUT='1')
    try:
        for application, target in MATRIX:
            name = application + '-' + target + '-' + source[:12]
            command = [sys.executable, str(ROOT / 'scripts/package-application.py'), '--application', application] if application in PARCEL else [sys.executable, str(ROOT / 'scripts/package-business.py'), '--composition', 'supply-assembly' if application == 'supply' else 'inspection-reference']
            command += ['--target', target, '--output', str(output / name), '--dotnet', a.dotnet]
            record['Commands'].append(command)
            write(output / 'build-record.json', record)
            with (output / (name + '.log')).open('xb') as log:
                subprocess.run(command, cwd=ROOT, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
            record['Packages'].append(verify_package(output / name, application, target, source))
            write(output / 'build-record.json', record)
            print('VERIFIED ' + name, flush=True)
        if git('rev-parse', 'HEAD') != source or git('status', '--porcelain', '--', *SOURCE_PATHS):
            raise ValueError('Source changed during release')
        record.update(State='PASS', FinishedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
        write(output / 'build-record.json', record)
        write(output / 'RELEASE.json', record)
        entries = sorted([output / 'RELEASE.json', output / 'build-record.json', *output.glob('*.zip'), *output.glob('*.log')])
        (output / 'SHA256SUMS').write_text(''.join(sha(f) + '  ' + f.name + '\n' for f in entries), encoding='utf-8', newline='\n')
    except BaseException as error:
        record.update(State='FAIL', Failure=str(error), FinishedUtc=datetime.datetime.now(datetime.timezone.utc).isoformat())
        write(output / 'build-record.json', record)
        raise


if __name__ == '__main__':
    main()
