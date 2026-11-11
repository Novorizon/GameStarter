using System.Reflection;
using System.Security.Cryptography;
using RVO;
using System.Text;
using System.Text.Json;
using NavigationLab.Candidates.BOrca;
using NavigationLab.Core;
using NavigationLab.Oracle;

var profiles = new[] { new UnitProfile(200, 100, 1000), new UnitProfile(250, 50, 600) };
var world = new WorldDefinition(
    12,
    12,
    1000,
    default,
    Enumerable.Repeat(true, 144).ToArray(),
    Enumerable.Repeat(true, 144).ToArray(),
    new[] { 13, 25, 37 },
    new[] { new TargetDefinition(1, new Point2(8500, 6500), 500) },
    10000);
var factory = new OrcaFactory();
VerifyExceptionIsolation();
var polygonChecks = VerifyPolygonGoalCompatibility();
VerifyTightZeroRangeArrival();
VerifySettledTargetRelocation();
VerifyOffsetExactWidthCorridor();
VerifyOffsetCornerRebuild();
var isolated = Run(false);
var interleaved = Run(true);
if (isolated.Hash != interleaved.Hash)
{
    throw new InvalidOperationException("Interleaved ORCA contexts changed the complete physical trace.");
}
Console.WriteLine(JsonSerializer.Serialize(new
{
    success = true,
    exceptionHostRestoration = true,
    tightZeroRangeArrival = true,
    settledTargetRelocation = true,
    offsetExactWidthCorridor = true,
    offsetCornerRebuild = true,
    polygonChecks,
    isolated = new { trajectorySha256 = isolated.Hash, pairSweeps = isolated.PairSweeps, bodySweeps = isolated.BodySweeps },
    interleaved = new { trajectorySha256 = interleaved.Hash, pairSweeps = interleaved.PairSweeps, bodySweeps = interleaved.BodySweeps },
    candidateDllSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(typeof(OrcaFactory).Assembly.Location))).ToLowerInvariant()
}, new JsonSerializerOptions { WriteIndented = true }));

(string Hash, long PairSweeps, long BodySweeps) Run(bool interleave)
{
    using var first = factory.Create(world, profiles);
    using var second = factory.Create(world, profiles);
    var oracle = new ContinuousOracle(world);
    var otherOracle = new ContinuousOracle(world);
    var trace = new StringBuilder();
    Birth(first, oracle, new SpawnRequest(1, profiles[0], 13, 1, 77));
    if (interleave)
    {
        Birth(second, otherOracle, new SpawnRequest(100, profiles[1], 37, 1, 90));
    }
    for (int step = 0; step < 3000; step++)
    {
        if (step == 200)
        {
            Birth(first, oracle, new SpawnRequest(2, profiles[1], 25, 1, 78));
        }
        if (step == 700)
        {
            var before = first.Snapshot();
            if (!first.Remove(1))
            {
                throw new InvalidOperationException("Expected explicit removal to succeed.");
            }
            oracle.RegisterDeath(1, before, first.Snapshot());
        }
        if (step == 900)
        {
            var change = new WorldChange { BlockCells = new[] { 40 } };
            var before = first.Snapshot();
            if (!first.ApplyWorldChange(change))
            {
                throw new InvalidOperationException("Expected dynamic rebuild support.");
            }
            oracle.ApplyChange(change, before, first.Snapshot());
        }
        if (step == 1100)
        {
            Birth(first, oracle, new SpawnRequest(3, profiles[0], 13, 1, 79));
        }
        Advance(first, oracle);
        foreach (var unit in first.Snapshot())
        {
            trace.Append(step).Append(':').Append(unit.Id).Append(':').Append(unit.Position.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append(',').Append(unit.Position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                .Append(':').Append(unit.GoalCell).Append(';');
        }
        if (interleave)
        {
            Advance(second, otherOracle);
            if (step == 500)
            {
                var before = second.Snapshot();
                second.Remove(100);
                otherOracle.RegisterDeath(100, before, second.Snapshot());
            }
            if (step == 1000)
            {
                Birth(second, otherOracle, new SpawnRequest(101, profiles[0], 37, 1, 103));
            }
        }
    }
    if (first.Snapshot().Any(unit => unit.State == MotionState.Moving))
    {
        throw new InvalidOperationException("The basic lifecycle world did not finish its final physical goals.");
    }
    return (Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(trace.ToString()))).ToLowerInvariant(), oracle.PairSweeps, oracle.BodySweeps);
}

