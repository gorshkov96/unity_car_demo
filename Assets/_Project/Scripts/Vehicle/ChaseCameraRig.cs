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

        private CinemachineOrbitalFollow _follow;
        private float _currentPullback;

        public void SetCar(CarController car) => _car = car;

        private void Awake()
        {
            _follow = GetComponent<CinemachineOrbitalFollow>();
            if (_follow != null)
            {
                _restDistance = _follow.Radius;
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
        }
    }
}
