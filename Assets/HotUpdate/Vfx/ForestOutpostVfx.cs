using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Game.HotUpdate.Vfx
{
    public enum ForestShotStyle
    {
        OakArrow,
        Starlight,
        RapidCannon
    }

    public sealed partial class ForestOutpostVfx : MonoBehaviour
    {
        private const int ShotLimit = 48;
        private const int BurstLimit = 16;
        private const float DefeatDelay = .10f;
        private const float DefeatDuration = .48f;
        private const float BaseHitDuration = .46f;

        private struct Shot
        {
            internal ForestShotStyle Style;
            internal Vector3 Muzzle;
            internal Vector3 End;
            internal Vector3 Feet;
            internal Transform Follow;
            internal Transform ReactionPivot;
            internal float Flight;
            internal float Rise;
            internal float Age;
            internal int Seed;
            internal bool Defeated;
        }

        private struct Burst
        {
            internal Vector3 Position;
            internal float Age;
            internal int Seed;
            internal bool Defeated;
        }

        private struct Reaction
        {
            internal Transform Pivot;
            internal Vector3 RestPosition;
            internal Quaternion RestRotation;
            internal Vector3 Push;
            internal Vector3 TiltAxis;
            internal float Strength;
            internal float Age;
        }

        private readonly List<Shot> shots = new List<Shot>(ShotLimit);
        private readonly List<Burst> bursts = new List<Burst>(BurstLimit);
        private readonly List<Reaction> reactions = new List<Reaction>(ShotLimit);
        private Camera battleCamera;
        private MeshRenderer batchRenderer;
        private Mesh mesh;
        private Material material;
        private int nextSeed;

        public bool Paused { get; set; }

        public void Initialize(Camera camera, Material combatMaterial)
        {
            if (combatMaterial == null)
            {
                throw new ArgumentNullException(nameof(combatMaterial));
            }

            Initialize(camera, combatMaterial.mainTexture as Texture2D, combatMaterial);
        }

        public void Initialize(Camera camera, Texture2D atlas, Material worldSprite)
        {
            if (mesh != null)
            {
                throw new InvalidOperationException("The battle VFX player is already initialized.");
            }

            if (camera == null || atlas == null || worldSprite == null)
            {
                throw new ArgumentException("The battle VFX player requires a camera, atlas and material.");
            }

            battleCamera = camera;
            material = new Material(worldSprite) { name = "Forest Outpost VFX", mainTexture = atlas };
            mesh = new Mesh { name = "Forest Outpost VFX batch" };
            mesh.MarkDynamic();
            var batch = new GameObject("VFX Batch");
            batch.layer = gameObject.layer;
            batch.transform.SetParent(transform, false);
            batch.AddComponent<MeshFilter>().sharedMesh = mesh;
            batchRenderer = batch.AddComponent<MeshRenderer>();
            batchRenderer.sharedMaterial = material;
            batchRenderer.shadowCastingMode = ShadowCastingMode.Off;
            batchRenderer.receiveShadows = false;
            batchRenderer.lightProbeUsage = LightProbeUsage.Off;
            batchRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            batchRenderer.enabled = false;
        }

        public float PlayShot(
            ForestShotStyle style,
            Vector3 muzzle,
            Vector3 hitPoint,
            Vector3 feet,
            Transform liveHitPoint = null,
            Transform reactionPivot = null,
            bool defeated = false)
        {
            if (!isActiveAndEnabled || mesh == null || shots.Count == ShotLimit)
            {
                return 0;
            }

            if ((uint)style >= Profiles.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(style));
            }

            var profile = Profiles[(int)style];
            Vector3 end = liveHitPoint != null ? liveHitPoint.position : hitPoint;
            float distance = Vector3.Distance(muzzle, end);
            float flight = Mathf.Clamp(distance / profile.Speed, profile.MinFlight, profile.MaxFlight);
            shots.Add(new Shot
            {
                Style = style,
                Muzzle = muzzle,
                End = end,
                Feet = feet + Vector3.ProjectOnPlane(end - hitPoint, Vector3.up),
                Follow = liveHitPoint,
                ReactionPivot = reactionPivot,
                Flight = flight,
                Rise = Mathf.Clamp(distance * profile.Loft, .05f, profile.MaxRise),
                Seed = nextSeed++,
                Defeated = defeated
            });
            return flight;
        }

        public void PlayBaseHit(Vector3 gate)
        {
            if (isActiveAndEnabled && mesh != null && bursts.Count < BurstLimit)
            {
                bursts.Add(new Burst { Position = gate, Seed = nextSeed++ });
            }
        }

        public void PlayEnemyDefeat(Vector3 feet)
        {
            if (isActiveAndEnabled && mesh != null && bursts.Count < BurstLimit)
            {
                bursts.Add(new Burst { Position = feet, Seed = nextSeed++, Defeated = true });
            }
        }

        public void Clear()
        {
            foreach (var reaction in reactions)
            {
                Restore(reaction);
            }

            shots.Clear();
            bursts.Clear();
            reactions.Clear();
            if (batchRenderer != null)
            {
                batchRenderer.enabled = false;
            }
        }

        private void LateUpdate()
        {
            Advance(Mathf.Min(Time.unscaledDeltaTime, .1f));
        }

        private void Advance(float elapsed)
        {
            if (battleCamera == null || mesh == null)
            {
                return;
            }

            float delta = Paused ? 0 : elapsed;
            UpdateReactions(delta);
            BeginBatch();
            for (int i = 0; i < shots.Count;)
            {
                var shot = shots[i];
                var profile = Profiles[(int)shot.Style];
                float previousAge = shot.Age;
                shot.Age += delta;
                if (previousAge < shot.Flight && !Paused && shot.Follow != null)
                {
                    Vector3 next = shot.Follow.position;
                    shot.Feet += Vector3.ProjectOnPlane(next - shot.End, Vector3.up);
                    shot.End = next;
                }

                if (previousAge < shot.Flight && shot.Age >= shot.Flight)
                {
                    React(shot, profile.Recoil);
                }

                float impactAge = shot.Age - shot.Flight;
                float end = shot.Defeated ? Mathf.Max(profile.Impact, DefeatDelay + DefeatDuration) : profile.Impact;
                if (impactAge >= end)
                {
                    shots.RemoveAt(i);
                    continue;
                }

                DrawShot(shot, profile, impactAge);
                shots[i++] = shot;
            }

            for (int i = 0; i < bursts.Count;)
            {
                var burst = bursts[i];
                burst.Age += delta;
                float duration = burst.Defeated ? DefeatDuration : BaseHitDuration;
                if (burst.Age >= duration)
                {
                    bursts.RemoveAt(i);
                    continue;
                }

                if (burst.Defeated)
                {
                    DrawDefeat(burst.Position, burst.Seed, burst.Age / duration);
                }
                else
                {
                    DrawBaseHit(burst.Position, burst.Seed, burst.Age / duration);
                }

                bursts[i++] = burst;
            }

            SubmitBatch();
        }

        private void React(Shot shot, float strength)
        {
            var pivot = shot.ReactionPivot;
            if (pivot == null)
            {
                return;
            }

            Vector3 direction = shot.End - shot.Muzzle;
            direction.y = 0;
            direction = direction.sqrMagnitude > .00001f ? direction.normalized : battleCamera.transform.right;
            var parent = pivot.parent;
            Vector3 push = parent == null ? direction : parent.InverseTransformVector(direction);
            Vector3 axis = Vector3.Cross(Vector3.up, direction);
            axis = parent == null ? axis : parent.InverseTransformDirection(axis);
            var reaction = new Reaction
            {
                Pivot = pivot,
                RestPosition = pivot.localPosition,
                RestRotation = pivot.localRotation,
                Push = push,
                TiltAxis = axis,
                Strength = strength
            };
            for (int i = 0; i < reactions.Count; i++)
            {
                if (reactions[i].Pivot == pivot)
                {
                    reaction.RestPosition = reactions[i].RestPosition;
                    reaction.RestRotation = reactions[i].RestRotation;
                    reactions[i] = reaction;
                    ApplyReaction(reaction);
                    return;
                }
            }

            reactions.Add(reaction);
            ApplyReaction(reaction);
        }

        private void UpdateReactions(float delta)
        {
            for (int i = reactions.Count - 1; i >= 0; i--)
            {
                var reaction = reactions[i];
                reaction.Age += delta;
                if (reaction.Pivot == null || reaction.Age >= .18f)
                {
                    Restore(reaction);
                    reactions.RemoveAt(i);
                    continue;
                }

                ApplyReaction(reaction);
                reactions[i] = reaction;
            }
        }

        private static void ApplyReaction(Reaction reaction)
        {
            float t = 1 - reaction.Age / .18f;
            float weight = reaction.Strength * t * t;
            reaction.Pivot.localPosition = reaction.RestPosition + reaction.Push * (.12f * weight);
            reaction.Pivot.localRotation = Quaternion.AngleAxis(14 * weight, reaction.TiltAxis) * reaction.RestRotation;
        }

        private static void Restore(Reaction reaction)
        {
            if (reaction.Pivot != null)
            {
                reaction.Pivot.localPosition = reaction.RestPosition;
                reaction.Pivot.localRotation = reaction.RestRotation;
            }
        }

        private void OnDisable()
        {
            Clear();
        }

        private void OnDestroy()
        {
            Clear();
            if (batchRenderer != null)
            {
                Destroy(batchRenderer.gameObject);
            }

            Destroy(mesh);
            Destroy(material);
        }
    }
}
