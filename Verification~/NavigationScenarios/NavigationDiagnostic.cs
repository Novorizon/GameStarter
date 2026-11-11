using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    internal enum NavigationPhase
    {
        Road,
        Gate,
        Approach,
        Staging,
        Staged,
        Attacking
    }

    internal readonly record struct NavigationSample(
        int Id, int Type, int RouteId, int Radius, int Speed, Position2 Position, int Distance, int Segment, NavigationPhase Phase,
        Position2 Goal, bool Hit);

    internal readonly record struct PendingEntry(Position2 Position, int Radius, int Due);

    internal readonly record struct AttackCapacity(
        int Id, int Radius, Position2 Position, bool Staged, int Static, Position2[] Free, int Reachable,
        int ReachableWithoutOtherStaged, int ReachableWithAttackersOnly);

    internal readonly record struct ParkingCapacity(int Radius, int MinimumGap, Position2[] Static, Position2[] Live, Position2[] AttackersOnly);

    internal readonly record struct IntakeCapacity(int Id, int Radius, int LiveSites, int SitesWithClearance);

    public sealed partial class FightSession
    {
        // This partial is compiled only into the isolated verification executable.
        internal void RemoveFixtureEnemy(int id)
        {
            var enemy = enemies.Single(item => item.Id == id);
            enemies.Remove(enemy);
        }

        internal IEnumerable<NavigationSample> ReadNavigationSamples()
        {
            foreach (var enemy in enemies)
            {
                var order = enemy.AssaultOrder;
                bool attacking = ReachedObjective(enemy) && Configuration.Objective.Type == BattleObjectiveType.DefendBase;
                var phase = attacking ? NavigationPhase.Attacking : NavigationPhase.Road;
                var goal = enemy.Route.Definition.Points[Math.Min(enemy.RoadSegmentIndex + 1, enemy.Route.Definition.Points.Length - 1)];
                if (order != null)
                {
                    goal = order.Points.Length > 0 ? order.Points[^1] : enemy.Position;
                    if (order.Staging)
                    {
                        phase = order.Arrived ? NavigationPhase.Staged : NavigationPhase.Staging;
                    }
                    else if (!attacking)
                    {
                        phase = NavigationPhase.Approach;
                    }
                }
                else if (!attacking && enemy.BaseAttack != null)
                {
                    goal = enemy.BaseAttack.Destination;
                    phase = NavigationPhase.Approach;
                }
                else if (!attacking && enemy.Distance >= enemy.Route.Length)
                {
                    phase = NavigationPhase.Gate;
                }

                bool hit = attacking && enemy.NextBaseAttackTick == Tick + Configuration.Objective.BaseAttackIntervalTicks;
                yield return new NavigationSample(enemy.Id, enemy.Definition.Id, enemy.Route.Definition.Id, enemy.Definition.BodyRadius,
                    enemy.Definition.SpeedPerTick, enemy.Position, enemy.Distance, enemy.RoadSegmentIndex, phase, goal, hit);
            }
        }

        internal IEnumerable<PendingEntry> ReadPendingEntries()
        {
            if (batchSpawned || WaveIndex == 0)
            {
                yield break;
            }

            var groups = Configuration.Waves[WaveIndex - 1].Groups;
            for (int i = 0; i < groups.Length; i++)
            {
                var group = groups[i];
                int age = waveTick - group.StartTick;
                int due = age < 0 ? 0 : Math.Min(group.Count, 1 + age / group.IntervalTicks) - emitted[i];
                if (due > 0)
                {
                    yield return new PendingEntry(routes[group.RouteId].Definition.Points[0], enemyDefinitions[group.EnemyId].BodyRadius, due);
                }
            }
        }

        internal AttackCapacity[] MeasureAttackCapacity()
        {
            var layout = Configuration.Objective.BaseAttack;
            if (layout?.LocalArea == null)
            {
                return Array.Empty<AttackCapacity>();
            }

            var original = enemies.ToArray();
            var result = new List<AttackCapacity>();
            foreach (var mover in original)
            {
                var order = mover.AssaultOrder;
                if (mover.Distance != mover.Route.Length || (order != null && (!order.Staging || !order.Arrived)))
                {
                    continue;
                }

                int radius = mover.Definition.BodyRadius;
                if (!assaultFields.TryGetValue(radius, out var field))
                {
                    continue;
                }

                var nearby = original.Where(other => other != mover && IsNearAssaultArea(other, layout.LocalArea)).ToArray();
                var withoutStaged = nearby.Where(other => other.AssaultOrder is not { Staging: true, Arrived: true }).ToArray();
                var attackers = nearby.Where(ReachedObjective).ToArray();
                var free = new List<Position2>();
                int geometry = Count(null, point =>
                {
                    if (IsClearOfAssaultTraffic(point, point, radius, nearby) && CanHoldAssaultPosition(mover, point))
                    {
                        free.Add(point);
                    }
                });
                int reachable = Count((from, to) => IsClearOfAssaultTraffic(from, to, radius, nearby));
                int reachableWithoutStaged = Count((from, to) => IsClearOfAssaultTraffic(from, to, radius, withoutStaged));
                int reachableWithAttackersOnly = Count((from, to) => IsClearOfAssaultTraffic(from, to, radius, attackers));
                result.Add(new AttackCapacity(mover.Id, radius, mover.Position, order?.Staging == true, geometry, free.ToArray(), reachable,
                    reachableWithoutStaged, reachableWithAttackersOnly));

                int Count(Func<Position2, Position2, bool> available, Action<Position2> visit = null)
                {
                    int cells = 0;
                    field.Grid.FindPath(mover.Position, available, (point, _, ingress) =>
                    {
                        if (ingress < int.MaxValue && CanStrikeFrom(point, mover, layout))
                        {
                            cells++;
                            visit?.Invoke(point);
                        }

                        return int.MinValue;
                    }, field.Ingress);
                    return cells;
                }
            }

            return result.ToArray();
        }

        internal IEnumerable<IntakeCapacity> MeasureIntakeCapacity()
        {
            var layout = Configuration.Objective.BaseAttack;
            if (layout?.LocalArea == null)
            {
                yield break;
            }

            foreach (var mover in enemies.Where(unit => unit.AssaultOrder == null && CanAdmitFromFinalRoad(unit, layout)))
            {
                int radius = mover.Definition.BodyRadius;
                if (!assaultFields.TryGetValue(radius, out var field))
                {
                    continue;
                }

                var traffic = enemies.Where(unit => unit != mover && IsNearAssaultArea(unit, layout.LocalArea)).ToArray();
                var withoutParked = traffic.Where(unit => unit.AssaultOrder is not { Arrived: true, Staging: true }).ToArray();
                yield return new IntakeCapacity(mover.Id, radius, Count(traffic), Count(withoutParked));

                int Count(Enemy[] bodies)
                {
                    int sites = 0;
                    field.Grid.FindPath(mover.Position,
                        (from, to) => IsClearOfTowers(from, to, radius) && IsClearOfAssaultTraffic(from, to, radius, bodies),
                        (point, _, ingress) =>
                        {
                            if (ingress != int.MaxValue && IsWaitingGround(point, radius, layout)
                                && CanHoldAssaultPosition(mover, point) && IsClearOfEnemyBodies(point, point, mover, radius))
                            {
                                sites++;
                            }

                            return int.MinValue;
                        }, field.Ingress);
                    return sites;
                }
            }
        }

        internal IEnumerable<ParkingCapacity> MeasureParkingCapacity()
        {
            var layout = Configuration.Objective.BaseAttack;
            var area = layout.LocalArea;
            var baseBounds = layout.Engagement;
            var route = routes.Values.OrderBy(item => item.Definition.Id).First();
            var ingress = route.Position(route.Length);
            var others = enemies.Where(other => !(other.Route == route && other.Position.Equals(ingress) && other.AssaultOrder == null)
                && IsNearAssaultArea(other, area)).ToArray();
            var attackers = others.Where(ReachedObjective).ToArray();
            var radii = Configuration.Waves.SelectMany(wave => wave.Groups).Select(group => enemyDefinitions[group.EnemyId].BodyRadius)
                .Distinct().OrderBy(radius => radius);
            foreach (int radius in radii)
            {
                var grid = new AssaultNavigationGrid(area.Min, area.Max, AssaultGridStep, (from, to) =>
                    IsClearOfLocalApproachObstacles(from, to, radius + 1, area) && FitsLocalApproachArea(from, radius + 1, area)
                    && Configuration.Terrain?.CanMove(from, to, radius + 1) != false && !CrossesBaseFootprint(from, to, baseBounds, radius + 1), AssaultNavigationGrid.ForwardFromIngress(ingress, baseBounds.Min, baseBounds.Max));
                var field = grid.BuildDistanceField(new[] { ingress });
                foreach (int minimum in new[] { baseBounds.Reach + radius + 2 * largestAssaultBodyRadius, baseBounds.Reach + radius + 100 })
                {
                    yield return new ParkingCapacity(radius, minimum, Measure(null), Measure(others), Measure(attackers));

                    Position2[] Measure(Enemy[] nearby)
                    {
                        var positions = new List<Position2>();
                        grid.FindPath(ingress, Available, (point, _, _) =>
                        {
                            long gap = baseBounds.DistanceToFootprintSquared(point);
                            if (gap >= (long)minimum * minimum && gap <= 3000L * 3000 && !IntersectsAuthoredRoad(point, radius, includeApproach: true)
                                && (nearby == null || !nearby.Any(other => IsReservedAssaultSpace(point, other, radius + other.Definition.BodyRadius + 2))))
                            {
                                positions.Add(point);
                            }

                            return int.MinValue;
                        }, field);
                        return positions.ToArray();

                        bool Available(Position2 from, Position2 to) => IsClearOfTowers(from, to, radius)
                            && (nearby == null || IsClearOfAssaultTraffic(from, to, radius, nearby));
                    }
                }
            }
        }
    }
}
