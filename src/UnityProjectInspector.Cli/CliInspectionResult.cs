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

    /// <summary>Per-rule evidence details for this requirement (M33).</summary>
    [JsonPropertyName("ruleEvidence")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<CliRuleEvidence>? RuleEvidence { get; init; }
}

/// <summary>
/// Evidence detail for a single rule check (M33).
/// Structured fields extracted from the rule definition and result.
/// </summary>
public class CliRuleEvidence
{
    [JsonPropertyName("ruleId")]
    public required string RuleId { get; init; }

    [JsonPropertyName("ruleType")]
    public required string RuleType { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("target")]
    public string? Target { get; init; }

    /// <summary>What was expected (e.g. component type, script class, parent name).</summary>
    [JsonPropertyName("expected")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Expected { get; init; }

    /// <summary>What was actually observed (e.g. "exists", "not found").</summary>
    [JsonPropertyName("actual")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Actual { get; init; }

    /// <summary>Human-readable reason for the result.</summary>
    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    /// <summary>Source/location when reliably available.</summary>
    [JsonPropertyName("source")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; init; }
}