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
    /// <summary>AOT 入口：选择发行并准备资源，再交给 HotGame 初始化系统与业务。</summary>
    public sealed partial class GameEntry : MonoSingleton<GameEntry>
    {
        [SerializeField]
        private StartupModule[] startupModules = Array.Empty<StartupModule>();
        [SerializeField]
        private InputActionAsset inputActions;
        [SerializeField]
        private string initialInputMap = "Gameplay";
        // 热更由本入口显式启动；配置直接写 C#，不读取启动 JSON。
        [SerializeField]
        private bool initializeHotUpdate = true;
        private static GameEntry sessionOwner;
        private GameSession session;
        private Task startupTask;
        private bool startupFailed;
        private bool startupReady;
        public ResourceDownloadProgress DownloadProgress { get; private set; }
        public string StartupMessage { get; private set; }

        public StartupState State
        {
            get
            {
                if (session == null)
                {
                    return StartupState.NotStarted;
                }

                if (startupFailed || session.State == StartupState.Failed)
                {
                    return StartupState.Failed;
                }

                if (session.State == StartupState.Ready && startupReady)
                {
                    return StartupState.Ready;
                }

                return session.State == StartupState.Stopped ? StartupState.Stopped : StartupState.Starting;
            }
        }

        protected override void OnStart()
        {
            startupFailed = false;
            startupReady = false;
            sessionOwner = this;
            session = new GameSession(Debug.LogException);
            startupTask = InitializeAsync(session);
        }

        protected override void OnExit()
        {
            Shutdown();
        }

        private void Shutdown()
        {
            var previous = session;
            session = null;
            previous?.Dispose();
            HotUpdateManager.Release();
            ResourceManager.Release();
            if (ReferenceEquals(sessionOwner, this))
            {
                sessionOwner = null;
            }
        }

        private async Task InitializeAsync(GameSession current)
        {
            Task InitializeModulesAsync(CancellationToken token)
            {
                return TaskUtil.WaitAsync(current.StartAsync(startupModules), token);
            }

            try
            {
                StartupMessage = "Selecting code and resource versions";
                var options = new GameStartupOptions(inputActions, initialInputMap, InitializeModulesAsync);
                var settings = GameReleaseConfiguration.CreateSettings();
                settings.loadFromCompiledAssemblies = !initializeHotUpdate;
#if NOVORIZON_COMPILED_GAME
                settings.loadFromCompiledAssemblies = true;
#endif
                settings.prepareContentAsync = PrepareResourcesAsync;
                await HotUpdateManager.Instance.InitializeAsync(settings, options, current.Token);
                current.Token.ThrowIfCancellationRequested();
                startupReady = true;
                StartupMessage = "Ready";
                Debug.Log("GAME_STARTER_READY");
                NativeSmokeExit(0);
            }
            catch (OperationCanceledException) when (current.Token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (ReferenceEquals(session, current))
                {
                    startupFailed = true;
                    StartupMessage = error.Message;
                    Debug.LogException(error);
                    NativeSmokeExit(1);
                }
            }
        }

        private async Task PrepareResourcesAsync(ReleaseContentContext content, CancellationToken token)
        {
            StartupMessage = "Preparing resources " + content.PackageVersion;
            var resources = ResourceManager.Instance;
#if UNITY_EDITOR
            if (resources.Initialized && resources.PackageVersion != content.PackageVersion)
            {
                await resources.ShutdownAsync();
            }

#endif

            var options = new ResourceInitializationOptions
            {
                PackageVersion = content.PackageVersion,
                DownloadAll = true,
                ConcurrentDownloads = 4,
                FailedTryAgain = 2,
                Progress = new Progress<ResourceDownloadProgress>(value =>
                {
                    if (token.IsCancellationRequested || startupReady)
                    {
                        return;
                    }

                    DownloadProgress = value;
                    StartupMessage = "Downloading resources " + value.CompletedCount + "/" + value.TotalCount;
                })
            };
#if UNITY_EDITOR
            options.Parameters = YooAssetsSettings.CreateEditorSimulateModeParameters();
            options.PackageVersion = null;
#else
            await ConfigurePlayerResourcesAsync(resources, options, content, token);
#endif
            if (!await resources.InitializeAsync(options, token))
            {
                throw new InvalidOperationException("Resource initialization/download failed for version " + content.PackageVersion);
            }
        }

        // 单独保留 Player 文件系统准备，Editor 模型验证也编译并测试该分支。
        internal static async Task ConfigurePlayerResourcesAsync(
            ResourceManager resources,
            ResourceInitializationOptions options,
            ReleaseContentContext content,
            CancellationToken token)
        {
            // 此时尚未创建 HotGame，DefaultPackage 由入口独占。
            // 回退时必须销毁旧 Host 文件系统，才能改用内置资源且不依赖下载站。
            var previousPackage = YooAsset.YooAssets.TryGetPackage(GameDataPaths.PackageName);
            if (previousPackage != null && previousPackage.InitializeStatus != YooAsset.EOperationStatus.None)
            {
                await resources.ShutdownAsync();
                var destroy = previousPackage.DestroyAsync();
                await destroy.Task;
                token.ThrowIfCancellationRequested();
                if (destroy.Status != YooAsset.EOperationStatus.Succeed)
                {
                    throw new InvalidOperationException("Could not reset the previous resource package: " + destroy.Error);
                }
            }

            if (content.IsPackaged || string.IsNullOrWhiteSpace(content.PackageVersion))
            {
                options.Parameters = YooAssetsSettings.CreateOfflinePlayModeParameters();
            }
            else
            {
                if (content.PackageName != GameDataPaths.PackageName)
                {
                    throw new InvalidOperationException("The selected release uses another resource package.");
                }

                options.Parameters = YooAssetsSettings.CreateHostPlayModeParameters(
                    GameReleaseConfiguration.ResourceUrl(content.Platform, content.PackageName));
            }
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

        public async Task WaitUntilReadyAsync(CancellationToken token = default)
        {
            var current = session;
            if (current == null || startupTask == null)
            {
                throw new InvalidOperationException("GameEntry startup has not begun.");
            }

            await TaskUtil.WaitAsync(startupTask, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(session, current))
            {
                throw new OperationCanceledException("GameEntry startup has ended.");
            }

            if (State != StartupState.Ready)
            {
                throw new InvalidOperationException("GameEntry startup failed. Check Console for initialization errors.");
            }
        }

        private void Update()
        {
            if (startupReady)
            {
                session?.Tick(Time.deltaTime, Time.unscaledDeltaTime);
            }
        }

        /// <summary>主动退出入口。各 Manager 的异步释放在各自内部继续完成。</summary>
        public Task ShutdownAsync()
        {
            Dispose();
            return Task.CompletedTask;
        }

        static GameEntry()
        {
            // 项目入口先退出，随后各服务基类清理，最后由生成器恢复静态声明。
            SingletonLifecycle.Register(EndPreviousSession, order: -200);
        }

        private static void EndPreviousSession()
        {
            if (sessionOwner != null)
            {
                sessionOwner.EndLifetime();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ResumeSession()
        {
            foreach (var entry in FindObjectsOfType<GameEntry>(true))
            {
                if (entry.gameObject.activeInHierarchy && !entry.IsDestroying)
                {
                    _ = Instance;
                    break;
                }
            }
        }
    }
}
