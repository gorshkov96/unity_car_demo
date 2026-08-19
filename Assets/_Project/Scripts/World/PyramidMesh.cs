using System.Collections.Generic;
using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Procedural low-poly peaks: a cone approximated by a handful of flat facets.
    ///
    /// Unity has no cone primitive, and a cylinder makes a drum, not a mountain. A four to
    /// six sided pyramid is the standard low-poly hill: few triangles, hard edges, and a
    /// silhouette that reads at any distance.
    /// </summary>
    public static class PyramidMesh
    {
        private static readonly Dictionary<int, Mesh> Cache = new Dictionary<int, Mesh>();

        /// <summary>Unit pyramid: 1 across, 1 tall, base centred on the origin.</summary>
        public static Mesh Get(int sides)
        {
            sides = Mathf.Clamp(sides, 3, 8);
            if (Cache.TryGetValue(sides, out Mesh cached) && cached != null) return cached;

            Mesh mesh = Build(sides);
            Cache[sides] = mesh;
            return mesh;
        }

        public static GameObject Create(string name, Transform parent, Vector3 localPosition,
            Vector3 size, Quaternion localRotation, Material material, int sides = 5)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            go.transform.localScale = size;

            go.AddComponent<MeshFilter>().sharedMesh = Get(sides);
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;

            return go;
        }

        private static Mesh Build(int sides)
        {
            var apex = new Vector3(0f, 1f, 0f);
            var rim = new Vector3[sides];

            for (int i = 0; i < sides; i++)
            {
                float angle = i / (float)sides * Mathf.PI * 2f;
                rim[i] = new Vector3(Mathf.Cos(angle) * 0.5f, 0f, Mathf.Sin(angle) * 0.5f);
            }

            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var indices = new List<int>();

            void AddTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                foreach (Vector3 vertex in new[] { a, b, c })
                {
                    indices.Add(vertices.Count);
                    vertices.Add(vertex);
                    normals.Add(normal);
                }
            }

            for (int i = 0; i < sides; i++)
            {
                Vector3 current = rim[i];
                Vector3 next = rim[(i + 1) % sides];

                // Side face, wound so its normal points away from the axis.
                AddTriangle(current, apex, next);

                // Base fan, wound the other way so it faces down.
                if (i >= 1 && i < sides - 1)
                {
                    AddTriangle(rim[0], next, current);
                }
            }

            var mesh = new Mesh { name = $"Pyramid_{sides}" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
