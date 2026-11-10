using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private sealed class VacancyNode
        {
            internal int Hole;
            internal VacancyNode Parent;
            internal int Actor;
            internal int AttackChange;
        }

        private sealed class VacancyMove
        {
            internal int Actor;
            internal int Destination;
        }

        private sealed class VacancyJob
        {
            internal Queue<VacancyMove> Moves;
            internal int ActiveActor;
            internal Profile AdmissionProfile;
            internal int AdmissionStart;
            internal int FinalHole;
            internal HashSet<int> GateActors;
        }

        private sealed class VacancyAdmission
        {
            internal Profile Profile;
            internal int Start;
            internal int Goal;
            internal int Revision;
            internal long Expires;
        }

        private readonly Dictionary<Profile, Dictionary<int, int>> vacancyAdmissionFailures = new Dictionary<Profile, Dictionary<int, int>>();
        private VacancyJob vacancyJob;
        private VacancyAdmission vacancyAdmission;
        private int vacancyAttackFailureRevision = -1;
        private int vacancyWork;

        public bool RecoveryActive => vacancyJob != null;
        public int StartedRecoveries { get; private set; }
        public int CompletedRecoveries { get; private set; }
        public int RecoveryMoveCount { get; private set; }
        public long RecoveryExpandedCells { get; private set; }

        /// <summary>All changes use scheduled movement; a vacancy is not an absent collider or an instantaneous swap.</summary>
        private bool TryStartAdmissionRecovery(Profile profile, int start)
        {
            if (RecoveryActive)
            {
                return true;
            }

            if (!SettledForRecovery())
            {
                return false;
            }

            if (!vacancyAdmissionFailures.TryGetValue(profile, out var failures))
            {
                failures = new Dictionary<int, int>();
                vacancyAdmissionFailures.Add(profile, failures);
            }

            if (failures.TryGetValue(start, out int revision) && revision == goalRevision)
            {
                return false;
            }

            vacancyWork = 0;
            foreach (int hole in VacantGoals().OrderByDescending(cell => profile.Attack[cell]).ThenBy(cell => cell))
            {
                var terminal = FindVacancyChain(hole, profile, start, false);
                if (terminal != null)
                {
                    return BeginVacancyJob(terminal);
                }

                if (vacancyWork > searchLimit)
                {
                    LastFailure = NavigationFailure.SearchLimitExceeded;
                    return false;
                }
            }

            failures[start] = goalRevision;
            return false;
        }

        /// <summary>Give already present waiting units the attack vacancy before admitting a distant replacement.</summary>
        private bool TryStartAttackRecovery()
        {
            if (RecoveryActive)
            {
                return true;
            }

            if (!agents.Any(agent => agent.Status == NavigationAgentStatus.Waiting) || vacancyAttackFailureRevision == goalRevision)
            {
                return false;
            }

            var freeAttacks = profiles.Values.Where(profile => profile.Registered).ToDictionary(
                profile => profile,
                profile => Enumerable.Range(0, grid.CellCount).Where(cell => profile.Attack[cell]
                    && !entries.Contains(cell) && !goalOwners.ContainsKey(cell)).ToArray());
            if (freeAttacks.Values.All(cells => cells.Length == 0))
            {
                vacancyAttackFailureRevision = goalRevision;
                return false;
            }

            if (!SettledForRecovery())
            {
                return PromoteWaitingWithTraffic(freeAttacks);
            }

            if (TryStartGateRecovery())
            {
                return true;
            }

            var known = profiles.Values.Where(profile => profile.Registered).ToArray();
            vacancyWork = 0;
            foreach (int hole in VacantGoals().Where(cell => known.Any(profile => profile.Attack[cell])).OrderBy(cell => cell))
            {
                var terminal = FindVacancyChain(hole, null, -1, true);
                if (terminal != null)
                {
                    return BeginVacancyJob(terminal);
                }

                if (vacancyWork > searchLimit)
                {
                    LastFailure = NavigationFailure.SearchLimitExceeded;
                    return false;
                }
            }

            vacancyAttackFailureRevision = goalRevision;
            return false;
        }

        private bool PromoteWaitingWithTraffic(Dictionary<Profile, int[]> freeAttacks)
        {
            bool awaitingVacancy = false;
            foreach (var state in occupants.Values.OrderBy(value => value.Agent.Id))
            {
                if (state.Agent.Status != NavigationAgentStatus.Waiting)
                {
                    continue;
                }

                if (!freeAttacks.TryGetValue(state.Profile, out var attacks) || attacks.Length == 0)
                {
                    continue;
                }

                var reachable = Flood(state.Profile, state.Agent.GoalCell, state.Agent.Id, -1, false);
                bool freeAttack = attacks.Any(cell => reachable[cell]);
                if (!freeAttack)
                {
                    continue;
                }

                awaitingVacancy = true;
                if (state.PromotionRevision == goalRevision && CurrentTick < state.NextPromotionTick)
                {
                    continue;
                }

                state.PromotionRevision = goalRevision;
                state.NextPromotionTick = CurrentTick + Math.Max(4, grid.CellSize / state.Agent.Speed);
                var path = FindPlan(state, state.Agent.GoalCell, false);
                if (path == null)
                {
                    continue;
                }

                AcceptPlan(state, path);
                UpdatePosition(state, CurrentTick);
                ReplanCount++;
                return true;
            }

            // Existing paths still have a finite arrival time. Keep the vacancy available for a present waiter
            // until those paths settle and a full vacancy chain can be evaluated, rather than assigning it away.
            return awaitingVacancy;
        }

        private bool SettledForRecovery()
        {
            return agents.All(agent => agent.Status != NavigationAgentStatus.Navigating
                && Same(agent.Position, grid.Center(agent.GoalCell)));
        }

        private IEnumerable<int> VacantGoals()
        {
            var known = profiles.Values.Where(profile => profile.Registered).ToArray();
            return Enumerable.Range(0, grid.CellCount).Where(cell => !entries.Contains(cell) && !goalOwners.ContainsKey(cell)
                && known.Any(profile => profile.Attack[cell] || profile.Gather[cell]));
        }

        private VacancyNode FindVacancyChain(int initialHole, Profile admissionProfile, int start, bool fillAttack)
        {
            var open = new Queue<VacancyNode>();
            var initial = new VacancyNode { Hole = initialHole };
            var bestAttackChange = Enumerable.Repeat(int.MinValue, grid.CellCount).ToArray();
            bestAttackChange[initialHole] = 0;
            open.Enqueue(initial);
            while (open.Count > 0)
            {
                var node = open.Dequeue();
                bool attackRestored = !fillAttack || node.AttackChange > 0;
                if (attackRestored && TryVacancyEntrance(node, initialHole, admissionProfile, start, out var reachableProfile, out int entrance))
                {
                    // Store the eventual entrance as a terminal marker; no unit is created here.
                    vacancyAdmission = new VacancyAdmission { Profile = reachableProfile, Start = entrance, Goal = node.Hole };
                    return node;
                }

                foreach (var profile in profiles.Values.Where(value => value.Registered))
                {
                    if (!CanHold(profile, node.Hole))
                    {
                        continue;
                    }

                    var seen = new bool[grid.CellCount];
                    var cells = new Queue<int>();
                    seen[node.Hole] = true;
                    cells.Enqueue(node.Hole);
                    while (cells.Count > 0)
                    {
                        int cell = cells.Dequeue();
                        if (!ConsumeVacancyWork())
                        {
                            return null;
                        }

                        foreach (int next in grid.Neighbours(cell))
                        {
                            if (seen[next] || !grid.CanTraverse(cell, next, profile.Radius))
                            {
                                continue;
                            }

                            seen[next] = true;
                            if (goalOwners.TryGetValue(next, out int owner))
                            {
                                if (!occupants.TryGetValue(owner, out var actor) || !ReferenceEquals(actor.Profile, profile)
                                    || ContainsVacancy(node, next))
                                {
                                    continue;
                                }

                                int change = node.AttackChange + (profile.Attack[node.Hole] ? 1 : 0) - (actor.AttackGoal ? 1 : 0);
                                if (change <= bestAttackChange[next])
                                {
                                    continue;
                                }

                                bestAttackChange[next] = change;
                                open.Enqueue(new VacancyNode { Hole = next, Parent = node, Actor = owner, AttackChange = change });
                            }
                            else if (next != initialHole || node.Hole == initialHole)
                            {
                                cells.Enqueue(next);
                            }
                        }
                    }
                }
            }

            return null;
        }

        private bool TryVacancyEntrance(
            VacancyNode node,
            int initialHole,
            Profile requiredProfile,
            int requiredStart,
            out Profile resultProfile,
            out int resultStart)
        {
            resultProfile = null;
            resultStart = -1;
            var candidates = requiredProfile == null ? profiles.Values.Where(value => value.Registered) : new[] { requiredProfile };
            foreach (var profile in candidates)
            {
                if (!CanHold(profile, node.Hole) || node.AttackChange + (profile.Attack[node.Hole] ? 1 : 0) < 0)
                {
                    continue;
                }

                var seen = new bool[grid.CellCount];
                var cells = new Queue<int>();
                seen[node.Hole] = true;
                cells.Enqueue(node.Hole);
                while (cells.Count > 0)
                {
                    int cell = cells.Dequeue();
                    if (!ConsumeVacancyWork())
                    {
                        return false;
                    }

                    if ((requiredStart >= 0 && cell == requiredStart) || (requiredStart < 0 && entries.Contains(cell)))
                    {
                        resultProfile = profile;
                        resultStart = cell;
                        return true;
                    }

                    foreach (int next in grid.Neighbours(cell))
                    {
                        if (seen[next] || !grid.CanTraverse(cell, next, profile.Radius) || (next == initialHole && next != node.Hole))
                        {
                            continue;
                        }

                        if (next != node.Hole && goalOwners.ContainsKey(next))
                        {
                            continue;
                        }

                        seen[next] = true;
                        cells.Enqueue(next);
                    }
                }
            }

            return false;
        }

        private bool ConsumeVacancyWork()
        {
            vacancyWork++;
            RecoveryExpandedCells++;
            TotalExpandedNodes++;
            return vacancyWork <= searchLimit;
        }

        private static bool ContainsVacancy(VacancyNode node, int cell)
        {
            for (; node != null; node = node.Parent)
            {
                if (node.Hole == cell)
                {
                    return true;
                }
            }

            return false;
        }

        private bool CanHold(Profile profile, int cell)
        {
            return cell >= 0 && cell < grid.CellCount && !entries.Contains(cell) && (profile.Attack[cell] || profile.Gather[cell]);
        }

        private bool BeginVacancyJob(VacancyNode terminal)
        {
            var moves = new List<VacancyMove>();
            for (var node = terminal; node.Parent != null; node = node.Parent)
            {
                moves.Add(new VacancyMove { Actor = node.Actor, Destination = node.Parent.Hole });
            }

            moves.Reverse();
            vacancyJob = new VacancyJob
            {
                Moves = new Queue<VacancyMove>(moves),
                AdmissionProfile = vacancyAdmission.Profile,
                AdmissionStart = vacancyAdmission.Start,
                FinalHole = terminal.Hole
            };
            vacancyAdmission = null;
            StartedRecoveries++;
            AdvanceRecovery();
            return RecoveryActive || vacancyAdmission != null;
        }

        /// <summary>Called after updating actual positions. Return true to skip ordinary promotions for this tick.</summary>
        private bool AdvanceRecovery()
        {
            if (vacancyJob == null)
            {
                return false;
            }

            if (vacancyJob.GateActors != null)
            {
                if (vacancyJob.GateActors.Any(id => occupants.TryGetValue(id, out var actor)
                    && actor.Agent.Status == NavigationAgentStatus.Navigating))
                {
                    return true;
                }

                vacancyJob = null;
                CompletedRecoveries++;
                return true;
            }

            if (vacancyJob.ActiveActor != 0 && occupants.TryGetValue(vacancyJob.ActiveActor, out var active)
                && active.Agent.Status == NavigationAgentStatus.Navigating)
            {
                return true;
            }

            vacancyJob.ActiveActor = 0;
            while (vacancyJob.Moves.Count > 0)
            {
                var move = vacancyJob.Moves.Dequeue();
                if (!occupants.TryGetValue(move.Actor, out var state))
                {
                    continue;
                }

                if (!CanHold(state.Profile, move.Destination) || goalOwners.ContainsKey(move.Destination)
                    || !Same(state.Agent.Position, grid.Center(state.Agent.GoalCell)))
                {
                    vacancyJob = null;
                    vacancyAdmission = null;
                    return true;
                }

                var path = FindVacancyPath(state, state.Agent.GoalCell, move.Destination);
                if (path == null)
                {
                    vacancyJob = null;
                    vacancyAdmission = null;
                    return true;
                }

                AcceptPlan(state, path);
                UpdatePosition(state, CurrentTick);
                vacancyJob.ActiveActor = move.Actor;
                RecoveryMoveCount++;
                ReplanCount++;
                return true;
            }

            if (!goalOwners.ContainsKey(vacancyJob.FinalHole))
            {
                vacancyAdmission = new VacancyAdmission
                {
                    Profile = vacancyJob.AdmissionProfile,
                    Start = vacancyJob.AdmissionStart,
                    Goal = vacancyJob.FinalHole,
                    Revision = goalRevision,
                    Expires = CurrentTick + 64
                };
            }

            vacancyJob = null;
            CompletedRecoveries++;
            return true;
        }

        private List<TimedCell> FindVacancyPath(Occupant state, int start, int goal, long planningStart = -1)
        {
            LastExpandedNodes = 0;
            var targets = new bool[grid.CellCount];
            targets[goal] = true;
            return Search(state, start, targets, new List<AccessRegion>(), new sbyte[grid.CellCount], new Dictionary<int, List<SafeInterval>>(), planningStart);
        }

        private bool HasRecoveryAdmission(Profile profile, int start)
        {
            if (vacancyAdmission == null)
            {
                return false;
            }

            if (vacancyAdmission.Revision != goalRevision || vacancyAdmission.Expires < CurrentTick || goalOwners.ContainsKey(vacancyAdmission.Goal))
            {
                vacancyAdmission = null;
                return false;
            }

            return ReferenceEquals(vacancyAdmission.Profile, profile) && vacancyAdmission.Start == start;
        }

        private bool TryPlanRecoveryAdmission(Occupant state, int start, out List<TimedCell> path)
        {
            path = null;
            if (!HasRecoveryAdmission(state.Profile, start))
            {
                return false;
            }

            path = FindVacancyPath(state, start, vacancyAdmission.Goal);
            vacancyAdmission = null;
            return path != null;
        }
    }
}
