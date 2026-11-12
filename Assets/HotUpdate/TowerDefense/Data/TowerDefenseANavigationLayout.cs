using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace Game.HotUpdate.TowerDefense.Data
{
    internal static class TowerDefenseANavigationLayout
    {
        // A cell is narrower than the configured attack band. A body can cover several cells.
        private const int CellSize = 250;

        internal static NavigationGrid Create(FightConfiguration configuration)
        {
            var terrain = configuration.Terrain;
            var attackLayout = configuration.Objective.BaseAttack;
            var footprint = attackLayout.Engagement;
            var approach = attackLayout.LocalArea;
            var route = configuration.Routes[0];
            var origin = terrain.Min;
            int width = DivideUp(terrain.Max.X - origin.X, CellSize);
            int height = DivideUp(terrain.Max.Y - origin.Y, CellSize);
            var walkable = new bool[width * height];
            var holding = new bool[walkable.Length];
            var target = new Position2((footprint.Min.X + footprint.Max.X) / 2, (footprint.Min.Y + footprint.Max.Y) / 2);

            bool StaticClear(Position2 from, Position2 to, int radius)
            {
                if (!terrain.CanMove(from, to, radius) || !ClearsLocalObstacles(approach, from, to, radius))
                {
                    return false;
                }

                double clearance = DistanceToFootprintSquared(footprint, from, to);
                return clearance > 0 && clearance >= (long)radius * radius;
            }

            bool AttackAllowed(Position2 position, int radius, int reach)
            {
                long distance = footprint.DistanceToFootprintSquared(position);
                long limit = (long)radius + reach;
                if (distance < (long)radius * radius || distance > limit * limit)
                {
                    return false;
                }

                var face = new Position2(Math.Clamp(position.X, footprint.Min.X, footprint.Max.X),
                    Math.Clamp(position.Y, footprint.Min.Y, footprint.Max.Y));
                return terrain.CanMove(position, face, 0) && ClearsLocalObstacles(approach, position, face, 0);
            }

            for (int cell = 0; cell < walkable.Length; cell++)
            {
                var position = CellCenter(origin, width, cell);
                walkable[cell] = StaticClear(position, position, 0);
                holding[cell] = walkable[cell] && (approach == null || position.X >= approach.Min.X && position.X <= approach.Max.X
                    && position.Y >= approach.Min.Y && position.Y <= approach.Max.Y);
            }

            var entries = FindEntrances(configuration, route, origin, width, walkable, StaticClear);
            int targetRadius = Math.Min(footprint.Max.X - footprint.Min.X, footprint.Max.Y - footprint.Min.Y) / 2;
            return new NavigationGrid(width, height, CellSize, origin, walkable, target, Math.Max(1, targetRadius), holding, entries,
                new[] { route.Points }, StaticClear, AttackAllowed);
        }

        internal static int FindSpawnCell(NavigationGrid grid, RouteDefinition route, int bodyRadius)
        {
            return FindEntrance(route, grid.CellCount, grid.Center, cell => grid.CanOccupy(cell, bodyRadius));
        }

        private static int[] FindEntrances(FightConfiguration configuration, RouteDefinition route, Position2 origin, int width, bool[] walkable,
            Func<Position2, Position2, int, bool> staticClear)
        {
            var scheduledEnemies = configuration.Waves.SelectMany(wave => wave.Groups).Select(group => group.EnemyId).ToHashSet();
            var radii = configuration.Enemies.Where(enemy => scheduledEnemies.Contains(enemy.Id)).Select(enemy => enemy.BodyRadius).Distinct().ToArray();
            var entries = new List<int>();
            foreach (int radius in radii.DefaultIfEmpty(0))
            {
                var cell = FindEntrance(route, walkable.Length, Center, candidate =>
                    walkable[candidate] && staticClear(Center(candidate), Center(candidate), radius));
                if (cell < 0)
                {
                    throw new InvalidOperationException($"The authored route entrance has no navigation cell for a {radius} mm body radius.");
                }

                if (!entries.Contains(cell))
                {
                    entries.Add(cell);
                }
            }

            return entries.ToArray();

            Position2 Center(int cell) => CellCenter(origin, width, cell);
        }

        private static int FindEntrance(RouteDefinition route, int cellCount, Func<int, Position2> center, Func<int, bool> canOccupy)
        {
            var start = route.Points[0];
            var next = route.Points[1];
            long bestDistance = long.MaxValue;
            int bestCell = -1;
            for (int cell = 0; cell < cellCount; cell++)
            {
                var point = center(cell);
                long dx = (long)point.X - start.X;
                long dy = (long)point.Y - start.Y;
                long distance = dx * dx + dy * dy;
                if (distance < bestDistance && DistanceToSegmentSquared(point, start, next) <= CellSize * CellSize && canOccupy(cell))
                {
                    bestCell = cell;
                    bestDistance = distance;
                }
            }

            return bestCell;
        }

        private static bool ClearsLocalObstacles(BaseApproachArea approach, Position2 from, Position2 to, int radius)
        {
            if (approach == null)
            {
                return true;
            }

            foreach (var obstacle in approach.Obstacles)
            {
                long limit = (long)radius + obstacle.Radius;
                if (DistanceToSegmentSquared(obstacle.Center, from, to) < limit * limit)
                {
                    return false;
                }
            }

            return true;
        }

        private static double DistanceToFootprintSquared(BaseAttackEngagement footprint, Position2 from, Position2 to)
        {
            // Grid edges run along one axis. The gaps to the rectangle then give the exact swept distance.
            long dx = Math.Max(0L, Math.Max((long)footprint.Min.X - Math.Max(from.X, to.X), (long)Math.Min(from.X, to.X) - footprint.Max.X));
            long dy = Math.Max(0L, Math.Max((long)footprint.Min.Y - Math.Max(from.Y, to.Y), (long)Math.Min(from.Y, to.Y) - footprint.Max.Y));
            if (from.X == to.X || from.Y == to.Y)
            {
                return dx * dx + dy * dy;
            }

            double first = 0;
            double last = 1;
            if (ClipSegment(from.X, to.X, footprint.Min.X, footprint.Max.X, ref first, ref last)
                && ClipSegment(from.Y, to.Y, footprint.Min.Y, footprint.Max.Y, ref first, ref last))
            {
                return 0;
            }

            double distance = Math.Min(footprint.DistanceToFootprintSquared(from), footprint.DistanceToFootprintSquared(to));
            distance = Math.Min(distance, DistanceToSegmentSquared(footprint.Min, from, to));
            distance = Math.Min(distance, DistanceToSegmentSquared(footprint.Max, from, to));
            distance = Math.Min(distance, DistanceToSegmentSquared(new Position2(footprint.Min.X, footprint.Max.Y), from, to));
            return Math.Min(distance, DistanceToSegmentSquared(new Position2(footprint.Max.X, footprint.Min.Y), from, to));
        }

        private static bool ClipSegment(int from, int to, int min, int max, ref double first, ref double last)
        {
            double a = (min - from) / (double)(to - from);
            double b = (max - from) / (double)(to - from);
            first = Math.Max(first, Math.Min(a, b));
            last = Math.Min(last, Math.Max(a, b));
            return first <= last;
        }

        private static double DistanceToSegmentSquared(Position2 point, Position2 from, Position2 to)
        {
            long dx = (long)to.X - from.X;
            long dy = (long)to.Y - from.Y;
            long length = dx * dx + dy * dy;
            long offsetX = (long)point.X - from.X;
            long offsetY = (long)point.Y - from.Y;
            double progress = length == 0 ? 0 : Math.Clamp((offsetX * dx + offsetY * dy) / (double)length, 0, 1);
            double x = offsetX - progress * dx;
            double y = offsetY - progress * dy;
            return x * x + y * y;
        }

        private static Position2 CellCenter(Position2 origin, int width, int cell)
        {
            return new Position2(origin.X + cell % width * CellSize + CellSize / 2, origin.Y + cell / width * CellSize + CellSize / 2);
        }

        private static int DivideUp(int value, int unit) => (value + unit - 1) / unit;
    }
}
