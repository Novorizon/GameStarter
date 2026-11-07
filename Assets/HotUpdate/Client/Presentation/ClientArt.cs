using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.Resource;
using TMPro;
using UnityEngine;

namespace Game.HotUpdate.Client
{
    public sealed class ClientArt : IDisposable
    {
        private readonly List<IDisposable> leases = new List<IDisposable>();
        public TMP_FontAsset Font { get; private set; }
        public Sprite Panel { get; private set; }
        public Sprite Avatar { get; private set; }
        public Texture2D Atlas { get; private set; }
        public Texture2D Background { get; private set; }
        public Sprite CampMap { get; private set; }
        public string ArrivalScript { get; private set; }
        public string Title { get; private set; }

        public static async Task<ClientArt> LoadAsync(cfg.ClientTheme theme, CancellationToken token)
        {
            var art = new ClientArt { Title = theme.Title };
            try
            {
                art.Font = await art.Load<TMP_FontAsset>(theme.Font, token);
                art.Panel = await art.Load<Sprite>(theme.Panel, token);
                art.Avatar = await art.Load<Sprite>(theme.Avatar, token);
                art.Atlas = await art.Load<Texture2D>(theme.Icons, token);
                art.CampMap = await art.Load<Sprite>(theme.CampMap, token);
                art.ArrivalScript = (await art.Load<TextAsset>("Camp/Story/Arrival", token)).text;
                // 显式资源依赖保留运行时程序化模型/阴影所需 Shader，避免仅靠 Shader.Find 被构建裁剪。
                await art.Load<Material>("Common/Rendering/WorldLit", token);
                await art.Load<Material>("Common/Rendering/WorldSprite", token);
                var picture = await art.Load<TextAsset>(theme.Background, token);
                var layout = await art.Load<TextAsset>(theme.StartupLayout, token);
                token.ThrowIfCancellationRequested();
                art.Background = StartupArtwork.CreateTexture(picture.bytes);
                StartupArtwork.CacheTheme(picture.bytes, layout.text);
                return art;
            }
            catch
            {
                art.Dispose();
                throw;
            }
        }

        private async Task<T> Load<T>(string location, CancellationToken token) where T : UnityEngine.Object
        {
            var lease = await ResourceManager.Instance.AcquireAsync<T>(location, token, true);
            leases.Add(lease);
            return lease.Asset;
        }

        public void Dispose()
        {
            if (Background != null)
            {
                UnityEngine.Object.Destroy(Background);
                Background = null;
            }

            foreach (var lease in leases)
            {
                lease.Dispose();
            }

            leases.Clear();
        }
    }
}
