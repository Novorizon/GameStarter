using System;
using System.Collections.Generic;
using NavigationLab.Core;

namespace NavigationLab.Candidates.BOrca
{
    /// <summary>
    /// Half-cell route nodes allow a continuous disc to use the middle of an even-width corridor. Final holding
    /// locations remain the original world's cells: route resolution does not add endpoints or change capacity.
    /// </summary>
    internal sealed class RouteMesh
    {
        private readonly WorldDefinition world;
        private readonly int width;
        private readonly int height;
        private readonly double step;
        private readonly bool[] stand;
        private readonly Dictionary<int, int[]> neighbours = new Dictionary<int, int[]>();
        private readonly double radius;
        private bool[]? component;
        private bool hasStaticJoin;
        private Point2 lastStaticJoinPoint;
        private int lastStaticJoin;

        internal RouteMesh(WorldDefinition world, double radius)
        {
            this.world = world;
            this.radius = radius;
            step = world.CellSizeMm / 2.0;
            width = world.Width * 2 + 1;
            height = world.Height * 2 + 1;
            stand = new bool[width * height];
            for (int index = 0; index < stand.Length; index++)
            {
                stand[index] = Geometry.Clear(world, Center(index), Center(index), radius);
            }
        }

        internal bool StaticallyReachable(Point2 start, Point2 goal, out int expanded)
        {
            expanded = 0;
            if (!hasStaticJoin || start != lastStaticJoinPoint)
            {
                lastStaticJoinPoint = start;
                lastStaticJoin = ClosestJoin(start, null);
                hasStaticJoin = true;
            }
            int startNode = lastStaticJoin;
            int goalNode = Node(goal);
            if (startNode < 0 || goalNode < 0 || !stand[goalNode])
            {
                return Geometry.Clear(world, start, goal, radius);
            }
            if (component == null || !component[startNode])
            {
                component = new bool[stand.Length];
                var queue = new Queue<int>();
                component[startNode] = true;
                queue.Enqueue(startNode);
                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    expanded++;
                    foreach (int next in Neighbours(current))
                    {
                        if (!component[next])
                        {
                            component[next] = true;
                            queue.Enqueue(next);
                        }
                    }
                }
            }
            return component[goalNode] || Geometry.Clear(world, start, goal, radius);
        }

        internal List<Point2>? Find(
            Point2 start,
            Point2 goal,
            Func<Point2, bool>? dynamicBlocked,
            out int expanded,
            Func<Point2, Point2, bool>? blockedSegment = null,
            int expansionLimit = int.MaxValue)
        {
            expanded = 0;
            if (Geometry.Clear(world, start, goal, radius) && SegmentAvoids(start, goal, dynamicBlocked)
                && (blockedSegment == null || !blockedSegment(start, goal)))
            {
                return new List<Point2> { goal };
            }

            int startNode = ClosestJoin(start, dynamicBlocked, blockedSegment);
            int goalNode = Node(goal);
            if (startNode < 0 || goalNode < 0 || !stand[goalNode])
            {
                return null;
            }

            var costs = new double[stand.Length];
            var previous = new int[stand.Length];
            for (int index = 0; index < costs.Length; index++)
            {
                costs[index] = double.PositiveInfinity;
                previous[index] = -1;
            }

            var heap = new Heap();
            costs[startNode] = Point2.Distance(start, Center(startNode));
            heap.Add(startNode, costs[startNode] + Point2.Distance(Center(startNode), goal));
            while (heap.Count > 0)
            {
                var entry = heap.Pop();
                int current = entry.Node;
                if (entry.Score > costs[current] + Point2.Distance(Center(current), goal) + .000001)
                {
                    continue;
                }

                expanded++;
                if (expanded > expansionLimit)
                {
                    return null;
                }
                if (current == goalNode)
                {
                    var result = new List<Point2>();
                    for (int at = current; at >= 0; at = previous[at])
                    {
                        result.Add(Center(at));
                    }

                    result.Reverse();
                    return result;
                }

                foreach (int next in Neighbours(current))
                {
                    if ((next != goalNode && dynamicBlocked != null && dynamicBlocked(Center(next)))
                        || (blockedSegment != null && blockedSegment(Center(current), Center(next))))
                    {
                        continue;
                    }

                    double cost = costs[current] + Point2.Distance(Center(current), Center(next));
                    if (cost + .000001 >= costs[next])
                    {
                        continue;
                    }

                    costs[next] = cost;
                    previous[next] = current;
                    heap.Add(next, cost + Point2.Distance(Center(next), goal));
                }
            }

            return null;
        }

