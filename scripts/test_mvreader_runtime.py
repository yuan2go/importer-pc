import hashlib
import json
import struct
import tempfile
import unittest
from pathlib import Path
from mvreader_runtime import publish


class MvReaderRuntimePolicy(unittest.TestCase):
    def test_missing_vendor_is_not_a_deployable_medium(self):
        root = Path(__file__).resolve().parents[1]
        with tempfile.TemporaryDirectory() as temp:
            with self.assertRaisesRegex(ValueError, 'requires --mvreader-runtime'):
                publish(root, Path(temp), None)
            record = publish(root, Path(temp), None, True)
            self.assertFalse(record['BusinessReady'])
            self.assertEqual(record['Status'], 'VERIFICATION_ONLY_VENDOR_ABSENT')
            self.assertFalse(list(Path(temp).iterdir()))

    def test_log_profile_is_pinned_transformed_and_copied_to_working_roots(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); runtime = root / 'runtime'; runtime.mkdir()
            source = root / 'source'; inventory = source / 'src/Ipc.Drivers.Hikrobot/bundled-runtime.json'; inventory.parent.mkdir(parents=True)
            original = b'log4j.appender.SDK_Info.File=C:/synthetic-legacy/sdk.log\r\n'
            configured = b'log4j.appender.SDK_Info.File=${IPC_MVCODEREADER_LOG_ROOT}/MvCodeReaderSdk.log\r\n'
            def record(data): return dict(Path='log4cxx.properties', Length=len(data), Sha256=hashlib.sha256(data).hexdigest().upper())
            inventory.write_text(json.dumps(dict(Abi='SYNTHETIC', Files=[record(configured)], Preload=[], LoggingSource=record(original))))
            (runtime / 'log4cxx.properties').write_bytes(original); (runtime / 'SDK-NOTICE.txt').write_text('Synthetic fixture')
            publish(source, root / 'out', runtime)
            for area in ['app', 'desktop', '']:
                self.assertEqual((root / 'out' / area / 'log4cxx.properties').read_bytes(), configured)
            self.assertEqual((runtime / 'log4cxx.properties').read_bytes(), original)
            (runtime / 'log4cxx.properties').write_bytes(original + b'UNREVIEWED')
            with self.assertRaisesRegex(ValueError, 'missing or changed'): publish(source, root / 'bad', runtime)

    def test_exact_native_and_support_files_are_copied_and_tampering_refused(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp); source = root / 'source'; runtime = root / 'runtime'
            inventory = source / 'src/Ipc.Drivers.Hikrobot/bundled-runtime.json'
            inventory.parent.mkdir(parents=True); runtime.mkdir()
            data = bytearray(256); data[:2] = b'MZ'; struct.pack_into('<I', data, 60, 128)
            data[128:132] = b'PE\0\0'; struct.pack_into('<H', data, 132, 0x8664)
            (runtime / 'MvCodeReaderCtrl.dll').write_bytes(data)
            (runtime / 'CodeReaderSdkConfig.ini').write_bytes(b'SYNTHETIC-CONFIG')
            (runtime / 'SDK-NOTICE.txt').write_text('Synthetic fixture; no vendor assets')
            files = [dict(Path=p.name, Length=p.stat().st_size, Sha256=hashlib.sha256(p.read_bytes()).hexdigest().upper())
                     for p in runtime.iterdir() if p.name != 'SDK-NOTICE.txt']
            inventory.write_text(json.dumps(dict(Abi='SYNTHETIC', Preload=[], Files=files)))
            result = publish(source, root / 'out', runtime)
            self.assertEqual(result['BarcodeRecognition'], 'SDK_RESULT_CALLBACK')
            self.assertFalse(result['BusinessReady'])
            for area in ['app', 'desktop']:
                for item in files:
                    self.assertEqual((root / 'out' / area / 'vendor/hikrobot-idmvs' / item['Path']).read_bytes(),
                                     (runtime / item['Path']).read_bytes())
            (runtime / 'CodeReaderSdkConfig.ini').write_bytes(b'CHANGED-CONFIG')
            with self.assertRaisesRegex(ValueError, 'missing or changed'):
                publish(source, root / 'bad', runtime)
