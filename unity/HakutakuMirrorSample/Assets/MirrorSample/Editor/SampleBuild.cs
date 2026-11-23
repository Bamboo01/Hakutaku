using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HakutakuMirrorSample.EditorTools
{
    // Builds one Windows player that can run as the server or a client (see SampleApp's command line). It defines
    // HAKUTAKU_SERVER so the server SDK is included, so this is a test build: never give it to players.
    public static class SampleBuild
    {
        const string ScenePath = "Assets/MirrorSample/SampleScene.unity";
        const string OutputPath = "Builds/Windows/HakutakuMirrorSample.exe";

        [MenuItem("Tools/Hakutaku Sample/Build test player")]
        public static void BuildPlayer()
        {
            if (!File.Exists(ScenePath))
            {
                var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                EditorSceneManager.SaveScene(scene, ScenePath);
            }

            PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone, ApiCompatibilityLevel.NET_Standard);
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.StandaloneWindows64,
                extraScriptingDefines = new[] { "HAKUTAKU_SERVER" },
                options = BuildOptions.None
            });

            Debug.Log($"[SampleBuild] {report.summary.result}, {report.summary.totalErrors} error(s), {OutputPath}");
            if (Application.isBatchMode)
                EditorApplication.Exit(report.summary.result == BuildResult.Succeeded ? 0 : 1);
        }
    }
}
