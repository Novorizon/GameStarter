using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace AdmissionTransactionAudit;

internal static class ForestCases
{
    internal static void Run(Audit audit)
    {
        // Derive late-transaction budgets from successful deterministic replays, so an improved search cannot
        // silently turn a supposed rollback case into a successful transaction.
        var fast = Calibrate(audit, 500);
        var slow = Calibrate(audit, 250);
        Audit.Require(fast.InitialState == slow.InitialState, "Successful calibration fixtures did not begin in the same state");
        int common = checked((int)Math.Min(fast.Expanded, slow.Expanded));
        foreach (int margin in new[] { 1, 128 })
        {
            int limit = common - margin;
            Audit.Require(limit > Math.Max(fast.GateExpanded, slow.GateExpanded), "Calibration did not leave a timed-search rollback budget");
            var crowd = Fixtures.Forest(limit);
            Audit.Require(CrowdSnapshot.InitialStateHash(crowd) == fast.InitialState, "Alternating budget changed the initial forest fixture");
            for (int attempt = 0; attempt < 8; attempt++)
            {
                int speed = (attempt & 1) == 0 ? 500 : 250;
                var result = audit.Reject(
                    "forest-alternating-" + attempt,
                    crowd,
                    45,
                    speed,
                    limit,
                    attempt >= 2,
                    NavigationFailure.SearchLimitExceeded,
                    attempt < 2);
                if (attempt < 2)
                {
                    Audit.Require(result.GateExpanded == fast.GateExpanded, "Alternating budget stopped before the calibrated gate layout completed");
                }
            }
        }

        foreach (var baseline in new[] { fast, slow })
        {
            foreach (int margin in new[] { 1, 128 })
            {
                int limit = checked((int)baseline.Expanded) - margin;
                Audit.Require(limit > baseline.GateExpanded, "Rollback fixture did not reach transaction path planning");
                var crowd = Fixtures.Forest(limit);
                Audit.Require(CrowdSnapshot.InitialStateHash(crowd) == baseline.InitialState, "Rollback budget changed the initial forest fixture");
                var result = audit.Reject("forest-exact-rollback", crowd, 45, baseline.Speed, limit, false, NavigationFailure.SearchLimitExceeded, true);
                Audit.Require(result.GateExpanded == baseline.GateExpanded, "Rollback budget stopped before the calibrated gate layout completed");
                audit.Reject("forest-exact-rollback-cached", crowd, 45, baseline.Speed, limit, true, NavigationFailure.SearchLimitExceeded);
                foreach (int id in crowd.Agents.Select(actor => actor.Id).OrderByDescending(id => id).ToArray())
                {
                    Audit.Require(crowd.Remove(id), "Failed to remove real actor " + id);
                    CrowdSnapshot.AssertNoGhosts(crowd);
                }

                Audit.Require(crowd.ReservationCount == 0, "Reservations remained after removing all actors");
                audit.Record(new
                {
                    test = "forest-remove-all-after-rollback",
                    limit,
                    speed = baseline.Speed,
                    removed = 44,
                    remainingActors = crowd.Agents.Count,
                    remainingClaims = CrowdSnapshot.GoalCount(crowd),
                    remainingReservations = crowd.ReservationCount
                });
            }
        }
    }

    private static Calibration Calibrate(Audit audit, int speed)
    {
        var crowd = Fixtures.Forest(200000);
        string initialState = CrowdSnapshot.InitialStateHash(crowd);
        var before = crowd.Agents.ToDictionary(actor => actor.Id, actor => actor.Position);
        long tick = crowd.CurrentTick;
        long expanded = crowd.TotalExpandedNodes;
        long gateCells = crowd.AdmissionGateExpandedCells;
        int recoveries = crowd.AdmissionGateRecoveries;
        bool accepted = crowd.TrySpawn(45, crowd.Grid.Center(crowd.Grid.SpawnCells[0]), speed, 180, 450);
        Audit.Require(accepted && crowd.AdmissionGateRecoveries == recoveries + 1, "Calibration did not commit a real multi-owner gate");
        Audit.Require(crowd.RecoveryActive && crowd.Agents.Count == 45, "Calibration did not retain all real actors");
        Audit.Require(crowd.CurrentTick == tick, "Gate admission advanced the clock");
        foreach (var actor in crowd.Agents.Where(actor => actor.Id != 45))
        {
            Audit.Require(actor.Position.Equals(before[actor.Id]), "Gate admission moved a real actor before simulation Step");
        }

        long cost = crowd.TotalExpandedNodes - expanded;
        long gateCost = crowd.AdmissionGateExpandedCells - gateCells;
        audit.Record(new
        {
            test = "forest-successful-gate-calibration",
            speed,
            accepted,
            expanded = cost,
            gateExpanded = gateCost,
            yielded = crowd.AdmissionGateYieldedActors,
            initialStateSha256 = initialState,
            clockAndActualPositionsUnchanged = true
        });

        var snapshot = CrowdSnapshot.Capture(crowd);
        expanded = crowd.TotalExpandedNodes;
        bool second = crowd.TrySpawn(46, crowd.Grid.Center(crowd.Grid.SpawnCells[0]), speed, 180, 450);
        Audit.Require(!second && crowd.LastFailure == NavigationFailure.ReservationBlocked, "Overlapping transaction must wait for committed recovery");
        Audit.Require(crowd.TotalExpandedNodes == expanded, "Overlapping transaction performed unexpected speculative work");
        snapshot.AssertUnchanged(crowd, "same-tick-committed-transaction-isolation");
        audit.Record(new
        {
            test = "same-tick-committed-transaction-isolation",
            speed,
            accepted = second,
            failure = crowd.LastFailure.ToString(),
            expanded = crowd.TotalExpandedNodes - expanded,
            committedPlanUnchanged = true,
            actualActors = crowd.Agents.Count
        });
        return new Calibration(speed, cost, gateCost, initialState);
    }

    private readonly record struct Calibration(int Speed, long Expanded, long GateExpanded, string InitialState);
}
