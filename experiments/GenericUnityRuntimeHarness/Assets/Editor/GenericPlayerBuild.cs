using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Generic Unity Player Build script.
///
/// Called by RuntimeRunner via -executeMethod GenericPlayerBuild.Build.
/// Builds a standalone macOS Player with ProductName = "RuntimePlayer".
///
/// This is the GENERIC harness build entry — any Unity project that uses
/// the GenericUnityRuntimeHarness can copy this script or invoke the same
/// method name for inspection builds.
/// </summary>
public static class GenericPlayerBuild
{
    /// <summary>
    /// Product name for the Player build executable.
    /// Must match the default in RuntimeRunOptions.ProductName ("RuntimePlayer").
    /// </summary>
    private const string ProductName = "RuntimePlayer";

    /// <summary>
    /// Default build output subdirectory (relative to project root).
    /// </summary>
    private const string DefaultBuildDir = "Build";

    /// <summary>
    /// Build method called via Unity CLI -executeMethod GenericPlayerBuild.Build.
    /// Uses BuildPipeline.BuildPlayer with StandaloneOSX target.
    /// Output path = {ProjectRoot}/Build/RuntimePlayer.app
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
            Debug.LogError("[GenericPlayerBuild] No scenes in Build Settings!");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[GenericPlayerBuild] Building with {scenes.Length} scenes:");
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
            Debug.Log($"[GenericPlayerBuild] Build succeeded: {report.summary.outputPath}");
            EditorApplication.Exit(0);
        }
        else
        {
            Debug.LogError($"[GenericPlayerBuild] Build failed: {report.summary.result}");
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