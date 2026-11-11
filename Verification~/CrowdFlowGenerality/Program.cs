using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace GameStarter.Verification.CrowdFlowGenerality;

internal static class Program
{
    private const int TickLimit = 1200;
    private const float FinishRadius = .16f;
    private const float ReservationPadding = .002f;

    private sealed record Result(int Admitted, int Arrived, string ByStream, int Crossed, int MaxSkippedCrossings, int YieldSteps, string Remaining,
        ulong Replay, string Failure);

    private static int Main(string[] args)
    {
        var scenarios = Scenarios.All().Where(scenario => args.Length == 0 || args.Contains(scenario.Name)).ToArray();
        if (scenarios.Length == 0)
        {
            Console.Error.WriteLine("No matching scenarios.");
            return 2;
        }

        int failed = 0;
        foreach (var scenario in scenarios)
        {
            try
            {
                VerifyFeasibility(scenario);
                var first = Run(scenario);
                var repeated = Run(scenario);
                bool passed = first.Replay == repeated.Replay && first.Failure == null;
                Console.WriteLine($"{(passed ? "PASS" : "FAIL")} {scenario.Name}: admitted={first.Admitted}, arrived={first.Arrived}/{scenario.Expected}, "
                    + $"streams={first.ByStream}, crossed={first.Crossed}, fairness={first.MaxSkippedCrossings}, yield={first.YieldSteps}, "
                    + $"replay={first.Replay:X16}, repeat={first.Replay == repeated.Replay}, remaining={first.Remaining}, failure={first.Failure ?? "none"}");
                failed += passed ? 0 : 1;
            }
            catch (InvalidOperationException error)
            {
                Console.Error.WriteLine($"FAIL {scenario.Name}: {error.Message}");
                failed++;
            }
        }

        return failed == 0 ? 0 : 1;
    }

    private static Result Run(Scenario scenario)
    {
        var pending = scenario.Bodies.ToList();
        var live = new List<BodyState>();
        var arrived = new HashSet<int>();
        var crossed = new HashSet<int>();
        var skippedByStream = new Dictionary<int, int>();
        var backend = new PrototypeBackend(scenario);
        int maxSkipped = 0;
        ulong hash = 14695981039346656037;
        for (int tick = 0; tick <= TickLimit; tick++)
        {
            foreach (var next in pending.Where(body => body.Due <= tick).ToArray())
            {
                if (live.Any(other => Vector2.Distance(next.Start, other.Position) < next.Radius + other.Spec.Radius + ReservationPadding))
                {
                    continue;
                }

                live.Add(new BodyState(next));
                pending.Remove(next);
            }

            var before = live.Select(body => body.Position).ToArray();
            backend.Advance(live);
            Mix(tick);
            Mix(pending.Count);
            for (int i = 0; i < live.Count; i++)
            {
                var body = live[i];
                CheckPose(scenario, live, before, i, tick);
                if (scenario.Crossing is { } crossing && body.Spec.Finish.HasValue && Progress(body, before[i], crossing) < 0
                    && Progress(body, body.Position, crossing) >= 0 && crossed.Add(body.Spec.Id))
                {
                    ObserveCrossing(body, crossing);
                }

                if (body.Spec.Finish is Vector2 finish && Vector2.DistanceSquared(body.Position, finish) <= FinishRadius * FinishRadius)
                {
                    arrived.Add(body.Spec.Id);
                }

                Mix(body.Spec.Id);
                Mix(BitConverter.SingleToInt32Bits(body.Position.X));
                Mix(BitConverter.SingleToInt32Bits(body.Position.Y));
            }

            live.RemoveAll(body => arrived.Contains(body.Spec.Id));
            if (pending.Count == 0 && live.All(body => body.Spec.Finish == null))
            {
                break;
            }
        }

        string failure = arrived.Count != scenario.Expected ? "arrival-count" : null;
        if (scenario.Crossing != null && (crossed.Count != scenario.Expected || maxSkipped > 4))
        {
            failure = failure == null ? "crossing-progress-or-fairness" : failure + "+crossing-progress-or-fairness";
        }

        if (failure == null && scenario.Witness.Length > 0)
        {
            var request = scenario.Bodies.Single(body => body.Finish.HasValue);
            var heading = Vector2.Normalize(request.Finish.Value - request.Start);
            if (live.Any(body => !body.Spec.Finish.HasValue && Vector2.Dot(body.Position - body.Spec.Start, heading) < .20f))
            {
                failure = "no-longitudinal-restaging";
            }
        }

        string streams = string.Join(",", scenario.Bodies.Where(body => body.Finish.HasValue).GroupBy(body => body.Stream)
            .Select(group => $"{group.Key}:{group.Count(body => arrived.Contains(body.Id))}/{group.Count()}"));
        string remaining = string.Join(" ", live.Where(body => body.Spec.Finish.HasValue).Take(4).Select(body => $"{body.Spec.Id}@{body.Position:F3}"));
        return new Result(scenario.Bodies.Length - pending.Count, arrived.Count, streams, crossed.Count, maxSkipped, backend.YieldSteps, remaining, hash, failure);

        void ObserveCrossing(BodyState mover, Crossing crossing)
        {
            skippedByStream[mover.Spec.Stream] = 0;
            var waiting = live.Where(other => other.Spec.Finish.HasValue && other.Spec.Stream != mover.Spec.Stream && !crossed.Contains(other.Spec.Id)
                && Progress(other, other.Position, crossing) >= -crossing.HalfLength - .35f).Select(other => other.Spec.Stream).Distinct();
            foreach (int stream in waiting)
            {
                int skipped = skippedByStream.GetValueOrDefault(stream) + 1;
                skippedByStream[stream] = skipped;
                maxSkipped = Math.Max(maxSkipped, skipped);
            }
        }

        void Mix(int value)
        {
            hash = unchecked((hash ^ (uint)value) * 1099511628211);
        }
    }

