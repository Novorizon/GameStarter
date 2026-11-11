using System.Collections.Generic;
using UnityEngine;

namespace Game.HotUpdate.Vfx
{
    public sealed partial class ForestOutpostVfx
    {
        private enum Tile
        {
            Halo, Star, Flare, Smoke,
            Ring, Rune, Spark, Shard,
            Arrow, Shell, Slash, Leaf,
            Crystal, Crack, Solid, Flower
        }

        private readonly struct Profile
        {
            internal readonly Color Color;
            internal readonly Color Core;
            internal readonly float Speed;
            internal readonly float MinFlight;
            internal readonly float MaxFlight;
            internal readonly float Loft;
            internal readonly float MaxRise;
            internal readonly float Impact;
            internal readonly float Recoil;

            internal Profile(Color color, Color core, float speed, float minFlight, float maxFlight, float loft, float maxRise, float impact, float recoil)
            {
                Color = color;
                Core = core;
                Speed = speed;
                MinFlight = minFlight;
                MaxFlight = maxFlight;
                Loft = loft;
                MaxRise = maxRise;
                Impact = impact;
                Recoil = recoil;
            }
        }

        private static readonly Color Ice = new Color(.36f, .70f, 1f);
        private static readonly Color Jade = new Color(.24f, .94f, .78f);
        private static readonly Color Lilac = new Color(.62f, .62f, 1f);
        private static readonly Color Amber = new Color(1f, .55f, .16f);
        private static readonly Color Cream = new Color(1f, .93f, .72f);
        private static readonly Color BlueInk = new Color(.16f, .29f, .64f);
        private static readonly Color Bronze = new Color(.70f, .29f, .11f);
        private static readonly Color Soot = new Color(.47f, .39f, .35f);
        private static readonly Color Dust = new Color(.79f, .66f, .52f);
        private static readonly Color Coral = new Color(1f, .40f, .26f);
        private static readonly Color Moss = new Color(.68f, .81f, .44f);
        private static readonly Color JadeShadow = new Color(.22f, .57f, .45f);
        private static readonly Profile[] Profiles =
        {
            new Profile(Ice, new Color(.89f, .97f, 1f), 17f, .12f, .31f, .06f, .29f, .29f, .55f),
            new Profile(Jade, new Color(.83f, 1f, .97f), 12f, .17f, .41f, .14f, .63f, .45f, .78f),
            new Profile(Amber, Cream, 9f, .22f, .50f, .43f, 1.72f, .62f, 1f)
        };

        private const int TrailSegments = 12;
        private const float AtlasInset = 3f / 512f;
        private readonly List<Vector3> vertices = new List<Vector3>(16384);
        private readonly List<Vector3> uvs = new List<Vector3>(16384);
        private readonly List<Color32> colors = new List<Color32>(16384);
        private readonly List<int> indices = new List<int>(24576);
        private Vector3 cameraRight;
        private Vector3 cameraUp;
        private Vector3 cameraForward;
        private Matrix4x4 worldToBatch;

        private void BeginBatch()
        {
            vertices.Clear();
            uvs.Clear();
            colors.Clear();
            indices.Clear();
            var view = battleCamera.transform;
            cameraRight = view.right;
            cameraUp = view.up;
            cameraForward = view.forward;
            worldToBatch = transform.worldToLocalMatrix;
        }

        private void SubmitBatch()
        {
            batchRenderer.enabled = vertices.Count > 0;
            if (!batchRenderer.enabled)
            {
                return;
            }

            mesh.Clear(true);
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetColors(colors);
            mesh.SetTriangles(indices, 0);
        }

        private void DrawShot(Shot shot, Profile profile, float impactAge)
        {
            float head = Mathf.Clamp01(shot.Age / shot.Flight);
            float tailFade = 1 - Mathf.Clamp01(impactAge / .13f);
            Vector3 forward = shot.End - shot.Muzzle;
            Vector3 side = Vector3.Cross(Vector3.up, forward).normalized;
            float angle = ScreenAngle(Path(shot, Mathf.Min(1, head + .01f)) - Path(shot, Mathf.Max(0, head - .01f)));
            DrawMuzzle(shot, profile, ScreenAngle(Path(shot, .05f) - shot.Muzzle));
            if (tailFade > 0)
            {
                DrawFlight(shot, profile, head, angle, side, tailFade);
            }

            if (impactAge >= 0 && impactAge < profile.Impact)
            {
                DrawImpact(shot, profile, impactAge / profile.Impact, angle);
            }

            float defeatAge = impactAge - DefeatDelay;
            if (shot.Defeated && defeatAge >= 0 && defeatAge < DefeatDuration)
            {
                DrawDefeat(shot.Feet, shot.Seed, defeatAge / DefeatDuration);
            }
        }

