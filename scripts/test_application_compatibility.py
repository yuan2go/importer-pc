"""Evidence custody and version mismatches, independent of the journey assertions."""
import copy
import json
from pathlib import Path
import tempfile
import unittest
from application_compatibility import CombinationRun, object_sha, read_business, require_combination, selected_providers, sha, verify_record
from composition import resolve


class CompatibilityTests(unittest.TestCase):
    def test_business_version_matches_every_declared_composition(self):
        root = Path(__file__).resolve().parent.parent
        for application in ['sportmaster', 'direct-plc', 'wcs-to-plc']:
            selected = resolve(root/'config/compositions.json', application+'-reference')
            version, policy = read_business(root, application, selected)
            self.assertEqual(version['BusinessVersion'], '1.1.0')
            self.assertEqual(version['CompatibilitySha256'], sha(policy))
            selected['components'][-2]['version'] = '9.0.0'
            # Change the actual business declaration regardless of topological order.
            next(c for c in selected['components'] if c['id'] == 'application.'+application)['version'] = '9.0.0'
            with self.assertRaisesRegex(ValueError, 'version'): read_business(root, application, selected)

    def fixture(self, root):
        config = json.loads((Path(__file__).resolve().parent.parent/'config/direct-plc/business.json').read_bytes())
        policy = json.loads((Path(__file__).resolve().parent.parent/'config/compatibility/direct-plc.json').read_bytes())
        app = root/'app'; app.mkdir()
        (app/'application-compatibility.json').write_text(json.dumps(policy))
        (app/'business.dll').write_text('fixed binary')
        release = dict(SchemaVersion=2, ApplicationId='direct-plc', BusinessVersion='1.1.0', BusinessAssembly='business.dll', SourceCommit='a'*40, CompatibilitySha256=sha(app/'application-compatibility.json'))
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
            run=CombinationRun(output, ['actual-smoke'])
            run.bind(root, identity, config); run.effective(config); run.event({'Status':'Started'}); run.finish({'Checks':['actual journey']})
            r=verify_record(output, root, config)
            self.assertEqual(r['Status'],'PASS')
            changed=copy.deepcopy(config); changed['Plc']['Version']='2.0.0'
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
            output=base/'run.json'; run=CombinationRun(output,['smoke'])
            run.bind(root,identity,config); run.effective(config); run.event({'Status':'Started'}); run.finish({'Checks':['journey']})
            original=json.loads(output.read_bytes())
            for key,value in [('BusinessVersion','2.0.0'),('Providers',[]),('CompatibilitySet','other')]:
                altered=dict(original); altered[key]=value; output.write_text(json.dumps(altered))
                with self.assertRaises(ValueError): verify_record(output,root)
            altered=copy.deepcopy(original); altered['EffectiveConfiguration']['Plc']['Target']='reference://other'
            output.write_text(json.dumps(altered))
            with self.assertRaises(ValueError): verify_record(output,root)


if __name__ == '__main__': unittest.main()
