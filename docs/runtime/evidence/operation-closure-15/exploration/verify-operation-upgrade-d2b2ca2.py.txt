#!/usr/bin/env python3
"""One isolated synthetic old exact deployment -> current published host journey.

Requires preserved matching old host/packages. Does not alter either release or seed;
all deployment transitions use SaveConfig/ActivateConfig and controlled restart.
Windows/ACL/hardware qualification is independent and remains NOT_RUN.
"""
import argparse
import copy
import hashlib
import json
import os
from pathlib import Path
import socket
import sqlite3
import subprocess
import time
import uuid
import zipfile

p = argparse.ArgumentParser()
p.add_argument('--old-release', type=Path, required=True)
p.add_argument('--new-release', type=Path, required=True)
p.add_argument('--dotnet', required=True)
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
old, new, work = a.old_release.resolve(), a.new_release.resolve(), a.output.resolve()
work.mkdir(parents=True, exist_ok=False)
sha = lambda f: hashlib.sha256(Path(f).read_bytes()).hexdigest()
result = dict(Status='RUNNING', Windows='NOT_RUN', Field='BLOCKED', Commands=[], Phases=[])
client = fixture = None
logs = []
env = dict(os.environ, NO_PROXY='127.0.0.1,localhost', no_proxy='127.0.0.1,localhost')


def run(command, name, success=True):
    out = subprocess.run(command, text=True, capture_output=True, timeout=45, env=env)
    (work / (name + '.log')).write_text(out.stdout + out.stderr)
    result['Commands'].append(dict(Arguments=command, ExitCode=out.returncode, Log=name+'.log'))
    if success and out.returncode != 0:
        raise AssertionError(name + ': ' + (out.stdout+out.stderr)[-2000:])
    return out


def packages(release):
    refs = []
    for f in sorted((release / 'offline-extensions').glob('*.ipcx')):
        with zipfile.ZipFile(f) as archive:
            raw = archive.read('manifest.json')
            manifest = json.loads(raw)
        refs.append((f, dict(Id=manifest['Id'], Version=manifest['Version'], ManifestSha256=hashlib.sha256(raw).hexdigest().upper())))
    if len(refs) != 2:
        raise AssertionError('Both original observation/integration packages required')
    return refs

old_packages, new_packages = packages(old), packages(new)
old_station = json.loads((old/'app/config/station.json').read_text())
new_station = json.loads((new/'app/config/station.json').read_text())
if (old_station['StationId'], old_station['Integration']) != (new_station['StationId'], new_station['Integration']):
    raise AssertionError('This controlled journey requires same station/target identity; do not rewrite release inputs')
trusts = [json.loads((r/'offline-extensions/trust.Simulation.reference.json').read_text()) for r in (old,new)]
publishers = {}
for policy in trusts:
    for publisher in policy['Publishers']:
        if publisher['Id'] in publishers and publisher != publishers[publisher['Id']]:
            raise AssertionError('Different keys need different publisher identity; do not replace original trust')
        publishers[publisher['Id']] = publisher
trust = work/'trust.Simulation.json'
trust.write_text(json.dumps(dict(SchemaVersion=1, Publishers=list(publishers.values())), indent=2)+'\n')
store, data = work/'store', work/'data'
database = data/'Simulation'/old_station['StationId']/'station.db'


def common(release):
    return [a.dotnet, str(release/'app/Ipc.Product.dll'), '--config', str(release/'app/config'), '--data-root', str(data), '--extension-root', str(store), '--extension-trust', str(trust)]


def raw(value):
    offset = len(client_path.read_text())
    client.stdin.write(value+'\n')
    client.stdin.flush()
    return offset


def read(marker, offset):
    deadline = time.monotonic()+20
    while time.monotonic() < deadline:
        text = client_path.read_text()[offset:]
        pos = text.find(marker)
        if pos >= 0:
            try:
                return json.JSONDecoder().raw_decode(text[pos:])[0]
            except json.JSONDecodeError:
                pass
        if client.poll() is not None:
            raise AssertionError('client stopped: '+text[-2000:])
        time.sleep(.025)
    raise TimeoutError(marker+' '+text[-2000:])


def snapshot():
    return read('{\n  "State"', raw('snapshot'))


def wait(predicate):
    deadline = time.monotonic()+20
    while time.monotonic() < deadline:
        s = snapshot()
        if predicate(s):
            return s
        time.sleep(.05)
    raise TimeoutError(json.dumps(s)[-3000:])


def command(kind, owner=None, value=None):
    for _ in range(10):
        s = snapshot()
        receipt = read('{\n  "Status"', raw(json.dumps(dict(Action='command', Command=dict(Id=uuid.uuid4().hex, ExpectedRevision=s['State']['Revision'], Kind=kind, OwnerId=owner, Value=value)))))
        if receipt['Status'] == 'Conflict':
            result['Phases'].append(dict(Kind=kind, PreExecutionConflict=receipt))
            continue  # explicit pre-execution CAS refusal only; no uncertain retry
        if receipt['Status'] != 'Persisted':
            raise AssertionError(kind+': '+json.dumps(receipt))
        return receipt
    raise AssertionError('CAS did not converge')


