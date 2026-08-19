using CarDemo.Core;
using UnityEditor;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Creates the project's layers in TagManager.asset from code, so the scene generator
    /// can rely on them existing rather than asking a human to add them by hand.
    /// </summary>
    public static class LayerBootstrapper
    {
        private static readonly string[] RequiredLayers =
        {
            GameLayers.GroundName,
            GameLayers.VehicleName,
            GameLayers.PropName,
            GameLayers.CameraObstacleName,
        };

        [MenuItem("CarDemo/Apply Layers")]
        public static void Apply()
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0)
            {
                Debug.LogError("[CarDemo] TagManager.asset not found.");
                return;
            }

            var tagManager = new SerializedObject(assets[0]);
            SerializedProperty layers = tagManager.FindProperty("layers");
            if (layers == null)
            {
                Debug.LogError("[CarDemo] 'layers' property not found in TagManager.");
                return;
            }

            foreach (string layerName in RequiredLayers)
            {
                if (!TryAddLayer(layers, layerName))
                {
                    Debug.LogError($"[CarDemo] No free user layer slot for '{layerName}'.");
                }
            }

            tagManager.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(assets[0]);
            AssetDatabase.SaveAssetIfDirty(assets[0]);
            Debug.Log("[CarDemo] Layers applied.");
        }

        private static bool TryAddLayer(SerializedProperty layers, string layerName)
        {
            // Slots 0-7 are reserved by Unity; user layers start at 8.
            for (int i = 0; i < layers.arraySize; i++)
            {
                if (layers.GetArrayElementAtIndex(i).stringValue == layerName) return true;
            }

            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty slot = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(slot.stringValue)) continue;

                slot.stringValue = layerName;
                return true;
            }

            return false;
        }
    }
}
