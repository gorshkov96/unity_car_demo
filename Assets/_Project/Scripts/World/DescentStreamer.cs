using System.Collections.Generic;
using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Streams the descent track: builds chunks ahead of the car and drops the ones that fall
    /// far enough behind.
    ///
    /// Chunks are reproducible from (seed, index), so discarding one loses nothing — turning
    /// around and driving back up rebuilds exactly the same track. That is the reason the
    /// generator is seeded per chunk rather than run once for the whole slope.
    /// </summary>
    public sealed class DescentStreamer : MonoBehaviour
    {
        [SerializeField] private DescentConfig _config;
        [SerializeField] private Transform _tracked;

        [Header("Materials")]
        [SerializeField] private Material _surfaceMaterial;
        [SerializeField] private Material _stripeMaterial;
        [SerializeField] private Material _wallMaterial;
        [SerializeField] private Material _jumpMaterial;
        [SerializeField] private Material _obstacleMaterial;
        [SerializeField] private Material _accentMaterial;
        [SerializeField] private Material _movingMaterial;
        [SerializeField] private Material _crateMaterial;
        [SerializeField] private Material _hazardMaterial;

        private const string SlopeRootName = "Slope";

        private readonly Dictionary<int, GameObject> _liveChunks = new Dictionary<int, GameObject>();
        private readonly List<int> _toRemove = new List<int>();
        private DescentChunkBuilder _builder;
        private Transform _slopeRoot;
        private ChunkWindow _window = new ChunkWindow(0, -1);

        public DescentConfig Config => _config;
        public Transform SlopeRoot => _slopeRoot;
        public int LiveChunkCount => _liveChunks.Count;

        /// <summary>How far the tracked object has travelled down the slope, in slope space.</summary>
        public float DistanceAlongTrack => _tracked == null || _slopeRoot == null
            ? 0f
            : _slopeRoot.InverseTransformPoint(_tracked.position).z;

        public void Configure(DescentConfig config, Transform tracked, DescentMaterials materials)
        {
            _config = config;
            _tracked = tracked;
            _surfaceMaterial = materials.Surface;
            _stripeMaterial = materials.Stripe;
            _wallMaterial = materials.Wall;
            _jumpMaterial = materials.Jump;
            _obstacleMaterial = materials.Obstacle;
            _accentMaterial = materials.Accent;
            _movingMaterial = materials.Moving;
            _crateMaterial = materials.Crate;
            _hazardMaterial = materials.Hazard;
        }

        public void SetTracked(Transform tracked) => _tracked = tracked;

        private void Awake()
        {
            EnsureInitialised();
        }

        private void Update()
        {
            Tick();
        }

        /// <summary>
        /// Advances streaming by one step. Public because headless simulation (the descent
        /// smoke test) drives the world without Unity's Update loop running.
        /// </summary>
        public void Tick()
        {
            if (_config == null || _tracked == null) return;

            EnsureInitialised();
            RefreshWindow();
        }

        /// <summary>
        /// Creates the tilted root, or adopts the one saved in the scene.
        ///
        /// The adoption matters: _slopeRoot is not serialised, so on load it is null and this
        /// method would happily build a second "Slope" beside the one the scene generator left
        /// behind. Everything within streaming range then existed twice — two road slabs
        /// z-fighting, every crate spawned inside its own twin for physics to shove apart, and
        /// the car taking contacts from two overlapping copies of every ramp. That reads in
        /// play as invisible obstacles and as tripping over flat ground.
        /// </summary>
        public void EnsureInitialised()
        {
            if (_slopeRoot != null || _config == null) return;

            Transform existing = transform.Find(SlopeRootName);
            if (existing != null)
            {
                // Chunks are streamed at runtime, so anything baked under this root is a
                // stale duplicate of what is about to be generated.
                for (int i = existing.childCount - 1; i >= 0; i--)
                {
                    GameObject child = existing.GetChild(i).gameObject;
                    if (Application.isPlaying) Destroy(child);
                    else DestroyImmediate(child);
                }

                existing.localRotation = Quaternion.Euler(_config.SlopeAngle, 0f, 0f);
                _slopeRoot = existing;
            }
            else
            {
                var root = new GameObject(SlopeRootName);
                root.transform.SetParent(transform, worldPositionStays: false);
                root.transform.localRotation = Quaternion.Euler(_config.SlopeAngle, 0f, 0f);
                _slopeRoot = root.transform;
            }

            _liveChunks.Clear();
            _window = new ChunkWindow(0, -1);

            _builder = new DescentChunkBuilder(_config, new DescentMaterials(
                _surfaceMaterial, _stripeMaterial, _wallMaterial, _jumpMaterial,
                _obstacleMaterial, _accentMaterial, _movingMaterial, _crateMaterial, _hazardMaterial));
        }

        /// <summary>Builds the chunks the car starts inside, so the first frame is not empty.</summary>
        public void PreloadAroundStart()
        {
            EnsureInitialised();
            ApplyWindow(ChunkWindow.Around(0f, _config.ChunkLength, _config.ChunksAhead, 0, _config.TotalChunks));
        }

        private void RefreshWindow()
        {
            ChunkWindow desired = ChunkWindow.Around(
                DistanceAlongTrack, _config.ChunkLength, _config.ChunksAhead, _config.ChunksBehind, _config.TotalChunks);

            if (desired.First == _window.First && desired.Last == _window.Last) return;

            ApplyWindow(desired);
        }

        private void ApplyWindow(ChunkWindow desired)
        {
            _window = desired;

            _toRemove.Clear();
            foreach (KeyValuePair<int, GameObject> entry in _liveChunks)
            {
                if (!desired.Contains(entry.Key)) _toRemove.Add(entry.Key);
            }

            for (int i = 0; i < _toRemove.Count; i++)
            {
                GameObject chunk = _liveChunks[_toRemove[i]];
                _liveChunks.Remove(_toRemove[i]);
                if (Application.isPlaying) Destroy(chunk);
                else DestroyImmediate(chunk);
            }

            for (int index = desired.First; index <= desired.Last; index++)
            {
                if (_liveChunks.ContainsKey(index)) continue;

                // Layers are assigned per object by the builder — the surface is drivable
                // ground, obstacles are props. Applying the root's layer over the whole chunk
                // here would flatten that distinction and blind the suspension rays.
                _liveChunks[index] = _builder.Build(index, _slopeRoot);
            }
        }

        /// <summary>World position and rotation where the car starts the descent.</summary>
        public void GetSpawn(out Vector3 position, out Quaternion rotation)
        {
            EnsureInitialised();
            position = _slopeRoot.TransformPoint(new Vector3(0f, 1.2f, _config.StartOffset));
            rotation = Quaternion.LookRotation(_slopeRoot.forward, Vector3.up);
        }
    }
}
