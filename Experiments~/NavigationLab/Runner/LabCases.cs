using System.Security.Cryptography;
using System.Text.Json;
using NavigationLab.Core;
using NavigationLab.Oracle;

internal sealed record ScheduledRequest(int ReadyTick, SpawnRequest Request);
internal sealed record TerrainEvent(int Tick, WorldChange Change, bool AfterSettlement = false);
internal sealed record DeathEvent(
    int Tick,
    int Count,
    bool AfterSettlement,
    bool Replace,
    bool RequireMoving = false,
    int Range = -1,
    bool AttackersOnly = false);
internal enum CaseExpectation
{
    FullArrival,
    EndpointPolicyOverflow,
    StaticUnreachable
}
internal sealed class LabCase
{
    public string Name { get; init; } = "";
    public string Family { get; init; } = "";
    public WorldDefinition World { get; init; } = null!;
    public List<ScheduledRequest> Requests { get; init; } = new();
    public List<TerrainEvent> TerrainEvents { get; init; } = new();
    public List<DeathEvent> DeathEvents { get; init; } = new();
    public NavigationFeatures Features { get; init; }
    public CaseExpectation Expectation { get; init; }
    public int ExpectedLive { get; init; }
    public int? ExpectedAttackers { get; init; }
    public Dictionary<int, int> MinimumAttackersByRange { get; init; } = new();
    public int DeadlineTicks { get; init; } = 12000;
    public int HoldTicks { get; init; } = 200;
    public int? RequireInitialSettledAtTick { get; init; }
    public int MinimumOldWaitingPromotions { get; init; }
    public int PhysicalAreaUpperBound { get; init; }
    public string Feasibility { get; init; } = "Original fixed input previously constructively witnessed; no route/goal assignment passed to candidate.";
}

