using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Trace;

namespace UnityProjectInspector.Cli.Tests;

/// <summary>
/// M34 Inspection Run Trace tests.
///
/// Covers:
///   - WorkflowStage model (serialization, null fields)
///   - WorkflowStageCollector (basic record, async, sync, skipped)
///   - WorkflowStageCollector exception handling (failed stages)
///   - Stage trace propagation through ResultFormatter.ToCliResult
///   - Stage trace in JSON output
///   - Stage trace in text output
///   - Stage trace in Markdown report output
///   - Stage trace in HTML report output
///   - Stage trace in Chinese result output
///   - Backward compatibility (no stages = null, existing output unchanged)
///   - Static-only stage list (5 stages, runtime-only stages OMITTED)
///   - Runtime-path stage list (8 stages: 1-3 + deploy-harness + build-player +
///     evaluate-requirements + finalize-session + generate-report)
///
/// All tests build Core DTOs directly — no fixture project needed.
/// </summary>
public class M34StageTraceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Builds a Core AssignmentInspectionResult with a static-only stage trace.
    /// Default: 5 stages — validate-inputs, load-assignment, scan-project,
    /// evaluate-requirements, generate-report. Runtime-only stages are OMITTED.
    /// </summary>
    private static AssignmentInspectionResult MakeCoreResultWithStages(
        List<WorkflowStage>? stages = null)
    {
        var req = new RequirementInspectionResult
        {
            Requirement = new RequirementDefinition
            {
                Id = "R001",
                Name = "Main menu exists",
                EvidenceRequirement = "StaticOnly",
            },
            Status = RuleStatus.Passed,
            Message = "Static checks passed.",
            StaticResults = new List<RuleResult>
            {
                new() { RuleId = "SR001", RuleName = "SceneExists", Status = RuleStatus.Passed, Severity = RuleSeverity.Info, Message = "Scene found." },
            },
        };

        // Default: static-only trace — runtime-only stages are OMITTED
        stages ??= new List<WorkflowStage>
        {
            new() { Name = "validate-inputs", Status = "passed", DurationMs = 1 },
            new() { Name = "load-assignment", Status = "passed", DurationMs = 2 },
            new() { Name = "scan-project", Status = "passed", DurationMs = 3 },
            new() { Name = "evaluate-requirements", Status = "passed", DurationMs = 5 },
            new() { Name = "generate-report", Status = "passed", DurationMs = 4 },
        };

        return new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition
            {
                Id = "m34-test",
                Name = "M34 Stage Trace Test",
            },
            RequirementResults = new List<RequirementInspectionResult> { req },
            FinalStatus = RuleStatus.Passed,
            Message = "Assignment passed: all requirements satisfied.",
            Stages = stages,
        };
    }

    /// <summary>
    /// Builds a Core result with a full runtime-path stage trace.
    /// Includes all 8 stages that appear in a runtime inspection run.
    /// </summary>
    private static AssignmentInspectionResult MakeCoreResultWithRunttimeStages()
    {
        var req = new RequirementInspectionResult
        {
            Requirement = new RequirementDefinition
            {
                Id = "R001",
                Name = "Main menu exists",
                EvidenceRequirement = "RuntimeRequired",
            },
            Status = RuleStatus.Passed,
            Message = "All checks passed.",
            StaticResults = new List<RuleResult>
            {
                new() { RuleId = "SR001", RuleName = "SceneExists", Status = RuleStatus.Passed, Severity = RuleSeverity.Info, Message = "Scene found." },
            },
        };

        return new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition
            {
                Id = "m34-runtime",
                Name = "M34 Runtime Trace Test",
            },
            RequirementResults = new List<RequirementInspectionResult> { req },
            FinalStatus = RuleStatus.Passed,
            Message = "Assignment passed: all requirements satisfied.",
            Stages = new List<WorkflowStage>
            {
                new() { Name = "validate-inputs", Status = "passed", DurationMs = 1 },
                new() { Name = "load-assignment", Status = "passed", DurationMs = 2 },
                new() { Name = "scan-project", Status = "passed", DurationMs = 3 },
                new() { Name = "deploy-harness", Status = "passed", DurationMs = 10 },
                new() { Name = "build-player", Status = "passed", DurationMs = 5000 },
                new() { Name = "evaluate-requirements", Status = "passed", DurationMs = 100 },
                new() { Name = "finalize-session", Status = "passed", DurationMs = 5 },
                new() { Name = "generate-report", Status = "passed", DurationMs = 4 },
            },
        };
    }

    /// <summary>
    /// Builds a Core result with null stages (backward compatibility).
    /// </summary>
    private static AssignmentInspectionResult MakeCoreResultNoStages()
    {
        var result = MakeCoreResultWithStages(null);
        result.Stages = null;
        return result;
    }

    private static AssignmentInspectionResult MakeCoreResultWithFailedStage()
    {
        var req = new RequirementInspectionResult
        {
            Requirement = new RequirementDefinition
            {
                Id = "R001",
                Name = "Check",
                EvidenceRequirement = "StaticOnly",
            },
            Status = RuleStatus.NotEvaluated,
            Message = "Could not evaluate.",
            StaticResults = new List<RuleResult>(),
        };

        return new AssignmentInspectionResult
        {
            Assignment = new AssignmentDefinition
            {
                Id = "m34-fail",
                Name = "M34 Failure Test",
            },
            RequirementResults = new List<RequirementInspectionResult> { req },
            FinalStatus = RuleStatus.NotEvaluated,
            Message = "Assignment could not be fully evaluated.",
            Stages = new List<WorkflowStage>
            {
                new() { Name = "validate-inputs", Status = "passed", DurationMs = 1 },
                new() { Name = "load-assignment", Status = "failed", DurationMs = 2, Message = "File not found" },
            },
        };
    }

    /// <summary>
    /// Serializes a CliInspectionResult to JSON for contract tests.
    /// </summary>
    private static string ToJson(CliInspectionResult result)
    {
        return JsonSerializer.Serialize(result, JsonOptions);
    }

    // ═══════════════════════════════════════════════════════════════
    // WorkflowStage model
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void WorkflowStage_SerializesCorrectly()
    {
        var stage = new WorkflowStage
        {
            Name = "build-player",
            Status = "passed",
            DurationMs = 12345,
        };

        var json = JsonSerializer.Serialize(stage, JsonOptions);
        Assert.Contains("build-player", json);
        Assert.Contains("passed", json);
        Assert.Contains("12345", json);
    }

    [Fact]
    public void WorkflowStage_NullMessage_OmittedFromJson()
    {
        var stage = new WorkflowStage
        {
            Name = "test",
            Status = "passed",
            DurationMs = 0,
            Message = null,
        };

        var json = JsonSerializer.Serialize(stage, JsonOptions);
        Assert.DoesNotContain("\"message\"", json);
    }

    [Fact]
    public void WorkflowStage_NonNullMessage_IncludedInJson()
    {
        var stage = new WorkflowStage
        {
            Name = "test",
            Status = "failed",
            DurationMs = 500,
            Message = "Something went wrong",
        };

        var json = JsonSerializer.Serialize(stage, JsonOptions);
        Assert.Contains("\"message\"", json);
        Assert.Contains("Something went wrong", json);
    }

    [Fact]
    public void WorkflowStage_ToString_IncludesStatusNameAndDuration()
    {
        var stage = new WorkflowStage
        {
            Name = "scan-project",
            Status = "passed",
            DurationMs = 42,
        };

        var str = stage.ToString();
        Assert.Contains("scan-project", str);
        Assert.Contains("passed", str);
        Assert.Contains("42", str);
    }

    // ═══════════════════════════════════════════════════════════════
    // WorkflowStageCollector
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Collector_RecordsSyncStage()
    {
        var collector = new WorkflowStageCollector();
        var result = collector.Record("sync-op", () => 42);

        Assert.Equal(42, result);
        Assert.Single(collector.Stages);
        Assert.Equal("sync-op", collector.Stages[0].Name);
        Assert.Equal("passed", collector.Stages[0].Status);
        Assert.True(collector.Stages[0].DurationMs >= 0);
    }

    [Fact]
    public async Task Collector_RecordsAsyncStage()
    {
        var collector = new WorkflowStageCollector();
        var result = await collector.RecordAsync("async-op", () => Task.FromResult("done"));

        Assert.Equal("done", result);
        Assert.Single(collector.Stages);
        Assert.Equal("async-op", collector.Stages[0].Name);
        Assert.Equal("passed", collector.Stages[0].Status);
    }

    [Fact]
    public async Task Collector_RecordsVoidAsyncStage()
    {
        var collector = new WorkflowStageCollector();
        await collector.RecordAsync("void-op", () => Task.CompletedTask);

        Assert.Single(collector.Stages);
        Assert.Equal("void-op", collector.Stages[0].Name);
        Assert.Equal("passed", collector.Stages[0].Status);
    }

    [Fact]
    public void Collector_RecordsSkippedStage()
    {
        var collector = new WorkflowStageCollector();
        collector.RecordSkipped("deploy-harness");

        Assert.Single(collector.Stages);
        Assert.Equal("deploy-harness", collector.Stages[0].Name);
        Assert.Equal("skipped", collector.Stages[0].Status);
        Assert.Equal(0, collector.Stages[0].DurationMs);
    }

    [Fact]
    public void Collector_DirectAdd()
    {
        var collector = new WorkflowStageCollector();
        collector.Add(new WorkflowStage
        {
            Name = "custom",
            Status = "passed",
            DurationMs = 100,
        });

        Assert.Single(collector.Stages);
        Assert.Equal("custom", collector.Stages[0].Name);
    }

    [Fact]
    public void Collector_StagesReturnsSnapshot()
    {
        var collector = new WorkflowStageCollector();
        collector.RecordSkipped("s1");

        // Get snapshot then modify original
        var snapshot = collector.Stages;
        collector.RecordSkipped("s2");

        Assert.Single(snapshot); // snapshot is from before s2
        Assert.Equal(2, collector.Stages.Count);
    }

    [Fact]
    public async Task Collector_ThreadSafety_NoExceptionOnConcurrentAdd()
    {
        var collector = new WorkflowStageCollector();
        var tasks = new List<Task>();

        for (int i = 0; i < 20; i++)
        {
            var idx = i;
            tasks.Add(Task.Run(() =>
            {
                collector.Record($"{idx}", () => idx);
            }));
        }

        await Task.WhenAll(tasks);
        Assert.Equal(20, collector.Stages.Count);
    }

    // ═══════════════════════════════════════════════════════════════
    // Collector — failure paths
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void Collector_SyncFailure_RecordsFailedStage()
    {
        var collector = new WorkflowStageCollector();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            collector.Record<int>("fail-op", () =>
                throw new InvalidOperationException("Oops")));

        Assert.Equal("Oops", ex.Message);
        Assert.Single(collector.Stages);
        Assert.Equal("fail-op", collector.Stages[0].Name);
        Assert.Equal("failed", collector.Stages[0].Status);
        Assert.Contains("Oops", collector.Stages[0].Message);
    }

    [Fact]
    public async Task Collector_AsyncFailure_RecordsFailedStage()
    {
        var collector = new WorkflowStageCollector();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            collector.RecordAsync<string>("async-fail", () =>
                throw new InvalidOperationException("Async error")));

        Assert.Equal("Async error", ex.Message);
        Assert.Single(collector.Stages);
        Assert.Equal("async-fail", collector.Stages[0].Name);
        Assert.Equal("failed", collector.Stages[0].Status);
        Assert.Contains("Async error", collector.Stages[0].Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // CliInspectionResult — no stages (backward compat)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ToCliResult_NullStages_StagesOmittedFromJson()
    {
        var coreResult = MakeCoreResultNoStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var json = ToJson(cliResult);

        // Stages must be absent from JSON
        Assert.DoesNotContain("\"stages\"", json);
        // Existing fields must still be present
        Assert.Contains("\"status\"", json);
        Assert.Contains("\"requirements\"", json);
    }

    [Fact]
    public void ToCliResult_NullStages_StagesIsNull()
    {
        var coreResult = MakeCoreResultNoStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.Null(cliResult.Stages);
    }

    // ═══════════════════════════════════════════════════════════════
    // Static-only trace — stages are OMITTED (not skipped)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void StaticOnlyTrace_HasCorrectStageCount()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.NotNull(cliResult.Stages);
        Assert.Equal(5, cliResult.Stages.Count);
    }

    [Fact]
    public void StaticOnlyTrace_DoesNotContainDeployHarness()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.DoesNotContain(cliResult.Stages!, s => s.Name == "deploy-harness");
    }

    [Fact]
    public void StaticOnlyTrace_DoesNotContainBuildPlayer()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.DoesNotContain(cliResult.Stages!, s => s.Name == "build-player");
    }

    [Fact]
    public void StaticOnlyTrace_DoesNotContainPlayerStartup()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.DoesNotContain(cliResult.Stages!, s => s.Name == "player-startup");
    }

    [Fact]
    public void StaticOnlyTrace_DoesNotContainFinalizeSession()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.DoesNotContain(cliResult.Stages!, s => s.Name == "finalize-session");
    }

    [Fact]
    public void StaticOnlyTrace_ContainsOnlyExpectedStages()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        var names = cliResult.Stages!.Select(s => s.Name).ToList();
        Assert.Equal(5, names.Count);
        Assert.Contains("validate-inputs", names);
        Assert.Contains("load-assignment", names);
        Assert.Contains("scan-project", names);
        Assert.Contains("evaluate-requirements", names);
        Assert.Contains("generate-report", names);
    }

    // ═══════════════════════════════════════════════════════════════
    // CliInspectionResult — static-only stages
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ToCliResult_WithStages_IncludesAllCoreStages()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.NotNull(cliResult.Stages);
        Assert.Equal(5, cliResult.Stages.Count);
    }

    [Fact]
    public void ToCliResult_StagesContainCorrectNames()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        var names = cliResult.Stages!.Select(s => s.Name).ToList();
        Assert.Contains("validate-inputs", names);
        Assert.Contains("scan-project", names);
        Assert.Contains("generate-report", names);
        Assert.DoesNotContain("deploy-harness", names);
        Assert.DoesNotContain("build-player", names);
    }

    [Fact]
    public void ToCliResult_Stages_StatusCorrect()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.Equal("passed", cliResult.Stages![0].Status); // validate-inputs
        Assert.Equal("passed", cliResult.Stages![3].Status); // evaluate-requirements
    }

    [Fact]
    public void ToCliResult_Stages_DurationPreserved()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.Equal(1, cliResult.Stages![0].DurationMs);
        Assert.Equal(3, cliResult.Stages![2].DurationMs); // scan-project
    }

    [Fact]
    public void ToCliResult_Stages_JsonOutputIncludesStages()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var json = ToJson(cliResult);

        Assert.Contains("\"stages\"", json);
        Assert.Contains("\"validate-inputs\"", json);
        Assert.Contains("\"scan-project\"", json);
        Assert.Contains("\"generate-report\"", json);
    }

    [Fact]
    public void ToCliResult_JsonOutput_ExistingFieldsPreserved()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var json = ToJson(cliResult);

        Assert.Contains("\"status\"", json);
        Assert.Contains("\"message\"", json);
        Assert.Contains("\"requirements\"", json);
        Assert.Contains("\"SR001\"", json); // rule evidence ruleId
    }

    [Fact]
    public void ToCliResult_FailedStage_IncludesMessage()
    {
        var coreResult = MakeCoreResultWithFailedStage();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        var failedStage = cliResult.Stages!.First(s => s.Name == "load-assignment");
        Assert.Equal("failed", failedStage.Status);
        Assert.Contains("File not found", failedStage.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // Runtime-path stage count
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void RuntimeTrace_HasCorrectStageCount()
    {
        var coreResult = MakeCoreResultWithRunttimeStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.NotNull(cliResult.Stages);
        Assert.Equal(8, cliResult.Stages.Count);
    }

    [Fact]
    public void RuntimeTrace_ContainsRunttimeStages()
    {
        var coreResult = MakeCoreResultWithRunttimeStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        var names = cliResult.Stages!.Select(s => s.Name).ToList();
        Assert.Contains("deploy-harness", names);
        Assert.Contains("build-player", names);
        Assert.Contains("finalize-session", names);
    }

    [Fact]
    public void RuntimeTrace_DoesNotContainPlayerStartup()
    {
        var coreResult = MakeCoreResultWithRunttimeStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        Assert.DoesNotContain(cliResult.Stages!, s => s.Name == "player-startup");
    }

    // ═══════════════════════════════════════════════════════════════
    // Text output — stage trace
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void TextOutput_WithStages_ShowsStageTrace()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var writer = new StringWriter();

        ResultFormatter.WriteText(cliResult, writer);
        var text = writer.ToString();

        Assert.Contains("Workflow Trace", text);
        Assert.Contains("validate-inputs", text);
        Assert.Contains("scan-project", text);
        Assert.Contains("generate-report", text);
        Assert.Contains("1ms", text);
    }

    [Fact]
    public void TextOutput_NoStages_DoesNotShowStageTrace()
    {
        var coreResult = MakeCoreResultNoStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var writer = new StringWriter();

        ResultFormatter.WriteText(cliResult, writer);
        var text = writer.ToString();

        Assert.DoesNotContain("Workflow Trace", text);
    }

    [Fact]
    public void TextOutput_RuntimeTrace_ShowsRunntimeStages()
    {
        var coreResult = MakeCoreResultWithRunttimeStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var writer = new StringWriter();

        ResultFormatter.WriteText(cliResult, writer);
        var text = writer.ToString();

        Assert.Contains("deploy-harness", text);
        Assert.Contains("build-player", text);
        Assert.Contains("finalize-session", text);
        Assert.Contains("passed", text);
        Assert.Contains("5000ms", text);
    }

    [Fact]
    public void TextOutput_StaticOnly_DoesNotShowRunntimeStages()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var writer = new StringWriter();

        ResultFormatter.WriteText(cliResult, writer);
        var text = writer.ToString();

        Assert.DoesNotContain("deploy-harness", text);
        Assert.DoesNotContain("build-player", text);
        Assert.DoesNotContain("finalize-session", text);
    }

    // ═══════════════════════════════════════════════════════════════
    // Chinese output — stage trace
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ChineseOutput_WithStages_ShowsStageTrace()
    {
        var coreResult = MakeCoreResultWithStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var writer = new StringWriter();

        ChineseResultFormatter.WriteDetailed(cliResult, writer);
        var text = writer.ToString();

        Assert.Contains("工作流程跟踪", text);
        Assert.Contains("validate-inputs", text);
        Assert.Contains("scan-project", text);
    }

    [Fact]
    public void ChineseOutput_NoStages_DoesNotShowStageTrace()
    {
        var coreResult = MakeCoreResultNoStages();
        var cliResult = ResultFormatter.ToCliResult(coreResult);
        var writer = new StringWriter();

        ChineseResultFormatter.WriteDetailed(cliResult, writer);
        var text = writer.ToString();

        Assert.DoesNotContain("工作流程跟踪", text);
    }

    // ═══════════════════════════════════════════════════════════════
    // Report output — Markdown
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void MarkdownReport_WithStages_ShowsStageTrace()
    {
        var coreResult = MakeCoreResultWithStages();
        var report = InspectionReportBuilder.Build(coreResult);
        var writer = new StringWriter();

        InspectionReportWriter.WriteMarkdown(report, writer);
        var text = writer.ToString();

        Assert.Contains("Workflow Trace", text);
        Assert.Contains("| Stage | Status | Duration | Message |", text);
        Assert.Contains("validate-inputs", text);
        Assert.Contains("scan-project", text);
    }

    [Fact]
    public void MarkdownReport_NoStages_NoStageTrace()
    {
        var coreResult = MakeCoreResultNoStages();
        var report = InspectionReportBuilder.Build(coreResult);
        var writer = new StringWriter();

        InspectionReportWriter.WriteMarkdown(report, writer);
        var text = writer.ToString();

        Assert.DoesNotContain("Workflow Trace", text);
    }

    [Fact]
    public void MarkdownReport_RuntimeTrace_ShowsRunntimeStages()
    {
        var coreResult = MakeCoreResultWithRunttimeStages();
        var report = InspectionReportBuilder.Build(coreResult);
        var writer = new StringWriter();

        InspectionReportWriter.WriteMarkdown(report, writer);
        var text = writer.ToString();

        Assert.Contains("deploy-harness", text);
        Assert.Contains("build-player", text);
    }

    // ═══════════════════════════════════════════════════════════════
    // Report output — HTML
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void HtmlReport_WithStages_ShowsStageTrace()
    {
        var coreResult = MakeCoreResultWithStages();
        var report = InspectionReportBuilder.Build(coreResult);
        var writer = new StringWriter();

        InspectionReportWriter.WriteHtml(report, writer);
        var text = writer.ToString();

        Assert.Contains("Workflow Trace", text);
        Assert.Contains("<table", text);
        Assert.Contains("validate-inputs", text);
    }

    [Fact]
    public void HtmlReport_NoStages_NoStageTraceSection()
    {
        var coreResult = MakeCoreResultNoStages();
        var report = InspectionReportBuilder.Build(coreResult);
        var writer = new StringWriter();

        InspectionReportWriter.WriteHtml(report, writer);
        var text = writer.ToString();

        Assert.DoesNotContain("Workflow Trace", text);
    }

    [Fact]
    public void HtmlReport_WithStages_IncludesDuration()
    {
        var coreResult = MakeCoreResultWithStages();
        var report = InspectionReportBuilder.Build(coreResult);
        var writer = new StringWriter();

        InspectionReportWriter.WriteHtml(report, writer);
        var text = writer.ToString();

        Assert.Contains("ms", text);
    }

    // ═══════════════════════════════════════════════════════════════
    // InspectionReportBuilder — stage propagation
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void ReportBuilder_WithStages_PropagatesStages()
    {
        var coreResult = MakeCoreResultWithStages();
        var report = InspectionReportBuilder.Build(coreResult);

        Assert.NotNull(report.Stages);
        Assert.Equal(5, report.Stages.Count);
        Assert.Equal("validate-inputs", report.Stages[0].Name);
    }

    [Fact]
    public void ReportBuilder_NoStages_StagesIsNull()
    {
        var coreResult = MakeCoreResultNoStages();
        var report = InspectionReportBuilder.Build(coreResult);

        Assert.Null(report.Stages);
    }

    [Fact]
    public void ReportBuilder_WithFailedStage_MessagePreserved()
    {
        var coreResult = MakeCoreResultWithFailedStage();
        var report = InspectionReportBuilder.Build(coreResult);

        var failed = report.Stages!.First(s => s.Status == "failed");
        Assert.Contains("File not found", failed.Message);
    }

    // ═══════════════════════════════════════════════════════════════
    // Edge cases
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void EmptyStageList_IsNull_NotAnEmptyArray()
    {
        var coreResult = MakeCoreResultWithStages(new List<WorkflowStage>());
        var cliResult = ResultFormatter.ToCliResult(coreResult);

        // An empty stage list is treated the same as null — omitted from JSON
        // to keep backward compatibility
        Assert.Null(cliResult.Stages);
    }

    [Fact]
    public void StageWithLargeDuration_SerializesCorrectly()
    {
        var stage = new WorkflowStage
        {
            Name = "build-player",
            Status = "passed",
            DurationMs = long.MaxValue,
            Message = "Very long build",
        };

        var json = JsonSerializer.Serialize(stage, JsonOptions);
        Assert.Contains(long.MaxValue.ToString(), json);
        Assert.Contains("Very long build", json);
    }

    [Fact]
    public void CliStageInfo_SerializedJsonFieldNames()
    {
        var stage = new CliStageInfo
        {
            Name = "test",
            Status = "passed",
            DurationMs = 123,
        };

        var json = JsonSerializer.Serialize(stage, JsonOptions);

        // Verify camelCase field names
        Assert.Contains("\"name\"", json);
        Assert.Contains("\"status\"", json);
        Assert.Contains("\"durationMs\"", json);
    }

    [Fact]
    public void CliStageInfo_NullMessage_Omitted()
    {
        var stage = new CliStageInfo
        {
            Name = "test",
            Status = "passed",
            DurationMs = 0,
            Message = null,
        };

        var json = JsonSerializer.Serialize(stage, JsonOptions);
        Assert.DoesNotContain("\"message\"", json);
    }
}