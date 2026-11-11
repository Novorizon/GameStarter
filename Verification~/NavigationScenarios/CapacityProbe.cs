using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Game.HotUpdate.TowerDefense.Domain;

internal static class CapacityProbe
{
    // These measurements use the full route buffer and freeze the current bodies. Adjacent cells are alternative positions.
    private const int PositionBandClearance = 1380;
    private const int RouteBuffer = 120;
    private const int UnitGap = 8;

    internal sealed record Result(int FreeStagingCells, int ReachableStagingCells, int FreeAttackCells, int ReachableAttackCells);

    internal sealed record NearResult(
        int WithinThreeMetres,
        int WithinSixMetres,
        int WithinNineMetres,
        int FurthestMillimetres,
        int DistantReserves,
        int NearFreeCells,
        int NearReachableCells,
        int NearLegalStagingCells,
        int NearReachableStagingCells,
        int NearMonotonicStagingCells,
        int ReservesWithMonotonicSpace,
        IReadOnlyList<MoveExample> Examples);

    internal sealed record MoveExample(int EnemyId, int Radius, Position2 From, Position2 To);

    internal sealed record AdjacentResult(int Reserves, int OccupiedSectors, int AvailableSectors, int OccupiedFlanks, int LongestEmptyRun);

    internal static Result Measure(AssaultNavigationMap map, FightConfiguration config, IReadOnlyList<EnemySnapshot> units, EnemySnapshot[] heads)
    {
        int largestRadius = config.Waves.SelectMany(wave => wave.Groups).Max(group => FightSession.EnemyNavigationRadiusMillimetres(group.EnemyId));
        var occupied = units.Select(unit => new Body(unit.Id, unit.Position, FightSession.EnemyNavigationRadiusMillimetres(unit.DefinitionId))).ToArray();
        var freeStaging = new HashSet<Position2>();
        var reachedStaging = new HashSet<Position2>();
        var freeAttack = new HashSet<Position2>();
        var reachedAttack = new HashSet<Position2>();
        foreach (var head in heads)
        {
            int radius = FightSession.EnemyNavigationRadiusMillimetres(head.DefinitionId);
            var grid = new AssaultNavigationGrid(map, radius);
            var body = new Body(head.Id, head.Position, radius);
            var open = new bool[grid.Count];
            var staging = new bool[grid.Count];
            var attack = new bool[grid.Count];
            int stagingClearance = radius + PositionBandClearance;
            for (int node = 0; node < grid.Count; node++)
            {
                var point = grid.Position(node);
                if (!grid.IsOpen(node) || !ClearOfUnits(point, point, body, occupied))
                {
                    continue;
                }

                open[node] = true;
                if (grid.IsAttackPosition(point, FightSession.BaseMeleeReachMillimetres))
                {
                    attack[node] = true;
                    freeAttack.Add(point);
                }
                else if (map.DistanceToBaseSquared(point) >= (long)stagingClearance * stagingClearance
                    && !InRouteBuffer(config.Routes, point, radius + largestRadius + RouteBuffer))
                {
                    staging[node] = true;
                    freeStaging.Add(point);
                }
            }

            foreach (int node in ReachableNodes(grid, body, occupied, open))
            {
                var point = grid.Position(node);
                if (staging[node])
                {
                    reachedStaging.Add(point);
                }

                if (attack[node])
                {
                    reachedAttack.Add(point);
                }
            }
        }

        return new Result(freeStaging.Count, reachedStaging.Count, freeAttack.Count, reachedAttack.Count);
    }

