using System;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Pure spring/damper math for the raycast suspension.
    /// Free of UnityEngine types so it is covered by fast EditMode tests.
    /// </summary>
    public static class SuspensionMath
    {
        /// <summary>
        /// Normalized compression in [0, 1] from a suspension raycast.
        /// 0 = wheel hanging at full rest length, 1 = suspension fully compressed.
        /// </summary>
        /// <param name="hitDistance">Raycast hit distance from the suspension anchor.</param>
        /// <param name="wheelRadius">Wheel radius; the ray must travel at least this far.</param>
        /// <param name="restLength">Suspension travel at rest.</param>
        public static float Compression(float hitDistance, float wheelRadius, float restLength)
        {
            if (restLength <= 0f)
            {
                return 0f;
            }

            float travel = hitDistance - wheelRadius;
            float compression = 1f - travel / restLength;
            return Math.Clamp(compression, 0f, 1f);
        }

        /// <summary>
        /// Upward suspension force: spring pushes proportionally to compression,
        /// damper resists the vertical velocity of the attachment point.
        /// Clamped at zero because a raycast suspension can push but never pull.
        /// </summary>
        /// <param name="compression">Normalized compression in [0, 1].</param>
        /// <param name="springStrength">Force at full compression, in newtons.</param>
        /// <param name="verticalVelocity">Velocity of the anchor along the car's up axis, m/s.</param>
        /// <param name="damperStrength">Damping force per m/s, in newton-seconds per meter.</param>
        public static float Force(float compression, float springStrength, float verticalVelocity, float damperStrength)
        {
            float force = compression * springStrength - verticalVelocity * damperStrength;
            return Math.Max(0f, force);
        }
    }
}
