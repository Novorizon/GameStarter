using System;
using System.Collections.Generic;
using NavigationLab.Core;

namespace NavigationLab.Candidates.EWarcraft3
{
    /// <summary>Independent double precision spatial checks used by this controller, not the test oracle.</summary>
    internal sealed class PathingGeometry
    {
        private readonly WorldDefinition world;
        private readonly Dictionary<int, bool[]> clearance = new Dictionary<int, bool[]>();

        internal PathingGeometry(WorldDefinition world)
        {
            this.world = world;
        }

        internal bool CanStand(int cell, int radius)
        {
            if (cell < 0 || cell >= world.CellCount)
            {
                return false;
            }

            if (!clearance.TryGetValue(radius, out var cells))
            {
                cells = new bool[world.CellCount];
                for (int current = 0; current < cells.Length; current++)
                {
                    Point2 center = world.Center(current);
                    cells[current] = world.IsWalkable(current) && ClearSegment(center, center, radius);
                }

                clearance.Add(radius, cells);
            }

            return cells[cell];
        }

        internal bool CanHold(int cell, UnitProfile profile, int targetId)
        {
            if (!world.HoldingAllowed(cell) || !CanStand(cell, profile.RadiusMm))
            {
                return false;
            }

            var target = world.Target(targetId);
            return CanAttack(cell, profile, targetId)
                || Point2.Distance(world.Center(cell), target.Center) <= target.RadiusMm + world.GatheringDistanceMm + 0.001;
        }

        internal bool CanAttack(int cell, UnitProfile profile, int targetId)
        {
            if (!CanStand(cell, profile.RadiusMm))
            {
                return false;
            }

            var target = world.Target(targetId);
            return Point2.Distance(world.Center(cell), target.Center) <= target.RadiusMm + profile.RadiusMm + profile.AttackRangeMm + 0.001
                && ClearSegment(world.Center(cell), target.Center, 0, true);
        }

        internal IEnumerable<int> Neighbours(int cell, int radius)
        {
            int x = cell % world.Width;
            int y = cell / world.Width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if ((dx == 0 && dy == 0) || x + dx < 0 || x + dx >= world.Width || y + dy < 0 || y + dy >= world.Height)
                    {
                        continue;
                    }

                    int next = (y + dy) * world.Width + x + dx;
                    if (CanStand(next, radius) && ClearSegment(world.Center(cell), world.Center(next), radius))
                    {
                        yield return next;
                    }
                }
            }
        }

        internal bool ClearSegment(Point2 from, Point2 to, double radius, bool ignoreTargets = false)
        {
            double left = world.Origin.X + radius;
            double bottom = world.Origin.Y + radius;
            double right = world.Origin.X + world.Width * world.CellSizeMm - radius;
            double top = world.Origin.Y + world.Height * world.CellSizeMm - radius;
            if (from.X < left - .001 || from.Y < bottom - .001 || from.X > right + .001 || from.Y > top + .001
                || to.X < left - .001 || to.Y < bottom - .001 || to.X > right + .001 || to.Y > top + .001)
            {
                return false;
            }

            if (!ignoreTargets)
            {
                foreach (var target in world.Targets)
                {
                    double distance = target.RadiusMm + radius - .001;
                    if (PointSegmentSquared(target.Center, from, to) < distance * distance)
                    {
                        return false;
                    }
                }
            }

            int minX = Math.Max(0, (int)Math.Floor((Math.Min(from.X, to.X) - radius - world.Origin.X) / world.CellSizeMm));
            int minY = Math.Max(0, (int)Math.Floor((Math.Min(from.Y, to.Y) - radius - world.Origin.Y) / world.CellSizeMm));
            int maxX = Math.Min(world.Width - 1, (int)Math.Floor((Math.Max(from.X, to.X) + radius - world.Origin.X) / world.CellSizeMm));
            int maxY = Math.Min(world.Height - 1, (int)Math.Floor((Math.Max(from.Y, to.Y) + radius - world.Origin.Y) / world.CellSizeMm));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (world.IsWalkable(y * world.Width + x))
                    {
                        continue;
                    }

                    double x0 = world.Origin.X + x * world.CellSizeMm;
                    double y0 = world.Origin.Y + y * world.CellSizeMm;
                    double squared = SegmentRectangleSquared(from, to, x0, y0, x0 + world.CellSizeMm, y0 + world.CellSizeMm);
                    if (radius <= 0 ? squared < .000001 : squared < (radius - .001) * (radius - .001))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static double PointSegmentSquared(Point2 point, Point2 from, Point2 to)
        {
            Point2 edge = to - from;
            double progress = edge.LengthSquared <= .00000001 ? 0 : Clamp(Point2.Dot(point - from, edge) / edge.LengthSquared, 0, 1);
            return (point - (from + edge * progress)).LengthSquared;
        }

        internal static double MovingPairSquared(Point2 first, Point2 firstNext, Point2 second, Point2 secondNext)
        {
            return PointSegmentSquared(default, first - second, firstNext - secondNext);
        }

        private static double SegmentRectangleSquared(Point2 a, Point2 b, double x0, double y0, double x1, double y1)
        {
            double low = 0;
            double high = 1;
            if (Clip(a.X, b.X - a.X, x0, x1, ref low, ref high) && Clip(a.Y, b.Y - a.Y, y0, y1, ref low, ref high))
            {
                return 0;
            }

            double result = Math.Min(PointRectangleSquared(a, x0, y0, x1, y1), PointRectangleSquared(b, x0, y0, x1, y1));
            result = Math.Min(result, PointSegmentSquared(new Point2(x0, y0), a, b));
            result = Math.Min(result, PointSegmentSquared(new Point2(x1, y0), a, b));
            result = Math.Min(result, PointSegmentSquared(new Point2(x0, y1), a, b));
            return Math.Min(result, PointSegmentSquared(new Point2(x1, y1), a, b));
        }

        private static double PointRectangleSquared(Point2 point, double x0, double y0, double x1, double y1)
        {
            double dx = Math.Max(x0 - point.X, Math.Max(0, point.X - x1));
            double dy = Math.Max(y0 - point.Y, Math.Max(0, point.Y - y1));
            return dx * dx + dy * dy;
        }

        private static bool Clip(double origin, double delta, double min, double max, ref double low, ref double high)
        {
            if (Math.Abs(delta) < .00000001)
            {
                return origin >= min && origin <= max;
            }

            double first = (min - origin) / delta;
            double last = (max - origin) / delta;
            if (first > last)
            {
                double temporary = first;
                first = last;
                last = temporary;
            }

            low = Math.Max(low, first);
            high = Math.Min(high, last);
            return low <= high;
        }

        private static double Clamp(double value, double min, double max)
        {
            return Math.Max(min, Math.Min(max, value));
        }
    }
}
