using UnityEngine;
using UnityEngine.SceneManagement;
using System.IO;
using System;

/// <summary>
/// Minimal Runtime probe for M12 experiment.
///
/// Attached to a GameObject in MainScene.
/// Uses [RuntimeInitializeOnLoadMethod] so it fires automatically
/// when the Player starts.
///
/// Only runs when M12_RESULT_DIR environment variable is set.
/// Writes JSON evidence to {M12_RESULT_DIR}/runtime_evidence.json.
/// </summary>
public class RuntimeProbe : MonoBehaviour
{
    private const string ResultDirEnvVar = "M12_RESULT_DIR";

    void Awake()
    {
        Debug.Log("[M12_RuntimeProbe] MonoBehaviour Awake() fired.");
    }

    void Start()
    {
        Debug.Log("[M12_RuntimeProbe] MonoBehaviour Start() fired.");
    }

    /// <summary>
    /// Static entry point triggered after the Player loads the scene.
    /// Reads the active scene and writes evidence JSON.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void OnAfterSceneLoad()
    {
        // GUARD: Only run when M12_RESULT_DIR is explicitly set
        var resultDir = Environment.GetEnvironmentVariable(ResultDirEnvVar);
        if (string.IsNullOrEmpty(resultDir))
        {
            Debug.Log("[M12_RuntimeProbe] Skipped (no M12_RESULT_DIR)");
            return;
        }

        Debug.Log("[M12_RuntimeProbe] Player runtime started. Reading ActiveScene...");

        var activeScene = SceneManager.GetActiveScene();
        var sceneName = activeScene.IsValid() ? activeScene.name : "(invalid)";

        Debug.Log($"[M12_RuntimeProbe] ActiveScene.name = '{sceneName}'");
        Debug.Log($"[M12_RuntimeProbe] Application.isEditor = {Application.isEditor}");
        Debug.Log($"[M12_RuntimeProbe] Application.isPlaying = {Application.isPlaying}");
        Debug.Log($"[M12_RuntimeProbe] Platform = {Application.platform}");

        // Build minimal result matching M12 spec
        var result = new M12ProbeResult
        {
            executionMode = "Runtime",
            activeScene = sceneName,
            isEditor = Application.isEditor,
            isPlaying = Application.isPlaying,
            success = activeScene.IsValid() && sceneName == "MainScene",
        };

        var json = JsonUtility.ToJson(result, true);
        Debug.Log($"[M12_RuntimeProbe] RESULT:\n{json}");

        Directory.CreateDirectory(resultDir);
        var outputPath = Path.Combine(resultDir, "runtime_evidence.json");
        File.WriteAllText(outputPath, json);
        Debug.Log($"[M12_RuntimeProbe] Evidence written to: {outputPath}");

        // Write marker for external polling
        var markerPath = Path.Combine(resultDir, "m12_done.marker");
        File.WriteAllText(markerPath, json);

        Debug.Log("[M12_RuntimeProbe] Done. Quitting Player.");
        Application.Quit();
    }
}

/// <summary>
/// Serializable result matching M12 spec
/// </summary>
[System.Serializable]
public class M12ProbeResult
{
    public string executionMode;
    public string activeScene;
    public bool isEditor;
    public bool isPlaying;
    public bool success;
}