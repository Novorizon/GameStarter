using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GameStarter.Verification.CrowdFlow
{
    internal readonly struct RouteTrafficBody
    {
        internal readonly int Id;
        internal readonly int RouteId;
        internal readonly float Progress;
        internal readonly float DesiredProgress;
        internal readonly float Radius;

        internal RouteTrafficBody(int id, int routeId, float progress, float desiredProgress, float radius)
        {
            Id = id;
            RouteId = routeId;
            Progress = progress;
            DesiredProgress = desiredProgress;
            Radius = radius;
        }
    }

    /// <summary>Admits road traffic through overlapping route corridors without moving the agents itself.</summary>
    internal sealed class RouteConflictCoordinator
    {
        private const float Epsilon = 0.00001f;

        private readonly struct Span
        {
            internal readonly float Start;
            internal readonly float End;

            internal Span(float start, float end)
            {
                Start = start;
                End = end;
            }

            internal bool Intersects(Span other) => Start <= other.End + Epsilon && other.Start <= End + Epsilon;
            internal bool Includes(Span other) => Start <= other.Start && End >= other.End;
            internal Span Combine(Span other) => new(MathF.Min(Start, other.Start), MathF.Max(End, other.End));
        }

        private readonly struct Conflict
        {
            internal readonly Span First;
            internal readonly Span Second;

            internal Conflict(Span first, Span second)
            {
                First = first;
                Second = second;
            }
        }

        private readonly struct Constraint
        {
            internal readonly Plan Other;
            internal readonly Span Own;
            internal readonly Span Opposing;

            internal Constraint(Plan other, Span own, Span opposing)
            {
                Other = other;
                Own = own;
                Opposing = opposing;
            }
        }

        private readonly struct Ticket
        {
            internal readonly long Tick;
            internal readonly Span Gate;

            internal Ticket(long tick, Span gate)
            {
                Tick = tick;
                Gate = gate;
            }
        }

        private sealed class Gate
        {
            internal Span Span;
            internal readonly List<Constraint> Constraints = new();

            internal Gate(Span span)
            {
                Span = span;
            }
        }

        private sealed class Plan
        {
            internal readonly RouteTrafficBody Body;
            internal readonly Route Route;
            internal readonly List<Constraint> Constraints = new();
            internal readonly List<Gate> Gates = new();
            internal readonly float Desired;
            internal Gate Request;
            internal float RequestStart;
            internal Ticket Ticket;

            internal Plan(RouteTrafficBody body, Route route)
            {
                Body = body;
                Route = route;
                Desired = Math.Clamp(body.DesiredProgress, body.Progress, route.Length);
            }
        }

        private readonly struct Segment
        {
            internal readonly Vector2 Start;
            internal readonly Vector2 End;
            internal readonly float ArcStart;
            internal readonly float Length;

            internal Segment(Vector2 start, Vector2 end, float arcStart, float length)
            {
                Start = start;
                End = end;
                ArcStart = arcStart;
                Length = length;
            }

            internal Vector2 Direction => (End - Start) / Length;
            internal float ArcEnd => ArcStart + Length;
            internal Vector2 Position(float arc) => Vector2.Lerp(Start, End, Math.Clamp((arc - ArcStart) / Length, 0, 1));
        }

        private sealed class Route
        {
            internal readonly Segment[] Segments;
            internal readonly float Length;

            internal Route(IReadOnlyList<Vector2> points)
            {
                if (points == null || points.Count < 2 || points.Any(point => !IsFinite(point)))
                {
                    throw new ArgumentException("An authored route needs at least two finite points.");
                }

                var segments = new List<Segment>();
                float arc = 0;
                for (int i = 1; i < points.Count; i++)
                {
                    float length = Vector2.Distance(points[i - 1], points[i]);
                    if (length > 0)
                    {
                        segments.Add(new Segment(points[i - 1], points[i], arc, length));
                        arc += length;
                    }
                }

                if (segments.Count == 0 || !float.IsFinite(arc))
                {
                    throw new ArgumentException("An authored route needs finite, nonzero length.");
                }

                Segments = segments.ToArray();
                Length = arc;
            }

            internal Vector2 Position(float arc)
            {
                foreach (var segment in Segments)
                {
                    if (arc <= segment.ArcEnd)
                    {
                        return segment.Position(arc);
                    }
                }

                return Segments[^1].End;
            }
        }

        private readonly float padding;
        private readonly Dictionary<int, Route> routes = new();
        private readonly Dictionary<(int First, int Second, float Clearance), Conflict[]> conflicts = new();
        private readonly Dictionary<int, float> allowed = new();
        private Dictionary<int, Ticket> tickets = new();
        private long tick;

        internal RouteConflictCoordinator(IReadOnlyDictionary<int, IReadOnlyList<Vector2>> routes, float padding = 0.002f)
        {
            if (routes == null)
            {
                throw new ArgumentNullException(nameof(routes));
            }
            if (!float.IsFinite(padding) || padding < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(padding));
            }

            this.padding = padding;
            foreach (var (id, points) in routes)
            {
                this.routes.Add(id, new Route(points));
            }
        }

        /// <summary>Uses current arc progress and this tick's desired arc progress for every active road agent.</summary>
        internal void Schedule(IReadOnlyList<RouteTrafficBody> bodies, Func<RouteTrafficBody, Vector2, Vector2, bool> canClearExit = null)
        {
            if (bodies == null)
            {
                throw new ArgumentNullException(nameof(bodies));
            }
            tick++;
            allowed.Clear();
            var plans = new List<Plan>(bodies.Count);
            foreach (var body in bodies)
            {
                if (!routes.TryGetValue(body.RouteId, out var route) || !float.IsFinite(body.Progress)
                    || !float.IsFinite(body.DesiredProgress) || !float.IsFinite(body.Radius)
                    || body.Radius <= 0 || body.Progress < 0 || body.Progress > route.Length + Epsilon)
                {
                    throw new ArgumentException("Invalid route traffic state.", nameof(bodies));
                }

                var normalized = new RouteTrafficBody(body.Id, body.RouteId, MathF.Min(body.Progress, route.Length), body.DesiredProgress, body.Radius);
                var plan = new Plan(normalized, route);
                allowed.Add(body.Id, plan.Desired);
                plans.Add(plan);
            }

            plans.Sort((a, b) => a.Body.RouteId != b.Body.RouteId
                ? a.Body.RouteId.CompareTo(b.Body.RouteId) : a.Body.Id.CompareTo(b.Body.Id));

            for (int i = 0; i < plans.Count; i++)
            {
                var first = plans[i];
                for (int j = i + 1; j < plans.Count; j++)
                {
                    var second = plans[j];
                    if (first.Body.RouteId == second.Body.RouteId)
                    {
                        continue;
                    }

                    float clearance = first.Body.Radius + second.Body.Radius + padding;
                    foreach (var conflict in ConflictsFor(first, second, clearance))
                    {
                        if (first.Body.Progress > conflict.First.End || second.Body.Progress > conflict.Second.End)
                        {
                            continue;
                        }

                        first.Constraints.Add(new Constraint(second, conflict.First, conflict.Second));
                        second.Constraints.Add(new Constraint(first, conflict.Second, conflict.First));
                    }
                }
            }

            var nextTickets = new Dictionary<int, Ticket>();
            foreach (var plan in plans)
            {
                PrepareGates(plan);
                if (plan.Request is not Gate gate)
                {
                    continue;
                }

                var requestSpan = new Span(plan.RequestStart, gate.Span.End);
                long arrival = tickets.TryGetValue(plan.Body.Id, out var previous) && previous.Gate.Intersects(requestSpan)
                    ? previous.Tick : tick;
                plan.Ticket = new Ticket(arrival, requestSpan);
                nextTickets.Add(plan.Body.Id, plan.Ticket);
            }

            tickets = nextTickets;
            foreach (var plan in plans)
            {
                if (plan.Request is not Gate gate)
                {
                    continue;
                }

                if (IsBlocked(plan, gate) || !ExitIsClear(plan, gate.Span.End, plans, canClearExit))
                {
                    allowed[plan.Body.Id] = MathF.Max(plan.Body.Progress, plan.RequestStart - Epsilon);
                    continue;
                }

                // Allow one new gate per tick. Its owner is recognized from actual progress next tick.
                foreach (var subsequent in plan.Gates)
                {
                    if (subsequent.Span.Start > gate.Span.End)
                    {
                        allowed[plan.Body.Id] = MathF.Min(plan.Desired, subsequent.Span.Start - Epsilon);
                        break;
                    }
                }
            }
        }

        internal float AllowedProgress(int id) => allowed[id];

        private static void PrepareGates(Plan plan)
        {
            plan.Constraints.Sort((a, b) =>
            {
                int order = a.Own.Start.CompareTo(b.Own.Start);
                return order != 0 ? order : a.Own.End.CompareTo(b.Own.End);
            });

            foreach (var constraint in plan.Constraints)
            {
                Gate gate = plan.Gates.Count > 0 ? plan.Gates[^1] : null;
                if (gate == null || !gate.Span.Intersects(constraint.Own))
                {
                    gate = new Gate(constraint.Own);
                    plan.Gates.Add(gate);
                }

                gate.Span = gate.Span.Combine(constraint.Own);
                gate.Constraints.Add(constraint);
            }

            foreach (var gate in plan.Gates)
            {
                foreach (var constraint in gate.Constraints)
                {
                    var span = constraint.Own;
                    if (!Occupies(plan, span) && plan.Body.Progress <= span.Start && plan.Desired > span.Start)
                    {
                        plan.Request = gate;
                        plan.RequestStart = span.Start;
                        return;
                    }
                }
            }
        }

        private static bool IsBlocked(Plan plan, Gate gate)
        {
            foreach (var constraint in gate.Constraints)
            {
                if (constraint.Own.Start < plan.RequestStart)
                {
                    // A later arrival can widen a gate around us. It does not grant the narrower crossing ahead.
                    continue;
                }

                var other = constraint.Other;
                bool hasOwner = Occupies(other, constraint.Opposing);
                bool earlierRequest = other.Request is Gate request && request.Span.Includes(constraint.Opposing)
                    && other.RequestStart <= constraint.Opposing.Start
                    && (other.Ticket.Tick < plan.Ticket.Tick || other.Ticket.Tick == plan.Ticket.Tick && other.Body.Id < plan.Body.Id);
                if (hasOwner || earlierRequest)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Occupies(Plan plan, Span span)
        {
            // A route can begin inside another route's corridor. A body present at that spawn already owns the space.
            float progress = plan.Body.Progress;
            return (progress > span.Start || span.Start == 0) && progress <= span.End;
        }

        private bool ExitIsClear(Plan plan, float exit, IReadOnlyList<Plan> traffic, Func<RouteTrafficBody, Vector2, Vector2, bool> canClearExit)
        {
            float clearAt = MathF.Min(plan.Route.Length, exit + MathF.Max(padding, Epsilon));
            foreach (var segment in plan.Route.Segments)
            {
                if (segment.ArcEnd < exit || segment.ArcStart > clearAt)
                {
                    continue;
                }

                var from = segment.Position(exit);
                var to = segment.Position(clearAt);
                if (canClearExit?.Invoke(plan.Body, from, to) == false)
                {
                    return false;
                }

                foreach (var other in traffic)
                {
                    if (other == plan)
                    {
                        continue;
                    }

                    float clearance = plan.Body.Radius + other.Body.Radius + padding;
                    if (DistanceToSegmentSquared(other.Route.Position(other.Body.Progress), from, to) < clearance * clearance)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private Conflict[] ConflictsFor(Plan first, Plan second, float clearance)
        {
            var key = (first.Body.RouteId, second.Body.RouteId, clearance);
            if (!conflicts.TryGetValue(key, out var cached))
            {
                var found = new List<Conflict>();
                foreach (var a in first.Route.Segments)
                {
                    foreach (var b in second.Route.Segments)
                    {
                        if (CapsuleSpan(a, b, clearance) is Span firstSpan && CapsuleSpan(b, a, clearance) is Span secondSpan)
                        {
                            AddConflict(found, new Conflict(firstSpan, secondSpan));
                        }
                    }
                }

                cached = found.ToArray();
                conflicts.Add(key, cached);
            }

            return cached;
        }

        private static void AddConflict(List<Conflict> found, Conflict candidate)
        {
            // Segment endpoints do not split a continuous crossing or shared corridor into separate permits.
            for (int i = 0; i < found.Count;)
            {
                var other = found[i];
                if (candidate.First.Intersects(other.First) && candidate.Second.Intersects(other.Second))
                {
                    candidate = new Conflict(candidate.First.Combine(other.First), candidate.Second.Combine(other.Second));
                    found.RemoveAt(i);
                    i = 0;
                }
                else
                {
                    i++;
                }
            }
            found.Add(candidate);
        }

        // A capsule is the union of its central strip and two endpoint disks.
        // Its intersection with a segment is a single closed interval in arc coordinates.
        private static Span? CapsuleSpan(Segment mover, Segment opposing, float radius)
        {
            var direction = mover.Direction;
            var axis = opposing.Direction;
            var delta = mover.Start - opposing.Start;
            double start = 0;
            double end = mover.Length;
            double earliest = double.PositiveInfinity;
            double latest = double.NegativeInfinity;
            if (Clip(Vector2.Dot(delta, axis), Vector2.Dot(direction, axis), 0, opposing.Length, ref start, ref end)
                && Clip(Cross(axis, delta), Cross(axis, direction), -radius, radius, ref start, ref end))
            {
                Include(start, end);
            }

            IncludeDisk(opposing.Start);
            IncludeDisk(opposing.End);
            return earliest <= latest ? new Span(mover.ArcStart + (float)earliest, mover.ArcStart + (float)latest) : null;

            void IncludeDisk(Vector2 center)
            {
                var offset = center - mover.Start;
                double cross = Cross(direction, offset);
                double remainder = (double)radius * radius - cross * cross;
                if (remainder >= 0)
                {
                    double centerArc = Vector2.Dot(direction, offset);
                    double half = Math.Sqrt(remainder);
                    Include(Math.Max(0, centerArc - half), Math.Min(mover.Length, centerArc + half));
                }
            }

            void Include(double first, double last)
            {
                if (first <= last)
                {
                    earliest = Math.Min(earliest, first);
                    latest = Math.Max(latest, last);
                }
            }
        }

        private static bool Clip(double offset, double slope, double min, double max, ref double start, ref double end)
        {
            if (slope == 0)
            {
                return offset >= min && offset <= max;
            }

            double a = (min - offset) / slope;
            double b = (max - offset) / slope;
            start = Math.Max(start, Math.Min(a, b));
            end = Math.Min(end, Math.Max(a, b));
            return start <= end;
        }

        private static double Cross(Vector2 a, Vector2 b) => (double)a.X * b.Y - (double)a.Y * b.X;

        private static float DistanceToSegmentSquared(Vector2 point, Vector2 a, Vector2 b)
        {
            var delta = b - a;
            float length = delta.LengthSquared();
            float fraction = length > 0 ? Math.Clamp(Vector2.Dot(point - a, delta) / length, 0, 1) : 0;
            return Vector2.DistanceSquared(point, a + delta * fraction);
        }

        private static bool IsFinite(Vector2 p) => float.IsFinite(p.X) && float.IsFinite(p.Y);
    }
}
