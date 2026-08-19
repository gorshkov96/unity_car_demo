using CarDemo.Core;
using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Composed props for the descent, built from primitives in a low-poly style.
    ///
    /// Each hazard is a small assembly rather than a single box: a spinner gets a hub, arms
    /// and bright tips, a sweeper gets a frame and hazard stripes. Flat-shaded primitives read
    /// as faceted shapes, so a handful of parts is enough to make an object legible at speed —
    /// which is the point, since the player sees each of these for about a second.
    /// </summary>
    public static class DescentProps
    {
        /// <summary>Rotating hazard: a hub with two arms and hazard-striped tips.</summary>
        public static void BuildSpinner(Transform pivot, float armLength, DescentMaterials materials)
        {
            // Hub: a column with a collar, so the pivot reads as a machine rather than a peg.
            // Hub reaches down to the road but no further: the pivot sits 1.3 m up, so the
            // column is sized to land exactly on the surface.
            // Keeps its collider: it is a solid column in the middle of the track, and
            // anything the player can see must be something the car can hit.
            GameObject hub = PrimitiveFactory.Cylinder("Hub", pivot, new Vector3(0f, -0.55f, 0f),
                new Vector3(1.5f, 0.75f, 1.5f), Quaternion.identity, materials.Accent);
            hub.layer = GameLayers.Prop;

            GameObject collar = PrimitiveFactory.Cylinder("Collar", pivot, new Vector3(0f, 0.2f, 0f),
                new Vector3(2.1f, 0.28f, 2.1f), Quaternion.identity, materials.Hazard);
            PrimitiveFactory.StripCollider(collar);

            GameObject cap = PrimitiveFactory.Box("Cap", pivot, new Vector3(0f, 0.75f, 0f),
                new Vector3(1.2f, 0.5f, 1.2f), Quaternion.Euler(0f, 45f, 0f), materials.Moving);
            PrimitiveFactory.StripCollider(cap);

            float halfArm = armLength * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject arm = PrimitiveFactory.Box("Arm", pivot,
                    new Vector3(side * halfArm * 0.5f, 0f, 0f),
                    new Vector3(halfArm, 0.9f, 0.9f), Quaternion.identity, materials.Moving);
                arm.layer = GameLayers.Prop;

                // Bright tip: the part the player must actually judge the distance to.
                GameObject tip = PrimitiveFactory.Box("Tip", pivot,
                    new Vector3(side * (halfArm - 0.9f), 0f, 0f),
                    new Vector3(1.8f, 1.3f, 1.3f), Quaternion.identity, materials.Hazard);
                tip.layer = GameLayers.Prop;

                // A diagonal brace turns a plain bar into something built.
                GameObject brace = PrimitiveFactory.Box("Brace", pivot,
                    new Vector3(side * halfArm * 0.45f, -0.35f, 0f),
                    new Vector3(halfArm * 0.7f, 0.35f, 0.35f),
                    Quaternion.Euler(0f, 0f, side * 7f), materials.Accent);
                PrimitiveFactory.StripCollider(brace);
            }
        }

        /// <summary>Sweeping barrier: a striped beam on a low frame.</summary>
        public static void BuildSweeper(Transform root, float width, DescentMaterials materials)
        {
            GameObject beam = PrimitiveFactory.Box("Beam", root, Vector3.zero,
                new Vector3(width, 1.5f, 1.1f), Quaternion.identity, materials.Moving);
            beam.layer = GameLayers.Prop;

            // Hazard stripes along the beam: three blocks, alternating colour.
            int stripes = Mathf.Max(3, Mathf.RoundToInt(width / 3f));
            for (int i = 0; i < stripes; i++)
            {
                float t = (i + 0.5f) / stripes - 0.5f;
                GameObject stripe = PrimitiveFactory.Box("Stripe", root,
                    new Vector3(t * width, 0f, -0.58f),
                    new Vector3(width / stripes * 0.55f, 1.1f, 0.12f),
                    Quaternion.identity, i % 2 == 0 ? materials.Hazard : materials.Accent);
                PrimitiveFactory.StripCollider(stripe);
            }

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject post = PrimitiveFactory.Box("Post", root,
                    new Vector3(side * width * 0.5f, -0.75f, 0f),
                    new Vector3(0.5f, 1.6f, 0.5f), Quaternion.identity, materials.Accent);
                PrimitiveFactory.StripCollider(post);
            }
        }

        /// <summary>Crate: a box with contrasting edge caps, so its faceting reads in motion.</summary>
        public static void BuildCrate(Transform root, float size, DescentMaterials materials)
        {
            GameObject body = PrimitiveFactory.Box("Body", root, Vector3.zero,
                new Vector3(size, size, size), Quaternion.identity, materials.Crate);
            body.layer = GameLayers.Prop;

            // Corner posts and a band: a bare cube tumbling at speed reads as a grey blob,
            // while edges catch the light and make the rotation legible.
            GameObject band = PrimitiveFactory.Box("Band", root, Vector3.zero,
                new Vector3(size * 1.04f, size * 0.2f, size * 1.04f),
                Quaternion.identity, materials.Accent);
            PrimitiveFactory.StripCollider(band);

            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    GameObject post = PrimitiveFactory.Box("Corner", root,
                        new Vector3(x * size * 0.46f, 0f, z * size * 0.46f),
                        new Vector3(size * 0.14f, size * 1.03f, size * 0.14f),
                        Quaternion.identity, materials.Accent);
                    PrimitiveFactory.StripCollider(post);
                }
            }
        }

        /// <summary>
        /// Ramp shapes. Different profiles change how the car leaves the ground, which is what
        /// keeps a long descent from feeling like one repeated jump.
        /// </summary>
        public enum RampKind
        {
            /// <summary>Long and shallow: a fast, flat launch.</summary>
            Kicker,

            /// <summary>Wedge up onto a flat top, then a drop off the end.</summary>
            Table,

            /// <summary>Two wedges back to back: up, over, down.</summary>
            Roller,

            /// <summary>Steep and short: throws the car high.</summary>
            Launcher,

            /// <summary>Banked left: the car leaves it already rolling.</summary>
            CornerLeft,

            /// <summary>Banked right.</summary>
            CornerRight,

            /// <summary>Two banked wedges side by side, leaning apart.</summary>
            Twin,
        }

        /// <summary>How far a wedge's nose is buried so its leading edge cannot catch a wheel.</summary>
        private const float WedgeSink = 0.08f;

        public static void BuildRamp(Transform root, RampKind kind, float width, DescentMaterials materials)
        {
            switch (kind)
            {
                case RampKind.Kicker:
                    Wedge(root, Vector3.zero, width * 0.55f, 16f, 2.2f, 0f, materials.Jump);
                    break;

                case RampKind.Table:
                {
                    // The slab's top face must line up with the top of the wedge exactly.
                    // Eight centimetres of mismatch is a step, and a step at 130 km/h is a
                    // launch the player did not ask for.
                    const float rampHeight = 2.2f;
                    const float slabThickness = 0.5f;
                    Wedge(root, new Vector3(0f, 0f, -7f), width * 0.5f, 13f, rampHeight, 0f, materials.Jump);
                    Slab(root, new Vector3(0f, rampHeight - WedgeSink - slabThickness * 0.5f, 5f),
                        width * 0.5f, 11f, materials.Jump);
                    break;
                }

                case RampKind.Roller:
                    Wedge(root, new Vector3(0f, 0f, -6f), width * 0.5f, 12f, 2f, 0f, materials.Jump);
                    // Mirrored: the far side drops away instead of climbing.
                    Wedge(root, new Vector3(0f, 0f, 6f), width * 0.5f, 12f, 2f, 0f, materials.Jump, mirrored: true);
                    break;

                case RampKind.Launcher:
                    Wedge(root, Vector3.zero, width * 0.42f, 11f, 2.8f, 0f, materials.Jump);
                    break;

                case RampKind.CornerLeft:
                case RampKind.CornerRight:
                {
                    // Banked wedge: one edge of the take-off is higher than the other, so the
                    // car leaves it rolling. That roll is the point — it turns a jump into a trick.
                    // 0.45 means the low side of the take-off is 45% shorter than the high
                    // side: a clear roll, without a lip steep enough to catch a wheel.
                    float tilt = kind == RampKind.CornerLeft ? -0.45f : 0.45f;
                    Wedge(root, Vector3.zero, width * 0.5f, 14f, 2.4f, tilt, materials.Hazard);
                    BankMarkers(root, width * 0.85f, tilt, materials);
                    break;
                }

                case RampKind.Twin:
                {
                    // A pair leaning away from each other: either one spins the car, and the
                    // gap between them is a clean line for anyone who would rather not.
                    float offset = width * 0.36f;
                    Wedge(root, new Vector3(-offset, 0f, 0f), width * 0.32f, 13f, 2.1f, -0.4f, materials.Hazard);
                    Wedge(root, new Vector3(offset, 0f, 0f), width * 0.32f, 13f, 2.1f, 0.4f, materials.Hazard);
                    BankMarkers(root, width, -0.4f, materials);
                    break;
                }
            }
        }

        /// <summary>
        /// Places one wedge ramp.
        ///
        /// The nose is sunk a little below the surface: a wedge tapers to nothing, and an edge
        /// sitting exactly at ground level still presents a lip for the suspension to catch.
        /// Burying it removes the trip without changing how the ramp looks.
        /// </summary>
        private static void Wedge(Transform root, Vector3 position, float width, float length,
            float height, float tilt, Material material, bool mirrored = false)
        {
            float yaw = mirrored ? 180f : 0f;
            GameObject wedge = WedgeMesh.Create("Wedge", root,
                position + new Vector3(0f, -WedgeSink, 0f),
                new Vector3(width, height, length),
                Quaternion.Euler(0f, yaw, 0f),
                material,
                tilt);
            wedge.layer = GameLayers.Ground;

            AddCheeks(root, position, width, length, height, tilt, mirrored);
        }

        /// <summary>
        /// Side cheeks: solid wedges flanking the ramp, a little taller than the ramp itself.
        ///
        /// They give the jump a silhouette against the road — a bare wedge lying at eight
        /// degrees is nearly invisible head-on, which is how a ramp ends up looking "sunken"
        /// even when it sits exactly on the surface. They are solid, like everything else the
        /// player can see.
        /// </summary>
        private static void AddCheeks(Transform root, Vector3 position, float width, float length,
            float height, float tilt, bool mirrored)
        {
            float yaw = mirrored ? 180f : 0f;

            for (int side = -1; side <= 1; side += 2)
            {
                float sideHeight = height * (side < 0
                    ? (tilt >= 0f ? 1f - tilt : 1f)
                    : (tilt >= 0f ? 1f : 1f + tilt));

                GameObject cheek = WedgeMesh.Create("Cheek", root,
                    position + new Vector3(side * (width * 0.5f + 0.17f), -WedgeSink, 0f),
                    new Vector3(0.35f, sideHeight * 1.04f, length),
                    Quaternion.Euler(0f, yaw, 0f),
                    null);
                cheek.layer = GameLayers.Ground;

                // Body colour comes from the ramp; the cheeks take the hazard colour so the
                // take-off edge is obvious from a distance.
                if (cheek.TryGetComponent(out MeshRenderer renderer))
                {
                    renderer.sharedMaterial = CheekMaterial;
                }
            }
        }

        /// <summary>Material used for ramp cheeks, set by the generator before building.</summary>
        public static Material CheekMaterial { get; set; }

        /// <summary>Flat chevrons showing which way a ramp is banked, readable on approach.</summary>
        private static void BankMarkers(Transform root, float width, float tilt, DescentMaterials materials)
        {
            int direction = tilt < 0f ? -1 : 1;
            for (int i = 0; i < 3; i++)
            {
                GameObject chevron = PrimitiveFactory.Box("Chevron", root,
                    new Vector3(direction * (i * 1.2f - 1.2f), 0.05f, -8f + i * 1.5f),
                    new Vector3(width * 0.45f, 0.06f, 0.55f),
                    Quaternion.Euler(0f, direction * 24f, 0f), materials.Stripe);
                PrimitiveFactory.StripCollider(chevron);
            }
        }

        private static void Slab(Transform root, Vector3 position, float width, float length, Material material)
        {
            GameObject slab = PrimitiveFactory.Box("RampTop", root, position,
                new Vector3(width, 0.5f, length), Quaternion.identity, material);
            slab.layer = GameLayers.Ground;
        }
    }
}
