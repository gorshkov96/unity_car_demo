using CarDemo.Core;
using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Builds the whole map from code: ground, boundary walls, a road ring,
    /// buildings, pillars, ramps, and the moving props.
    ///
    /// Why code and not a hand-placed scene: the scene file stays tiny and
    /// mergeable, the layout is reproducible from a seed, and changing the map
    /// is a readable diff instead of an opaque YAML blob.
    /// </summary>
    public sealed class WorldBuilder : MonoBehaviour
    {
        [SerializeField] private WorldConfig _config;

        [Header("Materials")]
        [SerializeField] private Material _groundMaterial;
        [SerializeField] private Material _roadMaterial;
        [SerializeField] private Material _buildingMaterial;
        [SerializeField] private Material _accentMaterial;
        [SerializeField] private Material _movingMaterial;

        [Header("Build")]
        [Tooltip("Build the world automatically on Awake. Off when a tool builds it in the editor.")]
        [SerializeField] private bool _buildOnAwake = true;

        private Transform _staticRoot;
        private Transform _dynamicRoot;

        public WorldConfig Config => _config;

        /// <summary>
        /// Where the car starts: on the road ring, in a zone the generator keeps
        /// free of props. Owned by the world so the two can never disagree.
        /// </summary>
        public Vector3 CarSpawnPosition
        {
            get
            {
                float angle = _config.SpawnAngleDegrees * Mathf.Deg2Rad;
                return new Vector3(Mathf.Cos(angle) * _config.RoadRadius, 1f, Mathf.Sin(angle) * _config.RoadRadius);
            }
        }

        /// <summary>Facing along the road at the spawn point (tangent to the ring).</summary>
        public Quaternion CarSpawnRotation
        {
            get
            {
                float angle = _config.SpawnAngleDegrees * Mathf.Deg2Rad;
                var tangent = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                return Quaternion.LookRotation(tangent, Vector3.up);
            }
        }

        private bool IsInsideSpawnZone(Vector3 point)
        {
            Vector3 spawn = CarSpawnPosition;
            float dx = point.x - spawn.x;
            float dz = point.z - spawn.z;
            return dx * dx + dz * dz < _config.SpawnClearance * _config.SpawnClearance;
        }

        /// <summary>
        /// Injects the config and materials. Used by the scene generator and by any
        /// runtime code that wants to build a world without an authored prefab.
        /// </summary>
        public void Configure(WorldConfig config, Material ground, Material road, Material building, Material accent, Material moving)
        {
            _config = config;
            _groundMaterial = ground;
            _roadMaterial = road;
            _buildingMaterial = building;
            _accentMaterial = accent;
            _movingMaterial = moving;
        }

        private void Awake()
        {
            if (_buildOnAwake && transform.childCount == 0)
            {
                Build();
            }
        }

        /// <summary>Rebuilds the map from scratch. Safe to call repeatedly.</summary>
        public void Build()
        {
            if (_config == null)
            {
                Debug.LogError($"{nameof(WorldBuilder)} on '{name}' has no WorldConfig assigned — nothing was generated.", this);
                throw new System.InvalidOperationException("WorldBuilder has no WorldConfig.");
            }

            Clear();

            var random = new DeterministicRandom(_config.Seed);
            _staticRoot = new GameObject("Static").transform;
            _staticRoot.SetParent(transform, worldPositionStays: false);
            _dynamicRoot = new GameObject("Dynamic").transform;
            _dynamicRoot.SetParent(transform, worldPositionStays: false);

            BuildGround();
            BuildWalls();
            BuildRoadRing();
            BuildBuildings(random);
            BuildPillars(random);
            BuildRamps(random);
            BuildSpinners(random);
            BuildPlatforms(random);
            BuildBumpers(random);

            PrimitiveFactory.MarkStatic(_staticRoot.gameObject);

            // Layers decide what the car's suspension is allowed to stand on. Static geometry
            // is drivable ground; loose and moving props are separate so they can be excluded
            // from the suspension rays later without touching the generator.
            GameLayers.ApplyRecursively(_staticRoot.gameObject, GameLayers.Ground);
            GameLayers.ApplyRecursively(_dynamicRoot.gameObject, GameLayers.Prop);
        }

        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        private void BuildGround()
        {
            float size = _config.GroundHalfSize * 2f;
            // A thick box instead of a plane: a plane is single-sided and paper thin,
            // which invites tunnelling at speed.
            PrimitiveFactory.Box(
                "Ground", _staticRoot,
                new Vector3(0f, -0.5f, 0f),
                new Vector3(size, 1f, size),
                Quaternion.identity,
                _groundMaterial);
        }

        private void BuildWalls()
        {
            float half = _config.GroundHalfSize;
            float height = _config.WallHeight;
            float thickness = 2f;
            float length = half * 2f + thickness * 2f;

            var offsets = new[]
            {
                (pos: new Vector3(0f, height * 0.5f, half + thickness * 0.5f), size: new Vector3(length, height, thickness)),
                (pos: new Vector3(0f, height * 0.5f, -half - thickness * 0.5f), size: new Vector3(length, height, thickness)),
                (pos: new Vector3(half + thickness * 0.5f, height * 0.5f, 0f), size: new Vector3(thickness, height, length)),
                (pos: new Vector3(-half - thickness * 0.5f, height * 0.5f, 0f), size: new Vector3(thickness, height, length)),
            };

            for (int i = 0; i < offsets.Length; i++)
            {
                PrimitiveFactory.Box($"Wall_{i}", _staticRoot, offsets[i].pos, offsets[i].size, Quaternion.identity, _accentMaterial);
            }
        }

        /// <summary>Lays a closed ring of flat road slabs to drive along.</summary>
        private void BuildRoadRing()
        {
            int segments = Mathf.Max(8, _config.RoadSegments);
            float radius = _config.RoadRadius;
            // Chord length of one segment, plus a little overlap so there are no seams.
            float segmentLength = 2f * Mathf.PI * radius / segments * 1.04f;

            Transform roadRoot = new GameObject("Road").transform;
            roadRoot.SetParent(_staticRoot, worldPositionStays: false);

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                var position = new Vector3(Mathf.Cos(angle) * radius, 0.02f, Mathf.Sin(angle) * radius);
                Quaternion rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg, 0f);
                GameObject slab = PrimitiveFactory.Box(
                    $"RoadSegment_{i}", roadRoot, position,
                    new Vector3(_config.RoadWidth, 0.04f, segmentLength),
                    rotation, _roadMaterial);
                // The road is decorative: the ground box below already carries the car.
                PrimitiveFactory.StripCollider(slab);
            }
        }

        private void BuildBuildings(DeterministicRandom random)
        {
            Transform root = new GameObject("Buildings").transform;
            root.SetParent(_staticRoot, worldPositionStays: false);

            for (int i = 0; i < _config.BuildingCount; i++)
            {
                Vector3 position = FindClearSpot(random, minRadius: 12f, clearanceFromRoad: 10f);
                float width = random.Range(_config.BuildingFootprint.x, _config.BuildingFootprint.y);
                float depth = random.Range(_config.BuildingFootprint.x, _config.BuildingFootprint.y);
                float height = random.Range(_config.BuildingHeight.x, _config.BuildingHeight.y);

                PrimitiveFactory.Box(
                    $"Building_{i}", root,
                    new Vector3(position.x, height * 0.5f, position.z),
                    new Vector3(width, height, depth),
                    Quaternion.Euler(0f, random.Range(0f, 360f), 0f),
                    _buildingMaterial);
            }
        }

        private void BuildPillars(DeterministicRandom random)
        {
            Transform root = new GameObject("Pillars").transform;
            root.SetParent(_staticRoot, worldPositionStays: false);

            for (int i = 0; i < _config.PillarCount; i++)
            {
                Vector3 position = FindClearSpot(random, minRadius: 8f, clearanceFromRoad: 8.5f);
                float height = random.Range(2f, 6f);
                PrimitiveFactory.Cylinder(
                    $"Pillar_{i}", root,
                    new Vector3(position.x, height * 0.5f, position.z),
                    new Vector3(0.8f, height * 0.5f, 0.8f),
                    Quaternion.identity,
                    _accentMaterial);
            }
        }

        /// <summary>Ramps let the player get the car airborne — cheap way to show off the suspension.</summary>
        private void BuildRamps(DeterministicRandom random)
        {
            Transform root = new GameObject("Ramps").transform;
            root.SetParent(_staticRoot, worldPositionStays: false);

            for (int i = 0; i < _config.RampCount; i++)
            {
                float angle = i / (float)Mathf.Max(1, _config.RampCount) * Mathf.PI * 2f + 0.4f;
                float radius = _config.RoadRadius - _config.RoadWidth * 0.5f - 6f;
                var position = new Vector3(Mathf.Cos(angle) * radius, -0.08f, Mathf.Sin(angle) * radius);
                Quaternion rotation = Quaternion.Euler(0f, -angle * Mathf.Rad2Deg + random.Range(-8f, 8f), 0f);
                if (IsInsideSpawnZone(position)) continue;

                // A wedge, not a tilted slab: a tilted slab buries its low edge and leaves
                // its high edge standing proud as a step for the car to trip over.
                GameObject ramp = WedgeMesh.Create(
                    $"Ramp_{i}", root, position, new Vector3(7f, 1.4f, 9f), rotation, _accentMaterial);
                ramp.layer = GameLayers.Ground;
            }
        }

        private void BuildSpinners(DeterministicRandom random)
        {
            Transform root = new GameObject("Spinners").transform;
            root.SetParent(_dynamicRoot, worldPositionStays: false);

            for (int i = 0; i < _config.SpinnerCount; i++)
            {
                // Half-step offset keeps spinner 0 off the spawn point.
                float angle = (i + 0.5f) / Mathf.Max(1, _config.SpinnerCount) * Mathf.PI * 2f;
                float radius = _config.RoadRadius;
                var position = new Vector3(Mathf.Cos(angle) * radius, 1.2f, Mathf.Sin(angle) * radius);
                if (IsInsideSpawnZone(position)) continue;

                var pivot = new GameObject($"Spinner_{i}");
                pivot.transform.SetParent(root, worldPositionStays: false);
                pivot.transform.position = position;

                GameObject arm = PrimitiveFactory.Box(
                    "Arm", pivot.transform, Vector3.zero,
                    new Vector3(_config.RoadWidth * 0.9f, 0.5f, 0.6f),
                    Quaternion.identity, _movingMaterial);
                arm.transform.localPosition = Vector3.zero;

                Rigidbody body = pivot.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                SpinningProp spinner = pivot.AddComponent<SpinningProp>();
                spinner.Configure(Vector3.up, random.Range(30f, 70f) * (i % 2 == 0 ? 1f : -1f));
            }
        }

        private void BuildPlatforms(DeterministicRandom random)
        {
            Transform root = new GameObject("Platforms").transform;
            root.SetParent(_dynamicRoot, worldPositionStays: false);

            for (int i = 0; i < _config.PlatformCount; i++)
            {
                Vector3 spot = FindClearSpot(random, minRadius: 15f, clearanceFromRoad: 14f);
                var position = new Vector3(spot.x, 0.4f, spot.z);

                GameObject platform = PrimitiveFactory.Box(
                    $"Platform_{i}", root, position,
                    new Vector3(6f, 0.6f, 6f),
                    Quaternion.identity, _movingMaterial);

                Rigidbody body = platform.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                MovingPlatform mover = platform.AddComponent<MovingPlatform>();
                var travel = new Vector3(random.Range(-16f, 16f), 0f, random.Range(-16f, 16f));
                mover.Configure(travel, random.Range(6f, 12f), random.Range(0f, 1f));
            }
        }

        /// <summary>Light dynamic boxes the car can scatter — the most legible physics demo there is.</summary>
        private void BuildBumpers(DeterministicRandom random)
        {
            Transform root = new GameObject("Bumpers").transform;
            root.SetParent(_dynamicRoot, worldPositionStays: false);

            for (int i = 0; i < _config.BumperCount; i++)
            {
                float angle = random.Range(0f, Mathf.PI * 2f);
                float radius = _config.RoadRadius + random.Range(-_config.RoadWidth * 0.35f, _config.RoadWidth * 0.35f);
                var position = new Vector3(Mathf.Cos(angle) * radius, 0.5f, Mathf.Sin(angle) * radius);
                if (IsInsideSpawnZone(position)) continue;

                GameObject box = PrimitiveFactory.Box(
                    $"Bumper_{i}", root, position,
                    new Vector3(0.9f, 0.9f, 0.9f),
                    Quaternion.Euler(0f, random.Range(0f, 360f), 0f),
                    _movingMaterial);

                Rigidbody body = box.AddComponent<Rigidbody>();
                body.mass = _config.BumperMass;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        /// <summary>
        /// Picks a point away from the spawn area and off the road ring, so generated
        /// props never block the drivable loop or land on the player.
        /// </summary>
        private Vector3 FindClearSpot(DeterministicRandom random, float minRadius, float clearanceFromRoad)
        {
            float limit = _config.GroundHalfSize - 12f;
            for (int attempt = 0; attempt < 32; attempt++)
            {
                Vector3 candidate = random.PointOnGround(limit);
                float distance = new Vector2(candidate.x, candidate.z).magnitude;
                if (distance < minRadius) continue;
                if (Mathf.Abs(distance - _config.RoadRadius) < clearanceFromRoad) continue;
                if (IsInsideSpawnZone(candidate)) continue;
                return candidate;
            }

            // Fallback: push it outside the ring rather than dropping the prop entirely.
            float fallbackAngle = random.Range(0f, Mathf.PI * 2f);
            float fallbackRadius = _config.RoadRadius + clearanceFromRoad + 6f;
            return new Vector3(Mathf.Cos(fallbackAngle) * fallbackRadius, 0f, Mathf.Sin(fallbackAngle) * fallbackRadius);
        }
    }
}
