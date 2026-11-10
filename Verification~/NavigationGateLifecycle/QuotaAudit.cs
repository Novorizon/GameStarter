using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Game.HotUpdate.TowerDefense.Domain;
using Game.HotUpdate.TowerDefense.Domain.Navigation;

internal sealed record QuotaRequest(int Id, int Range);
internal sealed record QuotaCommand(long Tick, string Kind, QuotaRequest Request, Position2 Position);
internal sealed record QuotaBefore(int Goal, NavigationAgentStatus Status);
internal sealed record QuotaInjection(long Tick, string Role, int Id, Position2 Position, int[] Participants, bool NonCenter, bool JobActive);

internal sealed class QuotaResult
{
    public string Role { get; init; } = "";
    public bool Success { get; set; }
    public string Error { get; set; } = "";
    public QuotaInjection? Injection { get; set; }
    public int Requested { get; set; }
    public int Admitted { get; set; }
    public int Active { get; set; }
    public int Pending { get; set; }
    public int ExplicitDeaths { get; set; }
    public int ShortAttackers { get; set; }
    public int LongAttackers { get; set; }
    public long FinalTick { get; set; }
    public long ElapsedMilliseconds { get; set; }
    public int QuotaExchanges { get; set; }
    public int SelectedQuotaRequester { get; set; }
    public int SelectedPermanentYielder { get; set; }
    public int SelectedTemporaryGate { get; set; }
    public bool QueueConserved { get; set; }
    public bool ContinuousOracleVerified { get; set; }
    public string InputCommandsSha256 { get; set; } = "";
    public int TemporaryGateRange { get; set; }
    public string LastFailure { get; set; } = "";
    public bool RecoveryActive { get; set; }
    public ForestBody[] FinalBodies { get; set; } = Array.Empty<ForestBody>();
    public QuotaRequest[] QueuedRequests { get; set; } = Array.Empty<QuotaRequest>();
    public List<QuotaCommand> Commands { get; } = new List<QuotaCommand>();
    public List<object> QuotaJobObservations { get; } = new List<object>();
}

