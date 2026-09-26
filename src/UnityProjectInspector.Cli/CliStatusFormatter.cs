using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Single CLI-layer mapping from Core <see cref="RuleStatus"/> to its stable string
/// representation, shared by the console output (<see cref="ResultFormatter"/>) and the
/// inspection report (<see cref="InspectionReportBuilder"/>).
///
/// The output text (PASSED / FAILED / NOT_EVALUATED / UNKNOWN) is a fixed contract —
/// do NOT change these values, because existing console output and report files depend
/// on them verbatim. This type exists only to remove the previously duplicated
/// <c>RuleStatus → string</c> logic that lived in two places.
/// </summary>
public static class CliStatusFormatter
{
    public static string StatusToString(RuleStatus status) => status switch
    {
        RuleStatus.Passed => "PASSED",
        RuleStatus.Failed => "FAILED",
        RuleStatus.NotEvaluated => "NOT_EVALUATED",
        _ => "UNKNOWN",
    };
}
