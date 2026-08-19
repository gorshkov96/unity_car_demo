using System.Text;
using Unity.Profiling;
using UnityEngine;

namespace CarDemo.Diagnostics
{
    /// <summary>
    /// Measures the frame budget with <see cref="ProfilerRecorder"/> and reports it.
    ///
    /// The project rule is "optimise from a measurement, not from a feeling", so this is the
    /// baseline instrument: CPU main thread time, render statistics and per-frame GC allocations,
    /// summarised as percentiles against the 16.66 ms budget for 60 FPS.
    ///
    /// Values are read in nanoseconds and converted once per frame; the text summary is rebuilt
    /// only on the reporting interval, never per frame.
    /// </summary>
    public sealed class PerformanceProbe : MonoBehaviour
    {
        [SerializeField] private float _targetFrameRate = 60f;
        [Tooltip("How often the summary is written to the log, seconds. Zero disables logging.")]
        [SerializeField] private float _reportInterval = 5f;
        [Tooltip("Stop the process after this many seconds. Zero keeps it running (interactive use).")]
        [SerializeField] private float _autoQuitAfter;

        private ProfilerRecorder _mainThreadTime;
        private ProfilerRecorder _drawCalls;
        private ProfilerRecorder _batches;
        private ProfilerRecorder _setPassCalls;
        private ProfilerRecorder _gcAllocatedInFrame;

        private FrameStats _cpuStats;
        private FrameStats _frameStats;
        private readonly StringBuilder _builder = new StringBuilder(256);
        private float _nextReportTime;
        private float _elapsed;
        private long _gcPeak;

        public double BudgetMilliseconds => 1000.0 / _targetFrameRate;
        public FrameStats CpuStats => _cpuStats;
        public FrameStats FrameTimeStats => _frameStats;
        public long DrawCalls => _drawCalls.LastValue;
        public long Batches => _batches.LastValue;
        public long SetPassCalls => _setPassCalls.LastValue;
        public long GcAllocatedInFrame => _gcAllocatedInFrame.LastValue;

        private void Awake()
        {
            _cpuStats = new FrameStats(1024);
            _frameStats = new FrameStats(1024);

            if (_autoQuitAfter > 0f)
            {
                // Benchmark run: uncap the frame rate so the measurement shows the actual
                // headroom. With VSync on, every frame reads as exactly the refresh interval
                // and the numbers say nothing about how much room is left.
                QualitySettings.vSyncCount = 0;
            }
        }

        private void OnEnable()
        {
            _mainThreadTime = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "CPU Main Thread Frame Time");
            _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            _setPassCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            _gcAllocatedInFrame = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
            _nextReportTime = _reportInterval;
        }

        private void OnDisable()
        {
            // Recorders hold native memory; not disposing them leaks.
            _mainThreadTime.Dispose();
            _drawCalls.Dispose();
            _batches.Dispose();
            _setPassCalls.Dispose();
            _gcAllocatedInFrame.Dispose();
        }

        private void Update()
        {
            _elapsed += Time.unscaledDeltaTime;

            if (_mainThreadTime.Valid && _mainThreadTime.LastValue > 0)
            {
                _cpuStats.Add(_mainThreadTime.LastValue * 1e-6);
            }

            _frameStats.Add(Time.unscaledDeltaTime * 1000.0);

            long gc = _gcAllocatedInFrame.LastValue;
            if (gc > _gcPeak) _gcPeak = gc;

            if (_reportInterval > 0f && _elapsed >= _nextReportTime)
            {
                _nextReportTime += _reportInterval;
                Debug.Log(BuildReport());
                _cpuStats.Clear();
                _frameStats.Clear();
                _gcPeak = 0;
            }

            if (_autoQuitAfter > 0f && _elapsed >= _autoQuitAfter)
            {
                Application.Quit();
            }
        }

        /// <summary>Builds the summary line. Allocates, so it runs on the report interval only.</summary>
        public string BuildReport()
        {
            // A frame is only over budget if it misses it by more than a rounding margin:
            // a VSync-locked 60 Hz display delivers 16.67 ms against a 16.66 ms budget, and
            // counting that as a miss would report 100% failure on a perfectly healthy frame.
            double budget = BudgetMilliseconds * 1.02;
            _builder.Clear();
            _builder.Append("[PERF] frame ms: median=").Append(_frameStats.Percentile(0.5).ToString("0.00"))
                .Append(" p95=").Append(_frameStats.Percentile(0.95).ToString("0.00"))
                .Append(" p99=").Append(_frameStats.Percentile(0.99).ToString("0.00"))
                .Append(" max=").Append(_frameStats.Max().ToString("0.00"))
                .Append(" | cpu main ms: median=").Append(_cpuStats.Percentile(0.5).ToString("0.00"))
                .Append(" p95=").Append(_cpuStats.Percentile(0.95).ToString("0.00"))
                .Append(" | over budget(").Append(BudgetMilliseconds.ToString("0.0")).Append("ms)=")
                .Append((_frameStats.ShareOverBudget(budget) * 100.0).ToString("0.0")).Append('%')
                .Append(" | fps=").Append((1000.0 / Mathf.Max(0.001f, (float)_frameStats.Percentile(0.5))).ToString("0"))
                .Append(" | draw=").Append(DrawCalls)
                .Append(" batches=").Append(Batches)
                .Append(" setpass=").Append(SetPassCalls)
                .Append(" | gc peak/frame=").Append(_gcPeak).Append('B')
                .Append(" samples=").Append(_frameStats.Count);
            return _builder.ToString();
        }

        /// <summary>Configures the probe before Awake, for scene generators and benchmark runs.</summary>
        public void Configure(float targetFrameRate, float reportInterval, float autoQuitAfter)
        {
            _targetFrameRate = targetFrameRate;
            _reportInterval = reportInterval;
            _autoQuitAfter = autoQuitAfter;
        }
    }
}
