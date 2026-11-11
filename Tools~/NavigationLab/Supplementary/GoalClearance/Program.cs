using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;
using System.Reflection;

if (args.Length != 4)
{
    throw new ArgumentException("Arguments: label output-directory candidate-assembly-path full-factory-type");
}
string label = args[0];
string output = Path.GetFullPath(args[1]);
var assembly = Assembly.LoadFrom(Path.GetFullPath(args[2]));
var factory = (INavigationFactory)Activator.CreateInstance(assembly.GetType(args[3], true)!)!;
string sourceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
Directory.CreateDirectory(output);
var cases = new[] { Make("two-large", new[] { 650, 650 }, false), Make("three-large", new[] { 650, 650, 650 }, true),
    Make("mixed-three", new[] { 650, 650, 180 }, false) };
var results = new List<object>();
foreach (var definition in cases)
{
    var input = new
    {
        definition.Name,
        definition.World.Width,
        definition.World.Height,
        definition.World.CellSizeMm,
        walkable = string.Concat(definition.World.CopyWalkable().Select(value => value ? '1' : '0')),
        holding = string.Concat(definition.World.CopyHolding().Select(value => value ? '1' : '0')),
        targets = definition.World.Targets.Select(value => new { value.Id, x = value.Center.X, y = value.Center.Y, value.RadiusMm }),
        requests = definition.Requests.Select(value => new { value.Id, value.SpawnCell, value.TargetId, value.AssignedGoalCell,
            radius = value.Profile.RadiusMm, speed = value.Profile.SpeedMmPerTick, range = value.Profile.AttackRangeMm }),
        independentFeasibleGoals = definition.FeasibleGoals
    };
    File.WriteAllText(Path.Combine(output, definition.Name + ".input.json"), JsonSerializer.Serialize(input, Pretty()) + "\n");
    string original = Path.Combine(sourceRoot, "original-e-before", definition.Name + ".input.json");
    if (FileHash(original) != FileHash(Path.Combine(output, definition.Name + ".input.json")))
    {
        throw new InvalidOperationException("Supplementary input differs from preserved original: " + definition.Name);
    }
    var witness = Witness(definition);
    var first = Run(definition, factory);
    var repeat = Run(definition, factory);
    var result = new
    {
        definition.Name,
        independentSerialWitness = witness,
        candidate = first,
        freshReplayIdentical = first.TraceSha256 == repeat.TraceSha256 && first.FinalState == repeat.FinalState,
        candidateAssemblySha256 = FileHash(assembly.Location),
        coreAssemblySha256 = FileHash(typeof(Point2).Assembly.Location),
        oracleAssemblySha256 = FileHash(typeof(ContinuousOracle).Assembly.Location)
    };
    results.Add(result);
    File.WriteAllText(Path.Combine(output, definition.Name + ".json"), JsonSerializer.Serialize(result, Pretty()) + "\n");
    Console.WriteLine(definition.Name + " witness=" + witness.Success + " goalsGap=" + first.ClaimedGoalMinimumGapMm
        + " settled=" + first.Settled + "/" + definition.Requests.Length + " steps=" + first.Steps);
}
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
{
    label,
    scope = "Supplementary goal compatibility contract, not an added or modified common49 case. Original world/radius/speed/holding cells kept before/after.",
    scriptSha256 = FileHash(Path.Combine(sourceRoot, "Program.cs")),
    driverAssemblySha256 = FileHash(Assembly.GetExecutingAssembly().Location),
    factoryType = factory.GetType().FullName,
    factoryId = factory.Id,
    declaredFeatures = factory.Features.ToString(),
    results
}, Pretty()) + "\n");

static Definition Make(string name, int[] radii, bool fourthGoal)
{
    const int width = 16;
    const int height = 12;
    var walk = new bool[width * height];
    for (int y = 1; y < height - 1; y++)
    {
        for (int x = 1; x < width - 1; x++)
        {
            walk[y * width + x] = true;
        }
    }
    int a = 11 + 4 * width;
    int b = 11 + 5 * width;
    int c = 9 + 3 * width;
    int d = 9 + 6 * width;
    var holds = new bool[walk.Length];
    foreach (int goal in fourthGoal ? new[] { a, b, c, d } : new[] { a, b, c })
    {
        holds[goal] = true;
    }
    int[] spawns = { 2 + 4 * width, 2 + 6 * width, 2 + 8 * width };
    var world = new WorldDefinition(width, height, 1000, new Point2(0, 0), walk, holds, spawns,
        new[] { new TargetDefinition(1, new Point2(12500, 5000), 300) }, 10000);
    var requests = radii.Select((radius, index) => new SpawnRequest(index + 1, new UnitProfile(radius, 1000, 2500), spawns[index])).ToArray();
    int[] feasible = radii.Length == 2 ? new[] { b, c } : fourthGoal ? new[] { b, c, d } : new[] { b, c, a };
    return new Definition(name, world, requests, feasible);
}

