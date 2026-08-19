using CarDemo.Core;
using CarDemo.Diagnostics;
using CarDemo.Game;
using CarDemo.UI;
using CarDemo.Vehicle;
using CarDemo.World;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Generates the descent scene: a long tilted track streamed in chunks, with the car
    /// starting at the top. Nothing is placed by hand — the scene holds a streamer and a car,
    /// and the track appears as the car rolls into it.
    /// </summary>
    public static class DescentSceneBootstrapper
    {
        private const string ProjectRoot = "Assets/_Project";
        private const string SettingsFolder = ProjectRoot + "/Settings";
        private const string MaterialsFolder = ProjectRoot + "/Materials";
        private const string ScenePath = ProjectRoot + "/Scenes/Descent.unity";
        private const string BenchmarkScenePath = ProjectRoot + "/Scenes/DescentBenchmark.unity";
        private const string DescentConfigPath = SettingsFolder + "/DescentConfig.asset";
        private const string CarConfigPath = SettingsFolder + "/CarConfig.asset";

        [MenuItem("CarDemo/Rebuild Descent Scene")]
        public static void RebuildDescentScene()
        {
            Build(autoDrive: false, ScenePath);
        }

        /// <summary>Descent that drives itself and measures the frame budget, then quits.</summary>
        [MenuItem("CarDemo/Rebuild Descent Benchmark")]
        public static void RebuildDescentBenchmark()
        {
            Build(autoDrive: true, BenchmarkScenePath);
        }

        public static void BuildFromCommandLine() => RebuildDescentScene();

        private static void Build(bool autoDrive, string scenePath)
        {
            LayerBootstrapper.Apply();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var descentConfig = CreateConfig<DescentConfig>(DescentConfigPath);
            var carConfig = AssetDatabase.LoadAssetAtPath<CarConfig>(CarConfigPath);
            if (descentConfig == null || carConfig == null)
            {
                Debug.LogError("[CarDemo] Descent needs both DescentConfig and CarConfig; "
                               + "run CarDemo > Rebuild Demo Scene first to create the car config.");
                return;
            }

            DescentMaterials materials = LoadMaterials();
            CreateLighting();

            var streamerRoot = new GameObject("Descent");
            DescentStreamer streamer = streamerRoot.AddComponent<DescentStreamer>();

            streamer.Configure(descentConfig, tracked: null, materials);
            streamer.EnsureInitialised();
            streamer.GetSpawn(out Vector3 spawnPosition, out Quaternion spawnRotation);

            CarRigFactory.Rig rig = CarRigFactory.Build(carConfig, spawnPosition, spawnRotation);
            CarController car = rig.Root.AddComponent<CarController>();
            car.Configure(carConfig);
            GameLayers.ApplyRecursively(rig.Root, GameLayers.Vehicle);

            CarWheelVisuals visuals = rig.Root.AddComponent<CarWheelVisuals>();
            visuals.SetWheelTransforms(rig.Wheels);
            rig.Root.AddComponent<CarResetter>();

            if (autoDrive)
            {
                DescentAutoDriver driver = rig.Root.AddComponent<DescentAutoDriver>();
                driver.Configure(streamer.SlopeRoot, descentConfig.HalfWidth * 0.8f, GameLayers.DrivableMask);
            }
            else
            {
                rig.Root.AddComponent<PlayerCarInput>();
            }

            streamer.SetTracked(rig.Root.transform);

            // Deliberately not preloading chunks here. They would be saved into the scene, and
            // the streamer rebuilds them at runtime anyway — the saved copy is pure duplicate
            // geometry. The Slope root itself stays, because the auto driver holds a
            // serialised reference to it.

            TrackGuard guard = streamerRoot.AddComponent<TrackGuard>();
            guard.Configure(streamer, rig.Body);

            // Counts real contacts and catches anything the car passes through without
            // touching — the failure the player sees as "objects go through me".
            CollisionAuditor auditor = rig.Root.AddComponent<CollisionAuditor>();
            auditor.Configure(LayerMask.GetMask(GameLayers.PropName), new Vector3(1.1f, 0.6f, 2.3f));

            CreateCamera(car);
            PerformanceProbe probe = CreateHud(car, autoDrive);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, scenePath);
            AssetDatabase.SaveAssets();
            string trackLength = descentConfig.IsEndless
                ? "endless"
                : $"{descentConfig.TotalLength:0} m / {descentConfig.TotalChunks} chunks";
            Debug.Log($"[CarDemo] Descent scene written to {scenePath} (track {trackLength}, "
                      + $"{probe.BudgetMilliseconds:0.0} ms budget)");
        }

        private static DescentMaterials LoadMaterials()
        {
            // Low-poly palette: flat, saturated, low smoothness. The docs warn against mixing
            // stylistic sources, so nothing here is metallic or glossy — the read comes from
            // faceted geometry and colour contrast, not from material detail.
            return new DescentMaterials(
                LoadOrCreateMaterial("DescentSurface", Hex("3B4A63"), 0.08f),
                LoadOrCreateMaterial("DescentStripe", Hex("F2F5FA"), 0.15f),
                LoadOrCreateMaterial("DescentWall", Hex("2A3348"), 0.05f),
                LoadOrCreateMaterial("DescentJump", Hex("F5A623"), 0.12f),
                LoadOrCreateMaterial("DescentObstacle", Hex("7A88A6"), 0.1f),
                LoadOrCreateMaterial("DescentAccent", Hex("2E3A55"), 0.1f),
                LoadOrCreateMaterial("DescentMoving", Hex("35C4D6"), 0.2f),
                LoadOrCreateMaterial("DescentCrate", Hex("C97B3C"), 0.08f),
                LoadOrCreateMaterial("DescentHazard", Hex("FF4D4D"), 0.15f));
        }

        /// <summary>Creates the config from code defaults, replacing any stale asset.</summary>
        private static T CreateConfig<T>(string path) where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>();
            if (AssetDatabase.LoadAssetAtPath<T>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        /// <summary>Parses a hex colour so the palette reads the way a designer writes it.</summary>
        private static Color Hex(string hex)
        {
            return ColorUtility.TryParseHtmlString("#" + hex, out Color color) ? color : Color.magenta;
        }

        private static Material LoadOrCreateMaterial(string name, Color color, float smoothness)
        {
            string path = $"{MaterialsFolder}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;

            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader) { name = name };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", smoothness);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// Builds a gradient sky.
        ///
        /// Without one the camera falls back to the default background, which renders as a
        /// flat brown wall above the horizon — the single ugliest thing in the frame. The
        /// procedural skybox costs nothing and gives the flat-shaded scenery something to sit
        /// against.
        /// </summary>
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
            sky.SetFloat("_SunSizeConvergence", 3f);
            sky.SetFloat("_AtmosphereThickness", 0.75f);
            sky.SetColor("_SkyTint", Hex("6FA8DC"));
            sky.SetColor("_GroundColor", Hex("2A3348"));
            sky.SetFloat("_Exposure", 1.15f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
        }

        private static void CreateLighting()
        {
            CreateSky();

            var sun = new GameObject("Directional Light");
            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Hex("FFF0D6");
            light.intensity = 1.7f;
            light.shadows = LightShadows.Soft;
            // Low sun: long shadows give flat-shaded geometry its shape.
            sun.transform.rotation = Quaternion.Euler(34f, -35f, 0f);

            // Gradient ambient is the cheapest strong stylisation lever the docs name, and
            // three flat colours suit a low-poly look better than a skybox probe.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = Hex("9CC3E8");
            RenderSettings.ambientEquatorColor = Hex("6E7E99");
            RenderSettings.ambientGroundColor = Hex("2E3446");
            // Fog is thin on purpose. At 0.0045 an obstacle 200 m out is already invisible,
            // and at 150 km/h that is four seconds of warning — the player runs into things
            // that were never shown to them. It now reads as depth haze, not as a wall.
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = Hex("8FA6C4");
            RenderSettings.fogDensity = 0.0016f;
        }

        private static void CreateCamera(CarController car)
        {
            var cameraRoot = new GameObject("Main Camera");
            cameraRoot.tag = "MainCamera";
            Camera camera = cameraRoot.AddComponent<Camera>();
            camera.fieldOfView = 66f;
            camera.farClipPlane = 700f;
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
            virtualCamera.Lens.FieldOfView = 66f;

            CinemachineOrbitalFollow follow = rigRoot.AddComponent<CinemachineOrbitalFollow>();
            follow.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
            follow.Radius = 8.5f;
            follow.TargetOffset = new Vector3(0f, 1.2f, 0f);
            // Recentre behind the tracking target, which is the travel-aligned proxy rather
            // than the car body — so a roll or a spin never drags the camera around with it.
            follow.RecenteringTarget = CinemachineOrbitalFollow.ReferenceFrames.TrackingTarget;
            follow.HorizontalAxis.Recentering.Enabled = true;
            follow.HorizontalAxis.Recentering.Wait = 0.3f;
            follow.HorizontalAxis.Recentering.Time = 1f;
            follow.VerticalAxis.Value = 12f;
            follow.VerticalAxis.Center = 12f;
            follow.TrackerSettings.PositionDamping = new Vector3(0.4f, 0.4f, 0.8f);
            follow.TrackerSettings.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.LockToTargetWithWorldUp;

            CinemachineRotationComposer composer = rigRoot.AddComponent<CinemachineRotationComposer>();
            composer.TargetOffset = new Vector3(0f, 1.4f, 0f);
            composer.Damping = new Vector2(0.4f, 0.4f);

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

            cameraRoot.transform.position = car.transform.TransformPoint(new Vector3(0f, 3.4f, -8.5f));
            cameraRoot.transform.LookAt(car.transform.position + Vector3.up);
        }

        private static PerformanceProbe CreateHud(CarController car, bool benchmark)
        {
            var hudRoot = new GameObject("HUD");
            UIDocument document = hudRoot.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(
                SettingsFolder + "/CarDemoPanelSettings.asset");

            PerformanceProbe probe = hudRoot.AddComponent<PerformanceProbe>();
            probe.Configure(
                targetFrameRate: 60f,
                reportInterval: benchmark ? 15f : 0f,
                autoQuitAfter: benchmark ? 330f : 0f);

            DriverHud hud = hudRoot.AddComponent<DriverHud>();
            hud.SetCar(car);
            hud.SetProbe(probe);

            if (!benchmark)
            {
                hudRoot.AddComponent<SceneSwitcher>();
                hudRoot.AddComponent<MapMenu>();
            }
            return probe;
        }
    }
}
