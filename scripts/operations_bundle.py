"""Merge the read-only SM observer into its matching immutable desktop candidate."""
import json
from pathlib import Path
import shutil


def merge_operations(observer: Path, desktop: Path):
    if not (observer / 'Ipc.Operations.Desktop.exe').is_file():
        raise ValueError('Operations apphost is missing')
    libraries = json.loads((observer / 'Ipc.Operations.Desktop.deps.json').read_bytes())['libraries']
    forbidden = ('Ipc.Assembly/', 'Ipc.Application.ParcelFlow/', 'Ipc.Application.Sportmaster/',
                 'Ipc.Platform.Storage/', 'Ipc.Drivers', 'Ipc.Integrations', 'Microsoft.Data.Sqlite', 'SQLitePCLRaw')
    if any(name.startswith(forbidden) for name in libraries):
        raise ValueError('Operations tool must not contain business, SDK, RCS or database owners')
    files = sorted(p for p in observer.rglob('*') if p.is_file())
    # Validate the entire set before modifying the desktop candidate.
    for binary in files:
        peer = desktop / binary.relative_to(observer)
        if peer.exists() and peer.read_bytes() != binary.read_bytes():
            raise ValueError('Operations/workstation shared bytes differ: ' + str(binary.relative_to(observer)))
    for binary in files:
        peer = desktop / binary.relative_to(observer)
        peer.parent.mkdir(parents=True, exist_ok=True)
        if not peer.exists():
            shutil.copyfile(binary, peer)
