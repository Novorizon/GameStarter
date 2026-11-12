using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private NavigationCrowd aCrowd;
        private Dictionary<int, Position2> aSpawns;
        private int aReach;
        private int aStepsPerBattleTick;

        internal bool UsesACrowd => aCrowd != null;

        private void InitializeANavigation()
        {
            var grid = Configuration.AGrid;
            if (grid == null)
            {
                return;
            }

            aStepsPerBattleTick = Configuration.ForestRedrawEntrance.HasValue ? 2 : 1;
            aCrowd = new NavigationCrowd(grid, planningHorizon: 4096 * aStepsPerBattleTick, stableSiegePositions: true);
            aReach = Configuration.Objective.BaseAttack.Engagement.Reach;
            aSpawns = new Dictionary<int, Position2>();
            var entrance = Configuration.Routes[0].Points[0];
            foreach (int radius in Configuration.Waves.SelectMany(wave => wave.Groups).Select(group => enemyDefinitions[group.EnemyId].BodyRadius).Distinct())
            {
                int spawnCell = grid.SpawnCells.Where(cell => grid.CanOccupy(cell, radius))
                    .OrderBy(cell => DistanceSquared(grid.Center(cell), entrance))
                    .First();
                aSpawns.Add(radius, grid.Center(spawnCell));
                aCrowd.RegisterProfile(radius, aReach);
            }
        }

        private NavigationAgent TrySpawnAEnemy(EnemyDefinition definition)
        {
            var spawn = aSpawns[definition.BodyRadius];
            int speed = Math.Max(1, definition.SpeedPerTick / aStepsPerBattleTick);
            return aCrowd.TrySpawn(nextEnemyId, spawn, speed, definition.BodyRadius, aReach) ? aCrowd.Agents[^1] : null;
        }

        private void AdvanceANavigation()
        {
            long end = Tick * aStepsPerBattleTick;
            for (long step = end - aStepsPerBattleTick + 1; step <= end; step++)
            {
                aCrowd.Step(step);
            }
            foreach (var enemy in enemies)
            {
                enemy.Position = enemy.NavigationAgent.Position;
                enemy.Distance = Math.Max(enemy.Distance, ProjectOntoRoute(enemy.Route, enemy.Position));
                if (enemy.NavigationAgent.Status != NavigationAgentStatus.Attacking)
                {
                    enemy.NextBaseAttackTick = 0;
                }
            }
        }

        private static int ProjectOntoRoute(Route route, Position2 position)
        {
            int start = 0;
            int progress = 0;
            double closest = double.PositiveInfinity;
            for (int i = 0; i < route.SegmentLengths.Length; i++)
            {
                var a = route.Definition.Points[i];
                var b = route.Definition.Points[i + 1];
                long dx = (long)b.X - a.X;
                long dy = (long)b.Y - a.Y;
                long offsetX = (long)position.X - a.X;
                long offsetY = (long)position.Y - a.Y;
                double fraction = Math.Clamp((offsetX * dx + offsetY * dy) / (double)(dx * dx + dy * dy), 0, 1);
                double x = offsetX - dx * fraction;
                double y = offsetY - dy * fraction;
                double distance = x * x + y * y;
                int length = route.SegmentLengths[i];
                if (distance < closest)
                {
                    closest = distance;
                    progress = start + (int)(length * fraction);
                }

                start += length;
            }

            return Math.Min(progress, route.Length);
        }

        private bool ChangeATowerTerrain(Position2 center, bool building, Tower soldTower = null)
        {
            var baseline = Configuration.AGrid;
            var cells = new List<int>();
            for (int cell = 0; cell < baseline.CellCount; cell++)
            {
                if (!baseline.IsWalkable(cell) || !baseline.DiscOverlapsCell(center, TowerFoundationRadiusMillimetres, cell))
                {
                    continue;
                }

                int footprintCell = cell;
                if (building || !towers.Any(tower => tower != soldTower
                    && baseline.DiscOverlapsCell(tower.Slot.Position, TowerFoundationRadiusMillimetres, footprintCell)))
                {
                    cells.Add(cell);
                }
            }

            if (cells.Count == 0)
            {
                return true;
            }

            return building
                ? aCrowd.TryApplyTerrainChange(cells, Array.Empty<int>())
                : aCrowd.TryApplyTerrainChange(Array.Empty<int>(), cells);
        }
    }
}
