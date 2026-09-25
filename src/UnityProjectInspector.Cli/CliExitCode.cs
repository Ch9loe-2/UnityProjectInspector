namespace UnityProjectInspector.Cli;

/// <summary>
/// CLI exit codes for the UnityProjectInspector tool.
///
/// These are a CLI-level contract and are independent of Core's RuleStatus/RuntimeResultStatus.
/// Mapping:
///   0 = Passed       — All requirements satisfied
///   1 = Failed       — One or more requirements failed
///   2 = InvalidInput — Bad arguments, missing files, invalid JSON
///   3 = RuntimeError — Unity runtime / build / launch failure
///   4 = InternalError — Unexpected exception within the inspection pipeline
/// </summary>
public enum CliExitCode
{
    Passed = 0,
    Failed = 1,
    InvalidInput = 2,
    RuntimeError = 3,
    InternalError = 4,
}