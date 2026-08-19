using UnityEngine;

namespace CarDemo.Diagnostics
{
    /// <summary>
    /// Counts real contacts and detects tunnelling: cases where the car crossed a collider
    /// between two physics steps without ever touching it.
    ///
    /// This exists because "objects pass through the car" is invisible to every other check —
    /// the car keeps driving, nothing errors, and only a human watching notices. The test is
    /// a swept check: if the straight line between where the car was and where it is now
    /// crosses solid geometry, but no contact was reported, something was passed through.
    /// </summary>
    public sealed class CollisionAuditor : MonoBehaviour
    {
        [Tooltip("Layers that count as solid obstacles for the tunnelling check. Ground is "
                 + "excluded deliberately: the car is always in contact with the road, and "
                 + "including it would report a hit on every single step.")]
        [SerializeField] private LayerMask _obstacleMask;
        [Tooltip("Half extents of the box used to detect overlaps with the car, meters.")]
        [SerializeField] private Vector3 _halfExtents = new Vector3(1f, 0.6f, 2.2f);
        [Tooltip("Radius of the swept tunnelling check, meters.")]
        [SerializeField] private float _sweepRadius = 0.7f;
        [Tooltip("Largest believable movement in one physics step, meters. Anything beyond "
                 + "this was a teleport, not driving.")]
        [SerializeField] private float _maxPlausibleStep = 2f;

        private readonly Collider[] _overlaps = new Collider[16];
        private Vector3 _previousPosition;
        private bool _hasPrevious;
        private int _touchingThisStep;

        /// <summary>Total collision contacts reported by the physics engine.</summary>
        public int ContactCount { get; private set; }

        /// <summary>Times the car crossed solid geometry without any contact being reported.</summary>
        public int TunnelCount { get; private set; }

        /// <summary>Longest single uncollided jump through solid geometry, meters.</summary>
        public float WorstTunnelDistance { get; private set; }

        public void ResetCounters()
        {
            ContactCount = 0;
            TunnelCount = 0;
            WorstTunnelDistance = 0f;
            _hasPrevious = false;
        }

        public void Configure(LayerMask obstacleMask, Vector3 halfExtents)
        {
            _obstacleMask = obstacleMask;
            _halfExtents = halfExtents;
        }

        /// <summary>
        /// Counts obstacles currently overlapping the car.
        ///
        /// Deliberately not OnCollisionEnter: MonoBehaviour collision callbacks are not raised
        /// when physics is stepped manually with Physics.Simulate, which is exactly how the
        /// headless tests run. A direct overlap query works in both modes.
        /// </summary>
        private int CountTouching()
        {
            int count = Physics.OverlapBoxNonAlloc(
                transform.position + transform.rotation * new Vector3(0f, 0.4f, 0f),
                _halfExtents, _overlaps, transform.rotation, _obstacleMask,
                QueryTriggerInteraction.Ignore);

            int foreign = 0;
            for (int i = 0; i < count; i++)
            {
                if (_overlaps[i] != null && _overlaps[i].transform.root != transform.root) foreign++;
            }

            return foreign;
        }

        /// <summary>
        /// Call once per physics step, after the step. Public so headless tests can drive it.
        /// </summary>
        public void CheckStep()
        {
            Vector3 current = transform.position;
            _touchingThisStep = CountTouching();
            ContactCount += _touchingThisStep;

            if (_hasPrevious)
            {
                Vector3 delta = current - _previousPosition;
                float distance = delta.magnitude;

                // A jump this large is not motion, it is a teleport — the safety net moving
                // the car clear. Counting it as tunnelling would blame the fix for the fault.
                bool teleported = distance > _maxPlausibleStep;

                // Only meaningful when the car moved and touched nothing: if it is already
                // overlapping an obstacle, the engine has clearly noticed it.
                if (!teleported && distance > 0.05f && _touchingThisStep == 0)
                {
                    // Sweep the path taken. Solid geometry found along a path that produced no
                    // contact was passed through.
                    if (Physics.SphereCast(_previousPosition, _sweepRadius, delta.normalized,
                            out RaycastHit hit, distance, _obstacleMask, QueryTriggerInteraction.Ignore)
                        && hit.transform.root != transform.root
                        && hit.distance < distance - 0.01f)
                    {
                        TunnelCount++;
                        if (distance > WorstTunnelDistance) WorstTunnelDistance = distance;
                    }
                }
            }

            _previousPosition = current;
            _hasPrevious = true;
        }

        private void FixedUpdate()
        {
            CheckStep();
        }
    }
}
