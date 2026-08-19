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
        [SerializeField] private float _autoResetDelay = 3f;
        [Tooltip("Car counts as flipped when its up axis points this far below horizontal.")]
        [SerializeField] private float _flippedDot = 0.1f;

        private Rigidbody _rigidbody;
        private InputAction _resetAction;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private float _flippedTime;

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

            bool flipped = Vector3.Dot(transform.up, Vector3.up) < _flippedDot;
            bool nearlyStill = _rigidbody.linearVelocity.sqrMagnitude < 1f;

            if (flipped && nearlyStill)
            {
                _flippedTime += Time.deltaTime;
                if (_flippedTime >= _autoResetDelay)
                {
                    ResetCar();
                }
            }
            else
            {
                _flippedTime = 0f;
            }
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
