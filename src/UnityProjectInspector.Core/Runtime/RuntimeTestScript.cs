namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// A runtime test script defines a sequence of actions to execute
/// against a running Unity Player and a set of assertions to evaluate
/// against the collected evidence.
///
/// Actions drive the Player through a scenario (click buttons, observe state).
/// Assertions verify the outcome (scene name matches, no exceptions).
///
/// Launch is NOT an action — RuntimeRunner manages process lifecycle.
/// The action pipeline runs AFTER the Player is launched and ready.
/// </summary>
public record RuntimeTestScript
{
    /// <summary>
    /// Human-readable name for this test scenario.
    /// Example: "Click MainMenuButton → Load TargetScene"
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Ordered sequence of actions to execute.
    /// Each action produces zero or more RuntimeEvidence items.
    /// </summary>
    public List<RuntimeAction> Actions { get; init; } = new();

    /// <summary>
    /// Assertions to evaluate against collected evidence.
    /// Evaluated AFTER all actions complete.
    /// </summary>
    public List<RuntimeAssertion> Assertions { get; init; } = new();
}