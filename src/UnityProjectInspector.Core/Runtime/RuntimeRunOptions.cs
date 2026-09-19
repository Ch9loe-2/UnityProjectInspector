namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Configuration for a single Runtime Runner invocation.
///
/// Based on M12 experimental evidence:
/// - Player Build takes ~5s for a minimal project on macOS
/// - Player Runtime produces evidence in ~0.2s
/// - Default timeout of 60s covers both build and runtime phases
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
    /// If not set, defaults to "M13RuntimePlayer".
    /// </summary>
    public string ProductName { get; init; } = "M13RuntimePlayer";

    /// <summary>
    /// Directory where the Player should write runtime evidence JSON.
    /// If not set, defaults to a temp directory.
    /// </summary>
    public string EvidenceDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Name of the evidence JSON file the Player produces.
    /// Default: "runtime_evidence.json"
    /// </summary>
    public string EvidenceFileName { get; init; } = "runtime_evidence.json";

    /// <summary>
    /// Name of the marker file the Player produces to signal completion.
    /// Default: "m13_done.marker"
    /// </summary>
    public string MarkerFileName { get; init; } = "m13_done.marker";

    /// <summary>
    /// Environment variable name used to pass the evidence directory to the Player.
    /// Must match the [RuntimeInitializeOnLoadMethod] guard.
    /// Default: "M13_RESULT_DIR"
    /// </summary>
    public string ResultDirEnvVar { get; init; } = "M13_RESULT_DIR";

    /// <summary>
    /// Maximum time in seconds to wait for the Player to produce evidence.
    /// Based on M12: Player produces evidence in ~0.2s. Default: 60s.
    /// </summary>
    public int PlayerTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Maximum time in seconds to wait for the Unity Player Build to complete.
    /// Based on M12: minimal project builds in ~5s. Default: 180s.
    /// </summary>
    public int BuildTimeoutSeconds { get; init; } = 180;
}