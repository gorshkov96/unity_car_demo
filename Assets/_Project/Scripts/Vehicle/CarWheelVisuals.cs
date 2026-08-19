using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Places the wheel meshes from the physics state published by <see cref="CarController"/>.
    /// Runs in Update for visual smoothness and never touches the Rigidbody — the visual layer
    /// reads physics, never the other way round.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class CarWheelVisuals : MonoBehaviour
    {
        [SerializeField] private Transform[] _wheelTransforms = new Transform[4];

        private CarController _controller;
        private float[] _rollAngles;

        private void Awake()
        {
            _controller = GetComponent<CarController>();
            _rollAngles = new float[_wheelTransforms.Length];
        }

        private void Update()
        {
            System.ReadOnlySpan<WheelState> wheels = _controller.Wheels;
            if (wheels.IsEmpty) return;

            float deltaTime = Time.deltaTime;
            int count = Mathf.Min(wheels.Length, _wheelTransforms.Length);

            for (int i = 0; i < count; i++)
            {
                Transform wheel = _wheelTransforms[i];
                if (wheel == null) continue;

                WheelState state = wheels[i];
                _rollAngles[i] = WheelPose.Advance(_rollAngles[i], state.SpinSpeed, deltaTime);

                // Local placement: the wheels are children of the car, so they inherit the
                // interpolated body transform instead of chasing it a step behind.
                wheel.SetLocalPositionAndRotation(
                    state.LocalPosition,
                    WheelPose.ComposeLocal(state.SteerAngle, _rollAngles[i]));
            }
        }

        public void SetWheelTransforms(Transform[] transforms)
        {
            _wheelTransforms = transforms;
            _rollAngles = new float[transforms.Length];
        }
    }
}
