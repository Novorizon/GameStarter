using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.Camp;

namespace Game.HotUpdate.Client
{
    public enum TravelerAppearance
    {
        Forest = 0,
        Shore = 1,
        Ember = 2
    }

    [Serializable]
    public sealed class LocalJourneyData
    {
        public int version = 2;
        public string nickname = "森林旅人";
        public TravelerAppearance appearance;
        public int experience;
        public int highestWave;
        public int victories;
        public int enemiesKilled;
        public int towersBuilt;
        public int cardsChosen;
        public string lastRunId = "";
        public List<int> completedTasks = new List<int>();
        public LocalCampData camp;
        public LocalCampaignData campaign;
    }

    public struct JourneyBattle
    {
        public string RunId;
        public bool Victory;
        public int CampLevelId;
        public int Wave;
        public int Kills;
        public int Towers;
        public int Cards;
        public int StageId;
        public bool RelayHeld;
    }

    public interface ILocalJourneyStore
    {
        LocalJourneyData Load();
        void Save(LocalJourneyData value);
    }

    /// <summary>本地探索档案；不代表服务器账号、正式经验或经济余额。</summary>
    public sealed class JourneyProgress
    {
        public const int MaxNicknameLength = 12;

        private readonly ILocalJourneyStore store;
        private readonly cfg.PlayerLevel[] levels;
        private readonly cfg.JourneyTask[] tasks;
        private LocalJourneyData data;
        internal LocalCampData CampData => data.camp;
        public CampProgress Camp { get; }

        public string Nickname => data.nickname;
        public TravelerAppearance Appearance => data.appearance;
        public int Experience => data.experience;
        public int HighestWave => data.highestWave;
        public int Victories => data.victories;
        public int EnemiesKilled => data.enemiesKilled;
        public int TowersBuilt => data.towersBuilt;
        public int CardsChosen => data.cardsChosen;
        public IReadOnlyList<cfg.JourneyTask> Tasks => tasks;
        public cfg.PlayerLevel Level => levels.Last(value => value.Experience <= data.experience);
        public cfg.PlayerLevel NextLevel => levels.FirstOrDefault(value => value.Experience > data.experience);
        public CampaignStep StoryStep => data.campaign.step;
        public CampaignEnding Ending => data.campaign.ending;
        public string KeeperName => data.campaign.keeperName;
        public bool RelayHeld => data.campaign.relayHeld;
        internal bool GuardingRelay => data.campaign.guardRelay;
        public int ClearedStages => StoryStep switch
        {
            CampaignStep.OutpostSupplies or CampaignStep.Outpost => 0,
            CampaignStep.OutpostReturn or CampaignStep.WaterPoint or CampaignStep.Sluice => 1,
            CampaignStep.SluiceReturn or CampaignStep.Ridge => 2,
            CampaignStep.RidgeReturn or CampaignStep.Lighthouse => 3,
            _ => 4
        };
        public int NextStageId => StoryStep switch
        {
            CampaignStep.Outpost => 1,
            CampaignStep.Sluice => 2,
            CampaignStep.Ridge => 3,
            CampaignStep.Lighthouse => 4,
            _ => 0
        };

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

            data.camp = Camp.Restore(data.camp, data.victories);
            // 旧版本只有林间哨站。曾通关的档案从首胜返营接续。
            if (data.campaign == null || data.campaign.format == 0)
            {
                data.campaign = new LocalCampaignData { format = 1, step = data.victories > 0 ? CampaignStep.OutpostReturn : CampaignStep.OutpostSupplies };
            }

            if (data.campaign.format != 1 || !Enum.IsDefined(typeof(CampaignStep), StoryStep) || !Enum.IsDefined(typeof(CampaignEnding), Ending))
            {
                throw new InvalidOperationException("The local campaign progress is invalid; the original file was preserved.");
            }

            data.version = 2;
        }

        internal LocalCampaignData ExpeditionChoices() => data.campaign.Copy();