internal static class LabCases
{
    public static List<LabCase> Build(string repository, string suite)
    {
        var cases = new List<LabCase>();
        var classic = ScenarioCatalog.Create(1).ToArray();
        foreach (var item in classic.Where(item => !item.ExpectCapacityRejection && !item.ExpectUnreachable && !item.HasDeathReplacement
            && item.UnitCount <= (suite == "stress" ? 300 : 72)))
        {
            var world = ClassicWorld(item);
            var requests = Enumerable.Range(1, item.UnitCount).Select(id => new ScheduledRequest(
                0,
                new SpawnRequest(id, new UnitProfile(item.UnitRadius, 1000 / item.MoveTicks[(id - 1) % item.MoveTicks.Length], 1000),
                    world.SpawnCells[(id - 1) % world.SpawnCells.Count]))).ToList();
            cases.Add(new LabCase
            {
                Name = item.Name,
                Family = "fixed-original",
                World = world,
                Requests = requests,
                ExpectedLive = requests.Count,
                ExpectedAttackers = item.Name.Contains("parking-lanes", StringComparison.Ordinal) ? 6
                    : item.Name == "cross-junction-original-4m" || item.UnitRadius == 490 ? 12 : 16,
                DeadlineTicks = Math.Min(item.Deadline, 12000)
            });
        }
        AddForest(repository, cases);
        var unreachable = classic.Single(item => item.ExpectUnreachable);
        var unreachableWorld = ClassicWorld(unreachable);
        cases.Add(new LabCase
        {
            Name = "static-unreachable-retains-pending",
            Family = "negative-geometry",
            World = unreachableWorld,
            Requests = Orders(unreachableWorld, 12, new UnitProfile(250, 1000, 1000)),
            Expectation = CaseExpectation.StaticUnreachable,
            ExpectedLive = 0,
            ExpectedAttackers = 0,
            DeadlineTicks = 500,
            Feasibility = "Closed wall x=15 disconnects every spawn from every attack candidate; independently checked flood."
        });
        var tiny = OpenWorld(9, 9, 1000, new[] { 4 + 4 * 9 }, new[] { 1 + 4 * 9 }, 4000);
        cases.Add(new LabCase
        {
            Name = "physical-and-endpoint-overflow-200",
            Family = "capacity",
            World = tiny,
            Requests = Orders(tiny, 200, new UnitProfile(490, 1000, 1000)),
            Expectation = CaseExpectation.EndpointPolicyOverflow,
            ExpectedLive = 39,
            ExpectedAttackers = 12,
            PhysicalAreaUpperBound = 60,
            DeadlineTicks = 6000,
            Feasibility = "Same 9x9/r490 fixture as PhysicalCapacityProof: physical area upper60, discrete endpoint upper39; constructive39 witness already archived."
        });
        cases.Add(new LabCase
        {
            Name = "capacity-death-refills-original-queue",
            Family = "capacity-lifecycle",
            World = tiny,
            Requests = Orders(tiny, 200, new UnitProfile(490, 1000, 1000)),
            Expectation = CaseExpectation.EndpointPolicyOverflow,
            ExpectedLive = 39,
            ExpectedAttackers = 12,
            PhysicalAreaUpperBound = 60,
            DeathEvents = new() { new DeathEvent(0, 13, true, false) },
            Features = NavigationFeatures.DeathAndReinforcement,
            DeadlineTicks = 8000,
            Feasibility = "Known39 endpoint witness plus13 explicit removals, original pending IDs must refill all39."
        });
        var lifecycleWorld = OpenWorld(35, 29, 1000, new[] { 27 + 14 * 35 }, new[] { 1 + 14 * 35 }, 7000);
        cases.Add(new LabCase
        {
            Name = "moving-noncenter-death-and-reinforcement",
            Family = "lifecycle",
            World = lifecycleWorld,
            Requests = Orders(lifecycleWorld, 24, new UnitProfile(250, 333, 1000)),
            ExpectedLive = 24,
            ExpectedAttackers = 16,
            Features = NavigationFeatures.DeathAndReinforcement,
            DeathEvents = new() { new DeathEvent(10, 1, false, true, true) }
        });
        cases.Add(new LabCase
        {
            Name = "settled-batch-death-and-reinforcement",
            Family = "lifecycle",
            World = lifecycleWorld,
            Requests = Orders(lifecycleWorld, 48, new UnitProfile(250, 1000, 1000)),
            ExpectedLive = 48,
            ExpectedAttackers = 16,
            Features = NavigationFeatures.DeathAndReinforcement,
            DeathEvents = new() { new DeathEvent(0, 6, true, true) }
        });
        cases.Add(new LabCase
        {
            Name = "old-waiters-refill-without-newcomers",
            Family = "lifecycle-fairness",
            World = lifecycleWorld,
            Requests = Orders(lifecycleWorld, 48, new UnitProfile(250, 1000, 1000)),
            ExpectedLive = 42,
            ExpectedAttackers = 16,
            Features = NavigationFeatures.DeathAndReinforcement,
            DeathEvents = new() { new DeathEvent(0, 6, true, false, AttackersOnly: true) },
            MinimumOldWaitingPromotions = 6,
            Feasibility = "Remove6 actual attackers only after48 bodies/16attackers settle; no newborn requests exist, so6 old waiting IDs must physically fill the released attack places."
        });
        AddFairness(cases);
        AddDynamic(cases);
        AddDirectedFlows(cases);
        foreach (int seed in suite == "stress" ? new[] { 11, 97, 713, 20261111, 1701, 991 } : new[] { 11, 97 })
        {
            cases.Add(RandomMaze(seed));
        }
        if (suite == "stress")
        {
            cases.Add(LargeTiled(128));
            cases.Add(LargeTiled(256));
        }
        if (suite == "smoke")
        {
            var names = new HashSet<string> { "open-arena", "cross-junction-original-4m", "forest-wave-1", "moving-noncenter-death-and-reinforcement", "dynamic-route-block-and-open", "large-body-wide-approach", "fast-per-tick-approach" };
            cases = cases.Where(item => names.Contains(item.Name)).ToList();
        }
        return cases;
    }

