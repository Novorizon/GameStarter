using System;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationGrid
    {
        internal const int RoadCostScale = 1000;

        private sealed class Clearance
        {
            internal bool[] Stand;
            internal bool[] North;
            internal bool[] East;
        }

        private Clearance CalculateClearance(int radius)
        {
            var profile = new Clearance { Stand = new bool[CellCount], North = new bool[CellCount], East = new bool[CellCount] };
            for (int cell = 0; cell < CellCount; cell++)
            {
                if (walkable[cell])
                {
                    var center = Center(cell);
                    profile.Stand[cell] = SegmentHasClearance(center, center, radius);
                }
            }

            for (int cell = 0; cell < CellCount; cell++)
            {
                if (!profile.Stand[cell])
                {
                    continue;
                }

                var center = Center(cell);
                if (cell % Width + 1 < Width && profile.Stand[cell + 1])
                {
                    profile.East[cell] = SegmentHasClearance(center, Center(cell + 1), radius);
                }

                if (cell + Width < CellCount && profile.Stand[cell + Width])
                {
                    profile.North[cell] = SegmentHasClearance(center, Center(cell + Width), radius);
                }
            }

            return profile;
        }

        private bool SegmentHasClearance(Position2 from, Position2 to, int radius)
        {
            if (staticClear != null)
            {
                return staticClear(from, to, radius) && !IntersectsBlockedSquares(from, to, radius, terrainBlocks, blockedValue: true);
            }

            long required = (long)TargetRadius + radius;
            return DistanceToSegmentSquared(TargetCenter, from, to) >= required * required
                && (radius <= CellSize / 2 || !IntersectsBlockedSquares(from, to, radius, walkable, blockedValue: false));
        }

        internal bool HasClearanceOnAxis(Position2 from, Position2 to, int radius)
        {
            return SupportsRadius(radius) && (from.X == to.X || from.Y == to.Y) && SegmentHasClearance(from, to, radius);
        }

        private bool IntersectsBlockedSquares(Position2 from, Position2 to, int radius, bool[] mask, bool blockedValue)
        {
            int left = Math.Min(from.X, to.X) - Origin.X;
            int right = Math.Max(from.X, to.X) - Origin.X;
            int bottom = Math.Min(from.Y, to.Y) - Origin.Y;
            int top = Math.Max(from.Y, to.Y) - Origin.Y;
            if (!blockedValue && (left < radius || bottom < radius || right + radius > Width * CellSize || top + radius > Height * CellSize))
            {
                return true;
            }

            int startX = Math.Max(0, (left - radius) / CellSize);
            int endX = Math.Min(Width - 1, (right + radius) / CellSize);
            int startY = Math.Max(0, (bottom - radius) / CellSize);
            int endY = Math.Min(Height - 1, (top + radius) / CellSize);
            long radiusSquared = (long)radius * radius;
            for (int y = startY; y <= endY; y++)
            {
                int dy = Math.Max(0, Math.Max(y * CellSize - top, bottom - (y + 1) * CellSize));
                for (int x = startX; x <= endX; x++)
                {
                    if (mask[y * Width + x] != blockedValue)
                    {
                        continue;
                    }

                    int dx = Math.Max(0, Math.Max(x * CellSize - right, left - (x + 1) * CellSize));
                    if ((long)dx * dx + (long)dy * dy < radiusSquared)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void CalculateRoadCosts()
        {
            for (int cell = 0; cell < CellCount; cell++)
            {
                double distance = double.PositiveInfinity;
                var point = Center(cell);
                foreach (var line in roadCenterlines)
                {
                    double progress = 0;
                    for (int index = 1; index < line.Length; index++)
                    {
                        var from = line[index - 1];
                        var to = line[index];
                        double vx = (double)to.X - from.X;
                        double vy = (double)to.Y - from.Y;
                        double length = Math.Sqrt(vx * vx + vy * vy);
                        double candidate = DistanceToSegmentSquared(point, from, to, out double fraction);
                        if (candidate < distance)
                        {
                            distance = candidate;
                            roadProgress[cell] = (int)(progress + length * fraction);
                        }

                        progress += length;
                    }
                }

                // Holding permission is independent of route preference. A 500 mm offset adds the cost of one edge.
                roadCosts[cell] = (int)Math.Min(3 * RoadCostScale, distance / 250000 * RoadCostScale);
            }
        }

        private static double DistanceToSegmentSquared(Position2 point, Position2 from, Position2 to)
        {
            return DistanceToSegmentSquared(point, from, to, out _);
        }

        private static double DistanceToSegmentSquared(Position2 point, Position2 from, Position2 to, out double projection)
        {
            double vx = (double)to.X - from.X;
            double vy = (double)to.Y - from.Y;
            double x = (double)point.X - from.X;
            double y = (double)point.Y - from.Y;
            double length = vx * vx + vy * vy;
            projection = length == 0 ? 0 : Math.Max(0, Math.Min(1, (x * vx + y * vy) / length));
            x -= projection * vx;
            y -= projection * vy;
            return x * x + y * y;
        }
    }
}
