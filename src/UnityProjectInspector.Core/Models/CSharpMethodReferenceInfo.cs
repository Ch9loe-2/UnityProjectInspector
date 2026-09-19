namespace UnityProjectInspector.Core.Models;

/// <summary>
/// A single method invocation found inside a UnityEvent-bound C# method.
/// Provides source context (which class/method this call belongs to)
/// and the invocation details (target, arguments, whether args are static).
///
/// Example:
///   SourceClass = "GameManager"
///   SourceMethod = "Start2D"
///   FullTarget = "SceneManager.LoadScene"
///   Arguments = ["\"Maze2D\""]
///   AreAllArgumentsStatic = true
/// </summary>
public class CSharpMethodReferenceInfo
{
    /// <summary>
    /// The class that contains the source method.
    /// </summary>
    public required string SourceClass { get; init; }

    /// <summary>
    /// The method that contains this invocation.
    /// </summary>
    public required string SourceMethod { get; init; }

    /// <summary>
    /// The full call target as written in source, e.g. "SceneManager.LoadScene".
    /// </summary>
    public required string FullTarget { get; init; }

    /// <summary>
    /// The argument list as source-code strings.
    /// Each argument is the raw text representation (not evaluated).
    /// </summary>
    public List<string> Arguments { get; init; } = new();

    /// <summary>
    /// Whether all arguments are statically determinable literals
    /// (string literals, numeric literals, etc.) rather than variable references.
    /// </summary>
    public bool AreAllArgumentsStatic { get; init; }
}