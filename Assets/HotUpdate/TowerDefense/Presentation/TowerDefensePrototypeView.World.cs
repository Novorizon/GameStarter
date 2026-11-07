using System;
using System.Collections.Generic;
using Game.HotUpdate.TowerDefense.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.HotUpdate.TowerDefense.Presentation
{
    public sealed partial class TowerDefensePrototypeView
    {
        private readonly Dictionary<string, Mesh> meshCache = new Dictionary<string, Mesh>();
        private const float CameraPitch = 55;
        private const float MapWidth = 22;
        private const float MapDepth = 15.12f;
        private Material stone;
        private Material stoneLight;
        private Material wood;
        private Material brass;
        private Material iron;
        private Material blue;
        private Material eye;
        private Material shadowMaterial;
        private Mesh shadowMesh;

        private void CreateWorld(Sprite map)
        {
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
            worldCamera.transform.position = new Vector3(0, 0, -1.60f) - worldCamera.transform.forward * 35;
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
            // The map already contains the camera projection. Keep it facing the camera, behind every 3D unit.
            mapRenderer.transform.position = worldCamera.transform.forward * 18;
            mapRenderer.transform.rotation = worldCamera.transform.rotation;
            ApplyMap(map);
            CreateCastle(configuration.Routes[0].Points[configuration.Routes[0].Points.Length - 1]);
            foreach (var slot in configuration.BuildSlots)
            {
                var pad = new GameObject("Build pad " + slot.Id);
                pad.transform.SetParent(field, false);
                pad.transform.localPosition = World(slot.Position, .06f);
                var collider = pad.AddComponent<BoxCollider>();
                collider.size = new Vector3(1.30f, .20f, 1.45f);
                pad.AddComponent<PrototypeWorldTarget>().SlotId = slot.Id;
                var outline = pad.AddComponent<LineRenderer>();
                outline.sharedMaterial = lineMaterial;
                outline.useWorldSpace = false;
                outline.loop = true;
                outline.startWidth = .035f;
                outline.endWidth = .035f;
                outline.positionCount = 4;
                outline.SetPositions(new[]
                {
                    new Vector3(-.57f, .02f, -.65f), new Vector3(-.57f, .02f, .65f),
                    new Vector3(.57f, .02f, .65f), new Vector3(.57f, .02f, -.65f)
                });
                outline.enabled = false;
                slots.Add(slot.Id, pad);
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
            rangeRing.enabled = false;
        }

        private void ApplyMap(Sprite map)
        {
            mapRenderer.sprite = map;
            mapRenderer.enabled = map != null;
            if (map != null)
            {
                float projectedDepth = MapDepth * Mathf.Sin(CameraPitch * Mathf.Deg2Rad);
                mapRenderer.transform.localScale = new Vector3(MapWidth / map.bounds.size.x, projectedDepth / map.bounds.size.y, 1);
            }
        }

        private void CreateCastle(Position2 position)
        {
            var root = new GameObject("Base - Forest Keep").transform;
            root.SetParent(field, false);
            root.localPosition = World(position, 0);
            AddShadow(root, 1.15f);
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

        private TowerView CreateTower(int id, int definition, Position2 position)
        {
            var root = new GameObject("Tower " + id + " - Stone Watchtower");
            root.transform.SetParent(field, false);
            root.transform.localPosition = World(position, .02f);
            AddShadow(root.transform, .72f);
            Prism("Foundation", root.transform, new Vector3(0, .10f, 0), .52f, .18f, 8, stoneLight);
            var body = Prism("Masonry", root.transform, new Vector3(0, .61f, 0), .39f, .90f, 8, stone, .34f);
            var collider = body.AddComponent<BoxCollider>();
            collider.size = new Vector3(.84f, 1.1f, .84f);
            body.AddComponent<PrototypeWorldTarget>().TowerId = id;
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
            float size = definition == 3 ? 1.25f : .90f;
            root.transform.localScale = Vector3.one * size;
            AddShadow(root.transform, .38f);
            Prism("Body", root.transform, new Vector3(0, .29f, 0), .20f, .35f, 6, wood, .24f);
            Prism("Head", root.transform, new Vector3(0, .65f, 0), .26f, .34f, 7, material, .16f);
            for (int side = -1; side <= 1; side += 2)
            {
                var ear = Prism("Ear", root.transform, new Vector3(side * .26f, .67f, 0), .10f, .30f, 4, material, 0);
                ear.transform.localRotation = Quaternion.Euler(0, 0, side * -55);
                var pupil = Primitive("Eye", PrimitiveType.Cube, root.transform, new Vector3(side * .095f, .67f, .227f), new Vector3(.065f, .065f, .04f), eye);
                Destroy(pupil.GetComponent<Collider>());
                var boot = Primitive("Boot", PrimitiveType.Cube, root.transform, new Vector3(side * .12f, .07f, .04f), new Vector3(.14f, .14f, .22f), iron);
                Destroy(boot.GetComponent<Collider>());
            }

            var bar = new GameObject("Health").AddComponent<LineRenderer>();
            bar.transform.SetParent(root.transform, false);
            bar.useWorldSpace = false;
            bar.sharedMaterial = lineMaterial;
            bar.startWidth = .045f;
            bar.endWidth = .045f;
            bar.positionCount = 2;
            return new EnemyView { Root = root, Health = bar };
        }

        private void SynchronizeWorld(float elapsed)
        {
            liveEnemies.Clear();
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
                var movement = next - view.Root.transform.localPosition;
                if (movement.sqrMagnitude > .0001f)
                {
                    view.Root.transform.rotation = Quaternion.LookRotation(movement);
                }

                view.Root.transform.localPosition = next;
                float health = state.MaxHealth == 0 ? 0 : Mathf.Clamp01((float)state.Health / state.MaxHealth);
                view.Health.SetPosition(0, new Vector3(-.32f, 1.05f, 0));
                view.Health.SetPosition(1, new Vector3(-.32f + health * .64f, 1.05f, 0));
            }

            rangeRing.enabled = false;
            foreach (var pair in slots)
            {
                bool occupied = false;
                foreach (var tower in session.Towers)
                {
                    occupied |= tower.SlotId == pair.Key;
                }

                pair.Value.GetComponent<LineRenderer>().enabled = buildMode && !occupied;
            }

            foreach (var state in session.Towers)
            {
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

                        view.Beam.SetPosition(0, view.Root.transform.position + Vector3.up * 1.45f);
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
            var material = new Material(Shader.Find(unlit ? "Sprites/Default" : "Standard"));
            material.color = color;
            if (!unlit)
            {
                material.SetFloat("_Glossiness", 0);
            }

            materials.Add(material);
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
            shadowMaterial = MakeMaterial(Color.white, true);
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
            root.GetComponent<MeshRenderer>().sharedMaterial = shadowMaterial;
        }
        private void HandlePointer()
        {
            Vector2 position;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                position = Touchscreen.current.primaryTouch.position.ReadValue();
            }
            else if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                position = Mouse.current.position.ReadValue();
            }
            else
            {
                return;
            }

            if (EventSystem.current != null)
            {
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
                if (uiHits.Count != 0)
                {
                    return;
                }
            }

            if (Physics.Raycast(worldCamera.ScreenPointToRay(position), out var hit, 100))
            {
                var target = hit.collider.GetComponent<PrototypeWorldTarget>();
                if (target == null)
                {
                    return;
                }

                if (target.TowerId != 0)
                {
                    selectedTower = target.TowerId;
                    buildMode = false;
                    SetNotice("已选择防御塔，圆圈显示实际攻击范围。可在下方升级。");
                }
                else
                {
                    bool occupied = false;
                    foreach (var tower in session.Towers)
                    {
                        if (tower.SlotId == target.SlotId)
                        {
                            selectedTower = tower.Id;
                            buildMode = false;
                            occupied = true;
                            break;
                        }
                    }

                    if (!occupied)
                    {
                        if (buildMode)
                        {
                            BuildAt(target.SlotId);
                        }
                        else
                        {
                            SetNotice("先选择下方塔卡，再点击出现的绿色建造区域。");
                        }
                    }
                }

                RefreshInterface();
            }
        }
    }
}
