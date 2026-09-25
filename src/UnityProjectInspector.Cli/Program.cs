using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Parsing;
using UnityProjectInspector.Core.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Cli;

/// <summary>
/// UnityProjectInspector CLI entry point.
///
/// Architecture:
///   CLI (args / validation / formatting)
///     ↓
///   Core (InspectionWorkflowRunner / RuleEngine / RuntimeRunner)
///
/// This file is the only "product entry point" — the thin CLI layer
/// that translates user input into Core API calls.
/// </summary>
public static class Program
{
    /// <summary>
    /// Repo root is determined by walking up from the CLI assembly location
    /// until we find a directory containing both src/ and UnityProjectInspector.slnx.
    /// This is used by HarnessDeployer to locate GenericRuntimeBridge.cs source files.
    /// </summary>
    internal static readonly string RepoRoot = ResolveRepoRoot();

    public static async Task<int> Main(string[] args)
    {
        // ─── Phase 1: Parse arguments ────────────────────────────
        var (command, options, helpRequested, parseError) = CliArgumentParser.Parse(args);

        // --help
        if (helpRequested)
        {
            Console.WriteLine(CliArgumentParser.HelpText);
            return (int)CliExitCode.Passed;
        }

        // Parse error
        if (parseError != null)
        {
            await Console.Error.WriteLineAsync($"Error: {parseError}");
            return (int)CliExitCode.InvalidInput;
        }

        // Unknown command (Parse already returns error for unknown commands)
        if (command == null || options == null)
        {
            await Console.Error.WriteLineAsync("Error: No command specified. Use --help for usage information.");
            return (int)CliExitCode.InvalidInput;
        }

        // ─── Phase 2: Validate inputs (before touching Unity) ────
        var validationError = await ValidateInputsAsync(options);
        if (validationError != null)
        {
            await Console.Error.WriteLineAsync($"Error: {validationError}");
            return (int)CliExitCode.InvalidInput;
        }

        // ─── Phase 3: Load Assignment JSON ────────────────────────
        AssignmentDefinition assignment;
        try
        {
            assignment = AssignmentLoader.Load(options.AssignmentPath);
        }
        catch (FileNotFoundException ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            return (int)CliExitCode.InvalidInput;
        }
        catch (JsonException ex)
        {
            await Console.Error.WriteLineAsync($"Error: Invalid assignment JSON: {ex.Message}");
            return (int)CliExitCode.InvalidInput;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: Failed to load assignment: {ex.Message}");
            return (int)CliExitCode.InvalidInput;
        }

        // ─── Phase 4: Validate assignment ────────────────────────
        var validator = new InspectionWorkflowValidator();
        var issues = validator.Validate(assignment);
        var errors = issues.Where(i => i.Severity == WorkflowIssueSeverity.Error).ToList();

        if (errors.Count > 0)
        {
            await Console.Error.WriteLineAsync("Error: Assignment configuration is invalid:");
            foreach (var err in errors)
            {
                await Console.Error.WriteLineAsync($"  - {err.Message}");
            }
            return (int)CliExitCode.InvalidInput;
        }

        // ─── Phase 5: Scan project & build InspectionContext ─────
        InspectionContext context;
        try
        {
            var scanner = new UnityProjectScanner();
            var projectInfo = scanner.Scan(options.ProjectPath);

            if (!projectInfo.IsValid)
            {
                await Console.Error.WriteLineAsync(
                    $"Error: '{options.ProjectPath}' does not appear to be a valid Unity project. " +
                    "Expected directories: Assets/, ProjectSettings/, Packages/.");
                return (int)CliExitCode.InvalidInput;
            }

            context = new InspectionContext
            {
                ProjectInfo = projectInfo,
            };
        }
        catch (DirectoryNotFoundException ex)
        {
            await Console.Error.WriteLineAsync($"Error: {ex.Message}");
            return (int)CliExitCode.InvalidInput;
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: Failed to scan Unity project: {ex.Message}");
            return (int)CliExitCode.InternalError;
        }

        // ─── Phase 6: Check if runtime is needed ─────────────────
        bool anyRuntimeRequired = assignment.Requirements.Any(
            r => ResultMerger.ParseEvidenceRequirement(r.EvidenceRequirement)
                 == EvidenceRequirement.RuntimeRequired);

        RuntimeRunOptions? runtimeOptions = null;

        if (anyRuntimeRequired)
        {
            // Try to find Unity executable if not explicitly provided
            var unityPath = options.UnityExecutable;
            if (string.IsNullOrWhiteSpace(unityPath))
            {
                unityPath = DetectUnityExecutable();
            }

            if (string.IsNullOrWhiteSpace(unityPath) || !File.Exists(unityPath))
            {
                await Console.Error.WriteLineAsync(
                    "Error: Assignment requires runtime inspection but no Unity executable found. " +
                    "Provide --unity <path> or install Unity Hub.");
                return (int)CliExitCode.InvalidInput;
            }

            runtimeOptions = new RuntimeRunOptions
            {
                UnityExecutable = unityPath,
                ProjectPath = Path.GetFullPath(options.ProjectPath),
            };
        }

        // ─── Phase 7: Construct Core pipeline & run ──────────────
        AssignmentInspectionResult coreResult;
        try
        {
            var ruleEngine = new RuleEngine();
            RuntimeRunner? runtimeRunner = null;
            HarnessDeployer? harnessDeployer = null;

            if (anyRuntimeRequired && runtimeOptions != null)
            {
                runtimeRunner = new RuntimeRunner();
                harnessDeployer = new HarnessDeployer(RepoRoot);
            }
            else
            {
                // For static-only, use a fallback runner that returns empty result
                runtimeRunner = new RuntimeRunner();
            }

            var workflowRunner = new InspectionWorkflowRunner(
                ruleEngine, runtimeRunner, harnessDeployer);

            coreResult = await workflowRunner.RunAsync(
                assignment, context, runtimeOptions);
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync($"Error: Inspection failed: {ex.Message}");
            return (int)CliExitCode.InternalError;
        }

        // ─── Phase 8: Format & output result ─────────────────────
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        // Output directory
        string? outputFilePath = null;
        if (options.OutputDirectory != ".")
        {
            try
            {
                Directory.CreateDirectory(options.OutputDirectory);
                var ext = options.Format == "json" ? "json" : "txt";
                outputFilePath = Path.Combine(
                    Path.GetFullPath(options.OutputDirectory),
                    $"inspection-result.{ext}");
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"Warning: Could not create output directory: {ex.Message}");
            }
        }

