namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a single Unity scene file (.unity) and its parsed contents.
/// </summary>
public class SceneInfo
{
    /// <summary>
    /// The display name of the scene (derived from the file name without extension).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The full file path to the .unity file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// The GameObjects parsed from this scene.
    /// Both root objects and children are included.
    /// </summary>
    public List<GameObjectInfo> GameObjects { get; init; } = new();
}