# Independent random-map feasibility witnesses

This directory is an audit artifact, not a navigation candidate. It never passes its route, goal order, or serial admission order to A–E.

For each of the six frozen `RandomMaze` inputs, the producer independently constructs a physical execution with the original 32 requests, entry allocation and IDs, radius 250 mm, speed cap 500 mm per 50 ms, range 1000 mm, original terrain/base, and original 600-second deadline. It first places all 16 valid attack-centre discs, then 16 sparse gathering-centre discs. Every unit is born at its original entry, walks a sequence of adjacent cell-centre edges while all earlier bodies remain present, and remains alive. Every entire straight edge is checked by the independent continuous oracle, with an explicit duration at or below the original speed cap. The final 32 bodies (16 attacking, 16 waiting) remain unchanged for 10 seconds / 200 logical ticks.

The checking interval here is one constant-velocity straight edge, generally 100 ms. Its analytical sweep covers the whole edge against terrain, the circular base, and all stationary bodies. This is a constructive existence certificate, not a 5 ms candidate-controller benchmark or a performance result. The recorder writes complete routes, segment endpoints, times, births and final bodies to gzip files. The separate Replay project reloads those saved artifacts, rebuilds the original world and request ledger, and checks every segment again without running the producer's path search.

The six worlds have distinct geometry hashes. Their serial executions, including the final hold, take 168000–324200 ms, all below the original 600000 ms deadline. All final physical body gaps are at least 500 mm. The online candidates must discover their own assignments and motions; a valid serial certificate does not prove that a particular candidate will find it.

For `large-portal-grid-128/256`, the audit only certifies 548 individually valid and pairwise non-overlapping attack-centre discs, all statically reachable from the common connected component of all three original entries. It does **not** certify a simultaneous-entry execution, online liveness, or arbitrary-parameter completeness. Both cases use the same world and profile; the count difference is demand only.

Reproduce from this directory with .NET 8:

```bash
dotnet run --project Feasibility.csproj -c Release -- --all
dotnet run --project Replay/Replay.csproj -c Release -- .
```

`Snapshot` preserves the exact Core, oracle, fixture generator and original ScenarioCatalog sources used. Fixture assertions may evolve in the live development tree; consumers should compare the certificate's actual world and request data, not only prose or an assertion-field hash. `manifest.json` records producer/replayer binaries, frozen source hashes and artifact hashes. No production, SDK, shared harness or candidate file was modified by this audit.
