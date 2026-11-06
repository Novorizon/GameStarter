namespace Game.HotUpdate.TowerDefense.Domain
{
    /// <summary>Small offline tuning fixture. Production runs use generated table adapters.</summary>
    public static class DemoConfiguration
    {
        public static FightConfiguration Create()
        {
            return new FightConfiguration
            {
                InitialGold = 180,
                BaseHealth = 20,
                Routes = new[]
                {
                    new RouteDefinition
                    {
                        Id = 1,
                        Points = new[]
                        {
                            new Position2(-9000, -4000),
                            new Position2(-5000, -4000),
                            new Position2(-5000, 3000),
                            new Position2(1000, 3000),
                            new Position2(1000, -2000),
                            new Position2(8000, -2000)
                        }
                    }
                },
                BuildSlots = new[]
                {
                    Slot(1, -7000, -2500),
                    Slot(2, -3300, -1000),
                    Slot(3, -3200, 4700),
                    Slot(4, -1000, 1200),
                    Slot(5, 2700, 1600),
                    Slot(6, 3100, -3700),
                    Slot(7, 5800, -300),
                    Slot(8, 7100, -3800)
                },
                Towers = new[]
                {
                    new TowerDefinition
                    {
                        Id = 1, Name = "Arrow Tower", BuildCost = 60, UpgradeCost = 45,
                        Damage = 12, Range = 3100, AttackIntervalTicks = 10,
                        UpgradeDamage = 9, UpgradeRange = 150, MaxLevel = 3
                    },
                    new TowerDefinition
                    {
                        Id = 2, Name = "Spark Tower", BuildCost = 90, UpgradeCost = 65,
                        Damage = 23, Range = 2700, AttackIntervalTicks = 16,
                        UpgradeDamage = 17, UpgradeRange = 150, MaxLevel = 3
                    },
                    new TowerDefinition
                    {
                        Id = 3, Name = "Cannon Tower", BuildCost = 120, UpgradeCost = 80,
                        Damage = 42, Range = 4000, AttackIntervalTicks = 28,
                        UpgradeDamage = 28, UpgradeRange = 200, MaxLevel = 3
                    }
                },
                Enemies = new[]
                {
                    new EnemyDefinition { Id = 1, Name = "Slime", Health = 36, SpeedPerTick = 90, GoldReward = 9, BaseDamage = 1 },
                    new EnemyDefinition { Id = 2, Name = "Runner", Health = 24, SpeedPerTick = 150, GoldReward = 10, BaseDamage = 1 },
                    new EnemyDefinition { Id = 3, Name = "Brute", Health = 170, SpeedPerTick = 65, GoldReward = 25, BaseDamage = 3 }
                },
                Waves = new[]
                {
                    Wave(1, 25, Group(1, 6, 0, 12)),
                    Wave(2, 30, Group(1, 8, 0, 10), Group(2, 3, 30, 15)),
                    Wave(3, 35, Group(2, 10, 0, 9), Group(3, 2, 45, 45)),
                    Wave(4, 40, Group(1, 12, 0, 6), Group(3, 4, 25, 25)),
                    Wave(5, 60, Group(2, 12, 0, 7), Group(3, 7, 35, 18))
                },
                Cards = new[]
                {
                    Card(1, "Sharpened Arrows", "All towers deal +20% damage.", CardEffect.DamagePercent, 20),
                    Card(2, "Rapid Reload", "All towers attack 15% faster.", CardEffect.AttackSpeedPercent, 15),
                    Card(3, "Watchful Eyes", "All towers gain +15% range.", CardEffect.RangePercent, 15),
                    Card(4, "Emergency Funds", "Gain 70 gold immediately.", CardEffect.Gold, 70),
                    Card(5, "Base Repairs", "Recover 4 base HP, up to its maximum.", CardEffect.HealBase, 4),
                    Card(6, "Power Surge", "All towers deal +30% damage.", CardEffect.DamagePercent, 30)
                }
            };
        }

        private static BuildSlotDefinition Slot(int id, int x, int y)
        {
            return new BuildSlotDefinition { Id = id, Position = new Position2(x, y) };
        }

        private static SpawnGroup Group(int enemy, int count, int start, int interval)
        {
            return new SpawnGroup { EnemyId = enemy, RouteId = 1, Count = count, StartTick = start, IntervalTicks = interval };
        }

        private static WaveDefinition Wave(int id, int reward, params SpawnGroup[] groups)
        {
            return new WaveDefinition { Id = id, RewardGold = reward, GiveCard = true, Groups = groups };
        }

        private static CardDefinition Card(int id, string name, string description, CardEffect effect, int value)
        {
            return new CardDefinition { Id = id, Name = name, Description = description, Effect = effect, Value = value };
        }
    }
}
