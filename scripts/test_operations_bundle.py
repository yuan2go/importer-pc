import json
from pathlib import Path
import tempfile
import unittest
from operations_bundle import merge_operations


class OperationsBundleTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.observer = Path(self.temporary.name) / 'observer'
        self.desktop = Path(self.temporary.name) / 'desktop'
        self.observer.mkdir()
        self.desktop.mkdir()
        (self.observer / 'Ipc.Operations.Desktop.exe').write_bytes(b'synthetic-apphost')
        (self.observer / 'Ipc.Platform.dll').write_bytes(b'shared-platform')
        (self.desktop / 'Ipc.Platform.dll').write_bytes(b'shared-platform')
        self.dependencies = self.observer / 'Ipc.Operations.Desktop.deps.json'
        self.dependencies.write_text(json.dumps({'libraries': {'Ipc.Operations/1.0.0': {}}}))

    def test_shared_bytes_and_observer_both_enter_same_candidate(self):
        merge_operations(self.observer, self.desktop)
        self.assertEqual((self.desktop / 'Ipc.Platform.dll').read_bytes(), b'shared-platform')
        self.assertEqual((self.desktop / 'Ipc.Operations.Desktop.exe').read_bytes(), b'synthetic-apphost')

    def test_test_installation_keeps_its_own_apphost(self):
        name = 'Ipc.Operations.Desktop.Test.exe'
        (self.observer / 'Ipc.Operations.Desktop.exe').rename(self.observer / name)
        merge_operations(self.observer, self.desktop, name)
        self.assertTrue((self.desktop / name).is_file())
        self.assertFalse((self.desktop / 'Ipc.Operations.Desktop.exe').exists())

    def test_conflicting_shared_build_is_rejected_before_copy(self):
        (self.desktop / 'Ipc.Platform.dll').write_bytes(b'other-build')
        with self.assertRaisesRegex(ValueError, 'shared bytes differ'):
            merge_operations(self.observer, self.desktop)
        self.assertEqual(sorted(p.name for p in self.desktop.iterdir()), ['Ipc.Platform.dll'])

    def test_actual_dependency_closure_cannot_acquire_database_or_equipment_owner(self):
        for dependency in ('Ipc.Application.ParcelFlow', 'Ipc.Drivers.Hikrobot.Mvs',
                           'Ipc.Integrations.SportmasterRcs', 'Microsoft.Data.Sqlite.Core'):
            self.dependencies.write_text(json.dumps({'libraries': {dependency + '/1.0.0': {}}}))
            with self.assertRaisesRegex(ValueError, 'must not contain'):
                merge_operations(self.observer, self.desktop)
            self.assertFalse((self.desktop / 'Ipc.Operations.Desktop.exe').exists())


if __name__ == '__main__':
    unittest.main()
