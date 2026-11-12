using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private readonly Dictionary<(int Radius, int Owner), bool[]> goalObstaclesByRadius = new Dictionary<(int, int), bool[]>();
        private int goalObstaclesRevision = -1;

        private bool[] GoalObstacles(int radius, int ignoredOwner)
        {
            if (goalObstaclesRevision != goalRevision)
            {
                goalObstaclesByRadius.Clear();
                goalObstaclesRevision = goalRevision;
            }

            int owner = occupants.ContainsKey(ignoredOwner) ? ignoredOwner : -1;
            var key = (radius, owner);
            if (goalObstaclesByRadius.TryGetValue(key, out var mask))
            {
                return mask;
            }

            mask = new bool[grid.CellCount];
            foreach (var goal in goalOwners)
            {
                if (goal.Value == owner || !occupants.TryGetValue(goal.Value, out var other))
                {
                    continue;
                }

                int x = goal.Key % grid.Width;
                int y = goal.Key / grid.Width;
                int otherRadius = other.Agent.Radius;
                int span = (radius + otherRadius - 1) / grid.CellSize;
                for (int nearY = Math.Max(0, y - span); nearY <= Math.Min(grid.Height - 1, y + span); nearY++)
                {
                    for (int nearX = Math.Max(0, x - span); nearX <= Math.Min(grid.Width - 1, x + span); nearX++)
                    {
                        int cell = nearY * grid.Width + nearX;
                        mask[cell] |= ConflictsWithGoal(cell, radius, goal.Key, otherRadius);
                    }
                }
            }

            goalObstaclesByRadius.Add(key, mask);
            return mask;
        }

        private bool ConflictsWithGoal(int cell, int radius, int goal, int otherRadius)
        {
            if (goal < 0)
            {
                return false;
            }

            long limit = (long)radius + otherRadius;
            long dx = (long)(cell % grid.Width - goal % grid.Width) * grid.CellSize;
            long dy = (long)(cell / grid.Width - goal / grid.Width) * grid.CellSize;
            return cell == goal || otherRadius > 0 && dx * dx + dy * dy < limit * limit;
        }

        private List<TimedCell> FindStableSiegePlan(Occupant state, int start, bool allowGathering)
        {
            var attack = new bool[grid.CellCount];
            var gathering = new List<int>();
            var occupied = GoalObstacles(state.Agent.Radius, state.Agent.Id);
            bool anyAttack = false;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                if (entries.Contains(cell) || occupied[cell])
                {
                    continue;
                }

                attack[cell] = state.Profile.Attack[cell];
                anyAttack |= attack[cell];
                if (allowGathering && state.Profile.Gather[cell])
                {
                    gathering.Add(cell);
                }
            }

            if (!anyAttack && gathering.Count == 0)
            {
                LastFailure = NavigationFailure.CapacityExceeded;
                return null;
            }

            var regions = AccessRegions(state, start);
            var safety = new sbyte[grid.CellCount];
            var intervals = new Dictionary<int, List<SafeInterval>>();
            if (anyAttack)
            {
                // All eligible positions share one search. The first legal result is the earliest attack arrival.
                var attackPlan = Search(state, start, attack, regions, safety, intervals);
                if (attackPlan != null)
                {
                    return attackPlan;
                }
            }

            var attackFailure = anyAttack ? LastFailure : NavigationFailure.GoalAccessBlocked;
            if (gathering.Count == 0)
            {
                return null;
            }

            var front = occupants.Values.Where(actor => actor.AttackGoal && !actor.TerrainRepairPending)
                .Select(actor => grid.Center(actor.Agent.GoalCell)).Distinct().ToArray();
            if (front.Length == 0)
            {
                front = Enumerable.Range(0, grid.CellCount).Where(cell => state.Profile.Attack[cell]).Select(grid.Center).ToArray();
            }

            var reachable = Flood(state.Profile, start, state.Agent.Id, -1, true);
            var profilesWithAttackSlots = profiles.Values.Where(profile => profile.Registered && !ReferenceEquals(profile, state.Profile)).ToArray();
            // Keep a real first-ring slot available to other body profiles when reachable waiting ground is available.
            var rings = gathering.Where(cell => reachable[cell]).GroupBy(cell => (
                AttackForOtherProfile: profilesWithAttackSlots.Any(profile => profile.Attack[cell]),
                Distance: SiegeWaitingRing(cell, front)))
                .OrderBy(ring => ring.Key.AttackForOtherProfile).ThenBy(ring => ring.Key.Distance);
            foreach (var ring in rings)
            {
                var waiting = new bool[grid.CellCount];
                foreach (int cell in ring)
                {
                    waiting[cell] = true;
                }

                var waitingPlan = Search(state, start, waiting, regions, safety, intervals);
                if (waitingPlan != null)
                {
                    return waitingPlan;
                }

                attackFailure = MergeFailure(attackFailure, LastFailure);
            }

            LastFailure = attackFailure;
            return null;
        }

        private int SiegeWaitingRing(int cell, Position2[] front)
        {
            var center = grid.Center(cell);
            long distance = front.Length == 0 ? NavigationGrid.DistanceSquared(center, grid.TargetCenter)
                : front.Min(position => NavigationGrid.DistanceSquared(center, position));
            return (int)Math.Ceiling(Math.Sqrt(distance) / grid.CellSize);
        }

        private bool PreservesSiegeApproach(List<AccessRegion> regions, int goal, int owner, int radius)
        {
            foreach (var region in regions)
            {
                int otherRadius = region.Profile.Radius;
                if (radius + otherRadius <= grid.CellSize && !region.Before[goal])
                {
                    continue;
                }

                var available = Enumerable.Range(0, grid.CellCount).Where(cell => region.Available[cell]
                    && region.Before[cell] && (region.Profile.Attack[cell] || region.Profile.Gather[cell])).ToArray();
                var open = available.Where(cell => !ConflictsWithGoal(cell, otherRadius, goal, radius)).ToArray();
                if (open.Length == 0)
                {
                    continue;
                }

                var remaining = Flood(region.Profile, region.Entry, owner, goal, true, radius);
                // A waiting body may cover nearby positions, but should not close a lane to a larger area beyond it.
                if (open.Any(cell => region.Profile.Attack[cell] && !remaining[cell])
                    || !region.Profile.Attack[goal] && (!open.Any(cell => remaining[cell])
                        || open.Count(cell => !remaining[cell]) > available.Length - open.Length))
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryPromoteSiegeWaiter()
        {
            var previousFailure = LastFailure;
            long gathering = (long)grid.TargetRadius + GatheringDistance;
            Occupant winner = null;
            List<TimedCell> winnerPlan = null;
            foreach (var state in occupants.Values.OrderBy(actor => actor.Agent.Id))
            {
                var position = state.Agent.Position;
                int cell = grid.CellAt(position);
                if (state.AttackGoal || state.RoadQueue || state.TerrainRepairPending || cell < 0 || !Same(position, grid.Center(cell))
                    || NavigationGrid.DistanceSquared(position, grid.TargetCenter) > gathering * gathering
                    || state.PromotionRevision == goalRevision && CurrentTick < state.NextPromotionTick)
                {
                    continue;
                }

                state.PromotionRevision = goalRevision;
                if (!Enumerable.Range(0, grid.CellCount).Any(candidate => state.Profile.Attack[candidate]
                    && !entries.Contains(candidate) && !goalOwners.ContainsKey(candidate)))
                {
                    state.NextPromotionTick = Forever;
                    continue;
                }

                state.NextPromotionTick = CurrentTick + Math.Max(4, grid.CellSize / state.Agent.Speed);
                var plan = FindPlan(state, cell, false);
                if (plan != null && (winnerPlan == null || plan[plan.Count - 1].Arrival < winnerPlan[winnerPlan.Count - 1].Arrival))
                {
                    winner = state;
                    winnerPlan = plan;
                }
            }

            LastFailure = previousFailure;
            if (winner == null)
            {
                return false;
            }

            AcceptPlan(winner, winnerPlan);
            UpdatePosition(winner, CurrentTick);
            ReplanCount++;
            return true;
        }

        private bool TryRecoverSiegeAttackAccess()
        {
            if (vacancyAttackFailureRevision == goalRevision || !agents.Any(agent => agent.Status == NavigationAgentStatus.Waiting)
                || !SettledForRecovery())
            {
                return false;
            }

            var waiting = occupants.Values.Where(state => state.Agent.Status == NavigationAgentStatus.Waiting)
                .Select(state => state.Profile).Distinct().ToArray();
            vacancyWork = 0;
            foreach (int cell in VacantGoals().Where(cell => waiting.Any(profile => profile.Attack[cell])))
            {
                var terminal = FindVacancyChain(cell, null, -1, true);
                if (terminal != null)
                {
                    return BeginVacancyJob(terminal);
                }

                if (vacancyWork > searchLimit)
                {
                    break;
                }
            }

            vacancyAttackFailureRevision = goalRevision;
            return false;
        }
    }
}
