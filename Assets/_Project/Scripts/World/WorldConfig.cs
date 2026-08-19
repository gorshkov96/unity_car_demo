using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Parameters for the procedurally built map. Generation is seeded, so the same
    /// config always produces the same world — reviewable, testable, and diff-free
    /// (the map lives in code, not in a giant scene YAML).
    /// </summary>
    [CreateAssetMenu(fileName = "WorldConfig", menuName = "CarDemo/World Config")]
    public sealed class WorldConfig : ScriptableObject
    {
        [Header("Seed")]
        [SerializeField] private int _seed = 20260819;

        [Header("Ground")]
        [Tooltip("Half-size of the square ground plate, meters.")]
        [SerializeField] private float _groundHalfSize = 120f;
        [SerializeField] private float _wallHeight = 4f;

        [Header("Road ring")]
        [SerializeField] private float _roadRadius = 70f;
        [SerializeField] private float _roadWidth = 12f;
        [SerializeField] private int _roadSegments = 48;

        [Header("Static props")]
        [SerializeField] private int _buildingCount = 26;
        [SerializeField] private Vector2 _buildingFootprint = new Vector2(6f, 14f);
        [SerializeField] private Vector2 _buildingHeight = new Vector2(6f, 26f);
        [SerializeField] private int _pillarCount = 40;
        [SerializeField] private int _rampCount = 4;

        [Header("Moving props")]
        [SerializeField] private int _spinnerCount = 8;
        [SerializeField] private int _platformCount = 5;
        [SerializeField] private int _bumperCount = 24;
        [Tooltip("Mass of the knock-around dynamic boxes, kg.")]
        [SerializeField] private float _bumperMass = 40f;

        [Header("Spawn")]
        [Tooltip("Angle on the road ring, degrees, where the car starts. Props keep clear of it.")]
        [SerializeField] private float _spawnAngleDegrees;
        [Tooltip("Radius around the spawn point that stays free of props, meters.")]
        [SerializeField] private float _spawnClearance = 14f;

        public int Seed => _seed;
        public float SpawnAngleDegrees => _spawnAngleDegrees;
        public float SpawnClearance => _spawnClearance;
        public float GroundHalfSize => _groundHalfSize;
        public float WallHeight => _wallHeight;
        public float RoadRadius => _roadRadius;
        public float RoadWidth => _roadWidth;
        public int RoadSegments => _roadSegments;
        public int BuildingCount => _buildingCount;
        public Vector2 BuildingFootprint => _buildingFootprint;
        public Vector2 BuildingHeight => _buildingHeight;
        public int PillarCount => _pillarCount;
        public int RampCount => _rampCount;
        public int SpinnerCount => _spinnerCount;
        public int PlatformCount => _platformCount;
        public int BumperCount => _bumperCount;
        public float BumperMass => _bumperMass;
    }
}