    internal static NearResult MeasureNearBase(AssaultNavigationMap map, FightSession session)
    {
        const long nearSquared = 4500L * 4500;
        var units = session.Enemies;
        var occupied = units.Select(unit => new Body(unit.Id, unit.Position, FightSession.EnemyNavigationRadiusMillimetres(unit.DefinitionId))).ToArray();
        var distant = units.Where(unit => unit.IsStaged && AssaultNavigationMap.DistanceSquared(unit.Position, map.BaseCenter) > nearSquared).ToArray();
        var free = new HashSet<Position2>();
        var legal = new HashSet<Position2>();
        var reachableFree = new HashSet<Position2>();
        var reachable = new HashSet<Position2>();
        var monotonic = new HashSet<Position2>();
        var examples = new List<MoveExample>();
        int reservesWithSpace = 0;
        foreach (var mover in distant)
        {
            int radius = FightSession.EnemyNavigationRadiusMillimetres(mover.DefinitionId);
            var body = new Body(mover.Id, mover.Position, radius);
            var (grid, staging) = session.FixtureStagingGrid(radius);
            var open = new bool[grid.Count];
            for (int node = 0; node < grid.Count; node++)
            {
                var point = grid.Position(node);
                open[node] = grid.IsOpen(node) && ClearOfUnits(point, point, body, occupied);
                if (open[node] && AssaultNavigationMap.DistanceSquared(point, map.BaseCenter) <= nearSquared)
                {
                    free.Add(point);
                    if (staging[node])
                    {
                        legal.Add(point);
                    }
                }
            }

            long bestSquared = long.MaxValue;
            Position2 destination = default;
            int closer = (int)Math.Sqrt(map.DistanceToBaseSquared(body.Position)) - session.FixtureMinimumTighteningDistance;
            foreach (int node in ReachableNodes(grid, body, occupied, open))
            {
                var point = grid.Position(node);
                long distance = AssaultNavigationMap.DistanceSquared(point, map.BaseCenter);
                if (distance > nearSquared)
                {
                    continue;
                }

                reachableFree.Add(point);
                if (!staging[node])
                {
                    continue;
                }

                reachable.Add(point);
                if (closer <= 0 || map.DistanceToBaseSquared(point) > (long)closer * closer)
                {
                    continue;
                }

                monotonic.Add(point);
                if (distance < bestSquared)
                {
                    bestSquared = distance;
                    destination = point;
                }
            }

            if (bestSquared < long.MaxValue)
            {
                reservesWithSpace++;
                examples.Add(new MoveExample(mover.Id, radius, mover.Position, destination));
            }
        }

        var distances = units.Select(unit => AssaultNavigationMap.DistanceSquared(unit.Position, map.BaseCenter)).ToArray();
        return new NearResult(
            distances.Count(distance => distance <= 3000L * 3000),
            distances.Count(distance => distance <= 6000L * 6000),
            distances.Count(distance => distance <= 9000L * 9000),
            distances.Length == 0 ? 0 : (int)Math.Round(Math.Sqrt(distances.Max())),
            distant.Length,
            free.Count,
            reachableFree.Count,
            legal.Count,
            reachable.Count,
            monotonic.Count,
            reservesWithSpace,
            examples);
    }

    internal static AdjacentResult MeasureAdjacentBand(AssaultNavigationMap map, FightSession session)
    {
        const int adjacentEdgeLimit = 850;
        var staged = session.Enemies.Where(unit => unit.IsStaged).ToArray();
        var attackers = session.Enemies.Where(unit => unit.AttackingBase)
            .Select(unit => new Body(unit.Id, unit.Position, FightSession.EnemyNavigationRadiusMillimetres(unit.DefinitionId))).ToArray();
        uint occupiedSectors = 0;
        uint availableSectors = 0;
        uint occupiedFlanks = 0;
        int reserveCount = 0;
        foreach (var reserve in staged)
        {
            int radius = FightSession.EnemyNavigationRadiusMillimetres(reserve.DefinitionId);
            if (!InBand(reserve.Position, radius))
            {
                continue;
            }

            reserveCount++;
            int dx = reserve.Position.X - map.BaseCenter.X;
            int dy = reserve.Position.Y - map.BaseCenter.Y;
            occupiedSectors |= Program.Sector(dx, dy);
            occupiedFlanks |= Math.Abs(dx) >= Math.Abs(dy) ? (dx >= 0 ? 1u : 2u) : (dy >= 0 ? 4u : 8u);
        }

        // Use the terrain grid and road clearance, independently of the planner's staging mask.
        foreach (int radius in staged.Select(unit => FightSession.EnemyNavigationRadiusMillimetres(unit.DefinitionId)).Distinct())
        {
            var (grid, _) = session.FixtureStagingGrid(radius);
            var candidate = new Body(0, default, radius);
            for (int node = 0; node < grid.Count; node++)
            {
                var point = grid.Position(node);
                if (grid.IsOpen(node) && InBand(point, radius) && ClearOfUnits(point, point, candidate, attackers))
                {
                    availableSectors |= Program.Sector(point.X - map.BaseCenter.X, point.Y - map.BaseCenter.Y);
                }
            }
        }

        uint empty = availableSectors & ~occupiedSectors;
        int run = 0;
        int longestRun = 0;
        for (int index = 0; index < 16; index++)
        {
            run = (empty & (1u << (index % 8))) != 0 ? run + 1 : 0;
            longestRun = Math.Max(longestRun, run);
        }

        return new AdjacentResult(
            reserveCount,
            BitOperations.PopCount(occupiedSectors),
            BitOperations.PopCount(availableSectors),
            BitOperations.PopCount(occupiedFlanks),
            Math.Min(longestRun, 8));

        bool InBand(Position2 point, int radius)
        {
            long distance = map.DistanceToBaseSquared(point);
            long minimum = radius + FightSession.BaseMeleeReachMillimetres;
            long maximum = radius + adjacentEdgeLimit;
            return distance >= minimum * minimum && distance <= maximum * maximum && !session.FixtureInProtectedApproach(point, radius);
        }
    }

