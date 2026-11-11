using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework.Story;
using Game.HotUpdate.Client;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.HotUpdate.Camp
{
    /// <summary>营地对白呈现器；推进、取消和结果由 SDK StoryPlayer 负责。</summary>
    public sealed class CampArrivalView : MonoBehaviour, IStoryPresenter, IDisposable
    {
        private StoryPlayer player;
        private StorySession session;
        private StorySequence sequence;
        private CampStoryScript content;
        private string finishLabel;
        private string traveler;
        private TMP_Text body;
        private TMP_Text speakerName;
        private TMP_Text counter;
        private Button next;
        private TaskCompletionSource<StoryStepOutcome> waiting;
        private StoryStepContext step;
        private int index;
        private bool disposed;
        public bool Playing => player != null && player.IsPlaying;

        public static CampArrivalView Create(Transform parent, ClientArt art, string script, string chapter, string finishLabel, string skipLabel)
        {
            return Create(parent, art, JsonUtility.FromJson<CampStoryScript>(script), chapter, finishLabel, skipLabel, "漂流者");
        }

        internal static CampArrivalView Create(
            Transform parent,
            ClientArt art,
            CampStoryScript script,
            string chapter,
            string finishLabel,
            string skipLabel,
            string traveler)
        {
            if (script?.lines == null || script.lines.Length == 0 || script.lines.Any(string.IsNullOrWhiteSpace)
                || (script.speakers?.Length > 0 && script.speakers.Length != script.lines.Length))
            {
                throw new InvalidOperationException("The camp story has no valid dialogue.");
            }

            var ui = new CampUI(art);
            var root = CampUI.Overlay("Camp" + chapter + "Story", parent, .52f);
            var view = root.gameObject.AddComponent<CampArrivalView>();
            view.content = script;
            view.finishLabel = finishLabel;
            view.traveler = traveler;

            var nodes = view.content.lines.Select((line, i) => new StoryNode("shore-" + i, StoryNodeKind.Say, textKey: line)).ToList();
            nodes.Add(new StoryNode("camp-ready", StoryNodeKind.End));
            view.sequence = new StorySequence(view.content.id, string.IsNullOrEmpty(view.content.version) ? "1" : view.content.version, nodes);
            var panel = ui.Panel("ArrivalDialogue", root, .016f, .034f, .626f, .322f);
            ui.Glyph(CampPiece.Journal, panel, .046f, .768f, .086f, .942f, CampUI.Gold);
            ui.Text("Chapter", panel, .096f, .774f, .747f, .938f, 31, view.content.title, CampUI.Cream, bold: true);
            var speaker = ui.Panel("SpeakerChip", panel, .762f, .797f, .954f, .933f, CampPiece.Badge);
            view.speakerName = ui.Text("Speaker", speaker, .08f, .12f, .92f, .88f, 21, view.content.speaker, CampUI.Mint);
            view.speakerName.alignment = TextAlignmentOptions.Center;
            CampUI.Divider(panel, .049f, .75f, .952f, .755f);
            view.body = ui.Text("Dialogue", panel, .052f, .33f, .947f, .731f, 28, "", CampUI.Cream);
            view.body.richText = false;
            view.counter = ui.Text("Step", panel, .052f, .096f, .24f, .252f, 22, "", CampUI.Muted, bold: true);
            view.next = ui.ActionButton(chapter + "Continue", panel, .669f, .069f, .954f, .277f, "继续", () => view.session?.Advance(), CampPiece.Primary);
            ui.ActionButton(chapter + "Skip", root, .854f, .887f, .984f, .984f, skipLabel, () => view.session?.Skip());
            view.player = new StoryPlayer(view);
            root.gameObject.SetActive(false);
            return view;
        }

        public async Task<StoryResult> PlayAsync(CancellationToken token)
        {
            index = 0;
            session = player.Play(sequence, token);
            return await session.Completion;
        }

        public Task OpenAsync(StorySequence value, Guid sessionId, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            transform.SetAsLastSibling();
            gameObject.SetActive(true);
            return Task.CompletedTask;
        }

        public async Task<StoryStepOutcome> ExecuteAsync(StoryNode node, StoryStepContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (node.Kind != StoryNodeKind.Say)
            {
                throw new InvalidOperationException("The camp presenter only supports dialogue nodes.");
            }

            step = context;
            body.text = node.TextKey.Replace("{traveler}", traveler);
            speakerName.text = content.speakers?.Length > 0 ? content.speakers[index] : content.speaker;
            counter.text = (++index) + " / " + content.lines.Length;
            next.GetComponentInChildren<TMP_Text>().text = index == content.lines.Length ? finishLabel : "继续";
            var completion = new TaskCompletionSource<StoryStepOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
            waiting = completion;
            next.onClick.RemoveAllListeners();
            next.onClick.AddListener(() => completion.TrySetResult(StoryStepOutcome.Continue));
            using var registration = token.Register(() => completion.TrySetCanceled(token));
            try
            {
                return await completion.Task;
            }
            finally
            {
                if (ReferenceEquals(waiting, completion))
                {
                    waiting = null;
                    step = null;
                    if (next != null)
                    {
                        next.onClick.RemoveAllListeners();
                    }
                }
            }
        }

        public void Advance(StoryStepContext context)
        {
            if (step != null && context.SessionId == step.SessionId && context.StepVersion == step.StepVersion)
            {
                waiting?.TrySetResult(StoryStepOutcome.Continue);
            }
        }

        public Task CloseAsync(Guid sessionId)
        {
            if (this != null)
            {
                gameObject.SetActive(false);
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            player?.Dispose();
        }

        private void OnDestroy()
        {
            Dispose();
        }
    }
}
