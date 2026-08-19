using CarDemo.World;
using NUnit.Framework;
using UnityEngine;

namespace CarDemo.Tests.EditMode
{
    /// <summary>
    /// Guards the ramp geometry.
    ///
    /// Winding is the kind of mistake that hides: a convex MeshCollider rebuilds its hull from
    /// the vertices and ignores winding, so an inside-out wedge still drives correctly and
    /// only looks wrong. These tests check the thing the eye would have to catch.
    /// </summary>
    public sealed class WedgeMeshTests
    {
        [Test]
        public void EveryFace_PointsOutwards()
        {
            AssertOutwardNormals(WedgeMesh.Shared, "level wedge");
        }

        [Test]
        public void BankedFaces_PointOutwards_BothWays()
        {
            AssertOutwardNormals(WedgeMesh.GetBanked(0.45f), "banked right");
            AssertOutwardNormals(WedgeMesh.GetBanked(-0.45f), "banked left");
        }

        [Test]
        public void Wedge_SitsOnTheGround_AndRisesToFullHeight()
        {
            Bounds bounds = WedgeMesh.Shared.bounds;

            Assert.AreEqual(0f, bounds.min.y, 1e-4f, "The base must lie on y = 0, not below it.");
            Assert.AreEqual(1f, bounds.max.y, 1e-4f, "A unit wedge must be exactly one unit tall.");
            Assert.AreEqual(1f, bounds.size.x, 1e-4f);
            Assert.AreEqual(1f, bounds.size.z, 1e-4f);
        }

        [Test]
        public void LeadingEdge_HasNoHeight()
        {
            // The whole point of a wedge over a tilted box: the approach edge is flat on the
            // ground, so there is no step for a wheel to catch.
            float lowestAtApproach = float.MaxValue;
            foreach (Vector3 vertex in WedgeMesh.Shared.vertices)
            {
                if (Mathf.Abs(vertex.z + 0.5f) > 1e-4f) continue;
                lowestAtApproach = Mathf.Min(lowestAtApproach, vertex.y);
            }

            Assert.AreEqual(0f, lowestAtApproach, 1e-4f);

            foreach (Vector3 vertex in WedgeMesh.Shared.vertices)
            {
                if (Mathf.Abs(vertex.z + 0.5f) > 1e-4f) continue;
                Assert.AreEqual(0f, vertex.y, 1e-4f, "The approach edge must be flat on the ground.");
            }
        }

        [Test]
        public void BankedWedge_HasOneSideLowerThanTheOther()
        {
            Mesh banked = WedgeMesh.GetBanked(0.45f);

            float leftTop = float.MinValue;
            float rightTop = float.MinValue;
            foreach (Vector3 vertex in banked.vertices)
            {
                if (vertex.x < 0f) leftTop = Mathf.Max(leftTop, vertex.y);
                else rightTop = Mathf.Max(rightTop, vertex.y);
            }

            Assert.Less(leftTop, rightTop - 0.1f, "A banked ramp must be lower on one side.");
        }

        [Test]
        public void SharedMesh_IsReused_NotRebuiltPerCall()
        {
            // Every ramp shares one mesh; a copy per ramp would defeat GPU instancing.
            Assert.AreSame(WedgeMesh.Shared, WedgeMesh.Shared);
            Assert.AreSame(WedgeMesh.GetBanked(0.4f), WedgeMesh.GetBanked(0.4f));
        }

        /// <summary>
        /// Checks orientation by convexity rather than by comparing against the centroid.
        ///
        /// The wedge is a convex solid, so for a correctly wound face every other vertex of
        /// the mesh lies behind its plane. Testing against the centroid instead is fragile:
        /// a face passing near the centre gives a dot product of about zero and the result
        /// becomes a coin flip on floating point noise.
        /// </summary>
        private static void AssertOutwardNormals(Mesh mesh, string label)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];

                Vector3 normal = Vector3.Cross(b - a, c - a);
                Assert.Greater(normal.magnitude, 1e-6f, $"{label}: triangle {i / 3} is degenerate.");
                normal.Normalize();

                foreach (Vector3 vertex in vertices)
                {
                    float distance = Vector3.Dot(normal, vertex - a);
                    Assert.LessOrEqual(distance, 1e-4f,
                        $"{label}: triangle {i / 3} faces inwards — the ramp renders as a hole.");
                }
            }
        }
    }
}
