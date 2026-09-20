namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Orchestrates the end-to-end Runtime Inspection Pipeline:
///   1. Build the Unity standalone Player
///   2. Launch the Player process
///   3. Execute Action Pipeline (Wait, ClickButton, ObserveActiveScene, ReadLogs)
///   4. Collect Runtime Evidence
///   5. Evaluate Assertions
///   6. Return a structured RuntimeSession
///
/// Launch is owned by RuntimeRunner — it is NOT a RuntimeAction.
/// Actions execute AFTER the Player process is running and ready.
/// Assertions evaluate AFTER all actions complete.
/// </summary>
public interface IRuntimeRunner
{
    /// <summary>
    /// Runs the full runtime inspection pipeline with a test script.
    /// </summary>
    /// <param name="options">Configuration for the run.</param>
    /// <param name="script">Runtime test script with actions and assertions.</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A RuntimeSession with evidence, result, and diagnostics.</returns>
    Task<RuntimeSession> RunAsync(
        RuntimeRunOptions options,
        RuntimeTestScript script,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the full runtime inspection pipeline (M13-compatible overload,
    /// probes only, no action pipeline).
    /// </summary>
    Task<RuntimeSession> RunAsync(
        RuntimeRunOptions options,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Extension interface for IRuntimeRunner implementations that support
/// session-level lifecycle management.
///
/// When a runner implements this, InspectionWorkflowRunner can share a single
/// Unity Player session across multiple RuntimeRequired requirements instead of
/// building + launching the Player separately for each requirement.
///
/// Lifecycle:
///   CreateSessionAsync  → Build + Launch + WaitReady
///   ExecuteScriptOnScopeAsync → Actions + Assertions (per requirement)
///   FinalizeScopeAsync  → Quit + CollectFinalEvidence + WaitExit
///   scope.Dispose()     → Kill process + Cleanup files
/// </summary>
public interface ISupportsSessionSharing
{
    /// <summary>
    /// Creates a new RuntimeSessionScope: builds the Player (if needed), launches it,
    /// waits for it to become ready, and reads the initial scene evidence.
    ///
    /// The returned scope holds the running Player process and IPC session directories.
    /// </summary>
    Task<RuntimeSessionScope> CreateSessionAsync(
        RuntimeRunOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a RuntimeTestScript's actions on an already-running session scope,
    /// evaluates assertions, and returns the per-requirement result.
    ///
    /// Does NOT send Quit or finalize the session.
    /// Does NOT kill the Player process.
    ///
    /// Evidence from actions is appended to scope.Session.Evidence (accumulated).
    /// The returned RuntimeSession contains per-requirement status and evidence.
    /// </summary>
    Task<RuntimeSession> ExecuteScriptOnScopeAsync(
        RuntimeSessionScope scope,
        RuntimeTestScript script,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes a RuntimeSessionScope: sends Quit command, collects final evidence,
    /// and waits for the Player process to exit.
    ///
    /// After this, scope.IsFinalized = true and scope.IsUsable = false.
    /// Caller MUST still call scope.Dispose() for cleanup.
    /// </summary>
    Task FinalizeScopeAsync(
        RuntimeSessionScope scope,
        CancellationToken cancellationToken = default);
}