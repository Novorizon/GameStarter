using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int ReflowRetryTicks = 40;
        private const int ReflowMaxBlockers = 3;
        private const int ReflowMaxGateTargets = 6;
        private const int ReflowMaxRelocationSearches = 12;
        private const int ReflowMaxRelocation = 2400;
        private const int ReflowTargetSpacing = 400;
        private static readonly int[] ReflowNudgeDistances = { 10, 20, 40, 80, 120, 160 };

        private sealed class SiegeReflowPlan
        {
            internal readonly Enemy Gate;
            internal readonly Position2[] GatePath;
            internal readonly List<(Enemy Unit, Position2[] Path)> Relocations;
            internal int Next;
            internal AssaultMotion Moving;

            internal SiegeReflowPlan(Enemy gate, Position2[] gatePath, List<(Enemy Unit, Position2[] Path)> relocations)
            {
                Gate = gate;
                GatePath = gatePath;
                Relocations = relocations;
            }
        }

        private SiegeReflowPlan siegeReflow;
        private readonly Dictionary<int, long> nextSiegeReflowTick = new();

        private bool TryReflowStaging(Enemy gate, BaseAttackLayout layout)
        {
            int gateRadius = gate.Definition.BodyRadius;
            int routeId = gate.Route.Definition.Id;
            if (siegeReflow != null || nextSiegeReflowTick.TryGetValue(routeId, out long retry) && Tick < retry
                || !assaultFields.TryGetValue(gateRadius, out var gateField))
            {
                return false;
            }

            var nearby = enemies.Where(unit => IsNearAssaultArea(unit, layout.LocalArea)).ToArray();
            var parked = nearby.Where(IsSettledCandidate).ToArray();
            if (parked.Length == 0)
            {
                return false;
            }

            nextSiegeReflowTick[routeId] = Tick + ReflowRetryTicks;
            var targets = new List<(Position2 Point, int Travel)>();
            var fixedTraffic = nearby.Where(unit => !IsSettledCandidate(unit)).ToArray();
            var originalPath = gateField.Grid.FindPath(gate.Position, AvailableWithoutParking, (point, travel, ingress) =>
            {
                if (ingress == int.MaxValue || !IsWaitingGround(point, gateRadius, layout) || !CanSweepReflow(gate, point, point, nearby))
                {
                    return int.MinValue;
                }

                targets.Add((point, travel));
                return -travel;
            }, gateField.Ingress);
            if (originalPath.Length == 0)
            {
                return false;
            }

            int searchesRemaining = ReflowMaxRelocationSearches;
            int alternatePathsRemaining = ReflowMaxBlockers + 1;
            var examined = new List<Position2>(ReflowMaxGateTargets);
            foreach (var target in targets.OrderBy(site => site.Travel))
            {
                if (examined.Any(point => DistanceSquared(point, target.Point) < (long)ReflowTargetSpacing * ReflowTargetSpacing))
                {
                    continue;
                }

                examined.Add(target.Point);
                var gatePath = target.Point.Equals(originalPath[^1]) ? originalPath : gateField.Grid.FindPath(gate.Position, AvailableWithoutParking,
                    (point, _, _) => point.Equals(target.Point) ? 0 : int.MinValue, gateField.Ingress);
                var blockers = ReflowBlockers(gate, gatePath, parked);
                if (TryPlanRelocations(gate, gatePath, layout, nearby, blockers, ref searchesRemaining, out var relocations)
                    || TryAlternateReflowPath(gate, gateField, target.Point, layout, nearby, parked, blockers,
                        ref alternatePathsRemaining, ref searchesRemaining, out gatePath, out relocations))
                {
                    siegeReflow = new SiegeReflowPlan(gate, gatePath, relocations);
                    return AdvanceReflowPlan();
                }

                if (examined.Count == ReflowMaxGateTargets || searchesRemaining == 0)
                {
                    break;
                }
            }

            return false;

            bool AvailableWithoutParking(Position2 from, Position2 to) => CanSweepReflow(gate, from, to, fixedTraffic);
        }

        private bool TryAlternateReflowPath(
            Enemy gate,
            (AssaultNavigationGrid Grid, int[] Ingress) field,
            Position2 target,
            BaseAttackLayout layout,
            Enemy[] nearby,
            Enemy[] parked,
            List<Enemy> blockers,
            ref int alternatePathsRemaining,
            ref int searchesRemaining,
            out Position2[] path,
            out List<(Enemy Unit, Position2[] Path)> relocations)
        {
            path = Array.Empty<Position2>();
            relocations = null;
            if (blockers.Count != ReflowMaxBlockers + 1)
            {
                return false;
            }

            // Preserve one of the first path's blockers and every other parked body. The
            // original shortest witness can cross a body which has a clear route around it.
            foreach (var retained in blockers.AsEnumerable().Reverse())
            {
                if (alternatePathsRemaining == 0 || searchesRemaining == 0)
                {
                    break;
                }

                alternatePathsRemaining--;
                var fixedTraffic = nearby.Where(unit => unit == retained || !blockers.Contains(unit)).ToArray();
                path = field.Grid.FindPath(gate.Position, (from, to) => CanSweepReflow(gate, from, to, fixedTraffic),
                    (point, _, _) => point.Equals(target) ? 0 : int.MinValue, field.Ingress);
                if (path.Length != 0 && TryPlanRelocations(gate, path, layout, nearby, ReflowBlockers(gate, path, parked),
                    ref searchesRemaining, out relocations))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryPlanRelocations(
            Enemy gate,
            Position2[] gatePath,
            BaseAttackLayout layout,
            Enemy[] nearby,
            List<Enemy> blockers,
            ref int searchesRemaining,
            out List<(Enemy Unit, Position2[] Path)> relocations)
        {
            relocations = new List<(Enemy Unit, Position2[] Path)>(ReflowMaxBlockers);
            if (gatePath.Length == 0)
            {
                return false;
            }

            if (blockers.Count == 0 || blockers.Count > ReflowMaxBlockers)
            {
                return false;
            }

            blockers = new List<Enemy>(blockers);
            var planned = new Dictionary<Enemy, Position2>(blockers.Count);
            while (blockers.Count != 0)
            {
                bool found = false;
                for (int index = 0; index < blockers.Count && searchesRemaining > 0; index++)
                {
                    var blocker = blockers[index];
                    int radius = blocker.Definition.BodyRadius;
                    if (!assaultFields.TryGetValue(radius, out var field))
                    {
                        continue;
                    }

                    searchesRemaining--;
                    var detour = TryFindReflowNudge(gate, gatePath, layout, blocker, nearby, planned);
                    if (detour.Length == 0)
                    {
                        detour = field.Grid.FindPath(blocker.Position, Available, (point, travel, ingress) =>
                        {
                            if (travel > ReflowMaxRelocation || ingress == int.MaxValue || !IsWaitingGround(point, radius, layout))
                            {
                                return int.MinValue;
                            }

                            return ClearsReflowPath(gate, gatePath, point, radius) ? -travel : int.MinValue;
                        }, field.Ingress);
                    }

                    if (detour.Length == 0)
                    {
                        continue;
                    }

                    planned.Add(blocker, detour[^1]);
                    relocations.Add((blocker, detour));
                    blockers.RemoveAt(index);
                    found = true;
                    break;

                    bool Available(Position2 from, Position2 to) => DistanceSquared(blocker.Position, to) <= (long)ReflowMaxRelocation * ReflowMaxRelocation
                        && CanSweepReflow(blocker, from, to, nearby, planned);
                }

                if (!found)
                {
                    return false;
                }
            }

            return CanFollowReflowPath(gate, gatePath, nearby, planned);
        }

        private static List<Enemy> ReflowBlockers(Enemy gate, Position2[] gatePath, Enemy[] parked) => parked
            .Where(unit => !ClearsReflowPath(gate, gatePath, unit.Position, unit.Definition.BodyRadius))
            .OrderByDescending(unit => DistanceSquared(gate.Position, unit.Position)).ThenBy(unit => unit.Id).ToList();

        private Position2[] TryFindReflowNudge(
            Enemy gate,
            Position2[] gatePath,
            BaseAttackLayout layout,
            Enemy blocker,
            IReadOnlyList<Enemy> nearby,
            Dictionary<Enemy, Position2> planned)
        {
            var origin = blocker.Position;
            int radius = blocker.Definition.BodyRadius;
            var from = gate.Position;
            double closestSquared = double.MaxValue;
            double angle = 0;
            foreach (var to in gatePath)
            {
                double dx = (long)to.X - from.X;
                double dy = (long)to.Y - from.Y;
                double lengthSquared = dx * dx + dy * dy;
                if (lengthSquared != 0)
                {
                    double x = (long)origin.X - from.X;
                    double y = (long)origin.Y - from.Y;
                    double t = Math.Clamp((x * dx + y * dy) / lengthSquared, 0, 1);
                    double offsetX = x - t * dx;
                    double offsetY = y - t * dy;
                    double distance = offsetX * offsetX + offsetY * offsetY;
                    if (distance < closestSquared)
                    {
                        closestSquared = distance;
                        angle = distance > 0 ? Math.Atan2(offsetY, offsetX) : Math.Atan2(dx, -dy);
                    }
                }

                from = to;
            }

            foreach (int distance in ReflowNudgeDistances)
            {
                for (int index = 0; index < 16; index++)
                {
                    int side = (index & 1) == 0 ? -(index / 2) : (index + 1) / 2;
                    double direction = angle + side * Math.PI / 8;
                    var point = new Position2(origin.X + (int)Math.Round(distance * Math.Cos(direction)),
                        origin.Y + (int)Math.Round(distance * Math.Sin(direction)));
                    if (IsWaitingGround(point, radius, layout) && ClearsReflowPath(gate, gatePath, point, radius)
                        && IsClearOfLocalApproachObstacles(origin, point, radius + 1, layout.LocalArea)
                        && Configuration.Terrain?.CanMove(origin, point, radius + 1) != false
                        && !CrossesBaseFootprint(origin, point, layout.Engagement, radius + 1)
                        && CanSweepReflow(blocker, origin, point, nearby, planned))
                    {
                        return new[] { point };
                    }
                }
            }

            return Array.Empty<Position2>();
        }

        private bool AdvanceReflowPlan()
        {
            var plan = siegeReflow;
            if (plan == null)
            {
                return false;
            }

            if (!enemies.Contains(plan.Gate) || plan.Gate.AssaultOrder != null)
            {
                siegeReflow = null;
                return false;
            }

            if (plan.Moving != null)
            {
                var previous = plan.Relocations[plan.Next - 1].Unit;
                if (!enemies.Contains(previous) || previous.AssaultOrder != plan.Moving)
                {
                    siegeReflow = null;
                    return false;
                }

                if (!plan.Moving.Arrived)
                {
                    return true;
                }

                plan.Moving = null;
            }

            if (plan.Next < plan.Relocations.Count)
            {
                var move = plan.Relocations[plan.Next];
                if (!enemies.Contains(move.Unit) || !IsSettledCandidate(move.Unit) || !CanFollowReflowPath(move.Unit, move.Path, enemies))
                {
                    siegeReflow = null;
                    return false;
                }

                plan.Next++;
                plan.Moving = new AssaultMotion(move.Unit.Position, move.Path, true);
                move.Unit.AssaultOrder = plan.Moving;
                return true;
            }

            siegeReflow = null;
            if (!CanFollowReflowPath(plan.Gate, plan.GatePath, enemies))
            {
                return false;
            }

            plan.Gate.Distance = plan.Gate.Route.Length;
            plan.Gate.AssaultOrder = new AssaultMotion(plan.Gate.Position, plan.GatePath, true);
            return true;
        }

        private bool CanFollowReflowPath(Enemy mover, Position2[] path, IReadOnlyList<Enemy> traffic, Dictionary<Enemy, Position2> planned = null)
        {
            var from = mover.Position;
            foreach (var to in path)
            {
                if (!CanSweepReflow(mover, from, to, traffic, planned))
                {
                    return false;
                }

                from = to;
            }

            return true;
        }

        private bool CanSweepReflow(Enemy mover, Position2 from, Position2 to, IReadOnlyList<Enemy> traffic, Dictionary<Enemy, Position2> planned = null)
        {
            int radius = mover.Definition.BodyRadius;
            if (!IsClearOfTowers(from, to, radius))
            {
                return false;
            }

            foreach (var other in traffic)
            {
                if (other == mover)
                {
                    continue;
                }

                var point = planned != null && planned.TryGetValue(other, out var position) ? position : other.Position;
                int bodyClearance = radius + other.Definition.BodyRadius;
                int clearance = bodyClearance + AssaultReservationPadding;
                double distance = SegmentDistanceSquared(point, from, to);
                if (distance < (long)bodyClearance * bodyClearance || (distance < (long)clearance * clearance && distance < DistanceSquared(from, point))
                    || IntersectsAssaultReservation(from, to, other, clearance))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ClearsReflowPath(Enemy gate, Position2[] path, Position2 point, int radius)
        {
            long clearance = gate.Definition.BodyRadius + radius + AssaultReservationPadding;
            var from = gate.Position;
            foreach (var to in path)
            {
                if (SegmentDistanceSquared(point, from, to) < clearance * clearance)
                {
                    return false;
                }

                from = to;
            }

            return true;
        }

        private static bool IsSettledCandidate(Enemy unit) => unit.AssaultOrder is { Arrived: true, Staging: true };

        private bool IsWaitingGround(Position2 point, int radius, BaseAttackLayout layout)
        {
            long gap = layout.Engagement.Reach + radius + radius / 2;
            return layout.Engagement.DistanceToFootprintSquared(point) >= gap * gap
                && !IntersectsAuthoredRoad(point, radius, includeApproach: true);
        }
    }
}
