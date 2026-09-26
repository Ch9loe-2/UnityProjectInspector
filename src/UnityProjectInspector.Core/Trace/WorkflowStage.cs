using System.Text.Json.Serialization;

namespace UnityProjectInspector.Core.Trace;

/// <summary>
/// A single recorded stage in an inspection workflow run trace.
///
/// Immutable after creation — all fields are init-only.
/// Nullable fields (Message) are omitted from JSON when null
/// so consumers can distinguish "not applicable" from "empty string".
/// </summary>
public class WorkflowStage
{
    /// <summary>Stage name in kebab-case, e.g. "build-player", "scan-project".</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Stage outcome: "passed", "failed", or "skipped".</summary>
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    /// <summary>Elapsed time in milliseconds. Zero for skipped or instantaneous stages.</summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    /// <summary>Optional human-readable message (e.g. error detail or success note).</summary>
    [JsonPropertyName("message")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Message { get; init; }

    public override string ToString() => $"[{Status}] {Name} ({DurationMs}ms)";
}