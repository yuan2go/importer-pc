# UX-COMMERCIAL-FIX-01: adapted immutable selected-release synthetic journey; original historical script preserved.
# Adapted from the retained N06 selected-release journey; executes only synthetic reference resources.
import json,zipfile,hashlib,subprocess,queue,threading,uuid,time,copy
from pathlib import Path
import sys
base=Path(sys.argv[1]).resolve()
exe=sys.argv[2]
for name in ['sportmaster-core','sportmaster-selected-portable']:
 target=base/(name+'-unpacked'); target.mkdir()
 with zipfile.ZipFile(base/(name+'.zip')) as z: z.extractall(target)
 for line in (target/'SHA256SUMS').read_text().splitlines():
  h,file=line.split('  ',1); assert hashlib.sha256((target/file).read_bytes()).hexdigest()==h
 root=target
lock=json.loads((root/'composition.lock.json').read_text()); assert len(lock['offline_bundles'])==3
assert not (base/'sportmaster-core-unpacked/offline-extensions').exists()
assert sorted(p.name for p in (root/'offline-extensions').glob('*.ipcx'))==['reference.barcode-1.3.0.ipcx','reference.parcel-capabilities-2.2.0.ipcx','sportmaster.rcs-task-1.1.0.ipcx']
run=base/'selected-runtime'; run.mkdir(); config=run/'config'; config.mkdir(); seed=json.loads((root/'app/config/business.json').read_text()); (config/'business.json').write_text(json.dumps(seed))
store=run/'store'; trust=Path(sys.argv[3]).resolve(); (config/'extension-store.json').write_text(json.dumps({'Root':str(store),'TrustFile':str(trust)}))
app=root/'app/Ipc.Assembly.dll'; log=[]; installed={}; modules={}
def action(kind,**args):
 command=[exe,str(app),'--action',kind,'--mode','Simulation','--extension-root',str(store),'--extension-trust',str(trust)]
 for key,value in args.items():command+=['--'+key,str(value)]
 result=subprocess.run(command,text=True,capture_output=True,check=True)
 value=json.loads(result.stdout); log.append({'action':kind,'result':value}); return value
for item in lock['offline_bundles']:
 file=root/item['File']; record=action('extension-install',file=file,sha256=item['PackageSha256']); assert record['Status']=='Installed'
 (base/('installed-'+item['Id']+'.json')).write_text(json.dumps(record)); installed[item['Id']]=record['Package']; recovered=action('extension-recover',operation=record['OperationId']); assert recovered['Package']==record['Package']
 with zipfile.ZipFile(file) as z: modules[item['Id']]=json.loads(z.read('manifest.json'))['Modules']
