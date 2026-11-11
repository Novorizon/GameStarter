using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;

const string name = "capacity-death-refills-original-queue";
using var input = JsonDocument.Parse(File.ReadAllBytes("inputs/" + name + ".json"));
using var original = JsonDocument.Parse(File.ReadAllBytes("original-report.json"));
var source = input.RootElement.GetProperty("world");
var origin = source.GetProperty("origin");
var world = new WorldDefinition(
    source.GetProperty("width").GetInt32(),
    source.GetProperty("height").GetInt32(),
    source.GetProperty("cellSizeMm").GetInt32(),
    new Point2(origin.GetProperty("x").GetDouble(), origin.GetProperty("y").GetDouble()),
    source.GetProperty("walkable").GetString()!.Select(value => value == '1').ToArray(),
    source.GetProperty("holding").GetString()!.Select(value => value == '1').ToArray(),
    source.GetProperty("spawnCells").EnumerateArray().Select(value => value.GetInt32()).ToArray(),
    source.GetProperty("targets").EnumerateArray().Select(value => new TargetDefinition(
        value.GetProperty("id").GetInt32(),
        new Point2(value.GetProperty("x").GetDouble(), value.GetProperty("y").GetDouble()),
        value.GetProperty("radiusMm").GetInt32())).ToArray(),
    source.GetProperty("gatheringDistanceMm").GetInt32());
var requests = input.RootElement.GetProperty("requests").EnumerateArray().ToDictionary(value => value.GetProperty("id").GetInt32(), value => new SpawnRequest(
    value.GetProperty("id").GetInt32(),
    new UnitProfile(value.GetProperty("radiusMm").GetInt32(), value.GetProperty("speedMmPerTick").GetInt32(), value.GetProperty("attackRangeMm").GetInt32()),
    value.GetProperty("spawnCell").GetInt32(),
    value.GetProperty("targetId").GetInt32(),
    value.GetProperty("assignedGoalCell").GetInt32()));
var initial = original.RootElement.GetProperty("FinalState").GetString()!.Split('|').Select(value =>
{
    var parts = value.Split(':');
    var coordinates = parts[1].Split(',');
    return new UnitSnapshot(
        int.Parse(parts[0]),
        new Point2(double.Parse(coordinates[0], CultureInfo.InvariantCulture), double.Parse(coordinates[1], CultureInfo.InvariantCulture)),
        (MotionState)int.Parse(parts[2]),
        int.Parse(parts[3]),
        int.Parse(parts[4]));
}).OrderBy(value => value.Id).ToArray();

if (args.Contains("--inspect"))
{
    Inspect(world, requests, original.RootElement);
    return;
}

