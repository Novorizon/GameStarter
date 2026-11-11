using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Story;
using Game.HotUpdate.Camp;

namespace Game.HotUpdate.Client
{
    public sealed partial class PlayerHubView
    {
        private CampArrivalView campaignDialogue;

        internal async Task<int> PlayCampaignStoryAsync(CampaignScene scene, CancellationToken token)
        {
            missions.Hide();
            archive.Hide();
            profileSetupPrompt.SetActive(false);
            string traveler = owner.Journey.KeeperName ?? owner.Journey.Nickname;
            var dialogue = CampArrivalView.Create(safe, owner.Art, scene.dialogue, scene.dialogue.id, scene.finish, "跳过对白", traveler);
            campaignDialogue = dialogue;
            try
            {
                var result = await dialogue.PlayAsync(token);
                token.ThrowIfCancellationRequested();
                if (result.Status == StoryStatus.Failed)
                {
                    throw result.Error ?? new InvalidOperationException("Campaign story failed.");
                }

                if (result.Status != StoryStatus.Completed && result.Status != StoryStatus.Skipped)
                {
                    return -1;
                }
            }
            finally
            {
                campaignDialogue = null;
                if (dialogue != null)
                {
                    dialogue.Dispose();
                    Destroy(dialogue.gameObject);
                }
            }

            return scene.choices?.Length > 0 ? await ChooseCampaignActionAsync(scene, token) : 0;
        }

        private async Task<int> ChooseCampaignActionAsync(CampaignScene scene, CancellationToken token)
        {
            var ui = new CampUI(owner.Art);
            var overlay = CampUI.Overlay("CampaignChoice", safe);
            var panel = ui.Panel("Decision", overlay, .224f, .239f, .776f, .761f);
            ui.Glyph(CampPiece.Journal, panel, .054f, .799f, .105f, .933f, CampUI.Gold);
            ui.Text("Title", panel, .121f, .815f, .746f, .935f, 36, scene.dialogue.title, CampUI.Cream, bold: true);
            ui.Text("Chapter", panel, .123f, .741f, .948f, .817f, 22, "潮汐之灯", CampUI.Muted);
            CampUI.Divider(panel, .049f, .723f, .95f, .729f);

            var selection = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool single = scene.choices.Length == 1;
            for (int index = 0; index < scene.choices.Length; index++)
            {
                int choiceIndex = index;
                var choice = scene.choices[index];
                float left = single ? .19f : .049f + index * .461f;
                float right = single ? .81f : left + .441f;
                var card = ui.Panel("Choice" + index, panel, left, .254f, right, .671f, CampPiece.Card);
                ui.Text("ChoiceTitle", card, .067f, .647f, .944f, .931f, 27, choice.title, CampUI.Cream, bold: true);
                ui.Text("ChoiceEffect", card, .067f, .331f, .944f, .646f, 23, choice.description, CampUI.Muted);
                ui.ActionButton("Choose" + index, card, .066f, .073f, .936f, .31f, single ? "确认" : "选择", () => selection.TrySetResult(choiceIndex), CampPiece.Primary);
            }

            ui.ActionButton("CloseChoice", panel, .049f, .052f, .26f, .176f, "返回营地", () => selection.TrySetResult(-1));
            using var registration = token.Register(() => selection.TrySetCanceled(token));
            try
            {
                return await selection.Task;
            }
            finally
            {
                if (overlay != null)
                {
                    overlay.gameObject.SetActive(false);
                    Destroy(overlay.gameObject);
                }
            }
        }
    }
}
