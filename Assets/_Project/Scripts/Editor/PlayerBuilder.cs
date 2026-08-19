using System;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CarDemo.EditorTools
{
    /// <summary>
    /// Builds a standalone macOS player from the command line, so the demo can be
    /// launched and watched without opening the editor.
    /// </summary>
    public static class PlayerBuilder
    {
        private const string ScenePath = "Assets/_Project/Scenes/Demo.unity";
        private const string OutputPath = "Builds/CarDemo.app";
        private const string DescentScenePath = "Assets/_Project/Scenes/Descent.unity";

        /// <summary>
        /// Development build of the benchmark scene. Development mode is required for the
        /// profiler counters the probe reads, and the docs are explicit that editor timings
        /// are not a valid measurement.
        /// </summary>
        [MenuItem("CarDemo/Build Benchmark Player")]
        public static void BuildBenchmark()
        {
            Build(new[] { "Assets/_Project/Scenes/Benchmark.unity" }, "Builds/CarDemoBenchmark.app",
                BuildOptions.Development | BuildOptions.EnableDeepProfilingSupport);
        }

        [MenuItem("CarDemo/Build macOS Player")]
        public static void BuildMac()
        {
            // Both maps go in: the runtime switcher cycles build indices, so shipping only
            // one scene would leave the Tab key doing nothing.
            Build(new[] { ScenePath, DescentScenePath }, OutputPath, BuildOptions.None);
        }

        /// <summary>Development build of the descent, driven by the auto driver.</summary>
        [MenuItem("CarDemo/Build Descent Benchmark Player")]
        public static void BuildDescentBenchmark()
        {
            Build(new[] { "Assets/_Project/Scenes/DescentBenchmark.unity" }, "Builds/CarDemoDescent.app",
                BuildOptions.Development);
        }

        private static void Build(string[] scenePaths, string outputPath, BuildOptions buildOptions)
        {
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);
            PlayerSettings.productName = "CarDemo";
            PlayerSettings.defaultScreenWidth = 1600;
            PlayerSettings.defaultScreenHeight = 900;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;

            var options = new BuildPlayerOptions
            {
                scenes = scenePaths,
                locationPathName = outputPath,
                target = BuildTarget.StandaloneOSX,
                options = buildOptions,
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"[BUILD] result={summary.result} errors={summary.totalErrors} " +
                      $"warnings={summary.totalWarnings} size={summary.totalSize} out={summary.outputPath}");

            if (Application.isBatchMode)
            {
                EditorApplication.Exit(summary.result == BuildResult.Succeeded ? 0 : 1);
            }
        }
    }
}
