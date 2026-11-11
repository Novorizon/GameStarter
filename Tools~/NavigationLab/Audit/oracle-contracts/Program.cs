using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;

var results = new List<object>();
int failed = 0;
Run("rectangle-crossing-with-clear-endpoints", () => Expect(!ContinuousOracle.SegmentFits(World(), P(4000, 5500), P(7000, 5500), 100)));
Run("rectangle-positive-tangent", () => Expect(ContinuousOracle.SegmentFits(World(), P(4000, 6100), P(7000, 6100), 100)));
Run("rectangle-submillimetre-penetration", () => Expect(!ContinuousOracle.SegmentFits(World(), P(4000, 6099.5), P(7000, 6099.5), 100)));
Run("rectangle-diagonal-interior-crossing", () => Expect(!ContinuousOracle.SegmentFits(World(), P(4000, 7000), P(7000, 4000), 250)));
Run("rectangle-rounded-corner-tangent", () =>
{
    double sum = 12000 + 250 * Math.Sqrt(2);
    Expect(ContinuousOracle.SegmentFits(World(), P(5000, sum - 5000), P(sum - 5000, 5000), 250));
});
Run("large-radius-obstacle-two-cells-away", () => Expect(!ContinuousOracle.CircleFits(World(), P(7000, 5500), 1200)));
Run("base-circle-chord", () => Expect(!ContinuousOracle.SegmentFits(World(), P(12500, 15000), P(17500, 15000), 250)));
Run("base-circle-tangent", () => Expect(ContinuousOracle.SegmentFits(World(), P(13000, 16250), P(17000, 16250), 250)));
Run("world-edge-tangent", () => Expect(ContinuousOracle.CircleFits(World(), P(250, 2500), 250)));
Run("world-edge-penetration", () => Expect(!ContinuousOracle.CircleFits(World(), P(249.8, 2500), 250)));
Run("continuous-pair-endpoint-swap", () =>
{
    var world = World();
    var oracle = BirthPair(world, 42, 44, 30000, out var before);
    var after = new[] { S(1, before[1].Position), S(2, before[0].Position) };
    Reject(() => oracle.ValidateTransition(before, after, 5), "Continuous body collision");
});
Run("continuous-diagonal-pair-crossing", () =>
{
    var world = World();
    var oracle = BirthPair(world, 42, 82, 30000, out var before);
    var after = new[] { S(1, world.Center(84)), S(2, world.Center(44)) };
    Reject(() => oracle.ValidateTransition(before, after, 5), "Continuous body collision");
});
Run("same-velocity-preserves-clearance", () =>
{
    var world = World();
    var oracle = BirthPair(world, 42, 44, 10000, out var before);
    var after = new[] { S(1, before[0].Position + P(500, 0)), S(2, before[1].Position + P(500, 0)) };
    oracle.ValidateTransition(before, after, 5);
});
Run("fast-agent-overtakes-slower-agent", () =>
{
    var world = World();
    var oracle = BirthPair(world, 42, 44, 40000, out var before);
    var after = new[] { S(1, before[0].Position + P(3000, 0)), S(2, before[1].Position + P(500, 0)) };
    Reject(() => oracle.ValidateTransition(before, after, 5), "Continuous body collision");
});
Run("55mm-speed-allows-5point5mm-microstep", () =>
{
    var world = World();
    var oracle = BirthOne(world, 42, new UnitProfile(250, 55, 450), out var before);
    oracle.ValidateTransition(before, new[] { S(1, before[0].Position + P(5.5, 0)) }, 5);
});
Run("55mm-speed-rejects-overspeed", () =>
{
    var world = World();
    var oracle = BirthOne(world, 42, new UnitProfile(250, 55, 450), out var before);
    Reject(() => oracle.ValidateTransition(before, new[] { S(1, before[0].Position + P(5.7, 0)) }, 5), "Speed/teleport");
});
Run("long-range-attacker-may-stand-outside-gather-distance", () =>
{
    var world = World(1000, new[] { 288 });
    var oracle = BirthOne(world, 288, new UnitProfile(250, 20000, 6000), out var before);
    var goal = world.Center(289);
    oracle.ValidateTransition(before, new[] { new UnitSnapshot(1, goal, MotionState.Attacking, 289) }, 5);
});
Run("rejected-birth-cannot-move-existing-unit", () =>
{
    var oracle = BirthOne(World(), 42, new UnitProfile(250, 55, 450), out var before);
    Reject(() => oracle.VerifyRejectedBirth(before, new[] { S(1, before[0].Position + P(1, 0)) }), "Instantaneous mutation");
});
Run("rejected-birth-cannot-hide-existing-unit", () =>
{
    var oracle = BirthOne(World(), 42, new UnitProfile(250, 55, 450), out var before);
    Reject(() => oracle.VerifyRejectedBirth(before, Array.Empty<UnitSnapshot>()), "Rejected spawn changed");
});
Run("change-cannot-place-wall-through-live-body", () =>
{
    var oracle = BirthOne(World(), 42, new UnitProfile(250, 55, 450), out var before);
    Reject(() => oracle.ApplyChange(new WorldChange { BlockCells = new[] { 42 } }, before, before), "INVALID TEST INPUT");
});
Run("unknown-body-id-rejected", () =>
{
    var oracle = BirthOne(World(), 42, new UnitProfile(250, 55, 450), out var before);
    Reject(() => oracle.ValidateTransition(before, new[] { S(99, before[0].Position) }, 5), "Live ID conservation");
});
File.WriteAllText(
    "results.json",
    JsonSerializer.Serialize(new { success = failed == 0, tests = results, failed }, new JsonSerializerOptions { WriteIndented = true }));
