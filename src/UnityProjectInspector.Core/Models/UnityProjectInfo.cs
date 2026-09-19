namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a Unity project at a given directory path, along with its parsed scenes.
/// </summary>
public class UnityProjectInfo
{
    /// <summary>
    /// The root directory of the Unity project.
    /// </summary>
    public required string RootPath { get; init; }

    /// <summary>
    /// Whether the directory appears to be a valid Unity project
    /// (contains Assets/, ProjectSettings/, and Packages/).
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// The scenes discovered and parsed from this project.
    /// </summary>
    public List<SceneInfo> Scenes { get; init; } = new();
}