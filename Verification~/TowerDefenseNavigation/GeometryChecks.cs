using System;
using System.Collections.Generic;

internal static class GeometryChecks
{
    public static void VerifyOracle()
    {
        var a0 = new UnitObservation(1, new Cell(0, 0), NavigationState.Moving, 0, 1, 0, 0, 400);
        var a1 = a0 with { X = 1000 };
        var b0 = new UnitObservation(2, new Cell(1, 0), NavigationState.Moving, 0, 1, 1000, 0, 400);
        var b1 = b0 with { Y = 1000 };
        bool collisionDetected = false;
        try
        {
            VerifyUnitClearance(new[] { a1, b1 }, new Dictionary<int, UnitObservation> { [1] = a0, [2] = b0 });
        }
        catch (InvalidOperationException)
        {
            collisionDetected = true;
        }

        if (!collisionDetected || PointSegment(250, 100, 0, 0, 500, 0) != 10000
            || SegmentRectangleDistanceSquared(-500, 500, 1500, 500, 0, 0, 1000, 1000) != 0
            || SegmentRectangleDistanceSquared(-500, 1500, 1500, 1500, 0, 0, 1000, 1000) != 250000)
        {
            throw new InvalidOperationException("Independent swept geometry oracle failed its known counterexamples.");
        }

        VerifyUnitClearance(
            new[] { a1 with { Radius = 250 }, b1 with { Radius = 250 } },
            new Dictionary<int, UnitObservation> { [1] = a0 with { Radius = 250 }, [2] = b0 with { Radius = 250 } });
        VerifyUnitClearance(
            new[] { a1 with { Radius = 250 }, b0 with { X = 0, Radius = 250 } },
            new Dictionary<int, UnitObservation> { [1] = a0 with { Radius = 250 } });
    }

