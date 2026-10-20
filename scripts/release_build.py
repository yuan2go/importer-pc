"""Shared frozen export and stable archive mechanics for the existing packagers."""
import datetime
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import zipfile


def frozen_export(root, source, destination):
    destination.mkdir(parents=True)
    extraction = ('\\\\?\\' + str(destination)) if os.name == 'nt' else str(destination)
    # Reading a frozen Git tree must preserve historical Windows device names.
    # Only this archive read relaxes Git's checkout guard; extraction still uses
    # data_filter and an extended-length destination, never a Win32 device path.
    archive_git = ['git', *(['-c', 'core.protectNTFS=false'] if os.name == 'nt' else [])]
    export = subprocess.Popen([*archive_git, 'archive', '--format=tar', source], cwd=root, stdout=subprocess.PIPE)
    try:
        with tarfile.open(fileobj=export.stdout, mode='r|') as archive:
            def safe(member, target):
                if os.name == 'nt':
                    member = member.replace(name=member.name.replace('/', os.sep))
                return tarfile.data_filter(member, target)
            archive.extractall(extraction, filter=safe)
        # git may still be writing padding after tar's EOF; drain before wait.
        while export.stdout.read(65536):
            pass
    except BaseException:
        if export.poll() is None:
            export.terminate()
        raise
    finally:
        export.stdout.close()
        code = export.wait()
    if code:
        raise ValueError('Frozen Git export failed')
    return extraction


def build_properties(source, snapshot, artifacts):
    artifacts.mkdir(parents=True, exist_ok=True)
    link = artifacts / 'frozen-source-link.json'
    link.write_text(json.dumps({'documents': {'/_/src/*':
        'https://raw.githubusercontent.com/yuan2go/importer-pc/' + source + '/*'}}), encoding='utf-8')
    return ['-p:PublishReadyToRun=false', '-p:PublishAot=false', '-p:DebugType=embedded',
            '-p:PathMap=' + str(snapshot) + '=/_/src%2C' + str(artifacts) + '=/_/build',
            '-p:SourceRevisionId=' + source, '-p:EnableSourceControlManagerQueries=false',
            '-p:EnableSourceLink=false', '-p:SourceLink=' + str(link)]


def cleanup(snapshot, extraction, artifacts, output):
    # Only recursively remove the two generated children of this new output.
    if snapshot.resolve().parent != output.resolve() or snapshot.name != 'source-snapshot' or artifacts.resolve().parent != output.resolve() or artifacts.name != 'build-intermediates':
        raise ValueError('Refuse cleanup outside generated build directories')
    shutil.rmtree(artifacts)
    shutil.rmtree(extraction)


def archive_package(output, archive, root, source):
    entries = sorted(f for f in output.rglob('*') if f.is_file())
    (output / 'SHA256SUMS').write_text(''.join(hashlib.sha256(f.read_bytes()).hexdigest() + '  ' +
        f.relative_to(output).as_posix() + '\n' for f in entries), encoding='utf-8', newline='\n')
    epoch = int(subprocess.check_output(['git', 'show', '-s', '--format=%ct', source], cwd=root, text=True))
    date = datetime.datetime.fromtimestamp(max(epoch, 315532800), datetime.timezone.utc).timetuple()[:6]
    with zipfile.ZipFile(archive, 'x', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
        for file in sorted(f for f in output.rglob('*') if f.is_file()):
            info = zipfile.ZipInfo(file.relative_to(output).as_posix(), date_time=date)
            info.create_system = 3
            info.external_attr = 0o100644 << 16
            info.compress_type = zipfile.ZIP_DEFLATED
            z.writestr(info, file.read_bytes(), compresslevel=9)
