using System.Text.Json.Serialization;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Base record for all Runtime Actions.
/// Actions are operations executed by the Player-side Bridge at runtime,
/// driving the Player process through a controlled test script.
///
/// Launch is NOT an action — RuntimeRunner owns process lifecycle.
/// Actions are executed AFTER the Player is already running.
/// </summary>
public abstract record RuntimeAction
{
    /// <summary>
    /// Unique identifier for this action instance (e.g. "click_1", "wait_load").
    /// Used to correlate action results in evidence.
    /// </summary>
    public required string ActionId { get; init; }

    /// <summary>
    /// Optional human-readable description.
    /// </summary>
    public string? Description { get; init; }
}

/// <summary>
/// Waits for a specified duration before the next action.
/// Used between actions (e.g. after clicking, wait for scene load).
/// </summary>
public sealed record WaitAction : RuntimeAction
{
    /// <summary>Wait duration in milliseconds. Default: 200ms.</summary>
    public int Milliseconds { get; init; } = 200;
}

/// <summary>
/// Clicks a UI Button by GameObject name at runtime.
/// Player executes: GameObject.Find(name) → GetComponent&lt;Button&gt;() → onClick.Invoke()
/// </summary>
public sealed record ClickButtonAction : RuntimeAction
{
    /// <summary>Exact name of the Button GameObject.</summary>
    public required string GameObjectName { get; init; }
}

/// <summary>
/// Reads the currently active scene name.
/// Player executes: SceneManager.GetActiveScene().name
/// Produces evidence with the observed scene name.
/// </summary>
public sealed record ObserveActiveSceneAction : RuntimeAction
{
}

/// <summary>
/// Captures runtime logs (errors, exceptions) accumulated so far.
/// Player reads from a log sink that collected entries throughout the session.
/// Produces evidence with error/exception presence flag.
/// </summary>
public sealed record ReadLogsAction : RuntimeAction
{
}