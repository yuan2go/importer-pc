#!/usr/bin/env python3
"""Verify immutable signed content across fresh independent builds and frozen hosts.

RSA-PSS signatures/ZIP containers may differ; signed manifest and every payload
must be identical. The supplied key stays outside output and package custody.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import zipfile

p = argparse.ArgumentParser()
p.add_argument('--contract-directory', type=Path, action='append', required=True)
p.add_argument('--key', type=Path, required=True)
p.add_argument('--trust', type=Path, required=True)
p.add_argument('--publisher', required=True)
p.add_argument('--version', required=True)
p.add_argument('--dotnet', default='dotnet')
p.add_argument('--output', type=Path, required=True)
a = p.parse_args()
root = Path(__file__).resolve().parent.parent
if a.output.exists():
    raise FileExistsError('Retain previous evidence; select a new output')
rows = []
for kind in ('observation', 'integration'):
    expected = None
    for directory in a.contract_directory:
        for repeat in range(2):
            with tempfile.TemporaryDirectory(prefix='ipc-reproducibility-') as temporary:
                package = Path(temporary) / 'probe.ipcx'
                subprocess.run(['python3', str(root / 'scripts/build-extension.py'), '--kind', kind,
                                '--contract', str(directory.resolve() / 'Ipc.Platform.dll'),
                                '--output', str(package), '--key', str(a.key.resolve()),
                                '--publisher', a.publisher, '--trust', str(a.trust.resolve()),
                                '--version', a.version, '--dotnet', a.dotnet], cwd=root, check=True,
                               stdout=subprocess.DEVNULL)
                with zipfile.ZipFile(package) as z:
                    raw = z.read('manifest.json')
                    manifest = json.loads(raw)
                    files = {f['Path']: hashlib.sha256(z.read(f['Path'])).hexdigest() for f in manifest['Files']}
                content = (raw, files)
                if expected is not None and content != expected:
                    raise AssertionError('Same ID/version changed signed content across independent builds: ' + kind)
                expected = content
                identity = json.loads(Path(str(package) + '.identity.json').read_text())
                assert identity['FrozenHostUnchanged'] is True
                rows.append(dict(Kind=kind, Repeat=repeat, ContractDirectory=str(directory),
                                 SourceCommit=identity['PluginSourceCommit'], Version=manifest['Version'],
                                 ManifestSha256=hashlib.sha256(raw).hexdigest(), Files=files,
                                 HostContractSha256=manifest['HostContractSha256'], FrozenHostUnchanged=True))
a.output.parent.mkdir(parents=True, exist_ok=True)
a.output.write_text(json.dumps(dict(Result='PASS_EXACT_SIGNED_CONTENT', Builds=rows,
                                   Windows='WINDOWS_NOT_RUN', PrivateKeyIncluded=False), indent=2) + '\n')
print('PASS two kinds: identical signed manifests/payloads across fresh builds and frozen host targets')
