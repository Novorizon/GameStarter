using System;
using UnityEngine;

namespace Game.HotUpdate.Client
{
    /// <summary>场景用共享平铺纹理区分占位几何体的材质。</summary>
    internal static class PrototypeTextures
    {
        private const int Size = 32;

        internal static Texture2D Wood()
        {
            return Create("Prototype Wood", (x, y) => (byte)(x % 8 == 0 ? 172 : 208 + (x * 11 + y / 8 * 3) % 32));
        }

        internal static Texture2D Stone()
        {
            return Create("Prototype Stone", (x, y) =>
            {
                int column = x + y / 8 % 2 * 8;
                return (byte)(y % 8 == 0 || column % 16 == 0 ? 185 : 220 + (x * 7 + y * 3) % 20);
            });
        }

        internal static Texture2D Canvas()
        {
            return Create("Prototype Canvas", (x, y) => (byte)(220 + (x + y) % 2 * 12 + (x ^ y) % 4 * 3));
        }

        private static Texture2D Create(string name, Func<int, int, byte> shade)
        {
            var pixels = new Color32[Size * Size];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    byte value = shade(x, y);
                    pixels[y * Size + x] = new Color32(value, value, value, 255);
                }
            }

            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear
            };
            texture.SetPixels32(pixels);
            texture.Apply(true, true);
            return texture;
        }
    }
}
