using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.Camp;

namespace Game.HotUpdate.Client
{
    [Serializable]
    public sealed class LocalJourneyData
    {
        public int version = 2;
        public string nickname = "森林旅人";
        public int experience;
        public int highestWave;
        public int victories;
        public int enemiesKilled;
        public int towersBuilt;
        public int cardsChosen;
        public string lastRunId = "";
        public List<int> completedTasks = new List<int>();
        public LocalCampData camp;
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
        internal LocalCampData CampData => data.camp;
        public CampProgress Camp { get; }

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

            var loaded = store.Load();
            data = loaded ?? new LocalJourneyData();
            if ((data.version != 1 && data.version != 2) || string.IsNullOrWhiteSpace(data.nickname) || data.experience < 0 || data.highestWave < 0
                || data.victories < 0 || data.enemiesKilled < 0 || data.towersBuilt < 0 || data.cardsChosen < 0 || data.completedTasks == null)
            {
                throw new InvalidOperationException("The local journey profile is invalid; the original file was preserved.");
            }

            Camp = new CampProgress(tables, this);
            if (data.version == 1 || loaded == null)
            {
                // 旧档案保留战绩；新档案与迁移只领取一次初始营地材料。
                data = Copy(data);
                data.camp = Camp.CreateInitial();
            }

            Camp.Validate(data.camp);
            data.version = 2;
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
                lastRunId = battle.RunId,
                camp = Camp.BattleLoot(battle.Victory)
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

        internal void SaveCamp(LocalCampData camp, int experience = 0)
        {
            Camp.Validate(camp);
            var next = Copy(data);
            next.camp = camp;
            next.experience = checked(next.experience + experience);
            store.Save(next);
            data = next;
        }

        private static LocalJourneyData Copy(LocalJourneyData source)
        {
            return new LocalJourneyData
            {
                nickname = source.nickname,
                experience = source.experience,
                highestWave = source.highestWave,
                victories = source.victories,
                enemiesKilled = source.enemiesKilled,
                towersBuilt = source.towersBuilt,
                cardsChosen = source.cardsChosen,
                lastRunId = source.lastRunId,
                completedTasks = new List<int>(source.completedTasks),
                camp = source.camp?.Copy()
            };
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
