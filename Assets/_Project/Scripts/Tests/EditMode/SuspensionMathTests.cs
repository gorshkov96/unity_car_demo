using CarDemo.Vehicle;
using NUnit.Framework;

namespace CarDemo.Tests.EditMode
{
    public sealed class SuspensionMathTests
    {
        [Test]
        public void Compression_AtFullRestLength_IsZero()
        {
            // Wheel hanging at the very end of its travel: no spring load.
            float compression = SuspensionMath.Compression(hitDistance: 0.75f, wheelRadius: 0.3f, restLength: 0.45f);
            Assert.AreEqual(0f, compression, 1e-4f);
        }

        [Test]
        public void Compression_AtWheelRadius_IsFullyCompressed()
        {
            float compression = SuspensionMath.Compression(hitDistance: 0.3f, wheelRadius: 0.3f, restLength: 0.45f);
            Assert.AreEqual(1f, compression, 1e-4f);
        }

        [Test]
        public void Compression_HalfTravel_IsHalf()
        {
            float compression = SuspensionMath.Compression(hitDistance: 0.525f, wheelRadius: 0.3f, restLength: 0.45f);
            Assert.AreEqual(0.5f, compression, 1e-4f);
        }

        [Test]
        public void Compression_IsClampedToUnitRange()
        {
            Assert.AreEqual(1f, SuspensionMath.Compression(0.1f, 0.3f, 0.45f), 1e-4f);
            Assert.AreEqual(0f, SuspensionMath.Compression(5f, 0.3f, 0.45f), 1e-4f);
        }

        [Test]
        public void Force_SpringPushesProportionallyToCompression()
        {
            float force = SuspensionMath.Force(compression: 0.5f, springStrength: 12000f, verticalVelocity: 0f, damperStrength: 2000f);
            Assert.AreEqual(6000f, force, 1e-2f);
        }

        [Test]
        public void Force_DamperResistsUpwardMotion()
        {
            float force = SuspensionMath.Force(compression: 0.5f, springStrength: 12000f, verticalVelocity: 1f, damperStrength: 2000f);
            Assert.AreEqual(4000f, force, 1e-2f);
        }

        [Test]
        public void Force_NeverPulls()
        {
            // A raycast suspension can push the car up but must never suck it down.
            float force = SuspensionMath.Force(compression: 0.1f, springStrength: 12000f, verticalVelocity: 10f, damperStrength: 2000f);
            Assert.AreEqual(0f, force, 1e-6f);
        }
    }
}
