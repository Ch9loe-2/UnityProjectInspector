using System.Diagnostics;

namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Wraps a running Unity Player process and its IPC session directories.
///
/// Ownership: created by RuntimeRunner.CreateSessionAsync,
/// owned by InspectionWorkflowRunner across multiple requirement evaluations.
///
/// Dispose() MUST be called to clean up the Player process and session files.
/// FinalizeSessionAsync should be called first to collect final evidence,
/// then Dispose() for cleanup.
/// </summary>
public class RuntimeSessionScope : IDisposable
{
    /// <summary>The RuntimeSession produced during Player initialization.</summary>
    public RuntimeSession Session { get; }

    /// <summary>The running Player process handle.</summary>
    internal Process PlayerProcess { get; }

    /// <summary>PID of the Player process.</summary>
    internal int PlayerPid { get; }

    /// <summary>Session directory for file-based IPC.</summary>
    public string SessionDir { get; }

    /// <summary>Directory for command files (written by Core).</summary>
    internal string CommandsDir { get; }

    /// <summary>Directory for result files (written by Player).</summary>
    internal string ResultsDir { get; }

    /// <summary>Path to the final evidence JSON file.</summary>
    internal string EvidenceFile { get; }

    /// <summary>Path to the done marker file.</summary>
    internal string DoneMarker { get; }

    /// <summary>
    /// Shared command index across multiple requirement executions.
    /// Incremented as each action is dispatched.
    /// </summary>
    internal int CommandIndex { get; set; } = 1;

    /// <summary>
    /// Whether the session is still usable (Player still running).
    /// </summary>
    public bool IsUsable => !PlayerProcess.HasExited;

    /// <summary>
    /// Whether the session has been finalized (Quit sent).
    /// </summary>
    public bool IsFinalized { get; internal set; }

    /// <summary>
    /// Evidence collected from the initial ObserveActiveScene (pre-requirement baseline).
    /// Stored separately so requirements don't misinterpret it as their own action evidence.
    /// </summary>
    internal RuntimeEvidence? InitialSceneEvidence { get; set; }

    /// <summary>
    /// The final PlayerEvidence collected during FinalizeSessionAsync.
    /// Null if FinalizeSessionAsync was not called.
    /// </summary>
    internal PlayerEvidence? FinalPlayerEvidence { get; set; }

    internal RuntimeSessionScope(
        RuntimeSession session,
        Process playerProcess,
        int playerPid,
        string sessionDir,
        string commandsDir,
        string resultsDir,
        string evidenceFile,
        string doneMarker,
        int commandTimeoutSeconds = 30,
        int pollIntervalMs = 100)
    {
        Session = session;
        PlayerProcess = playerProcess;
        PlayerPid = playerPid;
        SessionDir = sessionDir;
        CommandsDir = commandsDir;
        ResultsDir = resultsDir;
        EvidenceFile = evidenceFile;
        DoneMarker = doneMarker;
        CommandTimeoutSeconds = commandTimeoutSeconds;
        PollIntervalMs = pollIntervalMs;
    }

    /// <summary>
    /// Timeout for each IPC command in seconds.
    /// </summary>
    internal int CommandTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Poll interval in milliseconds for file-based IPC polling.
    /// </summary>
    internal int PollIntervalMs { get; set; } = 100;

    /// <summary>
    /// Creates a failed scope (build/launch failure) — no running process.
    /// </summary>
    internal static RuntimeSessionScope CreateFailed(RuntimeSession session)
    {
        return new RuntimeSessionScope(
            session,
            null!,
            0,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty);
    }

    /// <summary>
    /// Whether this scope was created from a build/launch failure.
    /// </summary>
    internal bool IsFailed => PlayerProcess == null;

    public virtual void Dispose()
    {
        if (!IsFailed && !PlayerProcess.HasExited)
        {
            // Best-effort kill
            try
            {
                using var killer = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "kill",
                        Arguments = PlayerPid.ToString(),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }
                };
                killer.Start();
                killer.WaitForExit(3000);
            }
            catch
            {
                // Best-effort
            }
        }

        // Clean up session directory files (best-effort)
        try
        {
            if (!string.IsNullOrEmpty(SessionDir) && Directory.Exists(SessionDir))
            {
                Directory.Delete(SessionDir, recursive: true);
            }
        }
        catch
        {
            // Best-effort
        }
    }
}