using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    public sealed partial class NavigationCrowd
    {
        private sealed class AdmissionGateLayout
        {
            internal int Goal;
            internal int[] Owners;
            internal int[] Buffers;
        }

        private sealed class AdmissionGateFailure
        {
            internal int GoalRevision;
            internal int TrafficRevision;
            internal bool BudgetExceeded;
            internal long RetryAfter;
        }

        private readonly Dictionary<Profile, Dictionary<long, AdmissionGateFailure>> admissionGateFailures =
            new Dictionary<Profile, Dictionary<long, AdmissionGateFailure>>();
        private long admissionBudgetStart = -1;
        private bool admissionBudgetExceeded;
        public long AdmissionGateExpandedCells { get; private set; }
        public int AdmissionGateBudgetFailures { get; private set; }
        public int CachedAdmissionGateRejections { get; private set; }
        public int AdmissionGateRelocations { get; private set; }

        public int AdmissionGateRecoveries { get; private set; }
        public int AdmissionGateYieldedActors { get; private set; }

        /// <summary>A pending request can open several gates without creating a hidden actor or moving the clock.</summary>
        private bool TryAdmitThroughGates(int id, Position2 spawn, Profile profile, int start, int speed)
        {
            admissionBudgetExceeded = false;
            if (!SettledForRecovery())
            {
                return false;
            }

            long key = ((long)start << 32) | (uint)speed;
            if (!admissionGateFailures.TryGetValue(profile, out var failures))
            {
                failures = new Dictionary<long, AdmissionGateFailure>();
                admissionGateFailures.Add(profile, failures);
            }

            if (failures.TryGetValue(key, out var previousFailure) && previousFailure.GoalRevision == goalRevision
                && previousFailure.TrafficRevision == trafficRevision && CurrentTick < previousFailure.RetryAfter)
            {
                CachedAdmissionGateRejections++;
                admissionBudgetExceeded = previousFailure.BudgetExceeded;
                if (admissionBudgetExceeded)
                {
                    LastFailure = NavigationFailure.SearchLimitExceeded;
                }

                return false;
            }

            admissionBudgetStart = TotalExpandedNodes;
            bool accepted = false;
            try
            {
                accepted = TryAdmitThroughGatesWithinBudget(id, spawn, profile, start, speed);
                return accepted;
            }
            finally
            {
                if (admissionBudgetExceeded)
                {
                    AdmissionGateBudgetFailures++;
                }

                if (!accepted && !RecoveryActive)
                {
                    failures[key] = new AdmissionGateFailure
                    {
                        GoalRevision = goalRevision,
                        TrafficRevision = trafficRevision,
                        BudgetExceeded = admissionBudgetExceeded,
                        RetryAfter = admissionBudgetExceeded ? CurrentTick + 64 : Forever
                    };
                }

                admissionBudgetStart = -1;
            }
        }

        private bool AdmissionBudgetAvailable()
        {
            if (admissionBudgetStart >= 0 && TotalExpandedNodes - admissionBudgetStart >= searchLimit)
            {
                admissionBudgetExceeded = true;
                LastFailure = NavigationFailure.SearchLimitExceeded;
                return false;
            }

            return !admissionBudgetExceeded || admissionBudgetStart < 0;
        }

        private bool SpendAdmissionCell()
        {
            if (!AdmissionBudgetAvailable())
            {
                return false;
            }

            LastExpandedNodes++;
            TotalExpandedNodes++;
            AdmissionGateExpandedCells++;
            return true;
        }

        private bool TryAdmitThroughGatesWithinBudget(
            int id,
            Position2 spawn,
            Profile profile,
            int start,
            int speed,
            Occupant requester = null,
            int[] relocationGoals = null)
        {
            if (!SettledForRecovery())
            {
                return false;
            }

            var available = relocationGoals ?? Enumerable.Range(0, grid.CellCount)
                .Where(cell => CanHold(profile, cell) && !goalOwners.ContainsKey(cell)).ToArray();
            if (available.Length == 0)
            {
                if (requester == null)
                {
                    TryRelocateCompatibleGoal(profile);
                }

                return false;
            }

            var direct = Flood(profile, start, requester == null ? -1 : id, -1, true);
            if (!AdmissionBudgetAvailable())
            {
                return false;
            }

            if (available.Any(cell => direct[cell]))
            {
                if (requester != null)
                {
                    return TryDirectGoalRelocation(requester, start, available.Where(cell => direct[cell]));
                }

                return false;
            }

            var layout = BuildAdmissionGateLayout(profile, start, available);
            if (layout == null || layout.Owners.Length == 0 || !AdmissionBudgetAvailable())
            {
                return false;
            }

            var actor = new NavigationAgent { Id = id, Position = spawn, Radius = profile.Radius, AttackRange = profile.Range, Speed = speed };
            var state = requester ?? new Occupant { Agent = actor, Profile = profile };
            var original = layout.Owners.ToDictionary(owner => owner, owner => occupants[owner].Path);
            if (requester != null && !original.ContainsKey(id))
            {
                original.Add(id, requester.Path);
            }

            int originalGoalRevision = goalRevision;
            int originalTrafficRevision = trafficRevision;
            var originalTraffic = original.Keys.ToDictionary(owner => owner, owner => occupants[owner].TrafficRevision);
            var originalGoals = goalOwners.Keys.ToHashSet();
            var outbound = new Dictionary<int, List<TimedCell>>();
            bool committed = false;
            try
            {
                long departure = CurrentTick;
                var unused = layout.Buffers.ToHashSet();
                foreach (int owner in layout.Owners)
                {
                    var gate = occupants[owner];
                    List<TimedCell> chosen = null;
                    int destination = -1;
                    foreach (int buffer in unused.OrderBy(cell => NavigationGrid.DistanceSquared(grid.Center(cell), gate.Agent.Position)).ThenBy(cell => cell))
                    {
                        var plan = FindVacancyPath(gate, gate.Agent.GoalCell, buffer, departure);
                        if (!AdmissionBudgetAvailable())
                        {
                            return false;
                        }

                        if (plan != null && (chosen == null || plan[plan.Count - 1].Arrival < chosen[chosen.Count - 1].Arrival))
                        {
                            chosen = plan;
                            destination = buffer;
                        }
                    }

                    if (chosen == null)
                    {
                        return false;
                    }

                    // This body remains at its real source until its future departure, and owns that source throughout.
                    chosen[0].Arrival = CurrentTick;
                    departure = chosen[chosen.Count - 1].Arrival;
                    unused.Remove(destination);
                    outbound.Add(owner, chosen);
                    AcceptPlan(gate, chosen);
                    UpdatePosition(gate, CurrentTick);
                }

                bool stagedRequester = requester != null && outbound.ContainsKey(id);
                int passageStart = stagedRequester ? outbound[id][outbound[id].Count - 1].Cell : start;
                long passageAfter = stagedRequester ? departure : CurrentTick;
                var targets = new bool[grid.CellCount];
                targets[layout.Goal] = true;
                var regions = AccessRegions(state, passageStart);
                foreach (var region in regions)
                {
                    foreach (int claim in originalGoals)
                    {
                        region.Available[claim] = false;
                    }
                }

                LastExpandedNodes = 0;
                var passage = Search(
                    state,
                    passageStart,
                    targets,
                    regions,
                    new sbyte[grid.CellCount],
                    new Dictionary<int, List<SafeInterval>>(),
                    passageAfter);
                if (passage == null || !AdmissionBudgetAvailable())
                {
                    return false;
                }

                if (stagedRequester)
                {
                    var combined = new List<TimedCell>(outbound[id]);
                    combined.AddRange(passage.Skip(1));
                    passage = combined;
                }

                AcceptPlan(state, passage);
                long returningAfter = passage[passage.Count - 1].Arrival;
                foreach (int owner in layout.Owners.Reverse().Where(owner => requester == null || owner != id))
                {
                    var gate = occupants[owner];
                    var first = outbound[owner];
                    int buffer = first[first.Count - 1].Cell;
                    int oldGoal = original[owner][original[owner].Count - 1].Cell;
                    long begin = Math.Max(returningAfter, first[first.Count - 1].Arrival);
                    var returning = FindVacancyPath(gate, buffer, oldGoal, begin);
                    if (returning == null || !AdmissionBudgetAvailable())
                    {
                        return false;
                    }

                    var combined = new List<TimedCell>(first);
                    combined.AddRange(returning.Skip(1));
                    AcceptPlan(gate, combined);
                    UpdatePosition(gate, CurrentTick);
                    returningAfter = returning[returning.Count - 1].Arrival;
                }

                // Only here does admission become observable. Yielding bodies retain their original final goals;
                // an existing requester may adopt a compatible exclusive hole instead of blocking the queued profile.
                if (requester == null)
                {
                    occupants.Add(id, state);
                    agents.Add(actor);
                }
                else
                {
                    AdmissionGateRelocations++;
                }

                UpdatePosition(state, CurrentTick);
                vacancyAdmission = null;
                vacancyJob = new VacancyJob { GateActors = new HashSet<int>(layout.Owners.Concat(new[] { id })) };
                StartedRecoveries++;
                AdmissionGateRecoveries++;
                AdmissionGateYieldedActors += layout.Owners.Length;
                RecoveryMoveCount += layout.Owners.Length * 2;
                ReplanCount += layout.Owners.Length;
                committed = true;
                LastFailure = NavigationFailure.None;
                return true;
            }
            finally
            {
                if (!committed)
                {
                    RemoveReservations(id);
                    if (state.Path != null)
                    {
                        goalOwners.Remove(state.Agent.GoalCell);
                    }

                    RestoreGateTrial(original);
                    // Failed speculation did not change any committed goal or path. Preserve semantic revisions,
                    // including each participant's traffic marker, so failures cannot invalidate one another.
                    goalRevision = originalGoalRevision;
                    trafficRevision = originalTrafficRevision;
                    foreach (var marker in originalTraffic)
                    {
                        occupants[marker.Key].TrafficRevision = marker.Value;
                    }
                }
            }
        }

        /// <summary>
        /// A less constrained body may have taken the only slot a queued profile can use after a death.
        /// Move that existing body to a compatible exclusive hole before retrying the pending request.
        /// </summary>
        private bool TryRelocateCompatibleGoal(Profile incoming)
        {
            foreach (var actor in occupants.Values.Where(state => CanHold(incoming, state.Agent.GoalCell)
                && !ReferenceEquals(state.Profile, incoming)).OrderBy(state => CountAttackSlots(state.Profile.Radius, state.Profile.Range))
                .ThenBy(state => state.Agent.Id))
            {
                if (!AdmissionBudgetAvailable())
                {
                    return false;
                }

                var holes = Enumerable.Range(0, grid.CellCount).Where(cell => CanHold(actor.Profile, cell)
                    && !CanHold(incoming, cell) && !goalOwners.ContainsKey(cell)).ToArray();
                if (holes.Length == 0)
                {
                    continue;
                }

                if (TryAdmitThroughGatesWithinBudget(
                    actor.Agent.Id,
                    actor.Agent.Position,
                    actor.Profile,
                    actor.Agent.GoalCell,
                    actor.Agent.Speed,
                    actor,
                    holes))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryDirectGoalRelocation(Occupant actor, int start, IEnumerable<int> goals)
        {
            foreach (int goal in goals.OrderBy(cell => NavigationGrid.DistanceSquared(grid.Center(cell), actor.Agent.Position)).ThenBy(cell => cell))
            {
                var path = FindVacancyPath(actor, start, goal);
                if (!AdmissionBudgetAvailable())
                {
                    return false;
                }

                if (path == null)
                {
                    continue;
                }

                AcceptPlan(actor, path);
                UpdatePosition(actor, CurrentTick);
                vacancyAdmission = null;
                vacancyJob = new VacancyJob { GateActors = new HashSet<int> { actor.Agent.Id } };
                StartedRecoveries++;
                AdmissionGateRelocations++;
                RecoveryMoveCount++;
                ReplanCount++;
                return true;
            }

            return false;
        }

        private AdmissionGateLayout BuildAdmissionGateLayout(Profile profile, int start, int[] goals)
        {
            var occupied = occupants.Values.ToDictionary(state => state.Agent.GoalCell, state => state);
            var sameProfile = occupied.Where(pair => ReferenceEquals(pair.Value.Profile, profile)).Select(pair => pair.Key).ToHashSet();
            AdmissionGateLayout best = null;
            long bestScore = long.MaxValue;
            foreach (int goal in goals)
            {
                if (!AdmissionBudgetAvailable())
                {
                    return null;
                }

                var protectedCells = new HashSet<int>();
                var needed = new HashSet<int>();
                bool feasible = true;
                foreach (int destination in new[] { goal }.Concat(goals.Where(cell => cell != goal)))
                {
                    var hard = new HashSet<int>(sameProfile);
                    if (destination != goal)
                    {
                        hard.Add(goal);
                    }

                    var path = AdmissionSoftPath(start, destination, profile.Radius, occupied.Keys.ToHashSet(), hard);
                    if (path == null)
                    {
                        feasible = false;
                        break;
                    }

                    protectedCells.UnionWith(path);
                    foreach (int cell in path.Where(occupied.ContainsKey))
                    {
                        needed.Add(occupied[cell].Agent.Id);
                    }
                }

                if (!feasible || needed.Count == 0)
                {
                    continue;
                }

                var remaining = occupied.ToDictionary(pair => pair.Value.Agent.Id, pair => pair.Key);
                var order = new List<int>();
                while (AdmissionBudgetAvailable() && needed.Any(remaining.ContainsKey))
                {
                    bool progressed = false;
                    foreach (int owner in needed.Where(remaining.ContainsKey).OrderBy(value => value))
                    {
                        var body = occupants[owner];
                        var blocked = remaining.Where(pair => pair.Key != owner).Select(pair => pair.Value).ToHashSet();
                        var tree = AdmissionFlood(remaining[owner], body.Profile.Radius, blocked);
                        if (!tree.ContainsKey(start))
                        {
                            continue;
                        }

                        protectedCells.UnionWith(AdmissionTrace(tree, start));
                        order.Add(owner);
                        remaining.Remove(owner);
                        progressed = true;
                        break;
                    }

                    if (progressed)
                    {
                        continue;
                    }

                    int desired = needed.Where(remaining.ContainsKey).Min();
                    var bodyDesired = occupants[desired];
                    var hard = sameProfile.Where(cell => remaining.Values.Contains(cell)).ToHashSet();
                    var route = AdmissionSoftPath(remaining[desired], start, bodyDesired.Profile.Radius, remaining.Values.ToHashSet(), hard);
                    int dependency = route == null ? -1 : route.Skip(1).Where(cell => remaining.Values.Contains(cell)).DefaultIfEmpty(-1).Last();
                    if (dependency < 0 || !needed.Add(occupied[dependency].Agent.Id))
                    {
                        feasible = false;
                        break;
                    }
                }

                if (!AdmissionBudgetAvailable())
                {
                    return null;
                }

                if (!feasible || order.Count == 0)
                {
                    continue;
                }

                int radius = order.Max(owner => occupants[owner].Profile.Radius);
                var initial = occupied.Keys.ToHashSet();
                var outside = AdmissionFlood(start, radius, initial).Keys.ToHashSet();
                var whole = AdmissionFlood(start, radius, new HashSet<int>()).Keys.ToHashSet();
                var buffers = new HashSet<int>();
                foreach (int candidate in outside.Where(cell => !entries.Contains(cell) && !grid.HoldingAllowed(cell) && !protectedCells.Contains(cell))
                    .OrderBy(cell => NavigationGrid.DistanceSquared(grid.Center(cell), grid.TargetCenter)).ThenBy(cell => cell))
                {
                    if (!AdmissionBudgetAvailable())
                    {
                        return null;
                    }

                    if (buffers.Count == order.Count)
                    {
                        break;
                    }

                    if (grid.Neighbours(candidate).Any(buffers.Contains))
                    {
                        continue;
                    }

                    var next = buffers.Append(candidate).ToHashSet();
                    if (AdmissionFlood(start, radius, initial.Concat(next).ToHashSet()).Count != outside.Count - next.Count
                        || AdmissionFlood(start, radius, next).Count != whole.Count - next.Count)
                    {
                        continue;
                    }

                    buffers.Add(candidate);
                }

                long score = order.Count * 1000000L + protectedCells.Count;
                if (buffers.Count == order.Count && score < bestScore)
                {
                    best = new AdmissionGateLayout { Goal = goal, Owners = order.ToArray(), Buffers = buffers.OrderBy(cell => cell).ToArray() };
                    bestScore = score;
                }
            }

            return best;
        }

        private Dictionary<int, int> AdmissionFlood(int start, int radius, HashSet<int> blocked)
        {
            var parent = new Dictionary<int, int>();
            if (!grid.CanOccupy(start, radius))
            {
                return parent;
            }

            parent.Add(start, -1);
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                if (!SpendAdmissionCell())
                {
                    return parent;
                }

                int cell = queue.Dequeue();
                foreach (int next in grid.Neighbours(cell))
                {
                    if (!parent.ContainsKey(next) && !blocked.Contains(next) && grid.CanTraverse(cell, next, radius))
                    {
                        parent.Add(next, cell);
                        queue.Enqueue(next);
                    }
                }
            }

            return parent;
        }

        private int[] AdmissionSoftPath(int start, int goal, int radius, HashSet<int> occupied, HashSet<int> hard)
        {
            var cost = Enumerable.Repeat(long.MaxValue, grid.CellCount).ToArray();
            var parent = new Dictionary<int, int> { [start] = -1 };
            var open = new MinHeap();
            cost[start] = 0;
            open.Push(new SearchNode { Cell = start, Estimate = 0 });
            long penalty = grid.CellCount + 1L;
            while (open.Count > 0)
            {
                if (!SpendAdmissionCell())
                {
                    return null;
                }

                var node = open.Pop();
                if (node.Estimate != cost[node.Cell])
                {
                    continue;
                }

                if (node.Cell == goal)
                {
                    return AdmissionTrace(parent, goal);
                }

                foreach (int next in grid.Neighbours(node.Cell))
                {
                    if (hard.Contains(next) || !grid.CanTraverse(node.Cell, next, radius))
                    {
                        continue;
                    }

                    long candidate = node.Estimate + 1 + (occupied.Contains(next) ? penalty : 0);
                    if (candidate < cost[next])
                    {
                        cost[next] = candidate;
                        parent[next] = node.Cell;
                        open.Push(new SearchNode { Cell = next, Estimate = candidate });
                    }
                }
            }

            return null;
        }

        private static int[] AdmissionTrace(Dictionary<int, int> parent, int goal)
        {
            var result = new List<int>();
            for (int cell = goal; cell >= 0; cell = parent[cell])
            {
                result.Add(cell);
            }

            result.Reverse();
            return result.ToArray();
        }
    }
}