        if (options.Format == "json")
        {
            if (outputFilePath != null)
            {
                using var fileWriter = new StreamWriter(outputFilePath);
                ResultFormatter.WriteJson(cliResult, fileWriter);
                await Console.Out.WriteLineAsync($"Result written to: {outputFilePath}");
            }
            else
            {
                ResultFormatter.WriteJson(cliResult, Console.Out);
            }
        }
        else
        {
            if (outputFilePath != null)
            {
                using var fileWriter = new StreamWriter(outputFilePath);
                ResultFormatter.WriteText(cliResult, fileWriter);
            }
            ResultFormatter.WriteText(cliResult, Console.Out);
        }

        // ─── Phase 9: Exit code ──────────────────────────────────
        return coreResult.FinalStatus switch
        {
            RuleStatus.Passed => (int)CliExitCode.Passed,
            RuleStatus.Failed => (int)CliExitCode.Failed,
            RuleStatus.NotEvaluated => (int)CliExitCode.RuntimeError,
            _ => (int)CliExitCode.InternalError,
        };
    }

    /// <summary>
    /// Validates CLI inputs before entering the Core pipeline.
    /// Checks: project path exists and is a Unity project, assignment file exists.
    /// </summary>
    private static async Task<string?> ValidateInputsAsync(CliOptions options)
    {
        // Project path
        if (!Directory.Exists(options.ProjectPath))
        {
            return $"Project directory not found: {options.ProjectPath}";
        }

        // Quick Unity project check (without parsing scenes)
        var assetsDir = Path.Combine(options.ProjectPath, "Assets");
        var psDir = Path.Combine(options.ProjectPath, "ProjectSettings");
        var pkDir = Path.Combine(options.ProjectPath, "Packages");

        if (!Directory.Exists(assetsDir) || !Directory.Exists(psDir) || !Directory.Exists(pkDir))
        {
            return $"'{options.ProjectPath}' does not appear to be a valid Unity project. " +
                   "Expected directories: Assets/, ProjectSettings/, Packages/.";
        }

        // Assignment file
        if (!File.Exists(options.AssignmentPath))
        {
            return $"Assignment file not found: {options.AssignmentPath}";
        }

        // Unity executable (only if explicitly provided)
        if (!string.IsNullOrWhiteSpace(options.UnityExecutable))
        {
            if (!File.Exists(options.UnityExecutable))
            {
                return $"Unity executable not found: {options.UnityExecutable}";
            }
        }

        // Output directory (only if provided and should be validated)
        if (options.OutputDirectory != ".")
        {
            var parentDir = Path.GetDirectoryName(Path.GetFullPath(options.OutputDirectory));
            if (parentDir != null && !Directory.Exists(parentDir))
            {
                return $"Output directory parent does not exist: {parentDir}";
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to auto-detect the Unity Editor executable path.
    /// Default macOS path via Unity Hub.
    /// </summary>
    private static string? DetectUnityExecutable()
    {
        // macOS — default path via Unity Hub
        var hubPath = "/Applications/Unity/Hub/Editor";
        if (Directory.Exists(hubPath))
        {
            // Find the highest version
            var versions = Directory.GetDirectories(hubPath).OrderByDescending(d => d).ToList();
            foreach (var ver in versions)
            {
                var unityExe = Path.Combine(ver, "Unity.app", "Contents", "MacOS", "Unity");
                if (File.Exists(unityExe))
                    return unityExe;
            }
        }

        return null;
    }

    /// <summary>
    /// Walks up from the CLI assembly location to find the repository root
    /// (directory containing src/ and UnityProjectInspector.slnx).
    /// Falls back to the current directory if not found.
    /// </summary>
    internal static string ResolveRepoRoot()
    {
        // Start from the assembly's directory (e.g. .../bin/Debug/net10.0/)
        var asmLocation = typeof(Program).Assembly.Location;
        var dir = Path.GetDirectoryName(asmLocation);

        while (dir != null)
        {
            var slnx = Path.Combine(dir, "UnityProjectInspector.slnx");
            var srcDir = Path.Combine(dir, "src");
            if (File.Exists(slnx) && Directory.Exists(srcDir))
            {
                return dir;
            }
            dir = Path.GetDirectoryName(dir);
        }

        // Fallback: current directory
        return Directory.GetCurrentDirectory();
    }
}