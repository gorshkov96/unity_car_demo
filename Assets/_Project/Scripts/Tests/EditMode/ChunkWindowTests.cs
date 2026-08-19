using CarDemo.World;
using NUnit.Framework;

namespace CarDemo.Tests.EditMode
{
    public sealed class ChunkWindowTests
    {
        [Test]
        public void AtTheStart_NothingIsKeptBeforeTheFirstChunk()
        {
            ChunkWindow window = ChunkWindow.Around(
                distanceAlongTrack: 10f, chunkLength: 100f, ahead: 3, behind: 2, totalChunks: 50);

            Assert.AreEqual(0, window.First, "There is no track above the start.");
            Assert.AreEqual(3, window.Last);
        }

        [Test]
        public void InTheMiddle_WindowFollowsTheCar()
        {
            ChunkWindow window = ChunkWindow.Around(1050f, 100f, ahead: 3, behind: 2, totalChunks: 50);

            Assert.AreEqual(8, window.First, "Chunk 10 minus two behind.");
            Assert.AreEqual(13, window.Last, "Chunk 10 plus three ahead.");
            Assert.AreEqual(6, window.Count);
        }

        [Test]
        public void NearTheEnd_WindowIsClampedToTheLastChunk()
        {
            ChunkWindow window = ChunkWindow.Around(4950f, 100f, ahead: 4, behind: 1, totalChunks: 50);

            Assert.AreEqual(49, window.Last, "There is no chunk past the end of the track.");
            Assert.IsTrue(window.Contains(49));
            Assert.IsFalse(window.Contains(50));
        }

        [Test]
        public void DrivingBackUp_KeepsChunksBehindTheCar()
        {
            // The car turned around at chunk 20 and drove back to chunk 18: the window must
            // follow it, so the rebuilt track is there.
            ChunkWindow window = ChunkWindow.Around(1850f, 100f, ahead: 3, behind: 2, totalChunks: 50);

            Assert.IsTrue(window.Contains(18));
            Assert.IsTrue(window.Contains(16), "Two chunks behind stay loaded for a turnaround.");
        }

        [Test]
        public void WellPastTheEnd_OfAFiniteTrack_NothingIsKept()
        {
            // Far beyond the last chunk of a finite track there is nothing to load — which is
            // exactly why the descent is endless by default: this state means empty space
            // under the car.
            ChunkWindow window = ChunkWindow.Around(9000f, 100f, ahead: 3, behind: 2, totalChunks: 50);
            Assert.AreEqual(0, window.Count);
        }

        [Test]
        public void OnTheLastChunk_OfAFiniteTrack_TailStaysLoaded()
        {
            ChunkWindow window = ChunkWindow.Around(4920f, 100f, ahead: 3, behind: 2, totalChunks: 50);
            Assert.AreEqual(47, window.First);
            Assert.AreEqual(49, window.Last, "Clamped to the final chunk.");
        }

        [Test]
        public void EndlessTrack_KeepsGeneratingAhead()
        {
            // totalChunks = 0 means endless: there must always be ground in front, however
            // far the car has driven.
            ChunkWindow window = ChunkWindow.Around(50_000f, 100f, ahead: 4, behind: 2, totalChunks: 0);

            Assert.AreEqual(498, window.First);
            Assert.AreEqual(504, window.Last);
            Assert.AreEqual(7, window.Count);
        }

        [Test]
        public void EndlessTrack_StillClampsAtTheTop()
        {
            ChunkWindow window = ChunkWindow.Around(10f, 100f, ahead: 4, behind: 3, totalChunks: 0);
            Assert.AreEqual(0, window.First, "There is no track above the start, endless or not.");
        }

        [Test]
        public void IndexOf_MapsDistanceToChunk()
        {
            Assert.AreEqual(0, ChunkWindow.IndexOf(0f, 100f));
            Assert.AreEqual(0, ChunkWindow.IndexOf(99.9f, 100f));
            Assert.AreEqual(1, ChunkWindow.IndexOf(100f, 100f));
            Assert.AreEqual(-1, ChunkWindow.IndexOf(-1f, 100f), "Above the start is a negative index.");
        }

        [Test]
        public void ZeroChunkLength_DoesNotThrow()
        {
            ChunkWindow window = ChunkWindow.Around(100f, 0f, 3, 2, 50);
            Assert.AreEqual(0, window.Count);
        }
    }
}
