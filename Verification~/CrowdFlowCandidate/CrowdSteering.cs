using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GameStarter.Verification.CrowdFlow;

internal sealed class CrowdAgent(int id, Vector2 position, float radius, float speed)
{
    internal int Id { get; } = id;
    internal Vector2 Position { get; set; } = position;
    internal float Radius { get; } = radius;
    internal float Speed { get; } = speed;
    internal DirectionField Field { get; set; }
    internal bool CanYield { get; init; }
    internal Vector2 Heading { get; set; }
    internal int LowProgressTicks { get; set; }
    internal int RestageRetryTick { get; set; }
}

internal sealed class CrowdSteering(Func<Vector2, Vector2, float, bool> canSweep, bool enableYield,
    Func<CrowdAgent, Vector2, bool> hasPassage = null)
{
    private const float Clearance = 0.002f;
    private const float RestageStep = 0.15f;
    private const float RestageLookAhead = 4f;
    private const float ParkingMargin = 0.06f;
    private const int RestageRetryDelay = 12;
    private static readonly float[] Turns = [0, 18, -18, 36, -36, 54, -54, 72, -72, 88, -88];
    private static readonly (int Forward, int Side)[] RestageNeighbors = [(1, 0), (1, 1), (1, -1), (0, 1), (0, -1), (-1, 1), (-1, -1), (-1, 0)];
    private RestageOrder restaging;
    private int tick;

    internal int YieldSteps { get; private set; }

    internal void Advance(IReadOnlyList<CrowdAgent> agents)
    {
        tick++;
        AdvanceRestaging(agents);
        bool canRestage = enableYield && hasPassage == null;
        foreach (var agent in agents)
        {
            if (agent.Field == null || restaging?.Requester == agent)
            {
                continue;
            }

            Vector2 preferred = agent.Field.PreferredDirection(agent.Position);
            if (preferred.LengthSquared() < 0.01f)
            {
                continue;
            }

            float previousCost = canRestage ? agent.Field.CostAt(agent.Position) : 0;
            bool moved = TryAdvance(agent, preferred, agents);
            if (!canRestage)
            {
                continue;
            }

            bool advanced = moved && previousCost - agent.Field.CostAt(agent.Position) >= agent.Speed * .15f;
            agent.LowProgressTicks = advanced ? 0 : agent.LowProgressTicks + 1;
            if (restaging == null && agent.LowProgressTicks >= 3 && tick >= agent.RestageRetryTick)
            {
                agent.RestageRetryTick = tick + RestageRetryDelay;
                TryStartRestaging(agent, preferred, agents);
            }

            if (!moved && restaging == null)
            {
                TryYield(agent, preferred, agents);
            }
        }
    }

    private void TryStartRestaging(CrowdAgent requester, Vector2 direction, IReadOnlyList<CrowdAgent> agents)
    {
        var firstStep = requester.Position + direction * requester.Speed;
        if (!agents.Any(other => other != requester && other.Field == null && other.CanYield
            && BlocksCorridor(other, requester, firstStep)))
        {
            return;
        }

        var farPoint = requester.Position + direction * RestageLookAhead;
        var blockers = agents.Where(other => other != requester && other.Field == null && other.CanYield && other.Speed > 0
            && Vector2.Dot(other.Position - requester.Position, direction) > 0 && BlocksCorridor(other, requester, farPoint))
            .OrderByDescending(other => Vector2.Dot(other.Position - requester.Position, direction)).ThenBy(other => other.Id);
        foreach (var blocker in blockers)
        {
            var path = FindParkingPath(blocker, requester, direction, agents);
            if (path.Length > 0)
            {
                restaging = new RestageOrder(requester, blocker, path);
                return;
            }
        }
    }

    private Vector2[] FindParkingPath(CrowdAgent blocker, CrowdAgent requester, Vector2 forward, IReadOnlyList<CrowdAgent> agents)
    {
        var sideways = new Vector2(-forward.Y, forward.X);
        var open = new Queue<(int Forward, int Side)>();
        var parent = new Dictionary<(int Forward, int Side), (int Forward, int Side)>();
        var start = (Forward: 0, Side: 0);
        open.Enqueue(start);
        parent.Add(start, start);
        float gap = requester.Radius + blocker.Radius + Clearance + ParkingMargin;
        while (open.TryDequeue(out var cell))
        {
            var from = Point(cell);
            if (cell != start && MathF.Abs(Vector2.Dot(from - requester.Position, sideways)) >= gap)
            {
                var result = new List<Vector2>();
                for (var step = cell; step != start; step = parent[step])
                {
                    result.Add(Point(step));
                }

                result.Reverse();
                return result.ToArray();
            }

            foreach (var delta in RestageNeighbors)
            {
                var next = (Forward: cell.Forward + delta.Forward, Side: cell.Side + delta.Side);
                if (next.Forward < -2 || next.Forward > 22 || Math.Abs(next.Side) > 10 || parent.ContainsKey(next)
                    || !CanSweepBodies(blocker, from, Point(next), agents))
                {
                    continue;
                }

                parent.Add(next, cell);
                open.Enqueue(next);
            }
        }

        return Array.Empty<Vector2>();

        Vector2 Point((int Forward, int Side) cell) => blocker.Position + (forward * cell.Forward + sideways * cell.Side) * RestageStep;
    }

    private void AdvanceRestaging(IReadOnlyList<CrowdAgent> agents)
    {
        var order = restaging;
        if (order == null)
        {
            return;
        }

        var blocker = order.Blocker;
        if (!agents.Contains(blocker) || !agents.Contains(order.Requester) || blocker.Field != null)
        {
            restaging = null;
            return;
        }

        var target = order.Path[order.Next];
        var offset = target - blocker.Position;
        float distance = offset.Length();
        bool reached = distance <= blocker.Speed;
        var next = reached ? target : blocker.Position + offset * (blocker.Speed / distance);
        if (!Clear(blocker, next, agents))
        {
            restaging = null;
            order.Requester.RestageRetryTick = tick + RestageRetryDelay;
            return;
        }

        blocker.Position = next;
        YieldSteps++;
        if (reached && ++order.Next == order.Path.Length)
        {
            restaging = null;
        }
    }

    private static bool BlocksCorridor(CrowdAgent body, CrowdAgent mover, Vector2 endpoint)
    {
        float radius = mover.Radius + body.Radius + Clearance;
        return SegmentDistanceSquared(body.Position, mover.Position, endpoint) < radius * radius;
    }

    private bool TryAdvance(CrowdAgent agent, Vector2 preferred, IReadOnlyList<CrowdAgent> agents)
    {
        Vector2 best = agent.Position;
        Vector2 heading = agent.Heading;
        float bestScore = float.NegativeInfinity;
        foreach (float turn in Turns)
        {
            float radians = turn * MathF.PI / 180;
            float sine = MathF.Sin(radians);
            float cosine = MathF.Cos(radians);
            var direction = new Vector2(preferred.X * cosine - preferred.Y * sine, preferred.X * sine + preferred.Y * cosine);
            var next = agent.Position + direction * agent.Speed;
            if (!Clear(agent, next, agents))
            {
                continue;
            }

            float remaining = agent.Field.CostAt(next);
            if (!float.IsFinite(remaining))
            {
                continue;
            }

            float score = -remaining / agent.Speed + 0.35f * Vector2.Dot(direction, preferred) + 0.15f * Vector2.Dot(direction, agent.Heading);
            if (score > bestScore)
            {
                bestScore = score;
                best = next;
                heading = direction;
            }
        }

        if (float.IsNegativeInfinity(bestScore))
        {
            return false;
        }

        agent.Position = best;
        agent.Heading = heading;
        return true;
    }

    private void TryYield(CrowdAgent mover, Vector2 direction, IReadOnlyList<CrowdAgent> agents)
    {
        Vector2 forward = mover.Position + direction * mover.Speed;
        Vector2 perpendicular = new(-direction.Y, direction.X);
        foreach (var blocker in agents)
        {
            float required = mover.Radius + blocker.Radius + Clearance;
            if (blocker.Field != null || !blocker.CanYield || SegmentDistanceSquared(blocker.Position, mover.Position, forward) >= required * required)
            {
                continue;
            }

            Vector2 side = Vector2.Dot(blocker.Position - mover.Position, perpendicular) < 0 ? -perpendicular : perpendicular;
            if (Slide(side) || Slide(-side))
            {
                YieldSteps++;
                return;
            }

            bool Slide(Vector2 offset)
            {
                var next = blocker.Position + offset * (mover.Speed * 0.65f);
                if (SegmentDistanceSquared(next, mover.Position, forward) <= SegmentDistanceSquared(blocker.Position, mover.Position, forward)
                    || !Clear(blocker, next, agents))
                {
                    return false;
                }
                blocker.Position = next;
                return true;
            }
        }
    }

    private bool Clear(CrowdAgent mover, Vector2 destination, IReadOnlyList<CrowdAgent> agents)
    {
        if (hasPassage?.Invoke(mover, destination) == false || !CanSweepBodies(mover, mover.Position, destination, agents))
        {
            return false;
        }

        var order = restaging;
        if (order != null && mover != order.Blocker)
        {
            var from = order.Blocker.Position;
            float gap = mover.Radius + order.Blocker.Radius + Clearance;
            for (int i = order.Next; i < order.Path.Length; i++)
            {
                var to = order.Path[i];
                if (SweepDistanceSquared(mover.Position, destination, from, to) < gap * gap)
                {
                    return false;
                }

                from = to;
            }
        }

        return true;
    }

    private bool CanSweepBodies(CrowdAgent mover, Vector2 from, Vector2 destination, IReadOnlyList<CrowdAgent> agents)
    {
        if (!canSweep(from, destination, mover.Radius))
        {
            return false;
        }

        foreach (var other in agents)
        {
            float required = mover.Radius + other.Radius + Clearance;
            if (other != mover && SegmentDistanceSquared(other.Position, from, destination) < required * required)
            {
                return false;
            }
        }

        return true;
    }

    private static float SegmentDistanceSquared(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float length = segment.LengthSquared();
        float progress = length == 0 ? 0 : Math.Clamp(Vector2.Dot(point - from, segment) / length, 0, 1);
        return Vector2.DistanceSquared(point, from + progress * segment);
    }

    private static float SweepDistanceSquared(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
    {
        float first = Cross(b - a, c - a);
        float second = Cross(b - a, d - a);
        float third = Cross(d - c, a - c);
        float fourth = Cross(d - c, b - c);
        if ((first < 0 && second > 0 || first > 0 && second < 0) && (third < 0 && fourth > 0 || third > 0 && fourth < 0))
        {
            return 0;
        }

        return MathF.Min(MathF.Min(SegmentDistanceSquared(a, c, d), SegmentDistanceSquared(b, c, d)),
            MathF.Min(SegmentDistanceSquared(c, a, b), SegmentDistanceSquared(d, a, b)));
    }

    private static float Cross(Vector2 first, Vector2 second) => first.X * second.Y - first.Y * second.X;

    private sealed class RestageOrder(CrowdAgent requester, CrowdAgent blocker, Vector2[] path)
    {
        internal CrowdAgent Requester { get; } = requester;
        internal CrowdAgent Blocker { get; } = blocker;
        internal Vector2[] Path { get; } = path;
        internal int Next { get; set; }
    }
}
