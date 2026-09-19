using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Core.Rules;

/// <summary>
/// Checks whether a file exists relative to the Unity project root.
///
/// Security: Path traversal attacks using "../" or absolute paths are
/// explicitly rejected to prevent accessing files outside the project.
///
/// Does NOT access the network, modify files, or create files.
/// Only checks File.Exists() on the resolved path.
/// </summary>
public class FileExistsRule : IRule
{
    public string Id => "FILE_EXISTS";
    public string Name => "File Exists";

    /// <summary>
    /// The relative file path under the Unity project root.
    /// Should use forward slashes (e.g. "Assets/Scenes/SampleScene.unity").
    /// </summary>
    public string RelativePath { get; }

    public FileExistsRule(string relativePath)
    {
        RelativePath = relativePath;
    }

    public RuleResult Evaluate(InspectionContext context)
    {
        // Security check 1: Reject absolute paths
        if (Path.IsPathRooted(RelativePath))
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Absolute paths are not allowed: '{RelativePath}'",
            };
        }

        // Security check 2: Reject path traversal
        var normalized = RelativePath.Replace('\\', '/');
        if (normalized.Contains(".."))
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Path traversal ('..') is not allowed: '{RelativePath}'",
            };
        }

        // Resolve against project root
        var fullPath = Path.GetFullPath(Path.Combine(context.ProjectInfo.RootPath, RelativePath));

        // Security check 3: Verify the resolved path is still inside the project root
        var projectRoot = Path.GetFullPath(context.ProjectInfo.RootPath);
        if (!fullPath.StartsWith(projectRoot, StringComparison.Ordinal))
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"Resolved path '{fullPath}' is outside the project root '{projectRoot}'",
            };
        }

        if (!File.Exists(fullPath) && !Directory.Exists(fullPath))
        {
            return new RuleResult
            {
                RuleId = Id,
                RuleName = Name,
                Status = RuleStatus.Failed,
                Severity = RuleSeverity.Error,
                Message = $"File not found: '{RelativePath}' (resolved: '{fullPath}')",
            };
        }

        return new RuleResult
        {
            RuleId = Id,
            RuleName = Name,
            Status = RuleStatus.Passed,
            Severity = RuleSeverity.Info,
            Message = $"File exists: '{RelativePath}'",
        };
    }
}