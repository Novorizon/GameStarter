using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.Candidates.EWarcraft3
{
    public sealed class WarcraftFactory : INavigationFactory
    {
        public string Id => "E-Warcraft3-Inspired-SpatialYield";
        public string Description => "WC3-inspired pathing map, A*, local replanning, collision stops and friendly yield; independent code.";
        public NavigationFeatures Features => NavigationFeatures.DeathAndReinforcement | NavigationFeatures.DynamicTerrain | NavigationFeatures.MovingTarget
            | NavigationFeatures.MultipleTargets | NavigationFeatures.AssignedGoals | NavigationFeatures.LargeBodies | NavigationFeatures.FastPerTick;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles) => new WarcraftNavigator(world, profiles, true, false);
    }

    public sealed class WarcraftRefinedFactory : INavigationFactory
    {
        public string Id => "E4-Warcraft3-Inspired-RefinedConnections";
        public string Description => "Spatial controller with bounded quarter-cell reconnection; original body geometry, terrain, goals and speed.";
        public NavigationFeatures Features => new WarcraftFactory().Features;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles)
        {
            return new WarcraftNavigator(world, profiles, true, false, false, true);
        }
    }

    public sealed class WarcraftBaselineFactory : INavigationFactory
    {
        public string Id => "E0-Warcraft3-Inspired-LocalRepath";
        public string Description => "Same independent spatial controller with explicit friendly-yield extension disabled; preserved failure baseline.";
        public NavigationFeatures Features => new WarcraftFactory().Features;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles) => new WarcraftNavigator(world, profiles, false, false);
    }

    public sealed class WarcraftGroupedYieldFactory : INavigationFactory
    {
        public string Id => "E2-Warcraft3-Inspired-GroupedYield";
        public string Description => "Rejected grouping experiment; retained to reproduce the extra blocking caused by freezing unrelated traffic.";
        public NavigationFeatures Features => new WarcraftFactory().Features;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles) => new WarcraftNavigator(world, profiles, true, true);
    }

    public sealed class WarcraftRequesterBackoffFactory : INavigationFactory
    {
        public string Id => "E3-Warcraft3-Inspired-RequesterBackoff";
        public string Description => "Rejected constrained-request retreat extension, retained for explicit comparison.";
        public NavigationFeatures Features => new WarcraftFactory().Features;
        public INavigator Create(WorldDefinition world, IReadOnlyList<UnitProfile> profiles) => new WarcraftNavigator(world, profiles, true, false, true);
    }

    internal sealed class MobileUnit
    {
        internal SpawnRequest Request;
        internal Point2 Position;
        internal int Goal;
        internal List<Point2> Path = new List<Point2>();
        internal int Waypoint;
        internal int BlockedSteps;
        internal long LastReplan;
        internal int YieldCell = -1;
        internal int YieldRequester;
        internal long YieldStarted;
        internal double RequesterStartGoalDistance;
        internal int RetreatCell = -1;
        internal long RetreatStarted;
        internal int ResumeAfterGate;
        internal int Id => Request.Id;
        internal UnitProfile Profile => Request.Profile;
        internal int TargetId => Request.TargetId;
    }

    internal sealed class GoalEligibility
    {
        internal readonly bool[] Holds;
        internal readonly bool[] Attacks;

        internal GoalEligibility(bool[] holds, bool[] attacks)
        {
            Holds = holds;
            Attacks = attacks;
        }
    }

    /// <summary>Spatial replanning and explicit body movement. There are no future time reservations in this controller.</summary>
    internal sealed class WarcraftNavigator : INavigator
    {
        private WorldDefinition world;
        private PathingGeometry geometry;
        private GlobalSearch search;
        private readonly UnitProfile[] profiles;
        private readonly bool friendlyYield;
        private readonly bool groupedYield;
        private readonly bool requesterBackoff;
        private readonly bool refinedConnections;
        private readonly List<int> yieldGroup = new List<int>();
        private readonly List<MobileUnit> units = new List<MobileUnit>();
        private readonly Dictionary<int, int> goalOwners = new Dictionary<int, int>();
        private readonly Dictionary<Tuple<int, int, int>, GoalEligibility> eligibility = new Dictionary<Tuple<int, int, int>, GoalEligibility>();
        private readonly Dictionary<string, Tuple<long, string>> admissionRejections = new Dictionary<string, Tuple<long, string>>();
        private readonly Dictionary<string, long> metrics = new Dictionary<string, long>();
        private long step;
        private long goalRevision;
        private bool disposed;
        private int activeRequester;
        private int returningUnit;
        private bool groupReturning;
        private long groupStarted;
        public IReadOnlyDictionary<string, long> Metrics => metrics;
        public string LastSpawnFailure { get; private set; } = "None";

        internal WarcraftNavigator(
            WorldDefinition world,
            IReadOnlyList<UnitProfile> profiles,
            bool friendlyYield,
            bool groupedYield,
            bool requesterBackoff = false,
            bool refinedConnections = false)
        {
            this.world = world.Clone();
            this.profiles = profiles.Distinct().ToArray();
            this.friendlyYield = friendlyYield;
            this.groupedYield = groupedYield;
            this.requesterBackoff = requesterBackoff;
            this.refinedConnections = refinedConnections;
            geometry = new PathingGeometry(this.world);
            search = new GlobalSearch(this.world, geometry, Count, refinedConnections);
            Count("FriendlyYieldEnabled", friendlyYield ? 1 : 0);
            Count("GroupedYieldEnabled", groupedYield ? 1 : 0);
            Count("RequesterBackoffEnabled", requesterBackoff ? 1 : 0);
            Count("RefinedConnectionsEnabled", refinedConnections ? 1 : 0);
        }

        public bool TrySpawn(SpawnRequest request)
        {
            Count("SpawnAttempts", 1);
            if (disposed || units.Any(unit => unit.Id == request.Id))
            {
                return Reject(disposed ? "Disposed" : "DuplicateId");
            }

            if ((groupedYield && activeRequester != 0) || HasConstrainedTraffic())
            {
                return Reject("TrafficNegotiationActive");
            }

            Point2 position = world.Center(request.SpawnCell);
            if (!geometry.CanStand(request.SpawnCell, request.Profile.RadiusMm))
            {
                return Reject("StaticSpawnBlocked");
            }

            foreach (var other in units)
            {
                if (Point2.Distance(position, other.Position) < request.Profile.RadiusMm + other.Profile.RadiusMm - .001)
                {
                    return Reject("SpawnOccupied");
                }
            }

            string rejectionKey = request.Profile.RadiusMm + ":" + request.Profile.AttackRangeMm + ":" + request.SpawnCell
                + ":" + request.TargetId + ":" + request.AssignedGoalCell;
            if (admissionRejections.TryGetValue(rejectionKey, out var rejection) && rejection.Item1 == goalRevision)
            {
                Count("CachedAdmissionRejections", 1);
                return Reject(rejection.Item2);
            }

            var reachable = search.Reachable(position, request.Profile);
            int goal = request.AssignedGoalCell >= 0
                ? (CanClaim(request.AssignedGoalCell, request.Profile, request.TargetId, reachable) ? request.AssignedGoalCell : -1)
                : SelectGoal(request.Profile, request.TargetId, reachable, position, false);
            if (goal < 0)
            {
                bool anyReachable = Enumerable.Range(0, world.CellCount).Any(cell => Eligible(request.Profile, request.TargetId).Holds[cell]
                    && !double.IsPositiveInfinity(reachable[cell]));
                string reason = anyReachable ? "NoFreeHoldingGoal" : "NoStaticPathToHoldingGoal";
                admissionRejections[rejectionKey] = Tuple.Create(goalRevision, reason);
                return Reject(reason);
            }

            var path = search.Find(position, goal, request.Profile);
            if (path == null)
            {
                return Reject("NoStaticPathToGoal");
            }

            var unit = new MobileUnit
            {
                Request = request,
                Position = position,
                Goal = goal,
                Path = path,
                LastReplan = step
            };
            units.Add(unit);
            units.Sort((first, second) => first.Id.CompareTo(second.Id));
            goalOwners.Add(goal, unit.Id);
            goalRevision++;
            Count("Spawned", 1);
            LastSpawnFailure = "None";
            return true;
        }

        public void Step()
        {
            if (disposed)
            {
                return;
            }

            step++;
            Count("SimulationSubsteps", 1);
            if (groupedYield)
            {
                UpdateGroup();
            }
            if (step % 100 == 0 && activeRequester == 0)
            {
                PromoteWaiting();
            }

            foreach (var unit in units)
            {
                UpdateRetreat(unit);
                if (!groupedYield)
                {
                    UpdateYield(unit);
                }
                if (CanMoveInGroup(unit) && unit.BlockedSteps >= 80 && step - unit.LastReplan >= 80)
                {
                    RepairPath(unit);
                }
            }

            var starts = units.Select(unit => unit.Position).ToArray();
            var next = new Point2[units.Count];
            for (int index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                next[index] = CanMoveInGroup(unit) ? PreferredPosition(unit) : unit.Position;
                if (!geometry.ClearSegment(starts[index], next[index], unit.Profile.RadiusMm))
                {
                    next[index] = starts[index];
                    Count("StaticMoveStops", 1);
                }
            }

            ResolveBodyContacts(starts, next);
            for (int index = 0; index < units.Count; index++)
            {
                var unit = units[index];
                double distance = Point2.Distance(starts[index], next[index]);
                unit.Position = next[index];
                if (distance > .001)
                {
                    unit.BlockedSteps = 0;
                    Count("BodyMoveSubsteps", 1);
                }
                else if (State(unit) == MotionState.Moving
                    && (unit.RetreatCell >= 0 || Point2.Distance(unit.Position, world.Center(unit.YieldCell >= 0 ? unit.YieldCell : unit.Goal)) > .001))
                {
                    unit.BlockedSteps++;
                    Count("WaitingSubsteps", 1);
                }

                AdvanceWaypoint(unit);
            }
        }

        public bool Remove(int id)
        {
            var unit = units.FirstOrDefault(value => value.Id == id);
            if (unit == null)
            {
                return false;
            }

            units.Remove(unit);
            yieldGroup.Remove(id);
            if (returningUnit == id)
            {
                returningUnit = 0;
            }
            if (goalOwners.TryGetValue(unit.Goal, out int owner) && owner == unit.Id)
            {
                goalOwners.Remove(unit.Goal);
                goalRevision++;
            }

            foreach (var other in units)
            {
                other.LastReplan = step - 80;
            }

            Count("RemovedByCaller", 1);
            return true;
        }

        public bool ApplyWorldChange(WorldChange change)
        {
            if (disposed)
            {
                return false;
            }

            world = world.WithChange(change);
            geometry = new PathingGeometry(world);
            search = new GlobalSearch(world, geometry, Count, refinedConnections);
            eligibility.Clear();
            admissionRejections.Clear();
            goalRevision++;
            if (activeRequester != 0)
            {
                Count("YieldGroupsAbortedByWorldChange", 1);
                activeRequester = 0;
                returningUnit = 0;
                yieldGroup.Clear();
            }
            Count("WorldChanges", 1);
            foreach (var unit in units)
            {
                if (!Eligible(unit.Profile, unit.TargetId).Holds[unit.Goal] && unit.Request.AssignedGoalCell < 0)
                {
                    goalOwners.Remove(unit.Goal);
                    int replacement = SelectGoal(unit.Profile, unit.TargetId, search.Reachable(unit.Position, unit.Profile), unit.Position, false);
                    if (replacement >= 0)
                    {
                        unit.Goal = replacement;
                        goalOwners.Add(replacement, unit.Id);
                        Count("GoalReassignments", 1);
                    }
                    else
                    {
                        goalOwners[unit.Goal] = unit.Id;
                    }
                }

                unit.YieldCell = -1;
                unit.RetreatCell = -1;
                unit.ResumeAfterGate = 0;
                SetPath(unit, search.Find(unit.Position, unit.Goal, unit.Profile));
                unit.LastReplan = step;
            }

            return true;
        }

        public IReadOnlyList<UnitSnapshot> Snapshot()
        {
            return units.Select(unit => new UnitSnapshot(unit.Id, unit.Position, State(unit), unit.Goal, unit.TargetId)).ToArray();
        }

        public void Dispose()
        {
            disposed = true;
            units.Clear();
            goalOwners.Clear();
            eligibility.Clear();
            admissionRejections.Clear();
            yieldGroup.Clear();
            activeRequester = 0;
        }

        private GoalEligibility Eligible(UnitProfile profile, int targetId)
        {
            var key = Tuple.Create(profile.RadiusMm, profile.AttackRangeMm, targetId);
            if (!eligibility.TryGetValue(key, out var result))
            {
                var holds = new bool[world.CellCount];
                var attacks = new bool[world.CellCount];
                for (int cell = 0; cell < holds.Length; cell++)
                {
                    holds[cell] = geometry.CanHold(cell, profile, targetId);
                    attacks[cell] = holds[cell] && geometry.CanAttack(cell, profile, targetId);
                }

                result = new GoalEligibility(holds, attacks);
                eligibility.Add(key, result);
            }

            return result;
        }

        private int SelectGoal(UnitProfile profile, int targetId, double[] reachable, Point2 from, bool attackOnly)
        {
            var mask = Eligible(profile, targetId);
            var target = world.Target(targetId);
            int best = -1;
            double bestScore = double.PositiveInfinity;
            for (int cell = 0; cell < world.CellCount; cell++)
            {
                if (!mask.Holds[cell] || (attackOnly && !mask.Attacks[cell]) || !CanClaim(cell, profile, targetId, reachable))
                {
                    continue;
                }

                int compatibility = profiles.Count(other => Eligible(other, targetId).Holds[cell]);
                double score = (mask.Attacks[cell] ? 0 : 100000000) + compatibility * 1000000
                    - Point2.Distance(world.Center(cell), from) * 10
                    - (mask.Attacks[cell] ? Point2.Distance(world.Center(cell), target.Center) : 0);
                if (score < bestScore || (score == bestScore && cell < best))
                {
                    best = cell;
                    bestScore = score;
                }
            }

            return best;
        }

        private bool CanClaim(int cell, UnitProfile profile, int targetId, double[] reachable)
        {
            return cell >= 0 && cell < world.CellCount && !goalOwners.ContainsKey(cell) && Eligible(profile, targetId).Holds[cell]
                && !double.IsPositiveInfinity(reachable[cell]) && !units.Any(unit => unit.YieldCell == cell || unit.RetreatCell == cell);
        }

        private MotionState State(MobileUnit unit)
        {
            if (unit.YieldCell >= 0 || unit.RetreatCell >= 0 || Point2.Distance(unit.Position, world.Center(unit.Goal)) > .001)
            {
                return MotionState.Moving;
            }

            return Eligible(unit.Profile, unit.TargetId).Attacks[unit.Goal] ? MotionState.Attacking : MotionState.Waiting;
        }

        private Point2 PreferredPosition(MobileUnit unit)
        {
            int destination = unit.RetreatCell >= 0 ? unit.RetreatCell : unit.YieldCell >= 0 ? unit.YieldCell : unit.Goal;
            if (Point2.Distance(unit.Position, world.Center(destination)) <= .001)
            {
                return unit.Position;
            }

            AdvanceWaypoint(unit);
            if (unit.Waypoint >= unit.Path.Count)
            {
                if (step - unit.LastReplan >= 80)
                {
                    SetPath(unit, search.Find(unit.Position, destination, unit.Profile));
                    unit.LastReplan = step;
                }

                return unit.Position;
            }

            Point2 delta = unit.Path[unit.Waypoint] - unit.Position;
            double maximum = unit.Profile.SpeedMmPerTick / (double)SimulationProtocol.Substeps;
            return unit.Position + delta.Normalized * Math.Min(delta.Length, maximum);
        }

        private void AdvanceWaypoint(MobileUnit unit)
        {
            while (unit.Waypoint < unit.Path.Count && Point2.Distance(unit.Position, unit.Path[unit.Waypoint]) <= .001)
            {
                unit.Waypoint++;
            }
        }

        private void ResolveBodyContacts(Point2[] starts, Point2[] next)
        {
            bool changed;
            do
            {
                changed = false;
                for (int first = 0; first < units.Count; first++)
                {
                    for (int second = first + 1; second < units.Count; second++)
                    {
                        double distance = units[first].Profile.RadiusMm + units[second].Profile.RadiusMm - .001;
                        if (PathingGeometry.MovingPairSquared(starts[first], next[first], starts[second], next[second]) >= distance * distance)
                        {
                            continue;
                        }

                        bool firstMoves = Point2.Distance(starts[first], next[first]) > .001;
                        bool secondMoves = Point2.Distance(starts[second], next[second]) > .001;
                        if (!firstMoves && !secondMoves)
                        {
                            Count("ExistingOverlapDetected", 1);
                            continue;
                        }

                        bool firstAlone = firstMoves && PathingGeometry.MovingPairSquared(starts[first], next[first], starts[second], starts[second])
                            >= distance * distance;
                        bool secondAlone = secondMoves && PathingGeometry.MovingPairSquared(starts[first], starts[first], starts[second], next[second])
                            >= distance * distance;
                        int stopped = !firstMoves ? second : !secondMoves ? first
                            : firstAlone && !secondAlone ? second : secondAlone && !firstAlone ? first
                            : (units[first].BlockedSteps > units[second].BlockedSteps ? second
                                : units[first].BlockedSteps < units[second].BlockedSteps ? first
                                : ((first + step) % Math.Max(1, units.Count) < (second + step) % Math.Max(1, units.Count) ? second : first));
                        next[stopped] = starts[stopped];
                        changed = true;
                        Count("SpatialCollisionStops", 1);
                    }
                }
            }
            while (changed);
        }

        private void RepairPath(MobileUnit unit)
        {
            unit.LastReplan = step;
            if (unit.RetreatCell >= 0 && Point2.Distance(unit.Position, world.Center(unit.RetreatCell)) <= .001)
            {
                if (unit.ResumeAfterGate == 0)
                {
                    SetPath(unit, search.Find(unit.Position, unit.Goal, unit.Profile));
                    TryRequestYield(unit);
                }
                return;
            }
            int destination = unit.RetreatCell >= 0 ? unit.RetreatCell : unit.YieldCell >= 0 ? unit.YieldCell : unit.Goal;
            Func<Point2, Point2, bool> clear = (from, to) => DynamicClear(unit, from, to);
            int finalRejoin = Math.Min(unit.Path.Count - 1, unit.Waypoint + 8);
            for (int rejoin = finalRejoin; rejoin >= unit.Waypoint; rejoin--)
            {
                int cell = world.CellAt(unit.Path[rejoin]);
                if (cell < 0 || Point2.Distance(unit.Path[rejoin], world.Center(cell)) > .001)
                {
                    Count("RefinedQuarterRejoinsSkipped", 1);
                    continue;
                }

                var local = search.Find(unit.Position, cell, unit.Profile, clear, 6);
                if (local != null)
                {
                    local.AddRange(unit.Path.Skip(rejoin + 1));
                    SetPath(unit, local);
                    Count("LocalRejoins", 1);
                    return;
                }
            }

            var path = search.Find(unit.Position, destination, unit.Profile, clear);
            if (path != null)
            {
                SetPath(unit, path);
                Count("DynamicGlobalRepaths", 1);
                return;
            }

            if (friendlyYield && unit.BlockedSteps >= 160 && unit.YieldCell < 0 && unit.RetreatCell < 0)
            {
                TryRequestYield(unit);
            }
        }

        private bool DynamicClear(MobileUnit self, Point2 from, Point2 to)
        {
            foreach (var other in units)
            {
                if (other.Id == self.Id)
                {
                    continue;
                }

                double distance = self.Profile.RadiusMm + other.Profile.RadiusMm - .001;
                if (PathingGeometry.PointSegmentSquared(other.Position, from, to) < distance * distance)
                {
                    return false;
                }
            }

            return true;
        }

        private void TryRequestYield(MobileUnit requester)
        {
            if (groupedYield && activeRequester != 0 && requester.Id != (groupReturning ? returningUnit : activeRequester))
            {
                return;
            }
            if (requester.Waypoint >= requester.Path.Count)
            {
                SetPath(requester, search.Find(requester.Position, requester.Goal, requester.Profile));
            }

            Point2 nextPoint = requester.Waypoint < requester.Path.Count ? requester.Path[requester.Waypoint] : world.Center(requester.Goal);
            var blocker = units.Where(other => other.Id != requester.Id && other.YieldCell < 0
                && (!groupedYield || other.Id != activeRequester)
                && (State(other) != MotionState.Moving || other.BlockedSteps >= 80)
                && Point2.Distance(requester.Position, other.Position) <= world.CellSizeMm * 2.5
                && PathingGeometry.PointSegmentSquared(other.Position, requester.Position, nextPoint)
                    < Math.Pow(requester.Profile.RadiusMm + other.Profile.RadiusMm + 5, 2))
                .OrderBy(other => Point2.Distance(requester.Position, other.Position)).ThenBy(other => other.Id).FirstOrDefault();
            if (blocker == null)
            {
                return;
            }

            Count("FriendlyYieldRequests", 1);
            int origin = world.CellAt(blocker.Position);
            var candidates = Enumerable.Range(0, world.CellCount).Where(cell => cell != blocker.Goal && !goalOwners.ContainsKey(cell)
                && !world.SpawnCells.Contains(cell) && geometry.CanStand(cell, blocker.Profile.RadiusMm)
                && Math.Abs(cell % world.Width - origin % world.Width) <= 6 && Math.Abs(cell / world.Width - origin / world.Width) <= 6
                && !units.Any(other => other.YieldCell == cell) && !BlocksRequesterRoute(requester, blocker, cell))
                .OrderBy(cell => Point2.Distance(blocker.Position, world.Center(cell))).ThenBy(cell => cell).Take(32);
            foreach (int cell in candidates)
            {
                var path = search.Find(blocker.Position, cell, blocker.Profile, (from, to) => DynamicClear(blocker, from, to));
                if (path == null)
                {
                    continue;
                }

                blocker.YieldCell = cell;
                goalRevision++;
                blocker.YieldRequester = requester.Id;
                blocker.YieldStarted = step;
                blocker.RequesterStartGoalDistance = Point2.Distance(requester.Position, world.Center(requester.Goal));
                if (requester.RetreatCell >= 0)
                {
                    requester.ResumeAfterGate = blocker.Id;
                }
                SetPath(blocker, path);
                blocker.BlockedSteps = 0;
                Count("FriendlyYieldAccepted", 1);
                if (groupedYield)
                {
                    if (activeRequester == 0)
                    {
                        activeRequester = requester.Id;
                        groupStarted = step;
                        groupReturning = false;
                        returningUnit = 0;
                        Count("YieldGroupsStarted", 1);
                    }
                    if (!yieldGroup.Contains(blocker.Id))
                    {
                        yieldGroup.Add(blocker.Id);
                    }
                }
                return;
            }

            Count("FriendlyYieldNoSidePocket", 1);
            if (NeedsConstrainedBackoff(requester) && requester.RetreatCell < 0)
            {
                TryRequesterRetreat(requester);
            }
        }

        private bool BlocksRequesterRoute(MobileUnit requester, MobileUnit blocker, int cell)
        {
            Point2 previous = requester.Position;
            Point2 temporary = world.Center(cell);
            double clearance = requester.Profile.RadiusMm + blocker.Profile.RadiusMm + 10;
            foreach (var next in requester.Path.Skip(requester.Waypoint).Take(6))
            {
                if (PathingGeometry.PointSegmentSquared(temporary, previous, next) < clearance * clearance)
                {
                    return true;
                }

                previous = next;
            }

            return false;
        }

        private void UpdateYield(MobileUnit unit)
        {
            if (unit.YieldCell < 0)
            {
                return;
            }

            var requester = units.FirstOrDefault(other => other.Id == unit.YieldRequester);
            bool passed = requester == null || (Point2.Distance(requester.Position, world.Center(unit.Goal))
                > requester.Profile.RadiusMm + unit.Profile.RadiusMm + world.CellSizeMm
                && Point2.Distance(requester.Position, world.Center(requester.Goal)) < unit.RequesterStartGoalDistance - world.CellSizeMm * .75);
            bool arrived = requester != null && State(requester) != MotionState.Moving;
            bool timedOut = step - unit.YieldStarted >= 6000;
            if ((!passed || (requester != null && NeedsConstrainedBackoff(requester))) && !arrived && !timedOut)
            {
                return;
            }

            unit.YieldCell = -1;
            goalRevision++;
            unit.YieldRequester = 0;
            SetPath(unit, search.Find(unit.Position, unit.Goal, unit.Profile));
            unit.LastReplan = step;
            Count(timedOut ? "FriendlyYieldTimeouts" : "FriendlyYieldCompleted", 1);
        }

        private void TryRequesterRetreat(MobileUnit unit)
        {
            var target = world.Target(unit.TargetId);
            double fromTarget = Point2.Distance(unit.Position, target.Center);
            int origin = world.CellAt(unit.Position);
            var candidates = Enumerable.Range(0, world.CellCount).Where(cell => !goalOwners.ContainsKey(cell)
                && !world.SpawnCells.Contains(cell) && geometry.CanStand(cell, unit.Profile.RadiusMm)
                && !units.Any(other => other.YieldCell == cell || other.RetreatCell == cell)
                && Math.Abs(cell % world.Width - origin % world.Width) <= 4 && Math.Abs(cell / world.Width - origin / world.Width) <= 4
                && Point2.Distance(world.Center(cell), target.Center) >= fromTarget + world.CellSizeMm
                && Point2.Distance(world.Center(cell), unit.Position) >= world.CellSizeMm * 1.5)
                .OrderBy(cell => Point2.Distance(world.Center(cell), unit.Position)).ThenBy(cell => cell).Take(32);
            foreach (int cell in candidates)
            {
                var path = search.Find(unit.Position, cell, unit.Profile, (from, to) => DynamicClear(unit, from, to));
                if (path == null)
                {
                    continue;
                }

                unit.RetreatCell = cell;
                unit.RetreatStarted = step;
                unit.ResumeAfterGate = 0;
                unit.BlockedSteps = 0;
                goalRevision++;
                SetPath(unit, path);
                Count("RequesterRetreatsStarted", 1);
                return;
            }

            Count("RequesterRetreatsBlocked", 1);
        }

        private bool NeedsConstrainedBackoff(MobileUnit unit)
        {
            return requesterBackoff && profiles.Any(profile => !Eligible(profile, unit.TargetId).Holds[unit.Goal]);
        }

        private bool HasConstrainedTraffic()
        {
            if (!requesterBackoff)
            {
                return false;
            }
            foreach (var unit in units)
            {
                if (unit.RetreatCell >= 0)
                {
                    return true;
                }
                if (unit.YieldCell >= 0)
                {
                    var requester = units.FirstOrDefault(other => other.Id == unit.YieldRequester);
                    if (requester != null && NeedsConstrainedBackoff(requester))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private void UpdateRetreat(MobileUnit unit)
        {
            if (unit.RetreatCell < 0)
            {
                return;
            }

            var gate = units.FirstOrDefault(other => other.Id == unit.ResumeAfterGate);
            bool gateReady = unit.ResumeAfterGate != 0 && (gate == null || gate.YieldCell < 0
                || Point2.Distance(gate.Position, world.Center(gate.YieldCell)) <= .001);
            bool timedOut = step - unit.RetreatStarted >= 4000;
            if (!gateReady && !timedOut)
            {
                return;
            }

            unit.RetreatCell = -1;
            unit.ResumeAfterGate = 0;
            unit.BlockedSteps = 0;
            goalRevision++;
            SetPath(unit, search.Find(unit.Position, unit.Goal, unit.Profile));
            unit.LastReplan = step;
            Count(timedOut ? "RequesterRetreatTimeouts" : "RequesterRetreatsCompleted", 1);
        }

        private void PromoteWaiting()
        {
            foreach (var unit in units)
            {
                if (State(unit) != MotionState.Waiting || unit.Request.AssignedGoalCell >= 0)
                {
                    continue;
                }

                var attacks = Eligible(unit.Profile, unit.TargetId).Attacks;
                if (!Enumerable.Range(0, world.CellCount).Any(cell => attacks[cell] && !goalOwners.ContainsKey(cell)))
                {
                    continue;
                }

                int goal = SelectGoal(unit.Profile, unit.TargetId, search.Reachable(unit.Position, unit.Profile), unit.Position, true);
                if (goal < 0)
                {
                    continue;
                }

                var path = search.Find(unit.Position, goal, unit.Profile);
                if (path == null)
                {
                    continue;
                }

                goalOwners.Remove(unit.Goal);
                unit.Goal = goal;
                goalOwners[goal] = unit.Id;
                goalRevision++;
                SetPath(unit, path);
                unit.LastReplan = step;
                Count("AttackPromotions", 1);
            }
        }

        private bool CanMoveInGroup(MobileUnit unit)
        {
            return !groupedYield || activeRequester == 0 || unit.YieldCell >= 0
                || unit.Id == (groupReturning ? returningUnit : activeRequester);
        }

        private void UpdateGroup()
        {
            if (activeRequester == 0)
            {
                return;
            }

            var requester = units.FirstOrDefault(unit => unit.Id == activeRequester);
            if (!groupReturning && (requester == null || State(requester) != MotionState.Moving || step - groupStarted > 20000))
            {
                groupReturning = true;
                Count(step - groupStarted > 20000 ? "YieldGroupTimeouts" : "YieldGroupPassagesCompleted", 1);
            }

            if (!groupReturning)
            {
                return;
            }

            if (returningUnit != 0)
            {
                var returning = units.FirstOrDefault(unit => unit.Id == returningUnit);
                if (returning != null && State(returning) == MotionState.Moving)
                {
                    return;
                }
                yieldGroup.Remove(returningUnit);
                returningUnit = 0;
                Count("FriendlyYieldCompleted", 1);
            }

            yieldGroup.RemoveAll(id => !units.Any(unit => unit.Id == id));
            if (yieldGroup.Count == 0)
            {
                activeRequester = 0;
                groupReturning = false;
                Count("YieldGroupsCompleted", 1);
                return;
            }

            returningUnit = yieldGroup[yieldGroup.Count - 1];
            var next = units.Single(unit => unit.Id == returningUnit);
            next.YieldCell = -1;
            next.YieldRequester = 0;
            goalRevision++;
            SetPath(next, search.Find(next.Position, next.Goal, next.Profile));
            next.LastReplan = step;
            Count("YieldGroupReturnStarts", 1);
        }

        private static void SetPath(MobileUnit unit, List<Point2>? path)
        {
            unit.Path = path ?? new List<Point2>();
            unit.Waypoint = 0;
        }

        private void Count(string key, long amount)
        {
            metrics.TryGetValue(key, out long previous);
            metrics[key] = previous + amount;
        }

        private bool Reject(string reason)
        {
            LastSpawnFailure = reason;
            Count("Rejected_" + reason, 1);
            return false;
        }
    }
}