        private bool SegmentAvoids(Point2 first, Point2 second, Func<Point2, bool>? dynamicBlocked)
        {
            if (dynamicBlocked == null)
            {
                return true;
            }

            int count = Math.Max(1, (int)Math.Ceiling(Point2.Distance(first, second) / Math.Max(10, radius * .5)));
            for (int index = 1; index < count; index++)
            {
                if (dynamicBlocked(first + (second - first) * (index / (double)count)))
                {
                    return false;
                }
            }

            return true;
        }

        private int ClosestJoin(Point2 point, Func<Point2, bool>? dynamicBlocked, Func<Point2, Point2, bool>? blockedSegment = null)
        {
            int centerX = (int)Math.Round((point.X - world.Origin.X) / step);
            int centerY = (int)Math.Round((point.Y - world.Origin.Y) / step);
            int result = -1;
            double distance = double.PositiveInfinity;
            for (int y = Math.Max(0, centerY - 2); y <= Math.Min(height - 1, centerY + 2); y++)
            {
                for (int x = Math.Max(0, centerX - 2); x <= Math.Min(width - 1, centerX + 2); x++)
                {
                    int node = y * width + x;
                    var target = Center(node);
                    double candidate = Point2.Distance(point, target);
                    if (!stand[node] || candidate >= distance || !Geometry.Clear(world, point, target, radius))
                    {
                        continue;
                    }

                    if ((dynamicBlocked != null && candidate > .01 && dynamicBlocked(target))
                        || (blockedSegment != null && blockedSegment(point, target)))
                    {
                        continue;
                    }

                    result = node;
                    distance = candidate;
                }
            }

            return result;
        }

        private int Node(Point2 point)
        {
            int x = (int)Math.Round((point.X - world.Origin.X) / step);
            int y = (int)Math.Round((point.Y - world.Origin.Y) / step);
            return x >= 0 && y >= 0 && x < width && y < height ? y * width + x : -1;
        }

        private Point2 Center(int node) => new Point2(world.Origin.X + node % width * step, world.Origin.Y + node / width * step);

        private int[] Neighbours(int node)
        {
            if (neighbours.TryGetValue(node, out var result))
            {
                return result;
            }

            var list = new List<int>();
            int x = node % width;
            int y = node / width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height)
                    {
                        continue;
                    }

                    int next = ny * width + nx;
                    if (stand[next] && Geometry.Clear(world, Center(node), Center(next), radius))
                    {
                        list.Add(next);
                    }
                }
            }

            result = list.ToArray();
            neighbours[node] = result;
            return result;
        }

        private sealed class Heap
        {
            private readonly List<(int Node, double Score)> items = new List<(int, double)>();
            internal int Count => items.Count;
            internal void Add(int node, double score)
            {
                items.Add((node, score));
                int index = items.Count - 1;
                while (index > 0)
                {
                    int parent = (index - 1) / 2;
                    if (!Less(items[index], items[parent]))
                    {
                        break;
                    }

                    var value = items[index];
                    items[index] = items[parent];
                    items[parent] = value;
                    index = parent;
                }
            }

            internal (int Node, double Score) Pop()
            {
                var result = items[0];
                items[0] = items[items.Count - 1];
                items.RemoveAt(items.Count - 1);
                int index = 0;
                while (index * 2 + 1 < items.Count)
                {
                    int next = index * 2 + 1;
                    if (next + 1 < items.Count && Less(items[next + 1], items[next]))
                    {
                        next++;
                    }

                    if (!Less(items[next], items[index]))
                    {
                        break;
                    }

                    var value = items[index];
                    items[index] = items[next];
                    items[next] = value;
                    index = next;
                }

                return result;
            }

            private static bool Less((int Node, double Score) first, (int Node, double Score) second)
            {
                return first.Score < second.Score || (first.Score == second.Score && first.Node < second.Node);
            }
        }
    }
}
