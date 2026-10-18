#!/usr/bin/env python3
"""Build/check/sign an independently committed declarative extension against a frozen host."""
import argparse
import json
from pathlib import Path
import signal
from extension_build import build


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--definition', type=Path, help='Committed build declaration; may belong to a separate Git repository')
    parser.add_argument('--kind', choices=['observation', 'integration'], help='Compatibility shortcut to a checked-in example declaration')
    parser.add_argument('--contract', type=Path, required=True)
    parser.add_argument('--check-host', type=Path, help='Frozen runnable Ipc.Product.dll with byte-identical public contracts; defaults beside target contract')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--key', type=Path, required=True)
    parser.add_argument('--publisher', required=True)
    parser.add_argument('--trust', type=Path, required=True)
    parser.add_argument('--version', help='New immutable three-part version; required by checked-in examples (independent declarations may retain an explicit Version)')
    parser.add_argument('--dependency-package', type=Path, action='append', default=[], help='Exact signed dependency archive; repeat for complete closure')
    parser.add_argument('--dotnet', default='dotnet')
    args = parser.parse_args()
    if args.definition is not None and args.kind is not None:
        parser.error('Use --definition or --kind, not both')
    if args.definition is None:
        root = Path(__file__).resolve().parent.parent
        # The shortcut chooses data only; no example schema/manifest/build rule lives here.
        args.definition = root / 'examples' / ('IntegrationExtension' if args.kind == 'integration' else 'BarcodeExtension') / 'extension.build.json'
    signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(KeyboardInterrupt()))
    print(json.dumps(build(args)))


if __name__ == '__main__':
    main()
