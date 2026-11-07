using System;
using System.IO;
using System.Linq;
using Game.Framework.Storage;
using Game.Framework.Data;
using Game.HotUpdate.Camp;
using Game.HotUpdate.Client;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Editor
{
    public static partial class TowerDefenseValidation
    {
        private static byte[] originalJourney;
        private static byte[] originalJourneyBackup;
        private static bool profilePrepared;
        private static int campStage;
        private static int arrivalSteps;
        private static bool campSecondRun;
        private static string JourneyPath => Path.Combine(Application.persistentDataPath, "Storage", "local-journey.json");

        private static void PrepareCampProfile()
        {
            originalJourney = File.Exists(JourneyPath) ? File.ReadAllBytes(JourneyPath) : null;
            originalJourneyBackup = File.Exists(JourneyPath + ".bak") ? File.ReadAllBytes(JourneyPath + ".bak") : null;
            Directory.CreateDirectory(Output);
            if (originalJourney != null)
            {
                File.WriteAllBytes(Path.Combine(Output, "original-local-journey.backup"), originalJourney);
                File.Delete(JourneyPath);
            }

            if (originalJourneyBackup != null)
            {
                File.WriteAllBytes(Path.Combine(Output, "original-local-journey.bak.backup"), originalJourneyBackup);
                File.Delete(JourneyPath + ".bak");
            }

            profilePrepared = true;
            campStage = 0;
            arrivalSteps = 0;
            campSecondRun = false;
        }

        private static void RestoreCampProfile()
        {
            if (!profilePrepared)
            {
                return;
            }

            if (originalJourney == null)
            {
                if (File.Exists(JourneyPath))
                {
                    File.Delete(JourneyPath);
                }
            }
            else
            {
                File.WriteAllBytes(JourneyPath, originalJourney);
            }

            result.originalProfileRestored = true;
            File.Delete(JourneyPath + ".bak");
            if (originalJourneyBackup != null)
            {
                File.WriteAllBytes(JourneyPath + ".bak", originalJourneyBackup);
            }
            profilePrepared = false;
        }

        private static Button CampButton(PlayerHubView page, string name)
        {
            return page.GetComponentsInChildren<Button>(true).Single(value => value.name == name);
        }

        private static GameObject FindCampPanel(PlayerHubView page, string name)
        {
            return page.GetComponentsInChildren<Transform>(true).Single(value => value.name == name).gameObject;
        }

        private static LocalJourneyData CampSave()
        {
            return LocalStorageManager.Instance.LoadJson<LocalJourneyData>("local-journey.json");
        }

        private static bool PrepareCampBeforeBattle(PlayerHubView page)
        {
            if (campStage == 0)
            {
                Require(
                    UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null,
                    "The latest aggregate package's URP setup was not completed.");
                foreach (var mesh in page.Scene.GetComponentsInChildren<MeshRenderer>())
                {
                    string shader = mesh.sharedMaterial.shader.name;
                    Require(
                        shader == "Sprites/Default" || shader == "Universal Render Pipeline/Lit",
                        "A camp mesh uses an unsupported pipeline material: " + shader);
                    Require(mesh.sharedMaterial.shader.isSupported, "A camp shader is unsupported on the native graphics device.");
                }

                VerifyNativeCampMigration();
                Require(page.ArrivalPlaying, "A fresh profile did not enter SDK Story playback.");
                Require(!CampButton(page, "EnterTowerDefense").interactable, "Expedition was enabled before arrival completed.");
                Capture("arrival-story", () =>
                {
                    CampButton(page, "ArrivalContinue").onClick.Invoke();
                    arrivalSteps++;
                    campStage = 1;
                    delayUntil = EditorApplication.timeSinceStartup + .15;
                });
                return false;
            }

            if (campStage == 1)
            {
                if (page.ArrivalPlaying)
                {
                    CampButton(page, "ArrivalContinue").onClick.Invoke();
                    arrivalSteps++;
                    delayUntil = EditorApplication.timeSinceStartup + .15;
                    return false;
                }

                Require(arrivalSteps == 3, "Arrival did not present exactly three real Story dialogue nodes.");
                Require(CampSave().camp.arrivalSeen, "Completed Story did not persist the camp arrival marker.");
                result.campStoryVerified = true;
                Capture("camp-tent", () => campStage = 2);
                return false;
            }

            if (campStage == 2)
            {
                Require(page.Scene.DisplayedLevel == 1, "Fresh camp did not show a tent.");
                CampButton(page, "OpenCampMissions").onClick.Invoke();
                Require(FindCampPanel(page, "CampMissions").activeSelf, "The task button did not open the journal.");
                Capture("camp-missions", () =>
                {
                    var claim = CampButton(page, "ClaimCampTask 1");
                    Require(claim.interactable, "Completed arrival task was not claimable.");
                    int wood = CampSave().camp.wood;
                    claim.onClick.Invoke();
                    claim.onClick.Invoke();
                    Require(CampSave().camp.wood == wood + 15, "Arrival supplies were not granted exactly once.");
                    CampButton(page, "CloseMissions").onClick.Invoke();
                    campStage = 3;
                });
                return false;
            }

            if (campStage == 3)
            {
                var upgrade = CampButton(page, "UpgradeCamp");
                Require(upgrade.interactable, "The fence upgrade should be affordable after arrival supplies.");
                int wood = CampSave().camp.wood;
                upgrade.onClick.Invoke();
                Require(CampSave().camp.level == 2 && CampSave().camp.wood == wood - 30, "Construction did not atomically spend materials.");
                Require(page.Scene.DisplayedLevel == 2, "Construction did not update the actual 3D camp.");
                upgrade.onClick.Invoke();
                Require(CampSave().camp.level == 2 && CampSave().camp.wood == wood - 30, "A locked upgrade spent materials again.");
                CampButton(page, "OpenCampMissions").onClick.Invoke();
                CampButton(page, "ClaimCampTask 2").onClick.Invoke();
                CampButton(page, "CloseMissions").onClick.Invoke();
                result.campTasksVerified = true;
                result.campConstructionVerified = true;
                Capture("camp-fence", () => campStage = 4);
                return false;
            }

            return true;
        }

        private sealed class MigrationStore : ILocalJourneyStore
        {
            public LocalJourneyData Data;
            public LocalJourneyData Load() => Data;
            public void Save(LocalJourneyData value) => Data = value;
        }

        private static void VerifyNativeCampMigration()
        {
            var tables = DataManager.Instance.Get<cfg.Tables>();
            var store = new MigrationStore
            {
                Data = JsonUtility.FromJson<LocalJourneyData>(
                    "{\"version\":1,\"nickname\":\"漂流旅人\",\"experience\":150,\"victories\":2,\"highestWave\":6,\"completedTasks\":[]}")
            };
            var migrated = new JourneyProgress(tables, store);
            Require(migrated.Camp.Level.Id == 1 && migrated.Camp.Wood == 40 && migrated.Camp.Stone == 15, "Native legacy JSON lost initial camp supplies.");
            Require(migrated.Victories == 2 && migrated.Experience == 150, "Native migration lost existing player progress.");
            migrated.Camp.CompleteArrival();
            Require(store.Data.version == 2 && store.Data.camp.format == 1, "Migration did not persist explicit camp/schema versions.");
            store.Data = JsonUtility.FromJson<LocalJourneyData>("{\"version\":2,\"nickname\":\"漂流旅人\",\"completedTasks\":[]}");
            bool rejected = false;
            try
            {
                _ = new JourneyProgress(tables, store);
            }
            catch (InvalidOperationException)
            {
                rejected = true;
            }

            Require(rejected, "Missing version-two camp data was silently reset by native JSON deserialization.");
            result.campMigrationVerified = true;
        }

        private static void BeginCampReturn(PlayerHubView page)
        {
            Require(page.Scene.DisplayedLevel == 2 && !page.ArrivalPlaying, "Returning lost the camp or replayed arrival.");
            CampButton(page, "OpenCampMissions").onClick.Invoke();
            Require(CampButton(page, "ClaimCampTask 3").interactable, "Tower-defense victory did not unlock the camp task.");
            CampButton(page, "ClaimCampTask 3").onClick.Invoke();
            CampButton(page, "CloseMissions").onClick.Invoke();
            CampButton(page, "UpgradeCamp").onClick.Invoke();
            Require(page.Scene.DisplayedLevel == 3 && CampSave().camp.level == 3, "The courtyard upgrade did not persist and render.");
            CampButton(page, "OpenCampMissions").onClick.Invoke();
            CampButton(page, "ClaimCampTask 4").onClick.Invoke();
            CampButton(page, "CloseMissions").onClick.Invoke();
            Capture("camp-courtyard", () =>
            {
                campSecondRun = true;
                verifyingDefeat = false;
                game = null;
                view = null;
                CampButton(page, "EnterTowerDefense").onClick.Invoke();
            });
        }

        private static void CompleteIslandHome()
        {
            if (CampSave().victories < 2)
            {
                return;
            }

            var returnButton = view.GetComponentsInChildren<Button>(true).Single(value => value.name == "营地");
            if (!returnButton.interactable)
            {
                return;
            }

            returnButton.onClick.Invoke();
            view = null;
            game = null;
            var page = UnityEngine.Object.FindObjectOfType<PlayerHubView>();
            Require(page != null && CampSave().victories == 2, "The second real victory was not recorded.");
            CampButton(page, "UpgradeCamp").onClick.Invoke();
            Require(CampSave().camp.level == 4 && page.Scene.DisplayedLevel == 4, "The final home stage was not built and rendered.");
            Require(!CampButton(page, "UpgradeCamp").interactable, "The maximum camp level still permits upgrades.");
            result.campHomeVerified = true;
            Capture(
                "camp-home",
                () => Finish(true, "SDK arrival Story, four camp stages, task claims, two real victories, battle restarts, return and cleanup verified."));
        }
    }
}
