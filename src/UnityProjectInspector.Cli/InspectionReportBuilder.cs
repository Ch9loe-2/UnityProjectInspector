using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Models.Rules;

namespace UnityProjectInspector.Cli;

/// <summary>
/// Builds an <see cref="InspectionReport"/> from a Core <see cref="AssignmentInspectionResult"/>.
///
/// Pure mapping — no Core inspection logic is replicated. This mirrors the role of
/// <see cref="ResultFormatter.ToCliResult"/> but preserves the rule-level detail that
/// the console DTO intentionally drops (every static <see cref="RuleResult"/> and the
/// merged runtime <see cref="CompositeInspectionResult"/> evidence).
/// </summary>
public static class InspectionReportBuilder
{
    public static InspectionReport Build(AssignmentInspectionResult coreResult)
    {
        ArgumentNullException.ThrowIfNull(coreResult);

        var assignment = coreResult.Assignment;

        var report = new InspectionReport
        {
            SchemaVersion = InspectionReport.CurrentSchemaVersion,
            AssignmentId = assignment?.Id ?? "unknown",
            AssignmentName = assignment?.Name ?? "Unknown",
            AssignmentDescription = assignment?.Description,
            GeneratedAt = DateTime.UtcNow.ToString("o"),
            FinalStatus = CliStatusFormatter.StatusToString(coreResult.FinalStatus),
            Message = coreResult.Message,
        };

        if (coreResult.RequirementResults != null)
        {
            foreach (var rr in coreResult.RequirementResults)
            {
                report.Requirements.Add(BuildRequirement(rr));
            }
        }

        return report;
    }

    private static InspectionReportRequirement BuildRequirement(RequirementInspectionResult rr)
    {
        var requirement = rr.Requirement;

        InspectionReportComposite? composite = null;
        if (rr.CompositeResult != null)
        {
            var c = rr.CompositeResult;
                composite = new InspectionReportComposite
                {
                    RuleId = c.RuleId,
                    RuleName = c.RuleName,
                    StaticStatus = c.StaticStatus.HasValue ? CliStatusFormatter.StatusToString(c.StaticStatus.Value) : null,
                    RuntimeStatus = c.RuntimeStatus.HasValue ? CliStatusFormatter.StatusToString(c.RuntimeStatus.Value) : null,
                    FinalStatus = CliStatusFormatter.StatusToString(c.FinalStatus),
                    Requirement = c.Requirement.ToString(),
                    Message = c.Message,
                    RuntimeResultDetail = c.RuntimeResultDetail?.ToString(),
                    RuntimeMessage = c.RuntimeMessage,
                };
        }

        var evidence = RuleEvidenceBuilder.BuildEvidence(
                requirement!, rr.StaticResults);

        var req = new InspectionReportRequirement
        {
            Id = requirement?.Id ?? "unknown",
            Name = requirement?.Name ?? "Unknown",
            Description = requirement?.Description,
            Status = CliStatusFormatter.StatusToString(rr.Status),
            Message = rr.Message,
            EvidenceRequirement = requirement?.EvidenceRequirement ?? "StaticOnly",
            HasRuntime = rr.CompositeResult?.RuntimeStatus != null,
            Composite = composite,
            RuleEvidence = evidence?.Select(e => new InspectionReportRuleEvidence
            {
                RuleId = e.RuleId,
                RuleType = e.RuleType,
                Status = e.Status,
                Target = e.Target,
                Expected = e.Expected,
                Actual = e.Actual,
                Reason = e.Reason,
                Source = e.Source,
            }).ToList(),
        };

        if (rr.StaticResults != null)
        {
            foreach (var sr in rr.StaticResults)
            {
                req.StaticRules.Add(new InspectionReportRuleResult
                {
                    RuleId = sr.RuleId,
                    RuleName = sr.RuleName,
                    Status = CliStatusFormatter.StatusToString(sr.Status),
                    Severity = sr.Severity.ToString(),
                    Message = sr.Message,
                });
            }
        }

        return req;
    }
}
