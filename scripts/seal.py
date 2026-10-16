#!/usr/bin/env python3
"""Seal a locally built reference product. Hashes identify bytes, not publisher trust/ACLs."""
import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--bin', type=Path, required=True)
p.add_argument('--config', type=Path, default=Path('config'))
p.add_argument('--mode', choices=['Simulation', 'Production'], default='Simulation')
p.add_argument('--architecture', choices=['any', 'x64'], default='any')
a = p.parse_args()
output = a.bin / 'config'
output.mkdir(exist_ok=True)
modules = []
for key, dependencies in [('platform', []), ('supply', ['platform']), ('adapters', ['supply']), ('product', ['adapters'])]:
    name = 'Ipc.' + key.title() + '.dll'
    data = (a.bin / name).read_bytes()
    modules.append(dict(Id=key, Kind=key, Version='1.0.0', ContractMajor=1, Dependencies=dependencies,
                        File=name, Sha256=hashlib.sha256(data).hexdigest().upper()))
manifest = dict(SchemaVersion=1, ProductId='ipc-reference', Mode=a.mode, Runtime='net10.0', Architecture=a.architecture,
                BusinessModule='supply', Views=['workstation', 'history', 'alarms', 'diagnostics'], Modules=modules)
(output / 'product.json').write_text(json.dumps(manifest, indent=2) + '\n')
# Bind generated artifacts to source bytes, even for a reviewed working-tree build.
source_root = Path(__file__).resolve().parent.parent
inputs = [source_root / name for name in ('global.json', 'Directory.Build.props', 'NuGet.Config')]
for directory in ('src', 'config', 'scripts'):
    inputs.extend(f for f in (source_root / directory).rglob('*') if f.is_file() and
                  not {'bin', 'obj', '__pycache__'}.intersection(f.relative_to(source_root).parts))
fingerprint = hashlib.sha256()
for source in sorted(inputs):
    fingerprint.update(source.relative_to(source_root).as_posix().encode() + b'\0' + source.read_bytes() + b'\0')
release = dict(WorkPackage='WP-IPC-RUNTIME-HARDENING-04', SchemaVersion=1, SourceCodeSha256=fingerprint.hexdigest(),
               GitBase=subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=source_root, text=True).strip(),
               Mode=a.mode, Target='win-x64' if a.architecture == 'x64' else 'portable-development')
(a.bin / 'release.json').write_text(json.dumps(release, indent=2) + '\n')
station = json.loads((a.config / 'station.simulation.json').read_text())
station['Mode'] = a.mode
if a.mode == 'Production':
    # Deliberately incomplete production template: the product must remain NotReady.
    station['Policy'] = 'UNAPPROVED'
    station['Integration'] = dict(Id='unavailable', Revision='UNAPPROVED', Contract='UNAPPROVED', Endpoint='https://unconfigured.invalid/')
    for binding in station['Devices']:
        binding['Driver'] = 'unavailable'
(output / 'station.json').write_text(json.dumps(station, indent=2) + '\n')
shutil.copyfile(a.config / 'presentation.json', output / 'presentation.json')
shutil.copyfile(a.config / 'replay.jsonl', output / 'replay.jsonl')
print(f'Sealed {a.mode} configuration: {output}')
