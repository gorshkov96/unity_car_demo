using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// All tunable numbers for one car, kept as an asset so designers can tweak
    /// without recompiling and multiple cars can share or fork setups.
    /// </summary>
    [CreateAssetMenu(fileName = "CarConfig", menuName = "CarDemo/Car Config")]
    public sealed class CarConfig : ScriptableObject
    {
        [Header("Chassis")]
        [Tooltip("Cap on how fast the engine may push the car out of an overlap, m/s. The "
                 + "engine default is a catapult and throws the car through the track.")]
        [SerializeField] private float _maxDepenetrationVelocity = 2f;
        [Tooltip("Physics solver iterations for the car only. The project default is left low; "
                 + "the player's car gets more so that high-speed impacts resolve without "
                 + "pushing it through the track.")]
        [SerializeField] private int _solverIterations = 12;
        [SerializeField] private float _mass = 1200f;
        [SerializeField] private Vector3 _centerOfMassOffset = new Vector3(0f, -0.35f, 0f);
        [SerializeField] private Vector3 _colliderSize = new Vector3(1.8f, 0.8f, 4.2f);
        [SerializeField] private Vector3 _colliderCenter = new Vector3(0f, 0.4f, 0f);

        [Header("Wheel layout")]
        [SerializeField] private float _wheelbase = 2.7f;
        [SerializeField] private float _track = 1.6f;
        [SerializeField] private float _wheelRadius = 0.35f;
        [Tooltip("Wheel axle height relative to the car origin. Negative = axles below the body centre.")]
        [SerializeField] private float _wheelVerticalOffset = -0.2f;
        [SerializeField] private float _wheelWidth = 0.25f;

        [Tooltip("Wheel collider radius as a fraction of the wheel radius. Below 1 so the "
                 + "spheres stay clear of the ground and leave the suspension in charge.")]
        [Range(0.4f, 1f)]
        [SerializeField] private float _wheelColliderScale = 0.55f;

        [Header("Suspension")]
        [Tooltip("Suspension travel below the anchor at rest, meters.")]
        [SerializeField] private float _suspensionRestLength = 0.3f;
        [Tooltip("Upward force at full compression, newtons. Sized so the car rests near 30% compression: mass*g / (wheels * 0.3).")]
        [SerializeField] private float _springStrength = 10000f;
        [Tooltip("Damping force per m/s of vertical anchor velocity. Too low and the car pogos on every bump.")]
        [SerializeField] private float _damperStrength = 2600f;

        [Header("Drive")]
        [Tooltip("Total forward force at full throttle, split across driven wheels.")]
        [SerializeField] private float _accelerationForce = 14000f;
        [SerializeField] private float _brakeForce = 12000f;
        [SerializeField] private float _maxSpeed = 38f;
        [SerializeField] private float _maxReverseSpeed = 8f;

        [Header("Steering")]
        [SerializeField] private float _maxSteerAngle = 40f;
        [Tooltip("Steer angle available at max speed; keeps the car stable when fast.")]
        [SerializeField] private float _steerAngleAtMaxSpeed = 22f;

        [Header("Grip")]
        [Range(0f, 1f)]
        [Tooltip("Fraction of lateral slip removed per physics step on front wheels.")]
        [SerializeField] private float _frontGrip = 0.95f;
        [Range(0f, 1f)]
        [SerializeField] private float _rearGrip = 0.85f;
        [Range(0f, 1f)]
        [Tooltip("Rear grip multiplier while the handbrake is held (low value = drift).")]
        [SerializeField] private float _handbrakeGripFactor = 0.25f;
        [Tooltip("Cap on lateral correction acceleration, m/s^2. Prevents infinite grip.")]
        [SerializeField] private float _maxGripAcceleration = 45f;
        [Tooltip("Tyre friction coefficient. Caps cornering force at mu * load, so the car "
                 + "slides when asked for more grip than the weight on the wheel allows. "
                 + "Around 1.0 is a road tyre; above 1.5 the car turns on rails.")]
        [SerializeField] private float _tireFriction = 2.1f;

        [Tooltip("How fast an airborne wheel loses spin, per second. Higher = stops sooner.")]
        [SerializeField] private float _airborneSpinDecay = 0.6f;

        [Header("Wheel slip (visual)")]
        [Tooltip("Extra wheel rotation at full throttle from a standstill, as a fraction of rolling speed.")]
        [SerializeField] private float _maxWheelspin = 1.2f;
        [Tooltip("Speed at which wheelspin has fully faded out, m/s.")]
        [SerializeField] private float _wheelspinFadeSpeed = 12f;

        [Header("Airborne behaviour")]
        [Tooltip("Hard cap on speed, m/s. It exists to stop a violent overlap resolution from "
                 + "flinging the car, not to limit the driving: on a long descent gravity "
                 + "should keep adding speed. Raised once the physics step went to 100 Hz, "
                 + "which halved how far the car travels between collision checks.")]
        [SerializeField] private float _maxLinearVelocity = 66f;
        [Tooltip("How strongly the car levels itself towards the horizon while airborne. "
                 + "An arcade cheat, but every arcade racer has it: without it a jump ends on the roof.")]
        [SerializeField] private float _airLevelTorque = 6.5f;
        [Tooltip("How long after take-off the car is left to rotate freely, seconds. Without "
                 + "this the levelling torque kills every spin the moment it starts.")]
        [SerializeField] private float _airControlDelay = 0.7f;
        [Tooltip("How strongly airborne rotation is damped.")]
        [SerializeField] private float _airAngularDamping = 2.5f;

        [Header("Aero & losses")]
        [Tooltip("Downward force per m/s of speed.")]
        [SerializeField] private float _downforce = 15f;
        [Tooltip("Longitudinal drag per m/s per grounded wheel, newtons.")]
        [SerializeField] private float _rollingResistance = 7f;

        [Header("Visuals (optional, primitives keep defaults when null)")]
        [SerializeField] private Material _bodyMaterial;
        [SerializeField] private Material _wheelMaterial;
        [Tooltip("Dark plastic and chrome: bumpers, mirrors, exhausts, pillars.")]
        [SerializeField] private Material _trimMaterial;
        [SerializeField] private Material _glassMaterial;
        [SerializeField] private Material _headlightMaterial;
        [SerializeField] private Material _taillightMaterial;
        [SerializeField] private Material _rimMaterial;
        [SerializeField] private Vector3 _bodySize = new Vector3(1.7f, 0.55f, 4.0f);
        [SerializeField] private Vector3 _cabinSize = new Vector3(1.5f, 0.45f, 1.8f);

        public float Mass => _mass;
        public int SolverIterations => Mathf.Max(1, _solverIterations);
        public float MaxDepenetrationVelocity => _maxDepenetrationVelocity;
        public Vector3 CenterOfMassOffset => _centerOfMassOffset;
        public Vector3 ColliderSize => _colliderSize;
        public Vector3 ColliderCenter => _colliderCenter;
        public float WheelRadius => _wheelRadius;
        public float WheelVerticalOffset => _wheelVerticalOffset;
        public float WheelColliderScale => _wheelColliderScale;
        public float WheelWidth => _wheelWidth;
        public float SuspensionRestLength => _suspensionRestLength;
        public float SpringStrength => _springStrength;
        public float DamperStrength => _damperStrength;
        public float AccelerationForce => _accelerationForce;
        public float BrakeForce => _brakeForce;
        public float MaxSpeed => _maxSpeed;
        public float MaxReverseSpeed => _maxReverseSpeed;
        public float MaxSteerAngle => _maxSteerAngle;
        public float SteerAngleAtMaxSpeed => _steerAngleAtMaxSpeed;
        public float FrontGrip => _frontGrip;
        public float RearGrip => _rearGrip;
        public float HandbrakeGripFactor => _handbrakeGripFactor;
        public float MaxGripAcceleration => _maxGripAcceleration;
        public float TireFriction => _tireFriction;
        public float AirborneSpinDecay => _airborneSpinDecay;
        public float MaxWheelspin => _maxWheelspin;
        public float WheelspinFadeSpeed => Mathf.Max(0.1f, _wheelspinFadeSpeed);
        public float MaxLinearVelocity => _maxLinearVelocity;
        public float AirLevelTorque => _airLevelTorque;
        public float AirControlDelay => _airControlDelay;
        public float AirAngularDamping => _airAngularDamping;
        public float Downforce => _downforce;
        public float RollingResistance => _rollingResistance;
        public Material BodyMaterial => _bodyMaterial;
        public Material WheelMaterial => _wheelMaterial;
        public Material TrimMaterial => _trimMaterial != null ? _trimMaterial : _wheelMaterial;
        public Material GlassMaterial => _glassMaterial;
        public Material HeadlightMaterial => _headlightMaterial;
        public Material TaillightMaterial => _taillightMaterial;
        public Material RimMaterial => _rimMaterial;
        public Vector3 BodySize => _bodySize;
        public Vector3 CabinSize => _cabinSize;

#if UNITY_EDITOR
        /// <summary>Editor-only hook so the scene generator can wire generated materials.</summary>
        public void SetEditorMaterials(
            Material body, Material wheel, Material trim, Material glass,
            Material headlight, Material taillight, Material rim)
        {
            _bodyMaterial = body;
            _wheelMaterial = wheel;
            _trimMaterial = trim;
            _glassMaterial = glass;
            _headlightMaterial = headlight;
            _taillightMaterial = taillight;
            _rimMaterial = rim;
        }
#endif

        /// <summary>
        /// Wheel anchors in car-local space. Front wheels steer, rear wheels drive.
        /// Single source of truth shared by physics and visuals.
        /// </summary>
        public WheelLayout[] BuildWheelLayout()
        {
            float halfBase = _wheelbase * 0.5f;
            float halfTrack = _track * 0.5f;
            float y = _wheelVerticalOffset;
            return new[]
            {
                new WheelLayout(new Vector3(-halfTrack, y, halfBase), steerable: true, driven: false),
                new WheelLayout(new Vector3(halfTrack, y, halfBase), steerable: true, driven: false),
                new WheelLayout(new Vector3(-halfTrack, y, -halfBase), steerable: false, driven: true),
                new WheelLayout(new Vector3(halfTrack, y, -halfBase), steerable: false, driven: true),
            };
        }
    }

    /// <summary>Static description of one wheel's mounting point and role.</summary>
    public readonly struct WheelLayout
    {
        public readonly Vector3 LocalPosition;
        public readonly bool Steerable;
        public readonly bool Driven;

        public WheelLayout(Vector3 localPosition, bool steerable, bool driven)
        {
            LocalPosition = localPosition;
            Steerable = steerable;
            Driven = driven;
        }
    }
}
