using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    // A waiting destination also becomes a long-lived obstacle. Rank positions by
    // how much access they preserve for the next unit, before choosing where to park.
    internal static class MultiIngressPlacementPolicy
    {
        private const int CandidateLimit = 128;

        internal readonly struct Probe
        {
            internal readonly AssaultNavigationGrid Grid;
            internal readonly Position2 Entrance;
            internal readonly int[] Cost;
            internal readonly int Radius;
            internal readonly bool InUse;
            internal readonly Func<Position2, Position2, bool> Available;
            internal readonly Func<Position2, bool> Waiting;

            internal Probe(
                AssaultNavigationGrid grid,
                Position2 entrance,
                int[] cost,
                int radius,
                bool inUse,
                Func<Position2, Position2, bool> available,
                Func<Position2, bool> waiting)
            {
                Grid = grid;
                Entrance = entrance;
                Cost = cost;
                Radius = radius;
                InUse = inUse;
                Available = available;
                Waiting = waiting;
            }
        }

        internal static Position2[] Plan(AssaultNavigationGrid grid, Position2 start, Position2[] entrances, int[] ingressCosts,
            int radius, Func<Position2, Position2, bool> traversable, Func<Position2, bool> waiting, IReadOnlyList<Probe> forecast,
            Position2 ownEntrance)
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
            if (parking.Count == 0)
            {
                return basePath;
            }

            // Probes on one body-radius grid share the same standing traffic and towers.
            // Keep edges directed because a body may leave an existing close contact.
            var forecastEdges = new Dictionary<AssaultNavigationGrid, Dictionary<(Position2, Position2), bool>>();
            var witnesses = forecast.Select(probe => (Probe: probe, Reachable: new HashSet<Position2>(),
                Cache: EdgesFor(probe.Grid))).ToArray();
            foreach (var entry in witnesses)
            {
                VisitFromEntrance(entry.Probe, entry.Cache, null, point => entry.Reachable.Add(point));
            }
            long sampleSpacingSquared = (long)radius * radius;
            var examined = new List<Position2>(CandidateLimit);
            int bestLoss = int.MaxValue;
            bool found = false;
            Position2 best = basePath[^1];
            foreach (var candidate in parking.OrderByDescending(site => IsOwnSector(site.Point)).ThenByDescending(site => site.Distance))
            {
                if (examined.Any(point => DistanceSquared(point, candidate.Point) < sampleSpacingSquared))
                {
                    continue;
                }

                examined.Add(candidate.Point);
                int loss = 0;
                bool cutsIntake = false;
                foreach (var entry in witnesses)
                {
                    long clearance = radius + entry.Probe.Radius + 2;
                    long clearanceSquared = clearance * clearance;
                    int remaining = entry.Reachable.Count(point => DistanceSquared(point, candidate.Point) >= clearanceSquared);
                    int retained = 0;
                    VisitFromEntrance(entry.Probe, entry.Cache, candidate.Point, point =>
                    {
                        if (entry.Reachable.Contains(point))
                        {
                            retained++;
                        }
                    });
                    if (entry.Probe.InUse && remaining != retained)
                    {
                        cutsIntake = true;
                        break;
                    }

                    loss += remaining - retained;
                }

                if (!cutsIntake && loss < bestLoss)
                {
                    found = true;
                    bestLoss = loss;
                    best = candidate.Point;
                }
                if (bestLoss == 0 || examined.Count >= CandidateLimit)
                {
                    break;
                }
            }

            return found ? grid.FindPath(start, Available, (point, _, _) => point.Equals(best) ? 0 : int.MinValue, ingressCosts)
                : Array.Empty<Position2>();

            bool IsOwnSector(Position2 point)
            {
                long distance = DistanceSquared(ownEntrance, point);
                return !entrances.Any(entrance => DistanceSquared(entrance, point) < distance);
            }

            bool Available(Position2 from, Position2 to)
            {
                if (!edges.TryGetValue((from, to), out bool open))
                {
                    open = traversable(from, to);
                    edges.Add((from, to), open);
                }
                return open;
            }

            Dictionary<(Position2, Position2), bool> EdgesFor(AssaultNavigationGrid forecastGrid)
            {
                if (!forecastEdges.TryGetValue(forecastGrid, out var cache))
                {
                    cache = new Dictionary<(Position2, Position2), bool>();
                    forecastEdges.Add(forecastGrid, cache);
                }

                return cache;
            }

            void VisitFromEntrance(Probe probe, Dictionary<(Position2, Position2), bool> cache, Position2? occupant, Action<Position2> visit)
            {
                long clearance = radius + probe.Radius + 2;
                long clearanceSquared = clearance * clearance;
                probe.Grid.VisitReachable(probe.Entrance, Open, probe.Cost, point =>
                {
                    if (probe.Waiting(point))
                    {
                        visit(point);
                    }
                });

                bool Open(Position2 from, Position2 to)
                {
                    if (occupant.HasValue && SegmentDistanceSquared(occupant.Value, from, to) < clearanceSquared)
                    {
                        return false;
                    }
                    if (!cache.TryGetValue((from, to), out bool clear))
                    {
                        clear = probe.Available(from, to);
                        cache.Add((from, to), clear);
                    }
                    return clear;
                }
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


