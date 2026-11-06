using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game
{
    /// <summary>项目启动参数，在 AOT 入口与热更入口之间传递，不拥有 Manager 或运行状态。</summary>
    public sealed class GameStartupOptions
    {
        public bool UseYooAsset { get; }
        public UnityEngine.Object InputActions { get; }
        public string InitialInputMap { get; }
        public object SceneLoader { get; }
        public object UIAssetLoader { get; }
        public Func<CancellationToken, Task> InitializeModulesAsync { get; }

        public GameStartupOptions(bool useYooAsset = false, UnityEngine.Object inputActions = null, string initialInputMap = "Gameplay", Func<CancellationToken, Task> initializeModulesAsync = null, object sceneLoader = null, object uiAssetLoader = null)
        {
            UseYooAsset = useYooAsset;
            InputActions = inputActions;
            InitialInputMap = initialInputMap;
            SceneLoader = sceneLoader;
            UIAssetLoader = uiAssetLoader;
            InitializeModulesAsync = initializeModulesAsync;
        }
    }
}
