using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Core.Merge;

/// <summary>
/// Merges Static inspection results with Runtime inspection results
/// into a single CompositeInspectionResult.
///
/// The merger implements strict merge rules. Runtime evidence NEVER
/// overrides a static failure — if static analysis fails, the composite
/// result is Failed regardless of what runtime shows.
///
/// Merge Matrix for StaticOnly:
///   Passed   + (any)      → Passed     (Runtime ignored)
///   Failed   + (any)      → Failed     (Runtime ignored)
///   NotEval  + (any)      → NotEval    (Runtime ignored)
///
/// Merge Matrix for RuntimeRequired:
///   Passed   + Passed      → Passed
///   Passed   + Failed      → Failed
///   Passed   + NotEval     → NotEval
///   Failed   + Passed      → Failed     (static prerequisite failed)
///   Failed   + Failed      → Failed
///   Failed   + NotEval     → Failed
///   NotEval  + Passed      → NotEval    (static not determined)
///   NotEval  + Failed      → NotEval    (static not determined)
///   NotEval  + NotEval     → NotEval
///
/// The merger is stateless — all inputs are passed explicitly.
/// </summary>
public static class ResultMerger
{
    private const string StaticPrerequisiteFailed =
        "Static prerequisite failed. Runtime evidence does not override static failure.";

    /// <summary>
    /// Merges a static RuleResult with a runtime session result.
    /// </summary>
    /// <param name="rule">The rule definition (provides Id, Name, and EvidenceRequirement).</param>
    /// <param name="staticResult">The static RuleResult from the RuleEngine.</param>
    /// <param name="runtimeSession">The runtime session (may be null if no runtime was run).</param>
    /// <returns>A merged CompositeInspectionResult.</returns>
    public static CompositeInspectionResult Merge(
        RuleDefinition rule,
        RuleResult staticResult,
        RuntimeSession? runtimeSession)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(staticResult);

        var requirement = ParseEvidenceRequirement(rule.EvidenceRequirement);

        // Extract runtime status from session
        RuleStatus? runtimeStatus = null;
        if (runtimeSession != null)
        {
            runtimeStatus = ConvertRuntimeStatus(runtimeSession.Result);
        }

        var (finalStatus, message) = MergeStatuses(
            staticResult.Status, runtimeStatus, requirement);

        return new CompositeInspectionResult
        {
            RuleId = rule.Id,
            RuleName = rule.Name,
            StaticStatus = staticResult.Status,
            RuntimeStatus = runtimeStatus,
            Requirement = requirement,
            FinalStatus = finalStatus,
            Message = message,
        };
    }

    /// <summary>
    /// Merges static and runtime statuses directly.
    /// Useful when you have pre-extracted status values.
    /// </summary>
    public static CompositeInspectionResult Merge(
        string ruleId,
        string ruleName,
        EvidenceRequirement requirement,
        RuleStatus staticStatus,
        RuleStatus? runtimeStatus,
        string? customMessage = null)
    {
        var (finalStatus, message) = MergeStatuses(
            staticStatus, runtimeStatus, requirement);

        return new CompositeInspectionResult
        {
            RuleId = ruleId,
            RuleName = ruleName,
            StaticStatus = staticStatus,
            RuntimeStatus = runtimeStatus,
            Requirement = requirement,
            FinalStatus = finalStatus,
            Message = customMessage ?? message,
        };
    }

    /// <summary>
    /// Merges static and runtime status values without requiring full model objects.
    /// Used by unit tests to verify the merge matrix directly.
    /// </summary>
    public static (RuleStatus FinalStatus, string Message) MergeStatuses(
        RuleStatus staticStatus,
        RuleStatus? runtimeStatus,
        EvidenceRequirement requirement)
    {
        switch (requirement)
        {
            // ════════════════════════════════════════════════════════
            // StaticOnly: runtime is irrelevant
            // ════════════════════════════════════════════════════════
            case EvidenceRequirement.StaticOnly:
                return staticStatus switch
                {
                    RuleStatus.Passed => (
                        RuleStatus.Passed,
                        "StaticOnly: static analysis passed. Runtime not required."),
                    RuleStatus.Failed => (
                        RuleStatus.Failed,
                        "StaticOnly: static analysis failed. Runtime not required."),
                    RuleStatus.NotEvaluated => (
                        RuleStatus.NotEvaluated,
                        "StaticOnly: static analysis could not be evaluated. Runtime not required."),
                    _ => (RuleStatus.NotEvaluated, $"Unknown static status: {staticStatus}"),
                };

            // ════════════════════════════════════════════════════════
            // RuntimeRequired: merge both
            // ════════════════════════════════════════════════════════
            case EvidenceRequirement.RuntimeRequired:
            {
                // Static is the gate: if static failed, always Failed
                if (staticStatus == RuleStatus.Failed)
                {
                    return (RuleStatus.Failed, StaticPrerequisiteFailed);
                }

                // Static not evaluated: cannot determine, even if runtime passed
                if (staticStatus == RuleStatus.NotEvaluated)
                {
                    return (RuleStatus.NotEvaluated,
                        "RuntimeRequired: static analysis could not be evaluated. " +
                        "Cannot determine final result even with runtime evidence.");
                }

                // Static Passed — result depends on runtime
                switch (runtimeStatus)
                {
                    case RuleStatus.Passed:
                        return (RuleStatus.Passed,
                            "RuntimeRequired: static analysis passed and runtime verification passed.");
                    case RuleStatus.Failed:
                        return (RuleStatus.Failed,
                            "RuntimeRequired: static analysis passed but runtime verification failed.");
                    case RuleStatus.NotEvaluated:
                        return (RuleStatus.NotEvaluated,
                            "RuntimeRequired: static analysis passed but runtime was not evaluated. " +
                            "Missing evidence — cannot determine final result.");
                    case null:
                        return (RuleStatus.NotEvaluated,
                            "RuntimeRequired: static analysis passed but no runtime session was performed. " +
                            "Evidence requirement not satisfied.");
                    default:
                        return (RuleStatus.NotEvaluated,
                            $"Unknown runtime status: {runtimeStatus}");
                }
            }

            default:
                return (RuleStatus.NotEvaluated, $"Unknown requirement: {requirement}");
        }
    }

    /// <summary>
    /// Parses a string evidence requirement from a RuleDefinition.
    /// Returns StaticOnly for null/empty/missing values (backward compatible).
    /// </summary>
    public static EvidenceRequirement ParseEvidenceRequirement(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return EvidenceRequirement.StaticOnly;

        return value.Trim() switch
        {
            "StaticOnly" => EvidenceRequirement.StaticOnly,
            "RuntimeRequired" => EvidenceRequirement.RuntimeRequired,
            _ => EvidenceRequirement.StaticOnly, // Unknown → safe default
        };
    }

    /// <summary>
    /// Converts RuntimeResultStatus to RuleStatus for merge purposes.
    /// Passed → Passed
    /// Failed → Failed
    /// NotEvaluated/Timeout/ProcessExited/BuildFailed → NotEvaluated
    /// </summary>
    private static RuleStatus ConvertRuntimeStatus(RuntimeResultStatus status)
    {
        return status switch
        {
            RuntimeResultStatus.Passed => RuleStatus.Passed,
            RuntimeResultStatus.Failed => RuleStatus.Failed,
            _ => RuleStatus.NotEvaluated,
        };
    }
}