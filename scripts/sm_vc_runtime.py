"""Stage only the pinned Microsoft x64 prerequisites in an offline SM medium."""

import hashlib
import json
from pathlib import Path


def publish(source, output, redistributables):
    inventory = json.loads((source / 'src/Ipc.Platform/sm-vc-runtime.json').read_text(encoding='utf-8'))
    inputs = {}
    for package in inventory['Packages']:
        path = redistributables / package['File']
        if path.is_symlink() or not path.is_file():
            raise ValueError('Missing Microsoft x64 prerequisite: ' + package['File'])
        data = path.read_bytes()
        if len(data) != package['Length'] or hashlib.sha256(data).hexdigest().upper() != package['Sha256']:
            raise ValueError('Microsoft x64 prerequisite differs from reviewed package: ' + package['File'])
        inputs[package['File']] = data
    destination = output / 'prerequisites'
    destination.mkdir()
    for name, data in inputs.items():
        (destination / name).write_bytes(data)
    (destination / 'vc90-probe.manifest').write_bytes(inventory['ActivationManifest'].encode('utf-8'))
    return inventory['Packages']
