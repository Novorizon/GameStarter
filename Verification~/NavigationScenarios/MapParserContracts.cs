using System;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;

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
    // The executable calls the production parser with on-disk fixture assets.
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
