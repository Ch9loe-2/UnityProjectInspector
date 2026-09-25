namespace UnityProjectInspector.Cli;

/// <summary>
/// Minimal manual argument parser for the UnityProjectInspector CLI.
///
/// No external dependencies. Supports --key value and --key=value syntax.
/// Single command: inspect
/// Global options: --help
/// </summary>
public static class CliArgumentParser
{
    public const string HelpText = """
UnityProjectInspector — Unity project inspection tool

Usage:
  unityprojectinspector inspect [options]

Options:
  --project <path>      Unity project path (required)
  --assignment <path>   Assignment JSON file path (required)
  --unity <path>        Unity Editor executable path (optional, auto-detect)
  --output <path>       Output directory (optional, default: current directory)
  --format <format>     Output format: text | json (optional, default: text)
  --help                Show this help

Examples:
  unityprojectinspector inspect --project ./MyUnityProject --assignment assignment.json
  unityprojectinspector inspect --project ./MyUnityProject --assignment a.json --format json --output ./results
""";

    /// <summary>
    /// Parses command-line arguments.
    /// Returns (Command, CliOptions, HelpRequested, ErrorMessage).
    /// </summary>
    public static (string? command, CliOptions? options, bool helpRequested, string? error) Parse(string[] args)
    {
        // --help anywhere → help, no command needed
        if (HasHelpFlag(args))
        {
            return (null, null, true, null);
        }

        if (args.Length == 0)
        {
            return (null, null, false, "No arguments provided. Use --help for usage information.");
        }

        // First non-flag argument is the command
        string? command = null;
        var remaining = new List<string>();

        foreach (var arg in args)
        {
            if (!arg.StartsWith("-") && command == null)
            {
                command = arg;
            }
            else
            {
                remaining.Add(arg);
            }
        }

        if (command != null && command != "inspect")
        {
            return (command, null, false, $"Unknown command: '{command}'. Use --help for usage information.");
        }

        // Parse key-value arguments
        string? projectPath = null;
        string? assignmentPath = null;
        string? unityPath = null;
        string? outputDir = null;
        string? format = null;

        for (int i = 0; i < remaining.Count; i++)
        {
            var arg = remaining[i];
            string key;
            string? value;

            if (arg.StartsWith("--"))
            {
                var eqIdx = arg.IndexOf('=');
                if (eqIdx > 0)
                {
                    key = arg[..eqIdx];
                    value = arg[(eqIdx + 1)..];
                }
                else
                {
                    key = arg;
                    // Next argument is the value (if not another flag)
                    if (i + 1 < remaining.Count && !remaining[i + 1].StartsWith("-"))
                    {
                        value = remaining[i + 1];
                        i++; // skip next
                    }
                    else
                    {
                        value = null;
                    }
                }
            }
            else
            {
                // Skip bare non-flag tokens (already consumed as command)
                continue;
            }

            switch (key)
            {
                case "--project":
                    projectPath = value;
                    break;
                case "--assignment":
                    assignmentPath = value;
                    break;
                case "--unity":
                    unityPath = value;
                    break;
                case "--output":
                    outputDir = value;
                    break;
                case "--format":
                    format = value;
                    break;
                default:
                    return (command, null, false, $"Unknown option: {key}. Use --help for usage information.");
            }
        }

        // Validation
        if (string.IsNullOrWhiteSpace(projectPath))
            return (command, null, false, "Missing required option: --project <path>");
        if (string.IsNullOrWhiteSpace(assignmentPath))
            return (command, null, false, "Missing required option: --assignment <path>");

        // Validate format
        if (format != null && format != "text" && format != "json")
            return (command, null, false, $"Invalid format: '{format}'. Use 'text' or 'json'.");

        var options = new CliOptions
        {
            ProjectPath = projectPath!,
            AssignmentPath = assignmentPath!,
            UnityExecutable = unityPath,
            OutputDirectory = outputDir ?? ".",
            Format = format ?? "text",
        };

        return (command ?? "inspect", options, false, null);
    }

    private static bool HasHelpFlag(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg == "--help" || arg == "-h" || arg == "/?")
                return true;
        }
        return false;
    }
}