using System;
using System.Collections.Generic;
using System.Linq;

internal sealed class CapacityEvidence
{
    public int PhysicalAreaUpperBound { get; set; }
    public int GridEndpointCount { get; set; }
    public int WitnessEndpointCount { get; set; }
    public int WitnessGridMoves { get; set; }
    public double MinimumBodyGapMm { get; set; }
    public List<int[]> WitnessPaths { get; } = new List<int[]>();
}

internal static class PhysicalCapacityProof
{
    private const int CellSize = 1000;
    private const int BaseRadius = 1000;

    /// <summary>
    /// A certificate for the deliberately small 9 x 9, radius-490 fixture only.
    /// The area argument is an upper bound; the separately executed paths are a constructive lower bound.
    /// </summary>
    public static CapacityEvidence Create(NavigationScenario geometry, int radius)
    {
        ValidateFixture(geometry, radius);
        GeometryChecks.VerifyOracle();

        // Every body lies inside the 7 m x 7 m walkable rectangle and outside the base circle.
        // pi > 3.14 gives a conservative rational upper bound, without relying on a rounded value of pi.
        long rectangleArea = 7L * CellSize * 7 * CellSize;
        long numerator = rectangleArea * 100 - 314L * BaseRadius * BaseRadius;
        long denominator = 314L * radius * radius;
        var evidence = new CapacityEvidence { PhysicalAreaUpperBound = checked((int)(numerator / denominator)) };

        int spawn = Index(geometry, geometry.SpawnCells[0]);
        var goals = Enumerable.Range(0, geometry.Width * geometry.Height)
            .Where(cell => cell != spawn && CanStand(geometry, cell, radius) && HoldingAllowed(geometry, cell) && InsideGatheringRange(geometry, cell))
            .ToHashSet();
        evidence.GridEndpointCount = goals.Count;
        Require(evidence.PhysicalAreaUpperBound == 60 && goals.Count == 39, "The small-fixture certificate no longer has its stated bounds.");

        var remaining = new HashSet<int>(goals);
        var evacuation = new List<int[]>();
        while (remaining.Count > 0)
        {
            var parents = FreeReachable(geometry, spawn, remaining, radius);
            int selected = -1;
            int exit = -1;
            foreach (int goal in remaining.OrderBy(value => value))
            {
                foreach (int neighbor in Neighbors(geometry, goal))
                {
                    if (parents.ContainsKey(neighbor) && ClearEdge(geometry, goal, neighbor, radius))
                    {
                        selected = goal;
                        exit = neighbor;
                        break;
                    }
                }

                if (selected >= 0)
                {
                    break;
                }
            }

            Require(selected >= 0, "The independent reverse evacuation could not connect an occupied endpoint to the fixed entrance.");
            var path = new List<int> { selected };
            for (int current = exit; current >= 0; current = parents[current])
            {
                path.Add(current);
            }

            path.Reverse();
            evacuation.Add(path.ToArray());
            remaining.Remove(selected);
        }

        evacuation.Reverse();
        evidence.WitnessPaths.AddRange(evacuation);
        VerifyPaths(geometry, radius, spawn, goals, evidence);
        return evidence;
    }

    private static void ValidateFixture(NavigationScenario geometry, int radius)
    {
        Require(geometry != null, "The capacity fixture is required.");
        Require(geometry.Width == 9 && geometry.Height == 9 && geometry.Target == new Cell(4, 4), "This certificate requires a centered 9 x 9 fixture.");
        Require(radius == 490 && geometry.GatheringDistance == 4000, "This certificate requires radius 490 mm and gathering distance 4000 mm.");
        Require(geometry.Walkable.Length == 81 && geometry.SpawnCells.Length == 1, "This certificate requires one fixed entrance and 81 terrain entries.");
        Require(geometry.HoldingAllowed == null || geometry.HoldingAllowed.Length == 81, "The holding mask must match the terrain dimensions.");
        int spawn = Index(geometry, geometry.SpawnCells[0]);
        for (int y = 0; y < geometry.Height; y++)
        {
            for (int x = 0; x < geometry.Width; x++)
            {
                bool interior = x > 0 && y > 0 && x < 8 && y < 8;
                Require(geometry.Open(new Cell(x, y)) == interior, "The proof requires a closed outer border and an entirely walkable 7 x 7 interior.");
                int cell = y * geometry.Width + x;
                if (interior && cell != spawn && CanStand(geometry, cell, radius))
                {
                    Require(HoldingAllowed(geometry, cell), "Every legal interior endpoint must retain its declared eligibility.");
                }
            }
        }

        Require(geometry.Open(geometry.SpawnCells[0]) && CanStand(geometry, spawn, radius), "The fixed entrance overlaps the boundary or base.");
    }

