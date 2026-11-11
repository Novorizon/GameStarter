using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;

string directory = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath(".");
var reports = new List<object>();
foreach (string path in Directory.GetFiles(directory, "random-multigate-maze-*-input.json").OrderBy(path => path, StringComparer.Ordinal))
{
    string name = Path.GetFileName(path).Replace("-input.json", "", StringComparison.Ordinal);
    string inputText = File.ReadAllText(path).TrimEnd('\r', '\n');
    using var inputDoc = JsonDocument.Parse(inputText);
    var input = inputDoc.RootElement;
    var worldJson = input.GetProperty("world");
    var world = new WorldDefinition(
        worldJson.GetProperty("width").GetInt32(),
        worldJson.GetProperty("height").GetInt32(),
        worldJson.GetProperty("cellSizeMm").GetInt32(),
        Point(worldJson.GetProperty("origin")),
        worldJson.GetProperty("walkable").GetString()!.Select(value => value == '1').ToArray(),
        worldJson.GetProperty("holding").GetString()!.Select(value => value == '1').ToArray(),
        worldJson.GetProperty("spawnCells").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
        worldJson.GetProperty("targets").EnumerateArray().Select(target => new TargetDefinition(
            target.GetProperty("id").GetInt32(),
            Point(target),
            target.GetProperty("radiusMm").GetInt32())).ToArray(),
        worldJson.GetProperty("gatheringDistanceMm").GetInt32());
    var requests = input.GetProperty("requests").EnumerateArray().ToDictionary(order => order.GetProperty("id").GetInt32(), order => order.Clone());
    string witnessPath = Path.Combine(directory, name + "-witness.json.gz");
    using var gzip = new GZipStream(File.OpenRead(witnessPath), CompressionMode.Decompress);
    using var document = JsonDocument.Parse(gzip);
    var witness = document.RootElement;
    Require(witness.GetProperty("success").GetBoolean(), "Witness header claims failure.");
    Require(witness.GetProperty("physicalInputSha256").GetString() == Hash(inputText), "Input hash mismatch.");
    Require(witness.GetProperty("geometrySha256").GetString() == Hash(worldJson.GetRawText()), "World hash mismatch.");
    var oracle = new ContinuousOracle(world);
    var bodies = new Dictionary<int, UnitSnapshot>();
    var steps = witness.GetProperty("movements").EnumerateArray().ToArray();
    int cursor = 0;
    long time = 0;
    var seen = new HashSet<int>();
    foreach (var route in witness.GetProperty("paths").EnumerateArray())
    {
        int id = route.GetProperty("Id").GetInt32();
        Require(seen.Add(id) && requests.TryGetValue(id, out _), "Unknown or duplicate birth.");
        var order = requests[id];
        var profile = new UnitProfile(order.GetProperty("radiusMm").GetInt32(), order.GetProperty("speedMmPerTick").GetInt32(),
            order.GetProperty("attackRangeMm").GetInt32());
        int spawn = order.GetProperty("spawnCell").GetInt32();
        int target = order.GetProperty("targetId").GetInt32();
        int goal = route.GetProperty("goal").GetInt32();
        var request = new SpawnRequest(id, profile, spawn, target, order.GetProperty("assignedGoalCell").GetInt32());
        Require(route.GetProperty("SpawnCell").GetInt32() == spawn && world.SpawnCells.Contains(spawn), "Birth entry changed.");
        Require(time >= order.GetProperty("readyTick").GetInt64() * 50 && route.GetProperty("birthAtMilliseconds").GetInt64() == time,
            "Original birth schedule was violated.");
        int[] cells = route.GetProperty("cells").EnumerateArray().Select(cell => cell.GetInt32()).ToArray();
        Require(cells.Length > 0 && cells[0] == spawn && cells[^1] == goal, "Path endpoint mismatch.");
        var before = Snapshot(bodies);
        bodies.Add(id, new UnitSnapshot(id, world.Center(spawn), MotionState.Moving, goal, target));
        oracle.RegisterBirth(request, before, Snapshot(bodies));
        for (int edge = 1; edge < cells.Length; edge++)
        {
            Require(world.Neighbours(cells[edge - 1]).Contains(cells[edge]), "Non-neighbour shortcut in saved path.");
            Require(cursor < steps.Length, "Missing movement records.");
            var step = steps[cursor++];
            Require(step.GetProperty("id").GetInt32() == id && step.GetProperty("startedMilliseconds").GetInt64() == time, "Movement order/time changed.");
            Point2 from = Point(step.GetProperty("from"));
            Point2 to = Point(step.GetProperty("to"));
            Require(from == bodies[id].Position && to == world.Center(cells[edge]), "Movement coordinate changed.");
            int duration = step.GetProperty("durationMilliseconds").GetInt32();
            Require(duration > 0, "Non-positive traversal time.");
            before = Snapshot(bodies);
            bodies[id] = new UnitSnapshot(id, to, MotionState.Moving, goal, target);
            oracle.ValidateTransition(before, Snapshot(bodies), duration);
            time += duration;
        }
        Require(route.GetProperty("arrivedAtMilliseconds").GetInt64() == time, "Arrival time changed.");
        var position = bodies[id].Position;
        var state = ContinuousOracle.CanAttack(world, position, profile, world.Target(target)) ? MotionState.Attacking : MotionState.Waiting;
        bodies[id] = new UnitSnapshot(id, position, state, goal, target);
        oracle.ValidateTransition(Snapshot(bodies), Snapshot(bodies), 0);
    }
    Require(seen.SetEquals(requests.Keys) && cursor == steps.Length, "ID or movement records were lost.");
    Require(bodies.Count == 32 && bodies.Values.Count(body => body.State == MotionState.Attacking) == 16
        && bodies.Values.Count(body => body.State == MotionState.Waiting) == 16, "Actual final state is not32/16/16.");
    long hold = witness.GetProperty("holdMilliseconds").GetInt64();
    Require(hold == 10000, "Hold interval changed.");
    oracle.ValidateTransition(Snapshot(bodies), Snapshot(bodies), checked((int)hold));
    time += hold;
    Require(time == witness.GetProperty("serialExecutionMilliseconds").GetInt64()
        && time <= witness.GetProperty("originalDeadlineTicks").GetInt64() * 50, "Witness misses its original deadline.");
    reports.Add(new { name, success = true, live = bodies.Count, attackers = 16, waiting = 16, physicalSegments = cursor,
        executionMilliseconds = time, oracle.BodySweeps, oracle.PairSweeps, oracle.MinimumBodyGapMm });
    Console.WriteLine("REPLAY PASS " + name + " segments=" + cursor + " timeMs=" + time);
}
Require(reports.Count == 6, "Expected all six independent input witnesses.");
File.WriteAllText(
    "replay-results.json",
    JsonSerializer.Serialize(new { success = true, count = reports.Count, cases = reports }, new JsonSerializerOptions { WriteIndented = true }));

static Point2 Point(JsonElement value) => new Point2(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble());
static UnitSnapshot[] Snapshot(Dictionary<int, UnitSnapshot> bodies) => bodies.Values.OrderBy(unit => unit.Id).ToArray();
static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
