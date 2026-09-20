using System.Text.Json.Serialization;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Base record for all Runtime Assertions.
/// Assertions are evaluated AFTER all actions in a test script complete.
/// They examine collected evidence and determine Passed/Failed/NotEvaluated.
///
/// JSON polymorphism: use type discriminator in JSON to choose subtype.
/// </summary>
[JsonDerivedType(typeof(AssertActiveScene), "AssertActiveScene")]
[JsonDerivedType(typeof(AssertNoExceptions), "AssertNoExceptions")]
public abstract record RuntimeAssertion
{
    /// <summary>
    /// Unique identifier for this assertion (e.g. "assert_scene_loaded").
    /// </summary>
    public required string AssertionId { get; init; }

    /// <summary>
    /// Optional human-readable description.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Asserts that the active scene name matches an expected value.
/// Finds ActiveSceneEvidence in the collected evidence
/// and compares the observed scene name against the expectation.
/// </summary>
public sealed record AssertActiveScene : RuntimeAssertion
{
    /// <summary>The expected active scene name (e.g. "TargetScene").</summary>
    public required string ExpectedSceneName { get; init; }
}

/// <summary>
/// Asserts that no runtime exceptions occurred during the observation window.
/// Checks exception evidence collected by ReadLogsAction.
/// </summary>
public sealed record AssertNoExceptions : RuntimeAssertion
{
    /// <summary>
    /// Optional list of exception message patterns to ignore.
    /// If non-empty, exceptions matching these patterns are not considered failures.
    /// </summary>
    public List<string> IgnoredPatterns { get; init; } = new();
}