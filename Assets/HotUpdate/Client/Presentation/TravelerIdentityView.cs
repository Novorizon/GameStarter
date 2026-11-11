using System;
using System.Collections.Generic;
using System.IO;
using Game.HotUpdate.Camp;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Client
{
    internal sealed class TravelerIdentityView
    {
        private static readonly (TravelerAppearance Appearance, string Label)[] Options =
        {
            (TravelerAppearance.Forest, "林地"),
            (TravelerAppearance.Shore, "海岸"),
            (TravelerAppearance.Ember, "营火")
        };

        private readonly ClientArt art;
        private readonly CampUI ui;
        private readonly JourneyProgress journey;
        private readonly Action saved;
        private readonly GameObject root;
        private readonly TMP_Text heading;
        private readonly TMP_Text subtitle;
        private readonly TMP_Text cancelCaption;
        private readonly TMP_Text feedback;
        private readonly TMP_InputField nickname;
        private readonly Image portrait;
        private readonly TMP_Text previewName;
        private readonly List<(TravelerAppearance Appearance, Image Surface, Image Check, TMP_Text Caption)> cards =
            new List<(TravelerAppearance, Image, Image, TMP_Text)>();
        private TravelerAppearance selection;

        internal bool Visible => root.activeSelf;

        internal TravelerIdentityView(Transform parent, ClientArt art, JourneyProgress journey, Action saved, Action cancelled)
        {
            this.art = art;
            this.journey = journey;
            this.saved = saved;

            ui = new CampUI(art);
            var panel = ui.Panel("TravelerIdentity", parent, .215f, .13f, .785f, .87f);
            root = panel.gameObject;
            ui.Glyph(CampPiece.User, panel, .056f, .825f, .108f, .949f, CampUI.Mint);
            heading = ui.Text("Title", panel, .125f, .867f, .95f, .957f, 40, "", CampUI.Cream, bold: true);
            subtitle = ui.Text("Subtitle", panel, .127f, .805f, .95f, .867f, 21, "", CampUI.Muted);
            CampUI.Divider(panel, .057f, .792f, .947f, .794f);

            var preview = ui.Panel("TravelerPreview", panel, .055f, .085f, .393f, .767f, CampPiece.Card);
            portrait = CreatePortrait(preview, .055f, .14f, .945f, .955f);
            previewName = ui.Text("PreviewName", preview, .06f, .035f, .94f, .14f, 26, "", CampUI.Cream);
            previewName.alignment = TextAlignmentOptions.Center;
            previewName.richText = false;
            previewName.enableWordWrapping = false;
            previewName.overflowMode = TextOverflowModes.Ellipsis;

            ui.Text("NicknameLabel", panel, .44f, .727f, .94f, .785f, 25, "昵称", CampUI.Cream);
            nickname = CreateNicknameField(ui, panel);
            ui.Text("AppearanceLabel", panel, .44f, .548f, .94f, .608f, 25, "选择外貌", CampUI.Cream);
            foreach (var option in Options)
            {
                if (art.HasPortrait(option.Appearance))
                {
                    CreateAppearanceCard(ui, panel, option.Appearance, option.Label);
                }
            }

            feedback = ui.Text("Feedback", panel, .44f, .179f, .947f, .264f, 21, "", CampUI.Muted);
            var cancel = ui.ActionButton("CancelIdentity", panel, .44f, .085f, .675f, .173f, "取消", cancelled);
            cancelCaption = cancel.GetComponentInChildren<TMP_Text>();
            cancelCaption.color = CampUI.Cream;
            ui.ActionButton("SaveIdentity", panel, .712f, .085f, .947f, .173f, "保存", Save, CampPiece.Primary);
            nickname.onValueChanged.AddListener(UpdateNicknamePreview);
            Hide();
        }

        internal void Show(bool firstSetup)
        {
            heading.text = firstSetup ? "设定旅人" : "编辑资料";
            subtitle.text = firstSetup ? "在这座岛上安顿下来，给你的旅人取个名字。" : "更改旅人的昵称和外貌。";
            cancelCaption.text = firstSetup ? "稍后再说" : "取消";
            root.SetActive(true);
            nickname.SetTextWithoutNotify(journey.Nickname);
            UpdateNicknamePreview(journey.Nickname);
            Select(journey.Appearance);
        }

        internal void Hide()
        {
            root.SetActive(false);
        }

        private static TMP_InputField CreateNicknameField(CampUI ui, Transform parent)
        {
            var background = ui.Panel("NicknameInput", parent, .44f, .629f, .947f, .724f, CampPiece.Card);
            var viewport = CampUI.Area("TextViewport", background, .045f, .095f, .955f, .905f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var placeholder = ui.Text("Placeholder", viewport, 0, 0, 1, 1, 26, "输入昵称", CampUI.Muted);
            placeholder.alignment = TextAlignmentOptions.MidlineLeft;
            var value = ui.Text("Text", viewport, 0, 0, 1, 1, 30, "", CampUI.Cream);
            value.alignment = TextAlignmentOptions.MidlineLeft;
            value.enableWordWrapping = false;
            value.enableAutoSizing = false;

            var input = background.gameObject.AddComponent<TMP_InputField>();
            input.targetGraphic = background.GetComponent<Image>();
            input.textViewport = viewport;
            input.textComponent = value;
            input.placeholder = placeholder;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.richText = false;
            input.characterLimit = 48;
            return input;
        }

        private void CreateAppearanceCard(CampUI ui, Transform parent, TravelerAppearance appearance, string label)
        {
            float left = .44f + cards.Count * .172f;
            var card = ui.Panel("Appearance" + appearance, parent, left, .274f, left + .163f, .545f, CampPiece.Card);
            var button = CampUI.BindButton(card, () => Select(appearance));
            CreatePortrait(card, .072f, .245f, .928f, .955f).sprite = art.Portrait(appearance);
            var caption = ui.Text("Caption", card, .05f, .035f, .95f, .242f, 23, label, CampUI.Cream);
            caption.alignment = TextAlignmentOptions.Center;
            var check = ui.Glyph(CampPiece.Check, card, .74f, .76f, .92f, .92f, CampUI.Gold);
            cards.Add((appearance, button.image, check, caption));
        }

        private static Image CreatePortrait(Transform parent, float left, float bottom, float right, float top)
        {
            var image = CampUI.Area("Portrait", parent, left, bottom, right, top).gameObject.AddComponent<Image>();
            image.preserveAspect = true;
            image.raycastTarget = false;
            return image;
        }

        private void Select(TravelerAppearance appearance)
        {
            selection = appearance;
            portrait.sprite = art.Portrait(selection);
            var visibleAppearance = art.HasPortrait(selection) ? selection : TravelerAppearance.Forest;
            foreach (var card in cards)
            {
                bool selected = card.Appearance == visibleAppearance;
                ui.Style(card.Surface, selected ? CampPiece.Selected : CampPiece.Card);
                card.Check.gameObject.SetActive(selected);
                card.Caption.color = selected ? CampUI.Gold : CampUI.Cream;
                card.Caption.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            }
        }

        private void UpdateNicknamePreview(string value)
        {
            previewName.text = value.Trim();
            feedback.color = CampUI.Muted;
            feedback.text = "昵称最多 " + JourneyProgress.MaxNicknameLength + " 个字符";
        }

        private void Save()
        {
            try
            {
                if (!journey.TrySaveIdentity(nickname.text, selection))
                {
                    feedback.color = CampUI.Danger;
                    feedback.text = "请输入 1–" + JourneyProgress.MaxNicknameLength + " 个字符，可用文字、数字、空格、下划线和短横线。";
                    return;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                feedback.color = CampUI.Danger;
                feedback.text = "资料未能保存，请稍后重试。";
                Debug.LogWarning("Traveler profile save failed: " + error.Message);
                return;
            }

            saved();
        }
    }
}
