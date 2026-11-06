using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Game.Editor
{
    /// <summary>Import only declared first-party samples into a clean validation checkout.</summary>
    public static class SampleValidation
    {
        private const string Destination = "Assets/NovorizonSamples";
        [MenuItem("Tools/Novorizon/GameStarter/Native Validation/Import All Package Samples")]
        public static void ImportSamples()
        {
            if (Directory.Exists(Destination))
            {
                throw new InvalidOperationException("Use a clean validation checkout; " + Destination + " already exists.");
            }

            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages().Where(package => package.name.StartsWith("com.novorizon.", StringComparison.Ordinal)).OrderBy(package => package.name, StringComparer.Ordinal).ToArray();
            var imported = new System.Collections.Generic.List<string>();
            foreach (var package in packages)
            {
                var description = JsonUtility.FromJson<PackageDescription>(File.ReadAllText(Path.Combine(package.resolvedPath, "package.json")));
                foreach (var sample in description.samples ?? Array.Empty<SampleDescription>())
                {
                    string source = Path.GetFullPath(Path.Combine(package.resolvedPath, sample.path));
                    string packageRoot = Path.GetFullPath(package.resolvedPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
                    if (!source.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase) || !Directory.Exists(source))
                    {
                        throw new InvalidDataException("Invalid sample source: " + source);
                    }

                    string target = Path.Combine(Destination, package.name, Path.GetFileName(source));
                    foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                    {
                        string destination = Path.Combine(target, file.Substring(source.Length + 1));
                        Directory.CreateDirectory(Path.GetDirectoryName(destination));
                        File.Copy(file, destination, false);
                    }

                    imported.Add(package.name + "/" + sample.path);
                }
            }

            if (imported.Count == 0)
            {
                throw new InvalidOperationException("No Novorizon samples were resolved. Check UPM dependencies first.");
            }

            Directory.CreateDirectory("Artifacts/NativeValidation");
            File.WriteAllLines("Artifacts/NativeValidation/imported-samples.txt", imported);
            AssetDatabase.Refresh();
            Debug.Log("PACKAGE_SAMPLES_IMPORTED " + imported.Count + ". Restart Unity before CompileScripts.");
        }

        [Serializable]
        private sealed class PackageDescription
        {
            public SampleDescription[] samples;
        }

        [Serializable]
        private sealed class SampleDescription
        {
            public string path;
        }
    }
}
