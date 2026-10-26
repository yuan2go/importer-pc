"""Business release identity and actual combination evidence; no dependency resolver."""
import datetime
import hashlib
import json
from pathlib import Path
import re
import threading
import uuid
import xml.etree.ElementTree as ET
from urllib.parse import urlsplit

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
    contract = ET.parse(root / 'src/Ipc.Application.Contracts/Ipc.Application.Contracts.csproj').findtext('./PropertyGroup/Version')
    declared_contract = next(c['version'] for c in selection['components'] if c['id'] == 'application-contracts')
    if declared_contract != contract:
        raise ValueError('Application contract project version and composition differ')
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


def run_input(record):
    keys = ['Command', 'WorkingDirectory', 'OptionalFeatures', 'AttachedUpload', 'ExternalPeerProcess', 'Windows', 'RealEquipment', 'Assurance', 'Checks', 'OptionalChecks'] if record['SchemaVersion'] >= 3 else []
    return {key: record[key] for key in ['RunId', 'Application', 'BusinessVersion', 'SourceCommit', 'Target',
        'CandidateFilesSha256', 'ReleaseSha256', 'AssemblyManifestSha256', 'CompatibilitySha256',
        'InputConfiguration', 'InputConfigurationSha256'] + keys}



def journey_checks(application, optional, attached):
    checks = ['candidate file hashes', 'actual selected assembly startup', 'camera-to-final-reference-chain',
              'persistent original phases on restart', 'orderly stop',
              'both packaged physical readers' if application == 'sportmaster' and not optional else 'selected topology']
    extra = ['authenticated admission', 'same-entity measured weight', 'actual PNG upload bytes',
             'attached upload independent from main' if attached else 'lost pre-main upload blocks PLC',
             'process restart original query',
             'independent upload reconciliation keeps original operator' if attached else 'explicit continuation original operator',
             'one execution per independent receiver'] if optional else []
    return checks, extra


def verify_execution(record, events, config):
    if (record.get('Target'), record.get('Windows'), record.get('RealEquipment'), record.get('Assurance')) != (
            'portable', 'NOT_RUN', 'BLOCKED', 'SIMULATION_REFERENCE_ONLY') or config.get('Mode') != 'Simulation':
        raise ValueError('Portable reference qualification cannot claim Windows or real equipment acceptance')
    command = record.get('Command')
    if not isinstance(command, list) or not command or any(not isinstance(v, str) for v in command):
        raise ValueError('Actual command must be a nonempty argument list')
    for key, flag in [('OptionalFeatures', '--optional-features'), ('AttachedUpload', '--attached-upload'), ('ExternalPeerProcess', '--peer-fixture')]:
        if type(record.get(key)) is not bool or record[key] != (flag in command) or command.count(flag) > 1:
            raise ValueError('Command execution mode differs from recorded scenario')
    if record['AttachedUpload'] and not record['OptionalFeatures'] or record['ExternalPeerProcess'] and not record['OptionalFeatures']:
        raise ValueError('Execution mode requires optional journey')
    checks, extra = journey_checks(record['Application'], record['OptionalFeatures'], record['AttachedUpload'])
    if record.get('Checks') != checks or record.get('OptionalChecks') != extra:
        raise ValueError('Claimed checks differ from the selected reference journey')
    bindings = [config.get('Wcs'), config.get('Plc'), (config.get('Images') or {}).get('Uploader')]
    endpoints = [(b.get('Parameters') or {}).get('endpoint') for b in bindings if b]
    peers = [e for e in events if e.get('Status') == 'ReferencePeerStarted']
    if not record['ExternalPeerProcess']:
        if any(endpoints) or peers:
            raise ValueError('Local reference execution cannot claim an external peer endpoint/process')
    else:
        if not endpoints or any(not ep or urlsplit(ep).scheme != 'http' or urlsplit(ep).hostname != '127.0.0.1' for ep in endpoints) or len(set(endpoints)) != 1:
            raise ValueError('External peer process must match every actual reference endpoint')
        if record['SchemaVersion'] >= 3:
            if len(peers) != 1:
                raise ValueError('Actual external peer process start evidence required')
            peer = peers[0]
            if type(peer.get('ProcessId')) is not int or peer['ProcessId'] <= 0 or peer.get('Endpoint') != endpoints[0] or peer.get('Health') != 'PARCEL-REFERENCE-v1' or not re.fullmatch('[0-9A-F]{64}', peer.get('FixtureSha256', '')):
                raise ValueError('External peer process identity/health differs from actual configuration')
            i = command.index('--peer-fixture')
            if i + 1 >= len(command) or peer.get('Fixture') != str((Path(record['WorkingDirectory']) / command[i+1]).resolve()) or events.index(peer) >= next(i for i,e in enumerate(events) if e.get('Status') == 'Started'):
                raise ValueError('External peer command identity/start order differs')


