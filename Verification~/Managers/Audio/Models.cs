// Test-only Manager API models. The package implementations and native Unity lifecycle are not replaced in the project.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using Game.Framework;

namespace Game.Framework.Audio
{
    public sealed class AudioManager : MonoSingleton<AudioManager>
    {
        protected override void OnExit()
        {
            ManagerContracts.Cleanup.Add("audio");
        }

        private bool initialized;
        protected override void OnStart() => Initialize();
        public override void Initialize()
        {
            if (initialized)
            {
                return;
            }

            initialized = true;
            ManagerContracts.Calls.Add("audio");
        }
    }
}