static void Birth(INavigator nav, ContinuousOracle oracle, SpawnRequest request)
{
    var before = nav.Snapshot();
    if (!nav.TrySpawn(request))
    {
        throw new InvalidOperationException("Unexpected self-test spawn rejection: " + nav.LastSpawnFailure);
    }
    oracle.RegisterBirth(request, before, nav.Snapshot());
}

static void Advance(INavigator nav, ContinuousOracle oracle)
{
    var before = nav.Snapshot();
    nav.Step();
    oracle.ValidateTransition(before, nav.Snapshot(), SimulationProtocol.MicrostepMilliseconds);
}

static void VerifyExceptionIsolation()
{
    var host = Simulator.Instance;
    var fields = typeof(Simulator).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    var original = fields.Select(field => field.GetValue(host)).ToArray();
    var contextType = typeof(OrcaFactory).Assembly.GetType("NavigationLab.Candidates.BOrca.SimulatorContext")!;
    var context = Activator.CreateInstance(contextType, true)!;
    var use = contextType.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
        .Single(method => method.Name == "Use" && !method.IsGenericMethod);
    bool threw = false;
    try
    {
        use.Invoke(context, new object[] { new Action<Simulator>(simulator =>
        {
            simulator.SetAgentDefaults(5, 10, 2, .5f, .2f, 2, default);
            simulator.AddAgent(new RVO.Vector2(1, 1));
            throw new InvalidOperationException("Injected callback failure after actual state mutation.");
        }) });
    }
    catch (TargetInvocationException error) when (error.InnerException is InvalidOperationException)
    {
        threw = true;
    }
    if (!threw || fields.Where((field, index) => !Equals(field.GetValue(host), original[index])).Any())
    {
        throw new InvalidOperationException("A callback exception failed to restore the original singleton host.");
    }
    use.Invoke(context, new object[] { new Action<Simulator>(simulator =>
    {
        if (simulator.NumAgents != 1)
        {
            throw new InvalidOperationException("An exception discarded the actual per-battle state.");
        }
        bool invalidArgumentRejected = false;
        try
        {
            simulator.AddAgent(default, -1, 10, 2, .5f, .2f, 2, default);
        }
        catch (System.ArgumentOutOfRangeException)
        {
            invalidArgumentRejected = true;
        }
        if (!invalidArgumentRejected)
        {
            throw new InvalidOperationException("netstandard argument compatibility changed invalid-input guards.");
        }
    }) });
    if (fields.Where((field, index) => !Equals(field.GetValue(host), original[index])).Any())
    {
        throw new InvalidOperationException("Context verification leaked into the singleton host.");
    }
}

static object[] VerifyPolygonGoalCompatibility()
{
    const int width = 240;
    var holding = new bool[width * width];
    int goal = 168 * width + 151;
    holding[goal] = true;
    var world = new WorldDefinition(
        width,
        width,
        20,
        new Point2(-10, -10),
        Enumerable.Repeat(true, width * width).ToArray(),
        holding,
        new[] { 200 * width + 40 },
        new[] { new TargetDefinition(1, new Point2(2000, 2000), 1000) },
        4000);
    if (!ContinuousOracle.CircleFits(world, world.Center(goal), 700))
    {
        throw new InvalidOperationException("The exact non-axis tangent test Goal must fit the original circle.");
    }
    var checks = new List<object>();
    foreach (int range in new[] { 0, 10, 450 })
    {
        var profiles = new[] { 250, 490, 700 }.Select(radius => new UnitProfile(radius, 100, range)).ToArray();
        using var nav = new OrcaFactory().Create(world, profiles);
        long failures = nav.Metrics.TryGetValue("BasePolygonGoalCompatibilityFailures", out long found) ? found : 0;
        if (failures != 0 || nav.Metrics["BasePolygonGoalCompatibilityChecks"] != 3)
        {
            throw new InvalidOperationException("Outer-circle approximation excluded an original legal profile Goal.");
        }
        checks.Add(new
        {
            range,
            radii = profiles.Select(profile => profile.RadiusMm).ToArray(),
            goal = new { x = world.Center(goal).X, y = world.Center(goal).Y },
            failures,
            vertices = nav.Metrics["MaximumBasePolygonVertices"],
            criticalPlanes = nav.Metrics["CriticalGoalTangentPlanes"],
            maximumExtraClearanceMicrometres = nav.Metrics["MaximumPolygonExtraClearanceMicrometres"]
        });
    }
    return checks.ToArray();
}

