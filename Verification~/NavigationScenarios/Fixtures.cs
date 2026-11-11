using System;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;

namespace GameStarter.Verification.Navigation
{
    internal enum ScenarioKind
    {
        Exit,
        Defeat,
        Saturation,
        Surround,
        Drain,
        Vacancies,
        SealedRoute,
        SealedApproach
    }

    internal sealed record Scenario(string Name, ScenarioKind Kind, FightConfiguration Config, int Limit, int MinimumRing = 0, bool BuildObstacle = false);

    internal static class Fixtures
    {
        private const int ExtendedBaseHealth = 1000000;

        internal static readonly string[] Names =
        {
            "forest-road", "forest-defeat", "forest-saturation", "forest-surround", "forest-vacancies", "forest-drain", "forest-rotate180", "forest-heavy-leading",
            "forest-hold-90", "forest-hold-180", "forest-hold-270", "forest-hold-heavy-leading", "crossing-hold", "wide-three-hold", "wide-three-delayed-hold",
            "bend-bridge", "sealed-bridge", "open-assault", "blocked-flank", "tower-flank", "two-entries", "merge-entries", "legacy-portal",
            "compound-detour", "size-limited-flank", "crossing-entries", "sealed-approach", "wide-three-entries", "wide-three-vacancies"
        };

        internal static Scenario Create(MapLoader maps, string name)
        {
            return name switch
            {
                "forest-road" => new Scenario(name, ScenarioKind.Exit, ExitAtBaseApproach(maps.Load(1)), 5000),
                "forest-defeat" => new Scenario(name, ScenarioKind.Defeat, DefeatFixture(maps.Load(1)), 3000),
                "forest-saturation" => Hold(name, maps.Load(1)),
                "forest-hold-90" => Hold(name, maps.Load(1, 1)),
                "forest-hold-180" => Hold(name, maps.Load(1, 2)),
                "forest-hold-270" => Hold(name, maps.Load(1, 3)),
                "forest-hold-heavy-leading" => Hold(name, HeavyLeading(maps.Load(1))),
                "crossing-hold" => Hold(name, LogicalMaps.Create("crossing-entries")),
                "wide-three-hold" => Hold(name, LogicalMaps.Create("wide-three-entries")),
                "wide-three-delayed-hold" => Hold(name, DelayedWest(LogicalMaps.Create("wide-three-entries"))) with { Limit = 5200 },
                "forest-surround" => new Scenario(name, ScenarioKind.Surround, Extended(maps.Load(1)), 1700, MinimumRing: 4),
                "forest-vacancies" => new Scenario(name, ScenarioKind.Vacancies, Extended(maps.Load(1)), 5000, MinimumRing: 4),
                "forest-drain" => new Scenario(name, ScenarioKind.Drain, Extended(maps.Load(1)), 5500, MinimumRing: 4),
                "forest-rotate180" => new Scenario(name, ScenarioKind.Drain, Extended(maps.Load(1, rotate: true)), 5500, MinimumRing: 4),
                "forest-heavy-leading" => new Scenario(name, ScenarioKind.Drain, HeavyLeading(maps.Load(1)), 5500, MinimumRing: 4),
                "bend-bridge" => new Scenario(name, ScenarioKind.Exit, ExitAtBaseApproach(Synthetic()), 1600),
                "sealed-bridge" => new Scenario(name, ScenarioKind.SealedRoute, ExitAtBaseApproach(Synthetic(sealedBridge: true)), 650),
                "open-assault" => new Scenario(name, ScenarioKind.Drain, Synthetic(), 2400, MinimumRing: 4),
                "blocked-flank" => new Scenario(name, ScenarioKind.Drain, Synthetic(blockedFlank: true), 2400, MinimumRing: 3),
                "tower-flank" => new Scenario(name, ScenarioKind.Drain, Synthetic(), 2400, MinimumRing: 3, BuildObstacle: true),
                "two-entries" => new Scenario(name, ScenarioKind.Drain, Synthetic(twoEntries: true), 2600, MinimumRing: 4),
                "merge-entries" => new Scenario(name, ScenarioKind.Drain, Synthetic(twoEntries: true, mergeEntries: true), 2600, MinimumRing: 4),
                "legacy-portal" => LegacyPortal(maps, name),
                "compound-detour" => new Scenario(name, ScenarioKind.Drain, LogicalMaps.Create(name), 3400, MinimumRing: 3),
                "size-limited-flank" => new Scenario(name, ScenarioKind.Drain, LogicalMaps.Create(name), 3000, MinimumRing: 4),
                "crossing-entries" => new Scenario(name, ScenarioKind.Drain, LogicalMaps.Create(name), 3200, MinimumRing: 4),
                "sealed-approach" => new Scenario(name, ScenarioKind.SealedApproach, LogicalMaps.Create(name), 900),
                "wide-three-entries" => new Scenario(name, ScenarioKind.Drain, LogicalMaps.Create(name), 3000, MinimumRing: 5),
                "wide-three-vacancies" => new Scenario(name, ScenarioKind.Vacancies, LogicalMaps.Create("wide-three-entries"), 5200, MinimumRing: 5),
                _ => throw new ArgumentException("Unknown scenario " + name)
            };
        }

