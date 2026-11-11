using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NavigationLab.Core;
using RVO;
using Vector = RVO.Vector2;

namespace NavigationLab.Candidates.BOrca
{
    internal sealed class OrcaNavigator : INavigator
    {
        internal const NavigationFeatures SupportedFeatures = NavigationFeatures.DeathAndReinforcement | NavigationFeatures.DynamicTerrain
            | NavigationFeatures.MovingTarget | NavigationFeatures.MultipleTargets | NavigationFeatures.AssignedGoals
            | NavigationFeatures.LargeBodies | NavigationFeatures.FastPerTick;
        private const double ExecutionToleranceMm = SimulationProtocol.GeometryToleranceMm / 4;
        private readonly SimulatorContext context = new SimulatorContext();
        private readonly Dictionary<int, Actor> actors = new Dictionary<int, Actor>();
        private readonly Dictionary<int, Actor> goalOwners = new Dictionary<int, Actor>();
        private readonly Dictionary<int, RouteMesh> meshes = new Dictionary<int, RouteMesh>();
        private readonly Dictionary<string, long> metrics = new Dictionary<string, long>();
        private readonly bool coordinatedYield;
        private WorldDefinition world;
        private long microstep;
        private bool disposed;
        private int minimumAttackRange;
        private readonly List<UnitProfile> declaredProfiles;

        private sealed class Actor
        {
            internal SpawnRequest Request;
            internal int Goal;
            internal int RvoId;
            internal Point2 Position;
            internal Point2 Velocity;
            internal List<Point2> Path = new List<Point2>();
            internal int Waypoint;
            internal long RepathAt;
            internal long LastProgress;
            internal double BestError = double.PositiveInfinity;
            internal bool Yielding;
            internal Point2 YieldTarget;
            internal int YieldRequester;
            internal long YieldStarted;
            internal long YieldRetryAt;
            internal int YieldTrialRound;
        }

        internal OrcaNavigator(WorldDefinition world, IReadOnlyList<UnitProfile> profiles, bool coordinatedYield)
        {
            this.world = world.Clone();
            this.coordinatedYield = coordinatedYield;
            declaredProfiles = profiles.Distinct().ToList();
            minimumAttackRange = profiles.Count == 0 ? 0 : profiles.Min(profile => profile.AttackRangeMm);
            Set("RegisteredProfiles", profiles.Count);
            Set("ExecutionNumericalToleranceMicrometres", (long)Math.Round(ExecutionToleranceMm * 1000));
            Set("RouteResolutionHalfCells", 1);
            Set("UsesSipp", 0);
            Set("NegotiatedYieldEnabled", coordinatedYield ? 1 : 0);
            Set("PhysicalSubstepMilliseconds", SimulationProtocol.MicrostepMilliseconds);
            RebuildSimulator();
        }

        public IReadOnlyDictionary<string, long> Metrics => metrics;
        public string LastSpawnFailure { get; private set; } = "None";

        public bool TrySpawn(SpawnRequest request)
        {
            CheckAlive();
            Add("SpawnAttempts");
            if (actors.ContainsKey(request.Id) || request.Profile.RadiusMm <= 0 || request.Profile.SpeedMmPerTick <= 0 || request.Profile.AttackRangeMm < 0
                || request.SpawnCell < 0 || request.SpawnCell >= world.CellCount)
            {
                return Reject("InvalidRequest");
            }

            var point = world.Center(request.SpawnCell);
            if (!Geometry.Clear(world, point, point, request.Profile.RadiusMm))
            {
                return Reject("StaticUnreachable");
            }

            foreach (var other in actors.Values)
            {
                if (Point2.Distance(point, other.Position) < request.Profile.RadiusMm + other.Request.Profile.RadiusMm + .05)
                {
                    return Reject("SpawnOccupied");
                }
            }

            int goal = SelectGoal(request, point);
            if (goal < 0)
            {
                return Reject("EndpointCapacityOrUnreachable");
            }

            var path = Mesh(request.Profile).Find(point, world.Center(goal), null, out int expanded);
            Add("GlobalExpandedNodes", expanded);
            if (path == null)
            {
                return Reject("StaticUnreachable");
            }

            var actor = new Actor { Request = request, Goal = goal, Position = point, Path = path, LastProgress = microstep };
            actors.Add(request.Id, actor);
            goalOwners.Add(goal, actor);
            if (!declaredProfiles.Contains(request.Profile))
            {
                declaredProfiles.Add(request.Profile);
                minimumAttackRange = Math.Min(minimumAttackRange, request.Profile.AttackRangeMm);
                RebuildSimulator();
            }
            else
            {
                context.Use(simulator => actor.RvoId = AddAgent(simulator, actor));
            }
            Add("Spawned");
            LastSpawnFailure = "None";
            if (coordinatedYield)
            {
                ProtectConstrainedNewcomer(actor);
            }

            return true;
        }

