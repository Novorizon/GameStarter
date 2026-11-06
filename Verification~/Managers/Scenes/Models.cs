// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Scenes
{
    public interface ISceneLoader
    {
    }

    public sealed class SceneManager : ConfigurableSingleton<SceneManager>, IDisposable
    {
        public static SceneManager Instance => GetOrCreateInstance(() => new SceneManager());
        public bool Initialized
        {
            get;
            private set;
        }

        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("scenes");
            ReleaseInstance(this);
        }

        public override void Initialize()
        {
            Initialize(null);
        }

        public void Initialize(object loader = null)
        {
            Initialized = true;
            ManagerContracts.Calls.Add("scenes");
        }

        public static void Reset() => ResetInstance(null);
    }
}
