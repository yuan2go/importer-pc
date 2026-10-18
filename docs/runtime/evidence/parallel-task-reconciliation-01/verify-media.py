"""Check real ZIP custody and release inventories; never execute candidate code."""
import hashlib
import json
from pathlib import Path, PurePosixPath
import sys
import zipfile

def digest(data): return hashlib.sha256(data).hexdigest().upper()

def verify(file):
    errors=[]
    with zipfile.ZipFile(file) as archive:
        names=archive.namelist()
        if len(names)!=len(set(names)) or any(PurePosixPath(n).is_absolute() or '..' in PurePosixPath(n).parts for n in names):
            raise ValueError('Unsafe/duplicate archive entries')
        data={name:archive.read(name) for name in names}
    recorded={}
    for line in data['SHA256SUMS'].decode().splitlines():
        value,name=line.split('  ',1)
        if name in recorded:errors.append('Duplicate checksum '+name)
        recorded[name]=value.upper()
    if set(recorded)|{'SHA256SUMS'}!=set(data):errors.append('Checksum inventory does not cover the exact medium')
    for name,value in recorded.items():
        if name not in data or digest(data[name])!=value:errors.append('File checksum '+name)
    identity=json.loads(data['ASSEMBLY.json']);lock=json.loads(data['composition.lock.json'])
    if identity!=lock:errors.append('Assembly and composition identities differ')
    binaries={name:digest(value) for name,value in data.items() if PurePosixPath(name).name.startswith('Ipc.') and name.endswith('.dll')}
    claimed={name:value.upper() for name,value in identity['Binaries'].items()}
    if binaries!=claimed:errors.append('Binary inventory differs: missing='+str(sorted(set(claimed)-set(binaries)))+' unlisted='+str(sorted(set(binaries)-set(claimed))))
    for prefix in ['app/','desktop/']:
        release_name=prefix+'application-release.json'
        if release_name not in data:continue
        release=json.loads(data[release_name]);files={name[len(prefix):]:digest(value) for name,value in data.items() if name.startswith(prefix) and name!=release_name}
        if files!=release['Files']:errors.append(prefix+'release Files differ from actual medium')
        if release['SourceCommit']!=identity['SourceCommit'] or release['BusinessVersion']!=identity['BusinessVersion']:errors.append(prefix+'business/source identity differs')
        if release['CompatibilitySha256']!=digest(data[prefix+'application-compatibility.json']):errors.append(prefix+'compatibility bytes differ')
    for name in data:
        parts=PurePosixPath(name).parts
        if any(p in parts for p in ['build-intermediates','source-snapshot','obj','bin','private']) or name.endswith(('.pem','.key','.pfx','.db','.ttf','.otf')):
            errors.append('Unshipped/private/generated resource '+name)
    bundles=identity.get('offline_bundles',[])
    if {x['File'] for x in bundles}!={n for n in data if n.endswith('.ipcx')}:errors.append('Offline selected inventory differs')
    for item in bundles:
        if digest(data[item['File']])!=item['PackageSha256']:errors.append('Offline package changed '+item['Id'])
    return dict(File=Path(file).name,Bytes=Path(file).stat().st_size,Sha256=digest(Path(file).read_bytes()),SourceCommit=identity['SourceCommit'],BusinessVersion=identity['BusinessVersion'],Target=identity['Target'],Files=len(data),Binaries=len(binaries),OfflinePackages=len(bundles),Status='FAIL' if errors else 'PASS',Errors=errors)

if __name__=='__main__':
    rows=[verify(Path(name)) for name in sys.argv[1:]]
    print(json.dumps(rows,indent=2))
    raise SystemExit(1 if any(r['Errors'] for r in rows) else 0)
