using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Ground-plane navigation shared by road movement, local assaults and construction.</summary>
    public sealed class BattleTerrain
    {
        private readonly Position2[] road;
        private readonly BlockedArea[] blockedAreas;
        private readonly int roadHalfWidth;
        public Position2 Min { get; }
        public Position2 Max { get; }
        public BattleTerrain(Position2 min, Position2 max, Position2[] road, int roadHalfWidth, Position2[][] blockedAreas)
        {
            if (max.X <= min.X || max.Y <= min.Y || (long)max.X - min.X > 32000 || (long)max.Y - min.Y > 32000
                || road == null || road.Length < 2 || roadHalfWidth <= 0 || roadHalfWidth > 2000 || blockedAreas == null)
            {
                throw new ArgumentException("Invalid battle terrain bounds, road or obstacles.");
            }

            Min = min;
            Max = max;
            this.road = (Position2[])road.Clone();
            this.roadHalfWidth = roadHalfWidth;
            this.blockedAreas = Array.ConvertAll(blockedAreas, points => new BlockedArea(points));
        }

        public bool CanBuild(Position2 position, int radius)
        {
            return !IsNearRoad(position, roadHalfWidth + radius) && CanMove(position, position, radius);
        }

        public bool CanMove(Position2 from, Position2 to, int radius)
        {
            if (!FitsBounds(from, radius) || !FitsBounds(to, radius))
            {
                return false;
            }

            foreach (var area in blockedAreas)
            {
                if (area.Blocks(from, to, radius))
                {
                    return false;
                }
            }

            return true;
        }

        private bool FitsBounds(Position2 point, int radius)
        {
            return point.X >= Min.X + radius && point.X <= Max.X - radius && point.Y >= Min.Y + radius && point.Y <= Max.Y - radius;
        }

        private bool IsNearRoad(Position2 point, int distance)
        {
            long distanceSquared = (long)distance * distance;
            for (int i = 1; i < road.Length; i++)
            {
                if (PointToSegmentSquared(point, road[i - 1], road[i]) <= distanceSquared)
                {
                    return true;
                }
            }

            return false;
        }

        private static long PointDistanceSquared(Position2 first, Position2 second)
        {
            long x = (long)first.X - second.X;
            long y = (long)first.Y - second.Y;
            return x * x + y * y;
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
                return PointDistanceSquared(point, start);
            }

            long lengthSquared = x * x + y * y;
            if (projection >= lengthSquared)
            {
                return PointDistanceSquared(point, end);
            }

            long cross = dx * y - dy * x;
            return (double)cross * cross / lengthSquared;
        }

        private sealed class BlockedArea
        {
            private readonly Position2[] vertices;
            private readonly int left;
            private readonly int right;
            private readonly int bottom;
            private readonly int top;

            internal BlockedArea(Position2[] points)
            {
                if (points == null || points.Length < 3)
                {
                    throw new ArgumentException("An obstacle requires a polygon with at least three vertices.");
                }

                vertices = (Position2[])points.Clone();
                left = right = points[0].X;
                bottom = top = points[0].Y;
                foreach (var point in vertices)
                {
                    left = Math.Min(left, point.X);
                    right = Math.Max(right, point.X);
                    bottom = Math.Min(bottom, point.Y);
                    top = Math.Max(top, point.Y);
                }
            }

            internal bool Blocks(Position2 from, Position2 to, int radius)
            {
                int minX = Math.Min(from.X, to.X);
                int maxX = Math.Max(from.X, to.X);
                int minY = Math.Min(from.Y, to.Y);
                int maxY = Math.Max(from.Y, to.Y);
                if (maxX + radius < left || minX - radius > right || maxY + radius < bottom || minY - radius > top)
                {
                    return false;
                }

                long clearance = (long)radius * radius;
                bool inside = false;
                var a = vertices[^1];
                foreach (var b in vertices)
                {
                    if (SegmentsTouch(from, to, a, b) || PointToSegmentSquared(a, from, to) < clearance
                        || PointToSegmentSquared(from, a, b) < clearance || PointToSegmentSquared(to, a, b) < clearance)
                    {
                        return true;
                    }

                    if ((a.Y > from.Y) != (b.Y > from.Y) && from.X < a.X + (from.Y - a.Y) * (double)(b.X - a.X) / (b.Y - a.Y))
                    {
                        inside = !inside;
                    }

                    a = b;
                }

                return inside;
            }

            internal static bool SegmentsTouch(Position2 a, Position2 b, Position2 c, Position2 d)
            {
                return Math.Max(a.X, b.X) >= Math.Min(c.X, d.X) && Math.Max(c.X, d.X) >= Math.Min(a.X, b.X)
                    && Math.Max(a.Y, b.Y) >= Math.Min(c.Y, d.Y) && Math.Max(c.Y, d.Y) >= Math.Min(a.Y, b.Y)
                    && Math.Sign(Orientation(a, b, c)) * Math.Sign(Orientation(a, b, d)) <= 0
                    && Math.Sign(Orientation(c, d, a)) * Math.Sign(Orientation(c, d, b)) <= 0;
            }

            private static long Orientation(Position2 a, Position2 b, Position2 c)
            {
                return ((long)b.X - a.X) * ((long)c.Y - a.Y) - ((long)b.Y - a.Y) * ((long)c.X - a.X);
            }
        }
    }
}
