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
            private readonly BattleTerrain terrain;
            private readonly Position2[] routePoints;
            private ForestRedrawNavigation forestRedraw;

            internal Settings(BattleObjective objective, BattleTerrain terrain = null, Position2[] routePoints = null)
            {
                this.objective = objective;
                this.terrain = terrain;
                this.routePoints = routePoints;
            }

            internal void ApplyTo(FightConfiguration configuration)
            {
                if (forestRedraw != null)
                {
                    ApplyForestRedraw(configuration);
                    return;
                }

                configuration.Objective = objective;
                if (terrain != null)
                {
                    TowerDefenseTables.ApplyTerrain(configuration, terrain, routePoints);
                    if (configuration.StageId == 1 && objective.Type == BattleObjectiveType.DefendBase
                        && objective.BaseAttack?.Engagement != null && objective.BaseAttack.LocalArea != null)
                    {
                        configuration.AGrid = TowerDefenseANavigationLayout.Create(configuration);
                    }
                }
            }

            internal void UseForestRedraw(string source)
            {
                forestRedraw = ForestRedrawNavigation.Parse(source);
            }

            private void ApplyForestRedraw(FightConfiguration configuration)
            {
                var original = objective.BaseAttack;
                var target = forestRedraw.Target;
                int radius = forestRedraw.TargetRadius;
                var bounds = new BaseAttackEngagement(
                    new Position2(target.X - radius, target.Y - radius),
                    new Position2(target.X + radius, target.Y + radius),
                    original.Engagement.Reach,
                    original.Engagement.WindupTicks);
                var approach = new BaseApproachArea(forestRedraw.HoldingMin, forestRedraw.HoldingMax, Array.Empty<BaseApproachObstacle>());
                var layout = new BaseAttackLayout(original.ApproachInset, Array.Empty<BaseAttackPath>(), bounds, approach);
                configuration.Objective = new BattleObjective(objective.Type, objective.TargetName, objective.BaseAttackIntervalTicks,
                    objective.EscapesToDefeat, objective.BaseUpgrades.ToArray(), objective.Assault, layout);
                TowerDefenseTables.ApplyTerrain(configuration, forestRedraw.BuildTerrain, forestRedraw.Route);
                configuration.ForestRedrawEntrance = forestRedraw.SpawnMarker;
                configuration.AGrid = forestRedraw.CreateGrid();
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
        private sealed class AttackPoint
        {
            public int x = 0;
            public int y = 0;
        }

        [Serializable]
        private sealed class AttackPath
        {
            public int side = 0;
            public int firstAttackPoint = 0;
            public int maxBodyRadius = 0;
            public AttackPoint[] points = null;
        }

        [Serializable]
        private sealed class BaseAttackRule
        {
            public int approachInset = 0;
            public AttackPath[] paths = null;
            public EngagementRule engagement = null;
            public LocalAreaRule localArea = null;
        }

        [Serializable]
        private sealed class EngagementRule
        {
            public AttackPoint min = null;
            public AttackPoint max = null;
            public int reach = 0;
            public int windupTicks = 0;
        }

        [Serializable]
        private sealed class LocalAreaRule
        {
            public AttackPoint min = null;
            public AttackPoint max = null;
            public LocalObstacleRule[] obstacles = null;
        }

        [Serializable]
        private sealed class LocalObstacleRule
        {
            public AttackPoint center = null;
            public int radius = 0;
        }

        [Serializable]
        private sealed class NavigationRule
        {
            public AttackPoint min = null;
            public AttackPoint max = null;
            public int roadHalfWidth = 0;
            public AttackPoint[] routePoints = null;
            public BlockedAreaRule[] blockedAreas = null;
        }

        [Serializable]
        private sealed class BlockedAreaRule
        {
            public AttackPoint[] points = null;
        }

        [Serializable]
        private sealed class MapRules
        {
            public string objectiveType = "";
            public string targetName = "";
            public int baseAttackIntervalTicks = 0;
            public int escapesToDefeat = 0;
            public BaseUpgradeRule[] baseUpgrades = null;
            public AssaultRule assault = null;
            public BaseAttackRule baseAttack = null;
            public NavigationRule navigation = null;
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
                    var map = Parse(lease.Asset.text);
                    if (stage.Id == 1)
                    {
                        using var grid = await ResourceManager.Instance.AcquireAsync<TextAsset>("TowerDefense/Maps/ForestRedrawGrid", token, true);
                        map.UseForestRedraw(grid.Asset.text);
                    }

                    settings.Add(stage.Id, map);
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
            var baseAttack = rules.baseAttack;
            var localArea = ParseLocalArea(baseAttack?.localArea);
            bool hasLayout = baseAttack != null && (baseAttack.approachInset != 0 || baseAttack.paths is { Length: > 0 } || localArea != null);
            var layout = hasLayout
                ? new BaseAttackLayout(
                    baseAttack.approachInset,
                    baseAttack.paths?.Select(ParseAttackPath).ToArray(),
                    ParseEngagement(baseAttack.engagement),
                    localArea)
                : null;
            var objective = new BattleObjective(type, rules.targetName, rules.baseAttackIntervalTicks, rules.escapesToDefeat, upgrades, assault, layout);
            return ParseNavigation(objective, rules.navigation);
        }

        private static Settings ParseNavigation(BattleObjective objective, NavigationRule rule)
        {
            // JsonUtility can instantiate an omitted inline object with default field values.
            if (rule == null || (rule.roadHalfWidth == 0 && (rule.routePoints == null || rule.routePoints.Length == 0)
                && (rule.blockedAreas == null || rule.blockedAreas.Length == 0)
                && (rule.min == null || rule.min.x == 0 && rule.min.y == 0)
                && (rule.max == null || rule.max.x == 0 && rule.max.y == 0)))
            {
                return new Settings(objective);
            }

            var min = ParseNavigationPoint(rule.min);
            var max = ParseNavigationPoint(rule.max);
            var route = ParseNavigationPoints(rule.routePoints, 2);
            var blocked = rule.blockedAreas?.Select(area => ParseNavigationPoints(area?.points, 3)).ToArray() ?? Array.Empty<Position2[]>();
            var terrain = new BattleTerrain(min, max, route, rule.roadHalfWidth, blocked);
            return new Settings(objective, terrain, route);
        }

        private static Position2[] ParseNavigationPoints(AttackPoint[] points, int minimum)
        {
            if (points == null || points.Length < minimum)
            {
                throw new ArgumentException("Missing route or obstacle navigation points.");
            }

            return points.Select(ParseNavigationPoint).ToArray();
        }

        private static Position2 ParseNavigationPoint(AttackPoint point)
        {
            if (point == null)
            {
                throw new ArgumentException("Missing map navigation coordinate.");
            }

            return new Position2(point.x, point.y);
        }

        private static BaseAttackEngagement ParseEngagement(EngagementRule rule)
        {
            if (rule == null || (rule.reach == 0 && rule.windupTicks == 0
                && (rule.min == null || rule.min.x == 0 && rule.min.y == 0)
                && (rule.max == null || rule.max.x == 0 && rule.max.y == 0)))
            {
                return null;
            }

            if (rule.min == null || rule.max == null)
            {
                throw new ArgumentException("Missing base engagement bounds.");
            }

            return new BaseAttackEngagement(new Position2(rule.min.x, rule.min.y), new Position2(rule.max.x, rule.max.y), rule.reach, rule.windupTicks);
        }

        private static BaseApproachArea ParseLocalArea(LocalAreaRule rule)
        {
            // An omitted inline section can deserialize as an object with default values.
            if (rule == null || ((rule.min == null || rule.min.x == 0 && rule.min.y == 0)
                && (rule.max == null || rule.max.x == 0 && rule.max.y == 0)
                && (rule.obstacles == null || rule.obstacles.Length == 0)))
            {
                return null;
            }

            if (rule.min == null || rule.max == null)
            {
                throw new ArgumentException("Missing local base approach bounds.");
            }

            var obstacles = rule.obstacles?.Select(ParseLocalObstacle).ToArray() ?? Array.Empty<BaseApproachObstacle>();
            return new BaseApproachArea(new Position2(rule.min.x, rule.min.y), new Position2(rule.max.x, rule.max.y), obstacles);
        }

        private static BaseApproachObstacle ParseLocalObstacle(LocalObstacleRule obstacle)
        {
            if (obstacle?.center == null)
            {
                throw new ArgumentException("Missing local base approach obstacle center.");
            }

            return new BaseApproachObstacle(new Position2(obstacle.center.x, obstacle.center.y), obstacle.radius);
        }

        private static BaseAttackPath ParseAttackPath(AttackPath path)
        {
            if (path == null || path.points == null || path.points.Any(point => point == null))
            {
                throw new ArgumentException("Missing base attack path or point.");
            }

            var points = path.points.Select(point => new Position2(point.x, point.y)).ToArray();
            return new BaseAttackPath(path.side, path.firstAttackPoint, path.maxBodyRadius, points);
        }
    }
}
