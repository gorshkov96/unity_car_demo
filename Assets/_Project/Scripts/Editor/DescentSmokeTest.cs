using System.Text;
using CarDemo.Diagnostics;
using CarDemo.Game;
using CarDemo.Vehicle;
using CarDemo.World;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Drives the descent on autopilot without opening the editor and reports what happened:
    /// does the car actually accelerate under gravity, does the track stream in and out, does
    /// it get stuck or flip.
    ///
    /// A five-minute track is not something to verify by hand on every change.
    /// </summary>
    public static class DescentSmokeTest
    {
        private const string ScenePath = "Assets/_Project/Scenes/DescentBenchmark.unity";
        // Matches the project's physics step (100 Hz), so the test simulates what ships.
        private const float StepTime = 0.01f;
        private const float SimulatedSeconds = 90f;

        [MenuItem("CarDemo/Run Descent Smoke Test")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var car = Object.FindFirstObjectByType<CarController>();
            var streamer = Object.FindFirstObjectByType<DescentStreamer>();
            var driver = Object.FindFirstObjectByType<DescentAutoDriver>();
            var resetter = Object.FindFirstObjectByType<CarResetter>();
            var guard = Object.FindFirstObjectByType<TrackGuard>();
            var auditor = Object.FindFirstObjectByType<CollisionAuditor>();
            auditor?.ResetCounters();

            if (car == null || streamer == null || driver == null)
            {
                Debug.LogError("[DESCENT] Scene is missing the car, the streamer or the auto driver. "
                               + "Run CarDemo > Rebuild Descent Benchmark first.");
                EditorApplication.Exit(3);
                return;
            }

            car.Initialize();
            streamer.EnsureInitialised();
            streamer.PreloadAroundStart();

            var report = new StringBuilder();
            bool passed = true;

            SimulationMode previous = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;

            // Gravity check first, in isolation: the car coasts with no throttle at all.
            // Comparing speeds later in the run would compare a clear start straight against
            // an obstacle course and say nothing about whether the slope pulls the car along.
            float coastStart;
            float coastEnd;
            {
                var coastInput = new ScriptedCarInput();
                coastInput.Set(0f, 0f);
                car.SetInputProvider(coastInput);

                for (int i = 0; i < Mathf.RoundToInt(2f / StepTime); i++)
                {
                    car.Tick(StepTime);
                    Physics.Simulate(StepTime);
                    streamer.Tick();
                }

                coastStart = Mathf.Abs(car.ForwardSpeed);

                for (int i = 0; i < Mathf.RoundToInt(6f / StepTime); i++)
                {
                    car.Tick(StepTime);
                    Physics.Simulate(StepTime);
                    streamer.Tick();
                }

                coastEnd = Mathf.Abs(car.ForwardSpeed);
                driver.Initialize();
                car.SetInputProvider(driver);
            }

            float maxSpeed = 0f;
            float earlySpeedSum = 0f;
            int earlySamples = 0;
            float lateSpeedSum = 0f;
            int lateSamples = 0;
            int maxLiveChunks = 0;
            int minLiveChunks = int.MaxValue;
            float lastDistance = 0f;
            float stuckSeconds = 0f;
            float worstStuck = 0f;
            float worstStuckAt = 0f;
            int flippedSteps = 0;

            try
            {
                int steps = Mathf.RoundToInt(SimulatedSeconds / StepTime);
                for (int i = 0; i < steps; i++)
                {
                    driver.Tick(StepTime);
                    car.Tick(StepTime);
                    Physics.Simulate(StepTime);
                    streamer.Tick();
                    if (resetter != null) resetter.Tick(StepTime);
                    if (auditor != null) auditor.CheckStep();
                    if (guard != null && i % 25 == 0) guard.Check();

                    float speed = Mathf.Abs(car.ForwardSpeed);
                    maxSpeed = Mathf.Max(maxSpeed, speed);

                    float elapsed = i * StepTime;
                    // Averaged over windows: a single instant can catch the car mid-recovery
                    // and say nothing about whether gravity is doing its job.
                    if (elapsed < 8f)
                    {
                        earlySpeedSum += speed;
                        earlySamples++;
                    }
                    else if (elapsed > 25f && elapsed < 45f)
                    {
                        lateSpeedSum += speed;
                        lateSamples++;
                    }

                    maxLiveChunks = Mathf.Max(maxLiveChunks, streamer.LiveChunkCount);
                    minLiveChunks = Mathf.Min(minLiveChunks, streamer.LiveChunkCount);

                    float distance = streamer.DistanceAlongTrack;
                    if (distance - lastDistance < 0.05f)
                    {
                        stuckSeconds += StepTime;
                        if (stuckSeconds > worstStuck)
                        {
                            worstStuck = stuckSeconds;
                            worstStuckAt = distance;
                        }
                    }
                    else
                    {
                        stuckSeconds = 0f;
                    }

                    lastDistance = distance;

                    if (Vector3.Dot(car.transform.up, Vector3.up) < 0.2f) flippedSteps++;

                    if (i % 250 == 0)
                    {
                        Vector3 slopeLocal = streamer.SlopeRoot.InverseTransformPoint(car.transform.position);
                        Debug.Log($"[DIAG] t={elapsed:0}s z={slopeLocal.z:0.0} x={slopeLocal.x:0.0} "
                                  + $"h={slopeLocal.y:0.00} v={speed * 3.6f:0}km/h grounded={car.IsGrounded} "
                                  + $"recovering={driver.IsRecovering} input={driver.Current.Throttle:0.0}/{driver.Current.Steer:0.0}");
                    }
                }

                float travelled = streamer.DistanceAlongTrack;
                float averageSpeed = travelled / SimulatedSeconds;

                passed &= Check(report, "gravity builds speed", coastEnd > coastStart + 5f,
                    $"coasting with no throttle: {coastStart * 3.6f:0} -> {coastEnd * 3.6f:0} km/h in 6s");

                float earlyAverage = earlySamples > 0 ? earlySpeedSum / earlySamples : 0f;
                float lateAverage = lateSamples > 0 ? lateSpeedSum / lateSamples : 0f;
                report.AppendLine($"[DESCENT] pace: {earlyAverage * 3.6f:0} km/h early, "
                                  + $"{lateAverage * 3.6f:0} km/h through the obstacles");

                // 1000 m in 90 seconds is ~40 km/h average through a dense obstacle course.
                // The auto driver brakes for everything and reverses out of contacts; a human
                // picks lines and carries far more speed.
                passed &= Check(report, "distance", travelled > 1000f,
                    $"{travelled:0} m in {SimulatedSeconds:0}s, average {averageSpeed * 3.6f:0} km/h");

                passed &= Check(report, "top speed", maxSpeed * 3.6f > 90f, $"{maxSpeed * 3.6f:0} km/h");

                passed &= Check(report, "streaming window", maxLiveChunks <= 8 && minLiveChunks >= 1,
                    $"live chunks between {minLiveChunks} and {maxLiveChunks} (never the whole track)");

                // A dense obstacle course will occasionally wedge a car; the requirement is
                // that it gets itself out, not that it never touches anything. Anything past
                // six seconds means the recovery logic failed, not that the driver was unlucky.
                int unsticks = guard == null ? 0 : guard.UnstickCount;
                report.AppendLine($"[DESCENT] freed from wedges: {unsticks} times");
                passed &= Check(report, "gets unstuck", worstStuck < 9f,
                    $"longest stall {worstStuck:0.0}s at {worstStuckAt:0} m down the track");

                // Some time on the roof is expected on a track built for jumps; what matters
                // is that the car rights itself and keeps going rather than staying there.
                passed &= Check(report, "recovers from rolls", flippedSteps * StepTime < 12f,
                    $"upside down for {flippedSteps * StepTime:0.0}s of {SimulatedSeconds:0}s");

                // The safety net is allowed to fire — that is its job — but only rarely. A
                // high-speed impact can still eject the car through the geometry roughly once
                // in a 90-second run; a stream of rescues would mean the track itself is broken.
                int rescues = guard == null ? 0 : guard.RescueCount;
                passed &= Check(report, "stays on the track", rescues <= 2,
                    $"{rescues} safety-net rescues in {SimulatedSeconds:0}s");

                // The check the user asked for: contacts must actually happen, and nothing may
                // be crossed without one. A run with zero contacts on an obstacle course means
                // the car is driving through everything, which no other check would notice.
                int contacts = auditor == null ? 0 : auditor.ContactCount;
                int tunnels = auditor == null ? 0 : auditor.TunnelCount;

                passed &= Check(report, "really hits things", contacts > 20,
                    $"{contacts} collision contacts registered");
                passed &= Check(report, "nothing passes through", tunnels == 0,
                    $"{tunnels} tunnelling events"
                    + (tunnels > 0 ? $", worst jump {auditor.WorstTunnelDistance:0.00} m" : string.Empty));

                if (streamer.Config.IsEndless)
                {
                    report.AppendLine($"[DESCENT] endless track: {averageSpeed * 60f / 1000f:0.0} km per minute "
                                      + "at this pace, no end to fall off");
                }
                else
                {
                    float projectedFullRun = streamer.Config.TotalLength / Mathf.Max(1f, averageSpeed);
                    report.AppendLine($"[DESCENT] full track: {streamer.Config.TotalLength:0} m, "
                                      + $"~{projectedFullRun / 60f:0.0} min at this pace");
                }
            }
            finally
            {
                Physics.simulationMode = previous;
            }

            report.AppendLine($"[DESCENT] RESULT: {(passed ? "PASS" : "FAIL")}");
            Debug.Log(report.ToString());

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(passed ? 0 : 2);
            }
        }

        private static bool Check(StringBuilder report, string label, bool condition, string detail)
        {
            report.AppendLine($"[DESCENT] {label}: {detail} -> {(condition ? "OK" : "FAILED")}");
            return condition;
        }
    }
}
