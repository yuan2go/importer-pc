"""Synthetic SDK frames -> actual ZXingCpp/Application/SQLite -> loopback Python RCS.

Build tests/Ipc.Application.Verification first. Uses a NEW absolute evidence root,
preserves failures, and terminates only its own simulator process. No camera or
production configuration/identity guard is changed; device qualification NOT_RUN.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import subprocess
import sys
import time


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--workspace", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--data-root", type=Path, required=True)
    args = parser.parse_args()
    workspace = args.workspace.resolve()
    root = args.data_root
    if not root.is_absolute() or root.exists():
        raise ValueError("A new absolute isolated evidence root is required")
    dll = workspace / "tests/Ipc.Application.Verification/bin/Release/net10.0/Ipc.Application.Verification.dll"
    if not dll.is_file():
        raise FileNotFoundError("Build the current verification project first")
    root.mkdir(parents=True)
    source = subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=workspace, text=True).strip()
    dirty = bool(subprocess.check_output(["git", "status", "--porcelain=v1"], cwd=workspace, text=True).strip())
    result = {"SourceCommit": source, "WorkingTreeDirty": dirty, "Status": "FAIL",
              "Tier": "Synthetic SDK port; actual software decoder, SM Application/SQLite, HTTP and Python; no real camera/Production qualification",
              "VerifierSha256": hashlib.sha256(dll.read_bytes()).hexdigest()}
    server = None
    server_log = (root / "python.log").open("wb")
    try:
        server_root = root / "simulator"
        server = subprocess.Popen([sys.executable, str(workspace / "scripts/rcs-local-simulator.py"),
                                   "--port", "0", "--data-root", str(server_root)],
                                  cwd=workspace, stdout=server_log, stderr=subprocess.STDOUT)
        for _ in range(100):
            if (server_root / "endpoint.json").exists():
                break
            if server.poll() is not None:
                raise RuntimeError("Owned Python simulator stopped before readiness")
            time.sleep(.05)
        endpoint = json.loads((server_root / "endpoint.json").read_text())["endpoint"]
        if not endpoint.startswith("http://127.0.0.1:"):
            raise ValueError("Owned simulator must publish loopback only")
        environment = os.environ.copy()
        environment["IPC_VERIFICATION_WITHOUT_VENDOR_RUNTIME"] = "1"
        client_log = root / "client.log"
        with client_log.open("wb") as output:
            child = subprocess.run(["dotnet", str(dll), str(workspace), "--mvs-software-flow",
                                    "--rcs-endpoint", endpoint, "--rcs-ledger", str(server_root / "simulator.sqlite")],
                                   cwd=workspace, env=environment, stdout=output, stderr=subprocess.STDOUT, timeout=120)
        result["ClientExitCode"] = child.returncode
        text = client_log.read_text(encoding="utf-8-sig", errors="replace")
        result["ClientLogSha256"] = hashlib.sha256(client_log.read_bytes()).hexdigest()
        result["PassLines"] = len(re.findall(r"^PASS\b", text, re.M))
        if child.returncode:
            raise RuntimeError("Client verification failed; inspect preserved client.log")
        if "VerifierSource=1.0.0+" + source not in text:
            raise ValueError("Loaded verification binary does not match checked source identity")
        with sqlite3.connect((server_root / "simulator.sqlite").as_uri() + "?mode=ro", uri=True) as db:
            tasks = []
            for request_id, body in db.execute("SELECT request_id,body FROM requests"):
                if any(x["command"] == "sorter.task.dest_request" for x in json.loads(body)["data"]):
                    tasks.append(request_id)
            assignments = db.execute("SELECT count(*) FROM assignments").fetchone()[0]
        if len(tasks) != 3 or len(set(tasks)) != 3 or assignments != 1:
            raise ValueError("Server originals/assignment count differs from the three-frame contract")
        if "Python ledger has no blank-frame request" not in text or "live Python ledger still matches every original" not in text:
            raise ValueError("Client did not complete independent original/corruption checks")
        result.update(Status="PASS", PythonTaskRequests=3, DistinctRequestIds=3, PythonAssignments=1,
                      OriginalCustodyAndNegativeChecks=True)
    except Exception as error:
        result["Error"] = repr(error)
        raise
    finally:
        if server is not None and server.poll() is None:
            server.terminate()  # Only the dedicated child this script created.
            server.wait(timeout=10)
        server_log.close()
        (root / "RESULT.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, ensure_ascii=False))


if __name__ == "__main__":
    main()
