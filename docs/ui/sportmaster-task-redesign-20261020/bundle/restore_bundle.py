"""Restore the original design ZIP from verified repository parts (Python 3.10+)."""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import tempfile


def restore(output: Path) -> None:
    source = Path(__file__).resolve().parent
    manifest = json.loads((source / "bundle-manifest.json").read_text(encoding="utf-8"))
    expected = manifest["original"]
    output = output.expanduser().resolve()
    if output.exists():
        if output.is_file() and hashlib.sha256(output.read_bytes()).hexdigest() == expected["sha256"]:
            print(f"Already verified: {output}")
            return
        raise ValueError(f"Output already exists with different content: {output}")
    if not output.parent.is_dir():
        raise ValueError(f"Output directory does not exist: {output.parent}")

    temporary: Path | None = None
    combined = hashlib.sha256()
    total = 0
    try:
        with tempfile.NamedTemporaryFile(prefix=".sportmaster-bundle-", suffix=".tmp",
                                         dir=output.parent, delete=False) as stream:
            temporary = Path(stream.name)
            for index, part in enumerate(manifest["parts"], start=1):
                name = part["path"]
                if part["index"] != index or Path(name).name != name or name in (".", ".."):
                    raise ValueError(f"Invalid part entry: {name}")
                part_path = source / name
                if part_path.is_symlink():
                    raise ValueError(f"Part must be a regular file: {name}")
                data = part_path.read_bytes()
                if len(data) != part["size_bytes"] or hashlib.sha256(data).hexdigest() != part["sha256"]:
                    raise ValueError(f"Part missing or changed: {name}")
                stream.write(data)
                combined.update(data)
                total += len(data)
            if total != expected["size_bytes"] or combined.hexdigest() != expected["sha256"]:
                raise ValueError("Combined archive identity does not match the original ZIP")
            stream.flush()
            os.fsync(stream.fileno())
        # Publish only a complete, verified file; refuse concurrent overwrite.
        os.link(temporary, output)
        print(f"Restored: {output}\nBytes: {total}\nSHA256: {combined.hexdigest()}")
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, required=True,
                        help="Destination ZIP; its parent directory must already exist")
    arguments = parser.parse_args()
    try:
        restore(arguments.output)
    except (OSError, ValueError, KeyError) as error:
        parser.exit(1, f"Archive was not restored: {error}\n")
