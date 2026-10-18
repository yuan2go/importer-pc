#!/usr/bin/env python3
"""Sign a reviewed deployment decision offline. Does not grant approval or install anything."""
import argparse
import base64
import json
from pathlib import Path
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--document', type=Path, required=True)
    parser.add_argument('--publisher', required=True)
    parser.add_argument('--key', type=Path, required=True)
    parser.add_argument('--trust', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    document = args.document.read_text(encoding='utf-8')
    value = json.loads(document)
    if value['SchemaVersion'] != 1 or not value['ApprovalEvidence']:
        raise ValueError('Reviewed schema1 decision and approval evidence are required')
    trust = json.loads(args.trust.read_text(encoding='utf-8'))
    publisher = next(p for p in trust['Publishers'] if p['Id'] == args.publisher)
    if value['Mode'] == 'Production' and publisher['TestOnly']:
        raise ValueError('Test signer cannot qualify Production')
    with tempfile.TemporaryDirectory(prefix='ipc-qualification-sign-') as temporary:
        root = Path(temporary)
        source = root / 'document.json'
        source.write_bytes(document.encode('utf-8'))
        signature = root / 'signature'
        public = root / 'public.pem'
        public.write_text(publisher['PublicKeyPem'], encoding='utf-8')
        options = ['-sigopt', 'rsa_padding_mode:pss', '-sigopt', 'rsa_pss_saltlen:digest']
        subprocess.run(['openssl', 'dgst', '-sha256', '-sign', str(args.key.resolve()), *options, '-out', str(signature), str(source)], check=True)
        subprocess.run(['openssl', 'dgst', '-sha256', '-verify', str(public), *options, '-signature', str(signature), str(source)], check=True)
        # Exclusive publication: no overwriting an old decision or exposing a private key.
        with args.output.open('x', encoding='utf-8') as output:
            json.dump(dict(Publisher=args.publisher, Document=document, Signature=base64.b64encode(signature.read_bytes()).decode('ascii')), output, ensure_ascii=False, indent=2)
            output.write('\n')
    print(json.dumps(dict(Status='SignedOffline', Output=str(args.output), PhysicalReadiness='NOT_VERIFIED')))


if __name__ == '__main__':
    main()
