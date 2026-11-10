using System;
using System.Collections.Generic;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    /// <summary>Explicit square-cell geometry. Origin is the lower-left boundary, not a cell centre.</summary>
    public sealed class NavigationGrid
    {
        private readonly bool[] walkable;
        private readonly bool[] holdingAllowed;
        public int Width { get; }
        public int Height { get; }
        public int CellSize { get; }
        public int CellCount => walkable.Length;
        public Position2 Origin { get; }
        public Position2 TargetCenter { get; }
        public int TargetRadius { get; }
        public IReadOnlyList<int> SpawnCells { get; }

        public NavigationGrid(
            int width,
            int height,
            int cellSize,
            Position2 origin,
            bool[] walkable,
            Position2 targetCenter,
            int targetRadius,
            bool[] holdingAllowed = null,
            int[] spawnCells = null)
        {
            if (width < 2 || height < 2 || width > 256 || height > 256 || cellSize < 50 || cellSize > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(width), "Expected a bounded grid and 50–10000 mm cells.");
            }

            if (walkable == null || walkable.Length != width * height || (holdingAllowed != null && holdingAllowed.Length != walkable.Length))
            {
                throw new ArgumentException("Grid masks must contain Width * Height entries.");
            }

            if (targetRadius <= 0 || targetRadius > 1000000)
            {
                throw new ArgumentOutOfRangeException(nameof(targetRadius));
            }

            if (Math.Abs((long)origin.X) > 100000000 || Math.Abs((long)origin.Y) > 100000000
                || Math.Abs((long)targetCenter.X) > 100000000 || Math.Abs((long)targetCenter.Y) > 100000000)
            {
                throw new ArgumentOutOfRangeException(nameof(origin), "Navigation coordinates must remain within ±100 km.");
            }

            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
            TargetCenter = targetCenter;
            TargetRadius = targetRadius;
            this.walkable = (bool[])walkable.Clone();
            this.holdingAllowed = holdingAllowed == null ? (bool[])walkable.Clone() : (bool[])holdingAllowed.Clone();
            var entries = spawnCells == null ? Array.Empty<int>() : (int[])spawnCells.Clone();
            foreach (int cell in entries)
            {
                if (!IsWalkable(cell))
                {
                    throw new ArgumentException("A declared spawn cell is not walkable.", nameof(spawnCells));
                }
            }

            SpawnCells = Array.AsReadOnly(entries);
            if (CellAt(targetCenter) < 0)
            {
                throw new ArgumentException("The target centre must be inside the grid.", nameof(targetCenter));
            }
        }

        public bool IsWalkable(int cell) => cell >= 0 && cell < CellCount && walkable[cell];
        public bool HoldingAllowed(int cell) => IsWalkable(cell) && holdingAllowed[cell];

        public int CellAt(Position2 point)
        {
            long x = (long)point.X - Origin.X;
            long y = (long)point.Y - Origin.Y;
            if (x < 0 || y < 0 || x >= (long)Width * CellSize || y >= (long)Height * CellSize)
            {
                return -1;
            }

            return (int)(y / CellSize) * Width + (int)(x / CellSize);
        }

        public Position2 Center(int cell)
        {
            if (cell < 0 || cell >= CellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(cell));
            }

            return new Position2(checked(Origin.X + cell % Width * CellSize + CellSize / 2), checked(Origin.Y + cell / Width * CellSize + CellSize / 2));
        }

        public bool CanOccupy(int cell, int radius)
        {
            if (!IsWalkable(cell) || radius <= 0 || radius > CellSize / 2)
            {
                return false;
            }

            long required = (long)TargetRadius + radius;
            return DistanceSquared(Center(cell), TargetCenter) >= required * required;
        }

        public bool HasAttackLine(int cell, int radius, int attackRange)
        {
            if (!CanOccupy(cell, radius) || attackRange < 0)
            {
                return false;
            }

            long reach = (long)TargetRadius + radius + attackRange;
            return DistanceSquared(Center(cell), TargetCenter) <= reach * reach && HasLine(Center(cell), TargetCenter);
        }

        public bool CanTraverse(int first, int second, int radius)
        {
            if (!CanOccupy(first, radius) || !CanOccupy(second, radius)
                || Math.Abs(first % Width - second % Width) + Math.Abs(first / Width - second / Width) != 1)
            {
                return false;
            }

            var from = Center(first);
            var to = Center(second);
            int closestX = Math.Max(Math.Min(from.X, to.X), Math.Min(Math.Max(from.X, to.X), TargetCenter.X));
            int closestY = Math.Max(Math.Min(from.Y, to.Y), Math.Min(Math.Max(from.Y, to.Y), TargetCenter.Y));
            long clearance = (long)TargetRadius + radius;
            return DistanceSquared(new Position2(closestX, closestY), TargetCenter) >= clearance * clearance;
        }

        /// <summary>Supercover grid traversal; a line through a blocked corner is not line of sight.</summary>
        public bool HasLine(Position2 from, Position2 to)
        {
            int start = CellAt(from);
            int end = CellAt(to);
            if (!IsWalkable(start) || !IsWalkable(end))
            {
                return false;
            }

            int x = start % Width;
            int y = start / Width;
            int endX = end % Width;
            int endY = end / Width;
            long dx = (long)to.X - from.X;
            long dy = (long)to.Y - from.Y;
            int sx = Math.Sign(dx);
            int sy = Math.Sign(dy);
            long ax = Math.Abs(dx);
            long ay = Math.Abs(dy);
            long nextX = sx == 0 ? 0 : Math.Abs((long)Origin.X + (sx > 0 ? x + 1 : x) * CellSize - from.X);
            long nextY = sy == 0 ? 0 : Math.Abs((long)Origin.Y + (sy > 0 ? y + 1 : y) * CellSize - from.Y);
            while (x != endX || y != endY)
            {
                long crossX = sx == 0 ? long.MaxValue : nextX * ay;
                long crossY = sy == 0 ? long.MaxValue : nextY * ax;
                if (crossX == crossY)
                {
                    if (!WalkableXY(x + sx, y) || !WalkableXY(x, y + sy))
                    {
                        return false;
                    }

                    x += sx;
                    y += sy;
                    nextX += CellSize;
                    nextY += CellSize;
                }
                else if (crossX < crossY)
                {
                    x += sx;
                    nextX += CellSize;
                }
                else
                {
                    y += sy;
                    nextY += CellSize;
                }

                if (!WalkableXY(x, y))
                {
                    return false;
                }
            }

            return true;
        }

        public IEnumerable<int> Neighbours(int cell)
        {
            int x = cell % Width;
            int y = cell / Width;
            if (x + 1 < Width)
            {
                yield return cell + 1;
            }

            if (y + 1 < Height)
            {
                yield return cell + Width;
            }

            if (x > 0)
            {
                yield return cell - 1;
            }

            if (y > 0)
            {
                yield return cell - Width;
            }
        }

        internal static long DistanceSquared(Position2 first, Position2 second)
        {
            long dx = (long)first.X - second.X;
            long dy = (long)first.Y - second.Y;
            return dx * dx + dy * dy;
        }

        private bool WalkableXY(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height && walkable[y * Width + x];
    }
}
