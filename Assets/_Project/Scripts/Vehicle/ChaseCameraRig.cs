using Unity.Cinemachine;
using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Tunes a Cinemachine rig for chase-camera behaviour: the camera pulls back as the car
    /// speeds up, which is what sells the sense of velocity.
    ///
    /// The rig itself (CinemachineCamera + OrbitalFollow + RotationComposer) does the
    /// following and aiming — this component only drives the parameters that depend on the
    /// car's state, instead of reimplementing a camera by hand.
    /// </summary>
    [RequireComponent(typeof(CinemachineCamera))]
    public sealed class ChaseCameraRig : MonoBehaviour
    {
        [SerializeField] private CarController _car;
        [Tooltip("Follow distance when standing still, meters.")]
        [SerializeField] private float _restDistance = 7.5f;
        [Tooltip("Extra distance at top speed, meters.")]
        [SerializeField] private float _speedPullback = 2.5f;
        [SerializeField] private float _distanceSmoothing = 3f;

        [Header("Slope adaptation")]
        [Tooltip("Camera height angle on flat ground, degrees.")]
        [SerializeField] private float _baseVerticalAngle = 14f;
        [Tooltip("How much of the car's pitch is added to the camera angle. On a descent the "
                 + "track falls away below a level camera, so the camera has to look down it.")]
        [SerializeField] private float _slopeAdaptation = 0.85f;
        [SerializeField] private float _slopeSmoothing = 2.5f;
        [SerializeField] private float _maxVerticalAngle = 42f;

        private CinemachineOrbitalFollow _follow;
        private CinemachineRotationComposer _composer;
        private Rigidbody _body;
        private Vector3 _baseTargetOffset;
        private float _currentPullback;
        private float _currentVerticalAngle;

        public void SetCar(CarController car)
        {
            _car = car;
            _body = car != null ? car.GetComponent<Rigidbody>() : null;
        }

        private void Awake()
        {
            _follow = GetComponent<CinemachineOrbitalFollow>();
            _composer = GetComponent<CinemachineRotationComposer>();
            if (_body == null && _car != null) _body = _car.GetComponent<Rigidbody>();

            if (_follow != null)
            {
                _restDistance = _follow.Radius;
                _currentVerticalAngle = _follow.VerticalAxis.Value;
            }

            if (_composer != null)
            {
                _baseTargetOffset = _composer.TargetOffset;
            }
        }

        private void LateUpdate()
        {
            if (_follow == null || _car == null || _car.Config == null) return;

            float speedFactor = Mathf.Clamp01(Mathf.Abs(_car.ForwardSpeed) / Mathf.Max(0.01f, _car.Config.MaxSpeed));
            float targetPullback = _speedPullback * speedFactor;

            // Exponential smoothing is frame-rate independent, unlike a raw Lerp with a constant.
            _currentPullback = Mathf.Lerp(_currentPullback, targetPullback,
                1f - Mathf.Exp(-_distanceSmoothing * Time.deltaTime));

            _follow.Radius = _restDistance + _currentPullback;

            AdaptToSlope();
        }

        /// <summary>
        /// Raises the camera as the car noses downhill.
        ///
        /// A camera locked to world up looks at the horizon, and on a descent the horizon is
        /// exactly where the track is not: the road falls out of frame and the player cannot
        /// see what they are driving into. Feeding the car's pitch into the camera's height
        /// angle keeps the track ahead in view.
        /// </summary>
        private void AdaptToSlope()
        {
            // Pitch is taken from where the car is going, not from where it is pointing.
            // Reading the body's forward axis would tilt the camera through every roll and
            // flip the view upside down the moment the car did.
            Vector3 velocity = _body != null ? _body.linearVelocity : Vector3.zero;
            float pitch = velocity.sqrMagnitude > 4f
                ? -Mathf.Asin(Mathf.Clamp(velocity.normalized.y, -1f, 1f)) * Mathf.Rad2Deg
                : 0f;

            float target = Mathf.Clamp(
                _baseVerticalAngle + Mathf.Max(0f, pitch) * _slopeAdaptation,
                -_maxVerticalAngle, _maxVerticalAngle);

            _currentVerticalAngle = Mathf.Lerp(_currentVerticalAngle, target,
                1f - Mathf.Exp(-_slopeSmoothing * Time.deltaTime));

            _follow.VerticalAxis.Value = _currentVerticalAngle;

            if (_composer == null) return;

            // Look further down the slope as it steepens, so the aim point sits on the track
            // rather than above it.
            Vector3 offset = _baseTargetOffset;
            offset.z = _baseTargetOffset.z + Mathf.Max(0f, pitch) * 0.06f;
            offset.y = _baseTargetOffset.y - Mathf.Max(0f, pitch) * 0.02f;
            _composer.TargetOffset = offset;
        }
    }
}
