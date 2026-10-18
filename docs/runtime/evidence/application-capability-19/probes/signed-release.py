#!/usr/bin/env python3
"""Exercise an exact signed provider in a frozen portable Application, synthetic roots only."""
import argparse
import hashlib
import json
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time
import uuid
import zipfile
from datetime import datetime, timezone

parser = argparse.ArgumentParser()
parser.add_argument('--application-root', type=Path, required=True, help='Frozen wcs-to-plc candidate app directory')
parser.add_argument('--package', type=Path, required=True)
parser.add_argument('--trust', type=Path, required=True)
parser.add_argument('--config-template', type=Path, required=True)
parser.add_argument('--dotnet', required=True)
parser.add_argument('--output', type=Path, required=True)
a = parser.parse_args()
app, package, trust = a.application_root.resolve(), a.package.resolve(), a.trust.resolve()
release = json.loads((app / 'application-release.json').read_text())
if release['ApplicationId'] != 'wcs-to-plc': raise ValueError('This exact journey requires wcs-to-plc')
root = Path(tempfile.mkdtemp(prefix='ipc19-release-signed-'))
with zipfile.ZipFile(package) as archive:
    raw = archive.read('manifest.json'); manifest = json.loads(raw)
if manifest['Mode'] != 'Simulation': raise ValueError('Reference journey accepts Simulation only')
reference = {key: manifest[key] for key in ['Id', 'Version']}
reference['ManifestSha256'] = hashlib.sha256(raw).hexdigest().upper()
receipt = subprocess.run([a.dotnet, str(app / 'Ipc.Assembly.dll'), '--action', 'extension-install', '--mode', 'Simulation',
                          '--file', str(package), '--sha256', hashlib.sha256(package.read_bytes()).hexdigest().upper(),
                          '--extension-root', str(root / 'store'), '--extension-trust', str(trust)], capture_output=True, text=True, check=True)
installed = json.loads(receipt.stdout)
if installed['Status'] != 'Installed': raise ValueError('Original store did not confirm installation')
config = root / 'config'; config.mkdir()
c = json.loads(a.config_template.read_text())
if c['Mode'] != 'Simulation': raise ValueError('No Production data or device access permitted')
for key, kind in [('Login', 'login'), ('Scale', 'weight'), ('Wcs', 'integration'), ('Plc', 'plc')]:
    bound = c[key]; module = 'strict-wcs' if kind == 'integration' else 'parcel-' + kind
    bound.update(Implementation='extension:' + reference['Id'] + '/' + module, Version=reference['Version'], Package=reference, Module=module)
    if kind in ['integration', 'plc']: bound['Parameters']['peer-root'] = str(root / 'external-peers')
for key, kind in [('Camera', 'camera'), ('Uploader', 'upload')]:
    bound = c['Images'][key]; module = 'parcel-' + kind
    bound.update(Implementation='extension:' + reference['Id'] + '/' + module, Version=reference['Version'], Package=reference, Module=module)
    if kind == 'upload': bound['Parameters']['peer-root'] = str(root / 'external-peers')
c['Extensions'] = {'Packages': [reference], 'Instances': []}
(config / 'business.json').write_text(json.dumps(c))
(config / 'extension-store.json').write_text(json.dumps({'Root': str(root / 'store'), 'TrustFile': str(trust)}))
p = subprocess.Popen([a.dotnet, str(app / 'Ipc.Assembly.dll'), '--config', str(config), '--data-root', str(root / 'data')],
                     stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
messages = queue.Queue()
def read():
    decoder = json.JSONDecoder(); buffer = ''
    for line in p.stdout:
        buffer += line
        try: value, length = decoder.raw_decode(buffer.lstrip())
        except json.JSONDecodeError: continue
        messages.put(value); buffer = buffer.lstrip()[length:].lstrip()
threading.Thread(target=read, daemon=True).start()
def send(value):
    p.stdin.write((value if isinstance(value, str) else json.dumps(value)) + '\n'); p.stdin.flush()
    return messages.get(timeout=10)
def command(kind, owner=None, value=None):
    command_id = uuid.uuid4().hex
    for _ in range(3):
        snapshot = send('snapshot')
        result = send(dict(Id=command_id, ExpectedRevision=snapshot['Revision'], Kind=kind, Owner=owner, Value=value))
        if result['Status'] != 'Conflict': return result
    return result
try:
    if messages.get(timeout=10)['Status'] != 'Started': raise ValueError('Application did not start')
    if command('Login', 'alice', 'SIMULATION_ONLY')['Status'] != 'Authenticated': raise ValueError('Selected login did not authenticate')
    if command('EmitReference', 'release-signed-parcel', '0000000019')['Status'] != 'Enqueued': raise ValueError('Observation not queued')
    for _ in range(100):
        snapshot = send('snapshot')
        if snapshot['Operations'] and (snapshot['Operations'][0].get('Capture') or {}).get('State') == 'Accepted': break
        time.sleep(.03)
    op = snapshot['Operations'][0]
    if op['State'] != 'WaitingWeight' or not op['Image']: raise ValueError('Original capture must precede weight')
    measurement = dict(Id=uuid.uuid4().hex, AdmissionId=op['AdmissionId'], DeviceId=c['Scale']['Target'], Value=2.345,
                       Unit='Kg', Precision=3, Quality='Valid', Stable=True, MeasuredAt=datetime.now(timezone.utc).isoformat())
    if command('EmitWeight', value=json.dumps(measurement))['Status'] != 'Enqueued': raise ValueError('Weight not queued')
    for _ in range(100):
        op = send('snapshot')['Operations'][0]
        if op['State'] == 'PlcAccepted': break
        time.sleep(.03)
    if op['State'] != 'PlcAccepted' or op['Upload']['State'] != 'Accepted': raise ValueError('Signed chain did not complete')
    if command('Reconcile', op['Id'])['Status'] != 'PhysicalCompleted': raise ValueError('Original peer completion not proven')
    op = send('snapshot')['Operations'][0]
    p.stdin.write('quit\n'); p.stdin.flush(); p.wait(timeout=10)
    if p.returncode: raise ValueError('Application failed to stop')
    facts = []
    for key in ['Upload', 'Wcs', 'Plc']:
        request = op[key]['Request']
        ledger = root / 'external-peers' / hashlib.sha256(request['Binding']['Target'].encode()).hexdigest().upper() / (request['Id'] + '.json')
        entry = json.loads(ledger.read_text())
        if entry['Executions'] != 1 or entry['Request'] != request: raise ValueError('Receiver repeated or changed original request')
        facts.append(dict(Stage=key, RequestId=request['Id'], PayloadHash=request['PayloadHash'], ReceiverLedgerSha256=hashlib.sha256(ledger.read_bytes()).hexdigest(), Executions=1))
    result = dict(Status='PASS', RuntimeSourceCommit=release['SourceCommit'], Package=reference, PackageSha256=hashlib.sha256(package.read_bytes()).hexdigest(),
                  InstallStatus=installed['Status'], FinalState=op['State'], OperationId=op['Id'], DeclaredWcsInputs=dict(MeasuredWeight=True, ImageAddress=True), ReceiverFacts=facts,
                  Boundary='Frozen portable application with exact release-contract package; synthetic local reference peers; Windows UI/SDK/field NOT_RUN/BLOCKED')
    a.output.write_text(json.dumps(result, indent=2) + '\n'); print(json.dumps(result))
finally:
    if p.poll() is None: p.kill(); p.wait()
