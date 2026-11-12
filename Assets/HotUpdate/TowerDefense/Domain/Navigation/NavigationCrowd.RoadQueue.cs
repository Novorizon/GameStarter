using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private readonly Dictionary<(int Cell, int Speed, Profile Profile), (int Revision, long Until, NavigationFailure Failure)> admissionRetry
            = new Dictionary<(int, int, Profile), (int, long, NavigationFailure)>();

        private bool UseRoadQueue => stableSiegePositions && grid.HasRoadPreference;

        private long nextRoadSideYieldTick;
        public int RoadSideYields { get; private set; }

        private bool TryGetAdmissionRetry(int start, int speed, Profile profile, out NavigationFailure failure)
        {
            if (admissionRetry.TryGetValue((start, speed, profile), out var retry) && retry.Revision == goalRevision && CurrentTick < retry.Until)
            {
                failure = retry.Failure;
                return true;
            }

            failure = NavigationFailure.None;
            return false;
        }

        private void RememberAdmissionRetry(int start, int speed, Profile profile)
        {
            admissionRetry[(start, speed, profile)] = (goalRevision, CurrentTick + 12, LastFailure);
        }

        private List<TimedCell> FindRoadQueuePlan(Occupant state, int start, int minimumProgress)
        {
            LastExpandedNodes = 0;
            var reachable = Flood(state.Profile, start, state.Agent.Id, -1, true);
            var bands = Enumerable.Range(0, grid.CellCount).Where(cell => reachable[cell] && !entries.Contains(cell) && !grid.HoldingAllowed(cell)
                && grid.RoadCost(cell) <= NavigationGrid.RoadCostScale && grid.RoadProgress(cell) >= minimumProgress)
                .GroupBy(cell => grid.RoadProgress(cell) / grid.CellSize).OrderByDescending(band => band.Key);
            var intervals = new Dictionary<int, List<SafeInterval>>();
            var safety = new sbyte[grid.CellCount];
            var regions = new List<AccessRegion>();
            LastFailure = NavigationFailure.GoalAccessBlocked;
            foreach (var band in bands)
            {
                var targets = new bool[grid.CellCount];
                foreach (int cell in band)
                {
                    targets[cell] = true;
                }

                // Existing timed reservations still protect all already committed motion, even if this road cell becomes a queue stop.
                var plan = Search(state, start, targets, regions, safety, intervals);
                if (plan != null || LastFailure == NavigationFailure.SearchLimitExceeded)
                {
                    return plan;
                }
            }

            return null;
        }

        private void AdvanceRoadQueue()
        {
            var previousFailure = LastFailure;
            var queue = occupants.Values.Where(state => state.Agent.Status == NavigationAgentStatus.Queued)
                .OrderByDescending(state => grid.RoadProgress(state.Agent.GoalCell)).ThenBy(state => state.Agent.Id).ToArray();
            var head = queue.FirstOrDefault(state => CurrentTick >= state.QueueRetryTick);
            if (head == null)
            {
                return;
            }

            int cell = head.Agent.GoalCell;
            head.QueueRetryTick = CurrentTick + 20;
            var plan = FindPlan(head, cell, true) ?? FindRoadQueuePlan(head, cell, grid.RoadProgress(cell) + grid.CellSize);
            if (plan != null)
            {
                AcceptPlan(head, plan);
                UpdatePosition(head, CurrentTick);
                ReplanCount++;
            }
            else if (ReferenceEquals(head, queue[0]) && queue.Length > 1 && CurrentTick >= nextRoadSideYieldTick
                && CurrentTick - head.Agent.ArrivalTick >= 20)
            {
                nextRoadSideYieldTick = CurrentTick + 40;
                TryYieldRoadPocket(queue);
            }

            LastFailure = previousFailure;
        }

        private void TryYieldRoadPocket(Occupant[] queue)
        {
            var front = queue[0].Agent.Position;
            var profilesWithAttackSlots = profiles.Values.Where(profile => profile.Registered).ToArray();
            for (int index = 0; index < Math.Min(3, queue.Length - 1); index++)
            {
                var yielding = queue[index];
                var position = yielding.Agent.Position;
                int start = yielding.Agent.GoalCell;
                int progress = grid.RoadProgress(start);
                if (grid.RoadCost(start) > NavigationGrid.RoadCostScale || NavigationGrid.DistanceSquared(position, front) > 4000L * 4000)
                {
                    continue;
                }

                var follower = queue[index + 1];
                if (NavigationGrid.DistanceSquared(position, follower.Agent.Position) > 2000L * 2000)
                {
                    continue;
                }

                var reachable = Flood(yielding.Profile, start, yielding.Agent.Id, -1, true);
                var candidates = Enumerable.Range(0, grid.CellCount).Where(cell => reachable[cell] && !entries.Contains(cell)
                    && grid.RoadProgress(cell) >= progress - grid.CellSize && (yielding.Profile.Gather[cell] || !grid.HoldingAllowed(cell)
                    && grid.RoadCost(cell) > NavigationGrid.RoadCostScale)
                    && !profilesWithAttackSlots.Any(profile => profile.Attack[cell]))
                    .Select(cell => (Cell: cell, Distance: NavigationGrid.DistanceSquared(position, grid.Center(cell))))
                    .Where(candidate => candidate.Distance > 0 && candidate.Distance <= 1800L * 1800)
                    .OrderBy(candidate => candidate.Distance).ThenBy(candidate => candidate.Cell).Take(8).ToArray();
                if (candidates.Length == 0)
                {
                    continue;
                }

                var before = MeasureLocalQueueAccess(follower, -1, -1, 0);
                foreach (var candidate in candidates)
                {
                    var after = MeasureLocalQueueAccess(follower, yielding.Agent.Id, candidate.Cell, yielding.Agent.Radius);
                    if (after.Holding <= before.Holding && after.Progress < before.Progress + 2 * grid.CellSize)
                    {
                        continue;
                    }

                    var plan = FindVacancyPath(yielding, start, candidate.Cell);
                    if (plan == null)
                    {
                        continue;
                    }

                    AcceptPlan(yielding, plan);
                    UpdatePosition(yielding, CurrentTick);
                    follower.QueueRetryTick = CurrentTick;
                    RoadSideYields++;
                    ReplanCount++;
                    return;
                }
            }
        }

        private (int Progress, int Holding) MeasureLocalQueueAccess(Occupant follower, int released, int pocket, int pocketRadius)
        {
            const int localDistance = 3000;
            int radius = follower.Agent.Radius;
            var origin = follower.Agent.Position;
            var nearbyGoals = occupants.Values.Where(state => state.Agent.Id != follower.Agent.Id && state.Agent.Id != released
                && NavigationGrid.DistanceSquared(origin, grid.Center(state.Agent.GoalCell))
                    <= (long)(localDistance + radius + state.Agent.Radius) * (localDistance + radius + state.Agent.Radius))
                .Select(state => (Cell: state.Agent.GoalCell, Radius: state.Agent.Radius)).ToList();
            if (pocket >= 0)
            {
                nearbyGoals.Add((pocket, pocketRadius));
            }

            int start = follower.Agent.GoalCell;
            if (nearbyGoals.Any(goal => ConflictsWithGoal(start, radius, goal.Cell, goal.Radius)))
            {
                return (-1, 0);
            }

            int progress = grid.RoadProgress(start);
            int holding = 0;
            var seen = new HashSet<int> { start };
            var open = new Queue<int>();
            open.Enqueue(start);
            while (open.Count > 0)
            {
                int cell = open.Dequeue();
                if (grid.RoadCost(cell) <= NavigationGrid.RoadCostScale)
                {
                    progress = Math.Max(progress, grid.RoadProgress(cell));
                }

                if (CanHold(follower.Profile, cell))
                {
                    holding++;
                }

                foreach (int next in grid.Neighbours(cell))
                {
                    if (seen.Contains(next) || NavigationGrid.DistanceSquared(origin, grid.Center(next)) > localDistance * localDistance
                        || !grid.CanTraverse(cell, next, radius) || nearbyGoals.Any(goal => ConflictsWithGoal(next, radius, goal.Cell, goal.Radius)))
                    {
                        continue;
                    }

                    seen.Add(next);
                    open.Enqueue(next);
                }
            }

            return (progress, holding);
        }
    }
}
