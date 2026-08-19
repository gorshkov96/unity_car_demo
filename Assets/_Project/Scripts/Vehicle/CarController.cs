using CarDemo.Core;
using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Arcade car physics on a single Rigidbody with four raycast suspension points.
    /// Chosen over WheelCollider deliberately: fewer hidden parameters, stable on
    /// procedurally generated ground, and predictable to tune.
    ///
    /// Every force is applied in FixedUpdate. Visuals are driven separately by
    /// <see cref="CarWheelVisuals"/> reading the per-wheel state published here.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        [SerializeField] private CarConfig _config;
        [Tooltip("Layers the suspension is allowed to stand on. Never leave this as Everything: "
                 + "the ray would accept the car's own chassis and any loose prop.")]
        [SerializeField] private LayerMask _groundMask;

        private Rigidbody _rigidbody;
        private ICarInputProvider _inputProvider;
        private WheelLayout[] _layout;
        private WheelState[] _wheels;
        private float _steerAngle;
        private int _drivenWheelCount;
        private float _airborneTime;
        private float _wheelMassShare;

        /// <summary>
        /// Per-wheel results of the last physics step, for visuals and UI.
        /// Exposed as a read-only view so consumers cannot write into the controller's state.
        /// </summary>
        public System.ReadOnlySpan<WheelState> Wheels => _wheels;

        /// <summary>Signed speed along the car's forward axis, m/s.</summary>
        public float ForwardSpeed => _rigidbody == null
            ? 0f
            : Vector3.Dot(_rigidbody.linearVelocity, transform.forward);

        public float SpeedKmh => Mathf.Abs(ForwardSpeed) * 3.6f;
        public bool IsGrounded { get; private set; }
        public CarConfig Config => _config;

        /// <summary>
        /// Injects the config before <see cref="Initialize"/>. Used by the scene
        /// generator and by tests that build a car in code.
        /// </summary>
        public void Configure(CarConfig config)
        {
            _config = config;
        }

        /// <summary>
        /// Replaces the input source. Lets tests and future AI drivers steer the same
        /// controller without any player component in the scene.
        /// </summary>
        public void SetInputProvider(ICarInputProvider provider)
        {
            _inputProvider = provider;
        }

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// Caches components and applies the config to the Rigidbody.
        /// Public so headless tools can drive the controller with
        /// <c>Physics.Simulate</c> outside of play mode, where Awake never runs.
        /// </summary>
        public void Initialize()
        {
            _rigidbody = GetComponent<Rigidbody>();

            if (_config == null)
            {
                Debug.LogError($"{nameof(CarController)} on '{name}' has no CarConfig assigned.", this);
                enabled = false;
                return;
            }

            _rigidbody.mass = _config.Mass;
            _rigidbody.centerOfMass = _config.CenterOfMassOffset;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rigidbody.maxLinearVelocity = _config.MaxLinearVelocity;
            // Higher than the project default, on this body only: a deep interpenetration at
            // 170 km/h resolves by ejecting the car, sometimes straight through the floor.
            _rigidbody.solverIterations = _config.SolverIterations;
            _rigidbody.solverVelocityIterations = _config.SolverIterations;

            // Set on the body as well as globally: a Rigidbody keeps whatever value it was
            // created with, so relying on the project default alone leaves the car able to be
            // fired out of an overlap at 10 m/s — which is how it ends up under the track.
            _rigidbody.maxDepenetrationVelocity = _config.MaxDepenetrationVelocity;

            _layout = _config.BuildWheelLayout();
            _wheels = new WheelState[_layout.Length];

            _drivenWheelCount = 0;
            for (int i = 0; i < _layout.Length; i++)
            {
                if (_layout[i].Driven) _drivenWheelCount++;
            }

            _drivenWheelCount = Mathf.Max(1, _drivenWheelCount);
            _wheelMassShare = _rigidbody.mass / _layout.Length;

            if (_groundMask == 0)
            {
                _groundMask = GameLayers.DrivableMask;
            }

            // Not ??=: GetComponent can return a destroyed MonoBehaviour, which compares equal
            // to null through Unity's overloaded operator but is not a null reference, so the
            // null-coalescing assignment would keep it.
            if (_inputProvider == null)
            {
                var component = GetComponent<ICarInputProvider>();
                _inputProvider = component as MonoBehaviour != null ? component : null;
            }

            if (_inputProvider == null)
            {
                _inputProvider = new ScriptedCarInput();
            }
        }

        private void FixedUpdate()
        {
            Tick(Time.fixedDeltaTime);
        }

        /// <summary>
        /// One physics step: suspension, drive, grip and downforce.
        /// Called from FixedUpdate at runtime, and directly by headless simulation
        /// (see the drive smoke test) where no MonoBehaviour loop is running.
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (_rigidbody == null || _layout == null) return;

            CarInputState input = _inputProvider.Current;

            UpdateSteerAngle(input.Steer);

            int groundedCount = 0;
            for (int i = 0; i < _layout.Length; i++)
            {
                if (UpdateWheel(i, input, deltaTime))
                {
                    groundedCount++;
                }
            }

            IsGrounded = groundedCount > 0;

            if (IsGrounded)
            {
                // Downforce scales with speed: keeps the car planted when fast
                // without making it sluggish at a standstill.
                float speed = _rigidbody.linearVelocity.magnitude;
                _rigidbody.AddForce(-transform.up * (speed * _config.Downforce), ForceMode.Force);
            }
            else
            {
                _airborneTime += deltaTime;
                StabiliseInAir();
            }

            if (IsGrounded) _airborneTime = 0f;
        }

        /// <summary>
        /// Levels the car towards the horizon while airborne and damps its tumbling.
        ///
        /// Openly an arcade cheat — real cars have no such control in the air. Without it a
        /// jump is a coin flip that usually lands on the roof, which is not a demo anyone
        /// wants to play. Only the roll and pitch are corrected; yaw is left alone so the
        /// player keeps their heading.
        /// </summary>
        private void StabiliseInAir()
        {
            // Hold off at first. A banked ramp exists to spin the car, and levelling it the
            // instant the wheels leave the ground erases the trick before anyone sees it.
            // After the grace period the correction fades in, so the landing is still on wheels.
            float authority = Mathf.Clamp01((_airborneTime - _config.AirControlDelay) / 0.5f);
            if (authority <= 0f) return;

            Vector3 up = transform.up;
            // Torque that rotates the car's up axis back towards world up.
            Vector3 levelling = Vector3.Cross(up, Vector3.up);
            _rigidbody.AddTorque(
                levelling * (_config.AirLevelTorque * _rigidbody.mass * 0.01f * authority),
                ForceMode.Force);

            // Damp the tumble so the correction settles instead of oscillating.
            _rigidbody.AddTorque(
                -_rigidbody.angularVelocity * (_config.AirAngularDamping * _rigidbody.mass * 0.01f * authority),
                ForceMode.Force);
        }

        private void UpdateSteerAngle(float steerInput)
        {
            // Steering authority shrinks with speed, otherwise a light flick at
            // 100 km/h spins the car instantly.
            float speedFactor = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / Mathf.Max(0.01f, _config.MaxSpeed));
            float maxAngle = Mathf.Lerp(_config.MaxSteerAngle, _config.SteerAngleAtMaxSpeed, speedFactor);
            _steerAngle = steerInput * maxAngle;
        }

        /// <summary>Applies suspension, drive and grip for one wheel. Returns true if grounded.</summary>
        private bool UpdateWheel(int index, CarInputState input, float deltaTime)
        {
            WheelLayout layout = _layout[index];
            Vector3 anchor = transform.TransformPoint(layout.LocalPosition + Vector3.up * _config.SuspensionRestLength);
            Quaternion steerRotation = layout.Steerable
                ? Quaternion.AngleAxis(_steerAngle, transform.up)
                : Quaternion.identity;
            Vector3 wheelForward = steerRotation * transform.forward;
            Vector3 wheelRight = steerRotation * transform.right;

            // SphereCast, not Raycast: a single ray down the centre of a wheel falls into
            // every seam between generated pieces of track and reports "no ground" for a step,
            // which reads as the wheel dropping through the world. A sphere the width of the
            // wheel rolls across those seams the way a real tyre does.
            float castRadius = _config.WheelRadius * 0.9f;
            bool grounded = Physics.SphereCast(
                anchor, castRadius, -transform.up, out RaycastHit hit,
                _config.SuspensionRestLength, _groundMask, QueryTriggerInteraction.Ignore);

            if (!grounded)
            {
                // Airborne: the wheel hangs at full droop and keeps spinning down slowly,
                // the way a free wheel loses speed to bearing friction rather than stopping dead.
                float freeSpin = _wheels[index].SpinSpeed * Mathf.Exp(-_config.AirborneSpinDecay * deltaTime);
                _wheels[index] = new WheelState(
                    WheelLocalPosition(layout, compression: 0f),
                    layout.Steerable ? _steerAngle : 0f,
                    grounded: false,
                    compression: 0f,
                    spinSpeed: freeSpin);
                return false;
            }

            Vector3 contactPoint = hit.point;
            // Velocity is sampled at the contact point, not at the anchor: the car rolls and
            // pitches, so each wheel's contact patch moves at its own speed. Sampling the
            // anchor instead is the classic source of a permanently bouncing car.
            Vector3 pointVelocity = _rigidbody.GetPointVelocity(contactPoint);

            // --- Suspension -------------------------------------------------
            float compression = SuspensionMath.CompressionFromSphere(hit.distance, _config.SuspensionRestLength);
            float verticalVelocity = Vector3.Dot(pointVelocity, transform.up);
            float suspensionForce = SuspensionMath.Force(compression, _config.SpringStrength, verticalVelocity, _config.DamperStrength);
            _rigidbody.AddForceAtPosition(transform.up * suspensionForce, anchor, ForceMode.Force);

            // --- Grip: cancel sideways slip ---------------------------------
            float grip = layout.Steerable ? _config.FrontGrip : _config.RearGrip;
            if (input.Handbrake && !layout.Steerable)
            {
                grip *= _config.HandbrakeGripFactor;
            }

            float lateralSpeed = Vector3.Dot(pointVelocity, wheelRight);

            // Force needed to remove `grip` fraction of the sideways slip this step.
            float desiredAcceleration = -lateralSpeed * grip / deltaTime;
            desiredAcceleration = Mathf.Clamp(desiredAcceleration, -_config.MaxGripAcceleration, _config.MaxGripAcceleration);
            float wheelMassShare = _wheelMassShare;
            float lateralForce = desiredAcceleration * wheelMassShare;

            // Friction circle, simplified: a tyre can only push sideways as hard as the weight
            // on it allows (F <= mu * N), where N is what the spring is currently carrying.
            // Without this cap the cornering force is unbounded, and cancelling sideways slip
            // quietly pumps energy into the car — it reads as the car speeding up mid-corner.
            float maxLateralForce = suspensionForce * _config.TireFriction;
            lateralForce = Mathf.Clamp(lateralForce, -maxLateralForce, maxLateralForce);

            _rigidbody.AddForceAtPosition(wheelRight * lateralForce, contactPoint, ForceMode.Force);

            // --- Drive and brake --------------------------------------------
            float forwardSpeed = Vector3.Dot(pointVelocity, wheelForward);
            float throttle = input.Throttle;
            bool braking = input.Handbrake
                           || (throttle < -0.01f && ForwardSpeed > 0.5f)
                           || (throttle > 0.01f && ForwardSpeed < -0.5f);

            if (braking)
            {
                float brakePerWheel = _config.BrakeForce / _layout.Length;
                float brakeDirection = -Mathf.Sign(forwardSpeed);
                // Never brake past a standstill into reverse acceleration.
                float maxStoppingForce = Mathf.Abs(forwardSpeed) * wheelMassShare / deltaTime;
                float brakeForce = Mathf.Min(brakePerWheel, maxStoppingForce);
                _rigidbody.AddForceAtPosition(wheelForward * (brakeDirection * brakeForce), contactPoint, ForceMode.Force);
            }
            else if (layout.Driven && Mathf.Abs(throttle) > 0.01f)
            {
                float speedLimit = throttle > 0f ? _config.MaxSpeed : _config.MaxReverseSpeed;
                float speedAlongThrottle = throttle > 0f ? ForwardSpeed : -ForwardSpeed;
                if (speedAlongThrottle < speedLimit)
                {
                    // Taper the last 20% of the speed range so the car eases into
                    // its top speed instead of slamming against a wall.
                    float headroom = Mathf.Clamp01((speedLimit - speedAlongThrottle) / (speedLimit * 0.2f));
                    float force = throttle * _config.AccelerationForce / _drivenWheelCount * headroom;
                    _rigidbody.AddForceAtPosition(wheelForward * force, contactPoint, ForceMode.Force);
                }
            }

            // --- Rolling resistance -----------------------------------------
            _rigidbody.AddForceAtPosition(
                wheelForward * (-forwardSpeed * _config.RollingResistance),
                contactPoint,
                ForceMode.Force);

            // Wheel spin. Free rolling is omega = v / r, but a wheel is not always rolling
            // freely: a locked wheel stops dead under the handbrake, and a driven wheel spins
            // faster than the car during a standing start. Both are what the eye reads as
            // traction, so they are modelled here rather than left to a constant.
            float rollingSpin = forwardSpeed / Mathf.Max(0.01f, _config.WheelRadius) * Mathf.Rad2Deg;
            float slip = 0f;
            float spinSpeed;

            bool locked = input.Handbrake && !layout.Steerable;
            if (locked)
            {
                spinSpeed = 0f;
                slip = -1f;
            }
            else if (layout.Driven && throttle > 0.01f)
            {
                // Wheelspin fades out as the car picks up speed: it is dramatic from a
                // standstill and gone by the time the car is rolling properly.
                float speedFactor = 1f - Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / _config.WheelspinFadeSpeed);
                slip = throttle * _config.MaxWheelspin * speedFactor;
                spinSpeed = rollingSpin * (1f + slip);
            }
            else
            {
                spinSpeed = rollingSpin;
            }

            // Position comes from the clamped compression, not from the raw contact point:
            // when something shoves the car into the ground the raw point would put the wheel
            // inside the body, and when the ray overshoots it would drop the wheel through the
            // floor. Suspension travel is the physical limit, so the visual respects it.
            _wheels[index] = new WheelState(
                WheelLocalPosition(layout, compression),
                layout.Steerable ? _steerAngle : 0f,
                grounded: true,
                compression: compression,
                spinSpeed: spinSpeed,
                slip: slip);
            return true;
        }

        /// <summary>
        /// Wheel position in car-local space for a given compression.
        /// At zero compression the wheel hangs at full droop; at full compression it sits at
        /// the suspension anchor.
        /// </summary>
        private Vector3 WheelLocalPosition(WheelLayout layout, float compression)
        {
            return layout.LocalPosition + Vector3.up * (_config.SuspensionRestLength * compression);
        }

        private void OnDrawGizmosSelected()
        {
            if (_config == null) return;

            Gizmos.color = Color.cyan;
            WheelLayout[] layout = _layout ?? _config.BuildWheelLayout();
            foreach (WheelLayout wheel in layout)
            {
                Vector3 anchor = transform.TransformPoint(wheel.LocalPosition + Vector3.up * _config.SuspensionRestLength);
                Gizmos.DrawLine(anchor, anchor - transform.up * (_config.SuspensionRestLength + _config.WheelRadius));
                Gizmos.DrawWireSphere(transform.TransformPoint(wheel.LocalPosition), _config.WheelRadius);
            }
        }
    }

    /// <summary>
    /// Per-wheel result of a physics step, consumed by visuals and UI.
    ///
    /// Publishes a spin *speed* rather than a per-step delta: the physics step and the render
    /// frame run at different rates, so a delta accumulated once per frame would spin the wheel
    /// at a speed that depends on the frame rate. Steering is a plain angle rather than a world
    /// space quaternion, so the visual layer can compose it in the car's local space and stay
    /// correct when the car rolls or pitches.
    /// </summary>
    public readonly struct WheelState
    {
        /// <summary>
        /// Wheel position in the car's local space, not world space.
        ///
        /// This matters: the Rigidbody is interpolated, so between physics steps the rendered
        /// body sits somewhere the physics transform never was. A world position captured in
        /// FixedUpdate would therefore drift against the interpolated body every frame, and the
        /// wheels visibly shake. A local offset rides along with whatever the body is doing.
        /// </summary>
        public readonly Vector3 LocalPosition;

        /// <summary>Steering angle in degrees around the car's up axis. Positive is right.</summary>
        public readonly float SteerAngle;

        /// <summary>Rolling speed in degrees per second. Signed: negative when reversing.</summary>
        public readonly float SpinSpeed;

        public readonly bool Grounded;
        public readonly float Compression;

        /// <summary>
        /// Longitudinal slip: positive when the wheel spins faster than the car is moving
        /// (wheelspin), -1 when the wheel is locked. Effects — smoke, skid marks, tyre
        /// squeal — read this rather than recomputing it.
        /// </summary>
        public readonly float Slip;

        public WheelState(Vector3 localPosition, float steerAngle, bool grounded, float compression, float spinSpeed, float slip = 0f)
        {
            LocalPosition = localPosition;
            SteerAngle = steerAngle;
            Grounded = grounded;
            Compression = compression;
            SpinSpeed = spinSpeed;
            Slip = slip;
        }
    }
}
