using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Static ground constraints used when generating tower slots.</summary>
    internal sealed class TowerPlacementArea
    {
        private readonly BlockedArea[] blockedAreas;
        private readonly int roadHalfWidth;
        internal Position2 Min { get; }
        internal Position2 Max { get; }

        internal TowerPlacementArea(Position2 min, Position2 max, int roadHalfWidth, Position2[][] blockedAreas)
        {
            if (max.X <= min.X || max.Y <= min.Y || (long)max.X - min.X > 32000 || (long)max.Y - min.Y > 32000
                || roadHalfWidth <= 0 || roadHalfWidth > 2000 || blockedAreas == null)
            {
                throw new ArgumentException("Invalid tower placement bounds, road width or obstacles.");
            }

            Min = min;
            Max = max;
            this.roadHalfWidth = roadHalfWidth;
            this.blockedAreas = Array.ConvertAll(blockedAreas, points => new BlockedArea(points));
        }

        internal bool CanBuild(Position2 position, int radius, RouteDefinition[] routes)
        {
            if (!FitsBounds(position, radius) || IsNearRoad(position, roadHalfWidth + radius, routes))
            {
                return false;
            }

            foreach (var area in blockedAreas)
            {
                if (area.Blocks(position, radius))
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

        private static bool IsNearRoad(Position2 point, int distance, RouteDefinition[] routes)
        {
            long distanceSquared = (long)distance * distance;
            foreach (var route in routes)
            {
                var points = route.Points;
                for (int i = 1; i < points.Length; i++)
                {
                    if (PointToSegmentSquared(point, points[i - 1], points[i]) <= distanceSquared)
                    {
                        return true;
                    }
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

            internal bool Blocks(Position2 position, int radius)
            {
                if (position.X + radius < left || position.X - radius > right || position.Y + radius < bottom || position.Y - radius > top)
                {
                    return false;
                }

                long clearance = (long)radius * radius;
                bool inside = false;
                var a = vertices[^1];
                foreach (var b in vertices)
                {
                    double edgeDistance = PointToSegmentSquared(position, a, b);
                    if (edgeDistance == 0 || edgeDistance < clearance)
                    {
                        return true;
                    }

                    if ((a.Y > position.Y) != (b.Y > position.Y)
                        && position.X < a.X + (position.Y - a.Y) * (double)(b.X - a.X) / (b.Y - a.Y))
                    {
                        inside = !inside;
                    }

                    a = b;
                }

                return inside;
            }
        }
    }
}
