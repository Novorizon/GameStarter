# Independent admission-gate transaction audit

This is a read-only audit snapshot of the `full-slots` candidate. Production files, packages, maps, bodies, and SDK configuration were not modified by this audit. All programs run copied domain sources in .NET 8; these results do not claim Unity or rendering validation.

The exact domain source hashes are in `source-hashes.json`; the navigation layout parser and original ForestNavigation bytes are copied locally and recorded in `input-hashes.json`. The admission-gate source SHA-256 is `7ee317ba7628d80ae920db7d1c9624c3016aced02937113a718b435fa27a9ca3`.

## Reproduce

With .NET 8 available, run each project with `dotnet run --project <project> -c Release`. `Cache.csproj`, `Alternating.csproj`, `Metadata.csproj`, and `Death.csproj` are separate executables. They use the copied forest map and domain code. The programs currently write their JSON evidence under `/tmp/gate-transaction-audit/final/`; the supplied `.log` and `*-results.json` files are retained evidence from the completed run.

## Confirmed defects and verified fixes

The earlier candidate repeatedly rebuilt a failed gate before checking its ordinary capacity cache and mislabeled budget exhaustion as `CapacityExceeded`. On a public-API 9×9 fixture with 17 settled Big bodies, identical pending Small requests repeatedly spent their entire configured search budget. The maintained candidate added a semantic-revision gate-failure cache, a 64-tick budget retry, and preserved `SearchLimitExceeded`.

A second confirmed defect was failed speculation changing `goalRevision`, `trafficRevision`, and participant traffic markers even though every actor and future reservation had returned to the original state. On the actual forest with 44 Big bodies, same-tick alternating Small requests at speed 500/250 invalidated one another, spending 17,200 expansions every attempt. The maintained candidate now restores the semantic revisions and participant traffic markers after rollback. On this snapshot, each distinct speed spends its budget once, and the next six alternating attempts spend zero expansions.

## Cache, rollback, and concurrency checks

`cache-results.json` covers limits 100/3,200/6,400 on the 9×9 fixture, repeated same-tick requests, 63-tick cached budget failures, exact 64-tick retry, stable geometric-failure caching, public `Remove` invalidating the old failure and immediately allowing a new Small, and an explicitly white-box traffic-only revision bump forcing recomputation. The public removal test succeeded at all three budgets with 51 expansions.

The actual-forest budget rollbacks at 15,000/17,200/17,500 leave all public agent fields and every future reservation unchanged. Repeating the same request on the frozen snapshot spends zero expansions. `alternating-results.json` additionally verifies the distinct-speed cache keys at budgets 17,200 and 17,500.

The successful actual-forest gate accepts actor 45, then a second request during the same tick is rejected as `ReservationBlocked`, with zero expansions, 45 actual actors, and no modification to any committed actor state, reservation, or clock value.

`metadata-results.json` covers 17,200/17,500 × pending speed 500/250. After a failed transaction it compares complete goal claims and all occupant metadata: original Agent/Profile/Path references, every timed path node, Segment, AttackGoal, PromotionRevision, NextPromotionTick, TrafficRevision, and public agent properties. All are unchanged. It then explicitly removes all 44 real actors one by one, checking after every removal that each goal and reservation belongs to a surviving actor. Every case ends with zero claims and zero reservations.

## Death and physical oracle

`death-results.json` covers actual ForestNavigation, 44 Big bodies (r250/range550), then one late Small (r180/range450) admitted through a real multi-owner gate. Six death phases are tested separately at speed 55 and maximum permitted speed 500: incoming Small immediately/after arrival; a participating Big immediately/after actual first outbound movement/at its temporary non-holding buffer/during return. Every death is an explicit public Remove; no additional actor creation or removal is accepted.

The independent per-tick oracle checks minimum relative swept-disc separation for all moving pairs, exact expanded-base separation, axis-aligned swept-disc versus blocked-cell rectangle distance, full world boundaries, and configured movement speed. It checks actual positions rather than planner reservations. There are 34,238,556 pair checks and 4,195,390 nearby blocked-cell sweep checks in the 12 cases. Touching at zero gap is allowed; penetration is rejected. All cases settle with exactly 44 survivors, every survivor at a legal holding endpoint, no active transaction, and 18 attack participants when the Small died or 19 when a Big died.

This audit tests 44 Big + one admitted Small and transaction safety. It does not replace the maintained 44 Big + two late Small saturation/20-attack test, the ordinary six-wave tests, or Unity runtime validation. Passing these finite cases is not a completeness proof for arbitrary multi-agent puzzles.