        public void Step()
        {
            CheckAlive();
            microstep++;
            Add("Microsteps");
            if (actors.Count == 0)
            {
                return;
            }

            var ordered = actors.Values.OrderBy(actor => actor.Request.Id).ToArray();
            if (coordinatedYield && microstep % SimulationProtocol.Substeps == 0)
            {
                CoordinateYield(ordered);
                if (microstep % 100 == 0)
                {
                    ExchangeCrossedMovingGoals(ordered);
                }
            }
            if (microstep % 100 == 0)
            {
                PromoteWaiting();
            }

            var before = ordered.Select(actor => actor.Position).ToArray();
            var next = new Point2[ordered.Length];
            context.Use(simulator =>
            {
                long phaseStarted = Stopwatch.GetTimestamp();
                foreach (var actor in ordered)
                {
                    UpdateRoute(actor, ordered);
                    Point2 destination = actor.Yielding ? actor.YieldTarget : GoalPoint(actor);
                    while (actor.Waypoint < actor.Path.Count
                        && Point2.Distance(actor.Position, actor.Path[actor.Waypoint]) <= .25)
                    {
                        actor.Waypoint++;
                    }

                    if (actor.Waypoint < actor.Path.Count)
                    {
                        destination = actor.Path[actor.Waypoint];
                    }

                    var delta = destination - actor.Position;
                    double maximum = actor.Request.Profile.SpeedMmPerTick / 50.0;
                    double distancePerStep = Math.Min(maximum * SimulationProtocol.MicrostepMilliseconds, delta.Length);
                    var velocity = delta.Length <= .005 ? default : delta.Normalized * (distancePerStep / SimulationProtocol.MicrostepMilliseconds);
                    simulator.SetAgentPrefVelocity(actor.RvoId, new Vector((float)velocity.X, (float)velocity.Y));
                }

                Add("RouteAndPreferenceStopwatchTicks", Stopwatch.GetTimestamp() - phaseStarted);
                phaseStarted = Stopwatch.GetTimestamp();
                simulator.DoStep();
                Add("OrcaSolverStopwatchTicks", Stopwatch.GetTimestamp() - phaseStarted);
                for (int index = 0; index < ordered.Length; index++)
                {
                    next[index] = Millimetres(simulator.GetAgentPosition(ordered[index].RvoId));
                }

                phaseStarted = Stopwatch.GetTimestamp();
                FilterUnsafeProposals(ordered, before, next);
                Add("PhysicalFilterStopwatchTicks", Stopwatch.GetTimestamp() - phaseStarted);
                for (int index = 0; index < ordered.Length; index++)
                {
                    var actor = ordered[index];
                    actor.Velocity = (next[index] - before[index]) / SimulationProtocol.MicrostepMilliseconds;
                    actor.Position = next[index];
                    simulator.SetAgentPosition(actor.RvoId, Metres(actor.Position));
                    simulator.SetAgentVelocity(actor.RvoId, new Vector((float)actor.Velocity.X, (float)actor.Velocity.Y));
                }
            });
            Add("SimulatorStateSwaps");
        }

        public bool Remove(int id)
        {
            CheckAlive();
            if (!actors.TryGetValue(id, out var removed))
            {
                return false;
            }

            actors.Remove(id);
            goalOwners.Remove(removed.Goal);
            foreach (var actor in actors.Values)
            {
                if (actor.Yielding && actor.YieldRequester == id)
                {
                    actor.Yielding = false;
                }

                actor.RepathAt = 0;
                actor.YieldRetryAt = 0;
            }

            RebuildSimulator();
            PromoteWaiting();
            Add("Removed");
            return true;
        }

        public bool ApplyWorldChange(WorldChange change)
        {
            CheckAlive();
            world = world.WithChange(change);
            meshes.Clear();
            goalOwners.Clear();
            foreach (var actor in actors.Values.OrderBy(actor => actor.Request.Id))
            {
                actor.Yielding = false;
                actor.Path.Clear();
                actor.Waypoint = 0;
                actor.RepathAt = 0;
                actor.YieldRetryAt = 0;
                if (!AvailableGoal(actor.Goal, actor.Request.Profile, actor.Request.TargetId))
                {
                    actor.Goal = SelectGoal(actor.Request, actor.Position);
                }

                if (actor.Goal >= 0)
                {
                    goalOwners[actor.Goal] = actor;
                }
            }

            RebuildSimulator();
            PromoteWaiting();
            Add("WorldChanges");
            return true;
        }

