using System;
using System.Collections.Generic;
using System.Linq;
using NavigationLab.Core;

namespace NavigationLab.Oracle
{
    /// <summary>Independent continuous geometry. Candidates reference Core only, never this assembly.</summary>
    public sealed class ContinuousOracle
    {
        private readonly Dictionary<int, SpawnRequest> live = new Dictionary<int, SpawnRequest>();
        public WorldDefinition World { get; private set; }
        public IReadOnlyDictionary<int, SpawnRequest> Live => live;
        public long PairSweeps { get; private set; }
        public long BodySweeps { get; private set; }
        public double MinimumBodyGapMm { get; private set; } = double.PositiveInfinity;
        public double MaximumObservedStepMm { get; private set; }

        public ContinuousOracle(WorldDefinition world)
        {
            World = world.Clone();
        }

        public void RegisterBirth(SpawnRequest request, IReadOnlyList<UnitSnapshot> before, IReadOnlyList<UnitSnapshot> after)
        {
            Require(!live.ContainsKey(request.Id), "Duplicate admitted ID " + request.Id);
            Require(after.Count == before.Count + 1, "Accepted spawn did not create exactly one physical body.");
            VerifyUnchangedBodies(before, after, -1);
            var spawned = after.Single(unit => unit.Id == request.Id);
            Require(spawned.Position == World.Center(request.SpawnCell), "Spawn position differs from original input centre.");
            live.Add(request.Id, request);
            ValidateTransition(after, after, 0);
        }

        public void VerifyRejectedBirth(IReadOnlyList<UnitSnapshot> before, IReadOnlyList<UnitSnapshot> after)
        {
            Require(before.Count == after.Count, "Rejected spawn changed live body count.");
            VerifyUnchangedBodies(before, after, -1);
            VerifyIdentity(after);
            ValidateTransition(after, after, 0);
        }

        public void RegisterDeath(int id, IReadOnlyList<UnitSnapshot> before, IReadOnlyList<UnitSnapshot> after)
        {
            Require(live.Remove(id), "Removed a body that was never admitted.");
            Require(after.Count == before.Count - 1 && after.All(unit => unit.Id != id), "Removal did not remove exactly the explicit ID.");
            VerifyUnchangedBodies(before, after, id);
            VerifyIdentity(after);
            ValidateTransition(after, after, 0);
        }

        public void ApplyChange(WorldChange change, IReadOnlyList<UnitSnapshot> before, IReadOnlyList<UnitSnapshot> after)
        {
            VerifyUnchangedBodies(before, after, -1);
            var next = World.WithChange(change.DeepClone());
            foreach (var unit in before)
            {
                Require(CircleFits(next, unit.Position, live[unit.Id].Profile.RadiusMm), "INVALID TEST INPUT: world change overlaps a live body.");
            }
            World = next;
            ValidateTransition(after, after, 0);
        }

