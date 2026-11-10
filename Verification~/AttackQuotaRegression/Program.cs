using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

string output = args.Length == 0 ? "Artifacts/AttackQuotaRegression" : args[0];
Directory.CreateDirectory(output);
var original = Hashes();
string project = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(value => value.Key == "VerificationProject").Value!;
var testFiles = Directory.GetFiles(project, "*.cs", SearchOption.TopDirectoryOnly).Concat(new[]
{
    Path.Combine(project, "../../Verification~/TowerDefenseNavigation/GeometryChecks.cs"),
    Path.Combine(project, "../../Verification~/TowerDefenseNavigation/Observations.cs"),
    Path.Combine(project, "../../Verification~/TowerDefenseNavigation/Scenarios.cs"),
    Path.Combine(project, "AttackQuotaRegression.csproj")
}).Select(Path.GetFullPath).ToDictionary(path => path,
    path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
File.WriteAllText(Path.Combine(output, "summary.json"), "{\"success\":false,\"state\":\"running\"}");
var results = new List<object>();
foreach (var spawn in (args.Contains("--stress")
    ? new[] { new Cell(1, 17), new Cell(17, 1), new Cell(33, 17), new Cell(17, 33) }
    : new[] { new Cell(1, 17) }))
{
    foreach (bool resetToZero in (args.Contains("--zero-only") ? new[] { true } : new[] { false, true }))
    {
        var test = new QuotaLifecycleTest(spawn);
        var result = test.Run(resetToZero);
        results.Add(result);
        File.WriteAllText(Path.Combine(output, (resetToZero ? "zero-and-new-wave-" : "demand-shrink-and-regrow-")
            + spawn.X + "-" + spawn.Y + ".json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("PASS " + JsonSerializer.Serialize(result));
    }
}

if (!original.OrderBy(pair => pair.Key).SequenceEqual(Hashes().OrderBy(pair => pair.Key)))
{
    throw new InvalidOperationException("Production source changed during quota verification.");
}

File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
{
    success = true,
    sourceFilesUnchanged = true,
    sourceHashes = original,
    testHashes = testFiles,
    testSourceFilesUnchanged = testFiles.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))).ToLowerInvariant()),
    binarySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))).ToLowerInvariant(),
    results
}, new JsonSerializerOptions { WriteIndented = true }));

static Dictionary<string, string> Hashes()
{
    string root = System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttributes<System.Reflection.AssemblyMetadataAttribute>()
        .Single(value => value.Key == "CandidateDomain").Value!;
    return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories).ToDictionary(path => path,
        path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
}

internal sealed class QuotaLifecycleTest
{
    private readonly NavigationScenario geometry;
    private readonly Cell spawn;
    private readonly NavigationCrowd crowd;
    private readonly Dictionary<int, UnitObservation> previous = new Dictionary<int, UnitObservation>();
    private readonly Dictionary<int, int> active = new Dictionary<int, int>();
    private readonly List<object> phases = new List<object>();
    private int next = 1;
    private int tick;

    internal QuotaLifecycleTest(Cell spawn)
    {
        this.spawn = spawn;
        GeometryChecks.VerifyOracle();
        geometry = new NavigationScenario
        {
            Width = 35,
            Height = 35,
            Target = new Cell(17, 17),
            Walkable = Enumerable.Repeat(true, 35 * 35).ToArray(),
            SpawnCells = new[] { spawn },
            GatheringDistance = 10000
        };
        var grid = new NavigationGrid(
            35,
            35,
            1000,
            new Position2(0, 0),
            geometry.Walkable,
            new Position2(17500, 17500),
            1000,
            null,
            new[] { spawn.Y * 35 + spawn.X });
        crowd = new NavigationCrowd(grid, planningHorizon: 10000, gatheringDistance: 10000);
        crowd.RegisterProfile(250, 1000);
        crowd.RegisterProfile(250, 4000);
    }

