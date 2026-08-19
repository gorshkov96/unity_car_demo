using CarDemo.Vehicle;
using CarDemo.World;
using UnityEngine;

namespace CarDemo.Game
{
    /// <summary>
    /// Builds the car's visual shell out of primitives: a wedge-shaped body with a raked
    /// windscreen, wheel arches, bumpers, lights, mirrors, a spoiler and exhausts.
    ///
    /// Still primitives on purpose — no binary meshes in the repository and every shape is
    /// readable code — but shaped enough to read as a car rather than a stack of boxes.
    /// None of it has colliders: the chassis box owns all collision.
    /// </summary>
    public static class CarBodyFactory
    {
        public static void Build(Transform visuals, CarConfig config)
        {
            float halfWidth = config.BodySize.x * 0.5f;
            float bodyY = config.ColliderCenter.y;
            float length = config.BodySize.z;
            float halfLength = length * 0.5f;

            Material body = config.BodyMaterial;
            Material trim = config.TrimMaterial;
            Material glass = config.GlassMaterial;

            // --- Main body: a lower slab plus a raised centre section ------------------
            Box(visuals, "LowerBody", new Vector3(0f, bodyY - 0.06f, 0f),
                new Vector3(config.BodySize.x, config.BodySize.y * 0.62f, length), Quaternion.identity, body);

            Box(visuals, "UpperBody", new Vector3(0f, bodyY + config.BodySize.y * 0.32f, -0.1f),
                new Vector3(config.BodySize.x * 0.94f, config.BodySize.y * 0.5f, length * 0.78f),
                Quaternion.identity, body);

            // --- Bonnet and boot: sloped panels give the silhouette a nose and a tail ---
            Box(visuals, "Bonnet", new Vector3(0f, bodyY + config.BodySize.y * 0.4f, halfLength * 0.62f),
                new Vector3(config.BodySize.x * 0.92f, 0.12f, length * 0.3f),
                Quaternion.Euler(-4f, 0f, 0f), body);

            Box(visuals, "Boot", new Vector3(0f, bodyY + config.BodySize.y * 0.42f, -halfLength * 0.72f),
                new Vector3(config.BodySize.x * 0.9f, 0.12f, length * 0.22f),
                Quaternion.Euler(3f, 0f, 0f), body);

            // --- Cabin ------------------------------------------------------------------
            // Built as a tapered box with glass set flush into it. The previous version put
            // full-height panes at the cabin's centre height, so they stood proud of the roof
            // and poked through the sides — glass floating around the car rather than in it.
            float cabinBase = bodyY + config.BodySize.y * 0.28f;
            float cabinHeight = config.CabinSize.y;
            float cabinTop = cabinBase + cabinHeight;
            float cabinHalfWidth = config.CabinSize.x * 0.5f;

            // Greenhouse: slightly narrower than the body, sitting on the shoulders.
            Box(visuals, "Greenhouse", new Vector3(0f, cabinBase + cabinHeight * 0.5f, -0.3f),
                new Vector3(config.CabinSize.x, cabinHeight, config.CabinSize.z),
                Quaternion.identity, glass);

            // Roof panel caps it, so the glass never reads as the top surface.
            Box(visuals, "Roof", new Vector3(0f, cabinTop - 0.02f, -0.3f),
                new Vector3(config.CabinSize.x * 1.02f, 0.1f, config.CabinSize.z * 0.94f),
                Quaternion.identity, body);

            // A-pillars and C-pillars: thin body-coloured posts at the corners of the
            // greenhouse. They break the glass box into windows, which is what makes it
            // read as a cabin.
            for (int side = -1; side <= 1; side += 2)
            {
                // Pillars stay inside the greenhouse footprint. Tilting them pushed the tops
                // out over the bonnet, which read as spikes growing off the car.
                Box(visuals, "PillarFront",
                    new Vector3(side * cabinHalfWidth * 0.94f, cabinBase + cabinHeight * 0.46f,
                        -0.3f + config.CabinSize.z * 0.42f),
                    new Vector3(0.08f, cabinHeight * 0.88f, 0.12f),
                    Quaternion.identity, body);

                Box(visuals, "PillarRear",
                    new Vector3(side * cabinHalfWidth * 0.94f, cabinBase + cabinHeight * 0.46f,
                        -0.3f - config.CabinSize.z * 0.42f),
                    new Vector3(0.08f, cabinHeight * 0.88f, 0.14f),
                    Quaternion.identity, body);

                // Waistline: the body-coloured strip under the windows.
                Box(visuals, "Waist",
                    new Vector3(side * cabinHalfWidth * 1.01f, cabinBase + 0.04f, -0.3f),
                    new Vector3(0.06f, 0.12f, config.CabinSize.z * 0.98f),
                    Quaternion.identity, body);
            }

            // Raked nose and tail of the greenhouse, in body colour, so the glass box is
            // clipped into a car silhouette rather than left as a slab.
            Box(visuals, "WindscreenFrame",
                new Vector3(0f, cabinTop - 0.06f, -0.3f + config.CabinSize.z * 0.52f),
                new Vector3(config.CabinSize.x * 1.01f, 0.1f, 0.24f),
                Quaternion.Euler(-28f, 0f, 0f), body);

            Box(visuals, "RearFrame",
                new Vector3(0f, cabinTop - 0.06f, -0.3f - config.CabinSize.z * 0.52f),
                new Vector3(config.CabinSize.x * 1.01f, 0.1f, 0.26f),
                Quaternion.Euler(32f, 0f, 0f), body);

            for (int side = -1; side <= 1; side += 2)
            {
                // Mirrors sit on the waistline, not floating beside the roof.
                Box(visuals, side < 0 ? "MirrorL" : "MirrorR",
                    new Vector3(side * (halfWidth + 0.04f), cabinBase + 0.06f, config.CabinSize.z * 0.3f),
                    new Vector3(0.16f, 0.08f, 0.06f), Quaternion.Euler(0f, side * 14f, 0f), trim);
            }

            // --- Wheel arches ----------------------------------------------------------
            // Arches wrap the wheels rather than sitting beside them: without something
            // bridging the gap the wheels read as four separate objects floating near a box.
            WheelLayout[] layout = config.BuildWheelLayout();
            foreach (WheelLayout wheel in layout)
            {
                float side = Mathf.Sign(wheel.LocalPosition.x);
                float archX = side * (halfWidth - 0.04f);

                Box(visuals, "ArchSide",
                    new Vector3(archX, bodyY - 0.1f, wheel.LocalPosition.z),
                    new Vector3(0.12f, config.BodySize.y * 0.62f, config.WheelRadius * 2.7f),
                    Quaternion.identity, trim);

                // Lip over the top of the wheel, closing the gap between body and tyre.
                Box(visuals, "ArchTop",
                    new Vector3(side * (halfWidth - 0.16f), bodyY - 0.02f, wheel.LocalPosition.z),
                    new Vector3(0.42f, 0.12f, config.WheelRadius * 2.6f),
                    Quaternion.identity, body);

                // Short skirt in front of and behind each wheel.
                for (int end = -1; end <= 1; end += 2)
                {
                    Box(visuals, "ArchEnd",
                        new Vector3(side * (halfWidth - 0.1f), bodyY - 0.22f,
                            wheel.LocalPosition.z + end * config.WheelRadius * 1.35f),
                        new Vector3(0.3f, 0.28f, 0.22f), Quaternion.identity, trim);
                }
            }

            // --- Bumpers and side skirts -----------------------------------------------
            Box(visuals, "FrontBumper", new Vector3(0f, bodyY - 0.16f, halfLength + 0.04f),
                new Vector3(config.BodySize.x * 1.01f, 0.2f, 0.16f), Quaternion.identity, trim);

            Box(visuals, "RearBumper", new Vector3(0f, bodyY - 0.16f, -halfLength - 0.04f),
                new Vector3(config.BodySize.x * 1.01f, 0.22f, 0.16f), Quaternion.identity, trim);

            for (int side = -1; side <= 1; side += 2)
            {
                Box(visuals, "SideSkirt",
                    new Vector3(side * (halfWidth - 0.01f), bodyY - 0.24f, 0f),
                    new Vector3(0.08f, 0.12f, length * 0.52f), Quaternion.identity, trim);
            }

            // --- Lights ------------------------------------------------------------------
            for (int side = -1; side <= 1; side += 2)
            {
                Box(visuals, side < 0 ? "HeadlightL" : "HeadlightR",
                    new Vector3(side * halfWidth * 0.62f, bodyY + 0.02f, halfLength + 0.01f),
                    new Vector3(config.BodySize.x * 0.26f, 0.12f, 0.06f),
                    Quaternion.identity, config.HeadlightMaterial);

                Box(visuals, side < 0 ? "TaillightL" : "TaillightR",
                    new Vector3(side * halfWidth * 0.64f, bodyY + 0.06f, -halfLength - 0.01f),
                    new Vector3(config.BodySize.x * 0.24f, 0.1f, 0.06f),
                    Quaternion.identity, config.TaillightMaterial);
            }

            // --- Spoiler ------------------------------------------------------------------
            for (int side = -1; side <= 1; side += 2)
            {
                Box(visuals, "SpoilerStand",
                    new Vector3(side * config.BodySize.x * 0.32f, bodyY + config.BodySize.y * 0.62f, -halfLength * 0.86f),
                    new Vector3(0.06f, 0.18f, 0.12f), Quaternion.identity, trim);
            }

            Box(visuals, "SpoilerWing",
                new Vector3(0f, bodyY + config.BodySize.y * 0.62f + 0.12f, -halfLength * 0.88f),
                new Vector3(config.BodySize.x * 0.86f, 0.05f, 0.3f),
                Quaternion.Euler(-8f, 0f, 0f), body);

            // --- Roof and bonnet detail --------------------------------------------------
            // In body colour, not trim: a dark panel on the roof reads as a hole in the car
            // from the chase camera, which is the angle the player sees most.
            Box(visuals, "RoofRib", new Vector3(0f, cabinTop + 0.04f, -0.3f),
                new Vector3(config.CabinSize.x * 0.24f, 0.06f, config.CabinSize.z * 0.5f),
                Quaternion.identity, body);

            Box(visuals, "BonnetVent", new Vector3(0f, bodyY + config.BodySize.y * 0.46f, halfLength * 0.5f),
                new Vector3(config.BodySize.x * 0.34f, 0.07f, 0.5f),
                Quaternion.Euler(-4f, 0f, 0f), trim);

            for (int side = -1; side <= 1; side += 2)
            {
                Box(visuals, "DoorLine",
                    new Vector3(side * (halfWidth + 0.01f), bodyY + 0.06f, -0.15f),
                    new Vector3(0.03f, 0.1f, length * 0.34f), Quaternion.identity, trim);
            }

            // --- Exhausts -----------------------------------------------------------------
            for (int side = -1; side <= 1; side += 2)
            {
                Cylinder(visuals, "Exhaust",
                    new Vector3(side * config.BodySize.x * 0.28f, bodyY - 0.26f, -halfLength - 0.08f),
                    new Vector3(0.08f, 0.06f, 0.08f), Quaternion.Euler(90f, 0f, 0f), trim);
            }
        }

