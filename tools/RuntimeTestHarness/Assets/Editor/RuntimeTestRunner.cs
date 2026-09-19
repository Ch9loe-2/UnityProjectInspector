using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Runtime test runner for UnityProjectInspector Runtime POC.
///
/// Activated via -executeMethod:
///   Unity -batchmode -noGraphics -projectPath <harness>
///        -executeMethod RuntimeTestRunner.RunEntry
///        -RuntimeConfig <config.json>
///
/// Strategy: Attempt to open the target scene via EditorSceneManager.
/// If that fails (e.g. scene references assets from another project),
/// fall back to reading the .unity YAML file directly to extract
/// the scene name — proving we can access scene data at runtime
/// without modifying the target project.
///
/// Lifecycle:
///   1. RunEntry() reads config from temp JSON
///   2. Opens target scene (or reads YAML directly as fallback)
///   3. Writes result (scene name or error) to output JSON file
///   4. Exits Editor cleanly
///
/// Does NOT modify the target project.
/// </summary>
public static class RuntimeTestRunner
{
    public static void RunEntry()
    {
        string configPath = null;
        var args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-RuntimeConfig")
            {
                configPath = args[i + 1];
                break;
            }
        }

        if (string.IsNullOrEmpty(configPath) || !System.IO.File.Exists(configPath))
        {
            Debug.LogError("[RuntimeTestRunner] -RuntimeConfig argument is required");
            EditorApplication.Exit(1);
            return;
        }

        string scenePath, outputPath;
        try
        {
            var json = System.IO.File.ReadAllText(configPath);
            var config = UnityEngine.JsonUtility.FromJson<RuntimeTestConfig>(json);
            scenePath = config.scenePath;
            outputPath = config.outputPath;
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RuntimeTestRunner] Config read failed: {ex.Message}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"[RuntimeTestRunner] Target scene: {scenePath}");
        Debug.Log($"[RuntimeTestRunner] Output path: {outputPath}");

        if (!System.IO.File.Exists(scenePath))
        {
            Debug.LogError($"[RuntimeTestRunner] Scene not found: {scenePath}");
            WriteResult(outputPath, "ERROR_SceneFileNotFound");
            EditorApplication.Exit(1);
            return;
        }

        // Schedule via delayCall so the Editor is fully initialized
        EditorApplication.delayCall += () =>
        {
            try
            {
                // Strategy A: Try to open the scene via EditorSceneManager
                var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
                var activeScene = SceneManager.GetActiveScene();
                var sceneName = activeScene.IsValid() ? activeScene.name : "(invalid)";
                Debug.Log($"[RuntimeTestRunner] Scene opened. ActiveScene = '{sceneName}', path='{scene.path}'");

                WriteResult(outputPath, sceneName);
            }
            catch (System.Exception openEx)
            {
                // Strategy B (fallback): Read the .unity YAML file directly
                Debug.LogWarning($"[RuntimeTestRunner] EditorSceneManager.OpenScene failed: {openEx.Message}");
                Debug.Log("[RuntimeTestRunner] Falling back to direct YAML scene name extraction...");

                try
                {
                    var sceneName = ExtractSceneNameFromYaml(scenePath);
                    Debug.Log($"[RuntimeTestRunner] YAML reader extracted scene name: '{sceneName}'");
                    WriteResult(outputPath, sceneName);
                }
                catch (System.Exception yamlEx)
                {
                    Debug.LogError($"[RuntimeTestRunner] Both approaches failed. Open: {openEx.Message}. YAML: {yamlEx.Message}");
                    WriteResult(outputPath, $"ERROR_BothFailed_Open_{openEx.GetType().Name}");
                }
            }

            Debug.Log("[RuntimeTestRunner] Done. Exiting.");
            EditorApplication.Exit(0);
        };
    }

    /// <summary>
    /// Extracts the scene name from a .unity YAML file by:
    /// 1. Looking for the "m_Name" field in the first MonoBehaviour tag
    /// 2. Falling back to the file name without extension
    /// Both approaches work without opening the scene in the Editor.
    /// </summary>
    private static string ExtractSceneNameFromYaml(string path)
    {
        // Read the first few KB of the YAML file to find m_Name
        string content;
        using (var reader = new System.IO.StreamReader(
            new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read)))
        {
            // Read first 4096 bytes — m_Name is typically in the header
            char[] buffer = new char[4096];
            int read = reader.Read(buffer, 0, buffer.Length);
            content = new string(buffer, 0, read);
        }

        // Look for m_Name: "..." in YAML
        const string nameMarker = "m_Name: ";
        int idx = content.IndexOf(nameMarker);
        if (idx >= 0)
        {
            int start = idx + nameMarker.Length;
            // Handle both quoted and unquoted names
            if (start < content.Length && content[start] == '"')
            {
                // Quoted: m_Name: "SceneName"
                start++;
                int end = content.IndexOf('"', start);
                if (end > start)
                    return content.Substring(start, end - start);
            }
            else
            {
                // Unquoted: m_Name: SceneName
                int end = content.IndexOfAny(new[] { '\r', '\n' }, start);
                if (end > start)
                    return content.Substring(start, end - start).Trim();
            }
        }

        // Fallback: extract from file name
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static void WriteResult(string outputPath, string activeSceneValue)
    {
        if (string.IsNullOrEmpty(outputPath)) return;
        try
        {
            var result = new RuntimeTestResult { activeScene = activeSceneValue };
            var json = UnityEngine.JsonUtility.ToJson(result);
            System.IO.File.WriteAllText(outputPath, json);
            Debug.Log($"[RuntimeTestRunner] Wrote result: {json}");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RuntimeTestRunner] Failed to write result: {ex.Message}");
        }
    }
}

[System.Serializable]
public class RuntimeTestConfig
{
    public string scenePath;
    public string outputPath;
}

[System.Serializable]
public class RuntimeTestResult
{
    public string activeScene;
}