        public IReadOnlyList<UnitSnapshot> Snapshot()
        {
            CheckAlive();
            return actors.Values.OrderBy(actor => actor.Request.Id).Select(actor => new UnitSnapshot(
                actor.Request.Id,
                actor.Position,
                Arrived(actor) ? (Geometry.CanAttack(world, actor.Position, actor.Request.Profile, actor.Request.TargetId)
                    ? MotionState.Attacking : MotionState.Waiting) : MotionState.Moving,
                actor.Goal,
                actor.Request.TargetId)).ToArray();
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }
            context.Use(simulator => simulator.Clear());
            Add("SimulatorReleases");
            disposed = true;
            actors.Clear();
            goalOwners.Clear();
            meshes.Clear();
        }

        private void UpdateRoute(Actor actor, Actor[] ordered)
        {
            if (actor.Goal < 0 && microstep >= actor.RepathAt)
            {
                actor.Goal = SelectGoal(actor.Request, actor.Position);
                actor.RepathAt = microstep + 200;
                if (actor.Goal >= 0)
                {
                    goalOwners[actor.Goal] = actor;
                    actor.RepathAt = 0;
                }
            }
            if (actor.Goal < 0 || actor.Yielding || Arrived(actor))
            {
                return;
            }

            if (microstep >= actor.RepathAt)
            {
                var settled = ordered.Where(other => other != actor && Arrived(other)).ToArray();
                Func<Point2, bool> blocked = point => settled.Any(other => Point2.Distance(point, other.Position)
                    < actor.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm - ExecutionToleranceMm);
                Func<Point2, Point2, bool> blockedSegment = (first, second) => settled.Any(other =>
                    Geometry.PointSegmentSquared(other.Position, first, second)
                        < Square(actor.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm - ExecutionToleranceMm));
                var path = Mesh(actor.Request.Profile).Find(actor.Position, GoalPoint(actor), blocked, out int expanded, blockedSegment);
                Add("GlobalExpandedNodes", expanded);
                Add("DynamicRepaths");
                if (path != null)
                {
                    actor.Path = path;
                    actor.Waypoint = 0;
                }

                actor.RepathAt = microstep + 200;
            }

            if (microstep % 50 == 0)
            {
                for (int next = actor.Path.Count - 1; next > actor.Waypoint; next--)
                {
                    var target = actor.Path[next];
                    if (Geometry.Clear(world, actor.Position, target, actor.Request.Profile.RadiusMm)
                        && !ordered.Any(other => other != actor && Arrived(other)
                            && Geometry.PointSegmentSquared(other.Position, actor.Position, target)
                                < Square(actor.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm - ExecutionToleranceMm)))
                    {
                        actor.Waypoint = next;
                        Add("VisibleRouteShortcuts");
                        break;
                    }
                }
            }
        }

        private int SelectGoal(SpawnRequest request, Point2 position)
        {
            if (request.AssignedGoalCell >= 0)
            {
                return AvailableGoal(request.AssignedGoalCell, request.Profile, request.TargetId) ? request.AssignedGoalCell : -1;
            }

            var target = world.Target(request.TargetId);
            foreach (int cell in Enumerable.Range(0, world.CellCount).Where(cell => AvailableGoal(cell, request.Profile, request.TargetId))
                .OrderByDescending(cell => Geometry.CanAttack(world, world.Center(cell), request.Profile, request.TargetId))
                .ThenByDescending(cell => Point2.Distance(world.Center(cell), target.Center))
                .ThenBy(cell => Point2.Distance(world.Center(cell), position)).ThenBy(cell => cell))
            {
                bool reachable = Mesh(request.Profile).StaticallyReachable(position, world.Center(cell), out int expanded);
                Add("StaticReachabilityExpandedNodes", expanded);
                if (reachable)
                {
                    return cell;
                }
            }

            return -1;
        }

        private bool AvailableGoal(int goal, UnitProfile profile, int targetId, Actor? ignore = null)
        {
            return (!goalOwners.TryGetValue(goal, out var owner) || owner == ignore) && Geometry.CanHold(world, goal, profile, targetId)
                && !goalOwners.Values.Any(other => other != ignore && Point2.Distance(world.Center(goal), world.Center(other.Goal))
                    < profile.RadiusMm + other.Request.Profile.RadiusMm - .000001);
        }

