using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Game
{
    /// <summary>登记本次启动拥有的公共服务，失败或退出时取消初始化并逆序清理。</summary>
    internal sealed class ManagerLifetime
    {
        private readonly List<Func<Task>> cleanup = new List<Func<Task>>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly Action<Exception> log;
        private readonly CancellationToken token;
        private Task stopping;
        public CancellationToken Token => token;

        internal ManagerLifetime(Action<Exception> log)
        {
            this.log = log;
            token = lifetime.Token;
        }

        public void Own(Func<Task> release)
        {
            token.ThrowIfCancellationRequested();
            cleanup.Add(release ?? throw new ArgumentNullException(nameof(release)));
        }

        public void Own(Action release)
        {
            if (release == null)
            {
                throw new ArgumentNullException(nameof(release));
            }

            Own(() =>
            {
                release();
                return Task.CompletedTask;
            });
        }

        public Task StopAsync()
        {
            if (stopping != null)
            {
                return stopping;
            }

            // 先发布，再调用任何用户清理代码，防止重入时重复清理。
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            stopping = completion.Task;
            _ = StopCoreAsync(completion);
            return stopping;
        }

        private async Task StopCoreAsync(TaskCompletionSource<bool> completion)
        {
            try
            {
                lifetime.Cancel();
            }
            catch (Exception error)
            {
                Report(error);
            }

            var previous = cleanup.ToArray();
            cleanup.Clear();
            for (int i = previous.Length - 1; i >= 0; i--)
            {
                try
                {
                    await previous[i]();
                }
                catch (Exception error)
                {
                    Report(error);
                }
            }

            lifetime.Dispose();
            completion.TrySetResult(true);
        }

        private void Report(Exception error)
        {
            try
            {
                log?.Invoke(error);
            }
            catch
            {
                // 日志接收方失败也不能阻断后续服务清理。
            }
        }
    }
}