static WitnessResult Witness(Definition definition)
{
    var oracle = new ContinuousOracle(definition.World);
    var bodies = Array.Empty<UnitSnapshot>();
    foreach (var request in definition.Requests)
    {
        var next = bodies.Concat(new[] { new UnitSnapshot(request.Id, definition.World.Center(request.SpawnCell), MotionState.Moving,
            definition.FeasibleGoals[request.Id - 1], request.TargetId) }).ToArray();
        oracle.RegisterBirth(request, bodies, next);
        bodies = next;
    }
    int steps = 0;
    foreach (var request in definition.Requests)
    {
        Point2 destination = definition.World.Center(definition.FeasibleGoals[request.Id - 1]);
        while (Point2.Distance(bodies.Single(value => value.Id == request.Id).Position, destination) > .000001)
        {
            Point2 from = bodies.Single(value => value.Id == request.Id).Position;
            Point2 delta = destination - from;
            Point2 to = from + delta.Normalized * Math.Min(delta.Length, request.Profile.SpeedMmPerTick / 10.0);
            var next = bodies.Select(value => value.Id == request.Id
                ? new UnitSnapshot(value.Id, to, MotionState.Moving, value.GoalCell, value.TargetId) : value).ToArray();
            oracle.ValidateTransition(bodies, next, 5);
            bodies = next;
            steps++;
        }
        var settled = bodies.Select(value => value.Id == request.Id
            ? new UnitSnapshot(value.Id, value.Position, MotionState.Attacking, value.GoalCell, value.TargetId) : value).ToArray();
        oracle.ValidateTransition(bodies, settled, 0);
        bodies = settled;
    }
    for (int hold = 0; hold < 2000; hold++)
    {
        oracle.ValidateTransition(bodies, bodies, 5);
    }
    return new WitnessResult(true, steps, oracle.MinimumBodyGapMm, oracle.PairSweeps);
}

static RunResult Run(Definition definition, INavigationFactory factory)
{
    using var navigator = factory.Create(definition.World, definition.Requests.Select(value => value.Profile).Distinct().ToArray());
    var oracle = new ContinuousOracle(definition.World);
    var pending = definition.Requests.ToList();
    var profiles = definition.Requests.ToDictionary(value => value.Id, value => value.Profile);
    var previous = navigator.Snapshot().ToArray();
    double? goalGap = null;
    int stable = 0;
    int step = 0;
    string rejection = "None";
    using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    void Admit()
    {
        foreach (var request in pending.ToArray())
        {
            var before = navigator.Snapshot().ToArray();
            bool accepted = navigator.TrySpawn(request);
            var next = navigator.Snapshot().ToArray();
            if (accepted)
            {
                oracle.RegisterBirth(request, before, next);
                pending.Remove(request);
            }
            else
            {
                oracle.VerifyRejectedBirth(before, next);
                rejection = navigator.LastSpawnFailure;
            }
            previous = next;
        }
    }
    void CheckGoals()
    {
        foreach (var first in previous)
        {
            foreach (var second in previous.Where(value => value.Id > first.Id))
            {
                double gap = Point2.Distance(definition.World.Center(first.GoalCell), definition.World.Center(second.GoalCell))
                    - profiles[first.Id].RadiusMm - profiles[second.Id].RadiusMm;
                goalGap = goalGap.HasValue ? Math.Min(goalGap.Value, gap) : gap;
            }
        }
    }
    string? error = null;
    try
    {
        Admit();
        CheckGoals();
        for (; step < 10000; step++)
        {
            if (step % SimulationProtocol.Substeps == 0 && pending.Count > 0)
            {
                Admit();
                CheckGoals();
            }
            navigator.Step();
            var next = navigator.Snapshot().ToArray();
            oracle.ValidateTransition(previous, next, SimulationProtocol.MicrostepMilliseconds);
            string state = Canonical(next);
            hash.AppendData(Encoding.UTF8.GetBytes(state + "\n"));
            stable = pending.Count == 0 && next.Length == definition.Requests.Length
                && next.All(value => value.State == MotionState.Attacking) && Canonical(previous) == state ? stable + 1 : 0;
            previous = next;
            CheckGoals();
            if (stable >= 2000)
            {
                step++;
                break;
            }
        }
    }
    catch (Exception caught)
    {
        error = caught.ToString();
    }
    return new RunResult(goalGap, previous.Count(value => value.State != MotionState.Moving), step, stable,
        definition.Requests.Length - pending.Count, pending.Count, rejection,
        Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), Canonical(previous),
        double.IsFinite(oracle.MinimumBodyGapMm) ? oracle.MinimumBodyGapMm : null, oracle.PairSweeps, error);
}

static string Canonical(UnitSnapshot[] bodies) => string.Join("|", bodies.OrderBy(value => value.Id).Select(value =>
    value.Id + ":" + value.Position.X.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ","
    + value.Position.Y.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ":" + (int)value.State + ":" + value.GoalCell));
static string FileHash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
static JsonSerializerOptions Pretty() => new() { WriteIndented = true };
internal record Definition(string Name, WorldDefinition World, SpawnRequest[] Requests, int[] FeasibleGoals);
internal record WitnessResult(bool Success, int Steps, double MinimumBodyGapMm, long PairSweeps);
internal record RunResult(double? ClaimedGoalMinimumGapMm, int Settled, int Steps, int StableMicrosteps,
    int Admitted, int Pending, string LastSpawnRejection, string TraceSha256, string FinalState,
    double? MinimumBodyGapMm, long PairSweeps, string? Error);
