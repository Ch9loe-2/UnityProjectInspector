using System.Text.Json.Serialization;

namespace UnityProjectInspector.Cli;

/// <summary>
/// CLI-level inspection result DTO.
///
/// Independent of Core's AssignmentInspectionResult / CompositeInspectionResult.
/// Keeps the JSON output clean, versionable, and free from Core domain model internals
/// (e.g. the full AssignmentDefinition will not be serialized into results).
/// </summary>
public class CliInspectionResult
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("requirements")]
    public List<CliRequirementResult> Requirements { get; init; } = new();
}

public class CliRequirementResult
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("hasRuntime")]
    public bool HasRuntime { get; init; }
}