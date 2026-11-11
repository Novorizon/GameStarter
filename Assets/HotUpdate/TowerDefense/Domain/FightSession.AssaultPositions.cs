using System;
using System.Collections.Generic;

namespace Game.HotUpdate.TowerDefense.Domain
{
    public sealed partial class FightSession
    {
        private const int LocalLinkDistance = 2 * AssaultNavigationGrid.CellSize;
        private const int MinimumTighteningDistance = 500;

        private sealed class LocalPathSearch
        {
            internal readonly int[] Parents;
            internal readonly int[] Distance;
            internal readonly int[] Queue;
            internal readonly byte[] Occupancy;
            internal readonly bool[] AttackPositions;
            internal readonly bool[] StagingPositions;
            internal readonly int[] StagingDepth;

            internal LocalPathSearch(AssaultNavigationGrid grid)
            {
                Parents = new int[grid.Count];
                Distance = new int[grid.Count];
                Queue = new int[grid.Count];
                Occupancy = new byte[grid.Count];
                AttackPositions = new bool[grid.Count];
                StagingPositions = new bool[grid.Count];
                StagingDepth = new int[grid.Count];
                for (int node = 0; node < grid.Count; node++)
                {
                    AttackPositions[node] = grid.IsOpen(node) && grid.IsAttackPosition(grid.Position(node), BaseMeleeReachMillimetres - GridClearanceMargin);
                }
            }
        }

        private Enemy yieldingUnit;
        private Enemy yieldingFor;
        private int lastYieldHead;
        private readonly HashSet<int> yieldedForHead = new HashSet<int>();
        private readonly List<Enemy> promotionCandidates = new List<Enemy>();
        private Position2? releasedAttackPosition;

        private void InitializeStagingDepths()
        {
            foreach (var entry in navigationGrids)
            {
                int radius = entry.Key;
                var grid = entry.Value;
                var search = pathSearches[radius];
                Array.Fill(search.StagingDepth, -1);
                int tail = 0;
                foreach (var approach in approachLimits)
                {
                    if (approach.Key.Radius != radius)
                    {
                        continue;
                    }

                    int node = grid.ClosestNode(routes[approach.Key.RouteId].PositionAt(approach.Value));
                    if (node >= 0 && search.StagingDepth[node] < 0)
                    {
                        search.StagingDepth[node] = 0;
                        search.Queue[tail++] = node;
                    }
                }

                for (int head = 0; head < tail; head++)
                {
                    int node = search.Queue[head];
                    for (int direction = 0; direction < AssaultNavigationGrid.NeighborCount; direction++)
                    {
                        int next = grid.Neighbor(node, direction);
                        if (next >= 0 && search.StagingDepth[next] < 0)
                        {
                            search.StagingDepth[next] = search.StagingDepth[node] + 1;
                            search.Queue[tail++] = next;
                        }
                    }
                }

                int buffer = StagingClearance + radius;
                for (int node = 0; node < grid.Count; node++)
                {
                    var position = grid.Position(node);
                    bool valid = search.StagingDepth[node] >= 0 && !BlocksAny(protectedApproaches, position, position, radius);
                    long distance = navigation.DistanceToBaseSquared(position);
                    search.StagingPositions[node] = valid && distance >= (long)buffer * buffer;
                }
            }
        }

