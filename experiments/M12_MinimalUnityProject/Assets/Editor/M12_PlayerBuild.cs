using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Builds M12 standalone macOS Player for Runtime evidence experiment.
///
/// Call via:
///   Unity -batchmode -projectPath <path> -executeMethod M12_PlayerBuild.Build -quit
/// </summary>
public static class M12_PlayerBuild
{
    private const string BuildPath = "Build/M12MinimalPlayer";

    public static void Build()
    {
        Debug.Log("[M12_Build] Starting M12 Player build...");

        var scenes = EditorBuildSettings.scenes;
        Debug.Log($"[M12_Build] Build Settings has {scenes?.Length ?? 0} scenes");

        var options = new BuildPlayerOptions
        {
            scenes = EditorBuildSettingsScene.GetActiveSceneList(scenes),
            locationPathName = BuildPath,
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None,
        };

        var report = BuildPipeline.BuildPlayer(options);
        var summary = report.summary;

        if (summary.result == BuildResult.Succeeded)
        {
            Debug.Log($"[M12_Build] Player built successfully to {BuildPath}");
            Debug.Log($"[M12_Build] Build size: {summary.totalSize} bytes");
            Debug.Log($"[M12_Build] Build time: {summary.totalTime.TotalSeconds:F1}s");
        }
        else
        {
            Debug.LogError($"[M12_Build] Build FAILED: {summary.result}");
            Debug.LogError($"[M12_Build] Errors: {summary.totalErrors}");
            foreach (var step in report.steps)
            {
                foreach (var msg in step.messages)
                {
                    if (msg.type == LogType.Error || msg.type == LogType.Exception)
                        Debug.LogError($"[M12_Build]   {msg.content}");
                }
            }
            EditorApplication.Exit(1);
        }
    }
}