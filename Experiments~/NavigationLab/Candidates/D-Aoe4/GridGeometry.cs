using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    internal readonly struct PlanningBody
    {
        internal readonly int Id;
        internal readonly Point2 Position;
        internal readonly int Radius;
        internal PlanningBody(int id, Point2 position, int radius)
        {
            Id = id;
            Position = position;
            Radius = radius;
        }
    }

    internal sealed class GridGeometry
    {
        private readonly PlanningBody[] bodies;
        private readonly Dictionary<int, bool[]> clearance = new Dictionary<int, bool[]>();
        private readonly Dictionary<int, byte[]> edgeClearance = new Dictionary<int, byte[]>();
        internal WorldDefinition World { get; private set; }
        internal int Revision { get; private set; }

        internal GridGeometry(WorldDefinition world, PlanningBody[]? bodies = null)
        {
            this.bodies = bodies == null ? Array.Empty<PlanningBody>() : (PlanningBody[])bodies.Clone();
            World = world.Clone();
        }

        internal void Apply(WorldChange change)
        {
            World = World.WithChange(change);
            Revision++;
            clearance.Clear();
            edgeClearance.Clear();
        }

        internal System.Collections.Generic.IEnumerable<int> Neighbours(int cell)
        {
            foreach (int next in World.Neighbours(cell))
            {
                yield return next;
            }
            int x = cell % World.Width;
            int y = cell / World.Width;
            for (int dy = -1; dy <= 1; dy += 2)
            {
                for (int dx = -1; dx <= 1; dx += 2)
                {
                    if (x + dx >= 0 && x + dx < World.Width && y + dy >= 0 && y + dy < World.Height)
                    {
                        yield return (y + dy) * World.Width + x + dx;
                    }
                }
            }
        }

        internal bool[] Clearance(int radius)
        {
            if (!clearance.TryGetValue(radius, out var cells))
            {
                cells = Enumerable.Range(0, World.CellCount).Select(cell => CanStand(World.Center(cell), radius)).ToArray();
                clearance.Add(radius, cells);
            }
            return cells;
        }

        internal bool CanStand(Point2 point, double radius)
        {
            if (!Inside(point, radius))
            {
                return false;
            }
            foreach (var target in World.Targets)
            {
                if (Point2.Distance(point, target.Center) < target.RadiusMm + radius - 1e-7)
                {
                    return false;
                }
            }
            foreach (var body in bodies)
            {
                if (Point2.Distance(point, body.Position) < body.Radius + radius - 1e-7)
                {
                    return false;
                }
            }
            foreach (int cell in Nearby(point, point, radius))
            {
                if (!World.IsWalkable(cell) && PointRectangleSquared(point, cell) < radius * radius - 1e-7)
                {
                    return false;
                }
            }
            return true;
        }

        internal bool CanSegment(Point2 from, Point2 to, double radius, int ignoreTarget = -1)
        {
            if (!Inside(from, radius) || !Inside(to, radius))
            {
                return false;
            }
            foreach (var target in World.Targets)
            {
                double reach = target.RadiusMm + radius;
                if (target.Id != ignoreTarget && PointSegmentSquared(target.Center, from, to) < reach * reach - 1e-7)
                {
                    return false;
                }
            }
            foreach (var body in bodies)
            {
                double reach = body.Radius + radius;
                if (PointSegmentSquared(body.Position, from, to) < reach * reach - 1e-7)
                {
                    return false;
                }
            }
            foreach (int cell in Nearby(from, to, radius))
            {
                if (!World.IsWalkable(cell) && (SegmentRectangleSquared(from, to, cell) < radius * radius - 1e-7
                    || (radius == 0 && SegmentRectangleSquared(from, to, cell) < 1e-12)))
                {
                    return false;
                }
            }
            return true;
        }

        internal bool CanEdge(int from, int to, int radius)
        {
            var cells = Clearance(radius);
            if (from < 0 || to < 0 || from >= cells.Length || to >= cells.Length)
            {
                return false;
            }
            int dx = to % World.Width - from % World.Width;
            int dy = to / World.Width - from / World.Width;
            if (dx < -1 || dx > 1 || dy < -1 || dy > 1 || (dx == 0 && dy == 0) || cells.Length > int.MaxValue / 8)
            {
                return cells[from] && cells[to] && CanSegment(World.Center(from), World.Center(to), radius);
            }
            if (!edgeClearance.TryGetValue(radius, out var edges))
            {
                edges = new byte[cells.Length * 8];
                edgeClearance.Add(radius, edges);
            }
            int direction = (dy + 1) * 3 + dx + 1;
            if (direction > 4)
            {
                direction--;
            }
            int index = from * 8 + direction;
            byte cached = edges[index];
            if (cached != 0)
            {
                return cached == 2;
            }
            bool result = cells[from] && cells[to] && CanSegment(World.Center(from), World.Center(to), radius);
            edges[index] = result ? (byte)2 : (byte)1;
            return result;
        }

        internal bool CanAttack(Point2 point, UnitProfile profile, int targetId)
        {
            var target = World.Target(targetId);
            return Point2.Distance(point, target.Center) <= target.RadiusMm + profile.RadiusMm + profile.AttackRangeMm + 1e-7
                && CanSegment(point, target.Center, 0, targetId);
        }

        internal bool CanHold(int cell, UnitProfile profile, int targetId)
        {
            var target = World.Target(targetId);
            return World.HoldingAllowed(cell) && Clearance(profile.RadiusMm)[cell]
                && (CanAttack(World.Center(cell), profile, targetId)
                    || Point2.Distance(World.Center(cell), target.Center) <= target.RadiusMm + World.GatheringDistanceMm + 1e-7);
        }

        internal int[] Reachable(int start, int radius)
        {
            var stand = Clearance(radius);
            var distances = Enumerable.Repeat(-1, World.CellCount).ToArray();
            if (start < 0 || !stand[start])
            {
                return distances;
            }
            var queue = new Queue<int>();
            distances[start] = 0;
            queue.Enqueue(start);
            while (queue.TryDequeue(out int cell))
            {
                foreach (int next in Neighbours(cell))
                {
                    if (distances[next] < 0 && CanEdge(cell, next, radius))
                    {
                        distances[next] = distances[cell] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            return distances;
        }

        internal static double SweptGapSquared(Point2 fromA, Point2 toA, Point2 fromB, Point2 toB)
        {
            Point2 relative = fromA - fromB;
            Point2 delta = (toA - fromA) - (toB - fromB);
            double time = delta.LengthSquared < 1e-18 ? 0 : Math.Clamp(-Point2.Dot(relative, delta) / delta.LengthSquared, 0, 1);
            return (relative + delta * time).LengthSquared;
        }

        internal static double PointSegmentSquared(Point2 point, Point2 from, Point2 to)
        {
            Point2 edge = to - from;
            double time = edge.LengthSquared < 1e-18 ? 0 : Math.Clamp(Point2.Dot(point - from, edge) / edge.LengthSquared, 0, 1);
            return (point - from - edge * time).LengthSquared;
        }

        private bool Inside(Point2 point, double radius)
        {
            return point.X >= World.Origin.X + radius - 1e-7 && point.Y >= World.Origin.Y + radius - 1e-7
                && point.X <= World.Origin.X + World.Width * World.CellSizeMm - radius + 1e-7
                && point.Y <= World.Origin.Y + World.Height * World.CellSizeMm - radius + 1e-7;
        }

        private IEnumerable<int> Nearby(Point2 from, Point2 to, double radius)
        {
            int minX = Math.Max(0, (int)Math.Floor((Math.Min(from.X, to.X) - radius - World.Origin.X) / World.CellSizeMm));
            int minY = Math.Max(0, (int)Math.Floor((Math.Min(from.Y, to.Y) - radius - World.Origin.Y) / World.CellSizeMm));
            int maxX = Math.Min(World.Width - 1, (int)Math.Floor((Math.Max(from.X, to.X) + radius - World.Origin.X) / World.CellSizeMm));
            int maxY = Math.Min(World.Height - 1, (int)Math.Floor((Math.Max(from.Y, to.Y) + radius - World.Origin.Y) / World.CellSizeMm));
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    yield return y * World.Width + x;
                }
            }
        }

        private double PointRectangleSquared(Point2 point, int cell)
        {
            double x = World.Origin.X + cell % World.Width * World.CellSizeMm;
            double y = World.Origin.Y + cell / World.Width * World.CellSizeMm;
            double dx = Math.Max(Math.Max(x - point.X, point.X - x - World.CellSizeMm), 0);
            double dy = Math.Max(Math.Max(y - point.Y, point.Y - y - World.CellSizeMm), 0);
            return dx * dx + dy * dy;
        }

        private double SegmentRectangleSquared(Point2 from, Point2 to, int cell)
        {
            double x = World.Origin.X + cell % World.Width * World.CellSizeMm;
            double y = World.Origin.Y + cell / World.Width * World.CellSizeMm;
            Point2 a = new Point2(x, y);
            Point2 b = new Point2(x + World.CellSizeMm, y);
            Point2 c = new Point2(x + World.CellSizeMm, y + World.CellSizeMm);
            Point2 d = new Point2(x, y + World.CellSizeMm);
            return Math.Min(
                Math.Min(PointRectangleSquared(from, cell), PointRectangleSquared(to, cell)),
                Math.Min(Math.Min(SegmentDistanceSquared(from, to, a, b), SegmentDistanceSquared(from, to, b, c)),
                    Math.Min(SegmentDistanceSquared(from, to, c, d), SegmentDistanceSquared(from, to, d, a))));
        }

        private static double SegmentDistanceSquared(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            Point2 r = b - a;
            Point2 s = d - c;
            double cross = Cross(r, s);
            if (Math.Abs(cross) > 1e-12)
            {
                double t = Cross(c - a, s) / cross;
                double u = Cross(c - a, r) / cross;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                {
                    return 0;
                }
            }
            return Math.Min(Math.Min(PointSegmentSquared(a, c, d), PointSegmentSquared(b, c, d)),
                Math.Min(PointSegmentSquared(c, a, b), PointSegmentSquared(d, a, b)));
        }

        private static double Cross(Point2 a, Point2 b)
        {
            return a.X * b.Y - a.Y * b.X;
        }
    }
}
