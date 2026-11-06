// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

public static class ManagerContracts
{
    public static readonly List<string> Calls = new List<string>();
    public static Exception UIFailure;
    public static readonly List<string> Cleanup = new List<string>();
    public static Task UIShutdown = Task.CompletedTask;
    public static Task UIRelease = Task.CompletedTask;
    public static Exception CleanupFailure;
    public static Exception ModuleFailure, ModuleTickFailure;
    public static Action ModuleShutdown;
    public static Task<bool> ResourceInitialization = Task.FromResult(true);
    public static readonly List<string> DataAcquisitions = new List<string>();
    public static Func<string, CancellationToken, Task<byte[]>> LoadDataBytes;
    public static int LeasesReleased;
    public static int ProtectedAcquisitions;
    public static Func<Game.Framework.HotUpdate.IGameModule> HotGameFactory;
    public static readonly List<string> HotCalls = new List<string>();
    public static Func<CancellationToken, Task> HotInitialization = _ => Task.CompletedTask;
}