    private static float Progress(BodyState body, Vector2 position, Crossing crossing)
        => Vector2.Dot(Vector2.Normalize(body.Spec.Finish.Value - body.Spec.Start), position - crossing.Center);

    private static void VerifyFeasibility(Scenario scenario)
    {
        var bodies = scenario.Bodies.ToDictionary(spec => spec.Id, spec => new BodyState(spec));
        foreach (var body in bodies.Values)
        {
            if (body.Spec.Finish is not Vector2 finish)
            {
                continue;
            }

            var from = body.Position;
            foreach (var to in scenario.Portals.Append(finish))
            {
                if (!scenario.CanSweep(from, to, body.Spec.Radius))
                {
                    throw new InvalidOperationException($"The terrain does not admit body {body.Spec.Id} along its witness from {from:F3} to {to:F3}.");
                }

                from = to;
            }
        }

        foreach (var move in scenario.Witness)
        {
            var body = bodies[move.Id];
            if (!scenario.CanSweep(body.Position, move.To, body.Spec.Radius) || bodies.Values.Any(other => other != body
                && SegmentDistanceSquared(other.Position, body.Position, move.To) < MathF.Pow(body.Spec.Radius + other.Spec.Radius + ReservationPadding, 2)))
            {
                throw new InvalidOperationException($"Invalid repositioning witness for {move.Id}, {body.Position:F3} to {move.To:F3}.");
            }

            body.Position = move.To;
        }
    }

    private static void CheckPose(Scenario scenario, IReadOnlyList<BodyState> live, Vector2[] before, int index, int tick)
    {
        var body = live[index];
        if (!float.IsFinite(body.Position.X) || !float.IsFinite(body.Position.Y) || !scenario.CanSweep(before[index], body.Position, body.Spec.Radius))
        {
            throw new InvalidOperationException($"Terrain sweep failed at {tick} for {body.Spec.Id}, {before[index]:F3} to {body.Position:F3}.");
        }

        if (!body.Spec.Finish.HasValue && !body.Spec.CanReposition && body.Position != body.Spec.Start)
        {
            throw new InvalidOperationException($"Immovable body {body.Spec.Id} was moved at {tick}.");
        }

        if (Vector2.Distance(before[index], body.Position) > body.Spec.Speed + .001f)
        {
            throw new InvalidOperationException($"Body {body.Spec.Id} exceeded its step length at {tick}.");
        }

        for (int j = 0; j < index; j++)
        {
            var other = live[j];
            float required = body.Spec.Radius + other.Spec.Radius;
            if (Vector2.DistanceSquared(body.Position, other.Position) < required * required)
            {
                throw new InvalidOperationException($"Body overlap at {tick} between {body.Spec.Id} and {other.Spec.Id}.");
            }

            float swept = (required - .001f) * (required - .001f);
            bool bodyFirst = SegmentDistanceSquared(before[j], before[index], body.Position) >= swept
                && SegmentDistanceSquared(body.Position, before[j], other.Position) >= swept;
            bool otherFirst = SegmentDistanceSquared(before[index], before[j], other.Position) >= swept
                && SegmentDistanceSquared(other.Position, before[index], body.Position) >= swept;
            if (!bodyFirst && !otherFirst)
            {
                throw new InvalidOperationException($"No collision-free sweep order at {tick} between {body.Spec.Id} and {other.Spec.Id}.");
            }
        }
    }

    private static float SegmentDistanceSquared(Vector2 point, Vector2 from, Vector2 to)
    {
        Vector2 segment = to - from;
        float length = segment.LengthSquared();
        float progress = length == 0 ? 0 : Math.Clamp(Vector2.Dot(point - from, segment) / length, 0, 1);
        return Vector2.DistanceSquared(point, from + progress * segment);
    }
}
