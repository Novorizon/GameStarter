using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal sealed record ForestRequest(int Id, int Radius, int Range, int Speed);
internal sealed record ForestCommand(long Tick, string Kind, ForestRequest Request, Position2 Position);
internal sealed record ForestInjection(long Tick, string Phase, int[] RemovedIds, int[] GateActors, bool JobActive);
internal sealed record ForestBody(int Id, int Radius, int Range, int Speed, int Goal, Position2 Position, string Status);

internal sealed class ForestResult
{
    public string Phase { get; init; } = "";
    public int Ordinal { get; init; }
    public int SmallSpeed { get; init; }
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public long BigArrivedTick { get; set; }
    public long SmallRequestedTick { get; set; }
    public ForestInjection? Injection { get; set; }
    public long FinalTick { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int Requested { get; set; }
    public int Admitted { get; set; }
    public int Active { get; set; }
    public int ExplicitDeaths { get; set; }
    public int Pending { get; set; }
    public int Attacking { get; set; }
    public bool QueueConserved { get; set; }
    public bool ContinuousOracleVerified { get; set; }
    public bool SurvivingBigGoalsRestored { get; set; }
    public string InputCommandsSha256 { get; set; } = "";
    public string LastFailure { get; set; } = "";
    public bool RecoveryActive { get; set; }
    public ForestBody[] FinalBodies { get; set; } = Array.Empty<ForestBody>();
    public ForestRequest[] QueuedRequests { get; set; } = Array.Empty<ForestRequest>();
    public List<ForestCommand> Commands { get; } = new List<ForestCommand>();
}

internal static class ForestGateLifecycle
{
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Run(string[] args)
    {
        string project = Path.GetFullPath(Metadata("AuditProjectRoot"));
        string domain = Path.GetFullPath(Metadata("NavigationDomainRoot"));
        string map = Argument(args, "--map") ?? Path.GetFullPath(Path.Combine(project, "../../Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"));
        string output = Argument(args, "--output") ?? "Artifacts/NavigationForestGateLifecycle";
        string? filter = Argument(args, "--phase");
        bool stress = Array.IndexOf(args, "--stress") >= 0;
        int[] speeds = int.TryParse(Argument(args, "--small-speed"), out int speed) ? new[] { speed } : stress ? new[] { 100, 165 } : new[] { 100 };
        var sourceHashes = Hashes(domain, "*.cs", true);
        var testHashes = Hashes(project, "*.cs", false);
        foreach (string file in new[]
        {
            "../TowerDefenseNavigationChallenges/Challenges.cs", "../TowerDefenseNavigationChallenges/GeometryTypes.cs",
            "../TowerDefenseNavigation/GeometryChecks.cs", "../TowerDefenseNavigation/Observations.cs"
        })
        {
            string path = Path.GetFullPath(Path.Combine(project, file));
            testHashes.Add(path, DigestFile(path));
        }
        testHashes.Add(map, DigestFile(map));
        string layout = Path.GetFullPath(Path.Combine(project, "../../Assets/HotUpdate/TowerDefense/Data/NavigationLayout.cs"));
        testHashes.Add(layout, DigestFile(layout));
        var results = new List<ForestResult>();
        Directory.CreateDirectory(output);
        GeometryChecks.VerifyOracle();
        QuotaGateLifecycle.VerifySpawnExclusionAndCache();
        foreach (int smallSpeed in speeds)
        {
            var cases = Cases(stress);
            if (filter != null)
            {
                cases = cases.Where(test => test.Phase == filter).ToArray();
                if (int.TryParse(Argument(args, "--ordinal"), out int ordinalFilter))
                {
                    cases = new[] { (Phase: filter, Ordinal: ordinalFilter) };
                }
            }
            foreach (var test in cases)
            {
                var result = Execute(map, test.Phase, test.Ordinal, smallSpeed);
                results.Add(result);
                File.WriteAllText(Path.Combine(output, test.Phase + "-" + test.Ordinal + "-" + smallSpeed + ".json"), JsonSerializer.Serialize(result, Json));
                Console.WriteLine($"{(result.Success ? "PASS" : "FAIL")} forest {test.Phase}[{test.Ordinal}] smallSpeed={smallSpeed}"
                    + $" active={result.Active} attack={result.Attacking} pending={result.Pending} dead={result.ExplicitDeaths} ticks={result.FinalTick}"
                    + $" injected={result.Injection != null} {result.Error}");
            }
        }
        bool sourceUnchanged = SameHashes(sourceHashes, Hashes(domain, "*.cs", true));
        bool testsUnchanged = testHashes.All(pair => DigestFile(pair.Key) == pair.Value);
        var summary = new
        {
            utc = DateTime.UtcNow.ToString("O"),
            domain,
            map,
            mapSha256 = DigestFile(map),
            sourceHashes,
            testHashes,
            testBinarySha256 = DigestFile(Assembly.GetExecutingAssembly().Location),
            sourceFilesUnchanged = sourceUnchanged,
            testSourceFilesUnchanged = testsUnchanged,
            nonSpawnFlexibilityAndEntryCacheVerified = true,
            count = results.Count,
            passed = results.Count(result => result.Success),
            success = results.Count > 0 && results.All(result => result.Success) && sourceUnchanged && testsUnchanged,
            results = results.Select(result => new
            {
                result.Phase, result.Ordinal, result.SmallSpeed, result.Success, result.Error, result.Injection,
                result.BigArrivedTick, result.SmallRequestedTick, result.FinalTick, result.ElapsedMilliseconds, result.Requested, result.Admitted,
                result.Active, result.Pending, result.ExplicitDeaths, result.Attacking, result.QueueConserved,
                result.ContinuousOracleVerified, result.SurvivingBigGoalsRestored, result.InputCommandsSha256
            })
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Json));
        return summary.success ? 0 : 1;
    }

