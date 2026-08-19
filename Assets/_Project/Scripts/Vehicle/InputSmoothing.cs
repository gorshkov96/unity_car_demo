using System;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Pure math for smoothing raw digital input (keys are 0/1) into analog-feeling values.
    /// Kept free of UnityEngine types so it is trivially covered by EditMode tests.
    /// </summary>
    public static class InputSmoothing
    {
        /// <summary>
        /// Moves <paramref name="current"/> towards <paramref name="target"/>.
        /// Uses <paramref name="riseRate"/> when moving away from zero and the usually
        /// faster <paramref name="fallRate"/> when returning to zero, so releasing
        /// a key re-centers quickly while pressing one ramps up gradually.
        /// Rates are in units per second.
        /// </summary>
        public static float Step(float current, float target, float riseRate, float fallRate, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return current;
            }

            bool returningToZero = Math.Abs(target) < Math.Abs(current)
                                   || (target != 0f && current != 0f && Math.Sign(target) != Math.Sign(current));
            float rate = returningToZero ? fallRate : riseRate;
            return MoveTowards(current, target, rate * deltaTime);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            float delta = target - current;
            if (Math.Abs(delta) <= maxDelta)
            {
                return target;
            }

            return current + Math.Sign(delta) * maxDelta;
        }
    }
}
