namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a single UnityEvent persistent call binding extracted from scene/prefab YAML.
///
/// Corresponds to one entry in m_PersistentCalls.m_Calls:
///   m_Target    → TargetComponentFileId (the component whose method will be invoked)
///   m_MethodName → MethodName
///
/// Source refers to the component that owns this UnityEvent (e.g. a Button's m_OnClick).
/// Target refers to the component and GameObject whose method is called when the event fires.
/// </summary>
public class UnityEventBindingInfo
{
    /// <summary>
    /// The fileId of the GameObject that owns the source component.
    /// </summary>
    public long SourceGameObjectFileId { get; init; }

    /// <summary>
    /// The fileId of the component that declares this UnityEvent (e.g. the Button MonoBehaviour).
    /// </summary>
    public long SourceComponentFileId { get; init; }

    /// <summary>
    /// The raw fileId from m_Target, which points to a component (not a GameObject).
    /// 0 means no target was set.
    /// </summary>
    public long TargetFileId { get; init; }

    /// <summary>
    /// After cross-referencing with SceneInfo:
    /// The fileId of the GameObject that contains the target component.
    /// 0 if the target could not be resolved to a GameObject.
    /// </summary>
    public long TargetGameObjectFileId { get; set; }

    /// <summary>
    /// The method name to be invoked, as stored in m_MethodName.
    /// Example: "Start2D"
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// The raw m_TargetAssemblyTypeName string from the YAML.
    /// Example: "GameManager, Assembly-CSharp"
    /// </summary>
    public string? TargetAssemblyTypeName { get; init; }

    /// <summary>
    /// The m_Mode value from the YAML (persistence mode for argument forwarding).
    /// </summary>
    public int Mode { get; init; }

    /// <summary>
    /// The m_CallState value (2 = EditorAndRuntime, otherwise varies).
    /// </summary>
    public int CallState { get; init; }

    /// <summary>
    /// Argument values from m_Arguments section.
    /// Currently only captures string/object argument values.
    /// </summary>
    public List<string> ArgumentValues { get; init; } = new();

    /// <summary>
    /// Whether the target component could be positively identified in the scene.
    /// True when TargetFileId was found in SceneInfo's component map.
    /// </summary>
    public bool IsResolved { get; set; }
}