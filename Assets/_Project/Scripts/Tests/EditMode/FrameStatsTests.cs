using CarDemo.Diagnostics;
using NUnit.Framework;

namespace CarDemo.Tests.EditMode
{
    public sealed class FrameStatsTests
    {
        [Test]
        public void Percentile_OnKnownSet_MatchesLinearInterpolation()
        {
            var stats = new FrameStats(16);
            for (int i = 1; i <= 10; i++) stats.Add(i);

            Assert.AreEqual(5.5, stats.Percentile(0.5), 1e-6);
            Assert.AreEqual(1.0, stats.Percentile(0.0), 1e-6);
            Assert.AreEqual(10.0, stats.Percentile(1.0), 1e-6);
        }

        [Test]
        public void RingBuffer_KeepsOnlyTheLastSamples()
        {
            var stats = new FrameStats(4);
            for (int i = 1; i <= 10; i++) stats.Add(i);

            Assert.AreEqual(4, stats.Count);
            Assert.AreEqual(7.0, stats.Percentile(0.0), 1e-6, "Oldest kept sample must be 7.");
            Assert.AreEqual(10.0, stats.Max(), 1e-6);
        }

        [Test]
        public void ShareOverBudget_CountsOnlyFramesAboveTheBudget()
        {
            var stats = new FrameStats(8);
            stats.Add(10);
            stats.Add(20);
            stats.Add(30);
            stats.Add(5);

            Assert.AreEqual(0.5, stats.ShareOverBudget(16.66), 1e-6);
        }

        [Test]
        public void EmptyStats_ReturnZeroInsteadOfThrowing()
        {
            var stats = new FrameStats(4);
            Assert.AreEqual(0, stats.Percentile(0.5));
            Assert.AreEqual(0, stats.Max());
            Assert.AreEqual(0, stats.Mean());
            Assert.AreEqual(0, stats.ShareOverBudget(16.66));
        }
    }
}
