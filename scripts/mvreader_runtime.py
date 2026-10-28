"""Copy the reviewed current MvCodeReader GigE closure into the selected offline SM medium.

Vendor binaries stay outside Git. No runtime download, installer action or PATH change.
"""
import hashlib
import json
from pathlib import Path
import shutil
import struct
import re


def publish(source, output, runtime, verification_only=False):
    inventory = json.loads((source / 'src/Ipc.Drivers.Hikrobot/bundled-runtime.json').read_text())
    if runtime is None:
        if not verification_only:
            raise ValueError('Sportmaster win-x64 delivery requires --mvreader-runtime with the current actual x64 GigE closure')
        return dict(Status='VERIFICATION_ONLY_VENDOR_ABSENT', BusinessReady=False,
                    Reason='No vendor bytes on verification runner; not the deployable SDK delivery')
    if verification_only:
        raise ValueError('Choose actual SDK delivery or verification-only omission explicitly')
    runtime = Path(runtime).resolve(strict=True)
    contents = {}
    for item in inventory['Files']:
        path = runtime / item['Path']
        if path.is_symlink() or not path.is_file():
            raise ValueError('Current MvCodeReader dependency missing or changed: ' + item['Path'])
        data = path.read_bytes()
        original = inventory.get('LoggingSource')
        if original and item['Path'] == original['Path'] and len(data) == original['Length'] and hashlib.sha256(data).hexdigest().upper() == original['Sha256']:
            data, count = re.subn(rb'(?m)^log4j\.appender\.SDK_Info\.File=[^\r\n]*', b'log4j.appender.SDK_Info.File=${IPC_MVCODEREADER_LOG_ROOT}/MvCodeReaderSdk.log', data)
            if count != 1: raise ValueError('Ambiguous SDK logging profile')
        if len(data) != item['Length'] or hashlib.sha256(data).hexdigest().upper() != item['Sha256']:
            raise ValueError('Current MvCodeReader dependency missing or changed: ' + item['Path'])
        contents[item['Path']] = data
        if path.suffix.lower() != '.dll':
            continue
        data = path.read_bytes()
        offset = struct.unpack_from('<I', data, 60)[0]
        if data[:2] != b'MZ' or data[offset:offset+4] != b'PE\0\0' or struct.unpack_from('<H', data, offset+4)[0] != 0x8664:
            raise ValueError('MvCodeReader dependency is not the reviewed native AMD64 PE: ' + item['Path'])
    notice = runtime / 'SDK-NOTICE.txt'
    if not notice.is_file() or not notice.stat().st_size:
        raise ValueError('Retain the supplied vendor license notice as SDK-NOTICE.txt')
    record = dict(Status='BUNDLED_CURRENT_MVCODEREADER_GIGE', Abi=inventory['Abi'],
                  Files=inventory['Files'], Preload=inventory['Preload'],
                  NoticeSha256=hashlib.sha256(notice.read_bytes()).hexdigest(),
                  RedistributionScope='User-authorized local offline delivery; no public vendor-binary upload',
                  BarcodeRecognition='SDK_RESULT_CALLBACK', BusinessReady=False, ReadinessPolicy='Per-result RCS reporting; actual camera qualification and Application guards remain required')
    for area in ('app', 'desktop'):
        destination = output / area / 'vendor/hikrobot-idmvs'
        destination.mkdir(parents=True)
        for item in inventory['Files']:
            (destination / item['Path']).write_bytes(contents[item['Path']])
        shutil.copyfile(notice, destination / 'SDK-NOTICE.txt')
        (destination / 'SDK-BUNDLE.json').write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
    # The vendor reads these from CWD, including before its first enumeration.
    for area in ('app', 'desktop', ''):
        for item in inventory['Files']:
            if not item['Path'].lower().endswith('.dll'):
                (output / area / item['Path']).write_bytes(contents[item['Path']])
    return record
