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
    /// or evidence was unobtainable. Distinguished from Failed because
    /// the runtime evaluation itself was incomplete.
    /// </summary>
    NotEvaluated,

    /// <summary>
    /// Player was launched but did not produce evidence within the configured timeout
    /// and did not exit on its own. The runner terminated the process.
    /// </summary>
    Timeout,

    /// <summary>
    /// Player process exited before producing evidence. The process ended
    /// (possibly with a crash or error) without writing the expected output.
    /// </summary>
    ProcessExited,

    /// <summary>
    /// Unity Player Build failed. The Player executable was never produced,
    /// so runtime evaluation could not proceed.
    /// </summary>
    BuildFailed,
}