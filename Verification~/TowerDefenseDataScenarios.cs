using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;

internal static partial class Program
{
    private static void TowerDefenseDataScenarios()
    {
        Check("real Luban binaries decode all ten tables and map the nine business tables", () =>
        {
            var loaded = new List<string>();
            var tables = ReadTowerDefenseTables(loaded);
            Equal(10, loaded.Count);
            Equal(10, loaded.Distinct().Count());
            True(loaded.Contains("tbexample") && loaded.Contains("tbtdviewasset"));
            var configuration = TowerDefenseTables.Create(tables, 1);
            Equal(6, configuration.Waves.Length);
            Equal(7, configuration.BuildSlots.Length);
            Equal(3, configuration.Towers.Length);
            Equal(4, configuration.Enemies.Length);
            Equal(7, configuration.Cards.Length);
            Equal(1, configuration.Routes.Length);
            Equal(6, configuration.Routes[0].Points.Length);
            Equal(tables.TbTdStage.Get(1).InitialGold, configuration.InitialGold);
            Equal(tables.TbTdStage.Get(1).BaseHealth, configuration.BaseHealth);
            True(tables.TbTdViewAsset.DataList.Count > 0);
            True(tables.TbTdViewAsset.DataList.All(row => !string.IsNullOrWhiteSpace(row.Key) && !string.IsNullOrWhiteSpace(row.Asset)));
            // Constructing the session validates every adapted spawn, route and card reference.
            var session = new FightSession(configuration, 12345);
            Equal(BattlePhase.Planning, session.Phase);
            Equal(configuration.InitialGold, session.Gold);
        });
        Check("Luban stage filtering and sequence ordering preserve authored coordinates and spawns", () =>
        {
            var tables = ReadTowerDefenseTables();
            var configuration = TowerDefenseTables.Create(tables, 1);
            var authoredRoute = tables.TbTdRoutePoint.DataList.Where(row => row.StageId == 1)
                .OrderBy(row => row.Sequence).ToArray();
            for (int i = 0; i < authoredRoute.Length; i++)
            {
                Equal(authoredRoute[i].X, configuration.Routes[0].Points[i].X);
                Equal(authoredRoute[i].Y, configuration.Routes[0].Points[i].Y);
            }

            var authoredWaves = tables.TbTdWave.DataList.Where(row => row.StageId == 1).OrderBy(row => row.Sequence).ToArray();
            Equal(string.Join(",", authoredWaves.Select(row => row.Id)), string.Join(",", configuration.Waves.Select(row => row.Id)));
            foreach (var wave in configuration.Waves)
            {
                var groups = tables.TbTdSpawn.DataList.Where(row => row.WaveId == wave.Id).ToArray();
                Equal(groups.Length, wave.Groups.Length);
                Equal(groups.Sum(row => row.Count), wave.Groups.Sum(group => group.Count));
                Equal(string.Join(",", groups.Select(row => row.StartTick)), string.Join(",", wave.Groups.Select(group => group.StartTick)));
                Equal(string.Join(",", groups.Select(row => row.IntervalTicks)), string.Join(",", wave.Groups.Select(group => group.IntervalTicks)));
            }

            foreach (var card in configuration.Cards)
            {
                var row = tables.TbTdCard.Get(card.Id);
                Equal((int)card.Effect, row.Effect);
                Equal(row.Value, card.Value);
                Equal(row.MaxStacks, card.MaxStacks);
                Equal(row.Weight, card.Weight);
            }
        });
        Check("invalid stage and missing decoded table set cannot fall back to demo tuning", () =>
        {
            var tables = ReadTowerDefenseTables();
            Throws<KeyNotFoundException>(() => TowerDefenseTables.Create(tables, int.MaxValue));
            Throws<ArgumentNullException>(() => TowerDefenseTables.Create(null, 1));
        });
        Check("real table battles replay identical seed and commands with equivalent fixed-tick batching", () =>
        {
            var first = RunTowerDefenseDataBattle(1, 12345);
            var repeated = RunTowerDefenseDataBattle(1, 12345);
            var batched = RunTowerDefenseDataBattle(17, 12345);
            Equal(first.Commands, repeated.Commands);
            Equal(first.State, repeated.State);
            Equal(first.Commands, batched.Commands);
            Equal(first.State, batched.State);
        });
        Check("authored first stage is winnable with build-upgrade and damage-or-gold draft policy", () =>
        {
            var run = RunTowerDefenseDataBattle(10, 12345);
            var session = run.Session;
            Equal(BattlePhase.Victory, session.Phase);
            Equal(6, session.WaveIndex);
            True(session.BaseHp > 0 && session.Tick > 0);
            Equal(session.Configuration.Waves.Sum(wave => wave.Groups.Sum(group => group.Count)), session.Kills + session.Leaks);
            Equal(0, session.Enemies.Count);
            Equal(0, session.CurrentCardOffers.Count);
            True(session.Towers.Count > 0 && session.Towers.Any(tower => tower.Level > 1));
        });
    }

