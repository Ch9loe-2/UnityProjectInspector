namespace UnityProjectInspector.Core.Models.Rules;

/// <summary>
/// Indicates how important a rule result is.
/// Severity is independent of Status — a Failed result can have Error or Warning,
/// and a Passed result can have Info.
/// </summary>
public enum RuleSeverity
{
    /// <summary>
    /// Informational — the rule passed and the result is just for reference.
    /// </summary>
    Info,

    /// <summary>
    /// Warning — the rule failed but this may not be critical.
    /// </summary>
    Warning,

    /// <summary>
    /// Error — the rule failed and this is a serious issue.
    /// </summary>
    Error,
}