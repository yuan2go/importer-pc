# Parameterized public reproduction; original executed script SHA256 164ad8138757c545e85a6884bee79b51be14fbf6fef67ebe01c531e576dbed11
import json,zipfile,hashlib,subprocess,queue,threading,uuid,time,copy
from pathlib import Path
import sys
base=Path(sys.argv[1]).resolve()
exe=sys.argv[2]
for name in ['sport-core','sport-selected']:
 target=base/(name+'-unpacked'); target.mkdir()
 with zipfile.ZipFile(base/(name+'.zip')) as z: z.extractall(target)
 for line in (target/'SHA256SUMS').read_text().splitlines():
  h,file=line.split('  ',1); assert hashlib.sha256((target/file).read_bytes()).hexdigest()==h
 root=target
lock=json.loads((root/'composition.lock.json').read_text()); assert len(lock['offline_bundles'])==2
assert not (base/'sport-core-unpacked/offline-extensions').exists()
assert sorted(p.name for p in (root/'offline-extensions').glob('*.ipcx'))==['reference.barcode-1.0.12.ipcx','reference.parcel-capabilities-1.7.2.ipcx']
run=base/'selected-runtime'; run.mkdir(); config=run/'config'; config.mkdir(); seed=json.loads((root/'app/config/business.json').read_text()); (config/'business.json').write_text(json.dumps(seed))
store=run/'store'; trust=base/'private/trust.json'; (config/'extension-store.json').write_text(json.dumps({'Root':str(store),'TrustFile':str(trust)}))
app=root/'app/Ipc.Assembly.dll'; log=[]; installed={}; modules={}
def action(kind,**args):
 command=[exe,str(app),'--action',kind,'--mode','Simulation','--extension-root',str(store),'--extension-trust',str(trust)]
 for key,value in args.items():command+=['--'+key,str(value)]
 result=subprocess.run(command,text=True,capture_output=True,check=True)
 value=json.loads(result.stdout); log.append({'action':kind,'result':value}); return value
for item in lock['offline_bundles']:
 file=root/item['File']; record=action('extension-install',file=file,sha256=item['PackageSha256']); assert record['Status']=='Installed'
 installed[item['Id']]=record['Package']; recovered=action('extension-recover',operation=record['OperationId']); assert recovered['Package']==record['Package']
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
   messages.put(value); buffer=''
 threading.Thread(target=reader,daemon=True).start()
 assert messages.get(timeout=15)['Status']=='Started'
 return proc,messages
proc,messages=start()
def send(value):
 proc.stdin.write((value if isinstance(value,str) else json.dumps(value))+'\n'); proc.stdin.flush(); return messages.get(timeout=15)
def snap():return send('snapshot')
def cmd(kind,owner=None,value=None,revision=None):
 s=snap(); out=send(dict(Id=uuid.uuid4().hex,ExpectedRevision=s['Revision'] if revision is None else revision,Kind=kind,Owner=owner,Value=value)); log.append({'command':kind,'result':out}); return out
def maintenance():
 assert cmd('Pause')['Status']=='Persisted'; assert cmd('AuthorizeMaintenance','maintenance','SIMULATION_ONLY')['Status']=='Authorized'
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
(base/'selected-journey.json').write_text(json.dumps(result,indent=2)+'\n'); print('PASS selected Release: two real signed readers, signed WCS, complete CAS/activation, startup failure keeps old Effective, safe retirement and original history')
