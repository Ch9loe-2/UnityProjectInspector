using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using System.IO;
using System.Collections;
using System;

/// <summary>
/// M14 Runtime Bridge — Player-side IPC handler.
///
/// Activated via [RuntimeInitializeOnLoadMethod] when M14_SESSION_DIR is set.
/// Polls for command files in the session directory, executes actions,
/// and writes results back. Supports the full Runtime Action pipeline.
///
/// Commands:
///   - Wait:           Sleep for N ms (via WaitForSeconds)
///   - ClickButton:    GameObject.Find(name) → Button → onClick.Invoke()
///   - ObserveActiveScene:  SceneManager.GetActiveScene().name
///   - ReadLogs:       Check accumulated runtime errors
///   - Quit:           Write evidence + done.marker + exit
/// </summary>
public class M14RuntimeBridge : MonoBehaviour
{
    private const string EnvVarName = "M14_SESSION_DIR";
    private const string CmdSubDir = "commands";
    private const string ResultSubDir = "results";

    private string _sessionDir;
    private int _commandIndex;
    private bool _hasError;
    private string _lastErrorMessage;
    private string _activeScene = "unknown";

    void Awake()
    {
        _sessionDir = Environment.GetEnvironmentVariable(EnvVarName);
        if (string.IsNullOrEmpty(_sessionDir))
        {
            Destroy(gameObject);
            return;
        }
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        StartCoroutine(RunCommandLoop());
    }

    // ─── Main Loop ───

    private IEnumerator RunCommandLoop()
    {
        Debug.Log("[M14Bridge] Bridge started. Session dir: " + _sessionDir);

        WireUpButton();
        WriteReadyMarker();

        while (true)
        {
            var cmdPath = GetCommandPath(_commandIndex);

            if (File.Exists(cmdPath))
            {
                string json = TryReadFile(cmdPath);
                if (json == null)
                {
                    yield return new WaitForSeconds(0.1f);
                    continue;
                }

                var cmd = M14JsonUtil.DeserializeCommand(json);
                if (cmd == null)
                {
                    WriteCommandResult(_commandIndex, false, null, "Failed to parse command JSON");
                    _commandIndex++;
                    continue;
                }

                // Execute command (may yield internally for Wait/Quit)
                var execResult = ProcessCommand(cmd, _commandIndex);

                if (execResult.NeedsYield)
                {
                    yield return execResult.YieldInstruction;
                }

                WriteCommandResult(_commandIndex, execResult.Success, execResult.Data, execResult.Error);

                // Update active scene after click (wait one frame for scene load)
                if (cmd.action == "ClickButton" && execResult.Success)
                {
                    yield return null;
                    var currentScene = SceneManager.GetActiveScene();
                    if (currentScene.IsValid())
                        _activeScene = currentScene.name;
                }

                // Handle Quit
                if (cmd.action == "Quit")
                {
                    yield return FinalizeAndQuit();
                    yield break;
                }

                _commandIndex++;
            }
            else
            {
                yield return new WaitForSeconds(0.1f);
            }
        }
    }

    // ─── Command Execution Helper (no yield in try block) ───

    private struct ExecResult
    {
        public bool NeedsYield;
        public YieldInstruction YieldInstruction;
        public bool Success;
        public string Error;
        public System.Collections.Generic.Dictionary<string, object> Data;
    }

