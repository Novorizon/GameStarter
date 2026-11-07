using System;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;

namespace Game.HotUpdate.TowerDefense.Data
{
    /// <summary>只在加载边界把 Luban 行转换为领域配置；战斗不依赖生成表或 Unity。</summary>
    public static class TowerDefenseTables
    {
        public static FightConfiguration Create(cfg.Tables tables, int stageId)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            var stage = tables.TbTdStage.Get(stageId);
            var points = tables.TbTdRoutePoint.DataList.Where(row => row.StageId == stageId).ToArray();
            var waves = tables.TbTdWave.DataList.Where(row => row.StageId == stageId).OrderBy(row => row.Sequence).ToArray();
            var waveIds = waves.Select(row => row.Id).ToHashSet();
            foreach (var spawn in tables.TbTdSpawn.DataList.Where(row => waveIds.Contains(row.WaveId)))
            {
                if (!points.Any(point => point.RouteId == spawn.RouteId) || !tables.TbTdEnemy.DataMap.ContainsKey(spawn.EnemyId))
                {
                    throw new InvalidOperationException("Invalid spawn route/enemy reference: " + spawn.Id);
                }
            }

            return new FightConfiguration
            {
                InitialGold = stage.InitialGold,
                BaseHealth = stage.BaseHealth,
                Routes = points.GroupBy(row => row.RouteId).Select(group => new RouteDefinition
                {
                    Id = group.Key,
                    Points = group.OrderBy(row => row.Sequence).Select(row => new Position2(row.X, row.Y)).ToArray()
                }).ToArray(),
                BuildSlots = tables.TbTdBuildSlot.DataList.Where(row => row.StageId == stageId).Select(row => new BuildSlotDefinition
                {
                    Id = row.Id,
                    Position = new Position2(row.X, row.Y)
                }).ToArray(),
                Towers = tables.TbTdTower.DataList.Select(row => new TowerDefinition
                {
                    Id = row.Id, Name = row.Name, BuildCost = row.BuildCost, UpgradeCost = row.UpgradeCost,
                    Damage = row.Damage, Range = row.Range, AttackIntervalTicks = row.AttackIntervalTicks,
                    MaxLevel = row.MaxLevel, UpgradeDamage = row.UpgradeDamage, UpgradeRange = row.UpgradeRange
                }).ToArray(),
                Enemies = tables.TbTdEnemy.DataList.Select(row => new EnemyDefinition
                {
                    Id = row.Id, Name = row.Name, Health = row.Health, SpeedPerTick = row.SpeedPerTick,
                    GoldReward = row.GoldReward, BaseDamage = row.BaseDamage
                }).ToArray(),
                Waves = waves.Select(row => new WaveDefinition
                {
                    Id = row.Id, RewardGold = row.RewardGold, GiveCard = row.GiveCard,
                    Groups = tables.TbTdSpawn.DataList.Where(spawn => spawn.WaveId == row.Id).Select(spawn => new SpawnGroup
                    {
                        EnemyId = spawn.EnemyId, RouteId = spawn.RouteId, Count = spawn.Count,
                        StartTick = spawn.StartTick, IntervalTicks = spawn.IntervalTicks
                    }).ToArray()
                }).ToArray(),
                Cards = tables.TbTdCard.DataList.Select(row => new CardDefinition
                {
                    Id = row.Id, Name = row.Name, Description = row.Description, Effect = (CardEffect)row.Effect,
                    Value = row.Value, Weight = row.Weight, MaxStacks = row.MaxStacks,
                    Rarity = row.Rarity, IconIndex = row.IconIndex
                }).ToArray()
            };
        }
    }
}
