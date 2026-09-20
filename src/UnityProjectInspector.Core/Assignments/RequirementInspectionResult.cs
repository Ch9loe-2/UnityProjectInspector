using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Assignments;

/// <summary>
/// The merged inspection result for a single RequirementDefinition.
///
/// Contains the static RuleResult, the runtime result (if applicable),
/// and the composite final status produced by ResultMerger.
/// </summary>
public class RequirementInspectionResult
{
    /// <summary>
    /// The requirement definition that was evaluated.
    /// </summary>
    public required RequirementDefinition Requirement { get; init; }

    /// <summary>
    /// Individual results from static rule evaluation.
    /// One entry per static rule in the requirement.
    /// Empty list if no static rules were defined.
    /// </summary>
    public List<RuleResult> StaticResults { get; init; } = new();

    /// <summary>
    /// The composite result after merging static and runtime.
    /// Null if static rules were empty and no runtime was needed.
    /// </summary>
    public CompositeInspectionResult? CompositeResult { get; init; }

    /// <summary>
    /// Overall status for this requirement:
    /// Passed — all checks passed.
    /// Failed — any check failed.
    /// NotEvaluated — could not be fully evaluated.
    /// </summary>
    public RuleStatus Status { get; init; }

    /// <summary>
    /// Human-readable message summarizing this requirement's outcome.
    /// </summary>
    public required string Message { get; init; }
}