using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    internal sealed class HierarchicalFlow
    {
        internal sealed class Route
        {
            internal int Goal;
            internal int Radius;
            internal int Revision;
            internal int[] Tiles = Array.Empty<int>();
            internal int[] Components = Array.Empty<int>();
            internal int[] Exits = Array.Empty<int>();
            internal int LastTile = -1;
            internal int LastSegmentIndex = -1;
            internal int CurrentIndex;
            internal Flow? LastFlow;
        }

        internal sealed class Flow
        {
            internal int Exit;
            internal double[] Distance = Array.Empty<double>();
            internal bool[] Included = Array.Empty<bool>();
        }

        private sealed class PortalGraph
        {
            internal int[] Cells = Array.Empty<int>();
            internal List<Edge>[] Edges = Array.Empty<List<Edge>>();
            internal Dictionary<int, List<int>> TileNodes = new Dictionary<int, List<int>>();
        }

        private readonly struct Edge
        {
            internal readonly int Node;
            internal readonly int Cost;
            internal Edge(int node, int cost)
            {
                Node = node;
                Cost = cost;
            }
        }

        private const int TileSize = 8;
        private readonly GridGeometry geometry;
        private readonly IDictionary<string, long> metrics;
        private readonly int largestRadius;
        private readonly Dictionary<int, int[]> components = new Dictionary<int, int[]>();
        private readonly Dictionary<int, PortalGraph> graphs = new Dictionary<int, PortalGraph>();
        private readonly Dictionary<string, Flow> flows = new Dictionary<string, Flow>();
        private readonly Queue<string> flowOrder = new Queue<string>();
        private long retainedCells;
        private WorldDefinition World => geometry.World;
        private int TileWidth => (World.Width + TileSize - 1) / TileSize;

        internal HierarchicalFlow(GridGeometry geometry, IDictionary<string, long> metrics, int largestRadius)
        {
            this.geometry = geometry;
            this.metrics = metrics;
            this.largestRadius = largestRadius;
        }

        internal void Invalidate()
        {
            graphs.Clear();
            components.Clear();
            flows.Clear();
            flowOrder.Clear();
            retainedCells = 0;
            Add("WorldInvalidations");
        }

        internal Route? FindRoute(int start, int goal, int radius)
        {
            Add("HpaSearches");
            var stand = geometry.Clearance(radius);
            if (start < 0 || goal < 0 || !stand[start] || !stand[goal])
            {
                return null;
            }
            int startTile = Tile(start);
            int goalTile = Tile(goal);
            var startDistance = TileDistances(start, radius);
            if (startTile == goalTile && startDistance[goal] >= 0)
            {
                return new Route
                {
                    Goal = goal,
                    Radius = radius,
                    Revision = geometry.Revision,
                    Tiles = new[] { startTile },
                    Components = new[] { Component(start, radius) },
                    Exits = new[] { goal }
                };
            }
            var graph = Graph(radius);
            if (!graph.TileNodes.TryGetValue(startTile, out var starts) || !graph.TileNodes.ContainsKey(goalTile))
            {
                return null;
            }
            var goalDistance = TileDistances(goal, radius);
            var cost = Enumerable.Repeat(double.PositiveInfinity, graph.Cells.Length).ToArray();
            var parent = Enumerable.Repeat(-1, graph.Cells.Length).ToArray();
            var open = new MinHeap();
            foreach (int node in starts)
            {
                int distance = startDistance[graph.Cells[node]];
                if (distance >= 0)
                {
                    cost[node] = distance * World.CellSizeMm;
                    open.Push(node, cost[node] + Heuristic(graph.Cells[node], goal));
                }
            }
            int end = -1;
            double best = double.PositiveInfinity;
            while (open.TryPop(out int node, out double priority))
            {
                if (priority > cost[node] + Heuristic(graph.Cells[node], goal) + 1e-7 || priority >= best)
                {
                    continue;
                }
                Add("PortalNodesExpanded");
                int cell = graph.Cells[node];
                if (Tile(cell) == goalTile && goalDistance[cell] >= 0)
                {
                    best = cost[node] + goalDistance[cell] * World.CellSizeMm;
                    end = node;
                }
                foreach (var edge in graph.Edges[node])
                {
                    double next = cost[node] + edge.Cost;
                    if (next + 1e-7 < cost[edge.Node])
                    {
                        cost[edge.Node] = next;
                        parent[edge.Node] = node;
                        open.Push(edge.Node, next + Heuristic(graph.Cells[edge.Node], goal));
                    }
                }
            }
            if (end < 0)
            {
                return null;
            }
            var reversed = new List<int>();
            for (int node = end; node >= 0; node = parent[node])
            {
                reversed.Add(graph.Cells[node]);
            }
            reversed.Reverse();
            reversed.Insert(0, start);
            reversed.Add(goal);
            var tiles = new List<int> { startTile };
            var routeComponents = new List<int> { Component(start, radius) };
            var exits = new List<int>();
            for (int i = 1; i < reversed.Count; i++)
            {
                int tile = Tile(reversed[i]);
                int component = Component(reversed[i], radius);
                if (component != routeComponents[routeComponents.Count - 1])
                {
                    exits.Add(reversed[i - 1]);
                    tiles.Add(tile);
                    routeComponents.Add(component);
                }
            }
            exits.Add(goal);
            return new Route
            {
                Goal = goal,
                Radius = radius,
                Revision = geometry.Revision,
                Tiles = tiles.ToArray(),
                Components = routeComponents.ToArray(),
                Exits = exits.ToArray()
            };
        }

        internal Point2 Direction(Point2 position, Route route, out Flow? usedFlow)
        {
            usedFlow = null;
            int cell = World.CellAt(position);
            if (cell < 0 || route.Revision != geometry.Revision)
            {
                return default;
            }
            int tile = Tile(cell);
            int component = Component(cell, route.Radius);
            int index = route.CurrentIndex;
            if (route.Components[index] != component)
            {
                var matches = Enumerable.Range(0, route.Components.Length).Where(i => route.Components[i] == component).ToArray();
                // Ambiguous re-entry is replanned from the real position instead of jumping to another visit.
                index = matches.Length == 1 ? matches[0] : -1;
            }
            if (index < 0)
            {
                return default;
            }
            route.CurrentIndex = index;
            if (route.LastTile == tile && route.LastSegmentIndex == index && route.LastFlow != null)
            {
                usedFlow = route.LastFlow;
                return FieldDirection(position, route.Radius, cell, route.LastFlow);
            }
            int end = Math.Min(route.Tiles.Length - 1, index + 2);
            var allowed = new HashSet<int>();
            for (int i = index; i <= end; i++)
            {
                allowed.Add(route.Tiles[i]);
            }
            // Leaf expansion gives locally displaced units flow information without generating the whole map.
            foreach (int existing in allowed.ToArray())
            {
                int tx = existing % TileWidth;
                int ty = existing / TileWidth;
                if (tx > 0)
                {
                    allowed.Add(existing - 1);
                }
                if (tx + 1 < TileWidth)
                {
                    allowed.Add(existing + 1);
                }
                if (ty > 0)
                {
                    allowed.Add(existing - TileWidth);
                }
                if ((ty + 1) * TileSize < World.Height)
                {
                    allowed.Add(existing + TileWidth);
                }
            }
            int exit = route.Exits[end];
            string key = route.Radius + ":" + exit + ":" + string.Join(",", allowed.OrderBy(value => value));
            if (!flows.TryGetValue(key, out var field))
            {
                field = Integrate(exit, route.Radius, allowed);
                flows.Add(key, field);
                flowOrder.Enqueue(key);
                retainedCells += field.Distance.Length;
                while (retainedCells > 2000000 && flowOrder.Count > 1)
                {
                    string old = flowOrder.Dequeue();
                    retainedCells -= flows[old].Distance.Length;
                    flows.Remove(old);
                }
            }
            else
            {
                Add("FlowCacheHits");
            }
            usedFlow = field;
            route.LastTile = tile;
            route.LastSegmentIndex = index;
            route.LastFlow = field;
            return FieldDirection(position, route.Radius, cell, field);
        }

        private Point2 FieldDirection(Point2 position, int radius, int cell, Flow field)
        {
            Point2 destination = World.Center(field.Exit);
            if (geometry.CanSegment(position, destination, radius))
            {
                Add("LosSteeringUses");
                return (destination - position).Normalized;
            }
            if (double.IsPositiveInfinity(field.Distance[cell]))
            {
                return default;
            }
            double west = Value(field, cell - 1, cell % World.Width > 0);
            double east = Value(field, cell + 1, cell % World.Width + 1 < World.Width);
            double south = Value(field, cell - World.Width, cell >= World.Width);
            double north = Value(field, cell + World.Width, cell + World.Width < World.CellCount);
            double dx = Gradient(west, east, field.Distance[cell]);
            double dy = Gradient(south, north, field.Distance[cell]);
            Point2 gradient = new Point2(dx, dy).Normalized;
            Point2 probe = position + gradient * Math.Min(radius, World.CellSizeMm * .2);
            if (gradient.LengthSquared > 0 && geometry.CanSegment(position, probe, radius))
            {
                return gradient;
            }
            int next = World.Neighbours(cell).Where(value => field.Included[value] && geometry.CanEdge(cell, value, radius))
                .OrderBy(value => field.Distance[value]).ThenBy(value => value).DefaultIfEmpty(-1).First();
            return next >= 0 && field.Distance[next] < field.Distance[cell] ? (World.Center(next) - position).Normalized : default;
        }

        internal bool OnRoute(Point2 position, Route route)
        {
            int cell = World.CellAt(position);
            return cell >= 0 && route.Revision == geometry.Revision && Array.IndexOf(route.Components, Component(cell, route.Radius)) >= 0;
        }

        private int Component(int cell, int radius)
        {
            if (!components.TryGetValue(radius, out var values))
            {
                values = Enumerable.Repeat(-1, World.CellCount).ToArray();
                var stand = geometry.Clearance(radius);
                for (int first = 0; first < World.CellCount; first++)
                {
                    if (!stand[first] || values[first] >= 0)
                    {
                        continue;
                    }
                    int tile = Tile(first);
                    var queue = new Queue<int>();
                    queue.Enqueue(first);
                    values[first] = first;
                    while (queue.Count > 0)
                    {
                        int current = queue.Dequeue();
                        foreach (int next in World.Neighbours(current))
                        {
                            if (values[next] < 0 && Tile(next) == tile && geometry.CanEdge(current, next, radius))
                            {
                                values[next] = first;
                                queue.Enqueue(next);
                            }
                        }
                    }
                    Add("TileConnectedComponents");
                }
                components.Add(radius, values);
            }
            return cell >= 0 && cell < values.Length ? values[cell] : -1;
        }

        private PortalGraph Graph(int radius)
        {
            if (graphs.TryGetValue(radius, out var cached))
            {
                return cached;
            }
            Add("PortalGraphBuilds");
            var stand = geometry.Clearance(radius);
            var cells = new List<int>();
            for (int cell = 0; cell < World.CellCount; cell++)
            {
                if (stand[cell] && World.Neighbours(cell).Any(next => Tile(next) != Tile(cell) && geometry.CanEdge(cell, next, radius)))
                {
                    cells.Add(cell);
                }
            }
            var graph = new PortalGraph { Cells = cells.ToArray(), Edges = cells.Select(_ => new List<Edge>()).ToArray() };
            var lookup = cells.Select((cell, index) => new { cell, index }).ToDictionary(pair => pair.cell, pair => pair.index);
            for (int i = 0; i < cells.Count; i++)
            {
                int tile = Tile(cells[i]);
                if (!graph.TileNodes.TryGetValue(tile, out var nodes))
                {
                    nodes = new List<int>();
                    graph.TileNodes.Add(tile, nodes);
                }
                nodes.Add(i);
            }
            for (int i = 0; i < cells.Count; i++)
            {
                int cell = cells[i];
                var distance = TileDistances(cell, radius);
                foreach (int other in graph.TileNodes[Tile(cell)])
                {
                    if (other != i && distance[cells[other]] >= 0)
                    {
                        graph.Edges[i].Add(new Edge(other, distance[cells[other]] * World.CellSizeMm));
                    }
                }
                foreach (int next in World.Neighbours(cell))
                {
                    if (Tile(next) != Tile(cell) && lookup.TryGetValue(next, out int other) && geometry.CanEdge(cell, next, radius))
                    {
                        graph.Edges[i].Add(new Edge(other, World.CellSizeMm));
                    }
                }
            }
            graphs.Add(radius, graph);
            Add("PortalGraphNodes", cells.Count);
            return graph;
        }

        private int[] TileDistances(int start, int radius)
        {
            var result = Enumerable.Repeat(-1, World.CellCount).ToArray();
            if (!geometry.Clearance(radius)[start])
            {
                return result;
            }
            int tile = Tile(start);
            var queue = new Queue<int>();
            result[start] = 0;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                foreach (int next in World.Neighbours(cell))
                {
                    if (Tile(next) == tile && result[next] < 0 && geometry.CanEdge(cell, next, radius))
                    {
                        result[next] = result[cell] + 1;
                        queue.Enqueue(next);
                    }
                }
            }
            return result;
        }

        private Flow Integrate(int exit, int radius, HashSet<int> tiles)
        {
            Add("SegmentFlowBuilds");
            var stand = geometry.Clearance(radius);
            var field = new Flow
            {
                Exit = exit,
                Included = Enumerable.Range(0, World.CellCount).Select(cell => stand[cell] && tiles.Contains(Tile(cell))).ToArray(),
                Distance = Enumerable.Repeat(double.PositiveInfinity, World.CellCount).ToArray()
            };
            if (!field.Included[exit])
            {
                return field;
            }
            var accepted = new bool[World.CellCount];
            var open = new MinHeap();
            if (radius < largestRadius && geometry.Clearance(largestRadius)[exit])
            {
                // Extend the restricted movement class over this same segment, never copy unreachable values.
                var restricted = Integrate(exit, largestRadius, tiles);
                int seeds = 0;
                for (int cell = 0; cell < World.CellCount; cell++)
                {
                    if (field.Included[cell] && !double.IsPositiveInfinity(restricted.Distance[cell]))
                    {
                        field.Distance[cell] = restricted.Distance[cell];
                        open.Push(cell, field.Distance[cell]);
                        seeds++;
                    }
                }
                if (seeds > 0)
                {
                    Add("ExtendedFlowBuilds");
                    Add("ExtendedSeedCells", seeds);
                }
            }
            for (int cell = 0; cell < World.CellCount; cell++)
            {
                if (field.Included[cell] && geometry.CanSegment(World.Center(cell), World.Center(exit), radius))
                {
                    double distance = Point2.Distance(World.Center(cell), World.Center(exit));
                    if (distance < field.Distance[cell])
                    {
                        field.Distance[cell] = distance;
                        open.Push(cell, distance);
                        Add("LosIntegrationSeeds");
                    }
                }
            }
            field.Distance[exit] = 0;
            open.Push(exit, 0);
            while (open.TryPop(out int cell, out double value))
            {
                if (accepted[cell] || value > field.Distance[cell] + 1e-7)
                {
                    continue;
                }
                accepted[cell] = true;
                Add("FmmAcceptedCells");
                foreach (int next in World.Neighbours(cell))
                {
                    if (!field.Included[next] || accepted[next] || !geometry.CanEdge(cell, next, radius))
                    {
                        continue;
                    }
                    double estimate = Eikonal(field, accepted, next, radius);
                    if (estimate < field.Distance[next] - 1e-7)
                    {
                        field.Distance[next] = estimate;
                        open.Push(next, estimate);
                    }
                }
            }
            return field;
        }

        private double Eikonal(Flow field, bool[] accepted, int cell, int radius)
        {
            double horizontal = double.PositiveInfinity;
            double vertical = double.PositiveInfinity;
            foreach (int next in World.Neighbours(cell))
            {
                if (!accepted[next] || !geometry.CanEdge(cell, next, radius))
                {
                    continue;
                }
                if (cell / World.Width == next / World.Width)
                {
                    horizontal = Math.Min(horizontal, field.Distance[next]);
                }
                else
                {
                    vertical = Math.Min(vertical, field.Distance[next]);
                }
            }
            double low = Math.Min(horizontal, vertical);
            double high = Math.Max(horizontal, vertical);
            double cost = World.CellSizeMm;
            if (double.IsPositiveInfinity(high) || high - low >= cost)
            {
                return low + cost;
            }
            return (low + high + Math.Sqrt(Math.Max(0, 2 * cost * cost - (high - low) * (high - low)))) * .5;
        }

        private int Tile(int cell)
        {
            return cell % World.Width / TileSize + cell / World.Width / TileSize * TileWidth;
        }

        private double Heuristic(int from, int to)
        {
            return (Math.Abs(from % World.Width - to % World.Width) + Math.Abs(from / World.Width - to / World.Width)) * World.CellSizeMm;
        }

        private static double Value(Flow field, int cell, bool inBounds)
        {
            return inBounds && field.Included[cell] ? field.Distance[cell] : double.PositiveInfinity;
        }

        private static double Gradient(double negative, double positive, double current)
        {
            if (double.IsPositiveInfinity(negative) && double.IsPositiveInfinity(positive))
            {
                return 0;
            }
            if (double.IsPositiveInfinity(negative))
            {
                return current - positive;
            }
            if (double.IsPositiveInfinity(positive))
            {
                return negative - current;
            }
            return negative - positive;
        }

        private void Add(string metric, long count = 1)
        {
            metrics.TryGetValue(metric, out long value);
            metrics[metric] = value + count;
        }
    }
}