        private void PlanAssaultMoves()
        {
            if (yieldingUnit?.Maneuver != null)
            {
                return;
            }

            if (TryContinueAttackRelay())
            {
                return;
            }

            RefreshAttackVacancy();
            if (yieldingFor != null)
            {
                var next = yieldingFor;
                yieldingFor = null;
                yieldingUnit = null;
                var attackPath = yieldingAttackPath;
                yieldingAttackPath = null;
                if (attackPath == null ? TryPlanAssaultMove(next, true) : TryResumeAttackVacancyYield(next, attackPath))
                {
                    return;
                }
            }

            ingress.Clear();
            promotionCandidates.Clear();
            foreach (var enemy in enemies)
            {
                if (enemy.AtAttackPosition || enemy.Maneuver != null || (!enemy.InAssaultArea && enemy.RouteDistance < enemy.ApproachLimit))
                {
                    continue;
                }

                if (enemy.HasStagingPosition)
                {
                    if (!checkedStaging.TryGetValue(enemy.Id, out int version) || version != attackLayoutVersion)
                    {
                        promotionCandidates.Add(enemy);
                    }
                }
                else
                {
                    ingress.Add(enemy);
                }
            }

            if (releasedAttackPosition is Position2 vacancy)
            {
                promotionCandidates.Sort((first, second) =>
                {
                    int order = DistanceSquared(first.Position, vacancy).CompareTo(DistanceSquared(second.Position, vacancy));
                    return order == 0 ? first.Id.CompareTo(second.Id) : order;
                });
            }

            int promotionBudget = releasedAttackPosition.HasValue ? 6 : 2;
            for (int index = 0; index < Math.Min(promotionBudget, promotionCandidates.Count); index++)
            {
                var enemy = promotionCandidates[index];
                if (TryPlanAssaultMove(enemy, false))
                {
                    return;
                }

                if (maneuvers.Count == 0)
                {
                    checkedStaging[enemy.Id] = attackLayoutVersion;
                }
            }

            foreach (var enemy in ingress)
            {
                var key = (enemy.Route.Id, enemy.Radius);
                bool atEntry = enemy.Position.Equals(routes[enemy.Route.Id].PositionAt(enemy.ApproachLimit));
                if (atEntry && blockedIngress.Contains(key))
                {
                    continue;
                }

                if (TryPlanAssaultMove(enemy, true))
                {
                    return;
                }

                if (atEntry && maneuvers.Count == 0)
                {
                    blockedIngress.Add(key);
                }
            }

            if (maneuvers.Count == 0 && promotionCandidates.Count <= promotionBudget && releasedAttackPosition.HasValue && TryClearAttackVacancy())
            {
                return;
            }

            if (ingress.Count > 0 && !TryUseRoadsideExit() && maneuvers.Count == 0)
            {
                TryClearIngress(ingress[0]);
            }
            else if (!releasedAttackPosition.HasValue && ingress.Count == 0 && AllEnemiesSpawned && enemies.TrueForAll(enemy => enemy.InAssaultArea))
            {
                TryTightenStaging();
            }
        }

        private void TryTightenStaging()
        {
            int attempts = 0;
            foreach (var parked in enemies)
            {
                if (!parked.HasStagingPosition || !checkedTightening.Add(parked.Id))
                {
                    continue;
                }

                int closer = (int)Math.Sqrt(navigation.DistanceToBaseSquared(parked.Position)) - MinimumTighteningDistance;
                if (closer > 0 && TryPlanAssaultMove(parked, true, (long)closer * closer))
                {
                    attackLayoutVersion++;
                    return;
                }

                if (++attempts == 2)
                {
                    break;
                }
            }
        }

        private bool TryUseRoadsideExit()
        {
            int attempts = 0;
            foreach (var enemy in enemies)
            {
                bool relayPending = enemy.Route.Id == Configuration.RelayRouteId
                    && enemy.RouteDistance < routes[enemy.Route.Id].DistanceAtPoint(Configuration.RelayPointIndex);
                if (enemy.InAssaultArea || enemy.RouteDistance >= enemy.ApproachLimit || !navigation.IsInsideArea(enemy.Position)
                    || relayPending
                    || checkedOverflow.TryGetValue(enemy.Id, out var previous) && previous.Equals(enemy.Position))
                {
                    continue;
                }

                checkedOverflow[enemy.Id] = enemy.Position;
                if (TryPlanAssaultMove(enemy, true))
                {
                    return true;
                }

                if (++attempts == 2)
                {
                    break;
                }
            }

            return false;
        }

        private void TryClearIngress(Enemy head)
        {
            if (lastYieldHead != head.Id)
            {
                lastYieldHead = head.Id;
                checkedRepositioning.Clear();
                yieldedForHead.Clear();
            }

            int attempts = 0;
            foreach (var parked in enemies)
            {
                if (!parked.HasStagingPosition || yieldedForHead.Contains(parked.Id) || !checkedRepositioning.Add(parked.Id))
                {
                    continue;
                }

                if (TryYieldPosition(head, parked) || ++attempts == 2)
                {
                    return;
                }
            }
        }

