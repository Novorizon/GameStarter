using System;
using System.Collections.Generic;
using NavigationLab.Core;

namespace NavigationLab.Candidates.EWarcraft3
{
    internal readonly struct RefinedConnection
    {
        internal readonly int Cell;
        internal readonly double Cost;
        internal readonly List<Point2> Path;

        internal RefinedConnection(int cell, double cost, List<Point2> path)
        {
            Cell = cell;
            Cost = cost;
            Path = path;
        }
    }

    internal sealed partial class GlobalSearch
    {
        // Refine only a bounded neighborhood after the dynamic coarse search has failed.
        // Original terrain cells, circular bodies, endpoints and motion speed remain unchanged.
        private List<RefinedConnection> RefinedConnections(Point2 from, int goal, UnitProfile profile, Func<Point2, Point2, bool> dynamicClear, int localRadius)
        {
            count("RefinedSearches", 1);
            var connections = new List<RefinedConnection>();
            int origin = world.CellAt(from);
            if (origin < 0)
            {
                return connections;
            }

            const int divisions = 4;
            int radius = localRadius > 0 ? Math.Min(6, localRadius) : 6;
            int half = radius * divisions;
            int side = half * 2 + 1;
            int nodeCount = side * side;
            double spacing = (double)world.CellSizeMm / divisions;
            Point2 center = world.Center(origin);
            var minimum = new Point2(center.X - half * spacing, center.Y - half * spacing);
            var costs = new double[nodeCount];
            var parents = new int[nodeCount];
            var standable = new byte[nodeCount];
            for (int index = 0; index < nodeCount; index++)
            {
                costs[index] = double.PositiveInfinity;
                parents[index] = -1;
            }

            Point2 Position(int node) => new Point2(minimum.X + node % side * spacing, minimum.Y + node / side * spacing);
            bool CanStand(int node)
            {
                if (standable[node] == 0)
                {
                    Point2 point = Position(node);
                    standable[node] = (byte)(geometry.ClearSegment(point, point, profile.RadiusMm) && dynamicClear(point, point) ? 2 : 1);
                }

                return standable[node] == 2;
            }

            List<Point2> Prefix(int node)
            {
                var points = new List<Point2>();
                while (node >= 0)
                {
                    points.Add(Position(node));
                    node = parents[node];
                }

                points.Reverse();
                if (points.Count > 0 && Point2.Distance(points[0], from) < .001)
                {
                    points.RemoveAt(0);
                }

                return points;
            }

            int startX = (int)Math.Floor((from.X - minimum.X) / spacing);
            int startY = (int)Math.Floor((from.Y - minimum.Y) / spacing);
            var queue = new SearchQueue();
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int x = startX + dx;
                    int y = startY + dy;
                    if (x < 0 || y < 0 || x >= side || y >= side)
                    {
                        continue;
                    }

                    int node = y * side + x;
                    Point2 point = Position(node);
                    if (!CanStand(node) || !geometry.ClearSegment(from, point, profile.RadiusMm) || !dynamicClear(from, point))
                    {
                        continue;
                    }

                    double cost = Point2.Distance(from, point);
                    costs[node] = cost;
                    queue.Add(node, cost, cost);
                }
            }

            int iterations = 0;
            int budget = nodeCount * 8;
            while (queue.Count > 0 && iterations++ < budget)
            {
                var item = queue.Pop();
                int node = item.Cell;
                if (item.Cost > costs[node] + .00001)
                {
                    continue;
                }

                count("RefinedExpandedNodes", 1);
                int x = node % side;
                int y = node / side;
                Point2 point = Position(node);
                if (x % divisions == 0 && y % divisions == 0)
                {
                    int cell = world.CellAt(point);
                    if (cell >= 0 && geometry.CanStand(cell, profile.RadiusMm) && Point2.Distance(point, world.Center(cell)) < .001)
                    {
                        var connection = new RefinedConnection(cell, item.Cost, Prefix(node));
                        if (cell == goal)
                        {
                            count("RefinedDirectGoals", 1);
                            return new List<RefinedConnection> { connection };
                        }

                        connections.Add(connection);
                    }
                }

                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if ((dx == 0 && dy == 0) || x + dx < 0 || y + dy < 0 || x + dx >= side || y + dy >= side)
                        {
                            continue;
                        }

                        int next = (y + dy) * side + x + dx;
                        Point2 nextPoint = Position(next);
                        double candidate = item.Cost + Point2.Distance(point, nextPoint);
                        if (candidate + .00001 >= costs[next] || !CanStand(next))
                        {
                            continue;
                        }

                        if (!geometry.ClearSegment(point, nextPoint, profile.RadiusMm) || !dynamicClear(point, nextPoint))
                        {
                            continue;
                        }

                        costs[next] = candidate;
                        parents[next] = node;
                        queue.Add(next, candidate, candidate);
                    }
                }
            }

            if (queue.Count > 0)
            {
                count("RefinedSearchBudgetExhaustions", 1);
            }

            count("RefinedCoarseConnections", connections.Count);
            return connections;
        }
    }
}
