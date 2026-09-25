using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Maps a Core <see cref="RuleStatus"/> (the final status of an inspection) onto a
/// CLI exit code.
///
/// This mapping is a CLI-level contract, independent of Core's internal status enum.
/// Extracted into its own pure function so it can be unit-tested without invoking the
/// full inspection pipeline (which would require a Unity Editor for runtime cases).
///
///   0 = Passed        — RuleStatus.Passed
///   1 = Failed        — RuleStatus.Failed
///   3 = RuntimeError  — RuleStatus.NotEvaluated (runtime could not be completed)
///   4 = InternalError — any other / unexpected status
/// </summary>
public static class CliResultMapping
{
    public static CliExitCode FromFinalStatus(RuleStatus status) => status switch
    {
        RuleStatus.Passed => CliExitCode.Passed,
        RuleStatus.Failed => CliExitCode.Failed,
        RuleStatus.NotEvaluated => CliExitCode.RuntimeError,
        _ => CliExitCode.InternalError,
    };
}
