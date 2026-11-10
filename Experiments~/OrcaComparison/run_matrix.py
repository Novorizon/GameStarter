#!/usr/bin/env python3
"""Run strict ORCA comparison cases and retain exact source/evidence fingerprints."""
import argparse
import datetime
import gzip
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys

BASE = Path(__file__).resolve().parent
ROOT = BASE.parents[1]
CASE = "concurrent-funnel-repath-yield"


def fingerprint():
    files = list(BASE.rglob("*.cs"))
    files += list((ROOT / "Assets/HotUpdate/TowerDefense/Domain").rglob("*.cs"))
    files += [
        BASE / "OrcaComparison.csproj",
        BASE / "run_matrix.py",
        BASE / "witness.json",
        ROOT / "Verification~/TowerDefenseNavigation/Scenarios.cs",
        ROOT / "Verification~/TowerDefenseNavigation/GeometryChecks.cs",
    ]
    return {
        p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in sorted(set(files))
        if not any(part in {"bin", "obj"} for part in p.parts)
    }


def execute(dotnet, output, case, birth, goal, limit):
    output.mkdir(parents=True, exist_ok=True)
    command = [
        dotnet, "run", "--no-build", "--project", str(BASE / "OrcaComparison.csproj"), "-c", "Release", "--",
        "--case", case, "--ticks", str(limit), "--guard-mm", "20", "--spawn-clearance-m", "1.5",
        "--seed", str(birth), "--shuffle-goals", str(goal), "--output", str(output),
    ]
    proc = subprocess.run(command, cwd=ROOT, capture_output=True, text=True)
    (output / "run.log").write_text(proc.stdout + proc.stderr, encoding="utf-8")
    report = output / (case + ".json")
    if not report.is_file():
        return {"case": case, "birthSeed": birth, "goalSeed": goal, "success": False, "exitCode": proc.returncode, "error": proc.stdout}
    data = json.loads(report.read_text())
    success = bool(data["Success"] and data["Spawned"] == 72 and data["Arrived"] == 72
                   and data["Attacking"] == 12 and data["Waiting"] == 60)
    if success:
        units = data["Trajectory"][-1]["Units"]
        assert len(units) == 72 and {u["Id"] for u in units} == set(range(1, 73))
        assert len({u["SlotId"] for u in units}) == 72
        assert all(u["Radius"] == 250 and u["State"] in {"Waiting", "Attacking"} for u in units)
        assert data["MaximumGoalErrorMm"] <= 5
    print(("PASS " if success else "FAIL ") + case + f" birth={birth} goal={goal} "
          + f"spawned={data['Spawned']}/72 arrived={data['Arrived']} finalIndex={data['Ticks']}", flush=True)
    return {
        "case": case, "birthSeed": birth, "goalSeed": goal, "success": success, "exitCode": proc.returncode,
        "spawned": data["Spawned"], "arrived": data["Arrived"], "attacking": data["Attacking"], "waiting": data["Waiting"],
        "finalLoopIndex": data["Ticks"], "logicalStepsExecuted": data["Ticks"] + 1,
        "peakMoving": data["PeakMoving"], "yieldRequests": data["YieldRequests"],
        "yieldCompletions": data["YieldCompletions"], "yieldTimeouts": data["YieldTimeouts"],
        "minimumSampledPhysicalGapMm": data["MinimumSampledPhysicalGapMm"],
        "maximumFinalGoalErrorMm": data["MaximumGoalErrorMm"], "elapsedMilliseconds": data["ElapsedMilliseconds"],
        "error": data["Error"], "report": str(report.relative_to(ROOT)),
        "reportSha256": hashlib.sha256(report.read_bytes()).hexdigest(),
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--output", type=Path, default=ROOT / "Artifacts/OrcaComparisonMatrix")
    parser.add_argument("--record-evidence", action="store_true")
    args = parser.parse_args()
    os.environ.setdefault("DOTNET_PROCESSOR_COUNT", "2")
    before = fingerprint()
    subprocess.run([args.dotnet, "build", str(BASE / "OrcaComparison.csproj"), "-c", "Release", "-m:1",
                    "--disable-build-servers", "--nologo"], cwd=ROOT, check=True)
    cases = []
    for birth in [0, 17, 733, 20261110]:
        for goal in [0, 1, 17, 733, 20261110]:
            cases.append(execute(args.dotnet, args.output / f"birth-{birth}-goal-{goal}", CASE, birth, goal, 6000))
    controls = [
        execute(args.dotnet, args.output / "control-basic", "concurrent-short-obstacle-horizon", 17, 1, 3000),
        execute(args.dotnet, args.output / "control-serial", "serial-short-obstacle-horizon", 0, 0, 6000),
    ]
    unchanged = before == fingerprint()
    summary = {
        "utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "scope": "Independent .NET 8 comparison only; no Unity, device, arbitrary-map, mixed-body or death lifecycle claim.",
        "upstream": "https://github.com/snape/RVO2-CS/tree/de124a275d51d421bb9e2a77952210527ab60b04",
        "sourceCommit": subprocess.check_output(["git", "rev-parse", "HEAD"], cwd=ROOT, text=True).strip(),
        "sourceFilesUnchanged": unchanged, "sourceHashes": before,
        "candidate": {"count": len(cases), "passed": sum(c["success"] for c in cases), "failed": sum(not c["success"] for c in cases)},
        "physicalContract": {"radiusMm": 250, "cellMm": 1000, "logicalTickSeconds": .05, "substeps": 10,
                             "maximumSpeedMmPerTick": 1000, "gatheringDistanceMm": 4000, "maximumCentreDistanceMm": 5000,
                             "goalToleranceMm": 5, "avoidanceGuardMm": 20, "spawnClearanceMetres": 1.5},
        "success": unchanged and len(cases) == 20 and all(c["success"] for c in cases),
        "cases": cases, "separateControlResults": controls,
    }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    if args.record_evidence:
        evidence = BASE / "Evidence"
        evidence.mkdir(parents=True, exist_ok=True)
        for index, case in enumerate(cases + controls):
            source = ROOT / case["report"]
            filename = f"{index:02d}-{case['case']}-birth-{case['birthSeed']}-goal-{case['goalSeed']}.json.gz"
            packed = gzip.compress(source.read_bytes(), mtime=0)
            (evidence / filename).write_bytes(packed)
            case["archivedReport"] = filename
            case["archiveSha256"] = hashlib.sha256(packed).hexdigest()
        (evidence / "matrix-summary.json").write_text(json.dumps(summary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return 0 if summary["success"] else 1


if __name__ == "__main__":
    sys.exit(main())
