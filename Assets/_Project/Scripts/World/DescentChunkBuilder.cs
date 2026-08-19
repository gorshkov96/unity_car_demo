using CarDemo.Core;
using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Builds the contents of one chunk of the descent.
    ///
    /// Everything is derived from (seed, chunkIndex), so the same chunk always comes out the
    /// same: it can be discarded when far behind and rebuilt identically if the car returns.
    /// Coordinates are local to the tilted slope root — z runs downhill, x across the track —
    /// which keeps the maths free of the slope angle.
    /// </summary>
    public sealed class DescentChunkBuilder
    {
        private readonly DescentConfig _config;
        private readonly DescentMaterials _materials;
        private readonly ChunkOccupancy _occupancy = new ChunkOccupancy();

        public DescentChunkBuilder(DescentConfig config, DescentMaterials materials)
        {
            _config = config;
            _materials = materials;
        }

        public GameObject Build(int chunkIndex, Transform parent)
        {
            var root = new GameObject($"Chunk_{chunkIndex}");
            root.transform.SetParent(parent, worldPositionStays: false);
            root.transform.localPosition = new Vector3(0f, 0f, chunkIndex * _config.ChunkLength);

            // Chunk index is folded into the seed so neighbouring chunks differ, while each
            // one stays reproducible on its own.
            var random = new DeterministicRandom(_config.Seed * 73856093 ^ chunkIndex * 19349663);

            _isFirstChunk = chunkIndex == 0;
            DescentProps.CheekMaterial = _materials.Hazard;
            _minZ = _isFirstChunk ? _config.StartOffset + _config.StartClearance : 5f;
            _occupancy.Clear();

            BuildSurface(root.transform);
            BuildJumps(root.transform, random);
            BuildGates(root.transform, random);
            BuildBlocks(root.transform, random);
            BuildPillars(root.transform, random);
            BuildSweepers(root.transform, random);
            BuildSpinners(root.transform, random);
            BuildCrates(root.transform, random);

            return root;
        }

        private void BuildSurface(Transform root)
        {
            float length = _config.ChunkLength;
            float halfLength = length * 0.5f;

            // Thick slab, not a thin plate: at 170 km/h the car covers most of a metre per
            // physics step, and a thin floor is an invitation to tunnel straight through it.
            GameObject road = PrimitiveFactory.Box(
                "Surface", root,
                new Vector3(0f, -4f, halfLength),
                new Vector3(_config.Width + 30f, 8f, length),
                Quaternion.identity, _materials.Surface);
            road.layer = GameLayers.Ground;

            // Lane stripes: without them a long slope reads as a static grey plane and the
            // sense of speed disappears.
            int stripes = Mathf.Max(1, Mathf.RoundToInt(length / 16f));
            for (int i = 0; i < stripes; i++)
            {
                GameObject stripe = PrimitiveFactory.Box(
                    "Stripe", root,
                    new Vector3(0f, 0.02f, i * 16f + 4f),
                    new Vector3(0.6f, 0.05f, 7f),
                    Quaternion.identity, _materials.Stripe);
                PrimitiveFactory.StripCollider(stripe);
            }

            // Closing wall at the very top: without it a hard impact can throw the car back
            // above the start, where the track has not been generated and never will be.
            if (IsFirstChunk)
            {
                GameObject topWall = PrimitiveFactory.Box(
                    "TopWall", root,
                    new Vector3(0f, _config.WallHeight * 0.5f - 0.5f, -1.5f),
                    new Vector3(_config.Width + 6f, _config.WallHeight, 3f),
                    Quaternion.identity, _materials.Wall);
                topWall.layer = GameLayers.Ground;
            }

            BuildWalls(root, length, halfLength);
        }

        /// <summary>
        /// Track walls, built as a leaning barrier with a stepped crest rather than one long
        /// slab. Flat-shaded facets need silhouette to look like anything, and a kilometre of
        /// identical box reads as a corridor in a debug scene.
        /// </summary>
        private void BuildWalls(Transform root, float length, float halfLength)
        {
            int sections = Mathf.Max(3, Mathf.RoundToInt(length / 16f));
            float sectionLength = length / sections;
            var random = new DeterministicRandom(_config.Seed ^ Mathf.RoundToInt(halfLength * 31f));

            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * (_config.HalfWidth + 1.5f);

                // Solid barrier behind the scenery, so nothing can leave the track no matter
                // what the decorative shapes in front of it do.
                GameObject wall = PrimitiveFactory.Box(
                    "Wall", root,
                    new Vector3(x + side * 1.2f, _config.WallHeight * 0.5f - 0.5f, halfLength),
                    new Vector3(3f, _config.WallHeight, length),
                    Quaternion.identity, _materials.Wall);
                wall.layer = GameLayers.Ground;

                for (int i = 0; i < sections; i++)
                {
                    float z = (i + 0.5f) * sectionLength;

                    // Rock face: leaning slabs of varying height, alternating which way they
                    // tip. A single extruded box reads as a corridor; broken facets read as
                    // terrain, which is the whole trick of low-poly scenery.
                    float rockHeight = random.Range(2.5f, 6.5f);
                    float lean = random.Range(6f, 16f) * (i % 2 == 0 ? 1f : -1f);

                    GameObject rock = WedgeMesh.Create(
                        "WallRock", root,
                        new Vector3(x - side * 0.8f, -0.4f, z),
                        new Vector3(sectionLength * random.Range(0.5f, 0.9f), rockHeight, sectionLength * 0.85f),
                        Quaternion.Euler(0f, side * 90f + lean, 0f),
                        i % 3 == 0 ? _materials.Accent : _materials.Obstacle);
                    rock.layer = GameLayers.Ground;

                    // Peaks rising behind the barrier: depth beyond the track, and something
                    // for the eye to measure speed against.
                    if (i % 2 != 0) continue;

                    float peakHeight = random.Range(14f, 40f);
                    float peakWidth = random.Range(16f, 34f);

                    // Peaks are decoration behind a solid barrier, so they carry no collider:
                    // the wall already stops the car, and colliders out here would only add
                    // physics work for scenery nobody can reach.
                    PyramidMesh.Create(
                        "Peak", root,
                        new Vector3(x + side * random.Range(18f, 55f), -1f, z + random.Range(-10f, 10f)),
                        new Vector3(peakWidth, peakHeight, peakWidth),
                        Quaternion.Euler(0f, random.Range(0f, 90f), 0f),
                        i % 4 == 0 ? _materials.Wall : _materials.Obstacle,
                        sides: i % 3 == 0 ? 4 : 5);
                }
            }
        }

        private void BuildJumps(Transform root, DeterministicRandom random)
        {
            int count = random.Range(_config.JumpsPerChunk.x, _config.JumpsPerChunk.y + 1);
            var kinds = new[]
            {
                DescentProps.RampKind.Kicker,
                DescentProps.RampKind.Table,
                DescentProps.RampKind.Roller,
                DescentProps.RampKind.Launcher,
                DescentProps.RampKind.CornerLeft,
                DescentProps.RampKind.CornerRight,
                DescentProps.RampKind.Twin,
            };

            for (int i = 0; i < count; i++)
            {
                float z = random.Range(_minZ + 5f, _config.ChunkLength - 25f);
                float x = random.Range(-_config.HalfWidth * 0.6f, _config.HalfWidth * 0.6f);
                float width = random.Range(_config.JumpWidth.x, _config.JumpWidth.y);
                DescentProps.RampKind kind = kinds[random.Range(0, kinds.Length)];

                // A table or roller occupies far more length than a plain kicker, and a
                // banked ramp needs clear ground behind it — the car lands there sideways.
                bool banked = kind == DescentProps.RampKind.CornerLeft
                              || kind == DescentProps.RampKind.CornerRight
                              || kind == DescentProps.RampKind.Twin;
                float footprint = kind == DescentProps.RampKind.Kicker ? 16f : 24f;
                float padding = banked ? 14f : 5f;
                if (!_occupancy.TryReserve(x, z, width, footprint, padding)) continue;

                var jumpRoot = new GameObject($"Jump_{kind}");
                jumpRoot.transform.SetParent(root, worldPositionStays: false);
                jumpRoot.transform.localPosition = new Vector3(x, 0f, z);
                jumpRoot.transform.localRotation = Quaternion.Euler(0f, random.Range(-5f, 5f), 0f);

                DescentProps.BuildRamp(jumpRoot.transform, kind, width, _materials);
            }
        }

        /// <summary>Slalom gates: two walls with a gap, forcing a line choice at speed.</summary>
        private void BuildGates(Transform root, DeterministicRandom random)
        {
            int count = random.Range(_config.GatesPerChunk.x, _config.GatesPerChunk.y + 1);
            for (int i = 0; i < count; i++)
            {
                float z = random.Range(_minZ + 15f, _config.ChunkLength - 25f);
                float gapCentre = random.Range(-_config.HalfWidth * 0.5f, _config.HalfWidth * 0.5f);
                float gapWidth = random.Range(18f, 28f);

                for (int side = -1; side <= 1; side += 2)
                {
                    float inner = gapCentre + side * gapWidth * 0.5f;
                    float outer = side < 0 ? -_config.HalfWidth : _config.HalfWidth;
                    float width = Mathf.Abs(outer - inner);
                    if (width < 1f) continue;

                    float centre = (inner + outer) * 0.5f;
                    if (!_occupancy.TryReserve(centre, z, width, 1.2f, padding: 1f)) continue;

                    GameObject wall = PrimitiveFactory.Box(
                        "GateWall", root,
                        new Vector3(centre, 1.1f, z),
                        new Vector3(width, 2.2f, 1.2f),
                        Quaternion.identity, _materials.Obstacle);
                    wall.layer = GameLayers.Prop;
                }
            }
        }

        private void BuildBlocks(Transform root, DeterministicRandom random)
        {
            int count = random.Range(_config.BlocksPerChunk.x, _config.BlocksPerChunk.y + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = RandomTrackPoint(random, chunkIndexClearance: true);
                float height = random.Range(1.2f, 3.4f);
                float footprint = random.Range(1.5f, 4f);
                if (!_occupancy.TryReserve(position.x, position.z, footprint, footprint)) continue;

                GameObject block = PrimitiveFactory.Box(
                    "Block", root,
                    new Vector3(position.x, height * 0.5f, position.z),
                    new Vector3(footprint, height, footprint),
                    Quaternion.Euler(0f, random.Range(0f, 360f), 0f),
                    _materials.Obstacle);
                block.layer = GameLayers.Prop;
            }
        }

        private void BuildPillars(Transform root, DeterministicRandom random)
        {
            int count = random.Range(_config.PillarsPerChunk.x, _config.PillarsPerChunk.y + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = RandomTrackPoint(random, chunkIndexClearance: true);
                float height = random.Range(2.5f, 5.5f);
                if (!_occupancy.TryReserve(position.x, position.z, 1.8f, 1.8f)) continue;

                GameObject pillar = PrimitiveFactory.Cylinder(
                    "Pillar", root,
                    new Vector3(position.x, height * 0.5f, position.z),
                    new Vector3(0.9f, height * 0.5f, 0.9f),
                    Quaternion.identity, _materials.Accent);
                pillar.layer = GameLayers.Prop;
            }
        }

        private void BuildSweepers(Transform root, DeterministicRandom random)
        {
            int count = random.Range(_config.SweepersPerChunk.x, _config.SweepersPerChunk.y + 1);
            for (int i = 0; i < count; i++)
            {
                float z = random.Range(_minZ + 10f, _config.ChunkLength - 20f);
                float x = random.Range(-8f, 8f);
                float barWidth = random.Range(10f, 20f);
                float travel = random.Range(_config.Width * 0.35f, _config.Width * 0.7f);

                // Reserve the whole band it sweeps: a kinematic barrier will not be stopped by
                // anything it slides into, so nothing may stand in its path.
                if (!_occupancy.TryReserveSwept(x, z, barWidth, 1.4f, travel, padding: 2.5f)) continue;

                var barrier = new GameObject("Sweeper");
                barrier.transform.SetParent(root, worldPositionStays: false);
                barrier.transform.localPosition = new Vector3(x, 1.4f, z);
                barrier.layer = GameLayers.Prop;

                DescentProps.BuildSweeper(barrier.transform, barWidth, _materials);

                Rigidbody body = barrier.AddComponent<Rigidbody>();
                body.isKinematic = true;
                // ContinuousSpeculative is the one continuous mode that works on kinematic
                // bodies, and it is what stops a fast car from passing through the barrier.
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                SweepingBarrier sweeper = barrier.AddComponent<SweepingBarrier>();
                sweeper.Configure(travel, period: random.Range(3.5f, 7f), phase: random.Range(0f, 1f));
            }
        }

        private void BuildSpinners(Transform root, DeterministicRandom random)
        {
            int count = random.Range(_config.SpinnersPerChunk.x, _config.SpinnersPerChunk.y + 1);
            for (int i = 0; i < count; i++)
            {
                float z = random.Range(_minZ + 15f, _config.ChunkLength - 25f);
                float x = random.Range(-12f, 12f);
                float armLength = random.Range(14f, 24f);

                // A spinning arm sweeps a circle of its own length, and being kinematic it
                // will pass through anything left inside that circle.
                if (!_occupancy.TryReserve(x, z, armLength, armLength, padding: 2f)) continue;

                var pivot = new GameObject("Spinner");
                pivot.transform.SetParent(root, worldPositionStays: false);
                pivot.transform.localPosition = new Vector3(x, 1.3f, z);
                pivot.layer = GameLayers.Prop;

                DescentProps.BuildSpinner(pivot.transform, armLength, _materials);

                Rigidbody body = pivot.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                SpinningProp spinner = pivot.AddComponent<SpinningProp>();
                spinner.Configure(Vector3.up, random.Range(35f, 90f) * (i % 2 == 0 ? 1f : -1f));
            }
        }

        private void BuildCrates(Transform root, DeterministicRandom random)
        {
            int count = random.Range(_config.CratesPerChunk.x, _config.CratesPerChunk.y + 1);
            for (int i = 0; i < count; i++)
            {
                Vector3 position = RandomTrackPoint(random, chunkIndexClearance: false);
                if (!_occupancy.TryReserve(position.x, position.z, 1.1f, 1.1f, padding: 0.6f)) continue;

                var crate = new GameObject("Crate");
                crate.transform.SetParent(root, worldPositionStays: false);
                crate.transform.localPosition = new Vector3(position.x, 0.6f, position.z);
                crate.transform.localRotation = Quaternion.Euler(0f, random.Range(0f, 360f), 0f);
                crate.layer = GameLayers.Prop;

                DescentProps.BuildCrate(crate.transform, 1.15f, _materials);

                Rigidbody body = crate.AddComponent<Rigidbody>();
                body.mass = _config.CrateMass;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                // Discrete detection would let the car pass straight through: at 47 m/s the
                // gap between physics steps is 0.47 m, comparable to the crate itself.
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            }
        }

        private Vector3 RandomTrackPoint(DeterministicRandom random, bool chunkIndexClearance)
        {
            float margin = chunkIndexClearance ? 4f : 2f;
            float x = random.Range(-_config.HalfWidth + margin, _config.HalfWidth - margin);
            float z = random.Range(_minZ, _config.ChunkLength - 5f);
            return new Vector3(x, 0f, z);
        }

        /// <summary>
        /// Lowest z an obstacle may occupy in the current chunk. In the first chunk this keeps
        /// the start clear, so the car does not begin its run wedged inside a block.
        /// </summary>
        private float _minZ = 5f;
        private bool IsFirstChunk => _isFirstChunk;
        private bool _isFirstChunk;
    }

    /// <summary>Materials used by the descent generator, passed in rather than looked up.</summary>
    public readonly struct DescentMaterials
    {
        public readonly Material Surface;
        public readonly Material Stripe;
        public readonly Material Wall;
        public readonly Material Jump;
        public readonly Material Obstacle;
        public readonly Material Accent;
        public readonly Material Moving;
        public readonly Material Crate;

        /// <summary>Bright warning colour for the parts the player must judge distance to.</summary>
        public readonly Material Hazard;

        public DescentMaterials(Material surface, Material stripe, Material wall, Material jump,
            Material obstacle, Material accent, Material moving, Material crate, Material hazard)
        {
            Surface = surface;
            Stripe = stripe;
            Wall = wall;
            Jump = jump;
            Obstacle = obstacle;
            Accent = accent;
            Moving = moving;
            Crate = crate;
            Hazard = hazard;
        }
    }
}