        public void ValidateTransition(IReadOnlyList<UnitSnapshot> before, IReadOnlyList<UnitSnapshot> after, int milliseconds)
        {
            VerifyIdentity(after);
            var previous = before.ToDictionary(unit => unit.Id);
            foreach (var unit in after)
            {
                Require(!double.IsNaN(unit.Position.X) && !double.IsInfinity(unit.Position.X) && !double.IsNaN(unit.Position.Y) && !double.IsInfinity(unit.Position.Y), "Nonfinite body position.");
                var request = live[unit.Id];
                Require(unit.TargetId == request.TargetId, "Navigator changed the unit's requested target.");
                Point2 from = previous.TryGetValue(unit.Id, out var old) ? old.Position : unit.Position;
                double moved = Point2.Distance(from, unit.Position);
                double maximum = request.Profile.SpeedMmPerTick * (double)milliseconds / SimulationProtocol.LogicalTickMilliseconds;
                Require(moved <= maximum + SimulationProtocol.GeometryToleranceMm, "Speed/teleport violation for ID " + unit.Id);
                MaximumObservedStepMm = Math.Max(MaximumObservedStepMm, moved);
                Require(SegmentFits(World, from, unit.Position, request.Profile.RadiusMm), "Static obstacle/base/world penetration for ID " + unit.Id);
                BodySweeps++;
                if (unit.State != MotionState.Moving)
                {
                    Require(unit.GoalCell >= 0 && unit.GoalCell < World.CellCount, "Settled body has no valid goal.");
                    Require(World.HoldingAllowed(unit.GoalCell), "Settled in transit-only, spawn or blocked cell.");
                    Require(Point2.Distance(unit.Position, World.Center(unit.GoalCell)) <= SimulationProtocol.GoalToleranceMm, "Settled body is not physically at its goal.");
                    if (request.AssignedGoalCell >= 0)
                    {
                        Require(unit.GoalCell == request.AssignedGoalCell, "Explicit destination changed.");
                    }
                    var target = World.Target(request.TargetId);
                    if (unit.State == MotionState.Waiting)
                    {
                        Require(Point2.Distance(unit.Position, target.Center) <= target.RadiusMm + World.GatheringDistanceMm + 0.1, "Waiting outside target gathering radius.");
                    }
                    bool attack = CanAttack(World, unit.Position, request.Profile, target);
                    Require((unit.State == MotionState.Attacking) == attack, "Attack state disagrees with physical range/line of sight.");
                }
            }
            for (int first = 0; first < after.Count; first++)
            {
                var a = after[first];
                Point2 a0 = previous.TryGetValue(a.Id, out var oldA) ? oldA.Position : a.Position;
                for (int second = first + 1; second < after.Count; second++)
                {
                    var b = after[second];
                    Point2 b0 = previous.TryGetValue(b.Id, out var oldB) ? oldB.Position : b.Position;
                    Point2 relativeStart = a0 - b0;
                    Point2 relativeVelocity = (a.Position - a0) - (b.Position - b0);
                    double time = relativeVelocity.LengthSquared > 1e-20 ? Clamp(-Point2.Dot(relativeStart, relativeVelocity) / relativeVelocity.LengthSquared, 0, 1) : 0;
                    double gap = (relativeStart + relativeVelocity * time).Length - live[a.Id].Profile.RadiusMm - live[b.Id].Profile.RadiusMm;
                    Require(gap >= -SimulationProtocol.GeometryToleranceMm, "Continuous body collision between " + a.Id + " and " + b.Id);
                    MinimumBodyGapMm = Math.Min(MinimumBodyGapMm, gap);
                    PairSweeps++;
                }
            }
        }

        public int MaximumEndpointAttackers()
        {
            var owners = new Dictionary<int, int>();
            var requests = live.Values.OrderBy(request => request.Profile.AttackRangeMm).ThenBy(request => request.Id).ToArray();
            var options = requests.ToDictionary(request => request.Id, request => Enumerable.Range(0, World.CellCount).Where(cell =>
                World.HoldingAllowed(cell) && CircleFits(World, World.Center(cell), request.Profile.RadiusMm)
                && CanAttack(World, World.Center(cell), request.Profile, World.Target(request.TargetId))).ToArray());
            int matched = 0;
            foreach (var request in requests)
            {
                if (Match(request.Id, new HashSet<int>(), options, owners))
                {
                    matched++;
                }
            }
            return matched;
        }

        public void VerifyIdentity(IReadOnlyList<UnitSnapshot> snapshot)
        {
            Require(snapshot.Count == live.Count && snapshot.Select(unit => unit.Id).Distinct().Count() == snapshot.Count
                && snapshot.All(unit => live.ContainsKey(unit.Id)), "Live ID conservation failed; bodies disappeared or unknown bodies appeared.");
        }

        public static void VerifyUnchangedBodies(IReadOnlyList<UnitSnapshot> before, IReadOnlyList<UnitSnapshot> after, int removedId)
        {
            var current = after.ToDictionary(unit => unit.Id);
            foreach (var unit in before)
            {
                if (unit.Id != removedId)
                {
                    Require(current.TryGetValue(unit.Id, out var actual) && actual.Position == unit.Position, "Instantaneous mutation moved or hid an existing body.");
                }
            }
        }

        public static bool CircleFits(WorldDefinition world, Point2 point, double radius) => SegmentFits(world, point, point, radius);

