#!/usr/bin/env python3
"""Run the actual unpacked portable Application and verify its durable reference journey."""
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

p = argparse.ArgumentParser()
p.add_argument('--package', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = a.package.resolve()
identity = json.loads((root / 'ASSEMBLY.json').read_text())
if identity['Target'] != 'portable': raise ValueError('This smoke requires the portable candidate, not an unexecuted Windows target')
for row in (root / 'SHA256SUMS').read_text().splitlines():
    expected, relative = row.split('  ', 1)
    if hashlib.sha256((root / relative).read_bytes()).hexdigest() != expected: raise ValueError('Candidate changed: ' + relative)
data = Path(tempfile.mkdtemp(prefix='ipc17-unpacked-'))
outputs = []

def start():
    process = subprocess.Popen([a.dotnet, str(root / 'app/Ipc.Assembly.dll'), '--config', str(root / 'app/config'), '--data-root', str(data)], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE, text=True)
    messages = queue.Queue()
    def read():
        buffer = ''; decoder = json.JSONDecoder()
        for line in process.stdout:
            buffer += line
            try:
                value, length = decoder.raw_decode(buffer.lstrip())
            except json.JSONDecodeError: continue
            messages.put(value); outputs.append(value); buffer = buffer.lstrip()[length:].lstrip()
    threading.Thread(target=read, daemon=True).start()
    value = messages.get(timeout=20)
    if value.get('Status') != 'Started': raise ValueError('Application did not start')
    return process, messages

def send(process, messages, value):
    process.stdin.write((value if isinstance(value, str) else json.dumps(value)) + '\n'); process.stdin.flush()
    return messages.get(timeout=10)

def stop(process):
    process.stdin.write('quit\n'); process.stdin.flush(); process.wait(timeout=15)
    if process.returncode: raise ValueError('Stop failed: ' + process.stderr.read())

process, messages = start()
try:
    result = send(process, messages, dict(Id=uuid.uuid4().hex, ExpectedRevision=0, Kind='EmitReference', Owner='unpacked-parcel', Value='000000917'))
    if result['Status'] != 'Enqueued': raise ValueError('Reference camera was not admitted')
    for _ in range(60):
        snapshot = send(process, messages, 'snapshot')
        if snapshot['Operations']: break
        time.sleep(.05)
    operation = snapshot['Operations'][0]
    expected = 'SlotResolved' if identity['product']['business'] == 'sportmaster' else 'PlcAccepted'
    if operation['State'] != expected: raise ValueError('Incomplete actual reference chain: ' + operation['State'])
    if operation['ParcelCode'] != '000000917': raise ValueError('Original code changed')
    stop(process)
    process, messages = start()
    restarted = send(process, messages, 'snapshot')
    recovered = restarted['Operations'][0]
    if recovered['Id'] != operation['Id'] or recovered['Wcs'] != operation['Wcs'] or recovered['Plc'] != operation['Plc']: raise ValueError('Restart changed frozen original phases')
    stop(process)
except BaseException:
    if process.poll() is None: process.kill(); process.wait()
    raise
result = dict(Status='PASS', SourceCommit=identity['SourceCommit'], Application=identity['product']['business'], Target='portable',
              Checks=['candidate file hashes', 'actual selected assembly startup', 'camera-to-final-reference-chain', 'persistent original phases on restart', 'orderly stop'],
              State=operation['State'], OperationId=operation['Id'], Windows='NOT_RUN', RealEquipment='BLOCKED')
a.output.write_text(json.dumps(result, indent=2) + '\n')
print(json.dumps(result))
