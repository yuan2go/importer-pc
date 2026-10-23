#!/usr/bin/env python3
"""Run the actual unpacked portable Application and verify its durable reference journey."""
import argparse
import atexit
import socket
import urllib.request
import hashlib
import json
from pathlib import Path
import queue
import subprocess
import tempfile
import threading
import time
import uuid
import sys
from application_compatibility import CombinationRun

p = argparse.ArgumentParser()
p.add_argument('--package', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--peer-fixture', type=Path, help='Run independent loopback WCS/PLC/upload receiver process; requires --optional-features')
p.add_argument('--output', type=Path, required=True)
p.add_argument('--optional-features', action='store_true', help='Authenticate, wait for correlated weight, capture/upload, restart after lost upload reply, then explicitly continue')
p.add_argument('--attached-upload', action='store_true', help='Validate independent attached upload uncertainty while the main chain completes')
a = p.parse_args()
def run(evidence):
    if a.attached_upload and not a.optional_features: raise ValueError('--attached-upload requires --optional-features')
    root = a.package.resolve()
    identity = json.loads((root / 'ASSEMBLY.json').read_text())
    if identity['Target'] != 'portable': raise ValueError('This smoke requires the portable candidate, not an unexecuted Windows target')
    for row in (root / 'SHA256SUMS').read_text().splitlines():
        expected, relative = row.split('  ', 1)
        if hashlib.sha256((root / relative).read_bytes()).hexdigest() != expected: raise ValueError('Candidate changed: ' + relative)
    data = Path(tempfile.mkdtemp(prefix='ipc17-unpacked-'))
    peer_root = None
    peer_url = None
    peer_evidence = None
    if a.peer_fixture:
        if not a.optional_features: raise ValueError('--peer-fixture requires --optional-features')
        listener = socket.socket(); listener.bind(('127.0.0.1', 0)); port = listener.getsockname()[1]; listener.close()
        peer_url = 'http://127.0.0.1:'+str(port)+'/'
        peer_root = data / 'external-peer'
        peer_log = open(data / 'external-peer.log', 'w')
        peer_process = subprocess.Popen([a.dotnet, str(a.peer_fixture.resolve()), '--url', peer_url, '--data', str(peer_root)], stdout=peer_log, stderr=subprocess.STDOUT)
        def close_peer():
            if peer_process.poll() is None:
                peer_process.terminate()
                try: peer_process.wait(timeout=10)
                except subprocess.TimeoutExpired: peer_process.kill(); peer_process.wait()
            peer_log.close()
        atexit.register(close_peer)
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
        for _ in range(100):
            try:
                if opener.open(peer_url+'parcel-reference/health', timeout=1).read() == b'PARCEL-REFERENCE-v1': break
            except OSError: pass
            if peer_process.poll() is not None: raise ValueError('Independent reference service failed; see '+str(data/'external-peer.log'))
            time.sleep(.05)
        else: raise TimeoutError('Independent reference service did not start')
        peer_evidence = dict(Status='ReferencePeerStarted', ProcessId=peer_process.pid, Fixture=str(a.peer_fixture.resolve()), FixtureSha256=hashlib.sha256(a.peer_fixture.read_bytes()).hexdigest().upper(), Endpoint=peer_url+'parcel-reference/', Health='PARCEL-REFERENCE-v1')
    config = root / 'app/config'
    if a.optional_features:
        config = data / 'config'; config.mkdir()
        c = json.loads((root / 'app/config/business.json').read_text())
        def binding(kind):
            return dict(Kind=kind, Implementation='reference-'+kind, Version='1.0.0', InstanceId=kind+'.smoke', Target='reference://'+kind+'/smoke', Contract='PARCEL-REFERENCE-v1', Parameters={})
        c['Login'] = binding('login'); c['Login']['Parameters'] = {'subjects':'alice,bob'}
        c['Weight'] = dict(Source='Measured', Unit='Kg', Precision=3, ConfiguredValue=None, MaximumAgeMs=3000)
        # This journey verifies recovery of an already-associated original weight.
        # Select that supported reference policy explicitly; UntilDispatch expiry is
        # separately verified to keep blocking sends after its configured deadline.
        c['WeightValidity'] = 'AtAssociation'
        c['Scale'] = binding('weight')
        c['Images'] = dict(Camera=binding('camera'), Uploader=binding('upload'), MaximumBytes=4194304, Strategy='Attached' if a.attached_upload else 'BeforeMain')
        c['Images']['Uploader']['Parameters'] = {'reply-mode':'lose-after-commit'}
        if len(c.get('Topology', {}).get('Positions', [])) == 2:
            camera_right = dict(c['Images']['Camera'], InstanceId='camera.smoke.right', Target='reference://camera/smoke/right')
            scale_right = dict(c['Scale'], InstanceId='weight.smoke.right', Target='reference://weight/smoke/right')
            c['Topology']['Positions'][0].update(CameraInstanceId=c['Images']['Camera']['InstanceId'], ScaleInstanceId=c['Scale']['InstanceId'])
            c['Topology']['Positions'][1].update(CameraInstanceId=camera_right['InstanceId'], ScaleInstanceId=scale_right['InstanceId'])
            c['CapabilityInstances'] = [b for b in [c['Scale'], scale_right, c['Wcs'], c['Plc'], c['Login'], c['MaintenanceLogin'], c['Images']['Camera'], camera_right, c['Images']['Uploader']] if b]
        if peer_url:
            for bound in [c['Wcs'], c['Plc'], c['Images']['Uploader']]:
                if bound: bound['Parameters']['endpoint'] = peer_url+'parcel-reference/'
        (config / 'business.json').write_text(json.dumps(c))
    outputs = []
    # Per-run synthetic password, only sent over stdin to the real management API.
    # Never include it in CombinationInput, evidence events, argv or a product seed.
    management_password = uuid.uuid4().hex

    def start():
        process = subprocess.Popen([a.dotnet, str(root / 'app/Ipc.Assembly.dll'), '--config', str(config), '--data-root', str(data)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
        messages = queue.Queue()
        def read():
            buffer = ''; decoder = json.JSONDecoder()
            for line in process.stdout:
                buffer += line
                try:
                    value, length = decoder.raw_decode(buffer.lstrip())
                except json.JSONDecodeError: continue
                evidence.event(value); messages.put(value); outputs.append(value); buffer = buffer.lstrip()[length:].lstrip()
        threading.Thread(target=read, daemon=True).start()
        try:
            value = messages.get(timeout=20)
            if value.get('Status') != 'Started': raise ValueError('Application did not start')
        except BaseException:
            if process.poll() is None: process.kill()
            process.wait(timeout=10)
            evidence.event(dict(Status='StartupFailed', ExitCode=process.returncode, Stderr=process.stderr.read()))
            raise
        return process, messages

    def send(process, messages, value):
        process.stdin.write((value if isinstance(value, str) else json.dumps(value)) + '\n'); process.stdin.flush()
        try:
            return messages.get(timeout=10)
        except queue.Empty:
            if process.poll() is not None:
                raise RuntimeError('Application terminated with exit '+str(process.returncode)+': '+process.stderr.read())
            raise TimeoutError('Application did not return a command/snapshot response within 10 seconds')

    def authorize(process, messages, configure=False):
        def management(kind):
            return send(process, messages, dict(Id=uuid.uuid4().hex, ExpectedRevision=0, Kind=kind, Value=management_password))
        if configure and management('ConfigureManagementPassword')['Status'] != 'PasswordConfigured':
            raise ValueError('Real isolated-root management password setup failed')
        if management('EnterManagementMode')['Status'] != 'Authorized':
            raise ValueError('Real management authorization failed')

    def stop(process):
        process.stdin.write('quit\n'); process.stdin.flush(); process.wait(timeout=15)
        if process.returncode: raise ValueError('Stop failed: ' + process.stderr.read())

    evidence.bind(root, identity, json.loads((config / 'business.json').read_bytes()))
    if peer_evidence: evidence.event(peer_evidence)
    process, messages = start()
    try:
        authorize(process, messages, configure=True)
        initial_snapshot = send(process, messages, 'snapshot')
        evidence.effective(initial_snapshot['Configuration']['Effective'])
        if a.optional_features:
            login = send(process, messages, dict(Id=uuid.uuid4().hex, ExpectedRevision=0, Kind='Login', Owner='alice', Value='SIMULATION_ONLY'))
            if login['Status'] != 'Authenticated': raise ValueError('Selected authentication component did not authenticate')
        result = send(process, messages, dict(Id=uuid.uuid4().hex, ExpectedRevision=0, Kind='EmitReference', Owner='unpacked-parcel', Value='000000917'))
        if result['Status'] != 'Enqueued': raise ValueError('Reference camera was not admitted')
        for _ in range(60):
            snapshot = send(process, messages, 'snapshot')
            if snapshot['Operations'] and ((snapshot['Operations'][0].get('Capture') or {}).get('State') == 'Accepted' if a.optional_features else snapshot['Operations'][0]['State'] == ('SlotResolved' if identity['product']['business'] == 'sportmaster' else 'PlcAccepted')): break
            time.sleep(.05)
        operation = snapshot['Operations'][0]
        if a.optional_features:
            if operation['State'] != 'WaitingWeight' or operation['Wcs'] is not None or operation['Plc'] is not None: raise ValueError('Weight wait guard failed')
            measurement = dict(Id=uuid.uuid4().hex, AdmissionId=operation['AdmissionId'], DeviceId=c['Scale']['Target'], Value=2.345, Unit='Kg', Precision=3, Quality='Valid', Stable=True, MeasuredAt=__import__('datetime').datetime.now(__import__('datetime').timezone.utc).isoformat())
            send(process, messages, dict(Id=uuid.uuid4().hex, ExpectedRevision=snapshot['Revision'], Kind='EmitWeight', Value=json.dumps(measurement)))
            for _ in range(60):
                snapshot = send(process, messages, 'snapshot'); operation = snapshot['Operations'][0]
                if (operation['Upload']['State'] == 'Unknown' and operation['State'] in ('SlotResolved', 'PlcAccepted')) if a.attached_upload else operation['State'] == 'ImageUnknown': break
                time.sleep(.05)
            if not a.attached_upload and (operation['State'] != 'ImageUnknown' or operation['Wcs'] is not None or operation['Plc'] is not None): raise ValueError('Lost pre-main upload must block downstream effects')
            if a.attached_upload and (operation['Upload']['State'] != 'Unknown' or operation['State'] not in ('SlotResolved','PlcAccepted')): raise ValueError('Attached uncertainty must not block main chain')
        expected = 'SlotResolved' if identity['product']['business'] == 'sportmaster' else 'PlcAccepted'
        if not a.optional_features and operation['State'] != expected: raise ValueError('Incomplete actual reference chain: ' + operation['State'])
        if operation['ParcelCode'] != '000000917': raise ValueError('Original code changed')
        if not a.optional_features and identity['product']['business'] == 'sportmaster':
            second = send(process, messages, dict(Id=uuid.uuid4().hex, ExpectedRevision=0, Kind='EmitReferenceAt', Owner='unpacked-right', Value=json.dumps(dict(PositionId='right', Code='000000917'))))
            if second['Status'] != 'Enqueued': raise ValueError('Second physical reader input rejected')
            for _ in range(60):
                snapshot = send(process, messages, 'snapshot')
                if len(snapshot['Operations']) == 2 and all(op['State'] == 'SlotResolved' for op in snapshot['Operations']): break
                time.sleep(.05)
            if {op.get('PositionId') for op in snapshot['Operations']} != {'left', 'right'} or any(op['Plc'] for op in snapshot['Operations']): raise ValueError('Packaged dual-position flow incomplete')
        stop(process)
        process, messages = start()
        authorize(process, messages)
        restarted = send(process, messages, 'snapshot')
        recovered = next(op for op in restarted['Operations'] if op['Id'] == operation['Id'])
        if recovered['Id'] != operation['Id'] or recovered['Wcs'] != operation['Wcs'] or recovered['Plc'] != operation['Plc']: raise ValueError('Restart changed frozen original phases')
        if a.optional_features:
            if recovered['Upload']['Request'] != operation['Upload']['Request'] or restarted['CurrentOperator'] is not None: raise ValueError('Restart changed upload or revived a session')
            def command(kind, owner=None, value=None):
                command_id = uuid.uuid4().hex
                for attempt in range(3):
                    snap = send(process, messages, 'snapshot')
                    result = send(process, messages, dict(Id=command_id, ExpectedRevision=snap['Revision'], Kind=kind, Owner=owner, Value=value))
                    if result['Status'] != 'Conflict': return result
                return result  # Only explicit local CAS rejection permits refresh; never retry an uncertain send.
            reconciled = command('Reconcile', operation['Id'])
            if a.attached_upload:
                if reconciled['Status'] not in ('SlotResolved','PlcAccepted','PhysicalCompleted'): raise ValueError('Attached original reconciliation failed')
                final = send(process, messages, 'snapshot')['Operations'][0]
                if final['Upload']['State'] != 'Accepted' or final['Wcs'] != operation['Wcs']: raise ValueError('Attached reconciliation changed original main WCS fact')
            else:
                if reconciled['Status'] != 'Prepared': raise ValueError('Original upload query did not recover')
                snap = send(process, messages, 'snapshot')
                if snap['Operations'][0]['Plc'] is not None or snap['Operations'][0]['Wcs'] is not None: raise ValueError('Query dispatched downstream')
                if command('Login', 'alice', 'SIMULATION_ONLY')['Status'] != 'Authenticated': raise ValueError('Original subject could not reauthenticate')
                if command('Resume')['Status'] != 'Persisted': raise ValueError('Explicit restart admission resume rejected')
                if command('ContinuePrepared', operation['Id'], 'Synthetic original parcel inspected after restart')['Status'] != 'Persisted': raise ValueError('Original continuation rejected')
                final = send(process, messages, 'snapshot')['Operations'][0]
                if final['State'] != expected: raise ValueError('Continuation did not finish main chain')
            if final['Operator'] != operation['Operator'] or final['Upload']['Attempts'] != 1 or final['Capture']['Attempts'] != 1: raise ValueError('Recovery changed identity or repeated image stage')
            for stage in [final['Upload'], *([final['Wcs']] if final['Wcs'] else []), *([final['Plc']] if final['Plc'] else [])]:
                request = stage['Request']; peer = (peer_root/'parcel-peers' if peer_root else Path(request['Binding']['Parameters']['peer-root'])) / hashlib.sha256(request['Binding']['Target'].encode()).hexdigest().upper()
                ledger = json.loads((peer / (request['Id']+'.json')).read_text())
                if ledger['Executions'] != 1 or ledger['Request'] != request: raise ValueError('Independent receiver execution/identity differs')
                if stage == final['Upload'] and hashlib.sha256((peer/(request['Id']+'.png')).read_bytes()).hexdigest().upper() != final['Image']['Sha256']: raise ValueError('Uploaded image bytes differ')
            operation = final
        stop(process)
    except BaseException:
        if process.poll() is None: process.kill(); process.wait()
        raise
    result = dict(Status='PASS', SourceCommit=identity['SourceCommit'], Application=identity['product']['business'], Target='portable',
                  Checks=['candidate file hashes', 'actual selected assembly startup', 'camera-to-final-reference-chain', 'persistent original phases on restart', 'orderly stop', 'both packaged physical readers' if identity['product']['business'] == 'sportmaster' and not a.optional_features else 'selected topology'],
                  AttachedUpload=a.attached_upload, ExternalPeerProcess=bool(a.peer_fixture), OptionalFeatures=a.optional_features, OptionalChecks=['authenticated admission', 'same-entity measured weight', 'actual PNG upload bytes', 'attached upload independent from main' if a.attached_upload else 'lost pre-main upload blocks PLC', 'process restart original query', 'independent upload reconciliation keeps original operator' if a.attached_upload else 'explicit continuation original operator', 'one execution per independent receiver'] if a.optional_features else [],
                  State=operation['State'], OperationId=operation['Id'], Windows='NOT_RUN', RealEquipment='BLOCKED')
    evidence.finish(result)
    print(json.dumps(result))

evidence = CombinationRun(a.output, sys.argv, optional_features=a.optional_features, attached_upload=a.attached_upload, external_peer_process=bool(a.peer_fixture))
try:
    run(evidence)
except BaseException as failure:
    evidence.finish(error=failure)
    raise
