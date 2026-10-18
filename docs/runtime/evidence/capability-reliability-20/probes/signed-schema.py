#!/usr/bin/env python3
"""Synthetic signed-schema probe only; no real protocol, approval, device or production claim."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

p = argparse.ArgumentParser()
p.add_argument('--base-package', type=Path, required=True)
p.add_argument('--key', type=Path, required=True)
p.add_argument('--trust', type=Path, required=True)
p.add_argument('--host', type=Path, required=True)
p.add_argument('--output', type=Path, required=True)
p.add_argument('--dotnet', required=True)
a = p.parse_args()
with zipfile.ZipFile(a.base_package) as package:
    entries = {item.filename: package.read(item) for item in package.infolist()}
manifest = json.loads(entries['manifest.json'])
assert manifest['Mode'] == 'Simulation'
manifest['Id'] = 'reference.schema-probe'
manifest['Version'] = '1.0.0'
module = next(m for m in manifest['Modules'] if m['Id'] == 'parcel-integration')
module['Parameters'].append(dict(Key='endpoint', Group='Synthetic schema', Label='Provider-owned endpoint', Type='string', Required=False, MaximumLength=2048))
entries['manifest.json'] = (json.dumps(manifest, indent=2) + '\n').encode()
with tempfile.TemporaryDirectory(prefix='ipc20-signed-schema-') as temporary:
    root = Path(temporary)
    source = root / 'manifest.json'
    source.write_bytes(entries['manifest.json'])
    signature = root / 'signature'
    subprocess.run(['openssl', 'dgst', '-sha256', '-sign', str(a.key), '-sigopt', 'rsa_padding_mode:pss', '-sigopt', 'rsa_pss_saltlen:digest', '-out', str(signature), str(source)], check=True)
    entries['manifest.sig'] = signature.read_bytes()
    with zipfile.ZipFile(a.output, 'x', zipfile.ZIP_DEFLATED) as target:
        for name, data in sorted(entries.items()):
            target.writestr(name, data)
subprocess.run([a.dotnet, str(a.host), '--action', 'extension-check', '--mode', 'Simulation', '--file', str(a.output), '--extension-trust', str(a.trust)], check=True)
print(json.dumps(dict(Probe='SYNTHETIC_SIGNED_METADATA_NOT_REAL_SERVICE', ProviderBinarySource=manifest['SourceCommit'],
                      BasePackageSha256=hashlib.sha256(a.base_package.read_bytes()).hexdigest(), FixtureSha256=hashlib.sha256(Path(__file__).read_bytes()).hexdigest(),
                      ProbePackageSha256=hashlib.sha256(a.output.read_bytes()).hexdigest())))
