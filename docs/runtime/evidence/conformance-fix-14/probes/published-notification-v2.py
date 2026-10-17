import pathlib,subprocess,socket,tempfile,json,threading,queue,time,urllib.request,hashlib,shutil
b=pathlib.Path('.artifacts/conformance-14');release=(b/'candidates/unpacked/fix14-68cdc36-integration-portable').resolve();work=pathlib.Path(tempfile.mkdtemp(prefix='ipc14-console-'));dotnet='/reference/dotnet-10.0.402/dotnet'
s=socket.socket();s.bind(('127.0.0.1',0));port=s.getsockname()[1];s.close();url=f'http://127.0.0.1:{port}/'
shutil.copytree(release/'app/config',work/'config');cfg=json.loads((work/'config/station.json').read_text());cfg['StationId']='SIM-HEADLESS-DELIVERY';cfg['Integration']['Endpoint']=url;(work/'config/station.json').write_text(json.dumps(cfg))
fixturelog=(work/'fixture.log').open('w');fixture=subprocess.Popen([dotnet,str(release/'fixture/Ipc.Fixture.dll'),'--url',url,'--data',str(work/'receiver')],stdout=fixturelog,stderr=subprocess.STDOUT)
client=None;messages=queue.Queue();cues=[];output=(work/'client.log').open('w')
try:
 for _ in range(200):
  try:
   if urllib.request.build_opener(urllib.request.ProxyHandler({})).open(url+'health',timeout=.3).status==200:break
  except OSError:time.sleep(.03)
 client=subprocess.Popen([dotnet,str(release/'app/Ipc.Product.dll'),'--config',str(work/'config'),'--data-root',str(work/'data'),'--action','serve'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,bufsize=1)
 def reader():
  buffer=''
  for line in client.stdout:
   output.write(line);output.flush()
   if not buffer and not line.lstrip().startswith('{'):continue
   buffer+=line
   try:obj=json.loads(buffer)
   except json.JSONDecodeError:continue
   buffer=''
   if 'Notification' in obj:cues.append(obj['Notification'])
   else:messages.put(obj)
 threading.Thread(target=reader,daemon=True).start()
 def send(value):client.stdin.write((value if isinstance(value,str) else json.dumps(value))+'\n');client.stdin.flush()
 def receive(predicate):
  end=time.monotonic()+10
  while time.monotonic()<end:
   obj=messages.get(timeout=end-time.monotonic())
   if predicate(obj):return obj
  raise TimeoutError('console response')
 def snapshot():send('snapshot');return receive(lambda x:'State'in x and 'ProjectionVersion'in x)
 def wait(predicate):
  end=time.monotonic()+10
  while time.monotonic()<end:
   current=snapshot()
   if predicate(current):return current
   time.sleep(.02)
  raise TimeoutError('native state')
 def command(kind,owner=None,value=None):
  snap=snapshot();send({'Action':'command','Command':{'Id':__import__('uuid').uuid4().hex,'ExpectedRevision':snap['State']['Revision'],'Kind':kind,'OwnerId':owner,'Value':value}});result=receive(lambda x:'Status'in x and 'Revision'in x);assert result['Status']=='Persisted',result
 def emit(kind):send({'Action':'emit','Lane':'left','Kind':kind,'Codes':['000123'],'Quantity':1})
 wait(lambda x: x['State']['IntegrationHealth'] and x['State']['IntegrationHealth']['Health']=='Ready')
 command('Login',value='maintenance');command('OpenContext',value='000001');wait(lambda x:x['State']['Session'] and x['State']['Session']['Status']=='Active')
 for index in range(2):
  emit('Enter');wait(lambda x:x['State']['Lanes'].get('left',{}).get('Occupancy')=='Occupied');emit('Read');snap=wait(lambda x:x['State']['Lanes']['left']['Status']=='AwaitingAction');command('Submit',owner=snap['State']['Lanes']['left']['Id']);wait(lambda x:x['State']['Lanes']['left']['Status']=='Accepted')
  end=time.monotonic()+10
  while len(cues)<=index and time.monotonic()<end:time.sleep(.02)
  cue=cues[index];send({'Action':'notification-ack','Sequence':cue['Sequence']});ack=receive(lambda x:x.get('Status')=='NotificationAcknowledged');assert ack['Sequence']==cue['Sequence']
  emit('Depart');wait(lambda x:x['State']['Lanes']['left']['Occupancy']=='Departed');emit('Reset');wait(lambda x:x['State']['Lanes']['left']['Occupancy']=='Clear')
 snap=snapshot();assert snap['NotificationBacklog']==0 and snap['AcceptedQuantity']==2;send('quit');client.wait(timeout=10);assert client.returncode==0
 result=dict(SourceCommit='68cdc36f7821825c1ee9e53d866015be5ecf3044',Status='PASS',PublishedConsoleDeliveryAndExplicitAck=len(cues),AcceptedQuantity=2,Backlog=0,ControlledStopExit=0,EvidenceRoot=str(work),Windows='NOT_RUN')
 (b/'published-notification.json').write_text(json.dumps(result,indent=2)+'\n');print(json.dumps(result))
finally:
 if client and client.poll() is None:client.kill();client.wait()
 fixture.terminate();fixture.wait(timeout=10);fixturelog.close();output.close()
