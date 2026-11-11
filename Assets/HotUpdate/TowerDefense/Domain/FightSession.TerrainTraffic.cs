using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private bool CanSpawnOnTerrain(Position2 position, int radius)
        {
            return IsClearBattleGround(position, position, radius) && IsClearOfEnemyBodies(position, position, null, radius);
        }

        private bool CanSweepAuthoredRoad(Enemy enemy, Position2 to)
        {
            var from = enemy.Position;
            int radius = enemy.Definition.BodyRadius;
            if (!IsClearBattleGround(from, to, radius) || !IsClearOfEnemyBodies(from, to, enemy, radius))
            {
                return false;
            }

            foreach (var other in enemies)
            {
                if (other == enemy)
                {
                    continue;
                }

                int clearance = radius + other.Definition.BodyRadius;
                if (IntersectsAssaultReservation(from, to, other, clearance))
                {
                    return false;
                }

            }

            return true;
        }

        private bool IsClearBattleGround(Position2 from, Position2 to, int radius)
        {
            var engagement = Configuration.Objective.BaseAttack?.Engagement;
            return Configuration.Terrain?.CanMove(from, to, radius) != false
                && (engagement == null || !CrossesBaseFootprint(from, to, engagement, radius))
                && IsClearOfTowers(from, to, radius);
        }

        private bool IsClearOfEnemyBodies(Position2 from, Position2 to, Enemy mover, int radius)
        {
            foreach (var other in enemies)
            {
                int clearance = radius + other.Definition.BodyRadius;
                if (other != mover && SegmentDistanceSquared(other.Position, from, to) < (long)clearance * clearance)
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanBuildAmongTerrainTraffic(Position2 position)
        {
            var layout = Configuration.Objective.BaseAttack;
            return !IntersectsAuthoredRoad(position, TowerFoundationRadiusMillimetres, includeApproach: layout?.LocalArea != null)
                && (layout?.Engagement == null || !CrossesBaseFootprint(position, position, layout.Engagement, TowerFoundationRadiusMillimetres))
                && CanBuildNearBaseApproach(position);
        }

        private bool IntersectsAuthoredRoad(Position2 position, int radius, bool includeApproach)
        {
            foreach (var route in routes.Values)
            {
                int clearance = radius + route.LargestBodyRadius;
                int stop = includeApproach ? route.Length + route.EndInset : route.Length;
                int progress = 0;
                for (int i = 0; i < route.SegmentLengths.Length && progress < stop; i++)
                {
                    int segmentEnd = progress + route.SegmentLengths[i];
                    int end = Math.Min(stop, segmentEnd);
                    var point = end == segmentEnd ? route.Definition.Points[i + 1] : route.Position(end);
                    if (SegmentDistanceSquared(position, route.Definition.Points[i], point) < (long)clearance * clearance)
                    {
                        return true;
                    }

                    progress = end;
                }
            }

            return false;
        }

        private bool CanBuildNearBaseApproach(Position2 position)
        {
            foreach (var enemy in enemies)
            {
                int clearance = TowerFoundationRadiusMillimetres + enemy.Definition.BodyRadius;
                if (DistanceSquared(position, enemy.Position) < (long)clearance * clearance
                    || IsReservedAssaultSpace(position, enemy, clearance))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
