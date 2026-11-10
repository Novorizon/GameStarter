using System;
using System.Collections.Generic;

internal static class ParkingLaneCertificate
{
    public static int Verify(NavigationScenario scenario)
    {
        Require(scenario.UnitRadius > 0 && scenario.UnitRadius <= 500, "The 1 m cell parking certificate requires a body radius in 1–500 mm.");
        bool[] holding = scenario.HoldingAllowed ?? throw new InvalidOperationException("A parking certificate requires an explicit holding mask.");
        var backbone = new bool[scenario.Width * scenario.Height];
        var reached = new bool[backbone.Length];
        for (int cell = 0; cell < backbone.Length; cell++)
        {
            backbone[cell] = scenario.Walkable[cell] && !holding[cell] && CanStand(scenario, cell);
        }

        int first = scenario.SpawnCells[0].Y * scenario.Width + scenario.SpawnCells[0].X;
        Require(backbone[first], "The declared entrance must be part of the circulation backbone.");
        var queue = new Queue<int>();
        queue.Enqueue(first);
        reached[first] = true;
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            foreach (int neighbor in Neighbors(scenario, cell))
            {
                if (!reached[neighbor] && backbone[neighbor] && ClearEdge(scenario, cell, neighbor))
                {
                    reached[neighbor] = true;
                    queue.Enqueue(neighbor);
                }
            }
        }

        foreach (var spawn in scenario.SpawnCells)
        {
            Require(reached[spawn.Y * scenario.Width + spawn.X], "Not all entrances remain connected after every holding cell is removed.");
        }

        int capacity = 0;
        long attackReach = 1000 + scenario.UnitRadius + 1000;
        long gatherReach = 1000 + scenario.GatheringDistance;
        for (int cell = 0; cell < backbone.Length; cell++)
        {
            long distance = TargetDistanceSquared(scenario, cell);
            if (!holding[cell] || !CanStand(scenario, cell) || distance > gatherReach * gatherReach)
            {
                continue;
            }

            bool exit = false;
            foreach (int neighbor in Neighbors(scenario, cell))
            {
                exit |= reached[neighbor] && ClearEdge(scenario, cell, neighbor);
            }

            Require(exit, "A parking body has no exit into the connected permanent circulation backbone: cell=" + cell);
            if (distance > attackReach * attackReach)
            {
                capacity++;
            }
        }

        return capacity;
    }

    private static bool CanStand(NavigationScenario scenario, int cell)
    {
        long clearance = 1000 + scenario.UnitRadius;
        return scenario.Walkable[cell] && TargetDistanceSquared(scenario, cell) >= clearance * clearance;
    }

    private static long TargetDistanceSquared(NavigationScenario scenario, int cell)
    {
        long dx = (cell % scenario.Width - scenario.Target.X) * 1000L;
        long dy = (cell / scenario.Width - scenario.Target.Y) * 1000L;
        return dx * dx + dy * dy;
    }

    private static bool ClearEdge(NavigationScenario scenario, int first, int second)
    {
        long ax = (first % scenario.Width - scenario.Target.X) * 1000L;
        long ay = (first / scenario.Width - scenario.Target.Y) * 1000L;
        long bx = (second % scenario.Width - scenario.Target.X) * 1000L;
        long by = (second / scenario.Width - scenario.Target.Y) * 1000L;
        long nearestX = Math.Clamp(0L, Math.Min(ax, bx), Math.Max(ax, bx));
        long nearestY = Math.Clamp(0L, Math.Min(ay, by), Math.Max(ay, by));
        long clearance = 1000 + scenario.UnitRadius;
        return nearestX * nearestX + nearestY * nearestY >= clearance * clearance;
    }

    private static IEnumerable<int> Neighbors(NavigationScenario scenario, int cell)
    {
        int x = cell % scenario.Width;
        int y = cell / scenario.Width;
        if (x > 0)
        {
            yield return cell - 1;
        }
        if (x + 1 < scenario.Width)
        {
            yield return cell + 1;
        }
        if (y > 0)
        {
            yield return cell - scenario.Width;
        }
        if (y + 1 < scenario.Height)
        {
            yield return cell + scenario.Width;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
