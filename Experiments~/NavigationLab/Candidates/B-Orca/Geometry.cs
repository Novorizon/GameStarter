using System;
using NavigationLab.Core;

namespace NavigationLab.Candidates.BOrca
{
    /// <summary>Candidate-side geometry. The runner independently verifies actual positions using its own oracle.</summary>
    internal static class Geometry
    {
        internal static bool Clear(WorldDefinition world, Point2 first, Point2 second, double radius, bool targets = true)
        {
            if (targets)
            {
                foreach (var target in world.Targets)
                {
                    if (PointSegmentSquared(target.Center, first, second) < Square(target.RadiusMm + radius) - .000001)
                    {
                        return false;
                    }
                }
            }

            double cellSize = world.CellSizeMm;
            int left = (int)Math.Floor((Math.Min(first.X, second.X) - radius - world.Origin.X) / cellSize);
            int right = (int)Math.Floor((Math.Max(first.X, second.X) + radius - world.Origin.X) / cellSize);
            int bottom = (int)Math.Floor((Math.Min(first.Y, second.Y) - radius - world.Origin.Y) / cellSize);
            int top = (int)Math.Floor((Math.Max(first.Y, second.Y) + radius - world.Origin.Y) / cellSize);
            for (int y = bottom; y <= top; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    if (Open(world, x, y))
                    {
                        continue;
                    }

                    double x0 = world.Origin.X + x * cellSize;
                    double y0 = world.Origin.Y + y * cellSize;
                    double distance = SegmentBoxSquared(first, second, x0, y0, x0 + cellSize, y0 + cellSize);
                    if (radius < .001 ? distance <= 1e-12 : distance < radius * radius - .000001)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        internal static bool CanHold(WorldDefinition world, int cell, UnitProfile profile, int targetId)
        {
            if (!world.HoldingAllowed(cell) || !Clear(world, world.Center(cell), world.Center(cell), profile.RadiusMm))
            {
                return false;
            }

            var target = world.Target(targetId);
            return CanAttack(world, world.Center(cell), profile, targetId)
                || Point2.Distance(world.Center(cell), target.Center) <= target.RadiusMm + world.GatheringDistanceMm + .000001;
        }

        internal static bool CanAttack(WorldDefinition world, Point2 point, UnitProfile profile, int targetId)
        {
            var target = world.Target(targetId);
            return Point2.Distance(point, target.Center) <= target.RadiusMm + profile.RadiusMm + profile.AttackRangeMm + SimulationProtocol.GeometryToleranceMm
                && Clear(world, point, target.Center, .00001, false);
        }

        internal static bool Open(WorldDefinition world, int x, int y)
        {
            return x >= 0 && y >= 0 && x < world.Width && y < world.Height && world.IsWalkable(y * world.Width + x);
        }

        internal static double PairSweepSquared(Point2 firstBefore, Point2 firstAfter, Point2 secondBefore, Point2 secondAfter)
        {
            return PointSegmentSquared(default, firstBefore - secondBefore, firstAfter - secondAfter);
        }

        internal static double PointSegmentSquared(Point2 point, Point2 first, Point2 second)
        {
            var direction = second - first;
            double t = direction.LengthSquared <= 1e-20 ? 0 : Math.Clamp(Point2.Dot(point - first, direction) / direction.LengthSquared, 0, 1);
            return (point - first - direction * t).LengthSquared;
        }

        private static double SegmentBoxSquared(Point2 first, Point2 second, double left, double bottom, double right, double top)
        {
            var a = new Point2(left, bottom);
            var b = new Point2(right, bottom);
            var c = new Point2(right, top);
            var d = new Point2(left, top);
            if (Intersects(first, second, a, b) || Intersects(first, second, b, c)
                || Intersects(first, second, c, d) || Intersects(first, second, d, a))
            {
                return 0;
            }

            double firstDistance = Square(first.X - Math.Clamp(first.X, left, right)) + Square(first.Y - Math.Clamp(first.Y, bottom, top));
            double secondDistance = Square(second.X - Math.Clamp(second.X, left, right)) + Square(second.Y - Math.Clamp(second.Y, bottom, top));
            double firstEdges = Math.Min(PointSegmentSquared(a, first, second), PointSegmentSquared(b, first, second));
            double secondEdges = Math.Min(PointSegmentSquared(c, first, second), PointSegmentSquared(d, first, second));
            return Math.Min(Math.Min(firstDistance, secondDistance), Math.Min(firstEdges, secondEdges));
        }

        private static bool Intersects(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            double first = Cross(b - a, c - a);
            double second = Cross(b - a, d - a);
            double third = Cross(d - c, a - c);
            double fourth = Cross(d - c, b - c);
            return first * second <= 0 && third * fourth <= 0
                && Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X))
                && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        }

        private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
        private static double Square(double value) => value * value;
    }
}
