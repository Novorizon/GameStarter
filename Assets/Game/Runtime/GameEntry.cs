using System;
using System.Threading;
using System.Threading.Tasks;
using ECS;
using Game.Framework;
using Game.Framework.Audio;
using Game.Framework.Config;
using Game.Framework.Data;
using Game.Framework.Input;
using Game.Framework.Storage;
using Game.Framework.Event;
using Game.Framework.Network;
using Game.Framework.Network.Unity;
using Game.Framework.Timing;
using Game.Resource;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using GameSceneManager = Game.Framework.Scenes.SceneManager;

namespace Game
{
    /// <summary>
    /// Bootstrap 场景中的游戏启动入口，单例功能复用 Framework 的 MonoSingleton。
    /// 先在 InitializeManagersAsync 中准备公共 Manager，再按 startupModules 列表执行额外启动模块。
    /// 公共服务由各库管理自己的运行生命周期；启动模块由 GameSession 更新并在退出时逆序清理。
    /// </summary>
    public sealed class GameEntry : MonoSingleton<GameEntry>
    {
        // 将项目的启动模块组件拖到这个列表中；被依赖的模块放前面。
        [SerializeField]
        private StartupModule[] startupModules = Array.Empty<StartupModule>();
        // 项目配置好 YooAsset 的 DefaultPackage 采集和资源包后开启；空模板先使用 UI 默认的 Resources 加载器。
        [SerializeField]
        private bool initializeYooAsset;
        // 项目自己的 .inputactions；留空时读取 Resources/InputActions。
        [SerializeField]
        private InputActionAsset inputActions;
        [SerializeField]
        private string initialInputMap = "Gameplay";
        // 记录上次运行的入口，兼容 Unity 关闭 Domain Reload 后保留静态字段的情况。
        private static GameEntry sessionOwner;
        // 每次运行创建一个会话，由它协调模块的初始化、更新、取消和清理。
        private GameSession session;
        private Task startupTask;
        private ManagerLifetime managers;
        private Task shutdownTask = Task.CompletedTask;
        // 新入口等待旧入口的异步清理，防止旧资源关闭流程覆盖新启动。
        private static Task serviceShutdown = Task.CompletedTask;
        // 关闭过程中禁止通过单例访问再次启动。
        private bool closing;
        private bool startupFailed;
        /// <summary>当前会话的启动状态；尚未创建或已经移除会话时返回 NotStarted。</summary>
        public StartupState State => session == null ? StartupState.NotStarted : startupFailed ? StartupState.Failed : session.State == StartupState.NotStarted ? StartupState.Starting : session.State;

        // 每次进入运行时都会调用，包括编辑器关闭 Domain Reload 的情况。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSession()
        {
            if (sessionOwner != null)
            {
                // 先清理上次运行留下的模块，再允许保留的场景组件重新启动。
                var previous = sessionOwner;
                previous.CloseSession();
                previous.closing = false;
            }

            sessionOwner = null;
            ResetSingletonReference();
        }

        // 场景加载后访问已有入口；兼容关闭 Scene Reload、不会重新执行 Awake 的情况。
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void ResumeSession()
        {
            foreach (var entry in FindObjectsOfType<GameEntry>(true))
            {
                if (entry.gameObject.activeInHierarchy && !entry.IsDestroying)
                {
                    entry.closing = false;
                    // Instance 会调用 OnStart；已启动的入口会被下方判断跳过。
                    _ = Instance;
                    break;
                }
            }
        }

        // Framework 会在单例 Awake 或访问 Instance 时调用此钩子。
        protected override void OnStart()
        {
            // 单例可能被多次访问，同一会话只启动一次。
            if (closing || session != null)
            {
                return;
            }

            startupFailed = false;
            sessionOwner = this;
            var current = new GameSession(Debug.LogException);
            session = current;
            // Unity 生命周期回调不能等待 Task，在异步方法中统一处理完成和异常。
            var owned = new ManagerLifetime(Debug.LogException);
            managers = owned;
            startupTask = StartSessionAsync(current, owned, serviceShutdown);
        }

        /// <summary>供热更新入口等待公共 Manager 和启动模块就绪；入口关闭、启动失败或调用取消时不会继续。</summary>
        public async Task WaitUntilReadyAsync(CancellationToken token = default)
        {
            var current = session;
            var pending = startupTask;
            if (current == null || pending == null)
            {
                throw new InvalidOperationException("GameEntry startup has not begun.");
            }

            await TaskUtil.WaitAsync(pending, token);
            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(session, current))
            {
                throw new OperationCanceledException("GameEntry startup has ended.");
            }

