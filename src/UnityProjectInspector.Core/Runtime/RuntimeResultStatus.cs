namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Defines the possible outcomes of a runtime test session.
/// </summary>
public enum RuntimeResultStatus
{
    /// <summary>Unity launched, Play Mode entered, evidence obtained and matches.</summary>
    Passed,

    /// <summary>Unity launched, Play Mode entered, evidence obtained but does NOT match.</summary>
    Failed,

    /// <summary>
    /// Could not perform the evaluation — Unity not found, project failed to load,
    /// timeout, or evidence was unobtainable. Distinguished from Failed because
    /// the runtime evaluation itself was incomplete.
    /// </summary>
    NotEvaluated,
}