        internal static int SpawnCount(FightConfiguration config) => config.Waves.Sum(wave => wave.Groups.Sum(group => group.Count));

        private static Scenario Hold(string name, FightConfiguration config)
            => new Scenario(name, ScenarioKind.Saturation, Extended(config), 3600, MinimumRing: 4);

        private static FightConfiguration DelayedWest(FightConfiguration config)
        {
            foreach (var group in config.Waves.SelectMany(wave => wave.Groups).Where(group => group.RouteId == 1))
            {
                group.StartTick += 600;
            }

            return config;
        }

        private static Scenario LegacyPortal(MapLoader maps, string name)
        {
            var config = Enumerable.Range(2, 7).Select(stage => maps.Load(stage))
                .First(map => map.Terrain == null && map.Objective.Type == BattleObjectiveType.InterceptEscapes);
            return new Scenario(name, ScenarioKind.Exit, config, 5000);
        }

        private static FightConfiguration Extended(FightConfiguration config)
        {
            // Isolate traffic capacity while retaining authored speeds, body radii, damage and spawn timing.
            config.BaseHealth = ExtendedBaseHealth;
            return config;
        }

        private static FightConfiguration DefeatFixture(FightConfiguration config)
        {
            config.BaseHealth = 20;
            return config;
        }

        private static FightConfiguration HeavyLeading(FightConfiguration config)
        {
            Extended(config);
            var largest = config.Enemies.Where(enemy => config.Waves.Any(wave => wave.Groups.Any(group => group.EnemyId == enemy.Id)))
                .OrderByDescending(enemy => enemy.BodyRadius).First();
            const int earlyCount = 4;
            var later = config.Waves.Skip(1).SelectMany(wave => wave.Groups).First(group => group.EnemyId == largest.Id && group.Count > earlyCount);
            later.Count -= earlyCount;
            var opening = config.Waves[0].Groups;
            foreach (var group in opening)
            {
                group.StartTick += 3;
            }

            config.Waves[0].Groups = opening.Prepend(new SpawnGroup
            {
                Count = earlyCount,
                EnemyId = largest.Id,
                RouteId = opening[0].RouteId,
                StartTick = 0,
                IntervalTicks = 9
            }).ToArray();
            return config;
        }

        private static FightConfiguration ExitAtBaseApproach(FightConfiguration config)
        {
            // Diagnose throughput before siege can intentionally fill the only way forward.
            var road = config.Routes.Single().Points;
            var from = road[^2];
            var to = road[^1];
            long x = to.X - from.X;
            long y = to.Y - from.Y;
            int length = (int)Math.Sqrt(x * x + y * y);
            int cutoff = length - config.Objective.BaseAttack.ApproachInset;
            if (cutoff <= 0)
            {
                throw new InvalidOperationException("The exit fixture expects the assault handoff on the final segment.");
            }

            road[^1] = new Position2(from.X + (int)(x * cutoff / length), from.Y + (int)(y * cutoff / length));
            config.Objective = new BattleObjective(BattleObjectiveType.InterceptEscapes, "Verification exit", escapesToDefeat: SpawnCount(config) + 1,
                assault: config.Objective.Assault);
            return config;
        }

