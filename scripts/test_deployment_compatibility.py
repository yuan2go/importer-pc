import hashlib
from pathlib import Path
import unittest
from application_compatibility import read_deployment_compatibility, read_storage_format


class RuntimeCompatibility(unittest.TestCase):
    def test_manifest_binds_current_readers_and_exact_dependency_bytes(self):
        root = Path(__file__).resolve().parents[1]
        files = {'Ipc.Own.dll': 'a' * 64, 'native/vendor.dll': 'b' * 64}
        value = read_deployment_compatibility(root, read_storage_format(root), {'CompatibilitySha256': 'C' * 64}, files)
        self.assertEqual((12, 3, 2, 3, 1, 1), tuple(value[k] for k in
            ('DataFormat', 'ConfigurationSchema', 'CredentialSchema', 'ManagementProtocol', 'RecoverySecurityEpoch', 'LifecycleProtocol')))
        self.assertEqual(hashlib.sha256(('native/vendor.dll\t' + 'B' * 64).encode()).hexdigest().upper(), value['DependencyInventorySha256'])
        files['Ipc.Own.dll'] = 'd' * 64
        self.assertEqual(value, read_deployment_compatibility(root, 12, {'CompatibilitySha256': 'C' * 64}, files))
        files['native/vendor.dll'] = 'e' * 64
        self.assertNotEqual(value['DependencyInventorySha256'], read_deployment_compatibility(root, 12, {'CompatibilitySha256': 'C' * 64}, files)['DependencyInventorySha256'])


if __name__ == '__main__':
    unittest.main()
