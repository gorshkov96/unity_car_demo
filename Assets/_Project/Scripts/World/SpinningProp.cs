using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Kinematic rotating obstacle. Moved with MovePosition/MoveRotation so the
    /// physics engine reports correct contact velocities to the car instead of
    /// teleporting through it.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SpinningProp : MonoBehaviour
    {
        [SerializeField] private Vector3 _axis = Vector3.up;
        [SerializeField] private float _degreesPerSecond = 45f;

        private Rigidbody _rigidbody;

        public void Configure(Vector3 axis, float degreesPerSecond)
        {
            _axis = axis;
            _degreesPerSecond = degreesPerSecond;
        }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _rigidbody.isKinematic = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        }

        private void FixedUpdate()
        {
            Quaternion delta = Quaternion.AngleAxis(_degreesPerSecond * Time.fixedDeltaTime, _axis.normalized);
            _rigidbody.MoveRotation(_rigidbody.rotation * delta);
        }
    }
}
