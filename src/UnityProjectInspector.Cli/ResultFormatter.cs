using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Trace;

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
                var evidence = RuleEvidenceBuilder.BuildEvidence(
                    rr.Requirement!, rr.StaticResults);

                reqResults.Add(new CliRequirementResult
                {
                    Id = rr.Requirement?.Id ?? "unknown",
                    Name = rr.Requirement?.Name ?? "Unknown",
                    Status = CliStatusFormatter.StatusToString(rr.Status),
                    Message = rr.Message,
                    HasRuntime = rr.CompositeResult?.RuntimeStatus != null,
                    RuleEvidence = evidence,
                });
            }
        }

        // Map Core stages to CLI stages
        List<CliStageInfo>? cliStages = null;
        if (coreResult.Stages != null && coreResult.Stages.Count > 0)
        {
            cliStages = coreResult.Stages
                .Select(s => new CliStageInfo
                {
                    Name = s.Name,
                    Status = s.Status,
                    DurationMs = s.DurationMs,
                    Message = s.Message,
                })
                .ToList();
        }

        return new CliInspectionResult
        {
            Status = CliStatusFormatter.StatusToString(coreResult.FinalStatus),
            Message = coreResult.Message,
            Requirements = reqResults,
            Stages = cliStages,
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

                // Per-rule evidence (M33)
                if (req.RuleEvidence != null && req.RuleEvidence.Count > 0)
                {
                    foreach (var ev in req.RuleEvidence)
                    {
                        var evSym = ev.Status == "PASSED" ? "✓" : "✗";
                        writer.WriteLine($"    {evSym} {ev.RuleType}");
                        if (ev.Target != null)
                            writer.WriteLine($"      目标: {ev.Target}");
                        if (ev.Expected != null)
                            writer.WriteLine($"      期望: {ev.Expected}");
                        if (ev.Actual != null)
                            writer.WriteLine($"      结果: {ev.Actual}");
                        if (ev.Source != null)
                            writer.WriteLine($"      来源: {ev.Source}");
                        if (ev.Reason != null)
                            writer.WriteLine($"      原因: {ev.Reason}");
                    }
                }
            }
            writer.WriteLine();
        }

        // Stage trace (M34)
        if (result.Stages != null && result.Stages.Count > 0)
        {
            writer.WriteLine("Workflow Trace:");
            writer.WriteLine("──────────────");
            foreach (var stage in result.Stages)
            {
                var sym = stage.Status == "passed" ? "✓" : stage.Status == "failed" ? "✗" : "·";
                writer.WriteLine($"  {sym} {stage.Name,-24} {stage.Status,-8} {stage.DurationMs,6}ms");
                if (stage.Message != null)
                    writer.WriteLine($"      {stage.Message}");
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
}