        /// <summary>Builds one wheel: a dark tyre with a lighter rim inset on the outer face.</summary>
        public static void BuildWheel(Transform wheelRoot, CarConfig config, float side)
        {
            PrimitiveFactory.Cylinder(
                "Tyre", wheelRoot, Vector3.zero,
                new Vector3(config.WheelRadius * 2f, config.WheelWidth * 0.5f, config.WheelRadius * 2f),
                Quaternion.identity, config.WheelMaterial)
                .transform.localPosition = Vector3.zero;

            // The rim sits slightly proud of the tyre's outer face so the wheel reads as
            // spinning: a plain black cylinder gives the eye nothing to track.
            GameObject rim = PrimitiveFactory.Cylinder(
                "Rim", wheelRoot, Vector3.zero,
                new Vector3(config.WheelRadius * 1.25f, config.WheelWidth * 0.52f, config.WheelRadius * 1.25f),
                Quaternion.identity, config.RimMaterial);
            rim.transform.localPosition = new Vector3(0f, side * 0.01f, 0f);

            GameObject spoke = PrimitiveFactory.Box(
                "Spoke", wheelRoot, Vector3.zero,
                new Vector3(config.WheelRadius * 1.7f, config.WheelWidth * 0.56f, config.WheelRadius * 0.28f),
                Quaternion.identity, config.RimMaterial);
            spoke.transform.localPosition = new Vector3(0f, side * 0.012f, 0f);
            PrimitiveFactory.StripCollider(spoke);

            GameObject spokeCross = PrimitiveFactory.Box(
                "SpokeCross", wheelRoot, Vector3.zero,
                new Vector3(config.WheelRadius * 0.28f, config.WheelWidth * 0.56f, config.WheelRadius * 1.7f),
                Quaternion.identity, config.RimMaterial);
            spokeCross.transform.localPosition = new Vector3(0f, side * 0.012f, 0f);
            PrimitiveFactory.StripCollider(spokeCross);

            foreach (Transform child in wheelRoot)
            {
                PrimitiveFactory.StripCollider(child.gameObject);
            }
        }

        private static void Box(Transform parent, string name, Vector3 position, Vector3 size, Quaternion rotation, Material material)
        {
            GameObject go = PrimitiveFactory.Box(name, parent, position, size, rotation, material);
            PrimitiveFactory.StripCollider(go);
        }

        private static void Cylinder(Transform parent, string name, Vector3 position, Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject go = PrimitiveFactory.Cylinder(name, parent, position, scale, rotation, material);
            PrimitiveFactory.StripCollider(go);
        }
    }
}
