using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.Client;

namespace Game.HotUpdate.Camp
{
    [Serializable]
    public sealed class LocalCampData
    {
        public int format;
        public int level = 1;
        public int wood;
        public int stone;
        public bool arrivalSeen;
        public List<int> claimedTasks = new List<int>();

        internal LocalCampData Copy()
        {
            return new LocalCampData
            {
                format = format,
                level = level,
                wood = wood,
                stone = stone,
                arrivalSeen = arrivalSeen,
                claimedTasks = new List<int>(claimedTasks)
            };
        }
    }

    /// <summary>营地建设与任务规则；和旅程使用同一份本地档案，保存成功后才发布变化。</summary>
    public sealed class CampProgress
    {
        private readonly JourneyProgress owner;
        private readonly cfg.CampLevel[] levels;
        private readonly cfg.CampTask[] tasks;
        private LocalCampData State => owner.CampData;
        public cfg.CampLevel Level => levels[State.level - 1];
        public cfg.CampLevel NextLevel => State.level < levels.Length ? levels[State.level] : null;
        public IReadOnlyList<cfg.CampTask> Tasks => tasks;
        public int Wood => State.wood;
        public int Stone => State.stone;
        public bool ArrivalSeen => State.arrivalSeen;

        internal CampProgress(cfg.Tables tables, JourneyProgress owner)
        {
            this.owner = owner;
            levels = tables.TbCampLevel.DataList.OrderBy(value => value.Id).ToArray();
            tasks = tables.TbCampTask.DataList.OrderBy(value => value.Id).ToArray();
            if (levels.Length == 0 || levels.Where((value, index) => value.Id != index + 1).Any()
                || levels.Any(value => value.WoodCost < 0 || value.StoneCost < 0 || value.RequiredVictories < 0
                    || value.StartingWood < 0 || value.StartingStone < 0 || value.VictoryWood < 0 || value.VictoryStone < 0
                    || value.DefeatWood < 0 || value.DefeatStone < 0)
                || tasks.Any(value => value.Metric < 1 || value.Metric > 3 || value.Target <= 0 || value.Wood < 0 || value.Stone < 0 || value.Experience < 0))
            {
                throw new ArgumentException("Camp configuration is invalid.");
            }
        }

        internal LocalCampData CreateInitial()
        {
            return new LocalCampData { format = 1, wood = levels[0].StartingWood, stone = levels[0].StartingStone };
        }

        internal void Validate(LocalCampData value)
        {
            if (value == null || value.format != 1 || value.level < 1 || value.level > levels.Length || value.wood < 0 || value.stone < 0
                || value.claimedTasks == null || value.claimedTasks.Distinct().Count() != value.claimedTasks.Count
                || value.claimedTasks.Any(id => !tasks.Any(task => task.Id == id)))
            {
                throw new InvalidOperationException("The local camp profile is invalid; the original file was preserved.");
            }
        }

        public string UpgradeBlockedReason
        {
            get
            {
                var next = NextLevel;
                if (next == null)
                {
                    return "营地已建设完成";
                }

                if (!ArrivalSeen)
                {
                    return "先完成抵达海岛的故事";
                }

                if (owner.Victories < next.RequiredVictories)
                {
                    return "需要塔防胜利 " + next.RequiredVictories + " 次";
                }

                return Wood < next.WoodCost || Stone < next.StoneCost ? "材料不足，完成任务或前往塔防获取" : null;
            }
        }

        public bool Upgrade()
        {
            if (UpgradeBlockedReason != null)
            {
                return false;
            }

            var next = NextLevel;
            var changed = State.Copy();
            changed.level = next.Id;
            changed.wood -= next.WoodCost;
            changed.stone -= next.StoneCost;
            owner.SaveCamp(changed);
            return true;
        }

        public bool CompleteArrival()
        {
            if (ArrivalSeen)
            {
                return false;
            }

            var changed = State.Copy();
            changed.arrivalSeen = true;
            owner.SaveCamp(changed);
            return true;
        }

        public int Progress(cfg.CampTask task)
        {
            int value;
            switch (task.Metric)
            {
                case 1:
                    value = ArrivalSeen ? 1 : 0;
                    break;
                case 2:
                    value = State.level;
                    break;
                case 3:
                    value = owner.Victories;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(task));
            }

            return Math.Min(task.Target, value);
        }

        public bool Claimed(int taskId) => State.claimedTasks.Contains(taskId);

        public bool Claim(int taskId)
        {
            var task = tasks.FirstOrDefault(value => value.Id == taskId);
            if (task == null || Claimed(taskId) || Progress(task) < task.Target)
            {
                return false;
            }

            var changed = State.Copy();
            changed.claimedTasks.Add(taskId);
            changed.wood = checked(changed.wood + task.Wood);
            changed.stone = checked(changed.stone + task.Stone);
            owner.SaveCamp(changed, task.Experience);
            return true;
        }

        internal LocalCampData BattleLoot(bool victory, int campLevelId)
        {
            var level = campLevelId == 0 ? Level : levels.FirstOrDefault(value => value.Id == campLevelId);
            if (level == null)
            {
                throw new ArgumentOutOfRangeException(nameof(campLevelId));
            }

            var changed = State.Copy();
            changed.wood = checked(changed.wood + (victory ? level.VictoryWood : level.DefeatWood));
            changed.stone = checked(changed.stone + (victory ? level.VictoryStone : level.DefeatStone));
            return changed;
        }
    }
}
