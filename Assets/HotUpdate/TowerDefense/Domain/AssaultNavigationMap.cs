using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Walkable bounds, assault area and obstacle footprints in map millimetres.</summary>
    public sealed class AssaultNavigationMap
    {
        internal const int DefaultApproachInset = 3600;
        private const int DefaultAreaHalfWidth = 5600;
        private const int DefaultBaseHalfWidth = 700;
        private const int MaxCoordinate = 2000000;
        private readonly Obstacle baseFootprint;
        private readonly Obstacle boundary;
        private readonly Obstacle[] obstacles;

        public Position2 Min { get; }
        public Position2 Max { get; }
        public Position2 AreaMin { get; }
        public Position2 AreaMax { get; }
        public Position2 BaseMin { get; }
        public Position2 BaseMax { get; }
        public Position2 BaseCenter { get; }
        public int ApproachInset { get; }

        public AssaultNavigationMap(
            Position2 min,
            Position2 max,
            Position2 areaMin,
            Position2 areaMax,
            Position2 baseMin,
            Position2 baseMax,
            Position2[][] blockedAreas = null,
            Position2[] walkableBoundary = null,
            int approachInset = DefaultApproachInset)
        {
            if (!ValidBounds(min, max) || !ValidBounds(areaMin, areaMax) || !ValidBounds(baseMin, baseMax)
                || !Contains(min, max, areaMin, 0) || !Contains(min, max, areaMax, 0)
                || !Contains(areaMin, areaMax, baseMin, 0) || !Contains(areaMin, areaMax, baseMax, 0)
                || (long)areaMax.X - areaMin.X > 16000 || (long)areaMax.Y - areaMin.Y > 16000)
            {
                throw new ArgumentException("Invalid bounds for assault navigation.");
            }

            if (approachInset < 600 || approachInset > 8000)
            {
                throw new ArgumentOutOfRangeException(nameof(approachInset));
            }

            Min = min;
            Max = max;
            AreaMin = areaMin;
            AreaMax = areaMax;
            BaseMin = baseMin;
            BaseMax = baseMax;
            BaseCenter = new Position2(baseMin.X + (baseMax.X - baseMin.X) / 2, baseMin.Y + (baseMax.Y - baseMin.Y) / 2);
            ApproachInset = approachInset;
            baseFootprint = new Obstacle(new[] { baseMin, new Position2(baseMax.X, baseMin.Y), baseMax, new Position2(baseMin.X, baseMax.Y) });
            boundary = walkableBoundary == null ? null : new Obstacle(walkableBoundary, blocksExterior: true);
            obstacles = Array.ConvertAll(blockedAreas ?? Array.Empty<Position2[]>(), vertices => new Obstacle(vertices));
        }

        internal static AssaultNavigationMap CreateDefault(Position2 baseCenter)
        {
            var areaMin = new Position2(baseCenter.X - DefaultAreaHalfWidth, baseCenter.Y - DefaultAreaHalfWidth);
            var areaMax = new Position2(baseCenter.X + DefaultAreaHalfWidth, baseCenter.Y + DefaultAreaHalfWidth);
            var baseMin = new Position2(baseCenter.X - DefaultBaseHalfWidth, baseCenter.Y - DefaultBaseHalfWidth);
            var baseMax = new Position2(baseCenter.X + DefaultBaseHalfWidth, baseCenter.Y + DefaultBaseHalfWidth);
            var min = new Position2(-MaxCoordinate, -MaxCoordinate);
            var max = new Position2(MaxCoordinate, MaxCoordinate);
            return new AssaultNavigationMap(min, max, areaMin, areaMax, baseMin, baseMax);
        }

        internal bool IsInsideArea(Position2 point) => Contains(AreaMin, AreaMax, point, 0);

        internal long DistanceToBaseSquared(Position2 point) => DistanceSquared(point, ClosestPointOnBase(point));

        internal bool CanSweep(Position2 from, Position2 to, int radius)
        {
            return Contains(Min, Max, from, radius) && Contains(Min, Max, to, radius)
                && !baseFootprint.Blocks(from, to, radius) && !BlockedByTerrain(from, to, radius);
        }

        internal bool CanReachBase(Position2 point, int reach)
        {
            Position2 closest = ClosestPointOnBase(point);
            return DistanceSquared(point, closest) <= (long)reach * reach && !BlockedByTerrain(point, closest, 0);
        }

        private Position2 ClosestPointOnBase(Position2 point)
        {
            return new Position2(Math.Clamp(point.X, BaseMin.X, BaseMax.X), Math.Clamp(point.Y, BaseMin.Y, BaseMax.Y));
        }

        private bool BlockedByTerrain(Position2 from, Position2 to, int radius)
        {
            if (boundary != null && boundary.Blocks(from, to, radius))
            {
                return true;
            }

            foreach (var obstacle in obstacles)
            {
                if (obstacle.Blocks(from, to, radius))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ValidBounds(Position2 min, Position2 max)
        {
            return ValidPoint(min) && ValidPoint(max) && min.X < max.X && min.Y < max.Y;
        }

        private static bool ValidPoint(Position2 point)
        {
            return Math.Abs((long)point.X) <= MaxCoordinate && Math.Abs((long)point.Y) <= MaxCoordinate;
        }

        private static bool Contains(Position2 min, Position2 max, Position2 point, int radius)
        {
            return point.X >= (long)min.X + radius && point.X <= (long)max.X - radius
                && point.Y >= (long)min.Y + radius && point.Y <= (long)max.Y - radius;
        }

        internal static long DistanceSquared(Position2 first, Position2 second)
        {
            long x = (long)first.X - second.X;
            long y = (long)first.Y - second.Y;
            return x * x + y * y;
        }

        private static long Cross(Position2 start, Position2 end, Position2 point)
        {
            return ((long)end.X - start.X) * ((long)point.Y - start.Y) - ((long)end.Y - start.Y) * ((long)point.X - start.X);
        }

        private static double PointToSegmentSquared(Position2 point, Position2 start, Position2 end)
        {
            long x = (long)end.X - start.X;
            long y = (long)end.Y - start.Y;
            long dx = (long)point.X - start.X;
            long dy = (long)point.Y - start.Y;
            long projection = dx * x + dy * y;
            if (projection <= 0)
            {
                return DistanceSquared(point, start);
            }

            long lengthSquared = x * x + y * y;
            if (projection >= lengthSquared)
            {
                return DistanceSquared(point, end);
            }

            long cross = dx * y - dy * x;
            return (double)cross * cross / lengthSquared;
        }

        private static bool SegmentsIntersect(Position2 a, Position2 b, Position2 c, Position2 d)
        {
            if (Math.Max(a.X, b.X) < Math.Min(c.X, d.X) || Math.Min(a.X, b.X) > Math.Max(c.X, d.X)
                || Math.Max(a.Y, b.Y) < Math.Min(c.Y, d.Y) || Math.Min(a.Y, b.Y) > Math.Max(c.Y, d.Y))
            {
                return false;
            }

            return OppositeOrCollinear(Cross(a, b, c), Cross(a, b, d)) && OppositeOrCollinear(Cross(c, d, a), Cross(c, d, b));
        }

        private static bool OppositeOrCollinear(long first, long second) => first == 0 || second == 0 || (first > 0) != (second > 0);

        private sealed class Obstacle
        {
            private readonly Position2[] vertices;
            private readonly bool blocksExterior;
            private readonly int minX;
            private readonly int maxX;
            private readonly int minY;
            private readonly int maxY;

            internal Obstacle(Position2[] vertices, bool blocksExterior = false)
            {
                if (vertices == null || vertices.Length < 3)
                {
                    throw new ArgumentException("An assault obstacle requires at least three vertices.");
                }

                this.vertices = (Position2[])vertices.Clone();
                this.blocksExterior = blocksExterior;
                minX = maxX = vertices[0].X;
                minY = maxY = vertices[0].Y;
                foreach (var point in this.vertices)
                {
                    if (!ValidPoint(point))
                    {
                        throw new ArgumentException("Assault obstacle coordinate exceeds supported bounds.");
                    }

                    minX = Math.Min(minX, point.X);
                    maxX = Math.Max(maxX, point.X);
                    minY = Math.Min(minY, point.Y);
                    maxY = Math.Max(maxY, point.Y);
                }
            }

            internal bool Blocks(Position2 from, Position2 to, int radius)
            {
                long left = (long)Math.Min(from.X, to.X) - radius;
                long right = (long)Math.Max(from.X, to.X) + radius;
                long bottom = (long)Math.Min(from.Y, to.Y) - radius;
                long top = (long)Math.Max(from.Y, to.Y) + radius;
                if (right < minX || left > maxX || top < minY || bottom > maxY)
                {
                    return blocksExterior;
                }

                long clearance = (long)radius * radius;
                bool stationary = from.X == to.X && from.Y == to.Y;
                bool inside = false;
                var a = vertices[^1];
                foreach (var b in vertices)
                {
                    bool nearEdge = Math.Max(a.X, b.X) >= left && Math.Min(a.X, b.X) <= right
                        && Math.Max(a.Y, b.Y) >= bottom && Math.Min(a.Y, b.Y) <= top;
                    if (nearEdge && (SegmentsIntersect(from, to, a, b) || clearance > 0 && (PointToSegmentSquared(from, a, b) < clearance
                        || !stationary && (PointToSegmentSquared(to, a, b) < clearance || PointToSegmentSquared(a, from, to) < clearance
                            || PointToSegmentSquared(b, from, to) < clearance))))
                    {
                        return true;
                    }

                    if ((a.Y > from.Y) != (b.Y > from.Y) && (Cross(a, b, from) > 0) == (b.Y > a.Y))
                    {
                        inside = !inside;
                    }

                    a = b;
                }

                return inside != blocksExterior;
            }
        }
    }
}
