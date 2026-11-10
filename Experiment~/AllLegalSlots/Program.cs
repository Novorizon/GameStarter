using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

string output = args.Length > 0 ? args[0] : "Artifacts/AllLegalSlots";
Directory.CreateDirectory(output);
string domain = Path.GetFullPath(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(value => value.Key == "CandidateDomain").Value!);
var hashes = Hashes(domain);
string project = Path.GetFullPath(Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(value => value.Key == "VerificationProject").Value!);
var testHashes = Directory.GetFiles(project, "*.cs", SearchOption.TopDirectoryOnly).OrderBy(path => path)
    .ToDictionary(path => path, path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { success = false, state = "running" }, JsonSettings.Value));
var timer = Stopwatch.StartNew();
var records = new List<object>();
foreach (var spec in (Array.IndexOf(args, "--late-only") >= 0 ? Array.Empty<(int Radius, int Range, int Speed)>()
    : new[] { (Radius: 250, Range: 550, Speed: 55), (Radius: 200, Range: 450, Speed: 100) }))
{
    var test = new ForestProductionTest(spec.Radius, spec.Range, spec.Speed);
    var record = test.Run();
    records.Add(record);
    File.WriteAllText(Path.Combine(output, "forest-r" + spec.Radius + ".json"), JsonSerializer.Serialize(record, JsonSettings.Value));
    Console.WriteLine("FOREST " + JsonSerializer.Serialize(record));
}

if (Array.IndexOf(args, "--late-small") >= 0)
{
    foreach (var variant in new[] { (Speed: 100, RequestTick: 0), (Speed: 165, RequestTick: 4000) })
    {
        var late = new ForestProductionTest(250, 550, 55, true, variant.Speed).RunLateSmall(variant.RequestTick);
        records.Add(late);
        File.WriteAllText(Path.Combine(output, "forest-late-small-" + variant.Speed + ".json"), JsonSerializer.Serialize(late, JsonSettings.Value));
        Console.WriteLine("LATE " + JsonSerializer.Serialize(late));
    }
}

bool unchanged = hashes.OrderBy(pair => pair.Key).SequenceEqual(Hashes(domain).OrderBy(pair => pair.Key));
if (!unchanged)
{
    throw new InvalidOperationException("Candidate source changed during verification.");
}

File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new
{
    success = true,
    utc = DateTime.UtcNow.ToString("O"),
    elapsedMilliseconds = timer.ElapsedMilliseconds,
    domain,
    sourceFilesUnchanged = unchanged,
    sourceHashes = hashes,
    testHashes,
    testFilesUnchanged = testHashes.All(pair => pair.Value == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(pair.Key))).ToLowerInvariant()),
    mapSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes("Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"))).ToLowerInvariant(),
    assemblySha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Assembly.GetExecutingAssembly().Location))).ToLowerInvariant(),
    records
}, JsonSettings.Value));

static Dictionary<string, string> Hashes(string directory)
{
    return Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories).OrderBy(path => path)
        .ToDictionary(path => Path.GetRelativePath(directory, path).Replace('\\', '/'),
            path => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
}

internal static class JsonSettings
{
    internal static readonly JsonSerializerOptions Value = new JsonSerializerOptions { WriteIndented = true, IncludeFields = true };
}

internal sealed class ForestProductionTest
{
    private readonly int radius;
    private readonly int range;
    private readonly int speed;
    private readonly NavigationGrid grid;
    private readonly NavigationCrowd crowd;
    private readonly Dictionary<int, Position2> previous = new Dictionary<int, Position2>();
    private readonly HashSet<int> active = new HashSet<int>();
    private readonly HashSet<int> smallActors = new HashSet<int>();
    private readonly int smallSpeed;
    private int next = 1;
    private long tick;
    private long checkedPairs;
    private long checkedBodies;
    private double minimumGap = double.MaxValue;
    private readonly HashSet<int> validatedAttackLines = new HashSet<int>();
    private int[] attackCells = Array.Empty<int>();
    private readonly List<object> admissionPlans = new List<object>();
    private readonly List<object> deaths = new List<object>();

