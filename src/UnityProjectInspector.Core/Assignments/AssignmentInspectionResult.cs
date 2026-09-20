using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Assignments;

/// <summary>
/// The final aggregated result of evaluating an entire assignment
/// against a Unity project.
///
/// Aggregation rules:
///   Any Requirement Failed            → Final Failed
///   No Failed, any NotEvaluated       → Final NotEvaluated
///   All Passed                        → Final Passed
///
/// These rules are stricter than lenient aggregation:
/// NotEvaluated requirements ALWAYS prevent a Passed final status.
/// </summary>
public class AssignmentInspectionResult
{
    /// <summary>
    /// The assignment definition that was evaluated.
    /// </summary>
    public required AssignmentDefinition Assignment { get; init; }

    /// <summary>
    /// Individual requirement results in the same order as the assignment.
    /// </summary>
    public List<RequirementInspectionResult> RequirementResults { get; init; } = new();

    /// <summary>
    /// Final aggregated status across all requirements.
    /// </summary>
    public RuleStatus FinalStatus { get; init; }

    /// <summary>
    /// Human-readable message summarizing the assignment outcome.
    /// </summary>
    public required string Message { get; init; }
}