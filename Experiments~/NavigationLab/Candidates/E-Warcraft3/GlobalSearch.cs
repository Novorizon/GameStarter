using System;
using System.Collections.Generic;
using NavigationLab.Core;

namespace NavigationLab.Candidates.EWarcraft3
{
    internal sealed class SearchQueue
    {
        private readonly List<Entry> entries = new List<Entry>();
        private long sequence;
        internal int Count => entries.Count;

        internal void Add(int cell, double cost, double priority)
        {
            var item = new Entry(cell, cost, priority, sequence++);
            entries.Add(item);
            int index = entries.Count - 1;
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Before(item, entries[parent]))
                {
                    break;
                }

                entries[index] = entries[parent];
                index = parent;
            }

            entries[index] = item;
        }

        internal Entry Pop()
        {
            var first = entries[0];
            var last = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            if (entries.Count == 0)
            {
                return first;
            }

            int index = 0;
            while (index * 2 + 1 < entries.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < entries.Count && Before(entries[child + 1], entries[child]))
                {
                    child++;
                }

                if (!Before(entries[child], last))
                {
                    break;
                }

                entries[index] = entries[child];
                index = child;
            }

            entries[index] = last;
            return first;
        }

        private static bool Before(Entry first, Entry second)
        {
            return first.Priority < second.Priority || (first.Priority == second.Priority
                && (first.Cell < second.Cell || (first.Cell == second.Cell && first.Sequence < second.Sequence)));
        }

        internal readonly struct Entry
        {
            internal readonly int Cell;
            internal readonly double Cost;
            internal readonly double Priority;
            internal readonly long Sequence;

            internal Entry(int cell, double cost, double priority, long sequence)
            {
                Cell = cell;
                Cost = cost;
                Priority = priority;
                Sequence = sequence;
            }
        }
    }

    internal sealed partial class GlobalSearch
    {
        private readonly WorldDefinition world;
        private readonly PathingGeometry geometry;
        private readonly Action<string, long> count;
        private readonly bool refinedConnections;

        internal GlobalSearch(WorldDefinition world, PathingGeometry geometry, Action<string, long> count) : this(world, geometry, count, false)
        {
        }

        internal GlobalSearch(WorldDefinition world, PathingGeometry geometry, Action<string, long> count, bool refinedConnections)
        {
            this.refinedConnections = refinedConnections;
            this.world = world;
            this.geometry = geometry;
            this.count = count;
        }

        internal double[] Reachable(Point2 from, UnitProfile profile)
        {
            var costs = NewCosts();
            var queue = new Queue<int>();
            foreach (int cell in Connections(from, profile.RadiusMm))
            {
                costs[cell] = Point2.Distance(from, world.Center(cell));
                queue.Enqueue(cell);
            }

            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                count("ReachabilityCells", 1);
                foreach (int next in geometry.Neighbours(cell, profile.RadiusMm))
                {
                    if (double.IsPositiveInfinity(costs[next]))
                    {
                        costs[next] = costs[cell] + Point2.Distance(world.Center(cell), world.Center(next));
                        queue.Enqueue(next);
                    }
                }
            }

            return costs;
        }

        internal List<Point2>? Find(Point2 from, int goal, UnitProfile profile, Func<Point2, Point2, bool>? dynamicClear = null, int localRadius = 0)
        {
            count(localRadius > 0 ? "LocalSearches" : "GlobalAStarSearches", 1);
            var path = FindCoarse(from, goal, profile, dynamicClear, localRadius, null);
            if (path != null || !refinedConnections || dynamicClear == null || !geometry.CanStand(goal, profile.RadiusMm))
            {
                return path;
            }

            if (!dynamicClear(world.Center(goal), world.Center(goal)))
            {
                count("RefinedBlockedDestinations", 1);
                return null;
            }

            var starts = RefinedConnections(from, goal, profile, dynamicClear, localRadius);
            if (starts.Count == 0)
            {
                count("RefinedNoConnection", 1);
                return null;
            }

            count("RefinedCoarseSearches", 1);
            path = FindCoarse(from, goal, profile, dynamicClear, localRadius, starts);
            count(path == null ? "RefinedNoCoarseRoute" : "RefinedRoutes", 1);
            return path;
        }

        private List<Point2>? FindCoarse(
            Point2 from,
            int goal,
            UnitProfile profile,
            Func<Point2, Point2, bool>? dynamicClear,
            int localRadius,
            List<RefinedConnection>? refinedStarts)
        {
            if (!geometry.CanStand(goal, profile.RadiusMm))
            {
                return null;
            }

            Point2 destination = world.Center(goal);
            if (geometry.ClearSegment(from, destination, profile.RadiusMm) && (dynamicClear == null || dynamicClear(from, destination)))
            {
                count("DirectConnectors", 1);
                return new List<Point2> { destination };
            }

            int startCell = world.CellAt(from);
            var costs = NewCosts();
            var parents = new int[world.CellCount];
            for (int index = 0; index < parents.Length; index++)
            {
                parents[index] = -1;
            }

            var queue = new SearchQueue();
            var prefixes = new Dictionary<int, List<Point2>>();
            if (refinedStarts == null)
            {
                foreach (int cell in Connections(from, profile.RadiusMm))
                {
                    Point2 center = world.Center(cell);
                    if (dynamicClear != null && !dynamicClear(from, center))
                    {
                        continue;
                    }

                    costs[cell] = Point2.Distance(from, center);
                    queue.Add(cell, costs[cell], costs[cell] + Point2.Distance(center, destination));
                }
            }
            else
            {
                foreach (var start in refinedStarts)
                {
                    costs[start.Cell] = start.Cost;
                    prefixes[start.Cell] = start.Path;
                    queue.Add(start.Cell, start.Cost, start.Cost + Point2.Distance(world.Center(start.Cell), destination));
                }
            }

            int iterations = 0;
            int budget = Math.Min(50000, world.CellCount * 8);
            while (queue.Count > 0 && iterations++ < budget)
            {
                var item = queue.Pop();
                int cell = item.Cell;
                if (item.Cost > costs[cell] + .00001)
                {
                    continue;
                }

                count("AStarExpandedCells", 1);
                if (cell == goal)
                {
                    var result = new List<Point2>();
                    while (cell >= 0)
                    {
                        result.Add(world.Center(cell));
                        cell = parents[cell];
                    }

                    result.Reverse();
                    int root = world.CellAt(result[0]);
                    if (prefixes.TryGetValue(root, out var prefix))
                    {
                        var joined = new List<Point2>(prefix);
                        if (joined.Count > 0)
                        {
                            joined.RemoveAt(joined.Count - 1);
                        }
                        joined.AddRange(result);
                        result = joined;
                    }
                    if (result.Count > 0 && Point2.Distance(result[0], from) < .001)
                    {
                        result.RemoveAt(0);
                    }

                    return result;
                }

                foreach (int next in geometry.Neighbours(cell, profile.RadiusMm))
                {
                    if (localRadius > 0 && (Math.Abs(next % world.Width - startCell % world.Width) > localRadius
                        || Math.Abs(next / world.Width - startCell / world.Width) > localRadius))
                    {
                        continue;
                    }

                    Point2 center = world.Center(cell);
                    Point2 nextCenter = world.Center(next);
                    if (dynamicClear != null && !dynamicClear(center, nextCenter))
                    {
                        continue;
                    }

                    double candidate = item.Cost + Point2.Distance(center, nextCenter);
                    if (candidate + .00001 < costs[next])
                    {
                        costs[next] = candidate;
                        parents[next] = cell;
                        queue.Add(next, candidate, candidate + Point2.Distance(nextCenter, destination));
                    }
                }
            }

            count(queue.Count > 0 ? "SearchBudgetExhaustions" : "NoSpatialPath", 1);
            return null;
        }

        private IEnumerable<int> Connections(Point2 from, int radius)
        {
            int origin = world.CellAt(from);
            if (origin < 0)
            {
                yield break;
            }

            int x = origin % world.Width;
            int y = origin / world.Width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (x + dx < 0 || x + dx >= world.Width || y + dy < 0 || y + dy >= world.Height)
                    {
                        continue;
                    }

                    int cell = (y + dy) * world.Width + x + dx;
                    if (geometry.CanStand(cell, radius) && geometry.ClearSegment(from, world.Center(cell), radius))
                    {
                        yield return cell;
                    }
                }
            }
        }

        private double[] NewCosts()
        {
            var result = new double[world.CellCount];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = double.PositiveInfinity;
            }

            return result;
        }
    }
}
