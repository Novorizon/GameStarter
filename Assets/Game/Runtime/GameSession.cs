using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    /// <summary>一次模块启动流程的状态。</summary>
    public enum StartupState
    {
        /// <summary>尚未开始初始化。</summary>
        NotStarted,
        /// <summary>正在按顺序等待模块初始化。</summary>
        Starting,
        /// <summary>所有模块初始化成功，可以进行每帧更新。</summary>
        Ready,
        /// <summary>初始化或更新发生异常，已中止流程并清理模块。</summary>
        Failed,
        /// <summary>会话已被主动关闭。</summary>
        Stopped
    }

    /// <summary>
    /// 一次运行的模块调度器，由 GameEntry 创建并驱动，本身不挂到场景上。
    /// 负责顺序初始化、就绪后更新，以及关闭或失败时的取消和逆序清理。
    /// </summary>
    internal sealed class GameSession : IDisposable
    {
        // 整个会话共用的取消信号，关闭或失败时通知正在初始化的模块停止工作。
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();

        // 记录已经进入初始化的模块，包含初始化未完成或抛出异常的模块，以便清理部分资源。
        private readonly List<IStartupModule> started = new List<IStartupModule>();

        // 日志由入口传入，使这个调度器不需要直接调用 Unity 的日志 API。
        private readonly Action<Exception> log;

        // 保存同一次启动的完成结果；重复调用 StartAsync 时返回同一个 Task。
        private TaskCompletionSource<bool> completion;
        private bool stopped;

        // 就绪后直接遍历此数组，避免每帧复制列表和分配内存。
        private IStartupModule[] readyModules = Array.Empty<IStartupModule>();

        /// <summary>会话初始值为 NotStarted，随后随启动、失败或关闭而更新。</summary>
        public StartupState State
        {
            get; private set;
        }

        internal GameSession(Action<Exception> log)
        {
            this.log = log;
        }

        /// <summary>检查模块列表后开始初始化；返回的 Task 报告整个启动流程的成功、失败或取消。</summary>
        internal Task StartAsync(IEnumerable<IStartupModule> modules)
        {
            if (completion != null)
                return completion.Task;
            if (stopped)
                throw new ObjectDisposedException(nameof(GameSession));

            // 固定本次启动使用的列表，并在执行任何模块之前拒绝空项和重复项。
            var selected = modules.ToArray();
            var unique = new HashSet<IStartupModule>();
            foreach (var module in selected)
                if (module == null || !unique.Add(module))
                    throw new ArgumentException("Startup modules must be non-null and unique.");

            // 先保存共享 Task，再启动异步流程，防止模块初始化期间重入造成重复启动。
            completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            State = StartupState.Starting;
            _ = InitializeAsync(selected, lifetime.Token);
            return completion.Task;
        }

        private async Task InitializeAsync(IStartupModule[] modules, CancellationToken token)
        {
            try
            {
                foreach (var module in modules)
                {
                    token.ThrowIfCancellationRequested();
                    // 必须先登记再初始化：本模块即使中途失败，也要获得 Shutdown 清理机会。
                    started.Add(module);
                    await module.InitializeAsync(token);
                }

                // 最后一个模块等待期间也可能发生关闭，取消后不能再进入 Ready。
                token.ThrowIfCancellationRequested();
                readyModules = modules;
                State = StartupState.Ready;
                completion.TrySetResult(true);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception exception)
            {
                // 任一模块失败就中止后续启动，并逆序释放已经开始初始化的模块。
                if (!stopped)
                {
                    State = StartupState.Failed;
                    CancelLifetime();
                    ReleaseModules();
                }
                completion.TrySetException(exception);
            }
        }

        // 启动中、失败或停止时都不向模块派发更新。
        internal void Tick(float deltaTime, float unscaledDeltaTime)
        {
            if (State != StartupState.Ready)
                return;
            // 模块可能在自己的 Tick 中关闭入口，因此保留本轮数组，并在每次调用前检查状态。
            var snapshot = readyModules;
            foreach (var module in snapshot)
            {
                if (State != StartupState.Ready)
                    break;
                try
                {
                    module.Tick(deltaTime, unscaledDeltaTime);
                }
                // 更新异常也会停止整次会话，避免其他模块在不完整的系统状态下继续运行。
                catch (Exception exception)
                {
                    State = StartupState.Failed;
                    CancelLifetime();
                    ReleaseModules();
                    log(exception);
                    break;
                }
            }
        }

        private void ReleaseModules()
        {
            // 先清空可更新模块和清理列表，再执行用户清理代码，避免重入时再次释放。
            readyModules = Array.Empty<IStartupModule>();
            var snapshot = started.ToArray();
            started.Clear();
            // 后启动的模块通常依赖先启动的模块，所以先清理后启动者。
            for (int i = snapshot.Length - 1; i >= 0; i--)
                try
                {
                    snapshot[i].Shutdown();
                }
                // 一个模块清理失败不能阻止其他模块释放资源。
                catch (Exception exception) { log(exception); }
        }

        private void CancelLifetime()
        {
            // 模块注册的取消回调可能抛异常；记录后仍要继续后续清理。
            try
            {
                lifetime.Cancel();
            }
            catch (Exception exception) { log(exception); }
        }

        /// <summary>关闭会话：停止更新、发出取消信号、清理模块；重复调用不会重复清理。</summary>
        public void Dispose()
        {
            if (stopped)
                return;
            stopped = true;
            State = StartupState.Stopped;
            CancelLifetime();
            ReleaseModules();
            lifetime.Dispose();
        }
    }
}
