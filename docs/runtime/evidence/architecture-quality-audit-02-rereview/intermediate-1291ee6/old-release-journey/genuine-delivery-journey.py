# Public path projection; original SHA256 2f89d7c3fd28e21146cfe1d8c9d6bb3f6a085de8bc2e2ec176f6e76eac252799
#!/usr/bin/env python3
"""Actual archived DB3 release -> matching Query -> DB5 -> independent new work.
Only synthetic reference targets are used. No schema/state mutation or device control.
A new --root is mandatory: all failures and earlier outputs remain untouched.
"""
import argparse, copy, hashlib, json, pathlib, queue, shutil, sqlite3, subprocess, threading, time, uuid
p=argparse.ArgumentParser();p.add_argument('--new-release',required=True,type=pathlib.Path);p.add_argument('--root',required=True,type=pathlib.Path)
p.add_argument('--source',type=pathlib.Path,default=pathlib.Path('${LOCAL_ARTIFACTS}/ipc-pr26-original-query-bridge-20261018/genuine-crash'))
p.add_argument('--old-release',type=pathlib.Path,default=pathlib.Path('${LOCAL_ARTIFACTS}/ipc-application-architecture-22-8981d54/sportmaster-portable/app'))
p.add_argument('--dotnet',default='${LOCAL_HOME}/.local/share/ipc-dotnet/dotnet');a=p.parse_args()
root=a.root.resolve();root.mkdir(parents=True,exist_ok=False);source=a.source.resolve();work=root/'genuine-recovery-delivery';shutil.copytree(source,work)
app=a.new_release.resolve();app=app/'Ipc.Assembly.dll' if app.is_dir() else app
if not app.is_file():raise ValueError('Missing new release assembly')
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def inventory(path):return {str(f.relative_to(path)):sha(f) for f in sorted(path.rglob('*')) if f.is_file() and not f.is_symlink()}
def write(name,value):(root/name).write_text(json.dumps(value,indent=2)+'\n')
def database_info(path):
 assert path.is_file(),str(path)
 with sqlite3.connect(str(path)) as db:
  return dict(Migration=db.execute('select max(version) from platform_migrations').fetchone()[0],Receipts=db.execute('select id,operation,stage,request,source,state,raw_hash from parcel_receipts order by rowid').fetchall())