        private bool TryPlanAssaultMove(Enemy enemy, bool allowStaging, long maximumStagingDistanceSquared = -1)
        {
            var grid = navigationGrids[enemy.Radius];
            if (!grid.CanStand(enemy.Position))
            {
                return false;
            }

            CollectPathObstacles(enemy);
            if (releasedAttackPosition is Position2 vacancy
                && grid.IsAttackPosition(vacancy, BaseMeleeReachMillimetres - GridClearanceMargin)
                && grid.CanSweep(enemy.Position, vacancy) && !BlocksAny(pathObstacles, enemy.Position, vacancy, enemy.Radius)
                && StartManeuver(enemy, new[] { vacancy }, true))
            {
                return true;
            }

            var search = pathSearches[enemy.Radius];
            int reachable = FloodReachablePositions(enemy, grid, search);
            bool tightening = maximumStagingDistanceSquared >= 0;
            if (tightening)
            {
                PrepareCompactStaging(enemy);
            }

            int attack = -1;
            int staging = -1;
            long bestAttack = long.MaxValue;
            long bestStaging = long.MaxValue;
            for (int index = 0; index < reachable; index++)
            {
                int node = search.Queue[index];
                var position = grid.Position(node);
                if (search.AttackPositions[node])
                {
                    // Keep positions that touch a narrow entrance free until deeper positions have been occupied.
                    long score = -(long)search.StagingDepth[node] * 1000 + navigation.DistanceToBaseSquared(position) / 1000;
                    if (score < bestAttack)
                    {
                        bestAttack = score;
                        attack = node;
                    }
                }
                else if (allowStaging)
                {
                    long baseDistance = navigation.DistanceToBaseSquared(position);
                    if (tightening && baseDistance > maximumStagingDistanceSquared)
                    {
                        continue;
                    }

                    long score = tightening ? baseDistance - (long)search.StagingDepth[node] * 20000 : StagingScore(grid, search, node);
                    bool ready = tightening && CanUseCompactStagingPosition(enemy, grid, position, baseDistance);
                    if (!ready && !search.StagingPositions[node])
                    {
                        continue;
                    }

                    if (ready)
                    {
                        score -= 100000000L;
                    }

                    if (score < bestStaging)
                    {
                        bestStaging = score;
                        staging = node;
                    }
                }
            }

            int destination = attack >= 0 ? attack : staging;
            if (destination < 0)
            {
                return false;
            }

            return StartManeuver(enemy, BuildManeuver(enemy, grid, search, destination), attack >= 0);
        }

        private long StagingScore(AssaultNavigationGrid grid, LocalPathSearch search, int node)
        {
            // Fill deeper pockets first so a parked unit cannot seal an otherwise usable pocket.
            long baseDistance = navigation.DistanceToBaseSquared(grid.Position(node));
            return baseDistance / 1000 - (long)search.StagingDepth[node] * 5000;
        }

        private bool StartManeuver(Enemy enemy, Position2[] path, bool attack)
        {
            int wait = FindManeuverStart(enemy, path);
            if (wait < 0)
            {
                return false;
            }

            enemy.AttackDestination = attack;
            enemy.InAssaultArea = true;
            enemy.HasStagingPosition = false;
            enemy.Maneuver = path;
            enemy.ManeuverIndex = 0;
            enemy.ManeuverSegmentStart = enemy.Position;
            enemy.ManeuverSegmentDistance = 0;
            enemy.ManeuverWaitTicks = wait;
            checkedStaging.Remove(enemy.Id);
            ReleaseCrossings(enemy);
            maneuvers.Add(enemy);
            return true;
        }

        private int FloodReachablePositions(Enemy enemy, AssaultNavigationGrid grid, LocalPathSearch search)
        {
            Array.Fill(search.Parents, -2);
            Array.Clear(search.Occupancy, 0, search.Occupancy.Length);
            int tail = 0;
            for (int node = 0; node < grid.Count; node++)
            {
                if (!grid.IsOpen(node))
                {
                    continue;
                }

                var position = grid.Position(node);
                if (DistanceSquared(enemy.Position, position) <= LocalLinkDistance * LocalLinkDistance
                    && grid.CanSweep(enemy.Position, position) && !BlocksAny(pathObstacles, enemy.Position, position, enemy.Radius))
                {
                    search.Parents[node] = -1;
                    search.Distance[node] = 0;
                    search.Queue[tail++] = node;
                }
            }

            for (int head = 0; head < tail; head++)
            {
                int node = search.Queue[head];
                var position = grid.Position(node);
                for (int direction = 0; direction < AssaultNavigationGrid.NeighborCount; direction++)
                {
                    int next = grid.Neighbor(node, direction);
                    if (next < 0 || search.Parents[next] != -2 || search.Occupancy[next] == 2)
                    {
                        continue;
                    }

                    var nextPosition = grid.Position(next);
                    if (search.Occupancy[next] == 0)
                    {
                        search.Occupancy[next] = BlocksAny(pathObstacles, nextPosition, nextPosition, enemy.Radius) ? (byte)2 : (byte)1;
                    }

                    if (search.Occupancy[next] == 2 || BlocksAny(pathObstacles, position, nextPosition, enemy.Radius))
                    {
                        continue;
                    }

                    search.Parents[next] = node;
                    search.Distance[next] = search.Distance[node] + 1;
                    search.Queue[tail++] = next;
                }
            }

            return tail;
        }

