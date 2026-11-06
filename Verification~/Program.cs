using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Game;
using Game.Editor;
using Game.Framework;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using UObject = UnityEngine.Object;

internal static partial class Program
{
    private static int passed;
    private static readonly ModelContext context = new ModelContext();
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Main()
    {
        SynchronizationContext.SetSynchronizationContext(context);
        Logger.Sink = new ModelLogger();
        Environment.SetEnvironmentVariable("NOVORIZON_SIGNING_KEY_PATH", Path.Combine(Path.GetTempPath(), "novorizon-verification-keys", Guid.NewGuid().ToString("N") + ".xml"));
        GameDataScenarios();
        TowerDefenseDataScenarios();
        RemoteReleaseScenarios();
        Check("empty startup", () =>
        {
            using var s = Session();
            Complete(s.StartAsync(Array.Empty<IStartupModule>()));
            Equal(StartupState.Ready, s.State);
        });
        Check("ordered async startup and reverse shutdown", Ordered);
        Check("partial initialization failure releases the failing module", StartupFailure);
        Check("shutdown during initialization prevents later startup", CancelStartup);
        Check("reentrant startup does not initialize twice", ReentrantStart);
        Check("tick failure stops updates and cleans all modules", TickFailure);
        Check("shutdown from update skips remaining updates", TickShutdown);
        Check("cleanup continues after a shutdown exception", CleanupFailure);
        Check("duplicate and null modules rejected before side effects", InvalidModules);
        Check("normal updates do not allocate", NoFrameAllocations);
        Check("entry uses actual Framework singleton and rejects duplicates", EntrySingleton);
        Check("entry restarts when scene/domain reload is disabled", EntryRestart);
        Check("entry prepares managers before optional startup modules and only once", EntryManagers);
        Check("entry initializes network only when NetworkSettings exists", EntryNetwork);
        Check("failed resource initialization blocks managers and modules", EntryResourceFailure);
        Check("closing while resources initialize prevents later manager creation", EntryResourceCancellation);
        Check("manager failure reports failed startup before modules run", EntryManagerFailure);
        Check("OnExit releases global managers after modules", EntryShutdownOrder);
        Check("manager initialization failure does not roll back other managers in GameEntry", EntryRollback);
        Check("a cleanup failure cannot skip later manager releases", EntryCleanupFailure);
        Check("manager asynchronous cleanup stays inside managers", EntryRestartWaitsForCleanup);
        Check("entry releases global managers without ownership bookkeeping", EntryBorrowsManagers);
        Check("entry prepares project input asset and uses one byte loader and releases each data asset lease", EntryInputAndData);
        Check("UI adapter retains prefab leases until the UI factory releases them", UILeaseOwnership);
        Check("startup module failure leaves manager cleanup to OnExit", EntryModuleFailure);
        Check("module Tick failure leaves manager cleanup to OnExit", EntryTickFailure);
        Check("quit and destroy do not rerun OnExit or create unused managers", EntryCleanupAfterReferenceReset);
        Check("exit reentry does not run cleanup twice", EntryShutdownReentry);
        Check("hot startup owns ordinary manager creation", HotOwnsManagers);
        Check("hot code is a separate assembly and AOT has no hot reference", HotAssemblyBoundary);
        Check("hot entry reports health without waiting for GameEntry", HotEntryReady);
        Check("resource preparation completes before game module initialization", HotEntryWaits);
        Check("cancelled hot startup cannot report readiness", HotEntryCancelled);
        Check("ordinary manager failure prevents game readiness", HotEntryFailure);
        Check("stopped hot entry cannot initialize or report readiness", HotEntryStopped);
        Check("GameEntry stays Starting until hot initialization finishes", EntryWaitsForHot);
        Check("GameEntry can select the compiled game module path", EntryHotDisabled);
        Check("GameEntry exit cancels hot startup before releasing services", EntryHotCancellation);
        Check("hot initialization failure fails GameEntry", EntryHotFailure);
        Check("central entry shutdown precedes editor reset and restarts retained scenes", EntryAutomaticReset);
        Check("catalog resolves dependencies once and before consumers", CatalogOrder);
        Check("catalog detects missing and circular dependencies", InvalidCatalog);
        Check("installer resumes after request handles are lost on reload", InstallerReload);
        Check("installer retains installed packages after an add failure", InstallerFailure);
        Check("installer verifies the resolved version", InstallerVersion);
        Check("HybridCLR embed request completes before installing consumers", InstallerEmbed);
        Check("installer waits for compilation and play mode", InstallerWaits);
        Check("optional installer migrates an old package identifier", InstallerMigration);
        Check("installer configures only after all dependencies exist", InstallerSetup);
        Console.WriteLine($"PASS {passed} lifecycle, package planning and installer scenarios (Unity/UPM API models; not native Unity).");
    }

