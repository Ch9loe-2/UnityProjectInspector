namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a C# script file discovered in a Unity project,
/// resolved from a MonoBehaviour's m_Script GUID reference.
/// </summary>
public class ScriptInfo
{
    /// <summary>
    /// The Unity GUID from the .meta file.
    /// </summary>
    public required string Guid { get; init; }

    /// <summary>
    /// The full path to the .meta file.
    /// Example: /Project/Assets/Scripts/GameManager.cs.meta
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The full path to the corresponding .cs source file.
    /// Example: /Project/Assets/Scripts/GameManager.cs
    /// Null when the .cs file does not exist on disk.
    /// </summary>
    public string? ScriptPath { get; init; }

    /// <summary>
    /// The name of the script file without extension.
    /// Example: "GameManager"
    /// </summary>
    public string ScriptName => Path.GetFileNameWithoutExtension(ScriptPath ?? FilePath);

    /// <summary>
    /// The class name parsed from the source file (e.g., "GameManager").
    /// Currently not resolved; always null in this milestone.
    /// Reserved for future C# static analysis.
    /// </summary>
    public string? ClassName { get; init; }

    /// <summary>
    /// Whether this script reference was successfully resolved
    /// (both .meta and corresponding .cs file exist).
    /// </summary>
    public bool IsResolved => ScriptPath != null;
}