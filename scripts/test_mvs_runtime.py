import hashlib
import json
import struct
import tempfile
import unittest
from pathlib import Path
from mvs_runtime import publish

class BundledRuntimePolicy(unittest.TestCase):
    def test_missing_actual_bytes_refuses_delivery(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(ValueError, "requires --mvs-runtime"):
                publish(Path(__file__).resolve().parents[1], Path(temp), None)

    def test_explicit_verification_omission_is_not_delivery(self):
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp)
            result = publish(Path(__file__).resolve().parents[1], output, None, True)
            self.assertEqual(result["Status"], "VERIFICATION_ONLY_VENDOR_ABSENT")
            self.assertFalse(result["BusinessReady"])
            self.assertFalse(list(output.iterdir()))

    def test_mixed_verification_and_vendor_input_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(ValueError, "Choose actual SDK delivery"):
                publish(Path(__file__).resolve().parents[1], Path(temp), Path(temp), True)


class BundledRuntimeInputValidation(unittest.TestCase):
    """Exercise the existing packager with unloadable synthetic PE headers only."""

    def setUp(self):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        self.root = Path(temporary.name)
        self.source = self.root / 'source'
        self.runtime = self.root / 'synthetic-runtime'
        self.output = self.root / 'output'
        self.runtime.mkdir()
        self.inventory_path = self.source / 'src/Ipc.Drivers.Hikrobot.Mvs/bundled-runtime.json'
        self.inventory_path.parent.mkdir(parents=True)
        self.inventory = dict(SchemaVersion=1, Abi='SYNTHETIC-NOT-A-SDK',
                              Entry='synthetic-entry.dll', Preload=['synthetic-transport.dll'], Files=[])
        for name in ('synthetic-entry.dll', 'synthetic-transport.dll'):
            data = bytearray(256)
            data[:2] = b'MZ'
            struct.pack_into('<I', data, 60, 128)
            data[128:132] = b'PE\0\0'
            struct.pack_into('<H', data, 132, 0x8664)
            data[160:185] = b'UNLOADABLE TEST DATA ONLY'
            (self.runtime / name).write_bytes(data)
            self.inventory['Files'].append(dict(Path=name, Length=len(data),
                Sha256=hashlib.sha256(data).hexdigest().upper()))
        self.write_inventory()
        self.notice = self.runtime / 'SDK-NOTICE.txt'
        self.notice.write_text('Synthetic notice; no vendor bytes or redistribution rights.\n', encoding='utf-8')

    def write_inventory(self):
        self.inventory_path.write_text(json.dumps(self.inventory), encoding='utf-8')

    def assert_refused_before_output(self, reason):
        with self.assertRaisesRegex(ValueError, reason):
            publish(self.source, self.output, self.runtime)
        self.assertFalse(self.output.exists(), 'Invalid inputs must not create a partial SDK delivery')

    def test_synthetic_control_copies_exact_bytes_without_barcode_readiness(self):
        record = publish(self.source, self.output, self.runtime)
        self.assertFalse(record['BusinessReady'])
        self.assertEqual(record['BarcodeRecognition'], 'COMPONENT_UNAVAILABLE')
        self.assertEqual(record['Abi'], 'SYNTHETIC-NOT-A-SDK')
        for area in ('app', 'desktop'):
            destination = self.output / area / 'vendor/hikrobot-mvs'
            self.assertEqual({p.name for p in destination.iterdir()},
                             {'synthetic-entry.dll', 'synthetic-transport.dll', 'SDK-NOTICE.txt', 'SDK-BUNDLE.json'})
            for item in self.inventory['Files']:
                self.assertEqual((destination / item['Path']).read_bytes(), (self.runtime / item['Path']).read_bytes())
            self.assertEqual((destination / 'SDK-NOTICE.txt').read_bytes(), self.notice.read_bytes())
            self.assertEqual(record['NoticeSha256'], hashlib.sha256(self.notice.read_bytes()).hexdigest())
            self.assertEqual(json.loads((destination / 'SDK-BUNDLE.json').read_text(encoding='utf-8')), record)

    def test_missing_later_dependency_refuses_before_any_copy(self):
        (self.runtime / 'synthetic-transport.dll').unlink()
        self.assert_refused_before_output('dependency missing or changed: synthetic-transport.dll')

    def test_same_size_modified_dependency_refuses_before_any_copy(self):
        path = self.runtime / 'synthetic-transport.dll'
        data = bytearray(path.read_bytes())
        data[-1] ^= 1
        path.write_bytes(data)
        self.assert_refused_before_output('dependency missing or changed: synthetic-transport.dll')

    def test_wrong_size_dependency_refuses_before_any_copy(self):
        path = self.runtime / 'synthetic-transport.dll'
        path.write_bytes(path.read_bytes() + b'changed')
        self.assert_refused_before_output('dependency missing or changed: synthetic-transport.dll')

    def test_declared_x86_dependency_refuses_before_any_copy(self):
        path = self.runtime / 'synthetic-transport.dll'
        data = bytearray(path.read_bytes())
        struct.pack_into('<H', data, 132, 0x014c)
        path.write_bytes(data)
        # Match this isolated declaration so the PE check, not the hash check, must reject x86.
        self.inventory['Files'][1]['Sha256'] = hashlib.sha256(data).hexdigest().upper()
        self.write_inventory()
        self.assert_refused_before_output('not the reviewed native AMD64 PE: synthetic-transport.dll')

    def test_missing_notice_refuses_before_any_copy(self):
        self.notice.unlink()
        self.assert_refused_before_output('Retain the supplied vendor license notice')

    def test_empty_notice_refuses_before_any_copy(self):
        self.notice.write_bytes(b'')
        self.assert_refused_before_output('Retain the supplied vendor license notice')