        internal void AdvanceCampaign(CampaignStep expected, int choice)
        {
            if (StoryStep != expected || !Camp.ExpeditionUnlocked || choice < 0 || choice > 1)
            {
                throw new InvalidOperationException("The campaign action is no longer available.");
            }

            var next = Copy(data);
            var campaign = next.campaign;
            switch (expected)
            {
                case CampaignStep.OutpostSupplies:
                    campaign.repairKit = choice == 1;
                    campaign.step = CampaignStep.Outpost;
                    break;
                case CampaignStep.OutpostReturn:
                    campaign.bringWater = choice == 1;
                    campaign.step = CampaignStep.WaterPoint;
                    break;
                case CampaignStep.WaterPoint:
                    campaign.step = CampaignStep.Sluice;
                    break;
                case CampaignStep.SluiceReturn:
                    campaign.guardRelay = choice == 0;
                    campaign.step = CampaignStep.Ridge;
                    break;
                case CampaignStep.RidgeReturn:
                    campaign.step = CampaignStep.Lighthouse;
                    break;
                case CampaignStep.ReturnBell:
                    campaign.step = CampaignStep.SecondBell;
                    break;
                case CampaignStep.SecondBell:
                    campaign.step = CampaignStep.TidalGate;
                    break;
                case CampaignStep.TidalGate:
                    campaign.step = CampaignStep.EndingChoice;
                    break;
                case CampaignStep.EndingChoice:
                    campaign.ending = choice == 0 ? CampaignEnding.Keeper : CampaignEnding.Chart;
                    campaign.keeperName = choice == 0 ? Nickname : null;
                    campaign.step = CampaignStep.Epilogue;
                    break;
                case CampaignStep.Epilogue:
                    campaign.step = CampaignStep.Complete;
                    break;
                default:
                    throw new InvalidOperationException("This campaign step is advanced by a battle result.");
            }

            store.Save(next);
            data = next;
        }

        public int Progress(cfg.JourneyTask task)
        {
            return Math.Min(task.Target, Metric(data, task.Metric));
        }

        public bool Completed(cfg.JourneyTask task)
        {
            return data.completedTasks.Contains(task.Id);
        }

        public bool TrySaveIdentity(string nickname, TravelerAppearance appearance)
        {
            string name = nickname == data.nickname ? nickname : nickname?.Trim();
            if (name != data.nickname && (string.IsNullOrEmpty(name) || name.Length > MaxNicknameLength
                || name.Any(character => !char.IsLetterOrDigit(character) && character != ' ' && character != '-' && character != '_')))
            {
                return false;
            }

            if (name == data.nickname && appearance == data.appearance)
            {
                return true;
            }

            var next = Copy(data);
            next.nickname = name;
            next.appearance = appearance;
            store.Save(next);
            data = next;
            return true;
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

            int victories = checked(data.victories + (battle.Victory ? 1 : 0));
            var next = Copy(data);
            next.highestWave = Math.Max(data.highestWave, battle.Wave);
            next.victories = victories;
            next.enemiesKilled = checked(data.enemiesKilled + battle.Kills);
            next.towersBuilt = checked(data.towersBuilt + battle.Towers);
            next.cardsChosen = checked(data.cardsChosen + battle.Cards);
            next.lastRunId = battle.RunId;
            next.camp = Camp.BattleLoot(battle.Victory, battle.CampLevelId, victories);
            if (battle.Victory && battle.StageId != 0 && battle.StageId == NextStageId)
            {
                next.campaign.step = StoryStep switch
                {
                    CampaignStep.Outpost => CampaignStep.OutpostReturn,
                    CampaignStep.Sluice => CampaignStep.SluiceReturn,
                    CampaignStep.Ridge => CampaignStep.RidgeReturn,
                    CampaignStep.Lighthouse => CampaignStep.ReturnBell,
                    _ => throw new InvalidOperationException("Unexpected campaign battle.")
                };
                if (StoryStep == CampaignStep.Ridge)
                {
                    next.campaign.relayHeld = data.campaign.guardRelay && battle.RelayHeld;
                }
            }

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
                appearance = source.appearance,
                experience = source.experience,
                highestWave = source.highestWave,
                victories = source.victories,
                enemiesKilled = source.enemiesKilled,
                towersBuilt = source.towersBuilt,
                cardsChosen = source.cardsChosen,
                lastRunId = source.lastRunId,
                completedTasks = new List<int>(source.completedTasks),
                camp = source.camp?.Copy(),
                campaign = source.campaign?.Copy()
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
