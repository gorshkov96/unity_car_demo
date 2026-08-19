using System.Collections.Generic;
using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Flat-shaded versions of Unity's primitive meshes, built once and shared.
    ///
    /// Unity's primitives have smoothed normals, which is what makes a cylinder look like a
    /// tube rather than a faceted prism. Low-poly style needs the opposite: every triangle
    /// lit by its own face normal, so edges read as hard creases. That requires splitting
    /// shared vertices — each triangle gets its own three — and assigning face normals.
    ///
    /// The result is cached per primitive type and assigned as sharedMesh. This matters for
    /// performance as much as for tidiness: the GPU Resident Drawer batches by mesh and
    /// material, so giving every object its own mesh copy would defeat instancing entirely.
    /// </summary>
    public static class FlatShadedMeshes
    {
        private static readonly Dictionary<PrimitiveType, Mesh> Cache = new Dictionary<PrimitiveType, Mesh>();

        /// <summary>Returns the shared flat-shaded mesh for a primitive type.</summary>
        public static Mesh Get(PrimitiveType type)
        {
            if (Cache.TryGetValue(type, out Mesh cached) && cached != null)
            {
                return cached;
            }

            Mesh source = LoadPrimitiveMesh(type);
            if (source == null) return null;

            Mesh flat = Faceted(source, $"{type}_Flat");
            Cache[type] = flat;
            return flat;
        }

        /// <summary>Replaces an object's mesh with the flat-shaded variant of the same shape.</summary>
        public static void Apply(GameObject go, PrimitiveType type)
        {
            if (go == null) return;
            if (!go.TryGetComponent(out MeshFilter filter)) return;

            Mesh flat = Get(type);
            if (flat != null) filter.sharedMesh = flat;
        }

        private static Mesh LoadPrimitiveMesh(PrimitiveType type)
        {
            // Creating a throwaway primitive is the only supported way to reach the built-in
            // meshes; it happens once per type, at generation time.
            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;

            if (Application.isPlaying) Object.Destroy(temp);
            else Object.DestroyImmediate(temp);

            return mesh;
        }

        /// <summary>Splits every triangle onto its own vertices and gives it a face normal.</summary>
        private static Mesh Faceted(Mesh source, string name)
        {
            var sourceVertices = new List<Vector3>();
            var sourceUvs = new List<Vector2>();
            source.GetVertices(sourceVertices);
            source.GetUVs(0, sourceUvs);

            int[] indices = source.triangles;
            var vertices = new Vector3[indices.Length];
            var normals = new Vector3[indices.Length];
            var uvs = new Vector2[indices.Length];
            var triangles = new int[indices.Length];

            bool hasUvs = sourceUvs.Count == sourceVertices.Count;

            for (int i = 0; i < indices.Length; i += 3)
            {
                Vector3 a = sourceVertices[indices[i]];
                Vector3 b = sourceVertices[indices[i + 1]];
                Vector3 c = sourceVertices[indices[i + 2]];

                Vector3 faceNormal = Vector3.Cross(b - a, c - a).normalized;

                for (int corner = 0; corner < 3; corner++)
                {
                    int target = i + corner;
                    vertices[target] = sourceVertices[indices[target]];
                    normals[target] = faceNormal;
                    uvs[target] = hasUvs ? sourceUvs[indices[target]] : Vector2.zero;
                    triangles[target] = target;
                }
            }

            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
