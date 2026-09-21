using UnityProjectInspector.Core.Assignments;
using UnityProjectInspector.Core.Merge;
using UnityProjectInspector.Core.Models;
using UnityProjectInspector.Core.Models.Rules;
using UnityProjectInspector.Core.Runtime;
using UnityProjectInspector.Core.Rules;

namespace UnityProjectInspector.Tests.Assignments;

/// <summary>
/// Tests for InspectionWorkflowRunner.
///
/// Covers:
///   - StaticOnly requirement (no runtime)
///   - RuntimeRequired requirement (with runtime)
///   - Static failure skips runtime
///   - Runtime failure
///   - Runtime missing
///   - All passed
///   - Not evaluated
/// </summary>
[Trait("Category", "Unit")]
public class WorkflowRunnerTests
{
    private readonly RuleEngine _ruleEngine;
    private readonly InspectionContext _emptyContext;

    public WorkflowRunnerTests()
    {
        _ruleEngine = new RuleEngine();
        _emptyContext = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>(),
            },
        };
    }

    // ─────────────────────────────────────────────────────────────
    // StaticOnly requirement
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_StaticOnly_NoRuntimeRunnerNeeded()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new SceneInfo { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "scene-ok",
                    Name = "Scene check",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.exists",
                            Name = "Scene exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Single(result.RequirementResults);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
    }

    [Fact]
    public async Task Run_StaticOnly_SceneNotExist_Fails()
    {
        // Empty context with no scenes at all
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>(),
            },
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "scene-mainmenu",
                    Name = "MainMenu exists",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.exists",
                            Name = "Scene exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        // SceneExists with empty context → Failed (scene not found)
        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
    }

    // ─────────────────────────────────────────────────────────────
    // RuntimeRequired requirement
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_RuntimeRequired_StaticFailed_SkipsRuntime()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "runtime-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.fail",
                            Name = "NonExistent scene",
                            Type = "SceneExists",
                            Target = "NonExistentScene",
                        },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test Runtime",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, _emptyContext, null);

        // SceneExists with empty context → Failed because target scene not found.
        // Runtime should be skipped because static prerequisite failed.
        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
    }

    [Fact]
    public async Task Run_RuntimeRequired_NoRuntimeOptions_ReturnsFailed()
    {
        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "runtime-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test Runtime",
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, _emptyContext, null);

        // RuntimeRequired but no options → NotEvaluated (runtime never ran, merged)
        // No static rules either
        Assert.Equal(RuleStatus.NotEvaluated, result.FinalStatus);
    }

    // ─────────────────────────────────────────────────────────────
    // Static + Runtime: mock runtime behavior
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Run_RuntimeRequired_RuntimePassed_ReturnsPassed()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new SceneInfo { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var options = new RuntimeRunOptions
        {
            UnityExecutable = "/fake/unity",
            ProjectPath = "/fake/project",
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "runtime-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.mainmenu",
                            Name = "MainMenu exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test Runtime",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new FixedResultRuntimeRunner(RuntimeResultStatus.Passed));
        var result = await runner.RunAsync(assignment, context, options);

        // Static Passed + Runtime Passed + RuntimeRequired → Composite Passed
        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Single(result.RequirementResults);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
    }

    [Fact]
    public async Task Run_RuntimeRequired_RuntimePassed_StaticFailed_ReturnsFailed()
    {
        var options = new RuntimeRunOptions
        {
            UnityExecutable = "/fake/unity",
            ProjectPath = "/fake/project",
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "runtime-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.fail",
                            Name = "NonExistent",
                            Type = "SceneExists",
                            Target = "NonExistent",
                        },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test Runtime",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new FixedResultRuntimeRunner(RuntimeResultStatus.Passed));
        var result = await runner.RunAsync(assignment, _emptyContext, options);

        // Static Failed → runtime skipped, even though runtime would have passed
        // Composite = Failed (static prerequisite failed)
        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
    }

    [Fact]
    public async Task Run_RuntimeRequired_RuntimeFailed_ReturnsFailed()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new SceneInfo { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var options = new RuntimeRunOptions
        {
            UnityExecutable = "/fake/unity",
            ProjectPath = "/fake/project",
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "runtime-req",
                    Name = "Runtime Req",
                    EvidenceRequirement = "RuntimeRequired",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.mainmenu",
                            Name = "MainMenu exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test Runtime",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new FixedResultRuntimeRunner(RuntimeResultStatus.Failed));
        var result = await runner.RunAsync(assignment, context, options);

        // Static Passed + Runtime Failed + RuntimeRequired → Failed
        Assert.Equal(RuleStatus.Failed, result.FinalStatus);
    }

    [Fact]
    public async Task Run_AllPassed_ReturnsPassed()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new SceneInfo { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "scene-ok",
                    Name = "Scene check",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.exists",
                            Name = "Scene exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Single(result.RequirementResults);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
    }

    [Fact]
    public async Task Run_MultipleStaticRulesOnSingleRequirement()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new SceneInfo { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                    new SceneInfo { Name = "TargetScene", FilePath = "Assets/Scenes/TargetScene.unity" },
                },
            },
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "multi-static",
                    Name = "Multiple static rules",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.mainmenu",
                            Name = "MainMenu exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                        new()
                        {
                            Id = "scene.target",
                            Name = "TargetScene exists",
                            Type = "SceneExists",
                            Target = "TargetScene",
                        },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new NeverCalledRuntimeRunner());
        var result = await runner.RunAsync(assignment, context);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Single(result.RequirementResults);
        Assert.Equal(2, result.RequirementResults[0].StaticResults.Count);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].StaticResults[0].Status);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].StaticResults[1].Status);
    }

    [Fact]
    public async Task Run_StaticOnlyAndRuntimeRequired_MixedAssignment()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new SceneInfo { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test-mixed",
            Name = "Mixed Assignment",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "static-req",
                    Name = "Static check",
                    EvidenceRequirement = "StaticOnly",
                    StaticRules = new()
                    {
                        new()
                        {
                            Id = "scene.mainmenu",
                            Name = "MainMenu exists",
                            Type = "SceneExists",
                            Target = "MainMenu",
                        },
                    },
                },
                new()
                {
                    Id = "runtime-req",
                    Name = "Runtime check",
                    EvidenceRequirement = "RuntimeRequired",
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test Runtime",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new FixedResultRuntimeRunner(RuntimeResultStatus.Passed));
        var options = new RuntimeRunOptions
        {
            UnityExecutable = "/fake/unity",
            ProjectPath = "/fake/project",
        };
        var result = await runner.RunAsync(assignment, context, options);

        // StaticOnly Passed, RuntimeRequired (Passed with options) → overall Passed
        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Equal(2, result.RequirementResults.Count);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[1].Status);
    }

    [Fact]
    public async Task Run_MultipleRuntimeRequired_HasRuntimeOptions_ReturnsFinalPassed()
    {
        var context = new InspectionContext
        {
            ProjectInfo = new UnityProjectInfo
            {
                RootPath = "/tmp/test",
                Scenes = new List<SceneInfo>
                {
                    new SceneInfo { Name = "MainMenu", FilePath = "Assets/Scenes/MainMenu.unity" },
                },
            },
        };

        var assignment = new AssignmentDefinition
        {
            Id = "test",
            Name = "Test",
            Requirements = new List<RequirementDefinition>
            {
                new()
                {
                    Id = "runtime-1",
                    Name = "Runtime check 1",
                    EvidenceRequirement = "RuntimeRequired",
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test 1",
                        Actions = new() { new WaitAction { ActionId = "w1" } },
                    },
                },
                new()
                {
                    Id = "runtime-2",
                    Name = "Runtime check 2",
                    EvidenceRequirement = "RuntimeRequired",
                    RuntimeTest = new RuntimeTestScript
                    {
                        Name = "Test 2",
                        Actions = new() { new WaitAction { ActionId = "w2" } },
                    },
                },
            },
        };

        var runner = new InspectionWorkflowRunner(_ruleEngine, new FixedResultRuntimeRunner(RuntimeResultStatus.Passed));
        var options = new RuntimeRunOptions
        {
            UnityExecutable = "/fake/unity",
            ProjectPath = "/fake/project",
        };
        var result = await runner.RunAsync(assignment, context, options);

        Assert.Equal(RuleStatus.Passed, result.FinalStatus);
        Assert.Equal(2, result.RequirementResults.Count);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[0].Status);
        Assert.Equal(RuleStatus.Passed, result.RequirementResults[1].Status);
    }
}