static void VerifyTightZeroRangeArrival()
{
    var holding = new bool[90];
    int goal = 88;
    holding[goal] = true;
    var world = new WorldDefinition(
        10,
        9,
        500,
        default,
        Enumerable.Repeat(true, 90).ToArray(),
        holding,
        new[] { 89 },
        new[] { new TargetDefinition(1, new Point2(3500, 3250), 1000) },
        4000);
    var profile = new UnitProfile(250, 100, 0);
    foreach (var factory in new INavigationFactory[] { new OrcaFactory(), new OrcaYieldFactory() })
    {
        using var nav = factory.Create(world, new[] { profile });
        var oracle = new ContinuousOracle(world);
        Birth(nav, oracle, new SpawnRequest(1, profile, 89, 1, goal));
        Point2? anchor = null;
        int stable = 0;
        for (int step = 0; step < 5000 && stable < 2000; step++)
        {
            Advance(nav, oracle);
            ObserveStationaryAttack(nav.Snapshot().Single(), ref anchor, ref stable);
        }
        if (stable < 2000)
        {
            var final = nav.Snapshot().Single();
            throw new InvalidOperationException("Zero-range base/ceiling tangent Goal did not arrive: " + factory.Id
                + " pos=" + final.Position.X + "," + final.Position.Y + " state=" + final.State);
        }
    }
}

static void VerifySettledTargetRelocation()
{
    var world = new WorldDefinition(
        20,
        12,
        1000,
        default,
        Enumerable.Repeat(true, 240).ToArray(),
        Enumerable.Repeat(true, 240).ToArray(),
        new[] { 121 },
        new[] { new TargetDefinition(1, new Point2(14500, 6500), 500) },
        7000);
    var profile = new UnitProfile(200, 1000, 1000);
    foreach (var factory in new INavigationFactory[] { new OrcaFactory(), new OrcaYieldFactory() })
    {
        using var nav = factory.Create(world, new[] { profile });
        var oracle = new ContinuousOracle(world);
        Birth(nav, oracle, new SpawnRequest(1, profile, 121));
        for (int phase = 0; phase < 2; phase++)
        {
            Point2? anchor = null;
            int stable = 0;
            for (int step = 0; step < 5000 && stable < 2000; step++)
            {
                Advance(nav, oracle);
                ObserveStationaryAttack(nav.Snapshot().Single(), ref anchor, ref stable);
            }
            if (stable < 2000)
            {
                throw new InvalidOperationException("Existing attackers did not retarget a moved base: " + factory.Id + " phase=" + phase);
            }
            if (phase == 0)
            {
                var before = nav.Snapshot();
                var change = new WorldChange { ReplaceTarget = new TargetDefinition(1, new Point2(10500, 6500), 500) };
                nav.ApplyWorldChange(change);
                oracle.ApplyChange(change, before, nav.Snapshot());
            }
        }
    }
}

static void VerifyOffsetExactWidthCorridor()
{
    var walk = new bool[24 * 9];
    for (int x = 0; x < 24; x++)
    {
        walk[4 * 24 + x] = true;
        if (x >= 16)
        {
            for (int y = 1; y < 8; y++)
            {
                walk[y * 24 + x] = true;
            }
        }
    }
    int goal = 4 * 24 + 18;
    int spawn = 4 * 24 + 1;
    var holding = new bool[walk.Length];
    holding[goal] = true;
    var world = new WorldDefinition(
        24,
        9,
        500,
        new Point2(0, -60),
        walk,
        holding,
        new[] { spawn },
        new[] { new TargetDefinition(1, new Point2(10750, 2190), 500) },
        4000);
    var profile = new UnitProfile(250, 55, 1000);
    if (!ContinuousOracle.SegmentFits(world, world.Center(spawn), world.Center(goal), profile.RadiusMm))
    {
        throw new InvalidOperationException("The non-binary exact-width corridor must be physically traversable.");
    }
    foreach (var factory in new INavigationFactory[] { new OrcaFactory(), new OrcaYieldFactory() })
    {
        using var nav = factory.Create(world, new[] { profile });
        var oracle = new ContinuousOracle(world);
        Birth(nav, oracle, new SpawnRequest(1, profile, spawn, 1, goal));
        Point2? anchor = null;
        int stable = 0;
        for (int step = 0; step < 7000 && stable < 2000; step++)
        {
            Advance(nav, oracle);
            ObserveStationaryAttack(nav.Snapshot().Single(), ref anchor, ref stable);
        }
        if (stable < 2000)
        {
            var final = nav.Snapshot().Single();
            throw new InvalidOperationException("Exact-width offset corridor did not arrive: " + factory.Id
                + " pos=" + final.Position.X + "," + final.Position.Y
                + " staticProposalStops=" + (nav.Metrics.TryGetValue("UnsafeStaticOrSpeedProposalStops", out long stops) ? stops : 0));
        }
    }
}

