using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal sealed class GeometryOracle
{
    private readonly NavigationGrid grid;
    private readonly HashSet<int> verifiedLines = new HashSet<int>();
    internal long CheckedBodySegments { get; private set; }
    internal long CheckedPairs { get; private set; }

    internal GeometryOracle(NavigationGrid grid)
    {
        this.grid = grid;
        Require(
            MinimumDistanceSquared(new Position2(0, 0), new Position2(1000, 0), new Position2(1000, 0), new Position2(1000, 1000)) < 800L * 800,
            "The independent swept oracle missed a known interval collision.");
        Require(
            LineHitsRectangle(new Position2(-1, 1), new Position2(3, 1), 0, 0, 2, 2)
                && !LineHitsRectangle(new Position2(-1, 3), new Position2(3, 3), 0, 0, 2, 2),
            "The independent line/rectangle oracle failed.");
    }

    internal void Verify(NavigationCrowd crowd, Dictionary<int, Request> requests, Dictionary<int, Position2> previous, bool swept)
    {
        Require(
            requests.Count == crowd.Agents.Count && requests.Keys.ToHashSet().SetEquals(crowd.Agents.Select(actor => actor.Id)),
            "An admitted body appeared or disappeared without a request.");
        foreach (var actor in crowd.Agents)
        {
            var request = requests[actor.Id];
            Require(
                actor.Speed == request.Speed && actor.Radius == request.Radius && actor.AttackRange == request.Range,
                "A body changed its original profile.");
            var from = swept && previous.TryGetValue(actor.Id, out var old) ? old : actor.Position;
            var to = actor.Position;
            Require(from.X == to.X || from.Y == to.Y, "A tick crossed multiple cardinal segments.");
            Require(Squared(from, to) <= (long)request.Speed * request.Speed, "A body exceeded the requested speed.");
            long right = grid.Origin.X + (long)grid.Width * grid.CellSize;
            long top = grid.Origin.Y + (long)grid.Height * grid.CellSize;
            Require(
                Math.Min(from.X, to.X) - request.Radius >= grid.Origin.X && Math.Max(from.X, to.X) + request.Radius <= right
                    && Math.Min(from.Y, to.Y) - request.Radius >= grid.Origin.Y && Math.Max(from.Y, to.Y) + request.Radius <= top,
                "A body crossed the world boundary.");
            long baseClearance = grid.TargetRadius + request.Radius;
            Require(
                MinimumDistanceSquared(from, to, grid.TargetCenter, grid.TargetCenter) + .000001 >= baseClearance * baseClearance,
                "A swept body penetrated the base.");
            VerifyTerrain(from, to, request.Radius);
            if (actor.Status != NavigationAgentStatus.Navigating)
            {
                var goal = grid.Center(actor.GoalCell);
                Require(to.X == goal.X && to.Y == goal.Y && grid.HoldingAllowed(actor.GoalCell), "A temporary or remote position was counted as arrived.");
                if (actor.Status == NavigationAgentStatus.Attacking)
                {
                    long reach = grid.TargetRadius + request.Radius + request.Range;
                    Require(Squared(to, grid.TargetCenter) <= reach * reach, "An attack was out of range.");
                    VerifyAttackLine(actor.GoalCell);
                }
                else
                {
                    long gather = grid.TargetRadius + crowd.GatheringDistance;
                    Require(Squared(to, grid.TargetCenter) <= gather * gather, "A body waited outside the declared gathering distance.");
                }
            }

            CheckedBodySegments++;
        }

        for (int first = 0; first < crowd.Agents.Count; first++)
        {
            var a = crowd.Agents[first];
            var a0 = swept && previous.TryGetValue(a.Id, out var oldA) ? oldA : a.Position;
            for (int second = first + 1; second < crowd.Agents.Count; second++)
            {
                var b = crowd.Agents[second];
                var b0 = swept && previous.TryGetValue(b.Id, out var oldB) ? oldB : b.Position;
                long clearance = a.Radius + b.Radius;
                Require(MinimumDistanceSquared(a0, a.Position, b0, b.Position) + .000001 >= clearance * clearance, "Swept bodies overlapped.");
                CheckedPairs++;
            }
        }
    }

    internal static object VerifyContracts()
    {
        Require(
            MinimumDistanceSquared(new Position2(-1000, 0), new Position2(1000, 0), new Position2(0, 500), new Position2(0, 500)) == 250000,
            "A tangent moving disk was classified incorrectly.");
        Require(
            MinimumDistanceSquared(new Position2(-1000, 0), new Position2(1000, 0), new Position2(0, 499), new Position2(0, 499)) < 250000,
            "A one-millimetre overlap was missed.");
        Require(
            MinimumDistanceSquared(new Position2(1000, 0), new Position2(1000, 0), new Position2(0, 0), new Position2(0, 0)) == 1000000,
            "A birth at an API boundary was swept backwards over an earlier interval.");
        Require(LineHitsRectangle(new Position2(-1, 1), new Position2(1, -1), 0, 0, 2, 2), "A blocked-corner attack ray was allowed.");
        var walkable = Enumerable.Repeat(true, 16).ToArray();
        walkable[10] = false;
        var grid = new NavigationGrid(4, 4, 500, new Position2(-1000, -1000), walkable, new Position2(750, 750), 100);
        var oracle = new GeometryOracle(grid);
        oracle.VerifyTerrain(new Position2(-500, -50), new Position2(500, -50), 50);
        ExpectBlocked(() => oracle.VerifyTerrain(new Position2(-500, -49), new Position2(500, -49), 50));
        ExpectBlocked(() => oracle.VerifyTerrain(new Position2(-500, 250), new Position2(750, 250), 1));
        return new { success = true, tangentDisk = true, overlapDisk = true, birthBoundary = true, cornerRay = true, terrainSweep = true };
    }

    private static void ExpectBlocked(Action action)
    {
        bool blocked = false;
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            blocked = true;
        }

        Require(blocked, "The independent obstacle oracle missed a known blocked sweep.");
    }

    private void VerifyTerrain(Position2 from, Position2 to, int radius)
    {
        int left = Math.Max(0, (int)Math.Floor((Math.Min(from.X, to.X) - radius - (double)grid.Origin.X) / grid.CellSize) - 1);
        int right = Math.Min(grid.Width - 1, (int)Math.Floor((Math.Max(from.X, to.X) + radius - (double)grid.Origin.X) / grid.CellSize) + 1);
        int bottom = Math.Max(0, (int)Math.Floor((Math.Min(from.Y, to.Y) - radius - (double)grid.Origin.Y) / grid.CellSize) - 1);
        int top = Math.Min(grid.Height - 1, (int)Math.Floor((Math.Max(from.Y, to.Y) + radius - (double)grid.Origin.Y) / grid.CellSize) + 1);
        for (int y = bottom; y <= top; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (grid.IsWalkable(y * grid.Width + x))
                {
                    continue;
                }

                long wallX = grid.Origin.X + (long)x * grid.CellSize;
                long wallY = grid.Origin.Y + (long)y * grid.CellSize;
                long dx = Math.Max(0, Math.Max(wallX - Math.Max(from.X, to.X), Math.Min(from.X, to.X) - wallX - grid.CellSize));
                long dy = Math.Max(0, Math.Max(wallY - Math.Max(from.Y, to.Y), Math.Min(from.Y, to.Y) - wallY - grid.CellSize));
                Require(dx * dx + dy * dy >= (long)radius * radius, "A swept body penetrated blocked terrain.");
            }
        }
    }

    private void VerifyAttackLine(int cell)
    {
        if (verifiedLines.Contains(cell))
        {
            return;
        }

        int target = grid.CellAt(grid.TargetCenter);
        for (int y = Math.Min(cell / grid.Width, target / grid.Width); y <= Math.Max(cell / grid.Width, target / grid.Width); y++)
        {
            for (int x = Math.Min(cell % grid.Width, target % grid.Width); x <= Math.Max(cell % grid.Width, target % grid.Width); x++)
            {
                if (grid.IsWalkable(y * grid.Width + x))
                {
                    continue;
                }

                int left = grid.Origin.X + x * grid.CellSize;
                int bottom = grid.Origin.Y + y * grid.CellSize;
                Require(
                    !LineHitsRectangle(grid.Center(cell), grid.TargetCenter, left, bottom, left + grid.CellSize, bottom + grid.CellSize),
                    "An attack line crossed blocked terrain.");
            }
        }

        verifiedLines.Add(cell);
    }

    private static bool LineHitsRectangle(Position2 from, Position2 to, double left, double bottom, double right, double top)
    {
        double first = 0;
        double last = 1;
        double dx = (double)to.X - from.X;
        double dy = (double)to.Y - from.Y;
        bool Clip(double direction, double distance)
        {
            if (direction == 0)
            {
                return distance >= 0;
            }

            double fraction = distance / direction;
            if (direction < 0)
            {
                if (fraction > last)
                {
                    return false;
                }

                first = Math.Max(first, fraction);
            }
            else
            {
                if (fraction < first)
                {
                    return false;
                }

                last = Math.Min(last, fraction);
            }

            return true;
        }

        return Clip(-dx, from.X - left) && Clip(dx, right - from.X) && Clip(-dy, from.Y - bottom) && Clip(dy, top - from.Y);
    }

    private static long Squared(Position2 first, Position2 second) => ((long)first.X - second.X) * ((long)first.X - second.X)
        + ((long)first.Y - second.Y) * ((long)first.Y - second.Y);

    private static double MinimumDistanceSquared(Position2 a0, Position2 a1, Position2 b0, Position2 b1)
    {
        double x = (double)a0.X - b0.X;
        double y = (double)a0.Y - b0.Y;
        double dx = (double)a1.X - a0.X - b1.X + b0.X;
        double dy = (double)a1.Y - a0.Y - b1.Y + b0.Y;
        double denominator = dx * dx + dy * dy;
        double fraction = denominator == 0 ? 0 : Math.Clamp(-(x * dx + y * dy) / denominator, 0, 1);
        x += dx * fraction;
        y += dy * fraction;
        return x * x + y * y;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
