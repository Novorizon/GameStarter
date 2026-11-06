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

        public float MasterVolume { get; set; } = 1;
        private readonly float[] groups = { 1, 1, 1, 1 };
        public float GetGroupVolume(AudioGroup group) => groups[(int)group];
        public void SetGroupVolume(AudioGroup group, float value) => groups[(int)group] = value;
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

namespace Game.Framework.Audio
{
    public enum AudioGroup { Music, Sound, Voice, UI }
}
