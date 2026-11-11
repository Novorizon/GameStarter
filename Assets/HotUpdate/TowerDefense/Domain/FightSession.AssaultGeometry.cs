using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private static bool IsClearOfLocalApproachObstacles(Position2 from, Position2 to, int radius, BaseApproachArea area)
        {
            if (!FitsLocalApproachArea(to, radius, area))
            {
                return false;
            }

            foreach (var obstacle in area.Obstacles)
            {
                int clearance = radius + obstacle.Radius;
                if (SegmentDistanceSquared(obstacle.Center, from, to) < (long)clearance * clearance)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FitsLocalApproachArea(Position2 point, int radius, BaseApproachArea area)
        {
            return point.X >= area.Min.X + radius && point.X <= area.Max.X - radius
                && point.Y >= area.Min.Y + radius && point.Y <= area.Max.Y - radius;
        }

        private static bool CrossesBaseFootprint(Position2 from, Position2 to, BaseAttackEngagement bounds, int radius)
        {
            var min = bounds.Min;
            var max = bounds.Max;
            if (!CrossesApproachBox(from, to, min.X - radius, min.Y - radius, max.X + radius, max.Y + radius))
            {
                return false;
            }

            // A circular body adds straight margins and rounded corners to the footprint.
            long radiusSquared = (long)radius * radius;
            return CrossesApproachBox(from, to, min.X - radius, min.Y, max.X + radius, max.Y)
                || CrossesApproachBox(from, to, min.X, min.Y - radius, max.X, max.Y + radius)
                || SegmentDistanceSquared(min, from, to) < radiusSquared
                || SegmentDistanceSquared(max, from, to) < radiusSquared
                || SegmentDistanceSquared(new Position2(min.X, max.Y), from, to) < radiusSquared
                || SegmentDistanceSquared(new Position2(max.X, min.Y), from, to) < radiusSquared;
        }

        private static bool CrossesApproachBox(Position2 from, Position2 to, int left, int bottom, int right, int top)
        {
            double first = 0;
            double last = 1;
            return ClipApproachAxis(from.X, to.X, left, right, ref first, ref last)
                && ClipApproachAxis(from.Y, to.Y, bottom, top, ref first, ref last);
        }

        private static bool ClipApproachAxis(int start, int end, int min, int max, ref double first, ref double last)
        {
            int delta = end - start;
            if (delta == 0)
            {
                return start >= min && start <= max;
            }

            double enter = (min - start) / (double)delta;
            double exit = (max - start) / (double)delta;
            first = Math.Max(first, Math.Min(enter, exit));
            last = Math.Min(last, Math.Max(enter, exit));
            return first <= last;
        }

        private bool IsClearOfTowers(Position2 from, Position2 to, int radius)
        {
            int clearance = TowerFoundationRadiusMillimetres + radius;
            foreach (var tower in towers)
            {
                if (SegmentDistanceSquared(tower.Slot.Position, from, to) < (long)clearance * clearance)
                {
                    return false;
                }
            }

            return true;
        }

    }
}
