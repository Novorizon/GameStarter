using System;
using System.Threading;
using System.Threading.Tasks;
using ECS;
using Game.Framework;
using Game.Framework.Audio;
using Game.Framework.Data;
using Game.Framework.Event;
using Game.Framework.HotUpdate;
using Game.Framework.Input;
using Game.Framework.Network;
using Game.Framework.Network.Unity;
using Game.Framework.Storage;
using Game.Framework.Timing;
using Game.Resource;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using GameSceneManager = Game.Framework.Scenes.SceneManager;

namespace Game.HotUpdate
{
    /// <summary>热更业务入口：准备公共系统，再进入业务；与 GameEntry 使用相同的启动函数名称。</summary>
    public sealed class HotGame : IGameModule
    {
        private const string Version = "1.0.0";
        private IGameHost host;
        private GameStartupOptions options;
        private bool stopped;
        private bool managersStarted;

        public async Task InitializeAsync(IGameHost gameHost, CancellationToken token)
        {
            if (gameHost == null)
            {
                throw new ArgumentNullException(nameof(gameHost));
            }

            token.ThrowIfCancellationRequested();
            if (stopped)
            {
                throw new OperationCanceledException("Hot update module has stopped.");
            }

            host = gameHost;
            options = (gameHost as IGameContextHost)?.StartupContext as GameStartupOptions ?? new GameStartupOptions();
            await InitializeManagersAsync(token);
            await StartGameAsync(token);
            token.ThrowIfCancellationRequested();
            if (stopped)
            {
                throw new OperationCanceledException("Hot update module has stopped.");
            }

            host.Log("HOT_UPDATE_READY " + Version);
            host.ReportReady(Version);
        }

        private Task InitializeManagersAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            managersStarted = true;
            EventManager.Instance.Initialize();
            TimerManager.Instance.Initialize();
            LocalStorageManager.Instance.Initialize();
            var source = options.InputActions as InputActionAsset ?? Resources.Load<InputActionAsset>("InputActions");
            InputManager.Instance.Initialize(source, source != null ? options.InitialInputMap : null);
            GameSceneManager.Instance.Initialize(options.SceneLoader as Game.Framework.Scenes.ISceneLoader);
            AudioManager.Instance.Initialize();
            if (options.UseYooAsset)
            {
                DataManager.Instance.Initialize(LoadDataBytesAsync);
            }
            else
            {
                DataManager.Instance.Initialize();
            }

            if (options.UIAssetLoader is IUIAssetLoader loader)
            {
                UIManager.Instance.SetAssetLoader(loader);
            }

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

        private async Task StartGameAsync(CancellationToken token)
        {
            if (options.InitializeModulesAsync != null)
            {
                await options.InitializeModulesAsync(token);
            }

            token.ThrowIfCancellationRequested();
            // 在这里加载本游戏使用的表、初始化玩法系统、加载首场景或打开登录界面。
            // 具体业务类放在 Assets/HotUpdate 的其他目录，由这里组织调用。
        }

        private static async Task<byte[]> LoadDataBytesAsync(string location, CancellationToken token)
        {
            using (var asset = await ResourceManager.Instance.AcquireAsync<TextAsset>(location, token))
            {
                return asset.Asset.bytes;
            }
        }

        public void Shutdown()
        {
            if (stopped)
            {
                return;
            }

            stopped = true;
            if (managersStarted)
            {
                ReleaseManagers();
                managersStarted = false;
            }

            host?.Log("HOT_UPDATE_STOPPED");
            host = null;
            options = null;
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
    }
}
