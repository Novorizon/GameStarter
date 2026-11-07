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
        private UpdateSettings settings;
        private object startupContext;
        public static string GetPlatformId() => "windows-x64";

        public void Initialize(UpdateSettings settings, object startupContext = null, CancellationToken token = default)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HotUpdateManager));
            }

            if (this.settings != null)
            {
                if (!ReferenceEquals(this.settings, settings) || !ReferenceEquals(this.startupContext, startupContext))
                {
                    throw new InvalidOperationException("Hot manager is already configured.");
                }

                return;
            }

            settings.Validate();
            token.ThrowIfCancellationRequested();
            this.settings = settings;
            this.startupContext = startupContext;
            ManagerContracts.LastUpdateSettings = settings;
            ManagerContracts.StartupSteps.Add("hot-configure");
            lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        }

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

            Initialize(settings, startupContext, token);
            return StartAsync();
        }

        public Task StartAsync()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(HotUpdateManager));
            }

            if (settings == null)
            {
                throw new InvalidOperationException("Initialize the hot manager first.");
            }

            if (startup != null)
            {
                return startup;
            }

            lifetime.Token.ThrowIfCancellationRequested();
            ManagerContracts.HotCalls.Add("start");
            ManagerContracts.StartupSteps.Add("hot-start");
            startup = RunAsync(settings, startupContext, lifetime.Token);
            return startup;
        }

        private async Task RunAsync(UpdateSettings settings, object startupContext, CancellationToken token)
        {
            if (settings.prepareContentAsync != null)
            {
                await settings.prepareContentAsync(new ReleaseContentContext("compiled", settings.content, true), token);
            }

            await ManagerContracts.HotInitialization(token);
            token.ThrowIfCancellationRequested();
            ManagerContracts.StartupSteps.Add("hot-game");
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
