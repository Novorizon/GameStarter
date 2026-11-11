# Supplemental goal clearance contract (shared v1)

Run the already-built driver; do not rebuild or edit the shared files concurrently.

```bash
source /workspace/.singleton-removal-env/activate.sh
export DOTNET_PROCESSOR_COUNT=2
dotnet /workspace/navigation-comparison/audit/goal-clearance-shared-v1/bin/Release/net8.0/GoalClearance.dll LABEL OUTPUT_DIRECTORY CANDIDATE_DLL FULL_FACTORY_TYPE
```

Use absolute output and candidate paths. Every case is regenerated from the fixed public definition and its exported bytes are checked against `original-e-before/{two-large,three-large,mixed-three}.input.json` before candidate execution. The factory receives only the original World and profiles; the independent feasible goals are used only by the independent sequential witness and are not sent to the candidate. All requests have AssignedGoalCell=-1. The map and Holding set are not changed before/after.

Cases are 2*r650, 3*r650, and r650/r650/r180, all speed1000mm/50ms and range2500mm, cell1000mm. A/B Holding centres are1000mm apart and conflict for two r650 bodies; C/D are original legal alternatives. The same original spawn points are distinct and physically clear. Every independent witness actually moves original bodies under the shared continuous Oracle and holds200 intervals. The caller retains rejected requests and retries at logical ticks until1000 logical ticks; no body is dropped. Each candidate runs twice in a fresh navigator, and checks every5ms, fixed radius/speed/base/terrain, identity, goal clearance and physical attack state.

A positive result requires no candidate.Error, every request Admitted, Pending=0, Settled=N, StableMicrosteps>=2000, ClaimedGoalMinimumGapMm>=-0.1, freshReplayIdentical=true, and independentSerialWitness.Success=true. Negative reservation gap is a goal allocation defect even if physical collision stops prevent penetration. These3 supplementary cases are reported separately and never added to the common49 score. `handoff-manifest.json` pins inputs, source, executable and common Core/Oracle. `original-e-before` preserves the initial E7f proof and `e-before` repeats it with this generic queued driver.
