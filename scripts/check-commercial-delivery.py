#!/usr/bin/env python3
"""Verify actual Sportmaster media and evidence; never grants production approval."""
import argparse
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
from commercial_delivery import digest, evaluate, plan, distribution_signature


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--release', type=Path, required=True)
    parser.add_argument('--evidence', type=Path, required=True)
    parser.add_argument('--level', choices=('engineering', 'production'), default='engineering')
    parser.add_argument('--prepare', action='store_true', help='Create an immutable NOT_RUN/BLOCKED plan after media verification')
    parser.add_argument('--approved-signer', type=Path, help='Externally approved public .cer signer, mandatory for production signature verification; never a private key')
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location('release_windows', Path(__file__).with_name('release-windows.py'))
    release = importlib.util.module_from_spec(spec); spec.loader.exec_module(release)
    root = args.release.resolve()
    receipt = release.read(root / 'RELEASE.json')
    if receipt.get('Profile') != 'sportmaster' or receipt.get('State') != 'PASS':
        raise ValueError('Expected completed Sportmaster release profile')
    subprocess.run([sys.executable, str(Path(__file__).with_name('release-windows.py')), '--output', str(root), '--profile', 'sportmaster', '--verify-only'], check=True, stdout=subprocess.PIPE)
    medium = root / next(Path(p['File']).stem for p in receipt['Packages'] if p['Target'] == 'win-x64')
    source, inventory = receipt['SourceCommit'], digest(medium / 'SHA256SUMS')
    if args.prepare:
        # Never overwrite an earlier result or let a fresh template erase FAIL.
        with args.evidence.open('x', encoding='utf-8') as stream:
            json.dump(plan(source, inventory), stream, ensure_ascii=False, indent=2)
        return
    report = json.loads(args.evidence.read_bytes())
    result = evaluate(args.evidence.resolve().parent, report, source, inventory, args.level)
    result['Authenticode'] = receipt.get('Authenticode', 'NOT_VERIFIED')
    if args.level == 'production':
        signature = distribution_signature(medium, args.approved_signer)
        result['DistributionSignature'] = signature
        if signature['Status'] != 'PASS':
            result['Status'] = 'FAIL'
            result['Outstanding'].append(dict(Id='distribution-signature', Status='BLOCKED', Reason=signature['Reason']))
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if result['Status'] != 'PASS':
        raise SystemExit(1)


if __name__ == '__main__':
    main()
