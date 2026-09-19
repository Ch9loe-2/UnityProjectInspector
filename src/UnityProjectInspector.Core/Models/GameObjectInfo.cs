namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a single GameObject parsed from a Unity scene file.
/// </summary>
public class GameObjectInfo
{
    /// <summary>
    /// The display name (m_Name) of the GameObject.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The unique file ID of this GameObject within the scene.
    /// </summary>
    public long FileId { get; init; }

    /// <summary>
    /// The file ID of the parent GameObject.
    /// 0 means this GameObject has no parent (it's a root object).
    /// </summary>
    public long ParentFileId { get; init; }

    /// <summary>
    /// The components attached to this GameObject.
    /// </summary>
    public List<ComponentInfo> Components { get; init; } = new();
}