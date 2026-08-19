using System;

namespace CarDemo.Diagnostics
{
    /// <summary>
    /// Rolling window of frame times with percentile queries.
    /// Plain C# and allocation-free after construction, so it is covered by EditMode tests
    /// and safe to update every frame.
    ///
    /// Percentiles matter more than an average here: the docs are explicit that a single
    /// frame over budget is already visible to the player, and an average hides exactly that.
    /// </summary>
    public sealed class FrameStats
    {
        private readonly double[] _samples;
        private readonly double[] _sortBuffer;
        private int _count;
        private int _writeIndex;

        public FrameStats(int capacity = 512)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            _samples = new double[capacity];
            _sortBuffer = new double[capacity];
        }

        public int Count => _count;
        public double Last { get; private set; }

        public void Add(double milliseconds)
        {
            Last = milliseconds;
            _samples[_writeIndex] = milliseconds;
            _writeIndex = (_writeIndex + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }

        public void Clear()
        {
            _count = 0;
            _writeIndex = 0;
            Last = 0;
        }

        /// <summary>Percentile in [0, 1]. 0.5 = median, 0.99 = worst percent of frames.</summary>
        public double Percentile(double percentile)
        {
            if (_count == 0) return 0;

            Array.Copy(_samples, _sortBuffer, _count);
            Array.Sort(_sortBuffer, 0, _count);

            double rank = percentile * (_count - 1);
            int low = (int)Math.Floor(rank);
            int high = (int)Math.Ceiling(rank);
            if (low == high) return _sortBuffer[low];

            double weight = rank - low;
            return _sortBuffer[low] * (1 - weight) + _sortBuffer[high] * weight;
        }

        public double Max()
        {
            double max = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_samples[i] > max) max = _samples[i];
            }

            return max;
        }

        public double Mean()
        {
            if (_count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < _count; i++) sum += _samples[i];
            return sum / _count;
        }

        /// <summary>Share of samples that exceeded the frame budget, in [0, 1].</summary>
        public double ShareOverBudget(double budgetMilliseconds)
        {
            if (_count == 0) return 0;
            int over = 0;
            for (int i = 0; i < _count; i++)
            {
                if (_samples[i] > budgetMilliseconds) over++;
            }

            return over / (double)_count;
        }
    }
}
