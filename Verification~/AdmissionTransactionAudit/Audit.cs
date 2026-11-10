using System.Text.Json;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace AdmissionTransactionAudit;

internal sealed class Audit
{
    internal List<object> Rows { get; } = new List<object>();

    internal void Record(object row)
    {
        Rows.Add(row);
        Console.WriteLine(JsonSerializer.Serialize(row));
    }

    internal Attempt Reject(
        string name,
        NavigationCrowd crowd,
        int id,
        int speed,
        int limit,
        bool cached,
        NavigationFailure expectedFailure,
        bool timedSearch = false)
    {
        var snapshot = CrowdSnapshot.Capture(crowd);
        long expanded = crowd.TotalExpandedNodes;
        long gateCells = crowd.AdmissionGateExpandedCells;
        int cacheHits = crowd.CachedAdmissionGateRejections;
        int budgetFailures = crowd.AdmissionGateBudgetFailures;
        bool accepted = crowd.TrySpawn(id, crowd.Grid.Center(crowd.Grid.SpawnCells[0]), speed, 180, 450);
        long delta = crowd.TotalExpandedNodes - expanded;
        long gateDelta = crowd.AdmissionGateExpandedCells - gateCells;
        int cacheDelta = crowd.CachedAdmissionGateRejections - cacheHits;
        int budgetDelta = crowd.AdmissionGateBudgetFailures - budgetFailures;
        Require(!accepted, name + ": request unexpectedly accepted");
        Require(crowd.LastFailure == expectedFailure, name + ": wrong failure classification " + crowd.LastFailure);
        Require(cached ? delta == 0 && cacheDelta == 1 : delta > 0 && cacheDelta == 0, name + ": wrong cache/search work");
        Require(delta <= limit, name + ": exceeded configured shared gate budget");
        bool budgetExpected = !cached && expectedFailure == NavigationFailure.SearchLimitExceeded;
        Require(budgetDelta == (budgetExpected ? 1 : 0), name + ": incorrect budget failure counter");
        Require(!budgetExpected || delta == limit, name + ": expected to exhaust the complete shared gate budget");
        if (timedSearch)
        {
            Require(delta > gateDelta, name + ": fixture did not reach timed path planning");
        }

        snapshot.AssertUnchanged(crowd, name);
        var result = new Attempt(delta, gateDelta);
        Record(new
        {
            test = name,
            limit,
            speed,
            tick = crowd.CurrentTick,
            accepted,
            failure = crowd.LastFailure.ToString(),
            expanded = delta,
            gateExpanded = gateDelta,
            cacheHits = cacheDelta,
            budgetFailures = budgetDelta,
            exactOccupantsAndReferences = true,
            exactGoalClaims = true,
            exactFutureReservations = true,
            unchangedSemanticRevisions = true
        });
        return result;
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal readonly record struct Attempt(long Expanded, long GateExpanded);
}
