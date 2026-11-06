using System;
using System.Threading;
using System.Threading.Tasks;
using ECS;
using Game.Framework;
using Game.Framework.Audio;
using Game.Framework.Data;
using Game.Framework.Event;
using Game.Framework.Input;
using Game.Framework.HotUpdate;
using Game.Framework.Network;
using Game.Framework.Network.Unity;
using Game.Framework.Storage;
using Game.Framework.Timing;
using Game.Resource;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using GameSceneManager = Game.Framework.Scenes.SceneManager;

namespace Game
{
    /// <summary>游戏入口：OnStart 初始化公共 Manager，OnExit 释放；额外启动模块仍由 GameSession 驱动。</summary>
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

        private Task InitializeManagersAsync(CancellationToken token)
        {
            EventManager.Instance.Initialize();
            TimerManager.Instance.Initialize();
            token.ThrowIfCancellationRequested();
            LocalStorageManager.Instance.Initialize();
            var source = inputActions != null ? inputActions : Resources.Load<InputActionAsset>("InputActions");
            InputManager.Instance.Initialize(source, source != null ? initialInputMap : null);
            GameSceneManager.Instance.Initialize(ResourceManager.Instance.CreateSceneLoader());
            AudioManager.Instance.Initialize();
            DataManager.Instance.Initialize(LoadDataBytesAsync);
            UIManager.Instance.SetAssetLoader(new YooAssetUIAssetLoader(ResourceManager.Instance));

            UIManager.Instance.Initialize();
            GpuAnimatorManager.Instance.Initialize();
            EntityManager.Instance.Initialize();
            WorldManager.Instance.Initialize();
            if (Resources.Load<NetworkSettings>("NetworkSettings") != null)
            {
                NetworkManager.Instance.Initialize();
            }

            return Task.CompletedTask;
        }

        private static async Task LoadTablesAsync(CancellationToken token)
        {
            await DataManager.Instance.LoadAsync<cfg.Tables>(CreateTables, GameDataPaths.ManifestLocation, token);
            token.ThrowIfCancellationRequested();
        }

        // DataManager 加载好文件后调用此方法；返回的 Tables 会由它保存。
        private static cfg.Tables CreateTables(Func<string, Luban.ByteBuf> readTable)
        {
            // readTable 是框架传进来的读表方法，Luban 用它读取每张表的二进制数据。
            return new cfg.Tables(readTable);
        }

        private static async Task<byte[]> LoadDataBytesAsync(string location, CancellationToken token)
        {
            using (var asset = await ResourceManager.Instance.AcquireAsync<TextAsset>(location, token))
            {
                return asset.Asset.bytes;
            }
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
            if (!initializeHotUpdate)
            {
                ReleaseManagers();
            }

            ResourceManager.Release();
            if (ReferenceEquals(sessionOwner, this))
            {
                sessionOwner = null;
            }
        }

        private async Task InitializeAsync(GameSession current)
        {
            try
            {
                await InitializeResourcesAsync(current.Token);
                current.Token.ThrowIfCancellationRequested();
                if (initializeHotUpdate)
                {
                    var options = new GameStartupOptions(inputActions, initialInputMap,
                        token => TaskUtil.WaitAsync(current.StartAsync(startupModules), token),
                        ResourceManager.Instance.CreateSceneLoader(),
                        new YooAssetUIAssetLoader(ResourceManager.Instance));
                    await HotUpdateManager.Instance.InitializeAsync(new UpdateSettings(), options, current.Token);
                }
                else
                {
                    await InitializeManagersAsync(current.Token);
                    await StartGameAsync(current);
                }

                current.Token.ThrowIfCancellationRequested();
                startupReady = true;
                Debug.Log("GAME_STARTER_READY");
            }
            catch (OperationCanceledException) when (current.Token.IsCancellationRequested)
            {
            }
            catch (Exception error)
            {
                if (ReferenceEquals(session, current))
                {
                    startupFailed = true;
                    Debug.LogException(error);
                }
            }
        }

        private async Task InitializeResourcesAsync(CancellationToken token)
        {
            bool ready = await TaskUtil.WaitAsync(ResourceManager.Instance.InitializeAsync(), token);
            if (!ready)
            {
                throw new InvalidOperationException("YooAsset DefaultPackage initialization failed.");
            }

            token.ThrowIfCancellationRequested();
        }

        private async Task StartGameAsync(GameSession current)
        {
            // 表已加载成功后，启动模块和后续业务才能查询 DataManager。
            await LoadTablesAsync(current.Token);
            await current.StartAsync(startupModules);
            current.Token.ThrowIfCancellationRequested();
            var example = DataManager.Instance.Get<cfg.Tables>().TbExample.Get(1);
            Debug.Log("LUBAN_EXAMPLE " + example.Id + " " + example.Name);
            // 关闭热更时，本项目的业务系统和首场景从这里启动。
        }

        private void ReleaseManagers()
        {
            NetworkManager.Release();
            WorldManager.Release();
            EntityManager.Release();
            GpuAnimatorManager.Release();
            UIManager.Release();
            AudioManager.Release();
            GameSceneManager.Release();
            InputManager.Release();
            LocalStorageManager.Release();
            DataManager.Release();
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