        private void PromoteWaiting()
        {
            foreach (var actor in actors.Values.Where(actor => actor.Request.AssignedGoalCell < 0 && actor.Goal >= 0
                && !Geometry.CanAttack(world, GoalPoint(actor), actor.Request.Profile, actor.Request.TargetId)).OrderBy(actor => actor.Request.Id).ToArray())
            {
                int goal = Enumerable.Range(0, world.CellCount).Where(cell => AvailableGoal(cell, actor.Request.Profile, actor.Request.TargetId, actor)
                    && Geometry.CanAttack(world, world.Center(cell), actor.Request.Profile, actor.Request.TargetId))
                    .OrderBy(cell => Point2.Distance(actor.Position, world.Center(cell))).ThenBy(cell => cell).DefaultIfEmpty(-1).First();
                if (goal < 0)
                {
                    continue;
                }

                goalOwners.Remove(actor.Goal);
                actor.Goal = goal;
                goalOwners.Add(goal, actor);
                actor.RepathAt = 0;
                actor.Path.Clear();
                actor.Waypoint = 0;
                Add("VacancyPromotions");
            }
        }

        private void ProtectConstrainedNewcomer(Actor incoming)
        {
            if (incoming.Request.AssignedGoalCell >= 0 || Geometry.CanAttack(world, GoalPoint(incoming), incoming.Request.Profile, incoming.Request.TargetId))
            {
                return;
            }

            foreach (var owner in actors.Values.Where(owner => owner != incoming && owner.Request.TargetId == incoming.Request.TargetId
                && owner.Request.AssignedGoalCell < 0 && owner.Request.Profile.AttackRangeMm > incoming.Request.Profile.AttackRangeMm)
                .OrderBy(owner => owner.Request.Id))
            {
                if (!Geometry.CanHold(world, incoming.Goal, owner.Request.Profile, owner.Request.TargetId)
                    || !Geometry.CanHold(world, owner.Goal, incoming.Request.Profile, incoming.Request.TargetId)
                    || !Geometry.CanAttack(world, GoalPoint(owner), incoming.Request.Profile, incoming.Request.TargetId)
                    || goalOwners.Values.Any(other => other != incoming && other != owner
                        && (Point2.Distance(GoalPoint(incoming), GoalPoint(other))
                                < owner.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm - .000001
                            || Point2.Distance(GoalPoint(owner), GoalPoint(other))
                                < incoming.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm - .000001)))
                {
                    continue;
                }

                int previous = incoming.Goal;
                incoming.Goal = owner.Goal;
                owner.Goal = previous;
                goalOwners[incoming.Goal] = incoming;
                goalOwners[owner.Goal] = owner;
                foreach (var changed in new[] { incoming, owner })
                {
                    changed.Path.Clear();
                    changed.Waypoint = 0;
                    changed.RepathAt = 0;
                    changed.BestError = double.PositiveInfinity;
                    changed.LastProgress = microstep;
                }

                Add("ConstrainedGoalExchanges");
                break;
            }
        }

        private void ExchangeCrossedMovingGoals(Actor[] ordered)
        {
            // Relabel only equal-profile, equal-target, freely assigned moving bodies. The set of legal
            // physical endpoints and every unit's radius, speed, target and assigned-order identity stay intact.
            foreach (var first in ordered)
            {
                if (first.Goal < 0 || first.Yielding || Arrived(first) || first.Request.AssignedGoalCell >= 0)
                {
                    continue;
                }
                Actor? selected = null;
                double bestGain = world.CellSizeMm * .25;
                foreach (var second in ordered)
                {
                    if (second.Goal < 0 || second.Request.Id <= first.Request.Id || second.Yielding || Arrived(second)
                        || second.Request.AssignedGoalCell >= 0 || !second.Request.Profile.Equals(first.Request.Profile)
                        || second.Request.TargetId != first.Request.TargetId)
                    {
                        continue;
                    }
                    double before = Point2.Distance(first.Position, GoalPoint(first)) + Point2.Distance(second.Position, GoalPoint(second));
                    double after = Point2.Distance(first.Position, GoalPoint(second)) + Point2.Distance(second.Position, GoalPoint(first));
                    if (before - after > bestGain)
                    {
                        bestGain = before - after;
                        selected = second;
                    }
                }
                if (selected == null)
                {
                    continue;
                }
                bool firstReachable = Mesh(first.Request.Profile).StaticallyReachable(first.Position, GoalPoint(selected), out int firstExpanded);
                bool secondReachable = Mesh(selected.Request.Profile).StaticallyReachable(selected.Position, GoalPoint(first), out int secondExpanded);
                Add("StaticReachabilityExpandedNodes", firstExpanded + secondExpanded);
                if (!firstReachable || !secondReachable)
                {
                    Add("CrossedGoalExchangeDisconnectedRejections");
                    continue;
                }
                int previous = first.Goal;
                first.Goal = selected.Goal;
                selected.Goal = previous;
                goalOwners[first.Goal] = first;
                goalOwners[selected.Goal] = selected;
                foreach (var changed in new[] { first, selected })
                {
                    changed.Path.Clear();
                    changed.Waypoint = 0;
                    changed.RepathAt = 0;
                    changed.BestError = double.PositiveInfinity;
                    changed.LastProgress = microstep;
                }
                Add("CrossedMovingGoalExchanges");
            }
        }

