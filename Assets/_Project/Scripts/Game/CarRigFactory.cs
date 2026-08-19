using CarDemo.Vehicle;
using CarDemo.World;
using UnityEngine;

namespace CarDemo.Game
{
    /// <summary>
    /// Builds the car hierarchy out of primitives: chassis collider, body, cabin
    /// and four wheels, all sized from the <see cref="CarConfig"/>.
    /// Used by the scene generator so the car layout stays code-defined and
    /// automatically follows config changes.
    /// </summary>
    public static class CarRigFactory
    {
        public readonly struct Rig
        {
            public readonly GameObject Root;
            public readonly Rigidbody Body;
            public readonly Transform[] Wheels;

            public Rig(GameObject root, Rigidbody body, Transform[] wheels)
            {
                Root = root;
                Body = body;
                Wheels = wheels;
            }
        }

        public static Rig Build(CarConfig config, Vector3 position, Quaternion rotation)
        {
            var root = new GameObject("Car");
            root.transform.SetPositionAndRotation(position, rotation);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.mass = config.Mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.size = config.ColliderSize;
            collider.center = config.ColliderCenter;

            // Visual shell. Colliderless: the single chassis box above does the colliding,
            // which keeps contacts predictable and cheap.
            var visuals = new GameObject("Visuals");
            visuals.transform.SetParent(root.transform, worldPositionStays: false);

            GameObject shell = PrimitiveFactory.Box(
                "Body", visuals.transform,
                new Vector3(0f, config.ColliderCenter.y, 0f),
                config.BodySize, Quaternion.identity, config.BodyMaterial);
            PrimitiveFactory.StripCollider(shell);

            GameObject cabin = PrimitiveFactory.Box(
                "Cabin", visuals.transform,
                new Vector3(0f, config.ColliderCenter.y + config.BodySize.y * 0.5f + config.CabinSize.y * 0.5f, -0.2f),
                config.CabinSize, Quaternion.identity, config.BodyMaterial);
            PrimitiveFactory.StripCollider(cabin);

            // A nose marker so "which way is forward" is obvious at a glance.
            GameObject nose = PrimitiveFactory.Box(
                "Nose", visuals.transform,
                new Vector3(0f, config.ColliderCenter.y, config.BodySize.z * 0.5f - 0.1f),
                new Vector3(config.BodySize.x * 0.6f, 0.2f, 0.3f),
                Quaternion.identity, config.WheelMaterial);
            PrimitiveFactory.StripCollider(nose);

            WheelLayout[] layout = config.BuildWheelLayout();
            var wheels = new Transform[layout.Length];
            var wheelRoot = new GameObject("Wheels");
            wheelRoot.transform.SetParent(root.transform, worldPositionStays: false);

            for (int i = 0; i < layout.Length; i++)
            {
                // A cylinder primitive is 2 units tall and 1 unit across at scale 1, so the
                // diameter maps to X/Z scale and half the width to Y.
                // Orientation is deliberately left at identity: the wheel's pose is owned by
                // CarWheelVisuals via WheelPose, and setting it here too would mean two places
                // knowing which way a wheel faces.
                GameObject wheel = PrimitiveFactory.Cylinder(
                    $"Wheel_{i}", wheelRoot.transform,
                    layout[i].LocalPosition,
                    new Vector3(config.WheelRadius * 2f, config.WheelWidth * 0.5f, config.WheelRadius * 2f),
                    WheelPose.CylinderToWheel,
                    config.WheelMaterial);
                PrimitiveFactory.StripCollider(wheel);
                wheels[i] = wheel.transform;
            }

            AddWheelColliders(root, config, layout);

            return new Rig(root, body, wheels);
        }

        /// <summary>
        /// Adds one sphere per wheel so props cannot pass through the wheels — most visibly
        /// when the car is upside down and its box collider no longer covers them.
        ///
        /// The spheres sit at the fixed wheel anchors (they do not follow the rolling visual,
        /// which would mean moving colliders every frame) and are deliberately smaller than the
        /// wheel: at rest they hover just above the ground, so the raycast suspension still
        /// carries the car and the spheres only ever catch sideways and overhead contacts.
        /// </summary>
        private static void AddWheelColliders(GameObject root, CarConfig config, WheelLayout[] layout)
        {
            float radius = config.WheelRadius * config.WheelColliderScale;

            // Frictionless: if a sphere does brush the ground on a bump, it must not drag the
            // car. All traction comes from the raycast suspension; these spheres exist purely
            // so that props bounce off the wheels instead of passing through them.
            var slippery = new PhysicsMaterial("WheelGuard")
            {
                dynamicFriction = 0f,
                staticFriction = 0f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Minimum,
                bounceCombine = PhysicsMaterialCombine.Minimum,
            };

            for (int i = 0; i < layout.Length; i++)
            {
                var holder = new GameObject($"WheelCollider_{i}");
                holder.transform.SetParent(root.transform, worldPositionStays: false);
                holder.transform.localPosition = layout[i].LocalPosition;

                SphereCollider collider = holder.AddComponent<SphereCollider>();
                collider.radius = radius;
                collider.sharedMaterial = slippery;
            }
        }
    }
}
