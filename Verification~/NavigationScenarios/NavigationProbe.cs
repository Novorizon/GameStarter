using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        // Compiled only into this verifier, so construction can target a future path while current bodies remain clear.
        internal int FixtureActiveManeuverCount => maneuvers.Count;
        internal int FixtureAdmittedCount => enemies.Count(enemy => enemy.InAssaultArea);
        internal int FixtureMinimumTighteningDistance => MinimumTighteningDistance;

        internal (AssaultNavigationGrid Grid, bool[] StagingPositions) FixtureStagingGrid(int radius)
        {
            return (navigationGrids[radius], pathSearches[radius].StagingPositions);
        }

        internal bool FixtureInProtectedApproach(Position2 position, int radius) => BlocksAny(protectedApproaches, position, position, radius);

        internal bool FixtureAttackVacancyYieldActive => yieldingAttackPath != null;

        internal bool FixtureHasBoundedManeuverWait(int id)
        {
            return maneuvers.Any(enemy => enemy.Id == id && enemy.Maneuver != null
                && enemy.ManeuverWaitTicks > 0 && enemy.ManeuverWaitTicks <= MaximumManeuverWaitTicks);
        }

        internal string FixtureMovementDetails(int id)
        {
            var enemy = enemies.Single(candidate => candidate.Id == id);
            string reservation = enemy.Maneuver == null ? "none"
                : $"{enemy.ManeuverIndex}/{enemy.Maneuver.Length} goal=({enemy.Maneuver[^1].X},{enemy.Maneuver[^1].Y}) wait={enemy.ManeuverWaitTicks}";
            return $"inAssault={enemy.InAssaultArea} staged={enemy.HasStagingPosition} attackDestination={enemy.AttackDestination} "
                + $"routeProgress={enemy.RouteDistance}/{enemy.ApproachLimit} maneuver={reservation}";
        }

        internal void FixtureArrangeFormation(params (int DefinitionId, Position2 Position, bool Attacking)[] placements)
        {
            var route = Configuration.Routes.Single();
            var track = routes[route.Id];
            batchSpawned = true;
            foreach (var placement in placements)
            {
                var definition = enemyDefinitions[placement.DefinitionId];
                int radius = EnemyNavigationRadiusMillimetres(definition.Id);
                enemies.Add(new Enemy
                {
                    Id = nextEnemyId++,
                    Definition = definition,
                    Route = route,
                    BatchIndex = 0,
                    Health = definition.Health,
                    Position = placement.Position,
                    PreviousPosition = placement.Position,
                    Radius = radius,
                    ApproachLimit = ApproachLimit(track, radius),
                    InAssaultArea = true,
                    AtAttackPosition = placement.Attacking,
                    AttackDestination = placement.Attacking,
                    HasStagingPosition = !placement.Attacking
                });
            }

            RefreshSnapshots();
        }

        internal int? FixtureRemoveAttacker(Position2 position)
        {
            var target = enemies.Find(enemy => enemy.AtAttackPosition && enemy.Position.Equals(position));
            if (target == null)
            {
                return null;
            }

            // Select a particular front-line casualty and apply the same bookkeeping as a lethal tower shot.
            Gold = SaturatingAdd(Gold, target.Definition.GoldReward);
            Kills++;
            enemies.Remove(target);
            NavigationEnemyRemoved(target);
            RewardResolvedEnemy(target);
            RefreshSnapshots();
            return target.Radius;
        }

        internal bool FixtureConstructionBlocksReservedPath(Position2 position)
        {
            var foundation = new NavigationObstacle(position, position, TowerFoundationRadiusMillimetres);
            foreach (var enemy in maneuvers)
            {
                var from = enemy.Position;
                for (int segment = enemy.ManeuverIndex; segment < enemy.Maneuver.Length; segment++)
                {
                    var to = enemy.Maneuver[segment];
                    if (foundation.Blocks(from, to, enemy.Radius))
                    {
                        return true;
                    }

                    from = to;
                }
            }

            return false;
        }
    }
}
