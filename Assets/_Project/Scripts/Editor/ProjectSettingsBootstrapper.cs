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

            // Sweep and Prune produces many false positives on flat worlds with many colliders,
            // and our map is exactly that: a 240x240 m plate. 1 = Automatic Box Pruning.
            SetInt(settings, "m_BroadphaseType", 1);

            // Without this every OnCollisionStay allocates a Collision object.
            SetBool(settings, "m_ReuseCollisionCallbacks", true);

            Commit(settings);
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
