#!/usr/bin/env python3
"""Remove the Android Analytics product declarations from Firebase Unity 14.1.0."""
import argparse
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]
ANALYTICS = b"com.google.firebase:firebase-analytics:"
ANALYTICS_ENTRY = re.compile(
    rb'(?m)^[ \t]*<androidPackage spec="com\.google\.firebase:firebase-analytics:24\.0\.0"'
    rb'(?:[ \t]*/>|>\s*</androidPackage>)[ \t]*\r?\n'
)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--project", type=Path, default=ROOT, help="GameStarter channel checkout with Firebase installed")
    project = parser.parse_args().project.resolve()

    products = {"app": "AppDependencies.xml", "crashlytics": "CrashlyticsDependencies.xml"}
    assets = {name: project / "Assets/Firebase/Editor" / file for name, file in products.items()}
    embedded = {
        name: project / "Packages" / f"com.google.firebase.{name}" / "Firebase/Editor" / file
        for name, file in products.items()
    }
    installations = [layout for layout in (assets, embedded) if any(path.exists() for path in layout.values())]
    if len(installations) != 1 or not all(path.is_file() for path in installations[0].values()):
        parser.error("Install Firebase App and Crashlytics using a single Assets or embedded UPM layout.")

    updates = {}
    for name, path in installations[0].items():
        contents = path.read_bytes()
        expected = f'spec="com.google.firebase:firebase-{name}-unity:14.1.0"'.encode()
        if expected not in contents:
            parser.error(f"{path} does not declare Firebase Unity 14.1.0.")
        if ANALYTICS not in contents:
            continue

        updated, removed = ANALYTICS_ENTRY.subn(b"", contents)
        if removed != 1 or ANALYTICS in updated:
            parser.error(f"Unexpected Firebase Analytics declaration in {path}.")
        updates[path] = updated

    for path, updated in updates.items():
        path.write_bytes(updated)
    print(f"Removed Android Analytics declarations from {len(updates)} file(s). Run EDM Resolve and check the exported Gradle dependency tree.")


if __name__ == "__main__":
    main()
