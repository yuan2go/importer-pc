#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet restore ImporterPc.slnx --locked-mode
dotnet build ImporterPc.slnx --no-restore -m:1 -nr:false
python3 scripts/seal.py --bin src/Ipc.Product/bin/Debug/net10.0
python3 scripts/seal.py --bin src/Ipc.Desktop/bin/Debug/net10.0-windows
