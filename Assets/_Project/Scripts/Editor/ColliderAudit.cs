using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Finds colliders the player cannot see: geometry that blocks the car with nothing drawn
    /// where it blocks. Invisible walls are the most disorienting bug a driving game can have,
    /// and nothing else in the test suite looks for them.
    ///
    /// Two kinds are reported: a collider with no renderer at all, and a collider noticeably
    /// larger than the mesh drawn in the same place.
    /// </summary>
    public static class ColliderAudit
    {
        private const string DescentScenePath = "Assets/_Project/Scenes/Descent.unity";
        private const string DemoScenePath = "Assets/_Project/Scenes/Demo.unity";

        [MenuItem("CarDemo/Audit Colliders")]
        public static void Run()
        {
            var report = new StringBuilder();
            int problems = 0;

            problems += AuditScene(DemoScenePath, report);
            problems += AuditScene(DescentScenePath, report);

            report.AppendLine($"[COLLIDERS] RESULT: {(problems == 0 ? "PASS" : "FAIL")} ({problems} problems)");
            Debug.Log(report.ToString());

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(problems == 0 ? 0 : 2);
            }
        }

        private static int AuditScene(string scenePath, StringBuilder report)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);

            Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsSortMode.None);
            var invisible = new List<string>();
            var oversized = new List<string>();

            foreach (Collider collider in colliders)
            {
                // The car's own wheel guards are deliberately invisible; they sit inside the
                // wheels and exist only so props cannot pass through them.
                if (collider.name.StartsWith("WheelCollider")) continue;

                var renderer = collider.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    // A collider on a parent whose children are drawn is fine.
                    if (collider.GetComponentInChildren<MeshRenderer>() == null)
                    {
                        invisible.Add($"{Path(collider.transform)} ({collider.GetType().Name})");
                    }

                    continue;
                }

                Vector3 colliderSize = collider.bounds.size;
                Vector3 visualSize = renderer.bounds.size;

                // Unity gives a cylinder a CAPSULE collider — there is no cylinder collider —
                // and a capsule bulges past a short, wide cylinder at both ends. That bulge is
                // an invisible wall. Tight margin on purpose so those get reported.
                if (colliderSize.x > visualSize.x * 1.12f + 0.05f
                    || colliderSize.y > visualSize.y * 1.12f + 0.05f
                    || colliderSize.z > visualSize.z * 1.12f + 0.05f)
                {
                    oversized.Add($"{Path(collider.transform)} [{collider.GetType().Name}]: "
                                  + $"collider {colliderSize} vs mesh {visualSize}");
                }
            }

            report.AppendLine($"[COLLIDERS] {scenePath}: {colliders.Length} colliders, "
                              + $"{invisible.Count} invisible, {oversized.Count} oversized");

            foreach (string entry in invisible) report.AppendLine($"[COLLIDERS]   invisible: {entry}");
            foreach (string entry in oversized) report.AppendLine($"[COLLIDERS]   oversized: {entry}");

            return invisible.Count + oversized.Count;
        }

        private static string Path(Transform transform)
        {
            var parts = new List<string>();
            for (Transform current = transform; current != null; current = current.parent)
            {
                parts.Insert(0, current.name);
            }

            return string.Join("/", parts);
        }
    }
}