        private void DrawMuzzle(Shot shot, Profile profile, float angle)
        {
            float duration = shot.Style == ForestShotStyle.RapidCannon ? .23f : .16f;
            float t = shot.Age / duration;
            if (t >= 1)
            {
                return;
            }

            float flash = Mathf.Pow(1 - t, 2.7f);
            Vector3 p = shot.Muzzle - cameraForward * .03f;
            switch (shot.Style)
            {
                case ForestShotStyle.OakArrow:
                    Billboard(Tile.Halo, p, .79f, .42f, angle, Ice, flash * .74f, .75f);
                    Billboard(Tile.Slash, p, .56f + t * .35f, .58f, angle + 84, Ice, (1 - t) * .75f);
                    Billboard(Tile.Star, p, .83f, .27f, angle, profile.Core, flash, .42f);
                    break;
                case ForestShotStyle.Starlight:
                    Billboard(Tile.Halo, p, .95f, .95f, 0, Jade, flash * .67f, .65f);
                    Billboard(Tile.Rune, p, .54f + t * .40f, .69f + t * .40f, t * 95, Jade, (1 - t) * .90f, .29f);
                    Billboard(Tile.Star, p, .53f, .62f, angle, profile.Core, flash, .50f);
                    break;
                case ForestShotStyle.RapidCannon:
                    Vector3 forward = ScreenVector(angle);
                    Vector3 smoke = p - forward * .11f + Vector3.up * (.14f + .21f * t);
                    Billboard(Tile.Smoke, smoke, .74f + .44f * t, .62f + .47f * t, 13, Color.white, (1 - t) * .66f);
                    Billboard(Tile.Halo, p + forward * .16f, 1.23f, .83f, angle, Amber, flash * .76f, .79f);
                    Billboard(Tile.Flare, p + forward * .20f, 1.22f + .20f * t, .79f, angle, Color.white, flash, .20f);
                    Billboard(Tile.Star, p, 1.14f, .45f, angle, Cream, flash, .64f);
                    break;
            }
        }

        private void DrawFlight(Shot shot, Profile profile, float head, float angle, Vector3 side, float fade)
        {
            Vector3 p = Path(shot, head);
            bool flying = shot.Age < shot.Flight;
            switch (shot.Style)
            {
                case ForestShotStyle.OakArrow:
                    Ribbon(shot, head, .71f, .17f, side, 0, BlueInk, fade * .69f);
                    Ribbon(shot, head, .69f, .093f, side, -.41f, Ice, fade * .72f, .28f);
                    Ribbon(shot, head, .66f, .113f, side, 0, Ice, fade * .93f, .17f);
                    Ribbon(shot, head, .62f, .061f, side, 0, profile.Core, fade, .55f);
                    FlightMotes(shot, head, side, Tile.Leaf, Ice, fade, 4);
                    if (flying)
                    {
                        Billboard(Tile.Arrow, p, .68f, .25f, angle, Color.white, 1);
                        Billboard(Tile.Halo, p + ScreenVector(angle) * .20f, .55f, .25f, angle, Ice, .62f, .67f);
                        Billboard(Tile.Star, p + ScreenVector(angle) * .23f, .24f, .16f, angle, profile.Core, .91f, .55f);
                    }
                    break;
                case ForestShotStyle.Starlight:
                    Ribbon(shot, head, .83f, .20f, side, 1, JadeShadow, fade * .71f);
                    Ribbon(shot, head, .80f, .127f, side, 1, Jade, fade * .90f, .20f);
                    Ribbon(shot, head, .76f, .061f, side, 1, profile.Core, fade, .51f);
                    Ribbon(shot, head, .81f, .17f, side, -1, BlueInk, fade * .68f);
                    Ribbon(shot, head, .78f, .081f, side, -1, Lilac, fade, .41f);
                    FlightMotes(shot, head, side, Tile.Star, profile.Core, fade, 6);
                    if (flying)
                    {
                        Billboard(Tile.Halo, p, .74f, .74f, 0, Jade, .74f, .73f);
                        Billboard(Tile.Crystal, p, .43f, .36f, angle + shot.Age * 350f, Color.white, 1);
                        Billboard(Tile.Star, p, .45f, .38f, angle, profile.Core, .94f, .64f);
                    }
                    break;
                case ForestShotStyle.RapidCannon:
                    for (int i = 8; i >= 1; i--)
                    {
                        float sample = head - .092f * i;
                        if (sample > 0)
                        {
                            float size = .19f + i * .040f;
                            float drift = i * .011f * Mathf.Sin(i * 3.1f + shot.Seed);
                            Vector3 smoke = Path(shot, sample) + Vector3.up * (i * .015f) + side * drift;
                            Billboard(Tile.Smoke, smoke, size, size * .90f, i * 61, Color.white, fade * (.82f - i * .052f));
                        }
                    }

                    Ribbon(shot, head, .69f, .075f, side, 0, Bronze, fade * .93f);
                    Ribbon(shot, head, .65f, .061f, side, 0, Amber, fade, .55f);
                    FlightMotes(shot, head, side, Tile.Spark, Cream, fade, 5);
                    if (flying)
                    {
                        Vector3 shadow = new Vector3(p.x, shot.Feet.y, p.z);
                        Ground(Tile.Halo, shadow, .48f + .21f * head, 0, Soot, .25f);
                        Billboard(Tile.Halo, p, .63f, .54f, 0, Amber, .76f, .73f);
                        Billboard(Tile.Shell, p, .42f, .42f, angle + shot.Age * 265f, Color.white, 1);
                    }
                    break;
            }
        }

