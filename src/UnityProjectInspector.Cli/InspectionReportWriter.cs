using System.Text.Json;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Writes an <see cref="InspectionReport"/> to a <see cref="TextWriter"/> in JSON or
/// Markdown. The format is chosen by the caller based on the --report file extension
/// (.json → JSON, .md/.markdown → Markdown).
/// </summary>
public static class InspectionReportWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void WriteJson(InspectionReport report, TextWriter writer)
    {
        var json = JsonSerializer.Serialize(report, JsonOptions);
        writer.WriteLine(json);
    }

    public static void WriteMarkdown(InspectionReport report, TextWriter writer)
    {
        writer.WriteLine($"# Inspection Report: {report.AssignmentName}");
        writer.WriteLine();
        writer.WriteLine($"- **Assignment ID:** {report.AssignmentId}");
        writer.WriteLine($"- **Generated at (UTC):** {report.GeneratedAt}");
        writer.WriteLine($"- **Final status:** {report.FinalStatus}");
        if (!string.IsNullOrWhiteSpace(report.AssignmentDescription))
        {
            writer.WriteLine($"- **Description:** {report.AssignmentDescription}");
        }

        writer.WriteLine();
        writer.WriteLine(report.Message);
        writer.WriteLine();

        if (report.Requirements.Count == 0)
        {
            writer.WriteLine("_No requirements were evaluated._");
            writer.WriteLine();
            return;
        }

        foreach (var req in report.Requirements)
        {
            writer.WriteLine($"## Requirement: {req.Name} (`{req.Id}`)");
            writer.WriteLine();
            writer.WriteLine($"- **Status:** {req.Status}");
            writer.WriteLine($"- **Evidence requirement:** {req.EvidenceRequirement}");
            writer.WriteLine($"- **Runtime evidence:** {(req.HasRuntime ? "yes" : "no")}");
            if (!string.IsNullOrWhiteSpace(req.Description))
            {
                writer.WriteLine($"- **Description:** {req.Description}");
            }

            writer.WriteLine($"- **Message:** {req.Message}");
            writer.WriteLine();

            if (req.StaticRules.Count > 0)
            {
                writer.WriteLine("### Static rule results");
                writer.WriteLine();
                writer.WriteLine("| Rule | Status | Severity | Message |");
                writer.WriteLine("| --- | --- | --- | --- |");
                foreach (var r in req.StaticRules)
                {
                    writer.WriteLine(
                        $"| {r.RuleName} (`{r.RuleId}`) | {r.Status} | {r.Severity} | {EscapePipe(r.Message)} |");
                }

                writer.WriteLine();
            }

            if (req.Composite != null)
            {
                writer.WriteLine("### Merged result (static + runtime)");
                writer.WriteLine();
                writer.WriteLine($"- **Rule:** {req.Composite.RuleName} (`{req.Composite.RuleId}`)");
                writer.WriteLine($"- **Static status:** {req.Composite.StaticStatus ?? "—"}");
                writer.WriteLine($"- **Runtime status:** {req.Composite.RuntimeStatus ?? "—"}");
                writer.WriteLine($"- **Final status:** {req.Composite.FinalStatus}");
                writer.WriteLine($"- **Merge note:** {req.Composite.Message}");
                if (!string.IsNullOrWhiteSpace(req.Composite.RuntimeResultDetail))
                {
                    writer.WriteLine($"- **Runtime result detail:** {req.Composite.RuntimeResultDetail}");
                }

                if (!string.IsNullOrWhiteSpace(req.Composite.RuntimeMessage))
                {
                    writer.WriteLine($"- **Runtime message:** {req.Composite.RuntimeMessage}");
                }

                writer.WriteLine();
            }
        }
    }

    private static string EscapePipe(string s) => (s ?? string.Empty).Replace("|", "\\|");
}