    private ExecResult ProcessCommand(M14Command cmd, int index)
    {
        Debug.Log("[M14Bridge] Executing command " + index + ": " + cmd.action);

        var data = new System.Collections.Generic.Dictionary<string, object>();
        bool success = true;
        string error = null;
        bool needsYield = false;
        YieldInstruction yieldInstruction = null;

        try
        {
            switch (cmd.action)
            {
                case "Wait":
                    var ms = GetParamMilliseconds(cmd, 200);
                    needsYield = true;
                    yieldInstruction = new WaitForSeconds(ms / 1000f);
                    break;

                case "ClickButton":
                    var goName = cmd.@params != null ? cmd.@params.gameObjectName : null;
                    if (string.IsNullOrEmpty(goName))
                    {
                        success = false;
                        error = "Missing gameObjectName parameter";
                        break;
                    }

                    var go = GameObject.Find(goName);
                    data["gameObjectName"] = goName;

                    if (go == null)
                    {
                        success = false;
                        error = "GameObject '" + goName + "' not found";
                        data["found"] = false;
                    }
                    else
                    {
                        data["found"] = true;
                        var btn = go.GetComponent<Button>();
                        if (btn == null)
                        {
                            success = false;
                            error = "GameObject '" + goName + "' has no Button component";
                            data["clicked"] = false;
                        }
                        else
                        {
                            btn.onClick.Invoke();
                            data["clicked"] = true;
                            Debug.Log("[M14Bridge] Clicked button '" + goName + "'");
                        }
                    }
                    break;

                case "ObserveActiveScene":
                    var scene = SceneManager.GetActiveScene();
                    var sceneName = scene.IsValid() ? scene.name : "(invalid)";
                    _activeScene = sceneName;
                    data["sceneName"] = sceneName;
                    Debug.Log("[M14Bridge] Active scene: '" + sceneName + "'");
                    break;

                case "ReadLogs":
                    data["hasErrors"] = _hasError;
                    data["hasExceptions"] = _hasError;
                    data["logCount"] = _hasError ? 1 : 0;
                    if (_hasError)
                        data["lastError"] = (_lastErrorMessage ?? "unknown");
                    break;

                default:
                    success = false;
                    error = "Unknown action type: " + cmd.action;
                    break;
            }
        }
        catch (Exception ex)
        {
            success = false;
            error = "Action '" + cmd.action + "' threw: " + ex.Message;
            _hasError = true;
            _lastErrorMessage = ex.Message;
            Debug.LogError("[M14Bridge] " + error);
        }

        return new ExecResult
        {
            NeedsYield = needsYield,
            YieldInstruction = yieldInstruction,
            Success = success,
            Error = error,
            Data = data,
        };
    }

    // ─── Button Wiring ───

