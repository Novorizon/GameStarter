using System;
using System.Linq;
using Game.HotUpdate.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor
{
    public static partial class TowerDefenseValidation
    {
        private static uint acceptedExpeditionSeed;

        private static void OpenPlayerArchive(PlayerHubView page)
        {
            CampButton(page, "PlayerProfile").onClick.Invoke();
            Require(FindCampPanel(page, "PlayerProfileDetails").activeSelf, "The profile button did not open the player archive.");
            var labels = page.GetComponentsInChildren<TMP_Text>().Where(label => label.name == "GoalProgress").ToArray();
            Require(labels.Length == 3, "The archive did not show all real journey goals.");
            var experience = page.GetComponentsInChildren<TMP_Text>().Single(label => label.name == "Experience");
            Require(experience.text.Contains(CampSave().experience.ToString()), "The archive did not refresh saved exploration experience.");
            result.playerArchiveVerified = true;
            Capture("player-profile", () =>
            {
                CampButton(page, "CloseProfile").onClick.Invoke();
                BeginPreparedExpedition(page);
            });
        }

        private static void BeginPreparedExpedition(PlayerHubView page, bool replayOutpost = false)
        {
            CampButton(page, "EnterTowerDefense").onClick.Invoke();
            Require(FindCampPanel(page, "ExpeditionPreparation").activeSelf, "The expedition entry bypassed preparation.");
            Require(
                UnityEngine.Object.FindObjectOfType<Game.HotUpdate.TowerDefense.Presentation.TowerDefensePrototypeView>() == null,
                "Battle resources/views were created before confirmation.");
            CampButton(page, "CancelExpedition").onClick.Invoke();
            Require(!FindCampPanel(page, "ExpeditionPreparation").activeSelf, "Cancelling preparation did not return to the camp.");
            CampButton(page, "EnterTowerDefense").onClick.Invoke();
            if (replayOutpost)
            {
                CampButton(page, "Chapter1").onClick.Invoke();
            }

            var labels = page.GetComponentsInChildren<TMP_Text>();
            Require(labels.Count(label => label.name == "WaveSummary") == 6, "The preparation page did not show six authored waves.");
            string identity = labels.Single(label => label.name == "Version").text;
            Require(uint.TryParse(identity.Split(' ').Last(), out acceptedExpeditionSeed), "Preparation did not display its accepted seed.");
            Canvas.ForceUpdateCanvases();
            var scroll = page.GetComponentsInChildren<ScrollRect>().Single(value => value.name == "WaveList");
            scroll.verticalNormalizedPosition = 0;
            Canvas.ForceUpdateCanvases();
            var finalWave = scroll.content.GetComponentsInChildren<RectTransform>().Single(value => value.name == "Wave 6");
            Vector3 center = finalWave.TransformPoint(finalWave.rect.center);
            Require(RectTransformUtility.RectangleContainsScreenPoint(scroll.viewport, center), "The last wave cannot be scrolled into the viewport.");
            result.waveListScrollVerified = true;
            Capture(campSecondRun ? "second-expedition" : "expedition-preparation", () =>
            {
                var confirm = CampButton(page, "ConfirmExpedition");
                confirm.onClick.Invoke();
                confirm.onClick.Invoke();
                result.expeditionPreparationVerified = true;
            });
        }

        private static void VerifyBattlePauseAndForecast()
        {
            var labels = view.GetComponentsInChildren<TMP_Text>();
            var forecast = labels.Single(label => label.name == "WaveIntel");
            Require(forecast.text.Contains(" " + game.WaveIndex + " / "), "The forecast was not refreshed for the current wave.");
            result.waveForecastVerified = true;
            var buttons = view.GetComponentsInChildren<Button>(true);
            var pause = buttons.Single(button => button.name == "Pause");
            var camp = buttons.Single(button => button.name == "营地");
            var cancel = buttons.Single(button => button.name == "继续防守");
            long tick = game.Tick;
            view.SendMessage("OnApplicationFocus", false);
            game.Step(10);
            Require(game.IsPaused && game.Tick == tick, "Native focus loss did not stop the actual fight.");
            view.SendMessage("OnApplicationPause", true);
            view.SendMessage("OnApplicationFocus", true);
            Require(game.IsPaused, "Focus restore cleared an independent application pause.");
            view.SendMessage("OnApplicationPause", false);
            Require(!game.IsPaused, "Clearing both system pause reasons did not restore the fight.");
            pause.onClick.Invoke();
            camp.onClick.Invoke();
            camp.onClick.Invoke();
            view.SendMessage("OnApplicationFocus", false);
            view.SendMessage("OnApplicationFocus", true);
            cancel.onClick.Invoke();
            game.Step(10);
            Require(game.IsPaused && game.Tick == tick, "Cancelling return or regaining focus cleared a manual pause.");
            pause.onClick.Invoke();
            Require(!game.IsPaused, "The actual pause button could not resume the manual pause.");
            camp.onClick.Invoke();
            view.SendMessage("OnApplicationPause", true);
            cancel.onClick.Invoke();
            Require(game.IsPaused, "Cancelling return cleared application suspension.");
            view.SendMessage("OnApplicationPause", false);
            Require(!game.IsPaused, "The fight remained paused after all reasons were cleared.");
            result.applicationPauseCallbacksVerified = true;
            result.nestedPauseVerified = true;
        }
    }
}
