#!/usr/bin/env python3
"""Build and verify one local SM installer and its frozen win-x64 medium."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

from release_build import INSTALLATION_CHANNELS, SM_WORKSTATION_PAGES
from release_provenance import installer_name


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--channel', choices=INSTALLATION_CHANNELS, required=True)
    parser.add_argument('--default-page', choices=SM_WORKSTATION_PAGES, required=True)
    parser.add_argument('--output', type=Path, required=True, help='New output directory')
    parser.add_argument('--dotnet', default='dotnet')
    parser.add_argument('--source-branch')
    parser.add_argument('--mvs-runtime', type=Path)
    parser.add_argument('--mvid-runtime', type=Path)
    parser.add_argument('--vc-redist', type=Path)
    parser.add_argument('--verification-without-vendor-runtime', action='store_true')
    args = parser.parse_args()

    scripts = Path(__file__).resolve().parent
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=False)
    medium = output / 'medium'
    common = ['--channel', args.channel, '--default-page', args.default_page, '--dotnet', args.dotnet]
    build_inputs = []
    for option in ('source_branch', 'mvs_runtime', 'mvid_runtime', 'vc_redist'):
        value = getattr(args, option)
        if value is not None:
            build_inputs.extend(['--' + option.replace('_', '-'), str(value)])
    if args.verification_without_vendor_runtime:
        build_inputs.append('--verification-without-vendor-runtime')
    subprocess.run([sys.executable, str(scripts / 'package-application.py'), '--application', 'sportmaster',
                    '--target', 'win-x64', '--output', str(medium), *common, *build_inputs], check=True)
    subprocess.run([sys.executable, str(scripts / 'package-installer.py'), '--media', str(medium.with_suffix('.zip')),
                    '--output', str(output), *common], check=True)
    provenance = json.loads((medium / 'BUILD-INFO.json').read_text(encoding='utf-8'))
    channel = args.channel.capitalize()
    if provenance['DefaultWorkstationPage'] != args.default_page or provenance['InstallationChannel'] != channel:
        raise ValueError('Frozen medium metadata differs from the requested installer')
    installer = output / installer_name('sportmaster', provenance['BusinessVersion'], args.channel == 'test')
    with installer.open('rb') as stream:
        sha256 = hashlib.file_digest(stream, 'sha256').hexdigest()
        stream.seek(0)
        md5 = hashlib.file_digest(stream, lambda: hashlib.md5(usedforsecurity=False)).hexdigest()
    print(json.dumps(dict(Installer=str(installer), Bytes=installer.stat().st_size, Sha256=sha256, MD5=md5,
                          InstallationChannel=channel, DefaultWorkstationPage=args.default_page), ensure_ascii=False))


if __name__ == '__main__':
    main()
