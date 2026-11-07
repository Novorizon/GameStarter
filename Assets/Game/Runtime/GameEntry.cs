using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.HotUpdate;
using Game.Resource;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    /// <summary>场景入口：配置开关，按顺序初始化 Manager，然后执行热更。</summary>
    public sealed partial class GameEntry : MonoSingleton<GameEntry>
    {
        [SerializeField]
        private bool initializeHotUpdate = true;
        [SerializeField]
        private ResourceMode resourceMode = ResourceMode.Online;
        [SerializeField]
        private bool simulateResourcesInEditor = true;
        [SerializeField]
        private InputActionAsset inputActions;
        [SerializeField]
        private string initialInputMap = "Gameplay";
        private GameHotUpdate hotUpdate;
        private CancellationTokenSource cts;
        private Task startupTask;
        private string startupError;

        public StartupState State { get; private set; }
        public ResourceDownloadProgress DownloadProgress => hotUpdate?.DownloadProgress ?? default;
        public string StartupMessage => startupError ?? hotUpdate?.StartupMessage;

        protected override void OnStart()
        {
            cts = new CancellationTokenSource();
            startupError = null;
            State = StartupState.Starting;
            startupTask = InitializeAsync();
            ObserveNativeStartup();
        }

        private async Task InitializeAsync()
        {
            var token = cts.Token;
            GameHotUpdate current = null;
            try
            {
                current = new GameHotUpdate(resourceMode, simulateResourcesInEditor);
                hotUpdate = current;
                // 先初始化资源文件系统；发行版本、清单和下载稍后由热更流程决定。
                if (!await ResourceManager.Instance.InitializePackageAsync(current.CreateResourceParameters(), token))
                {
                    throw new InvalidOperationException("Resource system initialization failed.");
                }

                token.ThrowIfCancellationRequested();
                var settings = current.CreateSettings(initializeHotUpdate);
                var options = new GameStartupOptions(inputActions, initialInputMap);
                HotUpdateManager.Instance.Initialize(settings, options, token);
                await current.RunAsync(token);
                token.ThrowIfCancellationRequested();
                State = StartupState.Ready;
                Debug.Log("GAME_STARTER_READY");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (ReferenceEquals(hotUpdate, current))
                {
                    State = StartupState.Failed;
                    startupError = error.Message;
                    Debug.LogException(error);
                }
            }
        }

        protected override void OnExit()
        {
            var stopping = cts;
            cts = null;
            hotUpdate?.Dispose();
            hotUpdate = null;
            startupTask = null;
            State = StartupState.NotStarted;
            try
            {
                stopping?.Cancel();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }

            HotUpdateManager.Release();
            ResourceManager.Release();
            stopping?.Dispose();
        }

        private void OnApplicationPause(bool paused)
        {
            if (HotUpdateManager.TryGetInstance(out var update))
            {
                update.NotifyApplicationPause(paused);
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (HotUpdateManager.TryGetInstance(out var update))
            {
                update.NotifyApplicationFocus(focused);
            }
        }

        public Task ShutdownAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }
    }
}
