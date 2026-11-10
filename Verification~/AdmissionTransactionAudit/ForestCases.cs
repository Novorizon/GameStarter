using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace AdmissionTransactionAudit;

internal static class ForestCases
{
    internal static void Run(Audit audit)
    {
        // A smaller budget can change fallback selection. Probe real outcomes rather than assuming cost - 1 fails.
        var fast = Calibrate(audit, 500);
        var slow = Calibrate(audit, 250);
        Audit.Require(fast.InitialState == slow.InitialState, "Successful calibration fixtures did not begin in the same state");
        int[] budgets = SelectRollbackBudgets(audit, fast, slow);
        foreach (int limit in budgets)
        {
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
            foreach (int limit in budgets)
            {
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

    private static int[] SelectRollbackBudgets(Audit audit, Calibration fast, Calibration slow)
    {
        int common = checked((int)Math.Min(fast.Expanded, slow.Expanded));
        var candidates = new[] { 6400, common - 1, common - 128, common - 64, common - 32, common - 16, common - 8, common - 4, common - 2, common - 256 };
        var selected = new List<int>();
        foreach (int limit in candidates.Where(value => value >= 100 && value < common).Distinct())
        {
            bool suitable = true;
            foreach (var baseline in new[] { fast, slow })
            {
                var crowd = Fixtures.Forest(limit);
                Audit.Require(CrowdSnapshot.InitialStateHash(crowd) == baseline.InitialState, "Probe budget changed the initial forest fixture");
                var snapshot = CrowdSnapshot.Capture(crowd);
                long expanded = crowd.TotalExpandedNodes;
                long gateCells = crowd.AdmissionGateExpandedCells;
                int budgetFailures = crowd.AdmissionGateBudgetFailures;
                long tick = crowd.CurrentTick;
                var positions = crowd.Agents.ToDictionary(actor => actor.Id, actor => actor.Position);
                bool accepted = crowd.TrySpawn(45, crowd.Grid.Center(crowd.Grid.SpawnCells[0]), baseline.Speed, 180, 450);
                long delta = crowd.TotalExpandedNodes - expanded;
                long gateDelta = crowd.AdmissionGateExpandedCells - gateCells;
                Audit.Require(delta <= limit, "Budget probe exceeded configured shared search budget");
                bool rolledBack = !accepted && !crowd.RecoveryActive && crowd.LastFailure == NavigationFailure.SearchLimitExceeded;
                if (rolledBack)
                {
                    snapshot.AssertUnchanged(crowd, "forest-budget-probe");
                    Audit.Require(delta == limit && crowd.AdmissionGateBudgetFailures == budgetFailures + 1, "Probe did not exhaust its gate budget");
                }
                else if (accepted)
                {
                    // An improved/fallback search succeeding at this budget is valid; it is not rollback evidence.
                    Audit.Require(crowd.CurrentTick == tick && crowd.Agents.Count == 45, "Accepted budget probe changed clock or actual actor count");
                    foreach (var actor in crowd.Agents.Where(actor => actor.Id != 45))
                    {
                        Audit.Require(actor.Position.Equals(positions[actor.Id]), "Accepted probe moved a body before Step");
                    }

                    CrowdSnapshot.AssertNoGhosts(crowd);
                }

                bool reachesSameLayout = gateDelta == baseline.GateExpanded && delta > gateDelta;
                suitable &= rolledBack && reachesSameLayout;
                audit.Record(new
                {
                    test = "forest-budget-candidate-probe",
                    limit,
                    speed = baseline.Speed,
                    accepted,
                    failure = crowd.LastFailure.ToString(),
                    expanded = delta,
                    gateExpanded = gateDelta,
                    exactRollback = rolledBack,
                    reachesSameLayout,
                    suitableForRollbackRegression = rolledBack && reachesSameLayout
                });
            }

            if (suitable)
            {
                selected.Add(limit);
                if (selected.Count == 2)
                {
                    return selected.ToArray();
                }
            }
        }

        throw new InvalidOperationException("Could not find two empirically failing timed-transaction budgets; rollback coverage is required.");
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
