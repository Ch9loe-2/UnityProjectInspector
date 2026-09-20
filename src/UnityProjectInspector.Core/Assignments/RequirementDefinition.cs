using System.Text.Json.Serialization;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Core.Assignments;

/// <summary>
/// Defines a single inspection requirement within an assignment.
///
/// A requirement can express:
///   - StaticOnly: static analysis (scene YAML parsing, C# AST) is sufficient.
///   - RuntimeRequired: runtime Player verification is also needed.
///
/// StaticRules use the existing RuleDefinition model — no new rule types.
/// RuntimeTest (if present) uses the existing RuntimeTestScript model.
///
/// Consistency rules (validated by InspectionWorkflowValidator):
///   - RuntimeRequired + RuntimeTest=null → Error (configuration invalid)
///   - StaticOnly + RuntimeTest!=null → Warning (RuntimeTest will be skipped)
///   - RuntimeTest=null is valid for StaticOnly requirements
/// </summary>
public class RequirementDefinition
{
    /// <summary>
    /// Unique identifier within the assignment (e.g. "maze-mainmenu-exists").
    /// Must be unique across all requirements in an assignment.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable requirement name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Optional description of what this requirement verifies.
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Evidence requirement: "StaticOnly" (default) or "RuntimeRequired".
    /// Null/absent defaults to StaticOnly for backward compatibility.
    /// </summary>
    [JsonPropertyName("evidenceRequirement")]
    public string? EvidenceRequirement { get; init; }

    /// <summary>
    /// Static rules to check against the Unity project.
    /// Each rule is an existing RuleDefinition — reusable with RuleFactory.
    /// May be empty if the requirement is purely runtime-driven.
    /// </summary>
    [JsonPropertyName("staticRules")]
    public List<RuleDefinition> StaticRules { get; init; } = new();

    /// <summary>
    /// Optional runtime test script defining Player-side actions and assertions.
    /// Null means no runtime verification is required for this requirement.
    ///
    /// Must be non-null when EvidenceRequirement is "RuntimeRequired",
    /// otherwise the configuration is invalid.
    /// When EvidenceRequirement is "StaticOnly", this is ignored
    /// (a warning is emitted during validation).
    /// </summary>
    [JsonPropertyName("runtimeTest")]
    public RuntimeTestScript? RuntimeTest { get; init; }
}