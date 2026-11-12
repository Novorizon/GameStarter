using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NavigationLab.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NavigationLab.UnityPlayground
{
    public sealed class PlaygroundWindow : EditorWindow
    {
        [SerializeField] private int candidate;
        [SerializeField] private string inputName = "forest-wave-1";
        [SerializeField] private bool isometric;
        [SerializeField] private float speed = 1;
        private Vector2 scroll;

        [MenuItem("Tools/GameStarter/Navigation Playground")]
        public static void Open()
        {
            GetWindow<PlaygroundWindow>("Navigation Playground").Show();
        }

        private void OnEnable()
        {
            minSize = new Vector2(430, 510);
            EditorApplication.update += Repaint;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("Navigation Playground", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("六种冻结方案 / 49 个原测试输入。无炮塔，几何体为 NPC；每次模拟独立创建导航器。", MessageType.Info);
            if (!string.IsNullOrEmpty(PlaygroundController.Error))
            {
                EditorGUILayout.HelpBox(PlaygroundController.Error, MessageType.Error);
            }
            using (new EditorGUI.DisabledScope(PlaygroundController.IsBusy))
            {
                candidate = EditorGUILayout.Popup("方案", candidate, PlaygroundController.CandidateNames);
                string[] inputs = PlaygroundController.InputNames;
                int selected = Mathf.Max(0, Array.IndexOf(inputs, inputName));
                if (inputs.Length > 0)
                {
                    selected = EditorGUILayout.Popup("测试地图", selected, inputs);
                    inputName = inputs[selected];
                }
            }
            EditorGUILayout.LabelField(PlaygroundController.CreateFactory(candidate).Description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(PlaygroundController.IsBusy || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying))
            {
                if (GUILayout.Button("开始 / 重新开始", GUILayout.Height(28)))
                {
                    PlaygroundController.Begin(candidate, inputName, speed, isometric);
                }
            }
            using (new EditorGUI.DisabledScope(!PlaygroundController.IsBusy))
            {
                if (GUILayout.Button(PlaygroundController.Paused ? "继续" : "暂停", GUILayout.Height(28)))
                {
                    PlaygroundController.SetPaused(!PlaygroundController.Paused);
                }
                if (GUILayout.Button("单步 50ms", GUILayout.Height(28)))
                {
                    PlaygroundController.Step();
                }
                if (GUILayout.Button("停止", GUILayout.Height(28)))
                {
                    PlaygroundController.Stop();
                }
            }
            EditorGUILayout.EndHorizontal();
            float updatedSpeed = EditorGUILayout.Slider("目标速度", speed, .25f, 20);
            if (updatedSpeed != speed)
            {
                speed = updatedSpeed;
                PlaygroundController.SetSpeed(speed);
            }
            bool updatedCamera = EditorGUILayout.Toggle("斜视相机（关闭为俯视）", isometric);
            if (updatedCamera != isometric)
            {
                isometric = updatedCamera;
                PlaygroundController.SetIsometric(isometric);
            }
            EditorGUILayout.Space();
            PlaygroundSnapshot snapshot = PlaygroundController.Latest;
            if (snapshot != null)
            {
                EditorGUILayout.LabelField("当前运行", PlaygroundController.ActiveLabel);
                EditorGUILayout.LabelField("状态", snapshot.Status);
                EditorGUILayout.LabelField("逻辑时间", $"{snapshot.Microstep * .005:F2}s / {snapshot.Microstep / 10} ticks");
                EditorGUILayout.LabelField("生成", $"提交计划 {snapshot.Issued} / 累计出生 {snapshot.Admitted} / 未出生 {snapshot.Pending}（含后续计划）");
                EditorGUILayout.LabelField("场上", $"{snapshot.Units.Length} / 移除 {snapshot.Deaths} / 稳定 {snapshot.StableTicks} ticks");
                EditorGUILayout.LabelField("最近物理步耗时", $"{snapshot.LastStepMilliseconds:F2} ms（包含当前方案计算）");
                if (!string.IsNullOrEmpty(snapshot.Detail))
                {
                    EditorGUILayout.HelpBox(snapshot.Detail, snapshot.Status == "Failed" ? MessageType.Error : MessageType.Info);
                }
                if (!string.IsNullOrEmpty(snapshot.LastSpawnFailure))
                {
                    EditorGUILayout.LabelField("最后出生反馈", snapshot.LastSpawnFailure, EditorStyles.wordWrappedLabel);
                }
                DrawSelection(snapshot);
            }
            else
            {
                EditorGUILayout.HelpBox(PlaygroundController.Message, MessageType.None);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("青色：移动  /  红色：攻击  /  金色：等待", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("地图：深灰阻挡，石色道路，绿色允许等待，赭色出生口。蓝柱为目标。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("在 Scene 视图点击单位看攻击范围和目标格。连线仅表示目标方向，不是实际路线。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.HelpBox("模拟在后台串行计算。调速不会跳过物理步；复杂场景可能慢于目标速度。停止会等待当前算法调用返回。", MessageType.None);
            EditorGUILayout.LabelField("Bootstrap 是原业务入口，仍使用 A；本工具是独立的编辑器观察场景。", EditorStyles.wordWrappedLabel);
            EditorGUILayout.EndScrollView();
        }

        private static void DrawSelection(PlaygroundSnapshot snapshot)
        {
            int id = PlaygroundController.SelectedId;
            foreach (UnitSnapshot unit in snapshot.Units)
            {
                if (unit.Id != id || !snapshot.Live.TryGetValue(id, out SpawnRequest request))
                {
                    continue;
                }
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"选中 NPC {id} / {unit.State}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("半径 / 攻击距离", $"{request.Profile.RadiusMm} / {request.Profile.AttackRangeMm} mm");
                EditorGUILayout.LabelField("位置 / 目标格", $"({unit.Position.X:F1}, {unit.Position.Y:F1}) / {unit.GoalCell}");
                if (GUILayout.Button("Scene 聚焦选中单位"))
                {
                    PlaygroundController.FocusSelection();
                }
                break;
            }
        }
    }

    [InitializeOnLoad]
    public static class PlaygroundController
    {
        public const string Root = "Assets/NavigationPlayground";
        public const string ScenePath = Root + "/Scenes/NavigationPlayground.unity";
        private const string PendingKey = "NavigationPlayground.Pending";
        private const string PreviousSceneKey = "NavigationPlayground.PreviousStartScene";
        private static readonly string[] Names = { "A / SIPP (Siege)", "B0 / ORCA", "B1 / ORCA + Yield", "C33 / Flow field (Siege)", "D12 / Hierarchical + exchange", "E / A* + refinement" };
        private static readonly string[] PluginHashes =
        {
            "NavigationLab.Core.dll:856fd3729b3e44972361c1e36fe0a0b333c7c215c9676e74ca372e397d5bbb73",
            "NavigationLab.Oracle.dll:59453caa82a3e057ccca6f5e9bdc69ddda61f6278d4d9dfe00da2a209a44d17b",
            "NavigationLab.ASipp.dll:96fdf5a72eb8f02b2863c050db4686435aa3b3902535e52c9747569eef25f725",
            "NavigationLab.BOrca.dll:a69bc87d5e10f614f463929f374d2b294ed0c0840377c91fcfc7b7f7280e2831",
            "NavigationLab.CSupCom2.dll:e6c556f0edae077273ee5fb90017086e7add19019fbf32dc9f367247d022435f",
            "NavigationLab.DAoe4.dll:9e6107cfe1dcd6d83ac137314109a145835c764b3a29a87171bde4412572b19c",
            "NavigationLab.EWarcraft3.dll:f0e3f31d8ade5fbb7eb691228b7b7fd71491f30c51ffffb4938755c78b99d29d"
        };
        private static PlaygroundSimulation simulation;
        private static PlaygroundView view;
        private static PlaygroundSnapshot rendered;
        private static bool paused;
        private static bool isometric;
        private static string[] inputNames;

        public static string[] CandidateNames => Names;
        public static string[] InputNames => inputNames ??= ReadInputNames();
        public static bool IsBusy => SessionState.GetString(PendingKey, "").Length > 0 || simulation != null && !simulation.IsCompleted;
        public static bool Paused => paused;
        public static PlaygroundSnapshot Latest => simulation?.Latest;
        public static int SelectedId => view?.SelectedId ?? -1;
        public static string ActiveLabel { get; private set; } = "";
        public static string Error { get; private set; } = "";
        public static string Message { get; private set; } = "选择方案和地图后点击开始。Unity 将进入专用 Play 场景，退出后恢复原编辑场景。";

        static PlaygroundController()
        {
            EditorApplication.playModeStateChanged += OnPlayMode;
            EditorApplication.update += Update;
            AssemblyReloadEvents.beforeAssemblyReload += Dispose;
            EditorApplication.quitting += Dispose;
            SceneView.duringSceneGui += DrawScene;
        }

        public static INavigationFactory CreateFactory(int index)
        {
            switch (index)
            {
                case 0: return new NavigationLab.Candidates.ASipp.SippFactory();
                case 1: return new NavigationLab.Candidates.BOrca.OrcaFactory();
                case 2: return new NavigationLab.Candidates.BOrca.OrcaYieldFactory();
                case 3: return new NavigationLab.CSupCom2.Factory();
                case 4: return new NavigationLab.DAoe4.Aoe4ProgressExchangeFactory();
                case 5: return new NavigationLab.Candidates.EWarcraft3.WarcraftRefinedFactory();
                default: throw new ArgumentOutOfRangeException(nameof(index));
            }
        }

        public static void Begin(int candidate, string input, double speed = 1, bool angled = false)
        {
            if (IsBusy)
            {
                return;
            }
            try
            {
                Error = "";
                ValidateAssets(input);
                var selection = new PendingSelection { candidate = candidate, input = input, speed = speed, isometric = angled };
                if (EditorApplication.isPlaying)
                {
                    if (SceneManager.GetActiveScene().path != ScenePath)
                    {
                        throw new InvalidOperationException("请先退出其他游戏的 Play，再开始导航观察。");
                    }
                    StartSimulation(selection);
                    return;
                }
                PrepareScene();
                SessionState.SetString(PreviousSceneKey, AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
                SessionState.SetString(PendingKey, JsonUtility.ToJson(selection));
                EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception error)
            {
                Error = error.ToString();
                Message = error.ToString();
                Debug.LogException(error);
                RestoreStartScene();
                SessionState.EraseString(PendingKey);
            }
        }

        public static void PrepareScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                return;
            }
            Directory.CreateDirectory(Root + "/Scenes");
            Scene original = SceneManager.GetActiveScene();
            bool emptyUntitled = SceneManager.sceneCount == 1 && string.IsNullOrEmpty(original.path) && !original.isDirty && original.rootCount == 0;
            if (string.IsNullOrEmpty(original.path) && !emptyUntitled)
            {
                throw new InvalidOperationException("专用测试场景缺失。请先保存当前未命名场景，再重新开始，或从 Git 恢复 NavigationPlayground.unity。");
            }
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, emptyUntitled ? NewSceneMode.Single : NewSceneMode.Additive);
            EditorSceneManager.SaveScene(scene, ScenePath);
            if (!emptyUntitled)
            {
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid())
                {
                    SceneManager.SetActiveScene(original);
                }
            }
            AssetDatabase.Refresh();
        }

        public static void SetPaused(bool value)
        {
            paused = value;
            simulation?.Pause(value || EditorApplication.isPaused);
        }

        public static void Step()
        {
            SetPaused(true);
            simulation?.StepLogicalTick();
        }

        public static void Stop()
        {
            simulation?.RequestStop();
        }

        public static void SetSpeed(double speed)
        {
            simulation?.SetSpeed(speed);
        }

        public static void SetIsometric(bool value)
        {
            isometric = value;
            view?.SetIsometric(value);
        }

        public static void FocusSelection()
        {
            view?.FocusSelection();
        }

        public static void Dispose()
        {
            simulation?.Dispose();
            simulation = null;
            view?.Dispose();
            view = null;
            rendered = null;
            paused = false;
        }

        private static void StartSimulation(PendingSelection selection)
        {
            Dispose();
            var input = JsonUtility.FromJson<PlaygroundInput>(File.ReadAllText(InputPath(selection.input)));
            simulation = new PlaygroundSimulation(input, CreateFactory(selection.candidate));
            simulation.SetSpeed(selection.speed);
            isometric = selection.isometric;
            view = new PlaygroundView();
            view.SetIsometric(isometric);
            ActiveLabel = Names[selection.candidate] + " / " + selection.input;
            simulation.Start();
            GetWindow();
        }

        private static void GetWindow()
        {
            PlaygroundWindow.Open();
        }

        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                string json = SessionState.GetString(PendingKey, "");
                SessionState.EraseString(PendingKey);
                RestoreStartScene();
                if (json.Length == 0)
                {
                    return;
                }
                try
                {
                    if (SceneManager.GetActiveScene().path != ScenePath)
                    {
                        throw new InvalidOperationException("Navigation Playground scene was not loaded.");
                    }
                    StartSimulation(JsonUtility.FromJson<PendingSelection>(json));
                }
                catch (Exception error)
                {
                    Error = error.ToString();
                    Message = error.ToString();
                    Debug.LogException(error);
                    Dispose();
                }
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Dispose();
            }
            else if (state == PlayModeStateChange.EnteredEditMode)
            {
                RestoreStartScene();
                SessionState.EraseString(PendingKey);
            }
        }

        private static void RestoreStartScene()
        {
            string previous = SessionState.GetString(PreviousSceneKey, "__unset");
            if (previous == "__unset")
            {
                return;
            }
            EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
            SessionState.EraseString(PreviousSceneKey);
        }

        private static void Update()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && SessionState.GetString(PendingKey, "").Length > 0)
            {
                RestoreStartScene();
                SessionState.EraseString(PendingKey);
                Message = "已取消进入 Play，可重新开始。";
            }
            if (simulation == null)
            {
                return;
            }
            simulation.Pause(paused || EditorApplication.isPaused);
            PlaygroundSnapshot snapshot = simulation.Latest;
            if (snapshot != null && snapshot.World != null && !ReferenceEquals(rendered, snapshot))
            {
                view.Render(snapshot.World, snapshot.Units, snapshot.Live);
                rendered = snapshot;
                SceneView.RepaintAll();
            }
        }

        private static void DrawScene(SceneView scene)
        {
            view?.DrawSceneHandles(scene);
        }

        private static string InputPath(string input)
        {
            if (!InputNames.Contains(input))
            {
                throw new ArgumentException("Unknown input: " + input);
            }
            return Root + "/Editor/Inputs/" + input + ".json";
        }

        private static string[] ReadInputNames()
        {
            string[] files = Directory.GetFiles(Root + "/Editor/Inputs", "*.json");
            return files.Select(Path.GetFileNameWithoutExtension).OrderBy(x => x).ToArray();
        }

        private static void ValidateAssets(string input)
        {
            Type[] loadedTypes =
            {
                typeof(INavigationFactory),
                typeof(NavigationLab.Oracle.ContinuousOracle),
                typeof(NavigationLab.Candidates.ASipp.SippFactory),
                typeof(NavigationLab.Candidates.BOrca.OrcaFactory),
                typeof(NavigationLab.CSupCom2.Factory),
                typeof(NavigationLab.DAoe4.Aoe4ProgressExchangeFactory),
                typeof(NavigationLab.Candidates.EWarcraft3.WarcraftRefinedFactory)
            };
            foreach (string binding in PluginHashes)
            {
                string[] parts = binding.Split(':');
                string path = Root + "/Editor/Plugins/" + parts[0];
                if (Hash(path) != parts[1])
                {
                    throw new InvalidOperationException("Frozen navigation DLL changed: " + path);
                }
                var assembly = loadedTypes.Select(type => type.Assembly).SingleOrDefault(a => a.GetName().Name == Path.GetFileNameWithoutExtension(path));
                if (assembly == null || Hash(assembly.Location) != parts[1])
                {
                    throw new InvalidOperationException("Loaded navigation assembly differs from frozen DLL: " + parts[0]);
                }
            }
            var bindings = JsonUtility.FromJson<InputBindings>(File.ReadAllText(Root + "/Editor/playground-bindings.json"));
            InputBinding selected = bindings.inputs.Single(x => x.name == input);
            if (Hash(InputPath(input)) != selected.unityFileSha256)
            {
                throw new InvalidOperationException("Frozen input changed: " + input);
            }
        }

        private static string Hash(string path)
        {
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant();
            }
        }

        [Serializable]
        private sealed class PendingSelection
        {
            public int candidate;
            public string input;
            public double speed;
            public bool isometric;
        }

        [Serializable]
        private sealed class InputBindings
        {
            public InputBinding[] inputs;
        }

        [Serializable]
        private sealed class InputBinding
        {
            public string name;
            public string unityFileSha256;
        }
    }
}
