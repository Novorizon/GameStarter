using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int TrafficGap = 8;
        private const int GridClearanceMargin = 3;
        internal const int BaseMeleeReachMillimetres = 300;
        private const int PlanningIntervalTicks = 4;
        private const int StagingClearance = 1380;

        private AssaultNavigationMap navigation;
        private readonly Dictionary<int, AssaultNavigationGrid> navigationGrids = new Dictionary<int, AssaultNavigationGrid>();
        private readonly Dictionary<(int RouteId, int Radius), int> approachLimits = new Dictionary<(int, int), int>();
        private readonly List<RoadCrossing> roadCrossings = new List<RoadCrossing>();
        private readonly List<NavigationObstacle> roadSweeps = new List<NavigationObstacle>();
        private readonly List<NavigationObstacle> protectedApproaches = new List<NavigationObstacle>();
        private readonly List<NavigationObstacle> pathObstacles = new List<NavigationObstacle>();
        private readonly List<Enemy> maneuvers = new List<Enemy>();
        private readonly List<Enemy> ingress = new List<Enemy>();
        private readonly Dictionary<int, LocalPathSearch> pathSearches = new Dictionary<int, LocalPathSearch>();
        private readonly HashSet<(int RouteId, int Radius)> blockedIngress = new HashSet<(int, int)>();
        private long nextPlanTick;
        private int attackLayoutVersion = 1;
        private readonly Dictionary<int, int> checkedStaging = new Dictionary<int, int>();
        private readonly HashSet<int> checkedRepositioning = new HashSet<int>();
        private readonly HashSet<int> checkedTightening = new HashSet<int>();
        private readonly Dictionary<int, Position2> checkedOverflow = new Dictionary<int, Position2>();

        private bool UsesTraffic => Configuration.Objective.Type != BattleObjectiveType.LegacyBaseLeak;

        internal static int EnemyNavigationRadiusMillimetres(int definitionId)
        {
            return definitionId switch
            {
                5 => 480,
                3 => 320,
                _ => 240
            };
        }

        private sealed class RouteTrack
        {
            private readonly int[] distances;
            internal RouteDefinition Route { get; }
            internal int Length => distances[^1];

            internal RouteTrack(RouteDefinition route)
            {
                Route = route;
                distances = new int[route.Points.Length];
                for (int index = 1; index < distances.Length; index++)
                {
                    distances[index] = distances[index - 1] + SegmentLength(route.Points[index - 1], route.Points[index]);
                    Require(distances[index] <= 10000000, "Route length exceeds the supported range.");
                }
            }

            internal int DistanceAtPoint(int index) => distances[index];

            internal int NextPointDistance(int distance)
            {
                int index = Array.BinarySearch(distances, distance);
                return distances[index >= 0 ? Math.Min(index + 1, distances.Length - 1) : ~index];
            }

            internal Position2 PositionAt(int distance)
            {
                int index = Array.BinarySearch(distances, distance);
                if (index >= 0)
                {
                    return Route.Points[index];
                }

                index = ~index;
                return Interpolate(Route.Points[index - 1], Route.Points[index], distance - distances[index - 1], distances[index] - distances[index - 1]);
            }
        }

        private sealed class RoadCrossing
        {
            private readonly Dictionary<int, (int Entry, int Exit, int FirstConflict)> ranges = new Dictionary<int, (int, int, int)>();
            internal Enemy Owner;

            internal bool TryGetRange(int routeId, out (int Entry, int Exit, int FirstConflict) range) => ranges.TryGetValue(routeId, out range);

            internal void Include(int routeId, int entry, int exit, int firstConflict)
            {
                if (ranges.TryGetValue(routeId, out var existing))
                {
                    entry = Math.Min(entry, existing.Entry);
                    exit = Math.Max(exit, existing.Exit);
                    firstConflict = Math.Min(firstConflict, existing.FirstConflict);
                }

                ranges[routeId] = (entry, exit, firstConflict);
            }

            internal bool Overlaps(RoadCrossing other)
            {
                foreach (var item in ranges)
                {
                    if (other.ranges.TryGetValue(item.Key, out var range) && item.Value.Entry <= range.Exit && range.Entry <= item.Value.Exit)
                    {
                        return true;
                    }
                }

                return false;
            }

            internal void Merge(RoadCrossing other)
            {
                foreach (var item in other.ranges)
                {
                    Include(item.Key, item.Value.Entry, item.Value.Exit, item.Value.FirstConflict);
                }
            }
        }

        private readonly struct NavigationObstacle
        {
            internal Position2 From { get; }
            internal Position2 To { get; }
            internal int Radius { get; }

            internal NavigationObstacle(Position2 from, Position2 to, int radius)
            {
                From = from;
                To = to;
                Radius = radius;
            }

            internal bool Blocks(Position2 from, Position2 to, int radius)
            {
                int clearance = radius + Radius + TrafficGap;
                if (Math.Min(from.X, to.X) >= Math.Max(From.X, To.X) + clearance || Math.Max(from.X, to.X) <= Math.Min(From.X, To.X) - clearance
                    || Math.Min(from.Y, to.Y) >= Math.Max(From.Y, To.Y) + clearance || Math.Max(from.Y, to.Y) <= Math.Min(From.Y, To.Y) - clearance)
                {
                    return false;
                }

                long squared = (long)clearance * clearance;
                return PointSegmentDistanceSquared(from, From, To) < squared || PointSegmentDistanceSquared(to, From, To) < squared
                    || PointSegmentDistanceSquared(From, from, to) < squared || PointSegmentDistanceSquared(To, from, to) < squared
                    || TryIntersect(from, to, From, To, out _, out _);
            }
        }

        private void InitializeNavigation()
        {
            if (Configuration.Objective.Type == BattleObjectiveType.DefendBase)
            {
                navigation = Configuration.AssaultNavigation ?? AssaultNavigationMap.CreateDefault(Configuration.Routes[0].Points[^1]);
                foreach (var group in Configuration.Waves.SelectMany(wave => wave.Groups))
                {
                    int radius = EnemyNavigationRadiusMillimetres(group.EnemyId);
                    if (!navigationGrids.ContainsKey(radius))
                    {
                        var grid = new AssaultNavigationGrid(navigation, radius + GridClearanceMargin);
                        navigationGrids.Add(radius, grid);
                        pathSearches.Add(radius, new LocalPathSearch(grid));
                    }

                    var key = (group.RouteId, radius);
                    if (!approachLimits.ContainsKey(key))
                    {
                        var route = routes[group.RouteId];
                        int limit = FindApproachLimit(route, navigationGrids[radius]);
                        approachLimits.Add(key, limit);
                        ProtectApproach(route, limit, radius);
                    }
                }

                InitializeStagingDepths();
            }

            if (UsesTraffic && routes.Count > 1)
            {
                CreateRoadCrossings();
            }
        }

        private int ApproachLimit(RouteTrack route, int radius)
        {
            return navigation == null ? route.Length : approachLimits[(route.Route.Id, radius)];
        }

        private int FindApproachLimit(RouteTrack route, AssaultNavigationGrid grid)
        {
            int first = Math.Max(0, route.Length - navigation.ApproachInset);
            if (route.Route.Id == Configuration.RelayRouteId)
            {
                first = Math.Max(first, route.DistanceAtPoint(Configuration.RelayPointIndex));
            }

            for (int distance = first; distance < route.Length; distance += 100)
            {
                var point = route.PositionAt(distance);
                if (!grid.IsInside(point))
                {
                    continue;
                }

                int node = grid.ClosestNode(point);
                if (node >= 0 && DistanceSquared(point, grid.Position(node)) <= LocalLinkDistance * LocalLinkDistance)
                {
                    return distance;
                }
            }

            // No local connection. Keep the road queue short of the footprint and wait for valid geometry.
            return first;
        }

        private void ProtectApproach(RouteTrack route, int limit, int radius)
        {
            int end = Math.Min(route.Length, limit + 950);
            for (int distance = 0; distance < end;)
            {
                int next = Math.Min(end, route.NextPointDistance(distance));
                protectedApproaches.Add(new NavigationObstacle(route.PositionAt(distance), route.PositionAt(next), radius + 120));
                distance = next;
            }
        }

        private void CreateRoadCrossings()
        {
            var tracks = Configuration.Routes.Select(route => routes[route.Id]).ToArray();
            int clearance = 2 * Configuration.Enemies.Max(enemy => EnemyNavigationRadiusMillimetres(enemy.Id)) + TrafficGap;
            for (int first = 0; first < tracks.Length; first++)
            {
                for (int second = first + 1; second < tracks.Length; second++)
                {
                    AddRoadCrossings(tracks[first], tracks[second], clearance);
                }
            }
        }

        private void AddRoadCrossings(RouteTrack first, RouteTrack second, int clearance)
        {
            int firstEnd = first.Route.Points.Length - 1;
            int secondEnd = second.Route.Points.Length - 1;
            while (firstEnd > 0 && secondEnd > 0 && first.Route.Points[firstEnd].Equals(second.Route.Points[secondEnd])
                && first.Route.Points[firstEnd - 1].Equals(second.Route.Points[secondEnd - 1]))
            {
                firstEnd--;
                secondEnd--;
            }

            // The common tail has one merge, then both streams follow the same direction.
            if (firstEnd < first.Route.Points.Length - 1)
            {
                AddRoadCrossing(first, second, first.DistanceAtPoint(firstEnd), second.DistanceAtPoint(secondEnd), clearance);
            }

            for (int a = 1; a <= firstEnd; a++)
            {
                for (int b = 1; b <= secondEnd; b++)
                {
                    var startA = first.Route.Points[a - 1];
                    var endA = first.Route.Points[a];
                    var startB = second.Route.Points[b - 1];
                    var endB = second.Route.Points[b];
                    if (!TryIntersect(startA, endA, startB, endB, out double alongA, out double alongB))
                    {
                        continue;
                    }

                    int distanceA = first.DistanceAtPoint(a - 1) + (int)(alongA * (first.DistanceAtPoint(a) - first.DistanceAtPoint(a - 1)));
                    int distanceB = second.DistanceAtPoint(b - 1) + (int)(alongB * (second.DistanceAtPoint(b) - second.DistanceAtPoint(b - 1)));
                    AddRoadCrossing(first, second, distanceA, distanceB, clearance);
                }
            }
        }

        private void AddRoadCrossing(RouteTrack first, RouteTrack second, int distanceA, int distanceB, int clearance)
        {
            int halfLength = 2 * clearance;
            var combined = new RoadCrossing();
            combined.Include(first.Route.Id, Math.Max(0, distanceA - halfLength), Math.Min(first.Length, distanceA + halfLength), distanceA);
            combined.Include(second.Route.Id, Math.Max(0, distanceB - halfLength), Math.Min(second.Length, distanceB + halfLength), distanceB);

            // A unit must clear an entire connected conflict area before another route can enter it.
            for (int index = 0; index < roadCrossings.Count;)
            {
                var existing = roadCrossings[index];
                if (!combined.Overlaps(existing))
                {
                    index++;
                    continue;
                }

                combined.Merge(existing);
                roadCrossings.RemoveAt(index);
                index = 0;
            }

            roadCrossings.Add(combined);
        }

        private bool CanOccupySpawn(Position2 spawn, int radius)
        {
            foreach (var other in enemies)
            {
                int clearance = radius + other.Radius + TrafficGap;
                if (DistanceSquared(spawn, other.Position) < (long)clearance * clearance)
                {
                    return false;
                }
            }

            return !BlocksManeuver(spawn, spawn, radius);
        }

        private void NavigationEnemyRemoved(Enemy enemy)
        {
            if (enemy.AtAttackPosition)
            {
                RecordAttackVacancy(enemy);
            }

            attackLayoutVersion++;
            maneuvers.Remove(enemy);
            if (yieldingFor == enemy)
            {
                yieldingFor = null;
                yieldingUnit = null;
            }
            else if (yieldingUnit == enemy)
            {
                yieldingUnit = null;
            }

            checkedStaging.Remove(enemy.Id);
            checkedRepositioning.Clear();
            checkedTightening.Clear();
            checkedOverflow.Clear();
            blockedIngress.Clear();
            ReleaseCrossings(enemy);
        }

        private void NavigationObstaclesChanged()
        {
            attackLayoutVersion++;
            nextPlanTick = Tick;
            blockedIngress.Clear();
            checkedRepositioning.Clear();
            checkedTightening.Clear();
            checkedOverflow.Clear();
            if (maneuvers.Any(ManeuverBlockedByTower))
            {
                // Stopping one mover invalidates the timing assumed by every other reserved trajectory.
                foreach (var enemy in maneuvers)
                {
                    enemy.Maneuver = null;
                    enemy.AttackDestination = false;
                }

                maneuvers.Clear();
            }
        }

        private long DistanceToObjective(Enemy enemy)
        {
            if (navigation != null && (enemy.InAssaultArea || enemy.RouteDistance >= enemy.ApproachLimit))
            {
                return navigation.DistanceToBaseSquared(enemy.Position);
            }

            long remaining = routes[enemy.Route.Id].Length - enemy.RouteDistance;
            return remaining * remaining;
        }

        private void AdvanceEnemies()
        {
            roadSweeps.Clear();
            foreach (var enemy in enemies)
            {
                enemy.PreviousPosition = enemy.Position;
            }

            for (int index = maneuvers.Count - 1; index >= 0; index--)
            {
                var enemy = maneuvers[index];
                if (AdvanceManeuver(enemy))
                {
                    maneuvers.RemoveAt(index);
                    blockedIngress.Clear();
                    checkedRepositioning.Clear();
                    ResetAttackVacancyProbe();
                    checkedTightening.Clear();
                    checkedOverflow.Clear();
                    nextPlanTick = Tick;
                }
            }

            int firstRoadSweep = roadSweeps.Count;
            foreach (var enemy in enemies)
            {
                if (!enemy.InAssaultArea && enemy.RouteDistance < enemy.ApproachLimit && (UsesTraffic || !AtObjective(enemy)))
                {
                    AdvanceOnRoad(enemy);
                }
            }

            if (navigation != null && (blockedIngress.Count != 0 || checkedOverflow.Count != 0 || checkedRepositioning.Count != 0))
            {
                for (int index = firstRoadSweep; index < roadSweeps.Count; index++)
                {
                    var sweep = roadSweeps[index];
                    if (!sweep.From.Equals(sweep.To) && navigation.IsInsideArea(sweep.From))
                    {
                        blockedIngress.Clear();
                        checkedOverflow.Clear();
                        checkedRepositioning.Clear();
                        break;
                    }
                }
            }

            if (navigation != null && Tick >= nextPlanTick)
            {
                nextPlanTick = Tick + PlanningIntervalTicks;
                PlanAssaultMoves();
            }
        }

        private void AdvanceOnRoad(Enemy enemy)
        {
            var track = routes[enemy.Route.Id];
            int limit = enemy.ApproachLimit;
            if (UsesTraffic)
            {
                limit = Math.Min(limit, track.NextPointDistance(enemy.RouteDistance));
            }

            int nextDistance = Math.Min(limit, enemy.RouteDistance + enemy.Definition.SpeedPerTick);
            var start = enemy.Position;
            if (UsesTraffic)
            {
                nextDistance = LimitAtCrossings(enemy, nextDistance);
                if (nextDistance <= enemy.RouteDistance || !CanMoveOnRoad(enemy, track.PositionAt(nextDistance)))
                {
                    return;
                }

                ClaimCrossings(enemy, nextDistance);
            }

            enemy.RouteDistance = nextDistance;
            enemy.Position = track.PositionAt(nextDistance);
            if (UsesTraffic)
            {
                roadSweeps.Add(new NavigationObstacle(start, enemy.Position, enemy.Radius));
                ReleasePassedCrossings(enemy);
            }
        }

        private int LimitAtCrossings(Enemy enemy, int nextDistance)
        {
            foreach (var crossing in roadCrossings)
            {
                if (crossing.Owner == enemy || crossing.Owner == null || !crossing.TryGetRange(enemy.Route.Id, out var range))
                {
                    continue;
                }

                if (nextDistance <= range.Entry || enemy.RouteDistance >= range.Exit || range.FirstConflict > enemy.ApproachLimit)
                {
                    continue;
                }

                nextDistance = Math.Min(nextDistance, Math.Max(enemy.RouteDistance, range.Entry));
            }

            return nextDistance;
        }

        private void ClaimCrossings(Enemy enemy, int nextDistance)
        {
            foreach (var crossing in roadCrossings)
            {
                if (crossing.Owner != null || !crossing.TryGetRange(enemy.Route.Id, out var range))
                {
                    continue;
                }

                if (nextDistance > range.Entry && enemy.RouteDistance < range.Exit && range.FirstConflict <= enemy.ApproachLimit)
                {
                    crossing.Owner = enemy;
                }
            }
        }

        private void ReleasePassedCrossings(Enemy enemy)
        {
            foreach (var crossing in roadCrossings)
            {
                if (crossing.Owner == enemy && crossing.TryGetRange(enemy.Route.Id, out var range) && enemy.RouteDistance >= range.Exit)
                {
                    crossing.Owner = null;
                }
            }
        }

        private void ReleaseCrossings(Enemy enemy)
        {
            foreach (var crossing in roadCrossings)
            {
                if (crossing.Owner == enemy)
                {
                    crossing.Owner = null;
                }
            }
        }

        private bool CanMoveOnRoad(Enemy enemy, Position2 next)
        {
            if (navigation != null && Configuration.AssaultNavigation != null && !navigationGrids[enemy.Radius].CanSweep(enemy.Position, next))
            {
                return false;
            }

            if (BlocksManeuver(enemy.Position, next, enemy.Radius) || BlocksAny(roadSweeps, enemy.Position, next, enemy.Radius))
            {
                return false;
            }

            foreach (var other in enemies)
            {
                if (other != enemy && new NavigationObstacle(other.Position, other.Position, other.Radius).Blocks(enemy.Position, next, enemy.Radius))
                {
                    return false;
                }
            }

            return true;
        }

        private bool IsTowerFoundationClear(Position2 position)
        {
            if (navigation == null || !navigation.IsInsideArea(position))
            {
                return true;
            }

            var foundation = new NavigationObstacle(position, position, TowerFoundationRadiusMillimetres);
            return !enemies.Any(enemy => foundation.Blocks(enemy.Position, enemy.Position, enemy.Radius));
        }

        private bool BlocksManeuver(Position2 from, Position2 to, int radius)
        {
            foreach (var mover in maneuvers)
            {
                var start = mover.Position;
                for (int index = mover.ManeuverIndex; index < mover.Maneuver.Length; index++)
                {
                    var end = mover.Maneuver[index];
                    if (new NavigationObstacle(start, end, mover.Radius).Blocks(from, to, radius))
                    {
                        return true;
                    }

                    start = end;
                }
            }

            return false;
        }

        private bool AdvanceManeuver(Enemy enemy)
        {
            if (enemy.ManeuverWaitTicks > 0)
            {
                enemy.ManeuverWaitTicks--;
                return false;
            }

            var start = enemy.Position;
            var target = enemy.Maneuver[enemy.ManeuverIndex];
            int length = SegmentLength(enemy.ManeuverSegmentStart, target);
            enemy.ManeuverSegmentDistance += enemy.Definition.SpeedPerTick;
            if (length > enemy.ManeuverSegmentDistance)
            {
                enemy.Position = Interpolate(enemy.ManeuverSegmentStart, target, enemy.ManeuverSegmentDistance, length);
            }
            else
            {
                enemy.Position = target;
                enemy.ManeuverIndex++;
                enemy.ManeuverSegmentStart = target;
                enemy.ManeuverSegmentDistance = 0;
            }

            roadSweeps.Add(new NavigationObstacle(start, enemy.Position, enemy.Radius));
            if (enemy.ManeuverIndex < enemy.Maneuver.Length)
            {
                return false;
            }

            enemy.Maneuver = null;
            enemy.AtAttackPosition = enemy.AttackDestination;
            enemy.HasStagingPosition = !enemy.AttackDestination;
            return true;
        }

        private static bool BlocksAny(List<NavigationObstacle> obstacles, Position2 from, Position2 to, int radius)
        {
            foreach (var obstacle in obstacles)
            {
                if (obstacle.Blocks(from, to, radius))
                {
                    return true;
                }
            }

            return false;
        }

        private static int SegmentLength(Position2 first, Position2 second) => (int)Math.Ceiling(Math.Sqrt(DistanceSquared(first, second)));

        private static Position2 Interpolate(Position2 from, Position2 to, int distance, int length)
        {
            return new Position2(from.X + (int)((long)(to.X - from.X) * distance / length), from.Y + (int)((long)(to.Y - from.Y) * distance / length));
        }

        private static double PointSegmentDistanceSquared(Position2 point, Position2 from, Position2 to)
        {
            long dx = (long)to.X - from.X;
            long dy = (long)to.Y - from.Y;
            long offsetX = (long)point.X - from.X;
            long offsetY = (long)point.Y - from.Y;
            long projection = offsetX * dx + offsetY * dy;
            if (projection <= 0)
            {
                return DistanceSquared(point, from);
            }

            long length = dx * dx + dy * dy;
            if (projection >= length)
            {
                return DistanceSquared(point, to);
            }

            long cross = offsetX * dy - offsetY * dx;
            return (double)cross * cross / length;
        }

        private static bool TryIntersect(Position2 a, Position2 b, Position2 c, Position2 d, out double alongFirst, out double alongSecond)
        {
            long abX = (long)b.X - a.X;
            long abY = (long)b.Y - a.Y;
            long cdX = (long)d.X - c.X;
            long cdY = (long)d.Y - c.Y;
            long determinant = abX * cdY - abY * cdX;
            if (determinant == 0)
            {
                alongFirst = alongSecond = 0;
                return false;
            }

            long x = (long)c.X - a.X;
            long y = (long)c.Y - a.Y;
            alongFirst = (double)(x * cdY - y * cdX) / determinant;
            alongSecond = (double)(x * abY - y * abX) / determinant;
            return alongFirst >= 0 && alongFirst <= 1 && alongSecond >= 0 && alongSecond <= 1;
        }
    }
}
