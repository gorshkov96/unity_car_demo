using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Pure math for placing a wheel mesh. Kept out of MonoBehaviour so the quaternion
    /// composition — the part that is easy to get subtly wrong — is covered by EditMode tests.
    /// </summary>
    public static class WheelPose
    {
        /// <summary>
        /// Extra rotation that lays a Unity cylinder primitive on its side: the primitive's
        /// axis runs along local Y, a wheel's axle runs along the car's X.
        /// </summary>
        public static readonly Quaternion CylinderToWheel = Quaternion.Euler(0f, 0f, 90f);

        /// <summary>
        /// World rotation of a wheel mesh.
        ///
        /// Order matters and is the whole point of this function: steering is applied in the
        /// car's local space, then the roll, and the cylinder correction goes last (innermost).
        /// Composing it the other way round — correction before roll — makes the axle itself
        /// rotate, so the wheel yaws instead of rolling.
        /// </summary>
        /// <param name="carRotation">World rotation of the car body.</param>
        /// <param name="steerAngle">Steering angle in degrees, around the car's up axis.</param>
        /// <param name="rollAngle">Accumulated rolling angle in degrees, around the axle.</param>
        public static Quaternion Compose(Quaternion carRotation, float steerAngle, float rollAngle)
        {
            return carRotation
                   * Quaternion.Euler(0f, steerAngle, 0f)
                   * Quaternion.Euler(rollAngle, 0f, 0f)
                   * CylinderToWheel;
        }

        /// <summary>
        /// Wheel rotation in the car's local space — what a wheel mesh parented to the car
        /// should use. Preferred over <see cref="Compose"/> for rendering: local placement
        /// follows the interpolated body instead of fighting it.
        /// </summary>
        public static Quaternion ComposeLocal(float steerAngle, float rollAngle)
        {
            return Quaternion.Euler(0f, steerAngle, 0f)
                   * Quaternion.Euler(rollAngle, 0f, 0f)
                   * CylinderToWheel;
        }

        /// <summary>
        /// Axle direction in the car's local space for a given steering angle.
        /// Exposed for tests: it must not depend on the rolling angle.
        /// </summary>
        public static Vector3 LocalAxle(float steerAngle, float rollAngle)
        {
            // The cylinder's axis is its local up.
            return ComposeLocal(steerAngle, rollAngle) * Vector3.up;
        }

        /// <summary>Integrates the rolling angle, wrapped to [0, 360) to keep float precision.</summary>
        public static float Advance(float currentAngle, float spinSpeedDegreesPerSecond, float deltaTime)
        {
            return Mathf.Repeat(currentAngle + spinSpeedDegreesPerSecond * deltaTime, 360f);
        }
    }
}
