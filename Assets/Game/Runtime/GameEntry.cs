using System;
using System.Threading;
using System.Threading.Tasks;
using ECS;
using Game.Framework;
using Game.Framework.Audio;
using Game.Framework.Config;
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
        // 配好 YooAsset DefaultPackage 的采集和构建后开启；默认使用 Resources。
        [SerializeField]
        private bool initializeYooAsset;
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
            startupTask = StartSessionAsync(session);
        }

        private async Task InitializeManagersAsync(CancellationToken token)
        {
            _ = EventManager.Instance;
            _ = TimerManager.Instance;
            if (initializeYooAsset)
            {
                bool ready = await TaskUtil.WaitAsync(ResourceManager.Instance.InitializeAsync(), token);
                if (!ready)
                {
                    throw new InvalidOperationException("YooAsset DefaultPackage initialization failed.");
                }
            }

            token.ThrowIfCancellationRequested();
            LocalStorageManager.Instance.Initialize();
            var source = inputActions != null ? inputActions : Resources.Load<InputActionAsset>("InputActions");
            InputManager.Instance.Initialize(source, source != null ? initialInputMap : null);
            GameSceneManager.Instance.Initialize(initializeYooAsset ? ResourceManager.Instance.CreateSceneLoader() : null);
            AudioManager.Instance.Initialize();
            if (initializeYooAsset)
            {
                ConfigManager.Instance.Initialize(LoadConfigTextAsync, LoadConfigBytesAsync);
                UIManager.Instance.SetAssetLoader(new YooAssetUIAssetLoader(ResourceManager.Instance));
            }
            else
            {
                ConfigManager.Instance.Initialize();
            }

            UIManager.Instance.Initialize();
            GpuAnimatorManager.Instance.Initialize();
            _ = EntityManager.Instance;
            WorldManager.Instance.Initialize();
            if (Resources.Load<NetworkSettings>("NetworkSettings") != null)
            {
                NetworkManager.Instance.Initialize();
            }
        }

        private static async Task<string> LoadConfigTextAsync(string location, CancellationToken token)
        {
            using (var asset = await ResourceManager.Instance.AcquireAsync<TextAsset>(location, token))
            {
                return asset.Asset.text;
            }
        }

        private static async Task<byte[]> LoadConfigBytesAsync(string location, CancellationToken token)
        {
            using (var asset = await ResourceManager.Instance.AcquireAsync<TextAsset>(location, token))
            {
                return asset.Asset.bytes;
            }
        }

        protected override void OnExit()
        {
            var previous = session;
            session = null;
            HotUpdateManager.Release();
            previous?.Dispose();
            NetworkManager.Release();
            WorldManager.Release();
            EntityManager.Release();
            GpuAnimatorManager.Release();
            UIManager.Release();
            ConfigManager.Release();
            AudioManager.Release();
            GameSceneManager.Release();
            InputManager.Release();
            LocalStorageManager.Release();
            DataManager.Release();
            ResourceManager.Release();
            if (ReferenceEquals(sessionOwner, this))
            {
                sessionOwner = null;
            }
        }

        private async Task StartSessionAsync(GameSession current)
        {
            try
            {
                await InitializeManagersAsync(current.Token);
                current.Token.ThrowIfCancellationRequested();
                await current.StartAsync(startupModules);
                current.Token.ThrowIfCancellationRequested();
                if (initializeHotUpdate)
                {
                    // Editor 使用已编译程序集；Player 补充 AOT 元数据、加载 DLL，再调用 HotGame。
                    await HotUpdateManager.Instance.InitializeAsync(new UpdateSettings(), current.Token);
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
