using System.Text.Json.Serialization;

namespace UnityProjectInspector.Cli;

/// <summary>
/// CLI-level inspection report DTO — a detailed, evidence-rich export built directly
/// from Core's <see cref="AssignmentInspectionResult"/>.
///
/// Unlike <see cref="CliInspectionResult"/> (which only carries requirement-level
/// status + message), this report preserves RULE-LEVEL detail: every static rule
/// result (id, name, status, severity, message) and, when present, the merged
/// runtime evidence (static/runtime/final status, result detail, and the raw
/// runtime diagnostic message). This is the artifact for audit / evidence export.
///
/// It is a flattened, serializable snapshot of the Core result. Core is NOT modified
/// to support this DTO — the mapping lives entirely in <see cref="InspectionReportBuilder"/>.
/// </summary>
public class InspectionReport
{
    [JsonPropertyName("assignmentId")]
    public required string AssignmentId { get; init; }

    [JsonPropertyName("assignmentName")]
    public required string AssignmentName { get; init; }

    [JsonPropertyName("assignmentDescription")]
    public string? AssignmentDescription { get; init; }

    /// <summary>ISO 8601 UTC timestamp of when the report was generated.</summary>
    [JsonPropertyName("generatedAt")]
    public required string GeneratedAt { get; init; }

    [JsonPropertyName("finalStatus")]
    public required string FinalStatus { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("requirements")]
    public List<InspectionReportRequirement> Requirements { get; init; } = new();
}

public class InspectionReportRequirement
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Evidence requirement string from the requirement definition ("StaticOnly" / "RuntimeRequired").</summary>
    [JsonPropertyName("evidenceRequirement")]
    public required string EvidenceRequirement { get; init; }

    [JsonPropertyName("hasRuntime")]
    public bool HasRuntime { get; init; }

    [JsonPropertyName("staticRules")]
    public List<InspectionReportRuleResult> StaticRules { get; init; } = new();

    [JsonPropertyName("composite")]
    public InspectionReportComposite? Composite { get; init; }
}

public class InspectionReportRuleResult
{
    [JsonPropertyName("ruleId")]
    public required string RuleId { get; init; }

    [JsonPropertyName("ruleName")]
    public required string RuleName { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("severity")]
    public required string Severity { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }
}

public class InspectionReportComposite
{
    [JsonPropertyName("ruleId")]
    public required string RuleId { get; init; }

    [JsonPropertyName("ruleName")]
    public required string RuleName { get; init; }

    [JsonPropertyName("staticStatus")]
    public string? StaticStatus { get; init; }

    [JsonPropertyName("runtimeStatus")]
    public string? RuntimeStatus { get; init; }

    [JsonPropertyName("finalStatus")]
    public required string FinalStatus { get; init; }

    [JsonPropertyName("requirement")]
    public required string Requirement { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>Raw RuntimeResultStatus (ProcessExited, Timeout, BuildFailed, ...) when a runtime session ran.</summary>
    [JsonPropertyName("runtimeResultDetail")]
    public string? RuntimeResultDetail { get; init; }

    [JsonPropertyName("runtimeMessage")]
    public string? RuntimeMessage { get; init; }
}
