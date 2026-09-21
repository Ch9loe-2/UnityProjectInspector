using System.Text.Json;
using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// M18 Assignment Full Pipeline Integration Tests.
///
/// Tests the complete pipeline:
///   Load JSON fixture (maze-2d-basic.json) → Validator → WorkflowRunner → ResultMerger → Result
///
/// For full-pipeline execution tests we use controlled inline assignments with
/// only SceneExists rules (which work against InspectionContext without real files).
/// The maze fixture is used for Validator round-trip and deserialization tests.
/// Runtime scenarios use FixedResultRuntimeRunner mocks.
/// </summary>
[Trait("Category", "Unit")]
public class M18AssignmentIntegrationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    private readonly RuleEngine _ruleEngine = new();

    private static readonly string FixturePath = Path.GetFullPath(
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory,
            "..", "..", "..", "..", "..",
            "tests", "TestData", "Assignments", "maze-2d-basic.json"));

    private static AssignmentDefinition LoadMazeFixture()
    {
        Assert.True(File.Exists(FixturePath),
            $"Fixture not found at {FixturePath}");

        var json = File.ReadAllText(FixturePath);
        var assignment = JsonSerializer.Deserialize<AssignmentDefinition>(json, JsonOptions);

        Assert.NotNull(assignment);
        return assignment!;
    }

    private static InspectionContext CreateContext(string[]? sceneNames = null)
    {
        sceneNames ??= new[] { "MainMenu", "TargetScene" };
        return new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = sceneNames.Select(n =>
                    new SceneInfo { Name = n, FilePath = $"Assets/Scenes/{n}.unity" }).ToList(),
            },
        };
    }

    private static AssignmentDefinition BuildSimpleAssignment(
        List<(string id, string name, string evidence, string? runtimeActionId)> requirements)
    {
        return new AssignmentDefinition
        {
            Id = "test-assignment",
            Name = "Test Assignment",
            Requirements = requirements.Select(r =>
            {
                var (id, name, evidence, runtimeActionId) = r;
                var isRuntime = evidence == "RuntimeRequired";
                return new RequirementDefinition
                {
                    Id = id,
                    Name = name,
                    EvidenceRequirement = evidence,
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = $"static.{id}",
                            Name = $"Static check for {id}",
                            Type = "SceneExists",
                            Target = name.StartsWith("Scene") ? name.Replace("Scene ", "") : "MainMenu",
                        },
                    },
                    RuntimeTest = isRuntime ? new RuntimeTestScript
                    {
                        Name = $"Runtime {name}",
                        Actions = runtimeActionId != null
                            ? new List<RuntimeAction> { new WaitAction { ActionId = runtimeActionId } }
                            : new List<RuntimeAction>(),
                    } : null,
                };
            }).ToList(),
        };
    }

    // ══════════════════════════════════════════════════════════════
    // Test 1: Full Pipeline — All Pass
    // AssignmentDefinition → Validator → WorkflowRunner → Static → 
    // MockRuntime → ResultMerger → RequirementInspectionResult → AssignmentInspectionResult
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_FullPipeline_AllPass()
    {
        // Controlled assignment: StaticOnly (SceneExists MainMenu) + RuntimeRequired (SceneExists MainMenu + Runtime)
        var assignment = new AssignmentDefinition
        {
            Id = "pipeline-pass",
            Name = "Pipeline Pass Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "static-scene",
                    Name = "StaticOnly Scene",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "MainMenu" },
                    },
                },
                new()
                {
                    Id = "runtime-scene",
                    Name = "RuntimeRequired Scene",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new() { Id = "s2", Name = "S2", Type = "SceneExists", Target = "MainMenu" },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Runtime test",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new() { Name = "MainMenu", FilePath = "MainMenu.unity" },
                },
            },
        };

        var options = new RuntimeRunOptions
        {
            UnityExecutable = "/fake/unity",
            ProjectPath = "/fake/project",
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine,
            new FixedResultRuntimeRunner(RuntimeResultStatus.Passed));

        var result = await runner.RunAsync(assignment, context, options);

        Assert.NotNull(result);
        Assert.Equal(2, result.RequirementResults.Count);

        // StaticOnly: SceneExists(MainMenu) → Passed
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);

        // RuntimeRequired: Static Passed + Runtime Passed → Passed
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[1].Status);
        Assert.NotNull(result.RequirementResults[1].CompositeResult);

        // Final: both Passed → Passed
        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Contains("passed", result.Message, StringComparison.OrdinalIgnoreCase);

        // Verify CompositeInspectionResult has correct merge data
        var composite = result.RequirementResults[1].CompositeResult!;
        Assert.Equal(EvidenceRequirement.RuntimeRequired, composite.Requirement);
        Assert.Equal(RuleStatus.Passed, composite.StaticStatus);
        Assert.Equal(RuleStatus.Passed, composite.RuntimeStatus);
        Assert.Equal(RuleStatus.Passed, composite.FinalStatus);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 2: Full Pipeline — StaticOnly No Runtime
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_FullPipeline_StaticOnlyNoRuntime()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "static-only",
            Name = "Static Only",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "scene-1",
                    Name = "StaticOnly Scene",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "MainMenu" },
                    },
                },
            },
        };

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new() { Name = "MainMenu", FilePath = "MainMenu.unity" },
                },
            },
        };

        // NeverCalledRuntimeRunner verifies runtime is never invoked for StaticOnly
        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Single(result.RequirementResults);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
        Assert.Single(result.RequirementResults[0].StaticResults);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].StaticResults[0].Status);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 3: Full Pipeline — Static Rule Fails → Assignment Fails
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_FullPipeline_StaticRuleFails_AssignmentFails()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "static-fail",
            Name = "Static Fail",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "scene-missing",
                    Name = "Missing scene",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "NonExistentScene" },
                    },
                },
            },
        };

        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>(),
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
        Assert.Equal(RuleStatus.Failed, result.RequirementResults[0].Status);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 4: Full Pipeline — Invalid Assignment → NotEvaluated
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_FullPipeline_InvalidAssignment_NotEvaluated()
    {
        var invalidAssignment = new AssignmentDefinition
        {
            Id = "",
            Name = "Bad",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "req1",
                    Name = "Req 1",
                    EvidenceRequirement = "RuntimeRequired",
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(invalidAssignment, CreateContext());

        Assert.Equal(RuleStatus.NotEvaluated, result.FinalStatus);
        Assert.Contains("invalid", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 5: Validator — maze-2d-basic.json has no errors
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public void M18_Validator_MazeFixtureIsValid()
    {
        var assignment = LoadMazeFixture();
        var validator = new InspectionWorkflowValidator();

        var issues = validator.Validate(assignment);

        var errors = issues.Where(i => i.Severity == WorkflowIssueSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 6: RuntimeRequired + Static Passed + Runtime Passed → Passed
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_FullPipeline_RuntimeRequired_StaticAndRuntimePass()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "rt-pass",
            Name = "Runtime Pass",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "rt-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "MainMenu" },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "RT Test",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var context = CreateContext(new[] { "MainMenu" });
        var options = new RuntimeRunOptions { UnityExecutable = "/fake/u", ProjectPath = "/fake/p" };
        var runner = new InspectionWorkflowRunner(_ruleEngine,
            new FixedResultRuntimeRunner(RuntimeResultStatus.Passed));

        var result = await runner.RunAsync(assignment, context, options);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);

        var composite = result.RequirementResults[0].CompositeResult;
        Assert.NotNull(composite);
        Assert.Equal(RuleStatus.Passed, composite!.StaticStatus);
        Assert.Equal(RuleStatus.Passed, composite.RuntimeStatus);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 7: RuntimeRequired + Runtime Failed → Failed
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_FullPipeline_RuntimeRequired_RuntimeFails()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "rt-fail",
            Name = "Runtime Fail",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "rt-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "MainMenu" },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "RT Test",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var context = CreateContext(new[] { "MainMenu" });
        var options = new RuntimeRunOptions { UnityExecutable = "/fake/u", ProjectPath = "/fake/p" };
        var runner = new InspectionWorkflowRunner(_ruleEngine,
            new FixedResultRuntimeRunner(RuntimeResultStatus.Failed));

        var result = await runner.RunAsync(assignment, context, options);

        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
        Assert.Equal(RuleStatus.Failed, result.RequirementResults[0].Status);

        var composite = result.RequirementResults[0].CompositeResult;
        Assert.NotNull(composite);
        Assert.Equal(RuleStatus.Passed, composite!.StaticStatus);
        Assert.Equal(RuleStatus.Failed, composite.RuntimeStatus);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 8: RuntimeRequired + No runtime options → NotEvaluated
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_FullPipeline_RuntimeRequired_NoOptions()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "rt-noopts",
            Name = "Runtime No Options",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "rt-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "RT Test",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, CreateContext(new[] { "MainMenu" }));

        Assert.Equal(RuleStatus.NotEvaluated, result.FinalStatus);
        Assert.Equal(RuleStatus.NotEvaluated, result.RequirementResults[0].Status);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 9: Assignment Aggregation — all Passed
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_AssignmentAggregation_AllPassed()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "all-passed",
            Name = "All Passed",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "s1", Name = "S1", EvidenceRequirement = "StaticOnly",
                    StaticRules = new() { new() { Id = "r1", Name = "R1", Type = "SceneExists", Target = "MainMenu" } },
                },
                new()
                {
                    Id = "s2", Name = "S2", EvidenceRequirement = "StaticOnly",
                    StaticRules = new() { new() { Id = "r2", Name = "R2", Type = "SceneExists", Target = "TargetScene" } },
                },
            },
        };

        var context = CreateContext(new[] { "MainMenu", "TargetScene" });
        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Equal(2, result.RequirementResults.Count);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[1].Status);
        Assert.Contains("passed", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ══════════════════════════════════════════════════════════════
    // Test 10: Assignment Aggregation — Mixed Passed + NotEvaluated
    // ══════════════════════════════════════════════════════════════

    [Fact]
    public async Task M18_AssignmentAggregation_MixedPassedAndNotEvaluated()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "mixed",
            Name = "Mixed",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "static-ok", Name = "Static OK", EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new() { Id = "s1", Name = "S1", Type = "SceneExists", Target = "MainMenu" },
                    },
                },
                new()
                {
                    Id = "runtime-noopts", Name = "Runtime No Options", EvidenceRequirement = "RuntimeRequired",
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "T", Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var context = CreateContext(new[] { "MainMenu" });
        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        // StaticOnly: Passed, RuntimeRequired (no options): NotEvaluated
        // Aggregation: any NotEvaluated → NotEvaluated
        Assert.Equal(RuleStatus.NotEvaluated, result.FinalStatus);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
        Assert.Equal(RuleStatus.NotEvaluated, result.RequirementResults[1].Status);
    }
}