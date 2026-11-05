using System;
using System.Threading.Tasks;
using Game.Framework;
using UnityEngine;

namespace Game
{
    /// <summary>
    /// Bootstrap 场景中的游戏启动入口，单例功能复用 Framework 的 MonoSingleton。
    /// 在 Inspector 中配置 startupModules：按列表顺序初始化，全部就绪后更新，退出时逆序清理。
    /// 默认列表为空，运行后只输出 GAME_STARTER_READY；项目自己的初始化逻辑写在 StartupModule 子类中。
    /// </summary>
    public sealed class GameEntry : MonoSingleton<GameEntry>
    {
        // 将项目的启动模块组件拖到这个列表中；被依赖的模块放前面。
        [SerializeField] private StartupModule[] startupModules = Array.Empty<StartupModule>();

        // 记录上次运行的入口，兼容 Unity 关闭 Domain Reload 后保留静态字段的情况。
        private static GameEntry sessionOwner;

        // 每次运行创建一个会话，由它协调模块的初始化、更新、取消和清理。
        private GameSession session;

        // 关闭过程中禁止通过单例访问再次启动。
        private bool closing;

        /// <summary>当前会话的启动状态；尚未创建或已经移除会话时返回 NotStarted。</summary>
        public StartupState State => session?.State ?? StartupState.NotStarted;

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
                if (entry.gameObject.activeInHierarchy && !entry.IsDestroying)
                {
                    entry.closing = false;
                    // Instance 会调用 OnStart；已启动的入口会被下方判断跳过。
                    _ = Instance;
                    break;
                }
        }

        // Framework 会在单例 Awake 或访问 Instance 时调用此钩子。
        protected override void OnStart()
        {
            // 单例可能被多次访问，同一会话只启动一次。
            if (closing || session != null) return;
            sessionOwner = this;
            var current = new GameSession(Debug.LogException);
            session = current;

            // Unity 生命周期回调不能等待 Task，在异步方法中统一处理完成和异常。
            _ = StartSessionAsync(current);
        }

        // 等待全部模块初始化结束；用 current 区分本次启动和后来创建的新会话。
        private async Task StartSessionAsync(GameSession current)
        {
            try
            {
                await current.StartAsync(startupModules);
                // 旧会话的异步操作结束时，不能给已经关闭或替换的会话报告就绪。
                if (ReferenceEquals(session, current)) Debug.Log("GAME_STARTER_READY");
            }
            // 退出或销毁入口会主动取消旧会话，这种取消属于正常结束。
            catch (OperationCanceledException) when (!ReferenceEquals(session, current)) { }
            catch (Exception exception) { if (ReferenceEquals(session, current)) Debug.LogException(exception); }
        }

        // 每帧将时间交给会话；会话只在所有模块就绪后调用模块 Tick。
        private void Update() { if (!IsDestroying) session?.Tick(Time.deltaTime, Time.unscaledDeltaTime); }

        // 两个 Unity 回调都可能到达，CloseSession 支持重复调用。
        protected override void OnApplicationQuit() { base.OnApplicationQuit(); CloseSession(); }
        protected override void OnDestroy() { base.OnDestroy(); CloseSession(); }

        // 停止更新、取消正在进行的初始化，并释放本次启动涉及的模块。
        private void CloseSession()
        {
            closing = true;
            var previous = session;
            // 先断开当前引用，防止异步完成回调或清理中的重入继续使用旧会话。
            session = null;
            previous?.Dispose();
            if (ReferenceEquals(sessionOwner, this)) sessionOwner = null;
        }
    }
}
