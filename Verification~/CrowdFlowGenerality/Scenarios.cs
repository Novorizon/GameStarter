using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Game.HotUpdate.TowerDefense.Domain;

namespace GameStarter.Verification.CrowdFlowGenerality;

internal sealed record BodySpec(int Id, int Stream, int Due, Vector2 Start, Vector2? Finish, float Radius, float Speed, bool CanReposition = false);
internal sealed record Crossing(Vector2 Center, float HalfLength);
internal sealed record WitnessMove(int Id, Vector2 To);

internal sealed class BodyState(BodySpec spec)
{
    internal BodySpec Spec { get; } = spec;
    internal Vector2 Position { get; set; } = spec.Start;
}

internal sealed class Scenario
{
    private readonly BattleTerrain terrain;

    internal Scenario(string name, Vector2 min, Vector2 max, Position2[][] obstacles, BodySpec[] bodies, int expected, Vector2 fieldOffset = default,
        Crossing crossing = null, WitnessMove[] witness = null, Vector2[] portals = null)
    {
        Name = name;
        FieldMin = min + fieldOffset;
        FieldMax = max;
        Bodies = bodies;
        Expected = expected;
        Crossing = crossing;
        Witness = witness ?? Array.Empty<WitnessMove>();
        Portals = portals ?? Array.Empty<Vector2>();
        terrain = new BattleTerrain(Millimetres(min), Millimetres(max), new[] { Millimetres(min), Millimetres(max) }, 1, obstacles);
    }

    internal string Name { get; }
    internal Vector2 FieldMin { get; }
    internal Vector2 FieldMax { get; }
    internal BodySpec[] Bodies { get; }
    internal int Expected { get; }
    internal Crossing Crossing { get; }
    internal WitnessMove[] Witness { get; }
    internal Vector2[] Portals { get; }

    internal bool CanSweep(Vector2 from, Vector2 to, float radius)
        => terrain.CanMove(Millimetres(from), Millimetres(to), (int)MathF.Ceiling(radius * 1000));

    internal static Position2 Millimetres(Vector2 p) => new Position2((int)MathF.Round(p.X * 1000), (int)MathF.Round(p.Y * 1000));
}

internal static class Scenarios
{
    internal static Scenario[] All() => new[]
    {
        ObliqueCrossing(false), ObliqueCrossing(true), LatticeCorridor(false), LatticeCorridor(true), BentCorridor(false), BentCorridor(true),
        OccupiedPocket(false), OccupiedPocket(true), OccupiedPocket(false, mirror: true), OccupiedPocket(false, reorder: true),
        OccupiedPocket(false, three: true)
    };

    private static Scenario ObliqueCrossing(bool swap)
    {
        var center = new Vector2(2.35f, -1.10f);
        var east = Angle(17);
        var south = Angle(74);
        var min = center - new Vector2(5.6f);
        var max = center + new Vector2(5.6f);
        Vector2[] rectangle = { min, new(max.X, min.Y), max, new(min.X, max.Y) };
        var eastNormal = new Vector2(-east.Y, east.X);
        var southNormal = new Vector2(-south.Y, south.X);
        var walls = new List<Position2[]>();
        foreach (int a in new[] { -1, 1 })
        {
            foreach (int b in new[] { -1, 1 })
            {
                var clipped = Clip(Clip(rectangle, center, a * eastNormal, .57f), center, b * southNormal, .57f);
                walls.Add(clipped.Select(Scenario.Millimetres).ToArray());
            }
        }

        var entries = new List<BodySpec>();
        for (int i = 0; i < 8; i++)
        {
            bool heavy = i % 3 == 0;
            entries.Add(new BodySpec(1 + 2 * i, 1, i * 8 + (swap ? 8 : 1), center - 4.6f * east, center + 4.6f * east,
                heavy ? .48f : .30f, heavy ? .07f : .125f));
            entries.Add(new BodySpec(2 + 2 * i, 2, i * 7 + (swap ? 1 : 8), center + 4.6f * south, center - 4.6f * south,
                heavy ? .30f : .48f, heavy ? .135f : .075f));
        }

        var due = entries.OrderBy(body => body.Due).ThenBy(body => body.Id).ToArray();
        return new Scenario(swap ? "oblique-crossing-swapped" : "oblique-crossing", min, max, walls.ToArray(), due, due.Length,
            crossing: new Crossing(center, 1.45f));
    }

