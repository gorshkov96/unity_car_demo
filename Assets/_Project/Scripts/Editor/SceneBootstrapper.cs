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
        private const string DescentScenePath = ScenesFolder + "/Descent.unity";
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

            CarConfig carConfig = CreateConfig<CarConfig>(CarConfigPath);
            WorldConfig worldConfig = CreateConfig<WorldConfig>(WorldConfigPath);

            Material ground = LoadOrCreateMaterial("Ground", Palette.Ground, smoothness: 0.05f);
            Material road = LoadOrCreateMaterial("Road", Palette.RingRoad, smoothness: 0.1f);
            Material building = LoadOrCreateMaterial("Building", Palette.Building, smoothness: 0.06f);
            Material accent = LoadOrCreateMaterial("Accent", Palette.Ramp, smoothness: 0.14f);
            Material moving = LoadOrCreateMaterial("Moving", Palette.Moving, smoothness: 0.22f);
            Material carBody = LoadOrCreateMaterial("CarBody", Palette.CarBody, smoothness: 0.55f);
            Material wheel = LoadOrCreateMaterial("Wheel", Palette.Tyre, smoothness: 0.08f);
            Material trim = LoadOrCreateMaterial("Trim", Palette.CarTrim, smoothness: 0.3f);
            Material glass = LoadOrCreateMaterial("Glass", Palette.CarGlass, smoothness: 0.85f);
            Material rim = LoadOrCreateMaterial("Rim", Palette.CarRim, smoothness: 0.5f, metallic: 0.6f);

            // Lights are emissive well above 1 so bloom, whose threshold sits at 1, sees them
            // and nothing else does.
            Material headlight = LoadOrCreateMaterial("Headlight", Palette.Headlight, smoothness: 0.8f,
                emission: Palette.Emissive(Palette.Headlight, 3.2f));
            Material taillight = LoadOrCreateMaterial("Taillight", Palette.Taillight, smoothness: 0.8f,
                emission: Palette.Emissive(Palette.Taillight, 2.6f));

            AssignMaterials(carConfig, carBody, wheel, trim, glass, headlight, taillight, rim);

            if (worldConfig == null || carConfig == null)
            {
                Debug.LogError("[CarDemo] Config assets could not be loaded — aborting.");
                return;
            }

            CreateLighting();
            PostProcessingBootstrapper.Create();
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

        /// <summary>
        /// Creates a config asset from the code defaults, replacing any existing one.
        ///
        /// This project treats the code as the source of truth: an asset created weeks ago
        /// keeps its serialised values forever, so editing a default in C# has no effect on
        /// the asset the game actually loads. That silently split the tuning in two — the
        /// numbers being read in review were not the numbers being played.
        /// </summary>
        private static T CreateConfig<T>(string path) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();

            if (AssetDatabase.LoadAssetAtPath<T>(path) != null)
            {
                AssetDatabase.DeleteAsset(path);
            }

            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Material LoadOrCreateMaterial(
            string name, Color color, float smoothness, float metallic = 0f, Color? emission = null)
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
            material.SetFloat("_Metallic", metallic);

            if (emission.HasValue)
            {
                // Emissive lights need both the keyword and the global illumination flag,
                // otherwise the colour is set but nothing glows.
                material.EnableKeyword("_EMISSION");
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                material.SetColor("_EmissionColor", emission.Value);
            }

            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void AssignMaterials(
            CarConfig config, Material body, Material wheel, Material trim, Material glass,
            Material headlight, Material taillight, Material rim)
        {
            config.SetEditorMaterials(body, wheel, trim, glass, headlight, taillight, rim);
            EditorUtility.SetDirty(config);
        }

        /// <summary>Gradient sky, so the horizon is not a flat default backdrop.</summary>
        private static void CreateSky()
        {
            const string path = MaterialsFolder + "/Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);

            if (sky == null)
            {
                Shader shader = Shader.Find("Skybox/Procedural");
                if (shader == null) return;

                sky = new Material(shader) { name = "Sky" };
                AssetDatabase.CreateAsset(sky, path);
            }

            sky.SetFloat("_SunSize", 0.06f);
            // Thinner atmosphere: the default scatters a strong cyan band along the
            // horizon, which reads as acid rather than as sky.
            sky.SetFloat("_AtmosphereThickness", 0.45f);
            sky.SetColor("_SkyTint", Palette.SkyTint);
            sky.SetColor("_GroundColor", Palette.SkyGround);
            sky.SetFloat("_Exposure", 1.05f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
        }

        private static void CreateLighting()
        {
            CreateSky();

            var sun = new GameObject("Directional Light");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Palette.SunLight;
            light.intensity = 2.1f;
            light.shadows = LightShadows.Soft;
            sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Palette.AmbientSky;
            RenderSettings.ambientEquatorColor = Palette.AmbientEquator;
            RenderSettings.ambientGroundColor = Palette.AmbientGround;

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Palette.Fog;
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

            hudRoot.AddComponent<SceneSwitcher>();
            hudRoot.AddComponent<MapMenu>();
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
            // The camera follows a target that faces the direction of travel rather than the
            // car's nose; on a slope or in a drift those are not the same thing.
            var targetRoot = new GameObject("Camera Target");
            ChaseCameraTarget target = targetRoot.AddComponent<ChaseCameraTarget>();
            target.Configure(car, car.GetComponent<Rigidbody>());

            CinemachineCamera virtualCamera = rigRoot.AddComponent<CinemachineCamera>();
            virtualCamera.Follow = targetRoot.transform;
            virtualCamera.LookAt = targetRoot.transform;
            virtualCamera.Lens.FieldOfView = 62f;

            CinemachineOrbitalFollow follow = rigRoot.AddComponent<CinemachineOrbitalFollow>();
            follow.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            follow.Radius = 7.5f;
            follow.TargetOffset = new Vector3(0f, 1.1f, 0f);

            // The horizontal axis recenters behind the car, so the camera swings back into
            // place after a spin instead of staring at the side of the car.
            // Recentre behind the tracking target, which is the travel-aligned proxy rather
            // than the car body — so a roll or a spin never drags the camera around with it.
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

            // Decollider, not Deoccluder: the docs pick it for a car camera because the job
            // here is pushing the camera out of walls and off the ground, not preserving line
            // of sight at any cost. Without it the camera clips into a wall and renders it
            // from the inside — which shows the player the back faces of the world.
            CinemachineDecollider decollider = rigRoot.AddComponent<CinemachineDecollider>();
            decollider.CameraRadius = 0.35f;
            decollider.Decollision.Enabled = true;
            decollider.Decollision.ObstacleLayers = GameLayers.CameraObstacleMask;
            decollider.Decollision.SmoothingTime = 0.2f;

            ChaseCameraRig rig = rigRoot.AddComponent<ChaseCameraRig>();
            rig.SetCar(car);

            cameraRoot.transform.position = car.transform.TransformPoint(new Vector3(0f, 3.2f, -7.5f));
            cameraRoot.transform.LookAt(car.transform.position + Vector3.up);
        }

        /// <summary>
        /// Registers both maps, in a stable order: the runtime map switcher cycles through
        /// build indices, so the order here is what the Tab key walks through.
        /// </summary>
        private static void RegisterSceneInBuildSettings()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene(DescentScenePath, true),
            };
        }
    }
}
