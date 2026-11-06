// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

// 这里只验证 GameEntry 的调用顺序；真实加载器由包仓库的独立用例验证。
namespace Game.Framework.HotUpdate
{
    public sealed class HotUpdateManager : ConfigurableSingleton<HotUpdateManager>, IDisposable
    {
        private CancellationTokenSource lifetime;
        private Task startup;
        private bool disposed;
        private IGameModule hotGame;
        public static HotUpdateManager Instance => GetOrCreateInstance(() => new HotUpdateManager());
        public Task InitializeAsync(UpdateSettings settings, CancellationToken token)
        {
            return InitializeAsync(settings, null, token);
        }

        public Task InitializeAsync(UpdateSettings settings, object startupContext, CancellationToken token)
        {
            if (startup != null)
            {
                return startup;
            }

            settings.Validate();
            ManagerContracts.HotCalls.Add("start");
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            startup = StartAsync(settings, startupContext, lifetime.Token);
            return startup;
        }

        private async Task StartAsync(UpdateSettings settings, object startupContext, CancellationToken token)
        {
            if (settings.prepareContentAsync != null)
            {
                await settings.prepareContentAsync(new ReleaseContentContext("compiled", settings.content, true), token);
            }

            await ManagerContracts.HotInitialization(token);
            token.ThrowIfCancellationRequested();
            hotGame = ManagerContracts.HotGameFactory?.Invoke();
            if (hotGame != null)
            {
                await hotGame.InitializeAsync(new ModelHost(startupContext), token);
            }
        }

        private sealed class ModelHost : IGameContextHost
        {
            public object StartupContext
            {
                get;
            }

            public ModelHost(object startupContext)
            {
                StartupContext = startupContext;
            }

            public void Log(string message)
            {
            }

            public void ReportReady(string moduleVersion)
            {
            }
        }

        public void NotifyApplicationPause(bool paused)
        {
            (hotGame as IGameApplicationEvents)?.OnApplicationPause(paused);
        }

        public void NotifyApplicationFocus(bool focused)
        {
            (hotGame as IGameApplicationEvents)?.OnApplicationFocus(focused);
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            ManagerContracts.HotCalls.Add("stop");
            ManagerContracts.Cleanup.Add("hot");
            lifetime?.Cancel();
            hotGame?.Shutdown();
            hotGame = null;
            lifetime?.Dispose();
            ReleaseInstance(this);
        }
    }
}
