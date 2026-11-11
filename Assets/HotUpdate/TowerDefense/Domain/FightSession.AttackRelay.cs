using System;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int AttackRelayDistance = 1350;
        private const long AttackRelayDistanceSquared = (long)AttackRelayDistance * AttackRelayDistance;
        private (Enemy Attacker, Position2 Vacancy)? pendingAttackRelay;

        private bool TryRelayAttackVacancy(Position2 vacancy)
        {
            foreach (var attacker in enemies)
            {
                if (!CanSlideAttacker(attacker, vacancy))
                {
                    continue;
                }

                var replacement = FindAttackRelayReplacement(attacker, vacancy);
                if (replacement != null && TryFinishAttackRelay(attacker, vacancy, replacement))
                {
                    return true;
                }
            }

            // A recently replaced attacker can leave its neighboring vacancy reachable only from the next front position.
            foreach (var first in enemies)
            {
                if (!CanSlideAttacker(first, vacancy))
                {
                    continue;
                }

                var nextVacancy = first.Position;
                foreach (var second in enemies)
                {
                    if (CanSlideAttacker(second, nextVacancy, first, vacancy)
                        && FindAttackRelayReplacement(second, nextVacancy, first, vacancy) != null
                        && StartAttackRelaySlide(first, vacancy))
                    {
                        pendingAttackRelay = (second, nextVacancy);
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryContinueAttackRelay()
        {
            if (pendingAttackRelay is not { } relay)
            {
                return false;
            }

            pendingAttackRelay = null;
            var first = yieldingUnit;
            yieldingUnit = null;
            if (first?.AtAttackPosition != true || !enemies.Contains(relay.Attacker) || !CanSlideAttacker(relay.Attacker, relay.Vacancy))
            {
                return false;
            }

            var replacement = FindAttackRelayReplacement(relay.Attacker, relay.Vacancy);
            return replacement != null && TryFinishAttackRelay(relay.Attacker, relay.Vacancy, replacement);
        }

        private bool TryFinishAttackRelay(Enemy attacker, Position2 vacancy, Enemy replacement)
        {
            var start = attacker.Position;
            if (!StartAttackRelaySlide(attacker, vacancy))
            {
                return false;
            }

            yieldingFor = replacement;
            yieldingAttackPath = new[] { start };
            return true;
        }

        private bool StartAttackRelaySlide(Enemy attacker, Position2 vacancy)
        {
            var start = attacker.Position;
            if (!StartManeuver(attacker, new[] { vacancy }, true))
            {
                return false;
            }

            // Keep the old position eligible for ordinary promotion if the reserved replacement is interrupted.
            RecordAttackVacancy(attacker);
            attacker.AtAttackPosition = false;
            long distance = SegmentLength(start, vacancy);
            int speed = attacker.Definition.SpeedPerTick;
            long arrival = Tick + attacker.ManeuverWaitTicks + (distance + speed - 1) / speed;
            attacker.NextBaseAttackTick = Math.Max(attacker.NextBaseAttackTick,
                arrival + Math.Max(1, Configuration.Objective.BaseAttackWindupTicks));
            attackLayoutVersion++;
            yieldingUnit = attacker;
            return true;
        }

        private bool CanSlideAttacker(Enemy attacker, Position2 vacancy, Enemy earlier = null, Position2 earlierDestination = default)
        {
            if (!attacker.AtAttackPosition || attacker == earlier || DistanceSquared(attacker.Position, vacancy) > AttackRelayDistanceSquared)
            {
                return false;
            }

            CollectPathObstacles(attacker, earlier);
            AddProjectedAttacker(earlier, earlierDestination);
            return CanSweepToAttackPosition(attacker, vacancy);
        }

        private Enemy FindAttackRelayReplacement(Enemy attacker, Position2 vacancy, Enemy earlier = null, Position2 earlierDestination = default)
        {
            var target = attacker.Position;
            var relocatedAttacker = new NavigationObstacle(vacancy, vacancy, attacker.Radius);
            Enemy replacement = null;
            long shortest = AttackRelayDistanceSquared + 1;
            foreach (var reserve in enemies)
            {
                long distance = DistanceSquared(reserve.Position, target);
                if (!reserve.HasStagingPosition || distance >= shortest || relocatedAttacker.Blocks(reserve.Position, target, reserve.Radius))
                {
                    continue;
                }

                CollectPathObstacles(reserve, attacker, earlier);
                AddProjectedAttacker(earlier, earlierDestination);
                if (CanSweepToAttackPosition(reserve, target))
                {
                    replacement = reserve;
                    shortest = distance;
                }
            }

            return replacement;
        }

        private void AddProjectedAttacker(Enemy earlier, Position2 destination)
        {
            if (earlier != null)
            {
                pathObstacles.Add(new NavigationObstacle(destination, destination, earlier.Radius));
            }
        }

        private bool CanSweepToAttackPosition(Enemy enemy, Position2 destination)
        {
            var grid = navigationGrids[enemy.Radius];
            return grid.IsAttackPosition(destination, BaseMeleeReachMillimetres - GridClearanceMargin)
                && grid.CanSweep(enemy.Position, destination) && !BlocksAny(pathObstacles, enemy.Position, destination, enemy.Radius);
        }
    }
}
