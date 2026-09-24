using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

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

    /// <summary>
    /// The raw RuntimeResultStatus from the RuntimeSession, if available.
    /// Preserves the original runtime diagnostic (ProcessExited, Timeout, BuildFailed, etc.)
    /// that is otherwise lost when ConvertRuntimeStatus maps it to RuleStatus.NotEvaluated.
    ///
    /// null if no runtime session was performed.
    /// </summary>
    public RuntimeResultStatus? RuntimeResultDetail { get; init; }

    /// <summary>
    /// The original message from the RuntimeSession, if available.
    /// Contains human-readable diagnostics from the runtime execution
    /// (e.g. exit code, timeout description, exception message).
    ///
    /// null if no runtime session was performed or no message was captured.
    /// </summary>
    public string? RuntimeMessage { get; init; }

    public override string ToString()
        => $"[{FinalStatus}] {RuleId}: {Message}";
}