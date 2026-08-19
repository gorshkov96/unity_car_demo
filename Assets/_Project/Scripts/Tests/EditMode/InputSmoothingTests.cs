using CarDemo.Vehicle;
using NUnit.Framework;

namespace CarDemo.Tests.EditMode
{
    public sealed class InputSmoothingTests
    {
        [Test]
        public void Step_RisesTowardsTarget_AtRiseRate()
        {
            float result = InputSmoothing.Step(current: 0f, target: 1f, riseRate: 2f, fallRate: 8f, deltaTime: 0.25f);
            Assert.AreEqual(0.5f, result, 1e-4f);
        }

        [Test]
        public void Step_ReturnsToZero_AtFallRate()
        {
            float result = InputSmoothing.Step(current: 1f, target: 0f, riseRate: 2f, fallRate: 8f, deltaTime: 0.05f);
            Assert.AreEqual(0.6f, result, 1e-4f);
        }

        [Test]
        public void Step_CrossingZero_UsesFallRate()
        {
            // Steering from full right to full left must release quickly, not ramp slowly.
            float result = InputSmoothing.Step(current: 0.5f, target: -1f, riseRate: 1f, fallRate: 10f, deltaTime: 0.02f);
            Assert.AreEqual(0.3f, result, 1e-4f);
        }

        [Test]
        public void Step_NeverOvershootsTarget()
        {
            float result = InputSmoothing.Step(current: 0.9f, target: 1f, riseRate: 100f, fallRate: 100f, deltaTime: 0.5f);
            Assert.AreEqual(1f, result, 1e-6f);
        }

        [Test]
        public void Step_WithZeroDeltaTime_DoesNotMove()
        {
            float result = InputSmoothing.Step(current: 0.3f, target: 1f, riseRate: 5f, fallRate: 5f, deltaTime: 0f);
            Assert.AreEqual(0.3f, result, 1e-6f);
        }
    }
}