            if (State != StartupState.Ready)
            {
                throw new InvalidOperationException("GameEntry startup failed. Check the initialization errors in Console.");
            }
        }

        /// <summary>项目所需的公共 Manager 在这里集中准备；按依赖顺序添加自己的初始化调用。</summary>
        private async Task InitializeManagersAsync(GameSession current, ManagerLifetime owned)
        {
            // 主线程、事件和时钟的宿主属于 Framework；这里借用，不创建第二个驱动。
            _ = EventManager.Instance;
            _ = TimerManager.Instance;
            var resources = ResourceManager.Instance;
            if (initializeYooAsset && !resources.Initialized)
            {
                // 先登记，初始化中途失败也能释放部分资源。
                owned.Own(resources.ShutdownAsync);
                if (!await TaskUtil.WaitAsync(resources.InitializeAsync(), owned.Token))
                {
                    throw new InvalidOperationException("YooAsset DefaultPackage initialization failed.");
                }
            }

            owned.Token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(session, current))
            {
                throw new OperationCanceledException();
            }

            if (!DataManager.TryGetInstance(out var data))
            {
                data = DataManager.Instance;
                owned.Own(data.Dispose);
            }

            bool hasStorage = LocalStorageManager.TryGetInstance(out var storage);
            if (!hasStorage)
            {
                storage = LocalStorageManager.Instance;
            }

            if (!hasStorage || storage.RootDirectory == null)
            {
                owned.Own(storage.Dispose);
            }

            if (storage.RootDirectory == null)
            {
                storage.Initialize();
            }

            var input = InputManager.Instance;
            if (!input.Initialized)
            {
                owned.Own(input.Dispose);
                var source = inputActions != null ? inputActions : Resources.Load<InputActionAsset>("InputActions");
                input.Initialize(source, source != null ? initialInputMap : null);
            }

            var scenes = GameSceneManager.Instance;
            if (!scenes.Initialized)
            {
                owned.Own(scenes.Dispose);
                scenes.Initialize(initializeYooAsset ? resources.CreateSceneLoader() : null);
            }

            bool ownsAudio = !AudioManager.TryGetInstance(out _);
            var audio = AudioManager.Instance;
            if (ownsAudio)
            {
                owned.Own(() => DestroyOwnedAsync(audio, () =>
                {
                    if (AudioManager.TryGetInstance(out var value) && ReferenceEquals(value, audio))
                    {
                        AudioManager.DestroyInstance();
                    }
                }));
            }

            audio.Initialize();
            var config = ConfigManager.Instance;
            if (!config.Initialized)
            {
                owned.Own(config.Dispose);
                if (initializeYooAsset)
                {
                    config.Initialize(async (location, token) =>
                    {
                        using (var text = await resources.AcquireAsync<TextAsset>(location, token))
                        {
                            return text.Asset.text;
                        }
                    }, async (location, token) =>
                    {
                        using (var binary = await resources.AcquireAsync<TextAsset>(location, token))
                        {
                            return binary.Asset.bytes;
                        }
                    });
                }
                else
                {
                    config.Initialize();
                }
            }

            bool ownsUI = !UIManager.TryGetInstance(out var existingUI) || !existingUI.IsInitialized;
            var ui = UIManager.Instance;
            if (ownsUI)
            {
                owned.Own(async () =>
                {
                    await ui.ShutdownAsync();
                    await DestroyOwnedAsync(ui, () =>
                    {
                        if (UIManager.TryGetInstance(out var value) && ReferenceEquals(value, ui))
                        {
                            UIManager.DestroyInstance();
                        }
                    });
                });
            }

            ui.Initialize();
            if (initializeYooAsset && ownsUI)
            {
                ui.SetAssetLoader(new YooAssetUIAssetLoader(resources));
            }

            bool ownsGpu = !GpuAnimatorManager.TryGetInstance(out _);
            var gpu = GpuAnimatorManager.Instance;
            if (ownsGpu)
            {
                owned.Own(() => DestroyOwnedAsync(gpu, () =>
                {
                    if (GpuAnimatorManager.TryGetInstance(out var value) && ReferenceEquals(value, gpu))
                    {
                        GpuAnimatorManager.DestroyInstance();
                    }
                }));
            }

