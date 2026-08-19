using CarDemo.Core;
using NUnit.Framework;
using UnityEngine;

namespace CarDemo.Tests.EditMode
{
    public sealed class DeterministicRandomTests
    {
        [Test]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new DeterministicRandom(42);
            var b = new DeterministicRandom(42);

            for (int i = 0; i < 16; i++)
            {
                Assert.AreEqual(a.Range(-10f, 10f), b.Range(-10f, 10f), 1e-6f);
            }
        }

        [Test]
        public void DifferentSeeds_Diverge()
        {
            var a = new DeterministicRandom(1);
            var b = new DeterministicRandom(2);
            Assert.AreNotEqual(a.Range(0f, 1000f), b.Range(0f, 1000f));
        }

        [Test]
        public void PointOnGround_StaysInsideBounds_AndOnThePlane()
        {
            var random = new DeterministicRandom(7);
            for (int i = 0; i < 100; i++)
            {
                Vector3 point = random.PointOnGround(50f);
                Assert.LessOrEqual(Mathf.Abs(point.x), 50f);
                Assert.LessOrEqual(Mathf.Abs(point.z), 50f);
                Assert.AreEqual(0f, point.y, 1e-6f);
            }
        }
    }
}
