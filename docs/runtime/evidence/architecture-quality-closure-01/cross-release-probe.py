"""Explicit Simulation-only old/new immutable assembly compatibility probe."""
import argparse, hashlib, json, queue, subprocess, tempfile, threading, time, uuid
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--old-package',type=Path,required=True);p.add_argument('--new-package',type=Path,required=True);p.add_argument('--dotnet',default='dotnet');p.add_argument('--output',type=Path,required=True);a=p.parse_args()
old=a.old_package.resolve();new=a.new_package.resolve();data=Path(tempfile.mkdtemp(prefix='ipc-aq-cross-release-',dir='/private/tmp' if Path('/private/tmp').is_dir() else None))
identities=[json.loads((root/'ASSEMBLY.json').read_text()) for root in [old,new]]
assert all(x['Target']=='portable' and x['product']['business']=='wcs-to-plc' for x in identities)
for root in [old,new]:
 for line in (root/'SHA256SUMS').read_text().splitlines():
  sha,rel=line.split('  ',1);assert hashlib.sha256((root/rel).read_bytes()).hexdigest()==sha
assert json.loads((old/'app/config/business.json').read_text())['Mode']=='Simulation'
active=[]
def start(root):
 proc=subprocess.Popen([a.dotnet,str(root/'app/Ipc.Assembly.dll'),'--config',str(root/'app/config'),'--data-root',str(data)],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);active.append(proc);messages=queue.Queue()
 def consume():
  buffer='';decoder=json.JSONDecoder()
  for line in proc.stdout:
   buffer+=line
   try:value,end=decoder.raw_decode(buffer.lstrip())
   except json.JSONDecodeError:continue
   messages.put(value);buffer=buffer.lstrip()[end:].lstrip()
 threading.Thread(target=consume,daemon=True).start();initial=messages.get(timeout=15);assert initial.get('Status')=='Started',initial
 return proc,messages

def send(proc,messages,value):
 proc.stdin.write(value if isinstance(value,str) else json.dumps(value));proc.stdin.write('\n');proc.stdin.flush();return messages.get(timeout=15)

def command(proc,messages,kind,owner=None,value=None):
 s=send(proc,messages,'snapshot');return send(proc,messages,dict(Id=uuid.uuid4().hex,ExpectedRevision=s['Revision'],Kind=kind,Owner=owner,Value=value))

def snapshot_until(proc,messages,predicate):
 for _ in range(150):
  s=send(proc,messages,'snapshot')
  if predicate(s):return s
  time.sleep(.03)
 raise AssertionError('reference phase timeout')

def stop(proc):
 proc.stdin.write('quit\n');proc.stdin.flush();proc.wait(timeout=15);assert proc.returncode==0,proc.stderr.read()

try:
 proc,messages=start(old)
 assert command(proc,messages,'EmitReferenceAt','cross-release-original',json.dumps(dict(PositionId='main',Code='000091')))['Status']=='Enqueued'
 before=snapshot_until(proc,messages,lambda s: bool(s['Operations']) and s['Operations'][0]['State']=='PlcAccepted')['Operations'][0];stop(proc)
 rejected=subprocess.run([a.dotnet,str(new/'app/Ipc.Assembly.dll'),'--config',str(new/'app/config'),'--data-root',str(data)],input='snapshot\nquit\n',text=True,capture_output=True,timeout=20)
 assert rejected.returncode!=0 and 'OriginalCapabilityUnavailable' in rejected.stderr,(rejected.returncode,rejected.stdout,rejected.stderr)
 (data/'new-host-rejection.log').write_text(rejected.stderr)
 proc,messages=start(old);recovered=send(proc,messages,'snapshot')['Operations'][0]
 assert recovered['Plc']['Request']==before['Plc']['Request'] and recovered['Plc']['Attempts']==1
 assert command(proc,messages,'Reconcile',before['Id'])['Status']=='PhysicalCompleted'
 settled=send(proc,messages,'snapshot')['Operations'][0];assert settled['Plc']['Attempts']==1 and settled['Plc']['Request']==before['Plc']['Request'];stop(proc)
 proc,messages=start(new);after=send(proc,messages,'snapshot');final=next(o for o in after['Operations'] if o['Id']==before['Id'])
 assert final['State']=='PhysicalCompleted' and final['Plc']['Request']==before['Plc']['Request'] and after['Paused'];stop(proc)
 request=before['Plc']['Request'];peer=Path(request['Binding']['Parameters']['peer-root'])/hashlib.sha256(request['Binding']['Target'].encode()).hexdigest().upper()/(request['Id']+'.json');ledger=json.loads(peer.read_text());assert ledger['Executions']==1
 result={'Status':'PASS','OldSourceCommit':identities[0]['SourceCommit'],'NewSourceCommit':identities[1]['SourceCommit'],'StorageVersion':4,'NewUnsettledHostExitCode':rejected.returncode,'Rejection':'OriginalCapabilityUnavailable before sources Start','RejectionLogSha256':hashlib.sha256(rejected.stderr.encode()).hexdigest(),'OriginalRequestId':request['Id'],'OriginalPayloadHash':request['PayloadHash'],'OriginalOperator':before.get('Operator'),'OldMatchingReleaseCleared':'PhysicalCompleted','NewHostAfterSettlement':'StartedPaused','ReceiverExecutions':ledger['Executions'],'FrozenRequestUnchanged':True,'RealEquipment':'BLOCKED','Windows':'NOT_RUN'}
 a.output.write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))
finally:
 for proc in active:
  if proc.poll() is None:proc.kill();proc.wait()