    private static ForestResult Execute(string map, string phase, int ordinal, int smallSpeed)
    {
        var result = new ForestResult { Phase = phase, Ordinal = ordinal, SmallSpeed = smallSpeed };
        var timer = Stopwatch.StartNew();
        var grid = NavigationLayout.Parse(File.ReadAllText(map));
        var crowd = new NavigationCrowd(grid);
        crowd.RegisterProfile(250, 550);
        crowd.RegisterProfile(180, 450);
        var requests = Enumerable.Range(1, 44).Select(id => new ForestRequest(id, 250, 550, 55)).ToDictionary(request => request.Id);
        var pending = requests.Values.ToList();
        var admitted = new HashSet<int>();
        var deaths = new HashSet<int>();
        var parked = new HashSet<int>();
        var prior = new Dictionary<int, UnitObservation>();
        var bigGoals = new Dictionary<int, int>();
        var previousTick = new Dictionary<int, Position2>();
        var gateClaims = new Dictionary<int, int>();
        object? activeGateJob = null;
        var geometry = new NavigationScenario
        {
            Width = grid.Width,
            Height = grid.Height,
            Walkable = Enumerable.Range(0, grid.CellCount).Select(grid.IsWalkable).ToArray()
        };
        int nextId = 47;
        int stage = 0;
        int stable = 0;
        try
        {
            for (int tick = 0; tick <= 20000; tick++)
            {
                Validate();
                if (stage == 2 && result.Injection == null && crowd.RecoveryActive)
                {
                    var participants = GateParticipants(crowd);
                    var gates = participants.Where(bigGoals.ContainsKey).OrderBy(id => id).ToArray();
                    foreach (int id in gates)
                    {
                        var body = crowd.Agents.FirstOrDefault(agent => agent.Id == id);
                        if (body != null && !grid.HoldingAllowed(grid.CellAt(body.Position)) && Same(body.Position, grid.Center(grid.CellAt(body.Position)))
                            && previousTick.TryGetValue(id, out var previousPosition) && Same(body.Position, previousPosition))
                        {
                            parked.Add(id);
                        }
                    }
                    var selected = gates.Length > ordinal ? crowd.Agents.FirstOrDefault(agent => agent.Id == gates[ordinal]) : null;
                    NavigationAgent[] victims = Array.Empty<NavigationAgent>();
                    if (phase == "outbound-big" && selected != null && NonCenter(selected.Position) && !parked.Contains(selected.Id))
                    {
                        victims = new[] { selected };
                    }
                    else if (phase == "parked-big" && selected != null && parked.Contains(selected.Id) && !NonCenter(selected.Position)
                        && !grid.HoldingAllowed(grid.CellAt(selected.Position)))
                    {
                        victims = new[] { selected };
                    }
                    else if (phase == "returning-big" && selected != null && parked.Contains(selected.Id) && NonCenter(selected.Position)
                        && crowd.Agents.Any(agent => agent.Radius == 180 && agent.Status == NavigationAgentStatus.Attacking)
                        && previousTick.TryGetValue(selected.Id, out var lastPosition)
                        && Squared(selected.Position, grid.Center(selected.GoalCell)) < Squared(lastPosition, grid.Center(selected.GoalCell)))
                    {
                        victims = new[] { selected };
                    }
                    else if (phase == "center-big" && selected != null && !NonCenter(selected.Position)
                        && !grid.HoldingAllowed(grid.CellAt(selected.Position)) && previousTick.TryGetValue(selected.Id, out var centerPrior)
                        && !Same(centerPrior, selected.Position))
                    {
                        victims = new[] { selected };
                    }
                    else if ((phase == "all-gates" || phase == "two-gates") && gates.Length > 0 && selected != null && NonCenter(selected.Position))
                    {
                        var removed = phase == "two-gates" ? gates.Take(2).ToHashSet() : gates.ToHashSet();
                        victims = crowd.Agents.Where(agent => removed.Contains(agent.Id)).ToArray();
                    }
                    else if (phase == "moving-small" || phase == "arrived-small")
                    {
                        victims = crowd.Agents.Where(agent => participants.Contains(agent.Id) && agent.Radius == 180)
                            .Where(agent => phase == "moving-small" ? NonCenter(agent.Position) : agent.Status == NavigationAgentStatus.Attacking)
                            .Take(1).ToArray();
                    }
                    if (victims.Length > 0)
                    {
                        result.Injection = new ForestInjection(
                            tick,
                            phase,
                            victims.Select(agent => agent.Id).ToArray(),
                            participants.OrderBy(id => id).ToArray(),
                            true);
                        foreach (var victim in victims)
                        {
                            var before = Positions(crowd);
                            long beforeTick = crowd.CurrentTick;
                            var request = requests[victim.Id];
                            Require(crowd.Remove(victim.Id) && deaths.Add(victim.Id), "An explicit forest death failed.");
                            VerifyApi(before, beforeTick);
                            Record(request, "remove", victim.Position);
                            var replacement = request with { Id = nextId++ };
                            requests.Add(replacement.Id, replacement);
                            pending.Add(replacement);
                        }
                    }
                }

                if (stage != 1)
                {
                    foreach (var request in pending.ToArray())
                    {
                        var before = Positions(crowd);
                        long beforeTick = crowd.CurrentTick;
                        var source = grid.Center(grid.SpawnCells[0]);
                        bool born = crowd.TrySpawn(request.Id, source, request.Speed, request.Radius, request.Range);
                        CaptureGateClaims();
                        VerifyApi(before, beforeTick);
                        Record(request, born ? "spawn" : "spawn-rejected", source);
                        if (!born)
                        {
                            Require(!crowd.Agents.Any(agent => agent.Id == request.Id), "A rejected forest request created a body.");
                            break;
                        }
                        Require(admitted.Add(request.Id), "A forest request was admitted twice.");
                        pending.Remove(request);
                        Require(Same(crowd.Agents.Single(agent => agent.Id == request.Id).Position, source), "A forest birth skipped its source.");
                    }
                }
                Validate();
                Require(requests.Count == crowd.Agents.Count + pending.Count + deaths.Count, "Forest request conservation failed.");
                Require(crowd.Agents.Select(agent => agent.Id).ToHashSet().SetEquals(admitted.Except(deaths)), "Forest active IDs changed silently.");
                bool settled = crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating);
                if (stage == 0 && pending.Count == 0 && settled)
                {
                    Require(crowd.Agents.Count == 44, "The starting Big saturation was not established.");
                    Require(
                        crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking) == 18,
                        "The initial 18 Big attackers were not established.");
                    result.BigArrivedTick = tick;
                    bigGoals = crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.GoalCell);
                    stage = 1;
                }
                if (stage == 1 && tick >= result.BigArrivedTick + 200)
                {
                    result.SmallRequestedTick = tick;
                    foreach (int id in new[] { 45, 46 })
                    {
                        var request = new ForestRequest(id, 180, 450, smallSpeed);
                        requests.Add(id, request);
                        pending.Add(request);
                    }
                    stage = 2;
                }
                bool ready = stage == 2 && result.Injection != null && pending.Count == 0 && settled && crowd.Agents.Count == 46;
                stable = ready ? stable + 1 : 0;
                if (stable >= 200)
                {
                    Require(
                        crowd.Agents.Count(agent => agent.Radius == 250) == 44 && crowd.Agents.Count(agent => agent.Radius == 180) == 2,
                        "The compatible 44 Big + 2 Small population was not restored.");
                    Require(crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking) == 20, "The full attack formation was not restored.");
                    var smallGoals = crowd.Agents.Where(agent => agent.Radius == 180).Select(agent => agent.GoalCell).OrderBy(cell => cell);
                    Require(smallGoals.SequenceEqual(new[] { 256, 257 }), "Small actors did not use the two Small-only cells.");
                    result.SurvivingBigGoalsRestored = bigGoals.Where(pair => !deaths.Contains(pair.Key))
                        .All(pair => crowd.Agents.Single(agent => agent.Id == pair.Key).GoalCell == pair.Value);
                    result.Success = true;
                    result.QueueConserved = true;
                    result.ContinuousOracleVerified = true;
                    break;
                }
                previousTick = Positions(crowd);
                crowd.Step(crowd.CurrentTick + 1);
                CaptureGateClaims();
            }
            Require(result.Success, "Deadline: feasible late-46 population was not fully restored, or the required phase injection did not execute.");
        }
        catch (Exception error)
        {
            result.Error = error.ToString();
        }
        result.Requested = requests.Count;
        result.Admitted = admitted.Count;
        result.Active = crowd.Agents.Count;
        result.Pending = pending.Count;
        result.ExplicitDeaths = deaths.Count;
        result.Attacking = crowd.Agents.Count(agent => agent.Status == NavigationAgentStatus.Attacking);
        result.FinalTick = crowd.CurrentTick;
        result.ElapsedMilliseconds = timer.ElapsedMilliseconds;
        result.InputCommandsSha256 = Program.Digest(result.Commands);
        result.LastFailure = crowd.LastFailure.ToString();
        result.RecoveryActive = crowd.RecoveryActive;
        result.FinalBodies = crowd.Agents.Select(agent => new ForestBody(
            agent.Id,
            agent.Radius,
            agent.AttackRange,
            agent.Speed,
            agent.GoalCell,
            agent.Position,
            agent.Status.ToString())).ToArray();
        result.QueuedRequests = pending.ToArray();
        return result;

        void CaptureGateClaims()
        {
            var job = CurrentJob(crowd);
            if (ReferenceEquals(job, activeGateJob))
            {
                return;
            }
            activeGateJob = job;
            var participants = GateParticipants(crowd);
            gateClaims = crowd.Agents.Where(agent => participants.Contains(agent.Id) && agent.Radius == 250)
                .ToDictionary(agent => agent.Id, agent => agent.GoalCell);
        }

        void VerifyApi(Dictionary<int, Position2> before, long beforeTick)
        {
            Require(crowd.CurrentTick == beforeTick, "A forest API advanced simulation time.");
            Require(crowd.Agents.All(agent => !before.TryGetValue(agent.Id, out var position) || Same(position, agent.Position)), "A forest API moved a body.");
        }
        void Record(ForestRequest request, string kind, Position2 position)
        {
            result.Commands.Add(new ForestCommand(crowd.CurrentTick, kind, request, position));
        }
        bool NonCenter(Position2 position) => !Same(position, grid.Center(grid.CellAt(position)));

        void Validate()
        {
            var units = crowd.Agents.Select(agent => new UnitObservation(
                agent.Id,
                new Cell((agent.Position.X - grid.Origin.X) / 500, (agent.Position.Y - grid.Origin.Y) / 500),
                NavigationState.Moving,
                agent.GoalCell,
                1,
                (agent.Position.X - grid.Origin.X) * 2,
                (agent.Position.Y - grid.Origin.Y) * 2,
                agent.Radius * 2)).ToArray();
            GeometryChecks.VerifyUnitClearance(units, prior);
            if (crowd.RecoveryActive)
            {
                foreach (var claim in gateClaims)
                {
                    var body = crowd.Agents.FirstOrDefault(agent => agent.Id == claim.Key);
                    Require(body == null || body.GoalCell == claim.Value, "A live yielded Big lost its final goal during the gate transaction.");
                }
            }
            foreach (var unit in units)
            {
                var previous = prior.TryGetValue(unit.Id, out var old) ? old : unit;
                GeometryChecks.VerifyObstacleClearance(geometry, previous, unit);
                var actor = crowd.Agents.Single(agent => agent.Id == unit.Id);
                var request = requests[unit.Id];
                Require(actor.Radius == request.Radius && actor.AttackRange == request.Range && actor.Speed == request.Speed, "A forest body changed profile.");
                long dx = (long)unit.X - previous.X;
                long dy = (long)unit.Y - previous.Y;
                Require(dx * dx + dy * dy <= (long)(request.Speed * 2) * (request.Speed * 2), "A forest body exceeded its speed.");
                var from = new Position2(previous.X / 2 + grid.Origin.X, previous.Y / 2 + grid.Origin.Y);
                long required = grid.TargetRadius + actor.Radius;
                Require(PointSegment(grid.TargetCenter, from, actor.Position) >= required * required - .001, "Actual off-centre base penetration.");
                if (actor.Status != NavigationAgentStatus.Navigating)
                {
                    Require(
                        Same(actor.Position, grid.Center(actor.GoalCell)) && grid.HoldingAllowed(actor.GoalCell),
                        "Temporary parking was counted as final arrival.");
                }
                if (actor.Status == NavigationAgentStatus.Attacking)
                {
                    long tx = (long)actor.Position.X - grid.TargetCenter.X;
                    long ty = (long)actor.Position.Y - grid.TargetCenter.Y;
                    long reach = grid.TargetRadius + actor.Radius + actor.AttackRange;
                    Require(tx * tx + ty * ty <= reach * reach, "A forest actor attacked outside its own range.");
                }
                prior[unit.Id] = unit;
            }
        }
    }

    private static (string Phase, int Ordinal)[] Cases(bool stress)
    {
        var cases = new List<(string Phase, int Ordinal)>();
        foreach (string phase in new[] { "outbound-big", "parked-big", "returning-big", "moving-small", "arrived-small", "all-gates" })
        {
            int[] ordinals = !stress ? new[] { 0 } : phase == "outbound-big" ? new[] { 0, 1, 2, 3 }
                : phase == "parked-big" || phase == "returning-big" ? new[] { 0, 1, 3 } : new[] { 0 };
            cases.AddRange(ordinals.Select(ordinal => (phase, ordinal)));
        }
        if (stress)
        {
            cases.Add(("center-big", 2));
            cases.Add(("two-gates", 0));
        }
        return cases.ToArray();
    }

    private static long Squared(Position2 first, Position2 last)
    {
        long dx = (long)first.X - last.X;
        long dy = (long)first.Y - last.Y;
        return dx * dx + dy * dy;
    }

    private static double PointSegment(Position2 point, Position2 first, Position2 last)
    {
        double dx = (double)last.X - first.X;
        double dy = (double)last.Y - first.Y;
        double length = dx * dx + dy * dy;
        double t = length == 0 ? 0 : Math.Clamp(((point.X - first.X) * dx + (point.Y - first.Y) * dy) / length, 0, 1);
        double x = first.X + dx * t - point.X;
        double y = first.Y + dy * t - point.Y;
        return x * x + y * y;
    }
    private static object? CurrentJob(NavigationCrowd crowd)
    {
        return typeof(NavigationCrowd).GetField("vacancyJob", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(crowd);
    }

    private static HashSet<int> GateParticipants(NavigationCrowd crowd)
    {
        var job = CurrentJob(crowd);
        return job?.GetType().GetField("GateActors", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(job) as HashSet<int> ?? new HashSet<int>();
    }
    private static Dictionary<int, Position2> Positions(NavigationCrowd crowd) => crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
    private static bool Same(Position2 first, Position2 last) => first.X == last.X && first.Y == last.Y;
    private static string Metadata(string key)
    {
        return Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(value => value.Key == key).Value!;
    }
    private static string DigestFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static Dictionary<string, string> Hashes(string path, string pattern, bool recurse)
    {
        var option = recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        return Directory.GetFiles(path, pattern, option).OrderBy(file => file).ToDictionary(file => file, DigestFile);
    }
    private static string? Argument(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
    private static bool SameHashes(Dictionary<string, string> first, Dictionary<string, string> second) => first.Count == second.Count
        && first.All(pair => second.TryGetValue(pair.Key, out string? value) && value == pair.Value);
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
