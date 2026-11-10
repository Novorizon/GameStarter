#!/usr/bin/env python3
"""Build linked current production sources, run the finite audit, and certify input stability."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
import subprocess
import sys
from pathlib import Path


HERE = Path(__file__).resolve().parent
REPOSITORY = HERE.parent.parent


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fingerprints(domain: Path, layout: Path, game_map: Path) -> dict:
    files = {path.relative_to(domain).as_posix(): sha256(path) for path in sorted(domain.rglob("*.cs"))}
    encoded = json.dumps(files, sort_keys=True, separators=(",", ":")).encode()
    harness = {path.name: sha256(path) for pattern in ("*.cs", "*.csproj", "*.py") for path in sorted(HERE.glob(pattern))}
    return {
        "domain": {"aggregateSha256": hashlib.sha256(encoded).hexdigest(), "files": files},
        "layoutSha256": sha256(layout),
        "mapSha256": sha256(game_map),
        "harness": harness,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--domain", "--candidate-domain", dest="domain", type=Path, default=REPOSITORY / "Assets/HotUpdate/TowerDefense/Domain")
    parser.add_argument("--layout", type=Path)
    parser.add_argument("--dotnet", default="dotnet", help=".NET 8 executable")
    parser.add_argument("--map", type=Path, default=REPOSITORY / "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes")
    parser.add_argument("--output", type=Path, default=REPOSITORY / "Artifacts/AdmissionTransactionAudit")
    parser.add_argument("--expect-domain-sha256", help="Optional aggregate fingerprint to bind a run to an expected frozen candidate")
    arguments = parser.parse_args()
    domain = arguments.domain.resolve()
    layout = (arguments.layout or domain.parent / "Data/NavigationLayout.cs").resolve()
    game_map = arguments.map.resolve()
    output = arguments.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    for filename in ("manifest.json", "results.json", "execution.log"):
        (output / filename).unlink(missing_ok=True)

    before = fingerprints(domain, layout, game_map)
    if not before["domain"]["files"]:
        parser.error("Domain contains no C# source")
    if arguments.expect_domain_sha256 and before["domain"]["aggregateSha256"] != arguments.expect_domain_sha256:
        parser.error("Domain fingerprint differs from the expected frozen candidate")

    build = output / "build"
    if build.exists():
        shutil.rmtree(build)
    build.mkdir()
    command = [
        arguments.dotnet, "build", str(HERE / "AdmissionTransactionAudit.csproj"), "-c", "Release", "--nologo",
        f"-p:NavigationDomainRoot={domain}", f"-p:NavigationLayoutFile={layout}", f"-p:NavigationMapFile={game_map}",
        f"-p:BaseIntermediateOutputPath={build / 'obj'}/", f"-p:OutputPath={build / 'bin'}/",
    ]
    return_code = 1
    error = None
    with (output / "execution.log").open("w") as log:
        try:
            print("Building linked Domain " + before["domain"]["aggregateSha256"], flush=True)
            built = subprocess.run(command, cwd=REPOSITORY, stdout=log, stderr=subprocess.STDOUT, check=False)
            if built.returncode == 0:
                result = subprocess.run(
                    [arguments.dotnet, str(build / "bin/AdmissionTransactionAudit.dll"), "--output", str(output / "results.json")],
                    cwd=REPOSITORY, stdout=log, stderr=subprocess.STDOUT, check=False,
                )
                return_code = result.returncode
            else:
                return_code = built.returncode
        except OSError as exception:
            error = str(exception)
            print(error, file=log)

    after = fingerprints(domain, layout, game_map)
    unchanged = before == after
    report = json.loads((output / "results.json").read_text()) if (output / "results.json").exists() else None
    passed = return_code == 0 and unchanged and report is not None and report["passed"]
    manifest = {
        "passed": passed,
        "executionReturnCode": return_code,
        "sourceAndInputsUnchanged": unchanged,
        "error": error,
        "environment": ".NET 8 executable; current linked Domain, no copied production source",
        "sourceBefore": before,
        "sourceAfter": after,
        "resultsSha256": sha256(output / "results.json") if report else None,
        "executionLogSha256": sha256(output / "execution.log"),
        "caseCount": len(report["cases"]) if report else 0,
    }
    (output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    print(f"ADMISSION_TRANSACTION_AUDIT {'PASS' if passed else 'FAIL'} cases={manifest['caseCount']} unchanged={unchanged}")
    print(output / "manifest.json")
    if not passed:
        print((output / "execution.log").read_text()[-12000:], file=sys.stderr)
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
