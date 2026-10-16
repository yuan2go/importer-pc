#!/usr/bin/env python3
"""Seal a locally built reference product. Hashes identify bytes, not publisher trust/ACLs."""
import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--require-clean-source', action='store_true')
p.add_argument('--bin', type=Path, required=True)
p.add_argument('--config', type=Path, default=Path('config'))
p.add_argument('--product-definition', type=Path, default=Path('config/product.reference.json'))
p.add_argument('--mode', choices=['Simulation', 'Production'], default='Simulation')
p.add_argument('--architecture', choices=['any', 'x64'], default='any')
a = p.parse_args()
output = a.bin / 'config'
output.mkdir(exist_ok=True)
definition = json.loads(a.product_definition.read_text())
required = {'ProductId', 'Runtime', 'BusinessModule', 'Modes', 'Modules', 'Views', 'Layouts', 'Drivers'}
if set(definition) != required or a.mode not in definition['Modes']:
    raise ValueError('Invalid product definition/mode')
registered = definition['Modules']
ids = [m['Id'] for m in registered]
if len(ids) != len(set(ids)) or definition['BusinessModule'] not in ids:
    raise ValueError('Duplicate/missing business registration')
seen, visiting = set(), set()
def visit(key):
    if key in seen:
        return
    if key in visiting or key not in ids:
        raise ValueError('Missing dependency/cycle: ' + key)
    visiting.add(key)
    for dependency in next(m for m in registered if m['Id'] == key)['Dependencies']:
        visit(dependency)
    visiting.remove(key)
    seen.add(key)
modules = []
for module in registered:
    if set(module) != {'Id', 'Kind', 'Version', 'ContractMajor', 'Dependencies', 'File'} or module['ContractMajor'] < 1:
        raise ValueError('Invalid module registration')
    visit(module['Id'])
    relative = Path(module['File'])
    if relative.is_absolute() or any(part in ('', '.', '..') for part in module['File'].split('/')) or '\\' in module['File'] or ':' in module['File']:
        raise ValueError('Unsafe module path')
    current = a.bin
    if current.is_symlink():
        raise ValueError('Linked install directory')
    for part in relative.parts:
        current = current / part
        if current.is_symlink():
            raise ValueError('Linked module path')
    data = current.read_bytes()
    modules.append(dict(module, Sha256=hashlib.sha256(data).hexdigest().upper()))
manifest = dict(SchemaVersion=1, ProductId=definition['ProductId'], Mode=a.mode, Runtime=definition['Runtime'], Architecture=a.architecture,
                BusinessModule=definition['BusinessModule'], Views=definition['Views'], Modules=modules)
(output / 'product.json').write_text(json.dumps(manifest, indent=2) + '\n')
# Bind generated artifacts to source bytes, even for a reviewed working-tree build.
source_root = Path(__file__).resolve().parent.parent
inputs = [source_root / name for name in ('global.json', 'Directory.Build.props', 'NuGet.Config', 'ImporterPc.slnx', 'docs/runtime/operations.md')]
for directory in ('src', 'config', 'scripts'):
    inputs.extend(f for f in (source_root / directory).rglob('*') if f.is_file() and
                  not {'bin', 'obj', '__pycache__'}.intersection(f.relative_to(source_root).parts))
fingerprint = hashlib.sha256()
for source in sorted(inputs):
    fingerprint.update(source.relative_to(source_root).as_posix().encode() + b'\0' + source.read_bytes() + b'\0')
source_commit = subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=source_root, text=True).strip()
source_paths = ['src', 'config', 'scripts', 'global.json', 'Directory.Build.props', 'NuGet.Config', 'ImporterPc.slnx', 'docs/runtime/operations.md']
source_changes = subprocess.check_output(['git', 'status', '--porcelain', '--', *source_paths], cwd=source_root, text=True).strip()
if a.require_clean_source and source_changes:
    raise ValueError('Publication requires committed runtime/configuration/packaging inputs')
release = dict(WorkPackage='WP-IPC-AUDIT-RECONCILIATION-01', SchemaVersion=1, ReleaseVersion='AUDIT-RECONCILIATION-01', StorageVersion=3, ContractVersion='SIMULATION-v1', ProductId=definition['ProductId'], ProductDefinitionSha256=hashlib.sha256(a.product_definition.read_bytes()).hexdigest(), SourceCodeSha256=fingerprint.hexdigest(),
               SourceCommit=source_commit if not source_changes else None, WorkingTree=bool(source_changes), GitBase=source_commit,
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
presentation = json.loads((a.config / 'presentation.json').read_text())
if presentation['View'] not in definition['Views'] or presentation['Layout'] not in definition['Layouts']:
    raise ValueError('Unregistered initial view/layout')
(output / 'presentation.json').write_text(json.dumps(presentation, indent=2) + '\n')
shutil.copyfile(a.product_definition, output / 'product.definition.json')
shutil.copyfile(a.config / 'replay.jsonl', output / 'replay.jsonl')
print(f'Sealed {a.mode} configuration: {output}')
