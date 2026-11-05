using System;
using System.Threading;
using System.Threading.Tasks;
using ECS;
using Game.Framework;
using Game.Framework.Event;
using Game.Framework.Network;
using Game.Framework.Network.Unity;
using Game.Framework.Timing;
using Game.Resource;
using UI;
using UnityEngine;

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
        [SerializeField] private StartupModule[] startupModules = Array.Empty<StartupModule>();

        // 项目配置好 YooAsset 的 DefaultPackage 采集和资源包后开启；空模板先使用 UI 默认的 Resources 加载器。
        [SerializeField] private bool initializeYooAsset;

        // 记录上次运行的入口，兼容 Unity 关闭 Domain Reload 后保留静态字段的情况。
        private static GameEntry sessionOwner;

        // 每次运行创建一个会话，由它协调模块的初始化、更新、取消和清理。
        private GameSession session;
        private Task startupTask;

        // 关闭过程中禁止通过单例访问再次启动。
        private bool closing;
        private bool startupFailed;

        /// <summary>当前会话的启动状态；尚未创建或已经移除会话时返回 NotStarted。</summary>
        public StartupState State => session == null ? StartupState.NotStarted : startupFailed ? StartupState.Failed :
            session.State == StartupState.NotStarted ? StartupState.Starting : session.State;

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
            startupTask = StartSessionAsync(current);
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
        private async Task InitializeManagersAsync(GameSession current)
        {
            // Framework 已安装主线程和时钟驱动，这里取得共享服务，不再重复驱动它们的 Update。
            _ = EventManager.Instance;
            _ = TimerManager.Instance;

            // 资源管理器本身是共享缓存；完整资源包初始化需要项目实际提供的 YooAsset 配置。
            var resources = ResourceManager.Instance;
            if (initializeYooAsset && !await resources.InitializeAsync())
            {
                throw new InvalidOperationException("YooAsset DefaultPackage initialization failed.");
            }

            // 等待资源初始化期间入口可能已关闭，旧流程不能继续创建 UI、ECS 或网络服务。
            if (!ReferenceEquals(session, current))
            {
                throw new OperationCanceledException();
            }

            // UIManager 会创建页面、弹窗、面板和提示管理器；ECS 单例会建立实体管理器和默认 World。
            UIManager.Instance.Initialize();
            GpuAnimatorManager.Instance.Initialize();
            _ = EntityManager.Instance;
            WorldManager.Instance.Initialize();

            // 有项目网络配置时准备客户端与回调派发器；实际连接由项目流程调用 ConnectAsync。
            if (Resources.Load<NetworkSettings>("NetworkSettings") != null)
            {
                NetworkManager.Instance.Initialize();
            }
        }

        // 确认这次异步启动仍然有效，入口关闭或重新启动后忽略旧结果。
        private async Task StartSessionAsync(GameSession current)
        {
            try
            {
                await InitializeManagersAsync(current);
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
                }
            }
        }

        // 每帧将时间交给会话；会话只在所有模块就绪后调用模块 Tick。
        private void Update()
        {
            if (!IsDestroying)
            {
                session?.Tick(Time.deltaTime, Time.unscaledDeltaTime);
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
        private void CloseSession()
        {
            closing = true;
            var previous = session;
            // 先断开当前引用，防止异步完成回调或清理中的重入继续使用旧会话。
            session = null;
            previous?.Dispose();
            if (ReferenceEquals(sessionOwner, this))
            {
                sessionOwner = null;
            }
        }
    }
}
