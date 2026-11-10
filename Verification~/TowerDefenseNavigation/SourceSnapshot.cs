using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

internal static class SourceSnapshot
{
    public static Dictionary<string, string> Capture()
    {
        string root = Directory.GetCurrentDirectory();
        if (!Directory.Exists(Path.Combine(root, "Assets/HotUpdate/TowerDefense/Domain")))
        {
            root = AppContext.BaseDirectory;
            while (!Directory.Exists(Path.Combine(root, "Assets/HotUpdate/TowerDefense/Domain")))
            {
                root = Directory.GetParent(root)?.FullName ?? throw new DirectoryNotFoundException("Cannot locate the GameStarter source root.");
            }
        }

        var files = Directory.GetFiles(Path.Combine(root, "Assets/HotUpdate/TowerDefense/Domain"), "*.cs", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(Path.Combine(root, "Verification~/TowerDefenseNavigation"), "*.cs"))
            .Concat(new[] { Path.Combine(root, "Assets/HotUpdate/TowerDefense/Data/NavigationLayout.cs") });
        return files.OrderBy(file => file, StringComparer.Ordinal).ToDictionary(
            file => Path.GetRelativePath(root, file).Replace('\\', '/'),
            file => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());
    }

    public static bool Matches(Dictionary<string, string> original)
    {
        var current = Capture();
        return original.Count == current.Count && original.All(file => current.TryGetValue(file.Key, out string? value) && value == file.Value);
    }
}