    private static void Check(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS " + name);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FAIL " + name + ": " + ex);
            Environment.Exit(1);
        }
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new Exception("Assertion failed");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new Exception($"Expected {expected}, got {actual}");
        }
    }

    private static void Throws<T>(Action action)
        where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception("Expected " + typeof(T).Name);
    }

    private static GameSession Session(List<Exception> errors = null) => new GameSession(e => errors?.Add(e));
    private sealed class Module : IStartupModule
    {
        internal Func<CancellationToken, Task> Init = _ => Task.CompletedTask;
        internal Action Update = () =>
        {
        }, Stop = () =>
        {
        };
        public Task InitializeAsync(CancellationToken token) => Init(token);
        public void Tick(float delta, float unscaled) => Update();
        public void Shutdown() => Stop();
    }

    private static void Ordered()
    {
        var events = new List<string>();
        var wait = new TaskCompletionSource<bool>();
        var a = new Module
        {
            Init = _ =>
            {
                events.Add("a+");
                return wait.Task;
            },
            Stop = () => events.Add("a-")
        };
        var b = new Module
        {
            Init = _ =>
            {
                events.Add("b+");
                return Task.CompletedTask;
            },
            Stop = () => events.Add("b-")
        };
        using var s = Session();
        Task start = s.StartAsync(new[] { a, b });
        Equal("a+", string.Join(",", events));
        Equal(StartupState.Starting, s.State);
        wait.SetResult(true);
        Complete(start);
        s.Dispose();
        s.Dispose();
        Equal("a+,b+,b-,a-", string.Join(",", events));
    }

    private static void StartupFailure()
    {
        var events = new List<int>();
        bool canceled = false;
        var a = new Module
        {
            Init = t =>
            {
                t.Register(() => canceled = true);
                return Task.CompletedTask;
            },
            Stop = () => events.Add(1)
        };
        var b = new Module
        {
            Init = _ => Task.FromException(new InvalidOperationException()),
            Stop = () => events.Add(2)
        };
        var c = new Module
        {
            Init = _ => throw new Exception("must not start")
        };
        using var s = Session();
        Throws<InvalidOperationException>(() => Complete(s.StartAsync(new[] { a, b, c })));
        Equal(StartupState.Failed, s.State);
        Equal("2,1", string.Join(",", events));
        True(canceled);
    }

    private static void CancelStartup()
    {
        var wait = new TaskCompletionSource<bool>();
        int stopped = 0, later = 0;
        var a = new Module
        {
            Init = _ => wait.Task,
            Stop = () => stopped++
        };
        var b = new Module
        {
            Init = _ =>
            {
                later++;
                return Task.CompletedTask;
            }
        };
        using var s = Session();
        Task start = s.StartAsync(new[] { a, b });
        s.Dispose();
        wait.SetResult(true);
        Throws<OperationCanceledException>(() => Complete(start));
        Equal(StartupState.Stopped, s.State);
        Equal(1, stopped);
        Equal(0, later);
    }

    private static void ReentrantStart()
    {
        using var s = Session();
        int count = 0;
        Task inner = null;
        var a = new Module
        {
            Init = _ =>
            {
                count++;
                inner = s.StartAsync(Array.Empty<IStartupModule>());
                return Task.CompletedTask;
            }
        };
        Task outer = s.StartAsync(new[] { a });
        Complete(outer);
        True(ReferenceEquals(inner, outer));
        Equal(1, count);
    }

    private static void TickFailure()
    {
        int stopped = 0, later = 0;
        var errors = new List<Exception>();
        using var s = Session(errors);
        Complete(s.StartAsync(new[] { new Module { Update = () => throw new InvalidOperationException(), Stop = () => stopped++ }, new Module { Update = () => later++, Stop = () => stopped++ } }));
        s.Tick(1, 1);
        s.Tick(1, 1);
        Equal(StartupState.Failed, s.State);
        Equal(2, stopped);
        Equal(0, later);
        Equal(1, errors.Count);
    }

    private static void TickShutdown()
    {
        using var s = Session();
        int later = 0;
        Complete(s.StartAsync(new[] { new Module { Update = s.Dispose }, new Module { Update = () => later++ } }));
        s.Tick(1, 1);
        Equal(0, later);
        Equal(StartupState.Stopped, s.State);
    }

    private static void CleanupFailure()
    {
        int stopped = 0;
        var errors = new List<Exception>();
        using var s = Session(errors);
        Complete(s.StartAsync(new[] { new Module { Stop = () => stopped++ }, new Module { Stop = () => throw new Exception("cleanup") } }));
        s.Dispose();
        Equal(1, stopped);
        Equal(1, errors.Count);
    }

    private static void InvalidModules()
    {
        using var s = Session();
        var a = new Module
        {
            Init = _ => throw new Exception("must not start")
        };
        Throws<ArgumentException>(() => s.StartAsync(new[] { a, a }));
        Throws<ArgumentException>(() => s.StartAsync(new Module[] { a, null }));
        Equal(StartupState.NotStarted, s.State);
    }

    private static void NoFrameAllocations()
    {
        using var s = Session();
        Complete(s.StartAsync(new[] { new Module() }));
        s.Tick(1, 1);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            s.Tick(1, 1);
        }

        Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private sealed class ModelLogger : ILogger
    {
        public void Info(string value) => Debug.Log(value);
        public void Warn(string value) => Debug.Log(value);
        public void Error(string value) => Debug.Log(value);
        public void Exception(Exception error) => Debug.LogException(error);
    }

    private static void ResetEntry()
    {
        UObject.ClearForTest();
        UObject.ResetSingletonsForPlay();
        UnityThread.Reset();
        UnityThread.InitializeManual();
        ManagerModels.Reset();
        ManagerContracts.HotGameFactory = () => new Game.HotUpdate.HotGame();
    }

    private static void EntrySingleton()
    {
        ResetEntry();
        var first = new GameObject().AddComponent<GameEntry>();
        Equal(StartupState.Ready, first.State);
        True(ReferenceEquals(first, GameEntry.Instance));
        var second = new GameObject().AddComponent<GameEntry>();
        True(second == null);
        True(ReferenceEquals(first, GameEntry.Instance));
        UObject.Destroy(first.gameObject);
        True(!GameEntry.TryGetInstance(out _));
        ResetEntry();
    }

    private static void EntryRestart()
    {
        ResetEntry();
        var entry = new GameObject().AddComponent<GameEntry>();
        // Unity Stop sends OnApplicationQuit before the next Play resets singleton references.
        UObject.Invoke(entry, "OnApplicationQuit");
        UObject.ResetSingletonsForPlay();
        Equal(StartupState.NotStarted, entry.State);
        typeof(GameEntry).GetMethod("ResumeSession", Static).Invoke(null, null);
        True(ReferenceEquals(entry, GameEntry.Instance));
        Complete(entry.WaitUntilReadyAsync());
        Equal(StartupState.Ready, entry.State);
        ResetEntry();
    }

    private sealed class RecordingStartupModule : StartupModule
    {
        public RecordingStartupModule()
        {
        }

        public override Task InitializeAsync(CancellationToken token)
        {
            ManagerContracts.Calls.Add("module");
            if (ManagerContracts.ModuleFailure != null)
            {
                throw ManagerContracts.ModuleFailure;
            }

            return Task.CompletedTask;
        }

        public override void Shutdown()
        {
            ManagerContracts.Cleanup.Add("module");
            ManagerContracts.ModuleShutdown?.Invoke();
        }

        public override void Tick(float delta, float unscaled)
        {
            if (ManagerContracts.ModuleTickFailure != null)
            {
                throw ManagerContracts.ModuleTickFailure;
            }
        }
    }

    private static GameEntry CreateEntry(bool hotUpdate = true)
    {
        var module = new GameObject().AddComponent<RecordingStartupModule>();
        var go = new GameObject
        {
            activeInHierarchy = false
        };
        var entry = go.AddComponent<GameEntry>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GameEntry).GetField("startupModules", flags).SetValue(entry, new StartupModule[] { module });
        typeof(GameEntry).GetField("initializeHotUpdate", flags).SetValue(entry, hotUpdate);
        go.activeInHierarchy = true;
        UObject.Invoke(entry, "Awake");
        return entry;
    }

    private static void EntryManagers()
    {
        ResetEntry();
        var entry = CreateEntry();
        Equal(StartupState.Ready, entry.State);
        Equal("resources,storage,input,scenes,audio,ui,gpu,entities,worlds,tables,module", string.Join(",", ManagerContracts.Calls));
        True(Game.Framework.Event.EventManager.IsCreated && Game.Framework.Timing.TimerManager.IsCreated);
        _ = GameEntry.Instance;
        _ = GameEntry.Instance;
        Equal("resources,storage,input,scenes,audio,ui,gpu,entities,worlds,tables,module", string.Join(",", ManagerContracts.Calls));
        ResetEntry();
    }

    private static void EntryNetwork()
    {
        ResetEntry();
        UnityEngine.Resources.Assets["NetworkSettings"] = new Game.Framework.Network.Unity.NetworkSettings();
        var entry = CreateEntry();
        Equal(StartupState.Ready, entry.State);
        Equal("resources,storage,input,scenes,audio,ui,gpu,entities,worlds,network,tables,module", string.Join(",", ManagerContracts.Calls));
        ResetEntry();
    }

    private static void EntryResourceFailure()
    {
        ResetEntry();
        ManagerContracts.ResourceInitialization = Task.FromResult(false);
        int errors = Debug.Exceptions.Count;
        var entry = CreateEntry();
        Equal(StartupState.Failed, entry.State);
        Equal("resources", string.Join(",", ManagerContracts.Calls));
        Equal(errors + 1, Debug.Exceptions.Count);
        ResetEntry();
    }

    private static void EntryResourceCancellation()
    {
        ResetEntry();
        var pending = new TaskCompletionSource<bool>();
        ManagerContracts.ResourceInitialization = pending.Task;
        var entry = CreateEntry();
        Equal(StartupState.Starting, entry.State);
        UObject.Destroy(entry.gameObject);
        pending.SetResult(true);
        Equal("resources", string.Join(",", ManagerContracts.Calls));
        True(!UI.UIManager.TryGetInstance(out _));
        ResetEntry();
    }

    private static void EntryManagerFailure()
    {
        ResetEntry();
        ManagerContracts.UIFailure = new InvalidOperationException("UI initialization failed");
        var entry = CreateEntry();
        Equal(StartupState.Failed, entry.State);
        True(!ManagerContracts.Calls.Contains("module"));
        ResetEntry();
    }

    private static void EntryShutdownOrder()
    {
        ResetEntry();
        UnityEngine.Resources.Assets["NetworkSettings"] = new Game.Framework.Network.Unity.NetworkSettings();
        var entry = CreateEntry();
        Complete(entry.ShutdownAsync());
        Equal("module,hot,network,worlds,entities,gpu,ui,audio,scenes,input,storage,resources", string.Join(",", ManagerContracts.Cleanup));
        Complete(entry.ShutdownAsync());
        True(!UI.UIManager.TryGetInstance(out _));
        True(!Game.Framework.Data.DataManager.IsCreated);
        True(!Game.Resource.ResourceManager.Instance.Initialized);
        ResetEntry();
    }

    private static void EntryRollback()
    {
        ResetEntry();
        ManagerContracts.UIFailure = new InvalidOperationException("UI init");
        var entry = CreateEntry();
        Equal(StartupState.Failed, entry.State);
        True(Game.Framework.Audio.AudioManager.TryGetInstance(out _));
        True(Game.Framework.Storage.LocalStorageManager.IsCreated);
        True(!ManagerContracts.Cleanup.Contains("audio") && !ManagerContracts.Cleanup.Contains("storage"));
        Complete(entry.ShutdownAsync());
        True(ManagerContracts.Cleanup.Contains("audio") && ManagerContracts.Cleanup.Contains("storage"));
        True(!Game.Framework.Data.DataManager.IsCreated);
        True(!Game.Framework.Scenes.SceneManager.IsCreated);
        ResetEntry();
    }

    private static void EntryCleanupFailure()
    {
        ResetEntry();
        var entry = CreateEntry();
        ManagerContracts.CleanupFailure = new InvalidOperationException("cleanup");
        int errors = Debug.Exceptions.Count;
        Complete(entry.ShutdownAsync());
        True(Debug.Exceptions.Count > errors);
        Equal("resources", ManagerContracts.Cleanup.Last());
        True(ManagerContracts.Cleanup.Contains("storage"));
        ResetEntry();
    }

    private static void EntryRestartWaitsForCleanup()
    {
        ResetEntry();
        var previous = CreateEntry();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ManagerContracts.UIShutdown = release.Task;
        var stopping = previous.ShutdownAsync();
        True(stopping.IsCompleted);
        True(!ManagerContracts.UIRelease.IsCompleted);
        var next = CreateEntry();
        Equal(StartupState.Ready, next.State);
        var activeUI = UI.UIManager.Instance;
        var oldRelease = ManagerContracts.UIRelease;
        release.SetResult();
        Complete(oldRelease);
        True(UI.UIManager.TryGetInstance(out var retained) && ReferenceEquals(activeUI, retained));
        Complete(next.ShutdownAsync());
        ResetEntry();
    }

    private static void EntryBorrowsManagers()
    {
        ResetEntry();
        var ui = UI.UIManager.Instance;
        var data = Game.Framework.Data.DataManager.Instance;
        var input = Game.Framework.Input.InputManager.Instance;
        var entry = CreateEntry();
        Complete(entry.ShutdownAsync());
        True(!UI.UIManager.TryGetInstance(out _));
        True(!Game.Framework.Data.DataManager.IsCreated);
        True(!Game.Framework.Input.InputManager.IsCreated);
        True(ManagerContracts.Cleanup.Contains("input") && ManagerContracts.Cleanup.Contains("ui"));
        ResetEntry();
    }

    private static void EntryInputAndData()
    {
        ResetEntry();
        var asset = new UnityEngine.InputSystem.InputActionAsset();
        UnityEngine.Resources.Assets["InputActions"] = asset;
        var entry = CreateEntry();
        Equal(StartupState.Ready, entry.State);
        var input = Game.Framework.Input.InputManager.Instance;
        True(ReferenceEquals(asset, input.Source));
        Equal("Gameplay", input.InitialMap);
        var data = Game.Framework.Data.DataManager.Instance;
        Equal("示例配置", data.Get<cfg.Tables>().TbExample.Get(1).Name);
        True(ManagerContracts.DataAcquisitions.Contains("Data/Luban/tbtdstage") && ManagerContracts.DataAcquisitions.Contains("Data/Luban/tbtdcard"));
            Equal(11, ManagerContracts.DataAcquisitions.Count);
        Equal(11, ManagerContracts.LeasesReleased);
        Complete(entry.ShutdownAsync());
        True(ManagerContracts.Cleanup.Contains("input"));
        ResetEntry();
    }

    private static void UILeaseOwnership()
    {
        ResetEntry();
        var resources = Game.Resource.ResourceManager.Instance;
        Complete(resources.InitializeAsync());
        var loader = new YooAssetUIAssetLoader(resources);
        var loading = loader.LoadPrefabAsync("window");
        Complete(loading);
        var prefab = loading.GetAwaiter().GetResult();
        Equal(1, ManagerContracts.ProtectedAcquisitions);
        Equal(0, ManagerContracts.LeasesReleased);
        loader.ReleasePrefab(prefab);
        Equal(1, ManagerContracts.LeasesReleased);
        ResetEntry();
    }

    private static void EntryModuleFailure()
    {
        ResetEntry();
        ManagerContracts.ModuleFailure = new InvalidOperationException("module init");
        var entry = CreateEntry();
        Equal(StartupState.Failed, entry.State);
        True(UI.UIManager.TryGetInstance(out _));
        True(!ManagerContracts.Cleanup.Contains("audio"));
        Complete(entry.ShutdownAsync());
        Equal("module", ManagerContracts.Cleanup.First());
        Equal("resources", ManagerContracts.Cleanup.Last());
        True(!UI.UIManager.TryGetInstance(out _));
        ResetEntry();
    }

    private static void EntryTickFailure()
    {
        ResetEntry();
        var entry = CreateEntry();
        ManagerContracts.ModuleTickFailure = new InvalidOperationException("module tick");
        UObject.Invoke(entry, "Update");
        Equal(StartupState.Failed, entry.State);
        True(UI.UIManager.TryGetInstance(out _));
        True(!ManagerContracts.Cleanup.Contains("audio"));
        Complete(entry.ShutdownAsync());
        Equal("module", ManagerContracts.Cleanup.First());
        Equal("resources", ManagerContracts.Cleanup.Last());
        ResetEntry();
    }

    private static void EntryCleanupAfterReferenceReset()
    {
        ResetEntry();
        var entry = CreateEntry();
        int calls = ManagerContracts.Calls.Count;
        Complete(entry.ShutdownAsync());
        UObject.Invoke(entry, "OnApplicationQuit");
        UObject.Invoke(entry, "OnDestroy");
        Equal(calls, ManagerContracts.Calls.Count);
        Equal(1, ManagerContracts.Cleanup.Count(name => name == "ui"));
        Equal(1, ManagerContracts.Cleanup.Count(name => name == "worlds"));
        True(!Game.Framework.Network.NetworkManager.IsCreated);
        ResetEntry();
    }

    private static void EntryShutdownReentry()
    {
        ResetEntry();
        var entry = CreateEntry();
        Task reentered = null;
        bool completed = true;
        ManagerContracts.ModuleShutdown = () =>
        {
            reentered = entry.ShutdownAsync();
            completed = reentered.IsCompleted;
        };
        var stopping = entry.ShutdownAsync();
        True(ReferenceEquals(stopping, reentered));
        True(completed);
        Complete(stopping);
        Equal(1, ManagerContracts.Cleanup.Count(name => name == "module"));
        ResetEntry();
    }

    private sealed class HotHost : Game.Framework.HotUpdate.IGameHost
    {
        internal readonly List<string> Logs = new List<string>();
        internal int ReadyCount;
        internal string Version;
        public void Log(string message)
        {
            Logs.Add(message);
        }

        public void ReportReady(string moduleVersion)
        {
            ReadyCount++;
            Version = moduleVersion;
        }
    }

    private sealed class BareHotGame : Game.Framework.HotUpdate.IGameModule
    {
        public async Task InitializeAsync(Game.Framework.HotUpdate.IGameHost host, CancellationToken token)
        {
            var contextHost = (Game.Framework.HotUpdate.IGameContextHost)host;
            var options = (GameStartupOptions)contextHost.StartupContext;
            True(!ManagerContracts.Calls.Contains("ui") && !ManagerContracts.Calls.Contains("input"));
            await options.InitializeModulesAsync(token);
            host.ReportReady("bare");
        }

        public void Shutdown()
        {
        }
    }

    private static void HotOwnsManagers()
    {
        ResetEntry();
        ManagerContracts.HotGameFactory = () => new BareHotGame();
        var entry = CreateEntry();
        Complete(entry.WaitUntilReadyAsync());
        Equal(StartupState.Ready, entry.State);
        True(!ManagerContracts.Calls.Contains("ui") && !ManagerContracts.Calls.Contains("input"));
        Complete(entry.ShutdownAsync());
        ResetEntry();
    }

    private static void HotAssemblyBoundary()
    {
        Equal("Game.HotUpdate", typeof(Game.HotUpdate.HotGame).Assembly.GetName().Name);
        Equal("GameStarter.Runtime", typeof(GameEntry).Assembly.GetName().Name);
        True(!typeof(GameEntry).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Game.HotUpdate"));
        True(typeof(Game.Framework.HotUpdate.IGameModule).IsAssignableFrom(typeof(Game.HotUpdate.HotGame)));
    }

    private static void HotEntryReady()
    {
        ResetEntry();
        var host = new HotHost();
        var module = new Game.HotUpdate.HotGame();
        Complete(Game.Resource.ResourceManager.Instance.InitializeAsync());
        Complete(module.InitializeAsync(host, CancellationToken.None));
        True(!GameEntry.TryGetInstance(out _));
        Equal(1, host.ReadyCount);
        Equal("1.0.0", host.Version);
        True(host.Logs.Contains("HOT_UPDATE_READY 1.0.0"));
        module.Shutdown();
        module.Shutdown();
        Equal(1, host.Logs.Count(s => s == "HOT_UPDATE_STOPPED"));
        ResetEntry();
    }

    private static void HotEntryWaits()
    {
        ResetEntry();
        var pending = new TaskCompletionSource<bool>();
        ManagerContracts.ResourceInitialization = pending.Task;
        var entry = CreateEntry();
        Equal(StartupState.Starting, entry.State);
        Equal("start", string.Join(",", ManagerContracts.HotCalls));
        True(!ManagerContracts.Calls.Contains("ui"));
        pending.SetResult(true);
        Complete(entry.WaitUntilReadyAsync());
        Equal("start", string.Join(",", ManagerContracts.HotCalls));
        ResetEntry();
    }

    private static void HotEntryCancelled()
    {
        ResetEntry();
        using var stop = new CancellationTokenSource();
        stop.Cancel();
        var host = new HotHost();
        var module = new Game.HotUpdate.HotGame();
        Throws<OperationCanceledException>(() => Complete(module.InitializeAsync(host, stop.Token)));
        Equal(0, host.ReadyCount);
        module.Shutdown();
        ResetEntry();
    }

    private static void HotEntryFailure()
    {
        ResetEntry();
        ManagerContracts.UIFailure = new InvalidOperationException("UI initialization failed");
        var entry = CreateEntry();
        Throws<InvalidOperationException>(() => Complete(entry.WaitUntilReadyAsync()));
        Equal("start", string.Join(",", ManagerContracts.HotCalls));
        ResetEntry();
    }

    private static void HotEntryStopped()
    {
        ResetEntry();
        var host = new HotHost();
        var module = new Game.HotUpdate.HotGame();
        module.Shutdown();
        Throws<OperationCanceledException>(() => Complete(module.InitializeAsync(host, CancellationToken.None)));
        Equal(0, host.ReadyCount);
        ResetEntry();
    }

    private static void EntryWaitsForHot()
    {
        ResetEntry();
        var pending = new TaskCompletionSource<bool>();
        ManagerContracts.HotInitialization = token =>
        {
            True(!ManagerContracts.Calls.Contains("ui") && !ManagerContracts.Calls.Contains("module"));
            return pending.Task;
        };
        var entry = CreateEntry();
        Equal(StartupState.Starting, entry.State);
        Task ready = entry.WaitUntilReadyAsync();
        True(!ready.IsCompleted);
        _ = GameEntry.Instance;
        Equal("start", string.Join(",", ManagerContracts.HotCalls));
        pending.SetResult(true);
        Complete(ready);
        Equal(StartupState.Ready, entry.State);
        ResetEntry();
    }

    private static void EntryHotDisabled()
    {
        ResetEntry();
        var entry = CreateEntry(hotUpdate: false);
        Equal(StartupState.Ready, entry.State);
        Equal("start", string.Join(",", ManagerContracts.HotCalls));
        True(Game.Framework.HotUpdate.HotUpdateManager.IsCreated);
        ResetEntry();
    }

    private static void EntryHotCancellation()
    {
        ResetEntry();
        CancellationToken hotToken = default;
        ManagerContracts.HotInitialization = token =>
        {
            hotToken = token;
            return Task.Delay(Timeout.Infinite, token);
        };
        var entry = CreateEntry();
        Equal(StartupState.Starting, entry.State);
        Complete(entry.ShutdownAsync());
        True(hotToken.IsCancellationRequested);
        Equal("start,stop", string.Join(",", ManagerContracts.HotCalls));
        Equal("hot", ManagerContracts.Cleanup.First());
        True(!Game.Framework.HotUpdate.HotUpdateManager.IsCreated);
        ResetEntry();
    }

    private static void EntryHotFailure()
    {
        ResetEntry();
        ManagerContracts.HotInitialization = _ => Task.FromException(new InvalidOperationException("hot failed"));
        var entry = CreateEntry();
        Equal(StartupState.Failed, entry.State);
        Throws<InvalidOperationException>(() => Complete(entry.WaitUntilReadyAsync()));
        True(!UI.UIManager.TryGetInstance(out _));
        Complete(entry.ShutdownAsync());
        Equal("hot", ManagerContracts.Cleanup.First());
        ResetEntry();
    }

    private static void EntryAutomaticReset()
    {
        ResetEntry();
        var entry = CreateEntry();
        for (int run = 0; run < 2; run++)
        {
            Equal(StartupState.Ready, entry.State);
            ManagerContracts.Cleanup.Clear();
            UObject.Invoke(entry, "OnApplicationQuit");
            UObject.ResetSingletonsForPlay();
            Equal(StartupState.NotStarted, entry.State);
            True(!GameEntry.TryGetInstance(out _));
            Equal("module", ManagerContracts.Cleanup.First());
            True(!UI.UIManager.TryGetInstance(out _));
            typeof(GameEntry).GetMethod("ResumeSession", Static).Invoke(null, null);
            Complete(entry.WaitUntilReadyAsync());
            True(ReferenceEquals(entry, GameEntry.Instance));
        }

        ResetEntry();
    }

    private static ModuleSpec Spec(string id, params string[] dependencies) => new ModuleSpec
    {
        id = id,
        title = id,
        version = "1.0.0",
        source = "git:" + id,
        dependencies = dependencies
    };
    private static void CatalogOrder()
    {
        var c = ModuleInstaller.ReadCatalog();
        var plan = c.Plan(new[] { "com.novorizon.framework.hybridclr", "com.novorizon.framework.ui" });
        Equal("com.novorizon.framework", plan[0].id);
        True(Array.FindIndex(plan, m => m.id == "com.code-philosophy.hybridclr") < Array.FindIndex(plan, m => m.id == "com.novorizon.framework.hybridclr"));
        Equal(4, plan.Length);
        True(c.Plan(new[] { "com.novorizon.ability" }).All(m => !m.id.Contains("ecs")));
    }

    private static void InvalidCatalog()
    {
        var c = new ModuleCatalog
        {
            modules = new[]
            {
                Spec("a", "missing")
            }
        };
        Throws<InvalidDataException>(() => c.Plan(new[] { "a" }));
        c.modules = new[]
        {
            Spec("a", "b"),
            Spec("b", "a")
        };
        Throws<InvalidDataException>(() => c.Plan(new[] { "a" }));
        c.modules = new[]
        {
            Spec("a"),
            Spec("a")
        };
        Throws<InvalidDataException>(() => c.Plan(new[] { "a" }));
    }

    private static void InProject(Action action)
    {
        string original = Directory.GetCurrentDirectory(), temp = Path.Combine(Path.GetTempPath(), "starter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(temp, "Assets/Settings"));
        SessionState.ClearForTest();
        Client.ClearForTest();
        try
        {
            Directory.SetCurrentDirectory(temp);
            action();
        }
        finally
        {
            typeof(ModuleInstaller).GetField("job", Static).SetValue(null, null);
            typeof(ModuleInstaller).GetField("adding", Static).SetValue(null, null);
            typeof(ModuleInstaller).GetField("listing", Static).SetValue(null, null);
            typeof(ModuleInstaller).GetField("embedding", Static).SetValue(null, null);
            EditorApplication.isCompiling = EditorApplication.isPlayingOrWillChangePlaymode = false;
            Directory.SetCurrentDirectory(original);
            Directory.Delete(temp, true);
        }
    }

    private static void WriteCatalog(params ModuleSpec[] specs)
    {
        File.WriteAllText(ModuleInstaller.CatalogPath, JsonUtility.ToJson(new ModuleCatalog { modules = specs }));
        foreach (var spec in specs)
        {
            Client.Sources[spec.source] = new PackageInfo
            {
                name = spec.id,
                version = spec.version
            };
        }
    }

    private static void Drain()
    {
        for (int i = 0; i < 60 && ModuleInstaller.Busy; i++)
        {
            EditorApplication.TickForTest();
        }

        True(!ModuleInstaller.Busy);
    }

    private static void InstallerReload() => InProject(() =>
    {
        WriteCatalog(Spec("base"), Spec("feature", "base"));
        ModuleInstaller.Install(new[] { "feature" });
        EditorApplication.TickForTest();
        EditorApplication.TickForTest(); // Add succeeded; its request handle is about to be lost.
        var job = typeof(ModuleInstaller).GetField("job", Static);
        var reloaded = typeof(JsonUtility).GetMethod(nameof(JsonUtility.FromJson)).MakeGenericMethod(job.FieldType).Invoke(null, new object[] { SessionState.GetString("Novorizon.GameStarter.InstallJob", "") });
        job.SetValue(null, reloaded);
        typeof(ModuleInstaller).GetField("adding", Static).SetValue(null, null);
        typeof(ModuleInstaller).GetField("listing", Static).SetValue(null, null);
        Drain();
        Equal(2, Client.Installed.Count);
        Equal(2, Client.Added.Count);
    });
    private static void InstallerFailure() => InProject(() =>
    {
        WriteCatalog(Spec("base"), Spec("feature", "base"));
        ModuleInstaller.Install(new[] { "feature" });
        while (!Client.Installed.ContainsKey("base"))
        {
            EditorApplication.TickForTest();
        }

        Client.FailNext = true;
        Drain();
        Equal(1, Client.Installed.Count);
        True(ModuleInstaller.Message.Contains("中止"));
        ModuleInstaller.Install(new[] { "feature" });
        Drain();
        Equal(2, Client.Installed.Count);
    });
    private static void InstallerVersion() => InProject(() =>
    {
        WriteCatalog(Spec("feature"));
        Client.Sources["git:feature"].version = "0.9.0";
        ModuleInstaller.Install(new[] { "feature" });
        Drain();
        True(ModuleInstaller.Message.Contains("mismatch"));
        Equal(1, Client.Added.Count);
    });
    private static void InstallerEmbed() => InProject(() =>
    {
        var spec = Spec("official");
        spec.embed = true;
        WriteCatalog(spec);
        string source = Path.GetFullPath("Cache/official");
        Directory.CreateDirectory(Path.Combine(source, "Data~"));
        File.WriteAllText(Path.Combine(source, "Data~/native.txt"), "native");
        File.WriteAllText(Path.Combine(source, "native.dll.meta"), "guid: original");
        Client.Sources[spec.source].resolvedPath = source;
        Client.HoldEmbed = true;
        ModuleInstaller.Install(new[] { "official" });
        for (int i = 0; i < 30 && Client.PendingEmbed == null; i++)
        {
            EditorApplication.TickForTest();
        }

        True(Client.PendingEmbed != null && ModuleInstaller.Busy);
        for (int i = 0; i < 3; i++)
        {
            EditorApplication.TickForTest();
        }

        True(ModuleInstaller.Busy);
        Client.PendingEmbed.IsCompleted = true;
        Drain();
        Equal("native", File.ReadAllText("Packages/official/Data~/native.txt"));
        Equal("guid: original", File.ReadAllText("Packages/official/native.dll.meta"));
    });
    private static void InstallerWaits() => InProject(() =>
    {
        WriteCatalog(Spec("a"));
        ModuleInstaller.Install(new[] { "a" });
        EditorApplication.isCompiling = true;
        EditorApplication.TickForTest();
        Equal(0, Client.Added.Count);
        EditorApplication.isCompiling = false;
        EditorApplication.isPlayingOrWillChangePlaymode = true;
        EditorApplication.TickForTest();
        Equal(0, Client.Added.Count);
        EditorApplication.isPlayingOrWillChangePlaymode = false;
        Drain();
        Equal(1, Client.Added.Count);
    });
    private static void InstallerMigration() => InProject(() =>
    {
        var module = Spec("feature");
        module.replaces = new[]
        {
            "old-feature"
        };
        WriteCatalog(module);
        Client.Installed["old-feature"] = new PackageInfo
        {
            name = "old-feature",
            version = "old"
        };
        ModuleInstaller.Install(new[] { "feature" });
        Drain();
        True(Client.Installed.ContainsKey("feature"));
        True(!Client.Installed.ContainsKey("old-feature"));
    });
    private static void InstallerSetup() => InProject(() =>
    {
        var module = Spec("feature", "base");
        module.setupType = typeof(SetupProbe).FullName;
        module.setupMethod = nameof(SetupProbe.Configure);
        WriteCatalog(Spec("base"), module);
        SetupProbe.Called = false;
        ModuleInstaller.Install(new[] { "feature" });
        Drain();
        True(SetupProbe.Called);
    });
    public static class SetupProbe
    {
        internal static bool Called;
        public static void Configure()
        {
            True(Client.Installed.ContainsKey("base") && Client.Installed.ContainsKey("feature"));
            Called = true;
        }
    }

    private static void Complete(Task task)
    {
        context.Until(() => task.IsCompleted);
        task.GetAwaiter().GetResult();
    }

    private sealed class ModelContext : SynchronizationContext
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(SendOrPostCallback callback, object state)> callbacks = new();
        public override void Post(SendOrPostCallback callback, object state) => callbacks.Enqueue((callback, state));
        internal void Until(Func<bool> condition)
        {
            var deadline = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (deadline.ElapsedMilliseconds > 10000)
                {
                    throw new TimeoutException("Unity main-thread model timed out.");
                }

                if (callbacks.TryDequeue(out var callback))
                {
                    callback.callback(callback.state);
                }
                else
                {
                    Thread.Sleep(1);
                }
            }
        }
    }
}
