using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.DAoe4
{
    public sealed class Aoe4Factory : INavigationFactory
    {
        public string Id => "D-aoe4-published-architecture";
        public string Description => "HPA portal route, overlapping LOS/FMM segments, restricted-size extension and sampled steering; original C# implementation.";
        public NavigationFeatures Features => NavigationFeatures.DeathAndReinforcement | NavigationFeatures.DynamicTerrain | NavigationFeatures.MovingTarget
            | NavigationFeatures.MultipleTargets | NavigationFeatures.AssignedGoals | NavigationFeatures.LargeBodies | NavigationFeatures.FastPerTick;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles)
        {
            return new AoeNavigator(world, profiles, false);
        }
    }

    public sealed class Aoe4CoordinatedFactory : INavigationFactory
    {
        public string Id => "D-aoe4-with-project-yield";
        public string Description => "D architecture plus separately identified project-level physical local yielding; not claimed as AoE IV proprietary coordination.";
        public NavigationFeatures Features => new Aoe4Factory().Features;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles)
        {
            return new AoeNavigator(world, profiles, true);
        }
    }

    internal sealed partial class AoeNavigator : INavigator
    {
        private sealed class Actor
        {
            internal SpawnRequest Request;
            internal Point2 Position;
            internal Point2 Velocity;
            internal int Goal;
            internal HierarchicalFlow.Route? Route;
            internal int RouteGoal = -1;
            internal int LastProgress;
            internal double BestGoalDistance = double.MaxValue;
            internal int YieldCell = -1;
            internal int YieldRequester;
            internal int YieldStarted;
            internal int YieldHoldStarted;
            internal int LastPromotionRevision = -1;
        }

        private readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        private readonly Dictionary<int, int> goalOwners = new Dictionary<int, int>();
        private readonly Dictionary<string, int[]> reachable = new Dictionary<string, int[]>();
        private readonly Dictionary<string, int[]> holding = new Dictionary<string, int[]>();
        private readonly Dictionary<string, int> attackPeaks = new Dictionary<string, int>();
        private readonly Dictionary<string, long> metrics = new Dictionary<string, long>();
        private readonly GridGeometry geometry;
        private readonly HierarchicalFlow planner;
        private readonly bool coordinated;
        private int step;
        private int goalRevision;
        private bool disposed;
        public IReadOnlyDictionary<string, long> Metrics => metrics;
        public string LastSpawnFailure { get; private set; } = "None";
        private WorldDefinition World => geometry.World;

        internal AoeNavigator(WorldDefinition world, IReadOnlyList<UnitProfile> profiles, bool coordinated)
        {
            geometry = new GridGeometry(world);
            this.coordinated = coordinated;
            planner = new HierarchicalFlow(geometry, metrics, profiles.Count == 0 ? 1 : profiles.Max(profile => profile.RadiusMm));
            Add("IndependentBattleInstances");
        }

        public bool TrySpawn(SpawnRequest request)
        {
            ThrowIfDisposed();
            Add("SpawnAttempts");
            if (actors.ContainsKey(request.Id))
            {
                throw new InvalidOperationException("Duplicate live ID.");
            }
            if (request.Profile.RadiusMm <= 0 || request.Profile.SpeedMmPerTick <= 0 || request.Profile.AttackRangeMm < 0
                || !World.Targets.Any(target => target.Id == request.TargetId))
            {
                LastSpawnFailure = "InvalidProfileOrTarget";
                return false;
            }
            Point2 spawn = World.Center(request.SpawnCell);
            if (!geometry.CanStand(spawn, request.Profile.RadiusMm))
            {
                LastSpawnFailure = "StaticSpawnBlocked";
                return false;
            }
            if (actors.Values.Any(other => Point2.Distance(other.Position, spawn) < other.Request.Profile.RadiusMm + request.Profile.RadiusMm + .1))
            {
                LastSpawnFailure = "SpawnOccupied";
                return false;
            }
            int[] distances = Reachability(request.SpawnCell, request.Profile.RadiusMm);
            int goal = request.AssignedGoalCell;
            if (goal >= 0)
            {
                if (goal >= World.CellCount || !geometry.CanHold(goal, request.Profile, request.TargetId) || distances[goal] < 0 || goalOwners.ContainsKey(goal) || !GoalClear(goal, request.Profile, request.Id))
                {
                    LastSpawnFailure = "AssignedGoalUnavailable";
                    return false;
                }
            }
            else
            {
                goal = PickGoal(request.Profile, request.TargetId, distances, spawn, true);
                if (goal < 0)
                {
                    goal = RelocateCompatibleOwner(request, distances);
                }
            }
            if (goal < 0)
            {
                LastSpawnFailure = "NoFreeCompatibleGoal";
                return false;
            }
            var route = planner.FindRoute(request.SpawnCell, goal, request.Profile.RadiusMm);
            if (route == null)
            {
                LastSpawnFailure = "StaticGoalUnreachable";
                return false;
            }
            var actor = new Actor { Request = request, Position = spawn, Goal = goal, Route = route, RouteGoal = goal, LastProgress = step };
            actors.Add(request.Id, actor);
            goalOwners.Add(goal, request.Id);
            goalRevision++;
            Add("Spawned");
            Add("GoalAssignments");
            LastSpawnFailure = "None";
            return true;
        }

        public void Step()
        {
            ThrowIfDisposed();
            step++;
            Add("Microsteps");
            var ordered = actors.Values.OrderBy(actor => actor.Request.Id).ToArray();
            if (step % SimulationProtocol.Substeps == 0)
            {
                TrackAttackPeaks(ordered);
                Promote(ordered);
                if (coordinated)
                {
                    UpdateYields(ordered);
                }
            }
            var desired = new Point2[ordered.Length];
            for (int i = 0; i < ordered.Length; i++)
            {
                var actor = ordered[i];
                Point2 destination = Destination(actor);
                double error = Point2.Distance(actor.Position, destination);
                if (error < actor.BestGoalDistance - .5)
                {
                    actor.BestGoalDistance = error;
                    actor.LastProgress = step;
                }
                if (error <= SimulationProtocol.GoalToleranceMm)
                {
                    desired[i] = default;
                    continue;
                }
                int goal = actor.YieldCell >= 0 ? actor.YieldCell : actor.Goal;
                if (actor.Route == null || actor.RouteGoal != goal || !planner.OnRoute(actor.Position, actor.Route))
                {
                    actor.Route = planner.FindRoute(World.CellAt(actor.Position), goal, actor.Request.Profile.RadiusMm);
                    actor.RouteGoal = goal;
                    Add("RouteRefreshes");
                }
                Point2 direction = actor.Route == null ? default : planner.Direction(actor.Position, actor.Route, out _);
                if (direction.LengthSquared < 1e-12 && step % SimulationProtocol.Substeps == 0)
                {
                    actor.Route = planner.FindRoute(World.CellAt(actor.Position), goal, actor.Request.Profile.RadiusMm);
                    direction = actor.Route == null ? default : planner.Direction(actor.Position, actor.Route, out _);
                    Add("FlowCoverageRepaths");
                }
                desired[i] = SampleVelocity(actor, direction, destination, ordered);
            }
            var proposed = ordered.Select((actor, index) => actor.Position + desired[index]).ToArray();
            // Deterministic engine collision stopping is visible in metrics; it never grants liveness by itself.
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int i = 0; i < ordered.Length; i++)
                {
                    for (int j = i + 1; j < ordered.Length; j++)
                    {
                        double radius = ordered[i].Request.Profile.RadiusMm + ordered[j].Request.Profile.RadiusMm;
                        if (GridGeometry.SweptGapSquared(ordered[i].Position, proposed[i], ordered[j].Position, proposed[j]) < radius * radius - 1e-5)
                        {
                            if (proposed[i] != ordered[i].Position || proposed[j] != ordered[j].Position)
                            {
                                proposed[i] = ordered[i].Position;
                                proposed[j] = ordered[j].Position;
                                changed = true;
                                Add("CollisionStoppedPairs");
                            }
                        }
                    }
                }
            }
            for (int i = 0; i < ordered.Length; i++)
            {
                var actor = ordered[i];
                actor.Velocity = proposed[i] - actor.Position;
                actor.Position = proposed[i];
            }
        }

        public bool Remove(int id)
        {
            ThrowIfDisposed();
            if (!actors.TryGetValue(id, out var actor))
            {
                return false;
            }
            actors.Remove(id);
            if (goalOwners.TryGetValue(actor.Goal, out int owner) && owner == id)
            {
                goalOwners.Remove(actor.Goal);
                goalRevision++;
            }
            foreach (var other in actors.Values.Where(other => other.YieldRequester == id))
            {
                EndYield(other);
            }
            string key = ProfileKey(actor.Request.Profile, actor.Request.TargetId);
            if (!actors.Values.Any(other => ProfileKey(other.Request.Profile, other.Request.TargetId) == key))
            {
                attackPeaks.Remove(key);
            }
            Add("DeathsRemoved");
            return true;
        }

        public bool ApplyWorldChange(WorldChange change)
        {
            ThrowIfDisposed();
            var updated = new GridGeometry(World.WithChange(change));
            if (actors.Values.Any(actor => !updated.CanStand(actor.Position, actor.Request.Profile.RadiusMm)))
            {
                Add("RejectedUnsafeWorldChanges");
                return false;
            }
            geometry.Apply(change);
            planner.Invalidate();
            reachable.Clear();
            holding.Clear();
            goalOwners.Clear();
            goalRevision++;
            foreach (var actor in actors.Values.OrderBy(actor => actor.Request.Profile.AttackRangeMm).ThenByDescending(actor => actor.Request.Profile.RadiusMm))
            {
                actor.YieldCell = -1;
                actor.Route = null;
                actor.BestGoalDistance = double.MaxValue;
                int start = World.CellAt(actor.Position);
                var distances = Reachability(start, actor.Request.Profile.RadiusMm);
                int goal = geometry.CanHold(actor.Goal, actor.Request.Profile, actor.Request.TargetId) && !goalOwners.ContainsKey(actor.Goal)
                    && GoalClear(actor.Goal, actor.Request.Profile, actor.Request.Id)
                    ? actor.Goal : PickGoal(actor.Request.Profile, actor.Request.TargetId, distances, actor.Position, true);
                if (goal >= 0)
                {
                    actor.Goal = goal;
                    goalOwners[goal] = actor.Request.Id;
                }
            }
            Add("AcceptedWorldChanges");
            return true;
        }

        public IReadOnlyList<UnitSnapshot> Snapshot()
        {
            ThrowIfDisposed();
            return actors.Values.OrderBy(actor => actor.Request.Id)
                .Select(actor => new UnitSnapshot(actor.Request.Id, actor.Position, State(actor), actor.Goal, actor.Request.TargetId)).ToArray();
        }

        public void Dispose()
        {
            actors.Clear();
            goalOwners.Clear();
            disposed = true;
        }

        private Point2 Destination(Actor actor)
        {
            return World.Center(actor.YieldCell >= 0 ? actor.YieldCell : actor.Goal);
        }

        private MotionState State(Actor actor)
        {
            if (actor.YieldCell >= 0 || Point2.Distance(actor.Position, World.Center(actor.Goal)) > SimulationProtocol.GoalToleranceMm)
            {
                return MotionState.Moving;
            }
            return geometry.CanAttack(actor.Position, actor.Request.Profile, actor.Request.TargetId) ? MotionState.Attacking : MotionState.Waiting;
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(AoeNavigator));
            }
        }

        private void Add(string key, long count = 1)
        {
            metrics.TryGetValue(key, out long value);
            metrics[key] = value + count;
        }

        private static string ProfileKey(UnitProfile profile, int target)
        {
            return target + ":" + profile.RadiusMm + ":" + profile.AttackRangeMm;
        }
    }
}
