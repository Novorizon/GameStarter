using System;
using System.IO;
using Game.HotUpdate.Client;

internal static partial class Program
{
    private sealed class JourneyStore : ILocalJourneyStore
    {
        public LocalJourneyData Data;
        public int Saves;
        public bool Fail;
        public LocalJourneyData Load() => Data;
        public void Save(LocalJourneyData value)
        {
            if (Fail)
            {
                throw new IOException("write failed");
            }

            Data = value;
            Saves++;
        }
    }

    private static void JourneyScenarios()
    {
        Check("local journey uses real task and level tables without a server wallet", () =>
        {
            var progress = new JourneyProgress(ReadTowerDefenseTables(), new JourneyStore());
            Equal(1, progress.Level.Id);
            Equal(0, progress.Experience);
            Equal(3, progress.Tasks.Count);
            True(progress.NextLevel.Experience > 0);
            foreach (var task in progress.Tasks)
            {
                Equal(0, progress.Progress(task));
                True(!progress.Completed(task));
            }
        });
        Check("a battle completes local goals once and advances the configured level", () =>
        {
            var store = new JourneyStore();
            var progress = new JourneyProgress(ReadTowerDefenseTables(), store);
            var summary = new JourneyBattle { RunId = "run-1", Victory = true, Wave = 6, Kills = 126, Towers = 4, Cards = 5 };
            True(progress.Record(summary));
            Equal(150, progress.Experience);
            Equal(2, progress.Level.Id);
            Equal(1, progress.Victories);
            Equal(6, progress.HighestWave);
            True(!progress.Record(summary));
            Equal(1, store.Saves);
            foreach (var task in progress.Tasks)
            {
                True(progress.Completed(task));
            }

            True(progress.Record(new JourneyBattle { RunId = "run-2", Wave = 2, Towers = 2 }));
            Equal(150, progress.Experience);
            Equal(1, progress.Victories);
            Equal(6, progress.HighestWave);
            Equal(6, progress.TowersBuilt);
            var restored = new JourneyProgress(ReadTowerDefenseTables(), store);
            Equal(150, restored.Experience);
            True(!restored.Record(new JourneyBattle { RunId = "run-2" }));
        });
        Check("failed local profile persistence publishes no partial counters and permits retry", () =>
        {
            var store = new JourneyStore { Fail = true };
            var progress = new JourneyProgress(ReadTowerDefenseTables(), store);
            var summary = new JourneyBattle { RunId = "retry", Victory = true, Wave = 6, Kills = 50, Towers = 4, Cards = 5 };
            Throws<IOException>(() => progress.Record(summary));
            Equal(0, progress.Victories);
            Equal(0, progress.Experience);
            store.Fail = false;
            True(progress.Record(summary));
            Equal(1, progress.Victories);
            Equal(150, progress.Experience);
        });
        Check("invalid local profiles and summaries are rejected without overwriting saved data", () =>
        {
            var store = new JourneyStore { Data = new LocalJourneyData { experience = -1 } };
            Throws<InvalidOperationException>(() => new JourneyProgress(ReadTowerDefenseTables(), store));
            Equal(-1, store.Data.experience);
            Equal(0, store.Saves);
            store.Data = null;
            var progress = new JourneyProgress(ReadTowerDefenseTables(), store);
            Throws<ArgumentException>(() => progress.Record(new JourneyBattle { RunId = "bad", Kills = -1 }));
            Equal(0, store.Saves);
        });
    }
}
