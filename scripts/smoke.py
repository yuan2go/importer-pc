#!/usr/bin/env python3
"""Native core publication/maintenance smoke; never claims Windows desktop validation."""
import argparse
import json
import os
import shutil
import socket
import subprocess
import tempfile
import time
import urllib.request
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--product', type=Path, required=True)
p.add_argument('--fixture', type=Path, required=True)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = Path(tempfile.mkdtemp(prefix='ipc-published-smoke-'))
install = root / 'install'
shutil.copytree(a.product, install)
config = install / 'config'
station = json.loads((config / 'station.json').read_text())
with socket.socket() as sock:
    sock.bind(('127.0.0.1', 0))
    port = sock.getsockname()[1]
url = f'http://127.0.0.1:{port}/'
station['Integration']['Endpoint'] = url
(config / 'station.json').write_text(json.dumps(station))
http = urllib.request.build_opener(urllib.request.ProxyHandler({}))
results = {}

def run(action, data, *extra, expected=0):
    command = [a.dotnet, str(install / 'Ipc.Product.dll'), '--config', str(config), '--data-root', str(data), '--action', action, *map(str, extra)]
    completed = subprocess.run(command, text=True, capture_output=True, timeout=20)
    if completed.returncode != expected:
        raise AssertionError(f'{action}: expected exit {expected}, got {completed.returncode}: {completed.stderr[-600:]}')
    return completed.stdout

def snapshot(output):
    start = output.index('{\n  "State"')
    return json.JSONDecoder().raw_decode(output[start:])[0]

fixture_log = (root / 'fixture.log').open('w')
fixture = subprocess.Popen([a.dotnet, str(a.fixture.resolve()), '--url', url, '--data', str(root / 'receiver')], stdout=fixture_log, stderr=subprocess.STDOUT)
first = None
try:
    for _ in range(100):
        try:
            if http.open(url + 'health', timeout=1).read() == b'SIMULATION-v1':
                break
        except OSError:
            time.sleep(.05)
    else:
        raise TimeoutError('Fixture startup')
    data = root / 'data'
    s = snapshot(run('snapshot', data))
    assert s['State']['Actor'] is None and not s['Admit']['Allowed']
    results['published_native_start'] = 'PASS'
    console_log = (root / 'console.log').open('w')
    first = subprocess.Popen([a.dotnet, str(install / 'Ipc.Product.dll'), '--config', str(config), '--data-root', str(data), '--action', 'serve'], stdin=subprocess.PIPE, stdout=console_log, stderr=subprocess.STDOUT, text=True)
    for _ in range(100):
        if 'JSON command console' in (root / 'console.log').read_text():
            break
        time.sleep(.05)
    else:
        raise TimeoutError('First instance did not acquire station')
    assert 'Startup NotReady' in run('snapshot', data, expected=2)
    first.communicate('quit\n', timeout=10)
    assert first.returncode == 0
    console_log.close()
    results['second_process_ownership'] = 'PASS'
    backup = root / 'backup.db'
    run('backup', data, '--file', backup)
    run('preflight', data)
    restored = root / 'restored'
    run('restore', restored, '--file', backup)
    assert snapshot(run('snapshot', restored))['State']['Quarantined']
    results['published_backup_preflight_restore_quarantine'] = 'PASS'
    diagnostic = root / 'diagnostic.json'
    run('export', data, '--file', diagnostic)
    assert url not in diagnostic.read_text() and 'Payload"' not in diagnostic.read_text()
    results['metadata_export'] = 'PASS'
    readonly = root / 'readonly'
    readonly.mkdir()
    readonly.chmod(0o500)
    try:
        assert 'Startup NotReady' in run('snapshot', readonly, expected=2)
    finally:
        readonly.chmod(0o700)
    results['unwritable_storage_not_ready'] = 'PASS'
    product = json.loads((config / 'product.json').read_text())
    changed = json.loads(json.dumps(product))
    changed['Modules'][0]['Sha256'] = '0' * 64
    (config / 'product.json').write_text(json.dumps(changed))
    assert 'Missing/changed module' in run('snapshot', root / 'invalid-module', expected=2)
    results['changed_module_not_ready'] = 'PASS'
    before = json.loads(http.open(url + 'statistics').read())['Calls']
    product['Mode'] = station['Mode'] = 'Production'
    (config / 'product.json').write_text(json.dumps(product))
    (config / 'station.json').write_text(json.dumps(station))
    assert 'Production NotReady' in run('snapshot', root / 'production', expected=2)
    after = json.loads(http.open(url + 'statistics').read())['Calls']
    assert before == after and not (root / 'production').exists()
    results['production_no_fixture_or_business'] = 'PASS'
    results['windows_desktop_runtime'] = 'BLOCKED_ON_NON_WINDOWS'
    a.output.parent.mkdir(parents=True, exist_ok=True)
    a.output.write_text(json.dumps(results, indent=2) + '\n')
    print(json.dumps(results))
    print('Local evidence retained:', root)
finally:
    if first is not None and first.poll() is None:
        first.kill()
        first.wait(timeout=10)
    fixture.terminate()
    fixture.wait(timeout=10)
    fixture_log.close()