    public static void VerifyUnitClearance(IReadOnlyList<UnitObservation> units, IReadOnlyDictionary<int, UnitObservation> previous)
    {
        var buckets = new Dictionary<Cell, List<UnitObservation>>();
        var checkedPairs = new HashSet<(int, int)>();
        foreach (var unit in units)
        {
            var before = previous.TryGetValue(unit.Id, out var old) ? old : unit;
            int left = (int)Math.Floor((Math.Min(before.X, unit.X) - unit.Radius) / 1000d);
            int right = (int)Math.Floor((Math.Max(before.X, unit.X) + unit.Radius) / 1000d);
            int bottom = (int)Math.Floor((Math.Min(before.Y, unit.Y) - unit.Radius) / 1000d);
            int top = (int)Math.Floor((Math.Max(before.Y, unit.Y) + unit.Radius) / 1000d);
            for (int y = bottom; y <= top; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    var key = new Cell(x, y);
                    if (!buckets.TryGetValue(key, out var neighbors))
                    {
                        neighbors = new List<UnitObservation>();
                        buckets.Add(key, neighbors);
                    }

                    foreach (var other in neighbors)
                    {
                        var pair = unit.Id < other.Id ? (unit.Id, other.Id) : (other.Id, unit.Id);
                        if (!checkedPairs.Add(pair))
                        {
                            continue;
                        }

                        bool existed = previous.ContainsKey(unit.Id);
                        bool otherExisted = previous.TryGetValue(other.Id, out var prior);
                        var pairBefore = existed && otherExisted ? before : unit;
                        var otherBefore = existed && otherExisted ? prior : other;
                        double relativeX = pairBefore.X - otherBefore.X;
                        double relativeY = pairBefore.Y - otherBefore.Y;
                        double velocityX = unit.X - pairBefore.X - (other.X - otherBefore.X);
                        double velocityY = unit.Y - pairBefore.Y - (other.Y - otherBefore.Y);
                        double speedSquared = velocityX * velocityX + velocityY * velocityY;
                        double t = speedSquared == 0 ? 0 : Math.Clamp(-(relativeX * velocityX + relativeY * velocityY) / speedSquared, 0, 1);
                        double dx = relativeX + velocityX * t;
                        double dy = relativeY + velocityY * t;
                        long radius = unit.Radius + other.Radius;
                        if (dx * dx + dy * dy < radius * radius - .001)
                        {
                            throw new InvalidOperationException("Swept collision between units " + unit.Id + " and " + other.Id
                                + " at interval fraction " + t.ToString("0.000") + ", distance=" + Math.Sqrt(dx * dx + dy * dy).ToString("0.000"));
                        }
                    }

                    neighbors.Add(unit);
                }
            }
        }
    }

    public static void VerifyObstacleClearance(NavigationScenario scenario, UnitObservation before, UnitObservation current)
    {
        int left = (int)Math.Floor((Math.Min(before.X, current.X) - current.Radius) / 1000d);
        int right = (int)Math.Floor((Math.Max(before.X, current.X) + current.Radius) / 1000d);
        int bottom = (int)Math.Floor((Math.Min(before.Y, current.Y) - current.Radius) / 1000d);
        int top = (int)Math.Floor((Math.Max(before.Y, current.Y) + current.Radius) / 1000d);
        for (int y = bottom; y <= top; y++)
        {
            for (int x = left; x <= right; x++)
            {
                if (scenario.Open(new Cell(x, y)))
                {
                    continue;
                }

                double distance = SegmentRectangleDistanceSquared(before.X, before.Y, current.X, current.Y, x * 1000, y * 1000, (x + 1) * 1000, (y + 1) * 1000);
                if (distance < (long)current.Radius * current.Radius - .001)
                {
                    throw new InvalidOperationException("Unit " + current.Id + " swept through obstacle " + new Cell(x, y));
                }
            }
        }
    }

    public static void VerifyTargetClearance(NavigationScenario scenario, UnitObservation before, UnitObservation current)
    {
        int targetX = scenario.Target.X * 1000 + 500;
        int targetY = scenario.Target.Y * 1000 + 500;
        long required = 1000 + current.Radius;
        if (PointSegment(targetX, targetY, before.X, before.Y, current.X, current.Y) < required * required - .001)
        {
            throw new InvalidOperationException("Unit " + current.Id + " swept through the target footprint.");
        }
    }

    public static void VerifyAttackLine(NavigationScenario scenario, UnitObservation unit)
    {
        int targetX = scenario.Target.X * 1000 + 500;
        int targetY = scenario.Target.Y * 1000 + 500;
        for (int y = Math.Min(unit.Cell.Y, scenario.Target.Y); y <= Math.Max(unit.Cell.Y, scenario.Target.Y); y++)
        {
            for (int x = Math.Min(unit.Cell.X, scenario.Target.X); x <= Math.Max(unit.Cell.X, scenario.Target.X); x++)
            {
                if (!scenario.Open(new Cell(x, y))
                    && SegmentRectangleDistanceSquared(unit.X, unit.Y, targetX, targetY, x * 1000, y * 1000, (x + 1) * 1000, (y + 1) * 1000) == 0)
                {
                    throw new InvalidOperationException("Unit " + unit.Id + " attacks through obstacle " + new Cell(x, y));
                }
            }
        }
    }

    private static double SegmentRectangleDistanceSquared(double ax, double ay, double bx, double by, double left, double bottom, double right, double top)
    {
        double first = 0;
        double last = 1;
        double dx = bx - ax;
        double dy = by - ay;
        if (Clip(-dx, ax - left, ref first, ref last) && Clip(dx, right - ax, ref first, ref last)
            && Clip(-dy, ay - bottom, ref first, ref last) && Clip(dy, top - ay, ref first, ref last))
        {
            return 0;
        }

        return Math.Min(
            Math.Min(PointRectangle(ax, ay, left, bottom, right, top), PointRectangle(bx, by, left, bottom, right, top)),
            Math.Min(
                Math.Min(PointSegment(left, bottom, ax, ay, bx, by), PointSegment(left, top, ax, ay, bx, by)),
                Math.Min(PointSegment(right, bottom, ax, ay, bx, by), PointSegment(right, top, ax, ay, bx, by))));
    }

    private static bool Clip(double direction, double distance, ref double first, ref double last)
    {
        if (direction == 0)
        {
            return distance >= 0;
        }

        double t = distance / direction;
        if (direction < 0)
        {
            if (t > last)
            {
                return false;
            }

            first = Math.Max(first, t);
        }
        else
        {
            if (t < first)
            {
                return false;
            }

            last = Math.Min(last, t);
        }

        return true;
    }

    private static double PointRectangle(double x, double y, double left, double bottom, double right, double top)
    {
        double dx = x - Math.Clamp(x, left, right);
        double dy = y - Math.Clamp(y, bottom, top);
        return dx * dx + dy * dy;
    }

    private static double PointSegment(double x, double y, double ax, double ay, double bx, double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double square = dx * dx + dy * dy;
        double t = square == 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / square, 0, 1);
        double offsetX = x - (ax + t * dx);
        double offsetY = y - (ay + t * dy);
        return offsetX * offsetX + offsetY * offsetY;
    }
}