        public static bool SegmentFits(WorldDefinition world, Point2 from, Point2 to, double radius)
        {
            double epsilon = SimulationProtocol.GeometryToleranceMm;
            if (Math.Min(from.X, to.X) - radius < world.Origin.X - epsilon || Math.Min(from.Y, to.Y) - radius < world.Origin.Y - epsilon
                || Math.Max(from.X, to.X) + radius > world.Origin.X + world.Width * world.CellSizeMm + epsilon
                || Math.Max(from.Y, to.Y) + radius > world.Origin.Y + world.Height * world.CellSizeMm + epsilon)
            {
                return false;
            }
            foreach (var target in world.Targets)
            {
                if (DistanceToSegment(target.Center, from, to) + epsilon < radius + target.RadiusMm)
                {
                    return false;
                }
            }
            int x0 = Math.Max(0, (int)Math.Floor((Math.Min(from.X, to.X) - radius - world.Origin.X) / world.CellSizeMm));
            int x1 = Math.Min(world.Width - 1, (int)Math.Floor((Math.Max(from.X, to.X) + radius - world.Origin.X) / world.CellSizeMm));
            int y0 = Math.Max(0, (int)Math.Floor((Math.Min(from.Y, to.Y) - radius - world.Origin.Y) / world.CellSizeMm));
            int y1 = Math.Min(world.Height - 1, (int)Math.Floor((Math.Max(from.Y, to.Y) + radius - world.Origin.Y) / world.CellSizeMm));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!world.IsWalkable(y * world.Width + x))
                    {
                        double left = world.Origin.X + x * world.CellSizeMm;
                        double bottom = world.Origin.Y + y * world.CellSizeMm;
                        if (SegmentRectangleDistance(from, to, left, bottom, left + world.CellSizeMm, bottom + world.CellSizeMm) + epsilon < radius)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public static bool CanAttack(WorldDefinition world, Point2 point, UnitProfile profile, TargetDefinition target)
        {
            if (Point2.Distance(point, target.Center) > profile.RadiusMm + profile.AttackRangeMm + target.RadiusMm + 0.1)
            {
                return false;
            }
            int x0 = Math.Max(0, (int)Math.Floor((Math.Min(point.X, target.Center.X) - world.Origin.X) / world.CellSizeMm));
            int x1 = Math.Min(world.Width - 1, (int)Math.Floor((Math.Max(point.X, target.Center.X) - world.Origin.X) / world.CellSizeMm));
            int y0 = Math.Max(0, (int)Math.Floor((Math.Min(point.Y, target.Center.Y) - world.Origin.Y) / world.CellSizeMm));
            int y1 = Math.Min(world.Height - 1, (int)Math.Floor((Math.Max(point.Y, target.Center.Y) - world.Origin.Y) / world.CellSizeMm));
            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (!world.IsWalkable(y * world.Width + x))
                    {
                        double left = world.Origin.X + x * world.CellSizeMm;
                        double bottom = world.Origin.Y + y * world.CellSizeMm;
                        if (SegmentRectangleDistance(point, target.Center, left, bottom, left + world.CellSizeMm, bottom + world.CellSizeMm) < 1e-7)
                        {
                            return false;
                        }
                    }
                }
            }
            return true;
        }

        public static double DistanceToSegment(Point2 point, Point2 from, Point2 to)
        {
            var vector = to - from;
            double t = vector.LengthSquared > 1e-20 ? Clamp(Point2.Dot(point - from, vector) / vector.LengthSquared, 0, 1) : 0;
            return Point2.Distance(point, from + vector * t);
        }

        private static double SegmentRectangleDistance(Point2 a, Point2 b, double left, double bottom, double right, double top)
        {
            if ((a.X >= left && a.X <= right && a.Y >= bottom && a.Y <= top) || (b.X >= left && b.X <= right && b.Y >= bottom && b.Y <= top))
            {
                return 0;
            }
            var corners = new[] { new Point2(left, bottom), new Point2(right, bottom), new Point2(right, top), new Point2(left, top) };
            double distance = double.PositiveInfinity;
            for (int edge = 0; edge < 4; edge++)
            {
                var c = corners[edge];
                var d = corners[(edge + 1) % 4];
                if (Intersects(a, b, c, d))
                {
                    return 0;
                }
                distance = Math.Min(distance, Math.Min(Math.Min(DistanceToSegment(a, c, d), DistanceToSegment(b, c, d)),
                    Math.Min(DistanceToSegment(c, a, b), DistanceToSegment(d, a, b))));
            }
            return distance;
        }

        private static bool Intersects(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            double cross(Point2 u, Point2 v) => u.X * v.Y - u.Y * v.X;
            double first = cross(b - a, c - a);
            double second = cross(b - a, d - a);
            double third = cross(d - c, a - c);
            double fourth = cross(d - c, b - c);
            return first * second <= 0 && third * fourth <= 0
                && Math.Max(Math.Min(a.X, b.X), Math.Min(c.X, d.X)) <= Math.Min(Math.Max(a.X, b.X), Math.Max(c.X, d.X)) + 1e-9
                && Math.Max(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y)) <= Math.Min(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y)) + 1e-9;
        }

        private static bool Match(int id, HashSet<int> seen, Dictionary<int, int[]> options, Dictionary<int, int> owners)
        {
            foreach (int cell in options[id])
            {
                if (seen.Add(cell) && (!owners.TryGetValue(cell, out int old) || Match(old, seen, options, owners)))
                {
                    owners[cell] = id;
                    return true;
                }
            }
            return false;
        }
        private static double Clamp(double value, double minimum, double maximum) => Math.Max(minimum, Math.Min(maximum, value));
        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }
    }
}
