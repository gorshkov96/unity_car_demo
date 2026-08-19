using UnityEngine;
using UnityEngine.InputSystem;

namespace CarDemo.Game
{
    /// <summary>
    /// Puts the car back on its wheels at the spawn point (R key), and does it
    /// automatically if it ends up upside down and motionless.
    /// A demo where you can flip the car and get stuck is a demo nobody finishes.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarResetter : MonoBehaviour
    {
        [SerializeField] private float _autoResetDelay = 1.2f;
        [Tooltip("Car counts as flipped when its up axis points this far below horizontal.")]
        [SerializeField] private float _flippedDot = 0.1f;
        [Tooltip("Speed below which the car counts as settled, m/s, squared.")]
        [SerializeField] private float _stillSpeedSquared = 9f;

        [Header("Self-righting")]
        [Tooltip("Roll torque used to flip the car back over, in acceleration units.")]
        [SerializeField] private float _recoveryTorque = 30f;
        [Tooltip("Upward push while rolling, so the roof does not drag.")]
        [SerializeField] private float _recoveryLift = 9f;
        [Tooltip("Car counts as recovered once its up axis is this close to vertical.")]
        [SerializeField] private float _recoveredDot = 0.6f;
        [SerializeField] private float _recoveryTimeout = 3f;
        [Tooltip("Righting kicks in after this long upside down even if the car is still "
                 + "sliding, so a roof-first landing at speed does not end the run.")]
        [SerializeField] private float _slidingResetDelay = 2f;

        private Rigidbody _rigidbody;
        private InputAction _resetAction;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private float _flippedTime;
        private bool _recovering;
        private float _recoveryTime;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
        }

        private void OnEnable()
        {
            // Through the actions asset rather than Keyboard.current, so a gamepad can reset
            // the car too and the binding stays rebindable.
            _resetAction = InputSystem.actions?.FindAction("Reset", throwIfNotFound: false);
            _resetAction?.Enable();
        }

        private void OnDisable()
        {
            _resetAction?.Disable();
        }

        private void Update()
        {
            // WasPressedThisFrame belongs in Update: in FixedUpdate it would miss presses or
            // fire twice, because FixedUpdate runs zero or several times per frame.
            if (_resetAction != null && _resetAction.WasPressedThisFrame())
            {
                ResetCar();
                return;
            }

            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Watches for the car ending up on its roof. Takes the step explicitly so headless
        /// simulation can drive it too — a demo that quietly leaves the car upside down for
        /// half the run is broken, and only a test that runs this will notice.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

            float uprightness = Vector3.Dot(transform.up, Vector3.up);

            if (_recovering)
            {
                _recoveryTime += deltaTime;
                ApplyRecoveryTorque();

                // Done as soon as the car is back on its wheels, or bail out if the roll is
                // going nowhere — better to stop pushing than to spin a wedged car forever.
                if (uprightness > _recoveredDot || _recoveryTime > _recoveryTimeout)
                {
                    _recovering = false;
                    _recoveryTime = 0f;
                }

                return;
            }

            bool flipped = uprightness < _flippedDot;
            if (!flipped)
            {
                _flippedTime = 0f;
                return;
            }

            _flippedTime += deltaTime;
            bool nearlyStill = _rigidbody.linearVelocity.sqrMagnitude < _stillSpeedSquared;

            // Two ways in: the car has come to rest on its roof, or it has been upside down
            // for a while regardless of speed. The second case matters at speed, where a car
            // sliding along on its roof never counts as "settled" and would otherwise stay
            // there for the rest of the run.
            if ((nearlyStill && _flippedTime >= _autoResetDelay) || _flippedTime >= _slidingResetDelay)
            {
                _flippedTime = 0f;
                _recovering = true;
                _recoveryTime = 0f;
            }
        }

        /// <summary>
        /// Rolls the car back onto its wheels with torque instead of teleporting it.
        ///
        /// A snap-back reset is jarring and, on a point-to-point track, throws away the run.
        /// A physical roll keeps the car where it earned its position, reads as part of the
        /// game rather than a correction, and works from a full roof landing.
        /// </summary>
        private void ApplyRecoveryTorque()
        {
            // Direction that brings the car's up axis back towards world up, expressed as a
            // roll about its own length.
            Vector3 toUpright = Vector3.Cross(transform.up, Vector3.up);
            float rollDirection = Vector3.Dot(toUpright, transform.forward);

            // Dead flat on the roof gives no preferred side, so pick one and commit.
            if (Mathf.Abs(rollDirection) < 0.05f) rollDirection = 1f;

            _rigidbody.AddTorque(
                transform.forward * (Mathf.Sign(rollDirection) * _recoveryTorque),
                ForceMode.Acceleration);

            // A little lift so the roof is not dragging while the car rotates.
            _rigidbody.AddForce(Vector3.up * _recoveryLift, ForceMode.Acceleration);
        }

        /// <summary>Instant reset to the spawn point, on the player's request.</summary>
        public void RecoverInPlace()
        {
            _recovering = true;
            _recoveryTime = 0f;
        }

        public void ResetCar()
        {
            _flippedTime = 0f;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            // Lift slightly so the car does not respawn intersecting the ground.
            _rigidbody.position = _spawnPosition + Vector3.up * 0.5f;
            _rigidbody.rotation = _spawnRotation;
            transform.SetPositionAndRotation(_rigidbody.position, _rigidbody.rotation);
        }
    }
}
