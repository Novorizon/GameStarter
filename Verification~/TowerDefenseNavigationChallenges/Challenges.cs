using System;
using System.Collections.Generic;
using System.Linq;

internal enum DeathPattern
{
    AttackGoalOrdinal,
    AllAttackers,
    AlternatingAttackers,
    FarthestWaiting,
    MixedSix,
    AllShortRange
}

internal sealed class RecoveryPhase
{
    public DeathPattern Pattern { get; init; }
    public int Ordinal { get; init; }
    public bool DuringArrival { get; init; }
}

internal sealed class AgentOrder
{
    public int Id { get; set; }
    public Cell Spawn { get; init; }
    public int Radius { get; init; } = 250;
    public int Range { get; init; } = 1000;
    public int Speed { get; init; } = 1000;
    public int Group { get; init; }
    public int ReleaseTick { get; init; }
}

internal sealed class Challenge
{
    public string Name { get; init; } = "";
    public NavigationScenario Geometry { get; init; } = null!;
    public List<AgentOrder> Orders { get; init; } = new List<AgentOrder>();
    public RecoveryPhase[] Recovery { get; init; } = Array.Empty<RecoveryPhase>();
    public int IdleTicks { get; init; } = 200;
    public int MaxTicks { get; init; } = 12000;
    public int PlanningHorizon { get; init; } = 20000;
    public int RequiredShortRangeAttackers { get; init; }
    public AgentOrder? Blocker { get; init; }
    public int RemoveBlockerTick { get; init; } = 200;
    public int RecoveryAfterBlockerTicks { get; init; } = 1000;
    public bool PhysicalOverflow { get; init; }
}

internal static class ChallengeCatalog
{
    public static IEnumerable<Challenge> Create(int seed)
    {
        var original = Open(
            37,
            37,
            new Cell(29, 18),
            new[] { new Cell(2, 18), new Cell(18, 2), new Cell(18, 34) },
            4000,
            blocked: cell => !(cell.Y >= 17 && cell.Y <= 19) && !(cell.X >= 17 && cell.X <= 19)
                && !(cell.X >= 22 && cell.Y >= 10 && cell.Y <= 26));
        original = LockOriginalTargets(original);
        yield return Basic("original-4m-all-72-long-hold", original, seed, idle: 2000);
        yield return Basic(
            "full-72-each-attack-slot-death",
            original,
            seed,
            recovery: Enumerable.Range(0, 12).Select(index => new RecoveryPhase { Pattern = DeathPattern.AttackGoalOrdinal, Ordinal = index }).ToArray());
        yield return Basic("full-72-all-attackers-die", original, seed, recovery: new[] { new RecoveryPhase { Pattern = DeathPattern.AllAttackers } });
        yield return Basic(
            "full-72-rear-and-side-deaths",
            original,
            seed,
            recovery: new[]
            {
                new RecoveryPhase { Pattern = DeathPattern.FarthestWaiting },
                new RecoveryPhase { Pattern = DeathPattern.AlternatingAttackers },
                new RecoveryPhase { Pattern = DeathPattern.MixedSix },
                new RecoveryPhase { Pattern = DeathPattern.FarthestWaiting }
            });
        yield return Basic(
            "death-during-inflight-arrivals",
            original,
            seed,
            recovery: new[] { new RecoveryPhase { Pattern = DeathPattern.AttackGoalOrdinal, DuringArrival = true } });
        var four = Copy(original, original.SpawnCells.Concat(new[] { new Cell(35, 18) }).ToArray());
        yield return Basic("original-4m-four-spawns-mixed-speed-72", four, seed, mixedSpeed: true);

        var open = Open(35, 35, new Cell(17, 17), new[] { new Cell(1, 17) }, 10000);
        yield return Mixed("mixed-range-full-outer-ring-inner-death", open, seed, 250, 250, 12);
        yield return Mixed("mixed-radius-ranged-inner-vacancy", open, seed, 490, 140, 8);
        var openFour = Copy(open, new[] { new Cell(1, 17), new Cell(17, 1), new Cell(33, 17), new Cell(17, 33) });
        yield return Mixed("mixed-range-multiple-spawns-inner-death", openFour, seed, 250, 250, 12);

        var throat = Open(
            39,
            27,
            new Cell(31, 13),
            new[] { new Cell(1, 5), new Cell(1, 13), new Cell(1, 21) },
            5000,
            blocked: cell => cell.X >= 15 && cell.X <= 18 && cell.Y != 13);
        var withBlocker = Copy(throat, new[] { new Cell(1, 13), new Cell(15, 13) });
        yield return new Challenge
        {
            Name = "temporary-throat-blocker-removed",
            Geometry = withBlocker,
            Orders = Orders(48, withBlocker, seed, spawnCount: 1),
            Blocker = new AgentOrder { Id = 1000000, Spawn = new Cell(15, 13), Radius = 490, Speed = 1 },
            MaxTicks = 4000,
            IdleTicks = 100
        };

        var large = Open(45, 45, new Cell(31, 22), new[] { new Cell(1, 8), new Cell(1, 22), new Cell(1, 36) }, 8000);
        var waves = new List<AgentOrder>();
        int first = 1;
        int wave = 0;
        foreach (int count in new[] { 8, 16, 18, 25, 30, 29 })
        {
            waves.AddRange(Orders(count, large, seed + wave, firstId: first, releaseStart: wave * 500, interval: 2));
            first += count;
            wave++;
        }

        yield return new Challenge { Name = "long-no-tower-all-126", Geometry = large, Orders = waves, IdleTicks = 2000 };
        var tiny = Open(9, 9, new Cell(4, 4), new[] { new Cell(1, 4) }, 4000);
        yield return new Challenge
        {
            Name = "true-physical-capacity-200-queue-retained",
            Geometry = tiny,
            Orders = Orders(200, tiny, seed, radius: 490),
            PhysicalOverflow = true,
            IdleTicks = 1000
        };
    }

