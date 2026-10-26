"""Reconcile canonical staged Git blobs; never inventory untracked local assets.

Stage intended source changes first, then run with --inherit <exact source SHA>
for each integrated PR. --check is read-only and checks the complete staged tree.
Historical manifest blobs remain accessible through the recorded source commits.
"""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path


def git(*args):
    return subprocess.check_output(["git", *args])


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--inherit", action="append", default=[])
    parser.add_argument("--check", action="store_true")
    args = parser.parse_args()
    path = Path("docs/manifest.json")
    current = json.loads(path.read_text(encoding="utf-8-sig"))
    rows = {row["path"]: row for row in current["files"]}
    identities = []
    for ref in args.inherit:
        sha = git("rev-parse", "--verify", ref + "^{commit}").decode().strip()
        raw = git("show", sha + ":docs/manifest.json")
        inherited = json.loads(raw)
        identities.append({"commit": sha, "manifest_sha256": hashlib.sha256(raw).hexdigest(),
                           "work_package": inherited.get("work_package")})
        for key, value in inherited.items():
            if key != "files":
                current.setdefault(key, value)
        for row in inherited["files"]:
            rows.setdefault(row["path"], row)
    entries = []
    for raw in git("ls-files", "--stage", "-z").split(b"\0"):
        if not raw:
            continue
        info, name = raw.split(b"\t", 1)
        mode, oid, stage = info.decode().split()
        if stage != "0":
            raise SystemExit("Resolve all conflicts before refreshing manifest")
        if mode not in ("100644", "100755"):
            raise SystemExit("Unsupported tracked mode: " + mode)
        name = name.decode("utf-8")
        if name not in ("docs/manifest.json", "docs/manifest.sha256"):
            entries.append((name, oid))
    result, cache = [], {}
    with subprocess.Popen(["git", "cat-file", "--batch"], stdin=subprocess.PIPE, stdout=subprocess.PIPE) as batch:
        for name, oid in entries:
            if oid not in cache:
                batch.stdin.write((oid + "\n").encode())
                batch.stdin.flush()
                header = batch.stdout.readline().split()
                if len(header) != 3 or header[1] != b"blob":
                    raise SystemExit("Missing canonical blob: " + name)
                size = int(header[2])
                digest = hashlib.sha256()
                remaining = size
                while remaining:
                    data = batch.stdout.read(min(remaining, 1024 * 1024))
                    if not data:
                        raise SystemExit("Truncated blob: " + name)
                    digest.update(data)
                    remaining -= len(data)
                if batch.stdout.read(1) != b"\n":
                    raise SystemExit("Invalid blob boundary: " + name)
                cache[oid] = (digest.hexdigest(), size)
            sha256, size = cache[oid]
            previous = rows.get(name)
            if args.check:
                if previous is None or previous.get("sha256") != sha256 or previous.get("size_bytes", previous.get("size")) != size:
                    raise SystemExit("Manifest differs from staged canonical blob: " + name)
                continue
            row = dict(previous or {"path": name, "version": "stability-integration-02",
                "status": "current_source_or_synthetic_evidence", "purpose": "Integrated source and bounded verification",
                "source": "WP-IPC-STABILITY-INTEGRATION-02; original evidence retains its own SourceCommit"})
            row.update(sha256=sha256, size_bytes=size, git_blob=oid, storage_scope="public_repository")
            result.append(row)
        batch.stdin.close()
    if args.check:
        if set(rows) != {name for name, _ in entries}:
            raise SystemExit("Manifest path inventory differs from staged tree")
        expected = hashlib.sha256(path.read_bytes()).hexdigest() + "  manifest.json"
        if Path("docs/manifest.sha256").read_text().strip() != expected:
            raise SystemExit("Manifest checksum differs")
        print("PASS canonical staged manifest: " + str(len(entries)) + " files")
        return
    current.update(work_package="WP-IPC-STABILITY-INTEGRATION-02",
        current_implementation_work_package="docs/work-packages/WP-IPC-STABILITY-INTEGRATION-02.md", files=result)
    if identities:
        current["stability_integration_02_sources"] = identities
    path.write_text(json.dumps(current, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    Path("docs/manifest.sha256").write_text(hashlib.sha256(path.read_bytes()).hexdigest() + "  manifest.json\n", encoding="ascii")
    print("Refreshed canonical manifest: " + str(len(result)) + " files")


if __name__ == "__main__":
    main()
