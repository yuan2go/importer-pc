"""Evidence custody and version mismatches, independent of the journey assertions."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from application_compatibility import CombinationRun, object_sha, read_business, require_combination, selected_providers, sha, verify_record
from composition import resolve


class CompatibilityTests(unittest.TestCase):
    def completed_run(self, output, root, config, identity):
        optional=bool(config.get('Images'))
        attached=bool(optional and config['Images'].get('Strategy','BeforeMain')=='Attached')
        command=['actual-smoke']+(['--optional-features'] if optional else [])+(['--attached-upload'] if attached else [])
        run = CombinationRun(output, command, optional_features=optional, attached_upload=attached)
        run.bind(root, identity, config)
        run.effective(config)
        operation={'Id':'operation-one','State':'PlcAccepted'}
        if optional: operation.update(Capture={'State':'Accepted'},Upload={'State':'Accepted'})
        for runtime in ['before-restart', 'after-restart']:
            run.event({'Status': 'Started', 'Business': config['ApplicationId']})
            run.event({'ApplicationId': config['ApplicationId'], 'RuntimeId': runtime,
                       'Configuration': {'Effective': config},
                       'Operations': [operation]})
        run.finish({'OptionalFeatures': optional, 'AttachedUpload': attached,
                    'OperationId': 'operation-one', 'State': 'PlcAccepted'})
        return run

    def test_business_version_matches_every_declared_composition(self):
        root = Path(__file__).resolve().parent.parent
        for application in ['sportmaster', 'direct-plc', 'wcs-to-plc']:
            selected = resolve(root/'config/compositions.json', application+'-reference')
            version, policy = read_business(root, application, selected)
            self.assertEqual(version['BusinessVersion'], '1.2.4')
            self.assertEqual(version['CompatibilitySha256'], sha(policy))
            selected['components'][-2]['version'] = '9.0.0'
            # Change the actual business declaration regardless of topological order.
            next(c for c in selected['components'] if c['id'] == 'application.'+application)['version'] = '9.0.0'
            with self.assertRaisesRegex(ValueError, 'version'): read_business(root, application, selected)

    def fixture(self, root, config=None):
        if config is None: config = json.loads((Path(__file__).resolve().parent.parent/'config/direct-plc/business.json').read_bytes())
        policy = json.loads((Path(__file__).resolve().parent.parent/'config/compatibility/direct-plc.json').read_bytes())
        app = root/'app'; app.mkdir()
        (app/'application-compatibility.json').write_text(json.dumps(policy))
        (app/'business.dll').write_text('fixed binary')
        release = dict(SchemaVersion=2, ApplicationId='direct-plc', BusinessVersion='1.2.4', BusinessAssembly='business.dll', SourceCommit='a'*40, CompatibilitySha256=sha(app/'application-compatibility.json'))
        (app/'application-release.json').write_text(json.dumps(release))
        identity = dict(release, Target='portable')
        (root/'ASSEMBLY.json').write_text(json.dumps(identity))
        (root/'SHA256SUMS').write_text(''.join(sha(f)+'  '+f.relative_to(root).as_posix()+'\n' for f in [*app.iterdir(), root/'ASSEMBLY.json']))
        return config, policy, identity

    def test_exact_candidate_configuration_events_and_provider_identity(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary); root=base/'package'; root.mkdir()
            config, policy, identity = self.fixture(root)
            output=base/'run.json'
            run=self.completed_run(output, root, config, identity)
            r=verify_record(output, root, config)
            self.assertEqual(r['Status'],'PASS')
            changed=copy.deepcopy(config); changed['Plc']['Version']='2.0.1'
            with self.assertRaises(ValueError): verify_record(output, root, changed)
            with self.assertRaises(ValueError): require_combination(policy, selected_providers(changed))
            with self.assertRaises(FileExistsError): CombinationRun(output,['another-run'])
            original=(app_binary:=root/'app/business.dll').read_bytes(); app_binary.write_text('different bytes')
            with self.assertRaisesRegex(ValueError,'file changed'): verify_record(output,root)
            app_binary.write_bytes(original)
            run.events_path.write_text('rewritten run events')
            with self.assertRaisesRegex(ValueError,'events differ'): verify_record(output,root)

    def test_failure_and_interruption_never_become_pass(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); output=base/'run.json'
            run=CombinationRun(output,['failed-start'])
            with self.assertRaises(ValueError): verify_record(output,base)
            run.event({'Status':'startup failed'}); run.finish(error=ValueError('wrong business version'))
            r=json.loads(output.read_bytes())
            self.assertEqual(r['Status'],'FAIL'); self.assertIn('wrong business',r['Failure']['Message'])
            self.assertEqual(r['EventsSha256'],sha(run.events_path))
            with self.assertRaises(ValueError): verify_record(output,base)

    def test_configuration_and_business_record_tampering_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, policy, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output, root, config, identity)
            original=json.loads(output.read_bytes())
            for key,value in [('BusinessVersion','2.0.1'),('Providers',[]),('CompatibilitySet','other')]:
                altered=dict(original); altered[key]=value; output.write_text(json.dumps(altered))
                with self.assertRaises(ValueError): verify_record(output,root)
            altered=copy.deepcopy(original); altered['EffectiveConfiguration']['Plc']['Target']='reference://other'
            output.write_text(json.dumps(altered))
            with self.assertRaises(ValueError): verify_record(output,root)

    def test_self_consistent_configuration_relabelling_cannot_reuse_events(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, policy, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output, root, config, identity)
            r=json.loads(output.read_bytes())
            original_events=(base/r['EventsFile']).read_bytes()
            for key in ['InputConfiguration', 'EffectiveConfiguration']:
                r[key]['Plc']['Target']='reference://not-executed'
                r[key+'Sha256']=object_sha(r[key])
            r['Providers']=selected_providers(r['EffectiveConfiguration'])
            r['CompatibilitySet']=require_combination(policy,r['Providers'])
            output.write_text(json.dumps(r))
            self.assertEqual((base/r['EventsFile']).read_bytes(),original_events)
            with self.assertRaisesRegex(ValueError,'input|snapshot'): verify_record(output,root,r['EffectiveConfiguration'])

    def test_scenario_and_result_must_be_observed(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output, root, config, identity)
            original=json.loads(output.read_bytes())
            for key,value in [('OptionalFeatures',True),('AttachedUpload',True),('OperationId','not-executed'),('State','PhysicalCompleted')]:
                altered=copy.deepcopy(original); altered[key]=value
                output.write_text(json.dumps(altered))
                with self.subTest(field=key):
                    with self.assertRaisesRegex(ValueError,'input|scenario|result'): verify_record(output,root)

    def test_original_snapshot_is_required_even_with_rehashed_events(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output, root, config, identity)
            r=json.loads(output.read_bytes()); events=base/r['EventsFile']
            rows=[json.loads(line) for line in events.read_text().splitlines()]
            events.write_text('\n'.join(json.dumps(row) for row in rows if 'Configuration' not in row['Value'])+'\n')
            r['EventsSha256']=sha(events); output.write_text(json.dumps(r))
            with self.assertRaisesRegex(ValueError,'snapshot'): verify_record(output,root)

    def test_finish_does_not_issue_pass_without_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; run=CombinationRun(output,['smoke'])
            run.bind(root,identity,config); run.effective(config); run.event({'Status':'Started'})
            with self.assertRaisesRegex(ValueError,'snapshot'):
                run.finish({'OptionalFeatures':False,'AttachedUpload':False,'OperationId':'not-observed','State':'PlcAccepted'})
            self.assertNotEqual(json.loads(output.read_bytes())['Status'],'PASS')

    def test_input_event_cannot_be_dropped_by_record_schema_downgrade(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output,root,config,identity)
            r=json.loads(output.read_bytes()); self.assertEqual(r['SchemaVersion'],3)
            r['SchemaVersion']=1; output.write_text(json.dumps(r))
            with self.assertRaisesRegex(ValueError,'schema'): verify_record(output,root)

    def test_legacy_record_keeps_original_bytes_and_requires_matching_snapshot(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output,root,config,identity)
            r=json.loads(output.read_bytes()); events=base/r['EventsFile']
            rows=[json.loads(line) for line in events.read_text().splitlines()]
            events.write_text('\n'.join(json.dumps(row) for row in rows if row['Value'].get('Status')!='CombinationInput')+'\n')
            r['SchemaVersion']=1; r['EventsSha256']=sha(events); output.write_text(json.dumps(r))
            original=output.read_bytes()
            with self.assertRaisesRegex(ValueError,'trusted'): verify_record(output,root)
            verify_record(output,root,trusted_record_sha256=sha(output)); self.assertEqual(output.read_bytes(),original)
            r['EffectiveConfiguration']['Plc']['Target']='reference://another'
            r['EffectiveConfigurationSha256']=object_sha(r['EffectiveConfiguration']); r['Providers']=selected_providers(r['EffectiveConfiguration'])
            output.write_text(json.dumps(r))
            with self.assertRaisesRegex(ValueError,'snapshot'): verify_record(output,root,trusted_record_sha256=sha(output))

    def test_schema3_preserves_valid_before_main_and_attached_journeys(self):
        config=json.loads((Path(__file__).resolve().parent.parent/'config/optional/direct-plc/business.json').read_bytes())
        for strategy in ['BeforeMain','Attached']:
            with self.subTest(strategy=strategy), tempfile.TemporaryDirectory() as temporary:
                base=Path(temporary); root=base/'package'; root.mkdir()
                selected=copy.deepcopy(config); selected['Images']['Strategy']=strategy
                selected, _, identity=self.fixture(root,selected)
                output=base/'run.json'; self.completed_run(output,root,selected,identity)
                self.assertEqual(verify_record(output,root,selected)['AttachedUpload'],strategy=='Attached')

    def test_input_and_run_identity_cannot_be_relabelled_independently(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output,root,config,identity)
            original=json.loads(output.read_bytes())
            altered=copy.deepcopy(original); altered['InputConfiguration']['StationId']='another-input'
            altered['InputConfigurationSha256']=object_sha(altered['InputConfiguration'])
            for r in [altered,dict(original,RunId='another-run')]:
                output.write_text(json.dumps(r))
                with self.assertRaisesRegex(ValueError,'input'): verify_record(output,root)

    def test_execution_command_qualification_and_checks_are_input_bound(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output,root,config,identity)
            original=json.loads(output.read_bytes()); events=(base/original['EventsFile']).read_bytes()
            attacks=[dict(original,ExternalPeerProcess=True,Command=original['Command']+['--peer-fixture','not-executed.dll']),
                     dict(original,Command=['another-command']),dict(original,Windows='PASS'),
                     dict(original,RealEquipment='PASS'),dict(original,Assurance='PRODUCTION_ACCEPTANCE'),
                     dict(original,Checks=original['Checks']+['unexecuted check']),
                     dict(original,OptionalChecks=['unexecuted optional check'])]
            for changed in attacks:
                output.write_text(json.dumps(changed))
                with self.assertRaisesRegex(ValueError,'input'): verify_record(output,root)
                self.assertEqual((base/original['EventsFile']).read_bytes(),events)

    def test_peer_and_qualification_cannot_be_forged_even_with_rebound_input(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output,root,config,identity)
            original=json.loads(output.read_bytes()); events=base/original['EventsFile']
            original_events=events.read_text()
            for changed in [dict(original,ExternalPeerProcess=True,Command=original['Command']+['--peer-fixture','not-executed.dll']),
                            dict(original,Windows='PASS'),dict(original,Assurance='PRODUCTION_ACCEPTANCE')]:
                # An adversary rehashes the metadata event, while actual snapshots remain unchanged.
                rows=[json.loads(line) for line in original_events.splitlines()]
                rows[0]['Value']['Binding']=__import__('application_compatibility').run_input(changed)
                events.write_text(''.join(json.dumps(row)+'\n' for row in rows));changed['EventsSha256']=sha(events)
                output.write_text(json.dumps(changed))
                with self.assertRaisesRegex(ValueError,'qualification|Command|endpoint|mode'): verify_record(output,root)

    def test_schema2_requires_trusted_original_record_hash(self):
        with tempfile.TemporaryDirectory() as temporary:
            base=Path(temporary); root=base/'package'; root.mkdir()
            config, _, identity=self.fixture(root)
            output=base/'run.json'; self.completed_run(output,root,config,identity)
            record=json.loads(output.read_bytes());record['SchemaVersion']=2
            rows=[json.loads(line) for line in (base/record['EventsFile']).read_text().splitlines()]
            rows[0]['Value']['Binding']=__import__('application_compatibility').run_input(record)
            (base/record['EventsFile']).write_text(''.join(json.dumps(row)+'\n' for row in rows))
            record['EventsSha256']=sha(base/record['EventsFile']);output.write_text(json.dumps(record))
            anchor=sha(output);original=output.read_bytes()
            with self.assertRaisesRegex(ValueError,'trusted'):verify_record(output,root)
            verify_record(output,root,trusted_record_sha256=anchor)
            record['Command']=['not-executed'];output.write_text(json.dumps(record))
            with self.assertRaisesRegex(ValueError,'trusted'):verify_record(output,root,trusted_record_sha256=anchor)
            self.assertNotEqual(output.read_bytes(),original)


if __name__ == '__main__': unittest.main()