    internal object Run(bool resetToZero)
    {
        Add(16, 1000);
        Add(84, 4000);
        Stable(16, 68);
        Save("initial-short16-long68");

        var small = active.Where(pair => pair.Value == 1000).Select(pair => pair.Key).OrderBy(id => id).ToArray();
        foreach (int id in small.Take(resetToZero ? 16 : 15))
        {
            Remove(id);
        }

        Stable(resetToZero ? 0 : 1, resetToZero ? 84 : 83);
        Save(resetToZero ? "profile-zero-no-reserved-vacancy" : "demand-one-no-reserved-vacancy");
        if (resetToZero)
        {
            Add(3, 1000);
            Stable(1, 83);
            Save("new-wave-resets-old-peak-to-one");
        }
        else
        {
            Add(15, 1000);
            Stable(16, 68);
            Save("regrown-demand-restores-established-sixteen");
        }

        Hold(300);
        if (resetToZero)
        {
            // Isolate ordinary-vacancy growth from the legitimate priority of an existing Long waiter.
            // These are explicit deaths; no body is removed by a solver or omitted from the live-ID oracle.
            foreach (var waiter in crowd.Agents.Where(actor => actor.AttackRange == 4000
                && actor.Status == NavigationAgentStatus.Waiting).ToArray())
            {
                Remove(waiter.Id);
            }
        }

        int before = Attackers(1000);
        int removed = 0;
        foreach (var actor in crowd.Agents.Where(actor => actor.AttackRange == 4000
            && actor.Status == NavigationAgentStatus.Attacking && ShortCompatible(actor.GoalCell)).ToArray())
        {
            Remove(actor.Id);
            removed++;
        }

        Require(removed > 0 || before == 16, "No ordinary Short vacancy was exercised.");
        int missing = 16 - active.Values.Count(range => range == 1000);
        Add(missing, 1000);
        Stable(16, 68);
        Save("ordinary-empty-attacks-can-grow-above-minimum");
        Hold(300);
        return new
        {
            success = true,
            resetToZero,
            spawn,
            tick,
            active = crowd.Agents.Count,
            shortAttackers = Attackers(1000),
            longAttackers = Attackers(4000),
            waiting = crowd.Agents.Count(actor => actor.Status == NavigationAgentStatus.Waiting),
            crowd.AttackQuotaExchanges,
            crowd.AdmissionGateBudgetFailures,
            phases
        };
    }

    private void Add(int count, int range)
    {
        int added = 0;
        int deadline = tick + 10000;
        while (added < count && tick < deadline)
        {
            var before = crowd.Agents.ToDictionary(actor => actor.Id, actor => actor.Position);
            long beforeWork = crowd.TotalExpandedNodes;
            long beforeGateWork = crowd.AdmissionGateExpandedCells;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            bool born = crowd.TrySpawn(next, crowd.Grid.Center(spawn.Y * 35 + spawn.X), 1000, 250, range);
            if (timer.ElapsedMilliseconds > 200)
            {
                Console.WriteLine("SLOW API tick=" + tick + " accepted=" + born + " ms=" + timer.ElapsedMilliseconds
                    + " work=" + (crowd.TotalExpandedNodes - beforeWork) + " gateWork=" + (crowd.AdmissionGateExpandedCells - beforeGateWork)
                    + " budgetFailures=" + crowd.AdmissionGateBudgetFailures + " last=" + crowd.LastFailure);
                if (crowd.GetType().GetProperty("AdmissionGateNotes")?.GetValue(crowd) is List<string> notes)
                {
                    Console.WriteLine("NOTES " + string.Join(" | ", notes.TakeLast(15)));
                }
            }
            Require(crowd.CurrentTick == tick, "An API advanced time.");
            foreach (var pair in before)
            {
                var now = crowd.Agents.Single(actor => actor.Id == pair.Key).Position;
                Require(now.X == pair.Value.X && now.Y == pair.Value.Y, "An API moved an existing body.");
            }

            if (born)
            {
                active.Add(next++, range);
                added++;
            }

            Validate();
            Step();
            if (tick % 500 == 0)
            {
                Console.WriteLine("ADD tick=" + tick + " added=" + added + "/" + count + " last=" + crowd.LastFailure
                    + " budget=" + crowd.AdmissionGateBudgetFailures + " Short=" + Attackers(1000));
            }
        }

        Require(added == count, "A feasible quota request was left pending.");
    }

