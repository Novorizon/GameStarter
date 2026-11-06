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

        public static UpdateSettings CreateSettings(GameUpdateKey key = null)
        {
            key = key != null ? key : Resources.Load<GameUpdateKey>("GameUpdateKey");
            string app = Application.identifier;
            return new UpdateSettings
            {
                appId = app,
                channel = Channel,
                remoteBaseUrl = DownloadRoot + "manifests/" + app + "/",
                remoteFilesBaseUrl = DownloadRoot + "patches/" + app + "/",
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
            return DownloadRoot + "assets/" + Application.identifier + "/" + platform + "/" + package + "/";
        }
    }
}
