using UnityEditor;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Applies the project settings required by docs/unity/06-performance.md and
    /// docs/unity/01-render-urp.md.
    ///
    /// These settings live in ProjectSettings/*.asset, which are opaque YAML singletons — this
    /// script is the reviewable, re-runnable record of what is set and why.
    /// </summary>
    public static class ProjectSettingsBootstrapper
    {
        [MenuItem("CarDemo/Apply Project Settings")]
        public static void Apply()
        {
            ApplyGraphicsSettings();
            ApplyPlayerSettings();
            ApplyPhysicsSettings();
            ApplyTimeSettings();
            ApplyQualitySettings();

            AssetDatabase.SaveAssets();
            Debug.Log("[CarDemo] Project settings applied.");
        }

        private static void ApplyGraphicsSettings()
        {
            SerializedObject settings = LoadSettings("ProjectSettings/GraphicsSettings.asset");
            if (settings == null) return;

            // Without Keep All, BatchRendererGroup shader variants are stripped from the build
            // and the GPU Resident Drawer silently falls back to the normal draw path.
            // 0 = Keep If Entities Graphics Package Is Installed, 2 = Keep All.
            SetInt(settings, "m_BrgStripping", 2);

            // Shadow culling relative to the camera avoids precision loss (flickering shadows)
            // once the car drives far from the world origin.
            SetBool(settings, "m_CameraRelativeShadowCulling", true);
            SetBool(settings, "m_CameraRelativeLightCulling", true);

            Commit(settings);
        }

        private static void ApplyPlayerSettings()
        {
            SerializedObject settings = LoadSettings("ProjectSettings/ProjectSettings.asset");
            if (settings == null) return;

            DisableStaticBatching();

            // Needed for the frame-time overlay to work in non-development builds.
            SetBool(settings, "enableFrameTimingStats", true);
            SetBool(settings, "PreloadedAssets", false, optional: true);

            Commit(settings);
        }

        /// <summary>
        /// Turns off Static Batching for every build target.
        ///
        /// Uses the internal PlayerSettings API through reflection: the public API has no
        /// setter, and the SerializedProperty for m_BuildTargetBatching is rebuilt from
        /// native state on save, so writing it directly does not stick.
        /// </summary>
        private static void DisableStaticBatching()
        {
            System.Reflection.MethodInfo method = typeof(PlayerSettings).GetMethod(
                "SetBatchingForPlatform",
                System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Public);

            if (method == null)
            {
                Debug.LogWarning("[CarDemo] PlayerSettings.SetBatchingForPlatform not available — "
                                 + "turn Static Batching off manually in Player Settings.");
                return;
            }

            foreach (BuildTarget target in new[] { BuildTarget.StandaloneOSX, BuildTarget.StandaloneWindows64, BuildTarget.iOS, BuildTarget.Android })
            {
                try
                {
                    method.Invoke(null, new object[] { target, 0, 0 });
                }
                catch (System.Exception exception)
                {
                    Debug.LogWarning($"[CarDemo] Could not set batching for {target}: {exception.Message}");
                }
            }

            Debug.Log("[CarDemo] Static Batching disabled (conflicts with GPU Resident Drawer).");
        }

        private static void ApplyPhysicsSettings()
        {
            SerializedObject settings = LoadSettings("ProjectSettings/DynamicsManager.asset");
            if (settings == null) return;

            // Sweep and Prune (0), deliberately, against the general advice in
            // docs/unity/06-performance.md to prefer Automatic Box Pruning on flat maps.
            //
            // That advice assumes a bounded map. Box Pruning only simulates inside
            // m_WorldBounds — a fixed box, 250 m by default — and silently ignores everything
            // outside it: no collisions at all, at any speed. The descent runs for kilometres
            // and drops hundreds of metres, so most of it lives outside any box we could set.
            // Sweep and Prune has no such limit.
            SetInt(settings, "m_BroadphaseType", 0);

            // Kept generous anyway, so the setting is harmless if the broadphase ever changes.
            SerializedProperty bounds = settings.FindProperty("m_WorldBounds");
            if (bounds != null)
            {
                SerializedProperty extent = bounds.FindPropertyRelative("m_Extent");
                if (extent != null) extent.vector3Value = new Vector3(20000f, 20000f, 20000f);
            }

            // Without this every OnCollisionStay allocates a Collision object.
            SetBool(settings, "m_ReuseCollisionCallbacks", true);

            // How fast the engine is allowed to push two overlapping bodies apart. The default
            // of 10 m/s is a catapult: a car that ends up inside a barrier at speed is fired
            // out of it, often downwards through the track. Capping it turns an ejection into
            // a firm nudge.
            SetFloat(settings, "m_DefaultMaxDepenetrationVelocity", 2f);

            // Contacts are generated slightly before surfaces touch, which gives the solver a
            // step to react instead of discovering a deep overlap after the fact.
            SetFloat(settings, "m_DefaultContactOffset", 0.02f);

            // Velocity iterations at the default of 1 leave high-speed impacts unresolved.
            SetInt(settings, "m_DefaultSolverVelocityIterations", 4);

            Commit(settings);
        }

        /// <summary>
        /// Halves the physics step to 100 Hz.
        ///
        /// The step length is what decides how far an object jumps between collision checks:
        /// at 170 km/h a 0.02 s step moves the car 0.94 m, which is more than the thickness of
        /// most things it can hit. At 0.01 s it is 0.47 m. The cost is twice as many physics
        /// steps, which this project can afford — the CPU sits at ~4 ms of a 16.6 ms budget.
        /// </summary>
        private static void ApplyTimeSettings()
        {
            // Set through the API, not the serialized field: in Unity 6 "Fixed Timestep" is
            // stored as a rational number (count over rate), so writing a float into it does
            // nothing. Time.fixedDeltaTime is the supported way in and persists to the asset.
            Time.fixedDeltaTime = 0.01f;
            Time.maximumDeltaTime = 0.1f;

            SerializedObject settings = LoadSettings("ProjectSettings/TimeManager.asset");
            if (settings != null)
            {
                SetFloat(settings, "Maximum Allowed Timestep", 0.1f);
                Commit(settings);
            }

            Debug.Log($"[CarDemo] Physics step set to {Time.fixedDeltaTime * 1000f:0.#} ms "
                      + $"({1f / Time.fixedDeltaTime:0} Hz)");
        }

        private static void ApplyQualitySettings()
        {
            SerializedObject settings = LoadSettings("ProjectSettings/QualitySettings.asset");
            if (settings == null) return;

            // The docs are explicit: cap the frame rate with vSyncCount on desktop, not with
            // Application.targetFrameRate, which introduces micro-stutter.
            SerializedProperty levels = settings.FindProperty("m_QualitySettings");
            if (levels != null && levels.isArray)
            {
                for (int i = 0; i < levels.arraySize; i++)
                {
                    SerializedProperty level = levels.GetArrayElementAtIndex(i);
                    SerializedProperty vSync = level.FindPropertyRelative("vSyncCount");
                    if (vSync != null) vSync.intValue = 1;
                }
            }

            Commit(settings);
        }

        private static SerializedObject LoadSettings(string path)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(path);
            if (assets == null || assets.Length == 0 || assets[0] == null)
            {
                Debug.LogError($"[CarDemo] Could not load settings asset: {path}");
                return null;
            }

            return new SerializedObject(assets[0]);
        }

        /// <summary>
        /// Writes a settings singleton back to disk. ProjectSettings assets are not covered by
        /// AssetDatabase.SaveAssets, so they need an explicit dirty flag plus a settings save.
        /// </summary>
        private static void Commit(SerializedObject settings)
        {
            settings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings.targetObject);
            AssetDatabase.SaveAssetIfDirty(settings.targetObject);
        }

        private static void SetFloat(SerializedObject settings, string property, float value)
        {
            SerializedProperty found = settings.FindProperty(property);
            if (found == null)
            {
                Debug.LogWarning($"[CarDemo] Setting '{property}' not found — skipped.");
                return;
            }

            found.floatValue = value;
        }

        private static void SetInt(SerializedObject settings, string property, int value)
        {
            SerializedProperty found = settings.FindProperty(property);
            if (found == null)
            {
                Debug.LogWarning($"[CarDemo] Setting '{property}' not found — skipped.");
                return;
            }

            found.intValue = value;
        }

        private static void SetBool(SerializedObject settings, string property, bool value, bool optional = false)
        {
            SerializedProperty found = settings.FindProperty(property);
            if (found == null)
            {
                if (!optional) Debug.LogWarning($"[CarDemo] Setting '{property}' not found — skipped.");
                return;
            }

            if (found.propertyType == SerializedPropertyType.Boolean) found.boolValue = value;
            else if (found.propertyType == SerializedPropertyType.Integer) found.intValue = value ? 1 : 0;
        }
    }
}