    private void Remove(int id)
    {
        Require(crowd.Remove(id) && active.Remove(id), "An explicit quota death failed.");
        previous.Remove(id);
        Validate();
    }

    private void Stable(int shortAttackers, int longAttackers)
    {
        int deadline = tick + 10000;
        int stable = 0;
        while (tick < deadline)
        {
            bool ready = Attackers(1000) == shortAttackers && Attackers(4000) == longAttackers
                && crowd.Agents.All(actor => actor.Status != NavigationAgentStatus.Navigating);
            stable = ready ? stable + 1 : 0;
            if (stable >= 50)
            {
                return;
            }

            Step();
        }

        throw new InvalidOperationException("Quota did not settle: short=" + Attackers(1000) + ", long=" + Attackers(4000));
    }

    private bool ShortCompatible(int goal)
    {
        var point = crowd.Grid.Center(goal);
        long x = point.X - 17500;
        long y = point.Y - 17500;
        return x * x + y * y <= 2250L * 2250;
    }

    private int Attackers(int range) => crowd.Agents.Count(actor => actor.AttackRange == range && actor.Status == NavigationAgentStatus.Attacking);

    private void Save(string phase)
    {
        var record = new { phase, tick, live = active.Count, shortAttackers = Attackers(1000), longAttackers = Attackers(4000) };
        phases.Add(record);
        Console.WriteLine("PHASE " + JsonSerializer.Serialize(record));
    }

    private void Hold(int count)
    {
        for (int index = 0; index < count; index++)
        {
            Step();
        }
    }

    private void Step()
    {
        crowd.Step(++tick);
        Validate();
    }

    private void Validate()
    {
        Require(active.Keys.ToHashSet().SetEquals(crowd.Agents.Select(actor => actor.Id)), "Quota lifecycle silently changed active IDs.");
        var current = crowd.Agents.Select(actor => new UnitObservation(actor.Id,
            new Cell(actor.Position.X / 1000, actor.Position.Y / 1000), NavigationState.Moving,
            actor.GoalCell, 1, actor.Position.X, actor.Position.Y, actor.Radius)).ToArray();
        GeometryChecks.VerifyUnitClearance(current, previous);
        foreach (var unit in current)
        {
            var old = previous.TryGetValue(unit.Id, out var prior) ? prior : unit;
            GeometryChecks.VerifyObstacleClearance(geometry, old, unit);
            GeometryChecks.VerifyTargetClearance(geometry, old, unit);
            Require((long)(unit.X - old.X) * (unit.X - old.X) + (long)(unit.Y - old.Y) * (unit.Y - old.Y) <= 1000000,
                "A quota body exceeded requested speed.");
            var actor = crowd.Agents.Single(actor => actor.Id == unit.Id);
            Require(actor.Radius == 250 && actor.Speed == 1000 && actor.AttackRange == active[actor.Id], "A quota body changed profile.");
            if (actor.Status != NavigationAgentStatus.Navigating)
            {
                var goal = crowd.Grid.Center(actor.GoalCell);
                Require(goal.X == unit.X && goal.Y == unit.Y && crowd.Grid.HoldingAllowed(actor.GoalCell), "A temporary cell was counted as final.");
            }

            if (actor.Status == NavigationAgentStatus.Attacking)
            {
                long reach = 1250L + actor.AttackRange;
                long x = unit.X - 17500;
                long y = unit.Y - 17500;
                Require(x * x + y * y <= reach * reach, "A quota attack was out of range.");
                GeometryChecks.VerifyAttackLine(geometry, unit);
            }

            previous[unit.Id] = unit;
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