    internal ForestProductionTest(int radius, int range, int speed, bool registerSmall = false, int smallSpeed = 100)
    {
        this.radius = radius;
        this.range = range;
        this.speed = speed;
        this.smallSpeed = smallSpeed;
        Require(MinimumDistanceSquared(new Position2(0, 0), new Position2(1000, 0),
            new Position2(1000, 0), new Position2(1000, 1000)) < 800L * 800,
            "The independent swept body oracle missed a known interval collision.");
        Require(LineHitsRectangle(new Position2(-1, 1), new Position2(3, 1), 0, 0, 2, 2)
            && !LineHitsRectangle(new Position2(-1, 3), new Position2(3, 3), 0, 0, 2, 2),
            "The independent line/rectangle oracle failed its known intersections.");
        grid = NavigationLayout.Parse(File.ReadAllText("Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
        crowd = new NavigationCrowd(grid);
        crowd.RegisterProfile(radius, range);
        if (registerSmall)
        {
            crowd.RegisterProfile(180, 450);
        }
    }

    internal object Run()
    {
        int count = crowd.CountAttackSlots(radius, range) + crowd.CountGatherSlots(radius, range);
        int expected = radius == 250 ? 44 : 45;
        Require(count == expected, "All legal forest goals did not match the independently witnessed count.");
        Fill(count);
        long admissionTick = tick;
        Hold(200);
        attackCells = crowd.Agents.Where(agent => agent.Status == NavigationAgentStatus.Attacking)
            .Select(agent => agent.GoalCell).OrderBy(cell => cell).ToArray();
        foreach (int goal in attackCells)
        {
            var victim = crowd.Agents.Single(agent => agent.GoalCell == goal && agent.Status == NavigationAgentStatus.Attacking);
            int id = victim.Id;
            Require(crowd.Remove(id) && active.Remove(id), "Explicit death failed.");
            previous.Remove(id);
            Validate();
            long start = tick;
            Fill(count);
            deaths.Add(new { goal, victim = id, recoveryTicks = tick - start, attacks = Attacks() });
        }

        Hold(200);
        return new
        {
            success = true, radius, range, speed, expected, active = crowd.Agents.Count, attacking = Attacks(),
            waiting = crowd.Agents.Count - Attacks(), admissionTick, finalTick = tick, deaths,
            checkedPairs, checkedBodies, minimumBodyGapMm = minimumGap,
            crowd.StartedRecoveries, crowd.CompletedRecoveries, crowd.RecoveryMoveCount, crowd.TrafficReplanCount,
            mapSha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(
                "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"))).ToLowerInvariant()
        };
    }

    internal object RunLateSmall(int requestTick)
    {
        Fill(44);
        long bigArrived = tick;
        var bigGoals = crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.GoalCell);
        while (tick < requestTick)
        {
            Step();
        }

        long requested = tick;
        long deadline = tick + 10000;
        while (tick < deadline)
        {
            Validate();
            if (smallActors.Count < 2)
            {
                var before = crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
                if (crowd.TrySpawn(next, grid.Center(grid.SpawnCells[0]), smallSpeed, 180, 450))
                {
                    Require(active.Add(next), "Duplicate mixed active identity.");
                    smallActors.Add(next++);
                    CaptureAdmissionPlan();
                }

                Require(crowd.CurrentTick == tick, "Late birth moved the clock.");
                foreach (var pair in before)
                {
                    Require(Same(pair.Value, crowd.Agents.Single(agent => agent.Id == pair.Key).Position), "Late birth moved a body.");
                }
            }

            Validate();
            if (smallActors.Count == 2 && crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating))
            {
                long allArrived = tick;
                Hold(200);
                Require(Attacks() == 20 && crowd.Agents.Count == 46, "Mixed final attack/holding quota was not reached.");
                Require(crowd.Agents.Where(agent => smallActors.Contains(agent.Id)).Select(agent => agent.GoalCell)
                    .OrderBy(cell => cell).SequenceEqual(new[] { 256, 257 }), "Small actors did not occupy the two Small-only cells.");
                foreach (var claim in bigGoals)
                {
                    Require(crowd.Agents.Single(agent => agent.Id == claim.Key).GoalCell == claim.Value,
                        "A yielded Big did not return to its original final goal.");
                }

                var finalGoals = crowd.Agents.OrderBy(agent => agent.Id)
                    .Select(agent => new { agent.Id, agent.GoalCell, agent.Status, agent.Position }).ToArray();
                var recovery = RunMixedDeaths();
                return new
                {
                    success = true, bigArrived, requested, allArrived, finalTick = tick, smallSpeed,
                    active = crowd.Agents.Count, attacking = Attacks(), waiting = crowd.Agents.Count - Attacks(),
                    admissionPlans, small = smallActors.Count, smallAttacking = crowd.Agents.Count(agent => smallActors.Contains(agent.Id)
                        && agent.Status == NavigationAgentStatus.Attacking), finalGoals, recovery, checkedPairs, checkedBodies,
                    crowd.StartedRecoveries, crowd.CompletedRecoveries, crowd.AdmissionGateRecoveries,
                    crowd.AdmissionGateYieldedActors, crowd.AdmissionGateExpandedCells, crowd.AdmissionGateBudgetFailures
                };
            }

