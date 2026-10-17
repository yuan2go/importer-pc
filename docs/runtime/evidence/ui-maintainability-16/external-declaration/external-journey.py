#!/usr/bin/env python3
"""Synthetic external package install -> CAS save -> explicit activate -> real source job."""
import argparse,hashlib,json,os
from pathlib import Path
import socket,subprocess,time,uuid,zipfile

p=argparse.ArgumentParser();p.add_argument('--release',type=Path,required=True);p.add_argument('--package',type=Path,required=True);p.add_argument('--trust',type=Path,required=True);p.add_argument('--dotnet',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
if a.output.exists(): raise FileExistsError('Retain earlier evidence')
a.output.mkdir(parents=True)
app=a.release.resolve()/'app';work=a.output.resolve();config=work/'config';config.mkdir();store=work/'extensions';data=work/'data'
hashes=lambda:{p.relative_to(app).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(app.rglob('*')) if p.is_file()}
before=hashes()
for name in ['product.json','presentation.json']: (config/name).write_bytes((app/'config'/name).read_bytes())
station=json.loads((app/'config/station.json').read_text());station['StationId']='EXTERNAL-'+uuid.uuid4().hex
with socket.socket() as s: s.bind(('127.0.0.1',0));port=s.getsockname()[1]
url=f'http://127.0.0.1:{port}/';station['Integration']['Endpoint']=url
(config/'station.json').write_text(json.dumps(station,indent=2)+'\n')
common=[a.dotnet,str(app/'Ipc.Product.dll'),'--config',str(config),'--data-root',str(data),'--extension-root',str(store),'--extension-trust',str(a.trust.resolve())]
with zipfile.ZipFile(a.package) as z:raw=z.read('manifest.json');manifest=json.loads(raw)
reference=dict(Id=manifest['Id'],Version=manifest['Version'],ManifestSha256=hashlib.sha256(raw).hexdigest().upper())
results=dict(ExternalSourceCommit=manifest['SourceCommit'],Package=reference,PackageSha256=hashlib.sha256(a.package.read_bytes()).hexdigest(),Windows='NOT_RUN')
fixture=client=None;client_log=fixture_log=None
try:
 for action in ['extension-inspect','extension-install']:
  command=[*common,'--action',action,'--mode','Simulation','--file',str(a.package.resolve())]
  if action=='extension-install':command+=['--sha256',results['PackageSha256'].upper()]
  process=subprocess.run(command,capture_output=True,text=True,timeout=30)
  (work/(action+'.log')).write_text(process.stdout+process.stderr)
  if process.returncode:raise RuntimeError(action+': '+process.stderr)
  results[action]='PASS'
 fixture_log=(work/'fixture.log').open('w');fixture=subprocess.Popen([a.dotnet,str(a.release.resolve()/'fixture/Ipc.Fixture.dll'),'--url',url,'--data',str(work/'receiver')],stdout=fixture_log,stderr=subprocess.STDOUT)
 def start(name):
  global client,client_log
  client_log=(work/name).open('w');client=subprocess.Popen([*common,'--action','serve'],stdin=subprocess.PIPE,stdout=client_log,stderr=subprocess.STDOUT,text=True)
 def output(offset,predicate):
  deadline=time.monotonic()+15
  while time.monotonic()<deadline:
   text=(work/current_log).read_text()[offset:]
   for i,c in enumerate(text):
    if c not in '[{':continue
    try:value,end=json.JSONDecoder().raw_decode(text[i:])
    except json.JSONDecodeError:continue
    if predicate(value):return value
   if client.poll() is not None:raise RuntimeError('Client exited: '+text[-2000:])
   time.sleep(.02)
  raise TimeoutError('Expected console output: '+text[-2000:])
 def write(line):
  offset=(work/current_log).stat().st_size;client.stdin.write(line+'\n');client.stdin.flush();return offset
 def snapshot():return output(write('snapshot'),lambda v:isinstance(v,dict) and 'State' in v and 'Requests' in v)
 def until(predicate):
  deadline=time.monotonic()+15
  while time.monotonic()<deadline:
   state=snapshot()
   if predicate(state):return state
   time.sleep(.05)
  raise TimeoutError(json.dumps(state)[-3000:])
 def command(kind,owner=None,value=None):
  state=snapshot();request=dict(Action='command',Command=dict(Id='external-'+uuid.uuid4().hex,ExpectedRevision=state['State']['Revision'],Kind=kind,OwnerId=owner,Value=value))
  receipt=output(write(json.dumps(request)),lambda v:isinstance(v,dict) and 'Status' in v)
  if receipt['Status']!='Persisted':raise AssertionError(kind+': '+json.dumps(receipt))
  return receipt
 def stop():
  client.communicate('quit\n',timeout=20)
  if client.returncode:raise RuntimeError('Controlled stop failed')
  client_log.close()
 current_log='initial-client.log';start(current_log)
 until(lambda s:(s['State'].get('IntegrationServiceHealth') or {}).get('Status')=='Ready' or (s['State'].get('IntegrationHealth') or {}).get('Health')=='Ready')
 command('Login',value='maintenance')
 draft=json.loads(json.dumps(station));draft['Version']='EXTERNAL-2';draft['Extensions']=dict(Packages=[reference],Instances=[])
 for index,role in enumerate(['left','right']):
  instance=dict(Id=role+'-external',PackageId=reference['Id'],ModuleId='barcode-source',DeviceId='external-'+role+'-'+station['StationId'],Enabled=True,Parameters={'input.prefix':'000' if role=='left' else '111'})
  draft['Extensions']['Instances'].append(instance);draft['Devices'][index].update(Driver='extension:'+instance['Id'],DeviceId=instance['DeviceId'],Revision=draft['Version'])
 results['SaveConfigReceipt']=command('SaveConfig',value=json.dumps(draft))
 revisions=output(write('configs'),lambda v:isinstance(v,list) and any(isinstance(x,dict) and x.get('Content',{}).get('Version')=='EXTERNAL-2' for x in v))
 saved=next(r for r in revisions if r['Content']['Version']=='EXTERNAL-2');results['SavedRevision']=saved['Id'];results['SavedStatus']=saved['Status']
 results['ActivateReceipt']=command('ActivateConfig',owner=saved['Id'])
 revisions=output(write('configs'),lambda v:isinstance(v,list) and any(isinstance(x,dict) and x.get('Id')==saved['Id'] for x in v))
 if next(r for r in revisions if r['Id']==saved['Id'])['Status']!='PendingActivation':raise AssertionError('Save/activate must be pending before controlled restart')
 results['PendingActivation']='PASS';stop()
 current_log='activated-client.log';start(current_log)
 until(lambda s:len(s['State']['Devices'])==2 and all(d.get('Health')=='Ready' for d in s['State']['Devices'].values()) and (s['State'].get('IntegrationHealth') or {}).get('Health')=='Ready')
 command('Login',value='maintenance')
 revisions=output(write('configs'),lambda v:isinstance(v,list) and any(isinstance(x,dict) and x.get('Id')==saved['Id'] for x in v))
 effective=next(r for r in revisions if r['Id']==saved['Id'])
 if effective['Status']!='Effective':raise AssertionError('Actual owned initialization must promote exact revision')
 results['EffectiveAfterControlledRestart']='PASS'
 command('OpenContext',value='000001');until(lambda s:s['State'].get('Session',{}).get('Status')=='Active')
 write(json.dumps(dict(Action='emit',Lane='left',Kind='Enter')));until(lambda s:'left' in s['State']['Lanes'])
 write(json.dumps(dict(Action='emit',Lane='left',Kind='Read',Codes=['000123'],Quantity=2)))
 ready=until(lambda s:s['State']['Lanes']['left']['Status']=='AwaitingAction');command('Submit',owner=ready['State']['Lanes']['left']['Id'])
 accepted=until(lambda s:s['State']['Lanes']['left']['Status']=='Accepted')
 if accepted['AcceptedQuantity']!=2:raise AssertionError('Unique acceptance ledger must count2')
 results['AcceptedQuantity']=accepted['AcceptedQuantity'];results['Requests']=accepted['Requests'];results['SchemaParametersApplied']=draft['Extensions']['Instances'];stop()
 results['ControlledStop']='PASS';results['HostUnchanged']=before==hashes()
 if not results['HostUnchanged']:raise AssertionError('Frozen host changed')
 results['Result']='PASS_EXTERNAL_BUILD_INSTALL_CONFIG_ACTIVATE_SOURCE_JOB_STOP'
 (work/'result.json').write_text(json.dumps(results,indent=2)+'\n')
 print(results['Result'])
finally:
 for process in [client,fixture]:
  if process is not None and process.poll() is None:
   process.terminate()
   try:process.wait(timeout=10)
   except subprocess.TimeoutExpired:process.kill();process.wait()
 for log in [client_log,fixture_log]:
  if log is not None and not log.closed:log.close()
