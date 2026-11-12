using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Domain;
using Game.Resource;
using UnityEngine;

namespace Game.HotUpdate.TowerDefense.Data
{
    internal static class TowerDefenseMapObjectives
    {
        internal sealed class Settings
        {
            private readonly BattleObjective objective;
            private readonly TowerPlacementArea towerPlacement;
            private readonly Position2[] routePoints;
            private readonly (float Width, float OriginX, float OriginY)? artFrame;

            internal Settings(BattleObjective objective, TowerPlacementArea towerPlacement, Position2[] routePoints,
                (float Width, float OriginX, float OriginY)? artFrame)
            {
                this.objective = objective;
                this.towerPlacement = towerPlacement;
                this.routePoints = routePoints;
                this.artFrame = artFrame;
            }

            internal void ApplyTo(FightConfiguration configuration)
            {
                configuration.Objective = objective;
                configuration.MapArtFrame = artFrame;
                if (routePoints != null)
                {
                    if (configuration.Routes.Length != 1)
                    {
                        throw new InvalidOperationException("Map route points require a single existing route for stage " + configuration.StageId);
                    }

                    configuration.Routes[0].Points = (Position2[])routePoints.Clone();
                }

                if (towerPlacement != null)
                {
                    TowerDefenseTables.ApplyTowerPlacement(configuration, towerPlacement);
                }
            }
        }

        [Serializable]
        private sealed class BaseUpgradeRule
        {
            public int goldCost = 0;
            public int durabilityIncrease = 0;
        }

        [Serializable]
        private sealed class AssaultRule
        {
            public int reinforcementGapTicks = 0;
            public int firstDraftPoints = 0;
            public int repeatDraftPoints = 0;
        }

        [Serializable]
        private sealed class MapPoint
        {
            public int x = 0;
            public int y = 0;
        }

        [Serializable]
        private sealed class TowerPlacementRule
        {
            public MapPoint min = null;
            public MapPoint max = null;
            public int roadHalfWidth = 0;
            public BlockedAreaRule[] blockedAreas = null;
        }

        [Serializable]
        private sealed class BlockedAreaRule
        {
            public MapPoint[] points = null;
        }

        [Serializable]
        private sealed class ArtFrameRule
        {
            public float width = 0;
            public float originX = 0;
            public float originY = 0;
        }

        [Serializable]
        private sealed class MapRules
        {
            public string objectiveType = "";
            public string targetName = "";
            public int baseAttackIntervalTicks = 0;
            public int baseAttackWindupTicks = 0;
            public int escapesToDefeat = 0;
            public BaseUpgradeRule[] baseUpgrades = null;
            public AssaultRule assault = null;
            public MapPoint[] routePoints = null;
            public TowerPlacementRule towerPlacement = null;
            public ArtFrameRule artFrame = null;
        }

        internal static async Task<Dictionary<int, Settings>> LoadAsync(IEnumerable<cfg.TdStage> stages, CancellationToken token)
        {
            var settings = new Dictionary<int, Settings>();
            foreach (var stage in stages)
            {
                string address = "TowerDefense/Maps/" + Path.GetFileName(stage.MapAsset);
                using var lease = await ResourceManager.Instance.AcquireAsync<TextAsset>(address, token, true);
                try
                {
                    settings.Add(stage.Id, Parse(lease.Asset.text));
                }
                catch (ArgumentException error)
                {
                    throw new InvalidOperationException("Invalid battle map settings in " + address, error);
                }
            }

            return settings;
        }

        private static Settings Parse(string json)
        {
            var rules = JsonUtility.FromJson<MapRules>(json);
            if (rules == null || !Enum.TryParse(rules.objectiveType, out BattleObjectiveType type)
                || type == BattleObjectiveType.LegacyBaseLeak || type.ToString() != rules.objectiveType)
            {
                throw new ArgumentException("Expected a supported battle objective type.");
            }

            var upgrades = rules.baseUpgrades?.Select(rule => rule == null ? default : new BaseUpgrade(rule.goldCost, rule.durabilityIncrease)).ToArray();
            var pace = rules.assault;
            // JsonUtility can create a default object for an omitted inline section.
            bool hasPace = pace != null && (pace.reinforcementGapTicks != 0 || pace.firstDraftPoints != 0 || pace.repeatDraftPoints != 0);
            var assault = hasPace ? new AssaultRules(pace.reinforcementGapTicks, pace.firstDraftPoints, pace.repeatDraftPoints) : AssaultRules.Default;
            var objective = new BattleObjective(
                type,
                rules.targetName,
                rules.baseAttackIntervalTicks,
                rules.escapesToDefeat,
                upgrades,
                assault,
                baseAttackWindupTicks: rules.baseAttackWindupTicks);
            var route = rules.routePoints is { Length: > 0 } ? ParsePoints(rules.routePoints, 2) : null;
            var art = rules.artFrame;
            (float Width, float OriginX, float OriginY)? artFrame = art is { width: > 0 } ? (art.width, art.originX, art.originY) : null;
            return new Settings(objective, ParseTowerPlacement(rules.towerPlacement), route, artFrame);
        }

        private static TowerPlacementArea ParseTowerPlacement(TowerPlacementRule rule)
        {
            // JsonUtility can instantiate an omitted inline object with default field values.
            if (rule == null || (rule.roadHalfWidth == 0
                && (rule.blockedAreas == null || rule.blockedAreas.Length == 0)
                && (rule.min == null || rule.min.x == 0 && rule.min.y == 0)
                && (rule.max == null || rule.max.x == 0 && rule.max.y == 0)))
            {
                return null;
            }

            var min = ParsePoint(rule.min);
            var max = ParsePoint(rule.max);
            var blocked = rule.blockedAreas?.Select(area => ParsePoints(area?.points, 3)).ToArray() ?? Array.Empty<Position2[]>();
            return new TowerPlacementArea(min, max, rule.roadHalfWidth, blocked);
        }

        private static Position2[] ParsePoints(MapPoint[] points, int minimum)
        {
            if (points == null || points.Length < minimum)
            {
                throw new ArgumentException("Missing map route or tower placement polygon points.");
            }

            return points.Select(ParsePoint).ToArray();
        }

        private static Position2 ParsePoint(MapPoint point)
        {
            if (point == null)
            {
                throw new ArgumentException("Missing map coordinate.");
            }

            return new Position2(point.x, point.y);
        }
    }
}
