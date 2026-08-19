using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Kinematic platform sliding back and forth between two points.
    /// Uses a smoothed (cosine) curve so it eases at the ends instead of
    /// snapping direction, which would launch anything standing on it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MovingPlatform : MonoBehaviour
    {
        [SerializeField] private Vector3 _travel = new Vector3(0f, 0f, 14f);
        [SerializeField] private float _period = 8f;
        [SerializeField] private float _phase;

        private Rigidbody _rigidbody;
        private Vector3 _origin;
        private float _time;

        public void Configure(Vector3 travel, float period, float phase)
        {
            _travel = travel;
            _period = Mathf.Max(0.1f, period);
            _phase = phase;
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _origin = transform.position;
            _time = _phase * _period;
        }

        private void FixedUpdate()
        {
            _time += Time.fixedDeltaTime;
            float t = (1f - Mathf.Cos(_time / _period * 2f * Mathf.PI)) * 0.5f;
            _rigidbody.MovePosition(_origin + _travel * t);
        }
    }
}
