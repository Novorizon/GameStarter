using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Game.HotUpdate.Camp;
using Game.Resource;
using TMPro;
using UnityEngine;

namespace Game.HotUpdate.Client
{
    public sealed class ClientArt : IDisposable
    {
        private readonly List<IDisposable> leases = new List<IDisposable>();
        private readonly Dictionary<TravelerAppearance, Sprite> portraits = new Dictionary<TravelerAppearance, Sprite>();
        private Sprite[] campSprites;
        private Sprite[] expeditionIcons;
        public TMP_FontAsset Font { get; private set; }
        internal TMP_FontAsset CampFont { get; private set; }
        internal TMP_FontAsset ExpeditionNumberFont { get; private set; }
        internal Texture2D ExpeditionBackdrop { get; private set; }
        internal Texture2D ExpeditionBanner { get; private set; }
        public Sprite Panel { get; private set; }
        public Texture2D Atlas { get; private set; }
        public Texture2D Background { get; private set; }
        public Sprite CampMap { get; private set; }
        public Material WorldLit { get; private set; }
        public Material WorldSprite { get; private set; }
        public string ArrivalScript { get; private set; }
        public string OutpostScript { get; private set; }
        internal CampaignStory Campaign { get; private set; }
        public string Title { get; private set; }

        public Sprite Portrait(TravelerAppearance appearance)
        {
            return portraits.TryGetValue(appearance, out var portrait) ? portrait : portraits[TravelerAppearance.Forest];
        }

        public bool HasPortrait(TravelerAppearance appearance) => portraits.ContainsKey(appearance);

        internal Sprite CampSprite(CampPiece piece) => campSprites?[(int)piece];

        internal Sprite ExpeditionIcon(ExpeditionGlyph glyph) => expeditionIcons?[(int)glyph];

        public static async Task<ClientArt> LoadAsync(cfg.ClientTheme theme, CancellationToken token)
        {
            var art = new ClientArt { Title = theme.Title };
            try
            {
                art.Font = await art.Load<TMP_FontAsset>(theme.Font, token);
                art.Panel = await art.Load<Sprite>(theme.Panel, token);
                if (ResourceManager.Instance.IsValid("Common/UI/CampHud"))
                {
                    art.campSprites = CampUI.SliceSkin(await art.Load<Texture2D>("Common/UI/CampHud", token));
                }

                if (ResourceManager.Instance.IsValid("Common/Fonts/BattleHudMedium"))
                {
                    art.CampFont = await art.Load<TMP_FontAsset>("Common/Fonts/BattleHudMedium", token);
                }

                art.ExpeditionNumberFont = await art.LoadOptional<TMP_FontAsset>("Common/Fonts/BattleHudBold", token);
                art.ExpeditionBackdrop = await art.LoadOptional<Texture2D>("Common/UI/ExpeditionBackdrop", token);
                art.ExpeditionBanner = await art.LoadOptional<Texture2D>("Common/UI/ExpeditionBanner", token);
                art.expeditionIcons = ExpeditionChrome.SliceIcons(await art.LoadOptional<Texture2D>("Common/UI/ExpeditionGlyphs", token));
                art.portraits.Add(TravelerAppearance.Forest, await art.Load<Sprite>(theme.Avatar, token));
                await art.LoadPortraitIfAvailable(TravelerAppearance.Shore, "Common/UI/AvatarShore", token);
                await art.LoadPortraitIfAvailable(TravelerAppearance.Ember, "Common/UI/AvatarEmber", token);
                art.Atlas = await art.Load<Texture2D>(theme.Icons, token);
                art.CampMap = await art.Load<Sprite>(theme.CampMap, token);
                art.ArrivalScript = (await art.Load<TextAsset>("Camp/Story/Arrival", token)).text;
                art.OutpostScript = (await art.Load<TextAsset>("Camp/Story/Outpost", token)).text;
                art.Campaign = CampaignStory.Load((await art.Load<TextAsset>("Camp/Story/TideCampaign", token)).text);
                art.WorldLit = await art.Load<Material>("Common/Rendering/WorldLit", token);
                art.WorldSprite = await art.Load<Material>("Common/Rendering/WorldSprite", token);
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

        private async Task LoadPortraitIfAvailable(TravelerAppearance appearance, string location, CancellationToken token)
        {
            if (ResourceManager.Instance.IsValid(location))
            {
                portraits.Add(appearance, await Load<Sprite>(location, token));
            }
        }

        private async Task<T> Load<T>(string location, CancellationToken token) where T : UnityEngine.Object
        {
            var lease = await ResourceManager.Instance.AcquireAsync<T>(location, token, true);
            leases.Add(lease);
            return lease.Asset;
        }

        private async Task<T> LoadOptional<T>(string location, CancellationToken token) where T : UnityEngine.Object
        {
            return ResourceManager.Instance.IsValid(location) ? await Load<T>(location, token) : null;
        }

        public void Dispose()
        {
            DestroySprites(campSprites);
            DestroySprites(expeditionIcons);
            campSprites = null;
            expeditionIcons = null;

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

        private static void DestroySprites(Sprite[] sprites)
        {
            if (sprites == null)
            {
                return;
            }

            foreach (var sprite in sprites)
            {
                UnityEngine.Object.Destroy(sprite);
            }
        }
    }
}
