import hashlib
from pathlib import Path
import tempfile
import unittest
from application_compatibility import read_deployment_compatibility, read_storage_format


class RuntimeCompatibility(unittest.TestCase):
    def test_manifest_binds_current_readers_and_exact_dependency_bytes(self):
        # Distinct synthetic declarations verify the authority for each field;
        # updating a real protocol must not require a second version table here.
        declarations = [
            ('DataFormat', 'src/Ipc.Application.ParcelFlow/ParcelRepository.cs', 'CurrentStorageVersion', 17),
            ('ConfigurationSchema', 'src/Ipc.Application.ParcelFlow/ParcelConfigurationRules.cs', 'CurrentConfigurationSchema', 19),
            ('CredentialSchema', 'src/Ipc.Platform/LocalManagementMode.cs', 'CredentialSchemaVersion', 23),
            ('ManagementProtocol', 'src/Ipc.Platform/ManagementModeBroker.Protocol.cs', 'ProtocolVersion', 29),
            ('RecoverySecurityEpoch', 'src/Ipc.Platform/RecoverySecurityPolicy.cs', 'CurrentEpoch', 31),
            ('LifecycleProtocol', 'src/Ipc.Platform/DeploymentRuntimeCompatibility.cs', 'CurrentLifecycleProtocol', 37),
        ]
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            for _, relative, name, version in declarations:
                source = root / relative
                source.parent.mkdir(parents=True, exist_ok=True)
                source.write_text(f'public static class Fixture\n{{\n    public const int {name} = {version};\n}}\n', encoding='utf-8')
            contract = root / 'src/Ipc.Application.Contracts/Ipc.Application.Contracts.csproj'
            contract.parent.mkdir(parents=True)
            contract.write_text('<Project><PropertyGroup><Version>7.8.9</Version></PropertyGroup></Project>', encoding='utf-8')
            files = {'Ipc.Own.dll': 'a' * 64, 'native/vendor.dll': 'b' * 64}

            def manifest():
                return read_deployment_compatibility(root, read_storage_format(root), {'CompatibilitySha256': 'C' * 64}, files)

            value = manifest()
            expected = {field: version for field, _, _, version in declarations}
            expected.update(SchemaVersion=1, ApplicationContractVersion='7.8.9', ProviderPolicySha256='C' * 64,
                            DependencyInventorySha256=hashlib.sha256(('native/vendor.dll\t' + 'B' * 64).encode()).hexdigest().upper())
            self.assertEqual(expected, value)
            files['Ipc.Own.dll'] = 'd' * 64
            self.assertEqual(value, manifest())
            lifecycle = root / 'src/Ipc.Platform/DeploymentRuntimeCompatibility.cs'
            lifecycle.write_text(lifecycle.read_text(encoding='utf-8').replace('= 37;', '= 38;'), encoding='utf-8')
            self.assertEqual(value | {'LifecycleProtocol': 38}, manifest())
            files['native/vendor.dll'] = 'e' * 64
            self.assertNotEqual(value['DependencyInventorySha256'], manifest()['DependencyInventorySha256'])


if __name__ == '__main__':
    unittest.main()
