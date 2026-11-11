using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;

internal static class Program
{
    private const int NavigationDurationTicks = 3600;
    private static readonly string Assets = Path.Combine("Assets", "GameAssets");

    private static int Main(string[] args)
    {
        var tables = new cfg.Tables(name => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(Assets, "Data", "Luban", name + ".bytes"))));
        if (args.Length == 1 && args[0] == "--forest-vacancy-audit")
        {
            return AuditForestVacancies(tables);
        }

        var cases = new[]
        {
            new NavigationCase("forest-authored", 12345, () => LoadNavigationStage(tables, 1), VerifyAuthoredForest),
            new NavigationCase("forest-defeat", 1, () => ForestDefeat(tables), VerifyDefeat),
            new NavigationCase("forest-32", 33, () => ForestAtStrength(tables, 32), VerifyCompactForest),
            new NavigationCase("forest-saturation", 12345, () => SaturationStage(tables), VerifySaturation),
            new NavigationCase("forest-long", 12345, () => SaturationStage(tables), VerifyFullySettledForest, DurationTicks: 6000, MeasureNearSpace: true),
            new NavigationCase("forest-dense-spawns", 4729, () => DenseForest(tables), VerifySaturation),
            new NavigationCase("forest-dense-long", 4729, () => DenseForest(tables), VerifyFullySettledForest, DurationTicks: 6000, MeasureNearSpace: true),
            new NavigationCase("forest-heavy-leading", 1, () => HeavyLeadingForest(tables), VerifySaturation),
            new NavigationCase(
                "forest-heavy-leading-long",
                1,
                () => HeavyLeadingForest(tables),
                VerifyFullySettledForest,
                DurationTicks: 6000,
                MeasureNearSpace: true),
            new NavigationCase("forest-pause-resume", 12345, () => SaturationStage(tables), VerifySaturation, PauseTick: 700),
            new NavigationCase("forest-vacancy", 12345, () => WithVacancyTower(SaturationStage(tables)), VerifyRefill, VacancyTick: 1800),
            new NavigationCase(
                "forest-late-vacancy",
                12345,
                () => WithVacancyTower(SaturationStage(tables)),
                VerifyRefill,
                VacancyTick: 6100,
                DurationTicks: 6500),
            ForestVacancyCase(tables, "forest-south-vacancy", new Position2(6940, -5120)),
            ForestVacancyCase(tables, "forest-southwest-vacancy", new Position2(5980, -5120)),
            ForestVacancyCase(tables, "forest-simultaneous-vacancies", new Position2(7900, -3840), new Position2(8060, -3360)),
            ForestVacancyCase(tables, "forest-sequential-vacancies", new Position2(6940, -5120), new Position2(6460, -4960))
                with { VacancySpacingTicks = 10 },
            new NavigationCase(
                "blocked-pocket-vacancy",
                1,
                () => BlockedPocketField(tables),
                VerifyBlockedPocketRefill,
                DurationTicks: 400,
                TargetedVacancies: new[] { new Position2(1280, 0) },
                BlockedPocketStress: true),
            new NavigationCase("waterfall-ridge-saturation", 12345, () => WaterfallRidgeAtSaturation(tables), VerifyWaterfallRidge),
            new NavigationCase("open-single-entry", 33, () => OpenField(tables, 1, 12, 7), VerifyOpenField),
            new NavigationCase("open-three-entries", 4729, () => OpenField(tables, 3, 12, 3), VerifyOpenField),
            new NavigationCase("open-three-entries-delayed", 1, () => OpenField(tables, 3, 12, 7, 600), VerifyOpenField),
            new NavigationCase("open-heavy-leading", 4729, () => OpenField(tables, 1, 12, 7, heavyLeading: true), VerifyOpenField),
            new NavigationCase("open-vacancy", 33, () => WithVacancyTower(OpenField(tables, 3, 12, 7)), VerifyRefill, VacancyTick: 1200),
            new NavigationCase("construction-cancels-reservations", 33, () => ConstructionField(tables), VerifyConstruction, ConstructionStress: true),
            new NavigationCase("blocked-flank", 12345, () => OpenField(tables, 1, 12, 5, blockedFlank: true), VerifyOpenField),
            new NavigationCase("sealed-approach", 12345, () => OpenField(tables, 1, 6, 10, sealedApproach: true), VerifySealed),
            new NavigationCase("cliff-crossing-drain", 12345, () => CliffCrossingDrain(tables), VerifyPortalDrain, DurationTicks: 6000),
            new NavigationCase("overlapping-crossing-locks", 1, () => OverlappingCrossingLocks(tables), VerifyPortalDrain, DurationTicks: 1000),
            new NavigationCase("overlapping-crossing-locks-reversed", 1, () => OverlappingCrossingLocks(tables, true), VerifyPortalDrain, DurationTicks: 1000),
        }.Concat(tables.TbTdStage.DataList.Where(stage => stage.Id != 1).Select(stage => new NavigationCase(
            "authored-stage-" + stage.Id, 12345, () => LoadNavigationStage(tables, stage.Id), VerifyAuthoredStage))).ToArray();

        bool reportOnly = args.Contains("--report-only");
        bool details = args.Contains("--details");
        bool timing = args.Contains("--timing");
        bool nearSpace = args.Contains("--near-space");
        var selection = args.Where(arg => arg != "--report-only" && arg != "--details" && arg != "--timing" && arg != "--near-space").ToHashSet();
        if (selection.Any(name => !cases.Any(test => test.Name == name)))
        {
            Console.Error.WriteLine("Available scenarios: " + string.Join(", ", cases.Select(test => test.Name)));
            return 2;
        }

        int failures = 0;
        int tested = 0;
        foreach (var test in cases.Where(test => selection.Count == 0 || selection.Contains(test.Name)))
        {
            tested++;
            try
            {
                var configuration = test.CreateConfiguration();
                var first = ObserveNavigation(test, configuration, timing, details, nearSpace || test.MeasureNearSpace);
                var repeatedTest = test.PauseTick > 0 ? test with { PauseTick = 0 } : test;
                var repeated = ObserveNavigation(repeatedTest, test.CreateConfiguration(), timing);
                Require(first.StateHash == repeated.StateHash, "fixed-tick positions, attacks and HP changed across identical runs");
                if (!reportOnly)
                {
                    test.Verify(configuration, first);
                    Require(test.PauseTick == 0 || first.PauseChecks == 1, "the pause fixture did not pause its active trajectories");
                }
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
            }
        }

        if (failures == 0)
        {
            string result = reportOnly ? "Reported" : "PASS";
            Console.WriteLine($"{result} {tested} NPC navigation scenarios, each executed twice.");
        }

        return failures == 0 ? 0 : 1;
    }

    private sealed record NavigationCase(
        string Name,
        uint Seed,
        Func<FightConfiguration> CreateConfiguration,
        Action<FightConfiguration, NavigationReport> Verify,
        int VacancyTick = 0,
        int DurationTicks = NavigationDurationTicks,
        int PauseTick = 0,
        bool ConstructionStress = false,
        bool MeasureNearSpace = false,
        Position2[] TargetedVacancies = null,
        int VacancySpacingTicks = 0,
        bool BlockedPocketStress = false);

    private static FightConfiguration SaturationStage(cfg.Tables tables)
    {
        var config = LoadNavigationStage(tables, 1);
        config.BaseHealth = 1000000;
        return config;
    }

    private static NavigationCase ForestVacancyCase(cfg.Tables tables, string name, params Position2[] positions)
    {
        return new NavigationCase(name, 12345, () => SaturationStage(tables), VerifyRefill, VacancyTick: 6100, DurationTicks: 6500,
            TargetedVacancies: positions);
    }

    private static int AuditForestVacancies(cfg.Tables tables)
    {
        var reference = new FightSession(SaturationStage(tables), 12345);
        Require(reference.StartNextWave(), "could not start the forest vacancy audit");
        while (reference.Tick < 6100 && reference.Phase == BattlePhase.Fighting)
        {
            reference.Step();
        }

        var formation = reference.Enemies;
        var attackers = formation.Where(enemy => enemy.AttackingBase).ToArray();
        Require(formation.Count == 126 && attackers.Length > 0 && formation.All(enemy => enemy.AttackingBase || enemy.IsStaged),
            "the forest vacancy audit did not start with a settled force of 126 enemies");

        int failures = 0;
        foreach (var attacker in attackers)
        {
            var vacancy = attacker.Position;
            var test = ForestVacancyCase(tables, $"forest-vacancy-{vacancy.X},{vacancy.Y}", vacancy);
            try
            {
                var config = test.CreateConfiguration();
                VerifyRefill(config, ObserveNavigation(test, config, timing: false));
            }
            catch (Exception error)
            {
                failures++;
                Console.Error.WriteLine($"FAIL {test.Name}: {error.Message}");
            }
        }

        Console.WriteLine($"VACANCY AUDIT {attackers.Length - failures}/{attackers.Length} occupied attack positions refilled within 300 ticks.");
        return failures == 0 ? 0 : 1;
    }

    private static FightConfiguration BlockedPocketField(cfg.Tables tables)
    {
        var config = SaturationStage(tables);
        config.RelayRouteId = 0;
        config.BuildSlots = Array.Empty<BuildSlotDefinition>();
        config.BuildGridStep = 0;
        config.Routes = new[] { new RouteDefinition { Id = 1, Points = new[] { new Position2(6500, 0), new Position2(0, 0) } } };
        config.Waves = new[]
        {
            new WaveDefinition
            {
                Id = 1,
                Groups = new[]
                {
                    new SpawnGroup { EnemyId = 1, RouteId = 1, Count = 2, IntervalTicks = 1 },
                    new SpawnGroup { EnemyId = 3, RouteId = 1, Count = 1, IntervalTicks = 1 }
                }
            }
        };
        var min = new Position2(-1200, -1200);
        var max = new Position2(7000, 1600);
        // A narrow neck reaches the attack position. The wider corridor allows a reserve to yield into a dead-end side pocket.
        var boundary = new[]
        {
            new Position2(-1200, -1200), new Position2(1000, -1200), new Position2(1000, -270), new Position2(2100, -270),
            new Position2(2100, -450), new Position2(7000, -450), new Position2(7000, 450), new Position2(3232, 450),
            new Position2(3232, 1500), new Position2(2528, 1500), new Position2(2528, 450), new Position2(2100, 450),
            new Position2(2100, 270), new Position2(1000, 270), new Position2(1000, 1200), new Position2(-1200, 1200)
        };
        config.AssaultNavigation = new AssaultNavigationMap(
            min,
            max,
            min,
            max,
            new Position2(-1000, -1000),
            new Position2(1000, 1000),
            walkableBoundary: boundary,
            approachInset: 5600);
        return config;
    }

    private static FightConfiguration WaterfallRidgeAtSaturation(cfg.Tables tables)
    {
        int stageId = tables.TbTdStage.DataList.Single(stage => Path.GetFileName(stage.MapAsset) == "WaterfallRidge").Id;
        var config = LoadNavigationStage(tables, stageId);
        config.BaseHealth = 1000000;
        return config;
    }

    private static FightConfiguration CliffCrossingDrain(cfg.Tables tables)
    {
        int stageId = tables.TbTdStage.DataList.Single(stage => Path.GetFileName(stage.MapAsset) == "CliffCrossing").Id;
        var config = LoadNavigationStage(tables, stageId);
        int expected = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        var objective = config.Objective;
        config.Objective = new BattleObjective(objective.Type, objective.TargetName, escapesToDefeat: expected + 1, assault: objective.Assault);
        return config;
    }

    private static FightConfiguration OverlappingCrossingLocks(cfg.Tables tables, bool reverseSpawns = false)
    {
        var config = LoadNavigationStage(tables, 1);
        config.AssaultNavigation = null;
        config.Objective = new BattleObjective(
            BattleObjectiveType.InterceptEscapes,
            "Regression Portal",
            escapesToDefeat: 3,
            assault: config.Objective.Assault);
        config.Routes = new[]
        {
            new RouteDefinition { Id = 1, Points = new[] { new Position2(-6000, 0), new Position2(6000, 0) } },
            new RouteDefinition
            {
                Id = 2,
                Points = new[]
                {
                    new Position2(1250, -6000), new Position2(1250, 500), new Position2(-1250, 500),
                    new Position2(-1250, -6000), new Position2(6000, -6000), new Position2(6000, 0)
                }
            }
        };
        var enemy = config.Enemies.Single(definition => definition.Id == 1);
        enemy.SpeedPerTick = 100;
        enemy.Health = 1;
        enemy.BaseDamage = 1;
        config.Waves = new[]
        {
            new WaveDefinition
            {
                Id = 1,
                Groups = config.Routes.Select(route => new SpawnGroup { EnemyId = 1, RouteId = route.Id, Count = 1, IntervalTicks = 1 }).ToArray()
            }
        };
        if (reverseSpawns)
        {
            Array.Reverse(config.Waves[0].Groups);
        }

        return config;
    }

    private static FightConfiguration ForestDefeat(cfg.Tables tables)
    {
        var config = LoadNavigationStage(tables, 1);
        config.BaseHealth = 20;
        return config;
    }

    private static FightConfiguration ForestAtStrength(cfg.Tables tables, int strength)
    {
        var config = SaturationStage(tables);
        var selectedWaves = new List<WaveDefinition>();
        foreach (var wave in config.Waves)
        {
            var selectedGroups = new List<SpawnGroup>();
            foreach (var group in wave.Groups)
            {
                if (strength == 0)
                {
                    break;
                }

                group.Count = Math.Min(strength, group.Count);
                strength -= group.Count;
                selectedGroups.Add(group);
            }

            wave.Groups = selectedGroups.ToArray();
            selectedWaves.Add(wave);
            if (strength == 0)
            {
                break;
            }
        }

        config.Waves = selectedWaves.ToArray();
        return config;
    }

    private static FightConfiguration DenseForest(cfg.Tables tables)
    {
        var config = SaturationStage(tables);
        foreach (var wave in config.Waves)
        {
            foreach (var group in wave.Groups)
            {
                group.StartTick /= 2;
                group.IntervalTicks = Math.Max(1, group.IntervalTicks / 2);
            }
        }

        return config;
    }

    private static FightConfiguration HeavyLeadingForest(cfg.Tables tables)
    {
        var config = SaturationStage(tables);
        var firstWave = config.Waves[0];
        var firstGroup = firstWave.Groups.OrderBy(group => group.StartTick).First();
        int heavyId = config.Waves.SelectMany(wave => wave.Groups).Select(group => group.EnemyId)
            .OrderByDescending(FightSession.EnemyNavigationRadiusMillimetres).First();
        var heavyWave = config.Waves.Last(wave => wave.Groups.Any(group => group.EnemyId == heavyId));
        var heavyGroup = heavyWave.Groups.First(group => group.EnemyId == heavyId);
        int swapCount = Math.Min(4, Math.Min(firstGroup.Count, heavyGroup.Count));
        SwapOpeningUnits(firstWave, firstGroup, heavyId, swapCount);
        SwapOpeningUnits(heavyWave, heavyGroup, firstGroup.EnemyId, swapCount);
        return config;
    }

    private static void SwapOpeningUnits(WaveDefinition wave, SpawnGroup group, int enemyId, int count)
    {
        var early = new SpawnGroup
        {
            EnemyId = enemyId,
            RouteId = group.RouteId,
            Count = count,
            StartTick = group.StartTick,
            IntervalTicks = group.IntervalTicks
        };
        group.Count -= count;
        group.StartTick += count * group.IntervalTicks;
        wave.Groups = wave.Groups.Where(candidate => candidate.Count > 0).Append(early).ToArray();
    }

    private static FightConfiguration OpenField(
        cfg.Tables tables,
        int entryCount,
        int unitsPerEntry,
        int interval,
        int delay = 0,
        bool blockedFlank = false,
        bool sealedApproach = false,
        bool heavyLeading = false)
    {
        var config = SaturationStage(tables);
        config.BuildSlots = Array.Empty<BuildSlotDefinition>();
        config.BuildGridStep = 0;
        Position2[] starts = { new Position2(-8500, 0), new Position2(0, 8500), new Position2(8500, 0) };
        config.Routes = starts.Take(entryCount).Select((position, index) => new RouteDefinition
        {
            Id = index + 1,
            Points = new[] { position, new Position2(position.X / 2, position.Y / 2), new Position2(0, 0) }
        }).ToArray();
        var spawnTypes = heavyLeading ? new[]
        {
            config.Enemies.OrderByDescending(enemy => FightSession.EnemyNavigationRadiusMillimetres(enemy.Id)).First(),
            config.Enemies[0]
        } : config.Enemies.Take(2).ToArray();
        config.Waves = new[]
        {
            new WaveDefinition
            {
                Id = 1,
                Groups = config.Routes.SelectMany(route => spawnTypes.Select((enemy, index) => new SpawnGroup
                {
                    EnemyId = enemy.Id,
                    RouteId = route.Id,
                    Count = unitsPerEntry / 2,
                    StartTick = (route.Id == entryCount && entryCount > 1 ? delay : 0) + index * interval,
                    IntervalTicks = 2 * interval
                })).ToArray()
            }
        };

        Position2[][] blockers = Array.Empty<Position2[]>();
        if (blockedFlank || sealedApproach)
        {
            int halfWall = sealedApproach ? 10000 : 1500;
            blockers = new[]
            {
                new[] { new Position2(-3000, -halfWall), new Position2(-2650, -halfWall), new Position2(-2650, halfWall), new Position2(-3000, halfWall) }
            };
        }

        config.AssaultNavigation = new AssaultNavigationMap(
            new Position2(-10000, -10000),
            new Position2(10000, 10000),
            new Position2(-5000, -5000),
            new Position2(5000, 5000),
            new Position2(-950, -950),
            new Position2(950, 950),
            blockers);
        return config;
    }

    private static FightConfiguration WithVacancyTower(FightConfiguration config)
    {
        var area = config.AssaultNavigation;
        config.BuildGridStep = 0;
        config.BuildSlots = new[] { new BuildSlotDefinition { Id = 1, Position = new Position2(area.AreaMin.X - 400, area.AreaMin.Y + 400) } };
        config.Towers = new[]
        {
            new TowerDefinition { Id = 1, BuildCost = 1, UpgradeCost = 1, AttackIntervalTicks = 10000, Damage = 1000000, Range = 25000 }
        };
        return config;
    }

    private static FightConfiguration ConstructionField(cfg.Tables tables)
    {
        var config = OpenField(tables, 3, 12, 3);
        config.InitialGold = 100;
        config.Towers = new[]
        {
            new TowerDefinition { Id = 1, BuildCost = 1, UpgradeCost = 1, AttackIntervalTicks = 10000, Damage = 1, Range = 1 }
        };
        var slots = new List<BuildSlotDefinition>();
        for (int y = -4000; y <= 4000; y += 500)
        {
            for (int x = -4000; x <= 4000; x += 500)
            {
                var point = new Position2(x, y);
                if (config.AssaultNavigation.CanSweep(point, point, FightSession.TowerFoundationRadiusMillimetres))
                {
                    slots.Add(new BuildSlotDefinition { Id = slots.Count + 1, Position = point });
                }
            }
        }

        config.BuildSlots = slots.ToArray();
        return config;
    }

    private static void VerifyDefeat(FightConfiguration config, NavigationReport report)
    {
        Require(report.FinalPhase == BattlePhase.Defeat, "the no-tower game did not end in defeat");
        if (config.Objective.Type == BattleObjectiveType.DefendBase)
        {
            Require(report.FirstDamageTick > 0 && report.Damage == config.BaseHealth, "the base did not lose its authored durability to attacks");
        }
        else
        {
            Require(report.Leaks >= config.Objective.EscapesToDefeat, "the authored portal defeat threshold was not reached");
        }

        VerifyMovementQuality(report);
    }

    private static void VerifyAuthoredForest(FightConfiguration config, NavigationReport report)
    {
        VerifyAuthoredBase(config, report);
        Require(report.AttackSectors >= 4, "damage at the authored base was concentrated on too few sides");
    }

    private static void VerifyAuthoredStage(FightConfiguration config, NavigationReport report)
    {
        if (config.Objective.Type == BattleObjectiveType.DefendBase)
        {
            VerifyAuthoredBase(config, report);
        }
        else
        {
            VerifyDefeat(config, report);
        }
    }

    private static void VerifyAuthoredBase(FightConfiguration config, NavigationReport report)
    {
        Require(config.BaseHealth == 100000, "the authored base did not start with 100000 health");
        Require(report.FinalPhase == BattlePhase.Fighting && report.FinalTick == NavigationDurationTicks,
            "the authored base battle ended before the observation window");
        int attackInterval = config.Objective.BaseAttackIntervalTicks;
        Require(report.FirstDamageTick > 0 && report.LastDamageTick - report.FirstDamageTick >= attackInterval,
            "enemies did not sustain damage to the authored base across attack intervals");
        Require(report.FinalTick - report.LastDamageTick < attackInterval, "enemies stopped damaging the authored base before the end of the window");
        VerifyMovementQuality(report);
    }

    private static void VerifySaturation(FightConfiguration config, NavigationReport report)
    {
        int expected = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        Require(report.Living == expected && report.Moved == expected, "not all configured enemies spawned and left the entrance");
        Require(report.Hit >= 13 && report.AttackSectors == 8, "fewer than 13 NPCs damaged the base or the attack left a sector unused");
        Require(report.Admitted == expected && report.StagingSectors >= 5, "the complete forest force did not reach the assault area");
        Require(report.QueueStopped == 0, "an unsettled forest unit stopped instead of continuing its approach or local movement");
        Require(report.Capacity == null || report.Capacity.ReachableAttackCells == 0, "the queue head could reach an unoccupied attack position");
        VerifyMovementQuality(report);
    }

    private static void VerifyWaterfallRidge(FightConfiguration config, NavigationReport report)
    {
        Require(report.Damage > 0 && report.AttackSectors >= 7, "the real Waterfall Ridge force did not attack from seven or more sectors");
        Require(report.Capacity == null || report.Capacity.ReachableAttackCells == 0, "the queue head could reach an unoccupied attack position");
        VerifyMovementQuality(report);
    }

    private static void VerifyFullySettledForest(FightConfiguration config, NavigationReport report)
    {
        VerifySaturation(config, report);
        int expected = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        Require(report.Settled == expected && report.ActiveManeuvers == 0, "the complete forest force did not settle after five minutes");
        Require(report.NearBase >= 76, "fewer than 76 forest enemies settled within 4.5 metres of the base after five minutes");
        Require(report.NearSpace?.NearMonotonicStagingCells == 0, "a distant reserve can still reach a legal closer staging position after five minutes");
        var adjacent = report.AdjacentBand;
        Require(adjacent != null && adjacent.Reserves >= 12, "the adjacent reserve band was mostly empty despite a settled forest force");
        Require(adjacent.OccupiedFlanks >= 3 && adjacent.OccupiedSectors >= 6 && adjacent.LongestEmptyRun <= 2,
            "the adjacent reserve band left a wide continuous gap on walkable terrain");
    }

    private static void VerifyPortalDrain(FightConfiguration config, NavigationReport report)
    {
        int expected = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        Require(
            report.Leaks == expected && report.Living == 0 && report.FinalPhase == BattlePhase.Victory,
            "not every enemy traversed the shared-route crossings and exited");
        VerifyMovementQuality(report);
    }

    private static void VerifyOpenField(FightConfiguration config, NavigationReport report)
    {
        int expected = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        Require(report.Settled == expected, "an enemy remained on the approach despite available local space");
        Require(report.Hit >= 8 && report.AttackSectors >= 5, "the attack concentrated on too few available sides");
        Require(report.QueueStopped == 0, "an enemy stopped before reaching an assigned position");
        VerifyMovementQuality(report);
    }

    private static void VerifyCompactForest(FightConfiguration config, NavigationReport report)
    {
        VerifyOpenField(config, report);
        int expected = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        Require(report.NearBase == expected, "a small forest force left reserves outside 4.5 metres despite nearby assembly space");
    }

    private static void VerifyRefill(FightConfiguration config, NavigationReport report)
    {
        Require(report.Kills == report.ExpectedRefills && report.KilledAttacker, "the test casualties did not free the expected attack positions");
        Require(report.RefillTicks > 0 && report.RefillTicks <= 300, "the attack line was not replenished and dealing damage within 15 seconds");
        VerifyMovementQuality(report);
    }

    private static void VerifyBlockedPocketRefill(FightConfiguration config, NavigationReport report)
    {
        VerifyRefill(config, report);
        Require(report.AttackVacancyYield, "a reserve did not yield to let a smaller unit reach the blocked attack position");
    }

    private static void VerifyConstruction(FightConfiguration config, NavigationReport report)
    {
        Require(report.ConstructionCancellations == 1 && report.CanceledManeuvers >= 2, "construction did not cancel concurrent reserved maneuvers");
        Require(report.Kills == 0, "the construction obstacle turret unexpectedly killed an NPC");
        VerifyOpenField(config, report);
    }

    private static void VerifySealed(FightConfiguration config, NavigationReport report)
    {
        int expected = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        Require(report.Moved == expected && report.Staged == expected, "an enemy failed to gather on the walkable side of the wall");
        Require(report.Damage == 0 && report.Hit == 0 && report.FurthestEast <= -3000, "an enemy crossed the sealed wall or damaged the base");
        VerifyMovementQuality(report);
    }

    private static void VerifyMovementQuality(NavigationReport report)
    {
        Require(report.PeakOverlap == 0, "enemy bodies overlapped at a simulation tick");
        Require(report.PeakInterpolatedOverlap <= 16, "enemy bodies overlapped while interpolating between simulation ticks");
        Require(report.InvalidTerrainSteps == 0, "an enemy penetrated blocked geometry inside the assault area");
        Require(report.InvalidAttackPositions == 0, "an enemy attacked from an invalid position");
        Require(report.InvalidTowerSteps == 0, "an enemy crossed a constructed tower foundation");
        Require(report.LongestOscillation < 6, "an enemy reversed repeatedly over the same short distance");
    }

    private static void Require(bool passed, string reason)
    {
        if (!passed)
        {
            throw new InvalidOperationException(reason);
        }
    }

    private static FightConfiguration LoadNavigationStage(cfg.Tables tables, int stageId)
    {
        var config = TowerDefenseTables.Create(tables, stageId);
        var mapName = Path.GetFileName(tables.TbTdStage.Get(stageId).MapAsset) + ".json";
        string json = File.ReadAllText(Path.Combine(Assets, "TowerDefense", "Maps", mapName));
        var settings = TowerDefenseMapObjectives.Parse(json);
        settings.ApplyTo(config);
        return config;
    }

    private sealed class NavigationReport
    {
        public ulong StateHash = 14695981039346656037UL;
        public int Moved;
        public int Approached;
        public int Hit;
        public int PeakAttackers;
        public int PeakOverlap;
        public int PeakInterpolatedOverlap;
        public int AttackSectors;
        public int StagingSectors;
        public int Staged;
        public int Settled;
        public int Admitted;
        public int ActiveManeuvers;
        public int NearBase;
        public int Living;
        public int Spawned;
        public int Leaks;
        public int QueueStopped;
        public int QueueReserved;
        public int LongestOscillation;
        public int InvalidTerrainSteps;
        public int InvalidAttackPositions;
        public int InvalidTowerSteps;
        public int ConstructionCancellations;
        public int CanceledManeuvers;
        public int PauseChecks;
        public int Kills;
        public int ExpectedRefills;
        public bool KilledAttacker;
        public bool AttackVacancyYield;
        public long RefillTicks;
        public int FurthestEast = int.MinValue;
        public long LastSpawnTick;
        public long AllSettledTick;
        public long FirstDamageTick;
        public long LastDamageTick;
        public long FinalTick;
        public int Damage;
        public BattlePhase FinalPhase;
        public CapacityProbe.Result Capacity;
        public CapacityProbe.NearResult NearSpace;
        public CapacityProbe.AdjacentResult AdjacentBand;

        public override string ToString()
        {
            string capacity = Capacity == null ? "" : $" freeStagingCells={Capacity.FreeStagingCells} reachableStagingCells={Capacity.ReachableStagingCells}"
                + $" freeAttackCells={Capacity.FreeAttackCells} reachableAttackCells={Capacity.ReachableAttackCells}";
            return $"tick={FinalTick} phase={FinalPhase} spawned={Spawned} lastSpawn={LastSpawnTick} moved={Moved} "
                + $"near={NearBase}/{Living} everNear={Approached} hit={Hit} firstDamage={FirstDamageTick} lastDamage={LastDamageTick} damage={Damage} "
                + $"peakAttackers={PeakAttackers} attackSectors={AttackSectors} staged={Staged} stagingSectors={StagingSectors} "
                + $"admitted={Admitted} settled={Settled} localManeuvers={ActiveManeuvers} "
                + $"overlapMm={PeakOverlap} interpolatedOverlapMm={PeakInterpolatedOverlap} "
                + $"terrainErrors={InvalidTerrainSteps} attackPositionErrors={InvalidAttackPositions} towerErrors={InvalidTowerSteps} "
                + $"queueStopped={QueueStopped} queueReserved={QueueReserved} queueMoving={Living - Settled - QueueStopped - QueueReserved} "
                + $"allSettledTick={AllSettledTick} "
                + $"oscillation={LongestOscillation} "
                + $"leaks={Leaks} kills={Kills} refillTicks={RefillTicks} cancellations={ConstructionCancellations} "
                + $"attackVacancyYield={AttackVacancyYield} canceledManeuvers={CanceledManeuvers} pauseChecks={PauseChecks} hash={StateHash:X16}"
                + capacity;
        }
    }

    private sealed class Motion
    {
        public Position2 Position;
        public int LastDx;
        public int LastDy;
        public int Radius;
        public int Reversals;
        public long LastMove;
        public long LastStaged;
    }

    private sealed class VacancyCheck
    {
        public Position2 Position;
        public int Radius;
        public int OccupantId;
        public long OccupantDistance;
        public bool Hit;
    }

    private static NavigationReport ObserveNavigation(NavigationCase test, FightConfiguration config, bool timing, bool details = false, bool nearSpace = false)
    {
        var session = new FightSession(config, test.Seed);
        if (!session.StartNextWave())
        {
            throw new InvalidOperationException("Could not start navigation scenario " + test.Name);
        }

        var navigation = config.AssaultNavigation;
        var objective = navigation?.BaseCenter ?? config.Routes[0].Points[^1];
        var routeStarts = config.Routes.ToDictionary(route => route.Id, route => route.Points[0]);
        var moved = new HashSet<int>();
        var approached = new HashSet<int>();
        var hit = new HashSet<int>();
        var motion = new Dictionary<int, Motion>();
        if (test.BlockedPocketStress)
        {
            session.FixtureArrangeFormation((1, new Position2(1280, 0), true), (3, new Position2(2880, 0), false), (1, new Position2(3840, 0), false));
            foreach (var enemy in session.Enemies)
            {
                motion.Add(enemy.Id, new Motion
                {
                    Position = enemy.Position,
                    Radius = FightSession.EnemyNavigationRadiusMillimetres(enemy.DefinitionId)
                });
            }
        }

        var targets = test.TargetedVacancies ?? Array.Empty<Position2>();
        var report = new NavigationReport { ExpectedRefills = Math.Max(1, targets.Length) };
        int expectedUnits = config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));
        var stepDurations = timing ? new List<long>(test.DurationTicks) : null;
        int initialHealth = session.BaseHp;
        int previousHealth = initialHealth;
        HashSet<int> attackersBeforeShot = null;
        long? killedAt = null;
        var vacancies = new List<VacancyCheck>(targets.Length);
        var newAttackersHit = new HashSet<int>();
        for (int tick = 0; tick < test.DurationTicks && session.Phase == BattlePhase.Fighting; tick++)
        {
            if (test.PauseTick > 0 && session.Tick == test.PauseTick)
            {
                VerifyPausedState(session);
                report.PauseChecks++;
            }

            if (test.ConstructionStress && report.ConstructionCancellations == 0 && session.FixtureActiveManeuverCount >= 2)
            {
                CancelManeuversThroughConstruction(session, config, report);
            }

            if ((test.VacancyTick > 0 || targets.Length > 0) && session.Tick == test.VacancyTick)
            {
                attackersBeforeShot = session.Enemies.Where(enemy => enemy.AttackingBase).Select(enemy => enemy.Id).ToHashSet();
                Require(attackersBeforeShot.Count > 0, "the base was not under attack before the refill test");
                if (targets.Length == 0)
                {
                    Require(session.BuildTower(1, 1), "could not build the single-shot refill tower");
                }
            }

            for (int index = vacancies.Count; index < targets.Length && session.Tick == test.VacancyTick + index * test.VacancySpacingTicks; index++)
            {
                var target = targets[index];
                int? radius = session.FixtureRemoveAttacker(target);
                Require(radius.HasValue, "the targeted position did not hold an attacker");
                vacancies.Add(new VacancyCheck { Position = target, Radius = radius.Value });
                killedAt ??= session.Tick;
                report.KilledAttacker = true;
            }

            if (test.BlockedPocketStress && killedAt == session.Tick)
            {
                var reserves = session.Enemies.Where(enemy => enemy.IsStaged).ToArray();
                var frozen = CapacityProbe.Measure(navigation, config, session.Enemies, reserves);
                Require(frozen.FreeAttackCells > 0 && frozen.ReachableAttackCells == 0, "the test vacancy was not blocked by the staged reserve");
            }

            if (stepDurations == null)
            {
                session.Step();
            }
            else
            {
                long start = Stopwatch.GetTimestamp();
                session.Step();
                stepDurations.Add(Stopwatch.GetTimestamp() - start);
            }
            if (session.BaseHp < previousHealth)
            {
                if (report.FirstDamageTick == 0)
                {
                    report.FirstDamageTick = session.Tick;
                }

                report.LastDamageTick = session.Tick;
            }

            previousHealth = session.BaseHp;

            if (attackersBeforeShot != null && killedAt == null && session.Kills == 1)
            {
                killedAt = session.Tick;
                report.KilledAttacker = attackersBeforeShot.Except(session.Enemies.Select(enemy => enemy.Id)).Any();
            }

            report.AttackVacancyYield |= session.FixtureAttackVacancyYieldActive;

            Hash((int)session.Phase);
            Hash(session.BaseHp);
            Hash(session.Leaks);
            Hash(session.Kills);
            Hash(session.Enemies.Count);
            int attackers = 0;
            int staged = 0;
            int nearBase = 0;
            uint sectors = 0;
            uint stagingSectors = 0;
            bool checkRefills = killedAt.HasValue && report.RefillTicks == 0;
            if (checkRefills)
            {
                foreach (var vacancy in vacancies)
                {
                    vacancy.OccupantId = 0;
                    vacancy.OccupantDistance = long.MaxValue;
                }
            }

            foreach (var enemy in session.Enemies)
            {
                Hash(enemy.Id);
                Hash(enemy.Position.X);
                Hash(enemy.Position.Y);
                Hash(enemy.AttackingBase ? 1 : 0);
                Hash(enemy.IsStaged ? 1 : 0);
                Hash((int)enemy.NextBaseAttackTick);
                if (!motion.TryGetValue(enemy.Id, out var movement))
                {
                    report.LastSpawnTick = session.Tick;
                    movement = new Motion
                    {
                        Position = routeStarts[enemy.RouteId],
                        LastMove = session.Tick,
                        Radius = FightSession.EnemyNavigationRadiusMillimetres(enemy.DefinitionId)
                    };
                    motion.Add(enemy.Id, movement);
                }

                report.FurthestEast = Math.Max(report.FurthestEast, enemy.Position.X);
                if (!enemy.Position.Equals(routeStarts[enemy.RouteId]))
                {
                    moved.Add(enemy.Id);
                }

                if (navigation != null && !navigation.CanSweep(movement.Position, enemy.Position, movement.Radius))
                {
                    report.InvalidTerrainSteps++;
                }

                UpdateMotion(movement, enemy.Position, session.Tick, report);

                int dx = enemy.Position.X - objective.X;
                int dy = enemy.Position.Y - objective.Y;
                if ((long)dx * dx + (long)dy * dy <= 4500L * 4500)
                {
                    approached.Add(enemy.Id);
                    nearBase++;
                }

                if (enemy.IsStaged)
                {
                    movement.LastStaged = session.Tick;
                    staged++;
                    stagingSectors |= Sector(dx, dy);
                }

                if (enemy.AttackingBase)
                {
                    attackers++;
                    if (navigation != null && !navigation.CanReachBase(enemy.Position, movement.Radius + FightSession.BaseMeleeReachMillimetres))
                    {
                        report.InvalidAttackPositions++;
                    }

                    sectors |= Sector(dx, dy);
                    bool landedHit = enemy.NextBaseAttackTick == session.Tick + config.Objective.BaseAttackIntervalTicks;
                    if (landedHit)
                    {
                        hit.Add(enemy.Id);
                    }

                    if (checkRefills)
                    {
                        if (landedHit && !attackersBeforeShot.Contains(enemy.Id))
                        {
                            newAttackersHit.Add(enemy.Id);
                        }

                        foreach (var vacancy in vacancies)
                        {
                            long clearance = vacancy.Radius + movement.Radius;
                            long distance = AssaultNavigationMap.DistanceSquared(enemy.Position, vacancy.Position);
                            if (distance >= clearance * clearance)
                            {
                                continue;
                            }

                            if (distance < vacancy.OccupantDistance)
                            {
                                vacancy.OccupantId = enemy.Id;
                                vacancy.OccupantDistance = distance;
                            }

                            vacancy.Hit |= landedHit;
                        }
                    }
                }
            }

            if (checkRefills && vacancies.Count == targets.Length && newAttackersHit.Count >= report.ExpectedRefills
                && (vacancies.Count == 0 || (attackers >= attackersBeforeShot.Count && vacancies.All(vacancy => vacancy.Hit && vacancy.OccupantId != 0)
                    && vacancies.Select(vacancy => vacancy.OccupantId).Distinct().Count() == vacancies.Count)))
            {
                report.RefillTicks = session.Tick - killedAt.Value;
            }

            report.NearBase = nearBase;
            report.Staged = staged;
            report.Settled = staged + attackers;
            report.StagingSectors = BitOperations.PopCount(stagingSectors);
            report.AttackSectors = Math.Max(report.AttackSectors, BitOperations.PopCount(sectors));
            report.PeakAttackers = Math.Max(report.PeakAttackers, attackers);
            if (report.AllSettledTick == 0 && report.Settled == expectedUnits)
            {
                report.AllSettledTick = session.Tick;
            }

            CheckUnitOverlap(session.Enemies, motion, report);
            CheckTowerSweeps(session.Enemies, session.Towers, motion, report);
        }

        report.Spawned = motion.Count;
        report.Moved = moved.Count;
        report.Approached = approached.Count;
        report.Hit = hit.Count;
        report.Living = session.Enemies.Count;
        report.Admitted = session.FixtureAdmittedCount;
        report.ActiveManeuvers = session.FixtureActiveManeuverCount;
        var unsettled = session.Enemies.Where(enemy => !enemy.AttackingBase && !enemy.IsStaged).ToArray();
        var reserved = unsettled.Where(enemy => session.FixtureHasBoundedManeuverWait(enemy.Id)).Select(enemy => enemy.Id).ToHashSet();
        var queued = unsettled.Where(enemy => !reserved.Contains(enemy.Id) && session.Tick - motion[enemy.Id].LastMove >= 40).ToArray();
        report.QueueReserved = reserved.Count;
        report.QueueStopped = queued.Length;
        if (navigation != null && queued.Length > 0 && session.Towers.Count == 0)
        {
            var heads = queued.GroupBy(enemy => enemy.RouteId).Select(route => route.First()).ToArray();
            report.Capacity = CapacityProbe.Measure(navigation, config, session.Enemies, heads);
        }

        report.Leaks = session.Leaks;
        report.Kills = session.Kills;
        report.Damage = initialHealth - session.BaseHp;
        report.FinalPhase = session.Phase;
        report.FinalTick = session.Tick;
        Console.WriteLine($"NAV {test.Name} seed={test.Seed} {report}");
        if (stepDurations is { Count: > 0 })
        {
            stepDurations.Sort();
            double micros = 1000000.0 / Stopwatch.Frequency;
            int p95 = (int)Math.Ceiling(0.95 * stepDurations.Count) - 1;
            int p99 = (int)Math.Ceiling(0.99 * stepDurations.Count) - 1;
            Console.WriteLine($"STEP {test.Name} p95={stepDurations[p95] * micros:F1}us p99={stepDurations[p99] * micros:F1}us "
                + $"max={stepDurations[^1] * micros:F1}us");
        }

        if (details)
        {
            PrintNavigationDetails(config, session, motion);
            foreach (var enemy in unsettled.Where(enemy => reserved.Contains(enemy.Id) || session.Tick - motion[enemy.Id].LastMove >= 40))
            {
                var movement = motion[enemy.Id];
                string status = reserved.Contains(enemy.Id) ? "RESERVED" : "STOPPED";
                Console.WriteLine($"{status} enemy={enemy.Id} at=({enemy.Position.X},{enemy.Position.Y}) lastMoved={movement.LastMove} "
                    + $"lastStaged={movement.LastStaged} "
                    + session.FixtureMovementDetails(enemy.Id));
            }
        }

        if (nearSpace && navigation != null && session.Enemies.Count > 0 && session.Towers.Count == 0)
        {
            var assembly = CapacityProbe.MeasureNearBase(navigation, session);
            report.NearSpace = assembly;
            var adjacent = CapacityProbe.MeasureAdjacentBand(navigation, session);
            report.AdjacentBand = adjacent;
            Console.WriteLine($"NEAR {test.Name} at3m={assembly.WithinThreeMetres} at4.5m={report.NearBase} at6m={assembly.WithinSixMetres} "
                + $"at9m={assembly.WithinNineMetres} furthestMm={assembly.FurthestMillimetres} distantReserves={assembly.DistantReserves} "
                + $"nearFreeCells={assembly.NearFreeCells} nearReachableCells={assembly.NearReachableCells} "
                + $"nearLegalStagingCells={assembly.NearLegalStagingCells} nearReachableStagingCells={assembly.NearReachableStagingCells} "
                + $"nearMonotonicStagingCells={assembly.NearMonotonicStagingCells} reservesWithMonotonicSpace={assembly.ReservesWithMonotonicSpace}");
            Console.WriteLine($"ADJACENT {test.Name} reserves={adjacent.Reserves} sectors={adjacent.OccupiedSectors}/{adjacent.AvailableSectors} "
                + $"flanks={adjacent.OccupiedFlanks} longestEmptyRun={adjacent.LongestEmptyRun}");
            if (details)
            {
                foreach (var move in assembly.Examples)
                {
                    Console.WriteLine($"NEAR MOVE enemy={move.EnemyId} radius={move.Radius} from=({move.From.X},{move.From.Y}) to=({move.To.X},{move.To.Y})");
                }
            }
        }

        return report;

        void Hash(int value)
        {
            report.StateHash = (report.StateHash ^ (uint)value) * 1099511628211UL;
        }
    }

    internal static uint Sector(int dx, int dy)
    {
        if (dx == 0 && dy == 0)
        {
            return 0;
        }

        double octant = Math.Atan2(dy, dx) * 4 / Math.PI;
        return 1u << (((int)Math.Floor(octant + 0.5) + 8) % 8);
    }

    private static void VerifyPausedState(FightSession session)
    {
        Require(session.FixtureActiveManeuverCount >= 2, "the pause test did not interrupt concurrent local maneuvers");
        string before = State();
        session.SetPaused(true);
        session.Step(127);
        session.Step(300);
        Require(session.IsPaused && before == State(), "the simulation changed while paused");
        session.SetPaused(false);
        Require(before == State(), "resuming changed the state before the next simulation tick");

        string State()
        {
            return $"{session.Tick}/{session.Phase}/{session.WaveIndex}/{session.BaseHp}/{session.Gold}/{session.Kills}/{session.Leaks}/"
                + string.Join("|", session.Enemies.Select(enemy => $"{enemy.Id},{enemy.Position.X},{enemy.Position.Y},"
                    + $"{enemy.PreviousPosition.X},{enemy.PreviousPosition.Y},{enemy.Health},{enemy.NextBaseAttackTick},"
                    + $"{enemy.IsStaged},{enemy.AttackingBase}"));
        }
    }

    private static void CancelManeuversThroughConstruction(FightSession session, FightConfiguration config, NavigationReport report)
    {
        var slot = config.BuildSlots.FirstOrDefault(candidate => session.CanBuildTower(candidate.Id, 1)
            && session.FixtureConstructionBlocksReservedPath(candidate.Position));
        if (slot == null)
        {
            return;
        }

        var positions = session.Enemies.ToDictionary(enemy => enemy.Id, enemy => enemy.Position);
        int canceled = session.FixtureActiveManeuverCount;
        Require(session.BuildTower(slot.Id, 1), "the mid-maneuver tower construction was rejected");
        Require(session.FixtureActiveManeuverCount == 0, "construction retained trajectories whose timing depended on a stopped NPC");
        Require(session.Enemies.All(enemy => positions[enemy.Id].Equals(enemy.Position)), "canceling a reserved maneuver teleported an NPC");
        report.ConstructionCancellations++;
        report.CanceledManeuvers = canceled;
    }

    private static void CheckTowerSweeps(
        IReadOnlyList<EnemySnapshot> enemies,
        IReadOnlyList<TowerSnapshot> towers,
        Dictionary<int, Motion> motion,
        NavigationReport report)
    {
        foreach (var tower in towers)
        {
            foreach (var enemy in enemies)
            {
                int clearance = FightSession.TowerFoundationRadiusMillimetres + motion[enemy.Id].Radius;
                long dx = (long)enemy.Position.X - enemy.PreviousPosition.X;
                long dy = (long)enemy.Position.Y - enemy.PreviousPosition.Y;
                long x = (long)tower.Position.X - enemy.PreviousPosition.X;
                long y = (long)tower.Position.Y - enemy.PreviousPosition.Y;
                long lengthSquared = dx * dx + dy * dy;
                double fraction = lengthSquared == 0 ? 0 : Math.Clamp((double)(x * dx + y * dy) / lengthSquared, 0, 1);
                double offsetX = x - fraction * dx;
                double offsetY = y - fraction * dy;
                if (offsetX * offsetX + offsetY * offsetY < (long)clearance * clearance)
                {
                    report.InvalidTowerSteps++;
                }
            }
        }
    }

    private static void PrintNavigationDetails(FightConfiguration config, FightSession session, Dictionary<int, Motion> motion)
    {
        Console.WriteLine($"Map: {config.StageName}, objective: {config.Objective.Type}, HP: {session.BaseHp}/{session.BaseMaxHp}");
        foreach (var route in config.Routes)
        {
            Console.WriteLine($"Route {route.Id}: " + string.Join(" ", route.Points.Select(point => $"({point.X},{point.Y})")));
        }

        Console.WriteLine("enemy,definition,route,x,y,lastMoved,attacking,staged");
        foreach (var enemy in session.Enemies)
        {
            Console.WriteLine($"{enemy.Id},{enemy.DefinitionId},{enemy.RouteId},{enemy.Position.X},{enemy.Position.Y},"
                + $"{motion[enemy.Id].LastMove},{enemy.AttackingBase},{enemy.IsStaged}");
        }
    }

    private static void CheckUnitOverlap(IReadOnlyList<EnemySnapshot> enemies, Dictionary<int, Motion> motion, NavigationReport report)
    {
        for (int index = 0; index < enemies.Count; index++)
        {
            var first = enemies[index];
            int firstRadius = motion[first.Id].Radius;
            for (int previous = 0; previous < index; previous++)
            {
                var second = enemies[previous];
                int clearance = firstRadius + motion[second.Id].Radius;
                long dx = (long)first.Position.X - second.Position.X;
                long dy = (long)first.Position.Y - second.Position.Y;
                long distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < (long)clearance * clearance)
                {
                    int overlap = clearance - (int)Math.Floor(Math.Sqrt(distanceSquared));
                    report.PeakOverlap = Math.Max(report.PeakOverlap, overlap);
                }

                long previousX = (long)first.PreviousPosition.X - second.PreviousPosition.X;
                long previousY = (long)first.PreviousPosition.Y - second.PreviousPosition.Y;
                if (Math.Min(dx, previousX) >= clearance || Math.Max(dx, previousX) <= -clearance
                    || Math.Min(dy, previousY) >= clearance || Math.Max(dy, previousY) <= -clearance)
                {
                    continue;
                }

                long deltaX = dx - previousX;
                long deltaY = dy - previousY;
                long lengthSquared = deltaX * deltaX + deltaY * deltaY;
                double projection = lengthSquared == 0 ? 0 : Math.Clamp(-(double)(previousX * deltaX + previousY * deltaY) / lengthSquared, 0, 1);
                double x = previousX + projection * deltaX;
                double y = previousY + projection * deltaY;
                double closest = x * x + y * y;
                if (closest < (long)clearance * clearance)
                {
                    int overlap = clearance - (int)Math.Floor(Math.Sqrt(closest));
                    report.PeakInterpolatedOverlap = Math.Max(report.PeakInterpolatedOverlap, overlap);
                }
            }
        }
    }

    private static void UpdateMotion(Motion previous, Position2 position, long tick, NavigationReport report)
    {
        if (position.Equals(previous.Position))
        {
            return;
        }

        int dx = position.X - previous.Position.X;
        int dy = position.Y - previous.Position.Y;
        int netX = dx + previous.LastDx;
        int netY = dy + previous.LastDy;
        long direction = (long)dx * previous.LastDx + (long)dy * previous.LastDy;
        bool reversal = tick - previous.LastMove <= 4 && direction < 0 && (long)netX * netX + (long)netY * netY <= 80L * 80;
        previous.Reversals = reversal ? previous.Reversals + 1 : 0;
        report.LongestOscillation = Math.Max(report.LongestOscillation, previous.Reversals);
        previous.Position = position;
        previous.LastDx = dx;
        previous.LastDy = dy;
        previous.LastMove = tick;
    }
}
