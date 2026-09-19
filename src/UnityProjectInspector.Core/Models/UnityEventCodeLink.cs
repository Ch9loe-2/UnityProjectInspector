namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents the complete static evidence chain from a UnityEvent binding
/// through C# source analysis:
///
///   Source GameObject
///     → UnityEvent
///       → Target Component
///         → C# Script
///           → Class
///             → Method
///               → Method Calls (invocations inside that method)
///
/// Example:
///   StartButton → UnityEvent → GameManager.Start2D → SceneManager.LoadScene("Maze2D")
/// </summary>
public class UnityEventCodeLink
{
    // ─── Source side ───

    /// <summary>
    /// The fileId of the GameObject that owns the UnityEvent source component.
    /// </summary>
    public long SourceGameObjectFileId { get; init; }

    /// <summary>
    /// The fileId of the component that declares this UnityEvent.
    /// </summary>
    public long SourceComponentFileId { get; init; }

    /// <summary>
    /// The resolved name of the source GameObject (e.g. "StartButton").
    /// Null if the SceneInfo couldn't resolve the name.
    /// </summary>
    public string? SourceGameObjectName { get; init; }

    // ─── Target side ───

    /// <summary>
    /// The raw fileId from m_Target (points to a Component, not a GameObject).
    /// </summary>
    public long TargetFileId { get; init; }

    /// <summary>
    /// The GameObject fileId that owns the target component.
    /// 0 if unresolved.
    /// </summary>
    public long TargetGameObjectFileId { get; set; }

    /// <summary>
    /// The resolved name of the target GameObject.
    /// Null if unresolved.
    /// </summary>
    public string? TargetGameObjectName { get; init; }

    /// <summary>
    /// The method name from the UnityEvent binding (e.g. "Start2D").
    /// </summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// The script path resolved via ScriptResolver.
    /// Null if the script could not be found.
    /// </summary>
    public string? ScriptPath { get; set; }

    /// <summary>
    /// The C# class name resolved via CSharpAnalyzer.
    /// Null if the class could not be found.
    /// </summary>
    public string? ClassName { get; set; }

    // ─── Resolution status ───

    /// <summary>
    /// Overall status of the evidence chain.
    /// Resolved:     full chain complete (Event → Class → Method → Calls extracted)
    /// PartiallyResolved: method found, but some arguments are dynamic
    /// Unresolved:   some link in the chain is broken
    /// </summary>
    public LinkStatus Status { get; set; } = LinkStatus.Unresolved;

    /// <summary>
    /// Human-readable description of what went wrong, if Status is not Resolved.
    /// </summary>
    public string? StatusMessage { get; set; }

    // ─── Method invocations within the target method ───

    /// <summary>
    /// Method invocations found inside the target method via CSharpAnalyzer.
    /// </summary>
    public List<CSharpMethodReferenceInfo> Calls { get; init; } = new();
}