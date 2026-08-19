using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Parameters of the descent: a long, wide slope the car rolls down under gravity,
    /// scattered with jumps, static obstacles and moving hazards.
    ///
    /// The track is built from fixed-length chunks generated on demand. Each chunk's contents
    /// are derived from the seed and its index, so a chunk is reproducible: it can be thrown
    /// away once it is far behind and rebuilt identically if the player turns around.
    /// </summary>
    [CreateAssetMenu(fileName = "DescentConfig", menuName = "CarDemo/Descent Config")]
    public sealed class DescentConfig : ScriptableObject
    {
        [Header("Seed")]
        [SerializeField] private int _seed = 8125;

        [Header("Slope")]
        [Tooltip("Downhill angle in degrees. 12-16 keeps speed building without a free fall.")]
        [Range(4f, 30f)]
        [SerializeField] private float _slopeAngle = 16f;
        [SerializeField] private float _width = 95f;
        [Tooltip("Wall height. Tall on purpose: at 160+ km/h a jump near the edge clears a "
                 + "low barrier, and a car outside the track has nothing to drive on.")]
        [SerializeField] private float _wallHeight = 10f;

        [Header("Streaming")]
        [Tooltip("Length of one generated chunk, meters.")]
        [SerializeField] private float _chunkLength = 220f;
        [Tooltip("How many chunks are kept ahead of the car.")]
        [SerializeField] private int _chunksAhead = 4;
        [Tooltip("How many chunks are kept behind. Turning around stays possible within this "
                 + "window; beyond it the track is rebuilt from the seed, identically.")]
        [SerializeField] private int _chunksBehind = 2;
        [Tooltip("Total chunks in the run, or 0 for an endless descent. Endless is the "
                 + "default: a finite slope ends in open space, and driving off the last chunk "
                 + "means falling through the world.")]
        [SerializeField] private int _totalChunks;

        [Header("Per chunk: jumps")]
        [SerializeField] private Vector2Int _jumpsPerChunk = new Vector2Int(3, 5);
        [SerializeField] private Vector2 _jumpWidth = new Vector2(10f, 20f);
        [SerializeField] private float _jumpRise = 1.7f;

        [Header("Per chunk: static obstacles")]
        [SerializeField] private Vector2Int _blocksPerChunk = new Vector2Int(3, 6);
        [SerializeField] private Vector2Int _pillarsPerChunk = new Vector2Int(2, 5);
        [Tooltip("Slalom gates: wall pairs leaving a gap to thread through.")]
        [SerializeField] private Vector2Int _gatesPerChunk = new Vector2Int(0, 1);

        [Header("Per chunk: moving hazards")]
        [SerializeField] private Vector2Int _sweepersPerChunk = new Vector2Int(1, 2);
        [SerializeField] private Vector2Int _spinnersPerChunk = new Vector2Int(1, 2);
        [SerializeField] private Vector2Int _cratesPerChunk = new Vector2Int(6, 14);
        [SerializeField] private float _crateMass = 35f;

        [Header("Start")]
        [SerializeField] private float _startOffset = 20f;
        [Tooltip("Clear run at the top before the first obstacles, meters. Long enough to "
                 + "build speed and enter the course with momentum instead of from a standstill.")]
        [SerializeField] private float _startClearance = 45f;

        public int Seed => _seed;
        public float SlopeAngle => _slopeAngle;
        public float Width => _width;
        public float HalfWidth => _width * 0.5f;
        public float WallHeight => _wallHeight;
        public float ChunkLength => _chunkLength;
        public int ChunksAhead => Mathf.Max(1, _chunksAhead);
        public int ChunksBehind => Mathf.Max(0, _chunksBehind);
        /// <summary>Chunk count, or zero when the descent is endless.</summary>
        public int TotalChunks => Mathf.Max(0, _totalChunks);

        public bool IsEndless => TotalChunks == 0;

        /// <summary>Total length, or zero when endless.</summary>
        public float TotalLength => _chunkLength * TotalChunks;
        public Vector2Int JumpsPerChunk => _jumpsPerChunk;
        public Vector2 JumpWidth => _jumpWidth;
        public float JumpRise => _jumpRise;
        public Vector2Int BlocksPerChunk => _blocksPerChunk;
        public Vector2Int PillarsPerChunk => _pillarsPerChunk;
        public Vector2Int GatesPerChunk => _gatesPerChunk;
        public Vector2Int SweepersPerChunk => _sweepersPerChunk;
        public Vector2Int SpinnersPerChunk => _spinnersPerChunk;
        public Vector2Int CratesPerChunk => _cratesPerChunk;
        public float CrateMass => _crateMass;
        public float StartOffset => _startOffset;
        public float StartClearance => _startClearance;
    }
}