original=json.loads((source/'old-operation.json').read_text());occurrence='1e637e066a674876966810edabec17b6'
db=work/'data/Simulation/reference-17/sportmaster/sportmaster.db';before_source=inventory(source);write('original-source-files.json',before_source)
write('candidate-input.json',dict(NewAssembly=str(app),Sha256=sha(app),OldRelease=str(a.old_release.resolve()),Source=str(source),SourceBefore=database_info(db)))
assert database_info(db)['Migration']==3
backup=root/'before-offline-recovery.db';command=[a.dotnet,str(app),'--action','recover-legacy-receipt','--file',str(db),'--sha256',sha(db).upper(),'--destination',str(backup),'--occurrence',occurrence,'--original-release',str(a.old_release.resolve()),'--reason','Synthetic delivery verification: original authoritative outcome only; independent missing raw remains']
wal=pathlib.Path(str(db)+'-wal')
if wal.is_file() and wal.stat().st_size:command+=['--wal-sha256',sha(wal).upper()]
write('offline-command.json',command)
result=subprocess.run(command,capture_output=True,text=True,timeout=60);(root/'offline.stdout').write_text(result.stdout);(root/'offline.stderr').write_text(result.stderr)
assert result.returncode==0,(result.returncode,result.stderr)
offline=json.loads(result.stdout);assert offline['Status']=='UpgradedCustodyRetained' and offline['DatabaseVersion']==5,offline
assert database_info(db)['Migration']==5
class Host:
 def __init__(self,name):
  self.name=name;self.events=[];self.error=open(root/(name+'.stderr'),'w');self.messages=queue.Queue()
  self.process=subprocess.Popen([a.dotnet,str(app),'--config',str(work/'config'),'--data-root',str(work/'data')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=self.error,text=True)
  def read():
   buffer=''
   for line in self.process.stdout:
    buffer+=line
    try:value=json.loads(buffer)
    except json.JSONDecodeError:continue
    self.messages.put(value);buffer=''
   self.messages.put({'EOF':True,'Tail':buffer})
  threading.Thread(target=read,daemon=True).start();started=self.get();assert started.get('Status')=='Started',started
 def get(self):
  value=self.messages.get(timeout=15);self.events.append(value);assert not value.get('EOF'),value;return value
 def send(self,value):
  self.process.stdin.write((value if isinstance(value,str) else json.dumps(value))+'\n');self.process.stdin.flush();return self.get()
 def snapshot(self):return self.send('snapshot')
 def cmd(self,kind,owner=None,value=None,expected=None):
  revision=self.snapshot()['Revision'] if expected is None else expected
  return self.send(dict(Id=uuid.uuid4().hex,ExpectedRevision=revision,Kind=kind,Owner=owner,Value=value))
 def close(self,clean=False):
  if self.process.poll() is None:
   if clean:self.process.stdin.write('quit\n');self.process.stdin.flush();self.process.wait(timeout=15)
   else:self.process.kill();self.process.wait()
  self.error.close();write(self.name+'.events.json',self.events)
  if clean:assert self.process.returncode==0,self.process.returncode
host=None
try:
 host=Host('old-envelope-current-host');s=host.snapshot();old=next(o for o in s['Operations'] if o['Id']==original['Id'])
 assert s['Paused'] and s['Configuration']['Confirmation']['Required'],s
 assert old['State']=='SlotResolved' and old['Upload']['State']=='Accepted' and not old.get('ImageReleased',False),old
 assert old['Upload']['Request']==original['Upload']['Request'] and old['FrozenConfiguration']==original['FrozenConfiguration']
 assert host.cmd('AuthorizeMaintenance','maintenance','SIMULATION_ONLY')['Status']=='Authorized'
 s=host.snapshot();owner=uuid.UUID(s['RuntimeId']).hex+':'+str(s['Configuration']['Revision'])+':'+s['Configuration']['Confirmation']['EffectiveHash']
 assert host.cmd('ConfirmConfiguration',owner,'Original old envelope inspected after successful initialization')['Status']=='Persisted'
 # New business uses a new current reference peer-root. The old frozen request must
 # keep querying its original target; the archived source fixture remains immutable.
 candidate=copy.deepcopy(host.snapshot()['Configuration']['Effective']);new_peer=str(work/'data/Simulation/reference-peers')
 def relocate_current(value):
  if isinstance(value,dict):
   if value.get('Kind') in ('integration','upload') and value.get('Package') is None and 'Parameters' in value:value['Parameters']['peer-root']=new_peer
   for child in value.values():relocate_current(child)
  elif isinstance(value,list):
   for child in value:relocate_current(child)
 relocate_current(candidate);s=host.snapshot()
 staged=host.cmd('StageConfiguration','Move future synthetic work to delivery-owned target; frozen old responsibility unchanged',json.dumps(candidate),s['Configuration']['Revision'])
 assert staged['Status']=='PendingRestart',staged
 write('new-current-candidate.json',candidate);host.close(clean=True);host=None
 host=Host('new-current-host');s=host.snapshot();assert s['Configuration']['Pending'] is None and s['Configuration']['Effective']==candidate,s['Configuration']
 assert not (s['Configuration'].get('Confirmation') or {}).get('Required',False)
 assert host.cmd('Resume')['Status']=='Persisted'
 admission='delivery-right-'+uuid.uuid4().hex
 assert host.cmd('EmitReferenceAt',admission,json.dumps(dict(PositionId='right',Code='000-right')))['Status']=='Enqueued'
 for _ in range(150):
  s=host.snapshot();new=next((o for o in s['Operations'] if o['AdmissionId']==admission),None)
  if new and new['State']=='SlotResolved':break
  time.sleep(.04)
 else:raise AssertionError(s)
 old=next(o for o in s['Operations'] if o['Id']==original['Id'])
 assert old['State']=='SlotResolved' and old['Upload']['State']=='Accepted' and not old.get('ImageReleased',False)
 assert old['Wcs']['Attempts']==1 and old['Upload']['Attempts']==1 and old['Upload']['Request']==original['Upload']['Request']
 host.close(clean=True);host=None
 with sqlite3.connect(str(db)) as sql:
  raw=sql.execute('select source,state,raw_hash from parcel_receipts where id=?',(occurrence,)).fetchone();assert raw==('Submit','Awaiting',None),raw
  query=sql.execute('select query_occurrence from parcel_receipt_custody where occurrence=?',(occurrence,)).fetchone()[0]
  query_row=sql.execute('select source,state,raw_hash from parcel_receipts where id=?',(query,)).fetchone();assert query_row[0:2]==('Query','Committed') and query_row[2]
  archived=sql.execute('select count(*) from parcel_archives where id=?',(original['Id'],)).fetchone()[0];assert archived==0
  revision=sql.execute('select max(version) from platform_migrations').fetchone()[0];assert revision==5
 image=db.parent/old['Image']['RelativeFile'];assert image.is_file() and sha(image).upper()==old['Image']['Sha256']
 counters=[]
 for stage in ('Wcs','Upload'):
  request=original[stage]['Request'];peer=pathlib.Path(request['Binding']['Parameters']['peer-root'])
  entries=[f for f in peer.glob('*/*.json') if json.loads(f.read_text())['Request']['Id']==request['Id']];assert len(entries)==1
  entry=json.loads(entries[0].read_text());assert entry['Executions']==1
  counters.append(dict(Stage=stage,Request=request['Id'],Executions=entry['Executions'],File=str(entries[0])))
 after_source=inventory(source)
 assert set(before_source)==set(after_source),'Original source files added/removed'
 lease_changes=[dict(File=k,Before=before_source[k],After=after_source[k]) for k in before_source if before_source[k]!=after_source[k]]
 assert all(x['File'].endswith('.lease') for x in lease_changes),'Original authoritative source facts changed'
 write('source-runtime-lease-changes.json',lease_changes)
 packages=old['FrozenConfiguration'].get('Extensions') or {};packages=packages.get('Packages') or []
 write('result.json',dict(Status='PASS',OldSource='8981d54a3dcb45aa08ea8953f1dd9d4cd2f079f7',OldDatabase=3,NewDatabase=5,OldMain=old['State'],OldUpload=old['Upload']['State'],OriginalOccurrence=occurrence,OriginalRaw=list(raw),QueryOccurrence=query,QueryRaw=list(query_row),OriginalImageHeld=True,OriginalArchived=False,OriginalAuthoritativeSourceUnchanged=True,OriginalRuntimeLeaseChanges=lease_changes,MaintenanceConfirmation=True,CurrentConfigurationTransition=True,IndependentRightMain=new['State'],Counters=counters,ActualFrozenPackages=packages,PackagePin='NOT_APPLICABLE: this actual old fixture has built-in reference providers; separate signed-package consumer evidence required',Offline=offline,Windows='NOT_RUN',SdkProtocolField='BLOCKED',Candidate=str(app),CandidateAssemblySha256=sha(app)))
 print('PASS actual DB3 interruption -> original-release Query -> DB5 custody retained -> old-envelope confirmation -> current configuration transition -> independent right work; original target Executions=1')
finally:
 if host is not None:host.close()
 write('source-after-files.json',inventory(source))
