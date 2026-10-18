"""Business release identity and actual combination evidence; no dependency resolver."""
import datetime
import hashlib
import json
from pathlib import Path
import re
import threading
import uuid
import xml.etree.ElementTree as ET

APPLICATIONS = {'sportmaster': 'Sportmaster', 'direct-plc': 'DirectPlc', 'wcs-to-plc': 'WcsToPlc'}


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest().upper()


def object_sha(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode()).hexdigest().upper()


def read_business(root, application, selection):
    module = APPLICATIONS[application]
    project = ET.parse(root / f'src/Ipc.Application.{module}/Ipc.Application.{module}.csproj')
    version = project.findtext('./PropertyGroup/Version')
    declared = next(c['version'] for c in selection['components'] if c['id'] == 'application.' + application)
    if not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', version or '') or declared != version:
        raise ValueError('Business project version and composition differ')
    policy = root / 'config/compatibility' / (application + '.json')
    declaration = json.loads(policy.read_bytes())
    if declaration['SchemaVersion'] != 1 or declaration['ApplicationId'] != application:
        raise ValueError('Business compatibility declaration identity differs')
    return dict(BusinessVersion=version, BusinessAssembly=f'Ipc.Application.{module}.dll', CompatibilitySha256=sha(policy)), policy


def selected_providers(configuration):
    """The full configuration hash binds parameters/targets; this lists exact implementations."""
    bindings = [configuration.get(k) for k in ['Scale', 'Wcs', 'Plc', 'Login', 'MaintenanceLogin']]
    bindings += [(configuration.get('Images') or {}).get(k) for k in ['Camera', 'Uploader']]
    bindings += configuration.get('CapabilityInstances') or []
    providers = []
    seen = set()
    for b in bindings:
        if b and b.get('Enabled', True) and b['InstanceId'] not in seen:
            seen.add(b['InstanceId'])
            providers.append({k: b[k] for k in ['Kind', 'Implementation', 'Version', 'Contract', 'InstanceId', 'Target']})
            if b.get('Package'): providers[-1]['Package'] = b['Package']
    for reader in (configuration.get('Topology') or {}).get('Readers', [configuration['Reader']]):
        provider = dict(Kind='observation', Implementation='manual', Version='1.0.0', Contract='CONTROLLED-OBSERVATION-v1', DeviceId=reader['DeviceId'], Revision=reader['Revision'])
        if reader['Driver'] != 'manual':
            deployment = configuration['Extensions']
            instance = next(i for i in deployment['Instances'] if 'extension:' + i['Id'] == reader['Driver'])
            package = next(p for p in deployment['Packages'] if p['Id'] == instance['PackageId'])
            provider.update(Implementation='extension:' + package['Id'] + '/' + instance['ModuleId'], Version=package['Version'], Package=package)
            # Reader contract is checked against its signed module by the real factory.
        providers.append(provider)
    return providers


def require_combination(declaration, providers):
    identities = [{k: p[k] for k in ['Kind', 'Implementation', 'Version', 'Contract']} for p in providers]
    for group in declaration['ProviderSets']:
        if identities and all(identity in group['Providers'] for identity in identities):
            return group['Id']
    raise ValueError('Selected provider combination is not declared compatible')


def utc():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


