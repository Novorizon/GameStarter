using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Game.Framework;
using Game.Framework.Audio;
using Game.Framework.Input;
using Game.Framework.Storage;

namespace Game.HotUpdate
{
    [Serializable]
    public sealed class GamePreferences
    {
        public float master = 1;
        public float music = 1;
        public float sound = 1;
        public float voice = 1;
        public float ui = 1;
        public string bindings = "";
    }

    public sealed class GameSettings : ConfigurableSingleton<GameSettings>, IDisposable
    {
        private const string FileName = "user-settings.json";
        private bool initialized;
        private bool disposed;
        private bool restoreFailed;
        public static GameSettings Instance => GetOrCreateInstance(() => new GameSettings());

        public override void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (disposed)
            {
                throw new ObjectDisposedException(nameof(GameSettings));
            }

            initialized = true;
            if (!LocalStorageManager.Instance.Exists(FileName))
            {
                return;
            }

            try
            {
                var value = LocalStorageManager.Instance.LoadJson<GamePreferences>(FileName);
                if (value == null)
                {
                    throw new InvalidDataException("Saved game settings are empty.");
                }

                Validate(value.master);
                Validate(value.music);
                Validate(value.sound);
                Validate(value.voice);
                Validate(value.ui);
                AudioManager.Instance.MasterVolume = value.master;
                AudioManager.Instance.SetGroupVolume(AudioGroup.Music, value.music);
                AudioManager.Instance.SetGroupVolume(AudioGroup.Sound, value.sound);
                AudioManager.Instance.SetGroupVolume(AudioGroup.Voice, value.voice);
                AudioManager.Instance.SetGroupVolume(AudioGroup.UI, value.ui);
                if (!string.IsNullOrWhiteSpace(value.bindings))
                {
                    InputManager.Instance.LoadBindingOverrides(value.bindings);
                }
            }
            catch (Exception error) when (error is IOException || error is ArgumentException || error is InvalidOperationException)
            {
                restoreFailed = true;
                Logger.Exception(error);
            }
        }

        public void SetMasterVolume(float value)
        {
            EnsureUsable();
            Validate(value);
            restoreFailed = false;
            AudioManager.Instance.MasterVolume = value;
            Save();
        }

        public void SetGroupVolume(AudioGroup group, float value)
        {
            EnsureUsable();
            Validate(value);
            restoreFailed = false;
            AudioManager.Instance.SetGroupVolume(group, value);
            Save();
        }

        public async Task RebindAsync(string action, int bindingIndex, CancellationToken token = default)
        {
            EnsureUsable();
            await InputManager.Instance.RebindAsync(action, bindingIndex, token);
            token.ThrowIfCancellationRequested();
            restoreFailed = false;
            Save();
        }

        public void Save()
        {
            if (!initialized || disposed || restoreFailed)
            {
                return;
            }

            if (!AudioManager.TryGetInstance(out var audio)
                || !InputManager.TryGetInstance(out var input)
                || !LocalStorageManager.TryGetInstance(out var storage))
            {
                return;
            }

            storage.SaveJson(FileName, new GamePreferences
            {
                master = audio.MasterVolume,
                music = audio.GetGroupVolume(AudioGroup.Music),
                sound = audio.GetGroupVolume(AudioGroup.Sound),
                voice = audio.GetGroupVolume(AudioGroup.Voice),
                ui = audio.GetGroupVolume(AudioGroup.UI),
                bindings = input.SaveBindingOverrides()
            });
        }

        private void EnsureUsable()
        {
            if (disposed || !initialized)
            {
                throw new InvalidOperationException("Initialize the current GameSettings instance before changing preferences.");
            }
        }

        private static void Validate(float value)
        {
            if (float.IsNaN(value) || value < 0 || value > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            try
            {
                Save();
            }
            finally
            {
                disposed = true;
                ReleaseInstance(this);
            }
        }
    }
}
