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

        /// <summary>
        /// Development build of the benchmark scene. Development mode is required for the
        /// profiler counters the probe reads, and the docs are explicit that editor timings
        /// are not a valid measurement.
        /// </summary>
        [MenuItem("CarDemo/Build Benchmark Player")]
        public static void BuildBenchmark()
        {
            Build("Assets/_Project/Scenes/Benchmark.unity", "Builds/CarDemoBenchmark.app",
                BuildOptions.Development | BuildOptions.EnableDeepProfilingSupport);
        }

        [MenuItem("CarDemo/Build macOS Player")]
        public static void BuildMac()
        {
            Build(ScenePath, OutputPath, BuildOptions.None);
        }

        private static void Build(string scenePath, string outputPath, BuildOptions buildOptions)
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
                scenes = new[] { scenePath },
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
