using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace AdmissionTransactionAudit;

internal static class CacheCases
{
    internal static void Run(Audit audit)
    {
        // This congestion is feasible at a larger budget. Only the explicit low-budget request must fail.
        CheckRetry(audit, Fixtures.Synthetic(100), 18, 100, true, "budget-congestion");
        // One declared exclusive final cell is occupied; this certifies logical capacity, not physical plane capacity.
        CheckRetry(audit, Fixtures.SaturatedSingleSlot(6400), 2, 6400, false, "saturated-one-slot");

        var trafficFixture = Fixtures.SaturatedSingleSlot(6400);
        audit.Reject("traffic-baseline", trafficFixture, 2, 500, 6400, false, NavigationFailure.CapacityExceeded, allowNoSearch: true);
        int traffic = CrowdSnapshot.Field<int>(trafficFixture, "trafficRevision");
        CrowdSnapshot.SetField(trafficFixture, "trafficRevision", traffic + 1);
        // A real Step processes the deliberately injected traffic event before the transaction snapshot.
        trafficFixture.Step(trafficFixture.CurrentTick + 1);
        audit.Reject("whitebox-traffic-revision-wakeup", trafficFixture, 2, 500, 6400, false, NavigationFailure.CapacityExceeded, allowNoSearch: true);
    }

    private static void CheckRetry(Audit audit, NavigationCrowd crowd, int id, int limit, bool budgetFailure, string name)
    {
        var failure = budgetFailure ? NavigationFailure.SearchLimitExceeded : NavigationFailure.CapacityExceeded;
        audit.Reject(name + "-first", crowd, id, 500, limit, false, failure, allowNoSearch: !budgetFailure);
        audit.Reject(name + "-same-tick", crowd, id, 500, limit, true, failure);
        long rejectedAt = crowd.CurrentTick;
        while (crowd.CurrentTick < rejectedAt + 63)
        {
            crowd.Step(crowd.CurrentTick + 1);
        }

        audit.Reject(name + "-backoff-63", crowd, id, 500, limit, true, failure);
        crowd.Step(crowd.CurrentTick + 1);
        audit.Reject(name + "-backoff-64", crowd, id, 500, limit, !budgetFailure, failure);
        audit.Reject(name + "-retry-same-tick", crowd, id, 500, limit, true, failure);

        int removed = crowd.Agents.Max(actor => actor.Id);
        long beforeRemoval = crowd.CurrentTick;
        Audit.Require(crowd.Remove(removed), "Public Remove failed");
        long expanded = crowd.TotalExpandedNodes;
        int cacheHits = crowd.CachedAdmissionGateRejections;
        bool accepted = crowd.TrySpawn(id, crowd.Grid.Center(crowd.Grid.SpawnCells[0]), 500, 180, 450);
        Audit.Require(accepted, "Public Remove did not immediately wake pending request: " + name);
        Audit.Require(crowd.CurrentTick == beforeRemoval, "Remove/wakeup advanced simulation clock");
        Audit.Require(crowd.TotalExpandedNodes > expanded, "Remove/wakeup reused stale rejection");
        Audit.Require(crowd.CachedAdmissionGateRejections == cacheHits, "Remove/wakeup hit stale gate cache");
        CrowdSnapshot.AssertNoGhosts(crowd);
        audit.Record(new
        {
            test = name + "-public-remove-immediate-wakeup",
            limit,
            removed,
            accepted,
            expanded = crowd.TotalExpandedNodes - expanded,
            tick = crowd.CurrentTick
        });
    }
}
