using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.TowerDefense.Domain;

namespace Game.HotUpdate.TowerDefense.Data
{
    /// <summary>只在加载边界把 Luban 行转换为领域配置；战斗不依赖生成表或 Unity。</summary>
    public static class TowerDefenseTables
    {
        private const int BuildGridStepMillimetres = 350;
        private const int LegacyBuildGridStepMillimetres = 450;
        private const int WideBuildGridStepMillimetres = 700;
        private const int MaxBuildSlots = 512;
        private static readonly int[] PlacementGridSteps = { BuildGridStepMillimetres, LegacyBuildGridStepMillimetres, WideBuildGridStepMillimetres };

        public static FightConfiguration Create(cfg.Tables tables, int stageId)
        {
            if (tables == null)
            {
                throw new ArgumentNullException(nameof(tables));
            }

            var stage = tables.TbTdStage.Get(stageId);
            var points = tables.TbTdRoutePoint.DataList.Where(row => row.StageId == stageId).ToArray();
            var waves = tables.TbTdWave.DataList.Where(row => row.StageId == stageId).OrderBy(row => row.Sequence).ToArray();
            var slots = tables.TbTdBuildSlot.DataList.Where(row => row.StageId == stageId).Select(row => new BuildSlotDefinition
            {
                Id = row.Id,
                Position = new Position2(row.X, row.Y)
            }).ToArray();
            int buildGridStep = FitsBuildGrid(slots, WideBuildGridStepMillimetres) ? WideBuildGridStepMillimetres
                : FitsBuildGrid(slots, BuildGridStepMillimetres) ? BuildGridStepMillimetres
                : FitsBuildGrid(slots, LegacyBuildGridStepMillimetres) ? LegacyBuildGridStepMillimetres : 0;
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
                StageId = stageId,
                StageName = stage.Name,
                InitialGold = stage.InitialGold,
                BaseHealth = stage.BaseHealth,
                BuildGridStep = buildGridStep,
                Routes = points.GroupBy(row => row.RouteId).Select(group => new RouteDefinition
                {
                    Id = group.Key,
                    Points = group.OrderBy(row => row.Sequence).Select(row => new Position2(row.X, row.Y)).ToArray()
                }).ToArray(),
                BuildSlots = slots,
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

        private static bool FitsBuildGrid(BuildSlotDefinition[] slots, int step)
        {
            return slots.Length > 0 && slots.All(slot => slot.Position.X % step == 0 && slot.Position.Y % step == 0);
        }

        internal static void ApplyTowerPlacement(FightConfiguration configuration, TowerPlacementArea placement)
        {
            foreach (int step in PlacementGridSteps)
            {
                var slots = CreatePlacementSlots(placement, configuration.Routes, step);
                if (slots == null || slots.Length == 0)
                {
                    continue;
                }

                configuration.BuildSlots = slots;
                configuration.BuildGridStep = step;
                return;
            }

            throw new InvalidOperationException("The tower placement area could not produce a supported build grid for stage " + configuration.StageId);
        }

        private static BuildSlotDefinition[] CreatePlacementSlots(TowerPlacementArea placement, RouteDefinition[] routes, int step)
        {
            var slots = new List<BuildSlotDefinition>();
            long firstX = (long)Math.Ceiling(placement.Min.X / (double)step) * step;
            long firstY = (long)Math.Ceiling(placement.Min.Y / (double)step) * step;
            int radius = FightSession.TowerFoundationRadiusMillimetres;
            for (long y = firstY; y <= placement.Max.Y; y += step)
            {
                for (long x = firstX; x <= placement.Max.X; x += step)
                {
                    var position = new Position2((int)x, (int)y);
                    if (!placement.CanBuild(position, radius, routes))
                    {
                        continue;
                    }

                    if (slots.Count == MaxBuildSlots)
                    {
                        return null;
                    }

                    slots.Add(new BuildSlotDefinition { Id = slots.Count + 1, Position = position });
                }
            }

            return slots.ToArray();
        }
    }
}
