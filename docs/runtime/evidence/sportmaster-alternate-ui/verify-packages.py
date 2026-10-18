"""Verify immutable archives and run the actual unpacked portable reference host. Windows UI is never claimed."""
from pathlib import Path
import hashlib,json,zipfile,subprocess,select,time,uuid,os,argparse,tempfile
parser=argparse.ArgumentParser()
parser.add_argument('--portable',type=Path,required=True)
parser.add_argument('--windows',type=Path,required=True)
parser.add_argument('--source',required=True)
parser.add_argument('--dotnet',required=True)
parser.add_argument('--report',type=Path,required=True)
args=parser.parse_args()
work=Path(tempfile.mkdtemp(prefix='ipc-sportmaster-package-check-')).resolve()
source=args.source
report={'SourceCommit':source,'Checks':[],'Packages':[],'WindowsUi':'NOT_RUN','PhysicalIntegration':'BLOCKED'}
def check(yes,reason):
 if not yes:raise AssertionError(reason)
 report['Checks'].append(reason)
for target in ['portable','win-x64']:
 archive=args.portable if target=='portable' else args.windows
 dest=work/target
 with zipfile.ZipFile(archive) as z:
  check(all(not n.startswith('/') and '..' not in Path(n).parts for n in z.namelist()),target+' archive paths safe')
  dest.mkdir();z.extractall(dest)
 for line in (dest/'SHA256SUMS').read_text().splitlines():
  sha,name=line.split('  ',1);assert hashlib.sha256((dest/name).read_bytes()).hexdigest()==sha,name
 check(True,target+' all shipped SHA256SUMS verified after extraction')
 metadata=json.loads((dest/'ASSEMBLY.json').read_text());check(metadata['SourceCommit']==source,target+' frozen source identity')
 for base in [dest/'app',dest/'desktop']:
  if not base.exists():continue
  registration=json.loads((base/'application-release.json').read_text())
  check(registration['SourceCommit']==source and registration['ApplicationId']=='sportmaster',target+'/'+base.name+' original business registration')
  for name,sha in registration['Files'].items():assert hashlib.sha256((base/name).read_bytes()).hexdigest().upper()==sha,name
  check(True,target+'/'+base.name+' all release file identities verified')
 check((dest/'SPORTMASTER-UI.md').exists() and (dest/'SPORTMASTER-UI-WINDOWS.md').exists(),target+' shipped UI and Windows guides')
 if target=='win-x64':
  check('--ui supply-station' in (dest/'APPLICATION-START.txt').read_text(), 'win-x64 startup instructions include selectable UI')
  data=(dest/'desktop/Ipc.Application.Sportmaster.Desktop.dll').read_bytes()
  for name in ['workstation.baml','supplystationwindow.baml','supplystationposition.baml','supplystationtheme.baml']:
   check(name.encode('utf-16le') in data or name.encode() in data,'win-x64 embedded resource '+name)
  check(not list((dest/'desktop').glob('*.html')),'original HTML absent from desktop package')
 else:check(not (dest/'desktop').exists(),'portable package has no WPF binary claim')
 report['Packages'].append({'Target':target,'Zip':archive.name,'Sha256':hashlib.sha256(archive.read_bytes()).hexdigest(),'Size':archive.stat().st_size})
base=work/'portable/app'
data=work/'runtime'
p=subprocess.Popen([args.dotnet,str(base/'Ipc.Assembly.dll'),'--config',str(base/'config'),'--data-root',str(data)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,bufsize=0)
pending=b''
def read():
 global pending
 end=time.monotonic()+15
 while True:
  try:
   text=pending.decode();trimmed=text.lstrip();value,length=json.JSONDecoder().raw_decode(trimmed)
   pending=trimmed[length:].encode();return value
  except (ValueError,UnicodeDecodeError):pass
  remaining=end-time.monotonic()
  if remaining<=0 or not select.select([p.stdout],[],[],remaining)[0]:raise RuntimeError('packaged process response timeout')
  chunk=os.read(p.stdout.fileno(),65536)
  if not chunk:raise RuntimeError('packaged process exited: '+p.stderr.read().decode())
  pending+=chunk
def send(command):p.stdin.write(((command if isinstance(command,str) else json.dumps(command))+'\n').encode());p.stdin.flush();return read()
try:
 started=read();check(started['Status']=='Started' and started['Business']=='sportmaster','actual unpacked portable host startup')
 initial=send('snapshot');runtime=initial['RuntimeId']
 for pos in ['left','right']:
  receipt=send({'Id':uuid.uuid4().hex,'ExpectedRevision':initial['Revision'],'Kind':'EmitReferenceAt','Owner':'packaged-'+pos,'Value':json.dumps({'PositionId':pos,'Code':'000000123'})})
  check(receipt['Status']=='Enqueued','packaged '+pos+' observation admitted')
 end=time.monotonic()+10
 while True:
  snapshot=send('snapshot')
  if len(snapshot['Operations'])==2 and all(o['State']=='SlotResolved' for o in snapshot['Operations']):break
  if time.monotonic()>end:raise RuntimeError('packaged stages timeout')
  time.sleep(.03)
 ops=snapshot['Operations'];check(snapshot['RuntimeId']==runtime and {o['PositionId'] for o in ops}=={'left','right'} and len({o['AdmissionId'] for o in ops})==2,'packaged same-owner dual-position identity preserved')
 check(all(o['ParcelCode']=='000000123' and o['Wcs']['Attempts']==1 and o['Plc'] is None for o in ops),'packaged WCS-only chain preserves leading zeros and one original attempt')
 p.stdin.write(b'quit\n');p.stdin.flush();p.wait(timeout=15)
 check(p.returncode==0,'packaged host drains and exits successfully')
finally:
 if p.poll() is None:p.kill();p.wait()
report['Status']='PASS';args.report.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps(report,ensure_ascii=False,indent=2))
