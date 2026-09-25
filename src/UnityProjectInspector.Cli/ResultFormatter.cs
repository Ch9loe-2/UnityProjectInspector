using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Formats inspection results for CLI output.
///
/// Supports two modes:
///   text — human-readable console output (default)
///   json — machine-readable JSON output
///
/// Uses a CLI-level DTO (CliInspectionResult) to keep the JSON output
/// independent of Core's domain model.
/// </summary>
public static class ResultFormatter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Converts a Core AssignmentInspectionResult into a CLI DTO.
    /// </summary>
    public static CliInspectionResult ToCliResult(AssignmentInspectionResult coreResult)
    {
        var reqResults = new List<CliRequirementResult>();

        if (coreResult.RequirementResults != null)
        {
            foreach (var rr in coreResult.RequirementResults)
            {
                reqResults.Add(new CliRequirementResult
                {
                    Id = rr.Requirement?.Id ?? "unknown",
                    Name = rr.Requirement?.Name ?? "Unknown",
                    Status = StatusToString(rr.Status),
                    Message = rr.Message,
                    HasRuntime = rr.CompositeResult?.RuntimeStatus != null,
                });
            }
        }

        return new CliInspectionResult
        {
            Status = StatusToString(coreResult.FinalStatus),
            Message = coreResult.Message,
            Requirements = reqResults,
        };
    }

    /// <summary>
    /// Formats the result as human-readable text.
    /// Writes to the provided TextWriter (typically Console.Out).
    /// </summary>
    public static void WriteText(CliInspectionResult result, TextWriter writer)
    {
        writer.WriteLine("UnityProjectInspector");
        writer.WriteLine("────────────────────────────");
        writer.WriteLine();

        // Summary line
        var statusSymbol = result.Status == "PASSED" ? "✓" : "✗";
        writer.WriteLine($"Result: {statusSymbol} {result.Status}");
        writer.WriteLine();

        if (result.Requirements.Count > 0)
        {
            writer.WriteLine("Requirements:");
            foreach (var req in result.Requirements)
            {
                var sym = req.Status == "PASSED" ? "✓" : req.Status == "FAILED" ? "✗" : "?";
                writer.WriteLine($"  {req.Id,-20} {sym} {req.Status,-12} {req.Message}");
            }
            writer.WriteLine();
        }

        writer.WriteLine(result.Message);
    }

    /// <summary>
    /// Formats the result as JSON.
    /// Writes to the provided TextWriter (typically Console.Out or a file).
    /// </summary>
    public static void WriteJson(CliInspectionResult result, TextWriter writer)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        writer.WriteLine(json);
    }

    private static string StatusToString(RuleStatus status)
    {
        return status switch
        {
            RuleStatus.Passed => "PASSED",
            RuleStatus.Failed => "FAILED",
            RuleStatus.NotEvaluated => "NOT_EVALUATED",
            _ => "UNKNOWN",
        };
    }
}