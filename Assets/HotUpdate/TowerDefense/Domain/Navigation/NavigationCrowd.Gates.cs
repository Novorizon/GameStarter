using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        /// <summary>
        /// Open an occupied outer gate for a more constrained attack profile, reserve passage, then restore the
        /// original attacker after that passage. All trial plans start at this tick; failed trials never move bodies.
        /// </summary>
        private bool TryStartGateRecovery()
        {
            var waiting = occupants.Values.Where(state => state.Agent.Status == NavigationAgentStatus.Waiting)
                .OrderBy(state => CountAttackSlots(state.Profile.Radius, state.Profile.Range))
                .ThenBy(state => state.Agent.ArrivalTick).ThenBy(state => state.Agent.Id).ToArray();
            var tried = new Dictionary<Profile, HashSet<int>>();
            long expandedBefore = TotalExpandedNodes;
            foreach (var requester in waiting)
            {
                if (!tried.TryGetValue(requester.Profile, out var triedBlockers))
                {
                    triedBlockers = new HashSet<int>();
                    tried.Add(requester.Profile, triedBlockers);
                }

                var reachable = Flood(requester.Profile, requester.Agent.GoalCell, requester.Agent.Id, -1, false);
                if (!Enumerable.Range(0, grid.CellCount).Any(cell => reachable[cell] && requester.Profile.Attack[cell]
                    && !goalOwners.ContainsKey(cell) && !entries.Contains(cell)))
                {
                    continue;
                }

                var blockers = occupants.Values.Where(actor => actor.AttackGoal && !ReferenceEquals(actor.Profile, requester.Profile)
                    && !requester.Profile.Attack[actor.Agent.GoalCell])
                    .OrderBy(actor => NavigationGrid.DistanceSquared(actor.Agent.Position, requester.Agent.Position))
                    .ThenBy(actor => actor.Agent.Id).ToArray();
                foreach (var blocker in blockers)
                {
                    if (!triedBlockers.Add(blocker.Agent.Id))
                    {
                        continue;
                    }

                    int gate = blocker.Agent.GoalCell;
                    var storageReachable = Flood(blocker.Profile, gate, blocker.Agent.Id, -1, true);
                    var spaces = Enumerable.Range(0, grid.CellCount).Where(cell => blocker.Profile.Gather[cell]
                        && storageReachable[cell] && !entries.Contains(cell) && !goalOwners.ContainsKey(cell))
                        .OrderBy(cell => NavigationGrid.DistanceSquared(grid.Center(cell), blocker.Agent.Position)).ThenBy(cell => cell);
                    foreach (int space in spaces)
                    {
                        if (TotalExpandedNodes - expandedBefore >= searchLimit)
                        {
                            LastFailure = NavigationFailure.SearchLimitExceeded;
                            return false;
                        }

                        var outbound = FindVacancyPath(blocker, gate, space);
                        if (outbound == null)
                        {
                            continue;
                        }

                        var original = new Dictionary<int, List<TimedCell>> { [blocker.Agent.Id] = blocker.Path };
                        AcceptPlan(blocker, outbound);
                        UpdatePosition(blocker, CurrentTick);
                        bool budgetExhausted = false;
                        foreach (var actor in waiting)
                        {
                            // The gate must remain a transit cell for this cohort, not become its permanent goal.
                            if (ReferenceEquals(actor.Profile, blocker.Profile) || actor.Profile.Attack[gate])
                            {
                                continue;
                            }

                            if (TotalExpandedNodes - expandedBefore >= searchLimit)
                            {
                                budgetExhausted = true;
                                break;
                            }

                            var passage = FindPlan(actor, actor.Agent.GoalCell, false);
                            if (passage == null)
                            {
                                continue;
                            }

                            original.Add(actor.Agent.Id, actor.Path);
                            AcceptPlan(actor, passage);
                            UpdatePosition(actor, CurrentTick);
                        }

                        budgetExhausted |= TotalExpandedNodes - expandedBefore >= searchLimit;
                        if (!budgetExhausted && original.Keys.Any(id => ReferenceEquals(occupants[id].Profile, requester.Profile)))
                        {
                            long outsideArrival = outbound[outbound.Count - 1].Arrival;
                            var returning = FindVacancyPath(blocker, space, gate, outsideArrival);
                            if (returning != null && TotalExpandedNodes - expandedBefore <= searchLimit)
                            {
                                var combined = new List<TimedCell>(outbound);
                                combined.AddRange(returning.Skip(1));
                                AcceptPlan(blocker, combined);
                                UpdatePosition(blocker, CurrentTick);
                                vacancyAdmission = null;
                                vacancyJob = new VacancyJob { GateActors = new HashSet<int>(original.Keys) };
                                StartedRecoveries++;
                                RecoveryMoveCount += original.Count;
                                ReplanCount += original.Count;
                                return true;
                            }
                        }

                        RestoreGateTrial(original);
                        if (budgetExhausted || TotalExpandedNodes - expandedBefore >= searchLimit)
                        {
                            LastFailure = NavigationFailure.SearchLimitExceeded;
                            return false;
                        }
                    }
                }
            }

            return false;
        }

        private void RestoreGateTrial(Dictionary<int, List<TimedCell>> original)
        {
            // Remove every provisional endpoint first: one trial route may use another participant's old goal.
            foreach (int id in original.Keys)
            {
                RemoveReservations(id);
                goalOwners.Remove(occupants[id].Agent.GoalCell);
            }

            foreach (var entry in original)
            {
                var actor = occupants[entry.Key];
                actor.Path = null;
                AcceptPlan(actor, entry.Value);
                UpdatePosition(actor, CurrentTick);
            }
        }
    }
}
