using System;
using System.IO;
using Game.HotUpdate.Client;
using Game.HotUpdate.Camp;

internal static partial class Program
{
    private static void CampScenarios()
    {
        Check("legacy local profile migrates camp once while retaining player history", () =>
        {
            var legacy = new LocalJourneyData { version = 1, victories = 2, highestWave = 6, experience = 150 };
            var store = new JourneyStore { Data = legacy };
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            Equal(2, journey.Victories);
            Equal(150, journey.Experience);
            Equal(1, journey.Camp.Level.Id);
            Equal(40, journey.Camp.Wood);
            True(journey.Camp.CompleteArrival());
            Equal(1, legacy.version);
            True(legacy.camp == null);
            Equal(2, store.Data.version);
            var reloaded = new JourneyProgress(ReadTowerDefenseTables(), store);
            True(!reloaded.Camp.CompleteArrival());
            Equal(40, reloaded.Camp.Wood);
            Equal(2, reloaded.Victories);
        });
        Check("camp setup and two victories automatically build the island home", () =>
        {
            var store = new JourneyStore();
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            var camp = journey.Camp;
            True(!camp.Upgrade());
            True(!camp.Claim(1));
            True(camp.CompleteArrival());
            True(journey.TrySaveIdentity("  海风  ", TravelerAppearance.Shore));
            True(camp.Upgrade());
            Equal(2, camp.Level.Id);
            True(!camp.ExpeditionUnlocked && camp.CompleteOutpostBriefing() && camp.ExpeditionUnlocked);
            True(camp.Claim(1));
            True(camp.Claim(2));
            True(!camp.Upgrade());
            journey.Record(new JourneyBattle { RunId = "island-first", Victory = true, Wave = 6, Kills = 126, Towers = 3, Cards = 5 });
            Equal(3, camp.Level.Id);
            True(camp.Claim(3));
            True(camp.Claim(4));
            True(!camp.Upgrade());
            journey.Record(new JourneyBattle { RunId = "island-second", Victory = true, Wave = 6, Kills = 126, Towers = 3, Cards = 5 });
            Equal(4, camp.Level.Id);
            True(camp.NextLevel == null && !camp.Upgrade());
            var reloaded = new JourneyProgress(ReadTowerDefenseTables(), store);
            Equal(4, reloaded.Camp.Level.Id);
            Equal(camp.Wood, reloaded.Camp.Wood);
            Equal("海风", reloaded.Nickname);
            Equal(TravelerAppearance.Shore, reloaded.Appearance);
            True(reloaded.Camp.Claimed(4));
        });
        Check("camp task claims are gated and idempotent across reloads", () =>
        {
            var store = new JourneyStore();
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            True(!journey.Camp.Claim(999) && !journey.Camp.Claim(4));
            journey.Camp.CompleteArrival();
            True(journey.Camp.Claim(1));
            int wood = journey.Camp.Wood;
            int xp = journey.Experience;
            True(!journey.Camp.Claim(1));
            var reloaded = new JourneyProgress(ReadTowerDefenseTables(), store);
            True(!reloaded.Camp.Claim(1));
            Equal(wood, reloaded.Camp.Wood);
            Equal(xp, reloaded.Experience);
        });
        Check("failed construction save leaves materials and level unchanged and permits retry", () =>
        {
            var store = new JourneyStore();
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            journey.Camp.CompleteArrival();
            int wood = journey.Camp.Wood;
            store.Fail = true;
            Throws<IOException>(() => journey.Camp.Upgrade());
            Equal(1, journey.Camp.Level.Id);
            Equal(wood, journey.Camp.Wood);
            store.Fail = false;
            True(journey.Camp.Upgrade());
            Equal(2, journey.Camp.Level.Id);
        });
        Check("failed task save cannot grant partial materials or experience", () =>
        {
            var store = new JourneyStore();
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            journey.Camp.CompleteArrival();
            int wood = journey.Camp.Wood;
            store.Fail = true;
            Throws<IOException>(() => journey.Camp.Claim(1));
            True(!journey.Camp.Claimed(1));
            Equal(wood, journey.Camp.Wood);
            Equal(0, journey.Experience);
            store.Fail = false;
            True(journey.Camp.Claim(1));
            Equal(30, journey.Experience);
        });
        Check("battle statistics and camp supplies commit together and never repeat the same result", () =>
        {
            var store = new JourneyStore();
            var journey = new JourneyProgress(ReadTowerDefenseTables(), store);
            var report = new JourneyBattle { RunId = "atomic-island", Victory = true, Wave = 6, Kills = 126 };
            int wood = journey.Camp.Wood;
            store.Fail = true;
            Throws<IOException>(() => journey.Record(report));
            Equal(0, journey.Victories);
            Equal(wood, journey.Camp.Wood);
            store.Fail = false;
            True(journey.Record(report));
            Equal(1, journey.Victories);
            Equal(wood + 35, journey.Camp.Wood);
            True(!journey.Record(report));
            Equal(wood + 35, journey.Camp.Wood);
        });
        Check("invalid version two camp data is preserved instead of resetting construction", () =>
        {
            var data = new LocalJourneyData { version = 2, camp = null };
            var store = new JourneyStore { Data = data };
            Throws<InvalidOperationException>(() => new JourneyProgress(ReadTowerDefenseTables(), store));
            Equal(0, store.Saves);
            data.camp = new LocalCampData { level = 99 };
            Throws<InvalidOperationException>(() => new JourneyProgress(ReadTowerDefenseTables(), store));
            Equal(99, data.camp.level);
        });
    }
}
