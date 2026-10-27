#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
# This entry builds the Supply reference, including its matching desktop.
# Application installers are built by the separate product publication pipeline.
for project in src/Ipc.Product src/Ipc.Fixture tests/Ipc.Verification src/Ipc.Desktop; do
    dotnet build "$project" -p:BusinessModule=Supply -p:SelfContained=false -p:RestoreLockedMode=true -m:1 -nr:false
done
python3 scripts/seal.py --bin src/Ipc.Product/bin/Debug/net10.0
python3 scripts/seal.py --bin src/Ipc.Desktop/bin/Debug/net10.0-windows
