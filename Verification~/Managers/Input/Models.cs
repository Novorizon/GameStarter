// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Input
{
    public sealed class InputManager : Singleton<InputManager>, IDisposable
    {
        public bool Initialized
        {
            get;
            private set;
        }

        public UnityEngine.InputSystem.InputActionAsset Source
        {
            get;
            private set;
        }

        public string InitialMap
        {
            get;
            private set;
        }

        public override void Initialize()
        {
            Initialize(null, null);
        }

        public void Initialize(UnityEngine.InputSystem.InputActionAsset source = null, string initialMap = null)
        {
            Source = source;
            InitialMap = initialMap;
            Initialized = true;
            ManagerContracts.Calls.Add("input");
        }

        private string bindings = "";
        public string SaveBindingOverrides() => bindings;
        public void LoadBindingOverrides(string value) => bindings = value;
        public Task<string> RebindAsync(string action, int index, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            bindings = "test-binding";
            return Task.FromResult(bindings);
        }

        public void Dispose()
        {
            ManagerContracts.Cleanup.Add("input");
            ReleaseInstance(this);
        }

        public static void Reset() => ResetInstance(null);
    }
}
