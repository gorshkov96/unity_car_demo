using System.Collections.Generic;
using UnityEngine;

namespace CarDemo.World
{
    /// <summary>
    /// Keeps track of the footprint each generated object occupies inside a chunk, so the
    /// generator can refuse to place one thing inside another.
    ///
    /// This is done analytically rather than with physics queries for two reasons: the result
    /// stays deterministic (a chunk must regenerate identically when the player drives back
    /// into it), and it works before the colliders exist.
    ///
    /// It is also the only way to keep moving hazards apart: spinners and sweepers are
    /// kinematic bodies, and in PhysX kinematic bodies pass straight through each other and
    /// through static geometry. Nothing at runtime will separate them — they have to be placed
    /// apart in the first place.
    /// </summary>
    public sealed class ChunkOccupancy
    {
        private readonly List<Rect> _taken = new List<Rect>();

        public int Count => _taken.Count;

        public void Clear() => _taken.Clear();

        /// <summary>
        /// Reserves an axis-aligned footprint on the track plane if it is free.
        /// Returns false when it overlaps something already placed.
        /// </summary>
        /// <param name="x">Centre across the track.</param>
        /// <param name="z">Centre along the track.</param>
        /// <param name="width">Full size across the track.</param>
        /// <param name="length">Full size along the track.</param>
        /// <param name="padding">Extra clearance kept around the object.</param>
        public bool TryReserve(float x, float z, float width, float length, float padding = 1.5f)
        {
            var candidate = new Rect(
                x - width * 0.5f - padding,
                z - length * 0.5f - padding,
                width + padding * 2f,
                length + padding * 2f);

            for (int i = 0; i < _taken.Count; i++)
            {
                if (_taken[i].Overlaps(candidate)) return false;
            }

            _taken.Add(candidate);
            return true;
        }

        /// <summary>
        /// Reserves a swept footprint: the full band an object covers as it moves.
        /// A sweeping barrier occupies everything it slides across, not just where it starts.
        /// </summary>
        public bool TryReserveSwept(float x, float z, float width, float length, float travel, float padding = 1.5f)
        {
            return TryReserve(x, z, width + Mathf.Abs(travel), length, padding);
        }
    }
}