            gpu.Initialize();
            bool ownsEntities = !EntityManager.TryGetInstance(out _);
            var entities = EntityManager.Instance;
            if (ownsEntities)
            {
                owned.Own(() => DestroyOwnedAsync(entities, () =>
                {
                    if (EntityManager.TryGetInstance(out var value) && ReferenceEquals(value, entities))
                    {
                        EntityManager.DestroyInstance();
                    }
                }));
            }

            bool ownsWorlds = !WorldManager.TryGetInstance(out _);
            var worlds = WorldManager.Instance;
            if (ownsWorlds)
            {
                owned.Own(() => DestroyOwnedAsync(worlds, () =>
                {
                    if (WorldManager.TryGetInstance(out var value) && ReferenceEquals(value, worlds))
                    {
                        WorldManager.DestroyInstance();
                    }
                }));
            }

            worlds.Initialize();
            if (Resources.Load<NetworkSettings>("NetworkSettings") != null)
            {
                var network = NetworkManager.Instance;
                if (!network.IsInitialized)
                {
                    owned.Own(async () => await network.DisposeAsync());
                    network.Initialize();
                }
            }
        }

        private static async Task DestroyOwnedAsync(UnityEngine.Object value, Action destroy)
        {
            destroy();
            // SubsystemRegistration 可能已清空单例引用，但旧入口仍拥有这个原生对象。
            if (value != null)
            {
                if (value is Component component)
                {
                    UnityEngine.Object.Destroy(component.gameObject);
                }
                else
                {
                    UnityEngine.Object.Destroy(value);
                }
            }

            // Destroy 在 Player 中延迟到帧尾；依赖资源必须保留到原生销毁完成。
            while (value != null)
            {
                await Task.Yield();
            }
        }

        // 确认这次异步启动仍然有效，入口关闭或重新启动后忽略旧结果。
        private async Task StartSessionAsync(GameSession current, ManagerLifetime owned, Task previousShutdown)
        {
            try
            {
                await TaskUtil.WaitAsync(previousShutdown, owned.Token);
                owned.Token.ThrowIfCancellationRequested();
                await InitializeManagersAsync(current, owned);
                if (!ReferenceEquals(session, current))
                {
                    return;
                }

                await current.StartAsync(startupModules);
                // 旧会话的异步操作结束时，不能给已经关闭或替换的会话报告就绪。
                if (ReferenceEquals(session, current))
                {
                    Debug.Log("GAME_STARTER_READY");
                }
            }
            // 退出或销毁入口会主动取消旧会话，这种取消属于正常结束。
            catch (OperationCanceledException) when (!ReferenceEquals(session, current))
            {
            }
            catch (Exception exception)
            {
                if (ReferenceEquals(session, current))
                {
                    startupFailed = true;
                    Debug.LogException(exception);
                    current.Dispose();
                    await owned.StopAsync();
                }
            }
        }

        // 每帧将时间交给会话；会话只在所有模块就绪后调用模块 Tick。
        private void Update()
        {
            if (!IsDestroying)
            {
                session?.Tick(Time.deltaTime, Time.unscaledDeltaTime);
                if (!startupFailed && session?.State == StartupState.Failed)
                {
                    startupFailed = true;
                    _ = managers.StopAsync();
                }
            }
        }

        // 两个 Unity 回调都可能到达，CloseSession 支持重复调用。
        protected override void OnApplicationQuit()
        {
            base.OnApplicationQuit();
            CloseSession();
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            CloseSession();
        }

        // 停止更新、取消正在进行的初始化，并释放本次启动涉及的模块。
        private void CloseSession() => _ = ShutdownAsync();
        /// <summary>停止启动模块，再逆序释放本入口拥有的公共服务。切换入口或主动退出时可等待此任务。</summary>
        public Task ShutdownAsync()
        {
            if (closing && session == null)
            {
                return shutdownTask;
            }

            closing = true;
            var previous = session;
            var owned = managers;
            session = null;
            managers = null;
            // 发布后再执行模块回调；回调重入 ShutdownAsync 时取得同一份未完成任务。
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            shutdownTask = completion.Task;
            serviceShutdown = Task.WhenAll(serviceShutdown, shutdownTask);
            if (ReferenceEquals(sessionOwner, this))
            {
                sessionOwner = null;
            }

            _ = CompleteShutdownAsync(previous, owned, completion);
            return shutdownTask;
        }

        private static async Task CompleteShutdownAsync(GameSession previous, ManagerLifetime owned, TaskCompletionSource<bool> completion)
        {
            try
            {
                previous?.Dispose();
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }

            try
            {
                if (owned != null)
                {
                    await owned.StopAsync();
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
            }

            completion.TrySetResult(true);
        }
    }
}
