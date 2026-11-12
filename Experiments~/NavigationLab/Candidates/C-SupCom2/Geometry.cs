using System;
using NavigationLab.Core;

namespace NavigationLab.CSupCom2
{

    internal static class Geometry
    {
        internal static bool Clear(WorldDefinition world, Point2 point, double radius)
        {
            if (point.X - radius < world.Origin.X - 1e-7 || point.Y - radius < world.Origin.Y - 1e-7
                || point.X + radius > world.Origin.X + world.Width * world.CellSizeMm + 1e-7
                || point.Y + radius > world.Origin.Y + world.Height * world.CellSizeMm + 1e-7)
            {
                return false;
            }
            for (int index = 0; index < world.Targets.Count; index++)
            {
                var target = world.Targets[index];
                if (Point2.Distance(point, target.Center) < radius + target.RadiusMm - 1e-7)
                {
                    return false;
                }
            }
            return StaticSegment(world, point, point, radius);
        }

        internal static bool Segment(WorldDefinition world, Point2 from, Point2 to, double radius)
        {
            if (!Clear(world, from, radius) || !Clear(world, to, radius))
            {
                return false;
            }
            for (int index = 0; index < world.Targets.Count; index++)
            {
                var target = world.Targets[index];
                if (PointSegment(target.Center, from, to) < radius + target.RadiusMm - 1e-7)
                {
                    return false;
                }
            }
            return StaticSegment(world, from, to, radius);
        }

        internal static bool StaticSegment(WorldDefinition world, Point2 from, Point2 to, double radius)
        {
            int x0 = Math.Max(0, (int)Math.Floor((Math.Min(from.X, to.X) - radius - world.Origin.X) / world.CellSizeMm));
            int y0 = Math.Max(0, (int)Math.Floor((Math.Min(from.Y, to.Y) - radius - world.Origin.Y) / world.CellSizeMm));
            int x1 = Math.Min(world.Width - 1, (int)Math.Floor((Math.Max(from.X, to.X) + radius - world.Origin.X) / world.CellSizeMm));
            int y1 = Math.Min(world.Height - 1, (int)Math.Floor((Math.Max(from.Y, to.Y) + radius - world.Origin.Y) / world.CellSizeMm));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (world.IsWalkable(y * world.Width + x))
                    {
                        continue;
                    }
                    double left = world.Origin.X + x * world.CellSizeMm;
                    double bottom = world.Origin.Y + y * world.CellSizeMm;
                    if (SegmentRectangle(from, to, left, bottom, left + world.CellSizeMm, bottom + world.CellSizeMm) < radius - 1e-7)
                    {
                        return false;
                    }
                    if (radius <= 1e-7 && IntersectsRectangle(from, to, left, bottom, left + world.CellSizeMm, bottom + world.CellSizeMm))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        internal static bool VisibleTarget(WorldDefinition world, Point2 from, TargetDefinition target)
        {
            Point2 direction = (target.Center - from).Normalized;
            Point2 near = target.Center - direction * target.RadiusMm;
            return StaticSegment(world, from, near, 0);
        }

        internal static double PointSegment(Point2 point, Point2 a, Point2 b)
        {
            Point2 delta = b - a;
            double t = delta.LengthSquared < 1e-18 ? 0 : Math.Clamp(Point2.Dot(point - a, delta) / delta.LengthSquared, 0, 1);
            return Point2.Distance(point, a + delta * t);
        }

        internal static double SweptBodies(Point2 a0, Point2 a1, Point2 b0, Point2 b1)
        {
            return PointSegment(default, a0 - b0, a1 - b1);
        }

        private static double SegmentRectangle(Point2 a, Point2 b, double left, double bottom, double right, double top)
        {
            if (IntersectsRectangle(a, b, left, bottom, right, top))
            {
                return 0;
            }
            var p0 = new Point2(left, bottom);
            var p1 = new Point2(right, bottom);
            var p2 = new Point2(right, top);
            var p3 = new Point2(left, top);
            return Math.Min(Math.Min(Segments(a, b, p0, p1), Segments(a, b, p1, p2)), Math.Min(Segments(a, b, p2, p3), Segments(a, b, p3, p0)));
        }

        private static double Segments(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            return Math.Min(Math.Min(PointSegment(a, c, d), PointSegment(b, c, d)), Math.Min(PointSegment(c, a, b), PointSegment(d, a, b)));
        }

        private static bool IntersectsRectangle(Point2 a, Point2 b, double left, double bottom, double right, double top)
        {
            Point2 direction = b - a;
            double low = 0;
            double high = 1;
            return Clip(-direction.X, a.X - left, ref low, ref high)
                && Clip(direction.X, right - a.X, ref low, ref high)
                && Clip(-direction.Y, a.Y - bottom, ref low, ref high)
                && Clip(direction.Y, top - a.Y, ref low, ref high);
        }

        private static bool Clip(double denominator, double numerator, ref double low, ref double high)
        {
            if (Math.Abs(denominator) < 1e-12)
            {
                return numerator >= 0;
            }
            double ratio = numerator / denominator;
            if (denominator < 0)
            {
                low = Math.Max(low, ratio);
            }
            else
            {
                high = Math.Min(high, ratio);
            }
            return low <= high;
        }
    }
}
