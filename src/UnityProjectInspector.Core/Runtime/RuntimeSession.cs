namespace UnityProjectInspector.Core.Runtime;

/// <summary>
/// Records a single runtime test session against a Unity project.
///
/// Minimal model — only serves the ActiveScene POC in this milestone.
/// </summary>
public class RuntimeSession
{
    /// <summary>Path to the Unity project being tested.</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Unity Editor version used to launch.</summary>
    public string? UnityVersion { get; init; }

    /// <summary>Process ID of the Unity Editor launched by this session (0 if not launched by us).</summary>
    public int ProcessId { get; init; }

    /// <summary>When the launch was initiated.</summary>
    public DateTime StartedAt { get; init; }

    /// <summary>When Play Mode was successfully entered.</summary>
    public DateTime? PlayModeEnteredAt { get; set; }

    /// <summary>The evidence collected during the session.</summary>
    public List<RuntimeEvidence> Evidence { get; init; } = new();

    /// <summary>Overall result of the session.</summary>
    public RuntimeResultStatus Result { get; set; } = RuntimeResultStatus.NotEvaluated;

    /// <summary>Human-readable summary of the outcome.</summary>
    public string? Message { get; set; }
}