streams=[]
def start():
 error=open(run/('stderr-'+str(len(streams))+'.log'),'w'); streams.append(error)
 proc=subprocess.Popen([exe,str(app),'--config',str(config),'--data-root',str(run/'data')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=error,text=True)
 messages=queue.Queue()
 def reader():
  buffer=''
  for line in proc.stdout:
   buffer+=line
   try: value=json.loads(buffer)
   except json.JSONDecodeError:continue
   log.append({'observed':value}); messages.put(value); buffer=''
 threading.Thread(target=reader,daemon=True).start()
 first=messages.get(timeout=15); assert first['Status']=='Started',first
 return proc,messages
proc,messages=start()
def send(value):
 proc.stdin.write((value if isinstance(value,str) else json.dumps(value))+'\n'); proc.stdin.flush(); return messages.get(timeout=15)
def snap():return send('snapshot')
def cmd(kind,owner=None,value=None,revision=None):
 for attempt in range(100):
  s=snap(); out=send(dict(Id=uuid.uuid4().hex,ExpectedRevision=s['Revision'] if revision is None else revision,Kind=kind,Owner=owner,Value=value)); log.append({'command':kind,'result':out}); (base/'command-receipts.json').write_text(json.dumps(log,indent=2)+'\n')
  if out.get('Status')=='Rejected' and ((kind=='ManageExtension' and out['Reason']=='Pause and authenticate maintenance after active calls drain') or (kind=='StageConfiguration' and out['Reason']=='Pause and authenticate maintenance before configuration')):
   time.sleep(.03);continue
  return out
 raise AssertionError('Definitive maintenance precondition remained rejected')
def wait_ready():
 for _ in range(200):
  s=snap()
  if s['Availability']['Configure']:return s
  time.sleep(.02)
 raise AssertionError(s)
def maintenance(require_configure=True):
 assert cmd('Pause')['Status']=='Persisted'; assert cmd('AuthorizeMaintenance','maintenance','SIMULATION_ONLY')['Status']=='Authorized'
 if require_configure:wait_ready()
def stop():
 proc.stdin.write('quit\n'); proc.stdin.flush(); proc.wait(timeout=15); assert proc.returncode==0
maintenance(); s=snap(); candidate=copy.deepcopy(s['Configuration']['Effective']); barcode=installed['reference.barcode']; cap=installed['reference.parcel-capabilities']
params={x['Key']:x['Default'] for x in modules['reference.barcode'][0]['Parameters'] if x.get('Default') is not None}
instances=[]
for i,reader in enumerate(candidate['Topology']['Readers']):
 name='selected.reader.'+str(i); reader['Driver']='extension:'+name
 instances.append(dict(Id=name,PackageId=barcode['Id'],ModuleId='barcode-source',DeviceId=reader['DeviceId'],Enabled=True,Parameters=params))
candidate['Reader']=candidate['Topology']['Readers'][0]
module=next(m for m in modules[cap['Id']] if m['Id']=='parcel-integration')
candidate['Wcs']=dict(Kind='integration',Implementation='extension:'+cap['Id']+'/parcel-integration',Version=cap['Version'],InstanceId=seed['Wcs']['InstanceId'],Target='reference://selected/wcs',Contract=module['ContractIdentity'],Parameters={'peer-root':str(run/'external-peer'),'slot':'SELECTED-07'},Package=cap,Module='parcel-integration',Enabled=True)
candidate['Extensions']=dict(Packages=[barcode,cap],Instances=instances)
assert cmd('ValidateConfiguration',value=json.dumps(candidate))['Status']=='Validated'
assert cmd('StageConfiguration','Selected signed drivers and WCS from unpacked medium',json.dumps(candidate),s['Configuration']['Revision'])['Status']=='PendingRestart'
assert cmd('ManageExtension','extension-uninstall',json.dumps(cap))['Status']=='Rejected'
stop(); proc,messages=start(); activated=snap(); assert activated['Configuration']['Status']=='Effective' and activated['Configuration']['Pending'] is None
assert cmd('Resume')['Status']=='Persisted'
for position in ['left','right']:
 assert cmd('EmitReferenceAt','selected-'+position,json.dumps({'PositionId':position,'Code':'000'+position}))['Status']=='Enqueued'
 for _ in range(100):
  current=snap(); found=[o for o in current['Operations'] if o['AdmissionId']=='selected-'+position]
  if found and found[0]['State']=='SlotResolved':break
  time.sleep(.03)
 else:raise AssertionError(current)
 assert found[0]['Slot']=='SELECTED-07' and found[0]['Wcs']['Request']['Binding']['Package']==cap and found[0]['Wcs']['Attempts']==1
assert {o['PositionId'] for o in current['Operations']}=={'left','right'}
# Shipped workflow: valid candidate with an explicit synthetic driver start failure.
maintenance(); s=snap(); faulty=copy.deepcopy(s['Configuration']['Effective']); faulty['Extensions']['Instances'][0]['Parameters']['fault.fail-start']='true'
assert cmd('StageConfiguration','Explicit reference startup failure recovery',json.dumps(faulty),s['Configuration']['Revision'])['Status']=='PendingRestart'
stop()
failed=subprocess.run([exe,str(app),'--config',str(config),'--data-root',str(run/'data')],input='quit\n',capture_output=True,text=True,timeout=20)
assert failed.returncode!=0
(run/'expected-failed-start.log').write_text(failed.stderr)
state=json.loads((run/'data/Simulation/reference-17/sportmaster/configuration.json').read_text()); assert state['Status']=='ActivationFailed' and state['Effective']['Extensions']['Instances'][0]['Parameters']['fault.fail-start']=='false'
proc,messages=start(); recovered=snap(); assert recovered['Configuration']['Status']=='ActivationFailed' and recovered['Configuration']['Pending'] is not None
maintenance(); s=snap(); assert cmd('StageConfiguration','Remove settled selected bindings through same full revision',json.dumps(seed),s['Configuration']['Revision'])['Status']=='PendingRestart'
stop(); proc,messages=start(); maintenance(); assert snap()['Configuration']['Effective']['Extensions'] is None
for package in [cap,barcode]: assert cmd('ManageExtension','extension-uninstall',json.dumps(package))['Status']=='Uninstalled'
history=cmd('History'); assert len(history['Items'])==2 and all(x['Operation']['Wcs']['Attempts']==1 for x in history['Items'])
stop()
for stream in streams:stream.close()
result=dict(Status='PASS',SourceCommit=lock['SourceCommit'],CorePackages=0,SelectedPackages=lock['offline_bundles'],InstalledActivatedRetired=True,Positions=['left','right'],FailedStartupRetainedPriorEffective=True,OriginalWcsAttempts=[1,1],Windows='NOT_RUN',PhysicalIntegration='BLOCKED',Log=log)
(base/'selected-journey.json').write_text(json.dumps(result,indent=2)+'\n'); print('PASS selected Release: two signed reference reader instances, signed WCS, complete CAS/activation, startup failure keeps old Effective, safe retirement and original history')

# The same unpacked Release entry point now selects the actual signed RCS adapter.
# This HTTP fixture is synthetic and lives in the verification process, separate
# from the Application subprocess. It is not a real customer RCS service.
from http.server import ThreadingHTTPServer, BaseHTTPRequestHandler
import base64
requests=[]; responses={}
class RcsHandler(BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def do_POST(self):
  assert self.path=='/rcs/sorter/api'
  raw=self.rfile.read(int(self.headers['Content-Length']));request=json.loads(raw);requests.append(raw)
  params=request['data'][0]['params'];unpack=params['stationCode']=='SYNTHETIC-L'
  body={'requestId':request['requestId'],'result':[{'index':1,'command':'sorter.task.dest_request','success':not unpack,'code':'NEED_UNPACK' if unpack else None,'message':'NEED_UNPACK' if unpack else None,'args':None,'data':{'barcode':params['loadCode'],'skuId':'SYNTH-SKU','scanQty':1,'chutes':[{'chuteNo':'SYNTH-CHUTE','orderNo':'SYNTH-ORDER','remaining':0,'phase':'ASSIGNED'}]} if unpack else {'chuteCode':'SYNTH-CHUTE','length':None,'width':None,'height':None,'weight':None,'taskId':'SYNTH-TASK','skuId':'SYNTH-SKU'}}]}
  data=json.dumps(body,separators=(',',':')).encode();responses[request['requestId']]=data
  self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(data)));self.end_headers();self.wfile.write(data)
