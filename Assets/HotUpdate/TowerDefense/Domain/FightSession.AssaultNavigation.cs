using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int AssaultGridStep = 200;
        private const int AssaultPlanningInterval = 5;
        private const int AssaultReservationPadding = 2;

        private sealed class AssaultMotion
        {
            internal readonly Position2[] Points;
            internal readonly bool Staging;
            internal Position2 SegmentStart;
            internal int SegmentProgress;
            internal int NextPoint;

            internal AssaultMotion(Position2 start, Position2[] points, bool staging)
            {
                SegmentStart = start;
                Points = points;
                Staging = staging;
            }

            internal bool Arrived => NextPoint == Points.Length;
        }

        private readonly Dictionary<int, (AssaultNavigationGrid Grid, int[] Ingress)> assaultFields = new();
        private Position2[] assaultEntrances;

        private Position2[] AssaultEntrances => assaultEntrances ??= routes.Values.OrderBy(route => route.Definition.Id)
            .Select(route => route.Position(route.Length)).ToArray();

        private void ScheduleAssaultPositions(BaseAttackLayout layout)
        {
            if (Tick % AssaultPlanningInterval != 0)
            {
                return;
            }

            if (AdvanceReflowPlan())
            {
                return;
            }

            bool frontOccupied = enemies.Any(enemy => enemy.AssaultOrder is { Staging: false });
            foreach (var enemy in movementOrder)
            {
                if (!CanAdmitFromFinalRoad(enemy, layout) || (enemy.AssaultOrder != null && (!enemy.AssaultOrder.Arrived || !enemy.AssaultOrder.Staging)))
                {
                    continue;
                }

                if (CanReserveAttackPosition(enemy, enemy.Position, layout))
                {
                    enemy.Distance = enemy.Route.Length;
                    enemy.AssaultOrder = new AssaultMotion(enemy.Position, Array.Empty<Position2>(), false);
                    frontOccupied = true;
                    continue;
                }

                var path = FindAssaultPath(enemy, layout, false, frontOccupied);
                if (path.Length > 0)
                {
                    enemy.Distance = enemy.Route.Length;
                    enemy.AssaultOrder = new AssaultMotion(enemy.Position, path, false);
                    frontOccupied = true;
                }
            }

            // Let the current arrivals clear the flanks before choosing another waiting position.
            if (enemies.Any(enemy => enemy.AssaultOrder is { Arrived: false }))
            {
                return;
            }

            foreach (var enemy in movementOrder)
            {
                if (!CanAdmitFromFinalRoad(enemy, layout) || enemy.AssaultOrder != null)
                {
                    continue;
                }

                var path = FindAssaultPath(enemy, layout, true, false);
                if (path.Length > 0)
                {
                    enemy.Distance = enemy.Route.Length;
                    enemy.AssaultOrder = new AssaultMotion(enemy.Position, path, true);
                    break;
                }
                if (TryReflowStaging(enemy, layout))
                {
                    break;
                }
            }
        }

        private Position2[] FindAssaultPath(Enemy enemy, BaseAttackLayout layout, bool staging, bool frontOccupied)
        {
            int radius = enemy.Definition.BodyRadius;
            var area = layout.LocalArea;
            var field = GetAssaultField(radius, layout);
            var near = enemies.Where(other => other != enemy && IsNearAssaultArea(other, area)).ToArray();
            if (staging)
            {
                var entrances = AssaultEntrances;
                return entrances.Length > 1
                    ? MultiIngressPlacementPolicy.Plan(field.Grid, enemy.Position, entrances, field.Ingress, radius, Available, Waiting,
                        ForecastSiegeAccess(enemy, layout), enemy.Route.Position(enemy.Route.Length))
                    : SiegePlacementPolicy.Plan(field.Grid, enemy.Position, entrances, field.Ingress, radius, Available, Waiting,
                        point => CanReserveAttackPosition(enemy, point, layout));

                bool Waiting(Position2 point) => IsWaitingGround(point, radius, layout) && CanHoldAssaultPosition(enemy, point);
            }

            // First contact takes a near attack position. Later arrivals fill the reachable flanks.
            return field.Grid.FindPath(enemy.Position, Available, (point, travel, ingress) =>
                ingress == int.MaxValue || !CanReserveAttackPosition(enemy, point, layout)
                    ? int.MinValue : frontOccupied ? ingress : -travel, field.Ingress);

            bool Available(Position2 from, Position2 to)
                => IsClearOfTowers(from, to, radius) && IsClearOfAssaultTraffic(from, to, radius, near);
        }

        private (AssaultNavigationGrid Grid, int[] Ingress) GetAssaultField(int radius, BaseAttackLayout layout)
        {
            if (assaultFields.TryGetValue(radius, out var field))
            {
                return field;
            }

            var area = layout.LocalArea;
            var engagement = layout.Engagement;
            var entrances = AssaultEntrances;
            var gridForward = AssaultNavigationGrid.ForwardFromIngress(entrances[0], engagement.Min, engagement.Max);
            var grid = new AssaultNavigationGrid(area.Min, area.Max, AssaultGridStep, (from, to) =>
                IsClearOfLocalApproachObstacles(from, to, radius + 1, area) && FitsLocalApproachArea(from, radius + 1, area)
                && Configuration.Terrain?.CanMove(from, to, radius + 1) != false && !CrossesBaseFootprint(from, to, engagement, radius + 1), gridForward);
            field = (grid, grid.BuildDistanceField(entrances));
            assaultFields.Add(radius, field);
            return field;
        }

        private bool IsNearAssaultArea(Enemy enemy, BaseApproachArea area)
        {
            int clearance = enemy.Definition.BodyRadius + largestAssaultBodyRadius + AssaultReservationPadding;
            return enemy.Position.X >= area.Min.X - clearance && enemy.Position.X <= area.Max.X + clearance
                && enemy.Position.Y >= area.Min.Y - clearance && enemy.Position.Y <= area.Max.Y + clearance;
        }

        private bool CanReserveAttackPosition(Enemy enemy, Position2 point, BaseAttackLayout layout)
        {
            return CanStrikeFrom(point, enemy, layout) && CanHoldAssaultPosition(enemy, point)
                && !IntersectsAuthoredRoad(point, enemy.Definition.BodyRadius + AssaultReservationPadding, includeApproach: false);
        }

        private bool CanStrikeFrom(Position2 point, Enemy enemy, BaseAttackLayout layout)
        {
            var bounds = layout.Engagement;
            if (!bounds.CanAttackFrom(point, enemy.Definition.BodyRadius))
            {
                return false;
            }

            var face = new Position2(Math.Clamp(point.X, bounds.Min.X, bounds.Max.X), Math.Clamp(point.Y, bounds.Min.Y, bounds.Max.Y));
            return Configuration.Terrain?.CanMove(point, face, 0) != false && IsClearOfTowers(point, face, 0)
                && IsClearOfLocalApproachObstacles(point, face, 0, layout.LocalArea);
        }

        private void AdvanceAssaultMotion(Enemy enemy, AssaultMotion motion, BaseAttackLayout layout)
        {
            int budget = enemy.Definition.SpeedPerTick;
            while (budget > 0 && !motion.Arrived)
            {
                var start = motion.SegmentStart;
                var end = motion.Points[motion.NextPoint];
                int length = (int)Math.Ceiling(Math.Sqrt(DistanceSquared(start, end)));
                int step = Math.Min(budget, length - motion.SegmentProgress);
                int progress = motion.SegmentProgress + step;
                var point = progress == length ? end : new Position2(start.X + (int)((long)(end.X - start.X) * progress / length),
                    start.Y + (int)((long)(end.Y - start.Y) * progress / length));
                var from = enemy.Position;
                int radius = enemy.Definition.BodyRadius;
                if (!IsClearBattleGround(from, point, radius) || !IsClearOfEnemyBodies(from, point, enemy, radius)
                    || !IsClearOfLocalApproachObstacles(from, point, radius, layout.LocalArea))
                {
                    enemy.AssaultOrder = null;
                    return;
                }

                enemy.Position = point;
                motion.SegmentProgress = progress;
                budget -= step;
                if (progress == length)
                {
                    motion.NextPoint++;
                    motion.SegmentStart = end;
                    motion.SegmentProgress = 0;
                }
            }
        }

        private static bool IsClearOfAssaultTraffic(Position2 from, Position2 to, int radius, IReadOnlyList<Enemy> nearby)
        {
            foreach (var other in nearby)
            {
                int bodyClearance = radius + other.Definition.BodyRadius;
                int clearance = bodyClearance + AssaultReservationPadding;
                double distance = SegmentDistanceSquared(other.Position, from, to);
                // A road follower can arrive already touching. An escape step can hold or increase that initial clearance.
                long startDistance = DistanceSquared(from, other.Position);
                if ((distance < (long)clearance * clearance && distance < startDistance)
                    || distance < (long)bodyClearance * bodyClearance
                    || IntersectsAssaultReservation(from, to, other, clearance))
                {
                    return false;
                }
            }

            return true;
        }

        private bool CanHoldAssaultPosition(Enemy enemy, Position2 point)
        {
            foreach (var other in enemies)
            {
                int clearance = enemy.Definition.BodyRadius + other.Definition.BodyRadius + AssaultReservationPadding;
                if (other != enemy && IsReservedAssaultSpace(point, other, clearance))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsReservedAssaultSpace(Position2 point, Enemy enemy, int clearance)
        {
            var motion = enemy.AssaultOrder;
            if (motion == null || motion.Arrived)
            {
                return false;
            }

            var start = enemy.Position;
            long required = (long)clearance * clearance;
            for (int i = motion.NextPoint; i < motion.Points.Length; i++)
            {
                var end = motion.Points[i];
                if (SegmentDistanceSquared(point, start, end) < required)
                {
                    return true;
                }

                start = end;
            }

            return false;
        }

        private static bool IntersectsAssaultReservation(Position2 from, Position2 to, Enemy other, int clearance)
        {
            var motion = other.AssaultOrder;
            if (motion == null || motion.Arrived)
            {
                return false;
            }

            var start = other.Position;
            long required = (long)clearance * clearance;
            for (int i = motion.NextPoint; i < motion.Points.Length; i++)
            {
                var end = motion.Points[i];
                // A shared start clearance permits a step away, but never cutting across a reserved path.
                double currentGap = SegmentDistanceSquared(from, start, end);
                if (SegmentDistanceSquared(to, start, end) < Math.Min(required, currentGap)
                    || SegmentDistanceSquared(start, from, to) < Math.Min(required, DistanceSquared(from, start))
                    || SegmentDistanceSquared(end, from, to) < Math.Min(required, DistanceSquared(from, end))
                    || CrossesReservationInterior(from, to, start, end))
                {
                    return true;
                }

                start = end;
            }

            return false;
        }

        private static bool CrossesReservationInterior(Position2 a, Position2 b, Position2 c, Position2 d)
        {
            long first = Cross(a, b, c);
            long second = Cross(a, b, d);
            long third = Cross(c, d, a);
            long fourth = Cross(c, d, b);
            return ((first < 0 && second > 0) || (first > 0 && second < 0)) && ((third < 0 && fourth > 0) || (third > 0 && fourth < 0));
        }

        private static long Cross(Position2 a, Position2 b, Position2 p)
        {
            return ((long)b.X - a.X) * ((long)p.Y - a.Y) - ((long)b.Y - a.Y) * ((long)p.X - a.X);
        }
    }
}


