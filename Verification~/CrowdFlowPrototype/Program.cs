using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Game.HotUpdate.TowerDefense.Domain;

namespace GameStarter.Verification.CrowdFlow;

internal static class Program
{
    private readonly record struct Entry(int Id, int Due, Vector2 Start, Vector2 Finish, float Radius, float Speed);
    private readonly record struct Result(int Arrived, int YieldSteps);

    private static int Main()
    {
        var detour = Enumerable.Range(1, 12).Select(id => new Entry(id, id * 5, new(0.65f, -0.65f), new(8.5f, -0.65f),
            id % 3 == 0 ? 0.46f : 0.34f, id % 3 == 0 ? 0.07f : 0.13f)).ToArray();
        var detourResult = Run("obstacle detour", new(0, -2.4f), new(9.2f, 2.4f),
            [Box(3.0f, -2.4f, 4.1f, 0.35f)], detour);
        var sizeGate = Run("820 mm gap with heavy-unit detour", new(0, -2.4f), new(9.2f, 2.8f),
            [Box(3, -2.4f, 4.1f, -0.81f), Box(3, 0.01f, 4.1f, 1.05f), Box(3, 2.22f, 4.1f, 2.8f)], detour);

        var lane = new[] { new Entry(1, 1, new(1, 0), new(7, 0), 0.34f, 0.1f) };
        var holding = Run("occupied lane with fixed bodies", new(0, -0.81f), new(8, 0.81f), [], lane,
            new(2, new(3.2f, 0), 0.34f, 0) { CanYield = true }, yield: false);
        var yielding = Run("occupied lane with yielding", new(0, -0.81f), new(8, 0.81f), [], lane,
            new(2, new(3.2f, 0), 0.34f, 0) { CanYield = true });

        var horizontal = Enumerable.Range(1, 9).Select(id => new Entry(id, id * 5, new(-4.4f, 0), new(4.4f, 0), 0.34f, 0.11f));
        var vertical = Enumerable.Range(10, 9).Select(id => new Entry(id, (id - 9) * 5, new(0, 4.4f), new(0, -4.4f), 0.40f, 0.11f));
        var crossing = horizontal.Concat(vertical).ToArray();
        var crossingResult = Run("open crossing", new(-5, -5), new(5, 5), [], crossing);
        Position2[][] walls = [Box(-5, -5, -0.58f, -0.58f), Box(0.58f, -5, 5, -0.58f),
            Box(-5, 0.58f, -0.58f, 5), Box(0.58f, 0.58f, 5, 5)];
        var ungated = Run("narrow crossing without priority", new(-5, -5), new(5, 5), walls, crossing);
        var gated = Run("narrow crossing with priority", new(-5, -5), new(5, 5), walls, crossing, coordinateJunction: true);
        var reversed = Run("narrow crossing with reversed arrival order", new(-5, -5), new(5, 5), walls,
            vertical.Select(entry => entry with { Due = entry.Due - 3 }).Concat(horizontal).ToArray(), coordinateJunction: true);

        return detourResult.Arrived == detour.Length && sizeGate.Arrived == detour.Length
            && holding.Arrived == 0 && yielding.Arrived == 1 && yielding.YieldSteps > 0
            && crossingResult.Arrived == 18 && ungated.Arrived == 0 && gated.Arrived == 18 && reversed.Arrived == 18 ? 0 : 1;
    }

    private static Result Run(string name, Vector2 min, Vector2 max, Position2[][] obstacles, Entry[] entries,
        CrowdAgent idle = null, bool yield = true, bool coordinateJunction = false)
    {
        var terrain = new BattleTerrain(Millimetres(min), Millimetres(max), [Millimetres(min), Millimetres(max)], 1, obstacles);
        bool CanSweep(Vector2 a, Vector2 b, float radius) => terrain.CanMove(Millimetres(a), Millimetres(b), (int)MathF.Ceiling(radius * 1000));
        Junction junction = coordinateJunction ? new(Vector2.Zero, 1.0f,
            entries.ToDictionary(entry => entry.Id, entry => Vector2.Normalize(entry.Finish - entry.Start))) : null;
        var steering = new CrowdSteering(CanSweep, yield, junction == null ? null : junction.CanAdvance);
        var fields = new Dictionary<(Vector2 Goal, float Radius), DirectionField>();
        var agents = new List<CrowdAgent>();
        if (idle != null)
        {
            agents.Add(idle);
        }

        var pending = entries.ToList();
        var finish = entries.ToDictionary(entry => entry.Id, entry => entry.Finish);
        int arrived = 0;
        int lastArrival = 0;
        for (int tick = 1; tick <= 1200 && arrived < entries.Length; tick++)
        {
            foreach (var entry in pending.Where(entry => entry.Due <= tick).ToArray())
            {
                if (agents.Any(other => Vector2.Distance(entry.Start, other.Position) < entry.Radius + other.Radius + 0.002f))
                {
                    continue;
                }

                var key = (entry.Finish, entry.Radius);
                if (!fields.TryGetValue(key, out var field))
                {
                    field = new DirectionField(min, max, 0.15f, (from, to) => CanSweep(from, to, entry.Radius), [entry.Finish]);
                    fields.Add(key, field);
                }
                agents.Add(new CrowdAgent(entry.Id, entry.Start, entry.Radius, entry.Speed) { Field = field });
                pending.Remove(entry);
            }

            var previous = agents.Select(agent => agent.Position).ToArray();
            junction?.Schedule(agents);
            steering.Advance(agents);
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (!CanSweep(previous[i], agent.Position, agent.Radius))
                {
                    throw new InvalidOperationException($"{name}: {agent.Id} crossed a wall at {tick}.");
                }
                for (int j = 0; j < i; j++)
                {
                    float required = agent.Radius + agents[j].Radius;
                    if (Vector2.DistanceSquared(agent.Position, agents[j].Position) < required * required)
                    {
                        throw new InvalidOperationException($"{name}: {agent.Id} overlapped {agents[j].Id} at {tick}.");
                    }
                }
            }

            int count = agents.RemoveAll(agent => agent.Field != null && Vector2.Distance(agent.Position, finish[agent.Id]) <= agent.Radius);
            if (count > 0)
            {
                arrived += count;
                lastArrival = tick;
            }
        }

        string waiting = string.Join(" ", agents.Where(agent => agent.Field != null).Take(3).Select(agent => $"{agent.Id}@{agent.Position:F2}"));
        Console.WriteLine($"{name}: arrived={arrived}/{entries.Length}, lastTick={lastArrival}, fields={fields.Count}, yieldSteps={steering.YieldSteps}, waiting={waiting}");
        return new Result(arrived, steering.YieldSteps);
    }

    private static Position2 Millimetres(Vector2 position) => new((int)MathF.Round(position.X * 1000), (int)MathF.Round(position.Y * 1000));

    private static Position2[] Box(float left, float bottom, float right, float top)
        => [Millimetres(new(left, bottom)), Millimetres(new(right, bottom)), Millimetres(new(right, top)), Millimetres(new(left, top))];
}