        private void DrawImpact(Shot shot, Profile profile, float t, float angle)
        {
            Vector3 p = shot.End - cameraForward * .18f;
            float fade = 1 - t;
            float flash = Mathf.Pow(fade, 3.2f);
            switch (shot.Style)
            {
                case ForestShotStyle.OakArrow:
                    Billboard(Tile.Halo, p, 1.28f, .68f, angle, Ice, flash * .78f, .65f);
                    Billboard(Tile.Slash, p, 1.10f + t * .29f, .68f + t * .10f, angle - 16, BlueInk, fade * .79f);
                    Billboard(Tile.Slash, p, 1.16f + t * .32f, .53f + t * .11f, angle - 18, Ice, fade * .95f, .32f);
                    Billboard(Tile.Star, p, 1.30f, .33f, angle - 16, profile.Core, flash, .60f);
                    Scatter(p, shot.Seed, t, 7, .55f, .17f, Tile.Shard, new Color(.99f, .76f, .40f), Ice);
                    break;
                case ForestShotStyle.Starlight:
                    Billboard(Tile.Halo, p, 1.59f, 1.49f, 0, Jade, flash * .74f, .71f);
                    float rune = 1.02f + t * .59f;
                    Billboard(Tile.Rune, p, rune, rune, 16 + 95 * t, JadeShadow, fade * .65f);
                    Billboard(Tile.Rune, p, rune * .98f, rune * .98f, 16 + 95 * t, Jade, fade * .78f, .32f);
                    float bloom = 1.08f + .61f * Ease(t);
                    Billboard(Tile.Flower, p, bloom, bloom, 16 + t * 43, Color.white, fade * fade, .25f);
                    Billboard(Tile.Star, p, .77f, .77f, 25 + 45 * t, profile.Core, flash, .65f);
                    for (int i = 0; i < 5; i++)
                    {
                        float a = i * 1.257f + t * 2.0f;
                        Vector3 mote = p + (cameraRight * Mathf.Cos(a) + cameraUp * Mathf.Sin(a)) * (.23f + t * .49f);
                        Billboard(Tile.Star, mote, .21f * fade + .06f, .20f * fade + .05f, i * 31 + 90 * t, profile.Core, fade, .38f);
                    }
                    Ground(Tile.Rune, shot.Feet, .67f + t * .99f, t * 70, Jade, fade * .79f);
                    break;
                case ForestShotStyle.RapidCannon:
                    Ground(Tile.Ring, shot.Feet, .88f + Ease(t) * 1.42f, 0, Bronze, fade * .85f);
                    Ground(Tile.Ring, shot.Feet, .83f + Ease(t) * 1.42f, 0, Amber, fade * .98f, .31f);
                    Ground(Tile.Ring, shot.Feet, .46f + Ease(t) * 1.12f, 35, Cream, flash * .95f, .55f);
                    for (int i = 0; i < 6; i++)
                    {
                        float a = i * 1.047f + shot.Seed * .51f;
                        Vector3 offset = new Vector3(Mathf.Cos(a), .18f + .57f * t, Mathf.Sin(a)) * (.14f + .56f * Ease(t));
                        float size = .37f + .58f * t + (i % 3) * .13f;
                        float smokeAlpha = fade * Mathf.Min(1, .27f + t * 5) * .86f;
                        Billboard(Tile.Smoke, p + offset, size, size, i * 67 - t * 28, Color.white, smokeAlpha);
                    }

                    Billboard(Tile.Halo, p, 2.35f, 1.91f, 0, Amber, fade * fade * .78f, .78f);
                    float flare = 1.45f + .66f * Ease(t);
                    Billboard(Tile.Flare, p, flare, flare * .87f, shot.Seed * 17 + 34 * t, Color.white, fade * fade, .16f);
                    Billboard(Tile.Star, p, 1.14f, .89f, 34, new Color(1f, .99f, .91f), flash, .63f);
                    Scatter(p, shot.Seed, t, 13, 1.08f, .39f, Tile.Spark, Cream, Amber);
                    if (t > .12f)
                    {
                        Scatter(p, shot.Seed + 10, Mathf.Lerp(.24f, 1, (t - .12f) / .88f), 6, .78f, .26f, Tile.Shard, Dust, Soot);
                    }
                    break;
            }
        }

