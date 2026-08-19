using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// The thing the chase camera actually follows: it sits on the car but is aimed along the
    /// car's direction of travel, and it deliberately knows nothing about which way the car is
    /// facing.
    ///
    /// That independence is the whole point. Following the car's own transform means a barrel
    /// roll spins the camera with the body, and a car that lands facing sideways drags the view
    /// with it. Aiming at velocity instead keeps the camera behind the line of travel through
    /// rolls, spins and landings — the player keeps seeing where they are going, which is the
    /// only thing they can act on.
    /// </summary>
    public sealed class ChaseCameraTarget : MonoBehaviour
    {
        [SerializeField] private CarController _car;
        [SerializeField] private Rigidbody _body;

        [Tooltip("Speed below which the heading is held rather than recomputed, m/s. A car "
                 + "that is barely moving has no meaningful direction of travel.")]
        [SerializeField] private float _minSpeed = 2.5f;

        [Tooltip("How fast the aim catches up, per second. Low enough to stay calm during a "
                 + "spin, high enough not to lag behind a corner.")]
        [SerializeField] private float _turnSmoothing = 3.5f;

        [Header("Reversing")]
        [Tooltip("Reverse speed that counts as actually driving backwards, m/s.")]
        [SerializeField] private float _reverseSpeed = 4f;
        [Tooltip("How long it must be reversing before the camera swings round, seconds. "
                 + "Stops the view flipping during a three-point turn.")]
        [SerializeField] private float _reverseHold = 0.6f;

        private Vector3 _heading = Vector3.forward;
        private Quaternion _current = Quaternion.identity;
        private float _reversingTime;

        public void Configure(CarController car, Rigidbody body)
        {
            _car = car;
            _body = body;
        }

        private void Awake()
        {
            if (_car != null)
            {
                _heading = Flatten(_car.transform.forward);
                _current = Quaternion.LookRotation(_heading, Vector3.up);
            }
        }

        private void LateUpdate()
        {
            if (_car == null || _body == null) return;

            transform.position = _car.transform.position;

            Vector3 travel = Flatten(_body.linearVelocity);
            float speed = travel.magnitude;

            if (speed > _minSpeed)
            {
                // Reversing gets a hold time before the camera commits: a short shunt
                // backwards should not whip the view around and back again.
                bool goingBackwards = Vector3.Dot(_body.linearVelocity, _car.transform.forward) < -_reverseSpeed;
                _reversingTime = goingBackwards ? _reversingTime + Time.deltaTime : 0f;

                if (!goingBackwards || _reversingTime > _reverseHold)
                {
                    _heading = travel / speed;
                }
            }
            else
            {
                // Too slow to have a direction: hold the last one. Note that the car's own
                // forward is NOT used as a fallback — while it is tumbling to a stop that
                // would spin the camera through every rotation of the car.
                _reversingTime = 0f;
            }

            Quaternion desired = Quaternion.LookRotation(_heading, Vector3.up);
            _current = Quaternion.Slerp(_current, desired, 1f - Mathf.Exp(-_turnSmoothing * Time.deltaTime));
            transform.rotation = _current;
        }

        /// <summary>
        /// Drops the vertical component. The camera stays level with the horizon: following
        /// the vertical part of the velocity would point it at the sky on every jump and at
        /// the ground on every landing.
        /// </summary>
        private static Vector3 Flatten(Vector3 direction)
        {
            Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
            return flat.sqrMagnitude > 1e-6f ? flat : Vector3.forward;
        }
    }
}
