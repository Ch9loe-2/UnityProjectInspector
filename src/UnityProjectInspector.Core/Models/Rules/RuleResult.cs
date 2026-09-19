namespace UnityProjectInspector.Core.Models.Rules;

/// <summary>
/// The result of evaluating a single rule against an InspectionContext.
///
/// Status indicates pass/fail/not-evaluated.
/// Severity indicates how important the result is (Info/Warning/Error).
/// These are independent — a failure can be Warning or Error,
/// a pass can be Info.
/// </summary>
public class RuleResult
{
    /// <summary>
    /// Unique identifier for the rule that produced this result.
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Human-readable rule name.
    /// </summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// Whether the check condition was satisfied.
    /// </summary>
    public RuleStatus Status { get; init; }

    /// <summary>
    /// How important this result is (independent of Status).
    /// </summary>
    public RuleSeverity Severity { get; init; }

    /// <summary>
    /// Human-readable message describing what was checked and the outcome.
    /// </summary>
    public required string Message { get; init; }

    public override string ToString()
        => $"[{Status}] {RuleId}: {Message}";
}