def verify_run_events(record, events_path):
    """Cross-check claims against the preserved input and actual process snapshots.

    This establishes consistency, not publisher trust: an independently trusted
    record/index is still needed to detect replacement of the entire evidence.
    """
    events = [json.loads(line)['Value'] for line in events_path.read_text().splitlines()]
    inputs = [event for event in events if event.get('Status') == 'CombinationInput']
    if record['SchemaVersion'] in (2, 3):
        if not events or len(inputs) != 1 or events[0] != dict(Status='CombinationInput', Binding=run_input(record)):
            raise ValueError('Recorded input/candidate identity differs from original run input event')
    elif inputs:
        raise ValueError('Run input event cannot be bypassed by record schema downgrade')
    snapshots = [event for event in events if isinstance(event.get('Configuration'), dict) and 'Effective' in event['Configuration']]
    if not snapshots:
        raise ValueError('No actual configuration snapshot in original run events')
    for snapshot in snapshots:
        if snapshot.get('ApplicationId') != record['Application'] or object_sha(snapshot['Configuration']['Effective']) != record['EffectiveConfigurationSha256']:
            raise ValueError('Recorded effective configuration differs from original run snapshot')
    starts = [event for event in events if event.get('Status') == 'Started']
    if len(starts) < 2 or any(event.get('Business') != record['Application'] for event in starts) or len({s.get('RuntimeId') for s in snapshots} - {None}) < 2:
        raise ValueError('Original run snapshots do not prove selected application restart')
    config = snapshots[0]['Configuration']['Effective']
    verify_execution(record, events, config)
    images = config.get('Images')
    optional = bool(config.get('Login') or config.get('Scale') or images)
    attached = bool(images and images.get('Strategy', 'BeforeMain') == 'Attached')
    if type(record.get('OptionalFeatures')) is not bool or type(record.get('AttachedUpload')) is not bool or record['OptionalFeatures'] != optional or record['AttachedUpload'] != attached:
        raise ValueError('Recorded scenario differs from original run snapshot')
    if optional and (not config.get('Login') or not config.get('Scale') or not images or config['Weight']['Source'] != 'Measured'):
        raise ValueError('Original run snapshot does not match optional journey scenario')
    operation = next((op for op in snapshots[-1].get('Operations', []) if op['Id'] == record.get('OperationId')), None)
    expected = 'SlotResolved' if record['Application'] == 'sportmaster' else 'PlcAccepted'
    if operation is None or operation.get('State') != record.get('State') or operation['State'] not in (expected, 'PhysicalCompleted'):
        raise ValueError('Recorded result differs from original final operation snapshot')
    if optional and ((operation.get('Capture') or {}).get('State') != 'Accepted' or (operation.get('Upload') or {}).get('State') != 'Accepted'):
        raise ValueError('Original result does not prove completed optional image journey')


class CombinationRun:
    def __init__(self, output, command, *, optional_features=False, attached_upload=False, external_peer_process=False):
        self.output = Path(output).resolve()
        self.events_path = self.output.with_suffix(self.output.suffix + '.events.jsonl')
        if self.events_path.exists(): raise FileExistsError('Evidence events already exist')
        self.record = dict(SchemaVersion=3, OptionalFeatures=optional_features, AttachedUpload=attached_upload, ExternalPeerProcess=external_peer_process, RunId=uuid.uuid4().hex, Status='RUNNING', StartedAt=utc(), Command=command, WorkingDirectory=str(Path.cwd()),
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
        checks, extra = journey_checks(release['ApplicationId'], self.record['OptionalFeatures'], self.record['AttachedUpload'])
        self.record.update(Checks=checks, OptionalChecks=extra)
        self.event(dict(Status='CombinationInput', Binding=run_input(self.record)))
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
        if error is None:
            verify_run_events(self.record, self.events_path)
        self.record.update(Status='FAIL' if error else 'PASS', FinishedAt=utc(), EventsSha256=sha(self.events_path), EventsFile=self.events_path.name)
        if error: self.record['Failure'] = dict(Type=type(error).__name__, Message=str(error))
        self.save()


def verify_record(record_path, root, effective_configuration=None, trusted_record_sha256=None, historical_audit=False):
    record_path, root = Path(record_path), Path(root)
    r = json.loads(record_path.read_bytes())
    if r.get('SchemaVersion') not in (1, 2, 3) or r.get('Status') != 'PASS': raise ValueError('Combination was not verified PASS')
    if r['SchemaVersion'] < 3 and not historical_audit:
        raise ValueError('Current qualification requires schema3; older records are historical audit only')
    if r['SchemaVersion'] < 3 and (not trusted_record_sha256 or sha(record_path) != trusted_record_sha256.upper()):
        raise ValueError('Legacy schema command/qualification was not input-bound; independently trusted original record SHA256 required')
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
    identity = json.loads((root/'ASSEMBLY.json').read_bytes())
    if identity.get('Target') != r['Target'] or identity.get('SourceCommit') != r['SourceCommit'] or identity.get('BusinessVersion') != r['BusinessVersion']:
        raise ValueError('Record execution target/business differs from actual candidate identity')
    providers = selected_providers(config)
    declaration = json.loads((root/'app/application-compatibility.json').read_bytes())
    if providers != r['Providers'] or require_combination(declaration, providers) != r['CompatibilitySet']: raise ValueError('Recorded provider combination differs')
    verify_run_events(r, record_path.parent/r['EventsFile'])
    return r


if __name__ == '__main__':
    import argparse
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--record', type=Path, required=True); p.add_argument('--package', type=Path, required=True)
    p.add_argument('--configuration', type=Path)
    p.add_argument('--historical-audit', action='store_true', help='Read-only audit of a trusted historical record; never current qualification')
    p.add_argument('--record-sha256', help='Original legacy record SHA256 from an independently trusted publication/index')
    a = p.parse_args()
    verified = verify_record(a.record, a.package, json.loads(a.configuration.read_bytes()) if a.configuration else None, a.record_sha256, historical_audit=a.historical_audit)
    print(json.dumps(dict(Status='HISTORICAL_AUDIT' if a.historical_audit else 'PASS', RunId=verified['RunId'], Application=verified['Application'], BusinessVersion=verified['BusinessVersion'])))