    private static void VerifyPaths(NavigationScenario geometry, int radius, int spawn, HashSet<int> goals, CapacityEvidence evidence)
    {
        var parked = new Dictionary<int, int>();
        var previous = new Dictionary<int, UnitObservation>();
        double minimumGap = double.PositiveInfinity;
        for (int index = 0; index < evidence.WitnessPaths.Count; index++)
        {
            int id = index + 1;
            int[] path = evidence.WitnessPaths[index];
            Require(path.Length > 1 && path[0] == spawn && goals.Contains(path[path.Length - 1]), "A witness path has invalid endpoints.");
            Require(!parked.Values.Contains(path[path.Length - 1]), "Two witness bodies were assigned one endpoint.");
            int goal = path[path.Length - 1];
            var moving = Observe(geometry, id, spawn, goal, radius, false);
            VerifyFrame(geometry, parked, moving, previous, radius);
            previous = Frame(geometry, parked, moving, radius).ToDictionary(unit => unit.Id);
            minimumGap = Math.Min(minimumGap, BodyGap(geometry, spawn, spawn, parked.Values, radius));
            for (int step = 1; step < path.Length; step++)
            {
                int from = path[step - 1];
                int to = path[step];
                Require(!parked.Values.Contains(to) && ClearEdge(geometry, from, to, radius), "A witness path crosses an occupied or illegal edge.");
                moving = Observe(geometry, id, to, goal, radius, step == path.Length - 1);
                VerifyFrame(geometry, parked, moving, previous, radius);
                previous = Frame(geometry, parked, moving, radius).ToDictionary(unit => unit.Id);
                minimumGap = Math.Min(minimumGap, BodyGap(geometry, from, to, parked.Values, radius));
                evidence.WitnessGridMoves++;
            }

            parked.Add(id, goal);
        }

        Require(parked.Count == goals.Count && parked.Values.ToHashSet().SetEquals(goals), "The witness did not retain every requested endpoint.");
        Require(!double.IsInfinity(minimumGap) && minimumGap >= 0, "The witness has no valid pairwise clearance evidence.");
        evidence.WitnessEndpointCount = parked.Count;
        evidence.MinimumBodyGapMm = minimumGap;
    }

    private static void VerifyFrame(
        NavigationScenario geometry,
        Dictionary<int, int> parked,
        UnitObservation moving,
        Dictionary<int, UnitObservation> previous,
        int radius)
    {
        var units = Frame(geometry, parked, moving, radius);
        GeometryChecks.VerifyUnitClearance(units, previous);
        foreach (var unit in units)
        {
            var before = previous.TryGetValue(unit.Id, out var old) ? old : unit;
            GeometryChecks.VerifyObstacleClearance(geometry, before, unit);
            GeometryChecks.VerifyTargetClearance(geometry, before, unit);
            if (unit.State == NavigationState.Attacking)
            {
                GeometryChecks.VerifyAttackLine(geometry, unit);
            }
        }
    }

    private static UnitObservation[] Frame(NavigationScenario geometry, Dictionary<int, int> parked, UnitObservation moving, int radius)
    {
        return parked.Select(pair => Observe(geometry, pair.Key, pair.Value, pair.Value, radius, true)).Append(moving).ToArray();
    }

    private static UnitObservation Observe(NavigationScenario geometry, int id, int cell, int goal, int radius, bool settled)
    {
        var point = Center(geometry, cell);
        long reach = BaseRadius + radius + 1000L;
        NavigationState state = !settled ? NavigationState.Moving
            : DistanceSquared(geometry, cell) <= reach * reach ? NavigationState.Attacking : NavigationState.Waiting;
        return new UnitObservation(id, new Cell(cell % geometry.Width, cell / geometry.Width), state, goal, 1, point.X, point.Y, radius);
    }

