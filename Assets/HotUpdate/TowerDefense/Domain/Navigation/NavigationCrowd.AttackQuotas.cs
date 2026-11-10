using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private readonly Dictionary<Profile, Dictionary<Profile, bool>> profileFlexibility = new Dictionary<Profile, Dictionary<Profile, bool>>();
        private int quotaFailureRevision = -1;
        private long quotaRetryTick;
        public int AttackQuotaExchanges { get; private set; }
        public int CoordinatedAttackPromotions { get; private set; }

        /// <summary>
        /// Preserve a profile's established actual attack share, bounded by its current live demand. A new profile
        /// receives one opportunity. Ordinary empty attack positions remain available regardless of this lower bound.
        /// </summary>
        private bool TryCoordinateWaitingAttacks()
        {
            if (!SettledForRecovery() || quotaFailureRevision == goalRevision && CurrentTick < quotaRetryTick)
            {
                return false;
            }

            var live = occupants.Values.GroupBy(actor => actor.Profile).ToDictionary(group => group.Key, group => group.Count());
            var attacking = occupants.Values.Where(actor => actor.Agent.Status == NavigationAgentStatus.Attacking)
                .GroupBy(actor => actor.Profile).ToDictionary(group => group.Key, group => group.Count());
            var freeAttacks = live.Keys.ToDictionary(profile => profile, profile => Enumerable.Range(0, grid.CellCount)
                .Where(cell => profile.Attack[cell] && !entries.Contains(cell) && !goalOwners.ContainsKey(cell)).ToArray());
            var waiting = occupants.Values.Where(actor => actor.Agent.Status == NavigationAgentStatus.Waiting
                && (freeAttacks[actor.Profile].Length > 0 || attacking.GetValueOrDefault(actor.Profile)
                    < Math.Min(live[actor.Profile], Math.Max(1, actor.Profile.HistoricalAttackQuota))))
                .OrderBy(actor => CountAttackSlots(actor.Profile.Radius, actor.Profile.Range))
                .ThenBy(actor => actor.Agent.ArrivalTick).ThenBy(actor => actor.Agent.Id).ToArray();
            if (waiting.Length == 0)
            {
                quotaFailureRevision = goalRevision;
                quotaRetryTick = Forever;
                return false;
            }

            admissionBudgetStart = TotalExpandedNodes;
            admissionBudgetExceeded = false;
            try
            {
                foreach (var requester in waiting)
                {
                    // A real empty attack cell may still be behind several occupied layers. Try one complete
                    // executable target transaction at a time instead of exhausting the old single-gate fallback.
                    foreach (int hole in freeAttacks[requester.Profile].OrderByDescending(cell => requester.Profile.PreferredAttack[cell])
                        .ThenBy(cell => NavigationGrid.DistanceSquared(grid.Center(cell), requester.Agent.Position)).ThenBy(cell => cell))
                    {
                        if (!AdmissionBudgetAvailable())
                        {
                            return false;
                        }

                        if (TryAdmitThroughGatesWithinBudget(
                            requester.Agent.Id,
                            requester.Agent.Position,
                            requester.Profile,
                            requester.Agent.GoalCell,
                            requester.Agent.Speed,
                            requester,
                            new[] { hole },
                            -1,
                            true))
                        {
                            CoordinatedAttackPromotions++;
                            return true;
                        }
                    }

                    bool quotaMissing = attacking.GetValueOrDefault(requester.Profile)
                        < Math.Min(live[requester.Profile], Math.Max(1, requester.Profile.HistoricalAttackQuota));
                    if (!quotaMissing)
                    {
                        continue;
                    }

                    var owners = occupants.Values.Where(owner => owner.AttackGoal && requester.Profile.Attack[owner.Agent.GoalCell]
                        && StrictlyMoreFlexible(owner.Profile, requester.Profile))
                        .OrderBy(owner => NavigationGrid.DistanceSquared(owner.Agent.Position, requester.Agent.Position))
                        .ThenBy(owner => owner.Agent.Id).ToArray();
                    foreach (var owner in owners)
                    {
                        if (!AdmissionBudgetAvailable())
                        {
                            return false;
                        }

                        if (TryAdmitThroughGatesWithinBudget(
                            requester.Agent.Id,
                            requester.Agent.Position,
                            requester.Profile,
                            requester.Agent.GoalCell,
                            requester.Agent.Speed,
                            requester,
                            new[] { owner.Agent.GoalCell },
                            owner.Agent.Id))
                        {
                            AttackQuotaExchanges++;
                            return true;
                        }
                    }
                }

                return false;
            }
            finally
            {
                if (admissionBudgetExceeded)
                {
                    AdmissionGateBudgetFailures++;
                }

                if (!RecoveryActive)
                {
                    quotaFailureRevision = goalRevision;
                    quotaRetryTick = admissionBudgetExceeded ? CurrentTick + 64 : Forever;
                }

                admissionBudgetStart = -1;
            }
        }

        private bool StrictlyMoreFlexible(Profile owner, Profile requester)
        {
            if (!profileFlexibility.TryGetValue(owner, out var known))
            {
                known = new Dictionary<Profile, bool>();
                profileFlexibility.Add(owner, known);
            }

            if (known.TryGetValue(requester, out bool result))
            {
                return result;
            }

            bool extra = false;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                if (entries.Contains(cell))
                {
                    continue;
                }

                if (requester.Attack[cell] && !owner.Attack[cell])
                {
                    known[requester] = false;
                    return false;
                }

                extra |= owner.Attack[cell] && !requester.Attack[cell];
            }

            known[requester] = extra;
            return extra;
        }
    }
}