    private static Scenario LatticeCorridor(bool shift)
    {
        // The .98m opening is physically wide enough for a .96m body even with both lattice phases.
        Position2[][] walls = { Box(2.8f, -2.4f, 6.0f, -.49f), Box(2.8f, .49f, 6.0f, 2.4f) };
        var entries = Enumerable.Range(1, 5).Select(id => new BodySpec(id, id < 4 ? 1 : 2,
            id < 4 ? id * 9 : 130 + id * 9, new Vector2(.85f, 0), new Vector2(8, 0), id < 4 ? .30f : .48f, id < 4 ? .125f : .08f)).ToArray();
        return new Scenario(shift ? "mixed-lane-shifted" : "mixed-lane-aligned", new Vector2(0, -2.4f), new Vector2(9, 2.4f), walls, entries,
            entries.Length, shift ? new Vector2(0, .073f) : Vector2.Zero);
    }

    private static Scenario OccupiedPocket(bool fixedBodies, bool mirror = false, bool reorder = false, bool three = false)
    {
        Position2[][] walls = { Box(0, .55f, 3.9f, 1.6f), Box(0, -1.6f, 5.05f, -.55f) };
        BodySpec[] entries =
        {
            new(1, 1, 1, new Vector2(1, 0), new Vector2(8, 0), .34f, .10f),
            new(2, 0, 0, new Vector2(3, 0), null, .46f, .07f, !fixedBodies),
            new(3, 0, 0, new Vector2(4, 0), null, .34f, .10f, !fixedBodies)
        };
        WitnessMove[] witness =
        {
            new(3, new Vector2(4.55f, 0)), new(3, new Vector2(4.55f, 1.02f)),
            new(2, new Vector2(5.42f, 0)), new(2, new Vector2(5.42f, 1.02f)), new(1, new Vector2(8, 0))
        };
        if (three)
        {
            entries = entries.Append(new BodySpec(4, 0, 0, new Vector2(4.9f, 0), null, .34f, .09f, true)).ToArray();
            witness = new[] { new WitnessMove(4, new Vector2(6.30f, 0)), new WitnessMove(4, new Vector2(6.30f, 1.02f)) }.Concat(witness).ToArray();
        }

        if (mirror)
        {
            entries = entries.Select(body => body with { Start = Mirror(body.Start), Finish = body.Finish is Vector2 goal ? Mirror(goal) : null }).ToArray();
            witness = witness.Select(move => move with { To = Mirror(move.To) }).ToArray();
            walls = walls.Select(wall => wall.Reverse().Select(point => new Position2(9000 - point.X, point.Y)).ToArray()).ToArray();
        }

        if (reorder)
        {
            Array.Reverse(entries);
        }

        string name = fixedBodies ? "fixed-pocket-control" : three ? "three-blocker-restaging" : "two-blocker-restaging";
        if (mirror || reorder)
        {
            name += mirror ? "-mirrored" : "-reordered";
        }

        return new Scenario(name, new Vector2(0, -1.6f), new Vector2(9, 1.6f), walls, entries, fixedBodies ? 0 : 1,
            witness: fixedBodies ? null : witness);

        static Vector2 Mirror(Vector2 point) => new Vector2(9 - point.X, point.Y);
    }

    private static Scenario BentCorridor(bool shift)
    {
        // Two .98m strips meet at a right angle. Their intersection supplies an off-lattice portal for the heavy body.
        Position2[][] walls = { Box(0, -2.4f, 8, -.49f), Box(0, .49f, 3.96f, 4.7f), Box(4.94f, -.49f, 8, 4.7f) };
        var portal = new Vector2(4.45f, 0);
        var start = new Vector2(.85f, 0);
        var goal = new Vector2(4.45f, 3.7f);
        BodySpec[] entries =
        {
            new(1, 1, 0, start, goal, .30f, .10f),
            new(2, 2, 130, start, goal, .48f, .08f)
        };
        var phase = shift ? new Vector2(.173f, .073f) : new Vector2(.10f, 0);
        return new Scenario(shift ? "bent-lane-shifted" : "bent-lane-aligned", new Vector2(0, -2.4f), new Vector2(8, 4.7f), walls,
            entries, entries.Length, phase, portals: new[] { portal });
    }

    private static Vector2[] Clip(Vector2[] polygon, Vector2 origin, Vector2 normal, float distance)
    {
        var output = new List<Vector2>();
        var from = polygon[^1];
        foreach (var to in polygon)
        {
            float a = Vector2.Dot(from - origin, normal) - distance;
            float b = Vector2.Dot(to - origin, normal) - distance;
            if ((a >= 0) != (b >= 0))
            {
                output.Add(Vector2.Lerp(from, to, a / (a - b)));
            }

            if (b >= 0)
            {
                output.Add(to);
            }

            from = to;
        }

        return output.ToArray();
    }

    private static Vector2 Angle(float degrees)
    {
        float radians = degrees * MathF.PI / 180;
        return new Vector2(MathF.Cos(radians), MathF.Sin(radians));
    }

    private static Position2[] Box(float left, float bottom, float right, float top)
        => new Vector2[] { new(left, bottom), new(right, bottom), new(right, top), new(left, top) }.Select(Scenario.Millimetres).ToArray();
}
