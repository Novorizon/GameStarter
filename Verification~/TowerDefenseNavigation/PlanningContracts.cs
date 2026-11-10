using System;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal sealed class PlanningContractReport
{
    public bool QueryPurityVerified { get; set; }
    public int QueryTraceTicks { get; set; }
    public int QueryTraceCharacters { get; set; }
    public string QueryTraceSha256 { get; set; } = "";
    public bool UnknownQueriedProfileRejected { get; set; }
    public bool AdmissionCacheVerified { get; set; }
    public int CapacityAdmitted { get; set; }
    public int IdleTicks { get; set; }
    public long IdleExpandedNodes { get; set; }
    public int CachedAdmissionRejections { get; set; }
    public int AdmittedAfterDeath { get; set; }
    public long ElapsedMilliseconds { get; set; }
}

internal static class PlanningContracts
{
    public static PlanningContractReport Verify()
    {
        var stopwatch = Stopwatch.StartNew();
        var report = new PlanningContractReport();
        string plain = QueryTrace(false);
        string queried = QueryTrace(true);
        Require(plain == queried, "Read-only slot queries changed the complete tick-by-tick trajectory.");
        report.QueryPurityVerified = true;
        report.UnknownQueriedProfileRejected = true;
        report.QueryTraceTicks = 800;
        report.QueryTraceCharacters = plain.Length;
        report.QueryTraceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(plain))).ToLowerInvariant();
        VerifyAdmissionCache(report);
        report.ElapsedMilliseconds = stopwatch.ElapsedMilliseconds;
        return report;
    }

    private static string QueryTrace(bool inspect)
    {
        const int width = 35;
        int start = 17 * width + 1;
        var grid = new NavigationGrid(
            width,
            width,
            1000,
            new Position2(0, 0),
            Enumerable.Repeat(true, width * width).ToArray(),
            new Position2(17500, 17500),
            1000,
            spawnCells: new[] { start });
        var crowd = new NavigationCrowd(grid, gatheringDistance: 6000);
        crowd.RegisterProfile(250, 1000);
        int next = 1;
        var trace = new StringBuilder();
        if (inspect)
        {
            crowd.CountAttackSlots(250, 4000);
            crowd.CountGatherSlots(490, 2500);
        }

        for (int tick = 0; tick < 800; tick++)
        {
            if (inspect && tick % 5 == 0)
            {
                crowd.CountAttackSlots(250, 4000);
                crowd.CountGatherSlots(490, 2500);
            }

            if (next <= 48 && crowd.TrySpawn(next, grid.Center(start), 1000, 250, 1000))
            {
                next++;
            }

            if (tick == 400)
            {
                Require(
                    crowd.Remove(crowd.Agents.First(agent => agent.Status == NavigationAgentStatus.Attacking).Id),
                    "Query probe cannot remove an attacker.");
            }

            trace.Append(tick).Append(':').Append(crowd.TotalExpandedNodes).Append(':').Append(crowd.ReplanCount).Append(':')
                .Append(crowd.LastFailure).Append(';');
            foreach (var agent in crowd.Agents.OrderBy(agent => agent.Id))
            {
                trace.Append(agent.Id).Append(':').Append(agent.Position.X).Append(':').Append(agent.Position.Y).Append(':')
                    .Append(agent.GoalCell).Append(':').Append(agent.Status).Append(':').Append(agent.ArrivalTick).Append(';');
            }

            crowd.Step(tick + 1);
        }

        Require(next == 49 && crowd.Agents.Count == 47, "Read-only query probe did not admit and retain its complete cohort.");
        bool unregistered = false;
        try
        {
            crowd.TrySpawn(1000, grid.Center(start), 1000, 250, 4000);
        }
        catch (InvalidOperationException)
        {
            unregistered = true;
        }

        Require(unregistered && crowd.Agents.Count == 47, "Querying an unknown profile implicitly registered or admitted it.");
        return trace.ToString();
    }

    private static void VerifyAdmissionCache(PlanningContractReport report)
    {
        var scenario = ScenarioCatalog.Create(1).Single(value => value.Name == "explicit-capacity-exhaustion");
        var grid = new NavigationGrid(
            scenario.Width,
            scenario.Height,
            1000,
            new Position2(0, 0),
            scenario.Walkable,
            Center(scenario.Target),
            1000,
            spawnCells: scenario.SpawnCells.Select(cell => cell.Y * scenario.Width + cell.X).ToArray());
        var crowd = new NavigationCrowd(grid);
        crowd.RegisterProfile(250, 1000);
        int next = 1;
        for (int tick = 1; tick <= 1000; tick++)
        {
            Retry(crowd, grid.Center(grid.SpawnCells[0]), ref next);
            crowd.Step(tick);
        }

        Require(crowd.Agents.Count > 0 && crowd.Agents.Count < scenario.UnitCount, "Capacity probe did not encounter a finite admission limit.");
        Require(crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating), "Capacity probe did not finish actual gathering.");
        long before = crowd.TotalExpandedNodes;
        int admitted = crowd.Agents.Count;
        for (int tick = 1001; tick <= 1200; tick++)
        {
            Require(!crowd.TrySpawn(next, grid.Center(grid.SpawnCells[0]), 1000, 250, 1000), "A stable cached capacity rejection changed without a command.");
            Require(crowd.LastFailure == NavigationFailure.CapacityExceeded, "A cached permanent admission limit became an ambiguous transient failure.");
            crowd.Step(tick);
            Require(crowd.TotalExpandedNodes == before, "Stable capacity waiting repeated expensive path search.");
        }

        report.CapacityAdmitted = admitted;
        report.IdleTicks = 200;
        report.IdleExpandedNodes = crowd.TotalExpandedNodes - before;
        report.CachedAdmissionRejections = crowd.CachedAdmissionRejections;
        Require(report.CachedAdmissionRejections >= 200, "The repeated capacity requests did not use the rejection cache.");
        Require(crowd.Remove(crowd.Agents.First(agent => agent.Status == NavigationAgentStatus.Attacking).Id), "Cache probe cannot remove an attacker.");
        int old = next;
        for (int tick = 1201; tick <= 1600; tick++)
        {
            Retry(crowd, grid.Center(grid.SpawnCells[0]), ref next);
            crowd.Step(tick);
        }

        Require(next > old && crowd.TotalExpandedNodes > before, "Death did not invalidate the admission cache and permit a new unit.");
        Require(crowd.Agents.Count == admitted - 1 + next - old, "The cache lifecycle lost a unit.");
        Require(crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating), "The post-death replacement did not actually arrive.");
        Require(crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking) == crowd.CountAttackSlots(250, 1000),
            "The attack vacancy was not filled after cache invalidation.");
        report.AdmittedAfterDeath = next - old;
        report.AdmissionCacheVerified = true;
    }

    private static void Retry(NavigationCrowd crowd, Position2 spawn, ref int next)
    {
        if (crowd.TrySpawn(next, spawn, 1000, 250, 1000))
        {
            next++;
            return;
        }

        Require(crowd.LastFailure == NavigationFailure.SpawnOccupied || crowd.LastFailure == NavigationFailure.ReservationBlocked
            || crowd.LastFailure == NavigationFailure.CapacityExceeded, "Unexpected rejection in capacity lifecycle: " + crowd.LastFailure);
    }

    private static Position2 Center(Cell cell) => new Position2(cell.X * 1000 + 500, cell.Y * 1000 + 500);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
