using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Drives the car around the road ring on its own.
    ///
    /// Two purposes: it makes performance measurements reproducible (a parked car measures
    /// nothing), and it is the proof that the input layer really is swappable — the physics
    /// controller cannot tell it apart from a human.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class AutoDriver : MonoBehaviour, ICarInputProvider
    {
        [SerializeField] private float _targetRadius = 70f;
        [SerializeField] private float _throttle = 1f;
        [Tooltip("How hard the driver corrects back onto the ring, per meter of error.")]
        [SerializeField] private float _steerGain = 0.08f;

        private CarController _car;

        public CarInputState Current { get; private set; }

        private void Awake()
        {
            _car = GetComponent<CarController>();
            _car.SetInputProvider(this);
        }

        private void Update()
        {
            Vector3 position = transform.position;
            var fromCentre = new Vector2(position.x, position.z);
            float radius = fromCentre.magnitude;

            // Tangent of the ring at the current angle, going counter-clockwise.
            var tangent = new Vector3(-fromCentre.y, 0f, fromCentre.x).normalized;

            // Steer towards the ring: blend the tangent with a correction toward the target radius.
            float radiusError = radius - _targetRadius;
            Vector3 inward = new Vector3(-fromCentre.x, 0f, -fromCentre.y).normalized;
            Vector3 desired = (tangent + inward * Mathf.Clamp(radiusError * _steerGain, -1f, 1f)).normalized;

            Vector3 forward = transform.forward;
            float steer = Mathf.Clamp(Vector3.SignedAngle(forward, desired, Vector3.up) / 30f, -1f, 1f);

            Current = new CarInputState(_throttle, steer, handbrake: false);
        }

        public void Configure(float targetRadius, float throttle)
        {
            _targetRadius = targetRadius;
            _throttle = throttle;
        }
    }
}
