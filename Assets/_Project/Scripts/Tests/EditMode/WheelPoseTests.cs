using CarDemo.Vehicle;
using NUnit.Framework;
using UnityEngine;

namespace CarDemo.Tests.EditMode
{
    /// <summary>
    /// Guards the wheel orientation math. The original implementation composed the
    /// quaternions in an order that rotated the axle itself, so wheels yawed instead of
    /// rolling; these tests fail loudly if that regresses.
    /// </summary>
    public sealed class WheelPoseTests
    {
        [Test]
        public void Axle_StaysFixed_WhileTheWheelRolls()
        {
            Vector3 axleAtRest = WheelPose.LocalAxle(steerAngle: 0f, rollAngle: 0f);

            for (float roll = 0f; roll < 360f; roll += 15f)
            {
                Vector3 axle = WheelPose.LocalAxle(steerAngle: 0f, rollAngle: roll);
                Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(axleAtRest, axle)), 1e-4f,
                    $"Axle moved at roll={roll} deg — the wheel is yawing, not rolling.");
            }
        }

        [Test]
        public void Axle_IsHorizontalAndAcrossTheCar()
        {
            Vector3 axle = WheelPose.LocalAxle(steerAngle: 0f, rollAngle: 137f);

            Assert.AreEqual(0f, axle.y, 1e-4f, "A wheel axle must be horizontal.");
            Assert.AreEqual(1f, Mathf.Abs(axle.x), 1e-4f, "A wheel axle must point across the car.");
            Assert.AreEqual(0f, axle.z, 1e-4f);
        }

        [Test]
        public void Steering_RotatesTheAxle_AroundTheUpAxis()
        {
            Vector3 straight = WheelPose.LocalAxle(0f, 0f);
            Vector3 steered = WheelPose.LocalAxle(30f, 0f);

            Assert.AreEqual(0f, steered.y, 1e-4f, "Steering must not tilt the axle.");
            float angle = Vector3.Angle(straight, steered);
            Assert.AreEqual(30f, angle, 1e-3f, "Axle must turn by exactly the steering angle.");
        }

        [Test]
        public void Rolling_MovesTheRim_InTheVerticalPlane()
        {
            // A point on the rim starts ahead of the axle and must swing downward-forward,
            // never sideways.
            Quaternion atRest = WheelPose.Compose(Quaternion.identity, 0f, 0f);
            Quaternion rolled = WheelPose.Compose(Quaternion.identity, 0f, 90f);

            Vector3 rimAtRest = atRest * Vector3.forward;
            Vector3 rimRolled = rolled * Vector3.forward;

            Assert.AreEqual(0f, rimAtRest.x, 1e-4f);
            Assert.AreEqual(0f, rimRolled.x, 1e-4f, "Rim moved sideways — rolling axis is wrong.");
            Assert.AreEqual(90f, Vector3.Angle(rimAtRest, rimRolled), 1e-3f);
        }

        [Test]
        public void Compose_FollowsTheCarRotation()
        {
            Quaternion carRotation = Quaternion.Euler(0f, 90f, 0f);
            Quaternion pose = WheelPose.Compose(carRotation, 0f, 0f);

            // With the car turned 90 degrees, the axle points along world Z.
            Vector3 worldAxle = pose * Vector3.up;
            Assert.AreEqual(0f, worldAxle.y, 1e-4f);
            Assert.AreEqual(1f, Mathf.Abs(worldAxle.z), 1e-4f);
        }

        [Test]
        public void Compose_SurvivesCarPitchAndRoll()
        {
            // On a ramp the car is pitched; the axle must stay perpendicular to the car's
            // forward axis, not to the world.
            Quaternion carRotation = Quaternion.Euler(-20f, 45f, 8f);
            Quaternion pose = WheelPose.Compose(carRotation, 0f, 210f);

            Vector3 worldAxle = pose * Vector3.up;
            Vector3 carRight = carRotation * Vector3.right;
            Assert.AreEqual(1f, Mathf.Abs(Vector3.Dot(worldAxle.normalized, carRight.normalized)), 1e-3f,
                "Axle must stay aligned with the car's right axis regardless of car orientation.");
        }

        [Test]
        public void Advance_IntegratesBySpeedAndTime_NotByStep()
        {
            // 360 deg/s for a tenth of a second is 36 degrees, whatever the frame rate.
            float onOneBigStep = WheelPose.Advance(0f, 360f, 0.1f);
            Assert.AreEqual(36f, onOneBigStep, 1e-3f);

            float accumulated = 0f;
            for (int i = 0; i < 10; i++) accumulated = WheelPose.Advance(accumulated, 360f, 0.01f);
            Assert.AreEqual(36f, accumulated, 1e-3f, "Result must not depend on how the time is sliced.");
        }

        [Test]
        public void Advance_WrapsAround_AndReversesWithNegativeSpeed()
        {
            Assert.AreEqual(10f, WheelPose.Advance(350f, 20f, 1f), 1e-3f);
            Assert.AreEqual(350f, WheelPose.Advance(10f, -20f, 1f), 1e-3f);
        }
    }
}
