using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Static eight-connected navigation graph for one NPC body radius.</summary>
    internal sealed class AssaultNavigationGrid
    {
        internal const int CellSize = 160;
        internal const int NeighborCount = 8;
        private static readonly (int X, int Y)[] Directions = { (1, 0), (0, 1), (1, 1), (-1, 1), (-1, 0), (0, -1), (-1, -1), (1, -1) };
        private readonly AssaultNavigationMap map;
        private readonly int radius;
        private readonly Position2[] points;
        private readonly bool[] open;
        private readonly int[] neighbors;

        internal int Count => points.Length;

        internal AssaultNavigationGrid(AssaultNavigationMap map, int radius)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            if (radius <= 0 || radius > 2000)
            {
                throw new ArgumentOutOfRangeException(nameof(radius));
            }

            this.radius = radius;
            var first = new Position2(map.AreaMin.X + CellSize / 2, map.AreaMin.Y + CellSize / 2);
            int columns = (map.AreaMax.X - map.AreaMin.X + CellSize / 2) / CellSize;
            int rows = (map.AreaMax.Y - map.AreaMin.Y + CellSize / 2) / CellSize;
            points = new Position2[columns * rows];
            open = new bool[points.Length];
            neighbors = new int[points.Length * NeighborCount];
            Array.Fill(neighbors, -1);

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int node = row * columns + column;
                    points[node] = new Position2(first.X + column * CellSize, first.Y + row * CellSize);
                    open[node] = CanStand(points[node]);
                }
            }

            for (int row = 0; row < rows; row++)
            {
                for (int column = 0; column < columns; column++)
                {
                    int node = row * columns + column;
                    if (!open[node])
                    {
                        continue;
                    }

                    for (int direction = 0; direction < NeighborCount / 2; direction++)
                    {
                        var offset = Directions[direction];
                        int x = column + offset.X;
                        int y = row + offset.Y;
                        if (x < 0 || x >= columns || y < 0 || y >= rows)
                        {
                            continue;
                        }

                        int next = y * columns + x;
                        if (open[next] && CanSweep(points[node], points[next]))
                        {
                            neighbors[node * NeighborCount + direction] = next;
                            neighbors[next * NeighborCount + direction + NeighborCount / 2] = node;
                        }
                    }
                }
            }
        }

        internal Position2 Position(int node) => points[node];
        internal int Neighbor(int node, int direction) => neighbors[node * NeighborCount + direction];
        internal bool IsOpen(int node) => open[node];
        internal bool IsInside(Position2 point) => map.IsInsideArea(point);
        internal bool CanStand(Position2 point) => CanSweep(point, point);
        internal bool CanSweep(Position2 from, Position2 to) => map.CanSweep(from, to, radius);

        internal bool IsAttackPosition(Position2 point, int meleeReach)
        {
            return meleeReach >= 0 && map.CanReachBase(point, radius + meleeReach) && CanStand(point);
        }

        internal int ClosestNode(Position2 point)
        {
            if (!CanStand(point))
            {
                return -1;
            }

            int best = -1;
            long bestDistance = long.MaxValue;
            for (int node = 0; node < points.Length; node++)
            {
                if (!open[node])
                {
                    continue;
                }

                long distance = AssaultNavigationMap.DistanceSquared(point, points[node]);
                if (distance < bestDistance && CanSweep(point, points[node]))
                {
                    best = node;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