        private void CoordinateYield(Actor[] ordered)
        {
            foreach (var actor in ordered)
            {
                double error = Point2.Distance(actor.Position, GoalPoint(actor));
                if (actor.Yielding)
                {
                    if (!actors.TryGetValue(actor.YieldRequester, out var requester) || Arrived(requester) || microstep - actor.YieldStarted >= 8000)
                    {
                        actor.Yielding = false;
                        actor.Path.Clear();
                        actor.Waypoint = 0;
                        actor.RepathAt = 0;
                        actor.BestError = double.PositiveInfinity;
                        actor.LastProgress = microstep;
                        Add(requester != null && Arrived(requester) ? "YieldCompletions" : "YieldTimeouts");
                    }

                    continue;
                }

                if (Arrived(actor) || error < actor.BestError - 10)
                {
                    actor.BestError = error;
                    actor.LastProgress = microstep;
                }
            }

            foreach (var requester in ordered)
            {
                if (requester.Yielding || Arrived(requester) || microstep < requester.YieldRetryAt || microstep - requester.LastProgress < 1000
                    || ordered.Count(actor => actor.Yielding && actor.YieldRequester == requester.Request.Id) >= 4)
                {
                    continue;
                }

                requester.YieldRetryAt = microstep + 200;
                foreach (var blocker in ordered.Where(actor => actor != requester && !actor.Yielding
                    && (Arrived(actor) || actor.Request.Id > requester.Request.Id && microstep - actor.LastProgress >= 500
                        && !ordered.Any(dependent => dependent.Yielding && dependent.YieldRequester == actor.Request.Id)))
                    .OrderBy(actor => Point2.Distance(actor.Position, requester.Position)))
                {
                    double distance = Point2.Distance(blocker.Position, requester.Position);
                    var routePoint = requester.Waypoint < requester.Path.Count ? requester.Path[requester.Waypoint] : GoalPoint(requester);
                    double routeDistance = Geometry.PointSegmentSquared(blocker.Position, requester.Position, routePoint);
                    double goalDistance = Geometry.PointSegmentSquared(blocker.Position, requester.Position, GoalPoint(requester));
                    if (distance > world.CellSizeMm * 3 || Math.Min(routeDistance, goalDistance)
                        > Square(requester.Request.Profile.RadiusMm + blocker.Request.Profile.RadiusMm + world.CellSizeMm * .5))
                    {
                        continue;
                    }

                    if (TryAngularYield(blocker, requester, ordered) || TryBeginYield(blocker, requester, ordered))
                    {
                        if (!Arrived(blocker))
                        {
                            Add("MovingPeerYieldRequests");
                        }
                        requester.LastProgress = microstep;
                        break;
                    }
                }
            }
        }

        private bool TryAngularYield(Actor blocker, Actor requester, Actor[] ordered)
        {
            var outward = (blocker.Position - world.Target(blocker.Request.TargetId).Center).Normalized;
            if (outward.LengthSquared < .1)
            {
                outward = (blocker.Position - requester.Position).Normalized;
            }
            double length = Math.Max(world.CellSizeMm, (requester.Request.Profile.RadiusMm + blocker.Request.Profile.RadiusMm) * 1.5);
            foreach (double angle in new[] { 0.0, Math.PI / 4, -Math.PI / 4, Math.PI / 2, -Math.PI / 2, Math.PI })
            {
                var direction = new Point2(outward.X * Math.Cos(angle) - outward.Y * Math.Sin(angle),
                    outward.X * Math.Sin(angle) + outward.Y * Math.Cos(angle));
                var destination = blocker.Position + direction * length;
                if (!Geometry.Clear(world, blocker.Position, destination, blocker.Request.Profile.RadiusMm)
                    || ordered.Any(other => other != blocker
                        && (Point2.Distance(destination, other.Position) < blocker.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm + 5
                            || other.Yielding && Point2.Distance(destination, other.YieldTarget)
                                < blocker.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm + 5)))
                {
                    continue;
                }
                blocker.Yielding = true;
                blocker.YieldTarget = destination;
                blocker.YieldRequester = requester.Request.Id;
                blocker.YieldStarted = microstep;
                blocker.Path = new List<Point2> { destination };
                blocker.Waypoint = 0;
                Add("YieldRequests");
                Add("AngularYieldRequests");
                return true;
            }
            return false;
        }

