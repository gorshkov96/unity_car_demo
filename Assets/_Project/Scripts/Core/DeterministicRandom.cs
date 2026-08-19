using UnityEngine;

namespace CarDemo.Core
{
    /// <summary>
    /// Seeded pseudo-random helper. Same seed always produces the same sequence,
    /// which keeps procedural generation reproducible and testable.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private readonly System.Random _random;

        public DeterministicRandom(int seed)
        {
            _random = new System.Random(seed);
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            return minInclusive + (float)_random.NextDouble() * (maxInclusive - minInclusive);
        }

        public int Range(int minInclusive, int maxExclusive)
        {
            return _random.Next(minInclusive, maxExclusive);
        }

        /// <summary>Random point inside a square of the given half-extent, on the XZ plane.</summary>
        public Vector3 PointOnGround(float halfExtent)
        {
            return new Vector3(Range(-halfExtent, halfExtent), 0f, Range(-halfExtent, halfExtent));
        }
    }
}