        private bool TryYieldPosition(Enemy head, Enemy parked, Position2? attackVacancy = null)
        {
            Position2[] desiredPath = FindYieldPath(head, parked, attackVacancy);
            if (desiredPath == null || !BlocksPath(parked.Position, parked.Radius, head, desiredPath))
            {
                return false;
            }

            var grid = navigationGrids[parked.Radius];
            var search = pathSearches[parked.Radius];
            CollectPathObstacles(parked);
            int reachable = FloodReachablePositions(parked, grid, search);
            int destination = -1;
            int shortest = int.MaxValue;
            for (int index = 0; index < reachable; index++)
            {
                int node = search.Queue[index];
                if (search.StagingPositions[node] && search.Distance[node] < shortest
                    && !BlocksPath(grid.Position(node), parked.Radius, head, desiredPath))
                {
                    shortest = search.Distance[node];
                    destination = node;
                }
            }

            if (destination < 0)
            {
                return false;
            }

            if (!StartManeuver(parked, BuildManeuver(parked, grid, search, destination), false))
            {
                return false;
            }

            attackLayoutVersion++;
            yieldingUnit = parked;
            yieldingFor = head;
            yieldingAttackPath = attackVacancy.HasValue ? desiredPath : null;
            if (!attackVacancy.HasValue)
            {
                yieldedForHead.Add(parked.Id);
            }
            return true;
        }

        private Position2[] FindYieldPath(Enemy head, Enemy parked, Position2? attackVacancy)
        {
            var grid = navigationGrids[head.Radius];
            CollectPathObstacles(head, parked);
            if (attackVacancy is Position2 point && grid.IsAttackPosition(point, BaseMeleeReachMillimetres - GridClearanceMargin)
                && grid.CanSweep(head.Position, point) && !BlocksAny(pathObstacles, head.Position, point, head.Radius))
            {
                return new[] { point };
            }

            var search = pathSearches[head.Radius];
            int reachable = FloodReachablePositions(head, grid, search);
            int goal = -1;
            long best = long.MaxValue;
            for (int index = 0; index < reachable; index++)
            {
                int node = search.Queue[index];
                bool attackPosition = search.AttackPositions[node];
                if (!attackPosition && (attackVacancy.HasValue || !search.StagingPositions[node]))
                {
                    continue;
                }

                long score = attackVacancy is Position2 vacancy ? DistanceSquared(grid.Position(node), vacancy)
                    : attackPosition ? (long)search.Distance[node] - 2000000 : StagingScore(grid, search, node);
                if (score < best)
                {
                    best = score;
                    goal = node;
                }
            }

            return goal < 0 ? null : BuildManeuver(head, grid, search, goal);
        }

        private static bool BlocksPath(Position2 position, int radius, Enemy mover, Position2[] path)
        {
            var obstacle = new NavigationObstacle(position, position, radius);
            var from = mover.Position;
            foreach (var to in path)
            {
                if (obstacle.Blocks(from, to, mover.Radius))
                {
                    return true;
                }

                from = to;
            }

            return false;
        }

        private Position2[] BuildManeuver(Enemy enemy, AssaultNavigationGrid grid, LocalPathSearch search, int destination)
        {
            var reversed = new List<Position2>();
            for (int node = destination; node >= 0; node = search.Parents[node])
            {
                reversed.Add(grid.Position(node));
            }

            var result = new List<Position2>();
            var from = enemy.Position;
            int last = reversed.Count;
            while (last > 0)
            {
                int next = 0;
                while (next < last - 1 && (!grid.CanSweep(from, reversed[next]) || BlocksAny(pathObstacles, from, reversed[next], enemy.Radius)))
                {
                    next++;
                }

                from = reversed[next];
                result.Add(from);
                last = next;
            }

            return result.ToArray();
        }

        private void CollectPathObstacles(Enemy enemy, Enemy ignore = null, Enemy additionalIgnore = null)
        {
            pathObstacles.Clear();
            foreach (var tower in towers)
            {
                var position = tower.Slot.Position;
                pathObstacles.Add(new NavigationObstacle(position, position, TowerFoundationRadiusMillimetres));
            }

            foreach (var other in enemies)
            {
                if (other == enemy || other == ignore || other == additionalIgnore)
                {
                    continue;
                }

                var from = other.Position;
                if (other.Maneuver != null)
                {
                    var destination = other.Maneuver[^1];
                    pathObstacles.Add(new NavigationObstacle(destination, destination, other.Radius));
                }
                else
                {
                    pathObstacles.Add(new NavigationObstacle(from, from, other.Radius));
                }
            }
        }

        private bool ManeuverBlockedByTower(Enemy enemy)
        {
            foreach (var tower in towers)
            {
                var position = tower.Slot.Position;
                var obstacle = new NavigationObstacle(position, position, TowerFoundationRadiusMillimetres);
                var start = enemy.Position;
                for (int index = enemy.ManeuverIndex; index < enemy.Maneuver.Length; index++)
                {
                    var end = enemy.Maneuver[index];
                    if (obstacle.Blocks(start, end, enemy.Radius))
                    {
                        return true;
                    }

                    start = end;
                }
            }

            return false;
        }
    }
}
