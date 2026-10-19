import zipfile,hashlib,json,struct,sys,shutil
from pathlib import Path
sys.path.insert(0,str(Path.cwd()/'scripts'));import release_documents
root=Path.cwd();base=root/'.artifacts/audit03-565abcc';unpack=base/'unpacked';unpack.mkdir();rows=[]
def sha(p):
 with p.open('rb') as f:return hashlib.file_digest(f,'sha256').hexdigest()
for archive in sorted(base.glob('*.zip')):
 target=unpack/archive.stem;target.mkdir()
 with zipfile.ZipFile(archive) as z:
  assert z.testzip() is None
  assert all(not n.startswith('/') and '..' not in Path(n).parts for n in z.namelist())
  z.extractall(target)
 expected={}
 for line in (target/'SHA256SUMS').read_text().splitlines():
  h,name=line.split('  ',1);assert name not in expected;assert sha(target/name)==h.lower();expected[name]=h
 assert set(expected)=={str(p.relative_to(target)) for p in target.rglob('*') if p.is_file()}-{'SHA256SUMS'}
 identity=json.loads((target/'ASSEMBLY.json').read_text());assert identity['SourceCommit']=='565abcc63b885dc60873aa21e25f2d4612963c0d' and identity['BusinessVersion']=='1.2.8'
 assert next(c['version'] for c in identity['components'] if c['id']=='application-contracts')=='1.8.0'
 for project,destination in [('Ipc.Assembly','app'),*([('Ipc.Desktop','desktop')] if identity['Target']=='win-x64' else [])]:
  assert 'Ipc.Application.Contracts/1.8.0' in json.loads((target/destination/(project+'.deps.json')).read_text())['libraries']
 app=identity['ApplicationId'];guides=release_documents.SPORTMASTER_GUIDES if app=='sportmaster' else release_documents.PARCEL_GUIDES
 for source,name in guides.items():assert (target/name).read_text()==release_documents.render(root,source,identity['SourceCommit'],guides)
 modes=[]
 for p in (target/'offline-extensions').glob('*.ipcx') if (target/'offline-extensions').exists() else []:
  with zipfile.ZipFile(p) as z:
   assert z.testzip() is None;m=json.loads(z.read('manifest.json'));modes.append({'Id':m['Id'],'Version':m['Version'],'Mode':m['Mode']});assert m['Mode']=='Simulation'
 dependency=json.loads((target/'DEPENDENCIES.json').read_text());assert dependency['Packages'] and all(p['LicenseExpression'] in ['MIT','Apache-2.0'] for p in dependency['Packages'])
 pe={}
 if identity['Target']=='win-x64':
  for name in ['app/Ipc.Assembly.exe','desktop/Ipc.Desktop.exe']:
   data=(target/name).read_bytes();off=struct.unpack_from('<I',data,0x3c)[0];machine=struct.unpack_from('<H',data,off+4)[0];assert machine==0x8664;pe[name]=hex(machine)
  assert (target/'application.ps1').read_bytes()==(root/'scripts/application.ps1').read_bytes()
 rows.append({'File':archive.name,'Bytes':archive.stat().st_size,'Sha256':sha(archive),'SourceCommit':identity['SourceCommit'],'Application':app,'Target':identity['Target'],'BusinessVersion':identity['BusinessVersion'],'FilesVerified':len(expected),'GuideSourcesVerified':len(guides),'OfflinePackages':modes,'PeAppHosts':pe,'Dependencies':len(dependency['Packages']),'Windows':'NOT_RUN','PhysicalIntegration':'BLOCKED'})
 print('Verified '+archive.name,flush=True)
(base/'media-verification.json').write_text(json.dumps(rows,indent=2)+'\n')
