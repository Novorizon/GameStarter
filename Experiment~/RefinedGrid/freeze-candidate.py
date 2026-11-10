#!/usr/bin/env python3
"""Create a fingerprinted read-only C# Domain snapshot; never edit the live candidate."""
import argparse
import datetime
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("domain", type=Path)
parser.add_argument("destination", type=Path)
args = parser.parse_args()
source = args.domain.resolve()
destination = args.destination.resolve()
assert destination != source and source not in destination.parents, "Keep snapshots outside the live source directory."


def read_sources():
    return {
        path.relative_to(source).as_posix(): path.read_bytes()
        for path in sorted(source.rglob("*.cs"))
    }


contents = read_sources()
assert contents, "No candidate C# sources found."
assert contents == read_sources(), "Live candidate changed during capture; retry after its owner freezes edits."
hashes = {name: hashlib.sha256(data).hexdigest() for name, data in contents.items()}
combined = hashlib.sha256(
    "".join(name + "\0" + digest + "\n" for name, digest in hashes.items()).encode("utf-8")
).hexdigest()
output = destination / ("domain-" + combined[:16])
if output.exists():
    assert json.loads((output / "manifest.json").read_text())["sha256"] == hashes, "Existing snapshot differs."
    existing = {
        path.relative_to(output / "Domain").as_posix(): path.read_bytes()
        for path in sorted((output / "Domain").rglob("*.cs"))
    }
    assert existing == contents, "Existing snapshot source bytes differ."
else:
    (output / "Domain").mkdir(parents=True)
    for name, data in contents.items():
        path = output / "Domain" / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(data)
        path.chmod(0o444)
    manifest = {
        "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "source": str(source),
        "combinedSha256": combined,
        "sha256": hashes,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (output / "manifest.json").chmod(0o444)
    for path in sorted((output / "Domain").rglob("*"), reverse=True):
        if path.is_dir():
            path.chmod(0o555)
    (output / "Domain").chmod(0o555)
    output.chmod(0o555)
print(output / "Domain")
print(combined)
