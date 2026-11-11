using System;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;

internal static class Program
{
    private static int passed;

    private static void Main()
    {
        Check("same seed and commands produce the same result across tick batching", () =>
        {
            Equal(Run(1), Run(17));
            Equal(Run(17), Run(53));
        });
        Check("construction and upgrades reject invalid commands without spending currency", () =>
        {
            var game = new FightSession(Tiny());
            True(!game.BuildTower(99, 1) && !game.BuildTower(1, 99));
            Equal(100, game.Gold);
            True(game.BuildTower(1, 1));
            Equal(50, game.Gold);
            True(!game.BuildTower(1, 1) && !game.UpgradeTower(99));
            Equal(50, game.Gold);
            True(game.UpgradeTower(1));
            Equal(25, game.Gold);
            Equal(2, game.Towers[0].Level);
            True(!game.UpgradeTower(1));
            Equal(25, game.Gold);
        });
        Check("maximum level cannot be exceeded and upgrade cost scales by current level", () =>
        {
            var config = Tiny();
            config.InitialGold = 200;
            var game = new FightSession(config);
            True(game.BuildTower(1, 1));
            True(game.UpgradeTower(1));
            True(game.UpgradeTower(1));
            Equal(75, game.Gold);
            True(!game.UpgradeTower(1));
            Equal(3, game.Towers[0].Level);
        });
        Check("draft offers exactly three unique cards and selection applies once", () =>
        {
            var game = new FightSession(Tiny(2));
            game.BuildTower(1, 1);
            game.StartNextWave();
            game.Step();
            Equal(BattlePhase.ChoosingCard, game.Phase);
            Equal(3, game.CurrentCardOffers.Select(card => card.Id).Distinct().Count());
            long tick = game.Tick;
            game.Step(100);
            Equal(tick, game.Tick);
            True(!game.BuildTower(2, 1) && !game.StartNextWave());
            int gold = game.Gold;
            True(!game.ChooseCard(-1) && !game.ChooseCard(3));
            int choice = game.CurrentCardOffers.ToList().FindIndex(card => card.Effect == CardEffect.Gold);
            game.SetPaused(true);
            True(!game.ChooseCard(choice));
            Equal(gold, game.Gold);
            game.SetPaused(false);
            True(game.ChooseCard(choice));
            Equal(gold + 10, game.Gold);
            True(!game.ChooseCard(choice));
            Equal(gold + 10, game.Gold);
            Equal(BattlePhase.Planning, game.Phase);
        });
        Check("kills and wave rewards settle once and final victory does not offer another draft", () =>
        {
            var game = new FightSession(Tiny());
            game.BuildTower(1, 1);
            True(game.StartNextWave());
            game.Step();
            Equal(BattlePhase.Victory, game.Phase);
            Equal(62, game.Gold);
            Equal(1, game.Kills);
            Equal(0, game.Leaks);
            Equal(0, game.CurrentCardOffers.Count);
            game.Step(100);
            True(!game.StartNextWave());
            Equal(62, game.Gold);
        });
        Check("an enemy at the objective damages the base once and defeat stops scheduled spawns", () =>
        {
            var config = Tiny();
            config.BaseHealth = 1;
            config.Routes[0].Points = new[] { new Position2(0, 0), new Position2(1000, 0), new Position2(0, 0) };
            config.Waves[0].Groups[0].Count = 2;
            var game = new FightSession(config);
            game.StartNextWave();
            game.Step();
            Equal(BattlePhase.Defeat, game.Phase);
            Equal(0, game.BaseHp);
            Equal(1, game.Leaks);
            Equal(100, game.Gold);
            game.Step(100);
            Equal(1, game.Leaks);
            Equal(0, game.Enemies.Count);
        });
        Check("attack cooldown is measured in ticks and does not tick while paused", () =>
        {
            var config = Tiny();
            config.Enemies[0].Health = 100;
            config.Towers[0].Damage = 7;
            config.Towers[0].AttackIntervalTicks = 3;
            var game = new FightSession(config);
            game.BuildTower(1, 1);
            game.StartNextWave();
            game.Step();
            Equal(93, game.Enemies[0].Health);
            game.SetPaused(true);
            game.Step(20);
            Equal(1L, game.Tick);
            Equal(93, game.Enemies[0].Health);
            game.SetPaused(false);
            game.Step(2);
            Equal(93, game.Enemies[0].Health);
            game.Step();
            Equal(86, game.Enemies[0].Health);
            Equal(2, game.Towers[0].ShotSequence);
        });
        Check("future spawn groups prevent premature wave completion", () =>
        {
            var config = Tiny();
            config.Waves[0].Groups[0].StartTick = 3;
            var game = new FightSession(config);
            game.StartNextWave();
            game.Step(3);
            Equal(BattlePhase.Fighting, game.Phase);
            Equal(0, game.Enemies.Count);
            game.Step();
            Equal(1, game.Enemies.Count);
        });
        Check("capped cards leave the pool while three repeatable cards keep drafts valid", () =>
        {
            var config = Tiny(3);
            config.Cards = config.Cards.Concat(new[]
            {
                new CardDefinition { Id = 4, Name = "Once", Effect = CardEffect.Gold, Value = 20, Weight = 1000000, MaxStacks = 1 }
            }).ToArray();
            var game = new FightSession(config);
            game.BuildTower(1, 1);
            game.StartNextWave();
            game.Step();
            int choice = game.CurrentCardOffers.ToList().FindIndex(card => card.Id == 4);
            True(choice >= 0 && game.ChooseCard(choice));
            game.StartNextWave();
            game.Step();
            Equal(3, game.CurrentCardOffers.Count);
            True(game.CurrentCardOffers.All(card => card.Id != 4));
        });
        Check("invalid authoring is rejected before simulation starts", () =>
        {
            var config = Tiny();
            config.Waves[0].Groups[0].RouteId = 99;
            Throws<ArgumentException>(() => new FightSession(config));
            config = Tiny();
            config.Routes[0].Points = new[] { new Position2(0, 0), new Position2(0, 0) };
            Throws<ArgumentException>(() => new FightSession(config));
            config = Tiny();
            config.Cards = config.Cards.Take(2).ToArray();
            Throws<ArgumentException>(() => new FightSession(config));
        });
        Console.WriteLine("PASS " + passed + " pure C# tower-defense scenarios; no Unity or server execution.");
    }

