using System.Text.Json.Serialization;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Represents the JSON evidence produced by a Unity Player at runtime.
///
/// Matches the M12 RuntimeProbe output shape:
/// {"executionMode":"Runtime","activeScene":"MainScene","isEditor":false,"isPlaying":true,"success":true}
/// </summary>
public class PlayerEvidence
{
    /// <summary>Always "Runtime" for a real Player build (vs "PlayMode (Editor)").</summary>
    [JsonPropertyName("executionMode")]
    public string? ExecutionMode { get; set; }

    /// <summary>Name of the active scene when the evidence was captured.</summary>
    [JsonPropertyName("activeScene")]
    public string? ActiveScene { get; set; }

    /// <summary>
    /// false in a real standalone Player, true in Editor PlayMode.
    /// This is the key discriminator for Runtime evidence.
    /// </summary>
    [JsonPropertyName("isEditor")]
    public bool IsEditor { get; set; }

    /// <summary>true when the Player is in play mode.</summary>
    [JsonPropertyName("isPlaying")]
    public bool IsPlaying { get; set; }

    /// <summary>true when the evidence indicates a successful inspection.</summary>
    [JsonPropertyName("success")]
    public bool Success { get; set; }

    /// <summary>
    /// Returns true if this evidence is from a real Player Runtime session.
    /// </summary>
    public bool IsValidRuntimeEvidence =>
        !string.IsNullOrEmpty(ExecutionMode) &&
        ExecutionMode == "Runtime" &&
        !IsEditor &&
        IsPlaying;
}