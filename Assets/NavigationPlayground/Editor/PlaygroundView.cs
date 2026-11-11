using System;
using System.Collections.Generic;
using NavigationLab.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace NavigationLab.UnityPlayground
{
    /// <summary>Unity is a viewer here. Positions and collisions belong to the navigation worker.</summary>
    public sealed class PlaygroundView : IDisposable
    {
        private const float MillimetresToMetres = 0.001f;
        private const int CircleSegments = 80;
        private readonly Dictionary<int, ActorView> actors = new Dictionary<int, ActorView>();
        private readonly Dictionary<int, int> actorInstanceIds = new Dictionary<int, int>();
        private readonly Dictionary<int, GameObject> targets = new Dictionary<int, GameObject>();
        private readonly HashSet<GameObject> ownedObjects = new HashSet<GameObject>();
        private readonly HashSet<int> visibleIds = new HashSet<int>();
        private readonly HashSet<int> spawnCells = new HashSet<int>();
        private readonly List<int> removals = new List<int>();
        private GameObject root;
        private GameObject ground;
        private Camera overviewCamera;
        private Mesh groundMesh;
        private Texture2D groundTexture;
        private Material groundMaterial;
        private Material movingMaterial;
        private Material attackingMaterial;
        private Material waitingMaterial;
        private Material targetMaterial;
        private Material selectionMaterial;
        private LineRenderer bodyCircle;
        private LineRenderer attackCircle;
        private LineRenderer goalCircle;
        private LineRenderer goalLine;
        private WorldDefinition displayedWorld;
        private Bounds mapBounds;
        private Bounds worldBounds;
        private bool isometric;
        private bool disposed;

        public int SelectedId { get; private set; } = -1;

        public void Render(WorldDefinition world, IReadOnlyList<UnitSnapshot> units, IReadOnlyDictionary<int, SpawnRequest> live)
        {
            if (disposed || world == null || units == null || live == null)
            {
                return;
            }

            EnsureObjects();
            if (!ReferenceEquals(displayedWorld, world))
            {
                UpdateWorld(world);
            }

            worldBounds = mapBounds;
            visibleIds.Clear();
            for (int i = 0; i < units.Count; i++)
            {
                UnitSnapshot snapshot = units[i];
                if (!live.TryGetValue(snapshot.Id, out SpawnRequest spawn))
                {
                    continue;
                }

                visibleIds.Add(snapshot.Id);
                if (!actors.TryGetValue(snapshot.Id, out ActorView actor) || actor.GameObject == null)
                {
                    if (actor != null)
                    {
                        actorInstanceIds.Remove(actor.InstanceId);
                    }

                    GameObject view = CreatePrimitive(PrimitiveType.Sphere, "Unit " + snapshot.Id);
                    actor = new ActorView(view);
                    actors[snapshot.Id] = actor;
                    actorInstanceIds[actor.InstanceId] = snapshot.Id;
                }

                actor.Snapshot = snapshot;
                actor.Profile = spawn.Profile;
                float diameter = spawn.Profile.RadiusMm * 2 * MillimetresToMetres;
                float height = Mathf.Max(diameter * 1.15f, 0.15f);
                actor.GameObject.transform.localScale = new Vector3(diameter, height, diameter);
                actor.GameObject.transform.position = ToWorld(snapshot.Position) + Vector3.up * (height * 0.5f);
                actor.Renderer.sharedMaterial = StateMaterial(snapshot.State);
                worldBounds.Encapsulate(new Bounds(actor.GameObject.transform.position, new Vector3(diameter, height, diameter)));
            }

            removals.Clear();
            foreach (KeyValuePair<int, ActorView> pair in actors)
            {
                if (!visibleIds.Contains(pair.Key))
                {
                    removals.Add(pair.Key);
                }
            }

            foreach (int id in removals)
            {
                ActorView actor = actors[id];
                actorInstanceIds.Remove(actor.InstanceId);
                DestroyOwnedGameObject(actor.GameObject);
                actors.Remove(id);
            }

            if (SelectedId >= 0 && !actors.ContainsKey(SelectedId))
            {
                SelectedId = -1;
            }

            UpdateSelectionGeometry();
            FitCamera();
        }

        public void SetIsometric(bool enabled)
        {
            isometric = enabled;
            FitCamera();
        }

        public void SetSelected(int id)
        {
            SelectedId = actors.ContainsKey(id) ? id : -1;
            if (SelectedId >= 0 && actors[SelectedId].GameObject != null)
            {
                Selection.activeGameObject = actors[SelectedId].GameObject;
            }
            else if (Selection.activeGameObject != null && actorInstanceIds.ContainsKey(Selection.activeGameObject.GetInstanceID()))
            {
                Selection.activeGameObject = null;
            }

            UpdateSelectionGeometry();
            SceneView.RepaintAll();
        }

        /// <summary>Focuses the Scene view; the Game view keeps its fitted overview camera.</summary>
        public void FocusSelection()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView == null || displayedWorld == null)
            {
                return;
            }

            Bounds focus = worldBounds;
            if (actors.TryGetValue(SelectedId, out ActorView actor))
            {
                float range = (actor.Profile.RadiusMm + actor.Profile.AttackRangeMm) * MillimetresToMetres;
                focus = new Bounds(ToWorld(actor.Snapshot.Position), Vector3.one * Mathf.Max(range * 2.6f, 2));
            }

            sceneView.orthographic = true;
            sceneView.rotation = CameraRotation();
            sceneView.Frame(focus, false);
            sceneView.Repaint();
        }

        public void DrawSceneHandles(SceneView sceneView)
        {
            if (disposed || root == null || displayedWorld == null || sceneView == null)
            {
                return;
            }

            GameObject active = Selection.activeGameObject;
            if (active != null && actorInstanceIds.TryGetValue(active.GetInstanceID(), out int activeId) && activeId != SelectedId)
            {
                SelectedId = activeId;
                UpdateSelectionGeometry();
            }

            Event current = Event.current;
            if (current != null && current.type == EventType.MouseDown && current.button == 0 && !current.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(current.mousePosition);
                var plane = new Plane(Vector3.up, Vector3.zero);
                if (plane.Raycast(ray, out float distance))
                {
                    Vector3 hit = ray.GetPoint(distance);
                    int nearest = -1;
                    float nearestDistance = float.PositiveInfinity;
                    foreach (KeyValuePair<int, ActorView> pair in actors)
                    {
                        Vector3 offset = hit - ToWorld(pair.Value.Snapshot.Position);
                        float radius = pair.Value.Profile.RadiusMm * MillimetresToMetres;
                        float squaredDistance = offset.sqrMagnitude;
                        if (squaredDistance <= radius * radius && squaredDistance < nearestDistance)
                        {
                            nearest = pair.Key;
                            nearestDistance = squaredDistance;
                        }
                    }

                    if (nearest >= 0)
                    {
                        SetSelected(nearest);
                        current.Use();
                    }
                }
            }

            if (!actors.TryGetValue(SelectedId, out ActorView selected))
            {
                return;
            }

            Color previousColor = Handles.color;
            CompareFunction previousTest = Handles.zTest;
            try
            {
                Handles.zTest = CompareFunction.Always;
                Handles.color = Color.white;
                Vector3 position = ToWorld(selected.Snapshot.Position) + Vector3.up * 0.06f;
                float radius = selected.Profile.RadiusMm * MillimetresToMetres;
                float range = (selected.Profile.RadiusMm + selected.Profile.AttackRangeMm) * MillimetresToMetres;
                Handles.DrawWireDisc(position, Vector3.up, radius);
                Handles.color = new Color(1, 0.82f, 0.3f);
                Handles.DrawWireDisc(position, Vector3.up, range);
                Handles.Label(position + Vector3.up * Mathf.Max(radius * 2, 0.4f), "#" + SelectedId + " " + selected.Snapshot.State);
                if (TryGetGoal(selected, out Vector3 goal))
                {
                    Handles.color = Color.white;
                    Handles.DrawDottedLine(position, goal, 4);
                    Handles.DrawWireDisc(goal, Vector3.up, Mathf.Max(displayedWorld.CellSizeMm * 0.12f * MillimetresToMetres, 0.04f));
                    Handles.Label(Vector3.Lerp(position, goal, 0.5f), "Goal 连线，不是路径");
                }
            }
            finally
            {
                Handles.color = previousColor;
                Handles.zTest = previousTest;
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            if (Selection.activeGameObject != null && actorInstanceIds.ContainsKey(Selection.activeGameObject.GetInstanceID()))
            {
                Selection.activeGameObject = null;
            }

            // Only our objects are disposable. Preserve any user objects reparented below our root.
            DestroyOwnedGameObject(root);
            foreach (GameObject item in new List<GameObject>(ownedObjects))
            {
                DestroyOwnedGameObject(item);
            }

            DestroyAsset(groundMesh);
            DestroyAsset(groundTexture);
            DestroyAsset(groundMaterial);
            DestroyAsset(movingMaterial);
            DestroyAsset(attackingMaterial);
            DestroyAsset(waitingMaterial);
            DestroyAsset(targetMaterial);
            DestroyAsset(selectionMaterial);
            actors.Clear();
            actorInstanceIds.Clear();
            targets.Clear();
            ownedObjects.Clear();
            displayedWorld = null;
            SelectedId = -1;
        }

        private void EnsureObjects()
        {
            if (root != null)
            {
                return;
            }

            // A user can delete the preview root manually. Recreate it without leaving owned survivors or a stale mesh.
            foreach (GameObject item in new List<GameObject>(ownedObjects))
            {
                DestroyOwnedGameObject(item);
            }

            ownedObjects.Clear();
            DestroyAsset(groundMesh);
            if (groundMaterial == null)
            {
                groundMaterial = CreateMaterial("Navigation floor", Color.white, true);
                movingMaterial = CreateMaterial("Navigation moving", new Color(0.2f, 0.83f, 0.9f));
                attackingMaterial = CreateMaterial("Navigation attacking", new Color(0.95f, 0.28f, 0.25f));
                waitingMaterial = CreateMaterial("Navigation waiting", new Color(0.98f, 0.73f, 0.22f));
                targetMaterial = CreateMaterial("Navigation target", new Color(0.2f, 0.4f, 0.9f));
                selectionMaterial = CreateMaterial("Navigation selection", Color.white);
            }

            actors.Clear();
            actorInstanceIds.Clear();
            targets.Clear();
            displayedWorld = null;
            root = Own(new GameObject("Navigation Playground Views"));
            ground = Own(new GameObject("Grid: grey blocked, stone path, green holding, ochre spawn"));
            ground.transform.SetParent(root.transform, false);
            groundMesh = new Mesh { name = "Navigation grid mesh", hideFlags = HideFlags.DontSave };
            ground.AddComponent<MeshFilter>().sharedMesh = groundMesh;
            ground.AddComponent<MeshRenderer>().sharedMaterial = groundMaterial;
            GameObject cameraObject = Own(new GameObject("Navigation Playground Camera"));
            cameraObject.transform.SetParent(root.transform, false);
            overviewCamera = cameraObject.AddComponent<Camera>();
            overviewCamera.orthographic = true;
            overviewCamera.clearFlags = CameraClearFlags.SolidColor;
            overviewCamera.backgroundColor = new Color(0.055f, 0.075f, 0.09f);
            overviewCamera.allowHDR = false;
            overviewCamera.allowMSAA = false;
            bodyCircle = CreateLine("Selected body radius", true, 0.035f);
            attackCircle = CreateLine("Selected attack reach: body radius + attack range", true, 0.022f);
            goalCircle = CreateLine("Goal cell centre", true, 0.025f);
            goalLine = CreateLine("Goal connection, NOT a path", false, 0.014f);
            goalLine.positionCount = 2;
        }

        private void UpdateWorld(WorldDefinition world)
        {
            displayedWorld = world;
            float width = world.Width * world.CellSizeMm * MillimetresToMetres;
            float height = world.Height * world.CellSizeMm * MillimetresToMetres;
            ground.transform.position = ToWorld(world.Origin) - Vector3.up * 0.02f;
            groundMesh.Clear();
            groundMesh.vertices = new[] { Vector3.zero, new Vector3(width, 0, 0), new Vector3(width, 0, height), new Vector3(0, 0, height) };
            groundMesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            groundMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.one, Vector2.up };
            groundMesh.RecalculateNormals();
            groundMesh.RecalculateBounds();
            mapBounds = new Bounds(ToWorld(world.Origin) + new Vector3(width * 0.5f, 0.75f, height * 0.5f), new Vector3(width, 1.5f, height));

            if (groundTexture == null || groundTexture.width != world.Width || groundTexture.height != world.Height)
            {
                DestroyAsset(groundTexture);
                groundTexture = new Texture2D(world.Width, world.Height, TextureFormat.RGB24, false);
                groundTexture.name = "Navigation grid cells";
                groundTexture.hideFlags = HideFlags.DontSave;
                groundTexture.filterMode = FilterMode.Point;
                groundTexture.wrapMode = TextureWrapMode.Clamp;
            }

            spawnCells.Clear();
            foreach (int cell in world.SpawnCells)
            {
                spawnCells.Add(cell);
            }

            var pixels = new Color32[world.CellCount];
            for (int cell = 0; cell < pixels.Length; cell++)
            {
                Color color;
                if (!world.IsWalkable(cell))
                {
                    color = new Color(0.28f, 0.31f, 0.33f);
                }
                else if (spawnCells.Contains(cell))
                {
                    color = new Color(0.64f, 0.48f, 0.25f);
                }
                else if (world.HoldingAllowed(cell))
                {
                    color = new Color(0.38f, 0.49f, 0.37f);
                }
                else
                {
                    color = new Color(0.61f, 0.63f, 0.58f);
                }

                pixels[cell] = color;
            }

            groundTexture.SetPixels32(pixels);
            groundTexture.Apply(false, false);
            if (groundMaterial != null)
            {
                SetTexture(groundMaterial, "_BaseMap", groundTexture);
                SetTexture(groundMaterial, "_MainTex", groundTexture);
            }

            visibleIds.Clear();
            foreach (TargetDefinition target in world.Targets)
            {
                visibleIds.Add(target.Id);
                if (!targets.TryGetValue(target.Id, out GameObject view) || view == null)
                {
                    view = CreatePrimitive(PrimitiveType.Cylinder, "Target " + target.Id);
                    view.GetComponent<Renderer>().sharedMaterial = targetMaterial;
                    targets[target.Id] = view;
                }

                float diameter = target.RadiusMm * 2 * MillimetresToMetres;
                float baseHeight = Mathf.Max(diameter * 0.4f, 0.4f);
                view.transform.localScale = new Vector3(diameter, baseHeight * 0.5f, diameter);
                view.transform.position = ToWorld(target.Center) + Vector3.up * (baseHeight * 0.5f);
                mapBounds.Encapsulate(new Bounds(view.transform.position, new Vector3(diameter, baseHeight, diameter)));
            }

            removals.Clear();
            foreach (KeyValuePair<int, GameObject> target in targets)
            {
                if (!visibleIds.Contains(target.Key))
                {
                    removals.Add(target.Key);
                }
            }

            foreach (int id in removals)
            {
                DestroyOwnedGameObject(targets[id]);
                targets.Remove(id);
            }
        }

        private void FitCamera()
        {
            if (overviewCamera == null || displayedWorld == null)
            {
                return;
            }

            Quaternion rotation = CameraRotation();
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 extents = worldBounds.extents;
            float horizontal = 0;
            float vertical = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                var offset = new Vector3(
                    (corner & 1) == 0 ? -extents.x : extents.x,
                    (corner & 2) == 0 ? -extents.y : extents.y,
                    (corner & 4) == 0 ? -extents.z : extents.z);
                horizontal = Mathf.Max(horizontal, Mathf.Abs(Vector3.Dot(offset, right)));
                vertical = Mathf.Max(vertical, Mathf.Abs(Vector3.Dot(offset, up)));
            }

            float aspect = Mathf.Max(overviewCamera.aspect, 0.1f);
            overviewCamera.orthographicSize = Mathf.Max(vertical, horizontal / aspect) * 1.08f;
            float distance = worldBounds.size.magnitude * 1.5f + 10;
            overviewCamera.transform.SetPositionAndRotation(worldBounds.center - rotation * Vector3.forward * distance, rotation);
            overviewCamera.nearClipPlane = 0.05f;
            overviewCamera.farClipPlane = distance + worldBounds.size.magnitude * 2 + 20;
        }

        private Quaternion CameraRotation()
        {
            return isometric ? Quaternion.Euler(55, 45, 0) : Quaternion.Euler(90, 0, 0);
        }

        private void UpdateSelectionGeometry()
        {
            if (bodyCircle == null)
            {
                return;
            }

            bool exists = actors.TryGetValue(SelectedId, out ActorView actor);
            bodyCircle.enabled = exists;
            attackCircle.enabled = exists;
            goalCircle.enabled = false;
            goalLine.enabled = false;
            if (!exists)
            {
                return;
            }

            Vector3 center = ToWorld(actor.Snapshot.Position) + Vector3.up * 0.04f;
            SetCircle(bodyCircle, center, actor.Profile.RadiusMm * MillimetresToMetres);
            SetCircle(attackCircle, center, (actor.Profile.RadiusMm + actor.Profile.AttackRangeMm) * MillimetresToMetres);
            if (TryGetGoal(actor, out Vector3 goal))
            {
                goalCircle.enabled = true;
                goalLine.enabled = true;
                SetCircle(goalCircle, goal, Mathf.Max(displayedWorld.CellSizeMm * 0.12f * MillimetresToMetres, 0.04f));
                goalLine.SetPosition(0, center);
                goalLine.SetPosition(1, goal);
            }
        }

        private bool TryGetGoal(ActorView actor, out Vector3 goal)
        {
            int cell = actor.Snapshot.GoalCell;
            if (displayedWorld != null && cell >= 0 && cell < displayedWorld.CellCount && displayedWorld.IsWalkable(cell))
            {
                goal = ToWorld(displayedWorld.Center(cell)) + Vector3.up * 0.045f;
                return true;
            }

            goal = default;
            return false;
        }

        private LineRenderer CreateLine(string name, bool loop, float width)
        {
            GameObject lineObject = Own(new GameObject(name));
            lineObject.transform.SetParent(root.transform, false);
            var line = lineObject.AddComponent<LineRenderer>();
            line.sharedMaterial = selectionMaterial;
            line.loop = loop;
            line.useWorldSpace = true;
            line.widthMultiplier = width;
            line.positionCount = CircleSegments;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.enabled = false;
            return line;
        }

        private static void SetCircle(LineRenderer line, Vector3 center, float radius)
        {
            for (int i = 0; i < CircleSegments; i++)
            {
                float angle = i * Mathf.PI * 2 / CircleSegments;
                line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius));
            }
        }

        private Material StateMaterial(MotionState state)
        {
            switch (state)
            {
                case MotionState.Moving:
                    return movingMaterial;
                case MotionState.Attacking:
                    return attackingMaterial;
                default:
                    return waitingMaterial;
            }
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name)
        {
            GameObject view = Own(GameObject.CreatePrimitive(type));
            view.name = name;
            view.transform.SetParent(root.transform, false);
            Collider collider = view.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            var renderer = view.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return view;
        }

        private GameObject Own(GameObject item)
        {
            item.hideFlags = HideFlags.DontSave;
            ownedObjects.Add(item);
            return item;
        }

        private void DestroyOwnedGameObject(GameObject item)
        {
            if (item == null || !ownedObjects.Contains(item))
            {
                return;
            }

            PreserveUserChildren(item.transform);
            Object.DestroyImmediate(item);
            ownedObjects.Remove(item);
        }

        private void PreserveUserChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                Transform child = parent.GetChild(i);
                if (ownedObjects.Contains(child.gameObject))
                {
                    PreserveUserChildren(child);
                }
                else
                {
                    child.SetParent(null, true);
                }
            }
        }

        private static Material CreateMaterial(string name, Color color, bool textured = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null && textured)
            {
                shader = Shader.Find("Unlit/Texture");
            }

            if (shader == null)
            {
                shader = Shader.Find("Unlit/Color");
            }

            if (shader == null)
            {
                shader = Shader.Find("Hidden/Internal-Colored");
            }

            if (shader == null)
            {
                // Null-graphics batch validation can have no renderable shader at all.
                return null;
            }

            var material = new Material(shader) { name = name, hideFlags = HideFlags.DontSave };
            if (material.HasProperty("_BaseColor"))
            {
                material.SetColor("_BaseColor", color);
            }

            if (material.HasProperty("_Color"))
            {
                material.SetColor("_Color", color);
            }

            return material;
        }

        private static void SetTexture(Material material, string property, Texture texture)
        {
            if (material.HasProperty(property))
            {
                material.SetTexture(property, texture);
            }
        }

        private static void DestroyAsset(Object asset)
        {
            if (asset != null)
            {
                Object.DestroyImmediate(asset);
            }
        }

        private static Vector3 ToWorld(Point2 point)
        {
            return new Vector3((float)point.X * MillimetresToMetres, 0, (float)point.Y * MillimetresToMetres);
        }

        private sealed class ActorView
        {
            public readonly GameObject GameObject;
            public readonly Renderer Renderer;
            public readonly int InstanceId;
            public UnitSnapshot Snapshot;
            public UnitProfile Profile;

            public ActorView(GameObject gameObject)
            {
                GameObject = gameObject;
                Renderer = gameObject.GetComponent<Renderer>();
                InstanceId = gameObject.GetInstanceID();
            }
        }
    }
}
