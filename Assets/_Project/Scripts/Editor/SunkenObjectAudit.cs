using System.Collections.Generic;
using System.Text;
using CarDemo.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Checks that generated props sit on the track instead of sinking into it.
    ///
    /// Objects are measured in the slope's own space, where the road surface is the plane
    /// y = 0. Every corner of every mesh is transformed into that space and the lowest one is
    /// compared against the surface: anything meaningfully below it is buried, which is both
    /// ugly and a source of invisible obstacles.
    ///
    /// Rotating a shape is the usual way this happens — tilt a slab and one edge swings under
    /// the ground — so this runs over the real generated chunks, not over authored objects.
    /// </summary>
    public static class SunkenObjectAudit
    {
        private const string DescentScenePath = "Assets/_Project/Scenes/Descent.unity";

        /// <summary>How far below the surface a mesh may reach before it counts as sunken.</summary>
        private const float AllowedDepth = 0.35f;

        /// <summary>Objects that are meant to be partly underground.</summary>
        private static readonly string[] Exempt = { "Surface", "Wall", "TopWall", "Stripe", "Chevron" };

        [MenuItem("CarDemo/Audit Sunken Objects")]
        public static void Run()
        {
            var report = new StringBuilder();
            int problems = AuditRingMap(report) + AuditDescent(report);

            report.AppendLine($"[SUNKEN] RESULT: {(problems == 0 ? "PASS" : "FAIL")}");
            Debug.Log(report.ToString());

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(problems == 0 ? 0 : 2);
            }
        }

        /// <summary>The ring map is baked into its scene, so its props are measured against world up.</summary>
        private static int AuditRingMap(StringBuilder report)
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/Demo.unity", OpenSceneMode.Single);

            var world = Object.FindFirstObjectByType<WorldBuilder>();
            if (world == null) return 0;

            var problems = new List<string>();
            int checkedCount = 0;

            foreach (MeshFilter filter in world.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || IsExempt(filter.name)) continue;
                if (filter.name.StartsWith("Ground") || filter.name.StartsWith("Road")) continue;

                checkedCount++;
                float lowest = LowestPointInSlopeSpace(filter, world.transform);
                if (lowest < -AllowedDepth)
                {
                    problems.Add($"Demo/{filter.name}: {(-lowest):0.00} m below ground");
                }
            }

            report.AppendLine($"[SUNKEN] ring map: checked {checkedCount}, {problems.Count} sunken");
            foreach (string problem in problems) report.AppendLine($"[SUNKEN]   {problem}");
            return problems.Count;
        }

        private static int AuditDescent(StringBuilder report)
        {
            EditorSceneManager.OpenScene(DescentScenePath, OpenSceneMode.Single);

            var streamer = Object.FindFirstObjectByType<DescentStreamer>();
            if (streamer == null)
            {
                report.AppendLine("[SUNKEN] descent: no DescentStreamer in the scene");
                return 1;
            }

            streamer.EnsureInitialised();
            streamer.PreloadAroundStart();

            Transform slope = streamer.SlopeRoot;
            var problems = new List<string>();
            int checkedCount = 0;

            foreach (MeshFilter filter in slope.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null) continue;
                if (IsExempt(filter.name)) continue;

                checkedCount++;
                float lowest = LowestPointInSlopeSpace(filter, slope);
                float height = filter.GetComponent<Renderer>() != null
                    ? filter.GetComponent<Renderer>().bounds.size.y
                    : 1f;

                if (lowest >= -AllowedDepth) continue;

                problems.Add($"Descent/{filter.name}: {(-lowest):0.00} m below the surface "
                             + $"(object is {height:0.00} m tall)");
            }

            report.AppendLine($"[SUNKEN] descent: checked {checkedCount} generated meshes, "
                              + $"{problems.Count} sunken (limit {AllowedDepth:0.00} m)");
            foreach (string problem in problems) report.AppendLine($"[SUNKEN]   {problem}");
            return problems.Count;
        }

        private static bool IsExempt(string name)
        {
            foreach (string exempt in Exempt)
            {
                if (name.StartsWith(exempt)) return true;
            }

            return false;
        }

        /// <summary>
        /// Lowest corner of a mesh expressed in slope space, where the road is y = 0.
        /// All eight corners are tested because rotation is exactly what pushes one under.
        /// </summary>
        private static float LowestPointInSlopeSpace(MeshFilter filter, Transform slope)
        {
            Bounds local = filter.sharedMesh.bounds;
            float lowest = float.MaxValue;

            for (int corner = 0; corner < 8; corner++)
            {
                var point = new Vector3(
                    (corner & 1) == 0 ? local.min.x : local.max.x,
                    (corner & 2) == 0 ? local.min.y : local.max.y,
                    (corner & 4) == 0 ? local.min.z : local.max.z);

                Vector3 world = filter.transform.TransformPoint(point);
                lowest = Mathf.Min(lowest, slope.InverseTransformPoint(world).y);
            }

            return lowest;
        }
    }
}