class CombinationRun:
    def __init__(self, output, command):
        self.output = Path(output).resolve()
        self.events_path = self.output.with_suffix(self.output.suffix + '.events.jsonl')
        if self.events_path.exists(): raise FileExistsError('Evidence events already exist')
        self.record = dict(SchemaVersion=1, RunId=uuid.uuid4().hex, Status='RUNNING', StartedAt=utc(), Command=command,
                           Windows='NOT_RUN', RealEquipment='BLOCKED', Assurance='SIMULATION_REFERENCE_ONLY')
        with self.output.open('x') as f: json.dump(self.record, f, indent=2)
        self.events = self.events_path.open('x')
        self.lock = threading.Lock()

    def save(self):
        temporary = self.output.with_name(self.output.name + '.' + self.record['RunId'] + '.tmp')
        temporary.write_text(json.dumps(self.record, indent=2) + '\n')
        temporary.replace(self.output)

    def bind(self, root, identity, initial_config):
        self.root = root
        self.initial_config = initial_config
        release = json.loads((root / 'app/application-release.json').read_bytes())
        declaration = json.loads((root / 'app/application-compatibility.json').read_bytes())
        if release['SchemaVersion'] != 2 or identity['BusinessVersion'] != release['BusinessVersion'] or identity['SourceCommit'] != release['SourceCommit'] or identity['CompatibilitySha256'] != sha(root / 'app/application-compatibility.json'):
            raise ValueError('Combination evidence requires matching business release schema2')
        self.declaration = declaration
        self.record.update(Application=release['ApplicationId'], BusinessVersion=release['BusinessVersion'], SourceCommit=release['SourceCommit'],
            Target=identity['Target'], CandidateFilesSha256=sha(root / 'SHA256SUMS'), ReleaseSha256=sha(root / 'app/application-release.json'),
            AssemblyManifestSha256=sha(root / 'ASSEMBLY.json'), CompatibilitySha256=release['CompatibilitySha256'],
            InputConfiguration=initial_config, InputConfigurationSha256=object_sha(initial_config))
        self.save()

    def effective(self, config):
        providers = selected_providers(config)
        self.record.update(EffectiveConfiguration=config, EffectiveConfigurationSha256=object_sha(config), Providers=providers,
                           CompatibilitySet=require_combination(self.declaration, providers))
        self.save()

    def event(self, value):
        with self.lock:
            if self.events.closed: return
            self.events.write(json.dumps(dict(At=utc(), Value=value), separators=(',', ':')) + '\n'); self.events.flush()

    def finish(self, result=None, error=None):
        # Preserve partial facts even when validation/startup/recovery fails.
        with self.lock:
            self.events.close()
        if result:
            if 'EffectiveConfigurationSha256' not in self.record: raise ValueError('No actual effective configuration was observed')
            self.record.update(result)
        self.record.update(Status='FAIL' if error else 'PASS', FinishedAt=utc(), EventsSha256=sha(self.events_path), EventsFile=self.events_path.name)
        if error: self.record['Failure'] = dict(Type=type(error).__name__, Message=str(error))
        self.save()


def verify_record(record_path, root, effective_configuration=None):
    record_path, root = Path(record_path), Path(root)
    r = json.loads(record_path.read_bytes())
    if r.get('SchemaVersion') != 1 or r.get('Status') != 'PASS': raise ValueError('Combination was not verified PASS')
    for key, path in [('CandidateFilesSha256', root/'SHA256SUMS'), ('ReleaseSha256', root/'app/application-release.json'),
                      ('AssemblyManifestSha256', root/'ASSEMBLY.json'), ('CompatibilitySha256', root/'app/application-compatibility.json')]:
        if r[key] != sha(path): raise ValueError('Record belongs to another/changed candidate: ' + key)
    for line in (root/'SHA256SUMS').read_text().splitlines():
        expected, name = line.split('  ', 1)
        path = (root/name).resolve()
        if not path.is_relative_to(root.resolve()) or sha(path) != expected.upper(): raise ValueError('Candidate file changed: ' + name)
    if Path(r['EventsFile']).name != r['EventsFile'] or sha(record_path.parent/r['EventsFile']) != r['EventsSha256']: raise ValueError('Original run events differ')
    config = r['EffectiveConfiguration']
    if object_sha(config) != r['EffectiveConfigurationSha256'] or object_sha(r['InputConfiguration']) != r['InputConfigurationSha256']:
        raise ValueError('Recorded configuration changed')
    if effective_configuration is not None and object_sha(effective_configuration) != r['EffectiveConfigurationSha256']:
        raise ValueError('Record belongs to a different effective configuration')
    release = json.loads((root/'app/application-release.json').read_bytes())
    if (r['Application'], r['BusinessVersion'], r['SourceCommit']) != (release['ApplicationId'], release['BusinessVersion'], release['SourceCommit']):
        raise ValueError('Record business release identity differs')
    providers = selected_providers(config)
    declaration = json.loads((root/'app/application-compatibility.json').read_bytes())
    if providers != r['Providers'] or require_combination(declaration, providers) != r['CompatibilitySet']: raise ValueError('Recorded provider combination differs')
    return r


if __name__ == '__main__':
    import argparse
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--record', type=Path, required=True); p.add_argument('--package', type=Path, required=True)
    p.add_argument('--configuration', type=Path)
    a = p.parse_args()
    verified = verify_record(a.record, a.package, json.loads(a.configuration.read_bytes()) if a.configuration else None)
    print(json.dumps(dict(Status='PASS', RunId=verified['RunId'], Application=verified['Application'], BusinessVersion=verified['BusinessVersion'])))
