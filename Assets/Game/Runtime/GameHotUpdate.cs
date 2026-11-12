using System;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.HotUpdate;
using Game.Resource;
using UnityEngine;
using YooAsset;

namespace Game
{
    public enum ResourceMode
    {
        Offline,
        Online
    }

    public enum StartupState
    {
        NotStarted,
        Starting,
        Ready,
        Failed,
        Stopped
    }

    /// <summary>热更步骤：发行配置、资源准备、下载进度与代码读取。Manager 由 GameEntry 初始化和释放。</summary>
    public sealed class GameHotUpdate : IDisposable
    {
        private readonly ResourceMode resourceMode;
        private readonly bool simulateResources;
        private bool acceptingProgress = true;
        private bool disposed;

        public ResourceDownloadProgress DownloadProgress { get; private set; }
        public string StartupMessage { get; private set; } = "初始化资源系统";
        public float Progress { get; private set; } = .03f;

        public GameHotUpdate(ResourceMode resourceMode, bool simulateResourcesInEditor = true)
        {
#if NOVORIZON_COMPILED_GAME
            this.resourceMode = ResourceMode.Offline;
#else
            this.resourceMode = resourceMode;
#endif
#if UNITY_EDITOR
            simulateResources = simulateResourcesInEditor;
#endif
        }

        public InitializeParameters CreateResourceParameters()
        {
            ValidateMode();
            if (simulateResources)
            {
                return YooAssetsSettings.CreateEditorSimulateModeParameters();
            }

            if (resourceMode == ResourceMode.Offline)
            {
                return YooAssetsSettings.CreateOfflinePlayModeParameters();
            }

#if UNITY_EDITOR
            const string platform = "compiled";
#else
            string platform = HotUpdateManager.GetPlatformId();
#endif
            return YooAssetsSettings.CreateHostPlayModeParameters(GameReleaseConfiguration.ResourceUrl(platform, GameDataPaths.PackageName));
        }

        public UpdateSettings CreateSettings(bool initializeHotUpdate)
        {
            ValidateMode();
            var settings = GameReleaseConfiguration.CreateSettings();
            settings.usePackagedReleaseOnly = resourceMode == ResourceMode.Offline;
            settings.enableRemote &= resourceMode == ResourceMode.Online && !simulateResources;
            settings.loadFromCompiledAssemblies = !initializeHotUpdate;
#if NOVORIZON_COMPILED_GAME
            settings.loadFromCompiledAssemblies = true;
#endif
            settings.prepareContentAsync = PrepareResourcesAsync;
            settings.loadCodeAsync = LoadCodeAsync;
            return settings;
        }

        private void ValidateMode()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GameHotUpdate));
            }

            if (resourceMode != ResourceMode.Offline && resourceMode != ResourceMode.Online)
            {
                throw new ArgumentOutOfRangeException(nameof(resourceMode));
            }
        }

        public async Task RunAsync(CancellationToken token)
        {
            ValidateMode();
            token.ThrowIfCancellationRequested();
            if (!HotUpdateManager.TryGetInstance(out var update))
            {
                throw new InvalidOperationException("Initialize HotUpdateManager in GameEntry before running updates.");
            }

            StartupMessage = "检查游戏版本";
            Progress = .12f;
            try
            {
                await TaskUtil.WaitAsync(update.StartAsync(), token);
                token.ThrowIfCancellationRequested();
                StartupMessage = "准备完成";
                Progress = 1;
            }
            finally
            {
                acceptingProgress = false;
            }
        }

        private async Task PrepareResourcesAsync(ReleaseContentContext content, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            StartupMessage = "准备游戏资源";
            Progress = .20f;
            var resources = ResourceManager.Instance;
            if (resources.Initialized && resources.PackageVersion != content.PackageVersion)
            {
                await resources.ShutdownAsync();
                token.ThrowIfCancellationRequested();
            }

            var options = new ResourceInitializationOptions
            {
                PackageVersion = content.PackageVersion,
                DownloadAll = true,
                ConcurrentDownloads = 4,
                FailedTryAgain = 2,
                Progress = new Progress<ResourceDownloadProgress>(ReportProgress)
            };
            if (simulateResources)
            {
                options.Parameters = YooAssetsSettings.CreateEditorSimulateModeParameters();
                options.PackageVersion = null;
            }
            else
            {
                options.Parameters = await ConfigureResourceModeAsync(content, token);
            }

            if (!await resources.PrepareContentAsync(options, token))
            {
                throw new InvalidOperationException("Resource initialization/download failed for version " + content.PackageVersion);
            }

            token.ThrowIfCancellationRequested();
            StartupMessage = "载入玩家页面";
            Progress = .80f;
        }

        private void ReportProgress(ResourceDownloadProgress value)
        {
            // Progress 会投递回调，完成或退出后不能覆盖最终状态。
            if (!acceptingProgress)
            {
                return;
            }

            DownloadProgress = value;
            StartupMessage = "更新资源 " + value.CompletedCount + "/" + value.TotalCount;
            Progress = .20f + .55f * value.Fraction;
        }

        private async Task<byte[]> LoadCodeAsync(ReleaseContentContext content, ReleaseFile file, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            StartupMessage = "载入游戏代码";
            Progress = .86f;
            using (var asset = await ResourceManager.Instance.AcquireAsync<TextAsset>(content.CodeLocation(file), token))
            {
                return asset.Asset.bytes;
            }
        }

        internal async Task<YooAsset.InitializeParameters> ConfigureResourceModeAsync(ReleaseContentContext content, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (resourceMode == ResourceMode.Offline && !content.IsPackaged)
            {
                throw new InvalidOperationException("Offline mode requires the packaged code and resource version.");
            }

            // 代码尚未加载，模块独占 DefaultPackage。
            // 回退时必须销毁旧 Host 文件系统，才能改用内置资源且不依赖下载站。
            var previousPackage = YooAsset.YooAssets.TryGetPackage(GameDataPaths.PackageName);
            if (previousPackage != null && previousPackage.InitializeStatus != YooAsset.EOperationStatus.None)
            {
                await ResourceManager.Instance.ShutdownAsync();
                token.ThrowIfCancellationRequested();
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
                return YooAssetsSettings.CreateOfflinePlayModeParameters();
            }
            else
            {
                if (content.PackageName != GameDataPaths.PackageName)
                {
                    throw new InvalidOperationException("The selected release uses another resource package.");
                }

                return YooAssetsSettings.CreateHostPlayModeParameters(GameReleaseConfiguration.ResourceUrl(content.Platform, content.PackageName));
            }
        }

        public void Dispose()
        {
            disposed = true;
            acceptingProgress = false;
        }
    }
}
