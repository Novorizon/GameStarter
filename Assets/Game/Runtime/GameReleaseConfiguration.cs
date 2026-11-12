using System;
using Game.Framework.HotUpdate;
using UnityEngine;

namespace Game
{
    public static class GameReleaseConfiguration
    {
        public const string DownloadRoot = "https://15000.top/game-updates/";
        public const string GameServerHost = "15000.top";
        public const int GameServerPort = 22370;
        public const string Channel = "development";
        private const string DefaultAppId = "com.Novorizon.GameStarter";

        public static string AppId
        {
            get
            {
                string identifier = Application.identifier;
                return string.IsNullOrWhiteSpace(identifier) ? DefaultAppId : identifier;
            }
        }

        public static UpdateSettings CreateSettings(GameUpdateKey key = null)
        {
            key = key != null ? key : Resources.Load<GameUpdateKey>("GameUpdateKey");
            string app = AppId;
            return new UpdateSettings
            {
                appId = app,
                channel = Channel,
                remoteBaseUrl = DownloadRoot + "manifests/" + app + "/",
                scopeRemoteByBaseline = true,
                enableRemote = key != null && !string.IsNullOrWhiteSpace(key.modulus),
                trustedKeys = key == null ? Array.Empty<TrustedKey>() : new[]
                {
                    new TrustedKey
                    {
                        keyId = key.keyId,
                        modulus = key.modulus,
                        exponent = key.exponent
                    }
                }
            };
        }

        public static string ResourceUrl(string platform, string package)
        {
            return DownloadRoot + "assets/" + AppId + "/" + platform + "/" + package + "/";
        }
    }
}
