using System;
using System.Collections.Generic;
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
}

internal sealed class CrowdSteering(Func<Vector2, Vector2, float, bool> canSweep, bool enableYield,
    Func<CrowdAgent, Vector2, bool> hasPassage = null)
{
    private const float Clearance = 0.002f;
    private static readonly float[] Turns = [0, 18, -18, 36, -36, 54, -54, 72, -72, 88, -88];

    internal int YieldSteps { get; private set; }

    internal void Advance(IReadOnlyList<CrowdAgent> agents)
    {
        foreach (var agent in agents)
        {
            if (agent.Field == null)
            {
                continue;
            }

            Vector2 preferred = agent.Field.PreferredDirection(agent.Position);
            if (preferred.LengthSquared() < 0.01f)
            {
                continue;
            }

            if (!TryAdvance(agent, preferred, agents) && enableYield)
            {
                TryYield(agent, preferred, agents);
            }
        }
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
        if (hasPassage?.Invoke(mover, destination) == false || !canSweep(mover.Position, destination, mover.Radius))
        {
            return false;
        }

        foreach (var other in agents)
        {
            float required = mover.Radius + other.Radius + Clearance;
            if (other != mover && SegmentDistanceSquared(other.Position, mover.Position, destination) < required * required)
            {
                return false;
            }
        }

        return true;
    }

    private static float SegmentDistanceSquared(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float progress = Math.Clamp(Vector2.Dot(point - from, segment) / segment.LengthSquared(), 0, 1);
        return Vector2.DistanceSquared(point, from + progress * segment);
    }
}