        private static FightConfiguration Synthetic(bool blockedFlank = false, bool twoEntries = false, bool sealedBridge = false, bool mergeEntries = false)
        {
            Position2[] road = { new(-6500, -2600), new(-3100, -2600), new(-2800, -2300), new(-2800, -300), new(-2500, 0), new(3200, 0) };
            var routes = new[] { new RouteDefinition { Id = 1, Points = road, BaseEntranceInset = 1100 } };
            if (twoEntries)
            {
                Position2[] secondRoad = mergeEntries
                    ? new Position2[] { new(-2500, 4300), road[^2], road[^1] }
                    : new Position2[] { new(3200, 4300), road[^1] };
                routes = new[] { routes[0], new RouteDefinition { Id = 2, Points = secondRoad, BaseEntranceInset = 1100 } };
            }

            Position2[][] obstacles =
            {
                Rectangle(-5900, -4200, -3900, -3180),
                Rectangle(-5900, -2020, -3900, -1150)
            };
            if (blockedFlank)
            {
                obstacles = obstacles.Append(Rectangle(-500, 1580, 5300, 4000)).ToArray();
            }

            if (sealedBridge)
            {
                obstacles = obstacles.Append(Rectangle(-5200, -3190, -4800, -2000)).ToArray();
            }

            var engagement = new BaseAttackEngagement(new Position2(2400, -1150), new Position2(4000, 1150), 275, 5);
            var local = new BaseApproachArea(new Position2(-900, -4100), new Position2(5350, 4100), Array.Empty<BaseApproachObstacle>());
            BaseAttackPath[] fronts =
            {
                new(0, 1, 480, new Position2[] { new(1250, -560), new(1900, -560) }),
                new(0, 1, 480, new Position2[] { new(1250, 560), new(1900, 560) })
            };
            var groups = new[]
            {
                new SpawnGroup { EnemyId = 2, RouteId = 1, Count = 12, StartTick = 1, IntervalTicks = 6 },
                new SpawnGroup { EnemyId = 1, RouteId = 1, Count = 20, StartTick = 2, IntervalTicks = 3 }
            };
            if (twoEntries)
            {
                groups = groups.Append(new SpawnGroup { EnemyId = 1, RouteId = 2, Count = 16, StartTick = 1, IntervalTicks = 4 }).ToArray();
            }

            return new FightConfiguration
            {
                InitialGold = 10,
                BaseHealth = ExtendedBaseHealth,
                Terrain = new BattleTerrain(new Position2(-7400, -4900), new Position2(5650, 4900), road, 440, obstacles),
                Objective = new BattleObjective(BattleObjectiveType.DefendBase, "Verification base", baseAttackIntervalTicks: 30,
                    assault: AssaultRules.Default, baseAttack: new BaseAttackLayout(2500, fronts, engagement, local)),
                Routes = routes,
                BuildSlots = new[] { new BuildSlotDefinition { Id = 1, Position = new Position2(1450, -1490) } },
                Towers = new[] { new TowerDefinition { Id = 1, BuildCost = 1, UpgradeCost = 1, Damage = 1, Range = 1, AttackIntervalTicks = 1 } },
                Waves = new[] { new WaveDefinition { Id = 1, Groups = groups } },
                Enemies = new[]
                {
                    new EnemyDefinition { Id = 1, SpeedPerTick = 135, BodyRadius = 300, Health = 100, BaseDamage = 1 },
                    new EnemyDefinition { Id = 2, SpeedPerTick = 65, BodyRadius = 480, Health = 100, BaseDamage = 2 }
                },
                Cards = new[]
                {
                    new CardDefinition { Id = 1, Effect = CardEffect.DamagePercent, Value = 1, Weight = 1 },
                    new CardDefinition { Id = 2, Effect = CardEffect.RangePercent, Value = 1, Weight = 1 },
                    new CardDefinition { Id = 3, Effect = CardEffect.AttackSpeedPercent, Value = 1, Weight = 1 }
                }
            };
        }

        private static Position2[] Rectangle(int left, int bottom, int right, int top)
        {
            return new Position2[] { new(left, bottom), new(right, bottom), new(right, top), new(left, top) };
        }
    }
}
