using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    /// <summary>Explicit square-cell geometry. Origin is the lower-left boundary, not a cell centre.</summary>
    public sealed partial class NavigationGrid
    {
        private readonly bool[] walkable;
        private readonly bool[] holdingAllowed;
        private readonly bool[] terrainBlocks;
        private readonly IReadOnlyList<Position2[]> roadCenterlines;
        private readonly int[] roadCosts;
        private readonly int[] roadProgress;
        private readonly Func<Position2, Position2, int, bool> staticClear;
        private readonly Func<Position2, int, int, bool> attackAllowed;
        private readonly ConcurrentDictionary<int, Clearance> clearance = new ConcurrentDictionary<int, Clearance>();
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
            int[] spawnCells = null,
            IReadOnlyList<Position2[]> roadCenterlines = null,
            Func<Position2, Position2, int, bool> staticClear = null,
            Func<Position2, int, int, bool> attackAllowed = null)
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
            terrainBlocks = new bool[walkable.Length];
            this.roadCenterlines = roadCenterlines?.Select(points => (Position2[])points.Clone()).ToArray();
            this.staticClear = staticClear;
            this.attackAllowed = attackAllowed;
            if (this.roadCenterlines != null && this.roadCenterlines.Any(points => points.Length > 1))
            {
                roadCosts = new int[CellCount];
                roadProgress = new int[CellCount];
                CalculateRoadCosts();
            }
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

        internal NavigationGrid WithTerrain(IReadOnlyCollection<int> blocked, IReadOnlyCollection<int> opened)
        {
            var next = (bool[])walkable.Clone();
            foreach (int cell in blocked)
            {
                next[cell] = false;
            }

            foreach (int cell in opened)
            {
                next[cell] = true;
            }

            var updated = new NavigationGrid(
                Width,
                Height,
                CellSize,
                Origin,
                next,
                TargetCenter,
                TargetRadius,
                holdingAllowed,
                SpawnCells.ToArray(),
                roadCenterlines,
                staticClear,
                attackAllowed);
            terrainBlocks.CopyTo(updated.terrainBlocks, 0);
            foreach (int cell in blocked)
            {
                updated.terrainBlocks[cell] = true;
            }

            foreach (int cell in opened)
            {
                updated.terrainBlocks[cell] = false;
            }

            return updated;
        }

        internal bool DiscOverlapsCell(Position2 position, int radius, int cell)
        {
            int left = Origin.X + cell % Width * CellSize;
            int bottom = Origin.Y + cell / Width * CellSize;
            int closestX = Math.Max(left, Math.Min(left + CellSize, position.X));
            int closestY = Math.Max(bottom, Math.Min(bottom + CellSize, position.Y));
            return DistanceSquared(position, new Position2(closestX, closestY)) < (long)radius * radius;
        }

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
            return IsWalkable(cell) && SupportsRadius(radius) && clearance.GetOrAdd(radius, CalculateClearance).Stand[cell];
        }

        public bool HasAttackLine(int cell, int radius, int attackRange)
        {
            if (!CanOccupy(cell, radius) || attackRange < 0)
            {
                return false;
            }

            var point = Center(cell);
            if (attackAllowed != null)
            {
                return attackAllowed(point, radius, attackRange) && HasLine(point, TargetCenter, onlyTerrainChanges: true);
            }

            long reach = (long)TargetRadius + radius + attackRange;
            return DistanceSquared(point, TargetCenter) <= reach * reach && HasLine(point, TargetCenter);
        }

        public bool CanTraverse(int first, int second, int radius)
        {
            if (!IsWalkable(first) || !IsWalkable(second) || !SupportsRadius(radius))
            {
                return false;
            }

            int lower = Math.Min(first, second);
            int difference = Math.Abs(first - second);
            var profile = clearance.GetOrAdd(radius, CalculateClearance);
            return difference == Width ? profile.North[lower]
                : difference == 1 && first / Width == second / Width && profile.East[lower];
        }

        /// <summary>Supercover grid traversal; a line through a blocked corner is not line of sight.</summary>
        public bool HasLine(Position2 from, Position2 to) => HasLine(from, to, false);

        private bool HasLine(Position2 from, Position2 to, bool onlyTerrainChanges)
        {
            int start = CellAt(from);
            int end = CellAt(to);
            if (start < 0 || end < 0 || !AllowsSight(start, onlyTerrainChanges) || !AllowsSight(end, onlyTerrainChanges))
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
                    if (!AllowsSight(x + sx, y, onlyTerrainChanges) || !AllowsSight(x, y + sy, onlyTerrainChanges))
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

                if (!AllowsSight(x, y, onlyTerrainChanges))
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

        internal bool SupportsRadius(int radius) => radius > 0 && radius <= Math.Max(2000, CellSize / 2);

        internal bool HasRoadPreference => roadCosts != null;

        internal int RoadCost(int cell) => roadCosts?[cell] ?? 0;

        internal int RoadProgress(int cell) => roadProgress?[cell] ?? 0;

        private bool AllowsSight(int cell, bool onlyTerrainChanges) => onlyTerrainChanges ? !terrainBlocks[cell] : walkable[cell];

        private bool AllowsSight(int x, int y, bool onlyTerrainChanges)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height && AllowsSight(y * Width + x, onlyTerrainChanges);
        }
    }
}
