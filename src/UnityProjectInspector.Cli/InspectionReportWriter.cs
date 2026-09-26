using System.Text.Json;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Writes an <see cref="InspectionReport"/> to a <see cref="TextWriter"/> in JSON,
/// Markdown, or HTML. The format is chosen by the caller based on the --report file
/// extension (.json → JSON, .md/.markdown → Markdown, .html → HTML).
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
        writer.WriteLine($"- **Schema version:** {report.SchemaVersion}");
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

    public static void WriteHtml(InspectionReport report, TextWriter writer)
    {
        var passedCount = report.Requirements.Count(r => r.Status == "PASSED");
        var failedCount = report.Requirements.Count(r => r.Status == "FAILED");
        var notEvalCount = report.Requirements.Count(r => r.Status == "NOT_EVALUATED");
        var totalCount = report.Requirements.Count;

        writer.WriteLine("<!DOCTYPE html>");
        writer.WriteLine("<html lang=\"en\">");
        writer.WriteLine("<head>");
        writer.WriteLine("<meta charset=\"utf-8\"/>");
        writer.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"/>");
        writer.WriteLine($"<meta name=\"report-schema-version\" content=\"{HtmlEscape(report.SchemaVersion)}\"/>");
        writer.WriteLine($"<title>Inspection Report: {HtmlEscape(report.AssignmentName)}</title>");
        writer.WriteLine("<style>");
        writer.WriteLine(GetCss());
        writer.WriteLine("</style>");
        writer.WriteLine("</head>");
        writer.WriteLine("<body>");
        writer.WriteLine("<div class=\"container\">");

        // ─── Header ──────────────────────────────────────────────
        writer.WriteLine($"<h1>Inspection Report: {HtmlEscape(report.AssignmentName)}</h1>");
        writer.WriteLine($"<div class=\"status-badge {StatusClass(report.FinalStatus)}\">{HtmlEscape(report.FinalStatus)}</div>");
        writer.WriteLine("<div class=\"meta-grid\">");
        writer.WriteLine($"  <div><strong>Assignment ID:</strong> {HtmlEscape(report.AssignmentId)}</div>");
        writer.WriteLine($"  <div><strong>Generated at (UTC):</strong> {HtmlEscape(report.GeneratedAt)}</div>");
        if (!string.IsNullOrWhiteSpace(report.AssignmentDescription))
        {
            writer.WriteLine($"  <div><strong>Description:</strong> {HtmlEscape(report.AssignmentDescription)}</div>");
        }
        writer.WriteLine("</div>");
        writer.WriteLine($"<p class=\"summary-message\">{HtmlEscape(report.Message)}</p>");

        // ─── Summary bar ─────────────────────────────────────────
        if (totalCount > 0)
        {
            writer.WriteLine("<div class=\"summary-bar\">");
            writer.WriteLine($"  <span class=\"summary-pass\">✔ {passedCount} passed</span>");
            if (failedCount > 0)
                writer.WriteLine($"  <span class=\"summary-fail\">✘ {failedCount} failed</span>");
            if (notEvalCount > 0)
                writer.WriteLine($"  <span class=\"summary-skip\">— {notEvalCount} not evaluated</span>");
            writer.WriteLine($"  <span class=\"summary-total\">{totalCount} total requirement{(totalCount != 1 ? "s" : "")}</span>");
            writer.WriteLine("</div>");
        }

        // ─── Requirements ────────────────────────────────────────
        if (totalCount == 0)
        {
            writer.WriteLine("<div class=\"empty-state\">No requirements were evaluated.</div>");
        }
        else
        {
            foreach (var req in report.Requirements)
            {
                var reqId = HtmlEscape(req.Id);
                var detailId = $"req-detail-{reqId}";

                writer.WriteLine("<div class=\"requirement-card\">");
                writer.WriteLine($"  <div class=\"req-header\">");
                writer.WriteLine($"    <span class=\"req-title\">{HtmlEscape(req.Name)} <code>{HtmlEscape(reqId)}</code></span>");
                writer.WriteLine($"    <span class=\"status-badge small {StatusClass(req.Status)}\">{HtmlEscape(req.Status)}</span>");
                writer.WriteLine("  </div>");

                var hasDetail = req.StaticRules.Count > 0 || req.Composite != null;
                if (!hasDetail)
                {
                    writer.WriteLine("  <div class=\"req-body\">");
                    writer.WriteLine($"    <p class=\"req-msg\">{HtmlEscape(req.Message)}</p>");
                    writer.WriteLine("  </div>");
                }
                else
                {
                    writer.WriteLine("  <div class=\"req-body\">");
                    writer.WriteLine($"    <p class=\"req-msg\">{HtmlEscape(req.Message)}</p>");

                    // Requirement metadata
                    writer.WriteLine("    <table class=\"req-meta\">");
                    writer.WriteLine($"      <tr><td>Evidence</td><td>{HtmlEscape(req.EvidenceRequirement)}</td></tr>");
                    writer.WriteLine($"      <tr><td>Runtime</td><td>{(req.HasRuntime ? "yes" : "no")}</td></tr>");
                    if (!string.IsNullOrWhiteSpace(req.Description))
                        writer.WriteLine($"      <tr><td>Description</td><td>{HtmlEscape(req.Description)}</td></tr>");
                    writer.WriteLine("    </table>");

                    // Toggle button
                    writer.WriteLine($"    <button class=\"toggle-btn\" onclick=\"toggleDetail('{detailId}')\">Show Details ▸</button>");
                    writer.WriteLine($"    <div id=\"{detailId}\" class=\"detail-content\" style=\"display:none\">");

                    // Static rule results
                    if (req.StaticRules.Count > 0)
                    {
                        writer.WriteLine("      <h3>Static Rule Results</h3>");
                        writer.WriteLine("      <table class=\"rule-table\">");
                        writer.WriteLine("        <thead><tr><th>Rule</th><th>Status</th><th>Severity</th><th>Message</th></tr></thead>");
                        writer.WriteLine("        <tbody>");
                        foreach (var r in req.StaticRules)
                        {
                            writer.WriteLine(
                                $"          <tr class=\"{StatusClass(r.Status.ToLowerInvariant())}\">" +
                                $"<td>{HtmlEscape(r.RuleName)} <code>{HtmlEscape(r.RuleId)}</code></td>" +
                                $"<td><span class=\"status-badge tiny {StatusClass(r.Status)}\">{HtmlEscape(r.Status)}</span></td>" +
                                $"<td>{HtmlEscape(r.Severity)}</td>" +
                                $"<td>{HtmlEscape(r.Message)}</td></tr>");
                        }
                        writer.WriteLine("        </tbody>");
                        writer.WriteLine("      </table>");
                    }

                    // Merged runtime result
                    if (req.Composite != null)
                    {
                        var c = req.Composite;
                        writer.WriteLine("      <h3>Merged Result (Static + Runtime)</h3>");
                        writer.WriteLine("      <table class=\"rule-table\">");
                        writer.WriteLine("        <thead><tr><th>Property</th><th>Value</th></tr></thead>");
                        writer.WriteLine("        <tbody>");
                        writer.WriteLine($"          <tr><td>Rule</td><td>{HtmlEscape(c.RuleName)} <code>{HtmlEscape(c.RuleId)}</code></td></tr>");
                        writer.WriteLine($"          <tr class=\"{StatusClass(c.StaticStatus ?? "NOT_EVALUATED")}\"><td>Static Status</td><td><span class=\"status-badge tiny {StatusClass(c.StaticStatus ?? "NOT_EVALUATED")}\">{HtmlEscape(c.StaticStatus ?? "—")}</span></td></tr>");
                        writer.WriteLine($"          <tr class=\"{StatusClass(c.RuntimeStatus ?? "NOT_EVALUATED")}\"><td>Runtime Status</td><td><span class=\"status-badge tiny {StatusClass(c.RuntimeStatus ?? "NOT_EVALUATED")}\">{HtmlEscape(c.RuntimeStatus ?? "—")}</span></td></tr>");
                        writer.WriteLine($"          <tr class=\"{StatusClass(c.FinalStatus)}\"><td>Final Status</td><td><span class=\"status-badge tiny {StatusClass(c.FinalStatus)}\">{HtmlEscape(c.FinalStatus)}</span></td></tr>");
                        writer.WriteLine($"          <tr><td>Merge Note</td><td>{HtmlEscape(c.Message)}</td></tr>");
                        if (!string.IsNullOrWhiteSpace(c.RuntimeResultDetail))
                            writer.WriteLine($"          <tr><td>Runtime Detail</td><td>{HtmlEscape(c.RuntimeResultDetail)}</td></tr>");
                        if (!string.IsNullOrWhiteSpace(c.RuntimeMessage))
                            writer.WriteLine($"          <tr><td>Runtime Message</td><td>{HtmlEscape(c.RuntimeMessage)}</td></tr>");
                        writer.WriteLine("        </tbody>");
                        writer.WriteLine("      </table>");
                    }

                    writer.WriteLine("    </div>"); // detail-content
                    writer.WriteLine("  </div>"); // req-body
                }
                writer.WriteLine("</div>"); // requirement-card
            }
        }

        // ─── Footer ──────────────────────────────────────────────
        writer.WriteLine($"<div class=\"footer\">Generated by UnityProjectInspector — report schema {HtmlEscape(report.SchemaVersion)} — at {HtmlEscape(report.GeneratedAt)}</div>");
        writer.WriteLine("</div>"); // container

        // ─── Script ──────────────────────────────────────────────
        writer.WriteLine("<script>");
        writer.WriteLine("function toggleDetail(id) {");
        writer.WriteLine("  var el = document.getElementById(id);");
        writer.WriteLine("  var btn = el.previousElementSibling;");
        writer.WriteLine("  if (el.style.display === 'none') {");
        writer.WriteLine("    el.style.display = 'block';");
        writer.WriteLine("    btn.textContent = 'Hide Details ▾';");
        writer.WriteLine("  } else {");
        writer.WriteLine("    el.style.display = 'none';");
        writer.WriteLine("    btn.textContent = 'Show Details ▸';");
        writer.WriteLine("  }");
        writer.WriteLine("}");
        writer.WriteLine("</script>");
        writer.WriteLine("</body>");
        writer.WriteLine("</html>");
    }

    private static string GetCss()
    {
        // Reset-in-a-box: clean, modern, readable, no external deps
        return """
* { margin: 0; padding: 0; box-sizing: border-box; }
body { font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial, sans-serif; background: #f5f7fa; color: #1f2937; line-height: 1.6; padding: 2rem 1rem; }
.container { max-width: 960px; margin: 0 auto; background: #fff; border-radius: 8px; box-shadow: 0 1px 3px rgba(0,0,0,.08); padding: 2rem; }

h1 { font-size: 1.5rem; font-weight: 600; margin-bottom: .75rem; display: inline-block; margin-right: 1rem; vertical-align: middle; }

/* Status badges */
.status-badge { display: inline-block; vertical-align: middle; padding: .25rem .75rem; border-radius: 9999px; font-weight: 600; font-size: .8125rem; letter-spacing: .02em; }
.status-badge.PASSED { color: #fff; background: #16a34a; }
.status-badge.FAILED { color: #fff; background: #dc2626; }
.status-badge.NOT_EVALUATED { color: #fff; background: #6b7280; }
.status-badge.small { font-size: .75rem; padding: .125rem .5rem; }
.status-badge.tiny { font-size: .6875rem; padding: .0625rem .375rem; }

/* Metadata grid */
.meta-grid { margin: .75rem 0; display: grid; grid-template-columns: 1fr 1fr; gap: .25rem 1.5rem; font-size: .875rem; color: #4b5563; }
.meta-grid strong { color: #1f2937; }

.summary-message { margin: .75rem 0 1rem; padding: .75rem 1rem; background: #f9fafb; border-left: 3px solid #3b82f6; border-radius: 4px; font-size: .9375rem; color: #374151; }

/* Summary bar */
.summary-bar { display: flex; gap: 1.25rem; margin-bottom: 1.25rem; padding: .625rem 1rem; background: #f9fafb; border-radius: 6px; font-size: .875rem; font-weight: 500; flex-wrap: wrap; }
.summary-pass { color: #16a34a; }
.summary-fail { color: #dc2626; }
.summary-skip { color: #6b7280; }
.summary-total { color: #4b5563; margin-left: auto; }

/* Empty state */
.empty-state { text-align: center; padding: 3rem 1rem; color: #9ca3af; font-style: italic; }

/* Requirement card */
.requirement-card { border: 1px solid #e5e7eb; border-radius: 6px; margin-bottom: 1rem; overflow: hidden; }
.req-header { display: flex; justify-content: space-between; align-items: center; padding: .75rem 1rem; background: #f9fafb; border-bottom: 1px solid #e5e7eb; }
.req-title { font-weight: 600; font-size: .9375rem; }
.req-title code { font-size: .8125rem; color: #6b7280; font-weight: 400; margin-left: .375rem; }
.req-body { padding: .75rem 1rem 1rem; }
.req-msg { font-size: .875rem; color: #374151; margin-bottom: .5rem; }

/* Requirement metadata table */
.req-meta { width: 100%; font-size: .8125rem; border-collapse: collapse; margin-bottom: .5rem; }
.req-meta td { padding: .125rem .5rem; border: none; color: #4b5563; }
.req-meta td:first-child { width: 7rem; font-weight: 500; color: #1f2937; vertical-align: top; }

/* Toggle button */
.toggle-btn { background: #f3f4f6; border: 1px solid #d1d5db; border-radius: 4px; padding: .25rem .75rem; font-size: .8125rem; cursor: pointer; color: #374151; }
.toggle-btn:hover { background: #e5e7eb; }

/* Detail content */
.detail-content { margin-top: .75rem; }
.detail-content h3 { font-size: .875rem; font-weight: 600; margin-bottom: .5rem; color: #1f2937; }

/* Rule table */
.rule-table { width: 100%; border-collapse: collapse; font-size: .8125rem; margin-bottom: .75rem; }
.rule-table th { text-align: left; padding: .375rem .5rem; background: #f3f4f6; border-bottom: 2px solid #e5e7eb; font-weight: 600; color: #374151; white-space: nowrap; }
.rule-table td { padding: .375rem .5rem; border-bottom: 1px solid #f3f4f6; color: #4b5563; vertical-align: middle; }
.rule-table tr:hover { background: #f9fafb; }
.rule-table tr.PASSED td:first-child { border-left: 3px solid #16a34a; }
.rule-table tr.FAILED td:first-child { border-left: 3px solid #dc2626; }
.rule-table tr.NOT_EVALUATED td:first-child { border-left: 3px solid #6b7280; }

/* Footer */
.footer { margin-top: 2rem; padding-top: 1rem; border-top: 1px solid #e5e7eb; font-size: .75rem; color: #9ca3af; text-align: center; }
""";
    }

    private static string StatusClass(string status)
    {
        var upper = status.ToUpperInvariant();
        return upper switch
        {
            "PASSED" => "PASSED",
            "FAILED" => "FAILED",
            _ => "NOT_EVALUATED",
        };
    }

    private static string HtmlEscape(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }

    private static string EscapePipe(string s) => (s ?? string.Empty).Replace("|", "\\|");
}
