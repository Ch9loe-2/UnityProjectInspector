namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Configuration for a single Runtime Runner invocation.
///
/// Based on M12/M14 experimental evidence:
/// - Player Build takes ~5s for a minimal project on macOS
/// - Player Runtime produces evidence in ~0.2s
/// - Session directory is used for file-based IPC between Core and Player
/// </summary>
public record class RuntimeRunOptions
{
    /// <summary>
    /// Full path to the Unity Editor executable.
    /// Example: /Applications/Unity/Hub/Editor/2022.3.62f3c1/Unity.app/Contents/MacOS/Unity
    /// </summary>
    public required string UnityExecutable { get; init; }

    /// <summary>
    /// Full path to the Unity project directory (must contain Assets/, ProjectSettings/, etc.).
    /// </summary>
    public required string ProjectPath { get; init; }

    /// <summary>
    /// Directory where the standalone Player will be built.
    /// If not set, defaults to {ProjectPath}/Build/
    /// </summary>
    public string BuildDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Unity -executeMethod entry point for building the Player.
    /// The target project must have a static method matching this.
    /// Default: "M12_PlayerBuild.Build" (compatible with M12_MinimalUnityProject).
    /// </summary>
    public string BuildMethod { get; init; } = "M12_PlayerBuild.Build";

    /// <summary>
    /// Product name for the Player build.
    /// If not set, defaults to "M14RuntimePlayer".
    /// </summary>
    public string ProductName { get; init; } = "M14RuntimePlayer";

    /// <summary>
    /// Session directory for file-based IPC between Core and Player.
    /// Core writes commands here; Player writes results, evidence, and markers.
    /// If not set, defaults to a unique temp directory.
    /// </summary>
    public string SessionDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Environment variable name used to pass the session directory to the Player.
    /// Must match the [RuntimeInitializeOnLoadMethod] guard in M14RuntimeBridge.
    /// Default: "M14_SESSION_DIR"
    /// </summary>
    public string SessionDirEnvVar { get; init; } = "M14_SESSION_DIR";

    /// <summary>
    /// Name of the ready marker file the Player writes when the bridge is initialized.
    /// </summary>
    public string ReadyMarkerFileName { get; init; } = "ready.marker";

    /// <summary>
    /// Name of the final evidence JSON file the Player produces.
    /// </summary>
    public string EvidenceFileName { get; init; } = "evidence.json";

    /// <summary>
    /// Name of the done marker file the Player writes when the session is complete.
    /// </summary>
    public string DoneMarkerFileName { get; init; } = "done.marker";

    /// <summary>
    /// Subdirectory name for commands (relative to SessionDirectory).
    /// </summary>
    public string CommandsSubDir { get; init; } = "commands";

    /// <summary>
    /// Subdirectory name for command results (relative to SessionDirectory).
    /// </summary>
    public string ResultsSubDir { get; init; } = "results";

    /// <summary>
    /// Maximum time in seconds to wait for the Player to become ready (write ready.marker).
    /// </summary>
    public int ReadyTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Maximum time in seconds to wait for a single command result from the Player.
    /// </summary>
    public int CommandTimeoutSeconds { get; init; } = 30;

    /// <summary>
    /// Maximum time in seconds to wait for the Player to produce final evidence.
    /// </summary>
    public int PlayerTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum time in seconds to wait for the Unity Player Build to complete.
    /// Based on M12: minimal project builds in ~5s. Default: 180s.
    /// </summary>
    public int BuildTimeoutSeconds { get; init; } = 180;

    /// <summary>
    /// Poll interval in milliseconds for file-based IPC polling.
    /// Default: 100ms.
    /// </summary>
    public int PollIntervalMs { get; init; } = 100;
}