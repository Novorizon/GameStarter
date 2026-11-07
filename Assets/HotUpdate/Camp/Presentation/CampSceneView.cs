using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.HotUpdate.Camp
{
    /// <summary>固定海岛场景表现；建设状态来自营地域，网格和材质只由场景拥有。</summary>
    public sealed class CampSceneView : MonoBehaviour, IDisposable
    {
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private Camera sceneCamera;
        private Transform settlement;
        private SpriteRenderer island;
        private GameObject buildings;
        private Material timber;
        private Material sand;
        private Material canvas;
        private Material dark;
        private Material teal;
        private Material stone;
        private Material flame;
        private int level;
        private int screenWidth;
        private int screenHeight;
        private bool disposed;
        private bool initialized;
        private bool ownsAmbient;
        private Color previousAmbient;
        private UnityEngine.Rendering.AmbientMode previousAmbientMode;
        public Action Selected;
        public int DisplayedLevel => level;

        public static CampSceneView Create(Transform parent, Sprite map)
        {
            var root = new GameObject("IslandCampScene");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<CampSceneView>();
            view.Initialize(map);
            return view;
        }

        private void Initialize(Sprite map)
        {
            var cameraRoot = new GameObject("CampCamera");
            cameraRoot.transform.SetParent(transform, false);
            cameraRoot.tag = "MainCamera";
            sceneCamera = cameraRoot.AddComponent<Camera>();
            sceneCamera.orthographic = true;
            sceneCamera.transform.rotation = Quaternion.Euler(55, 0, 0);
            sceneCamera.transform.position = -sceneCamera.transform.forward * 32;
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(.04f, .54f, .67f);
            sceneCamera.depth = -10;
            var sun = new GameObject("CampSun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55, -35, 0);
            island = new GameObject("IslandArtwork").AddComponent<SpriteRenderer>();
            island.transform.SetParent(transform, false);
            island.transform.position = sceneCamera.transform.forward * 18;
            island.transform.rotation = sceneCamera.transform.rotation;
            island.sprite = map;
            island.transform.localScale = new Vector3(25 / map.bounds.size.x, 14.07f / map.bounds.size.y, 1);
            settlement = new GameObject("CampSettlement").transform;
            settlement.SetParent(transform, false);
            settlement.localPosition = new Vector3(-1.25f, 0, .1f);
            settlement.localRotation = Quaternion.Euler(0, -15, 0);
            var target = settlement.gameObject.AddComponent<BoxCollider>();
            target.center = new Vector3(0, .6f, 0);
            target.size = new Vector3(6.6f, 1.5f, 4.6f);
            timber = Material(new Color(.43f, .25f, .11f));
            sand = Material(new Color(.86f, .72f, .45f));
            canvas = Material(new Color(.91f, .78f, .50f));
            dark = Material(new Color(.16f, .20f, .16f));
            teal = Material(new Color(.11f, .42f, .49f));
            stone = Material(new Color(.65f, .66f, .57f));
            flame = Material(new Color(1, .53f, .10f));
            initialized = true;
            ApplyAmbient();
            UpdateSize();
        }

        public void Refresh(CampProgress camp)
        {
            if (level == camp.Level.Id)
            {
                return;
            }

            level = camp.Level.Id;
            if (buildings != null)
            {
                buildings.SetActive(false);
                Destroy(buildings);
            }

            buildings = new GameObject("CampStage " + level);
            buildings.transform.SetParent(settlement, false);
            var root = buildings.transform;
            if (level >= 3)
            {
                for (int x = -3; x <= 3; x++)
                {
                    for (int z = -2; z <= 2; z++)
                    {
                        Box("CourtyardPaving", root, new Vector3(x * .82f, .025f, z * .78f), new Vector3(.78f, .05f, .74f), stone);
                    }
                }
            }

            Shadow(root);
            Shelter(root, level >= 4);
            if (level >= 2)
            {
                Fence(root);
            }

            if (level >= 3)
            {
                Well(root);
                Box("GardenSoil", root, new Vector3(2, .08f, 1.1f), new Vector3(1.15f, .13f, 1.3f), timber);
                for (int i = 0; i < 4; i++)
                {
                    Box("GardenLeaves", root, new Vector3(1.7f + i % 2 * .5f, .28f, .75f + i / 2 * .6f), new Vector3(.32f, .3f, .35f), teal);
                }
            }

            Box("SupplyChest", root, new Vector3(-2, .28f, -.3f), new Vector3(.75f, .5f, .6f), timber);
            Box("ChestBand", root, new Vector3(-2, .29f, -.32f), new Vector3(.12f, .55f, .65f), sand);
            for (int index = 0; index < 6; index++)
            {
                float angle = index * Mathf.PI / 3;
                Box("FireStone", root, new Vector3(.8f + Mathf.Cos(angle) * .4f, .10f, -.8f + Mathf.Sin(angle) * .4f), new Vector3(.27f, .20f, .25f), stone);
            }

            var fire = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fire.name = "Campfire";
            fire.transform.SetParent(root, false);
            fire.transform.localPosition = new Vector3(.8f, .23f, -.8f);
            fire.transform.localScale = new Vector3(.35f, .5f, .35f);
            fire.GetComponent<Renderer>().sharedMaterial = flame;
            Destroy(fire.GetComponent<Collider>());
        }

        private void Shelter(Transform root, bool house)
        {
            var body = new GameObject(house ? "IslandCabin" : "ArrivalTent").transform;
            body.SetParent(root, false);
            body.localPosition = new Vector3(-.45f, .06f, .55f);
            if (house)
            {
                Box("CabinBody", body, new Vector3(0, .65f, 0), new Vector3(2.05f, 1.25f, 1.8f), timber);
                Box("Door", body, new Vector3(0, .45f, -.92f), new Vector3(.6f, .9f, .055f), dark);
                Box("WarmWindow", body, new Vector3(.68f, .77f, -.94f), new Vector3(.36f, .38f, .04f), sand);
            }

            float bottom = house ? 1.27f : 0;
            var vertices = new[]
            {
                new Vector3(-1.2f, bottom, -1), new Vector3(0, bottom + 1.3f, -1), new Vector3(0, bottom + 1.3f, 1), new Vector3(-1.2f, bottom, 1),
                new Vector3(0, bottom + 1.3f, -1), new Vector3(1.2f, bottom, -1), new Vector3(1.2f, bottom, 1), new Vector3(0, bottom + 1.3f, 1),
                new Vector3(-1.2f, bottom, 1), new Vector3(0, bottom + 1.3f, 1), new Vector3(1.2f, bottom, 1)
            };
            var mesh = new Mesh { name = house ? "CabinRoofMesh" : "CanvasTentMesh", vertices = vertices };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6, 8, 10, 9 };
            mesh.RecalculateNormals();
            owned.Add(mesh);
            var roof = new GameObject("ShelterRoof", typeof(MeshFilter), typeof(MeshRenderer));
            roof.transform.SetParent(body, false);
            roof.GetComponent<MeshFilter>().sharedMesh = mesh;
            roof.GetComponent<MeshRenderer>().sharedMaterial = house ? teal : canvas;
            if (!house)
            {
                Box("TentInterior", body, new Vector3(0, .40f, -.82f), new Vector3(.88f, .8f, .03f), dark);
                Box("TentPole", body, new Vector3(0, .62f, -1.03f), new Vector3(.055f, 1.3f, .055f), timber);
                Box("SleepingMat", body, new Vector3(0, .04f, -.40f), new Vector3(1.35f, .06f, 1.5f), sand);
            }
        }

        private void Fence(Transform root)
        {
            for (int index = 0; index < 9; index++)
            {
                float x = -3.1f + index * .775f;
                Box("FencePost", root, new Vector3(x, .40f, 2.2f), new Vector3(.17f, .8f, .17f), timber);
                if (Math.Abs(x) > .7f)
                {
                    Box("FencePost", root, new Vector3(x, .40f, -2.2f), new Vector3(.17f, .8f, .17f), timber);
                }
            }

            foreach (float x in new[] { -3.1f, 3.1f })
            {
                for (int index = -2; index <= 2; index++)
                {
                    Box("FencePost", root, new Vector3(x, .4f, index * .88f), new Vector3(.17f, .8f, .17f), timber);
                }

                Box("FenceRail", root, new Vector3(x, .5f, 0), new Vector3(.10f, .12f, 4.5f), sand);
            }

            Box("FenceRail", root, new Vector3(0, .5f, 2.2f), new Vector3(6.2f, .12f, .10f), sand);
            foreach (float x in new[] { -1.95f, 1.95f })
            {
                Box("FenceRail", root, new Vector3(x, .5f, -2.2f), new Vector3(2.3f, .12f, .10f), sand);
            }
        }

        private void Well(Transform root)
        {
            var well = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            well.name = "CourtyardWell";
            well.transform.SetParent(root, false);
            well.transform.localPosition = new Vector3(2, .36f, -1);
            well.transform.localScale = new Vector3(.90f, .35f, .90f);
            well.GetComponent<Renderer>().sharedMaterial = stone;
            Destroy(well.GetComponent<Collider>());
            Box("WellBeam", root, new Vector3(2, 1.05f, -1), new Vector3(1.15f, .12f, .15f), timber);
            foreach (float x in new[] { 1.55f, 2.45f })
            {
                Box("WellPost", root, new Vector3(x, .7f, -1), new Vector3(.1f, .7f, .1f), timber);
            }
        }

        private Material Material(Color color)
        {
            string shader = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null ? "Universal Render Pipeline/Lit" : "Standard";
            var material = new Material(Shader.Find(shader)) { color = color };
            material.SetFloat(material.HasProperty("_Smoothness") ? "_Smoothness" : "_Glossiness", .05f);
            owned.Add(material);
            return material;
        }

        private static void Box(string label, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = label;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            Destroy(part.GetComponent<Collider>());
        }

        private void Shadow(Transform root)
        {
            var material = new Material(Shader.Find("Sprites/Default")) { color = Color.white };
            owned.Add(material);
            var mesh = new Mesh { name = "CampContactShadow" };
            var vertices = new Vector3[33];
            var colors = new Color[33];
            colors[0] = new Color(.03f, .10f, .04f, .23f);
            vertices[0] = new Vector3(0, .015f, 0);
            var triangles = new int[32 * 3];
            for (int i = 0; i < 32; i++)
            {
                float angle = i * Mathf.PI * 2 / 32;
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 3.2f, .015f, Mathf.Sin(angle) * 2.2f);
                colors[i + 1] = new Color(.03f, .10f, .04f, 0);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = 1 + (i + 1) % 32;
                triangles[i * 3 + 2] = i + 1;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            owned.Add(mesh);
            var shadow = new GameObject("CampContactShadow", typeof(MeshFilter), typeof(MeshRenderer));
            shadow.transform.SetParent(root, false);
            shadow.GetComponent<MeshFilter>().sharedMesh = mesh;
            shadow.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private void Update()
        {
            UpdateSize();
            bool pressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            Vector2 position = Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
            {
                pressed = true;
                position = Touchscreen.current.primaryTouch.position.ReadValue();
            }

            if (!pressed)
            {
                return;
            }

            if (EventSystem.current != null)
            {
                uiHits.Clear();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = position }, uiHits);
                if (uiHits.Count > 0)
                {
                    return;
                }
            }

            if (Physics.Raycast(sceneCamera.ScreenPointToRay(position), out var hit) && hit.transform == settlement)
            {
                Selected?.Invoke();
            }
        }

        private void UpdateSize()
        {
            if (screenWidth == Screen.width && screenHeight == Screen.height)
            {
                return;
            }

            screenWidth = Screen.width;
            screenHeight = Screen.height;
            sceneCamera.orthographicSize = Mathf.Max(6.6f, 11.75f / Mathf.Max(.1f, (float)screenWidth / Mathf.Max(1, screenHeight)));
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            gameObject.SetActive(false);
            foreach (var value in owned)
            {
                Destroy(value);
            }

            owned.Clear();
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            Dispose();
        }

        private void ApplyAmbient()
        {
            if (!initialized || ownsAmbient)
            {
                return;
            }

            previousAmbient = RenderSettings.ambientLight;
            previousAmbientMode = RenderSettings.ambientMode;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(.70f, .73f, .62f);
            ownsAmbient = true;
        }

        private void OnEnable()
        {
            ApplyAmbient();
        }

        private void OnDisable()
        {
            if (ownsAmbient)
            {
                RenderSettings.ambientLight = previousAmbient;
                RenderSettings.ambientMode = previousAmbientMode;
                ownsAmbient = false;
            }
        }
    }
}