        private bool TryBeginYield(Actor blocker, Actor requester, Actor[] ordered)
        {
            int currentCell = world.CellAt(blocker.Position);
            int x = currentCell % world.Width;
            int y = currentCell / world.Width;
            var candidates = new List<int>();
            for (int cy = Math.Max(0, y - 3); cy <= Math.Min(world.Height - 1, y + 3); cy++)
            {
                for (int cx = Math.Max(0, x - 3); cx <= Math.Min(world.Width - 1, x + 3); cx++)
                {
                    int cell = cy * world.Width + cx;
                    var point = world.Center(cell);
                    double distance = Point2.Distance(point, blocker.Position);
                    if (cell == blocker.Goal || !world.IsWalkable(cell) || distance < blocker.Request.Profile.RadiusMm * .5
                        || distance > world.CellSizeMm * 3 || !Geometry.Clear(world, point, point, blocker.Request.Profile.RadiusMm))
                    {
                        continue;
                    }
                    if (ordered.Any(other => other != blocker
                        && (Point2.Distance(point, other.Position) < blocker.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm + .1
                            || other.Yielding && Point2.Distance(point, other.YieldTarget)
                                < blocker.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm + .1)))
                    {
                        continue;
                    }
                    candidates.Add(cell);
                }
            }
            var protectedBodies = ordered.Where(other => other != blocker && (other == requester || Arrived(other))).ToArray();
            Func<Point2, bool> blocked = point => protectedBodies.Any(other => Point2.Distance(point, other.Position)
                < blocker.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm - ExecutionToleranceMm);
            Func<Point2, Point2, bool> blockedSegment = (first, second) => protectedBodies.Any(other =>
                Geometry.PointSegmentSquared(other.Position, first, second)
                    < Square(blocker.Request.Profile.RadiusMm + other.Request.Profile.RadiusMm - ExecutionToleranceMm));
            var sorted = candidates.OrderBy(cell => goalOwners.ContainsKey(cell) ? 2 : world.HoldingAllowed(cell) ? 1 : 0)
                .ThenBy(cell => Point2.Distance(world.Center(cell), blocker.Position)).ThenBy(cell => cell).ToArray();
            int offset = sorted.Length == 0 ? 0 : blocker.YieldTrialRound * 12 % sorted.Length;
            blocker.YieldTrialRound++;
            for (int trial = 0; trial < Math.Min(12, sorted.Length); trial++)
            {
                int cell = sorted[(offset + trial) % sorted.Length];
                var point = world.Center(cell);
                var path = Mesh(blocker.Request.Profile).Find(blocker.Position, point, blocked, out int expanded, blockedSegment, 2048);
                Add("GlobalExpandedNodes", expanded);
                if (expanded > 2048)
                {
                    Add("YieldSearchBudgetExhaustions");
                }
                Add("YieldPathTrials");
                if (path == null)
                {
                    continue;
                }
                blocker.Yielding = true;
                blocker.YieldTarget = point;
                blocker.YieldRequester = requester.Request.Id;
                blocker.YieldStarted = microstep;
                blocker.Path = path;
                blocker.Waypoint = 0;
                Add("YieldRequests");
                if (goalOwners.ContainsKey(cell))
                {
                    Add("BorrowedFutureGoalBuffers");
                }
                return true;
            }
            return false;
        }

        private void FilterUnsafeProposals(Actor[] ordered, Point2[] before, Point2[] next)
        {
            var stopped = new bool[ordered.Length];
            for (int index = 0; index < ordered.Length; index++)
            {
                var point = next[index];
                double allowed = ordered[index].Request.Profile.SpeedMmPerTick / 10.0;
                if (double.IsNaN(point.X) || double.IsNaN(point.Y) || double.IsInfinity(point.X) || double.IsInfinity(point.Y))
                {
                    throw new InvalidOperationException("ORCA returned a non-finite physical position.");
                }

                if (Point2.Distance(before[index], point) > allowed + ExecutionToleranceMm
                    || !Geometry.Clear(world, before[index], point, ordered[index].Request.Profile.RadiusMm, true, ExecutionToleranceMm))
                {
                    next[index] = before[index];
                    stopped[index] = true;
                    Add("UnsafeStaticOrSpeedProposalStops");
                }
            }

            bool changed;
            do
            {
                changed = false;
                for (int first = 0; first < ordered.Length; first++)
                {
                    for (int second = first + 1; second < ordered.Length; second++)
                    {
                        double radius = ordered[first].Request.Profile.RadiusMm + ordered[second].Request.Profile.RadiusMm;
                        if (Geometry.PairSweepSquared(before[first], next[first], before[second], next[second]) >= Square(radius - ExecutionToleranceMm))
                        {
                            continue;
                        }

                        if (!stopped[first] || !stopped[second])
                        {
                            next[first] = before[first];
                            next[second] = before[second];
                            stopped[first] = true;
                            stopped[second] = true;
                            Add("UnsafePairProposalStops");
                            changed = true;
                        }
                    }
                }
            }
            while (changed);
        }