    private static cfg.Tables ReadTowerDefenseTables(List<string> loaded = null)
    {
        return new cfg.Tables(name =>
        {
            loaded?.Add(name);
            return new Luban.ByteBuf(File.ReadAllBytes(Game.GameDataPaths.DataOutput + "/" + name + ".bytes"));
        });
    }

    private sealed class TowerDefenseDataRun
    {
        internal FightSession Session;
        internal string Commands;
        internal string State;
    }

    private static TowerDefenseDataRun RunTowerDefenseDataBattle(int batchTicks, uint seed)
    {
        var session = new FightSession(TowerDefenseTables.Create(ReadTowerDefenseTables(), 1), seed);
        var commands = new List<string>();
        int decisions = 0;
        while (session.Phase != BattlePhase.Victory && session.Phase != BattlePhase.Defeat
            && session.Tick < 100000 && decisions++ < 100000)
        {
            if (session.Phase == BattlePhase.Planning)
            {
                foreach (var slot in session.Configuration.BuildSlots.OrderBy(value => value.Id))
                {
                    if (session.BuildTower(slot.Id, 1))
                    {
                        commands.Add("build:" + session.Tick + ":" + slot.Id);
                    }
                }

                foreach (var tower in session.Towers.OrderBy(value => value.Id).ToArray())
                {
                    while (session.UpgradeTower(tower.Id))
                    {
                        commands.Add("upgrade:" + session.Tick + ":" + tower.Id);
                    }
                }

                True(session.StartNextWave());
                commands.Add("wave:" + session.Tick + ":" + session.WaveIndex);
            }
            else if (session.Phase == BattlePhase.ChoosingCard)
            {
                var choices = session.CurrentCardOffers.Select((card, index) => new { Card = card, Index = index });
                var choice = choices.OrderByDescending(value => TowerDefenseCardPriority(value.Card))
                    .ThenByDescending(value => value.Card.Value).ThenBy(value => value.Card.Id).First();
                commands.Add("draft:" + session.Tick + ":" + string.Join(",", session.CurrentCardOffers.Select(card => card.Id)));
                True(session.ChooseCard(choice.Index));
                commands.Add("choose:" + session.Tick + ":" + choice.Card.Id);
            }

            session.Step(batchTicks);
        }

        if (session.Phase != BattlePhase.Victory)
        {
            throw new InvalidOperationException(
                "Authored first-stage policy failed: phase=" + session.Phase + ", wave=" + session.WaveIndex
                + ", tick=" + session.Tick + ", baseHP=" + session.BaseHp + ", gold=" + session.Gold
                + ", kills=" + session.Kills + ", leaks=" + session.Leaks + ", seed=" + seed);
        }

        return new TowerDefenseDataRun
        {
            Session = session,
            Commands = string.Join(";", commands),
            State = string.Join(
                "|",
                session.Phase,
                session.Tick,
                session.Gold,
                session.BaseHp,
                session.Kills,
                session.Leaks,
                string.Join(",", session.Towers.Select(tower => tower.Id + ":" + tower.Level + ":" + tower.Damage + ":" + tower.ShotSequence)),
                string.Join(",", session.ChosenCardStacks.OrderBy(pair => pair.Key).Select(pair => pair.Key + ":" + pair.Value)))
        };
    }

    private static int TowerDefenseCardPriority(CardDefinition card)
    {
        switch (card.Effect)
        {
            case CardEffect.DamagePercent:
                return 5;
            case CardEffect.Gold:
                return 4;
            case CardEffect.AttackSpeedPercent:
                return 3;
            case CardEffect.RangePercent:
                return 2;
            default:
                return 1;
        }
    }
}
