using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace AdmissionTransactionAudit;

internal static class Fixtures
{
    internal static NavigationCrowd Synthetic(int limit)
    {
        const int width = 9;
        const int height = 9;
        const int spawn = 28;
        var target = new Position2(2250, 2000);
        var stand = Enumerable.Repeat(true, width * height).ToArray();
        var holding = Enumerable.Range(0, stand.Length).Select(cell =>
        {
            long x = cell % width * 500 + 250 - target.X;
            long y = cell / width * 500 + 250 - target.Y;
            return x * x + y * y <= 1900L * 1900;
        }).ToArray();
        holding[spawn] = false;
        var grid = new NavigationGrid(width, height, 500, new Position2(0, 0), stand, target, 1100, holding, new[] { spawn });
        var crowd = Create(grid, limit, 1000);
        Fill(crowd, 17);
        return crowd;
    }

    internal static NavigationCrowd Forest(int limit)
    {
        string map = Path.Combine(AppContext.BaseDirectory, "ForestNavigation.bytes");
        var grid = NavigationLayout.Parse(File.ReadAllText(map));
        var crowd = Create(grid, limit, 4096);
        Fill(crowd, 44);
        Audit.Require(crowd.CountAttackSlots(250, 550) == 18, "Forest fixture must use all legal Big attack cells");
        return crowd;
    }

    internal static NavigationCrowd SaturatedSingleSlot(int limit)
    {
        const int width = 9;
        const int height = 9;
        const int spawn = 4;
        const int goal = 58;
        var stand = Enumerable.Repeat(true, width * height).ToArray();
        var holding = new bool[stand.Length];
        holding[goal] = true;
        var grid = new NavigationGrid(width, height, 500, new Position2(0, 0), stand, new Position2(2250, 2250), 500, holding, new[] { spawn });
        var crowd = Create(grid, limit, 1000);
        Fill(crowd, 1);
        Audit.Require(crowd.CountAttackSlots(250, 550) + crowd.CountGatherSlots(250, 550) == 1, "Big fixture must have one legal holding cell");
        Audit.Require(crowd.CountAttackSlots(180, 450) + crowd.CountGatherSlots(180, 450) == 1, "Small fixture must have one legal holding cell");
        bool attackCompatible = crowd.CountAttackSlots(250, 550) == 1 && crowd.CountAttackSlots(180, 450) == 1;
        Audit.Require(attackCompatible, "The unique holding cell must have real attack reach and clearance for both profiles");
        Audit.Require(crowd.Agents[0].GoalCell == goal, "The only legal holding cell must already be occupied");
        return crowd;
    }

    private static NavigationCrowd Create(NavigationGrid grid, int limit, int horizon)
    {
        var crowd = new NavigationCrowd(grid, planningHorizon: horizon, searchLimit: limit);
        crowd.RegisterProfile(250, 550);
        crowd.RegisterProfile(180, 450);
        return crowd;
    }

    private static void Fill(NavigationCrowd crowd, int count)
    {
        int next = 1;
        for (int step = 0; step < 30000 && (next <= count || !Settled(crowd)); step++)
        {
            if (next <= count && Settled(crowd) && crowd.TrySpawn(next, crowd.Grid.Center(crowd.Grid.SpawnCells[0]), 500, 250, 550))
            {
                next++;
            }

            crowd.Step(crowd.CurrentTick + 1);
        }

        Audit.Require(next == count + 1 && crowd.Agents.Count == count && Settled(crowd), "Could not fill and settle fixture: " + count);
        CrowdSnapshot.AssertNoGhosts(crowd);
    }

    internal static bool Settled(NavigationCrowd crowd)
    {
        return !crowd.RecoveryActive && crowd.Agents.All(actor => actor.Status != NavigationAgentStatus.Navigating);
    }
}
