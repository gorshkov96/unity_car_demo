using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Procedural wedge — a triangular prism — used for ramps.
    ///
    /// Unity has no wedge primitive, and a tilted box is not a substitute: its lower edge
    /// buries itself in the ground while its upper edge sticks out as a step, and a car
    /// arriving at speed trips over that step instead of driving up the slope.
    ///
    /// The mesh is unit-sized (1 x 1 x 1) and scaled per ramp, so every ramp in the game
    /// shares one mesh and the GPU can still instance them. Faces carry flat normals to match
    /// the project's low-poly look.
    /// </summary>
    public static class WedgeMesh
    {
        private static Mesh _shared;
        private static readonly System.Collections.Generic.Dictionary<int, Mesh> BankedCache =
            new System.Collections.Generic.Dictionary<int, Mesh>();

        /// <summary>
        /// Unit wedge: 1 wide, 1 long, 1 tall. The leading edge sits at z = -0.5 with zero
        /// height, the tall end at z = +0.5. Origin is centred on the base.
        /// </summary>
        public static Mesh Shared
        {
            get
            {
                if (_shared == null) _shared = Build();
                return _shared;
            }
        }

        /// <summary>
        /// Wedge whose top edge is tilted across the ramp: one side of the take-off is higher
        /// than the other, so a car driving over it leaves the ground rolling.
        ///
        /// The tilt lives in the geometry rather than in the object's rotation. Rolling the
        /// whole wedge would swing one edge metres below the surface — the ramp would look
        /// half-buried, because it would be.
        /// </summary>
        /// <param name="tilt">
        /// Height of the low side as a fraction of the high side, in [0, 1]. Negative tilts the
        /// other way. Zero is a level wedge.
        /// </param>
        public static Mesh GetBanked(float tilt)
        {
            tilt = Mathf.Clamp(tilt, -0.9f, 0.9f);
            int key = Mathf.RoundToInt(tilt * 20f);

            if (BankedCache.TryGetValue(key, out Mesh cached) && cached != null)
            {
                return cached;
            }

            float normalised = key / 20f;
            float leftHeight = normalised >= 0f ? 1f - normalised : 1f;
            float rightHeight = normalised >= 0f ? 1f : 1f + normalised;

            Mesh mesh = Build(leftHeight, rightHeight);
            mesh.name = $"Wedge_Banked_{key}";
            BankedCache[key] = mesh;
            return mesh;
        }

        /// <summary>Creates a ramp object with the wedge mesh and a matching convex collider.</summary>
        public static GameObject Create(
            string name, Transform parent, Vector3 localPosition, Vector3 size,
            Quaternion localRotation, Material material, float tilt = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.SetLocalPositionAndRotation(localPosition, localRotation);
            go.transform.localScale = size;

            Mesh mesh = Mathf.Abs(tilt) < 0.01f ? Shared : GetBanked(tilt);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            if (material != null) renderer.sharedMaterial = material;

            // Convex mesh collider: the shape is six vertices, far under any convex limit,
            // and it follows the visual exactly — which a box collider cannot do here.
            MeshCollider collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = true;

            return go;
        }

        private static Mesh Build(float leftHeight = 1f, float rightHeight = 1f)
        {
            // Base corners (y = 0) and the tall edge at the far end. The two top corners can
            // sit at different heights, which is what banks the ramp without burying it.
            var frontLeft = new Vector3(-0.5f, 0f, -0.5f);
            var frontRight = new Vector3(0.5f, 0f, -0.5f);
            var backLeft = new Vector3(-0.5f, 0f, 0.5f);
            var backRight = new Vector3(0.5f, 0f, 0.5f);
            var topLeft = new Vector3(-0.5f, leftHeight, 0.5f);
            var topRight = new Vector3(0.5f, rightHeight, 0.5f);

            // Winding is clockwise seen from outside, which is what Unity treats as a front
            // face. Getting this backwards does not fail loudly: the collider still works,
            // because a convex MeshCollider rebuilds the hull from the vertices and ignores
            // winding entirely. Only the picture breaks — every face is culled from outside,
            // so the ramp renders as a hole in the road.
            //
            // The top quad needs care when the wedge is banked. With the two top corners at
            // different heights that quad is not planar, so the choice of diagonal changes the
            // surface. A convex collider always takes the upper envelope, so the mesh must
            // split along the same diagonal — otherwise the car drives through visibly empty
            // air, up to half a metre above the drawn ramp.
            var topQuad = rightHeight >= leftHeight
                ? new[] { (frontLeft, topLeft, topRight), (frontLeft, topRight, frontRight) }
                : new[] { (frontLeft, topLeft, frontRight), (frontRight, topLeft, topRight) };

            var triangles = new[]
            {
                // Ramp surface, facing up and towards the approach.
                topQuad[0],
                topQuad[1],

                // Base, facing down.
                (frontLeft, frontRight, backRight),
                (frontLeft, backRight, backLeft),

                // Back wall, facing away down the slope.
                (backLeft, backRight, topRight),
                (backLeft, topRight, topLeft),

                // Triangular sides.
                (frontLeft, backLeft, topLeft),
                (frontRight, topRight, backRight),
            };

            var vertices = new Vector3[triangles.Length * 3];
            var normals = new Vector3[triangles.Length * 3];
            var indices = new int[triangles.Length * 3];

            for (int i = 0; i < triangles.Length; i++)
            {
                (Vector3 a, Vector3 b, Vector3 c) = triangles[i];
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;

                int baseIndex = i * 3;
                vertices[baseIndex] = a;
                vertices[baseIndex + 1] = b;
                vertices[baseIndex + 2] = c;

                normals[baseIndex] = normal;
                normals[baseIndex + 1] = normal;
                normals[baseIndex + 2] = normal;

                indices[baseIndex] = baseIndex;
                indices[baseIndex + 1] = baseIndex + 1;
                indices[baseIndex + 2] = baseIndex + 2;
            }

            var mesh = new Mesh { name = "Wedge" };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(indices, 0);
            mesh.SetNormals(normals);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
