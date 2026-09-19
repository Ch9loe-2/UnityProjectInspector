namespace UnityProjectInspector.Core.Models;

/// <summary>
/// Represents a parsed C# source file (.cs), containing its classes, methods, and invocations.
/// Produced by CSharpAnalyzer using Roslyn SyntaxTree analysis.
/// </summary>
public class CSharpFileInfo
{
    /// <summary>
    /// The full file path to the .cs source file.
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// Classes declared in this file (top-level and nested within the file).
    /// </summary>
    public List<CSharpClassInfo> Classes { get; init; } = new();
}