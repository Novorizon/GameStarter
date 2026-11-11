using System.Collections.Generic;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int CompactStagingClearance = 350;
        private const int CompactTransferDistance = 1350;
        private const long CompactTransferDistanceSquared = (long)CompactTransferDistance * CompactTransferDistance;

        private readonly List<(Enemy Reserve, Enemy Attacker, Position2 From)> compactTransfers = new List<(Enemy, Enemy, Position2)>();
        private readonly List<Enemy> attackersWithoutReserve = new List<Enemy>();

        private void PrepareCompactStaging(Enemy moving)
        {
            compactTransfers.Clear();
            attackersWithoutReserve.Clear();
            foreach (var attacker in enemies)
            {
                if (!attacker.AtAttackPosition)
                {
                    continue;
                }

                Enemy reserve = null;
                Position2 from = default;
                long shortest = CompactTransferDistanceSquared + 1;
                foreach (var candidate in enemies)
                {
                    if (candidate == moving || candidate.AttackDestination || (!candidate.HasStagingPosition && candidate.Maneuver == null)
                        || compactTransfers.Exists(transfer => transfer.Reserve == candidate))
                    {
                        continue;
                    }

                    var point = ReservedPosition(candidate);
                    long distance = DistanceSquared(point, attacker.Position);
                    if (distance < shortest && CanTransferToAttackPosition(candidate, navigationGrids[candidate.Radius], point, attacker, moving))
                    {
                        reserve = candidate;
                        from = point;
                        shortest = distance;
                    }
                }

                if (reserve != null)
                {
                    compactTransfers.Add((reserve, attacker, from));
                }
                else
                {
                    attackersWithoutReserve.Add(attacker);
                }
            }
        }

        private bool CanUseCompactStagingPosition(Enemy mover, AssaultNavigationGrid grid, Position2 point, long baseDistance)
        {
            int minimum = CompactStagingClearance + mover.Radius;
            int maximum = StagingClearance + mover.Radius;
            if (baseDistance < (long)minimum * minimum || baseDistance >= (long)maximum * maximum
                || BlocksAny(protectedApproaches, point, point, mover.Radius))
            {
                return false;
            }

            // Keep every existing reserve's direct route to its attacker clear.
            var reserved = new NavigationObstacle(point, point, mover.Radius);
            foreach (var transfer in compactTransfers)
            {
                if (reserved.Blocks(transfer.From, transfer.Attacker.Position, transfer.Reserve.Radius))
                {
                    return false;
                }
            }

            foreach (var attacker in attackersWithoutReserve)
            {
                if (DistanceSquared(point, attacker.Position) <= CompactTransferDistanceSquared
                    && CanTransferToAttackPosition(mover, grid, point, attacker, mover))
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanTransferToAttackPosition(Enemy reserve, AssaultNavigationGrid grid, Position2 from, Enemy attacker, Enemy moving)
        {
            var to = attacker.Position;
            if (!grid.IsAttackPosition(to, BaseMeleeReachMillimetres - GridClearanceMargin) || !grid.CanSweep(from, to))
            {
                return false;
            }

            foreach (var other in enemies)
            {
                if (other != reserve && other != moving && other != attacker)
                {
                    var point = ReservedPosition(other);
                    if (new NavigationObstacle(point, point, other.Radius).Blocks(from, to, reserve.Radius))
                    {
                        return false;
                    }
                }
            }

            foreach (var tower in towers)
            {
                var point = tower.Slot.Position;
                if (new NavigationObstacle(point, point, TowerFoundationRadiusMillimetres).Blocks(from, to, reserve.Radius))
                {
                    return false;
                }
            }

            return true;
        }

        private static Position2 ReservedPosition(Enemy enemy) => enemy.Maneuver != null ? enemy.Maneuver[^1] : enemy.Position;
    }
}
