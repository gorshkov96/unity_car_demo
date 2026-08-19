using UnityEngine;

namespace CarDemo.Vehicle
{
    /// <summary>
    /// Drives the car down a slope on its own: keeps it pointed downhill, steers around what
    /// it sees ahead, and lifts off when it is about to bury itself in something.
    ///
    /// Exists so the descent can be verified end to end without a human at the keyboard —
    /// a five-minute track is not something to test by hand every time.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public sealed class DescentAutoDriver : MonoBehaviour, ICarInputProvider
    {
        [SerializeField] private Transform _slopeRoot;
        [Tooltip("Base look-ahead distance, meters. Scaled with speed — at 100 km/h a fixed "
                 + "26 m gives under a second to react, which is not driving, it is crashing.")]
        [SerializeField] private float _lookAhead = 22f;
        [Tooltip("Extra look-ahead per m/s of speed.")]
        [SerializeField] private float _lookAheadPerSpeed = 1.6f;
        [SerializeField] private float _feelerSpread = 16f;
        [SerializeField] private float _wideFeelerSpread = 34f;
        [SerializeField] private LayerMask _obstacleMask;
        [Tooltip("Half-width of the track the driver tries to stay within, meters.")]
        [SerializeField] private float _trackHalfWidth = 34f;

        [Header("Unsticking")]
        [Tooltip("Speed below which the car counts as stuck, m/s.")]
        [SerializeField] private float _stuckSpeed = 2f;
        [Tooltip("How long it must be stuck before reversing out, seconds.")]
        [SerializeField] private float _stuckPatience = 0.8f;
        [Tooltip("How long it reverses when unsticking, seconds.")]
        [SerializeField] private float _reverseDuration = 1.4f;

        private CarController _car;
        private readonly RaycastHit[] _hits = new RaycastHit[4];
        private float _stuckTime;
        private float _reverseTime;
        private float _reverseSteer;
        private int _consecutiveAttempts;
        private float _sinceLastAttempt;

        public CarInputState Current { get; private set; }
        public bool IsRecovering => _reverseTime > 0f;

        public void Configure(Transform slopeRoot, float trackHalfWidth, LayerMask obstacleMask)
        {
            _slopeRoot = slopeRoot;
            _trackHalfWidth = trackHalfWidth;
            _obstacleMask = obstacleMask;
        }

        private void Awake()
        {
            Initialize();
        }

        /// <summary>
        /// Caches the controller and takes over its input. Public because headless simulation
        /// runs outside play mode, where Awake never fires.
        /// </summary>
        public void Initialize()
        {
            if (_car != null) return;

            _car = GetComponent<CarController>();
            _car.SetInputProvider(this);
        }

        private void Update()
        {
            Tick(Time.deltaTime);
        }

        /// <summary>
        /// Recomputes the driving decision. Takes the step explicitly because headless
        /// simulation runs outside the Update loop, where Time.deltaTime does not advance.
        /// </summary>
        public void Tick(float deltaTime)
        {
            Initialize();

            if (_slopeRoot == null)
            {
                Current = new CarInputState(1f, 0f, false);
                return;
            }

            deltaTime = Mathf.Max(1e-4f, deltaTime);

            // Getting wedged against a block is normal on a track this dense; what matters is
            // getting out of it. Reverse, turn the wheel, try again — the same thing a human does.
            _sinceLastAttempt += deltaTime;

            // A run of clean driving means the last escape worked; forget the streak.
            if (_sinceLastAttempt > 6f) _consecutiveAttempts = 0;

            if (_reverseTime > 0f)
            {
                _reverseTime -= deltaTime;
                Current = new CarInputState(-1f, _reverseSteer, handbrake: false);
                return;
            }

            if (Mathf.Abs(_car.ForwardSpeed) < _stuckSpeed)
            {
                _stuckTime += deltaTime;
                if (_stuckTime >= _stuckPatience)
                {
                    _stuckTime = 0f;
                    _sinceLastAttempt = 0f;
                    _consecutiveAttempts++;

                    // Back out further each time it fails, and alternate which way the wheel
                    // goes: repeating the same manoeuvre against the same obstacle just
                    // repeats the same result.
                    _reverseTime = _reverseDuration * Mathf.Min(3f, _consecutiveAttempts);
                    float preferred = AvoidanceBias() >= 0f ? -0.8f : 0.8f;
                    _reverseSteer = _consecutiveAttempts % 2 == 0 ? -preferred : preferred;

                    Current = new CarInputState(-1f, _reverseSteer, handbrake: false);
                    return;
                }
            }
            else
            {
                _stuckTime = 0f;
            }

            Vector3 local = _slopeRoot.InverseTransformPoint(transform.position);
            Vector3 downhill = _slopeRoot.forward;

            // Base steering: hold the middle of the track, and turn back if drifting wide.
            float lateralError = Mathf.Clamp(local.x / Mathf.Max(1f, _trackHalfWidth), -1f, 1f);
            float headingError = Vector3.SignedAngle(transform.forward, downhill, Vector3.up) / 45f;
            float steer = Mathf.Clamp(headingError - lateralError * 0.8f, -1f, 1f);

            // Avoidance: feelers fan out ahead, and the driver turns away from the blocked side.
            steer += AvoidanceBias();

            float throttle = 1f;
            // If something is dead ahead and close, lift off rather than plough into it.
            if (IsBlocked(transform.forward, CurrentLookAhead * 0.45f))
            {
                throttle = 0.55f;
            }

            Current = new CarInputState(throttle, Mathf.Clamp(steer, -1f, 1f), handbrake: false);
        }

        /// <summary>Look-ahead grows with speed: the faster it goes, the earlier it must decide.</summary>
        private float CurrentLookAhead =>
            _lookAhead + Mathf.Abs(_car.ForwardSpeed) * _lookAheadPerSpeed;

        private float AvoidanceBias()
        {
            float distance = CurrentLookAhead;
            Vector3 forward = transform.forward;

            // Two pairs of feelers: a narrow pair that reacts to what is directly in the way,
            // and a wide pair that starts the line change earlier.
            bool nearLeft = IsBlocked(Quaternion.AngleAxis(-_feelerSpread, Vector3.up) * forward, distance);
            bool nearRight = IsBlocked(Quaternion.AngleAxis(_feelerSpread, Vector3.up) * forward, distance);
            bool wideLeft = IsBlocked(Quaternion.AngleAxis(-_wideFeelerSpread, Vector3.up) * forward, distance * 0.7f);
            bool wideRight = IsBlocked(Quaternion.AngleAxis(_wideFeelerSpread, Vector3.up) * forward, distance * 0.7f);

            float bias = 0f;
            if (nearLeft) bias += 0.8f;
            if (nearRight) bias -= 0.8f;
            if (wideLeft) bias += 0.3f;
            if (wideRight) bias -= 0.3f;

            // Both sides blocked: commit to the side with more room rather than freezing.
            if (nearLeft && nearRight)
            {
                bias = wideLeft && !wideRight ? 0.9f : -0.9f;
            }

            return Mathf.Clamp(bias, -1f, 1f);
        }

        private bool IsBlocked(Vector3 direction, float distance)
        {
            // RaycastNonAlloc: this runs every frame and RaycastAll would allocate an array
            // each time.
            var ray = new Ray(transform.position + Vector3.up * 0.5f, direction);
            int count = Physics.RaycastNonAlloc(ray, _hits, distance, _obstacleMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                if (_hits[i].transform.root != transform.root) return true;
            }

            return false;
        }
    }
}