def emit(kind, codes=None, quantity=None):
    raw(json.dumps(dict(Action='emit', Lane='left', Kind=kind, Codes=codes, Quantity=quantity)))


def start(release, phase):
    global client, client_path
    client_path = work/(phase+'.log')
    stream = client_path.open('w'); logs.append(stream)
    client = subprocess.Popen([*common(release), '--action','serve'], stdin=subprocess.PIPE, stdout=stream, stderr=subprocess.STDOUT, text=True, env=env)
    wait(lambda s: (s['State'].get('IntegrationServiceHealth') or {}).get('Status') == 'Ready' or (s['State'].get('IntegrationHealth') or {}).get('Health') == 'Ready')
    command('Login', value='maintenance')
    wait(lambda s: all(d['Health']=='Ready' for d in s['State']['Devices'].values()))


def stop():
    global client
    client.communicate('quit\n', timeout=20)
    if client.returncode != 0:
        raise AssertionError('controlled stop failed: '+client_path.read_text()[-2000:])
    client = None


def deploy(release, config, phase):
    saved = command('SaveConfig', value=json.dumps(config))
    command('ActivateConfig', saved['OwnerId'])
    result['Phases'].append(dict(Phase=phase, Revision=saved['OwnerId'], Version=config['Version'], State='PendingActivation'))
    stop(); start(release, phase+'-restart')
    state = read('{\n  "State"', raw('extensions'))
    if state['EffectiveDeployment'] != config['Version']:
        raise AssertionError('restart did not promote exact revision: '+json.dumps(state))
    result['Phases'].append(dict(Phase=phase, Effective=state['EffectiveDeployment']))


def extension_config(references, version):
    config = copy.deepcopy(old_station)
    config['Version'] = version
    refs = [x[1] for x in references]
    observation = next(r for r in refs if r['Id']=='reference.barcode')
    integration = next(r for r in refs if r['Id']=='reference.integration')
    instances = []
    for index, role in enumerate(('left','right')):
        binding = config['Devices'][index]
        instance = dict(Id=role+'-upgrade', PackageId=observation['Id'], ModuleId='barcode-source', DeviceId=binding['DeviceId'], Enabled=True, Parameters={'input.prefix':'000'})
        instances.append(instance)
        binding.update(Driver='extension:'+instance['Id'], Revision=version)
    config['Extensions'] = dict(Packages=refs, Instances=instances, Services=[dict(Id='upgrade-receiver',PackageId=integration['Id'],ModuleId='reference-http',Enabled=True,Parameters={})], Ports=[dict(Port='requests',InstanceId='upgrade-receiver',Target=config['Integration'])])
    return config


def supply(box, drop=False):
    command('OpenContext', value=box)
    wait(lambda s: (s['State'].get('Session') or {}).get('Status')=='Active')
    emit('Enter'); wait(lambda s: 'left' in s['State']['Lanes'])
    emit('Read',['000123'],2)
    current = wait(lambda s: s['State']['Lanes']['left']['Status']=='AwaitingAction')
    owner = current['State']['Lanes']['left']['Id']
    if drop:
        command('SetFixtureFault', value='Drop')
    command('Submit',owner)
    current = wait(lambda s: s['State']['Lanes']['left']['Status']==('OutcomeUnknown' if drop else 'Accepted'))
    request = next(r for r in current['Requests'] if r['Kind']=='Submit')
    if drop:
        raw(json.dumps(dict(Action='reconcile',RequestId=request['Id'])))
        current = wait(lambda s: s['State']['Lanes']['left']['Status']=='Accepted')
    if current['AcceptedQuantity'] != 2:
        raise AssertionError('unique acceptance incorrect')
    export = work/('history-'+box+'.json')
    raw(json.dumps(dict(Action='export',File=str(export),OwnerId=owner)))
    deadline = time.monotonic()+10
    while not export.exists() and time.monotonic()<deadline:
        time.sleep(.025)
    exported = json.loads(export.read_text())
    if len(exported['Requests']) != 2 or not any(x['Id']==request['Id'] and x['State']=='Accepted' for x in exported['Requests']):
        raise AssertionError('original validation/submission history not exported')
    emit('Depart'); wait(lambda s: s['State']['Lanes']['left']['Occupancy']=='Departed')
    emit('Reset'); wait(lambda s: s['State']['Lanes']['left']['Occupancy']=='Clear')
    command('CheckClose'); checked = wait(lambda s: s['State']['Session']['Status']=='AwaitingShortageDecision')
    command('ConfirmClose',checked['State']['Session']['CloseCheckRequest'],'CONFIRM')
    closed = wait(lambda s: s['State']['Session']['Status']=='Closed')
    result['Phases'].append(dict(Box=box,OriginalRequest=request,UniqueAccepted=closed['AcceptedQuantity'],ReconciledDrop=drop,ExportSha256=sha(export)))