internal static class QuotaGateLifecycle
{
    private const int Speed = 500;
    private static readonly JsonSerializerOptions Json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static int Run(string[] args)
    {
        string project = Metadata("AuditProjectRoot");
        string domain = Metadata("NavigationDomainRoot");
        string output = Argument(args, "--output") ?? "Artifacts/NavigationQuotaLifecycle";
        string? filter = Argument(args, "--role");
        string[] roles = filter == null ? new[] { "requester", "permanent-yielder", "temporary-gate" } : new[] { filter };
        var sourceHashes = Hashes(domain, true);
        var testHashes = Hashes(project, false);
        foreach (string file in new[]
        {
            "../TowerDefenseNavigationChallenges/Challenges.cs", "../TowerDefenseNavigationChallenges/GeometryTypes.cs",
            "../TowerDefenseNavigation/GeometryChecks.cs", "../TowerDefenseNavigation/Observations.cs",
            "../../Assets/HotUpdate/TowerDefense/Data/NavigationLayout.cs"
        })
        {
            string path = Path.GetFullPath(Path.Combine(project, file));
            testHashes[path] = DigestFile(path);
        }
        GeometryChecks.VerifyOracle();
        VerifySpawnExclusionAndCache();
        Directory.CreateDirectory(output);
        var results = new List<QuotaResult>();
        foreach (string role in roles)
        {
            if (role != "requester" && role != "permanent-yielder" && role != "temporary-gate")
            {
                throw new ArgumentException("Unknown quota role: " + role);
            }
            var result = Execute(role);
            results.Add(result);
            File.WriteAllText(Path.Combine(output, role + ".json"), JsonSerializer.Serialize(result, Json));
            Console.WriteLine($"{(result.Success ? "PASS" : "FAIL")} quota {role} active={result.Active} pending={result.Pending}"
                + $" shortAttack={result.ShortAttackers} longAttack={result.LongAttackers} injected={result.Injection != null} {result.Error}");
        }
        bool sourceUnchanged = SameHashes(sourceHashes, Hashes(domain, true));
        bool testsUnchanged = testHashes.All(pair => File.Exists(pair.Key) && DigestFile(pair.Key) == pair.Value);
        var summary = new
        {
            utc = DateTime.UtcNow.ToString("O"), domain, sourceHashes, testHashes,
            testBinarySha256 = DigestFile(Assembly.GetExecutingAssembly().Location),
            sourceFilesUnchanged = sourceUnchanged,
            testSourceFilesUnchanged = testsUnchanged,
            speedMillimetresPerTick = Speed,
            nonSpawnFlexibilityAndEntryCacheVerified = true,
            count = results.Count,
            passed = results.Count(result => result.Success),
            success = results.Count > 0 && results.All(result => result.Success) && sourceUnchanged && testsUnchanged,
            results = results.Select(result => new
            {
                result.Role, result.Success, result.Error, result.Injection, result.Requested, result.Admitted,
                result.Active, result.Pending, result.ExplicitDeaths, result.ShortAttackers, result.LongAttackers,
                result.FinalTick, result.ElapsedMilliseconds, result.QuotaExchanges, result.SelectedQuotaRequester,
                result.SelectedPermanentYielder, result.SelectedTemporaryGate, result.TemporaryGateRange, result.QueueConserved,
                result.ContinuousOracleVerified, result.InputCommandsSha256
            })
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Json));
        return summary.success ? 0 : 1;
    }

    private static QuotaResult Execute(string role)
    {
        var result = new QuotaResult { Role = role };
        var timer = Stopwatch.StartNew();
        var geometry = new NavigationScenario
        {
            Width = 35,
            Height = 35,
            Target = new Cell(17, 17),
            Walkable = Enumerable.Repeat(true, 35 * 35).ToArray(),
            SpawnCells = new[] { new Cell(1, 17) },
            GatheringDistance = 10000
        };
        var grid = new NavigationGrid(35, 35, 1000, new Position2(0, 0), geometry.Walkable, new Position2(17500, 17500), 1000, null, new[] { 17 * 35 + 1 });
        var crowd = new NavigationCrowd(grid, planningHorizon: 10000, gatheringDistance: 10000);
        crowd.RegisterProfile(250, 1000);
        crowd.RegisterProfile(250, 4000);
        var requests = new Dictionary<int, QuotaRequest>();
        var pending = new List<QuotaRequest>();
        var admitted = new HashSet<int>();
        var deaths = new HashSet<int>();
        var previous = new Dictionary<int, UnitObservation>();
        int next = 1;
        int phase = 0;
        int stable = 0;
        int exchangesBeforeRegrow = 0;
        object? observedJob = null;
        object? selectedJob = null;
        var jobClaims = new Dictionary<int, int>();
        int selectedVictim = 0;
        Add(16, 1000);
        try
        {
            Require(crowd.CountAttackSlots(250, 1000) == 16 && crowd.CountAttackSlots(250, 4000) == 84, "The quota geometry changed.");
            for (int tick = 0; tick <= 30000; tick++)
            {
                Validate();
                if (phase == 3 && result.Injection == null && selectedVictim != 0 && ReferenceEquals(Job(crowd), selectedJob))
                {
                    var victim = crowd.Agents.FirstOrDefault(agent => agent.Id == selectedVictim);
                    if (victim != null && NonCenter(victim.Position))
                    {
                        result.Injection = new QuotaInjection(
                            tick,
                            role,
                            victim.Id,
                            victim.Position,
                            Participants(crowd).OrderBy(id => id).ToArray(),
                            true,
                            crowd.RecoveryActive);
                        Remove(victim.Id, true);
                    }
                }
                foreach (var request in pending.ToArray())
                {
                    var positions = Positions(crowd);
                    var before = Before(crowd);
                    long priorTick = crowd.CurrentTick;
                    var source = new Position2(1500, 17500);
                    bool born = crowd.TrySpawn(request.Id, source, Speed, 250, request.Range);
                    VerifyApi(positions, priorTick);
                    TrackJob(before);
                    Record(request, born ? "spawn" : "spawn-rejected", source);
                    if (!born)
                    {
                        Require(!crowd.Agents.Any(agent => agent.Id == request.Id), "A rejected quota birth created a hidden actor.");
                        break;
                    }
                    Require(admitted.Add(request.Id), "A quota request was admitted twice.");
                    pending.Remove(request);
                    Require(Same(crowd.Agents.Single(agent => agent.Id == request.Id).Position, source), "A quota birth skipped its source.");
                }
                Validate();
                Require(requests.Count == crowd.Agents.Count + pending.Count + deaths.Count, "Quota request conservation failed.");
                Require(crowd.Agents.Select(agent => agent.Id).ToHashSet().SetEquals(admitted.Except(deaths)), "Quota active IDs changed silently.");
                bool settled = crowd.Agents.All(agent => agent.Status != NavigationAgentStatus.Navigating);
                if (pending.Count == 0 && settled)
                {
                    if (phase == 0)
                    {
                        Require(Attackers(1000) == 16, "The initial 16 Short attackers were not established.");
                        Add(84, 4000);
                        phase = 1;
                    }
                    else if (phase == 1)
                    {
                        Require(Attackers(1000) == 16 && Attackers(4000) == 68, "The initial full mixed formation was not established.");
                        var victims = crowd.Agents.Where(agent => agent.AttackRange == 1000).OrderBy(agent => agent.Id).Take(15)
                            .Select(agent => agent.Id).ToArray();
                        foreach (int id in victims)
                        {
                            Remove(id, false);
                        }
                        phase = 2;
                    }
                    else if (phase == 2 && Attackers(1000) == 1 && Attackers(4000) == 83)
                    {
                        exchangesBeforeRegrow = Metric(crowd, "AttackQuotaExchanges");
                        Add(15, 1000);
                        phase = 3;
                    }
                }
                bool ready = phase == 3 && result.Injection != null && pending.Count == 0 && settled
                    && crowd.Agents.Count == 100 && Attackers(1000) == 16 && Attackers(4000) == 68;
                stable = ready ? stable + 1 : 0;
                if (stable >= 200)
                {
                    Require(requests.Count == 116 && deaths.Count == 16 && admitted.Count == 116, "The intended quota lifecycle did not complete.");
                    result.Success = true;
                    result.QueueConserved = true;
                    result.ContinuousOracleVerified = true;
                    break;
                }
                var stepBefore = Before(crowd);
                crowd.Step(crowd.CurrentTick + 1);
                TrackJob(stepBefore);
            }
            Require(result.Success, "Deadline: the required quota exchange injection or 16 Short + 68 Long attack recovery did not complete.");
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
        result.ShortAttackers = Attackers(1000);
        result.LongAttackers = Attackers(4000);
        result.FinalTick = crowd.CurrentTick;
        result.ElapsedMilliseconds = timer.ElapsedMilliseconds;
        result.QuotaExchanges = Metric(crowd, "AttackQuotaExchanges");
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

        int Attackers(int range) => crowd.Agents.Count(agent => agent.AttackRange == range && agent.Status == NavigationAgentStatus.Attacking);
        bool NonCenter(Position2 position) => !Same(position, grid.Center(grid.CellAt(position)));
        void Add(int count, int range)
        {
            for (int index = 0; index < count; index++)
            {
                var request = new QuotaRequest(next++, range);
                requests.Add(request.Id, request);
                pending.Add(request);
            }
        }
        void Record(QuotaRequest request, string kind, Position2 position) => result.Commands.Add(new QuotaCommand(crowd.CurrentTick, kind, request, position));
        void Remove(int id, bool replace)
        {
            var actor = crowd.Agents.Single(agent => agent.Id == id);
            var positions = Positions(crowd);
            long priorTick = crowd.CurrentTick;
            Require(crowd.Remove(id) && deaths.Add(id), "An explicit quota death failed.");
            VerifyApi(positions, priorTick);
            Record(requests[id], "remove", actor.Position);
            if (replace)
            {
                Add(1, requests[id].Range);
            }
        }
        void VerifyApi(Dictionary<int, Position2> positions, long priorTick)
        {
            Require(crowd.CurrentTick == priorTick, "A quota API advanced time.");
            Require(crowd.Agents.All(agent => !positions.TryGetValue(agent.Id, out var point) || Same(point, agent.Position)), "A quota API moved a body.");
        }
        void TrackJob(Dictionary<int, QuotaBefore> before)
        {
            var job = Job(crowd);
            if (ReferenceEquals(observedJob, job))
            {
                return;
            }
            observedJob = job;
            var ids = Participants(crowd);
            jobClaims = crowd.Agents.Where(agent => ids.Contains(agent.Id)).ToDictionary(agent => agent.Id, agent => agent.GoalCell);
            if (phase != 3 || result.Injection != null || Metric(crowd, "AttackQuotaExchanges") < exchangesBeforeRegrow + 15)
            {
                return;
            }
            var cohort = crowd.Agents.Where(agent => ids.Contains(agent.Id) && before.ContainsKey(agent.Id)).ToArray();
            var requester = cohort.FirstOrDefault(agent => agent.AttackRange == 1000 && before[agent.Id].Goal != agent.GoalCell
                && grid.HasAttackLine(agent.GoalCell, 250, 1000));
            var permanent = cohort.FirstOrDefault(agent => agent.AttackRange == 4000 && before[agent.Id].Status == NavigationAgentStatus.Attacking
                && !grid.HasAttackLine(agent.GoalCell, 250, 4000) && grid.HoldingAllowed(agent.GoalCell));
            var temporary = cohort.Where(agent => before[agent.Id].Goal == agent.GoalCell
                && (requester == null || requester.Id != agent.Id) && (permanent == null || permanent.Id != agent.Id)
                && agent.Status == NavigationAgentStatus.Navigating).OrderBy(agent => agent.AttackRange).ThenBy(agent => agent.Id).FirstOrDefault();
            result.QuotaJobObservations.Add(new
            {
                tick = crowd.CurrentTick,
                exchanges = Metric(crowd, "AttackQuotaExchanges"),
                requester = requester?.Id,
                permanent = permanent?.Id,
                temporary = temporary?.Id,
                cohort = cohort.Select(agent => new
                {
                    agent.Id,
                    agent.AttackRange,
                    beforeGoal = before[agent.Id].Goal,
                    beforeStatus = before[agent.Id].Status.ToString(),
                    goal = agent.GoalCell,
                    status = agent.Status.ToString()
                }).ToArray()
            });
            if (requester == null || permanent == null || temporary == null)
            {
                return;
            }
            result.SelectedQuotaRequester = requester.Id;
            result.SelectedPermanentYielder = permanent.Id;
            result.SelectedTemporaryGate = temporary.Id;
            result.TemporaryGateRange = temporary.AttackRange;
            selectedJob = job;
            selectedVictim = role == "requester" ? requester.Id : role == "permanent-yielder" ? permanent.Id : temporary.Id;
        }
        void Validate()
        {
            var units = crowd.Agents.Select(agent => new UnitObservation(
                agent.Id,
                new Cell(agent.Position.X / 1000, agent.Position.Y / 1000),
                NavigationState.Moving,
                agent.GoalCell,
                1,
                agent.Position.X,
                agent.Position.Y,
                agent.Radius)).ToArray();
            GeometryChecks.VerifyUnitClearance(units, previous);
            foreach (var unit in units)
            {
                var before = previous.TryGetValue(unit.Id, out var old) ? old : unit;
                GeometryChecks.VerifyObstacleClearance(geometry, before, unit);
                GeometryChecks.VerifyTargetClearance(geometry, before, unit);
                var actor = crowd.Agents.Single(agent => agent.Id == unit.Id);
                long dx = (long)unit.X - before.X;
                long dy = (long)unit.Y - before.Y;
                Require(dx * dx + dy * dy <= Speed * Speed, "A quota actor exceeded its requested speed.");
                Require(actor.Radius == 250 && actor.Speed == Speed && actor.AttackRange == requests[unit.Id].Range, "A quota profile changed.");
                if (actor.Status != NavigationAgentStatus.Navigating)
                {
                    Require(
                        Same(actor.Position, grid.Center(actor.GoalCell)) && grid.HoldingAllowed(actor.GoalCell),
                        "Temporary quota parking was counted as final.");
                    long gx = unit.X - 17500L;
                    long gy = unit.Y - 17500L;
                    Require(gx * gx + gy * gy <= 11000L * 11000, "A remote quota wait was counted as gathering.");
                }
                if (actor.Status == NavigationAgentStatus.Attacking)
                {
                    long tx = unit.X - 17500L;
                    long ty = unit.Y - 17500L;
                    long reach = 1250L + actor.AttackRange;
                    Require(tx * tx + ty * ty <= reach * reach, "A quota actor attacked out of range.");
                    GeometryChecks.VerifyAttackLine(geometry, unit);
                }
                if (crowd.RecoveryActive && jobClaims.TryGetValue(actor.Id, out int claim))
                {
                    Require(actor.GoalCell == claim, "A quota actor changed its committed goal within the same active job.");
                }
                previous[unit.Id] = unit;
            }
        }
    }

    internal static void VerifySpawnExclusionAndCache()
    {
        const int width = 7;
        int source = 4 * width + 5;
        var holding = Enumerable.Range(0, width * width).Select(cell =>
        {
            int x = cell % width - 3;
            int y = cell / width - 3;
            return x * x + y * y <= 4 || cell == source;
        }).ToArray();
        var grid = new NavigationGrid(
            7,
            7,
            1000,
            new Position2(0, 0),
            Enumerable.Repeat(true, 49).ToArray(),
            new Position2(3500, 3500),
            1000,
            holding);
        var crowd = new NavigationCrowd(grid);
        crowd.RegisterProfile(250, 750);
        crowd.RegisterProfile(250, 1000);
        var type = typeof(NavigationCrowd);
        var query = type.GetMethod("GetProfile", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var shortProfile = query.Invoke(crowd, new object[] { 250, 750 });
        var longProfile = query.Invoke(crowd, new object[] { 250, 1000 });
        var compare = type.GetMethod("StrictlyMoreFlexible", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Require((bool)compare.Invoke(crowd, new[] { longProfile, shortProfile })!, "The initial strict-superset cache was not exercised.");
        var position = grid.Center(source);
        Require(crowd.TrySpawn(1, position, Speed, 250, 1000), "The new valid entrance was not admitted.");
        Require(crowd.CurrentTick == 0 && Same(crowd.Agents.Single().Position, position), "The classification fixture moved time or its newborn.");
        Require(crowd.CountAttackSlots(250, 750) == 8 && crowd.CountAttackSlots(250, 1000) == 8, "The equal actual attack sets were not established.");
        Require(!(bool)compare.Invoke(crowd, new[] { longProfile, shortProfile })!, "Spawn exclusion or flexibility cache invalidation failed.");
    }

    private static object? Job(NavigationCrowd crowd)
    {
        return typeof(NavigationCrowd).GetField("vacancyJob", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(crowd);
    }
    private static HashSet<int> Participants(NavigationCrowd crowd)
    {
        var job = Job(crowd);
        return job?.GetType().GetField("GateActors", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(job) as HashSet<int> ?? new HashSet<int>();
    }
    private static int Metric(NavigationCrowd crowd, string name) => Convert.ToInt32(typeof(NavigationCrowd).GetProperty(name)?.GetValue(crowd) ?? 0);
    private static Dictionary<int, Position2> Positions(NavigationCrowd crowd) => crowd.Agents.ToDictionary(agent => agent.Id, agent => agent.Position);
    private static Dictionary<int, QuotaBefore> Before(NavigationCrowd crowd)
    {
        return crowd.Agents.ToDictionary(agent => agent.Id, agent => new QuotaBefore(agent.GoalCell, agent.Status));
    }
    private static bool Same(Position2 first, Position2 last) => first.X == last.X && first.Y == last.Y;
    private static string Metadata(string key)
    {
        return Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().Single(value => value.Key == key).Value!;
    }
    private static string DigestFile(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    private static Dictionary<string, string> Hashes(string path, bool recurse)
    {
        return Directory.GetFiles(path, "*.cs", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
            .OrderBy(file => file).ToDictionary(file => file, DigestFile);
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
