using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M32 inspection report tests — cover DTO mapping (rule-level detail + runtime
/// evidence) and both output formats (JSON / Markdown).
/// </summary>
public class InspectionReportBuilderTests
{
    private static AssignmentInspectionResult MakeCoreResult()
    {
        var req = new RequirementInspectionResult
        {
            Requirement = new RequirementDefinition
            {
                Id = "R001",
                Name = "Main menu exists",
                Description = "The project must contain a main menu scene.",
                EvidenceRequirement = "RuntimeRequired",
            },
            Status = RuleStatus.Failed,
            Message = "Runtime verification failed.",
            StaticResults = new List<RuleResult>
            {
                new() { RuleId = "SR001", RuleName = "SceneExists", Status = RuleStatus.Passed, Severity = RuleSeverity.Info, Message = "Scene MainMenu found." },
                new() { RuleId = "SR002", RuleName = "GameObjectExists", Status = RuleStatus.Failed, Severity = RuleSeverity.Error, Message = "StartButton not found." },
            },
            CompositeResult = new CompositeInspectionResult
            {
                RuleId = "R001",
                RuleName = "Main menu exists",
                StaticStatus = RuleStatus.Passed,
                RuntimeStatus = RuleStatus.Failed,
                FinalStatus = RuleStatus.Failed,
                Requirement = EvidenceRequirement.RuntimeRequired,
                Message = "RuntimeRequired: static analysis passed but runtime verification failed.",
                RuntimeResultDetail = RuntimeResultStatus.ProcessExited,
                RuntimeMessage = "Player exited before producing evidence.",
            },
        };

        return new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition
            {
                Id = "maze-2d",
                Name = "2D Maze Assignment",
                Description = "Verify the maze project.",
            },
            RequirementResults = new List<RequirementInspectionResult> { req },
            FinalStatus = RuleStatus.Failed,
            Message = "Assignment failed: some requirements did not pass.",
        };
    }

    [Fact]
    public void Build_PreservesAssignmentAndRequirementMetadata()
    {
        var report = InspectionReportBuilder.Build(MakeCoreResult());

        Assert.Equal("maze-2d", report.AssignmentId);
        Assert.Equal("2D Maze Assignment", report.AssignmentName);
        Assert.Equal("Verify the maze project.", report.AssignmentDescription);
        Assert.Equal("FAILED", report.FinalStatus);
        Assert.Single(report.Requirements);

        var r = report.Requirements[0];
        Assert.Equal("R001", r.Id);
        Assert.Equal("Main menu exists", r.Name);
        Assert.Equal("The project must contain a main menu scene.", r.Description);
        Assert.Equal("RuntimeRequired", r.EvidenceRequirement);
        Assert.True(r.HasRuntime);
        Assert.Equal("FAILED", r.Status);
    }

    [Fact]
    public void Build_PreservesRuleLevelDetail()
    {
        var report = InspectionReportBuilder.Build(MakeCoreResult());
        var r = report.Requirements[0];

        Assert.Equal(2, r.StaticRules.Count);
        Assert.Equal("SR001", r.StaticRules[0].RuleId);
        Assert.Equal("PASSED", r.StaticRules[0].Status);
        Assert.Equal("Info", r.StaticRules[0].Severity);
        Assert.Equal("SR002", r.StaticRules[1].RuleId);
        Assert.Equal("FAILED", r.StaticRules[1].Status);
        Assert.Equal("Error", r.StaticRules[1].Severity);
    }

    [Fact]
    public void Build_PreservesRuntimeEvidence()
    {
        var report = InspectionReportBuilder.Build(MakeCoreResult());
        var composite = report.Requirements[0].Composite;

        Assert.NotNull(composite);
        Assert.Equal("PASSED", composite!.StaticStatus);
        Assert.Equal("FAILED", composite.RuntimeStatus);
        Assert.Equal("FAILED", composite.FinalStatus);
        Assert.Equal("RuntimeRequired", composite.Requirement);
        Assert.Equal("ProcessExited", composite.RuntimeResultDetail);
        Assert.Equal("Player exited before producing evidence.", composite.RuntimeMessage);
    }

    [Fact]
    public void Build_DoesNotThrow_WhenCompositeAbsent()
    {
        var core = new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition { Id = "a", Name = "A" },
            FinalStatus = RuleStatus.Passed,
            Message = "ok",
            RequirementResults = new List<RequirementInspectionResult>
            {
                new()
                {
                    Requirement = new RequirementDefinition { Id = "R1", Name = "R1", EvidenceRequirement = "StaticOnly" },
                    Status = RuleStatus.Passed,
                    Message = "ok",
                    StaticResults = new List<RuleResult>
                    {
                        new() { RuleId = "S1", RuleName = "S1", Status = RuleStatus.Passed, Severity = RuleSeverity.Info, Message = "ok" },
                    },
                },
            },
        };

        var report = InspectionReportBuilder.Build(core);
        Assert.Single(report.Requirements);
        Assert.Null(report.Requirements[0].Composite);
        Assert.False(report.Requirements[0].HasRuntime);
    }

    [Fact]
    public void WriteJson_ProducesValidParsableJson()
    {
        var report = InspectionReportBuilder.Build(MakeCoreResult());
        using var writer = new StringWriter();
        InspectionReportWriter.WriteJson(report, writer);
        var json = writer.ToString();

        var parsed = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(json);
        Assert.Equal("maze-2d", parsed.GetProperty("assignmentId").GetString());
        var reqs = parsed.GetProperty("requirements");
        Assert.Equal(1, reqs.GetArrayLength());
        Assert.Equal(2, reqs[0].GetProperty("staticRules").GetArrayLength());
        Assert.Equal("ProcessExited", reqs[0].GetProperty("composite").GetProperty("runtimeResultDetail").GetString());
    }

    [Fact]
    public void WriteMarkdown_ContainsRuleAndRuntimeDetail()
    {
        var report = InspectionReportBuilder.Build(MakeCoreResult());
        using var writer = new StringWriter();
        InspectionReportWriter.WriteMarkdown(report, writer);
        var md = writer.ToString();

        Assert.Contains("# Inspection Report: 2D Maze Assignment", md, StringComparison.Ordinal);
        Assert.Contains("R001", md, StringComparison.Ordinal);
        Assert.Contains("Static rule results", md, StringComparison.Ordinal);
        Assert.Contains("StartButton not found.", md, StringComparison.Ordinal);
        Assert.Contains("Merged result (static + runtime)", md, StringComparison.Ordinal);
        Assert.Contains("ProcessExited", md, StringComparison.Ordinal);
        Assert.Contains("Player exited before producing evidence.", md, StringComparison.Ordinal);
    }
}

public class CliArgumentParserReportTests
{
    [Fact]
    public void ReportFlag_ParsesCorrectly()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([
            "inspect",
            "--project", "/p",
            "--assignment", "/a",
            "--report", "./reports/inspection.json",
        ]);

        Assert.Null(err);
        Assert.NotNull(options);
        Assert.Equal("./reports/inspection.json", options!.ReportPath);
    }

    [Fact]
    public void ReportFlag_EqualSignSyntax_ParsesCorrectly()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([
            "inspect",
            "--project=/p",
            "--assignment=/a",
            "--report=out/report.md",
        ]);

        Assert.Null(err);
        Assert.NotNull(options);
        Assert.Equal("out/report.md", options!.ReportPath);
    }

    [Fact]
    public void ReportFlag_Absent_LeavesNull()
    {
        var (cmd, options, help, err) = CliArgumentParser.Parse([
            "inspect",
            "--project", "/p",
            "--assignment", "/a",
        ]);

        Assert.Null(err);
        Assert.Null(options!.ReportPath);
    }
}
