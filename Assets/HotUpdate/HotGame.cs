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
using Game.HotUpdate.Client;
using UI;
using UnityEngine;
using UnityEngine.InputSystem;
using GameSceneManager = Game.Framework.Scenes.SceneManager;

namespace Game.HotUpdate
{
    /// <summary>热更业务入口：准备公共系统，再进入业务；与 GameEntry 使用相同的启动函数名称。</summary>
    public sealed partial class HotGame : IGameModule, IGameApplicationEvents
    {
        private const string Version = "1.0.0";
        private IGameHost host;
        private GameStartupOptions options;
        private bool stopped;
        private bool managersStarted;
        private ClientFlow client;

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
            GameSceneManager.Instance.Initialize(ResourceManager.Instance.CreateSceneLoader());
            AudioManager.Instance.Initialize();
            DataManager.Instance.Initialize(LoadDataBytesAsync);
            GameSettings.Instance.Initialize();

            UIManager.Instance.SetAssetLoader(new YooAssetUIAssetLoader(ResourceManager.Instance));
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
            // 先把完整 cfg.Tables 交给 DataManager，再启动业务。
            await LoadTablesAsync(token);
            token.ThrowIfCancellationRequested();
            var tables = DataManager.Instance.Get<cfg.Tables>();
            ValidateNativeSchema(tables);

            var example = tables.TbExample.Get(1);
            host.Log("LUBAN_EXAMPLE " + example.Id + " " + example.Name);
            client = new ClientFlow();
            await client.StartAsync(tables, token);
        }

        private static async Task LoadTablesAsync(CancellationToken token)
        {
            await DataManager.Instance.LoadAsync<cfg.Tables>(CreateTables, GameDataPaths.ManifestLocation, token);
            token.ThrowIfCancellationRequested();
        }

        // DataManager 加载好文件后调用此方法；返回的 Tables 会由它保存。
        private static cfg.Tables CreateTables(Func<string, Luban.ByteBuf> readTable)
        {
            // readTable 是框架传进来的读表方法，Luban 用它读取每张表的二进制数据。
            return new cfg.Tables(readTable);
        }

        private static async Task<byte[]> LoadDataBytesAsync(string location, CancellationToken token)
        {
            using (var asset = await ResourceManager.Instance.AcquireAsync<TextAsset>(location, token))
            {
                return asset.Asset.bytes;
            }
        }

        public void OnApplicationPause(bool paused)
        {
            if (paused && GameSettings.TryGetInstance(out var settings))
            {
                settings.Save();
            }
        }

        public void OnApplicationFocus(bool focused)
        {
            if (!focused && GameSettings.TryGetInstance(out var settings))
            {
                settings.Save();
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
                client?.Dispose();
                client = null;
                ReleaseManagers();
                managersStarted = false;
            }

            host?.Log("HOT_UPDATE_STOPPED");
            host = null;
            options = null;
        }

        private void ReleaseManagers()
        {
            GameSettings.Release();
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
            TimerManager.Release();
            EventManager.Release();
        }
    }
}
