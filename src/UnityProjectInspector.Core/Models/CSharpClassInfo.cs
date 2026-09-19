namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a class declaration found in C# source code.
/// For example, "public class GameManager { ... }" produces:
///   Name = "GameManager"
/// </summary>
public class CSharpClassInfo
{
    /// <summary>
    /// The class name as declared in source, e.g. "GameManager".
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The namespace containing this class, if any.
    /// Example: "TestProject" for "namespace TestProject { class GameManager {} }"
    /// </summary>
    public string? Namespace { get; init; }

    /// <summary>
    /// Methods declared within this class.
    /// </summary>
    public List<CSharpMethodInfo> Methods { get; init; } = new();
}