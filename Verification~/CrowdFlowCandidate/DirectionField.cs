using System;
using System.Collections.Generic;
using System.Numerics;

namespace GameStarter.Verification.CrowdFlow
{
    internal sealed class DirectionField
    {
        private static readonly (int X, int Y)[] NeighborOffsets =
        {
            (-1, -1), (0, -1), (1, -1),
            (-1, 0),            (1, 0),
            (-1, 1),  (0, 1),  (1, 1)
        };

        private readonly Vector2 min;
        private readonly Vector2 max;
        private readonly float step;
        private readonly float connectionRange;
        private readonly float zeroDistanceSquared;
        private readonly int columns;
        private readonly int rows;
        private readonly int gridNodeCount;
        private readonly Func<Vector2, Vector2, bool> canTraverse;
        private readonly Vector2[] nodes;
        private readonly float[] costs;
        private readonly Vector2[] next;
        private readonly List<Vector2> goals = new();

        internal DirectionField(Vector2 min, Vector2 max, float step, Func<Vector2, Vector2, bool> canTraverse,
            IReadOnlyList<Vector2> goals, IReadOnlyList<Vector2> portals = null)
        {
            if (!IsFinite(min) || !IsFinite(max) || min.X > max.X || min.Y > max.Y || !float.IsFinite(step) || step <= 0)
            {
                throw new ArgumentException("Invalid direction field bounds or spacing.");
            }

            this.min = min;
            this.max = max;
            this.step = step;
            this.canTraverse = canTraverse ?? throw new ArgumentNullException(nameof(canTraverse));
            ArgumentNullException.ThrowIfNull(goals);
            connectionRange = 2 * step;
            zeroDistanceSquared = step * step * 1e-8f;
            columns = checked((int)MathF.Floor((max.X - min.X) / step) + 1);
            rows = checked((int)MathF.Floor((max.Y - min.Y) / step) + 1);
            gridNodeCount = checked(columns * rows);
            var visiblePortals = new List<Vector2>();
            if (portals != null)
            {
                foreach (var portal in portals)
                {
                    if (Contains(portal) && canTraverse(portal, portal) && !visiblePortals.Contains(portal))
                    {
                        visiblePortals.Add(portal);
                    }
                }
            }

            nodes = new Vector2[checked(gridNodeCount + visiblePortals.Count)];
            visiblePortals.CopyTo(nodes, gridNodeCount);
            costs = new float[nodes.Length];
            next = new Vector2[nodes.Length];
            Array.Fill(costs, float.PositiveInfinity);

            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    nodes[y * columns + x] = Vector2.Min(max, min + new Vector2(x * step, y * step));
                }
            }

            var frontier = new PriorityQueue<int, (float Cost, int Node)>();
            foreach (var goal in goals)
            {
                if (!Contains(goal) || !canTraverse(goal, goal))
                {
                    continue;
                }

                this.goals.Add(goal);
                foreach (int node in NearbyNodes(goal))
                {
                    float cost = Vector2.Distance(nodes[node], goal);
                    if (cost < costs[node] && canTraverse(nodes[node], goal))
                    {
                        SetPath(node, cost, goal, frontier);
                    }
                }

                for (int portal = gridNodeCount; portal < nodes.Length; portal++)
                {
                    float cost = Vector2.Distance(nodes[portal], goal);
                    if (cost < costs[portal] && canTraverse(nodes[portal], goal))
                    {
                        SetPath(portal, cost, goal, frontier);
                    }
                }
            }

            while (frontier.TryDequeue(out int node, out var priority))
            {
                if (priority.Cost != costs[node])
                {
                    continue;
                }

                if (node < gridNodeCount)
                {
                    int x = node % columns;
                    int y = node / columns;
                    foreach (var offset in NeighborOffsets)
                    {
                        int neighborX = x + offset.X;
                        int neighborY = y + offset.Y;
                        if (neighborX >= 0 && neighborX < columns && neighborY >= 0 && neighborY < rows)
                        {
                            Connect(neighborY * columns + neighborX, node, priority.Cost);
                        }
                    }
                }
                else
                {
                    foreach (int predecessor in NearbyNodes(nodes[node]))
                    {
                        Connect(predecessor, node, priority.Cost);
                    }
                }

                for (int portal = gridNodeCount; portal < nodes.Length; portal++)
                {
                    if (portal != node && (node >= gridNodeCount || Vector2.DistanceSquared(nodes[portal], nodes[node]) <= connectionRange * connectionRange))
                    {
                        Connect(portal, node, priority.Cost);
                    }
                }
            }

