using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.HotUpdate.Client
{
    [Serializable]
    public sealed class LocalJourneyData
    {
        public int version = 1;
        public string nickname = "森林旅人";
        public int experience;
        public int highestWave;
        public int victories;
        public int enemiesKilled;
        public int towersBuilt;
        public int cardsChosen;
        public string lastRunId = "";
        public List<int> completedTasks = new List<int>();
    }

    public struct JourneyBattle
    {
        public string RunId;
        public bool Victory;
        public int Wave;
        public int Kills;
        public int Towers;
        public int Cards;
    }

    public interface ILocalJourneyStore
    {
        LocalJourneyData Load();
        void Save(LocalJourneyData value);
    }

    /// <summary>本地探索档案；不代表服务器账号、正式经验或经济余额。</summary>
    public sealed class JourneyProgress
    {
        private readonly ILocalJourneyStore store;
        private readonly cfg.PlayerLevel[] levels;
        private readonly cfg.JourneyTask[] tasks;
        private LocalJourneyData data;

        public string Nickname => data.nickname;
        public int Experience => data.experience;
        public int HighestWave => data.highestWave;
        public int Victories => data.victories;
        public int EnemiesKilled => data.enemiesKilled;
        public int TowersBuilt => data.towersBuilt;
        public int CardsChosen => data.cardsChosen;
        public IReadOnlyList<cfg.JourneyTask> Tasks => tasks;
        public cfg.PlayerLevel Level => levels.Last(value => value.Experience <= data.experience);
        public cfg.PlayerLevel NextLevel => levels.FirstOrDefault(value => value.Experience > data.experience);

        public JourneyProgress(cfg.Tables tables, ILocalJourneyStore store)
        {
            this.store = store ?? throw new ArgumentNullException(nameof(store));
            levels = tables.TbPlayerLevel.DataList.OrderBy(value => value.Experience).ToArray();
            tasks = tables.TbJourneyTask.DataList.ToArray();
            if (levels.Length == 0 || levels[0].Experience != 0
                || tasks.Any(task => task.Metric < 1 || task.Metric > 4 || task.Target <= 0 || task.Experience < 0))
            {
                throw new ArgumentException("Player level/task configuration is invalid.");
            }

            data = store.Load() ?? new LocalJourneyData();
            if (data.version != 1 || string.IsNullOrWhiteSpace(data.nickname) || data.experience < 0 || data.highestWave < 0
                || data.victories < 0 || data.enemiesKilled < 0 || data.towersBuilt < 0 || data.cardsChosen < 0 || data.completedTasks == null)
            {
                throw new InvalidOperationException("The local journey profile is invalid; the original file was preserved.");
            }
        }

        public int Progress(cfg.JourneyTask task)
        {
            return Math.Min(task.Target, Metric(data, task.Metric));
        }

        public bool Completed(cfg.JourneyTask task)
        {
            return data.completedTasks.Contains(task.Id);
        }

        public bool Record(JourneyBattle battle)
        {
            if (string.IsNullOrWhiteSpace(battle.RunId) || battle.Wave < 0 || battle.Kills < 0 || battle.Towers < 0 || battle.Cards < 0)
            {
                throw new ArgumentException("A valid local battle summary is required.", nameof(battle));
            }

            if (data.lastRunId == battle.RunId)
            {
                return false;
            }

            var next = new LocalJourneyData
            {
                nickname = data.nickname,
                experience = data.experience,
                highestWave = Math.Max(data.highestWave, battle.Wave),
                victories = checked(data.victories + (battle.Victory ? 1 : 0)),
                enemiesKilled = checked(data.enemiesKilled + battle.Kills),
                towersBuilt = checked(data.towersBuilt + battle.Towers),
                cardsChosen = checked(data.cardsChosen + battle.Cards),
                completedTasks = new List<int>(data.completedTasks),
                lastRunId = battle.RunId
            };
            foreach (var task in tasks)
            {
                if (!next.completedTasks.Contains(task.Id) && Metric(next, task.Metric) >= task.Target)
                {
                    next.completedTasks.Add(task.Id);
                    next.experience = checked(next.experience + task.Experience);
                }
            }

            // 保存成功后再发布，失败时保留原状态，同一摘要仍可重试。
            store.Save(next);
            data = next;
            return true;
        }

        private static int Metric(LocalJourneyData value, int metric)
        {
            switch (metric)
            {
                case 1:
                    return value.victories;
                case 2:
                    return value.towersBuilt;
                case 3:
                    return value.cardsChosen;
                case 4:
                    return value.enemiesKilled;
                default:
                    throw new ArgumentOutOfRangeException(nameof(metric));
            }
        }
    }
}
