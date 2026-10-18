"""Focused tests for declaration custody and fail-closed publication (no runtime simulation evidence)."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import extension_build as builder


class ExtensionBuildTests(unittest.TestCase):
    def test_typed_provider_requirements_are_bounded_and_not_arbitrary_metadata(self):
        module = dict(Id='camera', Assembly='Provider.dll', EntryType='Provider.Camera', Kind='camera', MaximumInstances=1,
                      ContractIdentity='CAPTURE-v1', Requirements={'Query': True, 'ImageAddress': False})
        builder.validate_module(module)
        builder.validate_module(dict(module, Assurance=dict(SchemaVersion=1, Simulation=False, ProtocolEvidence='A' * 64), Requirements=dict(PhysicalConfirmation=True, CompletesOnSubmit=True, BusinessAcceptance=True)))
        with self.assertRaises(ValueError): builder.validate_module(dict(module, Assurance=dict(SchemaVersion=2, Simulation=False, ProtocolEvidence='test')))
        with self.assertRaises(ValueError): builder.validate_module(dict(module, Requirements=dict(CompletesOnSubmit=True)))
        for patch_value in [{'Requirements': {'Query': 'true'}}, {'Requirements': {'AnyCommand': True}}, {'ContractIdentity': 'x' * 129}, {'Parameters': []}]:
            with self.assertRaises(ValueError): builder.validate_module(dict(module, **patch_value))

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='ipc-build-tool-test-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name).resolve()
        self.repo = self.root / 'independent'
        self.repo.mkdir()
        subprocess.run(['git', 'init', '-q', str(self.repo)], check=True)
        self.files = {
            'plugin.csproj': '<Project />', 'plugin.cs': '// independent committed source',
            'manifest.json': json.dumps(dict(SchemaVersion=1, Id='example.external', Version='2.0.0', Runtime='net10.0', Rid='any', ContractMajor=1, Mode='Simulation', Dependencies=[], Prerequisites=[])),
            'modules.json': json.dumps([dict(Id='source', Assembly='External.dll', EntryType='External.Source', Kind='observation', MaximumInstances=2)]),
            'schema.json': '[]',
        }
        self.definition = dict(SchemaVersion=1, Project='plugin.csproj', SourceRoots=['.'], BuildInputs=[], Manifest='manifest.json', Modules='modules.json', ParameterSchemas={'source':'schema.json'}, Content=[dict(Root='build', Source='External.dll', Path='External.dll')], PublicContracts=[])
        self.files['extension.build.json'] = json.dumps(self.definition)
        for name, content in self.files.items():
            (self.repo / name).write_text(content)
        self.commit()
        self.host = self.root / 'host'
        self.host.mkdir()
        (self.host / 'Ipc.Platform.dll').write_bytes(b'frozen-public-contract')
        (self.host / 'Ipc.Product.dll').write_bytes(b'frozen-static-checker')
        self.trust = self.root / 'trust.json'
        self.trust.write_text(json.dumps(dict(SchemaVersion=1, Publishers=[dict(Id='test-only', PublicKeyPem='PUBLIC KEY', TestOnly=True)])))
        self.key = self.root / 'private.key'
        self.key.write_text('test placeholder; never distributed')
        self.args = argparse.Namespace(definition=self.repo / 'extension.build.json', contract=self.host / 'Ipc.Platform.dll', check_host=None, output=self.root / 'external.ipcx', key=self.key, trust=self.trust, publisher='test-only', version=None, dependency_package=[], dotnet='unit-test-dotnet')

    def commit(self):
        subprocess.run(['git', 'add', '.'], cwd=self.repo, check=True)
        subprocess.run(['git', '-c', 'user.name=Build Test', '-c', 'user.email=build@example.invalid', 'commit', '-qm', 'committed independent inputs'], cwd=self.repo, check=True)

    def fake_run(self, command, cwd=None):
        # Only compiler/checker process boundaries are mocked. Tests do not claim
        # that these placeholder DLLs satisfy real managed metadata acceptance.
        if 'build' in command:
            out = Path(command[command.index('-o') + 1]); out.mkdir()
            (out / 'External.dll').write_bytes(b'unit-test-binary')
        if '-sign' in command:
            Path(command[command.index('-out') + 1]).write_bytes(b'unit-test-signature')

    def test_external_git_snapshot_and_identity_stable(self):
        one = builder.source_snapshot(self.args.definition)
        two = builder.source_snapshot(self.args.definition)
        self.assertEqual(one, two)
        self.assertEqual(set(one[1]), set(self.files))
        self.assertRegex(one[2], r'^[a-f0-9]{40}$')
        self.assertNotIn('src/Ipc.Platform', ''.join(one[1]))

    def test_dirty_and_untracked_inputs_rejected(self):
        (self.repo / 'schema.json').write_text('[{}]')
        with self.assertRaisesRegex(ValueError, 'clean committed'):
            builder.source_snapshot(self.args.definition)
        (self.repo / 'schema.json').write_text('[]')
        (self.repo / 'new.cs').write_text('// uncommitted')
        with self.assertRaisesRegex(ValueError, 'clean committed'):
            builder.source_snapshot(self.args.definition)

    def test_source_and_payload_escape_links_rejected(self):
        with self.assertRaises(ValueError): builder.contained(self.repo, '../private.key')
        (self.repo / 'linked').symlink_to(self.key)
        with self.assertRaises(ValueError): builder.contained(self.repo, 'linked')
        for path in ['../x.dll', 'C:/x.dll', 'dir\\x.dll', 'con.txt', 'a./x.dll', 'manifest.json', 'dir//x.dll']:
            with self.subTest(path=path), self.assertRaises(ValueError): builder.validate_payload_path(path)

    def test_unknown_declaration_and_uncommitted_metadata_rejected(self):
        self.definition['Modules'] = 'missing.json'
        (self.repo / 'extension.build.json').write_text(json.dumps(self.definition)); self.commit()
        with self.assertRaisesRegex(ValueError, 'committed source snapshot'):
            builder.source_snapshot(self.args.definition)

    def test_testonly_simulation_can_publish_with_process_boundaries_mocked(self):
        with patch.object(builder, 'run', side_effect=self.fake_run):
            result = builder.build(self.args)
        self.assertTrue(self.args.output.exists())
        self.assertEqual(result['Mode'], 'Simulation')
        self.assertEqual(result['PluginSourceCommit'], builder.source_snapshot(self.args.definition)[2])
        self.assertNotIn(self.key.name, result['PayloadSha256'])
        with patch.object(builder, 'run', side_effect=self.fake_run), self.assertRaises(FileExistsError):
            builder.build(self.args)

    def test_production_testonly_rejected_before_compilation(self):
        path = self.repo / 'manifest.json'; manifest=json.loads(path.read_text()); manifest['Mode']='Production'; path.write_text(json.dumps(manifest)); self.commit()
        with patch.object(builder, 'run') as run, self.assertRaisesRegex(ValueError, 'TestOnly'):
            builder.build(self.args)
        run.assert_not_called()
        self.assertFalse(self.args.output.exists())

    def test_build_failure_and_cancellation_leave_no_success_artifact(self):
        for exception in [subprocess.CalledProcessError(1, 'compiler'), KeyboardInterrupt()]:
            with self.subTest(exception=type(exception).__name__), patch.object(builder, 'run', side_effect=exception), self.assertRaises(type(exception)):
                builder.build(self.args)
            self.assertFalse(self.args.output.exists())
            self.assertFalse(Path(str(self.args.output) + '.identity.json').exists())

    def test_checker_failure_and_frozen_host_drift_never_publish(self):
        def reject(command, cwd=None):
            self.fake_run(command, cwd)
            if 'extension-check' in command:
                raise subprocess.CalledProcessError(2, command)
        with patch.object(builder, 'run', side_effect=reject), self.assertRaises(subprocess.CalledProcessError):
            builder.build(self.args)
        self.assertFalse(self.args.output.exists())
        def drift(command, cwd=None):
            self.fake_run(command, cwd)
            if 'extension-check' in command:
                (self.host / 'Ipc.Platform.dll').write_bytes(b'host-was-mutated')
        with patch.object(builder, 'run', side_effect=drift), self.assertRaisesRegex(ValueError, 'Frozen host changed'):
            builder.build(self.args)
        self.assertFalse(self.args.output.exists())

    def test_undeclared_private_dependency_and_duplicate_payload_rejected(self):
        def dependency(command, cwd=None):
            self.fake_run(command, cwd)
            if 'build' in command:
                (Path(command[command.index('-o') + 1]) / 'PrivateDependency.dll').write_bytes(b'unowned')
        with patch.object(builder, 'run', side_effect=dependency), self.assertRaisesRegex(ValueError, 'private dependency'):
            builder.build(self.args)
        self.assertFalse(self.args.output.exists())
        self.definition['Content'].append(dict(Root='build', Source='External.dll', Path='external.dll'))
        (self.repo / 'extension.build.json').write_text(json.dumps(self.definition)); self.commit()
        with patch.object(builder, 'run', side_effect=self.fake_run), self.assertRaisesRegex(ValueError, 'Duplicate'):
            builder.build(self.args)

    def test_cross_target_checker_requires_byte_identical_public_contracts(self):
        checker = self.root / 'portable-checker'; checker.mkdir()
        (checker / 'Ipc.Product.dll').write_bytes(b'runnable portable checker')
        (checker / 'Ipc.Platform.dll').write_bytes(b'changed-public-contract')
        self.args.check_host = checker / 'Ipc.Product.dll'
        with patch.object(builder, 'run') as run, self.assertRaisesRegex(ValueError, 'differs from frozen target'):
            builder.build(self.args)
        run.assert_not_called()
        (checker / 'Ipc.Platform.dll').write_bytes((self.host / 'Ipc.Platform.dll').read_bytes())
        with patch.object(builder, 'run', side_effect=self.fake_run):
            result = builder.build(self.args)
        self.assertTrue(result['FrozenHostUnchanged'])
        self.assertTrue(result['FrozenCheckerUnchanged'])

    def test_atomic_publication_failure_rolls_back_created_identity(self):
        original = builder.os.link
        calls = 0
        def fail_second(source, destination):
            nonlocal calls
            calls += 1
            if calls == 2: raise OSError('disk/publication failure')
            return original(source, destination)
        with patch.object(builder, 'run', side_effect=self.fake_run), patch.object(builder.os, 'link', side_effect=fail_second), self.assertRaises(OSError):
            builder.build(self.args)
        self.assertFalse(self.args.output.exists())
        self.assertFalse(Path(str(self.args.output) + '.identity.json').exists())
        self.assertEqual(list(self.root.glob('.ipc-publish-*')), [])


if __name__ == '__main__': unittest.main()