/// <summary>
    /// Mock IRuntimeRunner that should never be called.
    /// Throws if invoked.
    /// </summary>
    public class NeverCalledRuntimeRunner : IRuntimeRunner
{
    public Task<RuntimeSession> RunAsync(RuntimeRunOptions options, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("RuntimeRunner should not have been called");
    }

    public Task<RuntimeSession> RunAsync(RuntimeRunOptions options, RuntimeTestScript script, CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException("RuntimeRunner should not have been called");
    }
}

/// <summary>
/// Mock IRuntimeRunner that returns a fixed result.
/// </summary>
public class FixedResultRuntimeRunner : IRuntimeRunner
{
    private readonly RuntimeResultStatus _status;

    public FixedResultRuntimeRunner(RuntimeResultStatus status)
    {
        _status = status;
    }

    public Task<RuntimeSession> RunAsync(RuntimeRunOptions options, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RuntimeSession
        {
            ProjectPath = options.ProjectPath,
            StartedAt = DateTime.UtcNow,
            Result = _status,
        });
    }

    public Task<RuntimeSession> RunAsync(RuntimeRunOptions options, RuntimeTestScript script, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new RuntimeSession
        {
            ProjectPath = options.ProjectPath,
            StartedAt = DateTime.UtcNow,
            Result = _status,
        });
    }
}