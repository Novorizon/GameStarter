using System;
using System.Collections.Generic;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int MaximumManeuverWaitTicks = 40;

        private readonly struct TimedSweep
        {
            internal readonly int Start;
            internal readonly int End;
            private readonly Position2 from;
            private readonly Position2 to;

            internal TimedSweep(int start, int end, Position2 from, Position2 to)
            {
                Start = start;
                End = end;
                this.from = from;
                this.to = to;
            }

            internal bool Conflicts(TimedSweep other, int clearance)
            {
                int first = Math.Max(Start, other.Start);
                int last = Math.Min(End, other.End);
                if (first > last)
                {
                    return false;
                }

                PositionAt(first, out double firstX, out double firstY);
                other.PositionAt(first, out double otherX, out double otherY);
                double x = firstX - otherX;
                double y = firstY - otherY;
                PositionAt(last, out double lastX, out double lastY);
                other.PositionAt(last, out double otherLastX, out double otherLastY);
                double dx = lastX - otherLastX - x;
                double dy = lastY - otherLastY - y;
                double length = dx * dx + dy * dy;
                double fraction = length == 0 ? 0 : Math.Clamp(-(x * dx + y * dy) / length, 0, 1);
                x += fraction * dx;
                y += fraction * dy;
                int margin = (from.Equals(to) ? 0 : 3) + (other.from.Equals(other.to) ? 0 : 3);
                long radius = clearance + margin;
                return x * x + y * y < radius * radius;
            }

            private void PositionAt(int time, out double x, out double y)
            {
                double fraction = (double)(time - Start) / (End - Start);
                x = from.X + (to.X - from.X) * fraction;
                y = from.Y + (to.Y - from.Y) * fraction;
            }
        }

        private int FindManeuverStart(Enemy enemy, Position2[] path)
        {
            if (maneuvers.Count == 0)
            {
                return 0;
            }

            var reservations = new List<TimedSweep>[maneuvers.Count];
            for (int index = 0; index < maneuvers.Count; index++)
            {
                var mover = maneuvers[index];
                reservations[index] = PredictManeuver(mover, mover.Maneuver, mover.ManeuverIndex, mover.ManeuverWaitTicks);
            }

            for (int wait = 0; wait <= MaximumManeuverWaitTicks; wait += 2)
            {
                var proposed = PredictManeuver(enemy, path, 0, wait);
                bool clear = true;
                for (int index = 0; index < maneuvers.Count && clear; index++)
                {
                    int clearance = enemy.Radius + maneuvers[index].Radius + TrafficGap;
                    clear = !ManeuversConflict(proposed, reservations[index], clearance);
                }

                if (clear)
                {
                    return wait;
                }
            }

            return -1;
        }

        private static List<TimedSweep> PredictManeuver(Enemy enemy, Position2[] path, int firstSegment, int wait)
        {
            var result = new List<TimedSweep>();
            int time = 0;
            var from = enemy.Position;
            if (wait > 0)
            {
                result.Add(new TimedSweep(0, wait, from, from));
                time = wait;
            }

            for (int index = firstSegment; index < path.Length; index++)
            {
                var target = path[index];
                bool continuing = path == enemy.Maneuver && index == firstSegment;
                var anchor = continuing ? enemy.ManeuverSegmentStart : from;
                int consumed = continuing ? enemy.ManeuverSegmentDistance : 0;
                int length = SegmentLength(anchor, target);
                int fullTicks = Math.Max(0, (length - 1 - consumed) / enemy.Definition.SpeedPerTick);
                if (fullTicks > 0)
                {
                    int distance = consumed + fullTicks * enemy.Definition.SpeedPerTick;
                    var next = Interpolate(anchor, target, distance, length);
                    result.Add(new TimedSweep(time, time + fullTicks, from, next));
                    time += fullTicks;
                    from = next;
                }

                result.Add(new TimedSweep(time, time + 1, from, target));
                time++;
                from = target;
            }

            result.Add(new TimedSweep(time, int.MaxValue, from, from));
            return result;
        }

        private static bool ManeuversConflict(List<TimedSweep> first, List<TimedSweep> second, int clearance)
        {
            int a = 0;
            int b = 0;
            while (a < first.Count && b < second.Count)
            {
                if (first[a].Conflicts(second[b], clearance))
                {
                    return true;
                }

                if (first[a].End < second[b].End)
                {
                    a++;
                }
                else
                {
                    b++;
                }
            }

            return false;
        }
    }
}
