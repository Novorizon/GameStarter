using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private List<TimedCell> FindStableSiegePlan(Occupant state, int start, bool allowGathering)
        {
            var attack = new bool[grid.CellCount];
            var gathering = new List<int>();
            bool anyAttack = false;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                if (entries.Contains(cell) || goalOwners.TryGetValue(cell, out int owner) && owner != state.Agent.Id)
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
            var rings = gathering.Where(cell => reachable[cell]).GroupBy(cell => SiegeWaitingRing(cell, front)).OrderBy(ring => ring.Key);
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

        private bool PreservesSiegeApproach(List<AccessRegion> regions, int goal, int owner)
        {
            foreach (var region in regions)
            {
                if (!region.Before[goal])
                {
                    continue;
                }

                var open = Enumerable.Range(0, grid.CellCount).Where(cell => cell != goal && region.Available[cell]
                    && region.Before[cell] && (region.Profile.Attack[cell] || region.Profile.Gather[cell])).ToArray();
                if (open.Length == 0)
                {
                    continue;
                }

                var remaining = Flood(region.Profile, region.Entry, owner, goal, true);
                // A genuine chokepoint must remain open to available attack positions. At saturation preserve
                // an approach to another waiting site, without setting aside every empty patch of ground.
                if (open.Any(cell => region.Profile.Attack[cell] && !remaining[cell])
                    || !region.Profile.Attack[goal] && !open.Any(cell => remaining[cell]))
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
                if (state.AttackGoal || state.TerrainRepairPending || cell < 0 || !Same(position, grid.Center(cell))
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