server=ThreadingHTTPServer(('127.0.0.1',0),RcsHandler);thread=threading.Thread(target=server.serve_forever,daemon=True);thread.start()
try:
 run=base/'rcs-runtime';run.mkdir();config=run/'config';config.mkdir()
 (config/'extension-store.json').write_text(json.dumps({'Root':str(store),'TrustFile':str(trust)}))
 subprocess.run([sys.executable,str(Path(sys.argv[4])/'scripts/configure-sportmaster-rcs.py'),'--base',str(root/'app/config/business.json'),'--installation-receipt',str(base/'installed-sportmaster.rcs-task.json'),'--parameters',str(Path(sys.argv[4])/'config/sportmaster/rcs.parameters.json'),'--target','http://127.0.0.1:'+str(server.server_port)+'/rcs/sorter/api','--output',str(config/'business.json')],check=True)
 proc,messages=start()
 for position in ['left','right']:
  assert cmd('EmitReferenceAt','rcs-'+position,json.dumps({'PositionId':position,'Code':'SYNTH-0001'}))['Status']=='Enqueued'
 for _ in range(200):
  current=snap()
  if len(current['Operations'])==2 and {o['State'] for o in current['Operations']}=={'UnpackRequired','SlotResolved'}:break
  time.sleep(.02)
 else:raise AssertionError(current)
 assert len(requests)==2 and len({json.loads(r)['requestId'] for r in requests})==2
 before={o['Id']:o for o in current['Operations']}
 for op in before.values():
  wire=op['Wcs']['Request']['IntegrationRequest'];reply=op['Wcs']['Replies'][0];received=next(r for r in requests if json.loads(r)['requestId']==wire['RequestId'])
  assert received==wire['Body'].encode() and hashlib.sha256(received).hexdigest().upper()==wire['BodyHash']
  assert base64.b64decode(reply['HttpResponse']['BodyBase64'])==responses[wire['RequestId']] and reply['HttpResponse']['Complete']
 unpack=next(o for o in before.values() if o['State']=='UnpackRequired');assert unpack['Wcs']['Replies'][0]['Destination']['Unpack']['Chutes'][0]['Remaining']==0
 assert cmd('AbandonUnsubmitted',unpack['Id'],'Synthetic inspected entity')['Status']=='Rejected'
 maintenance(False);assert cmd('AbandonUnsubmitted',unpack['Id'])['Status']=='Rejected'
 # Restart and unsupported Query/reconnect do not repeat either HTTP POST.
 stop();proc,messages=start();assert len(requests)==2
 for op in before.values():cmd('Reconcile',op['Id'])
 maintenance(False);assert cmd('ReconnectDevice',current['Configuration']['Effective']['Wcs']['InstanceId'])['Status']=='Reconnected';assert len(requests)==2
 for op in snap()['Operations']:assert op['Wcs']==before[op['Id']]['Wcs']
 assert cmd('AbandonUnsubmitted',unpack['Id'],'Synthetic entity inspected and removed; no RCS command')['Status']=='Persisted'
 retired=snap();after=next(o for o in retired['Operations'] if o['Id']==unpack['Id']);assert after['State']=='Abandoned' and after.get('RecoveryRequired',False) is False and after['Wcs']['Attempts']==1 and after['Wcs']['Replies'][0]['Destination']['Outcome']=='UnpackRequired' and len(requests)==2
 stop()
 (base/'rcs-journey.json').write_text(json.dumps({'Status':'PASS','SourceCommit':lock['SourceCommit'],'Requests':2,'NumericRequestIds':[json.loads(r)['requestId'] for r in requests],'RawBodiesAndResponsesVerified':True,'RestartQueryReconnectResent':False,'UnpackZeroRemainingRetained':True,'RunningAndReasonlessDispositionRejected':True,'GuardedLocalDispositionRetainsOriginalRcsReply':True,'Windows':'NOT_RUN','RealRcs':'BLOCKED'},indent=2)+'\n')
 print('PASS actual selected Release CLI: signed RCS, typed original wire/raw, dual positions, restart/no resend and guarded unpack disposition')
finally:
 if 'proc' in globals() and proc.poll() is None:
  proc.kill();proc.wait(timeout=10)
 server.shutdown();server.server_close();thread.join(timeout=5)
 for stream in streams:stream.close()