        private void DrawDefeat(Vector3 feet, int seed, float t)
        {
            Ground(Tile.Ring, feet, .48f + Ease(t) * 1.09f, 28, JadeShadow, (1 - t) * .88f);
            Ground(Tile.Rune, feet, .38f + Ease(t) * .76f, t * 78, Moss, (1 - t) * .75f);
            Billboard(Tile.Halo, feet + Vector3.up * .20f, .87f, .60f, 0, Moss, (1 - t) * .44f);
            Scatter(feet + Vector3.up * .15f, seed, t, 10, .59f, .52f, Tile.Leaf, Moss, JadeShadow);
        }

        private void DrawBaseHit(Vector3 gate, int seed, float t)
        {
            Vector3 p = gate - cameraForward * .12f;
            Vector3 feet = gate - Vector3.up * .36f;
            float fade = 1 - t;
            Billboard(Tile.Halo, p, 1.48f, 1.12f, 0, Coral, fade * fade * .65f);
            Billboard(Tile.Crack, p, .96f + t * .21f, .79f + t * .17f, -9 + 18 * t, Coral, fade * .95f);
            Billboard(Tile.Star, p, .79f, .34f, -21, Cream, Mathf.Pow(fade, 4));
            Ground(Tile.Ring, feet, .59f + Ease(t) * 1.26f, 0, Coral, fade * fade * .81f);
            Scatter(p, seed, t, 10, .73f, .22f, Tile.Shard, Dust, Coral);
        }