            Step();
        }

        throw new InvalidOperationException("Late small actors did not enter: big=" + (crowd.Agents.Count - smallActors.Count)
            + ", small=" + smallActors.Count + ", tick=" + tick + ", last=" + crowd.LastFailure);
    }

    private void CaptureAdmissionPlan()
    {
        object Read(object target, string field) => target.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(target)!;
        var states = (IDictionary)typeof(NavigationCrowd)
            .GetField("occupants", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(crowd)!;
        var paths = new List<object>();
        foreach (DictionaryEntry entry in states)
        {
            var agent = (NavigationAgent)Read(entry.Value!, "Agent");
            var cells = ((IEnumerable)Read(entry.Value!, "Path")).Cast<object>().Select(value => new
            {
                cell = (int)Read(value, "Cell"),
                arrival = (long)Read(value, "Arrival"),
                departure = (long)Read(value, "Departure")
            }).ToArray();
            if (cells.Length > 0 && cells[cells.Length - 1].arrival > tick)
            {
                paths.Add(new { agent.Id, agent.GoalCell, agent.Speed, cells });
            }
        }

        admissionPlans.Add(new { tick, paths });
    }

    private object[] RunMixedDeaths()
    {
        var records = new List<object>();
        var targets = crowd.Agents.Where(agent => agent.Status == NavigationAgentStatus.Attacking)
            .Select(agent => agent.GoalCell).OrderBy(cell => cell).ToArray();
        foreach (int goal in targets)
        {
            var victim = crowd.Agents.Single(agent => agent.GoalCell == goal);
            bool wasSmall = smallActors.Remove(victim.Id);
            Require(crowd.Remove(victim.Id) && active.Remove(victim.Id), "Mixed explicit death failed.");
            previous.Remove(victim.Id);
            long removed = tick;
            int replacement = next++;
            bool admitted = false;
            int stable = 0;
            while (tick < removed + 10000)
            {
                Validate();
                if (!admitted)
                {
                    var before = crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
                    admitted = crowd.TrySpawn(replacement, grid.Center(grid.SpawnCells[0]),
                        wasSmall ? smallSpeed : speed, wasSmall ? 180 : radius, wasSmall ? 450 : range);
                    Require(crowd.CurrentTick == tick, "Mixed birth moved the clock.");
                    foreach (var pair in before)
                    {
                        Require(Same(pair.Value, crowd.Agents.Single(agent => agent.Id == pair.Key).Position), "Mixed birth moved a body.");
                    }

                    if (admitted)
                    {
                        active.Add(replacement);
                        if (wasSmall)
                        {
                            smallActors.Add(replacement);
                        }
                    }
                }

                Validate();
                bool ready = admitted && crowd.Agents.Count == 46 && Attacks() == 20
                    && crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating);
                stable = ready ? stable + 1 : 0;
                if (stable >= 20)
                {
                    break;
                }

                Step();
            }

            Require(stable >= 20, "Mixed death/replenish did not return to all 46 bodies and 20 attacks; goal=" + goal);
            records.Add(new { goal, victim = victim.Id, wasSmall, replacement, recoveryTicks = tick - removed });
        }

        Hold(200);
        return records.ToArray();
    }

    private void Fill(int count)
    {
        long deadline = tick + 10000;
        int stable = 0;
        while (tick < deadline)
        {
            Validate();
            if (crowd.Agents.Count < count)
            {
                var before = crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
                if (crowd.TrySpawn(next, grid.Center(grid.SpawnCells[0]), speed, radius, range))
                {
                    Require(active.Add(next), "Duplicate active identity.");
                    next++;
                }

                Require(crowd.CurrentTick == tick, "Birth moved the simulation clock.");
                foreach (var pair in before)
                {
                    Require(Same(pair.Value, crowd.Agents.Single(agent => agent.Id == pair.Key).Position), "Birth moved an existing body.");
                }
            }

            Validate();
            bool ready = crowd.Agents.Count == count && crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating)
                && Attacks() == crowd.CountAttackSlots(radius, range);
            stable = ready ? stable + 1 : 0;
            if (stable >= 20)
            {
                return;
            }

            Step();
        }

        throw new InvalidOperationException("Forest did not saturate: active=" + crowd.Agents.Count + "/" + count
            + ", attacks=" + Attacks() + ", last=" + crowd.LastFailure + ", recovery=" + crowd.RecoveryActive);
    }

    private int Attacks() => crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking);

    private void Hold(int ticks)
    {
        for (int index = 0; index < ticks; index++)
        {
            Step();
        }
    }

    private void Step()
    {
        previous.Clear();
        foreach (var agent in crowd.Agents)
        {
            previous.Add(agent.Id, agent.Position);
        }

        crowd.Step(++tick);
        Validate(true);
    }

    private void Validate(bool swept = false)
    {
        Require(active.SetEquals(crowd.Agents.Select(agent => agent.Id)), "An admitted body disappeared or appeared without a request.");
        foreach (var agent in crowd.Agents)
        {
            int actorRadius = smallActors.Contains(agent.Id) ? 180 : radius;
            int actorRange = smallActors.Contains(agent.Id) ? 450 : range;
            int actorSpeed = smallActors.Contains(agent.Id) ? smallSpeed : speed;
            Require(agent.Radius == actorRadius && agent.AttackRange == actorRange && agent.Speed == actorSpeed, "A requested body was modified.");
            var from = swept && previous.TryGetValue(agent.Id, out var old) ? old : agent.Position;
            var to = agent.Position;
            Require(from.X == to.X || from.Y == to.Y, "Tick motion crossed more than one cardinal segment.");
            Require(Squared(from, to) <= (long)actorSpeed * actorSpeed, "Speed cap violated.");
            long borderRight = grid.Origin.X + (long)grid.Width * grid.CellSize;
            long borderTop = grid.Origin.Y + (long)grid.Height * grid.CellSize;
            Require(Math.Min(from.X, to.X) - actorRadius >= grid.Origin.X && Math.Max(from.X, to.X) + actorRadius <= borderRight
                && Math.Min(from.Y, to.Y) - actorRadius >= grid.Origin.Y && Math.Max(from.Y, to.Y) + actorRadius <= borderTop, "World boundary crossed.");
            long baseClearance = grid.TargetRadius + actorRadius;
            Require(MinimumDistanceSquared(from, to, grid.TargetCenter, grid.TargetCenter) + .000001 >= baseClearance * baseClearance,
                "Base penetration.");
            int left = Math.Max(0, (Math.Min(from.X, to.X) - actorRadius - grid.Origin.X) / grid.CellSize - 1);
            int right = Math.Min(grid.Width - 1, (Math.Max(from.X, to.X) + actorRadius - grid.Origin.X) / grid.CellSize + 1);
            int bottom = Math.Max(0, (Math.Min(from.Y, to.Y) - actorRadius - grid.Origin.Y) / grid.CellSize - 1);
            int top = Math.Min(grid.Height - 1, (Math.Max(from.Y, to.Y) + actorRadius - grid.Origin.Y) / grid.CellSize + 1);
            for (int y = bottom; y <= top; y++)
            {
                for (int x = left; x <= right; x++)
                {
                    if (grid.IsWalkable(y * grid.Width + x))
                    {
                        continue;
                    }

                    long wallX = grid.Origin.X + x * grid.CellSize;
                    long wallY = grid.Origin.Y + y * grid.CellSize;
                    long dx = Math.Max(0, Math.Max(wallX - Math.Max(from.X, to.X), Math.Min(from.X, to.X) - wallX - grid.CellSize));
                    long dy = Math.Max(0, Math.Max(wallY - Math.Max(from.Y, to.Y), Math.Min(from.Y, to.Y) - wallY - grid.CellSize));
                    Require(dx * dx + dy * dy >= (long)actorRadius * actorRadius, "Blocked terrain penetration.");
                }
            }

            if (agent.Status != NavigationAgentStatus.Navigating)
            {
                Require(Same(to, grid.Center(agent.GoalCell)) && grid.HoldingAllowed(agent.GoalCell), "Remote waiting was counted as arrival.");
                if (agent.Status == NavigationAgentStatus.Attacking)
                {
                    long reach = grid.TargetRadius + actorRadius + actorRange;
                    Require(Squared(to, grid.TargetCenter) <= reach * reach, "An attack was out of physical range.");
                    VerifyAttackLine(agent.GoalCell);
                }
            }

            checkedBodies++;
        }

        for (int first = 0; first < crowd.Agents.Count; first++)
        {
            var a = crowd.Agents[first];
            var a0 = swept && previous.TryGetValue(a.Id, out var oldA) ? oldA : a.Position;
            for (int second = first + 1; second < crowd.Agents.Count; second++)
            {
                var b = crowd.Agents[second];
                var b0 = swept && previous.TryGetValue(b.Id, out var oldB) ? oldB : b.Position;
                double square = MinimumDistanceSquared(a0, a.Position, b0, b.Position);
                Require(square + .000001 >= (long)(a.Radius + b.Radius) * (a.Radius + b.Radius), "Moving bodies overlapped.");
                minimumGap = Math.Min(minimumGap, Math.Sqrt(square) - a.Radius - b.Radius);
                checkedPairs++;
            }
        }
    }

    private void VerifyAttackLine(int cell)
    {
        if (validatedAttackLines.Contains(cell))
        {
            return;
        }

        int target = grid.CellAt(grid.TargetCenter);
        for (int y = Math.Min(cell / grid.Width, target / grid.Width); y <= Math.Max(cell / grid.Width, target / grid.Width); y++)
        {
            for (int x = Math.Min(cell % grid.Width, target % grid.Width); x <= Math.Max(cell % grid.Width, target % grid.Width); x++)
            {
                if (grid.IsWalkable(y * grid.Width + x))
                {
                    continue;
                }

                int left = grid.Origin.X + x * grid.CellSize;
                int bottom = grid.Origin.Y + y * grid.CellSize;
                Require(!LineHitsRectangle(grid.Center(cell), grid.TargetCenter, left, bottom,
                    left + grid.CellSize, bottom + grid.CellSize), "An attack line intersected blocked terrain.");
            }
        }

        validatedAttackLines.Add(cell);
    }

    private static bool LineHitsRectangle(Position2 from, Position2 to, double left, double bottom, double right, double top)
    {
        double first = 0;
        double last = 1;
        double dx = (double)to.X - from.X;
        double dy = (double)to.Y - from.Y;
        bool Clip(double direction, double distance)
        {
            if (direction == 0)
            {
                return distance >= 0;
            }

            double fraction = distance / direction;
            if (direction < 0)
            {
                if (fraction > last)
                {
                    return false;
                }

                first = Math.Max(first, fraction);
            }
            else
            {
                if (fraction < first)
                {
                    return false;
                }

                last = Math.Min(last, fraction);
            }

            return true;
        }

        return Clip(-dx, from.X - left) && Clip(dx, right - from.X)
            && Clip(-dy, from.Y - bottom) && Clip(dy, top - from.Y);
    }

    private static bool Same(Position2 a, Position2 b) => a.X == b.X && a.Y == b.Y;
    private static long Squared(Position2 a, Position2 b) => ((long)a.X - b.X) * ((long)a.X - b.X) + ((long)a.Y - b.Y) * ((long)a.Y - b.Y);

    private static double MinimumDistanceSquared(Position2 a0, Position2 a1, Position2 b0, Position2 b1)
    {
        double x = (double)a0.X - b0.X;
        double y = (double)a0.Y - b0.Y;
        double dx = (double)a1.X - a0.X - b1.X + b0.X;
        double dy = (double)a1.Y - a0.Y - b1.Y + b0.Y;
        double denominator = dx * dx + dy * dy;
        double fraction = denominator == 0 ? 0 : Math.Clamp(-(x * dx + y * dy) / denominator, 0, 1);
        x += dx * fraction;
        y += dy * fraction;
        return x * x + y * y;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
