using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Game.Framework.HotUpdate;
using Game.Framework.HotUpdate.Editor;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>工程发行接线：公钥进主包，私钥留在工程外；DLL 和资源由同一签名清单绑定。</summary>
    [InitializeOnLoad]
    public static class GameReleaseBuild
    {
        private const string KeyAsset = "Assets/Resources/GameUpdateKey.asset";
        private const string VersionFile = "Library/Novorizon/GameStarter/resource-version.txt";
        private const string DirectoryFile = "Library/Novorizon/GameStarter/resource-directory.txt";
        public static string LastResourceVersion => File.Exists(VersionFile) ? File.ReadAllText(VersionFile) : null;

        static GameReleaseBuild()
        {
            GameBuild.SettingsFactory = CreateBuildSettings;
            GameBuild.BuildContent = GameCodeResources.Build;
        }

        public static UpdateSettings CreateBuildSettings()
        {
            var settings = GameReleaseConfiguration.CreateSettings(AssetDatabase.LoadAssetAtPath<GameUpdateKey>(KeyAsset));
            if (!string.IsNullOrWhiteSpace(LastResourceVersion))
            {
                settings.content = new ReleaseContent
                {
                    packageName = GameDataPaths.PackageName,
                    version = LastResourceVersion,
                    codeInResources = true
                };
            }

            return settings;
        }

        public static string PrivateKeyPath
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("NOVORIZON_SIGNING_KEY_PATH");
                string path = string.IsNullOrWhiteSpace(configured)
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Novorizon", "Signing",
                        Application.identifier, "development.private.xml")
                    : configured;
                path = Path.GetFullPath(path);
                string root = Path.GetFullPath(".").TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Keep the signing private key outside the Unity project.");
                }

                return path;
            }
        }

        public static void EnsureSigningKey()
        {
            var key = AssetDatabase.LoadAssetAtPath<GameUpdateKey>(KeyAsset);
            string path = PrivateKeyPath;
            using var rsa = RSA.Create();
            if (File.Exists(path))
            {
                rsa.FromXmlString(File.ReadAllText(path));
            }
            else
            {
                if (key != null)
                {
                    throw new InvalidOperationException("The public key already exists. Import its private key and set NOVORIZON_SIGNING_KEY_PATH; do not replace a shipped key.");
                }

                rsa.KeySize = 3072;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                byte[] bytes = new UTF8Encoding(false).GetBytes(rsa.ToXmlString(true));
                file.Write(bytes, 0, bytes.Length);
                file.Flush(true);
            }

            var publicKey = rsa.ExportParameters(false);
            string modulus = Convert.ToBase64String(publicKey.Modulus);
            if (key != null && key.modulus != modulus)
            {
                throw new InvalidOperationException("The signing private key does not match this project's public key.");
            }

            if (key == null)
            {
                key = ScriptableObject.CreateInstance<GameUpdateKey>();
                key.keyId = "development";
                key.modulus = modulus;
                key.exponent = Convert.ToBase64String(publicKey.Exponent);
                Directory.CreateDirectory(Path.GetDirectoryName(KeyAsset));
                AssetDatabase.CreateAsset(key, KeyAsset);
                AssetDatabase.SaveAssets();
            }

            GameBuild.SettingsFactory = CreateBuildSettings;
        }

        public static void RecordResources(string version, string outputDirectory)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(VersionFile));
            File.WriteAllText(VersionFile, version);
            File.WriteAllText(DirectoryFile, Path.GetFullPath(outputDirectory));
        }

        [MenuItem("Tools/Novorizon/GameStarter/4. Export Signed Update")]
        public static void ExportSignedUpdate()
        {
            EnsureSigningKey();
            string unsigned = GameBuild.ExportUpdateToDirectory();
            string output = PublishUnsigned(unsigned, File.ReadAllText(DirectoryFile));
            Debug.Log("SIGNED_UPDATE_EXPORTED " + output + ". Upload assets/ first, then publish manifests/ to /srv/gameserver/updates/.");
        }

        public static string PublishUnsigned(string unsigned, string resourcesDirectory, string outputRoot = "Artifacts/Publish")
        {
            resourcesDirectory = Path.GetFullPath(resourcesDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            byte[] payload = File.ReadAllBytes(Path.Combine(unsigned, "manifest.json"));
            var manifest = JsonUtility.FromJson<ReleaseManifest>(Encoding.UTF8.GetString(payload));
            ReleasePolicy.Validate(manifest);
            if (manifest.content == null || !manifest.content.codeInResources
                || manifest.content.packageName != GameDataPaths.PackageName || manifest.content.version != LastResourceVersion)
            {
                throw new InvalidDataException("The unsigned DLL release must bind the resource version just built.");
            }

            // 固定版本清单必须来自本次资源构建；不能仅凭 Library 中的版本记录配对。
            string resourceManifest = YooAsset.YooAssetSettingsData.GetManifestBinaryFileName(manifest.content.packageName, manifest.content.version);
            if (!File.Exists(Path.Combine(resourcesDirectory, resourceManifest)))
            {
                throw new InvalidDataException("The resource output is missing its bound package manifest: " + resourceManifest);
            }

            var key = AssetDatabase.LoadAssetAtPath<GameUpdateKey>(KeyAsset);
            using var rsa = RSA.Create();
            rsa.FromXmlString(File.ReadAllText(PrivateKeyPath));
            var envelope = new SignedEnvelope
            {
                keyId = key.keyId,
                payload = Convert.ToBase64String(payload),
                signature = Convert.ToBase64String(rsa.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            };
            ReleasePolicy.VerifyEnvelope(envelope, GameReleaseConfiguration.CreateSettings(key).trustedKeys);
            string root = Path.GetFullPath(outputRoot);
            string relative = manifest.appId + "/" + manifest.platform + "/" + manifest.channel + "/" + manifest.baselineId;
            foreach (var dll in manifest.assemblies)
            {
                string source = Path.Combine(unsigned, dll.fileName);
                ReleasePolicy.VerifyFile(source, dll);
            }

            string assets = Path.Combine(root, "assets", manifest.appId, manifest.platform, manifest.content.packageName);
            foreach (string file in Directory.GetFiles(resourcesDirectory, "*", SearchOption.AllDirectories).Where(IsRuntimeResource))
            {
                CopyImmutable(file, Path.Combine(assets, file.Substring(resourcesDirectory.Length + 1)));
            }

            string latest = Path.Combine(root, "manifests", relative, "release.json");
            Directory.CreateDirectory(Path.GetDirectoryName(latest));
            string temporary = latest + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(envelope, true), new UTF8Encoding(false));
            if (File.Exists(latest))
            {
                File.Replace(temporary, latest, null);
            }
            else
            {
                File.Move(temporary, latest);
            }

            return root;
        }

        private static bool IsRuntimeResource(string path)
        {
            string name = Path.GetFileName(path);
            // Built-in 管线的 Unity 调试清单和 latest version 文件会复用固定文件名，
            // 客户端按签名中的精确版本读取 YooAsset 清单，不需要发布这些构建产物。
            return !name.EndsWith(".meta", StringComparison.Ordinal)
                && !name.EndsWith(".version", StringComparison.Ordinal)
                && name != "OutputCache"
                && name != "OutputCache.manifest";
        }

        private static void CopyImmutable(string source, string destination)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            if (File.Exists(destination))
            {
                if (ReleasePolicy.Sha256File(source) != ReleasePolicy.Sha256File(destination))
                {
                    throw new IOException("Published content is immutable: " + destination);
                }

                return;
            }

            File.Copy(source, destination);
        }
    }
}
