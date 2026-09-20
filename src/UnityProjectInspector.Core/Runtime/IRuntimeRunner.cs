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