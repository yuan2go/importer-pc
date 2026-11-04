import hashlib
import json
import struct
import tempfile
import unittest
from pathlib import Path
from mvid_runtime import publish


class MvidRuntimeDelivery(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory()
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)
        self.source, self.runtime, self.output = (self.root / name for name in ('source', 'runtime', 'output'))
        self.runtime.mkdir()
        self.metadata = self.source / 'src/Ipc.Drivers.Hikrobot.Mvs'
        self.metadata.mkdir(parents=True)
        config = b'[LogConfig]\nLogEnable = False\nAlgorithmCostTime = False\n'
        (self.metadata / 'mvid-runtime.ini').write_bytes(config)
        binary = bytearray(256)
        binary[:2] = b'MZ'
        struct.pack_into('<I', binary, 60, 128)
        binary[128:132] = b'PE\0\0'
        struct.pack_into('<H', binary, 132, 0x8664)
        self.inventory = dict(SchemaVersion=1, Version='SYNTHETIC', Entry='MVIDCodeReader.dll', Preload=['synthetic-transport.dll'], Files=[])
        for name, data in [('MVIDCodeReader.dll', binary), ('synthetic-transport.dll', binary), ('MVIDCodeReaderConfig.ini', config)]:
            if name.endswith('.dll'):
                (self.runtime / name).write_bytes(data)
            self.inventory['Files'].append(dict(Path=name, Length=len(data), Sha256=hashlib.sha256(data).hexdigest().upper()))
        self.save()
        (self.runtime / 'SDK-NOTICE.txt').write_text('Synthetic notice, no vendor bytes.\n', encoding='utf-8')

    def save(self):
        (self.metadata / 'mvid-runtime.json').write_text(json.dumps(self.inventory), encoding='utf-8')

    def refused(self):
        with self.assertRaises(ValueError):
            publish(self.source, self.output, self.runtime)
        self.assertFalse(self.output.exists())

    def test_optional_absence_does_not_claim_capability(self):
        self.assertEqual(publish(self.source, self.output, None)['Status'], 'MVID_NOT_INCLUDED')
        self.assertFalse(self.output.exists())

    def test_exact_closure_and_disabled_native_logs(self):
        result = publish(self.source, self.output, self.runtime)
        self.assertFalse(result['BusinessReady'])
        for area in ('app', 'desktop'):
            for item in self.inventory['Files']:
                data = (self.output / area / 'vendor/hikrobot-mvid' / item['Path']).read_bytes()
                self.assertEqual(hashlib.sha256(data).hexdigest().upper(), item['Sha256'])
            self.assertEqual((self.output / area / 'MVIDCodeReaderConfig.ini').read_bytes(), (self.output / 'MVIDCodeReaderConfig.ini').read_bytes())

    def test_missing_dependency(self):
        (self.runtime / 'synthetic-transport.dll').unlink()
        self.refused()

    def test_changed_bytes(self):
        path = self.runtime / 'MVIDCodeReader.dll'
        data = bytearray(path.read_bytes()); data[-1] ^= 1; path.write_bytes(data)
        self.refused()

    def test_x86_even_with_matching_hash(self):
        path = self.runtime / 'MVIDCodeReader.dll'
        data = bytearray(path.read_bytes()); struct.pack_into('<H', data, 132, 0x14c); path.write_bytes(data)
        self.inventory['Files'][0]['Sha256'] = hashlib.sha256(data).hexdigest().upper(); self.save()
        self.refused()

    def test_changed_working_configuration(self):
        (self.metadata / 'mvid-runtime.ini').write_text('[LogConfig]\nLogEnable = True\n', encoding='utf-8')
        self.refused()

    def test_notice_required(self):
        (self.runtime / 'SDK-NOTICE.txt').unlink()
        self.refused()

    def test_no_vendor_bytes_in_verification_medium(self):
        with self.assertRaises(ValueError):
            publish(self.source, self.output, self.runtime, True)
        self.assertFalse(self.output.exists())


if __name__ == '__main__':
    unittest.main()