            void Connect(int predecessor, int successor, float successorCost)
            {
                float cost = successorCost + Vector2.Distance(nodes[predecessor], nodes[successor]);
                if (cost < costs[predecessor] && canTraverse(nodes[predecessor], nodes[successor]))
                {
                    SetPath(predecessor, cost, nodes[successor], frontier);
                }
            }
        }

        internal Vector2 PreferredDirection(Vector2 position)
        {
            var guide = FindGuide(position);
            var target = guide.Target;
            if (guide.Node >= 0)
            {
                var downstream = next[guide.Node];
                // Skip a nearby anchor if its downhill successor is directly reachable.
                if (Vector2.DistanceSquared(position, target) <= zeroDistanceSquared
                    || (Vector2.DistanceSquared(position, downstream) > zeroDistanceSquared && canTraverse(position, downstream)))
                {
                    target = downstream;
                }
            }

            var direction = target - position;
            return direction.LengthSquared() <= zeroDistanceSquared ? Vector2.Zero : Vector2.Normalize(direction);
        }

        internal float CostAt(Vector2 position) => FindGuide(position).Cost;

        private (float Cost, Vector2 Target, int Node) FindGuide(Vector2 position)
        {
            float bestCost = float.PositiveInfinity;
            Vector2 target = position;
            int bestNode = -1;
            if (!Contains(position))
            {
                return (bestCost, target, bestNode);
            }

            foreach (var goal in goals)
            {
                float distance = Vector2.DistanceSquared(position, goal);
                if (distance < bestCost * bestCost && canTraverse(position, goal))
                {
                    bestCost = MathF.Sqrt(distance);
                    target = goal;
                }
            }

            if (goals.Count == 1 && float.IsFinite(bestCost))
            {
                return (bestCost, target, bestNode);
            }

            foreach (int node in NearbyNodes(position))
            {
                Consider(node);
            }

            // Portals are optional off-lattice anchors. Only exact directional sweeps can connect them.
            for (int portal = gridNodeCount; portal < nodes.Length; portal++)
            {
                Consider(portal);
            }

            return (bestCost, target, bestNode);

            void Consider(int node)
            {
                float cost = Vector2.Distance(position, nodes[node]) + costs[node];
                if (cost < bestCost && canTraverse(position, nodes[node]))
                {
                    bestCost = cost;
                    target = nodes[node];
                    bestNode = node;
                }
            }
        }

        private IEnumerable<int> NearbyNodes(Vector2 position)
        {
            var relative = (position - min) / step;
            int centerX = (int)MathF.Floor(relative.X);
            int centerY = (int)MathF.Floor(relative.Y);
            for (int y = Math.Max(0, centerY - 2); y <= Math.Min(rows - 1, centerY + 2); y++)
            {
                for (int x = Math.Max(0, centerX - 2); x <= Math.Min(columns - 1, centerX + 2); x++)
                {
                    int node = y * columns + x;
                    if (Vector2.DistanceSquared(position, nodes[node]) <= connectionRange * connectionRange)
                    {
                        yield return node;
                    }
                }
            }
        }

        private void SetPath(int node, float cost, Vector2 target, PriorityQueue<int, (float Cost, int Node)> frontier)
        {
            costs[node] = cost;
            next[node] = target;
            frontier.Enqueue(node, (cost, node));
        }

        private bool Contains(Vector2 p) =>
            IsFinite(p) && p.X >= min.X && p.X <= max.X && p.Y >= min.Y && p.Y <= max.Y;

        private static bool IsFinite(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
    }
}
