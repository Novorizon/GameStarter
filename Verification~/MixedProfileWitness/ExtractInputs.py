#!/usr/bin/env python3
"""Re-extract frozen static fixtures from the original failed navigation reports."""

import argparse
import hashlib
import json
from pathlib import Path


def digest(data):
    return hashlib.sha256(data).hexdigest()


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(",", ":")).encode()


def ordered_units(frame):
    return sorted(frame["units"], key=lambda unit: unit["id"])


def extract(source, expected):
    raw = source.read_bytes()
    if digest(raw) != expected["sourceFailureLogSha256"]:
        raise ValueError("Original failure log SHA256 mismatch: " + source.name)
    original = json.loads(raw)
    if original["success"]:
        raise ValueError("Expected a failed original report: " + source.name)
    frames = original["trajectory"]
    final = frames[-1]
    final_units = ordered_units(final)
    earliest = next(
        frame for frame in sorted(frames, key=lambda frame: frame["tick"])
        if ordered_units(frame) == final_units
        and all(unit["state"] != "Moving" for unit in frame["units"])
    )
    if any(command["tick"] > earliest["tick"] for command in original["commands"]):
        raise ValueError("A later command would invalidate the selected static pose.")
    fixture = {key: value for key, value in original.items() if key != "trajectory"}
    fixture.update({
        "sourceFailureLogName": source.name,
        "sourceFailureLogSha256": digest(raw),
        "sourceFailureFrameSha256": digest(canonical(final)),
        "sourceStaticFrameSha256": digest(canonical(earliest)),
        "extraction": "Original commands and geometry; only earliest identical static and final failure frames retained.",
        "trajectory": [earliest, final],
    })
    data = (json.dumps(fixture, indent=2) + "\n").encode()
    if digest(data) != expected["fixtureSha256"]:
        raise ValueError("Extracted fixture SHA256 mismatch: " + source.name)
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("original_reports", type=Path)
    parser.add_argument("--output", type=Path, default=Path(__file__).parent / "Inputs")
    args = parser.parse_args()
    manifest = json.loads((Path(__file__).parent / "InputManifest.json").read_text())
    args.output.mkdir(parents=True, exist_ok=True)
    for name, expected in manifest["fixtures"].items():
        data = extract(args.original_reports / name, expected)
        (args.output / name).write_bytes(data)
        print("Verified and extracted " + name)


if __name__ == "__main__":
    main()
