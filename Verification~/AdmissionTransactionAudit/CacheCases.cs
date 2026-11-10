using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace AdmissionTransactionAudit;

internal static class CacheCases
{
    internal static void Run(Audit audit)
    {
        foreach (int limit in new[] { 100, 3200, 6400 })
        {
            var crowd = Fixtures.Synthetic(limit);
            bool budgetFailure = limit < 6400;
            var failure = budgetFailure ? NavigationFailure.SearchLimitExceeded : NavigationFailure.CapacityExceeded;
            audit.Reject("synthetic-first", crowd, 18, 500, limit, false, failure);
            audit.Reject("synthetic-same-tick", crowd, 18, 500, limit, true, failure);
            long rejectedAt = crowd.CurrentTick;
            while (crowd.CurrentTick < rejectedAt + 63)
            {
                crowd.Step(crowd.CurrentTick + 1);
            }

            audit.Reject("synthetic-backoff-63", crowd, 18, 500, limit, true, failure);
            crowd.Step(crowd.CurrentTick + 1);
            audit.Reject("synthetic-backoff-64", crowd, 18, 500, limit, !budgetFailure, failure);
            audit.Reject("synthetic-retry-same-tick", crowd, 18, 500, limit, true, failure);

            int removed = crowd.Agents.Max(actor => actor.Id);
            long beforeRemoval = crowd.CurrentTick;
            Audit.Require(crowd.Remove(removed), "Public Remove failed");
            long expanded = crowd.TotalExpandedNodes;
            int cacheHits = crowd.CachedAdmissionGateRejections;
            bool accepted = crowd.TrySpawn(18, crowd.Grid.Center(crowd.Grid.SpawnCells[0]), 500, 180, 450);
            Audit.Require(accepted, "Public Remove did not immediately wake pending request at limit " + limit);
            Audit.Require(crowd.CurrentTick == beforeRemoval, "Remove/wakeup advanced simulation clock");
            Audit.Require(crowd.TotalExpandedNodes > expanded, "Remove/wakeup reused stale rejection");
            Audit.Require(crowd.CachedAdmissionGateRejections == cacheHits, "Remove/wakeup hit stale gate cache");
            CrowdSnapshot.AssertNoGhosts(crowd);
            audit.Record(new
            {
                test = "public-remove-immediate-wakeup",
                limit,
                removed,
                accepted,
                expanded = crowd.TotalExpandedNodes - expanded,
                tick = crowd.CurrentTick
            });
        }

        var trafficFixture = Fixtures.Synthetic(6400);
        audit.Reject("traffic-baseline", trafficFixture, 18, 500, 6400, false, NavigationFailure.CapacityExceeded);
        int traffic = CrowdSnapshot.Field<int>(trafficFixture, "trafficRevision");
        CrowdSnapshot.SetField(trafficFixture, "trafficRevision", traffic + 1);
        // Process the deliberately injected traffic event before taking the transaction snapshot.
        // Updating each real occupant's traffic marker is normal event handling, not failed-speculation damage.
        trafficFixture.Step(trafficFixture.CurrentTick + 1);
        audit.Reject("whitebox-traffic-revision-wakeup", trafficFixture, 18, 500, 6400, false, NavigationFailure.CapacityExceeded);
    }
}