    private static Challenge Basic(
        string name,
        NavigationScenario geometry,
        int seed,
        RecoveryPhase[]? recovery = null,
        int idle = 200,
        bool mixedSpeed = false)
    {
        return new Challenge
        {
            Name = name,
            Geometry = geometry,
            Orders = Orders(72, geometry, seed, mixedSpeed: mixedSpeed),
            Recovery = recovery ?? Array.Empty<RecoveryPhase>(),
            MaxTicks = 30000,
            IdleTicks = idle
        };
    }

    private static Challenge Mixed(string name, NavigationScenario geometry, int seed, int outerRadius, int innerRadius, int innerCount)
    {
        var orders = Orders(28, geometry, seed, radius: outerRadius, range: 4000);
        orders.AddRange(Orders(innerCount, geometry, seed, firstId: 29, radius: innerRadius, group: 1));
        return new Challenge
        {
            Name = name,
            Geometry = geometry,
            Orders = orders,
            Recovery = new[] { new RecoveryPhase { Pattern = DeathPattern.AllShortRange }, new RecoveryPhase { Pattern = DeathPattern.AllShortRange } },
            RequiredShortRangeAttackers = innerCount,
            MaxTicks = 30000
        };
    }

    private static List<AgentOrder> Orders(
        int count,
        NavigationScenario geometry,
        int seed,
        int firstId = 1,
        int radius = 250,
        int range = 1000,
        int group = 0,
        int releaseStart = 0,
        int interval = 0,
        bool mixedSpeed = false,
        int spawnCount = 0)
    {
        var result = new List<AgentOrder>();
        int entrances = spawnCount == 0 ? geometry.SpawnCells.Length : spawnCount;
        for (int index = 0; index < count; index++)
        {
            int id = firstId + index;
            result.Add(new AgentOrder
            {
                Id = id,
                Spawn = geometry.SpawnCells[(id + seed % entrances) % entrances],
                Radius = radius,
                Range = range,
                Speed = mixedSpeed ? 1000 / new[] { 1, 2, 4 }[(id + seed % 3) % 3] : 1000,
                Group = group,
                ReleaseTick = releaseStart + index * interval
            });
        }

        return result;
    }

    private static NavigationScenario LockOriginalTargets(NavigationScenario value)
    {
        var holding = (bool[])value.Walkable.Clone();
        foreach (int dx in new[] { -1, 1 })
        {
            foreach (int dy in new[] { -1, 1 })
            {
                holding[(value.Target.Y + dy) * value.Width + value.Target.X + dx] = false;
            }
        }

        return new NavigationScenario
        {
            Width = value.Width,
            Height = value.Height,
            Target = value.Target,
            Walkable = value.Walkable,
            HoldingAllowed = holding,
            GatheringDistance = value.GatheringDistance,
            SpawnCells = value.SpawnCells
        };
    }

    private static NavigationScenario Copy(NavigationScenario value, Cell[] spawns)
    {
        return new NavigationScenario
        {
            Width = value.Width,
            Height = value.Height,
            Target = value.Target,
            Walkable = value.Walkable,
            HoldingAllowed = value.HoldingAllowed,
            GatheringDistance = value.GatheringDistance,
            SpawnCells = spawns
        };
    }

    private static NavigationScenario Open(int width, int height, Cell target, Cell[] spawns, int gatheringDistance, Func<Cell, bool>? blocked = null)
    {
        var walk = new bool[width * height];
        for (int y = 1; y < height - 1; y++)
        {
            for (int x = 1; x < width - 1; x++)
            {
                walk[y * width + x] = !(blocked?.Invoke(new Cell(x, y)) ?? false);
            }
        }

        return new NavigationScenario
        {
            Width = width,
            Height = height,
            Target = target,
            Walkable = walk,
            SpawnCells = spawns,
            GatheringDistance = gatheringDistance
        };
    }
}
