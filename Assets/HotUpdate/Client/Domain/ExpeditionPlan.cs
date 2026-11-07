using System;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;

namespace Game.HotUpdate.Client
{
    /// <summary>一次出征锁定关卡配置和种子；加载失败重试仍使用这份计划。</summary>
    public sealed class ExpeditionPlan
    {
        public cfg.TdStage Stage { get; }
        public FightConfiguration Configuration { get; }
        public BattleBriefing Briefing { get; }
        public uint Seed { get; }

        public ExpeditionPlan(cfg.Tables tables, int stageId, uint seed)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            Stage = tables.TbTdStage.Get(stageId);
            Configuration = TowerDefenseTables.Create(tables, stageId);
            Briefing = new BattleBriefing(Configuration);
            Seed = seed;
        }
    }
}
