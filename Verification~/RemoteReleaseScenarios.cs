using System;
using System.IO;
using System.Linq;
using System.Text;
using Game;
using Game.Editor;
using Game.Framework.HotUpdate;
using Game.Framework.Audio;
using Game.Framework.Input;
using Game.Framework.Storage;
using Game.HotUpdate;
using UnityEngine;

internal static partial class Program
{
    private static void RemoteReleaseScenarios()
    {
        Check("packaged resource fallback resets the old host package and uses offline resources", () =>
        {
            ResetEntry();
            var previous = new YooAsset.ResourcePackage { InitializeStatus = YooAsset.EOperationStatus.Succeed };
            YooAsset.YooAssets.Package = previous;
            try
            {
                var content = new ReleaseContentContext("windows-x64", new ReleaseContent
                {
                    packageName = "DefaultPackage",
                    version = "baseline-resources"
                }, true);
                var options = new Game.Resource.ResourceInitializationOptions { PackageVersion = content.PackageVersion };
                Complete(GameEntry.ConfigurePlayerResourcesAsync(Game.Resource.ResourceManager.Instance, options, content, System.Threading.CancellationToken.None));
                Equal(1, previous.Destroyed);
                Equal("baseline-resources", options.PackageVersion);
                True(options.Parameters is YooAsset.OfflinePlayModeParameters);
            }
            finally
            {
                YooAsset.YooAssets.Package = null;
                ResetEntry();
            }
        });
        Check("AOT entry has no reference to hot Tables or the Luban runtime", () =>
        {
            var references = typeof(GameEntry).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            True(!references.Contains("Game.Data.Generated") && !references.Contains("Game.HotUpdate") && !references.Contains("Luban.Runtime"));
            Equal("Game.Data.Generated", typeof(cfg.Tables).Assembly.GetName().Name);
        });
        Check("HTTPS file roots are separated from the game TCP endpoint", () =>
        {
            var settings = GameReleaseConfiguration.CreateSettings();
            True(settings.remoteBaseUrl.StartsWith("https://15000.top/game-updates/manifests/"));
            True(settings.remoteFilesBaseUrl.StartsWith("https://15000.top/game-updates/patches/"));
            True(GameReleaseConfiguration.ResourceUrl("windows-x64", "DefaultPackage").StartsWith("https://15000.top/game-updates/assets/"));
            Equal(443, new Uri(settings.remoteBaseUrl).Port);
            Equal(22370, GameReleaseConfiguration.GameServerPort);
        });
        Check("volume and binding settings restore after an entry restart", () =>
        {
            ResetEntry();
            var entry = CreateEntry();
            var settings = GameSettings.Instance;
            settings.SetMasterVolume(.4f);
            settings.SetGroupVolume(AudioGroup.Music, .25f);
            Complete(settings.RebindAsync("Move", 0));
            Complete(entry.ShutdownAsync());
            var next = CreateEntry();
            Equal(.4f, AudioManager.Instance.MasterVolume);
            Equal(.25f, AudioManager.Instance.GetGroupVolume(AudioGroup.Music));
            Equal("test-binding", InputManager.Instance.SaveBindingOverrides());
            Throws<InvalidOperationException>(() => settings.SetMasterVolume(.9f));
            Complete(next.ShutdownAsync());
            ResetEntry();
        });
        Check("pause and focus callbacks save current preferences", () =>
        {
            ResetEntry();
            var entry = CreateEntry();
            AudioManager.Instance.MasterVolume = .6f;
            UnityEngine.Object.Invoke(entry, "OnApplicationPause", true);
            Equal(.6f, LocalStorageManager.Instance.LoadJson<GamePreferences>("user-settings.json").master);
            AudioManager.Instance.MasterVolume = .8f;
            UnityEngine.Object.Invoke(entry, "OnApplicationFocus", false);
            Equal(.8f, LocalStorageManager.Instance.LoadJson<GamePreferences>("user-settings.json").master);
            ResetEntry();
        });
        Check("bad preferences remain intact until an explicit settings change", () =>
        {
            ResetEntry();
            LocalStorageManager.Files["user-settings.json"] = "{\"master\":2,\"music\":1,\"sound\":1,\"voice\":1,\"ui\":1,\"bindings\":\"\"}";
            string bad = LocalStorageManager.Files["user-settings.json"];
            var entry = CreateEntry();
            GameSettings.Instance.Save();
            Equal(bad, LocalStorageManager.Files["user-settings.json"]);
            GameSettings.Instance.SetMasterVolume(.5f);
            Equal(.5f, LocalStorageManager.Instance.LoadJson<GamePreferences>("user-settings.json").master);
            Complete(entry.ShutdownAsync());
            ResetEntry();
        });
        Check("signed publication binds DLL bytes to the exact resource version", () =>
        {
            string temp = Path.Combine(Path.GetTempPath(), "signed-game-update-" + Guid.NewGuid().ToString("N"));
            string unsigned = Path.Combine(temp, "unsigned");
            string assets = Path.Combine(temp, "assets");
            string published = Path.Combine(temp, "published");
            Directory.CreateDirectory(unsigned);
            Directory.CreateDirectory(assets);
            try
            {
                GameReleaseBuild.EnsureSigningKey();
                GameReleaseBuild.RecordResources("resources-7", assets);
                byte[] bytes = { 1, 2, 3 };
                string dll = Path.Combine(unsigned, "Game.HotUpdate.dll.bytes");
                File.WriteAllBytes(dll, bytes);
                File.WriteAllText(Path.Combine(assets, "DefaultPackage_resources-7.bytes"), "resource manifest");
                File.WriteAllText(Path.Combine(assets, "OutputCache"), "Unity debug manifest");
                File.WriteAllText(Path.Combine(assets, "OutputCache.manifest"), "Unity debug text");
                File.WriteAllText(Path.Combine(assets, "DefaultPackage.version"), "resources-7");
                var manifest = new ReleaseManifest
                {
                    appId = Application.identifier,
                    platform = "windows-x64",
                    channel = GameReleaseConfiguration.Channel,
                    baselineId = "native-baseline",
                    version = 2,
                    assemblies = new[]
                    {
                        new ReleaseFile
                        {
                            assemblyName = "Game.HotUpdate", fileName = "Game.HotUpdate.dll.bytes",
                            size = bytes.Length, sha256 = ReleasePolicy.Sha256(bytes)
                        }
                    },
                    aotMetadata = Array.Empty<ReleaseFile>(),
                    content = new ReleaseContent
                    {
                        packageName = "DefaultPackage",
                        version = "resources-7"
                    }
                };
                File.WriteAllText(Path.Combine(unsigned, "manifest.json"), JsonUtility.ToJson(manifest));
                GameReleaseBuild.PublishUnsigned(unsigned, assets, published);
                string latest = Directory.GetFiles(Path.Combine(published, "manifests"), "release.json", SearchOption.AllDirectories).Single();
                string envelopeText = File.ReadAllText(latest);
                var envelope = JsonUtility.FromJson<SignedEnvelope>(envelopeText);
                byte[] payload = ReleasePolicy.VerifyEnvelope(envelope, GameReleaseBuild.CreateBuildSettings().trustedKeys);
                Equal("resources-7", JsonUtility.FromJson<ReleaseManifest>(Encoding.UTF8.GetString(payload)).content.version);
                True(Directory.GetFiles(Path.Combine(published, "patches"), "*.dll.bytes", SearchOption.AllDirectories).Length == 1);
                True(Directory.GetFiles(published, "*.xml", SearchOption.AllDirectories).Length == 0);
                True(Directory.GetFiles(published, "OutputCache*", SearchOption.AllDirectories).Length == 0);
                True(Directory.GetFiles(published, "*.version", SearchOption.AllDirectories).Length == 0);
                File.WriteAllBytes(dll, new byte[] { 9 });
                Throws<InvalidDataException>(() => GameReleaseBuild.PublishUnsigned(unsigned, assets, published));
                Equal(envelopeText, File.ReadAllText(latest));
            }
            finally
            {
                Directory.Delete(temp, true);
            }
        });
    }
}
