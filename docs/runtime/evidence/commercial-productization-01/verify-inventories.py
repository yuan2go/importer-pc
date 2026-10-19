"""Verify the six immutable local candidates without installing or starting a business."""
from pathlib import Path
import argparse,hashlib,json,re,subprocess,zipfile
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--releases',type=Path,required=True);p.add_argument('--source',required=True)
p.add_argument('--output',type=Path,required=True);p.add_argument('--dotnet',default='dotnet')
a=p.parse_args();assert re.fullmatch('[0-9a-f]{40}',a.source)
root=a.releases.resolve();a.output.mkdir(parents=True,exist_ok=False);records=[]
for app in ['sportmaster','direct-plc','wcs-to-plc']:
 for target in ['win-x64','portable']:
  medium=root/f'{app}-{a.source[:7]}-{target}'
  lock=json.loads((medium/'ASSEMBLY.json').read_bytes())
  assert lock['SourceCommit']==a.source and lock['ApplicationId']==app
  entries={}
  for line in (medium/'SHA256SUMS').read_text().splitlines():
   sha,name=line.split('  ',1);path=(medium/name).resolve()
   assert path.is_relative_to(medium) and name not in entries
   assert hashlib.sha256(path.read_bytes()).hexdigest()==sha
   entries[name]=sha
  assert {f.relative_to(medium).as_posix() for f in medium.rglob('*') if f.is_file()}==set(entries)|{'SHA256SUMS'}
  checks={}
  if target=='win-x64':
   for dll in (medium/'app').glob('Ipc.*.dll'):
    if (medium/'desktop'/dll.name).exists():assert entries['app/'+dll.name]==entries['desktop/'+dll.name],dll.name
   checks['MatchingPrivilegedHelperAndDesktop']=True
  archive=Path(str(medium)+'.zip')
  with zipfile.ZipFile(archive) as zipped:
   assert zipped.testzip() is None
   assert set(zipped.namelist())==set(entries)|{'SHA256SUMS'}
   for name,sha in entries.items():assert hashlib.sha256(zipped.read(name)).hexdigest()==sha
  run=subprocess.run([a.dotnet,str(medium/'app/Ipc.Assembly.dll'),'--action','business-identity'],capture_output=True,timeout=30)
  assert run.returncode==0,run.stderr.decode(errors='replace')
  identity=json.loads(run.stdout);assert identity['ApplicationId']==app and identity['BusinessVersion']=='1.4.0'
  (a.output/f'{app}-{target}-identity.json').write_bytes(run.stdout)
  records.append(dict(Application=app,Target=target,SourceCommit=a.source,Zip=archive.name,ZipSha256=hashlib.sha256(archive.read_bytes()).hexdigest(),ZipBytes=archive.stat().st_size,MediumInventorySha256=hashlib.sha256((medium/'SHA256SUMS').read_bytes()).hexdigest(),RegisteredFiles=len(entries),Status='PASS_FILE_INTEGRITY_CRC_BUSINESS_IDENTITY',Checks=checks,FullInstallation='NOT_RUN',Equipment='BLOCKED'))
(a.output/'media-identities.json').write_text(json.dumps(records,indent=2)+'\n',encoding='utf-8')
print('PASS six frozen media inventories, complete ZIP entries/CRC/content hashes, business identity and all common Ipc helper/desktop bytes')