static void VerifyOffsetCornerRebuild()
{
    var walk = new bool[24 * 14];
    for (int x = 1; x <= 18; x++)
    {
        walk[4 * 24 + x] = true;
        walk[10 * 24 + x] = true;
    }
    for (int y = 4; y <= 10; y++)
    {
        walk[y * 24 + 18] = true;
    }
    int spawn = 4 * 24 + 4;
    int goal = 10 * 24 + 4;
    var holding = new bool[walk.Length];
    holding[goal] = true;
    var world = new WorldDefinition(
        24,
        14,
        500,
        new Point2(0, -60),
        walk,
        holding,
        new[] { spawn },
        new[] { new TargetDefinition(1, new Point2(750, 5190), 500) },
        4000);
    var profile = new UnitProfile(250, 55, 1000);
    if (ContinuousOracle.SegmentFits(world, world.Center(spawn), world.Center(goal), profile.RadiusMm))
    {
        throw new InvalidOperationException("Corner-rebuild regression requires a blocked direct goal segment.");
    }
    var proof = new[] { world.Center(spawn), new Point2(9250, 2190), new Point2(9250, 5190), world.Center(goal) };
    for (int index = 1; index < proof.Length; index++)
    {
        if (!ContinuousOracle.SegmentFits(world, proof[index - 1], proof[index], profile.RadiusMm))
        {
            throw new InvalidOperationException("Exact-width corner fixture has no verified centre-line detour.");
        }
    }
    foreach (var factory in new INavigationFactory[] { new OrcaFactory(), new OrcaYieldFactory() })
    {
        using var nav = factory.Create(world, new[] { profile });
        var oracle = new ContinuousOracle(world);
        Birth(nav, oracle, new SpawnRequest(1, profile, spawn, 1, goal));
        Advance(nav, oracle);
        var actualStart = nav.Snapshot().Single();
        var change = new WorldChange { OpenCells = Array.Empty<int>() };
        var before = nav.Snapshot();
        if (!nav.ApplyWorldChange(change))
        {
            throw new InvalidOperationException("Expected path invalidation support.");
        }
        oracle.ApplyChange(change, before, nav.Snapshot());
        if (nav.Snapshot().Single().Position != actualStart.Position)
        {
            throw new InvalidOperationException("A path rebuild moved the physical body.");
        }
        Point2? anchor = null;
        int stable = 0;
        for (int step = 0; step < 14000 && stable < 2000; step++)
        {
            Advance(nav, oracle);
            ObserveStationaryAttack(nav.Snapshot().Single(), ref anchor, ref stable);
        }
        if (stable < 2000)
        {
            var current = nav.Snapshot().Single();
            throw new InvalidOperationException("Offset corner rebuild did not physically arrive and hold: " + factory.Id
                + " position=" + current.Position.X + "," + current.Position.Y);
        }
    }
}

static void ObserveStationaryAttack(UnitSnapshot current, ref Point2? anchor, ref int completeIntervals)
{
    if (current.State != MotionState.Attacking)
    {
        anchor = null;
        completeIntervals = 0;
        return;
    }
    if (!anchor.HasValue || Point2.Distance(current.Position, anchor.Value) > SimulationProtocol.GeometryToleranceMm)
    {
        // The anchor snapshot starts the observation: only the following 2000 full5ms intervals count.
        anchor = current.Position;
        completeIntervals = 0;
        return;
    }
    completeIntervals++;
}
