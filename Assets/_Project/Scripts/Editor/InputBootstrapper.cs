using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Assigns the project's own actions asset as the project-wide Input Actions.
    ///
    /// Unity 6 resolves <see cref="InputSystem.actions"/> from this single assignment, so the
    /// game code never has to hold a reference to an asset or care which device is connected.
    /// </summary>
    public static class InputBootstrapper
    {
        private const string ActionsPath = "Assets/_Project/Settings/CarControls.inputactions";
        private const string TemplateActionsPath = "Assets/InputSystem_Actions.inputactions";

        private static void AddToPreloadedAssets(Object asset)
        {
            var preloaded = new System.Collections.Generic.List<Object>(PlayerSettings.GetPreloadedAssets());
            preloaded.RemoveAll(entry => entry == null);

            if (!preloaded.Contains(asset))
            {
                preloaded.Add(asset);
                PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
                Debug.Log("[CarDemo] Actions asset added to Preloaded Assets (required for builds).");
            }
        }

        [MenuItem("CarDemo/Apply Input Actions")]
        public static void Apply()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(ActionsPath);
            if (actions == null)
            {
                Debug.LogError($"[CarDemo] Actions asset not found at {ActionsPath}.");
                return;
            }

            InputSystem.actions = actions;
            EditorUtility.SetDirty(actions);

            // Assigning the project-wide asset is not enough for a build: without it in
            // Preloaded Assets, InputSystem.actions is null in the player and the car simply
            // does not respond to anything.
            AddToPreloadedAssets(actions);

            // The template asset from the project template is unused dead weight once our own
            // asset is project-wide; leaving it invites editing the wrong file.
            if (AssetDatabase.LoadAssetAtPath<InputActionAsset>(TemplateActionsPath) != null)
            {
                AssetDatabase.DeleteAsset(TemplateActionsPath);
                Debug.Log($"[CarDemo] Removed unused template actions at {TemplateActionsPath}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[CarDemo] Project-wide input actions set to {ActionsPath}");
        }
    }
}
