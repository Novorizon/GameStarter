using System;
using System.Collections.Generic;
using Game.HotUpdate.Client;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace Game.HotUpdate.Camp
{
    internal enum CampCameraMove
    {
        Near,
        Far,
        Reset
    }

    /// <summary>海岛营地表现，拥有平面底图、建筑、材质和营地镜头。</summary>
    public sealed partial class CampSceneView : MonoBehaviour, IDisposable
    {
        private const float CameraTransitionSeconds = .32f;
        private const float OverviewHalfHeight = 6.6f;
        private const float OverviewPitch = 55;
        private const float CameraDistance = 32;
        private static readonly Vector2 ClearingFocus = new Vector2(.46f, .52f);
        private static readonly Vector2 CourtyardAnchor = new Vector2(.293f, .567f);
        private static readonly Vector2 ShelterAnchor = new Vector2(.278f, .509f);
        private static readonly Vector2 CampfireAnchor = new Vector2(.285f, .615f);
        private static readonly Vector2 SupplyAnchor = new Vector2(.161f, .664f);
        private static readonly Vector2 WorkbenchAnchor = new Vector2(.396f, .515f);
        private static readonly Vector2 FarmAnchor = new Vector2(.646f, .582f);
        private static readonly Vector2 WaterAnchor = new Vector2(.613f, .675f);
        private float cameraZoom = 1;
        private float targetZoom = 1;
        private float startZoom;
        private float cameraElapsed = CameraTransitionSeconds;
        private float overviewSize;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private readonly List<RaycastResult> uiHits = new List<RaycastResult>();
        private Camera sceneCamera;
        private Transform settlement;
        private Transform waterPoint;
        private GameObject buildings;
        private Material worldSprite;
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
        public Action WaterSelected;
        public int DisplayedLevel => level;

        public static CampSceneView Create(Transform parent, ClientArt art)
        {
            var root = new GameObject("IslandCampScene");
            root.transform.SetParent(parent, false);
            var view = root.AddComponent<CampSceneView>();
            view.Initialize(art);
            return view;
        }

        private void Initialize(ClientArt art)
        {
            var cameraRoot = new GameObject("CampCamera");
            cameraRoot.transform.SetParent(transform, false);
            cameraRoot.tag = "MainCamera";
            sceneCamera = cameraRoot.AddComponent<Camera>();
            sceneCamera.orthographic = true;
            sceneCamera.transform.localRotation = Quaternion.Euler(OverviewPitch, 0, 0);
            sceneCamera.transform.localPosition = sceneCamera.transform.localRotation * (Vector3.back * CameraDistance);
            sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(.21f, .19f, .18f);
            sceneCamera.depth = -10;
            var sun = new GameObject("CampSun").AddComponent<Light>();
            sun.transform.SetParent(transform, false);
            sun.type = LightType.Directional;
            sun.intensity = 1.1f;
            sun.transform.rotation = Quaternion.Euler(55, -35, 0);
            worldSprite = art.WorldSprite;
            CreateBackdrop(art.CampMap);
            settlement = new GameObject("CampSettlement").transform;
            settlement.SetParent(transform, false);
            settlement.localPosition = ArtworkGroundPoint(ClearingFocus);
            settlement.localRotation = Quaternion.Euler(0, -15, 0);
            var target = settlement.gameObject.AddComponent<BoxCollider>();
            target.center = CampPosition(CourtyardAnchor) + Vector3.up * .6f;
            target.size = new Vector3(5.6f, 1.5f, 3.7f);
            timber = Material(art.WorldLit, new Color(.43f, .25f, .11f));
            sand = Material(art.WorldLit, new Color(.86f, .72f, .45f));
            canvas = Material(art.WorldLit, new Color(.91f, .78f, .50f));
            dark = Material(art.WorldLit, new Color(.16f, .20f, .16f));
            teal = Material(art.WorldLit, new Color(.11f, .42f, .49f));
            stone = Material(art.WorldLit, new Color(.65f, .66f, .57f));
            flame = Material(art.WorldLit, new Color(1, .53f, .10f));
            var woodTexture = PrototypeTextures.Wood();
            var canvasTexture = PrototypeTextures.Canvas();
            var stoneTexture = PrototypeTextures.Stone();
            owned.Add(woodTexture);
            owned.Add(canvasTexture);
            owned.Add(stoneTexture);
            timber.mainTexture = woodTexture;
            canvas.mainTexture = canvasTexture;
            stone.mainTexture = stoneTexture;
            teal.mainTexture = woodTexture;
            initialized = true;
            ApplyAmbient();
            UpdateSize();
        }

        public void Refresh(CampProgress camp)
        {
            int displayed = camp.ArrivalSeen ? camp.Level.Id : 0;
            if (level == displayed)
            {
                return;
            }

            level = displayed;
            waterPoint = null;
            if (buildings != null)
            {
                buildings.SetActive(false);
                Destroy(buildings);
                buildings = null;
            }

            if (level == 0)
            {
                return;
            }

            buildings = new GameObject("CampStage " + level);
            buildings.transform.SetParent(settlement, false);
            var root = buildings.transform;
            var courtyard = new GameObject("CampCourtyard").transform;
            courtyard.SetParent(root, false);
            courtyard.localPosition = CampPosition(CourtyardAnchor);
            courtyard.localScale = Vector3.one * .76f;
            if (level >= 3)
            {
                for (int x = -3; x <= 3; x++)
                {
                    for (int z = -2; z <= 2; z++)
                    {
                        Box("CourtyardPaving", courtyard, new Vector3(x * .82f, .025f, z * .78f), new Vector3(.78f, .05f, .74f), stone);
                    }
                }
            }

            Shadow(courtyard);
            Shelter(root, level >= 4);
            if (level >= 2)
            {
                Fence(courtyard);
                Workbench(root);
            }

            if (level >= 3)
            {
                Well(root);
                Farm(root);
            }

            var supplySite = CampPosition(SupplyAnchor);
            Box("SupplyChest", root, supplySite + new Vector3(0, .28f, 0), new Vector3(.75f, .5f, .6f), timber);
            Box("ChestBand", root, supplySite + new Vector3(0, .29f, -.02f), new Vector3(.12f, .55f, .65f), sand);
            var fireSite = CampPosition(CampfireAnchor);
            for (int index = 0; index < 6; index++)
            {
                float angle = index * Mathf.PI / 3;
                var offset = new Vector3(Mathf.Cos(angle) * .4f, .10f, Mathf.Sin(angle) * .4f);
                Box("FireStone", root, fireSite + offset, new Vector3(.27f, .20f, .25f), stone);
            }

            var fire = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            fire.name = "Campfire";
            fire.transform.SetParent(root, false);
            fire.transform.localPosition = fireSite + Vector3.up * .23f;
            fire.transform.localScale = new Vector3(.35f, .5f, .35f);
            fire.GetComponent<Renderer>().sharedMaterial = flame;
            Destroy(fire.GetComponent<Collider>());
        }

        private void Shelter(Transform root, bool house)
        {
            var body = new GameObject(house ? "IslandCabin" : "ArrivalTent").transform;
            body.SetParent(root, false);
            body.localPosition = CampPosition(ShelterAnchor) + Vector3.up * .06f;
            body.localScale = Vector3.one * 1.13f;
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
            mesh.uv = new[]
            {
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.zero, Vector2.right, Vector2.one, Vector2.up,
                Vector2.zero, new Vector2(.5f, 1), Vector2.right
            };
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
                if (Math.Abs(x) > .7f)
                {
                    Box("FencePost", root, new Vector3(x, .40f, 2.2f), new Vector3(.17f, .8f, .17f), timber);
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

            foreach (float x in new[] { -1.95f, 1.95f })
            {
                Box("FenceRail", root, new Vector3(x, .5f, 2.2f), new Vector3(2.3f, .12f, .10f), sand);
                Box("FenceRail", root, new Vector3(x, .5f, -2.2f), new Vector3(2.3f, .12f, .10f), sand);
            }

            foreach (float x in new[] { -.72f, .72f })
            {
                Box("InlandGatePost", root, new Vector3(x, .58f, 2.2f), new Vector3(.23f, 1.16f, .23f), timber);
            }
        }

        private void Workbench(Transform root)
        {
            var bench = new GameObject("Workbench").transform;
            bench.SetParent(root, false);
            bench.localPosition = CampPosition(WorkbenchAnchor);
            Box("WorkbenchTop", bench, new Vector3(0, .65f, 0), new Vector3(1.2f, .13f, .7f), timber);
            foreach (float x in new[] { -.48f, .48f })
            {
                Box("WorkbenchLeg", bench, new Vector3(x, .29f, 0), new Vector3(.16f, .58f, .56f), timber);
            }

            Box("ToolBox", bench, new Vector3(.3f, .79f, .05f), new Vector3(.34f, .16f, .25f), sand);
        }

        private void Farm(Transform root)
        {
            var plot = new GameObject("FarmPlot").transform;
            plot.SetParent(root, false);
            plot.localPosition = CampPosition(FarmAnchor);
            plot.localScale = Vector3.one * 1.70f;
            Box("FarmSoil", plot, new Vector3(0, .065f, 0), new Vector3(1.55f, .13f, 1.30f), timber);
            for (int row = 0; row < 3; row++)
            {
                float x = (row - 1) * .43f;
                Box("FarmFurrow", plot, new Vector3(x, .14f, 0), new Vector3(.20f, .08f, 1.14f), dark);
                foreach (float z in new[] { -.31f, .31f })
                {
                    Box("FarmSprout", plot, new Vector3(x, .25f, z), new Vector3(.18f, .21f, .18f), teal);
                }
            }
        }

        private void Well(Transform root)
        {
            var point = new GameObject("WaterPoint").transform;
            waterPoint = point;
            point.SetParent(root, false);
            point.localPosition = CampPosition(WaterAnchor);
            var interaction = point.gameObject.AddComponent<BoxCollider>();
            interaction.center = new Vector3(0, .48f, 0);
            interaction.size = new Vector3(.85f, .96f, .85f);
            var well = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            well.name = "CourtyardWell";
            well.transform.SetParent(point, false);
            well.transform.localPosition = new Vector3(0, .29f, 0);
            well.transform.localScale = new Vector3(.76f, .29f, .76f);
            well.GetComponent<Renderer>().sharedMaterial = stone;
            Destroy(well.GetComponent<Collider>());
            Box("WellBeam", point, new Vector3(0, .90f, 0), new Vector3(.85f, .12f, .15f), timber);
            foreach (float x in new[] { -.32f, .32f })
            {
                Box("WellPost", point, new Vector3(x, .60f, 0), new Vector3(.1f, .57f, .1f), timber);
            }
        }

        private Material Material(Material template, Color color)
        {
            var material = new Material(template) { color = color };
            material.SetFloat("_Smoothness", .05f);
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
            shadow.GetComponent<MeshRenderer>().sharedMaterial = worldSprite;
        }

        private void Update()
        {
            UpdateSize();
            UpdateCameraTransition();
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

            if (!Physics.Raycast(sceneCamera.ScreenPointToRay(position), out var hit))
            {
                return;
            }

            if (hit.transform == waterPoint)
            {
                WaterSelected?.Invoke();
            }
            else if (hit.transform == settlement)
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
            float aspect = Mathf.Max(.1f, (float)screenWidth / Mathf.Max(1, screenHeight));
            overviewSize = Mathf.Max(OverviewHalfHeight, artworkHalfWidth / aspect);
            ApplyCameraZoom();
        }

        internal bool CanMoveCamera(CampCameraMove move)
        {
            return !Mathf.Approximately(ResolveCameraTarget(move), targetZoom);
        }

        internal void MoveCamera(CampCameraMove move)
        {
            float target = ResolveCameraTarget(move);
            if (Mathf.Approximately(target, targetZoom))
            {
                return;
            }

            startZoom = cameraZoom;
            targetZoom = target;
            cameraElapsed = 0;
        }

        private float ResolveCameraTarget(CampCameraMove move)
        {
            return move switch
            {
                CampCameraMove.Near => Mathf.Min(1.6f, targetZoom + .15f),
                CampCameraMove.Far => Mathf.Max(1, targetZoom - .15f),
                CampCameraMove.Reset => 1,
                _ => targetZoom
            };
        }

        private void UpdateCameraTransition()
        {
            if (cameraElapsed >= CameraTransitionSeconds)
            {
                return;
            }

            cameraElapsed = Mathf.Min(CameraTransitionSeconds, cameraElapsed + Time.unscaledDeltaTime);
            float blend = Mathf.SmoothStep(0, 1, cameraElapsed / CameraTransitionSeconds);
            cameraZoom = Mathf.Lerp(startZoom, targetZoom, blend);
            ApplyCameraZoom();
        }

        private void ApplyCameraZoom()
        {
            var cameraTransform = sceneCamera.transform;
            var overviewPosition = cameraTransform.localRotation * (Vector3.back * CameraDistance);
            cameraTransform.localPosition = overviewPosition + settlement.localPosition * (1 - 1 / cameraZoom);
            sceneCamera.orthographicSize = overviewSize / cameraZoom;
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