return failed == 0 ? 0 : 1;

void Run(string name, Action action)
{
    try
    {
        action();
        results.Add(new { name, passed = true });
        Console.WriteLine("PASS " + name);
    }
    catch (Exception error)
    {
        failed++;
        results.Add(new { name, passed = false, error = error.Message });
        Console.WriteLine("FAIL " + name + ": " + error.Message);
    }
}

static void Expect(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Expected geometry classification was false.");
    }
}

static void Reject(Action action, string expected)
{
    try
    {
        action();
    }
    catch (InvalidOperationException error) when (error.Message.Contains(expected, StringComparison.Ordinal))
    {
        return;
    }
    throw new InvalidOperationException("Expected rejection containing: " + expected);
}

static Point2 P(double x, double y) => new Point2(x, y);
static UnitSnapshot S(int id, Point2 position) => new UnitSnapshot(id, position, MotionState.Moving, -1);

static WorldDefinition World(int gather = 4000, int[]? spawns = null)
{
    var walkable = Enumerable.Repeat(true, 400).ToArray();
    walkable[105] = false;
    return new WorldDefinition(
        20,
        20,
        1000,
        P(0, 0),
        walkable,
        Enumerable.Repeat(true, 400).ToArray(),
        spawns ?? new[] { 42, 44, 82 },
        new[] { new TargetDefinition(1, P(15000, 15000), 1000) },
        gather);
}

static ContinuousOracle BirthOne(WorldDefinition world, int cell, UnitProfile profile, out UnitSnapshot[] snapshot)
{
    var oracle = new ContinuousOracle(world);
    snapshot = new[] { S(1, world.Center(cell)) };
    oracle.RegisterBirth(new SpawnRequest(1, profile, cell), Array.Empty<UnitSnapshot>(), snapshot);
    return oracle;
}

static ContinuousOracle BirthPair(WorldDefinition world, int first, int second, int speed, out UnitSnapshot[] snapshot)
{
    var profile = new UnitProfile(250, speed, 450);
    var oracle = BirthOne(world, first, profile, out var before);
    snapshot = new[] { before[0], S(2, world.Center(second)) };
    oracle.RegisterBirth(new SpawnRequest(2, profile, second), before, snapshot);
    return oracle;
}
