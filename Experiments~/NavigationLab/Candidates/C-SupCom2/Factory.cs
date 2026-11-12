using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.CSupCom2
{
    public sealed class Factory : INavigationFactory
    {
        public string Id => "C-SupCom2-FlowTiles";
        public string Description => "Public SupCom2 sector portals/merging A*/Eikonal flow tiles; project-specific safe steering and siege goals.";
        public NavigationFeatures Features => NavigationFeatures.DeathAndReinforcement | NavigationFeatures.DynamicTerrain | NavigationFeatures.MovingTarget
            | NavigationFeatures.MultipleTargets | NavigationFeatures.AssignedGoals | NavigationFeatures.LargeBodies | NavigationFeatures.FastPerTick;

        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles)
        {
            return new Navigator(world.Clone(), profiles);
        }
    }

    internal sealed class Navigator : INavigator
    {
        private sealed class Actor
        {
            internal int Id;
            internal UnitProfile Profile;
            internal Point2 Position;
            internal Point2 Previous;
            internal Point2 Velocity;
            internal Point2 ProgressAnchor;
            internal int Goal;
            internal int TargetId;
            internal bool AssignedGoal;
            internal int RequestedGoal;
            internal FlowRoute? Route;
            internal int YieldGoal = -1;
            internal int YieldRequester;
            internal long YieldUntil;
            internal int Stuck;
            internal bool Processed;
            internal bool VacancyTransfer;
            internal bool PreferClearCorridor;
            internal double BestDistance = double.PositiveInfinity;
            internal double SiegeWindowBest = double.PositiveInfinity;
        }

        private WorldDefinition world;
        private readonly UnitProfile[] profiles;
        private readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        private readonly Dictionary<int, int> owners = new Dictionary<int, int>();
        private readonly Dictionary<string, long> metrics = new Dictionary<string, long>();
        private Dictionary<int, TargetDefinition> targets;
        private readonly FlowTiles planner;
        private static readonly double[] SteeringAngles =
        {
            0, .2617993878, -.2617993878, .5235987756, -.5235987756, .7853981634, -.7853981634,
            1.0471975512, -1.0471975512, 1.5707963268, -1.5707963268
        };
        private long microstep;
        private bool completedGroupSleeping;
        private bool worldChanged;
        private int maximumClaimRadius;
        public IReadOnlyDictionary<string, long> Metrics => metrics;
        public string LastSpawnFailure { get; private set; } = "None";

        internal Navigator(WorldDefinition world, IReadOnlyList<UnitProfile> profiles)
        {
            this.world = world;
            this.profiles = profiles.Distinct().ToArray();
            targets = world.Targets.ToDictionary(target => target.Id);
            planner = new FlowTiles(world, metrics);
        }

        public bool TrySpawn(SpawnRequest request)
        {
            WakeCompletedGroup();
            if (actors.ContainsKey(request.Id) || request.Profile.RadiusMm <= 0 || request.Profile.SpeedMmPerTick <= 0)
            {
                LastSpawnFailure = "InvalidIdentityOrProfile";
                return false;
            }
            Point2 position = world.Center(request.SpawnCell);
            if (!planner.CanStand(request.SpawnCell, request.Profile.RadiusMm) || !FreePosition(position, request.Profile.RadiusMm, -1))
            {
                LastSpawnFailure = "SpawnOccupiedOrUnreachable";
                Count("SpawnBlocked");
                return false;
            }
            var actor = new Actor
            {
                Id = request.Id,
                Profile = request.Profile,
                Position = position,
                Previous = position,
                ProgressAnchor = position,
                Goal = request.AssignedGoalCell,
                AssignedGoal = request.AssignedGoalCell >= 0,
                RequestedGoal = request.AssignedGoalCell,
                TargetId = request.TargetId
            };
            var changes = new Dictionary<Actor, int>();
            if (!Assign(actor, new HashSet<int>(), changes, 0) || !CompatibleClaims(changes))
            {
                LastSpawnFailure = "NoCompatibleReachableGoal";
                Count("GoalRejected");
                return false;
            }
            foreach (var change in changes)
            {
                if (actors.ContainsKey(change.Key.Id))
                {
                    owners.Remove(change.Key.Goal);
                }
            }
            foreach (var change in changes)
            {
                if (change.Key.Goal != change.Value)
                {
                    Count("BusinessGoalReassignments");
                }
                change.Key.Goal = change.Value;
                change.Key.Route = null;
                change.Key.BestDistance = double.PositiveInfinity;
                change.Key.SiegeWindowBest = double.PositiveInfinity;
                change.Key.ProgressAnchor = change.Key.Position;
                change.Key.Stuck = 0;
                owners[change.Value] = change.Key.Id;
            }
            actors.Add(actor.Id, actor);
            maximumClaimRadius = Math.Max(maximumClaimRadius, actor.Profile.RadiusMm);
            EnsureAttackService(actor);
            LastSpawnFailure = "None";
            Count("Spawned");
            PromoteWaiting();
            return true;
        }

        public void Step()
        {
            microstep++;
            foreach (var actor in actors.Values)
            {
                if ((actor.VacancyTransfer || actor.PreferClearCorridor) && State(actor) != MotionState.Moving)
                {
                    if (actor.VacancyTransfer)
                    {
                        Count("VacancyTransfersArrived");
                    }
                    actor.VacancyTransfer = false;
                    actor.PreferClearCorridor = false;
                }
            }
            if (completedGroupSleeping)
            {
                Count("CompletedGroupSleepSteps");
                return;
            }
            if (actors.Values.All(Completed))
            {
                // Service an available attack position before accepting the whole group's stable rest state.
                PromoteWaiting();
                if (actors.Values.All(Completed))
                {
                    completedGroupSleeping = true;
                    Count("CompletedGroupSleepEntries");
                    return;
                }
            }
            Actor[] ordered = actors.Values.OrderBy(actor => actor.Id).ToArray();
            foreach (var actor in ordered)
            {
                actor.Previous = actor.Position;
                actor.Processed = false;
                if (actor.YieldGoal >= 0 && ReleaseYield(actor))
                {
                    actor.YieldGoal = -1;
                    actor.Route = null;
                    Count("YieldReturns");
                }
            }
            foreach (var actor in ordered)
            {
                int destination = actor.YieldGoal >= 0 ? actor.YieldGoal : actor.Goal;
                Point2 target = destination >= 0 ? world.Center(destination) : actor.Position;
                double remaining = Point2.Distance(actor.Position, target);
                if (remaining < .000001)
                {
                    actor.Velocity = default;
                    actor.Processed = true;
                    continue;
                }
                Point2? guide = planner.Guide(actor.Position, destination, actor.Profile.RadiusMm, ref actor.Route);
                if (!guide.HasValue)
                {
                    actor.Stuck++;
                    actor.Processed = true;
                    Count("MissingFlowDirections");
                    continue;
                }
                double maximum = actor.Profile.SpeedMmPerTick / (double)SimulationProtocol.Substeps;
                Point2 preferred = (guide.Value - actor.Position).Normalized;
                double guideDistance = Math.Min(remaining, Point2.Distance(actor.Position, guide.Value));
                Point2 next = PickMotion(actor, preferred, guide.Value, Math.Min(maximum, guideDistance), ordered);
                double moved = Point2.Distance(actor.Position, next);
                actor.Velocity = next - actor.Position;
                actor.Position = next;
                actor.Processed = true;
                double goalDistance = Point2.Distance(next, target);
                actor.BestDistance = Math.Min(actor.BestDistance, goalDistance);
                if (moved < maximum * .1)
                {
                    Count("CollisionStops");
                }
                if (actor.Stuck > 400 && microstep % 10 == 0)
                {
                    RequestYield(actor, ordered);
                }
            }
            if (microstep % 200 == 0)
            {
                foreach (var actor in ordered)
                {
                    bool nearby = InSiegeArea(actor);
                    double distance = actor.Goal >= 0 ? Point2.Distance(actor.Position, world.Center(actor.Goal)) : double.PositiveInfinity;
                    bool noGoalProgress = nearby && distance >= actor.SiegeWindowBest - actor.Profile.RadiusMm * .1;
                    actor.SiegeWindowBest = nearby ? Math.Min(actor.SiegeWindowBest, distance) : double.PositiveInfinity;
                    bool stagnant = Point2.Distance(actor.Position, actor.ProgressAnchor) < actor.Profile.RadiusMm * .5
                        || noGoalProgress && BodyBlocksGoalDirection(actor, ordered);
                    actor.Stuck = stagnant && State(actor) == MotionState.Moving ? actor.Stuck + 200 : 0;
                    actor.ProgressAnchor = actor.Position;
                }
                if (ordered.Any(actor => actor.Stuck > 200 && actor.YieldGoal < 0 && InSiegeArea(actor)))
                {
                    if (!RedistributeStalledGoals(ordered))
                    {
                        RedistributeSiegeGoals(ordered);
                    }
                }
            }
        }

        public bool Remove(int id)
        {
            WakeCompletedGroup();
            if (!actors.TryGetValue(id, out var actor))
            {
                return false;
            }
            actors.Remove(id);
            if (actor.Profile.RadiusMm == maximumClaimRadius)
            {
                maximumClaimRadius = actors.Values.Select(other => other.Profile.RadiusMm).DefaultIfEmpty(0).Max();
            }
            owners.Remove(actor.Goal);
            Count("Removed");
            if (worldChanged)
            {
                ReassignUnclaimedGoals(actors.Values.Where(other => other.Goal < 0).OrderBy(other => other.Id));
            }
            PromoteWaiting();
            return true;
        }

        public bool ApplyWorldChange(WorldChange change)
        {
            if (change == null || change.BlockCells == null || change.OpenCells == null
                || change.BlockCells.Concat(change.OpenCells).Any(cell => cell < 0 || cell >= world.CellCount)
                || change.BlockCells.Intersect(change.OpenCells).Any()
                || change.ReplaceTarget is TargetDefinition replacement && (!targets.ContainsKey(replacement.Id) || replacement.RadiusMm < 0
                    || double.IsNaN(replacement.Center.X) || double.IsInfinity(replacement.Center.X)
                    || double.IsNaN(replacement.Center.Y) || double.IsInfinity(replacement.Center.Y)))
            {
                Count("InvalidWorldChanges");
                return false;
            }

            change = new WorldChange
            {
                BlockCells = change.BlockCells.Where(world.IsWalkable).ToArray(),
                OpenCells = change.OpenCells.Where(cell => !world.IsWalkable(cell)).ToArray(),
                ReplaceTarget = change.ReplaceTarget
            };
            if (change.ReplaceTarget is TargetDefinition target && targets[target.Id].Center == target.Center && targets[target.Id].RadiusMm == target.RadiusMm)
            {
                change.ReplaceTarget = null;
            }
            if (change.BlockCells.Length == 0 && change.OpenCells.Length == 0 && !change.ReplaceTarget.HasValue)
            {
                Count("NoOpWorldChanges");
                return true;
            }

            WorldDefinition next = world.WithChange(change);
            if (actors.Values.Any(actor => !Geometry.Clear(next, actor.Position, actor.Profile.RadiusMm)))
            {
                Count("InvalidWorldChanges");
                return false;
            }
            WakeCompletedGroup();
            worldChanged = true;
            HashSet<int> dirty = planner.ChangeWorld(next, change);
            world = next;
            targets = world.Targets.ToDictionary(target => target.Id);
            Actor[] ordered = actors.Values.OrderBy(actor => actor.Id).ToArray();
            foreach (var actor in ordered)
            {
                if (actor.Goal >= 0 && (!CanHold(actor, actor.Goal) || !planner.Connected(actor.Position, actor.Goal, actor.Profile.RadiusMm)))
                {
                    owners.Remove(actor.Goal);
                    actor.Goal = -1;
                }
            }
            foreach (var actor in ordered)
            {
                int destination = actor.YieldGoal >= 0 ? actor.YieldGoal : actor.Goal;
                bool invalidYield = actor.YieldGoal >= 0 && (!planner.Connected(actor.Position, destination, actor.Profile.RadiusMm)
                    || !actors.TryGetValue(actor.YieldRequester, out var requester) || requester.Goal < 0);
                if (actor.Goal < 0 || invalidYield || change.ReplaceTarget?.Id == actor.TargetId || planner.IsDirty(actor.Position, destination, dirty)
                    || actor.Route != null && !planner.RebindRoute(actor.Route, actor.Profile.RadiusMm, dirty))
                {
                    if (actor.Route != null)
                    {
                        Count("WorldChangeRoutesInvalidated");
                    }
                    ResetGoalProgress(actor);
                }
                else if (actor.Route != null)
                {
                    Count("WorldChangeRoutesPreserved");
                }
            }
            ReassignUnclaimedGoals(ordered);
            PromoteWaiting();
            Count("WorldChanges");
            return true;
        }

        private void ReassignUnclaimedGoals(IEnumerable<Actor> ordered)
        {
            foreach (var actor in ordered)
            {
                if (actor.Goal >= 0)
                {
                    continue;
                }
                var changes = new Dictionary<Actor, int>();
                if (!Assign(actor, new HashSet<int>(), changes, 0) || !CommitGoals(changes))
                {
                    Count("InvalidatedGoalsWithoutReplacement");
                }
            }
        }

        public IReadOnlyList<UnitSnapshot> Snapshot()
        {
            return actors.Values.OrderBy(actor => actor.Id)
                .Select(actor => new UnitSnapshot(actor.Id, actor.Position, State(actor), actor.Goal, actor.TargetId)).ToArray();
        }

        public void Dispose()
        {
            actors.Clear();
            owners.Clear();
            completedGroupSleeping = false;
        }

        private void WakeCompletedGroup()
        {
            completedGroupSleeping = false;
            Count("CompletedGroupWakeCalls");
        }

        private bool Completed(Actor actor)
        {
            return !actor.VacancyTransfer && State(actor) != MotionState.Moving && CanHold(actor, actor.Goal)
                && Geometry.Clear(world, actor.Position, actor.Profile.RadiusMm);
        }

        private bool Assign(Actor actor, HashSet<int> seen, Dictionary<Actor, int> changes, int depth)
        {
            if (depth > 12)
            {
                return false;
            }
            IEnumerable<int> candidates = actor.AssignedGoal ? new[] { actor.RequestedGoal } : Enumerable.Range(0, world.CellCount);
            var ordered = candidates.Where(cell => CanHold(actor, cell) && (worldChanged ? planner.Connected(actor.Position, cell, actor.Profile.RadiusMm)
                : planner.Connected(world.CellAt(actor.Position), cell, actor.Profile.RadiusMm)))
                .OrderBy(cell => Attack(actor, world.Center(cell)) ? 0 : 1)
                .ThenBy(cell => Rarity(actor, cell))
                .ThenByDescending(cell => Point2.Distance(actor.Position, world.Center(cell)))
                .ThenBy(cell => cell).ToArray();
            foreach (int goal in ordered)
            {
                if (seen.Contains(goal) || owners.TryGetValue(goal, out int currentOwner) && currentOwner == actor.Id)
                {
                    continue;
                }
                if (!owners.ContainsKey(goal) && FitsClaim(actor, goal, changes))
                {
                    seen.Add(goal);
                    changes[actor] = goal;
                    return true;
                }
            }
            foreach (int goal in ordered)
            {
                if (!seen.Add(goal) || !owners.TryGetValue(goal, out int owner) || owner == actor.Id)
                {
                    continue;
                }
                Actor occupant = actors[owner];
                if (occupant.AssignedGoal || occupant.VacancyTransfer)
                {
                    continue;
                }
                bool cellClaimsSuffice = maximumClaimRadius <= world.CellSizeMm * .5 && actor.Profile.RadiusMm <= world.CellSizeMm * .5
                    && changes.Keys.All(other => other.Profile.RadiusMm <= world.CellSizeMm * .5);
                if (cellClaimsSuffice && Assign(occupant, seen, changes, depth + 1))
                {
                    changes[actor] = goal;
                    return true;
                }
                if (!cellClaimsSuffice)
                {
                    var branchChanges = new Dictionary<Actor, int>(changes);
                    var branchSeen = new HashSet<int>(seen);
                    if (Assign(occupant, branchSeen, branchChanges, depth + 1) && FitsClaim(actor, goal, branchChanges))
                    {
                        changes.Clear();
                        foreach (var update in branchChanges)
                        {
                            changes[update.Key] = update.Value;
                        }
                        changes[actor] = goal;
                        seen.UnionWith(branchSeen);
                        return true;
                    }
                }
            }
            return false;
        }

        private bool FitsClaim(Actor actor, int goal, IReadOnlyDictionary<Actor, int> updates)
        {
            int largest = maximumClaimRadius;
            foreach (var other in updates.Keys)
            {
                largest = Math.Max(largest, other.Profile.RadiusMm);
            }
            if ((double)actor.Profile.RadiusMm + largest <= world.CellSizeMm)
            {
                return true;
            }
            var all = actors.Values.Concat(updates.Keys.Where(other => !actors.ContainsKey(other.Id)));
            foreach (var other in all)
            {
                int otherGoal = updates.TryGetValue(other, out int replacement) ? replacement : other.Goal;
                if (other == actor || otherGoal < 0)
                {
                    continue;
                }
                if (Point2.Distance(world.Center(goal), world.Center(otherGoal)) < (double)actor.Profile.RadiusMm + other.Profile.RadiusMm - 1e-6)
                {
                    return false;
                }
            }
            return true;
        }

        private bool CompatibleClaims(IReadOnlyDictionary<Actor, int> updates)
        {
            var claimed = new HashSet<int>();
            foreach (var actor in actors.Values.Concat(updates.Keys.Where(other => !actors.ContainsKey(other.Id))))
            {
                int goal = updates.TryGetValue(actor, out int replacement) ? replacement : actor.Goal;
                if (goal >= 0 && (!claimed.Add(goal) || !FitsClaim(actor, goal, updates)))
                {
                    return false;
                }
            }
            return true;
        }

        private int Rarity(Actor actor, int cell)
        {
            bool attack = Attack(actor, world.Center(cell));
            int count = 0;
            foreach (var profile in profiles)
            {
                if (!planner.CanStand(cell, profile.RadiusMm))
                {
                    continue;
                }
                var target = targets[actor.TargetId];
                if (!attack || Point2.Distance(world.Center(cell), target.Center) <= target.RadiusMm + profile.RadiusMm + profile.AttackRangeMm + 1e-7)
                {
                    count++;
                }
            }
            return count;
        }

        private bool CanHold(Actor actor, int cell)
        {
            if (cell < 0 || !world.HoldingAllowed(cell) || !planner.CanStand(cell, actor.Profile.RadiusMm))
            {
                return false;
            }
            var target = targets[actor.TargetId];
            return Point2.Distance(world.Center(cell), target.Center) <= target.RadiusMm + world.GatheringDistanceMm + 1e-7;
        }

        private bool Attack(Actor actor, Point2 position)
        {
            var target = targets[actor.TargetId];
            return Point2.Distance(position, target.Center)
                <= target.RadiusMm + actor.Profile.RadiusMm + actor.Profile.AttackRangeMm + SimulationProtocol.GeometryToleranceMm
                && Geometry.VisibleTarget(world, position, target);
        }

        private MotionState State(Actor actor)
        {
            if (actor.Goal < 0 || actor.YieldGoal >= 0 || Point2.Distance(actor.Position, world.Center(actor.Goal)) > SimulationProtocol.GoalToleranceMm)
            {
                return MotionState.Moving;
            }
            TargetDefinition target = targets[actor.TargetId];
            if (Point2.Distance(actor.Position, target.Center) > target.RadiusMm + world.GatheringDistanceMm + SimulationProtocol.GeometryToleranceMm)
            {
                return MotionState.Moving;
            }
            return Attack(actor, actor.Position) ? MotionState.Attacking : MotionState.Waiting;
        }

        private Point2 PickMotion(Actor actor, Point2 preferred, Point2 target, double distance, Actor[] ordered)
        {
            Point2 toGoal = target - actor.Position;
            if (toGoal.Length <= distance && SafeMotion(actor, target, ordered))
            {
                return target;
            }
            Point2 separation = default;
            foreach (var other in ordered)
            {
                if (other.Id == actor.Id)
                {
                    continue;
                }
                Point2 delta = actor.Position - other.Position;
                double desired = actor.Profile.RadiusMm + other.Profile.RadiusMm + world.CellSizeMm * .6;
                if (delta.LengthSquared < desired * desired && delta.Length > 1e-6)
                {
                    separation += delta.Normalized * ((desired - delta.Length) / desired);
                }
            }
            Point2 side = new Point2(preferred.Y, -preferred.X);
            Point2 direction = (preferred + separation * 1.5 + side * (actor.Stuck > 5 ? .35 : .05)).Normalized;
            Point2 best = actor.Position;
            double bestScore = double.NegativeInfinity;
            Point2 forward = actor.Position + preferred * distance;
            if (SafeMotion(actor, forward, ordered))
            {
                if (actor.PreferClearCorridor && ClearBodyCorridor(actor, target, ordered))
                {
                    Count("ClearCorridorForwardSteps");
                    return forward;
                }
                best = forward;
                bestScore = 1 + .3 * Point2.Dot(preferred, direction);
            }
            foreach (double angle in SteeringAngles)
            {
                double cos = Math.Cos(angle);
                double sin = Math.Sin(angle);
                Point2 vector = new Point2(direction.X * cos - direction.Y * sin, direction.X * sin + direction.Y * cos);
                Point2 candidate = actor.Position + vector * distance;
                if (!SafeMotion(actor, candidate, ordered))
                {
                    continue;
                }
                double score = Point2.Dot(vector, preferred) + .3 * Point2.Dot(vector, direction);
                if (score > bestScore)
                {
                    best = candidate;
                    bestScore = score;
                }
            }
            return best;
        }

        private bool SafeMotion(Actor actor, Point2 destination, Actor[] ordered)
        {
            if (!Geometry.Segment(world, actor.Position, destination, actor.Profile.RadiusMm))
            {
                return false;
            }
            foreach (var other in ordered)
            {
                if (other.Id == actor.Id)
                {
                    continue;
                }
                double radius = actor.Profile.RadiusMm + other.Profile.RadiusMm;
                Point2 end = other.Processed ? other.Position : other.Previous;
                if (Geometry.SweptBodies(actor.Previous, destination, other.Previous, end) < radius - 1e-6)
                {
                    Count("RejectedBodyMotions");
                    return false;
                }
            }
            return true;
        }

        private bool FreePosition(Point2 position, int radius, int ignore)
        {
            if (!Geometry.Clear(world, position, radius))
            {
                return false;
            }
            return actors.Values.All(actor => actor.Id == ignore || Point2.Distance(position, actor.Position) >= radius + actor.Profile.RadiusMm - 1e-7);
        }

        private void RequestYield(Actor requester, Actor[] ordered)
        {
            if (State(requester) != MotionState.Moving)
            {
                Count("CompletedYieldRequestsSuppressed");
                return;
            }
            Point2 routeDirection = (world.Center(requester.YieldGoal >= 0 ? requester.YieldGoal : requester.Goal) - requester.Position).Normalized;
            Actor? indirectBlocker = null;
            int indirectGoal = -1;
            foreach (var blocker in ordered.OrderBy(actor => Point2.Distance(actor.Position, requester.Position)))
            {
                if (blocker.Id == requester.Id || blocker.YieldGoal >= 0)
                {
                    continue;
                }
                if (State(blocker) == MotionState.Moving && (blocker.Stuck <= 400 || blocker.Id < requester.Id))
                {
                    continue;
                }
                double radius = requester.Profile.RadiusMm + blocker.Profile.RadiusMm;
                Point2 delta = blocker.Position - requester.Position;
                if (delta.Length > radius + world.CellSizeMm || Point2.Dot(delta, routeDirection) < -requester.Profile.RadiusMm)
                {
                    continue;
                }
                int origin = world.CellAt(blocker.Position);
                var candidates = Enumerable.Range(0, world.CellCount)
                    .Where(cell => cell != blocker.Goal && !world.SpawnCells.Contains(cell) && planner.CanStand(cell, blocker.Profile.RadiusMm))
                    .Where(cell => Point2.Distance(world.Center(cell), blocker.Position) <= world.CellSizeMm * 4)
                    .Where(cell => planner.Connected(origin, cell, blocker.Profile.RadiusMm))
                    .Where(cell => FreePosition(world.Center(cell), blocker.Profile.RadiusMm, blocker.Id))
                    .Where(cell => !ordered.Any(other => other.Id != blocker.Id && other.YieldGoal == cell))
                    .Where(cell => cell != requester.Goal || CanExchangeVacancyGoals(requester, blocker, ordered))
                    .OrderByDescending(cell => Geometry.PointSegment(world.Center(cell), requester.Position, world.Center(requester.Goal)))
                    .ThenBy(cell => Point2.Distance(world.Center(cell), blocker.Position)).ThenBy(cell => cell).ToArray();
                if (candidates.Length == 0)
                {
                    continue;
                }
                int[] direct = candidates.Where(cell => ClearBodyCorridor(blocker, world.Center(cell), ordered)).ToArray();
                if (direct.Length == 0)
                {
                    if (indirectBlocker == null)
                    {
                        indirectBlocker = blocker;
                        indirectGoal = candidates[0];
                    }
                    continue;
                }
                candidates = direct;
                if (candidates[0] == requester.Goal)
                {
                    CommitVacancyExchange(requester, blocker);
                    return;
                }
                BeginYield(requester, blocker, candidates[0], false);
                return;
            }
            if (InSiegeArea(requester))
            {
                Actor? mover = ordered.Where(blocker => blocker.Id != requester.Id && CanExchangeVacancyGoals(requester, blocker, ordered))
                    .OrderBy(blocker => GoalStepsFrom(requester, blocker.Goal)).ThenBy(blocker => blocker.Id).FirstOrDefault();
                if (mover != null)
                {
                    CommitVacancyExchange(requester, mover);
                    return;
                }
            }
            if (indirectBlocker != null)
            {
                BeginYield(requester, indirectBlocker, indirectGoal, true);
            }
        }

        private void BeginYield(Actor requester, Actor blocker, int goal, bool indirect)
        {
            blocker.YieldGoal = goal;
            blocker.PreferClearCorridor = true;
            blocker.YieldRequester = requester.Id;
            blocker.YieldUntil = microstep + 1600;
            blocker.Route = null;
            blocker.BestDistance = double.PositiveInfinity;
            requester.Stuck = 0;
            Count("LocalPushRequests");
            Count(indirect ? "IndirectYieldFallbacks" : "DirectYieldRequests");
        }

        private void CommitVacancyExchange(Actor requester, Actor mover)
        {
            int vacated = mover.Goal;
            if (!CommitGoals(new Dictionary<Actor, int> { [mover] = requester.Goal, [requester] = vacated }))
            {
                return;
            }
            mover.VacancyTransfer = true;
            requester.PreferClearCorridor = true;
            mover.PreferClearCorridor = true;
            Count("PermanentVacancyGoalExchanges");
        }

        private bool CanExchangeVacancyGoals(Actor requester, Actor blocker, Actor[] ordered)
        {
            if (requester.AssignedGoal || blocker.AssignedGoal || requester.TargetId != blocker.TargetId
                || !requester.Profile.Equals(blocker.Profile) || blocker.VacancyTransfer || State(blocker) == MotionState.Moving
                || !CanHold(requester, blocker.Goal) || !CanHold(blocker, requester.Goal)
                || GoalStepsFrom(requester, blocker.Goal) >= GoalStepsFrom(requester, requester.Goal)
                || !planner.Connected(requester.Goal, blocker.Goal, requester.Profile.RadiusMm)
                || ordered.Any(other => other.Id != requester.Id && other.Id != blocker.Id && other.YieldGoal == requester.Goal)
                || !owners.TryGetValue(requester.Goal, out int requesterOwner) || requesterOwner != requester.Id
                || !owners.TryGetValue(blocker.Goal, out int blockerOwner) || blockerOwner != blocker.Id)
            {
                return false;
            }
            return ClearBodyCorridor(blocker, world.Center(requester.Goal), ordered);
        }

        private bool ClearBodyCorridor(Actor actor, Point2 destination, Actor[] ordered)
        {
            return Geometry.Segment(world, actor.Position, destination, actor.Profile.RadiusMm)
                && ordered.All(other => other.Id == actor.Id || Geometry.PointSegment(other.Position, actor.Position, destination)
                    >= actor.Profile.RadiusMm + other.Profile.RadiusMm - 1e-6);
        }

        private int GoalStepsFrom(Actor actor, int goal)
        {
            int source = world.CellAt(actor.Position);
            return Math.Abs(source % world.Width - goal % world.Width) + Math.Abs(source / world.Width - goal / world.Width);
        }

        private bool ReleaseYield(Actor blocker)
        {
            if (!actors.TryGetValue(blocker.YieldRequester, out var requester) || State(requester) != MotionState.Moving || microstep >= blocker.YieldUntil)
            {
                return true;
            }
            Point2 origin = world.Center(blocker.Goal);
            Point2 destination = world.Center(requester.Goal);
            return Point2.Distance(requester.Position, origin) > blocker.Profile.RadiusMm + requester.Profile.RadiusMm + world.CellSizeMm
                && Point2.Distance(requester.Position, destination) + world.CellSizeMm < Point2.Distance(origin, destination);
        }

        private void PromoteWaiting()
        {
            foreach (var actor in actors.Values.OrderBy(actor => actor.Id))
            {
                EnsureAttackService(actor);
            }
        }

        private void EnsureAttackService(Actor actor)
        {
            if (actor.AssignedGoal || actor.VacancyTransfer || actor.Goal < 0 || Attack(actor, world.Center(actor.Goal)))
            {
                return;
            }
            var updates = new Dictionary<Actor, int>();
            if (AugmentAttack(actor, new HashSet<int>(), updates) && CommitGoals(updates))
            {
                Count("WaitingPromotions");
                return;
            }
            updates.Clear();
            int[] requested = AttackCells(actor).ToArray();
            foreach (int goal in requested)
            {
                if (!owners.TryGetValue(goal, out int owner) || owner == actor.Id)
                {
                    continue;
                }
                Actor flexible = actors[owner];
                if (flexible.AssignedGoal || flexible.VacancyTransfer || flexible.TargetId != actor.TargetId)
                {
                    continue;
                }
                var flexibleSet = new HashSet<int>(AttackCells(flexible));
                if (flexibleSet.Count <= requested.Length || requested.Any(cell => !flexibleSet.Contains(cell)))
                {
                    continue;
                }
                int sameProfileClaims = actors.Values.Count(other => other.TargetId == actor.TargetId && other.Profile.Equals(flexible.Profile)
                    && other.Goal >= 0 && Attack(other, world.Center(other.Goal)));
                if (sameProfileClaims <= 1)
                {
                    continue;
                }
                int alternative = Enumerable.Range(0, world.CellCount)
                    .Where(cell => cell == actor.Goal || !owners.ContainsKey(cell))
                    .Where(cell => CanHold(flexible, cell) && planner.Connected(flexible.Goal, cell, flexible.Profile.RadiusMm))
                    .OrderBy(cell => Attack(flexible, world.Center(cell)) ? 0 : 1)
                    .ThenBy(cell => Point2.Distance(flexible.Position, world.Center(cell))).ThenBy(cell => cell).DefaultIfEmpty(-1).First();
                if (alternative < 0)
                {
                    continue;
                }
                updates[flexible] = alternative;
                updates[actor] = goal;
                if (CommitGoals(updates))
                {
                    Count("AttackServiceExchanges");
                    return;
                }
                updates.Clear();
            }
        }

        private bool AugmentAttack(Actor actor, HashSet<int> visited, Dictionary<Actor, int> updates)
        {
            foreach (int goal in AttackCells(actor).OrderBy(cell => Point2.Distance(actor.Position, world.Center(cell))).ThenBy(cell => cell))
            {
                if (!visited.Add(goal))
                {
                    continue;
                }
                if (!owners.TryGetValue(goal, out int owner))
                {
                    updates[actor] = goal;
                    return true;
                }
                if (owner == actor.Id || actors[owner].AssignedGoal || actors[owner].VacancyTransfer)
                {
                    continue;
                }
                if (AugmentAttack(actors[owner], visited, updates))
                {
                    updates[actor] = goal;
                    return true;
                }
            }
            return false;
        }

        private IEnumerable<int> AttackCells(Actor actor)
        {
            return Enumerable.Range(0, world.CellCount).Where(cell => CanHold(actor, cell) && Attack(actor, world.Center(cell))
                && (!worldChanged || planner.Connected(actor.Position, cell, actor.Profile.RadiusMm)));
        }

        private bool CommitGoals(Dictionary<Actor, int> updates)
        {
            if (worldChanged && updates.Any(update => !planner.Connected(update.Key.Position, update.Value, update.Key.Profile.RadiusMm)))
            {
                Count("RejectedUnreachableGoalChanges");
                return false;
            }
            if (!CompatibleClaims(updates))
            {
                Count("RejectedOverlappingGoalChanges");
                return false;
            }
            foreach (var actor in updates.Keys)
            {
                if (owners.TryGetValue(actor.Goal, out int owner) && owner == actor.Id)
                {
                    owners.Remove(actor.Goal);
                }
            }
            foreach (var update in updates)
            {
                update.Key.Goal = update.Value;
                ResetGoalProgress(update.Key);
                owners[update.Value] = update.Key.Id;
            }
            return true;
        }

        private static void ResetGoalProgress(Actor actor)
        {
            actor.Route = null;
            actor.YieldGoal = -1;
            actor.VacancyTransfer = false;
            actor.PreferClearCorridor = false;
            actor.BestDistance = double.PositiveInfinity;
            actor.Stuck = 0;
            actor.ProgressAnchor = actor.Position;
            actor.SiegeWindowBest = double.PositiveInfinity;
        }

        private void RedistributeSiegeGoals(Actor[] ordered)
        {
            // This is the experiment's siege-slot layer, not a claim about the closed game's attack AI.
            // A min-cost matching changes only destinations; every body still moves through the flow/physics pipeline.
            ordered = ordered.Where(actor => !actor.VacancyTransfer && InSiegeArea(actor)).ToArray();
            if (ordered.Length == 0 || ordered.Length > 160)
            {
                return;
            }
            var included = new HashSet<int>(ordered.Select(actor => actor.Id));
            int[] goals = Enumerable.Range(0, world.CellCount).Where(world.HoldingAllowed)
                .Where(cell => !owners.TryGetValue(cell, out int owner) || included.Contains(owner))
                .Where(cell => ordered.Any(actor => CanHold(actor, cell))).ToArray();
            int count = ordered.Length;
            if (goals.Length < count)
            {
                return;
            }
            const double impossible = 1e16;
            var costs = new double[count, goals.Length];
            for (int row = 0; row < count; row++)
            {
                Actor actor = ordered[row];
                int origin = world.CellAt(actor.Position);
                if (!planner.CanStand(origin, actor.Profile.RadiusMm))
                {
                    origin = actor.Goal;
                }
                int attackCount = AttackCells(actor).Count();
                bool settled = State(actor) != MotionState.Moving;
                var bodies = actors.Values.Where(other => other.Id != actor.Id && State(other) != MotionState.Moving)
                    .Select(other => new CrowdBody(other.Position, other.Profile.RadiusMm)).ToArray();
                double[] traffic = planner.TrafficCosts(actor.Position, actor.Profile.RadiusMm, bodies);
                for (int column = 0; column < goals.Length; column++)
                {
                    int goal = goals[column];
                    if (actor.AssignedGoal && goal != actor.RequestedGoal || !CanHold(actor, goal)
                        || !planner.Connected(origin, goal, actor.Profile.RadiusMm))
                    {
                        costs[row, column] = impossible;
                        continue;
                    }
                    double cost = traffic[goal];
                    if (double.IsInfinity(cost))
                    {
                        costs[row, column] = impossible;
                        continue;
                    }
                    if (Attack(actor, world.Center(goal)))
                    {
                        cost -= 1e12 + 1e8 / Math.Max(1, attackCount);
                    }
                    if (settled && goal != actor.Goal)
                    {
                        cost += 10000;
                    }
                    costs[row, column] = cost;
                }
            }
            int[]? matching = MinimumCostMatching(costs, impossible);
            if (matching == null)
            {
                Count("SiegeMatchingInfeasible");
                return;
            }
            var updates = new Dictionary<Actor, int>();
            for (int index = 0; index < count; index++)
            {
                int goal = goals[matching[index]];
                if (goal != ordered[index].Goal)
                {
                    updates[ordered[index]] = goal;
                }
            }
            if (updates.Count > 0)
            {
                if (CommitGoals(updates))
                {
                    Count("SiegeMatchingReassignments", updates.Count);
                }
            }
            Count("SiegeMatchingRuns");
        }

        private bool RedistributeStalledGoals(Actor[] ordered)
        {
            var eligible = ordered.Where(actor => actor.Stuck > 400 && actor.YieldGoal < 0 && actor.Goal >= 0
                && !actor.AssignedGoal && !actor.VacancyTransfer && State(actor) == MotionState.Moving && InSiegeArea(actor)).ToArray();
            if (eligible.Length < 2 || eligible.Length > 160)
            {
                return false;
            }
            var visited = new HashSet<int>();
            foreach (var seed in eligible)
            {
                if (!visited.Add(seed.Id))
                {
                    continue;
                }
                var members = new List<Actor> { seed };
                for (int index = 0; index < members.Count; index++)
                {
                    Actor current = members[index];
                    foreach (var other in eligible)
                    {
                        if (!visited.Contains(other.Id) && other.TargetId == seed.TargetId && other.Profile.Equals(seed.Profile)
                            && Point2.Distance(current.Position, other.Position) <= current.Profile.RadiusMm + other.Profile.RadiusMm + world.CellSizeMm)
                        {
                            visited.Add(other.Id);
                            members.Add(other);
                        }
                    }
                }
                if (members.Count < 2)
                {
                    continue;
                }
                var goals = members.Select(actor => actor.Goal).ToArray();
                var costs = new double[members.Count, goals.Length];
                const double impossible = 1e16;
                double oldCost = 0;
                for (int row = 0; row < members.Count; row++)
                {
                    Actor actor = members[row];
                    int origin = planner.CanStand(world.CellAt(actor.Position), actor.Profile.RadiusMm) ? world.CellAt(actor.Position) : actor.Goal;
                    var bodies = ordered.Where(other => other.Id != actor.Id && State(other) != MotionState.Moving)
                        .Select(other => new CrowdBody(other.Position, other.Profile.RadiusMm)).ToArray();
                    double[] traffic = planner.TrafficCosts(actor.Position, actor.Profile.RadiusMm, bodies);
                    for (int column = 0; column < goals.Length; column++)
                    {
                        int goal = goals[column];
                        if (!CanHold(actor, goal) || !planner.Connected(origin, goal, actor.Profile.RadiusMm) || double.IsInfinity(traffic[goal]))
                        {
                            costs[row, column] = impossible;
                            continue;
                        }
                        Point2 destination = world.Center(goal);
                        double cost = traffic[goal] + Point2.Distance(actor.Position, destination) * .1;
                        // This is a momentary executable-step cost, not a future collision reservation.
                        // A wall-detour goal is left to the ordinary static path cost rather than penalized for lacking direct LOS.
                        if (Geometry.Segment(world, actor.Position, destination, actor.Profile.RadiusMm) && !CanAdvanceToward(actor, destination, ordered))
                        {
                            cost += world.CellSizeMm * 2;
                        }
                        costs[row, column] = cost;
                    }
                    oldCost += costs[row, row];
                }
                int[]? matching = MinimumCostMatching(costs, impossible);
                if (matching == null)
                {
                    continue;
                }
                double newCost = Enumerable.Range(0, members.Count).Sum(row => costs[row, matching[row]]);
                if (newCost >= oldCost - 1e-7)
                {
                    continue;
                }
                var updates = new Dictionary<Actor, int>();
                for (int row = 0; row < members.Count; row++)
                {
                    if (members[row].Goal != goals[matching[row]])
                    {
                        updates[members[row]] = goals[matching[row]];
                    }
                }
                if (updates.Count > 0 && CommitGoals(updates))
                {
                    Count("StalledClusterReassignments", updates.Count);
                    Count("StalledClusterRecoveries");
                    return true;
                }
            }
            return false;
        }

        private bool CanAdvanceToward(Actor actor, Point2 destination, Actor[] ordered)
        {
            Point2 offset = destination - actor.Position;
            double distance = Math.Min(offset.Length, actor.Profile.SpeedMmPerTick / (double)SimulationProtocol.Substeps);
            return ClearBodyCorridor(actor, actor.Position + offset.Normalized * distance, ordered);
        }

        private bool InSiegeArea(Actor actor)
        {
            TargetDefinition target = targets[actor.TargetId];
            if (Point2.Distance(actor.Position, target.Center) > target.RadiusMm + world.GatheringDistanceMm + world.CellSizeMm * 2)
            {
                return false;
            }
            return planner.NearHolding(actor.Position, actor.Profile.RadiusMm, 4);
        }

        private bool BodyBlocksGoalDirection(Actor actor, Actor[] ordered)
        {
            if (actor.Goal < 0)
            {
                return false;
            }
            Point2 direction = (world.Center(actor.Goal) - actor.Position).Normalized;
            Point2 end = actor.Position + direction * Math.Max(world.CellSizeMm, actor.Profile.RadiusMm * 4);
            foreach (var other in ordered)
            {
                if (other.Id != actor.Id && Geometry.PointSegment(other.Position, actor.Position, end)
                    < actor.Profile.RadiusMm + other.Profile.RadiusMm + actor.Profile.RadiusMm * .1)
                {
                    return true;
                }
            }
            return false;
        }

        private static int[]? MinimumCostMatching(double[,] costs, double impossible)
        {
            int rows = costs.GetLength(0);
            int columns = costs.GetLength(1);
            var u = new double[rows + 1];
            var v = new double[columns + 1];
            var p = new int[columns + 1];
            var way = new int[columns + 1];
            for (int row = 1; row <= rows; row++)
            {
                p[0] = row;
                int column = 0;
                var minimum = Enumerable.Repeat(impossible, columns + 1).ToArray();
                var used = new bool[columns + 1];
                do
                {
                    used[column] = true;
                    int active = p[column];
                    double delta = impossible;
                    int next = 0;
                    for (int candidate = 1; candidate <= columns; candidate++)
                    {
                        if (used[candidate])
                        {
                            continue;
                        }
                        double reduced = costs[active - 1, candidate - 1] - u[active] - v[candidate];
                        if (reduced < minimum[candidate])
                        {
                            minimum[candidate] = reduced;
                            way[candidate] = column;
                        }
                        if (minimum[candidate] < delta)
                        {
                            delta = minimum[candidate];
                            next = candidate;
                        }
                    }
                    if (delta >= impossible * .5)
                    {
                        return null;
                    }
                    for (int candidate = 0; candidate <= columns; candidate++)
                    {
                        if (used[candidate])
                        {
                            u[p[candidate]] += delta;
                            v[candidate] -= delta;
                        }
                        else
                        {
                            minimum[candidate] -= delta;
                        }
                    }
                    column = next;
                }
                while (p[column] != 0);
                do
                {
                    int previous = way[column];
                    p[column] = p[previous];
                    column = previous;
                }
                while (column != 0);
            }
            var result = new int[rows];
            for (int column = 1; column <= columns; column++)
            {
                if (p[column] > 0)
                {
                    if (costs[p[column] - 1, column - 1] >= impossible * .5)
                    {
                        return null;
                    }
                    result[p[column] - 1] = column - 1;
                }
            }
            return result;
        }

        private void Count(string name, long amount = 1)
        {
            metrics.TryGetValue(name, out long current);
            metrics[name] = current + amount;
        }
    }
}
