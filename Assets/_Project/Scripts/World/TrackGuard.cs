using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Last-resort safety net: puts the car back on the track if it ends up far below the
    /// surface or outside the walls.
    ///
    /// Streaming plus jumps plus physics means a car can occasionally end up somewhere the
    /// track is not. Falling forever is the worst possible failure — it looks like the game
    /// broke, and nothing brings the player back.
    /// </summary>
    public sealed class TrackGuard : MonoBehaviour
    {
        [SerializeField] private DescentStreamer _streamer;
        [SerializeField] private Rigidbody _car;
        [Tooltip("How far below the track surface counts as fallen through, meters.")]
        [SerializeField] private float _fallThreshold = 6f;
        [Tooltip("How far outside the walls counts as off the track, meters.")]
        [SerializeField] private float _sideMargin = 12f;
        [SerializeField] private float _checkInterval = 0.2f;

        [Header("Stuck recovery")]
        [Tooltip("Progress down the slope, in meters, that counts as still moving between checks.")]
        [SerializeField] private float _progressThreshold = 0.4f;
        [Tooltip("How long it may sit wedged before being nudged clear, seconds.")]
        [SerializeField] private float _stuckTimeout = 5f;
        [Tooltip("How far down the track it is moved when freed, meters.")]
        [SerializeField] private float _freeAheadDistance = 12f;

        private float _nextCheck;
        private float _stuckTime;
        private float _lastProgressZ = float.NegativeInfinity;
        private float _lastRescueZ = float.NegativeInfinity;
        private int _repeatedRescues;

        public int RescueCount { get; private set; }

        /// <summary>Times the car had to be freed from being wedged in geometry.</summary>
        public int UnstickCount { get; private set; }

        /// <summary>Why the last rescue fired, for diagnostics.</summary>
        public string LastRescueReason { get; private set; } = "none";

        public void Configure(DescentStreamer streamer, Rigidbody car)
        {
            _streamer = streamer;
            _car = car;
        }

        /// <summary>Places the car upright on the surface at a given point down the slope.</summary>
        private void FreeAt(Vector3 local, DescentConfig config, float z)
        {
            float safeX = Mathf.Clamp(local.x, -config.HalfWidth * 0.55f, config.HalfWidth * 0.55f);
            float safeZ = Mathf.Max(z, config.StartOffset);
            Vector3 target = _streamer.SlopeRoot.TransformPoint(new Vector3(safeX, 2.2f, safeZ));

            _car.linearVelocity = Vector3.zero;
            _car.angularVelocity = Vector3.zero;
            _car.position = target;
            _car.rotation = Quaternion.LookRotation(_streamer.SlopeRoot.forward, Vector3.up);
            _car.transform.SetPositionAndRotation(target, _car.rotation);
            _lastRescueZ = safeZ;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + _checkInterval;
            Check();
        }

        /// <summary>Runs one check. Public so headless simulation can drive it.</summary>
        public void Check()
        {
            if (_streamer == null || _car == null || _streamer.SlopeRoot == null) return;

            Vector3 local = _streamer.SlopeRoot.InverseTransformPoint(_car.position);
            DescentConfig config = _streamer.Config;
            if (config == null) return;

            // Wedged: on a track this dense a car can end up jammed against a ramp with no way
            // out under its own power. Progress along the slope is the honest measure — a car
            // spinning its wheels against a wall still has speed, it just is not going
            // anywhere, and a speed-based check would never notice.
            if (local.z - _lastProgressZ < _progressThreshold)
            {
                _stuckTime += _checkInterval;
                if (_stuckTime >= _stuckTimeout)
                {
                    _stuckTime = 0f;
                    _repeatedRescues++;

                    // Each repeat moves it further and closer to the middle: being freed back
                    // into the same pile-up just repeats the same wedge.
                    float ahead = _freeAheadDistance * Mathf.Min(4, _repeatedRescues);
                    float centred = Mathf.Lerp(local.x, 0f, 0.5f * Mathf.Min(1f, _repeatedRescues * 0.4f));

                    _lastProgressZ = local.z + ahead;
                    FreeAt(new Vector3(centred, local.y, local.z), config, local.z + ahead);
                    UnstickCount++;
                    Debug.Log($"[GUARD] freed a wedged car at z={local.z:0} (attempt {_repeatedRescues})");
                    return;
                }
            }
            else
            {
                _stuckTime = 0f;
                _lastProgressZ = local.z;

                // Moving well again means the last rescue worked; forget the streak.
                if (local.z - _lastRescueZ > 60f) _repeatedRescues = 0;
            }

            bool fellThrough = local.y < -_fallThreshold;
            bool wentWide = Mathf.Abs(local.x) > config.HalfWidth + _sideMargin;
            if (!fellThrough && !wentWide) return;

            LastRescueReason = fellThrough
                ? $"fell through at y={local.y:0.0} (z={local.z:0}, x={local.x:0})"
                : $"went outside the walls at x={local.x:0.0} (z={local.z:0})";
            Debug.Log($"[GUARD] {LastRescueReason}");

            // Put it back on the surface, facing downhill.
            //
            // The z is clamped to the start of the track: an impact can throw the car back
            // past the top, where no chunk exists. Restoring it to that same point would drop
            // it into the same void again, and the rescue would loop forever.
            float safeZ = Mathf.Max(local.z, config.StartOffset);
            if (!config.IsEndless)
            {
                safeZ = Mathf.Min(safeZ, config.TotalLength - config.ChunkLength * 0.5f);
            }

            FreeAt(local, config, safeZ);
            RescueCount++;
        }
    }
}
