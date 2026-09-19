namespace UnityProjectInspector.Core.Models.Rules;

/// <summary>
/// Describes whether a rule passed its check or not.
/// This is NOT the same as severity — a rule can fail with Error severity,
/// or pass with Info severity.
/// </summary>
public enum RuleStatus
{
    /// <summary>
    /// The check condition was satisfied.
    /// </summary>
    Passed,

    /// <summary>
    /// The check condition was not satisfied.
    /// </summary>
    Failed,

    /// <summary>
    /// The rule could not be evaluated because its preconditions are not met
    /// (e.g. the scene or object it depends on doesn't exist).
    /// </summary>
    NotEvaluated,
}