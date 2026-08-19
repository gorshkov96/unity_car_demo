using System.Collections.Generic;
using System.Text;
using CarDemo.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Verifies that what the player sees is what the car hits.
    ///
    /// Three failures are checked, all of which look the same from the driver's seat — running
    /// into nothing:
    ///   1. a collider with no visible mesh anywhere near it;
    ///   2. a collider noticeably larger than the mesh drawn in its place;
    ///   3. geometry sunk below the road surface, so its visible part misrepresents its solid part.
    ///
    /// Critically, this runs against the streamed chunks, built the same way the game builds
    /// them. An earlier version of this check inspected the saved scene file and passed
    /// happily — the saved scene contains five colliders, because the entire track is created
    /// at runtime.
    /// </summary>
    public static class ShapeAudit
    {
        private const string DescentScenePath = "Assets/_Project/Scenes/Descent.unity";
        private const string DemoScenePath = "Assets/_Project/Scenes/Demo.unity";
        private const float AllowedDepth = 0.3f;
        private const float SizeTolerance = 1.15f;

        /// <summary>
        /// Parts that are meant to reach below the surface, or to be scenery only.
        ///
        /// "Peak" is the distant mountain geometry: it stands behind a solid barrier the car
        /// cannot pass, its base is deliberately buried so no gap shows at the horizon, and it
        /// carries no collider because nothing can ever reach it. Excluding it is a decision,
        /// not an oversight — everything inside the barrier is still checked.
        /// </summary>
        private static readonly string[] MayBeUnderground =
        {
            "Surface", "Wall", "TopWall", "Ground", "Peak",
        };

        /// <summary>Flat markings and trim: drawn, deliberately not solid, and obviously so.</summary>
        private static readonly string[] Decoration =
        {
            "Stripe", "Chevron", "Band", "Corner", "Brace", "Collar", "Cap", "Post", "Marker",
            "Arch", "Mirror", "Door", "Vent", "Scoop", "Skirt", "Spoiler", "Headlight",
            "Taillight", "Exhaust", "Nose", "Rim", "Spoke", "Tyre", "Glass", "Bumper",
            "Body", "Cabin", "Roof", "Window", "Bonnet", "Boot", "Wheel",
            "Peak", "Greenhouse", "Pillar", "Waist", "Frame", "Rib",
        };

        [MenuItem("CarDemo/Audit Shapes")]
        public static void Run()
        {
            var report = new StringBuilder();
            int problems = 0;

            problems += AuditDescent(report);
            problems += AuditRingMap(report);

            report.AppendLine($"[SHAPES] RESULT: {(problems == 0 ? "PASS" : "FAIL")} ({problems} problems)");
            Debug.Log(report.ToString());

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(problems == 0 ? 0 : 2);
            }
        }

        private static int AuditDescent(StringBuilder report)
        {
            EditorSceneManager.OpenScene(DescentScenePath, OpenSceneMode.Single);

            var streamer = Object.FindFirstObjectByType<DescentStreamer>();
            if (streamer == null)
            {
                report.AppendLine("[SHAPES] descent: no streamer");
                return 1;
            }

            // Build the real thing: the chunks the game streams, not what the scene happens
            // to have saved.
            streamer.EnsureInitialised();
            streamer.PreloadAroundStart();

            return Audit("descent", streamer.SlopeRoot, streamer.SlopeRoot, report);
        }

        private static int AuditRingMap(StringBuilder report)
        {
            EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Single);

            var world = Object.FindFirstObjectByType<WorldBuilder>();
            if (world == null)
            {
                report.AppendLine("[SHAPES] ring map: no world builder");
                return 1;
            }

            return Audit("ring map", world.transform, world.transform, report);
        }

        private static int Audit(string label, Transform root, Transform surfaceSpace, StringBuilder report)
        {
            var problems = new List<string>();
            int colliders = 0;

            foreach (Collider collider in root.GetComponentsInChildren<Collider>())
            {
                colliders++;

                var renderer = collider.GetComponent<MeshRenderer>();
                if (renderer == null)
                {
                    if (collider.GetComponentInChildren<MeshRenderer>() == null)
                    {
                        problems.Add($"{Path(collider.transform)}: solid but invisible "
                                     + $"({collider.GetType().Name})");
                    }

                    continue;
                }

                // Shape mismatch: the collider blocks more space than the mesh occupies.
                Vector3 solid = collider.bounds.size;
                Vector3 visible = renderer.bounds.size;
                if (solid.x > visible.x * SizeTolerance + 0.1f
                    || solid.y > visible.y * SizeTolerance + 0.1f
                    || solid.z > visible.z * SizeTolerance + 0.1f)
                {
                    problems.Add($"{Path(collider.transform)}: collider {Round(solid)} "
                                 + $"larger than mesh {Round(visible)} [{collider.GetType().Name}]");
                }
            }

            int meshes = 0;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                if (filter.sharedMesh == null || IsExempt(filter.name)) continue;

                meshes++;
                float lowest = Lowest(filter, surfaceSpace);
                if (lowest < -AllowedDepth)
                {
                    problems.Add($"{Path(filter.transform)}: sunk {(-lowest):0.00} m below the surface");
                }

                // The other half of "what you see is what you hit": something drawn as a solid
                // object with no collider is a ghost the car drives through, which reads as a
                // broken world just as much as an invisible wall does.
                if (IsDecoration(filter.name)) continue;

                if (filter.GetComponent<Collider>() == null && filter.GetComponentInParent<Collider>() == null)
                {
                    Bounds bounds = filter.sharedMesh.bounds;
                    Vector3 size = Vector3.Scale(bounds.size, filter.transform.lossyScale);

                    // Only report things big enough to look solid. Thin trim is clearly detail.
                    if (size.x > 0.4f && size.y > 0.4f && size.z > 0.4f)
                    {
                        problems.Add($"{Path(filter.transform)}: looks solid ({Round(size)}) "
                                     + "but has no collider");
                    }
                }
            }

            report.AppendLine($"[SHAPES] {label}: {colliders} colliders, {meshes} meshes, "
                              + $"{problems.Count} problems");
            foreach (string problem in problems) report.AppendLine($"[SHAPES]   {problem}");
            return problems.Count;
        }

        private static bool IsDecoration(string name)
        {
            foreach (string decoration in Decoration)
            {
                if (name.StartsWith(decoration)) return true;
            }

            return false;
        }

        private static bool IsExempt(string name)
        {
            foreach (string exempt in MayBeUnderground)
            {
                if (name.StartsWith(exempt)) return true;
            }

            return false;
        }

        /// <summary>Lowest mesh corner in surface space, where the road is the plane y = 0.</summary>
        private static float Lowest(MeshFilter filter, Transform surfaceSpace)
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
                lowest = Mathf.Min(lowest, surfaceSpace.InverseTransformPoint(world).y);
            }

            return lowest;
        }

        private static string Round(Vector3 value) =>
            $"({value.x:0.00}, {value.y:0.00}, {value.z:0.00})";

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
