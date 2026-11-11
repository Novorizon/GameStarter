using System;
using System.Collections.Generic;
using Game.HotUpdate.Client;
using Game.HotUpdate.TowerDefense.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace Game.HotUpdate.TowerDefense.Presentation
{
    public sealed partial class TowerDefensePrototypeView
    {
        private readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
        private const float CameraPitch = 55;
        private const float MapWidth = 22;
        private const float MapDepth = 15.12f;
        private const float TowerFoundationRadius = FightSession.TowerFoundationRadiusMillimetres / 1000f;
        private const float TowerModelFoundationRadius = .52f;
        private const float TowerModelScale = TowerFoundationRadius / TowerModelFoundationRadius;
        private const float TowerGroundOffset = .02f;
        private static readonly float DefaultProjectedMapDepth = MapDepth * Mathf.Sin(CameraPitch * Mathf.Deg2Rad);
        private float projectedMapDepth = DefaultProjectedMapDepth;
        private float CellHalfWidth => configuration.BuildGridStep > 0 ? configuration.BuildGridStep / 2000f : .54f;
        private float CellHalfDepth => configuration.BuildGridStep > 0 ? configuration.BuildGridStep / 2000f : .58f;
        private Material stone;
        private Material stoneLight;
        private Material wood;
        private Material brass;
        private Material iron;
        private Material blue;
        private Material eye;
        private Material vertexColorMaterial;
        private Material litTemplate;
        private Material spriteTemplate;
        private Transform baseTarget;
        private readonly Dictionary<Material, Material> previewMaterials = new Dictionary<Material, Material>();
        private GameObject placementGhost;
        private LineRenderer placementFootprint;
        private bool? previewCanBuild;
        private PointerEventData pointerQuery;
        private bool trackingPlacementTouch;
        private int ghostDefinitionId;
        private Mesh shadowMesh;
        private long interpolationHoldTick = -1;

        private void CreateWorld(Sprite map, ClientArt art)
        {
            litTemplate = art?.WorldLit;
            spriteTemplate = art?.WorldSprite;
            field = new GameObject("Battlefield").transform;
            field.SetParent(transform, false);
            previousCamera = Camera.main;
            if (previousCamera != null)
            {
                previousCameraEnabled = previousCamera.enabled;
                previousCamera.enabled = false;
            }

            var cameraRoot = new GameObject("PrototypeCamera");
            cameraRoot.transform.SetParent(transform, false);
            cameraRoot.tag = "MainCamera";
            worldCamera = cameraRoot.AddComponent<Camera>();
            worldCamera.orthographic = true;
            worldCamera.transform.rotation = Quaternion.Euler(CameraPitch, 0, 0);
            worldCamera.transform.position = -worldCamera.transform.forward * 35;
            worldCamera.orthographicSize = 7.65f;
            worldCamera.clearFlags = CameraClearFlags.SolidColor;
            worldCamera.backgroundColor = new Color(.16f, .19f, .17f);
            worldCamera.depth = 20;
            var sun = new GameObject("DioramaSun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.15f;
            sun.transform.rotation = Quaternion.Euler(55, -35, 0);
            stone = MakeMaterial(new Color(.48f, .51f, .46f));
            stoneLight = MakeMaterial(new Color(.79f, .77f, .64f));
            wood = MakeMaterial(new Color(.39f, .24f, .12f));
            var stoneTexture = PrototypeTextures.Stone();
            var woodTexture = PrototypeTextures.Wood();
            materialAssets.Add(stoneTexture);
            materialAssets.Add(woodTexture);
            stone.mainTexture = stoneTexture;
            stoneLight.mainTexture = stoneTexture;
            wood.mainTexture = woodTexture;
            brass = MakeMaterial(new Color(.83f, .59f, .23f));
            iron = MakeMaterial(new Color(.20f, .25f, .25f));
            blue = MakeMaterial(new Color(.16f, .43f, .63f));
            eye = MakeMaterial(new Color(.07f, .12f, .09f), true);
            towerMaterials = new[] { blue, MakeMaterial(new Color(.13f, .80f, .77f)), brass };
            enemyMaterials = new[]
            {
                MakeMaterial(new Color(.43f, .65f, .20f)), MakeMaterial(new Color(.66f, .31f, .25f)),
                MakeMaterial(new Color(.48f, .32f, .61f)), MakeMaterial(new Color(.25f, .49f, .60f))
            };
            lineMaterial = MakeMaterial(new Color(.75f, .93f, .69f), true);
            CreateShadowMesh();
            mapRenderer = new GameObject("DioramaMapArtwork").AddComponent<SpriteRenderer>();
            mapRenderer.transform.SetParent(field, false);
            if (spriteTemplate != null)
            {
                mapRenderer.sharedMaterial = spriteTemplate;
            }

            // The map already contains the camera projection. Keep it facing the camera, behind every 3D unit.
            mapRenderer.transform.rotation = worldCamera.transform.rotation;
            ApplyMap(map);
            CreateObjective(configuration.Routes[0]);
            if (configuration.RelayRouteId != 0)
            {
                foreach (var route in configuration.Routes)
                {
                    if (route.Id == configuration.RelayRouteId)
                    {
                        CreateRelay(route);
                    }
                }
            }

            rangeRing = new GameObject("SelectedTowerRange").AddComponent<LineRenderer>();
            rangeRing.transform.SetParent(field, false);
            rangeRing.transform.rotation = Quaternion.Euler(90, 0, 0);
            rangeRing.alignment = LineAlignment.TransformZ;
            rangeRing.useWorldSpace = true;
            rangeRing.sharedMaterial = lineMaterial;
            rangeRing.loop = true;
            rangeRing.positionCount = 64;
            rangeRing.startWidth = .035f;
            rangeRing.endWidth = .035f;
            rangeRing.startColor = rangeRing.endColor = new Color(.57f, .93f, 1f, .95f);
            rangeRing.enabled = false;
        }

        private void ApplyMap(Sprite map)
        {
            mapRenderer.sprite = map;
            mapRenderer.enabled = map != null;
            float depth = DefaultProjectedMapDepth;
            if (map != null)
            {
                // Level coordinates are authored against the complete image, including its transparent border.
                var size = map.rect.size;
                var pivot = map.pivot - size * .5f;
                var camera = worldCamera.transform;
                float unitsPerPixel = MapWidth / size.x;
                float scale = unitsPerPixel * map.pixelsPerUnit;
                depth = size.y * unitsPerPixel;
                mapRenderer.transform.localScale = new Vector3(scale, scale, 1);
                mapRenderer.transform.position = field.position + camera.forward * 18
                    + camera.right * (pivot.x * unitsPerPixel) + camera.up * (pivot.y * unitsPerPixel);
            }

            if (!Mathf.Approximately(depth, projectedMapDepth))
            {
                projectedMapDepth = depth;
                lastWidth = 0;
            }
        }

        private void CreateObjective(RouteDefinition route)
        {
            var root = new GameObject("Defense Objective").transform;
            root.SetParent(field, false);
            var points = route.Points;
            var center = World(points[points.Length - 1], 0);
            root.localPosition = center;
            root.localRotation = Quaternion.LookRotation(center - World(points[points.Length - 2], 0));
            if (!configuration.Objective.HasBase)
            {
                CreatePortal(root);
                return;
            }

            if (configuration.Objective.BaseUpgrades.Count != 0)
            {
                baseTarget = root;
                float height = configuration.StageId switch
                {
                    2 => 1.82f,
                    3 => 2.13f,
                    4 => 3.12f,
                    _ => 2.38f
                };
                var hitbox = root.gameObject.AddComponent<BoxCollider>();
                hitbox.center = new Vector3(0, .5f * height, 0);
                hitbox.size = new Vector3(1.72f, height, 1.48f);
            }

            AddShadow(root, 1.15f);
            if (configuration.StageId == 2)
            {
                Primitive("Sluice platform", PrimitiveType.Cube, root, new Vector3(0, .1f, 0), new Vector3(1.8f, .2f, 1.7f), stoneLight);
                Primitive("Sluice door", PrimitiveType.Cube, root, new Vector3(0, .75f, .12f), new Vector3(1.33f, 1.32f, .30f), wood);
                foreach (float x in new[] { -.78f, .78f })
                {
                    Primitive("Sluice pillar", PrimitiveType.Cube, root, new Vector3(x, .93f, .1f), new Vector3(.25f, 1.65f, .40f), stone);
                }

                Primitive("Winch beam", PrimitiveType.Cube, root, new Vector3(0, 1.70f, .1f), new Vector3(1.88f, .23f, .41f), wood);
                var wheel = Primitive("Winch wheel", PrimitiveType.Cylinder, root, new Vector3(0, 1.45f, -.23f), new Vector3(.55f, .09f, .55f), brass);
                wheel.transform.localRotation = Quaternion.Euler(90, 0, 0);
                return;
            }

            if (configuration.StageId == 3)
            {
                Primitive("Signal platform", PrimitiveType.Cube, root, new Vector3(0, .16f, 0), new Vector3(1.62f, .32f, 1.50f), stoneLight);
                Primitive("Signal station", PrimitiveType.Cube, root, new Vector3(0, .79f, 0), new Vector3(1.05f, 1.09f, .93f), wood);
                Primitive("Tower flag", PrimitiveType.Cube, root, new Vector3(.40f, 1.99f, 0), new Vector3(.66f, .27f, .045f), blue);
                Prism("Alarm bell", root, new Vector3(0, 1.50f, 0), .34f, .40f, 8, brass, .18f);
                Prism("Signal roof", root, new Vector3(0, 1.93f, 0), .67f, .38f, 4, blue).transform.localRotation = Quaternion.Euler(0, 45, 0);
                return;
            }

            if (configuration.StageId == 4)
            {
                Prism("Lighthouse steps", root, new Vector3(0, .14f, 0), .90f, .27f, 8, stoneLight);
                Prism("White stone tower", root, new Vector3(0, 1.16f, 0), .62f, 1.90f, 8, stoneLight, .48f);
                Prism("Blue cornice", root, new Vector3(0, 2.11f, 0), .59f, .24f, 8, blue);
                Prism("Beacon cage", root, new Vector3(0, 2.44f, 0), .39f, .51f, 8, brass);
                Prism("Copper roof", root, new Vector3(0, 2.85f, 0), .55f, .39f, 8, blue, .10f);
                Primitive("Tower door", PrimitiveType.Cube, root, new Vector3(0, .52f, -.57f), new Vector3(.36f, .59f, .08f), wood);
                return;
            }

            Primitive("Keep foundation", PrimitiveType.Cube, root, new Vector3(0, .10f, 0), new Vector3(1.7f, .20f, 1.4f), stoneLight);
            Primitive("Keep hall", PrimitiveType.Cube, root, new Vector3(0, .65f, 0), new Vector3(1.15f, 1.05f, .95f), stoneLight);
            var roof = Prism("Blue slate roof", root, new Vector3(0, 1.40f, 0), .92f, .65f, 4, blue, 0);
            roof.transform.localRotation = Quaternion.Euler(0, 45, 0);
            Primitive("Keep gate", PrimitiveType.Cube, root, new Vector3(0, .43f, -.49f), new Vector3(.38f, .65f, .06f), wood);
            for (int side = -1; side <= 1; side += 2)
            {
                Prism("Gate tower", root, new Vector3(side * .67f, .56f, -.42f), .26f, 1.0f, 6, stone);
                Prism("Gate crown", root, new Vector3(side * .67f, 1.1f, -.42f), .32f, .16f, 6, stoneLight);
            }

            Primitive("Keep flagpole", PrimitiveType.Cube, root, new Vector3(0, 2, 0), new Vector3(.035f, .85f, .035f), brass);
            Primitive("Keep banner", PrimitiveType.Cube, root, new Vector3(.24f, 2.17f, 0), new Vector3(.46f, .25f, .035f), blue);
        }

        private void CreatePortal(Transform root)
        {
            AddShadow(root, .92f);
            Prism("Portal plinth", root, new Vector3(0, .10f, 0), .82f, .20f, 8, stoneLight);
            var ring = new GameObject("Portal ring").AddComponent<LineRenderer>();
            ring.transform.SetParent(root, false);
            ring.sharedMaterial = MakeMaterial(new Color(.36f, .91f, 1f), true);
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.startWidth = ring.endWidth = .12f;
            ring.positionCount = 40;
            for (int index = 0; index < ring.positionCount; index++)
            {
                float angle = 2 * Mathf.PI * index / ring.positionCount;
                ring.SetPosition(index, new Vector3(.66f * Mathf.Cos(angle), 1.05f + .86f * Mathf.Sin(angle), 0));
            }
        }

        private Position2 RelayPosition(RouteDefinition route)
        {
            var point = route.Points[configuration.RelayPointIndex];
            return new Position2(point.X, point.Y + 780);
        }

        private void CreateRelay(RouteDefinition route)
        {
            var root = new GameObject("Side Signal").transform;
            root.SetParent(field, false);
            root.localPosition = World(RelayPosition(route), 0);
            AddShadow(root, .50f);
            Prism("Side signal plinth", root, new Vector3(0, .16f, 0), .42f, .29f, 6, stoneLight);
            Primitive("Side signal mast", PrimitiveType.Cube, root, new Vector3(0, .64f, 0), new Vector3(.18f, .83f, .18f), wood);
            var light = MakeMaterial(new Color(1, .76f, .24f), true);
            relayLamp = Prism("Signal lantern", root, new Vector3(0, 1.09f, 0), .24f, .31f, 6, light);
        }

        private TowerView CreateTower(int id, int definition, Position2 position, bool preview = false)
        {
            var root = new GameObject("Tower " + id + " - Stone Watchtower");
            root.transform.SetParent(field, false);
            root.transform.localPosition = World(position, TowerGroundOffset);
            root.transform.localScale = Vector3.one * TowerModelScale;
            if (!preview)
            {
                AddShadow(root.transform, .72f);
            }

            Prism("Foundation", root.transform, new Vector3(0, .10f, 0), TowerModelFoundationRadius, .18f, 8, stoneLight);
            var body = Prism("Masonry", root.transform, new Vector3(0, .61f, 0), .39f, .90f, 8, stone, .34f);
            if (!preview)
            {
                var collider = body.AddComponent<BoxCollider>();
                collider.size = new Vector3(.84f, 1.1f, .84f);
                body.AddComponent<PrototypeWorldTarget>().TowerId = id;
            }

            Prism("Coping", root.transform, new Vector3(0, 1.10f, 0), .48f, .16f, 8, stoneLight);
            for (int course = 1; course <= 3; course++)
            {
                Prism("Stone course", root.transform, new Vector3(0, .20f + course * .24f, 0), .405f - course * .013f, .025f, 8, stoneLight);
            }
            for (int index = 0; index < 8; index++)
            {
                float angle = index * Mathf.PI / 4;
                var crenel = Primitive(
                    "Battlement",
                    PrimitiveType.Cube,
                    root.transform,
                    new Vector3(Mathf.Sin(angle) * .38f, 1.24f, Mathf.Cos(angle) * .38f),
                    new Vector3(.18f, .20f, .18f),
                    stoneLight);
                crenel.transform.localRotation = Quaternion.Euler(0, index * 45, 0);
                Destroy(crenel.GetComponent<Collider>());
            }

            var banner = Primitive("Blue banner", PrimitiveType.Cube, root.transform, new Vector3(0, .67f, -.39f), new Vector3(.22f, .42f, .025f), blue);
            Destroy(banner.GetComponent<Collider>());
            var turret = new GameObject("Turret").transform;
            turret.SetParent(root.transform, false);
            turret.localPosition = new Vector3(0, 1.18f, 0);
            if (configuration.Towers.Length > 1 && definition == configuration.Towers[1].Id)
            {
                Prism("Crystal pedestal", turret, new Vector3(0, .12f, 0), .28f, .16f, 8, brass);
                Prism("Crystal", turret, new Vector3(0, .49f, 0), .24f, .60f, 5, towerMaterials[1], 0);
                Prism("Crystal foot", turret, new Vector3(0, .18f, 0), .24f, .20f, 5, towerMaterials[1], 0);
            }
            else if (configuration.Towers.Length > 2 && definition == configuration.Towers[2].Id)
            {
                var barrel = Prism("Iron cannon", turret, new Vector3(0, .28f, .12f), .23f, .65f, 8, iron);
                barrel.transform.localRotation = Quaternion.Euler(70, 0, 0);
                Prism("Cannon mounting", turret, new Vector3(0, .10f, 0), .28f, .14f, 8, brass);
            }
            else
            {
                Primitive("Crossbow stock", PrimitiveType.Cube, turret, new Vector3(0, .19f, 0), new Vector3(.12f, .14f, .70f), wood);
                Primitive("Crossbow arms", PrimitiveType.Cube, turret, new Vector3(0, .23f, .10f), new Vector3(.70f, .10f, .12f), wood);
                Primitive("Arrow", PrimitiveType.Cube, turret, new Vector3(0, .30f, .06f), new Vector3(.035f, .035f, .78f), stoneLight);
            }

            foreach (var part in turret.GetComponentsInChildren<Collider>())
            {
                Destroy(part);
            }

            if (preview)
            {
                return new TowerView { Root = root, Turret = turret };
            }

            var beam = new GameObject("Shot").AddComponent<LineRenderer>();
            beam.transform.SetParent(root.transform, false);
            beam.sharedMaterial = lineMaterial;
            beam.startWidth = .055f;
            beam.endWidth = .018f;
            beam.positionCount = 2;
            beam.enabled = false;
            return new TowerView { Root = root, Turret = turret, Beam = beam };
        }

        private EnemyView CreateEnemy(int id, int definition)
        {
            var root = new GameObject("Enemy " + id + " - Forest Raider");
            root.transform.SetParent(field, false);
            var material = enemyMaterials[(Math.Abs(definition) - 1) % enemyMaterials.Length];
            bool colossus = definition == 5;
            float size = colossus ? 2.15f : definition == 3 ? 1.25f : .90f;
            root.transform.localScale = Vector3.one * size;
            AddShadow(root.transform, .38f);
            Prism("Body", root.transform, new Vector3(0, .29f, 0), .20f, .35f, 6, colossus ? stone : wood, .24f);
            Prism("Head", root.transform, new Vector3(0, .65f, 0), .26f, .34f, 7, colossus ? stoneLight : material, .16f);
            for (int side = -1; side <= 1; side += 2)
            {
                var ear = Prism("Ear", root.transform, new Vector3(side * .26f, .67f, 0), .10f, .30f, 4, material, 0);
                ear.transform.localRotation = Quaternion.Euler(0, 0, side * -55);
                var pupil = Primitive("Eye", PrimitiveType.Cube, root.transform, new Vector3(side * .095f, .67f, .227f), new Vector3(.065f, .065f, .04f), eye);
                Destroy(pupil.GetComponent<Collider>());
                var boot = Primitive("Boot", PrimitiveType.Cube, root.transform, new Vector3(side * .12f, .07f, .04f), new Vector3(.14f, .14f, .22f), iron);
                Destroy(boot.GetComponent<Collider>());
                if (colossus)
                {
                    Prism("Tide shell", root.transform, new Vector3(side * .26f, .36f, -.11f), .22f, .35f, 6, blue, .18f);
                }
            }

            var health = new GameObject("Health").transform;
            health.SetParent(root.transform, false);
            health.localPosition = new Vector3(0, 1.05f, 0);
            CreateHealthStroke(health, "Track", .085f, new Color(.08f, .10f, .13f), 0);
            var bar = CreateHealthStroke(health, "Fill", .053f, new Color(1, .41f, .44f), 1);
            return new EnemyView { Root = root, Health = bar };
        }

        private LineRenderer CreateHealthStroke(Transform parent, string name, float width, Color color, int order)
        {
            var line = new GameObject(name).AddComponent<LineRenderer>();
            line.transform.SetParent(parent, false);
            line.useWorldSpace = false;
            line.sharedMaterial = lineMaterial;
            line.startWidth = line.endWidth = width;
            line.startColor = line.endColor = color;
            line.numCapVertices = 8;
            line.sortingOrder = order;
            line.positionCount = 2;
            line.SetPosition(0, new Vector3(-.32f, 0, 0));
            line.SetPosition(1, new Vector3(.32f, 0, 0));
            return line;
        }

        private void SynchronizeWorld(float elapsed)
        {
            liveEnemies.Clear();
            var objectivePosition = World(configuration.Routes[0].Points[^1], .05f);
            const float tickSeconds = FightSession.TickMilliseconds / 1000f;
            bool interpolating = session.Phase == BattlePhase.Fighting && !session.IsPaused;
            if (!interpolating)
            {
                interpolationHoldTick = session.Tick;
            }

            float interpolation = interpolating && session.Tick != interpolationHoldTick ? Mathf.Clamp01(accumulator / tickSeconds) : 1f;
            foreach (var state in session.Enemies)
            {
                liveEnemies.Add(state.Id);
                if (!enemies.TryGetValue(state.Id, out var view))
                {
                    view = CreateEnemy(state.Id, state.DefinitionId);
                    view.Root.transform.localPosition = World(state.Position, .05f);
                    enemies.Add(state.Id, view);
                }

                var next = World(state.Position, .05f);
                var previous = World(state.PreviousPosition, .05f);
                var direction = state.AttackingBase ? objectivePosition - next : next - previous;
                if (direction.sqrMagnitude > .0001f)
                {
                    var rotation = Quaternion.LookRotation(direction);
                    int windupTicks = configuration.Objective.BaseAttackWindupTicks;
                    if (state.AttackingBase && windupTicks > 0)
                    {
                        long untilHit = state.NextBaseAttackTick - session.Tick;
                        float pitch = 0f;
                        if (untilHit == configuration.Objective.BaseAttackIntervalTicks)
                        {
                            pitch = 16f;
                        }
                        else if (untilHit > 0 && untilHit <= windupTicks)
                        {
                            pitch = -12f * (windupTicks - untilHit + 1) / windupTicks;
                        }

                        rotation *= Quaternion.Euler(pitch, 0, 0);
                    }

                    view.Root.transform.rotation = Quaternion.RotateTowards(view.Root.transform.rotation, rotation, elapsed * 720f);
                }

                view.Root.transform.localPosition = Vector3.Lerp(previous, next, interpolation);
                float health = state.MaxHealth == 0 ? 0 : Mathf.Clamp01((float)state.Health / state.MaxHealth);
                view.Health.transform.parent.rotation = worldCamera.transform.rotation;
                view.Health.SetPosition(1, new Vector3(-.32f + health * .64f, 0, 0));
            }

            rangeRing.enabled = false;
            liveTowers.Clear();
            foreach (var state in session.Towers)
            {
                liveTowers.Add(state.Id);
                if (!towers.TryGetValue(state.Id, out var view))
                {
                    view = CreateTower(state.Id, state.DefinitionId, state.Position);
                    towers.Add(state.Id, view);
                }

                if (state.ShotSequence != view.ShotSequence)
                {
                    view.ShotSequence = state.ShotSequence;
                    if (enemies.TryGetValue(state.LastTargetId, out var target))
                    {
                        var direction = target.Root.transform.position - view.Root.transform.position;
                        direction.y = 0;
                        if (direction.sqrMagnitude > .0001f)
                        {
                            view.Turret.rotation = Quaternion.LookRotation(direction);
                        }

                        view.Beam.SetPosition(0, view.Root.transform.TransformPoint(new Vector3(0, 1.45f, 0)));
                        view.Beam.SetPosition(1, target.Root.transform.position + Vector3.up * .40f);
                        view.BeamSeconds = .10f;
                    }
                }

                view.BeamSeconds = Mathf.Max(0, view.BeamSeconds - elapsed);
                view.Beam.enabled = view.BeamSeconds > 0;
                if (state.Id == selectedTower)
                {
                    DrawRange(state.Position, state.Range / 1000f);
                }
            }

            removals.Clear();
            foreach (var pair in enemies)
            {
                if (!liveEnemies.Contains(pair.Key))
                {
                    Destroy(pair.Value.Root);
                    removals.Add(pair.Key);
                }
            }

            foreach (int id in removals)
            {
                enemies.Remove(id);
            }

            removals.Clear();
            foreach (var pair in towers)
            {
                if (!liveTowers.Contains(pair.Key))
                {
                    pair.Value.Root.SetActive(false);
                    Destroy(pair.Value.Root);
                    removals.Add(pair.Key);
                }
            }

            foreach (int id in removals)
            {
                towers.Remove(id);
            }
        }

        private void DrawRange(Position2 position, float radius)
        {
            rangeRing.enabled = true;
            for (int index = 0; index < rangeRing.positionCount; index++)
            {
                float angle = index * Mathf.PI * 2 / rangeRing.positionCount;
                rangeRing.SetPosition(index, World(position, .10f) + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
        }

        private static Vector3 World(Position2 point, float height)
        {
            return new Vector3(point.X / 1000f, height, point.Y / 1000f);
        }

        private Material MakeMaterial(Color color, bool unlit = false)
        {
            var template = unlit ? spriteTemplate : litTemplate;
            Material material;
            if (template != null)
            {
                material = new Material(template);
            }
            else
            {
                bool urp = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null;
                string shader = unlit ? "Sprites/Default" : urp ? "Universal Render Pipeline/Lit" : "Standard";
                material = new Material(Shader.Find(shader));
            }

            material.color = color;
            if (!unlit)
            {
                material.SetFloat(material.HasProperty("_Smoothness") ? "_Smoothness" : "_Glossiness", 0);
            }

            materialAssets.Add(material);
            return material;
        }

        private static GameObject Primitive(string label, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var value = GameObject.CreatePrimitive(type);
            value.name = label;
            value.transform.SetParent(parent, false);
            value.transform.localPosition = position;
            value.transform.localScale = scale;
            value.GetComponent<Renderer>().sharedMaterial = material;
            return value;
        }

        private GameObject Prism(
            string label,
            Transform parent,
            Vector3 position,
            float radius,
            float height,
            int sides,
            Material material,
            float topRadius = -1)
        {
            if (topRadius < 0)
            {
                topRadius = radius;
            }

            string shape = FormattableString.Invariant($"{radius:R}/{height:R}/{sides}/{topRadius:R}");
            if (!meshCache.TryGetValue(shape, out var mesh))
            {
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                for (int index = 0; index < sides; index++)
                {
                    float a = index * Mathf.PI * 2 / sides;
                    float b = (index + 1) * Mathf.PI * 2 / sides;
                    var lowA = new Vector3(Mathf.Cos(a) * radius, -height / 2, Mathf.Sin(a) * radius);
                    var lowB = new Vector3(Mathf.Cos(b) * radius, -height / 2, Mathf.Sin(b) * radius);
                    var highA = new Vector3(Mathf.Cos(a) * topRadius, height / 2, Mathf.Sin(a) * topRadius);
                    var highB = new Vector3(Mathf.Cos(b) * topRadius, height / 2, Mathf.Sin(b) * topRadius);
                    int offset = vertices.Count;
                    vertices.AddRange(new[] { lowA, highA, highB, lowB, highA, new Vector3(0, height / 2, 0), highB });
                    triangles.AddRange(new[] { offset, offset + 1, offset + 2, offset, offset + 2, offset + 3, offset + 4, offset + 5, offset + 6 });
                }

                mesh = new Mesh { name = label + " faceted mesh" };
                mesh.SetVertices(vertices);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                generatedMeshes.Add(mesh);
                meshCache.Add(shape, mesh);
            }

            var root = new GameObject(label, typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = position;
            root.GetComponent<MeshFilter>().sharedMesh = mesh;
            root.GetComponent<MeshRenderer>().sharedMaterial = material;
            return root;
        }

        private void CreateShadowMesh()
        {
            vertexColorMaterial = MakeMaterial(Color.white, true);
            var vertices = new List<Vector3> { Vector3.zero };
            var colors = new List<Color> { new Color(0, 0, 0, .26f) };
            var triangles = new List<int>();
            const int sides = 24;
            for (int index = 0; index <= sides; index++)
            {
                float angle = index * Mathf.PI * 2 / sides;
                vertices.Add(new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)));
                colors.Add(new Color(0, 0, 0, 0));
                if (index < sides)
                {
                    triangles.AddRange(new[] { 0, index + 2, index + 1 });
                }
            }

            shadowMesh = new Mesh { name = "Soft contact shadow" };
            shadowMesh.SetVertices(vertices);
            shadowMesh.SetColors(colors);
            shadowMesh.SetTriangles(triangles, 0);
            shadowMesh.RecalculateBounds();
            generatedMeshes.Add(shadowMesh);
        }

        private void AddShadow(Transform parent, float radius)
        {
            var root = new GameObject("Contact shadow", typeof(MeshFilter), typeof(MeshRenderer));
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0, .018f, 0);
            root.transform.localScale = new Vector3(radius, 1, radius);
            root.GetComponent<MeshFilter>().sharedMesh = shadowMesh;
            root.GetComponent<MeshRenderer>().sharedMaterial = vertexColorMaterial;
        }
        private bool IsOverInterface(Vector2 position)
        {
            if (EventSystem.current == null)
            {
                return false;
            }

            pointerQuery ??= new PointerEventData(EventSystem.current);
            pointerQuery.position = position;
            uiHits.Clear();
            EventSystem.current.RaycastAll(pointerQuery, uiHits);
            return uiHits.Count != 0;
        }

        private bool CanPointAtField(Vector2 position)
        {
            return battlefieldPixels.Contains(position) && !IsOverInterface(position);
        }

        private bool ResolvePlacement(Vector2 pointer, out Vector3 position, out int slotId)
        {
            position = default;
            slotId = 0;
            var ray = worldCamera.ScreenPointToRay(pointer);
            var ground = new Plane(field.up, field.position);
            if (!ground.Raycast(ray, out float distance))
            {
                return false;
            }

            position = field.InverseTransformPoint(ray.GetPoint(distance));
            position.y = TowerGroundOffset;
            float halfWidth = CellHalfWidth;
            float halfDepth = CellHalfDepth;
            foreach (var cell in configuration.BuildSlots)
            {
                var center = World(cell.Position, position.y);
                if (Mathf.Abs(position.x - center.x) <= halfWidth && Mathf.Abs(position.z - center.z) <= halfDepth)
                {
                    slotId = cell.Id;
                    position = center;
                    break;
                }
            }

            return true;
        }

        private void UpdatePlacementPreview()
        {
            var touch = Touchscreen.current?.primaryTouch;
            bool previewTouch = trackingPlacementTouch && touch?.press.isPressed == true;
            var mouse = Mouse.current;
            if (!buildMode || session.IsPaused || (!previewTouch && mouse == null))
            {
                placementGhost?.SetActive(false);
                return;
            }

            var pointer = previewTouch ? touch.position.ReadValue() : mouse.position.ReadValue();
            if (!CanPointAtField(pointer) || !ResolvePlacement(pointer, out var point, out int slotId))
            {
                placementGhost?.SetActive(false);
                return;
            }

            if (placementGhost == null || ghostDefinitionId != selectedDefinition)
            {
                if (placementGhost != null)
                {
                    placementGhost.SetActive(false);
                    Destroy(placementGhost);
                }

                placementGhost = CreateTower(0, selectedDefinition, default, preview: true).Root;
                placementGhost.name = "Tower placement preview";
                ghostDefinitionId = selectedDefinition;
                foreach (var collider in placementGhost.GetComponentsInChildren<Collider>())
                {
                    collider.enabled = false;
                }

                foreach (var renderer in placementGhost.GetComponentsInChildren<MeshRenderer>())
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                    var materials = renderer.sharedMaterials;
                    for (int index = 0; index < materials.Length; index++)
                    {
                        materials[index] = PreviewMaterial(materials[index]);
                    }

                    renderer.sharedMaterials = materials;
                }

                placementFootprint = CreatePlacementFootprint(placementGhost.transform);
                previewCanBuild = null;
            }

            bool valid = slotId != 0 && session.CanBuildTower(slotId, selectedDefinition);
            placementGhost.transform.localPosition = point;
            if (previewCanBuild != valid)
            {
                foreach (var pair in previewMaterials)
                {
                    var sourceColor = pair.Key.color;
                    var tint = valid ? sourceColor : Color.Lerp(sourceColor, new Color(1, .17f, .12f, sourceColor.a), .62f);
                    tint.a *= .64f;
                    pair.Value.color = tint;
                }

                placementFootprint.startColor = placementFootprint.endColor = valid ? new Color(.96f, .91f, .78f, .82f) : new Color(1, .38f, .31f);
                previewCanBuild = valid;
            }

            placementGhost.SetActive(true);
        }

        private Material PreviewMaterial(Material source)
        {
            if (previewMaterials.TryGetValue(source, out var preview))
            {
                return preview;
            }

            preview = new Material(source);
            if (preview.HasProperty("_Surface"))
            {
                // URP Lit reads _Surface for output alpha, so the preview can reuse the bundled lighting variant.
                preview.SetFloat("_Surface", 1);
                preview.SetInt("_SrcBlendAlpha", (int)BlendMode.One);
                preview.SetInt("_DstBlendAlpha", (int)BlendMode.OneMinusSrcAlpha);
                preview.SetShaderPassEnabled("DepthOnly", false);
            }
            else if (preview.HasProperty("_Mode"))
            {
                preview.SetFloat("_Mode", 2);
                preview.EnableKeyword("_ALPHABLEND_ON");
            }

            preview.SetOverrideTag("RenderType", "Transparent");
            preview.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            preview.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            preview.SetInt("_ZWrite", 0);
            preview.renderQueue = (int)RenderQueue.Transparent;
            previewMaterials.Add(source, preview);
            materialAssets.Add(preview);
            return preview;
        }

        private LineRenderer CreatePlacementFootprint(Transform ghost)
        {
            var ring = new GameObject("Placement footprint").AddComponent<LineRenderer>();
            ring.transform.SetParent(ghost, false);
            ring.transform.localPosition = Vector3.down * (TowerGroundOffset / TowerModelScale);
            ring.transform.localScale = Vector3.one / TowerModelScale;
            ring.sharedMaterial = vertexColorMaterial;
            ring.useWorldSpace = false;
            ring.loop = true;
            ring.startWidth = ring.endWidth = .052f;
            ring.positionCount = 48;
            for (int index = 0; index < ring.positionCount; index++)
            {
                float angle = index * 2 * Mathf.PI / ring.positionCount;
                ring.SetPosition(index, new Vector3(Mathf.Cos(angle) * TowerFoundationRadius, 0, Mathf.Sin(angle) * TowerFoundationRadius));
            }

            return ring;
        }

        private void HandlePointer()
        {
            var mouse = Mouse.current;
            bool rightPressed = mouse?.rightButton.wasPressedThisFrame == true;
            if (rightPressed && (systemMenu.activeSelf || mapDetails.activeSelf || restartConfirm.activeSelf || exitConfirm.activeSelf))
            {
                HandleEscape();
                return;
            }

            if (rightPressed && buildMode)
            {
                CancelPlacement();
                return;
            }

            var touch = Touchscreen.current?.primaryTouch;
            bool touchPressed = touch?.press.wasPressedThisFrame == true;
            if (!buildMode || session.IsPaused || touch == null)
            {
                trackingPlacementTouch = false;
            }
            else if (touchPressed)
            {
                trackingPlacementTouch = CanPointAtField(touch.startPosition.ReadValue());
            }

            bool placeTouch = trackingPlacementTouch && touch.press.wasReleasedThisFrame;
            if (placeTouch)
            {
                trackingPlacementTouch = false;
                if (touch.phase.ReadValue() != UnityEngine.InputSystem.TouchPhase.Ended)
                {
                    return;
                }
            }

            Vector2 position;
            bool quick = false;
            bool mousePressed = mouse != null && (mouse.leftButton.wasPressedThisFrame || rightPressed);
            if (placeTouch || (!buildMode && touchPressed))
            {
                position = touch.position.ReadValue();
            }
            else if (mousePressed && touch?.press.isPressed != true)
            {
                quick = rightPressed && !mouse.leftButton.wasPressedThisFrame;
                position = mouse.position.ReadValue();
            }
            else
            {
                return;
            }

            if (!CanPointAtField(position))
            {
                return;
            }

            if (buildMode)
            {
                BuildAt(position);
                return;
            }

            if (Physics.Raycast(worldCamera.ScreenPointToRay(position), out var hit, 100))
            {
                if (hit.collider.TryGetComponent<PrototypeWorldTarget>(out var target) && target.TowerId != 0)
                {
                    selectedTower = target.TowerId;
                    selectedBase = false;
                    useQuickActions = quick;
                    SetNotice("");
                    RefreshInterface();
                    return;
                }

                var picked = hit.collider.transform;
                if (baseTarget != null && (picked == baseTarget || picked.IsChildOf(baseTarget)))
                {
                    SelectBase();
                    return;
                }
            }

            if (selectedTower != 0 || selectedBase)
            {
                CloseSelection();
            }
        }
    }
}
