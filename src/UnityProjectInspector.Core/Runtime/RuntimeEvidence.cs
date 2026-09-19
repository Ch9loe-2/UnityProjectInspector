namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Represents a single piece of runtime evidence observed during a test.
///
/// Minimal model — only serves the ActiveScene capability in this milestone.
/// Future milestones may extend this with additional EvidenceType values.
/// </summary>
public class RuntimeEvidence
{
    /// <summary>
    /// What kind of evidence this is (e.g. "ActiveScene").
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The expected value (e.g. "SampleScene").
    /// </summary>
    public required string Expected { get; init; }

    /// <summary>
    /// The value observed at runtime (e.g. "SampleScene", or null if unobtainable).
    /// </summary>
    public string? Observed { get; init; }

    /// <summary>
    /// Whether the evidence was successfully obtained from the runtime.
    /// </summary>
    public bool Obtained { get; init; }

    /// <summary>
    /// Human-readable description of the evidence.
    /// </summary>
    public string? Message { get; init; }
}