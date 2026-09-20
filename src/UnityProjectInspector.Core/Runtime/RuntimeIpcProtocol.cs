using System.Text.Json.Serialization;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// JSON protocol for Core-to-Player command IPC.
/// Written by RuntimeRunner to command.json in the session directory.
/// Read by Player-side M14RuntimeBridge.
/// </summary>
public class RuntimeCommand
{
    /// <summary>
    /// Action type discriminator.
    /// Supported: "Wait", "ClickButton", "ObserveActiveScene", "ReadLogs"
    /// </summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>
    /// Action-specific parameters as a JSON object.
    /// Wait:       { "milliseconds": 200 }
    /// ClickButton: { "gameObjectName": "MyButton" }
    /// ObserveActiveScene: {}
    /// ReadLogs:   {}
    /// </summary>
    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; init; }
}

/// <summary>
/// JSON protocol for Player-to-Core command result IPC.
/// Written by Player-side M14RuntimeBridge to command-result.json in the session directory.
/// Read by RuntimeRunner after each command.
/// </summary>
public class CommandResult
{
    /// <summary>
    /// The action type that was executed (echoed back from the command).
    /// </summary>
    [JsonPropertyName("action")]
    public required string Action { get; init; }

    /// <summary>
    /// Whether the action executed without Player-side errors.
    /// true = action completed (even if logical failure, e.g. button not found)
    /// false = Player could not execute the action (script exception, etc.)
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Action-specific result data as a JSON object.
    /// Wait:              { }
    /// ClickButton:       { "gameObjectName": "...", "found": true, "clicked": true }
    /// ObserveActiveScene: { "sceneName": "TargetScene" }
    /// ReadLogs:          { "hasErrors": false, "hasExceptions": false, "logCount": 0 }
    /// </summary>
    [JsonPropertyName("result")]
    public Dictionary<string, object>? Result { get; init; }

    /// <summary>
    /// Error message if the action failed at the Player level.
    /// Null if the action completed normally.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}