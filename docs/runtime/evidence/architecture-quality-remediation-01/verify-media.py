from pathlib import Path,PurePosixPath
import hashlib,json,zipfile
base=Path('D:/audits/importer-pc/AQA01-remediation-20261023')
source='7024f1e0128a4d8cf6af5ec6b51d6100a22c5d62'
items=[]
for archive in sorted((base/'packages').glob('*.zip')):
 target=base/'unpacked'/archive.stem
 if not target.exists():
  with zipfile.ZipFile(archive) as z:
   for n in z.namelist():
    p=PurePosixPath(n)
    assert not p.is_absolute() and '..' not in p.parts and ':' not in n,n
   z.extractall(target)
 identity=json.loads((target/'ASSEMBLY.json').read_bytes());assert identity['SourceCommit']==source
 count=0
 for row in (target/'SHA256SUMS').read_text().splitlines():
  sha,relative=row.split('  ',1);assert hashlib.sha256((target/relative).read_bytes()).hexdigest()==sha;count+=1
 assert (target/'ARCHITECTURE-QUALITY-REMEDIATION-01.md').exists()
 assert 'Parcel使用数据库8' in (target/'GETTING-STARTED.md').read_text(encoding='utf-8')
 module={'sportmaster':'Sportmaster','direct-plc':'DirectPlc','wcs-to-plc':'WcsToPlc'}[identity['product']['business']]
 assert (target/'app'/f'Ipc.Application.{module}.dll').exists()
 if module=='DirectPlc':assert not (target/'app/Ipc.Integrations.dll').exists()
 items.append(dict(Package=archive.name,Sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),Bytes=archive.stat().st_size,SourceCommit=source,Target=identity['Target'],CheckedFiles=count,Business=module,Version=identity['BusinessVersion'],GuideChecks='DB8/current ABI/quality guide present',Path=str(target)))
(base/'package-inventory.json').write_text(json.dumps(items,ensure_ascii=False,indent=2)+'\n')
print(json.dumps(items,ensure_ascii=False))
