namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a single Unity Component attached to a GameObject.
/// </summary>
public class ComponentInfo
{
    /// <summary>
    /// The human-readable type name (e.g., "Transform", "Camera", "MeshRenderer").
    /// For unrecognized components, this may include the numeric class ID.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// The unique file ID of this component within the scene.
    /// </summary>
    public long FileId { get; init; }

    /// <summary>
    /// Optional: future extension point for component-specific properties.
    /// </summary>
    public Dictionary<string, string> Properties { get; init; } = new();
}