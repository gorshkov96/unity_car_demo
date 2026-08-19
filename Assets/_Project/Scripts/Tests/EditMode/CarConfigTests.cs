using CarDemo.Vehicle;
using NUnit.Framework;
using UnityEngine;

namespace CarDemo.Tests.EditMode
{
    public sealed class CarConfigTests
    {
        [Test]
        public void WheelLayout_HasFourWheels_TwoSteeringTwoDriven()
        {
            var config = ScriptableObject.CreateInstance<CarConfig>();
            WheelLayout[] layout = config.BuildWheelLayout();

            Assert.AreEqual(4, layout.Length);
            Assert.AreEqual(2, System.Array.FindAll(layout, w => w.Steerable).Length);
            Assert.AreEqual(2, System.Array.FindAll(layout, w => w.Driven).Length);

            Object.DestroyImmediate(config);
        }

        [Test]
        public void WheelLayout_FrontWheelsSteer_RearWheelsDrive()
        {
            var config = ScriptableObject.CreateInstance<CarConfig>();
            WheelLayout[] layout = config.BuildWheelLayout();

            foreach (WheelLayout wheel in layout)
            {
                if (wheel.LocalPosition.z > 0f)
                {
                    Assert.IsTrue(wheel.Steerable, "Front wheels must steer.");
                    Assert.IsFalse(wheel.Driven, "This demo is rear wheel drive.");
                }
                else
                {
                    Assert.IsFalse(wheel.Steerable);
                    Assert.IsTrue(wheel.Driven);
                }
            }

            Object.DestroyImmediate(config);
        }

        [Test]
        public void WheelLayout_IsSymmetricAcrossTheCentreLine()
        {
            var config = ScriptableObject.CreateInstance<CarConfig>();
            WheelLayout[] layout = config.BuildWheelLayout();

            float sumX = 0f;
            foreach (WheelLayout wheel in layout) sumX += wheel.LocalPosition.x;
            Assert.AreEqual(0f, sumX, 1e-4f, "Wheels must be mirrored, or the car pulls to one side.");

            Object.DestroyImmediate(config);
        }
    }
}
