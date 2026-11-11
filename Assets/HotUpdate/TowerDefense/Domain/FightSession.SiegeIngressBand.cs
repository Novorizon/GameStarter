using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private bool CanAdmitFromFinalRoad(Enemy enemy, BaseAttackLayout layout)
        {
            if (enemy.Distance == enemy.Route.Length)
            {
                return true;
            }

            if (enemy.RoadSegmentIndex != enemy.Route.SegmentLengths.Length - 1)
            {
                return false;
            }

            // Past the last mandatory turn, use the final stretch as an intake zone.
            // Stop on the route until a verified side path can take over.
            int remaining = enemy.Route.Length - enemy.Distance;
            return remaining <= layout.ApproachInset
                && FitsLocalApproachArea(enemy.Position, enemy.Definition.BodyRadius + 1, layout.LocalArea);
        }
    }
}
