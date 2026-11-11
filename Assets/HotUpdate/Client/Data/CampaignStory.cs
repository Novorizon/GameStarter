using System;
using System.Collections.Generic;
using System.Linq;
using Game.HotUpdate.Camp;
using Game.HotUpdate.TowerDefense.Domain;
using UnityEngine;

namespace Game.HotUpdate.Client
{
    [Serializable]
    internal sealed class CampaignChoice
    {
        public string title;
        public string description;
    }

    [Serializable]
    internal sealed class CampaignScene
    {
        public string hint;
        public string action;
        public string finish;
        public CampStoryScript dialogue;
        public CampaignChoice[] choices;
    }

    [Serializable]
    internal sealed class CampaignChapter
    {
        public int stage;
        public bool sideBattle;
        public string title;
        public string objective;
        public string terrain;
        public string strategy;
        public string[] tactics;
        public string[] entrances;
        public string[] planningCues;
    }

    [Serializable]
    internal sealed class CampaignStory
    {
        private const int ArrowTower = 1;
        private const int ArrowKitDiscount = 15;
        private const int RepairAmount = 4;
        private const int RationGold = 45;

        public CampaignScene[] scenes;
        public CampaignChapter[] chapters;
        private Dictionary<string, CampaignScene> sceneById;
        private Dictionary<int, CampaignChapter> chapterById;

        public static CampaignStory Load(string json)
        {
            var story = JsonUtility.FromJson<CampaignStory>(json);
            if (story?.scenes == null || story.chapters == null)
            {
                throw new InvalidOperationException("The Tide campaign has no scenes or chapters.");
            }

            story.sceneById = story.scenes.ToDictionary(scene => scene.dialogue.id, StringComparer.Ordinal);
            story.chapterById = story.chapters.ToDictionary(chapter => chapter.stage);
            return story;
        }

        public CampaignScene Scene(JourneyProgress journey)
        {
            if (journey.NextStageId != 0 || journey.StoryStep == CampaignStep.Complete)
            {
                return null;
            }

            string id = journey.StoryStep switch
            {
                CampaignStep.RidgeReturn when journey.RelayHeld => "RidgeReturnHeld",
                CampaignStep.RidgeReturn when journey.GuardingRelay => "RidgeReturnLost",
                CampaignStep.Epilogue when journey.Ending == CampaignEnding.Keeper => "Keeper",
                CampaignStep.Epilogue => "Chart",
                _ => journey.StoryStep.ToString()
            };
            return sceneById[id];
        }

        public CampaignChapter Chapter(int stageId) => chapterById[stageId];

        public bool IsSideBattle(int stageId) => chapterById.TryGetValue(stageId, out var chapter) && chapter.sideBattle;

        public string ApplyTo(FightConfiguration fight, LocalCampaignData choices)
        {
            var chapter = Chapter(fight.StageId);
            if (chapter.entrances.Length != fight.Routes.Length || chapter.planningCues.Length != fight.Waves.Length)
            {
                throw new InvalidOperationException("The chapter briefing does not match the routes or waves for stage " + fight.StageId);
            }

            foreach (var route in fight.Routes)
            {
                route.EntranceName = chapter.entrances[route.Id - 1];
            }

            for (int index = 0; index < fight.Waves.Length; index++)
            {
                fight.Waves[index].PlanningCue = chapter.planningCues[index];
            }

            if (chapter.sideBattle)
            {
                return "支线防守 · 战后按营地等级结算补给";
            }

            switch (fight.StageId)
            {
                case 1:
                    if (choices.repairKit && fight.Objective.HasBase)
                    {
                        fight.PlanningRepair = RepairAmount;
                        return "修理工具 · 战斗中可修复 4 点耐久，限一次";
                    }

                    fight.OpeningDiscountTowerId = ArrowTower;
                    fight.OpeningDiscountGold = ArrowKitDiscount;
                    return "箭具箱 · 第一座橡木箭塔少花 15 金币";
                case 2:
                    return ApplySupplies(fight, choices.bringWater);
                case 3:
                    if (choices.guardRelay)
                    {
                        fight.RelayRouteId = 3;
                        fight.RelayPointIndex = 3;
                        foreach (var group in fight.Waves.SelectMany(wave => wave.Groups).Where(group => group.RouteId == 2))
                        {
                            group.RouteId = fight.RelayRouteId;
                        }
                    }

                    return ApplySupplies(fight, !choices.bringWater);
                case 4:
                    fight.HidePlannedRoutes = !choices.relayHeld;
                    return choices.relayHeld ? "副站回报 · 可提前看到增援入口" : "灯塔补给 · 守住主塔，等候返潮信号";
                default:
                    throw new ArgumentOutOfRangeException(nameof(fight.StageId));
            }
        }

        private static string ApplySupplies(FightConfiguration fight, bool water)
        {
            if (water && fight.Objective.HasBase)
            {
                fight.PlanningRepair = RepairAmount;
                return "清水与修理包 · 战斗中可修复 4 点耐久，限一次";
            }

            fight.InitialGold += RationGold;
            return "干粮补给 · 开局多带 45 金币";
        }
    }
}
