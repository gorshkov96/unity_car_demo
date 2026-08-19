using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Kinematic barrier sliding side to side across the track. Moved with MovePosition so the
    /// physics engine reports a real contact velocity and can actually shove the car.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SweepingBarrier : MonoBehaviour
    {
        [SerializeField] private float _travel = 20f;
        [SerializeField] private float _period = 5f;
        [SerializeField] private float _phase;

        private Rigidbody _rigidbody;
        private Vector3 _origin;
        private Vector3 _axis = Vector3.right;
        private float _time;

        public void Configure(float travel, float period, float phase)
        {
            _travel = travel;
            _period = Mathf.Max(0.2f, period);
            _phase = phase;
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            // Kinematic bodies only support speculative continuous detection, and without it
            // a car arriving at 45 m/s can be missed entirely between physics steps.
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _origin = transform.position;
            // Sweeps across the track, in the slope's local right direction.
            _axis = transform.parent != null ? transform.parent.right : Vector3.right;
            _time = _phase * _period;
        }

        private void FixedUpdate()
        {
            _time += Time.fixedDeltaTime;
            float offset = Mathf.Sin(_time / _period * 2f * Mathf.PI) * _travel * 0.5f;
            _rigidbody.MovePosition(_origin + _axis * offset);
        }
    }
}
