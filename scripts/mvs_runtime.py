"""Copy the reviewed current MVS GigE closure into the selected offline SM medium.

Vendor binaries stay outside Git. No runtime download, installer action or PATH change.
"""
import hashlib
import json
from pathlib import Path
import shutil
import struct


def publish(source, output, runtime, verification_only=False):
    inventory = json.loads((source / 'src/Ipc.Drivers.Hikrobot.Mvs/bundled-runtime.json').read_text())
    if runtime is None:
        if not verification_only:
            raise ValueError('Sportmaster win-x64 delivery requires --mvs-runtime with the current actual x64 GigE closure')
        return dict(Status='VERIFICATION_ONLY_VENDOR_ABSENT', BusinessReady=False,
                    Reason='No vendor bytes on verification runner; not the deployable SDK delivery')
    if verification_only:
        raise ValueError('Choose actual SDK delivery or verification-only omission explicitly')
    runtime = Path(runtime).resolve(strict=True)
    for item in inventory['Files']:
        path = runtime / item['Path']
        if path.is_symlink() or not path.is_file() or path.stat().st_size != item['Length'] or hashlib.sha256(path.read_bytes()).hexdigest().upper() != item['Sha256']:
            raise ValueError('Current MVS dependency missing or changed: ' + item['Path'])
        data = path.read_bytes()
        offset = struct.unpack_from('<I', data, 60)[0]
        if data[:2] != b'MZ' or data[offset:offset+4] != b'PE\0\0' or struct.unpack_from('<H', data, offset+4)[0] != 0x8664:
            raise ValueError('MVS dependency is not the reviewed native AMD64 PE: ' + item['Path'])
    notice = runtime / 'SDK-NOTICE.txt'
    if not notice.is_file() or not notice.stat().st_size:
        raise ValueError('Retain the supplied vendor license notice as SDK-NOTICE.txt')
    record = dict(Status='BUNDLED_CURRENT_MVS_GIGE', Abi=inventory['Abi'],
                  Files=inventory['Files'], Preload=inventory['Preload'],
                  NoticeSha256=hashlib.sha256(notice.read_bytes()).hexdigest(),
                  RedistributionScope='User-authorized local offline delivery; no public vendor-binary upload',
                  BarcodeRecognition='ZXingCpp-0.5.3_ENGINE-3.1.1_MONO8', BusinessReady=False,
                  BusinessBoundary='Decoder capability only; actual configuration/Application/RCS guards still apply')
    for area in ('app', 'desktop'):
        destination = output / area / 'vendor/hikrobot-mvs'
        destination.mkdir(parents=True)
        for item in inventory['Files']:
            shutil.copyfile(runtime / item['Path'], destination / item['Path'])
        shutil.copyfile(notice, destination / 'SDK-NOTICE.txt')
        (destination / 'SDK-BUNDLE.json').write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
    return record