        private void Scatter(Vector3 center, int seed, float t, int count, float range, float rise, Tile tile, Color core, Color rim)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (seed & 63) * .27f + i * 2.39996f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                float variance = .54f + (i * 7 % 11) * .047f;
                Vector3 p = center + dir * ((.09f + Ease(t) * range) * variance)
                    + Vector3.up * (rise * variance * (4 * t * (1 - t) + t * .65f));
                float width = (.18f + .13f * variance) * (1 - .57f * t);
                float height = tile == Tile.Spark ? width * .39f : width * .79f;
                Billboard(tile, p, width, height, ScreenAngle(dir) + i % 3 * 16 + t * 85, i % 3 == 0 ? core : rim, 1 - t);
            }
        }

        private void FlightMotes(Shot shot, float head, Vector3 side, Tile tile, Color color, float fade, int count)
        {
            for (int i = 0; i < count; i++)
            {
                float t = head - .085f * (i + 1);
                if (t > 0)
                {
                    float offset = (.05f + i * .017f) * (i % 2 == 0 ? -1 : 1);
                    Vector3 p = Path(shot, t) + side * offset + Vector3.up * (.015f * i);
                    Billboard(tile, p, .17f, .093f, shot.Seed * 21 + i * 31, color, fade * (1 - i / (float)count));
                }
            }
        }

        private static Vector3 Path(Shot shot, float t)
        {
            return Vector3.LerpUnclamped(shot.Muzzle, shot.End, t) + Vector3.up * (4 * shot.Rise * t * (1 - t));
        }

        private static Vector3 Trace(Shot shot, float t, Vector3 side, float curl)
        {
            Vector3 p = Path(shot, t);
            if (curl == 0)
            {
                return p;
            }

            float angle = t * 16 + shot.Age * 8;
            float amplitude = curl * Mathf.Sin(t * Mathf.PI) * .18f;
            return p + (side * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle)) * amplitude;
        }

        private void Ribbon(Shot shot, float head, float trail, float width, Vector3 side, float curl, Color color, float alpha, float emission = 0)
        {
            float start = Mathf.Max(0, head - trail);
            if (head - start < .001f)
            {
                return;
            }

            int first = vertices.Count;
            int solid = (int)Tile.Solid;
            Vector3 uv = new Vector3((solid % 4 + .5f) * .25f, 1 - (solid / 4 + .5f) * .25f, emission);
            for (int i = 0; i <= TrailSegments; i++)
            {
                float f = i / (float)TrailSegments;
                float t = Mathf.Lerp(start, head, f);
                Vector3 p = Trace(shot, t, side, curl);
                Vector3 tangent = Trace(shot, t + .003f, side, curl) - p;
                Vector3 edge = Vector3.Cross(cameraForward, tangent).normalized * (width * (.09f + .41f * f));
                Color32 tint = Tint(color, alpha * f);
                Vertex(p - edge, uv, tint);
                Vertex(p + edge, uv, tint);
                if (i > 0)
                {
                    AddTriangles(first + (i - 1) * 2);
                }
            }
        }

        private Vector3 ScreenVector(float angle)
        {
            float radians = angle * Mathf.Deg2Rad;
            return cameraRight * Mathf.Cos(radians) + cameraUp * Mathf.Sin(radians);
        }

        private float ScreenAngle(Vector3 vector)
        {
            return Mathf.Atan2(Vector3.Dot(vector, cameraUp), Vector3.Dot(vector, cameraRight)) * Mathf.Rad2Deg;
        }

        private void Billboard(Tile tile, Vector3 p, float width, float height, float angle, Color color, float alpha, float emission = 0)
        {
            Vector3 right = ScreenVector(angle);
            Vector3 up = Vector3.Cross(cameraForward, right);
            Quad(tile, p, right * (width * .5f), up * (height * .5f), color, alpha, emission);
        }

        private void Ground(Tile tile, Vector3 p, float size, float angle, Color color, float alpha, float emission = 0)
        {
            var rotation = Quaternion.Euler(0, angle, 0);
            float radius = size * .5f;
            Quad(tile, p + Vector3.up * .04f, rotation * Vector3.right * radius, rotation * Vector3.forward * radius, color, alpha, emission);
        }

        private void Quad(Tile tile, Vector3 p, Vector3 right, Vector3 up, Color color, float alpha, float emission)
        {
            if (alpha <= .01f)
            {
                return;
            }

            int index = (int)tile;
            float u = index % 4 * .25f;
            float v = .75f - index / 4 * .25f;
            float left = u + AtlasInset;
            float bottom = v + AtlasInset;
            float rightUv = u + .25f - AtlasInset;
            float top = v + .25f - AtlasInset;
            Color32 tint = Tint(color, alpha);
            int first = vertices.Count;
            Vertex(p - right - up, new Vector3(left, bottom, emission), tint);
            Vertex(p - right + up, new Vector3(left, top, emission), tint);
            Vertex(p + right - up, new Vector3(rightUv, bottom, emission), tint);
            Vertex(p + right + up, new Vector3(rightUv, top, emission), tint);
            AddTriangles(first);
        }

        private void Vertex(Vector3 p, Vector3 uv, Color32 tint)
        {
            vertices.Add(worldToBatch.MultiplyPoint3x4(p));
            uvs.Add(uv);
            colors.Add(tint);
        }

        private void AddTriangles(int first)
        {
            indices.Add(first);
            indices.Add(first + 1);
            indices.Add(first + 2);
            indices.Add(first + 2);
            indices.Add(first + 1);
            indices.Add(first + 3);
        }

        private static Color32 Tint(Color color, float alpha)
        {
            return new Color(color.r, color.g, color.b, Mathf.Clamp01(alpha));
        }

        private static float Ease(float t)
        {
            return 1 - (1 - t) * (1 - t);
        }
    }
}
