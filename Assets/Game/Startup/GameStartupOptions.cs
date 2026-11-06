using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game
{
    /// <summary>项目启动参数，在 AOT 入口与热更入口之间传递，不拥有 Manager 或运行状态。</summary>
    public sealed class GameStartupOptions
    {
        public UnityEngine.Object InputActions { get; }
        public string InitialInputMap { get; }
        public Func<object> SceneLoaderFactory { get; }
        public Func<object> UIAssetLoaderFactory { get; }
        public Func<CancellationToken, Task> InitializeModulesAsync { get; }

        public GameStartupOptions(UnityEngine.Object inputActions = null, string initialInputMap = "Gameplay", Func<CancellationToken, Task> initializeModulesAsync = null, Func<object> sceneLoaderFactory = null, Func<object> uiAssetLoaderFactory = null)
        {
            InputActions = inputActions;
            InitialInputMap = initialInputMap;
            SceneLoaderFactory = sceneLoaderFactory;
            UIAssetLoaderFactory = uiAssetLoaderFactory;
            InitializeModulesAsync = initializeModulesAsync;
        }
    }
}
