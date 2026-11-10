#!/usr/bin/env python3
"""Read and verify an extracted candidate archive without changing its source files."""
import argparse
import hashlib
import json
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("snapshot", type=Path, help="Directory containing manifest.json and Domain/.")
args = parser.parse_args()
manifest = json.loads((args.snapshot / "manifest.json").read_text(encoding="utf-8"))
domain = args.snapshot / "Domain"
actual = {
    path.relative_to(domain).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
    for path in sorted(domain.rglob("*.cs"))
}
assert actual == manifest["sha256"], "Candidate source files differ from the recorded immutable snapshot."
combined = hashlib.sha256(
    "".join(name + "\0" + digest + "\n" for name, digest in actual.items()).encode("utf-8")
).hexdigest()
assert combined == manifest["combinedSha256"], "Combined candidate fingerprint differs."
print("PASS immutable candidate " + combined)
