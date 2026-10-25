#!/usr/bin/env python3
"""Write a new RCS configuration candidate from an installation receipt; never activate it."""
import argparse
import json
from pathlib import Path
import re
from urllib.parse import urlsplit


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--base', type=Path, required=True)
    parser.add_argument('--installation-receipt', type=Path, required=True)
    parser.add_argument('--parameters', type=Path, required=True)
    parser.add_argument('--target', required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    base = json.loads(args.base.read_text())
    receipt = json.loads(args.installation_receipt.read_text())
    parameters = json.loads(args.parameters.read_text())
    package = receipt['Package']
    if (receipt['Status'] != 'Installed' or package['Id'] != 'sportmaster.rcs-task'
            or not re.fullmatch(r'\d+\.\d+\.\d+', package['Version'])
            or not re.fullmatch(r'[0-9a-fA-F]{64}', package['ManifestSha256'])):
        raise ValueError('An exact installed sportmaster.rcs-task receipt is required')
    target = urlsplit(args.target)
    if (target.scheme not in {'http', 'https'} or not target.netloc or target.path != '/rcs/sorter/api'
            or target.username or target.password or target.query or target.fragment):
        raise ValueError('Use an explicit HTTP(S) /rcs/sorter/api target without credentials/query/fragment')
    if base['ApplicationId'] != 'sportmaster' or base['SchemaVersion'] != 3 or base['Plc'] is not None:
        raise ValueError('Use an explicit schema3 Sportmaster configuration')
    if not isinstance(parameters, dict) or any(not isinstance(v, str) for v in parameters.values()):
        raise ValueError('Provider parameter values must be strings; installed schema preflight is still required')
    # Old installed schemas may require this key; it is never a user quantity choice.
    if 'quantity' in parameters:
        parameters['quantity'] = '1'
    base['Wcs'] = dict(Kind='integration', Implementation='extension:' + package['Id'] + '/sportmaster-rcs-task',
                       Version=package['Version'], InstanceId=base['Wcs']['InstanceId'], Target=args.target,
                       Contract='SPORTMASTER-RCS-SORTER-v1', Parameters=parameters, Package=package, Module='sportmaster-rcs-task', Enabled=True)
    deployment = base.get('Extensions') or dict(Packages=[], Instances=[])
    if package not in deployment['Packages']:
        deployment['Packages'].append(package)
    base['Extensions'] = deployment
    if base.get('CapabilityInstances') is not None:
        base['CapabilityInstances'] = [base['Wcs'] if b['InstanceId'] == base['Wcs']['InstanceId'] else b for b in base['CapabilityInstances']]
    with args.output.open('x', encoding='utf-8') as output:
        json.dump(base, output, ensure_ascii=False, indent=2)
        output.write('\n')
    print(json.dumps(dict(Status='CandidateOnly', File=str(args.output), ExternalCalls=0,
                         Next='Validate with selected host deployment-check; activate through controlled configuration/restart only')))


if __name__ == '__main__':
    main()
