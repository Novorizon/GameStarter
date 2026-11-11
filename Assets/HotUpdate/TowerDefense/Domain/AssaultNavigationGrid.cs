using System;
using System.Collections.Generic;

namespace Game.HotUpdate.TowerDefense.Domain
{
    internal sealed class AssaultNavigationGrid
    {
        private static readonly (int X, int Y)[] Directions =
        {
            (1, 0), (0, 1), (1, 1), (-1, 1),
            (-1, 0), (0, -1), (-1, -1), (1, -1)
        };

        private readonly Position2 anchor;
        private readonly (int X, int Y) forward;
        private readonly int step;
        private readonly int diagonalCost;
        private readonly int columns;
        private readonly int rows;
        private readonly Position2[] points;
        private readonly byte[] connections;
        private readonly Func<Position2, Position2, bool> staticClear;

        internal AssaultNavigationGrid(Position2 min, Position2 max, int step, Func<Position2, Position2, bool> staticClear, (int X, int Y) forward)
        {
            if (step <= 0 || max.X < min.X || max.Y < min.Y)
            {
                throw new ArgumentException("Invalid assault navigation grid.");
            }

            this.forward = forward;
            anchor = new Position2(forward.X - forward.Y > 0 ? min.X : max.X, forward.X + forward.Y > 0 ? min.Y : max.Y);
            this.step = step;
            this.staticClear = staticClear ?? throw new ArgumentNullException(nameof(staticClear));
            diagonalCost = checked((int)Math.Ceiling(step * Math.Sqrt(2)));
            long width = (long)max.X - min.X;
            long height = (long)max.Y - min.Y;
            columns = checked((int)((forward.X != 0 ? width : height) / step + 1));
            rows = checked((int)((forward.X != 0 ? height : width) / step + 1));
            points = new Position2[checked(columns * rows)];
            connections = new byte[points.Length];
            var walkable = new bool[points.Length];
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int cell = y * columns + x;
                    var point = new Position2(
                        (int)(anchor.X + ((long)forward.X * x - (long)forward.Y * y) * step),
                        (int)(anchor.Y + ((long)forward.Y * x + (long)forward.X * y) * step));
                    points[cell] = point;
                    walkable[cell] = staticClear(point, point);
                }
            }

