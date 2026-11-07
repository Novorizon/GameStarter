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
    /// <summary>抵达剧情的营地呈现器；推进、取消和结果由 SDK StoryPlayer 负责。</summary>
    public sealed class CampArrivalView : MonoBehaviour, IStoryPresenter, IDisposable
    {
        [Serializable]
        private sealed class Script
        {
            public string id;
            public string version;
            public string title;
            public string speaker;
            public string[] lines;
        }

        private StoryPlayer player;
        private StorySession session;
        private StorySequence sequence;
        private Script content;
        private TMP_Text body;
        private TMP_Text counter;
        private Button next;
        private TaskCompletionSource<StoryStepOutcome> waiting;
        private StoryStepContext step;
        private int index;
        private bool disposed;
        public bool Playing => player != null && player.IsPlaying;

        public static CampArrivalView Create(Transform parent, ClientArt art)
        {
            var ui = new CampUI(art);
            var root = CampUI.Overlay("CampArrivalStory", parent);
            var view = root.gameObject.AddComponent<CampArrivalView>();
            view.content = JsonUtility.FromJson<Script>(art.ArrivalScript);
            if (view.content?.lines == null || view.content.lines.Length == 0 || view.content.lines.Any(string.IsNullOrWhiteSpace))
            {
                throw new InvalidOperationException("The island arrival story has no valid dialogue.");
            }

            var nodes = view.content.lines.Select((line, i) => new StoryNode("shore-" + i, StoryNodeKind.Say, textKey: line)).ToList();
            nodes.Add(new StoryNode("camp-ready", StoryNodeKind.End));
            view.sequence = new StorySequence(view.content.id, view.content.version, nodes);
            var panel = ui.Panel("ArrivalDialogue", root, .16f, .18f, .84f, .68f);
            ui.Text("Chapter", panel, .055f, .79f, .84f, .92f, 30, view.content.title, CampUI.Gold);
            ui.Text("Speaker", panel, .055f, .66f, .90f, .78f, 25, view.content.speaker, CampUI.Muted);
            view.body = ui.Text("Dialogue", panel, .055f, .28f, .94f, .65f, 28, "", CampUI.Cream);
            view.counter = ui.Text("Step", panel, .055f, .09f, .35f, .23f, 21, "", CampUI.Muted);
            view.next = ui.ActionButton("ArrivalContinue", panel, .64f, .07f, .94f, .24f, "继续", () => view.session?.Advance());
            ui.ActionButton("ArrivalSkip", root, .83f, .87f, .96f, .95f, "跳过序章", () => view.session?.Skip());
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
            gameObject.SetActive(true);
            return Task.CompletedTask;
        }

        public async Task<StoryStepOutcome> ExecuteAsync(StoryNode node, StoryStepContext context, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (node.Kind != StoryNodeKind.Say)
            {
                throw new InvalidOperationException("The arrival presenter only supports dialogue nodes.");
            }

            step = context;
            body.text = node.TextKey;
            counter.text = (++index) + " / " + content.lines.Length;
            next.GetComponentInChildren<TMP_Text>().text = index == content.lines.Length ? "从这里开始" : "继续";
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