    private static void AddForest(string repository, List<LabCase> cases)
    {
        var lines = File.ReadAllLines(Path.Combine(repository, "Assets/GameAssets/TowerDefense/Data/ForestNavigation.bytes"))
            .Select(line => line.Trim()).Where(line => line.Length > 0 && !line.StartsWith(';')).ToArray();
        int[] Parse(int index) => lines[index].Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(int.Parse).ToArray();
        var size = Parse(1);
        int cell = Parse(2)[0];
        var origin = Parse(3);
        var target = Parse(4);
        var walk = new bool[size[0] * size[1]];
        var holding = new bool[walk.Length];
        var entries = new List<int>();
        for (int row = 0; row < size[1]; row++)
        {
            for (int x = 0; x < size[0]; x++)
            {
                char symbol = lines[row + 6][x];
                int index = (size[1] - 1 - row) * size[0] + x;
                walk[index] = ".+aSB".Contains(symbol);
                holding[index] = symbol == 'a';
                if (symbol == 'S')
                {
                    entries.Add(index);
                }
            }
        }
        var world = new WorldDefinition(size[0], size[1], cell, new Point2(origin[0], origin[1]), walk, holding, entries.ToArray(),
            new[] { new TargetDefinition(1, new Point2(target[0], target[1]), target[2]) }, 4000);
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(repository, "Experiments~/NavigationLab/Inputs/authored-forest.json")));
        foreach (var source in document.RootElement.GetProperty("sourceHashes").EnumerateObject())
        {
            if (Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(repository, source.Name)))).ToLowerInvariant() != source.Value.GetString())
            {
                throw new InvalidOperationException("Authored table export is stale: " + source.Name);
            }
        }
        var profiles = document.RootElement.GetProperty("enemies").EnumerateArray().ToDictionary(
            item => item.GetProperty("id").GetInt32(), item => new UnitProfile(item.GetProperty("radius").GetInt32(),
                item.GetProperty("speedPerTick").GetInt32(), item.GetProperty("attackRange").GetInt32()));
        foreach (var single in new[] { ("small", 2), ("medium", 1), ("large", 4) })
        {
            cases.Add(new LabCase
            {
                Name = "forest-single-" + single.Item1,
                Family = "forest-single-body",
                World = world,
                Requests = Orders(world, 1, profiles[single.Item2]),
                ExpectedLive = 1,
                ExpectedAttackers = 1,
                DeadlineTicks = 7000
            });
        }
        int[] attackers = { 8, 13, 14, 15, 15, 15 };
        for (int wave = 1; wave <= 6; wave++)
        {
            var orders = new List<ScheduledRequest>();
            foreach (var group in document.RootElement.GetProperty("spawns").EnumerateArray().Where(item => item.GetProperty("waveId").GetInt32() == wave))
            {
                for (int index = 0; index < group.GetProperty("count").GetInt32(); index++)
                {
                    int ready = group.GetProperty("startTick").GetInt32() + index * group.GetProperty("intervalTicks").GetInt32();
                    orders.Add(new ScheduledRequest(ready, new SpawnRequest(orders.Count + 1, profiles[group.GetProperty("enemyId").GetInt32()], entries[0])));
                }
            }
            cases.Add(new LabCase { Name = "forest-wave-" + wave, Family = "authored-forest", World = world, Requests = orders,
                ExpectedLive = orders.Count, ExpectedAttackers = attackers[wave - 1], DeadlineTicks = 7000 });
        }
        foreach (var spec in new[] { ("forest-full44-large", 44, 4, 18), ("forest-full45-medium", 45, 1, 12) })
        {
            cases.Add(new LabCase { Name = spec.Item1, Family = "forest-saturation", World = world,
                Requests = Orders(world, spec.Item2, profiles[spec.Item3]), ExpectedLive = spec.Item2, ExpectedAttackers = spec.Item4 });
        }
        var late = Orders(world, 44, profiles[4]);
        late.AddRange(Orders(world, 2, profiles[2], 4000, 45));
        cases.Add(new LabCase { Name = "forest-full44-two-late-small", Family = "forest-saturation", World = world, Requests = late,
            ExpectedLive = 46, ExpectedAttackers = 20, RequireInitialSettledAtTick = 3999, DeadlineTicks = 12000 });
    }

    private static void AddFairness(List<LabCase> cases)
    {
        var world = OpenWorld(35, 35, 1000, new[] { 17 + 17 * 35 }, new[] { 1 + 17 * 35 }, 10000, false);
        var shortProfile = new UnitProfile(250, 1000, 1000);
        var longProfile = new UnitProfile(250, 1000, 4000);
        var lateShort = Orders(world, 84, longProfile);
        lateShort.AddRange(Orders(world, 1, shortProfile, 2000, 85));
        cases.Add(new LabCase { Name = "ranged84-before-short1-fairness", Family = "profile-fairness", World = world, Requests = lateShort,
            ExpectedLive = 85, ExpectedAttackers = 84, MinimumAttackersByRange = new() { [1000] = 1 }, RequireInitialSettledAtTick = 1999,
            DeathEvents = new() { new DeathEvent(0, 1, true, true, false, 1000) }, Features = NavigationFeatures.DeathAndReinforcement });
        var shortFirst = Orders(world, 16, shortProfile);
        shortFirst.AddRange(Orders(world, 84, longProfile, 2000, 17));
        cases.Add(new LabCase { Name = "short16-before-ranged84-fairness", Family = "profile-fairness", World = world, Requests = shortFirst,
            ExpectedLive = 100, ExpectedAttackers = 84, MinimumAttackersByRange = new() { [1000] = 16 }, RequireInitialSettledAtTick = 1999,
            DeathEvents = new() { new DeathEvent(0, 1, true, true, false, 1000) }, Features = NavigationFeatures.DeathAndReinforcement });
    }

    private static void AddDynamic(List<LabCase> cases)
    {
        int width = 35;
        var world = OpenWorld(width, 29, 1000, new[] { 27 + 14 * width }, new[] { 1 + 14 * width }, 7000);
        int[] wall = Enumerable.Range(1, 27).Where(y => y != 5 && y != 23).Select(y => 15 + y * width).ToArray();
        cases.Add(new LabCase { Name = "dynamic-route-block-and-open", Family = "dynamic-terrain", World = world,
            Requests = Orders(world, 24, new UnitProfile(250, 333, 1000)), ExpectedLive = 24, ExpectedAttackers = 16, Features = NavigationFeatures.DynamicTerrain,
            TerrainEvents = new() { new TerrainEvent(3, new WorldChange { BlockCells = wall }), new TerrainEvent(600, new WorldChange { OpenCells = wall }) },
            Feasibility = "Door wall is inserted ahead of all speed-bounded bodies; two permanent alternate openings and later full reopening remain feasible." });
        cases.Add(new LabCase { Name = "target-relocation-before-arrival", Family = "target-relocation", World = world,
            Requests = Orders(world, 24, new UnitProfile(250, 333, 1000)), ExpectedLive = 24, ExpectedAttackers = 16, Features = NavigationFeatures.MovingTarget,
            TerrainEvents = new() { new TerrainEvent(3, new WorldChange { ReplaceTarget = new TargetDefinition(1, world.Center(27 + 19 * width), 1000) }) },
            Feasibility = "Discrete target relocation in empty far-side arena before any bounded-speed body can approach; no continuous solid-target sweep is claimed." });
        cases.Add(new LabCase
        {
            Name = "target-relocation-after-settlement",
            Family = "target-relocation",
            World = world,
            Requests = Orders(world, 24, new UnitProfile(250, 333, 1000)),
            ExpectedLive = 24,
            ExpectedAttackers = 16,
            Features = NavigationFeatures.MovingTarget,
            TerrainEvents = new()
            {
                new TerrainEvent(1000, new WorldChange { ReplaceTarget = new TargetDefinition(1, world.Center(16 + 14 * width), 1000) }, true)
            },
            Feasibility = "After24 physically settle, base relocates11m left. Old settled centres are at most8.005m from old base, so new base is clear by at least2.995m; new16 attack ring is open. Discrete relocation only."
        });
    }

    private static void AddDirectedFlows(List<LabCase> cases)
    {
        int width = 35;
        var world = OpenWorld(width, 25, 1000, new[] { 4 + 12 * width, 30 + 12 * width }, new[] { 2 + 8 * width, 32 + 16 * width }, 30000);
        var orders = new List<ScheduledRequest>();
        for (int index = 0; index < 8; index++)
        {
            int spawn = world.SpawnCells[index % 2];
            int target = index % 2 == 0 ? 2 : 1;
            int goal = index % 2 == 0 ? 28 + (6 + index / 2 * 3) * width : 6 + (6 + index / 2 * 3) * width;
            orders.Add(new ScheduledRequest(index * 3, new SpawnRequest(index + 1, new UnitProfile(250, 500, 2000), spawn, target, goal)));
        }
        cases.Add(new LabCase { Name = "opposing-flow-wide-arena", Family = "multiple-target-flow", World = world, Requests = orders,
            ExpectedLive = 8, ExpectedAttackers = 2, Features = NavigationFeatures.MultipleTargets | NavigationFeatures.AssignedGoals,
            Feasibility = "Wide open 33m x23m interior,8 spaced distinct goals and two disjoint upper/lower passing lanes provide an explicit simple feasible routing." });
        var largeWorld = OpenWorld(35, 29, 1000, new[] { 27 + 14 * 35 }, new[] { 2 + 14 * 35 }, 10000);
        cases.Add(new LabCase { Name = "large-body-wide-approach", Family = "large-body", World = largeWorld,
            Requests = Orders(largeWorld, 1, new UnitProfile(700, 500, 2000)), ExpectedLive = 1, ExpectedAttackers = 1, Features = NavigationFeatures.LargeBodies,
            Feasibility = "One 700mm disc in a wide open room, no packing interaction; reachable valid target centres are independently checked." });
        var sparse = largeWorld.CopyWalkable();
        for (int index = 0; index < sparse.Length; index++)
        {
            sparse[index] = sparse[index] && index % largeWorld.Width % 4 == 0 && index / largeWorld.Width % 4 == 0;
        }
        var sparseWorld = new WorldDefinition(largeWorld.Width, largeWorld.Height, 1000, default, largeWorld.CopyWalkable(), sparse,
            largeWorld.SpawnCells.ToArray(), largeWorld.Targets.ToArray(), 10000);
        foreach (bool mixed in new[] { false, true })
        {
            var sparseRequests = Orders(sparseWorld, 8, new UnitProfile(650, 500, 6000));
            if (mixed)
            {
                sparseRequests.AddRange(Orders(sparseWorld, 3, new UnitProfile(180, 1000, 8000), 0, 9));
            }
            cases.Add(new LabCase
            {
                Name = mixed ? "large-small-sparse-group" : "large-body-sparse-group",
                Family = "large-body-group",
                World = sparseWorld,
                Requests = sparseRequests,
                ExpectedLive = sparseRequests.Count,
                ExpectedAttackers = sparseRequests.Count,
                Features = NavigationFeatures.LargeBodies,
                Feasibility = "Open35x29 room with holding centres spaced4m, maximum body diameter1.3m; distinct endpoints leave wide traversable corridors and all demands have compatible attack positions."
            });
        }
        cases.Add(new LabCase { Name = "fast-per-tick-approach", Family = "fast-speed", World = largeWorld,
            Requests = Orders(largeWorld, 4, new UnitProfile(250, 2200, 1000)), ExpectedLive = 4, ExpectedAttackers = 4, Features = NavigationFeatures.FastPerTick,
            Feasibility = "Sparse fast bodies in wide open room; 5ms displacements remain220mm and are continuously checked." });
    }

    private static LabCase RandomMaze(int seed)
    {
        const int width = 45;
        const int height = 35;
        var random = new Random(seed);
        var world = OpenWorld(width, height, 1000, new[] { 36 + 17 * width }, new[] { 2 + 5 * width, 2 + 29 * width }, 8000);
        var cells = world.CopyWalkable();
        for (int x = 10; x <= 26; x += 8)
        {
            int gap = random.Next(4, height - 5);
            for (int y = 1; y < height - 1; y++)
            {
                if (Math.Abs(y - gap) > 2)
                {
                    cells[y * width + x] = false;
                }
            }
        }
        world = new WorldDefinition(width, height, 1000, default, cells, world.CopyHolding(), world.SpawnCells.ToArray(), world.Targets.ToArray(), 8000);
        var requests = Orders(world, 32, new UnitProfile(250, 500, 1000));
        var ids = Enumerable.Range(1, requests.Count).OrderBy(_ => random.Next()).ToArray();
        requests = requests.Select((request, index) => request with
        {
            Request = new SpawnRequest(ids[index], request.Request.Profile, request.Request.SpawnCell)
        }).ToList();
        return new LabCase { Name = "random-multigate-maze-" + seed, Family = "seeded-geometry", World = world, Requests = requests,
            ExpectedLive = requests.Count, ExpectedAttackers = 16, Feasibility = "Three vertical walls each retain a5-cell opening; target-side open holding arena is unobstructed; routes and distinct parking centres exist." };
    }

    private static LabCase LargeTiled(int count)
    {
        const int width = 128;
        const int height = 96;
        var world = OpenWorld(width, height, 1000, new[] { 112 + 48 * width }, new[] { 2 + 12 * width, 2 + 48 * width, 2 + 84 * width }, 20000);
        var cells = world.CopyWalkable();
        for (int x = 24; x <= 88; x += 32)
        {
            for (int y = 1; y < height - 1; y++)
            {
                if (!(y >= 16 && y <= 23) && !(y >= 44 && y <= 51) && !(y >= 72 && y <= 79))
                {
                    cells[y * width + x] = false;
                }
            }
        }
        world = new WorldDefinition(width, height, 1000, default, cells, cells, world.SpawnCells.ToArray(), world.Targets.ToArray(), 20000);
        return new LabCase { Name = "large-portal-grid-" + count, Family = "large-shared-route", World = world,
            Requests = Orders(world, count, new UnitProfile(250, 1000, 12000)), ExpectedLive = count, ExpectedAttackers = count, DeadlineTicks = 15000,
            Feasibility = "128x96 world, three eight-cell portals per wall and broad target-side arena; more distinct sparse goals than demand." };
    }

    private static WorldDefinition ClassicWorld(NavigationScenario item) => new WorldDefinition(
        item.Width, item.Height, 1000, default, item.Walkable, item.HoldingAllowed ?? item.Walkable,
        item.SpawnCells.Select(cell => cell.Y * item.Width + cell.X).ToArray(),
        new[] { new TargetDefinition(1, new Point2(item.Target.X * 1000 + 500, item.Target.Y * 1000 + 500), 1000) }, item.GatheringDistance);

    private static WorldDefinition OpenWorld(int width, int height, int cell, int[] targets, int[] entries, int gather, bool border = true)
    {
        var walk = new bool[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                walk[y * width + x] = !border || x > 0 && y > 0 && x < width - 1 && y < height - 1;
            }
        }
        var definitions = targets.Select((index, i) =>
        {
            var center = new Point2(index % width * cell + cell / 2, index / width * cell + cell / 2);
            return new TargetDefinition(i + 1, center, 1000);
        }).ToArray();
        return new WorldDefinition(width, height, cell, default, walk, walk, entries, definitions, gather);
    }

    private static List<ScheduledRequest> Orders(WorldDefinition world, int count, UnitProfile profile, int ready = 0, int first = 1)
    {
        return Enumerable.Range(first, count).Select((id, index) =>
        {
            var request = new SpawnRequest(id, profile, world.SpawnCells[index % world.SpawnCells.Count]);
            return new ScheduledRequest(ready, request);
        }).ToList();
    }
}
