using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private IReadOnlyList<MultiIngressPlacementPolicy.Probe> ForecastSiegeAccess(Enemy mover, BaseAttackLayout layout)
        {
            var area = layout.LocalArea;
            var standing = enemies.Where(unit => unit != mover && unit.AssaultOrder != null && IsNearAssaultArea(unit, area)).ToArray();
            var probes = new List<MultiIngressPlacementPolicy.Probe>();
            foreach (int radius in enemyDefinitions.Values.Select(type => type.BodyRadius).Distinct().OrderBy(value => value))
            {
                var field = GetAssaultField(radius, layout);
                foreach (var route in routes.Values.OrderBy(item => item.Definition.Id))
                {
                    Position2 entrance = route.Position(route.Length);
                    bool inUse = HasWaitingOrPendingTraffic(route, radius, mover);
                    probes.Add(new MultiIngressPlacementPolicy.Probe(field.Grid, entrance, field.Ingress, radius, inUse,
                        (from, to) => IsClearOfTowers(from, to, radius) && IsClearOfAssaultTraffic(from, to, radius, standing),
                        point => IsWaitingGround(point, radius, layout)));
                }
            }
            return probes;
        }

        private bool HasWaitingOrPendingTraffic(Route route, int radius, Enemy mover)
        {
            if (enemies.Any(unit => unit != mover && unit.Route == route && unit.AssaultOrder == null && unit.Definition.BodyRadius == radius))
            {
                return true;
            }

            int current = WaveIndex - 1;
            for (int index = Math.Max(0, current); index < Configuration.Waves.Length; index++)
            {
                var groups = Configuration.Waves[index].Groups;
                for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
                {
                    var group = groups[groupIndex];
                    if (group.RouteId == route.Definition.Id && enemyDefinitions[group.EnemyId].BodyRadius == radius
                        && (index != current || groupIndex >= emitted.Length || emitted[groupIndex] < group.Count))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}