    private static string Run(int batch)
    {
        var config = Tiny(3);
        config.Enemies[0].Health = 5;
        config.Towers[0].Damage = 1;
        foreach (var wave in config.Waves)
        {
            wave.Groups[0].Count = 3;
            wave.Groups[0].IntervalTicks = 2;
        }

        var game = new FightSession(config, 419);
        game.BuildTower(1, 1);
        string drafts = "";
        for (int i = 0; i < 10000 && game.Phase != BattlePhase.Victory && game.Phase != BattlePhase.Defeat; i++)
        {
            if (game.Phase == BattlePhase.Planning)
            {
                game.StartNextWave();
            }
            else if (game.Phase == BattlePhase.ChoosingCard)
            {
                drafts += string.Join(",", game.CurrentCardOffers.Select(card => card.Id)) + ";";
                game.ChooseCard(0);
            }

            game.Step(batch);
        }

        Equal(BattlePhase.Victory, game.Phase);
        return string.Join(
            "|",
            game.Phase,
            game.Tick,
            game.Gold,
            game.BaseHp,
            game.Kills,
            game.Leaks,
            drafts,
            string.Join(",", game.Towers.Select(tower => tower.ShotSequence)),
            string.Join(",", game.ChosenCardStacks.OrderBy(pair => pair.Key).Select(pair => pair.Key + ":" + pair.Value)));
    }

    private static FightConfiguration Tiny(int waves = 1)
    {
        return new FightConfiguration
        {
            InitialGold = 100,
            BaseHealth = 10,
            Routes = new[] { new RouteDefinition { Id = 1, Points = new[] { new Position2(0, 0), new Position2(1000, 0) } } },
            BuildSlots = new[] { new BuildSlotDefinition { Id = 1, Position = new Position2(0, 0) }, new BuildSlotDefinition { Id = 2, Position = new Position2(500, 0) } },
            Towers = new[] { new TowerDefinition { Id = 1, Name = "Test", BuildCost = 50, UpgradeCost = 25, Damage = 10, Range = 2000, AttackIntervalTicks = 1, UpgradeDamage = 5, MaxLevel = 3 } },
            Enemies = new[] { new EnemyDefinition { Id = 1, Name = "Test", Health = 1, SpeedPerTick = 100, GoldReward = 5, BaseDamage = 1 } },
            Waves = Enumerable.Range(1, waves).Select(id => new WaveDefinition
            {
                Id = id, RewardGold = 7,
                Groups = new[] { new SpawnGroup { EnemyId = 1, RouteId = 1, Count = 1, StartTick = 0, IntervalTicks = 1 } }
            }).ToArray(),
            Cards = new[]
            {
                new CardDefinition { Id = 1, Name = "Gold", Effect = CardEffect.Gold, Value = 10 },
                new CardDefinition { Id = 2, Name = "Damage", Effect = CardEffect.DamagePercent, Value = 20 },
                new CardDefinition { Id = 3, Name = "Speed", Effect = CardEffect.AttackSpeedPercent, Value = 20 }
            }
        };
    }

    private static void Check(string name, Action test)
    {
        test();
        passed++;
        Console.WriteLine("PASS " + name);
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Assertion failed");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!Equals(expected, actual))
        {
            throw new InvalidOperationException("Expected " + expected + ", got " + actual);
        }
    }

    private static void Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new InvalidOperationException("Expected " + typeof(T).Name);
    }
}
