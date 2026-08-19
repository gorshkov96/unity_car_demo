using System;

namespace CarDemo.World
{
    /// <summary>
    /// Decides which chunks should exist for a given position along the track.
    ///
    /// Pure logic, no Unity types: streaming rules are exactly the kind of thing that breaks
    /// at the edges (start of the track, end of the track, driving backwards), so they are
    /// covered by fast EditMode tests instead of being debugged in play mode.
    /// </summary>
    public readonly struct ChunkWindow
    {
        public readonly int First;
        public readonly int Last;

        public ChunkWindow(int first, int last)
        {
            First = first;
            Last = last;
        }

        public int Count => Math.Max(0, Last - First + 1);

        public bool Contains(int index) => index >= First && index <= Last;

        /// <summary>
        /// Window of chunks to keep loaded around <paramref name="distanceAlongTrack"/>.
        /// Clamped to the track: no chunks before the start or past the end.
        /// </summary>
        /// <param name="totalChunks">
        /// Number of chunks in the track, or zero for an endless one. Endless is the safer
        /// default: a finite track ends in empty space, and a car that drives off the last
        /// chunk falls through the world with nothing to collide with.
        /// </param>
        public static ChunkWindow Around(
            float distanceAlongTrack, float chunkLength, int ahead, int behind, int totalChunks)
        {
            if (chunkLength <= 0f)
            {
                return new ChunkWindow(0, -1);
            }

            int current = (int)Math.Floor(distanceAlongTrack / chunkLength);
            int first = Math.Max(0, current - Math.Max(0, behind));
            int last = current + Math.Max(0, ahead);

            if (totalChunks > 0)
            {
                last = Math.Min(totalChunks - 1, last);

                // Past the end of a finite track there is nothing left to keep.
                if (first > totalChunks - 1)
                {
                    return new ChunkWindow(0, -1);
                }
            }

            return new ChunkWindow(first, last);
        }

        /// <summary>Index of the chunk containing a distance, unclamped.</summary>
        public static int IndexOf(float distanceAlongTrack, float chunkLength)
        {
            if (chunkLength <= 0f) return 0;
            return (int)Math.Floor(distanceAlongTrack / chunkLength);
        }
    }
}
