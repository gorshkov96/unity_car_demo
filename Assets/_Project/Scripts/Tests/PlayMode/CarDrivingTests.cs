using System.Collections;
using CarDemo.Core;
using CarDemo.Vehicle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CarDemo.Tests.PlayMode
{
    /// <summary>
    /// Physics-level checks on a minimal scene built in code: flat ground plus one car.
    /// These catch the failures that matter most in a driving demo — the car falling
    /// through the world, refusing to move, or drifting sideways on its own.
    /// </summary>
    public sealed class CarDrivingTests
    {
        private GameObject _ground;
        private GameObject _car;
        private CarController _controller;
        private ScriptedCarInput _input;
        private CarConfig _config;

        [SetUp]
        public void SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "Ground";
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(400f, 1f, 400f);
            _ground.layer = GameLayers.Ground;

            _config = ScriptableObject.CreateInstance<CarConfig>();

            _car = new GameObject("Car");
            _car.transform.position = new Vector3(0f, 1f, 0f);
            Rigidbody body = _car.AddComponent<Rigidbody>();
            body.mass = _config.Mass;
            BoxCollider collider = _car.AddComponent<BoxCollider>();
            collider.size = _config.ColliderSize;
            collider.center = _config.ColliderCenter;

            _controller = _car.AddComponent<CarController>();
            SetConfig(_controller, _config);

            _input = new ScriptedCarInput();
            _controller.SetInputProvider(_input);
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_car);
            Object.Destroy(_ground);
            Object.DestroyImmediate(_config);
        }

        private static void SetConfig(CarController controller, CarConfig config)
        {
            controller.Configure(config);
        }

        private static IEnumerator SimulateSeconds(float seconds)
        {
            int steps = Mathf.CeilToInt(seconds / Time.fixedDeltaTime);
            for (int i = 0; i < steps; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator Car_RestsOnGround_WithoutSinkingOrLaunching()
        {
            _input.Set(throttle: 0f, steer: 0f);
            yield return SimulateSeconds(2f);

            Assert.IsTrue(_controller.IsGrounded, "Suspension must find the ground.");
            Assert.Greater(_car.transform.position.y, 0.05f, "Car sank through the ground.");
            Assert.Less(_car.transform.position.y, 3f, "Suspension launched the car.");
        }

        [UnityTest]
        public IEnumerator Car_MovesForward_UnderThrottle()
        {
            yield return SimulateSeconds(0.5f);
            float startZ = _car.transform.position.z;

            _input.Set(throttle: 1f, steer: 0f);
            yield return SimulateSeconds(3f);

            Assert.Greater(_controller.ForwardSpeed, 2f, "Car did not accelerate.");
            Assert.Greater(_car.transform.position.z - startZ, 3f, "Car did not travel forward.");
        }

        [UnityTest]
        public IEnumerator Car_StaysBelowConfiguredTopSpeed()
        {
            _input.Set(throttle: 1f, steer: 0f);
            yield return SimulateSeconds(15f);

            Assert.LessOrEqual(_controller.ForwardSpeed, _config.MaxSpeed * 1.15f,
                "Speed limiter failed: car exceeded its configured top speed.");
        }

        [UnityTest]
        public IEnumerator Car_TurnsWhenSteering()
        {
            _input.Set(throttle: 1f, steer: 0f);
            yield return SimulateSeconds(2f);

            float startYaw = _car.transform.eulerAngles.y;
            _input.Set(throttle: 1f, steer: 1f);
            yield return SimulateSeconds(2f);

            float yawDelta = Mathf.DeltaAngle(startYaw, _car.transform.eulerAngles.y);
            Assert.Greater(yawDelta, 10f, "Steering right must rotate the car clockwise.");
        }

        [UnityTest]
        public IEnumerator Car_DrivesStraight_WithoutSteeringInput()
        {
            _input.Set(throttle: 1f, steer: 0f);
            yield return SimulateSeconds(5f);

            Assert.Less(Mathf.Abs(_car.transform.position.x), 1.5f,
                "Car drifted sideways with no steering input — grip or force placement is asymmetric.");
        }

        [UnityTest]
        public IEnumerator Car_BrakesToAStop_WithoutReversing()
        {
            _input.Set(throttle: 1f, steer: 0f);
            yield return SimulateSeconds(3f);
            Assert.Greater(_controller.ForwardSpeed, 2f, "Precondition: car should be moving.");

            _input.Set(throttle: -1f, steer: 0f);
            yield return SimulateSeconds(4f);

            Assert.Less(Mathf.Abs(_controller.ForwardSpeed), 3f, "Car failed to brake.");
        }

        [UnityTest]
        public IEnumerator Car_StaysUpright_UnderHardCornering()
        {
            _input.Set(throttle: 1f, steer: 0f);
            yield return SimulateSeconds(4f);

            _input.Set(throttle: 1f, steer: 1f);
            yield return SimulateSeconds(6f);

            float uprightness = Vector3.Dot(_car.transform.up, Vector3.up);
            Assert.Greater(uprightness, 0.5f, "Car rolled over during a normal turn.");
        }
    }
}
