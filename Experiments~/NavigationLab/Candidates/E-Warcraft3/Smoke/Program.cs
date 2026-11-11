using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;
using NavigationLab.Candidates.EWarcraft3;

string repository = args[0];
string name = args[1];
string output = args[2];
bool baseline = args.Contains("--baseline");
Directory.CreateDirectory(output);
var watch = Stopwatch.StartNew();
var world = ParseForest(Path.Combine(repository, "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
var big = new UnitProfile(250, 55, 550);
var medium = new UnitProfile(200, 100, 450);
var small = new UnitProfile(180, 165, 450);
var profile = name.StartsWith("medium", StringComparison.Ordinal) ? medium : name.StartsWith("small", StringComparison.Ordinal) ? small : big;
int count = name == "late46" ? 46 : int.Parse(new string(name.Where(char.IsDigit).ToArray()));
var inputs = Enumerable.Range(1, count).Select(id => new SpawnRequest(id, name == "late46" && id > 44 ? small : profile, world.SpawnCells[0])).ToArray();
var original = Sources(repository);
var oracle = new ContinuousOracle(world);
INavigationFactory factory = baseline ? new WarcraftBaselineFactory() : new WarcraftFactory();
using var navigator = factory.Create(world, name == "late46" ? new[] { small, big } : new[] { profile });
int next = 0;
int firstSettled = -1;
int hold = 0;
int tick = 0;
string? failure = null;
try
{
    for (; tick < 12000; tick++)
    {
        if (next < inputs.Length && (name != "late46" || next < 44 || tick >= 4000))
        {
            var before = navigator.Snapshot().ToArray();
            bool accepted = navigator.TrySpawn(inputs[next]);
            var after = navigator.Snapshot().ToArray();
            if (accepted)
            {
                oracle.RegisterBirth(inputs[next++], before, after);
            }
            else
            {
                oracle.VerifyRejectedBirth(before, after);
            }
        }

        for (int micro = 0; micro < 10; micro++)
        {
            var before = navigator.Snapshot().ToArray();
            navigator.Step();
            oracle.ValidateTransition(before, navigator.Snapshot().ToArray(), 5);
        }

        var current = navigator.Snapshot().ToArray();
        if (next == inputs.Length && current.All(unit => unit.State != MotionState.Moving))
        {
            if (firstSettled < 0)
            {
                firstSettled = tick;
            }
            hold++;
            if (hold >= 200)
            {
                int optimum = oracle.MaximumEndpointAttackers();
                if (current.Count(unit => unit.State == MotionState.Attacking) != optimum)
                {
                    throw new InvalidOperationException("Actual attackers do not fill independent compatible endpoint matching: " + optimum);
                }
                break;
            }
        }
        else
        {
            firstSettled = -1;
            hold = 0;
        }

        if (tick % 1000 == 0)
        {
            Console.WriteLine($"{name} tick={tick} born={next}/{count} moving={current.Count(unit => unit.State == MotionState.Moving)} "
                + $"attack={current.Count(unit => unit.State == MotionState.Attacking)} elapsedMs={watch.ElapsedMilliseconds}");
        }
    }
    if (tick >= 12000)
    {
        failure = "Arrival/200-tick stability timeout";
    }
}
catch (Exception error)
{
    failure = error.ToString();
}
var bodies = navigator.Snapshot().ToArray();
var afterSources = Sources(repository);
bool sourceUnchanged = original.OrderBy(pair => pair.Key).SequenceEqual(afterSources.OrderBy(pair => pair.Key));
var result = new
{
    scope = "Early real-Forest debugging only; not the shared cross-algorithm matrix or Unity acceptance.",
    success = failure == null && sourceUnchanged,
    factory = factory.Id,
    name,
    tick,
    firstSettled,
    hold,
    requested = count,
    spawned = next,
    pending = count - next,
    attacking = bodies.Count(unit => unit.State == MotionState.Attacking),
    waiting = bodies.Count(unit => unit.State == MotionState.Waiting),
    moving = bodies.Count(unit => unit.State == MotionState.Moving),
    failure,
    elapsedMilliseconds = watch.ElapsedMilliseconds,
    sourceUnchanged,
    sourceHashes = original,
    debugUnits = DebugUnits(navigator),
    metrics = navigator.Metrics,
    oracle.PairSweeps,
    oracle.BodySweeps,
    minimumBodyGapMm = double.IsFinite(oracle.MinimumBodyGapMm) ? oracle.MinimumBodyGapMm : (double?)null,
    bodies = bodies.Select(body => new { body.Id, x = body.Position.X, y = body.Position.Y, body.State, body.GoalCell, body.TargetId })
};
File.WriteAllText(Path.Combine(output, name + (baseline ? "-baseline" : "-yield") + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions
{
    WriteIndented = true
}));
Console.WriteLine($"FINAL {name} pass={result.success} born={next}/{count} tick={tick} attack={result.attacking} wait={result.waiting} failure={failure}");
return result.success ? 0 : 1;

static Dictionary<string, string> Sources(string root)
{
    string source = Path.Combine(root, "Experiments~/NavigationLab/Candidates/E-Warcraft3");
    return Directory.GetFiles(source, "*.cs", SearchOption.AllDirectories).Where(path => !path.Contains("/obj/") && !path.Contains("/bin/"))
        .OrderBy(path => path).ToDictionary(path => Path.GetRelativePath(root, path), path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
}

static object[] DebugUnits(INavigator navigator)
{
    const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
    var field = navigator.GetType().GetField("units", flags)!;
    var units = (System.Collections.IEnumerable)field.GetValue(navigator)!;
    var result = new List<object>();
    foreach (object unit in units)
    {
        var type = unit.GetType();
        object? Get(string name) => type.GetField(name, flags)?.GetValue(unit);
        var path = (IReadOnlyList<Point2>)Get("Path")!;
        int waypoint = (int)Get("Waypoint")!;
        result.Add(new
        {
            id = type.GetProperty("Id", flags)!.GetValue(unit),
            blocked = Get("BlockedSteps"),
            yieldCell = Get("YieldCell"),
            yieldRequester = Get("YieldRequester"),
            waypoint,
            pathCount = path.Count,
            remainder = path.Skip(waypoint).Take(12).Select(point => new { point.X, point.Y })
        });
    }
    return result.ToArray();
}

static WorldDefinition ParseForest(string path)
{
    var lines = File.ReadAllLines(path).Where(line => line.Length > 0 && !line.StartsWith(';')).ToArray();
    if (lines[0] != "NAVGRID 1")
    {
        throw new InvalidOperationException("Unexpected Forest format.");
    }
    var size = lines[1].Split(' ');
    int width = int.Parse(size[1]);
    int height = int.Parse(size[2]);
    int cell = int.Parse(lines[2].Split(' ')[1]);
    var origin = lines[3].Split(' ');
    var target = lines[4].Split(' ');
    bool[] walk = new bool[width * height];
    bool[] holds = new bool[walk.Length];
    var spawns = new List<int>();
    for (int row = 0; row < height; row++)
    {
        for (int x = 0; x < width; x++)
        {
            char symbol = lines[6 + row][x];
            int index = (height - 1 - row) * width + x;
            walk[index] = ".+aBS".Contains(symbol);
            holds[index] = symbol == 'a';
            if (symbol == 'S')
            {
                spawns.Add(index);
            }
        }
    }
    return new WorldDefinition(
        width,
        height,
        cell,
        new Point2(int.Parse(origin[1]), int.Parse(origin[2])),
        walk,
        holds,
        spawns.ToArray(),
        new[] { new TargetDefinition(1, new Point2(int.Parse(target[1]), int.Parse(target[2])), int.Parse(target[3])) });
}