    private static double BodyGap(NavigationScenario geometry, int from, int to, IEnumerable<int> parked, int radius)
    {
        var start = Center(geometry, from);
        var end = Center(geometry, to);
        double gap = double.PositiveInfinity;
        foreach (int cell in parked)
        {
            var other = Center(geometry, cell);
            gap = Math.Min(gap, Math.Sqrt(PointSegmentDistanceSquared(other.X, other.Y, start.X, start.Y, end.X, end.Y)) - 2 * radius);
        }

        return gap;
    }

    private static Dictionary<int, int> FreeReachable(NavigationScenario geometry, int spawn, HashSet<int> occupied, int radius)
    {
        var parents = new Dictionary<int, int> { [spawn] = -1 };
        var queue = new Queue<int>();
        queue.Enqueue(spawn);
        while (queue.Count > 0)
        {
            int cell = queue.Dequeue();
            foreach (int next in Neighbors(geometry, cell))
            {
                if (!occupied.Contains(next) && !parents.ContainsKey(next) && ClearEdge(geometry, cell, next, radius))
                {
                    parents.Add(next, cell);
                    queue.Enqueue(next);
                }
            }
        }

        return parents;
    }

    private static bool CanStand(NavigationScenario geometry, int cell, int radius)
    {
        if (cell < 0 || cell >= geometry.Width * geometry.Height || !geometry.Walkable[cell])
        {
            return false;
        }

        long clearance = BaseRadius + radius;
        return DistanceSquared(geometry, cell) >= clearance * clearance;
    }

    private static bool HoldingAllowed(NavigationScenario geometry, int cell) => geometry.HoldingAllowed == null || geometry.HoldingAllowed[cell];

    private static bool InsideGatheringRange(NavigationScenario geometry, int cell)
    {
        long reach = BaseRadius + geometry.GatheringDistance;
        return DistanceSquared(geometry, cell) <= reach * reach;
    }

    private static bool ClearEdge(NavigationScenario geometry, int from, int to, int radius)
    {
        if (!CanStand(geometry, from, radius) || !CanStand(geometry, to, radius)
            || Math.Abs(from % geometry.Width - to % geometry.Width) + Math.Abs(from / geometry.Width - to / geometry.Width) != 1)
        {
            return false;
        }

        var target = Center(geometry, Index(geometry, geometry.Target));
        var start = Center(geometry, from);
        var end = Center(geometry, to);
        long clearance = BaseRadius + radius;
        return PointSegmentDistanceSquared(target.X, target.Y, start.X, start.Y, end.X, end.Y) >= clearance * clearance;
    }

    private static double PointSegmentDistanceSquared(double px, double py, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double length = dx * dx + dy * dy;
        double t = length == 0 ? 0 : Math.Clamp(((px - ax) * dx + (py - ay) * dy) / length, 0, 1);
        double x = px - ax - t * dx;
        double y = py - ay - t * dy;
        return x * x + y * y;
    }

    private static long DistanceSquared(NavigationScenario geometry, int cell)
    {
        long x = (cell % geometry.Width - geometry.Target.X) * (long)CellSize;
        long y = (cell / geometry.Width - geometry.Target.Y) * (long)CellSize;
        return x * x + y * y;
    }

    private static (int X, int Y) Center(NavigationScenario geometry, int cell)
    {
        return (cell % geometry.Width * CellSize + CellSize / 2, cell / geometry.Width * CellSize + CellSize / 2);
    }

    private static int Index(NavigationScenario geometry, Cell cell) => cell.Y * geometry.Width + cell.X;

    private static IEnumerable<int> Neighbors(NavigationScenario geometry, int cell)
    {
        int x = cell % geometry.Width;
        int y = cell / geometry.Width;
        if (x + 1 < geometry.Width)
        {
            yield return cell + 1;
        }
        if (y + 1 < geometry.Height)
        {
            yield return cell + geometry.Width;
        }
        if (x > 0)
        {
            yield return cell - 1;
        }
        if (y > 0)
        {
            yield return cell - geometry.Width;
        }
    }

    private static void Require(bool condition, string failure)
    {
        if (!condition)
        {
            throw new InvalidOperationException(failure);
        }
    }
}
