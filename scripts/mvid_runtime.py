"""Optional, pinned MVIDCodeReader runtime for local offline SM delivery.

Vendor bytes and notices are build inputs, never Git assets or runtime downloads.
"""
import hashlib
import json
from pathlib import Path
import shutil
import struct


def publish(source, output, runtime, verification_only=False):
    if runtime is None:
        return dict(Status='MVID_NOT_INCLUDED', BusinessReady=False)
    if verification_only:
        raise ValueError('Do not mix native MVID delivery with vendor-free verification')
    source, output, runtime = Path(source), Path(output), Path(runtime).resolve(strict=True)
    inventory = json.loads((source / 'src/Ipc.Drivers.Hikrobot.Mvs/mvid-runtime.json').read_text(encoding='utf-8'))
    if inventory['SchemaVersion'] != 1 or inventory['Entry'] != 'MVIDCodeReader.dll':
        raise ValueError('Unsupported MVID runtime inventory')
    inputs = {}
    for item in inventory['Files']:
        name = item['Path']
        if Path(name).name != name or '/' in name or '\\' in name:
            raise ValueError('Unsafe MVID runtime filename')
        if name == 'MVIDCodeReaderConfig.ini':
            data = (source / 'src/Ipc.Drivers.Hikrobot.Mvs/mvid-runtime.ini').read_text(encoding='utf-8').encode('utf-8')
        else:
            path = runtime / name
            if path.is_symlink() or not path.is_file():
                raise ValueError('MVID dependency missing or changed: ' + name)
            data = path.read_bytes()
        if len(data) != item['Length'] or hashlib.sha256(data).hexdigest().upper() != item['Sha256']:
            raise ValueError('MVID dependency missing or changed: ' + name)
        if name.endswith('.dll'):
            if len(data) < 64:
                raise ValueError('MVID dependency is not native AMD64 PE: ' + name)
            offset = struct.unpack_from('<I', data, 60)[0]
            if offset + 6 > len(data) or data[:2] != b'MZ' or data[offset:offset+4] != b'PE\0\0' or struct.unpack_from('<H', data, offset+4)[0] != 0x8664:
                raise ValueError('MVID dependency is not native AMD64 PE: ' + name)
        inputs[name] = data
    notice = runtime / 'SDK-NOTICE.txt'
    if notice.is_symlink() or not notice.is_file() or not notice.stat().st_size:
        raise ValueError('Retain the supplied vendor license notice as SDK-NOTICE.txt')
    record = dict(Status='BUNDLED_MVID_CODEREADER', Version=inventory['Version'], Files=inventory['Files'],
                  Preload=inventory['Preload'], BusinessReady=False,
                  NoticeSha256=hashlib.sha256(notice.read_bytes()).hexdigest(),
                  OfflineInstallerPrerequisites=['Microsoft Visual C++ 2008 SP1 x64', 'Microsoft Visual C++ 2010 SP1 x64'],
                  ExternalPrerequisites=['Matching camera transport driver'],
                  BusinessBoundary='Native recognition only; Application owns filtering, placement and RCS guards')
    # Validate every byte before writing anything, including the working-directory config.
    for area in ('app', 'desktop'):
        destination = output / area / 'vendor/hikrobot-mvid'
        destination.mkdir(parents=True)
        for name, data in inputs.items():
            (destination / name).write_bytes(data)
        shutil.copyfile(notice, destination / 'SDK-NOTICE.txt')
        (destination / 'SDK-BUNDLE.json').write_text(json.dumps(record, indent=2)+'\n', encoding='utf-8')
        (output / area / 'MVIDCodeReaderConfig.ini').write_bytes(inputs['MVIDCodeReaderConfig.ini'])
    (output / 'MVIDCodeReaderConfig.ini').write_bytes(inputs['MVIDCodeReaderConfig.ini'])
    return record
