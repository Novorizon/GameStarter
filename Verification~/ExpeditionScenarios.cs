using System;
using System.IO;
using System.Linq;
using Game.HotUpdate.Client;
using Game.HotUpdate.Camp;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;

internal static partial class Program
{
    private static void ExpeditionScenarios()
    {
        Check("expedition briefing uses the real stage and binds the accepted seed to the fight", () =>
        {
            var plan = new ExpeditionPlan(ReadTowerDefenseTables(), 1, 4729, 2);
            Equal("TowerDefense/Art/Map", plan.Stage.MapAsset);
            Equal(6, plan.Briefing.Waves.Count);
            Equal(126, plan.Briefing.EnemyCount);
            Equal(240, plan.Configuration.InitialGold);
            Equal(7, plan.Configuration.BuildSlots.Length);
            Equal(2, plan.CampLevel.Id);
            var fight = new FightSession(plan.Configuration, plan.Seed);
            Equal(4729u, fight.Seed);
            Equal(0, fight.WaveIndex);
        });
        Check("wave intelligence merges separate spawn groups of one enemy without losing counts", () =>
        {
            var config = TowerDefenseTables.Create(ReadTowerDefenseTables(), 1);
            int first = config.Enemies[0].Id;
            int second = config.Enemies[1].Id;
            config.Waves = new[]
            {
                new WaveDefinition
                {
                    Id = 1,
                    RewardGold = 25,
                    Groups = new[]
                    {
                        new SpawnGroup { EnemyId = first, Count = 3 },
                        new SpawnGroup { EnemyId = second, Count = 4 },
                        new SpawnGroup { EnemyId = first, Count = 2 }
                    }
                }
            };
            var briefing = new BattleBriefing(config);
            Equal(9, briefing.EnemyCount);
            Equal(2, briefing.Waves[0].Enemies.Count);
            Equal(5, briefing.Waves[0].Enemies.Single(enemy => enemy.Id == first).Count);
            Equal(25, briefing.Waves[0].RewardGold);
        });
        Check("final wave intelligence never promises an extra card draft", () =>
        {
            var config = TowerDefenseTables.Create(ReadTowerDefenseTables(), 1);
            config.Waves.Last().GiveCard = true;
            var briefing = new BattleBriefing(config);
            True(briefing.Waves[0].OffersCard);
            True(!briefing.Waves.Last().OffersCard);
        });
        Check("return confirmation cannot clear a previous manual pause even after repeated requests", () =>
        {
            var pause = new BattlePauseState();
            pause.SetManualPaused(true);
            pause.BeginReturn();
            pause.BeginReturn();
            pause.CancelReturn();
            True(pause.ManualPaused && pause.IsPaused && !pause.ReturnPending);
            pause.SetManualPaused(false);
            True(!pause.IsPaused);
        });
        Check("application pause and focus loss must both clear before simulation resumes", () =>
        {
            var pause = new BattlePauseState();
            pause.SetApplicationFocused(false);
            pause.SetApplicationPaused(true);
            pause.SetApplicationFocused(true);
            True(pause.IsPaused);
            pause.SetApplicationPaused(false);
            True(!pause.IsPaused);
            pause.SetApplicationPaused(true);
            pause.SetApplicationFocused(false);
            pause.SetApplicationPaused(false);
            True(pause.IsPaused);
            pause.SetApplicationFocused(true);
            True(!pause.IsPaused);
        });
        Check("manual continue cannot override an application or return-confirmation pause", () =>
        {
            var pause = new BattlePauseState();
            pause.BeginReturn();
            pause.SetManualPaused(false);
            True(pause.IsPaused);
            pause.SetApplicationPaused(true);
            pause.CancelReturn();
            True(pause.IsPaused);
            pause.SetApplicationPaused(false);
            True(!pause.IsPaused);
        });
        Check("a focus roundtrip preserves a manual pause and a pending return confirmation", () =>
        {
            var pause = new BattlePauseState();
            pause.SetManualPaused(true);
            pause.BeginReturn();
            pause.SetApplicationFocused(false);
            pause.SetApplicationFocused(true);
            True(pause.IsPaused && pause.ManualPaused && pause.ReturnPending);
            pause.CancelReturn();
            True(pause.IsPaused);
        });
        Check("restart clears user prompts while keeping the application suspension", () =>
        {
            var pause = new BattlePauseState();
            pause.SetManualPaused(true);
            pause.BeginReturn();
            pause.SetApplicationPaused(true);
            pause.ResetForNewRun();
            True(pause.IsPaused && !pause.ManualPaused && !pause.ReturnPending);
            pause.SetApplicationPaused(false);
            True(!pause.IsPaused);
        });
        Check("arrival save failure can retry without granting supplies or completing the story early", () =>
        {
            var store = new JourneyStore { Fail = true };
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            Throws<IOException>(() => journey.Camp.CompleteArrival());
            True(!journey.Camp.ArrivalSeen);
            Equal(40, journey.Camp.Wood);
            store.Fail = false;
            True(journey.Camp.CompleteArrival());
            True(!journey.Camp.CompleteArrival());
            Equal(40, new JourneyProgress(ReadTowerDefenseTables(), store).Camp.Wood);
            Equal(1, store.Saves);
        });
        Check("authored stage remains winnable across varied expedition seeds", () =>
        {
            foreach (uint seed in new uint[] { 0, 1, 33, 4729, 12345, 987654, 2147483648, uint.MaxValue })
            {
                var run = RunTowerDefenseDataBattle(10, seed);
                Equal(BattlePhase.Victory, run.Session.Phase);
                Equal(6, run.Session.WaveIndex);
            }
        });
        Check("retrying a battle after camp construction keeps the departure reward level", () =>
        {
            var store = new JourneyStore
            {
                Data = new LocalJourneyData
                {
                    victories = 1,
                    camp = new LocalCampData { format = 1, level = 2, wood = 100, stone = 100, arrivalSeen = true }
                }
            };
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            var battle = new JourneyBattle { RunId = "bound-reward", CampLevelId = 2, Victory = true, Wave = 6 };
            store.Fail = true;
            Throws<IOException>(() => journey.Record(battle));
            store.Fail = false;
            True(journey.Camp.Upgrade());
            Equal(3, journey.Camp.Level.Id);
            int wood = journey.Camp.Wood;
            True(journey.Record(battle));
            Equal(wood + 35, journey.Camp.Wood);
            True(!journey.Record(battle));
            True(journey.Record(new JourneyBattle { RunId = "bound-defeat", CampLevelId = 2, Wave = 2 }));
            Equal(wood + 35 + 8, journey.Camp.Wood);
        });
        Check("a result with an unknown departure reward level cannot publish partial progress", () =>
        {
            var store = new JourneyStore();
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            Throws<ArgumentOutOfRangeException>(() => journey.Record(new JourneyBattle { RunId = "bad-reward", CampLevelId = 99, Victory = true }));
            Equal(0, store.Saves);
            Equal(0, journey.Victories);
            Equal(40, journey.Camp.Wood);
        });
    }
}
