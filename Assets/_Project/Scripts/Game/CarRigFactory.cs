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
            CarBodyFactory.Build(visuals.transform, config);

            WheelLayout[] layout = config.BuildWheelLayout();
            var wheels = new Transform[layout.Length];
            var wheelRoot = new GameObject("Wheels");
            wheelRoot.transform.SetParent(root.transform, worldPositionStays: false);

            for (int i = 0; i < layout.Length; i++)
            {
                // The wheel's own pose is owned by CarWheelVisuals through WheelPose; this
                // object is just the pivot it drives, so it starts at identity rotation.
                var wheel = new GameObject($"Wheel_{i}");
                wheel.transform.SetParent(wheelRoot.transform, worldPositionStays: false);
                wheel.transform.localPosition = layout[i].LocalPosition;

                CarBodyFactory.BuildWheel(wheel.transform, config, Mathf.Sign(layout[i].LocalPosition.x));
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
