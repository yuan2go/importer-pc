#!/usr/bin/env python3
"""Exercise newly unpacked core/selected Release media using their exact signed identities.

External review artifact. No repository or original medium is modified. All generated
runtime data, command/raw process logs and outcomes are created in a fresh output root.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path
import queue
import subprocess
import threading
import time
import traceback
import uuid
import zipfile


def sha(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', type=Path, required=True, help='Directory containing sport-core.zip and sport-selected.zip')
    parser.add_argument('--core-zip', type=Path)
    parser.add_argument('--selected-zip', type=Path)
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--trust', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, required=True, help='Must not exist; preserves original medium and previous evidence')
    args = parser.parse_args()
    base = args.base.resolve(strict=True)
    trust = args.trust.resolve(strict=True)
    out = args.output_dir.resolve()
    out.mkdir(parents=True, exist_ok=False)
    log = []
    sessions = []
    lock = None
    result = dict(Status='RUNNING', Windows='NOT_RUN', PhysicalIntegration='BLOCKED', Log=log)
    result['Inputs'] = dict(Base=str(base), TrustSha256=sha(trust), ScriptSha256=sha(Path(__file__).resolve()),
                           Dotnet=args.dotnet, Output=str(out))
    started = time.time()

    def write_result():
        result['ElapsedSeconds'] = round(time.time() - started, 3)
        (out / 'selected-journey.json').write_text(json.dumps(result, indent=2) + '\n')

    try:
        extracted = {}
        media = {'sport-core': args.core_zip or base / 'sport-core.zip',
                 'sport-selected': args.selected_zip or base / 'sport-selected.zip'}
        result['Media'] = {}
        for name, archive_path in media.items():
            archive_path = archive_path.resolve(strict=True)
            target = out / (name + '-unpacked')
            target.mkdir()
            with zipfile.ZipFile(archive_path) as archive:
                for info in archive.infolist():
                    destination = (target / info.filename).resolve()
                    assert destination.is_relative_to(target), 'Archive path escapes fresh output root'
                    assert info.external_attr >> 16 & 0o170000 != 0o120000, 'Archive links are refused'
                archive.extractall(target)
            sums = (target / 'SHA256SUMS').read_text().splitlines()
            for line in sums:
                digest, name_in_zip = line.split('  ', 1)
                assert sha(target / name_in_zip) == digest.lower(), name_in_zip
            result['Media'][name] = dict(File=str(archive_path), Sha256=sha(archive_path), VerifiedFiles=len(sums))
            extracted[name] = target
        root = extracted['sport-selected']
        lock = json.loads((root / 'composition.lock.json').read_text())
        core_lock = json.loads((extracted['sport-core'] / 'composition.lock.json').read_text())
        assert core_lock['offline_bundles'] == []
        assert not (extracted['sport-core'] / 'offline-extensions').exists()
        bundles = {item['Id']: item for item in lock['offline_bundles']}
        assert len(bundles) == 2 and set(bundles) == {'reference.barcode', 'reference.parcel-capabilities'}
        assert sorted(p.name for p in (root / 'offline-extensions').glob('*.ipcx')) == sorted(Path(x['File']).name for x in bundles.values())
        assert core_lock['SourceCommit'] == lock['SourceCommit']
        result.update(SourceCommit=lock['SourceCommit'], CorePackages=0, SelectedPackages=lock['offline_bundles'])
        run = out / 'selected-runtime'
        run.mkdir()
        config = run / 'config'
        config.mkdir()
        seed = json.loads((root / 'app/config/business.json').read_text())
        (config / 'business.json').write_text(json.dumps(seed))
        store = run / 'store'
        (config / 'extension-store.json').write_text(json.dumps({'Root': str(store), 'TrustFile': str(trust)}))
        app = root / 'app/Ipc.Assembly.dll'
        installed, modules = {}, {}

        def action(kind, **kwargs):
            command = [args.dotnet, str(app), '--action', kind, '--mode', 'Simulation',
                       '--extension-root', str(store), '--extension-trust', str(trust)]
            for key, value in kwargs.items():
                command += ['--' + key, str(value)]
            index = len(log)
            process = subprocess.run(command, text=True, capture_output=True, timeout=30)
            (run / f'action-{index:03d}-{kind}.stdout.log').write_text(process.stdout)
            (run / f'action-{index:03d}-{kind}.stderr.log').write_text(process.stderr)
            row = dict(Action=kind, ExitCode=process.returncode, Command=command)
            log.append(row)
            assert process.returncode == 0, row
            value = json.loads(process.stdout)
            row['Result'] = value
            return value

        for item in bundles.values():
            file = root / item['File']
            assert sha(file).upper() == item['PackageSha256'].upper()
            with zipfile.ZipFile(file) as archive:
                raw = archive.read('manifest.json')
                manifest = json.loads(raw)
                assert hashlib.sha256(raw).hexdigest().upper() == item['ManifestSha256'].upper()
                assert (manifest['Id'], manifest['Version']) == (item['Id'], item['Version'])
                modules[item['Id']] = manifest['Modules']
            record = action('extension-install', file=file, sha256=item['PackageSha256'])
            assert record['Status'] == 'Installed'
            installed[item['Id']] = record['Package']
            assert action('extension-recover', operation=record['OperationId'])['Package'] == record['Package']

        proc = messages = None

        def start():
            number = len(sessions)
            stderr = open(run / f'process-{number}.stderr.log', 'w')
            stdout = open(run / f'process-{number}.stdout.log', 'w')
            stdin = open(run / f'process-{number}.stdin.jsonl', 'w')
            process = subprocess.Popen([args.dotnet, str(app), '--config', str(config), '--data-root', str(run / 'data')],
                                       stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=stderr, text=True)
            q = queue.Queue()
            session = dict(Process=process, Stderr=stderr, Stdout=stdout, Stdin=stdin)
            sessions.append(session)

            def reader():
                buffer = ''
                for line in process.stdout:
                    stdout.write(line)
                    stdout.flush()
                    buffer += line
                    try:
                        value = json.loads(buffer)
                    except json.JSONDecodeError:
                        continue
                    q.put(value)
                    buffer = ''
            thread = threading.Thread(target=reader, daemon=True)
            session['Thread'] = thread
            thread.start()
            assert q.get(timeout=20)['Status'] == 'Started'
            return process, q

        def send(value):
            text = value if isinstance(value, str) else json.dumps(value)
            sessions[-1]['Stdin'].write(text + '\n')
            sessions[-1]['Stdin'].flush()
            proc.stdin.write(text + '\n')
            proc.stdin.flush()
            return messages.get(timeout=20)

        def snap():
            return send('snapshot')

        def cmd(kind, owner=None, value=None, revision=None):
            snapshot = snap()
            request = dict(Id=uuid.uuid4().hex, ExpectedRevision=snapshot['Revision'] if revision is None else revision,
                           Kind=kind, Owner=owner, Value=value)
            response = send(request)
            log.append(dict(Command=kind, Request=request, Result=response))
            return response

        def maintenance():
            assert cmd('Pause')['Status'] == 'Persisted'
            assert cmd('AuthorizeMaintenance', 'maintenance', 'SIMULATION_ONLY')['Status'] == 'Authorized'

        def stop():
            sessions[-1]['Stdin'].write('quit\n')
            sessions[-1]['Stdin'].flush()
            proc.stdin.write('quit\n')
            proc.stdin.flush()
            proc.wait(timeout=20)
            sessions[-1]['Thread'].join(timeout=3)
            assert proc.returncode == 0
            log.append(dict(ProcessExit=proc.returncode))

        proc, messages = start()
        initial = snap()
        assert initial['Configuration']['Confirmation']['Source'] == 'Seed'
        assert initial['Configuration']['Confirmation']['Required'] is False
        assert initial['Configuration']['Status'] == 'Effective'
        result['SeedConfirmedOnlyAfterSuccessfulResources'] = True
        maintenance()
        current = snap()
        candidate = copy.deepcopy(current['Configuration']['Effective'])
        barcode = installed['reference.barcode']
        cap = installed['reference.parcel-capabilities']
        params = {x['Key']: x['Default'] for x in modules[barcode['Id']][0]['Parameters'] if x.get('Default') is not None}
        instances = []
        for i, reader in enumerate(candidate['Topology']['Readers']):
            name = 'selected.reader.' + str(i)
            reader['Driver'] = 'extension:' + name
            instances.append(dict(Id=name, PackageId=barcode['Id'], ModuleId='barcode-source', DeviceId=reader['DeviceId'],
                                  Enabled=True, Parameters=copy.deepcopy(params)))
        candidate['Reader'] = copy.deepcopy(candidate['Topology']['Readers'][0])
        module = next(m for m in modules[cap['Id']] if m['Id'] == 'parcel-integration')
        candidate['Wcs'] = dict(Kind='integration', Implementation='extension:' + cap['Id'] + '/parcel-integration', Version=cap['Version'],
                                InstanceId=seed['Wcs']['InstanceId'], Target='reference://selected/wcs', Contract=module['ContractIdentity'],
                                Parameters={'peer-root': str(run / 'external-peer'), 'slot': 'SELECTED-07'}, Package=cap, Module='parcel-integration', Enabled=True)
        if candidate.get('CapabilityInstances') is not None:
            candidate['CapabilityInstances'] = [candidate['Wcs'] if x['InstanceId'] == candidate['Wcs']['InstanceId'] else x for x in candidate['CapabilityInstances']]
        candidate['Extensions'] = dict(Packages=[barcode, cap], Instances=instances)
        assert cmd('ValidateConfiguration', value=json.dumps(candidate))['Status'] == 'Validated'
        assert cmd('StageConfiguration', 'Selected signed independent readers and WCS from unpacked medium', json.dumps(candidate), current['Configuration']['Revision'])['Status'] == 'PendingRestart'
        assert cmd('ManageExtension', 'extension-uninstall', json.dumps(cap))['Status'] == 'Rejected'
        stop()
        proc, messages = start()
        activated = snap()
        assert activated['Configuration']['Status'] == 'Effective' and activated['Configuration']['Pending'] is None
        assert activated['Configuration']['Confirmation']['Required'] is False
        assert activated['Configuration']['Effective']['Extensions']['Packages'] == [barcode, cap]
        maintenance()
        assert cmd('ManageExtension', 'extension-uninstall', json.dumps(cap))['Status'] == 'Rejected'
        assert cmd('ReconnectDevice', candidate['Wcs']['InstanceId'])['Status'] == 'Reconnected'
        for _ in range(100):
            ready = snap()
            if ready['Availability']['Resume']:
                break
            time.sleep(.03)
        assert cmd('Resume')['Status'] == 'Persisted'
        for position in ['left', 'right']:
            assert cmd('EmitReferenceAt', 'selected-' + position, json.dumps({'PositionId': position, 'Code': '000' + position}))['Status'] == 'Enqueued'
            for _ in range(150):
                current = snap()
                found = [o for o in current['Operations'] if o['AdmissionId'] == 'selected-' + position]
                if found and found[0]['State'] == 'SlotResolved':
                    break
                time.sleep(.03)
            else:
                raise AssertionError(current)
            assert found[0]['Slot'] == 'SELECTED-07' and found[0]['Wcs']['Request']['Binding']['Package'] == cap and found[0]['Wcs']['Attempts'] == 1
        assert {o['PositionId'] for o in current['Operations']} == {'left', 'right'}
        result['BeforeStartupFailure'] = current
        maintenance()
        current = snap()
        good_effective = copy.deepcopy(current['Configuration']['Effective'])
        faulty = copy.deepcopy(good_effective)
        faulty['Extensions']['Instances'][0]['Parameters']['fault.fail-start'] = 'true'
        assert cmd('StageConfiguration', 'Explicit synthetic reader startup failure recovery', json.dumps(faulty), current['Configuration']['Revision'])['Status'] == 'PendingRestart'
        stop()
        failed = subprocess.run([args.dotnet, str(app), '--config', str(config), '--data-root', str(run / 'data')],
                                input='quit\n', capture_output=True, text=True, timeout=25)
        (run / 'expected-failed-start.stdout.log').write_text(failed.stdout)
        (run / 'expected-failed-start.stderr.log').write_text(failed.stderr)
        assert failed.returncode != 0
        authority = run / 'data' / seed['Mode'] / seed['StationId'] / seed['ApplicationId'] / 'configuration.json'
        state = json.loads(authority.read_text())
        assert state['Status'] == 'ActivationFailed' and state['Effective'] == good_effective and state['Pending'] == faulty
        result['ExpectedStartupFailure'] = dict(ExitCode=failed.returncode, Configuration=state)
        proc, messages = start()
        recovered = snap()
        assert recovered['Configuration']['Status'] == 'ActivationFailed' and recovered['Configuration']['Pending'] == faulty
        assert recovered['Configuration']['Effective'] == good_effective
        maintenance()
        current = snap()
        assert cmd('StageConfiguration', 'Remove settled selected bindings through full revision', json.dumps(seed), current['Configuration']['Revision'])['Status'] == 'PendingRestart'
        stop()
        proc, messages = start()
        maintenance()
        assert snap()['Configuration']['Effective']['Extensions'] is None
        for package in [cap, barcode]:
            assert cmd('ManageExtension', 'extension-uninstall', json.dumps(package))['Status'] == 'Uninstalled'
        history = cmd('History')
        assert len(history['Items']) == 2 and all(x['Operation']['Wcs']['Attempts'] == 1 for x in history['Items'])
        stop()
        assert sha(trust) == result['Inputs']['TrustSha256']
        result.update(Status='PASS', InstalledActivatedRetired=True, Positions=['left', 'right'],
                      FailedStartupRetainedPriorEffective=True, OriginalWcsAttempts=[1, 1], SelectedServiceReconnect=True)
        print('PASS selected Release: dynamic exact packages, two independent signed readers/WCS, effective/pending guards, startup failure preserves prior, safe unbind/retirement')
    except BaseException as error:
        result.update(Status='FAIL', Failure=repr(error), Traceback=traceback.format_exc())
        raise
    finally:
        for session in sessions:
            process = session['Process']
            if process.poll() is None:
                try:
                    process.stdin.write('quit\n')
                    process.stdin.flush()
                    process.wait(timeout=10)
                except Exception:
                    process.kill()
                    process.wait(timeout=5)
            session['Thread'].join(timeout=2)
            for name in ['Stderr', 'Stdout', 'Stdin']:
                session[name].close()
        write_result()


if __name__ == '__main__':
    main()
