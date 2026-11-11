using System;
using Game.HotUpdate.TowerDefense.Domain;

namespace GameStarter.Verification.Navigation
{
    public static class LogicalMaps
    {
        private const int LightRadius = 300;
        private const int HeavyRadius = 480;

        public static FightConfiguration Create(string name)
        {
            return name switch
            {
                "compound-detour" => CompoundDetour(),
                "size-limited-flank" => Partition(false),
                "sealed-approach" => Partition(true),
                "crossing-entries" => CrossingEntries(),
                "wide-three-entries" => WideThreeEntries(),
                _ => throw new ArgumentException("Unknown logical navigation map " + name, nameof(name))
            };
        }

        private static FightConfiguration CompoundDetour()
        {
            var route = Route(1, new Position2(-6300, 0), new Position2(3400, 0));
            // The base is inside a three-sided compound. Its north and south walls leave no body-width passage against the base.
            Position2[][] walls =
            {
                Box(900, -1900, 1320, 2100),
                Box(1300, 1650, 4200, 2100),
                Box(1300, -1900, 4200, -1450)
            };
            var baseBounds = Base(new Position2(2600, -1200), new Position2(4200, 1200));
            return Configure("compound-detour", new Position2(-7100, -4500), new Position2(6600, 5000),
                Area(new Position2(-1450, -4150), new Position2(6100, 4550)), baseBounds, 3300,
                new[] { route }, walls, new Position2(4700, 0),
                Group(1, 2, 12, 1, 8), Group(1, 1, 20, 2, 4));
        }

        private static FightConfiguration Partition(bool sealedApproach)
        {
            var west = Route(1, new Position2(-6500, 0), new Position2(3400, 0));
            var east = Route(2, new Position2(8200, 0), new Position2(3400, 0));
            // The split wall has an 820mm opening. Light bodies are 600mm wide and heavy bodies are 960mm wide.
            Position2[][] walls = sealedApproach
                ? new[] { Box(1050, -4900, 1500, 4900) }
                : new[] { Box(1050, -4900, 1500, -410), Box(1050, 410, 1500, 4900) };
            var routes = sealedApproach ? new[] { west } : new[] { west, east };
            var groups = sealedApproach
                ? new[] { Group(1, 2, 12, 1, 5), Group(1, 1, 8, 2, 7) }
                : new[] { Group(1, 1, 20, 1, 4), Group(2, 2, 14, 1, 8) };
            return Configure(sealedApproach ? "sealed-approach" : "size-limited-flank", new Position2(-7300, -4900), new Position2(9050, 4900),
                Area(new Position2(-1250, -4350), new Position2(7300, 4350)), Base(new Position2(2600, -1200), new Position2(4200, 1200)),
                2900, routes, walls, new Position2(2100, 0), groups);
        }

        private static FightConfiguration CrossingEntries()
        {
            // The north route crosses the west route at (-2100, 0) but keeps its own southern approach.
            var west = Route(1, new Position2(-6500, 0), new Position2(3400, 0));
            var south = Route(2, new Position2(-2100, 4600), new Position2(-2100, -3500), new Position2(3400, -3500), new Position2(3400, 0));
            return Configure("crossing-entries", new Position2(-7300, -5350), new Position2(6550, 5500),
                Area(new Position2(-1200, -4650), new Position2(6150, 4300)), Base(new Position2(2600, -1200), new Position2(4200, 1200)),
                2500, new[] { west, south }, Array.Empty<Position2[]>(), new Position2(2100, 0),
                Group(1, 2, 10, 1, 8), Group(1, 1, 12, 2, 5), Group(2, 1, 13, 1, 5), Group(2, 2, 9, 4, 11));
        }

        private static FightConfiguration WideThreeEntries()
        {
            var center = new Position2(3700, 0);
            var west = Route(1, new Position2(-6500, 0), center);
            var north = Route(2, new Position2(3700, 6200), center);
            var east = Route(3, new Position2(9500, 0), center);
            Position2[][] terrain =
            {
                Box(-850, -3100, 950, -2350),
                Box(6350, 1850, 7700, 2700)
            };
            return Configure("wide-three-entries", new Position2(-7300, -5450), new Position2(10300, 7000),
                Area(new Position2(-1400, -4900), new Position2(8800, 5100)), Base(new Position2(1500, -1700), new Position2(5900, 1700)),
                3500, new[] { west, north, east }, terrain, new Position2(1000, 0),
                Group(1, 2, 8, 1, 8), Group(1, 1, 12, 2, 5), Group(2, 1, 14, 1, 5), Group(2, 2, 8, 4, 11), Group(3, 1, 20, 1, 7));
        }

        private static FightConfiguration Configure(
            string name,
            Position2 terrainMin,
            Position2 terrainMax,
            BaseApproachArea area,
            BaseAttackEngagement engagement,
            int inset,
            RouteDefinition[] routes,
            Position2[][] obstacles,
            Position2 legacyAttackPosition,
            params SpawnGroup[] groups)
        {
            var legacyPaths = new[] { new BaseAttackPath(0, 0, HeavyRadius, new[] { legacyAttackPosition }) };
            return new FightConfiguration
            {
                StageName = name,
                BaseHealth = 1000000,
                Terrain = new BattleTerrain(terrainMin, terrainMax, routes[0].Points, 415, obstacles),
                Objective = new BattleObjective(BattleObjectiveType.DefendBase, "Verification base", baseAttackIntervalTicks: 30,
                    assault: new AssaultRules(100, 6, 12), baseAttack: new BaseAttackLayout(inset, legacyPaths, engagement, area)),
                Routes = routes,
                Waves = new[] { new WaveDefinition { Id = 1, Groups = groups } },
                Enemies = new[]
                {
                    new EnemyDefinition { Id = 1, SpeedPerTick = 135, BodyRadius = LightRadius, Health = 100, BaseDamage = 1 },
                    new EnemyDefinition { Id = 2, SpeedPerTick = 65, BodyRadius = HeavyRadius, Health = 100, BaseDamage = 2 }
                },
                Cards = new[]
                {
                    new CardDefinition { Id = 1, Effect = CardEffect.DamagePercent, Value = 1, Weight = 1 },
                    new CardDefinition { Id = 2, Effect = CardEffect.RangePercent, Value = 1, Weight = 1 },
                    new CardDefinition { Id = 3, Effect = CardEffect.AttackSpeedPercent, Value = 1, Weight = 1 }
                }
            };
        }

        private static BaseApproachArea Area(Position2 min, Position2 max) => new BaseApproachArea(min, max, Array.Empty<BaseApproachObstacle>());

        private static BaseAttackEngagement Base(Position2 min, Position2 max) => new BaseAttackEngagement(min, max, 275, 5);

        private static RouteDefinition Route(int id, params Position2[] points) => new RouteDefinition { Id = id, Points = points };

        private static SpawnGroup Group(int route, int enemy, int count, int start, int interval)
            => new SpawnGroup { RouteId = route, EnemyId = enemy, Count = count, StartTick = start, IntervalTicks = interval };

        private static Position2[] Box(int left, int bottom, int right, int top)
            => new Position2[] { new(left, bottom), new(right, bottom), new(right, top), new(left, top) };
    }
}
