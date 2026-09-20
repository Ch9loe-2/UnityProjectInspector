using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Merge;

/// <summary>
/// The merged result of Static inspection + Runtime inspection
/// for a single inspection rule.
///
/// A rule's requirement determines whether runtime evidence is needed.
/// The merger combines both results according to strict merge rules
/// (see ResultMerger for the full matrix).
///
/// Fields mirror RuleResult where possible for API consistency.
/// </summary>
public class CompositeInspectionResult
{
    /// <summary>
    /// Unique identifier for the rule that produced this result.
    /// Propagated from RuleDefinition.Id.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Human-readable rule name.
    /// Propagated from RuleDefinition.Name.
    /// </summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// The static inspection result.
    /// Null if static inspection was not performed.
    /// </summary>
    public RuleStatus? StaticStatus { get; init; }

    /// <summary>
    /// The runtime inspection result.
    /// Null if runtime inspection was not performed or not applicable.
    /// </summary>
    public RuleStatus? RuntimeStatus { get; init; }

    /// <summary>
    /// What kind of evidence this rule requires.
    /// </summary>
    public EvidenceRequirement Requirement { get; init; }

    /// <summary>
    /// The final merged status after applying merge rules.
    /// </summary>
    public RuleStatus FinalStatus { get; init; }

    /// <summary>
    /// Human-readable message describing the merged outcome.
    /// Includes details about the merge decision.
    /// </summary>
    public required string Message { get; init; }

    public override string ToString()
        => $"[{FinalStatus}] {RuleId}: {Message}";
}