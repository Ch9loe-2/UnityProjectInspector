namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a single method invocation expression found in C# source code.
/// For example, SceneManager.LoadScene("Maze2D") produces:
///   Target = "SceneManager.LoadScene"
///   Arguments = ["\"Maze2D\""]
/// </summary>
public class CSharpMethodCallInfo
{
    /// <summary>
    /// The full call target as written in source, e.g. "SceneManager.LoadScene",
    /// "Debug.Log", "Application.Quit".
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// The argument list as source-code strings.
    /// Each argument is the raw text representation (not evaluated or type-resolved).
    /// </summary>
    public List<string> Arguments { get; init; } = new();
}