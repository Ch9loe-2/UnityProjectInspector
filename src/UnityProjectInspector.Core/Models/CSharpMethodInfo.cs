namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a single method declaration found in C# source code.
/// For example, "public void Start2D()" produces:
///   Name = "Start2D"
///   Calls = [CSharpMethodCallInfo...]
/// </summary>
public class CSharpMethodInfo
{
    /// <summary>
    /// The method name as declared in source, e.g. "Start2D", "QuitGame".
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Method invocations found within this method's body.
    /// </summary>
    public List<CSharpMethodCallInfo> Calls { get; init; } = new();
}