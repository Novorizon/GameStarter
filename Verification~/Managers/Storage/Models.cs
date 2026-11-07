// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Storage
{
    public sealed partial class LocalStorageManager : Singleton<LocalStorageManager>, IDisposable
    {
        public string RootDirectory
        {
            get;
            private set;
        }

        public override void Initialize()
        {
            RootDirectory = "test";
            ManagerContracts.Calls.Add("storage");
        }

        public static readonly Dictionary<string, string> Files = new Dictionary<string, string>();
        public bool Exists(string name) => Files.ContainsKey(name);
        public void SaveJson<T>(string name, T data, int version = 1) where T : class => Files[name] = UnityEngine.JsonUtility.ToJson(data);
        public T LoadJson<T>(string name, int version = 1, Func<int, string, string> migrate = null) where T : class => UnityEngine.JsonUtility.FromJson<T>(Files[name]);

        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("storage");
            ReleaseInstance(this);
            if (ManagerContracts.CleanupFailure != null)
            {
                throw ManagerContracts.CleanupFailure;
            }
        }

        public static void Reset() => ResetInstance(null);
    }
}
