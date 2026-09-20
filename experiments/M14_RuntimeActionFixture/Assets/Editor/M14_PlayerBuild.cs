using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Editor build script for M14 Runtime Action Fixture.
/// Called by RuntimeRunner via -executeMethod.
/// Builds a standalone macOS Player.
/// </summary>
public static class M14_PlayerBuild
{
    private const string ProductName = "M14RuntimePlayer";
    private const string DefaultBuildDir = "Build";

    /// <summary>
    /// Build method called via Unity CLI -executeMethod.
    /// Uses BuildPipeline.BuildPlayer with StandaloneOSX target.
    /// Output path = {ProjectRoot}/Build/M14RuntimePlayer.app
    ///
    /// Forces PlayerSettings.productName to ensure the executable
    /// inside the .app bundle matches ProductName.
    /// </summary>
    public static void Build()
    {
        // Force product name to match what RuntimeRunner expects
        PlayerSettings.productName = ProductName;

        var scenes = EditorBuildSettingsScene.GetActiveSceneList(
            EditorBuildSettings.scenes);

        if (scenes == null || scenes.Length == 0)
        {
            Debug.LogError("[M14_PlayerBuild] No scenes in Build Settings!");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[M14_PlayerBuild] Building with {scenes.Length} scenes:");
        foreach (var s in scenes) Debug.Log($"  {s}");

        var buildDir = System.IO.Path.Combine(Application.dataPath, "..", DefaultBuildDir);

        var buildPlayerOptions = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = System.IO.Path.Combine(buildDir, ProductName + ".app"),
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(buildPlayerOptions);

        if (report.summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[M14_PlayerBuild] Build succeeded: {report.summary.outputPath}");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[M14_PlayerBuild] Build failed: {report.summary.result}");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                    {
                        Debug.LogError($"  [{msg.type}] {msg.content}");
                    }
                }
            }
            EditorApplication.Exit(1);
        }
    }
}