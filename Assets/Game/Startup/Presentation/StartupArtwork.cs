using System;
using System.IO;
using UnityEngine;

namespace Game
{
    /// <summary>已通过资源系统加载的启动图片可供下次启动使用；不缓存或执行代码。</summary>
    public static class StartupArtwork
    {
        private static string CachedPath => Path.Combine(Application.persistentDataPath, "Startup", "background.png");
        private static string LayoutPath => Path.Combine(Application.persistentDataPath, "Startup", "layout.json");

        public static Texture2D CreateTexture(byte[] bytes)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(texture, bytes, true))
            {
                UnityEngine.Object.Destroy(texture);
                throw new InvalidDataException("The startup background is not a supported image.");
            }

            return texture;
        }

        public static Texture2D LoadBackground(out bool owned)
        {
            owned = false;
            try
            {
                if (File.Exists(CachedPath))
                {
                    var texture = CreateTexture(File.ReadAllBytes(CachedPath));
                    owned = true;
                    return texture;
                }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                Debug.LogWarning("Could not load the previous startup picture: " + error.Message);
            }

            return Resources.Load<Texture2D>("Bootstrap/Background");
        }

        public static StartupLayout LoadLayout()
        {
            try
            {
                var builtin = Resources.Load<TextAsset>("Bootstrap/Layout");
                string text = File.Exists(LayoutPath) ? File.ReadAllText(LayoutPath) : builtin.text;
                var layout = JsonUtility.FromJson<StartupLayout>(text);
                return layout != null && layout.version == 1 && layout.widgets != null ? layout : new StartupLayout();
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning("Could not load the startup layout: " + error.Message);
                return new StartupLayout();
            }
        }

        public static void CacheTheme(byte[] bytes, string layoutJson)
        {
            try
            {
                var layout = JsonUtility.FromJson<StartupLayout>(layoutJson);
                if (layout == null || layout.version != 1)
                {
                    Debug.LogWarning("Unsupported startup layout version; the previous startup theme was preserved.");
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(CachedPath));
                string temporary = CachedPath + ".pending";
                File.WriteAllBytes(temporary, bytes);
                File.Copy(temporary, CachedPath, true);
                File.Delete(temporary);
                File.WriteAllText(LayoutPath, layoutJson);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException || error is ArgumentException)
            {
                Debug.LogWarning("Could not preserve the startup picture: " + error.Message);
            }
        }
    }
}