    private void WireUpButton()
    {
        try
        {
            var buttonGo = GameObject.Find("MainMenuButton");
            if (buttonGo == null)
            {
                Debug.Log("[M14Bridge] MainMenuButton not found (expected outside MainMenu)");
                return;
            }

            var button = buttonGo.GetComponent<Button>();
            if (button == null)
            {
                Debug.LogWarning("[M14Bridge] MainMenuButton has no Button");
                return;
            }

            var switcher = FindObjectOfType<SceneSwitcher>();
            button.onClick.RemoveAllListeners();
            if (switcher != null)
            {
                button.onClick.AddListener(switcher.SwitchToTargetScene);
                Debug.Log("[M14Bridge] Wired -> SceneSwitcher.SwitchToTargetScene()");
            }
            else
            {
                button.onClick.AddListener(() => SceneManager.LoadScene("TargetScene"));
                Debug.Log("[M14Bridge] Wired -> SceneManager.LoadScene(TargetScene)");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("[M14Bridge] WireUpButton failed: " + ex.Message);
        }
    }

    // ─── Markers ───

    private void WriteReadyMarker()
    {
        try
        {
            File.WriteAllText(Path.Combine(_sessionDir, "ready.marker"), "ready");
            Debug.Log("[M14Bridge] Ready marker written");
        }
        catch (Exception ex)
        {
            Debug.LogError("[M14Bridge] Ready marker failed: " + ex.Message);
        }
    }

    // ─── File Helper (no yield in try-catch) ───

    private static string TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var content = File.ReadAllText(path);
            File.Delete(path);
            return content;
        }
        catch
        {
            return null;
        }
    }

    // ─── Finalize ───

    private IEnumerator FinalizeAndQuit()
    {
        Debug.Log("[M14Bridge] Finalizing...");

        var evidence = new M14Evidence
        {
            executionMode = "Runtime",
            isEditor = Application.isEditor,
            isPlaying = Application.isPlaying,
            activeScene = _activeScene,
            success = !_hasError,
            hasErrors = _hasError,
            errorMessage = _lastErrorMessage,
        };

        var evidenceJson = JsonUtility.ToJson(evidence, true);
        File.WriteAllText(Path.Combine(_sessionDir, "evidence.json"), evidenceJson);
        File.WriteAllText(Path.Combine(_sessionDir, "done.marker"), evidenceJson);

        Debug.Log("[M14Bridge] Evidence + done written. Quitting.");

        yield return new WaitForSeconds(0.1f);
        Application.Quit();
    }

    // ─── Result Writer ───

    private void WriteCommandResult(int index, bool success,
        System.Collections.Generic.Dictionary<string, object> data, string error)
    {
        try
        {
            // Build JSON manually to ensure correct types:
            // Core expects "success":true (bool), "error":"msg" (string or null)
            var jsonParts = new System.Collections.Generic.List<string>();
            jsonParts.Add("\"action\":\"result\"");
            jsonParts.Add("\"success\":" + (success ? "true" : "false"));

            if (!string.IsNullOrEmpty(error))
                jsonParts.Add("\"error\":\"" + EscapeJson(error) + "\"");
            else
                jsonParts.Add("\"error\":null");

            if (data != null)
            {
                foreach (var kv in data)
                {
                    switch (kv.Key)
                    {
                        case "sceneName":
                            jsonParts.Add("\"sceneName\":\"" + EscapeJson(kv.Value?.ToString() ?? "") + "\"");
                            break;
                        case "found":
                            jsonParts.Add("\"found\":" + ((kv.Value is bool fb && fb) ? "true" : "false"));
                            break;
                        case "clicked":
                            jsonParts.Add("\"clicked\":" + ((kv.Value is bool cb && cb) ? "true" : "false"));
                            break;
                        case "hasErrors":
                            jsonParts.Add("\"hasErrors\":" + ((kv.Value is bool eb && eb) ? "true" : "false"));
                            break;
                        case "hasExceptions":
                            jsonParts.Add("\"hasExceptions\":" + ((kv.Value is bool exb && exb) ? "true" : "false"));
                            break;
                        case "logCount":
                            jsonParts.Add("\"logCount\":" + (kv.Value is int iv ? iv.ToString() : "0"));
                            break;
                    }
                }
            }

            var json = "{" + string.Join(",", jsonParts.ToArray()) + "}";
            var resultPath = GetResultPath(index);
            Directory.CreateDirectory(Path.GetDirectoryName(resultPath));
            File.WriteAllText(resultPath, json);
            Debug.Log("[M14Bridge] Result " + index + " written: " + json);
        }
        catch (Exception ex)
        {
            Debug.LogError("[M14Bridge] WriteResult failed: " + ex.Message);
        }
    }

    private static string EscapeJson(string s)
    {
        return s.Replace("\\", "\\\\").Replace("\"", "\\\"")
                .Replace("\n", "\\n").Replace("\r", "\\r")
                .Replace("\t", "\\t");
    }

    // ─── Paths ───

    private string GetCommandPath(int index) =>
        Path.Combine(_sessionDir, CmdSubDir, "cmd_" + index + ".json");
    private string GetResultPath(int index) =>
        Path.Combine(_sessionDir, ResultSubDir, "result_" + index + ".json");

    // ─── Param Helpers ───

    private static int GetParamMilliseconds(M14Command cmd, int defaultValue)
    {
        if (cmd.@params != null)
            return cmd.@params.milliseconds > 0 ? cmd.@params.milliseconds : defaultValue;
        return defaultValue;
    }
}

// ─── Serialization Models ───

[System.Serializable]
public class M14Command
{
    public string action;
    public M14Params @params;
}

[System.Serializable]
public class M14Params
{
    public int milliseconds;
    public string gameObjectName;
}

[System.Serializable]
public class M14CommandResult
{
    public string action;
    public int status;
    // JsonUtility serializes fields, NOT properties.
    // success and error must be string fields so they appear in JSON
    // as "success":"true"/"false" and "error":"message".
    // The Core-side CommandResult uses JsonSerializer which reads
    // these by name ("success" as bool, "error" as string).
    public string success;   // "true" or "false"
    public string error;     // error message or null

    public string buttonName;
    public bool found;
    public bool clicked;
    public string sceneName;
    public bool hasErrors;
    public bool hasExceptions;
    public int logCount;
}

[System.Serializable]
public class M14Evidence
{
    public string executionMode;
    public string activeScene;
    public bool isEditor;
    public bool isPlaying;
    public bool success;
    public bool hasErrors;
    public string errorMessage;
}

// ─── JSON Utility ───

public static class M14JsonUtil
{
    public static M14Command DeserializeCommand(string json)
    {
        try
        {
            var cmd = JsonUtility.FromJson<M14Command>(json);
            if (cmd != null && !string.IsNullOrEmpty(cmd.action))
                return cmd;
            return null;
        }
        catch
        {
            return null;
        }
    }
}