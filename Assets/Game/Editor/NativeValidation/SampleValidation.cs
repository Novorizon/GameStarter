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

            var packages = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Where(package => package.name.StartsWith("com.novorizon.", StringComparison.Ordinal))
                .OrderBy(package => package.name, StringComparer.Ordinal)
                .ToArray();
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

                    string target = SampleTarget(package.name, source);
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

        public static void VerifyCompiledSamples()
        {
            string reportPath = "Artifacts/NativeValidation/sample-assemblies.json";
            if (File.Exists(reportPath))
            {
                File.Delete(reportPath);
            }

            var editor = UnityEditor.Compilation.CompilationPipeline
                .GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor)
                .ToDictionary(assembly => assembly.name);
            var player = UnityEditor.Compilation.CompilationPipeline
                .GetAssemblies(UnityEditor.Compilation.AssembliesType.Player)
                .ToDictionary(assembly => assembly.name);
            var verified = new System.Collections.Generic.List<CompiledSampleAssembly>();
            int sampleCount = 0;
            foreach (var package in UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Where(package => package.name.StartsWith("com.novorizon.", StringComparison.Ordinal))
                .OrderBy(package => package.name, StringComparer.Ordinal))
            {
                var description = JsonUtility.FromJson<PackageDescription>(File.ReadAllText(Path.Combine(package.resolvedPath, "package.json")));
                foreach (var sample in description.samples ?? Array.Empty<SampleDescription>())
                {
                    string source = Path.GetFullPath(Path.Combine(package.resolvedPath, sample.path));
                    string target = SampleTarget(package.name, source);
                    string prefix = Path.GetFullPath(target) + Path.DirectorySeparatorChar;
                    string[] definitions = Directory.GetFiles(source, "*.asmdef", SearchOption.AllDirectories);
                    if (definitions.Length == 0)
                    {
                        throw new InvalidOperationException("The sample declares no assembly definitions: " + source);
                    }

                    foreach (string definition in definitions)
                    {
                        string imported = Path.Combine(target, definition.Substring(source.Length + 1));
                        var expected = JsonUtility.FromJson<AssemblyDescription>(File.ReadAllText(definition));
                        if (!File.Exists(imported) || !editor.TryGetValue(expected.name, out var assembly)
                            || !File.Exists(assembly.outputPath)
                            || !assembly.sourceFiles.Any(file => Path.GetFullPath(file).StartsWith(prefix, StringComparison.Ordinal)))
                        {
                            throw new InvalidOperationException("Sample assembly was not compiled from its imported sources: " + expected.name + " (" + target + "). Import samples, then restart Unity before compiling.");
                        }

                        bool editorOnly = expected.includePlatforms != null && expected.includePlatforms.SequenceEqual(new[] { "Editor" });
                        if (!editorOnly && (!player.TryGetValue(expected.name, out var playerAssembly)
                            || !playerAssembly.sourceFiles.Any(file => Path.GetFullPath(file).StartsWith(prefix, StringComparison.Ordinal))))
                        {
                            throw new InvalidOperationException("Sample assembly is missing from the active Player compilation: " + expected.name);
                        }

                        verified.Add(new CompiledSampleAssembly
                        {
                            name = expected.name,
                            definition = imported,
                            editorDll = assembly.outputPath,
                            sourceFiles = assembly.sourceFiles,
                            player = !editorOnly
                        });
                    }

                    sampleCount++;
                }
            }

            if (sampleCount == 0)
            {
                throw new InvalidOperationException("No declared Novorizon samples were found to verify.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            File.WriteAllText(
                reportPath,
                JsonUtility.ToJson(
                    new SampleAssemblyReport
                    {
                        success = true,
                        sampleCount = sampleCount,
                        assemblies = verified.ToArray()
                    },
                    true));
            Debug.Log("PACKAGE_SAMPLE_ASSEMBLIES_VERIFIED " + sampleCount + " samples, " + verified.Count + " assemblies.");
        }

        private static string SampleTarget(string package, string source)
        {
            // A directory ending in .framework is treated as a native plugin by Unity.
            return Path.Combine(Destination, package.Replace('.', '_'), Path.GetFileName(source));
        }

        [Serializable]
        private sealed class AssemblyDescription
        {
            public string name;
            public string[] includePlatforms;
        }

        [Serializable]
        private sealed class CompiledSampleAssembly
        {
            public string name, definition, editorDll;
            public string[] sourceFiles;
            public bool player;
        }

        [Serializable]
        private sealed class SampleAssemblyReport
        {
            public bool success;
            public int sampleCount;
            public CompiledSampleAssembly[] assemblies;
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
