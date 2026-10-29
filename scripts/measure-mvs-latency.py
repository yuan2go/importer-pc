"""Offline actual ZXing measurement with source/binary custody. Never opens devices or calls RCS."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--data-root", type=Path, required=True)
    parser.add_argument("--private-pgm-directory", type=Path)
    parser.add_argument("--reference-contrast", action="store_true", help="Use the original scalar contrast formula in this verifier only")
    parser.add_argument("--repeats", type=int, default=3, choices=range(1, 21))
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    root = args.data_root
    if not root.is_absolute() or root.exists():
        raise ValueError("A new absolute D-drive evidence root is required")
    if os.name == "nt" and root.drive.lower() != "d:":
        raise ValueError("Project evidence must remain on D drive")
    output = workspace / "tests/Ipc.Application.Verification/bin/Release/net10.0"
    verifier = output / "Ipc.Application.Verification.dll"
    driver = output / "Ipc.Drivers.Hikrobot.Mvs.dll"
    if not verifier.is_file() or not driver.is_file():
        raise FileNotFoundError("Build current Application verification first")
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=workspace, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain=v1"], cwd=workspace, text=True).strip())
    root.mkdir(parents=True)
    result = {"SourceCommit": source, "WorkingTreeDirty": dirty, "Status": "FAILED",
              "Tier": "Offline actual ZXing; no live camera/Application/RCS/Production acceptance",
              "VerifierSha256": hashlib.sha256(verifier.read_bytes()).hexdigest(),
              "DriverSha256": hashlib.sha256(driver.read_bytes()).hexdigest()}
    try:
        command = ["dotnet", str(verifier), str(workspace), "--mvs-latency", "--repeats", str(args.repeats),
                   "--timing-output", str(root / "timings.json")]
        if args.private_pgm_directory is not None:
            if not args.private_pgm_directory.is_absolute():
                raise ValueError("Explicit absolute private image directory required")
            command.extend(["--private-pgm-directory", str(args.private_pgm_directory)])
        if args.reference_contrast:
            command.append("--reference-contrast")
        with (root / "measurement.log").open("wb") as log:
            completed = subprocess.run(command, cwd=workspace, stdout=log, stderr=subprocess.STDOUT, timeout=1800)
        result["ExitCode"] = completed.returncode
        if completed.returncode:
            raise RuntimeError("Offline measurement failed; inspect preserved log")
        measurement = json.loads((root / "timings.json").read_text(encoding="utf-8-sig"))
        if "1.0.0+" + source != measurement["VerifierSource"]:
            raise ValueError("Loaded verifier commit differs from workspace; rebuild first")
        result.update(Status="MEASURED_NOT_ACCEPTANCE", Images=measurement["Length"], Repeats=measurement["Repeats"], Contrast=measurement["Contrast"],
                      TimingSha256=hashlib.sha256((root / "timings.json").read_bytes()).hexdigest())
    except Exception as error:
        result["Error"] = type(error).__name__  # private paths/identities stay out of summary
        raise
    finally:
        (root / "RESULT.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
