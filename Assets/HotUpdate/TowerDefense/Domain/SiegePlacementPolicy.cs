using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    // A waiting destination also becomes a long-lived obstacle. Rank positions by
    // how much access they preserve for the next unit, before choosing where to park.
    internal static class SiegePlacementPolicy
    {
        private const int CandidateLimit = 16;

        internal static Position2[] Plan(AssaultNavigationGrid grid, Position2 start, Position2[] entrances, int[] ingressCosts,
            int radius, Func<Position2, Position2, bool> traversable, Func<Position2, bool> waiting, Func<Position2, bool> attack)
        {
            var edges = new Dictionary<(Position2, Position2), bool>();
            var parking = new List<(Position2 Point, int Distance)>();
            var basePath = grid.FindPath(start, Available, (point, _, ingress) =>
            {
                if (ingress == int.MaxValue || !waiting(point))
                {
                    return int.MinValue;
                }

                parking.Add((point, ingress));
                return ingress;
            }, ingressCosts);
            if (parking.Count < 2)
            {
                return basePath;
            }

            Position2 entrance = entrances.OrderBy(point => DistanceSquared(start, point)).First();
            var accessible = new Dictionary<Position2, int>();
            VisitFromEntrance(Available, (point, weight) => accessible.Add(point, weight));
            if (accessible.Count < 2)
            {
                return basePath;
            }

            long clearanceSquared = (2L * radius + 2) * (2L * radius + 2);
            long sampleSpacingSquared = (long)radius * radius;
            var examined = new List<Position2>(CandidateLimit);
            int bestLoss = int.MaxValue;
            Position2 best = basePath[^1];
            foreach (var candidate in parking.OrderByDescending(site => site.Distance))
            {
                if (examined.Any(point => DistanceSquared(point, candidate.Point) < sampleSpacingSquared))
                {
                    continue;
                }

                examined.Add(candidate.Point);
                int loss = 0;
                foreach (var site in accessible)
                {
                    if (DistanceSquared(site.Key, candidate.Point) >= clearanceSquared)
                    {
                        loss += site.Value;
                    }
                }
                VisitFromEntrance((from, to) => SegmentDistanceSquared(candidate.Point, from, to) >= clearanceSquared && Available(from, to),
                    (point, _) =>
                    {
                        if (accessible.TryGetValue(point, out int weight))
                        {
                            loss -= weight;
                        }
                    });

                if (loss < bestLoss)
                {
                    bestLoss = loss;
                    best = candidate.Point;
                }
                if (bestLoss == 0 || examined.Count >= CandidateLimit)
                {
                    break;
                }
            }

            return grid.FindPath(start, Available, (point, _, _) => point.Equals(best) ? 0 : int.MinValue, ingressCosts);

            bool Available(Position2 from, Position2 to)
            {
                if (!edges.TryGetValue((from, to), out bool open))
                {
                    open = traversable(from, to);
                    edges.Add((from, to), open);
                }
                return open;
            }

            void VisitFromEntrance(Func<Position2, Position2, bool> allowed, Action<Position2, int> visit)
            {
                grid.FindPath(entrance, allowed, (point, _, ingress) =>
                {
                    if (ingress != int.MaxValue)
                    {
                        int weight = attack(point) ? 8 : waiting(point) ? 1 : 0;
                        if (weight > 0)
                        {
                            visit(point, weight);
                        }
                    }
                    return int.MinValue;
                }, ingressCosts);
            }
        }

        private static long DistanceSquared(Position2 a, Position2 b)
        {
            long x = (long)a.X - b.X;
            long y = (long)a.Y - b.Y;
            return x * x + y * y;
        }

        private static double SegmentDistanceSquared(Position2 point, Position2 from, Position2 to)
        {
            double x = (long)to.X - from.X;
            double y = (long)to.Y - from.Y;
            double lengthSquared = x * x + y * y;
            if (lengthSquared == 0)
            {
                return DistanceSquared(point, from);
            }

            double px = (long)point.X - from.X;
            double py = (long)point.Y - from.Y;
            double t = Math.Clamp((px * x + py * y) / lengthSquared, 0, 1);
            double dx = px - t * x;
            double dy = py - t * y;
            return dx * dx + dy * dy;
        }
    }
}
