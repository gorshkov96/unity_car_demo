using UnityEngine;
using UnityEngine.InputSystem;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Reads the driving actions and smooths the digital keys into analog values.
    ///
    /// Bindings come from the project-wide Input Actions asset rather than from direct device
    /// polling: rebinding, control schemes and gamepad dead zones then live in the asset, and
    /// the code stops caring which device is connected.
    ///
    /// Input is read in Update and buffered; <see cref="CarController"/> applies it in
    /// FixedUpdate, which may run zero, one or several times per frame.
    /// </summary>
    public sealed class PlayerCarInput : MonoBehaviour, ICarInputProvider
    {
        [Header("Smoothing (units per second)")]
        [Tooltip("Higher values reach full input sooner. These are tuned for a car that has to "
                 + "feel immediate: full throttle in ~0.15 s, full lock in ~0.11 s.")]
        [SerializeField] private float _throttleRise = 7f;
        [SerializeField] private float _throttleFall = 12f;
        [SerializeField] private float _steerRise = 9f;
        [SerializeField] private float _steerFall = 16f;

        private InputAction _steerAction;
        private InputAction _throttleAction;
        private InputAction _brakeAction;
        private InputAction _handbrakeAction;

        private float _throttle;
        private float _steer;
        private bool _handbrake;

        public CarInputState Current => new CarInputState(_throttle, _steer, _handbrake);

        private void OnEnable()
        {
            InputActionAsset actions = InputSystem.actions;
            if (actions == null)
            {
                Debug.LogError("[CarDemo] No project-wide Input Actions asset assigned; car input is dead.", this);
                enabled = false;
                return;
            }

            _steerAction = FindAndEnable(actions, "Steer");
            _throttleAction = FindAndEnable(actions, "Throttle");
            _brakeAction = FindAndEnable(actions, "Brake");
            _handbrakeAction = FindAndEnable(actions, "Handbrake");
        }

        private void OnDisable()
        {
            _steerAction?.Disable();
            _throttleAction?.Disable();
            _brakeAction?.Disable();
            _handbrakeAction?.Disable();
        }

        private static InputAction FindAndEnable(InputActionAsset actions, string name)
        {
            InputAction action = actions.FindAction(name, throwIfNotFound: false);
            if (action == null)
            {
                Debug.LogError($"[CarDemo] Input action '{name}' not found in the project-wide asset.");
                return null;
            }

            action.Enable();
            return action;
        }

        private void Update()
        {
            if (_steerAction == null) return;

            // Throttle and brake are separate axes: on a gamepad or a wheel they are two
            // independent pedals, and merging them would make partial-brake-while-accelerating
            // impossible to express.
            float throttleTarget = _throttleAction.ReadValue<float>() - _brakeAction.ReadValue<float>();
            float steerTarget = _steerAction.ReadValue<float>();

            float deltaTime = Time.deltaTime;
            _throttle = InputSmoothing.Step(_throttle, Mathf.Clamp(throttleTarget, -1f, 1f), _throttleRise, _throttleFall, deltaTime);
            _steer = InputSmoothing.Step(_steer, Mathf.Clamp(steerTarget, -1f, 1f), _steerRise, _steerFall, deltaTime);
            _handbrake = _handbrakeAction != null && _handbrakeAction.IsPressed();
        }
    }
}