// The oracle begins at the already validated failed state, not at fabricated original births.
var oracle = new ContinuousOracle(world);
var live = (Dictionary<int, SpawnRequest>)typeof(ContinuousOracle).GetField("live", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(oracle)!;
foreach (var actor in initial)
{
    live.Add(actor.Id, requests[actor.Id]);
}
oracle.ValidateTransition(initial, initial, 0);
var newGoals = new Dictionary<int, int> { [40] = 57, [45] = 56, [46] = 47 };
Require(newGoals.Keys.All(id => requests[id].AssignedGoalCell < 0), "Witness tried to change an explicit destination.");
Require(newGoals.Keys.Select(id => requests[id].Profile).Distinct().Count() == 1, "Witness mixed incompatible profiles.");
var originalGoals = initial.Where(actor => newGoals.ContainsKey(actor.Id)).Select(actor => actor.GoalCell).OrderBy(cell => cell);
Require(newGoals.Values.OrderBy(cell => cell).SequenceEqual(originalGoals), "Witness changed the final goal set.");
var current = initial.Select(actor => newGoals.TryGetValue(actor.Id, out int goal)
    ? new UnitSnapshot(actor.Id, actor.Position, MotionState.Moving, goal, actor.TargetId) : actor).ToArray();
oracle.ValidateTransition(initial, current, 0);
var moves = new (int Id, Point2 Destination)[]
{
    (48, new Point2(initial.Single(actor => actor.Id == 48).Position.X, 4500)),
    (48, new Point2(1500, 4500)),
    (40, new Point2(3500, 6500)),
    (45, new Point2(2500, 6500)),
    (46, new Point2(2500, 5500)),
    (48, new Point2(2500, 4500))
};
var executed = new List<object>();
int microstep = 0;
foreach (var move in moves)
{
    Point2 from = current.Single(actor => actor.Id == move.Id).Position;
    int begin = microstep;
    while (Point2.Distance(current.Single(actor => actor.Id == move.Id).Position, move.Destination) > 1e-7)
    {
        Point2 position = current.Single(actor => actor.Id == move.Id).Position;
        Point2 delta = move.Destination - position;
        double maximum = requests[move.Id].Profile.SpeedMmPerTick / (double)SimulationProtocol.Substeps;
        Point2 destination = position + delta.Normalized * Math.Min(delta.Length, maximum);
        var next = current.Select(actor => actor.Id == move.Id
            ? new UnitSnapshot(actor.Id, destination, MotionState.Moving, actor.GoalCell, actor.TargetId) : actor).ToArray();
        oracle.ValidateTransition(current, next, SimulationProtocol.MicrostepMilliseconds);
        current = next;
        microstep++;
    }
    executed.Add(new
    {
        id = move.Id,
        from = new { x = from.X, y = from.Y },
        to = new { x = move.Destination.X, y = move.Destination.Y },
        microsteps = microstep - begin
    });
}
var final = current.Select(actor =>
{
    Require(Point2.Distance(actor.Position, world.Center(actor.GoalCell)) < 1e-6, "Witness left actor off goal.");
    bool attacks = ContinuousOracle.CanAttack(world, actor.Position, requests[actor.Id].Profile, world.Target(actor.TargetId));
    return new UnitSnapshot(actor.Id, actor.Position, attacks ? MotionState.Attacking : MotionState.Waiting, actor.GoalCell, actor.TargetId);
}).ToArray();
oracle.ValidateTransition(current, final, 0);
for (int hold = 0; hold < 200 * SimulationProtocol.Substeps; hold++)
{
    oracle.ValidateTransition(final, final, SimulationProtocol.MicrostepMilliseconds);
}
Require(
    final.Count(actor => actor.State == MotionState.Attacking) == 12 && final.All(actor => actor.State != MotionState.Moving),
    "35-body witness incomplete.");
var result = new
{
    success = true,
    scope = "Independent cooperative recovery witness from actual C17 failed state, not successful candidate execution or original spawn-history replay. "
        + "The remaining 152 queued requests were not generated by this witness; "
        + "completing35 present bodies alone is not a pass for the original200-request test.",
    originalReportSha256 = FileHash("original-report.json"),
    originalCanonicalInputSha256 = original.RootElement.GetProperty("InputSha256").GetString(),
    coreAssemblySha256 = FileHash(typeof(Point2).Assembly.Location),
    oracleAssemblySha256 = FileHash(typeof(ContinuousOracle).Assembly.Location),
    originalFrozenC17CandidateSha256 = FileHash("FrozenDlls/NavigationLab.CSupCom2.dll"),
    alive = final.Length,
    originalPending = original.RootElement.GetProperty("Pending").GetInt32(),
    changedGoalSet = false,
    permutedCompatibleUnassignedGoalOwners = newGoals,
    originalRadiiMm = requests.Values.Select(request => request.Profile.RadiusMm).Distinct(),
    originalSpeedMmPer50ms = requests.Values.Select(request => request.Profile.SpeedMmPerTick).Distinct(),
    movementMicrosteps = microstep,
    movementMilliseconds = microstep * SimulationProtocol.MicrostepMilliseconds,
    executedMoves = executed,
    finalAttack = final.Count(actor => actor.State == MotionState.Attacking),
    finalWait = final.Count(actor => actor.State == MotionState.Waiting),
    holdLogicalTicks = 200,
    holdDriftMm = 0,
    pairSweeps = oracle.PairSweeps,
    bodySweeps = oracle.BodySweeps,
    minimumBodyGapMm = oracle.MinimumBodyGapMm,
    maximumStepMm = oracle.MaximumObservedStepMm,
    scriptSha256 = FileHash("Program.cs")
};
File.WriteAllText("cooperative-replay.json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));

static string FileHash(string name) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(name))).ToLowerInvariant();

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Inspect(WorldDefinition world, Dictionary<int, SpawnRequest> requests, JsonElement original)
{
    var factory = new NavigationLab.CSupCom2.Factory();
    using var navigator = factory.Create(world, requests.Values.Select(request => request.Profile).Distinct().ToArray());
    var commands = original.GetProperty("Commands").EnumerateArray().ToArray();
    int commandIndex = 0;
    var kindType = navigator.GetType();
    const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    var actorsField = kindType.GetField("actors", fields)!;
    var actors = (System.Collections.IDictionary)actorsField.GetValue(navigator)!;
    var selected = new HashSet<int> { 38, 40, 45, 46, 48 };
    var last = new Dictionary<int, string>();
    var events = new List<object>();
    int calls = 0;
    for (int microstep = 0; microstep <= 80000; microstep++)
    {
        while (commandIndex < commands.Length && commands[commandIndex].GetProperty("microstep").GetInt32() == microstep)
        {
            var command = commands[commandIndex++];
            int id = command.GetProperty("Id").GetInt32();
            string kind = command.GetProperty("kind").GetString()!;
            if (kind == "death")
            {
                Require(navigator.Remove(id), "Recorded death failed.");
            }
            else if (kind == "spawn" || kind == "spawn-rejected")
            {
                bool accepted = navigator.TrySpawn(requests[id]);
                Require(accepted == (kind == "spawn"), "Recorded birth result differs at microstep " + microstep);
            }
            else
            {
                throw new InvalidOperationException("Unexpected command " + kind);
            }
            calls++;
        }
        navigator.Step();
        if (microstep < 31610)
        {
            continue;
        }
        foreach (System.Collections.DictionaryEntry entry in actors)
        {
            int id = (int)entry.Key;
            if (!selected.Contains(id))
            {
                continue;
            }
            object actor = entry.Value!;
            object? Get(string name) => actor.GetType().GetField(name, fields)!.GetValue(actor);
            string signature = Get("Goal") + ":" + Get("YieldGoal") + ":" + Get("YieldRequester") + ":" + Get("YieldUntil");
            if (last.TryGetValue(id, out string? previous) && signature == previous)
            {
                continue;
            }
            last[id] = signature;
            Point2 position = (Point2)Get("Position")!;
            events.Add(new
            {
                microstep = microstep + 1,
                id,
                x = position.X,
                y = position.Y,
                goal = Get("Goal"),
                yieldGoal = Get("YieldGoal"),
                requester = Get("YieldRequester"),
                yieldUntil = Get("YieldUntil"),
                stuck = Get("Stuck")
            });
        }
    }
    string canonical = string.Join("|", navigator.Snapshot().OrderBy(actor => actor.Id).Select(actor => actor.Id + ":"
        + actor.Position.X.ToString("R", CultureInfo.InvariantCulture) + "," + actor.Position.Y.ToString("R", CultureInfo.InvariantCulture)
        + ":" + (int)actor.State + ":" + actor.GoalCell + ":" + actor.TargetId));
    Require(canonical == original.GetProperty("FinalState").GetString(), "Read-only command replay differs from original final state.");
    var result = new
    {
        success = true,
        scope = "Read-only replay of all recorded commands against final frozen C17 DLL to expose internal yield transitions; no new matrix acceptance.",
        originalFinalStateExactlyMatched = true,
        commandsExecuted = calls,
        microstepsExecuted = 80001,
        candidateSha256 = FileHash(typeof(NavigationLab.CSupCom2.Factory).Assembly.Location),
        sourceSha256 = FileHash("Program.cs"),
        events
    };
    File.WriteAllText("inspection-result.json", JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }) + "\n");
    Console.WriteLine("PASS same35-body final state after80001 steps; observed " + events.Count + " goal/yield transitions.");
}