            for (int cell = 0; cell < points.Length; cell++)
            {
                if (!walkable[cell])
                {
                    continue;
                }

                int x = cell % columns;
                int y = cell / columns;
                // A static sweep is symmetric. Check each undirected edge once.
                for (int direction = 0; direction < Directions.Length / 2; direction++)
                {
                    var offset = Directions[direction];
                    int nx = x + offset.X;
                    int ny = y + offset.Y;
                    if (nx < 0 || nx >= columns || ny < 0 || ny >= rows)
                    {
                        continue;
                    }

                    int neighbor = ny * columns + nx;
                    if (walkable[neighbor] && staticClear(points[cell], points[neighbor]))
                    {
                        connections[cell] |= (byte)(1 << direction);
                        connections[neighbor] |= (byte)(1 << (direction + Directions.Length / 2));
                    }
                }
            }
        }

        // Grid +X follows the dominant ingress-to-target direction. At a diagonal,
        // select the same side of that diagonal under each right-angle rotation.
        internal static (int X, int Y) ForwardFromIngress(Position2 primaryIngress, Position2 targetMin, Position2 targetMax)
        {
            long dx = (long)targetMin.X + targetMax.X - 2L * primaryIngress.X;
            long dy = (long)targetMin.Y + targetMax.Y - 2L * primaryIngress.Y;
            long horizontal = Math.Abs(dx);
            long vertical = Math.Abs(dy);
            bool useHorizontal = horizontal > vertical || (horizontal == vertical && (dx < 0) == (dy < 0));
            return useHorizontal ? (dx < 0 ? -1 : 1, 0) : (0, dy < 0 ? -1 : 1);
        }

        // Distances are in millimetres. Cells not connected to any ingress have int.MaxValue.
        internal int[] BuildDistanceField(IReadOnlyList<Position2> sources)
        {
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }

            return Traverse(sources, null, null, null);
        }

        internal void VisitReachable(Position2 start, Func<Position2, Position2, bool> available,
            int[] ingressDistance, Action<Position2> visit)
        {
            var visited = new bool[points.Length];
            var queue = new int[points.Length];
            int count = 0;
            VisitSourceCells(start, available, (cell, _) => Enqueue(cell));
            for (int head = 0; head < count; head++)
            {
                int cell = queue[head];
                var point = points[cell];
                if (ingressDistance[cell] != int.MaxValue && !SamePoint(point, start))
                {
                    visit(point);
                }

                byte connected = connections[cell];
                for (int direction = 0; direction < Directions.Length; direction++)
                {
                    if ((connected & (1 << direction)) == 0)
                    {
                        continue;
                    }

                    var offset = Directions[direction];
                    int neighbor = cell + offset.X + offset.Y * columns;
                    if (!visited[neighbor] && available(point, points[neighbor]))
                    {
                        Enqueue(neighbor);
                    }
                }
            }

            void Enqueue(int cell)
            {
                visited[cell] = true;
                queue[count++] = cell;
            }
        }

        internal Position2[] FindPath(Position2 start, Func<Position2, Position2, bool> available, Func<Position2, int, int, int> score, int[] ingressDistance)
        {
            if (score == null)
            {
                throw new ArgumentNullException(nameof(score));
            }

            if (ingressDistance == null || ingressDistance.Length != points.Length)
            {
                throw new ArgumentException("The ingress distance field does not match the assault grid.", nameof(ingressDistance));
            }

            int bestCell = -1;
            int bestScore = int.MinValue;
            var parents = new int[points.Length];
            Traverse(new[] { start }, available, parents, Evaluate);
            if (bestCell < 0)
            {
                return Array.Empty<Position2>();
            }

            var reversed = new List<Position2>();
            for (int cell = bestCell; cell >= 0; cell = parents[cell])
            {
                if (!SamePoint(points[cell], start))
                {
                    reversed.Add(points[cell]);
                }
            }

            var result = new List<Position2>();
            var from = start;
            for (int remaining = reversed.Count - 1; remaining >= 0;)
            {
                int next = 0;
                while (next < remaining && !CanConnect(from, reversed[next], available))
                {
                    next++;
                }

                from = reversed[next];
                result.Add(from);
                remaining = next - 1;
            }

            return result.ToArray();

            void Evaluate(int cell, int cost)
            {
                if (SamePoint(points[cell], start))
                {
                    return;
                }

                int value = score(points[cell], cost, ingressDistance[cell]);
                // First visit at equal score wins: lowest travel cost, then smallest grid index.
                if (value > bestScore)
                {
                    bestScore = value;
                    bestCell = cell;
                }
            }
        }

        private int[] Traverse(IReadOnlyList<Position2> sources, Func<Position2, Position2, bool> available, int[] parents, Action<int, int> visit)
        {
            var costs = new int[points.Length];
            Array.Fill(costs, int.MaxValue);
            var open = new SortedSet<(int Cost, int Cell)>();
            foreach (var source in sources)
            {
                VisitSourceCells(source, available, AddSource);
            }

            while (open.Count > 0)
            {
                var current = open.Min;
                open.Remove(current);
                visit?.Invoke(current.Cell, current.Cost);
                byte connected = connections[current.Cell];
                for (int direction = 0; direction < Directions.Length; direction++)
                {
                    if ((connected & (1 << direction)) == 0)
                    {
                        continue;
                    }

                    var offset = Directions[direction];
                    int neighbor = current.Cell + offset.X + offset.Y * columns;
                    int edgeCost = offset.X == 0 || offset.Y == 0 ? step : diagonalCost;
                    long cost = (long)current.Cost + edgeCost;
                    if (cost < costs[neighbor] && (available == null || available(points[current.Cell], points[neighbor])))
                    {
                        Relax(neighbor, (int)cost, current.Cell);
                    }
                }
            }

            return costs;

            void AddSource(int cell, int cost) => Relax(cell, cost, -1);

            void Relax(int cell, int cost, int parent)
            {
                if (cost >= costs[cell])
                {
                    return;
                }

                if (costs[cell] != int.MaxValue)
                {
                    open.Remove((costs[cell], cell));
                }

                costs[cell] = cost;
                if (parents != null)
                {
                    parents[cell] = parent;
                }

                open.Add((cost, cell));
            }
        }

        private void VisitSourceCells(Position2 source, Func<Position2, Position2, bool> available, Action<int, int> visit)
        {
            long connectionRange = 2L * step;
            double connectionRangeSquared = (double)connectionRange * connectionRange;
            long offsetX = (long)source.X - anchor.X;
            long offsetY = (long)source.Y - anchor.Y;
            long sx = forward.X * offsetX + forward.Y * offsetY;
            long sy = forward.X * offsetY - forward.Y * offsetX;
            long left = Math.Max(0, (sx - connectionRange) / step);
            long bottom = Math.Max(0, (sy - connectionRange) / step);
            long right = Math.Min(columns - 1, (sx + connectionRange) / step);
            long top = Math.Min(rows - 1, (sy + connectionRange) / step);
            if (left > right || bottom > top)
            {
                return;
            }

            for (int y = (int)bottom; y <= top; y++)
            {
                for (int x = (int)left; x <= right; x++)
                {
                    int cell = y * columns + x;
                    var point = points[cell];
                    double dx = (long)source.X - point.X;
                    double dy = (long)source.Y - point.Y;
                    double distanceSquared = dx * dx + dy * dy;
                    if (distanceSquared <= connectionRangeSquared && CanConnect(source, point, available))
                    {
                        double cost = Math.Ceiling(Math.Sqrt(distanceSquared));
                        if (cost < int.MaxValue)
                        {
                            visit(cell, (int)cost);
                        }
                    }
                }
            }
        }

        private bool CanConnect(Position2 from, Position2 to, Func<Position2, Position2, bool> available)
        {
            return staticClear(from, to) && (available == null || available(from, to));
        }

        private static bool SamePoint(Position2 first, Position2 second)
        {
            return first.X == second.X && first.Y == second.Y;
        }
    }
}