        private void RebuildSimulator()
        {
            context.Use(simulator =>
            {
                simulator.Clear();
                simulator.NumWorkers = 1;
                simulator.TimeStep = SimulationProtocol.MicrostepMilliseconds / 1000f;
                BuildObstacles(simulator);
                simulator.ProcessObstacles();
                foreach (var actor in actors.Values.OrderBy(actor => actor.Request.Id))
                {
                    actor.RvoId = AddAgent(simulator, actor);
                }
            });
            Add("SimulatorRebuilds");
            Add("SimulatorStateSwaps");
        }

        private int AddAgent(Simulator simulator, Actor actor)
        {
            float radius = actor.Request.Profile.RadiusMm / 1000f;
            float speed = actor.Request.Profile.SpeedMmPerTick / 50f;
            return simulator.AddAgent(
                Metres(actor.Position),
                Math.Max(5, radius * 12),
                256,
                2,
                .5f,
                radius,
                speed,
                new Vector((float)actor.Velocity.X, (float)actor.Velocity.Y));
        }

        private void BuildObstacles(Simulator simulator)
        {
            double size = world.CellSizeMm;
            for (int y = 0; y < world.Height; y++)
            {
                for (int x = 0; x < world.Width; x++)
                {
                    if (!Geometry.Open(world, x, y))
                    {
                        continue;
                    }

                    double left = world.Origin.X + x * size;
                    double bottom = world.Origin.Y + y * size;
                    if (!Geometry.Open(world, x - 1, y))
                    {
                        AddWall(simulator, new Point2(left, bottom), new Point2(left, bottom + size));
                    }
                    if (!Geometry.Open(world, x + 1, y))
                    {
                        AddWall(simulator, new Point2(left + size, bottom + size), new Point2(left + size, bottom));
                    }
                    if (!Geometry.Open(world, x, y - 1))
                    {
                        AddWall(simulator, new Point2(left + size, bottom), new Point2(left, bottom));
                    }
                    if (!Geometry.Open(world, x, y + 1))
                    {
                        AddWall(simulator, new Point2(left, bottom + size), new Point2(left + size, bottom + size));
                    }
                }
            }

            foreach (var target in world.Targets)
            {
                if (target.RadiusMm == 0)
                {
                    Add("ZeroRadiusBasesUseExactPhysicalGate");
                    continue;
                }
                double maximumErrorMm = Math.Max(.02, Math.Min(.5, minimumAttackRange * .01));
                int required = (int)Math.Ceiling(Math.PI / Math.Acos(target.RadiusMm / (target.RadiusMm + maximumErrorMm)));
                int count = 32;
                while (count < required)
                {
                    count *= 2;
                }
                var angles = Enumerable.Range(0, count).Select(index => index * Math.PI * 2 / count).ToList();
                foreach (var profile in declaredProfiles)
                {
                    for (int cell = 0; cell < world.CellCount; cell++)
                    {
                        if (!world.HoldingAllowed(cell))
                        {
                            continue;
                        }
                        var position = world.Center(cell);
                        double clearance = Point2.Distance(position, target.Center) - target.RadiusMm - profile.RadiusMm;
                        if (clearance >= -.000001 && clearance <= maximumErrorMm + .01
                            && Geometry.Clear(world, position, position, profile.RadiusMm))
                        {
                            double angle = Math.Atan2(position.Y - target.Center.Y, position.X - target.Center.X);
                            angles.Add(angle < 0 ? angle + Math.PI * 2 : angle);
                            Add("CriticalGoalTangentPlanes");
                        }
                    }
                }
                angles.Sort();
                var distinct = new List<double>();
                foreach (double angle in angles)
                {
                    if (distinct.Count == 0 || angle - distinct[distinct.Count - 1] > 1e-8)
                    {
                        distinct.Add(angle);
                    }
                }
                var polygon = new List<Vector>();
                var points = new List<Point2>();
                for (int index = 0; index < distinct.Count; index++)
                {
                    double nextAngle = index + 1 < distinct.Count ? distinct[index + 1] : distinct[0] + Math.PI * 2;
                    double half = (nextAngle - distinct[index]) / 2;
                    double angle = distinct[index] + half;
                    double distance = target.RadiusMm / Math.Cos(half);
                    var vertex = Metres(target.Center + new Point2(Math.Cos(angle), Math.Sin(angle)) * distance);
                    if (polygon.Count == 0 || Point2.Distance(Millimetres(vertex), Millimetres(polygon[polygon.Count - 1])) > .00001)
                    {
                        polygon.Add(vertex);
                        points.Add(Millimetres(vertex));
                    }
                    long errorMicrometres = (long)Math.Ceiling((distance - target.RadiusMm) * 1000);
                    Set("MaximumPolygonExtraClearanceMicrometres", metrics.TryGetValue("MaximumPolygonExtraClearanceMicrometres", out long old)
                        ? Math.Max(old, errorMicrometres) : errorMicrometres);
                }
                if (polygon.Count > 1 && Point2.Distance(points[0], points[points.Count - 1]) <= .00001)
                {
                    polygon.RemoveAt(polygon.Count - 1);
                    points.RemoveAt(points.Count - 1);
                }
                Add("BasePolygonVerticesBuilt", polygon.Count);
                Set("MaximumBasePolygonVertices", metrics.TryGetValue("MaximumBasePolygonVertices", out long current)
                    ? Math.Max(current, polygon.Count) : polygon.Count);
                foreach (var profile in declaredProfiles)
                {
                    for (int cell = 0; cell < world.CellCount; cell++)
                    {
                        var point = world.Center(cell);
                        if (!world.HoldingAllowed(cell) || !Geometry.Clear(world, point, point, profile.RadiusMm))
                        {
                            continue;
                        }
                        double squared = double.PositiveInfinity;
                        for (int index = 0; index < points.Count; index++)
                        {
                            squared = Math.Min(squared, Geometry.PointSegmentSquared(point, points[index], points[(index + 1) % points.Count]));
                        }
                        Add("BasePolygonGoalCompatibilityChecks");
                        if (Math.Sqrt(squared) < profile.RadiusMm - .01)
                        {
                            Add("BasePolygonGoalCompatibilityFailures");
                            throw new InvalidOperationException("ORCA base approximation excluded a legal physical holding Goal.");
                        }
                    }
                }
                simulator.AddObstacle(polygon);
            }
        }

