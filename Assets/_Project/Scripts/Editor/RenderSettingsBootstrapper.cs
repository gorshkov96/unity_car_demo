using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Applies the project's rendering rules to the URP assets from code.
    ///
    /// These live in .asset YAML that cannot be reviewed in a diff, so the settings are
    /// expressed here instead: what is set, and why, in one readable place that can be re-run.
    /// Rules come from docs/unity/01-render-urp.md and docs/unity/06-performance.md.
    /// </summary>
    public static class RenderSettingsBootstrapper
    {
        [MenuItem("CarDemo/Apply Render Settings")]
        public static void Apply()
        {
            string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
            if (guids.Length == 0)
            {
                Debug.LogError("[CarDemo] No UniversalRenderPipelineAsset found.");
                return;
            }

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (asset == null) continue;

                ConfigureAsset(asset, path);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[CarDemo] Render settings applied.");
        }

        private static void ConfigureAsset(UniversalRenderPipelineAsset asset, string path)
        {
            var serialized = new SerializedObject(asset);

            // GPU Resident Drawer: moves per-object culling and draw submission to the GPU.
            // Requires Forward+ (already set on the renderer) and rules out Static Batching
            // and MaterialPropertyBlock, both of which this project avoids.
            SetInt(serialized, "m_GPUResidentDrawerMode", 1); // InstancedDrawing
            SetBool(serialized, "m_GPUResidentDrawerEnableOcclusionCullingInCameras", true);
            SetFloat(serialized, "m_SmallMeshScreenPercentage", 0f);

            // SRP Batcher stays on: it is what keeps the per-draw CPU cost low.
            SetBool(serialized, "m_UseSRPBatcher", true);

            // Shadow settings come straight from docs/unity/02-lighting-shadows.md: 60-90 m
            // distance, four cascades, 2048-4096 shadow map, Medium soft shadows.
            SetFloat(serialized, "m_ShadowDistance", 70f);
            SetInt(serialized, "m_ShadowCascadeCount", 4);
            SetInt(serialized, "m_MainLightShadowmapResolution", 2048);
            SetBool(serialized, "m_ConservativeEnclosingSphere", true);

            // Soft shadows: Medium, per the same doc. High costs more sampling for a difference
            // that is invisible at this shadow distance.
            SetInt(serialized, "m_SoftShadowQuality", 2);

            // Depth on (SSAO, decals, soft particles later), Opaque off until glass or water
            // actually needs it.
            SetBool(serialized, "m_RequireDepthTexture", true);
            SetBool(serialized, "m_RequireOpaqueTexture", false);

            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            Debug.Log($"[CarDemo] Configured URP asset: {path}");
            ConfigureRendererFeatures(asset);
        }

        /// <summary>
        /// Tunes the SSAO renderer feature. Defaults render it at full resolution with the
        /// most expensive source and a 100 m falloff; the project doc asks for Downsample on,
        /// Medium samples and blur, and a 30-50 m falloff.
        /// </summary>
        private static void ConfigureRendererFeatures(UniversalRenderPipelineAsset asset)
        {
            var rendererData = new SerializedObject(asset).FindProperty("m_RendererDataList");
            if (rendererData == null) return;

            for (int i = 0; i < rendererData.arraySize; i++)
            {
                var data = rendererData.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data == null) continue;

                foreach (ScriptableRendererFeature feature in data.rendererFeatures)
                {
                    if (feature == null || feature.GetType().Name != "ScreenSpaceAmbientOcclusion") continue;

                    var featureObject = new SerializedObject(feature);
                    SerializedProperty settings = featureObject.FindProperty("m_Settings");
                    if (settings == null) continue;

                    SetRelativeBool(settings, "Downsample", true);
                    SetRelativeInt(settings, "Samples", 1);      // Medium
                    SetRelativeInt(settings, "BlurQuality", 1);  // Medium
                    SetRelativeFloat(settings, "Falloff", 40f);

                    featureObject.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(feature);
                    Debug.Log($"[CarDemo] Tuned SSAO on {data.name}");
                }
            }
        }

        private static void SetRelativeBool(SerializedProperty parent, string name, bool value)
        {
            SerializedProperty found = parent.FindPropertyRelative(name);
            if (found == null) return;
            if (found.propertyType == SerializedPropertyType.Boolean) found.boolValue = value;
            else found.intValue = value ? 1 : 0;
        }

        private static void SetRelativeInt(SerializedProperty parent, string name, int value)
        {
            SerializedProperty found = parent.FindPropertyRelative(name);
            if (found != null) found.intValue = value;
        }

        private static void SetRelativeFloat(SerializedProperty parent, string name, float value)
        {
            SerializedProperty found = parent.FindPropertyRelative(name);
            if (found != null) found.floatValue = value;
        }

        private static void SetInt(SerializedObject serialized, string property, int value)
        {
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                Debug.LogWarning($"[CarDemo] URP property '{property}' not found — skipped.");
                return;
            }

            found.intValue = value;
        }

        private static void SetBool(SerializedObject serialized, string property, bool value)
        {
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                Debug.LogWarning($"[CarDemo] URP property '{property}' not found — skipped.");
                return;
            }

            found.boolValue = value;
        }

        private static void SetFloat(SerializedObject serialized, string property, float value)
        {
            SerializedProperty found = serialized.FindProperty(property);
            if (found == null)
            {
                Debug.LogWarning($"[CarDemo] URP property '{property}' not found — skipped.");
                return;
            }

            found.floatValue = value;
        }
    }
}
