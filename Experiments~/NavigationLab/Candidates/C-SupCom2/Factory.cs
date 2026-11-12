using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.CSupCom2
{
    public sealed class Factory : INavigationFactory
    {
        public string Id => "C-SupCom2-NearSiegePositions";
        public string Description => "Sector portal flow tiles with stable, approach-ranked siege positions and continuous body safety.";
        public NavigationFeatures Features => NavigationFeatures.DeathAndReinforcement | NavigationFeatures.DynamicTerrain | NavigationFeatures.MovingTarget
            | NavigationFeatures.MultipleTargets | NavigationFeatures.AssignedGoals | NavigationFeatures.LargeBodies | NavigationFeatures.FastPerTick;

        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles)
        {
            return new Navigator(world.Clone(), profiles);
        }
    }

    internal sealed class Navigator : INavigator
    {
        private const double CenterToleranceMm = .000001;

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
            internal Point2[]? RecoveryPath;
            internal Point2[]? CandidateRecoveryPath;
            internal CrowdBody[]? RecoveryBodies;
            internal Point2 RecoveryOrigin;
            internal int RecoveryWaypoint;
            internal long NextRecoveryAt;
            internal bool YieldRequested;
            internal bool PreferClearCorridor;
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
            int goal = FindUnclaimedGoal(actor);
            if (goal < 0)
            {
                LastSpawnFailure = "NoCompatibleReachableGoal";
                Count("GoalRejected");
                return false;
            }

            AssignGoal(actor, goal);
            actors.Add(actor.Id, actor);
            maximumClaimRadius = Math.Max(maximumClaimRadius, actor.Profile.RadiusMm);
            LastSpawnFailure = "None";
            Count("Spawned");
            return true;
        }

        public void Step()
        {
            microstep++;
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
                    actor.PreferClearCorridor = false;
                    Count("YieldReturns");
                }
            }
            foreach (var actor in ordered)
            {
                int destination = actor.YieldGoal >= 0 ? actor.YieldGoal : actor.Goal;
                Point2 target = destination >= 0 ? world.Center(destination) : actor.Position;
                double remaining = Point2.Distance(actor.Position, target);
                if (remaining < CenterToleranceMm)
                {
                    actor.Velocity = default;
                    actor.Processed = true;
                    continue;
                }
                Point2? guide = RecoveryGuide(actor) ?? planner.Guide(actor.Position, destination, actor.Profile.RadiusMm, ref actor.Route);
                if (!guide.HasValue)
                {
                    actor.Stuck++;
                    actor.Processed = true;
                    Count("MissingFlowDirections");
                    continue;
                }
                if (actor.RecoveryPath == null && actor.YieldGoal < 0 && actor.NextRecoveryAt <= microstep && remaining <= world.CellSizeMm * 5
                    && SettledBodyBlocks(actor, guide.Value, ordered))
                {
                    actor.NextRecoveryAt = microstep + 200;
                    if (RecoverApproach(actor, ordered))
                    {
                        guide = RecoveryGuide(actor) ?? guide;
                    }
                }
                double maximum = actor.Profile.SpeedMmPerTick / (double)SimulationProtocol.Substeps;
                Point2 preferred = (guide.Value - actor.Position).Normalized;
                double guideDistance = Math.Min(remaining, Point2.Distance(actor.Position, guide.Value));
                Point2 next = PickMotion(actor, preferred, guide.Value, Math.Min(maximum, guideDistance), ordered);
                double moved = Point2.Distance(actor.Position, next);
                actor.Velocity = next - actor.Position;
                actor.Position = next;
                actor.Processed = true;
                if (moved < maximum * .1)
                {
                    Count("CollisionStops");
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
                foreach (var actor in ordered.Where(actor => actor.Stuck >= 200 && actor.YieldGoal < 0 && actor.Goal >= 0
                    && actor.NextRecoveryAt <= microstep))
                {
                    actor.NextRecoveryAt = microstep + 200;
                    if (!RecoverApproach(actor, ordered) && actor.Stuck >= 400 && !actor.YieldRequested)
                    {
                        RequestYield(actor, ordered);
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
                if (actor.RecoveryPath != null && actor.RecoveryPath.Skip(actor.RecoveryWaypoint).Any(point => planner.IsDirty(point, destination, dirty)))
                {
                    actor.RecoveryPath = null;
                    actor.PreferClearCorridor = false;
                }
                actor.RecoveryBodies = null;
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
                int goal = FindUnclaimedGoal(actor);
                if (goal < 0)
                {
                    Count("InvalidatedGoalsWithoutReplacement");
                    continue;
                }
                AssignGoal(actor, goal);
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
            return State(actor) != MotionState.Moving && CanHold(actor, actor.Goal)
                && Geometry.Clear(world, actor.Position, actor.Profile.RadiusMm);
        }

        private int FindUnclaimedGoal(Actor actor, bool attackOnly = false)
        {
            if (actor.AssignedGoal)
            {
                int goal = actor.RequestedGoal;
                return CanHold(actor, goal) && FitsClaim(actor, goal) && planner.Connected(actor.Position, goal, actor.Profile.RadiusMm) ? goal : -1;
            }

            TargetDefinition target = targets[actor.TargetId];
            var candidates = Enumerable.Range(0, world.CellCount)
                .Where(cell => cell != actor.Goal && CanHold(actor, cell) && FitsClaim(actor, cell))
                .Select(cell => (Cell: cell, Attacks: Attack(actor, world.Center(cell))))
                .Where(candidate => !attackOnly || candidate.Attacks)
                .ToArray();
            if (candidates.Length == 0)
            {
                return -1;
            }

            CrowdBody[] claims = GetApproachBodies(actor, true);
            double[] costs = planner.ApproachCosts(actor.Position, actor.Profile.RadiusMm, claims);
            foreach (var candidate in candidates.Where(item => !double.IsPositiveInfinity(costs[item.Cell]))
                .OrderByDescending(item => item.Attacks)
                .ThenBy(item => item.Attacks ? 0 : Point2.Distance(world.Center(item.Cell), target.Center))
                .ThenBy(item => costs[item.Cell])
                .ThenBy(item => item.Attacks ? Rarity(actor, item.Cell) : 0)
                .ThenBy(item => item.Cell))
            {
                if (KeepsPassageOpen(actor, candidate.Cell, claims, costs, candidates.Select(item => item.Cell)))
                {
                    return candidate.Cell;
                }
            }
            return -1;
        }

        private bool KeepsPassageOpen(Actor actor, int candidate, CrowdBody[] claims, double[] costs, IEnumerable<int> available)
        {
            int radius = actor.Profile.RadiusMm;
            if (!planner.IsPossiblePassage(candidate, radius, costs))
            {
                return true;
            }

            var proposed = new CrowdBody(world.Center(candidate), radius);
            if (Point2.Distance(proposed.Position, actor.Position) < radius * 2 - SimulationProtocol.GeometryToleranceMm)
            {
                return true;
            }
            double[] after = planner.ApproachCosts(actor.Position, radius, claims.Append(proposed).ToArray());
            bool cutsAccess = available.Any(cell => cell != candidate && !double.IsPositiveInfinity(costs[cell])
                && double.IsPositiveInfinity(after[cell]) && Point2.Distance(world.Center(cell), proposed.Position) >= radius * 2);
            if (cutsAccess)
            {
                Count("PassageGoalsKeptClear");
            }
            return !cutsAccess;
        }

        private CrowdBody[] GetApproachBodies(Actor actor, bool includeClaims)
        {
            return actors.Values.Where(other => other != actor && (includeClaims ? other.Goal >= 0 : State(other) != MotionState.Moving))
                .Select(other => new CrowdBody(includeClaims ? world.Center(other.Goal) : other.Position, other.Profile.RadiusMm))
                .Where(body => !includeClaims || Point2.Distance(actor.Position, body.Position)
                    >= actor.Profile.RadiusMm + body.Radius - SimulationProtocol.GeometryToleranceMm)
                .ToArray();
        }

        private bool FitsClaim(Actor actor, int goal)
        {
            if (owners.TryGetValue(goal, out int owner) && owner != actor.Id)
            {
                return false;
            }
            if ((double)actor.Profile.RadiusMm + maximumClaimRadius <= world.CellSizeMm)
            {
                return true;
            }
            return actors.Values.All(other => other == actor || other.Goal < 0
                || Point2.Distance(world.Center(goal), world.Center(other.Goal)) >= (double)actor.Profile.RadiusMm + other.Profile.RadiusMm - 1e-6);
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
            if (actor.Goal < 0 || actor.YieldGoal >= 0 || Point2.Distance(actor.Position, world.Center(actor.Goal)) >= CenterToleranceMm)
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
            Point2 forward = actor.Position + preferred * distance;
            if (actor.RecoveryPath != null && SafeMotion(actor, forward, ordered))
            {
                return forward;
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
                if (blocker.Id == requester.Id || blocker.YieldGoal >= 0 || State(blocker) != MotionState.Moving)
                {
                    continue;
                }
                if (blocker.Stuck < 400 || blocker.Id < requester.Id)
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
                    .Where(cell => cell != requester.Goal)
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
                BeginYield(requester, blocker, direct[0], false);
                return;
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
            blocker.RecoveryPath = null;
            requester.YieldRequested = true;
            requester.Stuck = 0;
            Count("LocalPushRequests");
            Count(indirect ? "IndirectYieldFallbacks" : "DirectYieldRequests");
        }

        private bool ClearBodyCorridor(Actor actor, Point2 destination, Actor[] ordered)
        {
            return Geometry.Segment(world, actor.Position, destination, actor.Profile.RadiusMm)
                && ordered.All(other => other.Id == actor.Id || Geometry.PointSegment(other.Position, actor.Position, destination)
                    >= actor.Profile.RadiusMm + other.Profile.RadiusMm - 1e-6);
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
                if (actor.AssignedGoal || actor.Goal < 0 || Attack(actor, world.Center(actor.Goal)))
                {
                    continue;
                }

                int goal = FindUnclaimedGoal(actor, true);
                if (goal >= 0)
                {
                    AssignGoal(actor, goal);
                    Count("WaitingPromotions");
                }
            }
        }

        private void AssignGoal(Actor actor, int goal)
        {
            if (owners.TryGetValue(actor.Goal, out int owner) && owner == actor.Id)
            {
                owners.Remove(actor.Goal);
            }
            if (actor.Goal != goal)
            {
                Count("BusinessGoalReassignments");
            }
            actor.Goal = goal;
            ResetGoalProgress(actor);
            owners[goal] = actor.Id;
        }

        private static void ResetGoalProgress(Actor actor)
        {
            actor.Route = null;
            actor.YieldGoal = -1;
            actor.RecoveryPath = null;
            actor.CandidateRecoveryPath = null;
            actor.RecoveryBodies = null;
            actor.RecoveryWaypoint = 0;
            actor.NextRecoveryAt = 0;
            actor.YieldRequested = false;
            actor.PreferClearCorridor = false;
            actor.Stuck = 0;
            actor.ProgressAnchor = actor.Position;
            actor.SiegeWindowBest = double.PositiveInfinity;
        }

        private bool RecoverApproach(Actor actor, Actor[] ordered)
        {
            if (actor.RecoveryPath != null && actor.Stuck < 400)
            {
                return true;
            }

            CrowdBody[] settled = GetApproachBodies(actor, false);
            if (actor.RecoveryBodies == null || !actor.RecoveryBodies.SequenceEqual(settled)
                || Point2.Distance(actor.Position, actor.RecoveryOrigin) >= SimulationProtocol.GoalToleranceMm)
            {
                actor.CandidateRecoveryPath = planner.FindApproach(actor.Position, actor.Goal, actor.Profile.RadiusMm, settled);
                actor.RecoveryOrigin = actor.Position;
                actor.RecoveryBodies = settled;
            }

            var path = actor.CandidateRecoveryPath!;
            int first = ReferenceEquals(path, actor.RecoveryPath) ? actor.RecoveryWaypoint : 0;
            for (int index = path.Length - 1; index >= first; index--)
            {
                Point2 waypoint = path[index];
                if (Point2.Distance(actor.Position, waypoint) <= CenterToleranceMm || !ClearBodyCorridor(actor, waypoint, ordered))
                {
                    continue;
                }
                if (ReferenceEquals(path, actor.RecoveryPath) && index == actor.RecoveryWaypoint)
                {
                    return false;
                }
                actor.RecoveryPath = path;
                actor.RecoveryWaypoint = index;
                actor.PreferClearCorridor = true;
                actor.Stuck = 0;
                Count("SameGoalApproachRecoveries");
                return true;
            }
            return false;
        }

        private bool SettledBodyBlocks(Actor actor, Point2 waypoint, Actor[] ordered)
        {
            foreach (var other in ordered)
            {
                if (other.Id != actor.Id && State(other) != MotionState.Moving
                    && Geometry.PointSegment(other.Position, actor.Position, waypoint) < actor.Profile.RadiusMm + other.Profile.RadiusMm - 1e-7)
                {
                    return true;
                }
            }
            return false;
        }

        private static Point2? RecoveryGuide(Actor actor)
        {
            if (actor.RecoveryPath == null)
            {
                return null;
            }
            var path = actor.RecoveryPath;
            while (actor.RecoveryWaypoint < path.Length
                && Point2.Distance(actor.Position, path[actor.RecoveryWaypoint]) < CenterToleranceMm)
            {
                actor.RecoveryWaypoint++;
            }
            if (actor.RecoveryWaypoint < path.Length)
            {
                return path[actor.RecoveryWaypoint];
            }
            actor.RecoveryPath = null;
            actor.PreferClearCorridor = false;
            return null;
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

        private void Count(string name, long amount = 1)
        {
            metrics.TryGetValue(name, out long current);
            metrics[name] = current + amount;
        }
    }
}
