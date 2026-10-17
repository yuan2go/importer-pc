#!/usr/bin/env python3
"""Exercise an independently signed extension against an already published frozen host.

Real core/SQLite/Outbox/loopback, synthetic entity input. Never WPF/Windows acceptance.
"""
import argparse
import hashlib
import json
import socket
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path
import zipfile
import uuid
import sqlite3

p = argparse.ArgumentParser()
p.add_argument('--release', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
release = a.release.resolve()
work = Path(tempfile.mkdtemp(prefix='ipc-frozen-extension-smoke-'))
app = release / 'app'
hashes = lambda: {f.name: hashlib.sha256(f.read_bytes()).hexdigest() for f in app.glob('Ipc.*') if f.is_file()}
before = hashes()
package = release / 'offline-extensions/reference.barcode-1.0.0.ipcx'
trust = release / 'offline-extensions/trust.Simulation.reference.json'
integration_package = release / 'offline-extensions/reference.integration-1.0.0.ipcx'
with zipfile.ZipFile(package) as archive:
    raw_manifest = archive.read('manifest.json')
    manifest = json.loads(raw_manifest)
reference = dict(Id=manifest['Id'], Version=manifest['Version'], ManifestSha256=hashlib.sha256(raw_manifest).hexdigest().upper())
extensions = work / 'extensions'
config = work / 'config'
config.mkdir()
for name in ('product.json', 'presentation.json'):
    (config / name).write_bytes((app / 'config' / name).read_bytes())
station = json.loads((app / 'config/station.json').read_text())
station['StationId'] = 'SIM-' + uuid.uuid4().hex
station['Version'] = 'FROZEN-EXTENSION-1'
with socket.socket() as listener:
    listener.bind(('127.0.0.1', 0))
    port = listener.getsockname()[1]
url = f'http://127.0.0.1:{port}/'
station['Integration']['Endpoint'] = url
station['Extensions'] = dict(Packages=[reference], Instances=[])
for i, role in enumerate(('left', 'right')):
    instance = dict(Id=role + '-extension', PackageId=reference['Id'], ModuleId='barcode-source', DeviceId=work.name + '-' + role,
                    Enabled=True, Parameters={'input.prefix': '000' if i == 0 else '111'})
    station['Extensions']['Instances'].append(instance)
    station['Devices'][i].update(Driver='extension:' + instance['Id'], DeviceId=instance['DeviceId'], Revision=station['Version'])
if integration_package.exists():
    with zipfile.ZipFile(integration_package) as archive:
        raw = archive.read('manifest.json')
        integration_manifest = json.loads(raw)
    integration_reference = dict(Id=integration_manifest['Id'], Version=integration_manifest['Version'], ManifestSha256=hashlib.sha256(raw).hexdigest().upper())
    station['Extensions']['Packages'].append(integration_reference)
    station['Extensions']['Services'] = [dict(Id='receiver-primary', PackageId=integration_reference['Id'], ModuleId='reference-http', Enabled=True, Parameters={'metadata.label':'frozen host late integration'})]
    station['Extensions']['Ports'] = [dict(Port='requests', InstanceId='receiver-primary', Target=station['Integration'])]
(config / 'station.json').write_text(json.dumps(station))
common = [a.dotnet, str(app / 'Ipc.Product.dll'), '--config', str(config), '--data-root', str(work / 'data'),
          '--extension-root', str(extensions), '--extension-trust', str(trust)]
results = dict(Windows='NOT_RUN', PhysicalDevice='NOT_RUN', HostBefore=before, Package=reference)
client = fixture = None
client_log = (work / 'client.log').open('w')
fixture_log = (work / 'fixture.log').open('w')
try:
    for selected in [package] + ([integration_package] if integration_package.exists() else []):
        for action in ('extension-inspect', 'extension-install'):
            command = [*common, '--action', action, '--mode', 'Simulation', '--file', str(selected)]
            if action == 'extension-install':
                command.extend(['--sha256', hashlib.sha256(selected.read_bytes()).hexdigest().upper()])
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            (work / (selected.stem + '-' + action + '.log')).write_text(result.stdout + result.stderr)
            if result.returncode != 0:
                raise AssertionError(action + ': ' + result.stderr[-1200:])
            results[selected.stem + '-' + action] = 'PASS'
    results['TypedIntegration'] = integration_reference if integration_package.exists() else 'not selected'
    fixture = subprocess.Popen([a.dotnet, str(release / 'fixture/Ipc.Fixture.dll'), '--url', url, '--data', str(work / 'receiver')], stdout=fixture_log, stderr=subprocess.STDOUT)
    client = subprocess.Popen([*common, '--action', 'serve'], stdin=subprocess.PIPE, stdout=client_log, stderr=subprocess.STDOUT, text=True)

    def output_json(marker, offset):
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            data = (work / 'client.log').read_text()[offset:]
            start = data.find(marker)
            if start >= 0:
                try:
                    return json.JSONDecoder().raw_decode(data[start:])[0]
                except json.JSONDecodeError:
                    pass
            if client.poll() is not None:
                raise AssertionError('Frozen client exited: ' + data[-1200:])
            time.sleep(.025)
        raise TimeoutError(marker)

    def write(value):
        offset = (work / 'client.log').stat().st_size
        client.stdin.write(value + '\n')
        client.stdin.flush()
        return offset

    def snapshot():
        return output_json('{\n  "State"', write('snapshot'))

    def until(predicate):
        deadline = time.monotonic() + 15
        while time.monotonic() < deadline:
            state = snapshot()
            if predicate(state):
                return state
            time.sleep(.05)
        raise TimeoutError(json.dumps(state)[-1500:])

    def command(kind, owner=None, value=None):
        state = snapshot()
        payload = dict(Action='command', Command=dict(Id=f'smoke-{time.monotonic_ns()}', ExpectedRevision=state['State']['Revision'], Kind=kind, OwnerId=owner, Value=value))
        receipt = output_json('{\n  "Status"', write(json.dumps(payload)))
        if receipt['Status'] != 'Persisted':
            raise AssertionError(kind + ': ' + json.dumps(receipt))
        return receipt

    def emit(role, kind, codes=None, quantity=None):
        write(json.dumps(dict(Action='emit', Lane=role, Kind=kind, Codes=codes, Quantity=quantity)))

    until(lambda s: (s['State'].get('IntegrationServiceHealth') or {}).get('Status') == 'Ready' or (s['State'].get('IntegrationHealth') or {}).get('Health') == 'Ready')
    command('Login', value='maintenance')
    command('OpenContext', value='000001')
    until(lambda s: s['State'].get('Session', {}).get('Status') == 'Active')
    emit('left', 'Enter')
    until(lambda s: 'left' in s['State']['Lanes'])
    emit('left', 'Read', ['000123'], 2)
    read = until(lambda s: s['State']['Lanes']['left']['Status'] == 'AwaitingAction')
    command('Submit', owner=read['State']['Lanes']['left']['Id'])
    accepted = until(lambda s: s['State']['Lanes']['left']['Status'] == 'Accepted')
    if accepted['AcceptedQuantity'] != 2:
        raise AssertionError('Actual acceptance ledger must count 2 once')
    results['ActualExtensionBusinessSource'] = 'PASS'
    results['AcceptedQuantity'] = accepted['AcceptedQuantity']
    results['RequestEvidence'] = accepted['Requests']
    results['DeploymentRevision'] = station['Version']
    results['InputStationCanonicalJsonSha256'] = hashlib.sha256(json.dumps(station, sort_keys=True).encode()).hexdigest()
    client.communicate('quit\n', timeout=15)
    if client.returncode != 0:
        raise AssertionError('Controlled extension stop failed')
    with sqlite3.connect(f'file:{work / "data" / "Simulation" / station["StationId"] / "station.db"}?mode=ro', uri=True) as database:
        phases = [json.loads(row[0]) for row in database.execute("SELECT json FROM audit WHERE json_extract(json,'$.Kind')='DeploymentPhase' ORDER BY rowid")]
    results['DeploymentPhases'] = phases
    results['ControlledStop'] = 'PASS'
    results['HostAfter'] = hashes()
    if before != results['HostAfter']:
        raise AssertionError('Frozen host bytes changed')
    results['FrozenHostUnchanged'] = True
    results['Result'] = 'PASS_REFERENCE_PUBLISHED_HOST'
finally:
    for process in (client, fixture):
        if process is not None and process.poll() is None:
            process.terminate()
            process.wait(timeout=15)
    client_log.close()
    fixture_log.close()
    results['EvidenceRoot'] = str(work)
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps(results))
