namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Describes how completely a UnityEvent binding was resolved through C# static analysis.
/// </summary>
public enum LinkStatus
{
    /// <summary>
    /// Full chain resolved: UnityEvent → target class → method → method calls extracted.
    /// </summary>
    Resolved,

    /// <summary>
    /// Method exists but some call arguments could not be statically determined
    /// (e.g. a variable name instead of a string literal).
    /// </summary>
    PartiallyResolved,

    /// <summary>
    /// Target component, script, class, or method could not be found or identified.
    /// </summary>
    Unresolved,
}