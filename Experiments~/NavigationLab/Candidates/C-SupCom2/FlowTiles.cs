using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.CSupCom2
{
    internal sealed class MinHeap
    {
        private readonly List<KeyValuePair<int, double>> entries = new List<KeyValuePair<int, double>>();
        internal int Count => entries.Count;

        internal void Push(int item, double cost)
        {
            var entry = new KeyValuePair<int, double>(item, cost);
            int index = entries.Count;
            entries.Add(entry);
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!Less(entry, entries[parent]))
                {
                    break;
                }
                entries[index] = entries[parent];
                index = parent;
            }
            entries[index] = entry;
        }

        internal KeyValuePair<int, double> Pop()
        {
            var result = entries[0];
            var last = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            if (entries.Count == 0)
            {
                return result;
            }
            int index = 0;
            while (index * 2 + 1 < entries.Count)
            {
                int child = index * 2 + 1;
                if (child + 1 < entries.Count && Less(entries[child + 1], entries[child]))
                {
                    child++;
                }
                if (!Less(entries[child], last))
                {
                    break;
                }
                entries[index] = entries[child];
                index = child;
            }
            entries[index] = last;
            return result;
        }

        private static bool Less(KeyValuePair<int, double> a, KeyValuePair<int, double> b)
        {
            return a.Value < b.Value || a.Value == b.Value && a.Key < b.Key;
        }
    }

    internal sealed class FlowRoute
    {
        internal int[] Nodes = Array.Empty<int>();
        internal IReadOnlyList<FlowTiles.Portal> Portals = Array.Empty<FlowTiles.Portal>();
        internal int Phase;
        internal int Goal;
        internal int GraphVersion;
    }

    internal readonly struct CrowdBody
    {
        internal Point2 Position { get; }
        internal int Radius { get; }
        internal CrowdBody(Point2 position, int radius)
        {
            Position = position;
            Radius = radius;
        }
    }

    internal sealed class FlowTiles
    {
        internal sealed class Portal
        {
            internal int Id;
            internal int Sector;
            internal int Peer;
            internal int[] Cells = Array.Empty<int>();
            internal int Center => Cells[Cells.Length / 2];
            internal readonly List<KeyValuePair<int, double>> Edges = new List<KeyValuePair<int, double>>();
        }

        private sealed class RadiusGraph
        {
            internal int Radius;
            internal int Version;
            internal bool[] Stand = Array.Empty<bool>();
            internal bool[] Edges = Array.Empty<bool>();
            internal int[]? ApproachEdges;
            internal double[] Costs = Array.Empty<double>();
            internal int[] Components = Array.Empty<int>();
            internal int[] HoldingDepth = Array.Empty<int>();
            internal readonly List<Portal> Portals = new List<Portal>();
            internal readonly Dictionary<int, List<int>> SectorPortals = new Dictionary<int, List<int>>();
            internal readonly Dictionary<int, Dictionary<int, int[]>> MergedRoutes = new Dictionary<int, Dictionary<int, int[]>>();
        }

        private class TileValues
        {
            internal int Sector;
            internal int Width;
            internal int SectorColumns;
            internal double[] Values = Enumerable.Repeat(double.PositiveInfinity, SectorSide * SectorSide).ToArray();
            internal int LocalIndex(int cell) => cell / Width % SectorSide * SectorSide + cell % Width % SectorSide;
            internal double this[int cell]
            {
                get
                {
                    int actualSector = cell % Width / SectorSide + cell / Width / SectorSide * SectorColumns;
                    return actualSector == Sector ? Values[LocalIndex(cell)] : double.PositiveInfinity;
                }
                set
                {
                    Values[LocalIndex(cell)] = value;
                }
            }
        }

        private sealed class Field : TileValues
        {
            internal int Goal;
            internal Portal? Exit;
            internal int[] Flow = Enumerable.Repeat(-1, SectorSide * SectorSide).ToArray();
            internal bool[] HasLineOfSight = new bool[SectorSide * SectorSide];
            internal long LastUse;
        }

        private WorldDefinition world;
        private readonly Dictionary<string, long> metrics;
        private readonly Dictionary<int, RadiusGraph> graphs = new Dictionary<int, RadiusGraph>();
        private readonly Dictionary<string, Field> fields = new Dictionary<string, Field>();
        private readonly int[] sectorRevisions;
        private readonly int sectorColumns;
        private readonly int sectorRows;
        private int graphVersion;
        private long useSequence;
        private const int SectorSide = 10;
        private const int MaximumCachedFields = 512;

        internal FlowTiles(WorldDefinition world, Dictionary<string, long> metrics)
        {
            this.world = world;
            this.metrics = metrics;
            sectorColumns = (world.Width + SectorSide - 1) / SectorSide;
            sectorRows = (world.Height + SectorSide - 1) / SectorSide;
            sectorRevisions = new int[sectorColumns * sectorRows];
        }

        internal bool CanStand(int cell, int radius)
        {
            return cell >= 0 && cell < world.CellCount && Graph(radius).Stand[cell];
        }

        internal bool Connected(int source, int goal, int radius)
        {
            RadiusGraph graph = Graph(radius);
            return source >= 0 && goal >= 0 && source < world.CellCount && goal < world.CellCount
                && graph.Components[source] >= 0 && graph.Components[source] == graph.Components[goal];
        }

        internal bool Connected(Point2 position, int goal, int radius)
        {
            int cell = world.CellAt(position);
            RadiusGraph graph = Graph(radius);
            if (cell < 0 || !CanStand(goal, radius))
            {
                return false;
            }
            return graph.Stand[cell] ? graph.Components[cell] == graph.Components[goal] : FindConnector(position, cell, goal, graph) >= 0;
        }

        internal bool NearHolding(Point2 position, int radius, int maximumSteps)
        {
            RadiusGraph graph = Graph(radius);
            int cell = world.CellAt(position);
            if (cell < 0)
            {
                return false;
            }
            if (graph.Stand[cell])
            {
                return graph.HoldingDepth[cell] <= maximumSteps;
            }
            int sx = cell % world.Width;
            int sy = cell / world.Width;
            for (int dy = -2; dy <= 2; dy++)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int x = sx + dx;
                    int y = sy + dy;
                    if (x < 0 || y < 0 || x >= world.Width || y >= world.Height)
                    {
                        continue;
                    }
                    int next = y * world.Width + x;
                    if (graph.Stand[next] && graph.HoldingDepth[next] + Math.Abs(dx) + Math.Abs(dy) <= maximumSteps
                        && Geometry.Segment(world, position, world.Center(next), radius))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal double[] ApproachCosts(Point2 position, int radius, IReadOnlyList<CrowdBody> bodies, int goal = -1, int[]? parents = null)
        {
            RadiusGraph graph = Graph(radius);
            int[] edges = graph.ApproachEdges ??= BuildApproachEdges(graph);
            var occupied = new bool[world.CellCount];
            var blockedEdges = new bool[edges.Length];
            foreach (var body in bodies)
            {
                double clearance = radius + body.Radius - SimulationProtocol.GeometryToleranceMm;
                int padding = (int)Math.Ceiling(clearance / world.CellSizeMm) + 1;
                int bodyCell = world.CellAt(body.Position);
                int x0 = Math.Max(0, bodyCell % world.Width - padding);
                int x1 = Math.Min(world.Width - 1, bodyCell % world.Width + padding);
                int y0 = Math.Max(0, bodyCell / world.Width - padding);
                int y1 = Math.Min(world.Height - 1, bodyCell / world.Width + padding);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        int cell = y * world.Width + x;
                        Point2 center = world.Center(cell);
                        occupied[cell] |= Point2.Distance(center, body.Position) < clearance;
                        int firstEdge = cell * 8;
                        for (int edge = firstEdge; edge < firstEdge + 8; edge++)
                        {
                            int next = edges[edge];
                            if (next >= 0 && Geometry.PointSegment(body.Position, center, world.Center(next)) < clearance)
                            {
                                blockedEdges[edge] = true;
                            }
                        }
                    }
                }
            }

            var distances = Enumerable.Repeat(double.PositiveInfinity, world.CellCount).ToArray();
            int origin = world.CellAt(position);
            if (origin < 0)
            {
                return distances;
            }

            var heap = new MinHeap();
            int sx = origin % world.Width;
            int sy = origin / world.Width;
            for (int y = Math.Max(0, sy - 2); y <= Math.Min(world.Height - 1, sy + 2); y++)
            {
                for (int x = Math.Max(0, sx - 2); x <= Math.Min(world.Width - 1, sx + 2); x++)
                {
                    int seed = y * world.Width + x;
                    Point2 center = world.Center(seed);
                    if (!graph.Stand[seed] || occupied[seed] || !Geometry.Segment(world, position, center, radius)
                        || bodies.Any(body => Geometry.PointSegment(body.Position, position, center)
                            < radius + body.Radius - SimulationProtocol.GeometryToleranceMm))
                    {
                        continue;
                    }

                    distances[seed] = Point2.Distance(position, center);
                    heap.Push(seed, distances[seed]);
                }
            }
            while (heap.Count > 0)
            {
                var item = heap.Pop();
                if (item.Value > distances[item.Key] + 1e-7)
                {
                    continue;
                }
                if (item.Key == goal)
                {
                    break;
                }

                int firstEdge = item.Key * 8;
                for (int edge = firstEdge; edge < firstEdge + 8; edge++)
                {
                    int next = edges[edge];
                    if (next < 0 || blockedEdges[edge] || occupied[next])
                    {
                        continue;
                    }
                    double distance = item.Value + world.CellSizeMm * (edge - firstEdge < 4 ? 1 : Math.Sqrt(2));
                    if (distance + 1e-7 < distances[next])
                    {
                        distances[next] = distance;
                        if (parents != null)
                        {
                            parents[next] = item.Key;
                        }
                        heap.Push(next, distance);
                    }
                }
            }
            Count("ApproachSearches");
            return distances;
        }

        private int[] BuildApproachEdges(RadiusGraph graph)
        {
            var edges = Enumerable.Repeat(-1, world.CellCount * 8).ToArray();
            for (int cell = 0; cell < world.CellCount; cell++)
            {
                if (!graph.Stand[cell])
                {
                    continue;
                }
                int first = cell * 8;
                int direction = 0;
                foreach (int next in FourNeighbours(cell))
                {
                    if (graph.Edges[cell * 4 + direction])
                    {
                        edges[first + direction] = next;
                    }
                    direction++;
                }
                for (int dy = -1; dy <= 1; dy += 2)
                {
                    for (int dx = -1; dx <= 1; dx += 2)
                    {
                        int x = cell % world.Width + dx;
                        int y = cell / world.Width + dy;
                        if (x >= 0 && x < world.Width && y >= 0 && y < world.Height)
                        {
                            int next = y * world.Width + x;
                            if (graph.Stand[next] && Geometry.Segment(world, world.Center(cell), world.Center(next), graph.Radius))
                            {
                                edges[first + direction] = next;
                            }
                        }
                        direction++;
                    }
                }
            }
            return edges;
        }

        internal Point2[] FindApproach(Point2 position, int goal, int radius, IReadOnlyList<CrowdBody> bodies)
        {
            var parents = Enumerable.Repeat(-1, world.CellCount).ToArray();
            double[] distances = ApproachCosts(position, radius, bodies, goal, parents);
            if (double.IsPositiveInfinity(distances[goal]))
            {
                return FindLocalApproach(position, world.Center(goal), radius, bodies);
            }
            var path = new List<Point2>();
            for (int cell = goal; cell >= 0; cell = parents[cell])
            {
                path.Add(world.Center(cell));
            }
            path.Reverse();
            return path.ToArray();
        }

        private Point2[] FindLocalApproach(Point2 position, Point2 target, int radius, IReadOnlyList<CrowdBody> bodies)
        {
            double half = world.CellSizeMm * .5;
            int startX = (int)Math.Round((position.X - world.Origin.X) / half);
            int startY = (int)Math.Round((position.Y - world.Origin.Y) / half);
            int targetX = (int)Math.Round((target.X - world.Origin.X) / half);
            int targetY = (int)Math.Round((target.Y - world.Origin.Y) / half);
            const int padding = 6;
            const int maximumSpan = 28;
            if (Math.Abs(startX - targetX) > maximumSpan || Math.Abs(startY - targetY) > maximumSpan)
            {
                return Array.Empty<Point2>();
            }

            int left = Math.Max(0, Math.Min(startX, targetX) - padding);
            int bottom = Math.Max(0, Math.Min(startY, targetY) - padding);
            int width = Math.Min(world.Width * 2, Math.Max(startX, targetX) + padding) - left + 1;
            int height = Math.Min(world.Height * 2, Math.Max(startY, targetY) + padding) - bottom + 1;
            int Index(int x, int y) => (y - bottom) * width + x - left;
            Point2 Position(int index) => world.Origin + new Point2((left + index % width) * half, (bottom + index / width) * half);
            var nearby = bodies.Where(body => body.Position.X >= world.Origin.X + left * half - body.Radius - radius
                && body.Position.X <= world.Origin.X + (left + width - 1) * half + body.Radius + radius
                && body.Position.Y >= world.Origin.Y + bottom * half - body.Radius - radius
                && body.Position.Y <= world.Origin.Y + (bottom + height - 1) * half + body.Radius + radius).ToArray();
            bool Clear(Point2 from, Point2 to) => Geometry.Segment(world, from, to, radius) && nearby.All(body =>
                Geometry.PointSegment(body.Position, from, to) >= radius + body.Radius - 1e-7);

            var distances = Enumerable.Repeat(double.PositiveInfinity, width * height).ToArray();
            var parents = Enumerable.Repeat(-1, distances.Length).ToArray();
            var connectors = new Point2[distances.Length];
            var heap = new MinHeap();
            for (int y = Math.Max(bottom, startY - 2); y < Math.Min(bottom + height, startY + 3); y++)
            {
                for (int x = Math.Max(left, startX - 2); x < Math.Min(left + width, startX + 3); x++)
                {
                    int index = Index(x, y);
                    Point2 end = Position(index);
                    var entry = new[] { position, new Point2(position.X, end.Y), new Point2(end.X, position.Y) };
                    foreach (Point2 connector in entry)
                    {
                        double length = Point2.Distance(position, connector) + Point2.Distance(connector, end);
                        if (length < distances[index] && Clear(position, connector) && Clear(connector, end))
                        {
                            distances[index] = length;
                            connectors[index] = connector;
                        }
                    }
                    if (!double.IsPositiveInfinity(distances[index]))
                    {
                        heap.Push(index, distances[index]);
                    }
                }
            }
            int goal = Index(targetX, targetY);
            while (heap.Count > 0)
            {
                var current = heap.Pop();
                if (current.Value > distances[current.Key])
                {
                    continue;
                }
                if (current.Key == goal)
                {
                    var path = new List<Point2>();
                    int seed = goal;
                    while (parents[seed] >= 0)
                    {
                        path.Add(Position(seed));
                        seed = parents[seed];
                    }
                    path.Add(Position(seed));
                    path.Add(connectors[seed]);
                    path.Reverse();
                    Count("HalfCellApproachRecoveries");
                    return path.ToArray();
                }

                int x0 = current.Key % width;
                int y0 = current.Key / width;
                Point2 from = Position(current.Key);
                for (int y = Math.Max(0, y0 - 1); y <= Math.Min(height - 1, y0 + 1); y++)
                {
                    for (int x = Math.Max(0, x0 - 1); x <= Math.Min(width - 1, x0 + 1); x++)
                    {
                        int next = y * width + x;
                        Point2 to = Position(next);
                        double distance = current.Value + Point2.Distance(from, to);
                        if (distance < distances[next] && Clear(from, to))
                        {
                            distances[next] = distance;
                            parents[next] = current.Key;
                            heap.Push(next, distance);
                        }
                    }
                }
            }
            return Array.Empty<Point2>();
        }

        internal bool IsPossiblePassage(int cell, int radius, double[] costs)
        {
            RadiusGraph graph = Graph(radius);
            int neighbours = 0;
            int direction = 0;
            foreach (int next in FourNeighbours(cell))
            {
                if (graph.Edges[cell * 4 + direction++] && !double.IsPositiveInfinity(costs[next]))
                {
                    neighbours++;
                }
            }
            return neighbours == 2;
        }

        internal HashSet<int> ChangeWorld(WorldDefinition next, WorldChange change)
        {
            var dirty = new HashSet<int>();
            int largestRadius = graphs.Count == 0 ? world.CellSizeMm : graphs.Keys.Max();
            int paddingCells = (int)Math.Ceiling(largestRadius / (double)world.CellSizeMm) + 1;
            foreach (int cell in change.BlockCells.Concat(change.OpenCells))
            {
                int x0 = Math.Max(0, cell % world.Width - paddingCells);
                int x1 = Math.Min(world.Width - 1, cell % world.Width + paddingCells);
                int y0 = Math.Max(0, cell / world.Width - paddingCells);
                int y1 = Math.Min(world.Height - 1, cell / world.Width + paddingCells);
                for (int y = y0; y <= y1; y++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        dirty.Add(Sector(y * world.Width + x));
                    }
                }
            }
            if (change.ReplaceTarget.HasValue)
            {
                var before = world.Target(change.ReplaceTarget.Value.Id);
                var after = change.ReplaceTarget.Value;
                for (int cell = 0; cell < world.CellCount; cell++)
                {
                    Point2 center = world.Center(cell);
                    double padding = world.CellSizeMm + largestRadius;
                    if (Point2.Distance(center, before.Center) <= before.RadiusMm + padding
                        || Point2.Distance(center, after.Center) <= after.RadiusMm + padding)
                    {
                        dirty.Add(Sector(cell));
                    }
                }
            }
            foreach (int sector in dirty)
            {
                sectorRevisions[sector]++;
            }
            foreach (string key in fields.Where(pair => dirty.Contains(pair.Value.Sector)).Select(pair => pair.Key).ToArray())
            {
                fields.Remove(key);
            }
            world = next;
            graphVersion++;
            graphs.Clear();
            Count("DirtySectors", dirty.Count);
            Count("DynamicGraphRebuilds");
            return dirty;
        }

        internal bool IsDirty(Point2 position, int goal, HashSet<int> dirty)
        {
            int current = world.CellAt(position);
            return current < 0 || goal < 0 || dirty.Contains(Sector(current)) || dirty.Contains(Sector(goal));
        }

        internal bool RebindRoute(FlowRoute route, int radius, HashSet<int> dirty)
        {
            RadiusGraph graph = Graph(radius);
            var rebound = new int[route.Nodes.Length];
            // Guide reads the preceding portal to recognize that the current portal is an arrival anchor.
            for (int index = Math.Max(0, route.Phase - 1); index < route.Nodes.Length; index++)
            {
                Portal old = route.Portals[route.Nodes[index]];
                Portal oldPeer = route.Portals[old.Peer];
                if (index >= route.Phase && (dirty.Contains(old.Sector) || dirty.Contains(oldPeer.Sector))
                    || !graph.SectorPortals.TryGetValue(old.Sector, out var candidates))
                {
                    return false;
                }
                int replacement = candidates.Where(id => graph.Portals[id].Cells.SequenceEqual(old.Cells)
                    && graph.Portals[graph.Portals[id].Peer].Cells.SequenceEqual(oldPeer.Cells)).DefaultIfEmpty(-1).First();
                if (replacement < 0)
                {
                    return false;
                }
                rebound[index] = replacement;
            }
            route.Nodes = rebound;
            route.Portals = graph.Portals;
            route.GraphVersion = graph.Version;
            return true;
        }

        internal FlowRoute? Route(int source, int goal, int radius)
        {
            RadiusGraph graph = Graph(radius);
            if (source < 0 || goal < 0 || !graph.Stand[source] || !graph.Stand[goal])
            {
                return null;
            }
            int sourceSector = Sector(source);
            int goalSector = Sector(goal);
            TileValues toGoal = GridDistances(graph, goalSector, goal);
            if (sourceSector == goalSector && !double.IsInfinity(toGoal[source]))
            {
                return new FlowRoute { Goal = goal, GraphVersion = graph.Version };
            }
            if (!graph.SectorPortals.TryGetValue(sourceSector, out var starts))
            {
                return null;
            }
            if (!graph.MergedRoutes.TryGetValue(goal, out var suffixes))
            {
                suffixes = new Dictionary<int, int[]>();
                graph.MergedRoutes[goal] = suffixes;
            }
            Count("PortalAStarRequests");
            TileValues sourceCosts = GridDistances(graph, sourceSector, source);
            var distances = Enumerable.Repeat(double.PositiveInfinity, graph.Portals.Count).ToArray();
            var parents = Enumerable.Repeat(-1, graph.Portals.Count).ToArray();
            var queue = new MinHeap();
            foreach (int start in starts)
            {
                int center = graph.Portals[start].Center;
                if (!double.IsInfinity(sourceCosts[center]))
                {
                    distances[start] = sourceCosts[center];
                    queue.Push(start, distances[start] + Point2.Distance(world.Center(center), world.Center(goal)));
                }
            }
            double best = double.PositiveInfinity;
            int finish = -1;
            int[]? mergedSuffix = null;
            while (queue.Count > 0)
            {
                var item = queue.Pop();
                var portal = graph.Portals[item.Key];
                double priority = distances[item.Key] + Point2.Distance(world.Center(portal.Center), world.Center(goal));
                if (item.Value > priority + 1e-7 || item.Value >= best)
                {
                    continue;
                }
                Count("PortalAStarExpanded");
                if (suffixes.TryGetValue(item.Key, out mergedSuffix))
                {
                    finish = item.Key;
                    Count("MergingAStarHits");
                    break;
                }
                if (portal.Sector == goalSector && !double.IsInfinity(toGoal[portal.Center]))
                {
                    double total = distances[item.Key] + toGoal[portal.Center];
                    if (total < best)
                    {
                        best = total;
                        finish = item.Key;
                    }
                }
                foreach (var edge in portal.Edges)
                {
                    double next = distances[item.Key] + edge.Value;
                    if (next + 1e-7 < distances[edge.Key])
                    {
                        distances[edge.Key] = next;
                        parents[edge.Key] = item.Key;
                        int center = graph.Portals[edge.Key].Center;
                        queue.Push(edge.Key, next + Point2.Distance(world.Center(center), world.Center(goal)));
                    }
                }
            }
            if (finish < 0)
            {
                return null;
            }
            var nodes = new List<int>();
            for (int node = finish; node >= 0; node = parents[node])
            {
                nodes.Add(node);
            }
            nodes.Reverse();
            if (mergedSuffix != null)
            {
                nodes.AddRange(mergedSuffix.Skip(1));
            }
            for (int index = 0; index < nodes.Count; index++)
            {
                if (!suffixes.ContainsKey(nodes[index]))
                {
                    suffixes[nodes[index]] = nodes.Skip(index).ToArray();
                }
            }
            return new FlowRoute { Nodes = nodes.ToArray(), Portals = graph.Portals, Goal = goal, GraphVersion = graph.Version };
        }

        internal Point2? Guide(Point2 position, int goal, int radius, ref FlowRoute? route)
        {
            int cell = world.CellAt(position);
            RadiusGraph graph = Graph(radius);
            if (cell < 0 || goal < 0 || !graph.Stand[goal])
            {
                return null;
            }
            if (!graph.Stand[cell])
            {
                // Continuous steering can legally enter part of a cell whose centre lacks clearance.
                // Reconnect through a physically visible route cell; never relocate the body to it.
                int connector = FindConnector(position, cell, goal, graph);
                route = null;
                Count("DisplacedSourceReconnects");
                return connector < 0 ? (Point2?)null : world.Center(connector);
            }
            if (route == null || route.Goal != goal || route.GraphVersion != graph.Version)
            {
                route = Route(cell, goal, radius);
            }
            if (route == null)
            {
                return null;
            }
            int sector = Sector(cell);
            if (route.Phase < route.Nodes.Length && sector == Sector(goal) && !double.IsInfinity(GridDistances(graph, sector, goal)[cell]))
            {
                // The final portal is an arrival anchor, not an instruction to leave a reachable goal sector.
                route.Phase = route.Nodes.Length;
                Count("TerminalPortalCompletions");
            }
            while (route.Phase < route.Nodes.Length)
            {
                Portal node = graph.Portals[route.Nodes[route.Phase]];
                if (node.Sector != sector)
                {
                    route.Phase++;
                    continue;
                }
                if (route.Phase > 0 && graph.Portals[route.Nodes[route.Phase - 1]].Peer == node.Id)
                {
                    route.Phase++;
                    continue;
                }
                break;
            }
            Portal? exit = route.Phase < route.Nodes.Length ? graph.Portals[route.Nodes[route.Phase]] : null;
            if (exit == null && sector != Sector(goal))
            {
                route = Route(cell, goal, radius);
                if (route == null)
                {
                    return null;
                }
                return Guide(position, goal, radius, ref route);
            }
            if (exit != null)
            {
                int offset = Array.IndexOf(exit.Cells, cell);
                if (offset >= 0)
                {
                    Portal peer = graph.Portals[exit.Peer];
                    Point2 destination = world.Center(peer.Cells[offset]);
                    return destination;
                }
            }
            else if (Geometry.Segment(world, position, world.Center(goal), radius))
            {
                Count("GoalLosQueries");
                return world.Center(goal);
            }
            Field field = GetField(graph, sector, goal, exit);
            if (double.IsInfinity(field[cell]))
            {
                route = Route(cell, goal, radius);
                Count("DisplacedSourceRepaths");
                return route == null ? (Point2?)null : Guide(position, goal, radius, ref route);
            }
            int cached = field.Flow[field.LocalIndex(cell)];
            if (cached >= 0 && Geometry.Segment(world, position, world.Center(cached), radius))
            {
                Count("CachedFlowDirections");
                return world.Center(cached);
            }
            double best = field[cell];
            int bestCell = cell;
            int x = cell % world.Width;
            int y = cell / world.Width;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height)
                    {
                        continue;
                    }
                    int next = ny * world.Width + nx;
                    if (Sector(next) != sector || field[next] >= best - 1e-7 || !graph.Stand[next])
                    {
                        continue;
                    }
                    if (!Geometry.Segment(world, position, world.Center(next), radius))
                    {
                        continue;
                    }
                    best = field[next];
                    bestCell = next;
                }
            }
            return world.Center(bestCell);
        }

        internal double GoalDistance(Point2 position, int goal, int radius)
        {
            int source = world.CellAt(position);
            if (source < 0)
            {
                return double.PositiveInfinity;
            }
            var route = Route(source, goal, radius);
            return route == null ? double.PositiveInfinity : Point2.Distance(position, world.Center(goal));
        }

        private int FindConnector(Point2 position, int cell, int goal, RadiusGraph graph)
        {
            int x0 = Math.Max(0, cell % world.Width - 2);
            int x1 = Math.Min(world.Width - 1, cell % world.Width + 2);
            int y0 = Math.Max(0, cell / world.Width - 2);
            int y1 = Math.Min(world.Height - 1, cell / world.Width + 2);
            int connector = -1;
            double bestConnector = double.PositiveInfinity;
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    int next = y * world.Width + x;
                    if (!graph.Stand[next] || graph.Components[next] != graph.Components[goal]
                        || !Geometry.Segment(world, position, world.Center(next), graph.Radius))
                    {
                        continue;
                    }
                    double cost = Point2.Distance(world.Center(next), world.Center(goal)) + Point2.Distance(position, world.Center(next)) * .1;
                    if (cost < bestConnector)
                    {
                        connector = next;
                        bestConnector = cost;
                    }
                }
            }
            return connector;
        }

        private RadiusGraph Graph(int radius)
        {
            if (graphs.TryGetValue(radius, out var existing))
            {
                return existing;
            }
            var graph = new RadiusGraph
            {
                Radius = radius,
                Version = graphVersion,
                Stand = new bool[world.CellCount],
                Edges = new bool[world.CellCount * 4],
                Costs = new double[world.CellCount]
            };
            for (int cell = 0; cell < world.CellCount; cell++)
            {
                graph.Stand[cell] = world.IsWalkable(cell) && Geometry.Clear(world, world.Center(cell), radius);
            }
            for (int cell = 0; cell < world.CellCount; cell++)
            {
                if (!graph.Stand[cell])
                {
                    continue;
                }
                int direction = 0;
                int blockedNeighbours = 0;
                foreach (int next in FourNeighbours(cell))
                {
                    bool valid = next >= 0 && graph.Stand[next] && Geometry.Segment(world, world.Center(cell), world.Center(next), radius);
                    graph.Edges[cell * 4 + direction++] = valid;
                    if (!valid)
                    {
                        blockedNeighbours++;
                    }
                }
                graph.Costs[cell] = 1 + blockedNeighbours * .125;
            }
            graph.Components = Enumerable.Repeat(-1, world.CellCount).ToArray();
            var componentQueue = new int[world.CellCount];
            int component = 0;
            for (int start = 0; start < world.CellCount; start++)
            {
                if (!graph.Stand[start] || graph.Components[start] >= 0)
                {
                    continue;
                }
                int first = 0;
                int end = 1;
                componentQueue[0] = start;
                graph.Components[start] = component;
                while (first < end)
                {
                    int cell = componentQueue[first++];
                    int direction = 0;
                    foreach (int next in FourNeighbours(cell))
                    {
                        bool valid = graph.Edges[cell * 4 + direction++];
                        if (!valid || graph.Components[next] >= 0)
                        {
                            continue;
                        }
                        graph.Components[next] = component;
                        componentQueue[end++] = next;
                    }
                }
                component++;
            }
            graph.HoldingDepth = Enumerable.Repeat(int.MaxValue, world.CellCount).ToArray();
            int holdingFirst = 0;
            int holdingEnd = 0;
            for (int cell = 0; cell < world.CellCount; cell++)
            {
                if (graph.Stand[cell] && world.HoldingAllowed(cell))
                {
                    graph.HoldingDepth[cell] = 0;
                    componentQueue[holdingEnd++] = cell;
                }
            }
            while (holdingFirst < holdingEnd)
            {
                int cell = componentQueue[holdingFirst++];
                int direction = 0;
                foreach (int next in FourNeighbours(cell))
                {
                    bool valid = graph.Edges[cell * 4 + direction++];
                    if (!valid || graph.HoldingDepth[next] <= graph.HoldingDepth[cell] + 1)
                    {
                        continue;
                    }
                    graph.HoldingDepth[next] = graph.HoldingDepth[cell] + 1;
                    componentQueue[holdingEnd++] = next;
                }
            }
            BuildPortals(graph);
            foreach (var portal in graph.Portals)
            {
                var distances = GridDistances(graph, portal.Sector, portal.Center);
                foreach (int other in graph.SectorPortals[portal.Sector])
                {
                    if (other != portal.Id && !double.IsInfinity(distances[graph.Portals[other].Center]))
                    {
                        portal.Edges.Add(new KeyValuePair<int, double>(other, distances[graph.Portals[other].Center]));
                    }
                }
                portal.Edges.Add(new KeyValuePair<int, double>(portal.Peer, world.CellSizeMm));
            }
            graphs[radius] = graph;
            Count("RadiusGraphs");
            Count("PortalNodesBuilt", graph.Portals.Count);
            return graph;
        }

        private void BuildPortals(RadiusGraph graph)
        {
            for (int sectorY = 0; sectorY < sectorRows; sectorY++)
            {
                for (int sectorX = 0; sectorX < sectorColumns; sectorX++)
                {
                    if (sectorX + 1 < sectorColumns)
                    {
                        int x = (sectorX + 1) * SectorSide - 1;
                        BuildWindowRuns(graph, Enumerable.Range(sectorY * SectorSide, Math.Min(SectorSide, world.Height - sectorY * SectorSide))
                            .Select(y => y * world.Width + x).ToArray(), 1);
                    }
                    if (sectorY + 1 < sectorRows)
                    {
                        int y = (sectorY + 1) * SectorSide - 1;
                        BuildWindowRuns(graph, Enumerable.Range(sectorX * SectorSide, Math.Min(SectorSide, world.Width - sectorX * SectorSide))
                            .Select(x => y * world.Width + x).ToArray(), world.Width);
                    }
                }
            }
        }

        private void BuildWindowRuns(RadiusGraph graph, int[] border, int delta)
        {
            var run = new List<int>();
            foreach (int cell in border)
            {
                bool passable = graph.Stand[cell] && graph.Stand[cell + delta]
                    && Geometry.Segment(world, world.Center(cell), world.Center(cell + delta), graph.Radius);
                if (passable && run.Count > 0)
                {
                    int previous = run[run.Count - 1];
                    passable = Geometry.Segment(world, world.Center(previous), world.Center(cell), graph.Radius)
                        && Geometry.Segment(world, world.Center(previous + delta), world.Center(cell + delta), graph.Radius);
                    if (!passable)
                    {
                        AddPortalPair(graph, run, delta);
                        run.Clear();
                        passable = true;
                    }
                }
                if (passable)
                {
                    run.Add(cell);
                }
                else if (run.Count > 0)
                {
                    AddPortalPair(graph, run, delta);
                    run.Clear();
                }
            }
            if (run.Count > 0)
            {
                AddPortalPair(graph, run, delta);
            }
        }

        private void AddPortalPair(RadiusGraph graph, List<int> cells, int delta)
        {
            int first = graph.Portals.Count;
            var a = new Portal { Id = first, Peer = first + 1, Cells = cells.ToArray(), Sector = Sector(cells[0]) };
            var b = new Portal { Id = first + 1, Peer = first, Cells = cells.Select(cell => cell + delta).ToArray(), Sector = Sector(cells[0] + delta) };
            foreach (var portal in new[] { a, b })
            {
                graph.Portals.Add(portal);
                if (!graph.SectorPortals.TryGetValue(portal.Sector, out var list))
                {
                    list = new List<int>();
                    graph.SectorPortals[portal.Sector] = list;
                }
                list.Add(portal.Id);
            }
        }

        private TileValues GridDistances(RadiusGraph graph, int sector, int goal)
        {
            var costs = new TileValues { Sector = sector, Width = world.Width, SectorColumns = sectorColumns };
            var heap = new MinHeap();
            costs[goal] = 0;
            heap.Push(goal, 0);
            while (heap.Count > 0)
            {
                var item = heap.Pop();
                if (item.Value > costs[item.Key])
                {
                    continue;
                }
                int direction = 0;
                foreach (int next in FourNeighbours(item.Key))
                {
                    bool valid = graph.Edges[item.Key * 4 + direction++];
                    if (!valid || Sector(next) != sector)
                    {
                        continue;
                    }
                    double cost = item.Value + world.CellSizeMm * (graph.Costs[item.Key] + graph.Costs[next]) * .5;
                    if (cost + 1e-7 < costs[next])
                    {
                        costs[next] = cost;
                        heap.Push(next, cost);
                    }
                }
            }
            return costs;
        }

        private Field GetField(RadiusGraph graph, int sector, int goal, Portal? exit)
        {
            string endpoint = exit == null ? "goal:" + goal
                : "portal:" + exit.Cells[0] + ":" + exit.Cells[exit.Cells.Length - 1] + ":" + (graph.Portals[exit.Peer].Center - exit.Center);
            string key = graph.Radius + ":" + sector + ":" + sectorRevisions[sector] + ":" + endpoint;
            if (fields.TryGetValue(key, out var existing))
            {
                existing.LastUse = ++useSequence;
                Count("FlowCacheHits");
                return existing;
            }
            var field = new Field
            {
                Sector = sector,
                Goal = goal,
                Exit = exit,
                Width = world.Width,
                SectorColumns = sectorColumns,
                LastUse = ++useSequence
            };
            var heap = new MinHeap();
            foreach (int seed in exit == null ? new[] { goal } : exit.Cells)
            {
                field[seed] = 0;
                heap.Push(seed, 0);
            }
            while (heap.Count > 0)
            {
                var item = heap.Pop();
                if (item.Value > field[item.Key] + 1e-7)
                {
                    continue;
                }
                Count("IntegrationExpanded");
                int direction = 0;
                foreach (int next in FourNeighbours(item.Key))
                {
                    bool valid = graph.Edges[item.Key * 4 + direction++];
                    if (!valid || Sector(next) != sector)
                    {
                        continue;
                    }
                    double integrated = Eikonal(graph, field, next);
                    if (integrated + 1e-7 < field[next])
                    {
                        field[next] = integrated;
                        heap.Push(next, integrated);
                    }
                }
            }
            BuildDirections(graph, field);
            fields[key] = field;
            if (fields.Count > MaximumCachedFields)
            {
                fields.Remove(fields.OrderBy(pair => pair.Value.LastUse).First().Key);
            }
            Count("FlowTilesBuilt");
            return field;
        }

        private void BuildDirections(RadiusGraph graph, Field field)
        {
            int sx = field.Sector % sectorColumns * SectorSide;
            int sy = field.Sector / sectorColumns * SectorSide;
            for (int y = sy; y < Math.Min(world.Height, sy + SectorSide); y++)
            {
                for (int x = sx; x < Math.Min(world.Width, sx + SectorSide); x++)
                {
                    int cell = y * world.Width + x;
                    if (!graph.Stand[cell] || double.IsInfinity(field[cell]))
                    {
                        continue;
                    }
                    int local = field.LocalIndex(cell);
                    if (field.Exit != null)
                    {
                        int offset = Array.IndexOf(field.Exit.Cells, cell);
                        if (offset >= 0)
                        {
                            field.Flow[local] = graph.Portals[field.Exit.Peer].Cells[offset];
                            continue;
                        }
                    }
                    else if (Geometry.Segment(world, world.Center(cell), world.Center(field.Goal), graph.Radius))
                    {
                        field.Flow[local] = field.Goal;
                        field.HasLineOfSight[local] = true;
                        continue;
                    }
                    double best = field[cell];
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= world.Width || ny >= world.Height)
                            {
                                continue;
                            }
                            int next = ny * world.Width + nx;
                            if (Sector(next) != field.Sector || !graph.Stand[next] || field[next] >= best - 1e-7
                                || !Geometry.Segment(world, world.Center(cell), world.Center(next), graph.Radius))
                            {
                                continue;
                            }
                            best = field[next];
                            field.Flow[local] = next;
                        }
                    }
                }
            }
            Count("FlowDirectionsBuilt");
        }

        private double Eikonal(RadiusGraph graph, Field field, int cell)
        {
            var neighbours = FourNeighbours(cell).ToArray();
            double x = Math.Min(Value(0), Value(1));
            double y = Math.Min(Value(2), Value(3));
            double cost = world.CellSizeMm * graph.Costs[cell];
            if (double.IsInfinity(x))
            {
                return y + cost;
            }
            if (double.IsInfinity(y))
            {
                return x + cost;
            }
            double difference = Math.Abs(x - y);
            return difference >= cost ? Math.Min(x, y) + cost : (x + y + Math.Sqrt(2 * cost * cost - difference * difference)) * .5;

            double Value(int direction)
            {
                int next = neighbours[direction];
                return graph.Edges[cell * 4 + direction] && Sector(next) == field.Sector ? field[next] : double.PositiveInfinity;
            }
        }

        private IEnumerable<int> FourNeighbours(int cell)
        {
            int x = cell % world.Width;
            int y = cell / world.Width;
            yield return x > 0 ? cell - 1 : -1;
            yield return x + 1 < world.Width ? cell + 1 : -1;
            yield return y > 0 ? cell - world.Width : -1;
            yield return y + 1 < world.Height ? cell + world.Width : -1;
        }

        private int Sector(int cell)
        {
            return cell % world.Width / SectorSide + cell / world.Width / SectorSide * sectorColumns;
        }

        private void Count(string name, long amount = 1)
        {
            metrics.TryGetValue(name, out long current);
            metrics[name] = current + amount;
        }
    }
}
