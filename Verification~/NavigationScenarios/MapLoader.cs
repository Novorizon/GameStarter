using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.TowerDefense.Data;
using Game.HotUpdate.TowerDefense.Domain;

namespace GameStarter.Verification.Navigation
{
    internal sealed class MapLoader
    {
        private readonly string assets;
        private readonly cfg.Tables tables;

        internal MapLoader(string project)
        {
            assets = Path.Combine(project, "Assets", "GameAssets");
            tables = new cfg.Tables(name => new Luban.ByteBuf(File.ReadAllBytes(Path.Combine(assets, "Data", "Luban", name + ".bytes"))));
        }

        internal FightConfiguration Load(int stage, bool rotate = false)
            => Load(stage, rotate ? 2 : 0);

        internal FightConfiguration Load(int stage, int quarterTurns)
        {
            var config = TowerDefenseTables.Create(tables, stage);
            string name = Path.GetFileNameWithoutExtension(tables.TbTdStage.Get(stage).MapAsset);
            string json = File.ReadAllText(Path.Combine(assets, "TowerDefense", "Maps", name + ".json"));
            if (quarterTurns != 0)
            {
                var transformed = JsonNode.Parse(json);
                RotateCoordinates(transformed, quarterTurns);
                json = transformed.ToJsonString();
            }

            var parse = typeof(TowerDefenseMapObjectives).GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
            if (parse?.Invoke(null, new object[] { json }) is not TowerDefenseMapObjectives.Settings settings)
            {
                throw new InvalidOperationException("Failed to load navigation settings for stage " + stage);
            }

            settings.ApplyTo(config);
            return config;
        }

        private static void RotateCoordinates(JsonNode node, int quarterTurns)
        {
            if (node is JsonArray array)
            {
                foreach (var child in array)
                {
                    RotateCoordinates(child, quarterTurns);
                }
            }
            else if (node is JsonObject item)
            {
                if (item["x"] is JsonValue x && item["y"] is JsonValue y)
                {
                    int px = x.GetValue<int>();
                    int py = y.GetValue<int>();
                    (int rx, int ry) = quarterTurns switch
                    {
                        1 => (-py, px),
                        2 => (-px, -py),
                        3 => (py, -px),
                        _ => throw new ArgumentOutOfRangeException(nameof(quarterTurns))
                    };
                    item["x"] = rx;
                    item["y"] = ry;
                }

                foreach (var member in item)
                {
                    RotateCoordinates(member.Value, quarterTurns);
                }

                if (item["min"] is JsonObject min && item["max"] is JsonObject max)
                {
                    int ax = min["x"].GetValue<int>();
                    int ay = min["y"].GetValue<int>();
                    int bx = max["x"].GetValue<int>();
                    int by = max["y"].GetValue<int>();
                    min["x"] = Math.Min(ax, bx);
                    min["y"] = Math.Min(ay, by);
                    max["x"] = Math.Max(ax, bx);
                    max["y"] = Math.Max(ay, by);
                }
            }
        }
    }
}

namespace UnityEngine
{
    internal sealed class TextAsset
    {
        internal string text => throw new NotSupportedException();
    }

    internal static class JsonUtility
    {
        private static readonly JsonSerializerOptions Options = CreateOptions();

        internal static T FromJson<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);

        private static JsonSerializerOptions CreateOptions()
        {
            var resolver = new DefaultJsonTypeInfoResolver();
            resolver.Modifiers.Add(info =>
            {
                if (info.Kind == JsonTypeInfoKind.Object && info.Type.DeclaringType != null)
                {
                    info.CreateObject = () => Activator.CreateInstance(info.Type, nonPublic: true);
                }
            });
            return new JsonSerializerOptions { IncludeFields = true, TypeInfoResolver = resolver };
        }
    }
}

namespace Game.Resource
{
    internal sealed class ResourceManager
    {
        internal static ResourceManager Instance => throw new NotSupportedException();
        internal Task<Lease<T>> AcquireAsync<T>(string address, CancellationToken token, bool required) => throw new NotSupportedException();
    }

    internal sealed class Lease<T> : IDisposable
    {
        internal T Asset => throw new NotSupportedException();
        public void Dispose()
        {
        }
    }
}
