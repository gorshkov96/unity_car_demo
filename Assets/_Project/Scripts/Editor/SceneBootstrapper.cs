using System.IO;
using CarDemo.Core;
using CarDemo.Diagnostics;
using Unity.Cinemachine;
using CarDemo.Game;
using CarDemo.UI;
using CarDemo.Vehicle;
using CarDemo.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Generates the demo scene and every asset it needs from code.
    ///
    /// Rationale: a hand-built .unity file is an opaque YAML blob that cannot be
    /// reviewed in a diff or regenerated after a config change. This tool makes
    /// the scene a build artifact of readable code — run it from the menu or
    /// from the command line with -executeMethod.
    /// </summary>
    public static class SceneBootstrapper
    {
        private const string ProjectRoot = "Assets/_Project";
        private const string SettingsFolder = ProjectRoot + "/Settings";
        private const string MaterialsFolder = ProjectRoot + "/Materials";
        private const string ScenesFolder = ProjectRoot + "/Scenes";
        private const string ScenePath = ScenesFolder + "/Demo.unity";
        private const string BenchmarkScenePath = ScenesFolder + "/Benchmark.unity";
        private const string PanelSettingsPath = SettingsFolder + "/CarDemoPanelSettings.asset";
        private const string ThemePath = ProjectRoot + "/UI/CarDemoTheme.tss";
        private const string CarConfigPath = SettingsFolder + "/CarConfig.asset";
        private const string WorldConfigPath = SettingsFolder + "/WorldConfig.asset";

        [MenuItem("CarDemo/Rebuild Demo Scene")]
        public static void RebuildDemoScene()
        {
            EnsureFolders();
            LayerBootstrapper.Apply();

            // The empty scene is created FIRST on purpose: NewScene unloads unused assets,
            // which would kill freshly loaded config references and leave the generator
            // silently building nothing.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CarConfig carConfig = LoadOrCreate<CarConfig>(CarConfigPath);
            WorldConfig worldConfig = LoadOrCreate<WorldConfig>(WorldConfigPath);

            Material ground = LoadOrCreateMaterial("Ground", new Color(0.24f, 0.26f, 0.28f), smoothness: 0.15f);
            Material road = LoadOrCreateMaterial("Road", new Color(0.13f, 0.14f, 0.16f), smoothness: 0.35f);
            Material building = LoadOrCreateMaterial("Building", new Color(0.62f, 0.63f, 0.66f), smoothness: 0.2f);
            Material accent = LoadOrCreateMaterial("Accent", new Color(0.85f, 0.42f, 0.16f), smoothness: 0.3f);
            Material moving = LoadOrCreateMaterial("Moving", new Color(0.25f, 0.65f, 0.85f), smoothness: 0.5f);
            Material carBody = LoadOrCreateMaterial("CarBody", new Color(0.75f, 0.13f, 0.16f), smoothness: 0.6f);
            Material wheel = LoadOrCreateMaterial("Wheel", new Color(0.09f, 0.09f, 0.1f), smoothness: 0.25f);

            AssignMaterials(carConfig, carBody, wheel);

            if (worldConfig == null || carConfig == null)
            {
                Debug.LogError("[CarDemo] Config assets could not be loaded — aborting.");
                return;
            }

            CreateLighting();
            GameObject worldRoot = CreateWorld(worldConfig, ground, road, building, accent, moving);
            CarController car = CreateCar(carConfig, worldRoot.GetComponent<WorldBuilder>());
            CreateCamera(car);

            worldRoot.transform.SetAsFirstSibling();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            RegisterSceneInBuildSettings();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[CarDemo] Demo scene rebuilt at {ScenePath}");
        }

        /// <summary>Entry point for `-executeMethod CarDemo.EditorTools.SceneBootstrapper.BuildFromCommandLine`.</summary>
        public static void BuildFromCommandLine()
        {
            RebuildDemoScene();
        }

        /// <summary>
        /// Builds a variant of the demo scene that drives itself and measures the frame budget,
        /// then quits. This is the reproducible baseline the optimisation work is judged against.
        /// </summary>
        [MenuItem("CarDemo/Rebuild Benchmark Scene")]
        public static void RebuildBenchmarkScene()
        {
            RebuildDemoScene();

            var world = Object.FindFirstObjectByType<WorldBuilder>();
            var car = Object.FindFirstObjectByType<CarController>();
            if (world == null || car == null)
            {
                Debug.LogError("[CarDemo] Benchmark scene needs both a world and a car.");
                return;
            }

            // The player input component would fight the auto driver for the controller's
            // single input provider, so it is removed rather than left disabled.
            var playerInput = car.GetComponent<PlayerCarInput>();
            if (playerInput != null) Object.DestroyImmediate(playerInput);

            AutoDriver driver = car.gameObject.AddComponent<AutoDriver>();
            driver.Configure(world.Config.RoadRadius, throttle: 1f);

            PerformanceProbe probe = Object.FindFirstObjectByType<PerformanceProbe>();
            if (probe == null)
            {
                Debug.LogError("[CarDemo] Benchmark scene has no PerformanceProbe.");
                return;
            }

            probe.Configure(targetFrameRate: 60f, reportInterval: 5f, autoQuitAfter: 35f);

            Scene scene = SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, BenchmarkScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BenchmarkScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log($"[CarDemo] Benchmark scene written to {BenchmarkScenePath}");
        }

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { SettingsFolder, MaterialsFolder, ScenesFolder })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;

                string parent = Path.GetDirectoryName(folder)!.Replace('\\', '/');
                string leaf = Path.GetFileName(folder);
                if (!AssetDatabase.IsValidFolder(parent))
                {
                    AssetDatabase.CreateFolder(Path.GetDirectoryName(parent)!.Replace('\\', '/'), Path.GetFileName(parent));
                }

                AssetDatabase.CreateFolder(parent, leaf);
            }
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Material LoadOrCreateMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                Debug.LogWarning("[CarDemo] URP Lit shader not found, falling back to Standard.");
                shader = Shader.Find("Standard");
            }

            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void AssignMaterials(CarConfig config, Material body, Material wheel)
        {
            config.SetEditorMaterials(body, wheel);
            EditorUtility.SetDirty(config);
        }

        private static void CreateLighting()
        {
            var sun = new GameObject("Directional Light");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.intensity = 1.4f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, 35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.52f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.3f, 0.32f, 0.35f);
            RenderSettings.ambientGroundColor = new Color(0.15f, 0.15f, 0.16f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.55f, 0.6f, 0.68f);
            RenderSettings.fogDensity = 0.0035f;
        }

        private static GameObject CreateWorld(
            WorldConfig config, Material ground, Material road, Material building, Material accent, Material moving)
        {
            var root = new GameObject("World");
            WorldBuilder builder = root.AddComponent<WorldBuilder>();
            builder.Configure(config, ground, road, building, accent, moving);

            // Bake the geometry into the scene so it is visible in the editor without
            // entering play mode. The runtime auto-build then no-ops (children exist).
            builder.Build();
            return root;
        }

        private static CarController CreateCar(CarConfig carConfig, WorldBuilder world)
        {
            CarRigFactory.Rig rig = CarRigFactory.Build(carConfig, world.CarSpawnPosition, world.CarSpawnRotation);

            CarController controller = rig.Root.AddComponent<CarController>();
            controller.Configure(carConfig);
            GameLayers.ApplyRecursively(rig.Root, GameLayers.Vehicle);

            rig.Root.AddComponent<PlayerCarInput>();

            CarWheelVisuals visuals = rig.Root.AddComponent<CarWheelVisuals>();
            visuals.SetWheelTransforms(rig.Wheels);

            rig.Root.AddComponent<CarResetter>();

            CreateHud(controller);
            return controller;
        }

        /// <summary>Builds the UI Toolkit HUD and the PanelSettings asset it needs.</summary>
        private static void CreateHud(CarController car)
        {
            var hudRoot = new GameObject("HUD");
            UIDocument document = hudRoot.AddComponent<UIDocument>();
            document.panelSettings = LoadOrCreatePanelSettings();

            // The probe lives in the normal scene too: the frame budget is part of what this
            // demo is showing off, so the number stays on screen instead of hiding in a log.
            PerformanceProbe probe = hudRoot.AddComponent<PerformanceProbe>();
            probe.Configure(targetFrameRate: 60f, reportInterval: 0f, autoQuitAfter: 0f);

            DriverHud hud = hudRoot.AddComponent<DriverHud>();
            hud.SetCar(car);
            hud.SetProbe(probe);
        }

        private static PanelSettings LoadOrCreatePanelSettings()
        {
            var existing = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (existing != null) return existing;

            var settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.name = "CarDemoPanelSettings";
            // Scale the UI with the window so the HUD keeps its proportions on a 4K display.
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;

            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            if (theme != null)
            {
                settings.themeStyleSheet = theme;
            }
            else
            {
                Debug.LogWarning($"[CarDemo] Theme not found at {ThemePath}; HUD will use no theme.");
            }

            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return settings;
        }

        /// <summary>
        /// Builds the Cinemachine rig: a plain Camera with a CinemachineBrain, plus a virtual
        /// camera that follows the car. The camera is a root object, never a child of the car —
        /// parenting it would inherit the body's roll and pitch and make the picture unreadable.
        /// </summary>
        private static void CreateCamera(CarController car)
        {
            var cameraRoot = new GameObject("Main Camera");
            cameraRoot.tag = "MainCamera";
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.fieldOfView = 62f;
            camera.farClipPlane = 500f;
            cameraRoot.AddComponent<AudioListener>();
            cameraRoot.AddComponent<CinemachineBrain>();

            var rigRoot = new GameObject("Chase Camera");
            CinemachineCamera virtualCamera = rigRoot.AddComponent<CinemachineCamera>();
            virtualCamera.Follow = car.transform;
            virtualCamera.LookAt = car.transform;
            virtualCamera.Lens.FieldOfView = 62f;

            CinemachineOrbitalFollow follow = rigRoot.AddComponent<CinemachineOrbitalFollow>();
            follow.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            follow.Radius = 7.5f;
            follow.TargetOffset = new Vector3(0f, 1.1f, 0f);

            // The horizontal axis recenters behind the car, so the camera swings back into
            // place after a spin instead of staring at the side of the car.
            follow.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.TrackingTarget;
            follow.HorizontalAxis.Recentering.Enabled = true;
            follow.HorizontalAxis.Recentering.Wait = 0.4f;
            follow.HorizontalAxis.Recentering.Time = 1.2f;
            follow.VerticalAxis.Value = 14f;
            follow.VerticalAxis.Center = 14f;

            // Damping is uneven on purpose: loose along the direction of travel so the camera
            // trails on acceleration, tighter sideways so corners stay readable.
            follow.TrackerSettings.PositionDamping = new Vector3(0.4f, 0.5f, 0.9f);
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LockToTargetWithWorldUp;

            CinemachineRotationComposer composer = rigRoot.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, 1.2f, 0f);
            composer.Damping = new Vector2(0.5f, 0.5f);

            ChaseCameraRig rig = rigRoot.AddComponent<ChaseCameraRig>();
            rig.SetCar(car);

            cameraRoot.transform.position = car.transform.TransformPoint(new Vector3(0f, 3.2f, -7.5f));
            cameraRoot.transform.LookAt(car.transform.position + Vector3.up);
        }

        private static void RegisterSceneInBuildSettings()
        {
            var scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            EditorBuildSettings.scenes = scenes;
        }
    }
}
