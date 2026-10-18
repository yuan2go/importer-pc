"""Select and statically accept exact offline packages through the real target host."""
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile
from extension_build import decode_json


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest().upper()


def read_packages(paths, selection):
    packages = {}
    core = {c['id'] for c in selection['components'] if c['delivery'] == 'core'}
    for path in paths:
        path = path.resolve(strict=True)
        with zipfile.ZipFile(path) as z:
            info = z.getinfo('manifest.json')
            if info.file_size > 512 * 1024:
                raise ValueError('Offline manifest quota exceeded')
            raw = z.read(info)
        m = decode_json(raw.decode('utf-8'))
        key = m['Id']
        if key in packages or key in core:
            raise ValueError('Duplicate/colliding selected package: ' + key)
        packages[key] = dict(path=path, manifest=m, manifest_hash=hashlib.sha256(raw).hexdigest().upper(), package_hash=sha(path))
    # A dependency is an exact package reference, never a version range or a core DLL.
    visiting, seen = set(), set()
    def visit(key):
        if key in seen: return
        if key in visiting: raise ValueError('Cyclic offline dependency')
        visiting.add(key)
        for dependency in packages[key]['manifest']['Dependencies']:
            candidate = packages.get(dependency['Id'])
            if candidate is None or candidate['manifest']['Version'] != dependency['Version'] or candidate['manifest_hash'] != dependency['ManifestSha256']:
                raise ValueError('Selected offline dependency missing or changed: ' + dependency['Id'])
            visit(dependency['Id'])
        visiting.remove(key); seen.add(key)
    for key in packages: visit(key)
    declared = {c['id']: c for c in selection['components'] if c['delivery'] == 'offline-bundle'}
    if any(key not in packages or packages[key]['manifest']['Version'] != c['version'] for key,c in declared.items()):
        raise ValueError('Composition requires its exact declared offline bundles')
    return packages


def publish_packages(packages, selection, output, dotnet, trust, checker):
    selection['offline_bundles'] = []
    if not packages: return
    app = output / 'app'
    host = checker.resolve(strict=True) if checker else app / 'Ipc.Assembly.dll'
    # An executable from another release cannot validate different target contracts.
    contracts = {'Ipc.Platform', *(name for p in packages.values() for name in p['manifest'].get('PublicContracts', {}))}
    for name in contracts:
        if sha(app / (name + '.dll')) != sha(host.parent / (name + '.dll')):
            raise ValueError('Checker and target public contract differ: ' + name)
        desktop = output / 'desktop' / (name + '.dll')
        if desktop.exists() and sha(desktop) != sha(app / (name + '.dll')):
            raise ValueError('Desktop and console public contract differ: ' + name)
    before = {f: sha(f) for f in host.parent.iterdir() if f.is_file()}
    trust_hash = sha(trust)
    with tempfile.TemporaryDirectory(prefix='ipc-offline-selection-') as temporary:
        dependencies = Path(temporary) / 'dependencies.json'
        for key,p in packages.items():
            closure = set()
            def visit(id):
                for reference in packages[id]['manifest']['Dependencies']:
                    if reference['Id'] not in closure:
                        closure.add(reference['Id']); visit(reference['Id'])
            visit(key)
            dependencies.write_text(json.dumps([str(packages[id]['path']) for id in sorted(closure)]))
            subprocess.run([dotnet, str(host), '--action', 'extension-check', '--mode', p['manifest']['Mode'],
                            '--file', str(p['path']), '--extension-trust', str(trust.resolve()), '--dependency-files', str(dependencies)], check=True)
    if sha(trust) != trust_hash or any(not f.is_file() or sha(f) != digest for f,digest in before.items()):
        raise ValueError('Frozen checker/trust changed during acceptance')
    destination = output / 'offline-extensions'; destination.mkdir()
    declared = {c['id'] for c in selection['components']}
    for key,p in packages.items():
        if sha(p['path']) != p['package_hash']: raise ValueError('Selected package changed during acceptance')
        m = p['manifest']; filename = key + '-' + m['Version'] + '.ipcx'
        if Path(filename).name != filename or any(c not in 'abcdefghijklmnopqrstuvwxyz0123456789.-' for c in key + m['Version']):
            raise ValueError('Unsafe accepted package identity')
        target = destination / filename; shutil.copyfile(p['path'], target)
        if sha(target) != p['package_hash']: raise ValueError('Offline copy custody differs')
        selection['offline_bundles'].append(dict(Id=key, Version=m['Version'], ManifestSha256=p['manifest_hash'],
            PackageSha256=p['package_hash'], File='offline-extensions/' + filename, Mode=m['Mode'], TrustSha256=trust_hash,
            Installed=False, Activated=False, PublicContracts=m.get('PublicContracts', {})))
        if key not in declared:
            selection['components'].append(dict(id=key, version=m['Version'], contract=m['ContractMajor'],
                dependencies=[d['Id'] for d in m['Dependencies']], delivery='offline-bundle'))
    (destination / 'INSTALL.md').write_text(
        '# Selected offline packages\n\nVerify composition.lock.json and SHA256SUMS. Obtain the approved trust policy independently; it is not supplied by a package.\n'
        'Use EXTENSIONS.md: extension-inspect, extension-install with the original uppercase package hash, then edit the complete schema3 draft, validate and StageConfiguration.\n'
        'A package in this medium is not installed, bound or activated. Controlled restart confirms Effective only after resources start; original responsibility barriers still apply.\n'
        'Use extension-operations / extension-recover / extension-uninstall for maintenance; consumers, pending configurations, original receipts, backups and live leases prevent unsafe retirement.\n')