try:
    result['HostSources'] = {name:json.loads((release/'app/release.json').read_text()) for name,release in [('old',old),('new',new)]}
    result['OriginalPackages'] = [dict(Reference=r,Sha256=sha(f)) for f,r in old_packages]
    result['NewPackages'] = [dict(Reference=r,Sha256=sha(f)) for f,r in new_packages]
    host_before = {str(release): {str(f.relative_to(release)):sha(f) for f in release.rglob('*') if f.is_file()} for release in (old,new)}
    # A literal loopback fixture using the old matching, retained contract implementation.
    from urllib.parse import urlparse
    endpoint = old_station['Integration']['Endpoint']; parsed = urlparse(endpoint)
    with socket.socket() as sock:
        sock.bind(('127.0.0.1',parsed.port))
    stream = (work/'fixture.log').open('w'); logs.append(stream)
    fixture = subprocess.Popen([a.dotnet,str(old/'fixture/Ipc.Fixture.dll'),'--url',endpoint,'--data',str(work/'receiver')],stdout=stream,stderr=subprocess.STDOUT,env=env)
    for f,_ in old_packages:
        run([*common(old),'--action','extension-install','--mode','Simulation','--file',str(f),'--sha256',sha(f)],f.stem+'-install')
    start(old,'old-start')
    deploy(old,extension_config(old_packages,'UPGRADE-OLD-1.0.1'),'old-deployment')
    supply('000001',drop=True)
    transition = copy.deepcopy(old_station); transition['Version']='UPGRADE-TRANSITION'; transition.pop('Extensions',None)
    deploy(old,transition,'transition-without-extensions')
    stop()
    original_hash = sha(database)
    collision = work/'occupied-backup.db'; collision.write_text('AC15 reserved destination, never overwrite')
    upgrade_args = [*common(new),'--action','upgrade','--previous-release',str(old),'--target-release',str(new)]
    failed = run([*upgrade_args,'--file',str(collision),'--receipt',str(work/'failed-upgrade.json')],'upgrade-collision',False)
    if failed.returncode == 0 or sha(database)!=original_hash or collision.read_text()!='AC15 reserved destination, never overwrite':
        raise AssertionError('failed upgrade did not preserve old database/destination')
    backup = work/'before-upgrade.db'; receipt_path = work/'upgrade.json'
    run([*upgrade_args,'--file',str(backup),'--receipt',str(receipt_path)],'upgrade')
    receipt = json.loads(receipt_path.read_text())
    if receipt['Status']!='ForwardMigrationCommitted' or receipt['Backup']['Source']['FormatVersion']!=3 or receipt['DestinationFormat']!=5:
        raise AssertionError('actual backed-up 3 -> 5 upgrade missing')
    upgraded_hash = sha(database)
    refused = run([*common(old),'--action','preflight'],'old-reader-refusal',False)
    if refused.returncode == 0 or sha(database)!=upgraded_hash:
        raise AssertionError('old reader did not refuse unchanged new format')
    for f,_ in new_packages:
        run([*common(new),'--action','extension-install','--mode','Simulation','--file',str(f),'--sha256',sha(f)],f.stem+'-install')
    start(new,'new-transition-start')
    deploy(new,extension_config(new_packages,'UPGRADE-NEW-1.0.3'),'new-deployment')
    supply('000002')
    stop(); start(new,'new-final-restart')
    final = snapshot()
    if final['State']['Session']['Status']!='Closed' or final['Recovery']:
        raise AssertionError('final restarted deployment not closed/drained')
    stop()
    with sqlite3.connect(f'file:{database}?mode=ro',uri=True) as db:
        if db.execute('pragma integrity_check').fetchone()[0]!='ok' or db.execute('select sum(quantity) from acceptance').fetchone()[0]!=4:
            raise AssertionError('upgraded aggregate/acceptance invalid')
        configs = [json.loads(x[0]) for x in db.execute('select json from configs')]
        if next(x for x in configs if x['Status']=='Effective')['Content']['Version']!='UPGRADE-NEW-1.0.3':
            raise AssertionError('effective deployment differs')
        result['Database'] = dict(Format=5,UniqueAcceptance=4,Requests=db.execute('select count(*) from requests').fetchone()[0],OriginalConfigsRetained=len(configs))
    if any(host_before[str(r)] != {str(f.relative_to(r)):sha(f) for f in r.rglob('*') if f.is_file()} for r in (old,new)):
        raise AssertionError('immutable release bytes changed')
    result.update(Status='PASS_SYNTHETIC_OLD_DEPLOYMENT_JOURNEY',BackupSha256=sha(backup),UpgradeReceipt=receipt,OriginalReleaseBytesUnchanged=True,OldReader='REFUSED_UNCHANGED',LegacyConsumerCoverage='UNVERIFIED_REFUSES_UNINSTALL_NO_OVERRIDE')
except BaseException as ex:
    result.update(Status='FAIL',Failure=type(ex).__name__+': '+str(ex))
    raise
finally:
    for process in (client,fixture):
        if process is not None and process.poll() is None:
            process.terminate(); process.wait(timeout=15)
    for log in logs:
        log.close()
    (work/'result.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    print(json.dumps(dict(Status=result['Status'],Evidence=str(work/'result.json'))))
