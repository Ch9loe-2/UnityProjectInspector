namespace UnityProjectInspector.Cli;

/// <summary>
/// Parsed CLI options for the "inspect" command.
/// Created by CliArgumentParser after argument validation.
/// </summary>
public class CliOptions
{
    /// <summary>Path to the Unity project to inspect (required).</summary>
    public required string ProjectPath { get; init; }

    /// <summary>Path to the assignment JSON file (required).</summary>
    public required string AssignmentPath { get; init; }

    /// <summary>Path to the Unity Editor executable (optional, auto-detected).</summary>
    public string? UnityExecutable { get; init; }

    /// <summary>Output directory for results (optional, defaults to current directory).</summary>
    public string OutputDirectory { get; init; } = ".";

    /// <summary>Output format: "text", "json", or "chinese" (default: "text").</summary>
    public string Format { get; init; } = "text";

    /// <summary>
    /// Optional path to write a detailed inspection report. Format is inferred from
    /// the file extension: <c>.json</c> → JSON, <c>.md</c>/<c>.markdown</c> → Markdown.
    /// Independent of <see cref="Format"/>; the console output is unaffected.
    /// </summary>
    public string? ReportPath { get; init; }

    /// <summary>
    /// When true, unexpected internal errors print the full exception stack trace.
    /// By default (false) the CLI stays productized and only shows a clean message.
    /// </summary>
    public bool Debug { get; init; }
}