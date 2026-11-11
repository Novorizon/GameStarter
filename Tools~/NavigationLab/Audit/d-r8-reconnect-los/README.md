# D R8 reconnection guard audit

This read-only diagnostic binds the original completed R8 open48 failure and exact frozen candidate DLL `fcca5c1ddbef60a8139b47d2067c2bc7d4f7cc277d06c4ba99548bf6001eab4d`. No physical state, goal, request, speed, world or original report is changed.

Actor 3's real point lies in cell 457, whose **centre** is invalid, so the implementation reconnects toward anchor cell 456. The actual entire segment from actor 3 to its original goal 427 is legal: body gap is at least 5.880102 mm; base gap is +0.000000138 mm at the starting contact boundary and grows afterward; closed rectangles are disjoint from the swept-disc AABB. The segment can be executed as 17 steps of 5 ms within the unchanged speed cap.

The exact-DLL planner query records `reconnecting=true`, `physicalCellCenterValid=false`, `CanSegment(actualPoint, originalGoal, r250)=true`, `route=null`, and zero direction. Consequently the R8 recovery condition with `!reconnecting` is false; without that extra condition it is true. A physical cell's invalid centre does not invalidate a verified actual-point-to-goal segment. The existing `directFlowExit = directGoalRecovery || ...` logic already clears the anchor waypoint, so removing just the guard is sufficient for this specific correction.

R9's separate real 1000-tick short run subsequently moved actor 3 and reduced the remaining moving count from eight to two, but this audit does not turn either short prefix or the original R8 failure into a completed-case PASS.

The geometric report also contains an independent actor 1 escape path to its unchanged goal 551, with all other 47 bodies fixed and minimum continuous gap 9.901951 mm. Its initial eastward step is opposite the original preferred waypoint 485 and has a candidate score upper bound below -.2 even after the maximum possible neighbour lateral bonuses. This shows an available reverse escape that the local preference cannot select; it does not prove that the later R9 correction needs another strategy or that all remaining units share this limitation.

Treating long-stationary Moving units as temporary **private planning circles** can be a reasonable same-architecture recovery policy, without altering the original physical world or body states. Each query must exclude its own circle, cache keys/invalidation must include the obstacle set and query identity, movement must promptly release temporary occupancy, and the actual continuous collision checks must remain unchanged. Such a policy is not a general convergence guarantee.

`manifest.json` binds code, query output, independent geometry, the original case/input/trace and the actual copied Core/candidate DLLs. No large trace copy is made.
