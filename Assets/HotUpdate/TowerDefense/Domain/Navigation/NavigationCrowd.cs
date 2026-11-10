using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Game.HotUpdate.TowerDefense.Domain.Navigation
{
    /// <summary>
    /// Prioritized cooperative paths with safe-interval search. A moving disc conservatively owns both endpoint cells
    /// for its complete transit interval. Goal occupation lasts until removal or a transactionally accepted new plan.
    /// This is deliberately not a complete solver for arbitrary multi-agent rearrangement puzzles.
    /// </summary>
    public sealed partial class NavigationCrowd
    {
        private const long Forever = long.MaxValue / 4;
        private sealed class Profile
        {
            internal bool Registered;
            internal int Radius;
            internal int Range;
            internal bool[] Stand;
            internal bool[] Attack;
            internal bool[] PreferredAttack;
            internal int HistoricalAttackQuota;
            internal bool[] Gather;
            internal bool[] Preferred;
            internal readonly Dictionary<int, int> CapacityRevisions = new Dictionary<int, int>();
        }

        private sealed class Occupant
        {
            internal NavigationAgent Agent;
            internal Profile Profile;
            internal List<TimedCell> Path;
            internal int Segment;
            internal bool AttackGoal;
            internal int PromotionRevision = -1;
            internal long NextPromotionTick;
            internal int TrafficRevision;
        }

        private sealed class TimedCell
        {
            internal int Cell;
            internal long Arrival;
            internal long Departure;
        }

        private sealed class Reservation
        {
            internal int Owner;
            internal long Start;
            internal long End;
        }

        private readonly NavigationGrid grid;
        private readonly int planningHorizon;
        private readonly int searchLimit;
        private readonly Dictionary<long, Profile> profiles = new Dictionary<long, Profile>();
        private readonly Dictionary<int, Occupant> occupants = new Dictionary<int, Occupant>();
        private readonly List<NavigationAgent> agents = new List<NavigationAgent>();
        private readonly Dictionary<int, List<Reservation>> reservations = new Dictionary<int, List<Reservation>>();
        private readonly Dictionary<int, int> goalOwners = new Dictionary<int, int>();
        private readonly HashSet<int> entries = new HashSet<int>();
        private readonly Dictionary<int, bool[]> geometryComponents = new Dictionary<int, bool[]>();
        private int goalRevision;
        private int trafficRevision;
        private bool parkingPrepared;
        public NavigationGrid Grid => grid;
        public IReadOnlyList<NavigationAgent> Agents { get; }
        public long CurrentTick { get; private set; }
        public int GatheringDistance { get; }
        public NavigationFailure LastFailure { get; private set; }
        public int LastExpandedNodes { get; private set; }
        public long TotalExpandedNodes { get; private set; }
        public int ReplanCount { get; private set; }
        public int RejectedSpawns { get; private set; }
        public int CachedAdmissionRejections { get; private set; }
        public int ReservationCount => reservations.Values.Sum(list => list.Count);

        public NavigationCrowd(NavigationGrid grid, int planningHorizon = 4096, int searchLimit = 200000, int gatheringDistance = 4000)
        {
            this.grid = grid ?? throw new ArgumentNullException(nameof(grid));
            if (planningHorizon < 4 || planningHorizon > 100000 || searchLimit < 100 || searchLimit > 5000000
                || gatheringDistance < grid.CellSize || gatheringDistance > 1000000)
            {
                throw new ArgumentOutOfRangeException(nameof(planningHorizon));
            }

            this.planningHorizon = planningHorizon;
            this.searchLimit = searchLimit;
            GatheringDistance = gatheringDistance;
            Agents = new ReadOnlyCollection<NavigationAgent>(agents);
            foreach (int cell in grid.SpawnCells)
            {
                entries.Add(cell);
            }
        }

        /// <summary>Register every wave's body/range combination before admission so goal allocation preserves their access.</summary>
        public void RegisterProfile(int radius, int attackRange)
        {
            if (agents.Count != 0)
            {
                throw new InvalidOperationException("Register wave profiles before admitting agents.");
            }

            ActivateProfile(GetProfile(radius, attackRange));
        }

        /// <summary>Individual geometrically valid candidates; not a promise that all can be safely occupied together.</summary>
        public int CountAttackSlots(int radius, int attackRange)
        {
            var profile = GetProfile(radius, attackRange);
            return Enumerable.Range(0, grid.CellCount).Count(cell => profile.Attack[cell] && !entries.Contains(cell));
        }

        /// <summary>Individual geometrically valid candidates; lanes and other profiles can lower simultaneous admission.</summary>
        public int CountGatherSlots(int radius, int attackRange)
        {
            var profile = GetProfile(radius, attackRange);
            return Enumerable.Range(0, grid.CellCount).Count(cell => profile.Gather[cell] && !entries.Contains(cell));
        }

        /// <summary>The emitter must supply an explicit cell centre. A rejected request remains outside the simulation.</summary>
        public bool TrySpawn(int id, Position2 spawn, int speed, int radius, int attackRange)
        {
            if (id <= 0 || occupants.ContainsKey(id))
            {
                throw new ArgumentException("Agent IDs must be positive and unique.", nameof(id));
            }

            if (speed <= 0 || speed > grid.CellSize)
            {
                throw new ArgumentOutOfRangeException(nameof(speed), "Speed must be 1..CellSize mm per tick.");
            }

            LastExpandedNodes = 0;
            var profile = AdmissionProfile(radius, attackRange);
            int start = grid.CellAt(spawn);
            if (start < 0 || !profile.Stand[start] || !Same(spawn, grid.Center(start)))
            {
                return Reject(NavigationFailure.InvalidSpawn);
            }

            if (agents.Count != 0 && !entries.Contains(start))
            {
                throw new InvalidOperationException("Declare every spawn cell before admitting the first agent.");
            }

            if (entries.Add(start))
            {
                parkingPrepared = false;
                profileFlexibility.Clear();
                foreach (var known in profiles.Values)
                {
                    known.CapacityRevisions.Clear();
                }
            }

            ReplanReleasedTraffic();
            if (RecoveryActive || TryStartAttackRecovery())
            {
                return Reject(NavigationFailure.ReservationBlocked);
            }

            if (IsReserved(start, CurrentTick, CurrentTick + 1, -1))
            {
                return Reject(NavigationFailure.SpawnOccupied);
            }

            if (TryAdmitThroughGates(id, spawn, profile, start, speed))
            {
                return true;
            }

            if (RecoveryActive)
            {
                return Reject(NavigationFailure.ReservationBlocked);
            }

            if (admissionBudgetExceeded)
            {
                return Reject(NavigationFailure.SearchLimitExceeded);
            }

            if (!HasRecoveryAdmission(profile, start) && profile.CapacityRevisions.TryGetValue(start, out int revision) && revision == goalRevision)
            {
                if (TryStartAdmissionRecovery(profile, start) && RecoveryActive)
                {
                    return Reject(NavigationFailure.ReservationBlocked);
                }
                if (!HasRecoveryAdmission(profile, start))
                {
                    CachedAdmissionRejections++;
                    return Reject(NavigationFailure.CapacityExceeded);
                }
            }

            var reachable = Flood(profile, start, -1, -1, false);
            if (!Enumerable.Range(0, grid.CellCount).Any(cell => profile.Attack[cell] && reachable[cell] && !entries.Contains(cell)))
            {
                return Reject(CountAttackSlots(radius, attackRange) == 0 ? NavigationFailure.NoAttackPositions : NavigationFailure.NoStaticPath);
            }

            var actor = new NavigationAgent { Id = id, Position = spawn, Radius = radius, AttackRange = attackRange, Speed = speed };
            var state = new Occupant { Agent = actor, Profile = profile };
            if (!TryPlanRecoveryAdmission(state, start, out var path))
            {
                path = FindPlan(state, start, true);
            }
            if (path == null)
            {
                if ((LastFailure == NavigationFailure.CapacityExceeded || LastFailure == NavigationFailure.GoalAccessBlocked)
                    && TryStartAdmissionRecovery(profile, start))
                {
                    if (!TryPlanRecoveryAdmission(state, start, out path))
                    {
                        return Reject(NavigationFailure.ReservationBlocked);
                    }
                }
                else
                {
                    if (LastFailure == NavigationFailure.CapacityExceeded)
                    {
                        profile.CapacityRevisions[start] = goalRevision;
                    }
                    return Reject(LastFailure);
                }
            }

            AcceptPlan(state, path);
            occupants.Add(id, state);
            agents.Add(actor);
            UpdatePosition(state, CurrentTick);
            LastFailure = NavigationFailure.None;
            return true;
        }

        public bool Remove(int id)
        {
            if (!occupants.TryGetValue(id, out var state))
            {
                return false;
            }

            RemoveReservations(id);
            goalOwners.Remove(state.Agent.GoalCell);
            occupants.Remove(id);
            agents.Remove(state.Agent);
            if (!occupants.Values.Any(other => ReferenceEquals(other.Profile, state.Profile)))
            {
                state.Profile.HistoricalAttackQuota = 0;
            }

            goalRevision++;
            trafficRevision++;
            return true;
        }

        public void Step(long tick)
        {
            if (tick != CurrentTick + 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tick), "Advance navigation exactly once per consecutive simulation tick.");
            }

            CurrentTick = tick;
            foreach (var agent in agents)
            {
                UpdatePosition(occupants[agent.Id], tick);
            }

            ReplanReleasedTraffic();
            if (!AdvanceRecovery() && !TryStartAttackRecovery())
            {
                // A revision is needed only after goal ownership changes. No frame-by-frame random shuffling or teleporting.
                foreach (var state in occupants.Values.OrderBy(value => value.Agent.Id))
                {
                    if (state.Agent.Status != NavigationAgentStatus.Waiting
                        || (state.PromotionRevision == goalRevision && tick < state.NextPromotionTick))
                    {
                        continue;
                    }

                    state.PromotionRevision = goalRevision;
                    if (!Enumerable.Range(0, grid.CellCount)
                        .Any(cell => state.Profile.Attack[cell] && !goalOwners.ContainsKey(cell) && !entries.Contains(cell)))
                    {
                        state.NextPromotionTick = Forever;
                        continue;
                    }

                    state.NextPromotionTick = tick + Math.Max(4, grid.CellSize / state.Agent.Speed);
                    var plan = FindPlan(state, state.Agent.GoalCell, false);
                    if (plan != null)
                    {
                        AcceptPlan(state, plan);
                        ReplanCount++;
                        UpdatePosition(state, tick);
                    }
                    else if (LastFailure == NavigationFailure.GoalAccessBlocked || LastFailure == NavigationFailure.CapacityExceeded)
                    {
                        state.NextPromotionTick = Forever;
                    }
                }

            }

            if ((tick & 63) == 0)
            {
                foreach (var list in reservations.Values)
                {
                    list.RemoveAll(value => value.End <= tick);
                }
            }
        }

        private Profile GetProfile(int radius, int attackRange)
        {
            if (radius <= 0 || radius > grid.CellSize / 2 || attackRange < 0 || attackRange > 1000000)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "A body must fit inside its reserved cell; attack range must be bounded.");
            }

            long key = ((long)radius << 32) | (uint)attackRange;
            if (profiles.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var profile = new Profile { Radius = radius, Range = attackRange, Stand = new bool[grid.CellCount] };
            profile.Attack = new bool[grid.CellCount];
            profile.PreferredAttack = new bool[grid.CellCount];
            profile.Gather = new bool[grid.CellCount];
            var eligible = new bool[grid.CellCount];
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                profile.Stand[cell] = grid.CanOccupy(cell, radius);
                eligible[cell] = grid.HasAttackLine(cell, radius, attackRange);
            }

            long reach = (long)grid.TargetRadius + radius + attackRange;
            long gathering = (long)grid.TargetRadius + GatheringDistance;
            for (int cell = 0; cell < grid.CellCount; cell++)
            {
                if (!profile.Stand[cell] || !grid.HoldingAllowed(cell))
                {
                    continue;
                }

                // Prefer the outward frontier without discarding legal inner attack positions.
                profile.Attack[cell] = eligible[cell];
                profile.PreferredAttack[cell] = eligible[cell] && grid.Neighbours(cell).Any(next => grid.CanTraverse(cell, next, radius)
                    && NavigationGrid.DistanceSquared(grid.Center(next), grid.TargetCenter) > reach * reach);
                profile.Gather[cell] = !eligible[cell]
                    && NavigationGrid.DistanceSquared(grid.Center(cell), grid.TargetCenter) <= gathering * gathering;
            }

            profiles.Add(key, profile);
            return profile;
        }

        private Profile AdmissionProfile(int radius, int attackRange)
        {
            var profile = GetProfile(radius, attackRange);
            if (!profile.Registered)
            {
                if (agents.Count != 0)
                {
                    throw new InvalidOperationException("Register every body/range profile before admitting the first agent.");
                }

                ActivateProfile(profile);
            }

            return profile;
        }

        private void ActivateProfile(Profile profile)
        {
            if (profile.Registered)
            {
                return;
            }

            profile.Registered = true;
            parkingPrepared = false;
            foreach (var known in profiles.Values)
            {
                known.CapacityRevisions.Clear();
            }
        }

        private bool[] Flood(Profile profile, int start, int ignoreOwner, int extraBlocked, bool includeGoals)
        {
            // Static reachability depends only on this immutable grid and body radius. Reuse the last component
            // per radius; callers only read this mask. Budgeted trials still perform and count the original flood.
            bool geometryOnly = !includeGoals && extraBlocked < 0 && admissionBudgetStart < 0;
            if (geometryOnly && start >= 0 && start < grid.CellCount
                && geometryComponents.TryGetValue(profile.Radius, out var component) && component[start])
            {
                return component;
            }

            var visited = new bool[grid.CellCount];
            if (start < 0 || !profile.Stand[start] || start == extraBlocked)
            {
                return visited;
            }

            var queue = new Queue<int>();
            visited[start] = true;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                if (admissionBudgetStart >= 0 && !SpendAdmissionCell())
                {
                    return visited;
                }

                int cell = queue.Dequeue();
                foreach (int next in grid.Neighbours(cell))
                {
                    if (visited[next] || next == extraBlocked || !profile.Stand[next] || !grid.CanTraverse(cell, next, profile.Radius))
                    {
                        continue;
                    }

                    if (includeGoals && goalOwners.TryGetValue(next, out int owner) && owner != ignoreOwner)
                    {
                        continue;
                    }

                    visited[next] = true;
                    queue.Enqueue(next);
                }
            }

            if (geometryOnly)
            {
                // One component per radius bounds retained memory, even for maps with many disconnected cells.
                geometryComponents[profile.Radius] = visited;
            }

            return visited;
        }

        private sealed class AccessRegion
        {
            internal Profile Profile;
            internal int Entry;
            internal bool[] Before;
            internal bool[] Available;
            internal List<int[]> GoalExits;
        }

        private List<AccessRegion> AccessRegions(Occupant state, int start)
        {
            var regions = new List<AccessRegion>();
            foreach (var profile in profiles.Values.Where(value => value.Registered))
            {
                var available = new bool[grid.CellCount];
                for (int cell = 0; cell < grid.CellCount; cell++)
                {
                    available[cell] = (profile.Attack[cell] || profile.Gather[cell]) && !entries.Contains(cell)
                        && (!goalOwners.TryGetValue(cell, out int owner) || owner == state.Agent.Id);
                }

                // An entrance remains an entrance even when its isolated tail contains no holding cells.
                foreach (int entry in entries)
                {
                    available[entry] = profile.Stand[entry];
                }

                if (ReferenceEquals(profile, state.Profile))
                {
                    available[start] = true;
                }

                var goalExits = new List<int[]>();
                foreach (var other in occupants.Values)
                {
                    if (!ReferenceEquals(other.Profile, profile) || other.Agent.Id == state.Agent.Id)
                    {
                        continue;
                    }

                    goalExits.Add(grid.Neighbours(other.Agent.GoalCell)
                        .Where(next => grid.CanTraverse(other.Agent.GoalCell, next, profile.Radius)
                            && (!goalOwners.TryGetValue(next, out int owner) || owner == state.Agent.Id)).ToArray());
                }

                var roots = entries.Concat(goalExits.SelectMany(value => value));
                if (ReferenceEquals(profile, state.Profile))
                {
                    roots = roots.Concat(new[] { start });
                }

                var covered = new bool[grid.CellCount];
                foreach (int entry in roots.Distinct())
                {
                    if (covered[entry] || !profile.Stand[entry])
                    {
                        continue;
                    }

                    var before = Flood(profile, entry, state.Agent.Id, -1, true);
                    for (int cell = 0; cell < grid.CellCount; cell++)
                    {
                        covered[cell] |= before[cell];
                    }

                    regions.Add(new AccessRegion { Profile = profile, Entry = entry, Before = before, Available = available, GoalExits = goalExits });
                }
            }

            return regions;
        }

        private bool GoalPreservesAccess(List<AccessRegion> regions, int goal, int owner)
        {
            foreach (var region in regions)
            {
                if (!region.Before[goal])
                {
                    continue;
                }

                var after = Flood(region.Profile, region.Entry, owner, goal, true);
                for (int cell = 0; cell < grid.CellCount; cell++)
                {
                    if (cell != goal && region.Available[cell] && region.Before[cell] && !after[cell])
                    {
                        return false;
                    }
                }

                // Experimental: packing may close an occupied endpoint's exit. Vacancies must be
                // propagated cooperatively after a death instead of reserving an empty neighbour forever.
            }

            return true;
        }

        private void AcceptPlan(Occupant state, List<TimedCell> path)
        {
            int owner = state.Agent.Id;
            RemoveReservations(owner);
            if (state.Path != null)
            {
                goalOwners.Remove(state.Agent.GoalCell);
                trafficRevision++;
            }

            state.Path = path;
            state.Segment = 0;
            var last = path[path.Count - 1];
            state.Agent.GoalCell = last.Cell;
            state.Agent.ArrivalTick = last.Arrival;
            state.AttackGoal = state.Profile.Attack[last.Cell];
            for (int i = 0; i + 1 < path.Count; i++)
            {
                var from = path[i];
                var to = path[i + 1];
                Reserve(from.Cell, owner, from.Arrival, to.Arrival);
                Reserve(to.Cell, owner, to.Departure, to.Arrival);
            }

            Reserve(last.Cell, owner, last.Arrival, Forever);
            goalOwners.Add(last.Cell, owner);
            goalRevision++;
            state.TrafficRevision = trafficRevision;
        }

        private void UpdatePosition(Occupant state, long tick)
        {
            var actor = state.Agent;
            var previous = actor.Position;
            bool wasAttacking = actor.Status == NavigationAgentStatus.Attacking;
            var path = state.Path;
            while (state.Segment + 1 < path.Count && path[state.Segment + 1].Arrival <= tick)
            {
                state.Segment++;
            }

            if (state.Segment + 1 >= path.Count)
            {
                actor.Position = grid.Center(actor.GoalCell);
                actor.Status = state.AttackGoal ? NavigationAgentStatus.Attacking : NavigationAgentStatus.Waiting;
                actor.RemainingDistance = 0;
            }
            else
            {
                var from = grid.Center(path[state.Segment].Cell);
                var next = path[state.Segment + 1];
                var to = grid.Center(next.Cell);
                long elapsed = Math.Max(0, tick - next.Departure);
                long duration = next.Arrival - next.Departure;
                actor.Position = new Position2(from.X + (int)((to.X - from.X) * elapsed / duration), from.Y + (int)((to.Y - from.Y) * elapsed / duration));
                actor.Status = NavigationAgentStatus.Navigating;
                actor.RemainingDistance = checked((path.Count - state.Segment - 2) * grid.CellSize
                    + Math.Abs(to.X - actor.Position.X) + Math.Abs(to.Y - actor.Position.Y));
            }

            if (!wasAttacking && actor.Status == NavigationAgentStatus.Attacking)
            {
                int actualAttackers = agents.Count(other => other.Radius == actor.Radius && other.AttackRange == actor.AttackRange
                    && other.Status == NavigationAgentStatus.Attacking);
                state.Profile.HistoricalAttackQuota = Math.Max(state.Profile.HistoricalAttackQuota, actualAttackers);
            }

            actor.DistanceTravelled = checked(actor.DistanceTravelled + Math.Abs(previous.X - actor.Position.X) + Math.Abs(previous.Y - actor.Position.Y));
        }

        private void Reserve(int cell, int owner, long start, long end)
        {
            if (end <= start)
            {
                return;
            }

            if (!reservations.TryGetValue(cell, out var list))
            {
                list = new List<Reservation>();
                reservations.Add(cell, list);
            }

            list.Add(new Reservation { Owner = owner, Start = start, End = end });
        }

        private bool IsReserved(int cell, long start, long end, int ignoreOwner)
        {
            return reservations.TryGetValue(cell, out var list) && list.Any(value => value.Owner != ignoreOwner && value.Start < end && start < value.End);
        }

        private void RemoveReservations(int owner)
        {
            foreach (var list in reservations.Values)
            {
                list.RemoveAll(value => value.Owner == owner);
            }
        }

        private bool Reject(NavigationFailure failure)
        {
            LastFailure = failure;
            RejectedSpawns++;
            return false;
        }

        private static bool Same(Position2 first, Position2 second) => first.X == second.X && first.Y == second.Y;
    }
}