        private static void AddWall(Simulator simulator, Point2 first, Point2 second)
        {
            simulator.AddObstacle(new[] { Metres(first), Metres(second) });
        }

        private RouteMesh Mesh(UnitProfile profile)
        {
            if (!meshes.TryGetValue(profile.RadiusMm, out var result))
            {
                result = new RouteMesh(world, profile.RadiusMm);
                meshes.Add(profile.RadiusMm, result);
                Add("RouteMeshBuilds");
            }

            return result;
        }

        private bool Arrived(Actor actor)
        {
            if (actor.Yielding || actor.Goal < 0 || Point2.Distance(actor.Position, world.Center(actor.Goal)) > SimulationProtocol.GoalToleranceMm)
            {
                return false;
            }

            bool actualAttack = Geometry.CanAttack(world, actor.Position, actor.Request.Profile, actor.Request.TargetId);
            if (Geometry.CanAttack(world, world.Center(actor.Goal), actor.Request.Profile, actor.Request.TargetId))
            {
                return actualAttack;
            }
            var target = world.Target(actor.Request.TargetId);
            return actualAttack || Point2.Distance(actor.Position, target.Center)
                <= target.RadiusMm + world.GatheringDistanceMm + SimulationProtocol.GeometryToleranceMm;
        }
        private Point2 GoalPoint(Actor actor) => actor.Goal >= 0 ? world.Center(actor.Goal) : actor.Position;
        private static Vector Metres(Point2 point) => new Vector((float)(point.X / 1000), (float)(point.Y / 1000));
        private static Point2 Millimetres(Vector point) => new Point2(point.X * 1000.0, point.Y * 1000.0);
        private static double Square(double value) => value * value;
        private void Add(string key, long value = 1) => Set(key, metrics.TryGetValue(key, out long previous) ? previous + value : value);
        private void Set(string key, long value) => metrics[key] = value;
        private bool Reject(string reason)
        {
            LastSpawnFailure = reason;
            Add("RejectedSpawns");
            return false;
        }

        private void CheckAlive()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(OrcaNavigator));
            }
        }
    }
}
