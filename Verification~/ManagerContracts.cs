// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;
using static ManagerContracts;

internal static class ManagerModels
{
    internal static void Reset()
    {
        Game.Framework.HotUpdate.HotUpdateManager.Release();
        HotCalls.Clear();
        HotInitialization = _ => Task.CompletedTask;
        Calls.Clear();
        LeasesReleased = 0;
        ProtectedAcquisitions = 0;
        Cleanup.Clear();
        UIShutdown = Task.CompletedTask;
        UIRelease = Task.CompletedTask;
        CleanupFailure = null;
        ModuleFailure = ModuleTickFailure = null;
        ModuleShutdown = null;
        Game.Resource.ResourceManager.Instance.ResetForTest();
        Game.Framework.Storage.LocalStorageManager.Reset();
        UIFailure = null;
        ResourceInitialization = Task.FromResult(true);
        UnityEngine.Resources.Assets.Clear();
        Game.Framework.Network.NetworkManager.Reset();
        Game.Framework.Data.DataManager.Reset();
        Game.Framework.Scenes.SceneManager.Reset();
        Game.Framework.Config.ConfigManager.Reset();
        Game.Framework.Input.InputManager.Reset();
    }
}