    private static IEnumerable<int> ReachableNodes(AssaultNavigationGrid grid, Body mover, Body[] occupied, bool[] open)
    {
        var queue = new Queue<int>();
        var visited = new bool[grid.Count];
        int linkDistance = 2 * AssaultNavigationGrid.CellSize;
        for (int node = 0; node < grid.Count; node++)
        {
            var point = grid.Position(node);
            if (open[node] && AssaultNavigationMap.DistanceSquared(mover.Position, point) <= (long)linkDistance * linkDistance
                && grid.CanSweep(mover.Position, point) && ClearOfUnits(mover.Position, point, mover, occupied))
            {
                queue.Enqueue(node);
                visited[node] = true;
            }
        }

        while (queue.TryDequeue(out int node))
        {
            yield return node;
            var point = grid.Position(node);
            for (int direction = 0; direction < AssaultNavigationGrid.NeighborCount; direction++)
            {
                int next = grid.Neighbor(node, direction);
                if (next >= 0 && !visited[next] && open[next] && ClearOfUnits(point, grid.Position(next), mover, occupied))
                {
                    queue.Enqueue(next);
                    visited[next] = true;
                }
            }
        }
    }

    private sealed record Body(int Id, Position2 Position, int Radius);

    private static bool ClearOfUnits(Position2 from, Position2 to, Body mover, Body[] units)
    {
        foreach (var unit in units)
        {
            int clearance = mover.Radius + unit.Radius + UnitGap;
            if (unit.Id != mover.Id && BlocksSegment(unit.Position, from, to, clearance))
            {
                return false;
            }
        }

        return true;
    }

    private static bool InRouteBuffer(RouteDefinition[] routes, Position2 point, int clearance)
    {
        foreach (var route in routes)
        {
            for (int index = 1; index < route.Points.Length; index++)
            {
                if (BlocksSegment(point, route.Points[index - 1], route.Points[index], clearance))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool BlocksSegment(Position2 point, Position2 from, Position2 to, int clearance)
    {
        if (point.X <= Math.Min(from.X, to.X) - clearance || point.X >= Math.Max(from.X, to.X) + clearance
            || point.Y <= Math.Min(from.Y, to.Y) - clearance || point.Y >= Math.Max(from.Y, to.Y) + clearance)
        {
            return false;
        }

        long dx = (long)to.X - from.X;
        long dy = (long)to.Y - from.Y;
        long x = (long)point.X - from.X;
        long y = (long)point.Y - from.Y;
        long lengthSquared = dx * dx + dy * dy;
        long projection = dx * x + dy * y;
        double fraction = lengthSquared == 0 ? 0 : Math.Clamp((double)projection / lengthSquared, 0, 1);
        double offsetX = x - fraction * dx;
        double offsetY = y - fraction * dy;
        return offsetX * offsetX + offsetY * offsetY < (long)clearance * clearance;
    }
}
