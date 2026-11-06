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

internal static class Program
{
    private static int passed;
    private static readonly ModelContext context = new ModelContext();
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    private static void Main()
    {
        SynchronizationContext.SetSynchronizationContext(context);
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
        Check("entry shutdown releases owned managers in reverse order after modules", EntryShutdownOrder);
        Check("partial manager startup failure rolls back resources and earlier managers", EntryRollback);
        Check("a cleanup failure cannot skip later manager releases", EntryCleanupFailure);
        Check("new entry waits for previous asynchronous UI and resource cleanup", EntryRestartWaitsForCleanup);
        Check("entry borrows externally initialized managers without disposing them", EntryBorrowsManagers);
        Check("startup module failure also releases public managers", EntryModuleFailure);
        Check("tick failure also releases public managers", EntryTickFailure);
        Check("entry releases owned native objects after singleton references were reset", EntryCleanupAfterReferenceReset);
        Check("shutdown reentry observes the same unfinished cleanup task", EntryShutdownReentry);
        Check("hot code is a separate assembly and AOT has no hot reference", HotAssemblyBoundary);
        Check("hot entry waits for AOT readiness and reports startup health", HotEntryReady);
        Check("hot entry waits for resource initialization", HotEntryWaits);
        Check("cancelled hot startup cannot report readiness", HotEntryCancelled);
        Check("AOT startup failure prevents hot readiness", HotEntryFailure);
        Check("hot shutdown during initialization suppresses readiness", HotEntryStopped);
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

    private static void ResetEntry()
    {
        UObject.ClearForTest();
        typeof(GameEntry).GetMethod("ResetSession", Static).Invoke(null, null);
        Complete((Task)typeof(GameEntry).GetField("serviceShutdown", Static).GetValue(null));
        var lifecycle = typeof(MonoSingleton<>).Assembly.GetType("Game.Framework.MonoSingletonLifecycle");
        lifecycle.GetMethod("Reset", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
        UnityThread.Reset();
        UnityThread.InitializeManual();
        ManagerContracts.Reset();
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
        // Stop sends OnApplicationQuit, which clears sessionOwner before the next play session.
        UObject.Invoke(entry, "OnApplicationQuit");
        typeof(GameEntry).GetMethod("ResetSession", Static).Invoke(null, null);
        typeof(MonoSingleton<>).Assembly.GetType("Game.Framework.MonoSingletonLifecycle").GetMethod("Reset", Static).Invoke(null, null);
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

    private static GameEntry CreateEntry(bool resources = false)
    {
        var module = new GameObject().AddComponent<RecordingStartupModule>();
        var go = new GameObject
        {
            activeInHierarchy = false
        };
        var entry = go.AddComponent<GameEntry>();
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(GameEntry).GetField("startupModules", flags).SetValue(entry, new StartupModule[] { module });
        typeof(GameEntry).GetField("initializeYooAsset", flags).SetValue(entry, resources);
        go.activeInHierarchy = true;
        UObject.Invoke(entry, "Awake");
        return entry;
    }

    private static void EntryManagers()
    {
        ResetEntry();
        var entry = CreateEntry();
        Equal(StartupState.Ready, entry.State);
        Equal("storage,scenes,audio,config,ui,gpu,entities,worlds,module", string.Join(",", ManagerContracts.Calls));
        True(Game.Framework.Event.EventManager.IsCreated && Game.Framework.Timing.TimerManager.IsCreated);
        _ = GameEntry.Instance;
        _ = GameEntry.Instance;
        Equal("storage,scenes,audio,config,ui,gpu,entities,worlds,module", string.Join(",", ManagerContracts.Calls));
        ResetEntry();
    }

    private static void EntryNetwork()
    {
        ResetEntry();
        UnityEngine.Resources.Assets["NetworkSettings"] = new Game.Framework.Network.Unity.NetworkSettings();
        var entry = CreateEntry();
        Equal(StartupState.Ready, entry.State);
        Equal("storage,scenes,audio,config,ui,gpu,entities,worlds,network,module", string.Join(",", ManagerContracts.Calls));
        ResetEntry();
    }

    private static void EntryResourceFailure()
    {
        ResetEntry();
        ManagerContracts.ResourceInitialization = Task.FromResult(false);
        int errors = Debug.Exceptions.Count;
        var entry = CreateEntry(resources: true);
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
        var entry = CreateEntry(resources: true);
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

    private sealed class OwnedData : IDisposable
    {
        public void Dispose() => ManagerContracts.Cleanup.Add("data");
    }

    private static void EntryShutdownOrder()
    {
        ResetEntry();
        UnityEngine.Resources.Assets["NetworkSettings"] = new Game.Framework.Network.Unity.NetworkSettings();
        var entry = CreateEntry(resources: true);
        Game.Framework.Data.DataManager.Instance.Register(new OwnedData());
        Complete(entry.ShutdownAsync());
        Equal("module,network,worlds,entities,gpu,ui,config,audio,scenes,storage,data,resources", string.Join(",", ManagerContracts.Cleanup));
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
        var entry = CreateEntry(resources: true);
        Equal(StartupState.Failed, entry.State);
        Complete(entry.ShutdownAsync());
        Equal("config,audio,scenes,storage,resources", string.Join(",", ManagerContracts.Cleanup));
        True(!Game.Framework.Data.DataManager.IsCreated);
        True(!Game.Framework.Config.ConfigManager.IsCreated);
        True(!Game.Framework.Scenes.SceneManager.IsCreated);
        True(!Game.Framework.Audio.AudioManager.TryGetInstance(out _));
        ResetEntry();
    }

    private static void EntryCleanupFailure()
    {
        ResetEntry();
        var entry = CreateEntry(resources: true);
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
        var previous = CreateEntry(resources: true);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ManagerContracts.UIShutdown = release.Task;
        var stopping = previous.ShutdownAsync();
        True(!stopping.IsCompleted);
        UObject.Destroy(previous.gameObject);
        int calls = ManagerContracts.Calls.Count;
        var next = CreateEntry(resources: true);
        Equal(StartupState.Starting, next.State);
        Equal(calls, ManagerContracts.Calls.Count);
        release.SetResult();
        Complete(next.WaitUntilReadyAsync());
        True(stopping.IsCompleted);
        True(Game.Resource.ResourceManager.Instance.Initialized);
        Equal(StartupState.Ready, next.State);
        Complete(next.ShutdownAsync());
        ResetEntry();
    }

    private static void EntryBorrowsManagers()
    {
        ResetEntry();
        var ui = UI.UIManager.Instance;
        var config = Game.Framework.Config.ConfigManager.Instance;
        config.Initialize();
        var data = Game.Framework.Data.DataManager.Instance;
        var entry = CreateEntry();
        Complete(entry.ShutdownAsync());
        True(UI.UIManager.TryGetInstance(out var retained) && ReferenceEquals(ui, retained));
        True(Game.Framework.Config.ConfigManager.IsCreated);
        True(Game.Framework.Data.DataManager.IsCreated);
        True(!ManagerContracts.Cleanup.Contains("ui") && !ManagerContracts.Cleanup.Contains("config"));
        ResetEntry();
    }

    private static void EntryModuleFailure()
    {
        ResetEntry();
        ManagerContracts.ModuleFailure = new InvalidOperationException("module init");
        var entry = CreateEntry(resources: true);
        Equal(StartupState.Failed, entry.State);
        Complete(entry.ShutdownAsync());
        Equal("module", ManagerContracts.Cleanup.First());
        Equal("resources", ManagerContracts.Cleanup.Last());
        True(!UI.UIManager.TryGetInstance(out _));
        ResetEntry();
    }

    private static void EntryTickFailure()
    {
        ResetEntry();
        var entry = CreateEntry(resources: true);
        ManagerContracts.ModuleTickFailure = new InvalidOperationException("module tick");
        UObject.Invoke(entry, "Update");
        Equal(StartupState.Failed, entry.State);
        Complete(entry.ShutdownAsync());
        Equal("module", ManagerContracts.Cleanup.First());
        Equal("resources", ManagerContracts.Cleanup.Last());
        ResetEntry();
    }

    private static void EntryCleanupAfterReferenceReset()
    {
        ResetEntry();
        var entry = CreateEntry(resources: true);
        var oldUI = UI.UIManager.Instance;
        typeof(MonoSingleton<>).Assembly.GetType("Game.Framework.MonoSingletonLifecycle").GetMethod("Reset", Static).Invoke(null, null);
        True(!UI.UIManager.TryGetInstance(out _));
        Complete(entry.ShutdownAsync());
        True(oldUI == null);
        True(ManagerContracts.Cleanup.Contains("worlds"));
        Equal("resources", ManagerContracts.Cleanup.Last());
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
        True(!completed);
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

    private static void HotAssemblyBoundary()
    {
        Equal("Game.HotUpdate", typeof(Game.HotUpdate.GameModule).Assembly.GetName().Name);
        Equal("GameStarter.Runtime", typeof(GameEntry).Assembly.GetName().Name);
        True(!typeof(GameEntry).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Game.HotUpdate"));
        True(typeof(Game.Framework.HotUpdate.IGameModule).IsAssignableFrom(typeof(Game.HotUpdate.GameModule)));
    }

    private static void HotEntryReady()
    {
        ResetEntry();
        var host = new HotHost();
        var module = new Game.HotUpdate.GameModule();
        Complete(module.InitializeAsync(host, CancellationToken.None));
        Equal(StartupState.Ready, GameEntry.Instance.State);
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
        CreateEntry(resources: true);
        var host = new HotHost();
        var module = new Game.HotUpdate.GameModule();
        Task start = module.InitializeAsync(host, CancellationToken.None);
        True(!start.IsCompleted);
        Equal(0, host.ReadyCount);
        pending.SetResult(true);
        Complete(start);
        Equal(1, host.ReadyCount);
        module.Shutdown();
        ResetEntry();
    }

    private static void HotEntryCancelled()
    {
        ResetEntry();
        var pending = new TaskCompletionSource<bool>();
        ManagerContracts.ResourceInitialization = pending.Task;
        CreateEntry(resources: true);
        using var stop = new CancellationTokenSource();
        var host = new HotHost();
        var module = new Game.HotUpdate.GameModule();
        Task start = module.InitializeAsync(host, stop.Token);
        stop.Cancel();
        Throws<OperationCanceledException>(() => Complete(start));
        Equal(0, host.ReadyCount);
        module.Shutdown();
        pending.SetResult(true);
        ResetEntry();
    }

    private static void HotEntryFailure()
    {
        ResetEntry();
        ManagerContracts.UIFailure = new InvalidOperationException("UI initialization failed");
        var host = new HotHost();
        var module = new Game.HotUpdate.GameModule();
        Throws<InvalidOperationException>(() => Complete(module.InitializeAsync(host, CancellationToken.None)));
        Equal(0, host.ReadyCount);
        module.Shutdown();
        ResetEntry();
    }

    private static void HotEntryStopped()
    {
        ResetEntry();
        var pending = new TaskCompletionSource<bool>();
        ManagerContracts.ResourceInitialization = pending.Task;
        CreateEntry(resources: true);
        var host = new HotHost();
        var module = new Game.HotUpdate.GameModule();
        Task start = module.InitializeAsync(host, CancellationToken.None);
        module.Shutdown();
        pending.SetResult(true);
        Throws<OperationCanceledException>(() => Complete(start));
        Equal(0, host.ReadyCount);
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
