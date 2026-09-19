namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Orchestrates the end-to-end Runtime Inspection Pipeline:
///   1. Build the Unity standalone Player
///   2. Launch the Player process
///   3. Wait for and collect Runtime Evidence JSON
///   4. Classify the result
///   5. Return a structured RuntimeSession
///
/// This is the formal C# entry point replacing the M12 experiment orchestrator.
/// </summary>
public interface IRuntimeRunner
{
    /// <summary>
    /// Runs the full runtime inspection pipeline against a Unity project.
    /// </summary>
    /// <param name="options">Configuration for the run (Unity path, project path, timeouts, etc.).</param>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    /// <returns>A RuntimeSession with the result status, evidence, and diagnostics.</returns>
    Task<RuntimeSession> RunAsync(
        RuntimeRunOptions options,
        CancellationToken cancellationToken = default);
}